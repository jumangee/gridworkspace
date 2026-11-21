using System;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using Vgs.Core;
using Vgs.Model;

namespace Vgs.Game
{
    /// <summary>
    /// Writes a connection into the source block's real toolbar.
    ///
    /// <b>The toolbar is the connection.</b> When the user connects "timer → lamp, BlockOn",
    /// an item appears in the timer's toolbar, and that item is what makes the lamp switch on
    /// when the timer triggers. The editor does not keep a private wiring next to the block's
    /// own: it writes the item the player could have placed by hand, so the connection works
    /// with no session component running, survives a reload, and can be inspected and edited
    /// in the vanilla terminal.
    ///
    /// <b>Which API.</b> <see cref="IMyToolbarSlots.BeginChanges"/> →
    /// <see cref="IMyToolbarChangeBuilder.Set"/> → <see cref="IMyToolbarChangeBuilder.Apply"/>,
    /// all from <c>VRage.Game.ModAPI</c>. The engine's own <c>MyToolbar.SetItemAtIndex</c> does
    /// the same thing and was the first choice here, but it lives in
    /// <c>Sandbox.Game.Screens.Helpers</c>, which the game's IL whitelist does not allow: using
    /// it made the whole mod fail to load with "The type or member 'MyToolbar' is prohibited".
    /// The ModAPI route is the supported one.
    ///
    /// <b>Where it works.</b> <c>Apply()</c> ends in <c>MyToolbar.SendChanges</c>, which returns
    /// false unless <c>MyToolbar.IsOwnedHere</c> — for a block toolbar that means
    /// <c>Sync.IsServer</c>. So a write succeeds in single player and on the host, and is
    /// refused on a remote multiplayer client, where the block's toolbar belongs to the server.
    /// <c>Apply()</c> reports that refusal honestly, and this writer turns it into
    /// <see cref="WriteResult.Rejected"/> rather than pretending the connection exists. Reading
    /// a toolbar (<see cref="ToolbarProbe"/>, the reconcile pass) works everywhere, so a client
    /// still sees connections the server wrote.
    ///
    /// <b>Placement rules, all read from the game rather than assumed.</b>
    ///
    ///  1. A yes/no toolbar has one slot per group: "yes" writes slot 0, "no" writes slot 1.
    ///  2. A single-group toolbar accepts any free cell.
    ///  3. The same action on the same target block may appear only once per <i>page</i>.
    ///     That is not our rule: the vanilla toolbar-config screen deletes every other slot
    ///     of the current page holding an equal item before it drops a new one
    ///     (<c>MyGuiScreenToolbarConfigBase.DropGridItemToToolbar</c>), so a duplicate we
    ///     wrote on one page would be silently removed the first time the player edited that
    ///     page. The editor therefore spreads repeats across pages, and reports a failure
    ///     when every page already holds that action.
    ///
    /// Everything here is a boundary: it is called from a click handler and must never throw
    /// into the game loop.
    /// </summary>
    public static class ToolbarWriter
    {
        /// <summary>Outcome of an attempt to place one connection in a toolbar.</summary>
        public enum WriteResult
        {
            /// <summary>The item is in the toolbar and the cell is reported back.</summary>
            Ok = 0,

            /// <summary>The block has no toolbar the game fires by itself.</summary>
            NotDrivable = 1,

            /// <summary>Every page has a free cell of the right slot, but all are reserved for other actions.</summary>
            NoFreeCell = 2,

            /// <summary>Every page already holds this very action on this target: the game refuses it.</summary>
            ActionOnEveryPage = 3,

            /// <summary>The target no longer reports the action (it was rebuilt, or the list changed).</summary>
            ActionUnavailable = 4,

            /// <summary>The game refused the item; nothing was written.</summary>
            Rejected = 5
        }

