using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using Vgs.Core;
using Vgs.Model;

namespace Vgs.Game
{
    /// <summary>
    /// Runs one terminal action of a card right now, on the player's behalf (ADR-0041).
    ///
    /// <b>What this is not.</b> It is not a connection. Nothing is written to a toolbar and the
    /// graph does not change (ADR-0011): the manual action menu lists exactly the rows the bind
    /// menu would offer for the same target, and choosing one applies it once, immediately. It is
    /// the editor's answer to pressing the button in the vanilla terminal, for a player who does
    /// not want to leave the schema to press it.
    ///
    /// <b>Which API.</b> `IMyTerminalBlock.GetActionWithName(id)` → `ITerminalAction.Apply(block)`,
    /// both on the ModAPI mirror. Verified against the installed game's assemblies:
    /// `dump-se-api.ps1 -Types 'Sandbox.ModAPI.Ingame.IMyTerminalBlock','Sandbox.ModAPI.Interfaces.ITerminalAction'`
    /// gives `ITerminalAction GetActionWithName(String name)` and `Void Apply(IMyCubeBlock block)`.
    ///
    /// <b>What the game itself does when a wired item fires, and why this follows it exactly.</b>
    /// `dump-se-il.ps1 -Type 'Sandbox.Game.Screens.Helpers.MyToolbarItemTerminalBlock' -Method 'Activate'`
    /// reads, in full:
    ///
    /// <code>
    /// action = GetCurrentAction();
    /// if (m_block != null &amp;&amp; action != null) action.Apply(m_block, m_parameters);
    /// </code>
    ///
    /// There is <b>no</b> `IsEnabled` check in that path. So a manual run does not make one
    /// either: `IsEnabled == false` means "the control is switched off for this block right now",
    /// and ADR-0038 already ruled that this is *not* "the action is unavailable" — the action list
    /// keeps such rows, and a connection may be wired to one. Refusing here would make the manual
    /// row disagree with the identical row in the bind menu, which is the one thing this mode must
    /// not do. What the action then does with the block is the game's answer, not ours (ADR-0018).
    ///
    /// <b>A group has no block and no entity</b> (ADR-0015): its actions are applied to every
    /// member the terminal system reports, which is what the game's `GroupAction` item does
    /// (`docs/01-research/api-block-groups.md` §4, read from IL). The members are re-read from the
    /// world here rather than taken from the graph, because the graph lists only the *drawn*
    /// members while the action applies to all of them. Two members are skipped, for the game's
    /// two reasons: one that does not report the action at all, and one that is not functional
    /// (`Activate` tests `MyCubeBlock.get_IsFunctional` before applying). The member set comes
    /// from `IMyBlockGroup.GetBlocks`, which is the same walk with the game's
    /// `IsAccessibleForProgrammableBlock` filter on it — the raw `MyBlockGroup.Blocks` set the
    /// vanilla item walks is on the engine type and outside the mod's whitelist, and the filter is
    /// true for every block the game creates unless something deliberately hid it.
    ///
    /// <b>Refusals are answers.</b> A block ground down since the last refresh, an action a
    /// rebuilt block no longer has, a group renamed in the terminal — each is its own result, so
    /// the caller can say which one it was. Only an exception from the game is unexpected, and it
    /// is caught here: this is called from a click handler and must never reach the game loop.
    /// </summary>
    public static class TerminalActionRunner
    {
        /// <summary>What happened to the action that was asked to run.</summary>
        public enum RunResult
        {
            /// <summary>The action was applied at least once.</summary>
            Ok = 0,

            /// <summary>The block the card stands for no longer exists.</summary>
            BlockGone = 1,

            /// <summary>The target no longer reports this action, so there is nothing to run.</summary>
            ActionGone = 2,

            /// <summary>The group the frame stands for is no longer in the terminal system.</summary>
            GroupGone = 3,

