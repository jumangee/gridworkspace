using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using Vgs.Core;

namespace Vgs.Game
{
    /// <summary>
    /// Opens the game's terminal on one block — the action behind the card's jump button
    /// (ADR-0021).
    ///
    /// <b>Why this is a separate type.</b> The editor works on a snapshot
    /// (<see cref="Vgs.Model.GridGraph"/>) and does not read live entities while drawing. A jump is
    /// the opposite of a draw: one user action that has to reach the world, needing the block's
    /// live entity, the local player and the terminal screen. That is the same shape as
    /// <see cref="ToolbarWriter"/> and <see cref="NodeStateStore"/>, so it lives next to them in
    /// <c>Vgs.Game</c>, and the UI only asks "did it work, and if not, why".
    ///
    /// <b>How the terminal is reached.</b> Two facts about the installed game decide the
    /// implementation (both read from IL, recorded in
    /// <c>docs/01-research/api-terminal-jump.md</c>):
    ///
    ///  * <c>IMyGui.ShowTerminalPage(page, character, entity, isRemote)</c> is a thin wrapper over
    ///    <c>MyGuiScreenTerminal.Show</c>, which <b>returns immediately when a terminal screen
    ///    already exists</b> — it does not switch the open screen to another block. It is
    ///    therefore only good for opening a closed terminal.
    ///  * <c>IMyGui.ChangeInteractedEntity(entity, isRemote)</c> sets the screen's interacted
    ///    entity and, when the screen is open, rebuilds its tabs
    ///    (<c>MyGuiScreenTerminal.set_InteractedEntity</c> → <c>RecreateTabs</c>), which selects
    ///    the Control Panel page for the new block.
    ///
    /// The editor is opened from the cockpit's terminal button, so in normal use the terminal is
    /// open and merely hidden underneath the modal screen — the jump therefore takes the
    /// <c>ChangeInteractedEntity</c> path. The <c>ShowTerminalPage</c> path exists for the case
    /// where the terminal is genuinely closed, and the choice is made by asking the game which
    /// page is showing: the <c>GetCurrentScreen</c> property returns
    /// <see cref="MyTerminalPageEnum.None"/> exactly when no terminal screen exists.
    ///
    /// <b>Which page the terminal lands on.</b> The setter sets the start page to Control Panel
    /// only when the target differs from the block the terminal was <i>opened with</i>
    /// (<c>m_openInventoryInteractedEntity</c>, written once by <c>Show</c>). Jumping to that very
    /// block leaves the start page as <c>Show</c> set it. The editor is opened from a cockpit's
    /// terminal button, which the game opens with <c>page = ControlPanel</c>, so both branches land
    /// on Control Panel in practice; a terminal opened with the inventory shortcut would keep
    /// Inventory for that one block. Forcing the tab is not possible through <c>IMyGui</c> — the
    /// screen type that owns the tab control is outside the whitelist — and the only permitted
    /// workaround (close the terminal, wait for it to disappear, open it again) costs a multi-frame
    /// state machine for a case the player can fix with one click. Recorded as a limitation in
    /// ADR-0021 rather than worked around.
    ///
    /// <b>Why the "remote" flag depends on distance.</b> The game re-checks the connection every
    /// frame (<c>MyGuiScreenTerminal.Update</c> → <c>MyTerminalPropertiesController.TestConnection</c>)
    /// and, when the check fails, hides the tabs and shows "Ship has been disconnected from
    /// terminal". For a block on the same grid as the block the terminal was opened with, that
    /// check is a plain distance test: the player's character must be within
    /// <c>MyConstants.DEFAULT_INTERACTIVE_DISTANCE</c> (5 m) of the block, the same reach as
    /// pressing F on it. Jumping from a cockpit to a block across the ship therefore used to
    /// succeed and then immediately disconnect — the tabs vanished and the screen would not
    /// return, while the terminal underneath had already switched, which is why closing the
    /// editor put the player back in the cockpit. The distance test is skipped when the screen is
    /// in the game's remote-access mode (<c>isRemote</c>), and that mode asks the antenna system
    /// instead: the grid must have a broadcasting antenna. So the flag is chosen here from the
    /// measured distance — local when the player is in reach (exactly what pressing F does),
    /// remote when they are not, and refused outright when there is no antenna to carry the
    /// remote session, because that is the one combination the game cannot honour and the player
    /// would get the disconnect screen again.
    ///
    /// <b>Nothing here throws.</b> This runs from the screen's input handler, which the game calls:
    /// an exception would break the session, so every failure becomes a message for the bottom bar
    /// and a line in the log.
    /// </summary>
    public static class TerminalJump
    {
        /// <summary>
        /// Switches the game's terminal to <paramref name="entityId"/>, opening it if it is
        /// closed. Returns false and sets <paramref name="failure"/> to a user-facing reason when
        /// the jump did not happen; the caller must then keep the editor open.
        ///
        /// <paramref name="entityId"/> is the node's id, not a live entity: the block may have been
        /// destroyed since the last refresh, and resolving it here is what turns "the card is
        /// stale" into an honest refusal instead of a terminal that shows nothing.
        ///
        /// <b>Logging.</b> Only the exception path logs here. The refusals and the success are the
        /// caller's to report: it owns the bottom bar, and a second line for the same event would
        /// mean two places to change when the wording changes.
        /// </summary>
        public static bool TryOpen(long entityId, out string failure)
        {
            failure = null;

            try
            {
                if (entityId == VgsConstants.NoEntityId)
                {
                    failure = VgsText.WarnJumpBlockGone;
                    return false;
                }

                if (!VgsSession.HasLocalPlayer)
                {
                    // A dedicated server has no GUI at all; a client without a player is still
                    // loading. Either way there is nobody to show a terminal to.
                    failure = VgsText.WarnJumpNoPlayer;
                    return false;
                }

                IMyGui gui = MyAPIGateway.Gui;
                if (gui == null)
                {
                    failure = VgsText.WarnJumpFailed;
                    return false;
                }

                IMyEntity entity;
                if (MyAPIGateway.Entities == null
                    || !MyAPIGateway.Entities.TryGetEntityById(entityId, out entity)
                    || entity == null)
                {
                    failure = VgsText.WarnJumpBlockGone;
                    return false;
                }

                var block = entity as IMyTerminalBlock;
                if (block == null)
                {
                    // Practically unreachable — a node is built from a terminal block — but the
                    // sentence has to be about this case, not about a block that plainly exists.
                    failure = VgsText.WarnJumpNotTerminal;
                    return false;
                }

                IMyPlayer player = MyAPIGateway.Session == null ? null : MyAPIGateway.Session.Player;
                IMyCharacter character = player == null ? null : player.Character;
                if (character == null)
                {
                    // The terminal screen needs a character to own the interaction; a spectator
                    // or a session mid-load has none, and passing null would fail inside the game.
                    failure = VgsText.WarnJumpNoPlayer;
                    return false;
                }

                // The game's own connection test decides which of the two modes to ask for: in
                // reach it is a local interaction, out of reach it can only be a remote session
                // through an antenna. Asking for the wrong one is not a cosmetic difference —
                // the terminal would open and then hide its tabs on the next frame.
                bool remote = IsOutOfInteractiveReach(block, character);
                if (remote && !HasBroadcastingAntenna(block.CubeGrid))
                {
                    // The game has no way to show this block's terminal: too far to interact,
                    // and nothing to carry a remote session. Refusing keeps the schema open and
                    // says why, instead of switching the terminal and letting it disconnect.
                    failure = VgsText.WarnJumpNoAntenna;
                    return false;
                }

                if (gui.GetCurrentScreen != MyTerminalPageEnum.None)
                {
                    // The terminal exists — in normal use open, hidden under this screen. Setting
                    // the interacted entity rebuilds its tabs on the Control Panel page of the
                    // new block. The bool is the game's "remote access" flag; false is what a
                    // local interaction passes.
                    gui.ChangeInteractedEntity(block, remote);
                }
                else
                {
                    gui.ShowTerminalPage(MyTerminalPageEnum.ControlPanel, character, block, remote);
                }

                return true;
            }
            catch (Exception e)
            {
                VgsLog.Error("terminal jump to block " + entityId + " failed", e);
                failure = VgsText.WarnJumpFailed;
                return false;
            }
        }

