using System;
using System.Collections.Generic;
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
    /// <b>Two kinds of target.</b> A cell holds either a block-plus-action item
    /// (<see cref="MyToolbarItemKind.BlockAction"/>) or a group-plus-action item
    /// (<see cref="MyToolbarItemKind.GroupAction"/>, ADR-0015). The second one names a block
    /// group of the grid's terminal system and applies the action to every functional member
    /// when it fires; the game finds the group through the block the item stores, which is why
    /// a group item carries an entity id at all. The ModAPI's own
    /// <c>MyToolbarApiExtensions.GroupAction</c> passes the *context* block's id there, and so
    /// does this writer: the source block is on the same grid as the group by construction, and
    /// it is the one block that must exist for the item to fire at all.
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
    ///  3. The same action on the same target may appear only once per <i>page</i>. That is
    ///     not our rule: the vanilla toolbar-config screen deletes every other slot of the
    ///     current page holding an equal item before it drops a new one
    ///     (<c>MyGuiScreenToolbarConfigBase.DropGridItemToToolbar</c>), so a duplicate we
    ///     wrote on one page would be silently removed the first time the player edited that
    ///     page. The editor therefore spreads repeats across pages, and reports a failure
    ///     when every page already holds that action. "Same target" means the same block or
    ///     the same group name, so a block and a group that contains it are different targets.
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

            /// <summary>No page has a free cell of the required slot: every one holds something else.</summary>
            NoFreeCell = 2,

            /// <summary>Every page already holds this very action on this target: the game refuses it.</summary>
            ActionOnEveryPage = 3,

            /// <summary>The target no longer reports the action (it was rebuilt, or the list changed).</summary>
            ActionUnavailable = 4,

            /// <summary>The game refused the item; nothing was written.</summary>
            Rejected = 5
        }

        // Cell contents live in Vgs.Model (CellContents / CellKind): the placement rule is a
        // pure decision on that value, so it has to be compiled by tools/offline-check.ps1
        // together with the planner. This class only *reads* the game's item into it — see
        // ReadCell, the one place where MyToolbarItemKind meets CellKind.

        /// <summary>
        /// Places "block → target block, action" into a free cell of the source's toolbar and
        /// reports which cell it took. Returns <see cref="WriteResult.Ok"/> only after reading
        /// the cell back and finding the expected item there — the game can refuse an item, and
        /// reporting a connection that does not exist would be worse than reporting the failure.
        /// </summary>
        public static WriteResult TryWrite(IMyTerminalBlock source, Node sourceNode, Port eventPort,
                                           IMyTerminalBlock target, string actionId, out ToolbarCell cell)
        {
            return TryWrite(source, sourceNode, eventPort, target, actionId, ParameterSet.None, out cell);
        }

        /// <summary>
        /// The same, with the arguments the item must carry (ADR-0050).
        ///
        /// <b>The arguments are part of the item, not a detail beside it.</b> The game stores them
        /// inside the toolbar item and hands them to the action when it fires; an item without
        /// them is an item that runs an action the way a bare button would, which for
        /// <c>Run</c> or <c>RotateToAngle</c> is not the connection the player drew. So they are
        /// written with the item, and the read-back check compares them too — a cell that holds
        /// the right action with the wrong values is not this connection.
        /// </summary>
        public static WriteResult TryWrite(IMyTerminalBlock source, Node sourceNode, Port eventPort,
                                           IMyTerminalBlock target, string actionId,
                                           ParameterSet parameters, out ToolbarCell cell)
        {
            cell = ToolbarCell.None;
            if (target == null) return WriteResult.ActionUnavailable;

            // The action must still be on the target: a block rebuilt between the refresh and
            // the click can have lost it, and an item naming a missing action is dead.
            try
            {
                if (target.GetActionWithName(actionId) == null) return WriteResult.ActionUnavailable;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("toolbar-target", "could not read target actions", e, 120);
                return WriteResult.ActionUnavailable;
            }

            long targetId = target.EntityId;
            ParameterSet values = parameters ?? ParameterSet.None;

            return WriteItem(
                source, sourceNode, eventPort,
                MyToolbarItemInfo.BlockAction(targetId, actionId),
                values,
                (toolbar, index) => ReadCell(toolbar, index).HoldsBlockAction(targetId, actionId, values),
                contents => contents.HoldsBlockAction(targetId, actionId, values),
                out cell);
        }

        /// <summary>
        /// Places "block → group, action" into a free cell of the source's toolbar.
        ///
        /// The group is identified by name, exactly as the game's own item identifies it
        /// (ADR-0015). The source block is stored in the item as its context block: the game
        /// resolves the grid — and through it the group — from that block, and the source is
        /// both on the right grid and guaranteed to exist for as long as the item does.
        ///
        /// <b>The group is resolved before writing, not assumed.</b> A group is a name, so it
        /// can be renamed or dissolved between the refresh and the click; an item naming a group
        /// the terminal system no longer has is a cell the game can never fire, and reading the
        /// cell back would still "succeed" because the item is stored verbatim. So the group is
        /// looked up, and the action is confirmed against its members, before anything is
        /// written — and <see cref="WriteResult.ActionUnavailable"/> then means exactly what the
        /// user is told: the group is gone, or it no longer offers that action.
        /// </summary>
        public static WriteResult TryWriteGroup(IMyTerminalBlock source, Node sourceNode, Port eventPort,
                                                string groupName, string actionId, out ToolbarCell cell)
        {
            return TryWriteGroup(source, sourceNode, eventPort, groupName, actionId, ParameterSet.None,
                                 out cell);
        }

        /// <summary>The same, with the arguments the group item carries (ADR-0050).</summary>
        public static WriteResult TryWriteGroup(IMyTerminalBlock source, Node sourceNode, Port eventPort,
                                                string groupName, string actionId, ParameterSet parameters,
                                                out ToolbarCell cell)
        {
            cell = ToolbarCell.None;
            if (string.IsNullOrEmpty(groupName) || string.IsNullOrEmpty(actionId))
            {
                return WriteResult.ActionUnavailable;
            }

            if (!GroupStillOffers(source, groupName, actionId)) return WriteResult.ActionUnavailable;

            long contextId = source != null ? source.EntityId : 0;
            ParameterSet values = parameters ?? ParameterSet.None;

            return WriteItem(
                source, sourceNode, eventPort,
                MyToolbarItemInfo.GroupAction(contextId, groupName, actionId),
                values,
                (toolbar, index) => ReadCell(toolbar, index).HoldsGroupAction(groupName, actionId, values),
                contents => contents.HoldsGroupAction(groupName, actionId, values),
                out cell);
        }

        /// <summary>
        /// True when the grid's terminal system still has this group and at least one of its
        /// members still reports the action.
        ///
        /// It is the cheap half of the check the block path gets for free from
        /// `GetActionWithName` on a single block: a group has no single block to ask, so the
        /// members are asked instead and the first one that knows the action is enough — the
        /// game itself applies the action per member and skips the ones that do not have it.
        /// </summary>
        private static bool GroupStillOffers(IMyTerminalBlock source, string groupName, string actionId)
        {
            try
            {
                if (source == null || MyAPIGateway.TerminalActionsHelper == null) return true;

                IMyCubeGrid grid = source.CubeGrid;
                if (grid == null) return true;

                IMyGridTerminalSystem system = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);
                if (system == null) return true;

                IMyBlockGroup group = system.GetBlockGroupWithName(groupName);
                if (group == null) return false;

                var blocks = new List<IMyTerminalBlock>();
                group.GetBlocks(blocks, null);

                for (int i = 0; i < blocks.Count; i++)
                {
                    IMyTerminalBlock block = blocks[i];
                    if (block == null) continue;
                    if (block.GetActionWithName(actionId) != null) return true;
                }

                return false;
            }
            catch (Exception e)
            {
                // A read that failed is not a refusal: writing an item the game may still
                // accept is better than blocking a legitimate connection, and the read-back
                // below catches the case where it was not accepted after all.
                VgsLog.Throttled("group-check", "could not verify block group", e, 300);
                return true;
            }
        }

        /// <summary>
        /// The shared write: plan a cell, set one item, apply, read the cell back. Both target
        /// kinds go through here so the placement rules and the read-back check cannot drift
        /// apart between a block connection and a group connection.
        /// </summary>
        private static WriteResult WriteItem(IMyTerminalBlock source, Node sourceNode, Port eventPort,
                                             MyToolbarItemInfo item,
                                             ParameterSet parameters,
                                             Func<IMyToolbar, int, bool> pageHoldsSameAction,
                                             Func<CellContents, bool> matches,
                                             out ToolbarCell cell)
        {
            cell = ToolbarCell.None;

            if (source == null || sourceNode == null || eventPort == null) return WriteResult.NotDrivable;

            ToolbarLayout layout = sourceNode.Toolbar;
            if (layout == null || !layout.CanDrive) return WriteResult.NotDrivable;

            try
            {
                IMyToolbar toolbar = ToolbarProbe.GetToolbar(source, layout.ToolbarIndex);
                if (toolbar == null) return WriteResult.NotDrivable;

                // Which cell to take is a pure decision (ToolbarPlanner); reading the current
                // contents of the toolbar is the only part that needs the live object. The
                // planner is handed the contents themselves, never a pre-computed "is it free"
                // bool: that argument was once passed inverted and turned every empty toolbar
                // into "no free cell" (R-009).
                int index;
                ToolbarPlacement placement = ToolbarPlanner.Plan(
                    layout,
                    eventPort.Group,
                    cellIndex => ReadCell(toolbar, cellIndex),
                    page => PageHolds(toolbar, layout, page, pageHoldsSameAction),
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
                //
                // <b>Set with parameters, not Set alone.</b> The ModAPI's own
                // `MyToolbarApiExtensions.Set` is the only way to hand the values to the builder:
                // the plain `change.Set(index, item)` writes an item with no arguments, which for
                // a parameterised action is a different item. The extension is also safe when the
                // builder is not the engine's own implementation — it rejects instead of throwing.
                IMyToolbarChangeBuilder change = toolbar.BeginChanges();
                if (change == null) return WriteResult.Rejected;

                ToolbarParameters.Set(change, index, item, parameters);

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
                if (!matches(ReadCell(toolbar, index)))
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
        /// Replaces the item in a cell the connection already occupies, with new arguments — the
        /// write behind "edit the parameters of this connection" (ADR-0050).
        ///
        /// <b>Why not clear-then-write.</b> Clearing first would leave the cell empty for the
        /// duration of two batches, and a timer firing between them would run nothing: the
        /// connection would be silently lost for a tick. One batch that overwrites the cell has
        /// no such window, and the read-back check is the same one every other write uses.
        ///
        /// Returns <see cref="WriteResult.Ok"/> only when the cell holds the new item afterwards;
        /// a refusal leaves the old item in place, so the caller can tell the player that nothing
        /// changed rather than showing arguments the block does not have.
        /// </summary>
        public static WriteResult TryRewrite(IMyTerminalBlock source, Node sourceNode,
                                             ToolbarCell cell, string actionId,
                                             ParameterSet parameters,
                                             Func<CellContents, bool> matches)
        {
            if (source == null || sourceNode == null || !cell.IsValid) return WriteResult.NotDrivable;

            ToolbarLayout layout = sourceNode.Toolbar;
            if (layout == null || !layout.CanDrive) return WriteResult.NotDrivable;

            try
            {
                IMyToolbar toolbar = ToolbarProbe.GetToolbar(source, cell.ToolbarIndex);
                if (toolbar == null) return WriteResult.NotDrivable;
                if (cell.Index < 0 || cell.Index >= toolbar.ItemCount) return WriteResult.NoFreeCell;

                CellContents current = ReadCell(toolbar, cell.Index);
                if (current.IsEmpty) return WriteResult.NoFreeCell;
                if (!string.Equals(current.ActionId, actionId, StringComparison.Ordinal))
                {
                    // Somebody replaced the item in the terminal since the schema was drawn.
                    // Overwriting it would destroy a connection the player made by hand.
                    return WriteResult.ActionUnavailable;
                }

                IMyToolbarChangeBuilder change = toolbar.BeginChanges();
                if (change == null) return WriteResult.Rejected;

                MyToolbarItemInfo item = current.IsGroupAction
                    ? MyToolbarItemInfo.GroupAction(source.EntityId, current.GroupName, actionId)
                    : MyToolbarItemInfo.BlockAction(current.BlockEntityId, actionId);

                ToolbarParameters.Set(change, cell.Index, item, parameters);

                if (!change.Apply()) return WriteResult.Rejected;
                if (!matches(ReadCell(toolbar, cell.Index))) return WriteResult.Rejected;

                return WriteResult.Ok;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("toolbar-rewrite", "could not rewrite toolbar item", e, 120);
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

                return ReadCell(toolbar, cell.Index).IsEmpty;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("toolbar-clear", "could not clear toolbar item", e, 120);
                return false;
            }
        }

        /// <summary>
        /// Reads what a cell holds. A cell outside the toolbar and an empty cell both report
        /// <see cref="CellContents.IsEmpty"/>; an item the editor cannot draw does not — see
        /// <see cref="CellContents"/> for why the cases are kept apart.
        /// </summary>
        public static CellContents TryDescribe(IMyToolbar toolbar, int index)
        {
            return ReadCell(toolbar, index);
        }

        /// <summary>
        /// True when any cell of this page already holds the same item. This is the vanilla
        /// duplicate rule; see the type comment for where it comes from.
        /// </summary>
        private static bool PageHolds(IMyToolbar toolbar, ToolbarLayout layout, int page,
                                      Func<IMyToolbar, int, bool> holds)
        {
            for (int s = 0; s < layout.SlotCount; s++)
            {
                if (holds(toolbar, layout.CellIndex(page, s))) return true;
            }
            return false;
        }

        /// <summary>
        /// Reads one cell through the ModAPI mirror of the toolbar.
        ///
        /// <b>This is the only place where the game's item vocabulary meets the model's.</b>
        /// Both item kinds the editor can draw are reported with their identity: a
        /// <see cref="MyToolbarItemKind.BlockAction"/> with its block and action, a
        /// <see cref="MyToolbarItemKind.GroupAction"/> with its group name and action. Anything
        /// else (a block definition, an inventory item) becomes <see cref="CellKind.Other"/>,
        /// carrying the game's kind name for the log only.
        ///
        /// An item that claims to be a block or group action but carries no identity — no
        /// block, no action, no group name — is also reported as
        /// <see cref="CellKind.Other"/> rather than as empty. It is not ours and the editor
        /// cannot draw it, but it is also not a free cell: reporting it as empty would let the
        /// planner overwrite somebody's item, which is exactly what
        /// <see cref="CellKind.Other"/> exists to prevent.
        /// </summary>
        private static CellContents ReadCell(IMyToolbar toolbar, int index)
        {
            if (toolbar == null || index < 0 || index >= toolbar.ItemCount) return CellContents.Empty;

            MyToolbarItemInfo info;
            if (!toolbar.TryGetItem(index, out info)) return CellContents.Empty;

            if (info.Kind == MyToolbarItemKind.BlockAction)
            {
                if (info.BlockEntityId == 0 || string.IsNullOrEmpty(info.ActionId))
                {
                    return CellContents.Other(info.Kind.ToString());
                }

                // The arguments travel with the item (ADR-0050). Reading them here rather than
                // on demand is what makes a cell edited in the vanilla terminal a *different*
                // cell, which is how the schema notices that the connection changed.
                return CellContents.BlockAction(info.BlockEntityId, info.ActionId,
                                                ToolbarParameters.Read(toolbar, index));
            }

            if (info.Kind == MyToolbarItemKind.GroupAction)
            {
                if (string.IsNullOrEmpty(info.GroupName) || string.IsNullOrEmpty(info.ActionId))
                {
                    return CellContents.Other(info.Kind.ToString());
                }
                return CellContents.GroupAction(info.GroupName, info.ActionId,
                                                ToolbarParameters.Read(toolbar, index));
            }

            return CellContents.Other(info.Kind.ToString());
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