            /// <summary>The game threw while applying the action: nothing can be claimed.</summary>
            Refused = 4
        }

        /// <summary>
        /// Applies <paramref name="action"/> to the node it belongs to — to the block, or to every
        /// functional member of the group — and reports how many blocks it ran on.
        ///
        /// <paramref name="groupGridEntityId"/> is only read for a group node, and only to reach
        /// the terminal system that knows the group by name: a group is not an entity, so the
        /// caller passes the grid of one of its members. It is the same indirection the game's own
        /// group item uses (it stores a context block for the same reason), and it is the caller's
        /// because the caller already holds the graph and its cached grid ids.
        /// </summary>
        public static RunResult TryRun(Node node, Port action, long groupGridEntityId, out int applied)
        {
            applied = 0;
            if (node == null || action == null) return RunResult.Refused;

            return node.IsGroup
                ? TryRunGroup(node.GroupName, groupGridEntityId, action, out applied)
                : TryRunBlock(node.EntityId, action, out applied);
        }

        /// <summary>
        /// Applies the action to one block. The action is looked up on the block rather than on
        /// the node's port: the port remembers an id, and the block is what can answer whether that
        /// id still exists for it.
        /// </summary>
        private static RunResult TryRunBlock(long entityId, Port action, out int applied)
        {
            applied = 0;

            IMyTerminalBlock block;
            if (!EntityResolver.TryResolveTerminalBlock(entityId, out block) || block == null)
            {
                return RunResult.BlockGone;
            }

            Sandbox.ModAPI.Interfaces.ITerminalAction gameAction;
            if (!TryFindAction(block, action.Key, out gameAction) || gameAction == null)
            {
                return RunResult.ActionGone;
            }

            if (!TryApply(gameAction, block)) return RunResult.Refused;

            applied = 1;
            return RunResult.Ok;
        }

        /// <summary>
        /// Applies the action to every member of the group the game would apply it to: the ones
        /// that still report the action and are functional. One member refusing is not a failure —
        /// the game applies a group action member by member — so a run where no member took the
        /// action is reported as "the group does not offer it" or, when the attempts threw, as a
        /// refusal. Those are different stories and the player is told which one happened.
        /// </summary>
        private static RunResult TryRunGroup(string groupName, long gridEntityId, Port action,
                                            out int applied)
        {
            applied = 0;

            var members = new List<IMyTerminalBlock>(8);
            if (!TryReadGroupMembers(gridEntityId, groupName, members)) return RunResult.GroupGone;

            int refusals = 0;

            for (int i = 0; i < members.Count; i++)
            {
                IMyTerminalBlock block = members[i];
                if (block == null) continue;

                // The game's own two skips (`MyToolbarItemTerminalGroup.Activate`): a block that
                // is not functional does not run, and one that does not report the action is
                // passed over rather than failing the whole group.
                if (!IsFunctional(block)) continue;

                Sandbox.ModAPI.Interfaces.ITerminalAction gameAction;
                if (!TryFindAction(block, action.Key, out gameAction) || gameAction == null)
                {
                    continue;
                }

                if (TryApply(gameAction, block)) applied++;
                else refusals++;
            }

            if (applied > 0) return RunResult.Ok;
            return refusals > 0 ? RunResult.Refused : RunResult.ActionGone;
        }