        /// <summary>
        /// Places the connection into a free cell of the source's toolbar and reports which
        /// cell it took. Returns <see cref="WriteResult.Ok"/> only after reading the cell back
        /// and finding the expected item there — the game can refuse an item, and reporting a
        /// connection that does not exist would be worse than reporting the failure.
        /// </summary>
        public static WriteResult TryWrite(IMyTerminalBlock source, Node sourceNode, Port eventPort,
                                           IMyTerminalBlock target, string actionId, out ToolbarCell cell)
        {
            cell = ToolbarCell.None;

            if (source == null || sourceNode == null || eventPort == null || target == null) return WriteResult.NotDrivable;
            if (string.IsNullOrEmpty(actionId)) return WriteResult.ActionUnavailable;

            ToolbarLayout layout = sourceNode.Toolbar;
            if (layout == null || !layout.CanDrive) return WriteResult.NotDrivable;

            try
            {
                IMyToolbar toolbar = ToolbarProbe.GetToolbar(source, layout.ToolbarIndex);
                if (toolbar == null) return WriteResult.NotDrivable;

                // The action must still be on the target: a block rebuilt between the refresh
                // and the click can have lost it, and an item naming a missing action is dead.
                if (target.GetActionWithName(actionId) == null) return WriteResult.ActionUnavailable;

                // Which cell to take is a pure decision (ToolbarPlanner); reading the current
                // contents of the toolbar is the only part that needs the live object.
                int index;
                ToolbarPlacement placement = ToolbarPlanner.Plan(
                    layout,
                    eventPort.Group,
                    cellIndex => !ReadCell(toolbar, cellIndex, out _, out _),
                    page => PageHoldsAction(toolbar, layout, page, target.EntityId, actionId),
                    out index);

                switch (placement)
                {
                    case ToolbarPlacement.Ok:
                        break;
                    case ToolbarPlacement.ActionOnEveryPage:
                        return WriteResult.ActionOnEveryPage;
                    case ToolbarPlacement.NoFreeCell:
                        return WriteResult.NoFreeCell;
                    default:
                        return WriteResult.NotDrivable;
                }

                // One batch for the single change: the builder validates the item (an unknown
                // action or a missing block makes it reject the whole batch) and Apply reports
                // whether the game took it.
                IMyToolbarChangeBuilder change = toolbar.BeginChanges();
                if (change == null) return WriteResult.Rejected;

                change.Set(index, MyToolbarItemInfo.BlockAction(target.EntityId, actionId));

                if (!change.Apply())
                {
                    // The game refused the batch — most often because this machine is not the
                    // one that owns the block's toolbar (a multiplayer client). Nothing was
                    // written, so there is nothing to roll back.
                    return WriteResult.Rejected;
                }

                // Read back rather than trust: the batch reports success even when it only
                // queued the change, and a cell that still holds something else means the
                // connection is not there.
                long writtenBlock;
                string writtenAction;
                if (!ReadCell(toolbar, index, out writtenBlock, out writtenAction)
                    || writtenBlock != target.EntityId
                    || !string.Equals(writtenAction, actionId, StringComparison.Ordinal))
                {
                    ClearCell(toolbar, index);
                    return WriteResult.Rejected;
                }

                cell = new ToolbarCell(layout.ToolbarIndex, index);
                return WriteResult.Ok;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("toolbar-write", "could not write toolbar item", e, 120);
                return WriteResult.Rejected;
            }
        }

        /// <summary>
        /// Empties the cell a connection occupied. Returns true when the cell is empty
        /// afterwards. Used when the graph refuses an edge after the item was already
        /// written, so the block never keeps a connection the editor does not show.
        /// </summary>
        public static bool TryClear(IMyTerminalBlock source, Node sourceNode, ToolbarCell cell)
        {
            if (source == null || sourceNode == null || !cell.IsValid) return false;

            try
            {
                IMyToolbar toolbar = ToolbarProbe.GetToolbar(source, cell.ToolbarIndex);
                if (toolbar == null) return false;
                if (cell.Index < 0 || cell.Index >= toolbar.ItemCount) return false;

                if (!ClearCell(toolbar, cell.Index)) return false;

                long blockId;
                string action;
                return !ReadCell(toolbar, cell.Index, out blockId, out action);
            }
            catch (Exception e)
            {
                VgsLog.Throttled("toolbar-clear", "could not clear toolbar item", e, 120);
                return false;
            }
        }

        /// <summary>
        /// Reads what a cell holds. False for an empty cell, a non-block item (a group item or
        /// a block definition), or a cell outside the toolbar.
        /// </summary>
        public static bool TryDescribe(IMyToolbar toolbar, int index, out long blockEntityId, out string actionId)
        {
            return ReadCell(toolbar, index, out blockEntityId, out actionId);
        }

        /// <summary>
        /// True when any cell of this page already holds the same action on the same target.
        /// This is the vanilla duplicate rule; see the type comment for where it comes from.
        /// </summary>
        private static bool PageHoldsAction(IMyToolbar toolbar, ToolbarLayout layout, int page,
                                            long targetEntityId, string actionId)
        {
            for (int s = 0; s < layout.SlotCount; s++)
            {
                int index = layout.CellIndex(page, s);

                long blockId;
                string action;
                if (!ReadCell(toolbar, index, out blockId, out action)) continue;
                if (blockId != targetEntityId) continue;
                if (string.Equals(action, actionId, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>
        /// Reads one cell through the ModAPI mirror of the toolbar. Only
        /// <see cref="MyToolbarItemKind.BlockAction"/> cells describe a block-plus-action
        /// connection; a group action or a block definition is a different kind of toolbar
        /// entry and is not something this editor can represent, so it reads as empty.
        /// </summary>
        private static bool ReadCell(IMyToolbar toolbar, int index, out long blockEntityId, out string actionId)
        {
            blockEntityId = 0;
            actionId = null;

            if (toolbar == null || index < 0 || index >= toolbar.ItemCount) return false;

            MyToolbarItemInfo info;
            if (!toolbar.TryGetItem(index, out info)) return false;
            if (info.Kind != MyToolbarItemKind.BlockAction) return false;

            blockEntityId = info.BlockEntityId;
            actionId = info.ActionId;
            return blockEntityId != 0 && !string.IsNullOrEmpty(actionId);
        }

        /// <summary>
        /// Empties one cell. Returns false when the game refused the batch.
        /// </summary>
        private static bool ClearCell(IMyToolbar toolbar, int index)
        {
            IMyToolbarChangeBuilder change = toolbar.BeginChanges();
            if (change == null) return false;

            change.Clear(index);
            return change.Apply();
        }
    }
}