        /// <summary>
        /// True when the player's character is farther from the block than the game lets a local
        /// interaction reach. Transcribed from <c>MyTerminalPropertiesController.TestConnection</c>:
        /// it compares <c>BoundingBoxD.DistanceSquared(interactedEntity.PositionComp.WorldAABB,
        /// character position)</c> against the square of
        /// <c>VRage.Game.MyConstants.DEFAULT_INTERACTIVE_DISTANCE</c> and treats "inside" as
        /// connected. The game's own constant is read rather than copied, so a change on their side
        /// moves this with it.
        /// </summary>
        private static bool IsOutOfInteractiveReach(IMyTerminalBlock block, IMyCharacter character)
        {
            float reach = VRage.Game.MyConstants.DEFAULT_INTERACTIVE_DISTANCE;
            return block.WorldAABB.DistanceSquared(character.GetPosition()) >= reach * reach;
        }

        /// <summary>
        /// True when any antenna of the grid — or of a grid in the same logical group, which is
        /// what the game's connection test walks — is actually broadcasting. A grid without one
        /// cannot serve a remote terminal, and <c>IsBroadcasting</c> already folds in "enabled",
        /// "working" and "broadcasting switched on", so it is the same condition the antenna system
        /// uses to register a broadcaster.
        /// </summary>
        private static bool HasBroadcastingAntenna(IMyCubeGrid grid)
        {
            if (grid == null) return false;
            if (GridHasBroadcastingAntenna(grid)) return true;

            // A subgrid's antenna counts for the terminal, because the connection test works on
            // the logical group rather than on one grid. Walking it here keeps the refusal honest
            // for ships that carry their antenna on a hinge or a piston.
            IMyGridGroupData group = grid.GetGridGroup(GridLinkTypeEnum.Logical);
            if (group == null) return false;

            List<IMyCubeGrid> grids = new List<IMyCubeGrid>();
            group.GetGrids(grids);
            for (int i = 0; i < grids.Count; i++)
            {
                if (!ReferenceEquals(grids[i], grid) && GridHasBroadcastingAntenna(grids[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool GridHasBroadcastingAntenna(IMyCubeGrid grid)
        {
            if (grid == null) return false;

            foreach (IMyRadioAntenna antenna in grid.GetFatBlocks<IMyRadioAntenna>())
            {
                if (antenna != null && antenna.IsBroadcasting) return true;
            }

            return false;
        }
    }
}