        /// <summary>
        /// Fills <paramref name="into"/> with the members of the named group, read through the
        /// terminal system of the given grid. False means "the group could not be read at all",
        /// which the caller reports as a missing group: a terminal system that cannot be reached
        /// and a group that is not there look the same from here, and both mean "nothing to run".
        ///
        /// The list is allocated per call rather than kept in a static buffer: this runs on a click,
        /// not on a frame, and a shared buffer in a static type is state every caller can corrupt.
        /// </summary>
        private static bool TryReadGroupMembers(long gridEntityId, string groupName,
                                               List<IMyTerminalBlock> into)
        {
            into.Clear();
            if (string.IsNullOrEmpty(groupName)) return false;

            IMyCubeGrid grid;
            if (!EntityResolver.TryResolveGrid(gridEntityId, out grid) || grid == null) return false;

            try
            {
                if (MyAPIGateway.TerminalActionsHelper == null) return false;

                IMyGridTerminalSystem system =
                    MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);
                if (system == null) return false;

                IMyBlockGroup group = system.GetBlockGroupWithName(groupName);
                if (group == null) return false;

                // `GetBlocks(list, null)` and not the group's raw `Blocks` set: the raw set is on
                // the engine type, which the whitelist forbids. The ModAPI overload is the same
                // walk, with the game's `IsAccessibleForProgrammableBlock` filter applied — it
                // defaults to true for every block the game creates, so the set differs from the
                // vanilla item's only for a block something deliberately hid from programmable
                // blocks, and hiding a block from scripts is a statement that it is not to be
                // driven by something other than its own toolbar.
                //
                // The list is cleared by the callee as well as by the caller: `TryReadGroupMembers`
                // clears before it can fail, so a stale member list can never be applied twice.
                group.GetBlocks(into, null);
                return true;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("manual-group", "could not read the members of a block group", e, 300);
                return false;
            }
        }

        /// <summary>
        /// The block's action with this id, or null/false when the block does not report it.
        ///
        /// `GetActionWithName` is a lookup in a per-type list the game builds and caches, so it is
        /// cheap, and it answers exactly the question a row asks: "does this block still have this
        /// action". The exception is caught rather than the boolean trusted: a block destroyed
        /// between the refresh and the click can throw from inside the lookup.
        /// </summary>
        private static bool TryFindAction(IMyTerminalBlock block, string actionId,
                                          out Sandbox.ModAPI.Interfaces.ITerminalAction action)
        {
            action = null;
            if (string.IsNullOrEmpty(actionId)) return false;

            try
            {
                action = block.GetActionWithName(actionId);
                return true;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("manual-action", "could not read a block's action", e, 300);
                return false;
            }
        }

        /// <summary>
        /// Whether the block can run anything at all — the game's own test for a group member.
        ///
        /// It is a **state**, not a kind: a block built but damaged below its critical integrity
        /// answers false, and the game skips it exactly as it skips a block under construction.
        /// It is deliberately *not* the question `GridGraphBuilder` asks when it computes a
        /// group's action list — that one is `block is IMyFunctionalBlock`, a fact about the type
        /// (`GridGraphBuilder.IsFunctional`, duplicated from the game's own `isinst MyFunctionalBlock`
        /// in `GetValidActions`). Two different questions with one name in two files: this one is
        /// read at the moment the action is applied, the other once while the list is built.
        ///
        /// A single block is not tested at all: the game's own single-block activation does not
        /// test it, and refusing to run an action the player can see would be a rule of ours, not
        /// the game's.
        ///
        /// A read that throws answers **true** — the same rule the action list follows for a
        /// control whose `Visible` callback failed (ADR-0039): losing a real action because a
        /// property threw would take away something the player can see, and the apply itself is
        /// guarded anyway.
        /// </summary>
        private static bool IsFunctional(IMyTerminalBlock block)
        {
            try
            {
                return block.IsFunctional;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("manual-functional", "could not read whether a block is functional",
                                 e, 300);
                return true;
            }
        }

        /// <summary>
        /// Applies the action, reporting a throw as a refusal. This is the one call that changes
        /// the world, and it is the game's own action body that runs — no state of ours is written,
        /// which is why nothing has to be rolled back on failure (ADR-0018).
        /// </summary>
        private static bool TryApply(Sandbox.ModAPI.Interfaces.ITerminalAction action,
                                     IMyTerminalBlock block)
        {
            try
            {
                action.Apply(block);
                return true;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("manual-apply", "the game refused a manual action", e, 120);
                return false;
            }
        }
    }
}
