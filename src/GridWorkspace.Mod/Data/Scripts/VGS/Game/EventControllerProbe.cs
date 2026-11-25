using System;
using System.Collections.Generic;
using System.Globalization;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.ModAPI;
using Vgs.Core;
using Vgs.Model;

namespace Vgs.Game
{
    /// <summary>
    /// Talks to a live event controller: what it checks, what it watches, whether a block may
    /// join its list, and the two writes that change that list (ADR-0017, ADR-0018).
    ///
    /// <b>What a controller is.</b> The block that owns a list of blocks it *observes* and
    /// fires its own toolbar when one of them satisfies a condition. The test is the game's own
    /// interface — <c>Sandbox.ModAPI.IMyEventControllerBlock</c> — never the subtype name, for
    /// the same reason <see cref="ToolbarProbe"/> tests <c>IMyToolbarProvider</c>: a modded
    /// block that implements the interface is a controller too.
    ///
    /// <b>Reading is the game's, writing is the component's.</b> The list the player edits in
    /// the terminal is read through <c>IMyCubeBlock.GetObjectBuilderCubeBlock()</c>, which the
    /// controller fills from its live private fields; the list has no public setter, so what the
    /// editor adds goes through <c>Sandbox.ModAPI.IMyEventComponentWithGui.AddBlocks</c> /
    /// <c>RemoveBlocks</c> — the same door the game's own terminal buttons use. That asymmetry is
    /// the whole design: see ADR-0018. Facts and commands:
    /// <c>docs/01-research/api-event-controller.md</c>.
    ///
    /// <b>Why the subscription only works on the server.</b> The component's
    /// <c>AddBlocks</c> subscribes to the watched block's event only under
    /// <c>Sync.IsServer</c>; on a client it writes into its dictionary and never hears the
    /// event. A watch added on a client would therefore be a port with nothing behind it, which
    /// is the failure this whole design exists to avoid — so the write is refused there and the
    /// player is told why, the same answer <see cref="ToolbarWriter"/> gives.
    ///
    /// Everything here is a boundary: it runs on a click or on a refresh, and an exception
    /// escaping it would reach the game loop.
    /// </summary>
    public static class EventControllerProbe
    {
        /// <summary>Why a block could not be attached to (or detached from) a controller's list.</summary>
        public enum WatchResult
        {
            /// <summary>The list was changed.</summary>
            Ok = 0,

            /// <summary>The block is already in the list.</summary>
            Duplicate = 1,

            /// <summary>The controller has no event selected, so there is nothing to check.</summary>
            NoEvent = 2,

            /// <summary>The selected event does not look at blocks at all.</summary>
            EventIgnoresBlocks = 3,

            /// <summary>The block is on another construct; the controller only sees its own.</summary>
            DifferentConstruct = 4,

            /// <summary>The event itself rejects this kind of block (the game's own test).</summary>
            NotAccepted = 5,

            /// <summary>This machine is not the server, so the subscription would not happen.</summary>
            ClientNotServer = 6,

            /// <summary>The block (or the controller) is gone.</summary>
            BlockGone = 7,

            /// <summary>The game refused the change; nothing was written.</summary>
            Rejected = 8,

            /// <summary>
            /// The block is in the controller's *own* selection — the list the vanilla terminal
            /// edits. A mod can add to the event component but cannot remove from that list, so
            /// the only honest answer is to send the player to the terminal (ADR-0018).
            /// </summary>
            GameOwned = 9
        }

        /// <summary>
        /// True when the block is an event controller. Read from the game's interface, once per
        /// node, when the graph is built.
        /// </summary>
        public static bool IsController(IMyTerminalBlock block)
        {
            if (block == null) return false;

            try
            {
                return block is Sandbox.ModAPI.IMyEventControllerBlock;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("ec-kind", "could not read block kind", e, 600);
                return false;
            }
        }

        /// <summary>
        /// One line describing what the controller currently checks — event, comparison
        /// direction, threshold, and the "AND" mode when it is on — or null when the block is
        /// not a controller or the description cannot be read.
        ///
        /// The event name is the game's own localised string
        /// (<c>SelectedEvent.EventDisplayName</c> through <c>MyTexts</c>), not a name of our
        /// own: the player has to recognise it from the terminal. The threshold and the
        /// comparison sign appear only when the selected event uses them
        /// (<c>IsThresholdUsed</c> / <c>IsConditionSelectionUsed</c>), which is what "whatever
        /// applies" means for this card.
        ///
        /// Called from the refresh pass, never from the draw path: it reads the live block.
        /// </summary>
        public static string Describe(IMyTerminalBlock block)
        {
            if (block == null) return null;

            try
            {
                var controller = block as Sandbox.ModAPI.IMyEventControllerBlock;
                if (controller == null) return null;

                var ingame = block as Sandbox.ModAPI.Ingame.IMyEventControllerBlock;
                if (ingame == null) return null;

                Sandbox.ModAPI.IMyEventControllerEntityComponent selected = controller.SelectedEvent;
                var gui = selected as Sandbox.ModAPI.IMyEventComponentWithGui;
                if (gui == null) return VgsText.WatchStatusNoEvent;

                string name = EventName(selected);
                if (string.IsNullOrEmpty(name)) return VgsText.WatchStatusNoEvent;

                string text;
                if (gui.IsThresholdUsed || gui.IsConditionSelectionUsed)
                {
                    string sign = ingame.IsLowerOrEqualCondition
                        ? VgsText.WatchSignLower
                        : VgsText.WatchSignHigher;

                    string value = ingame.Threshold.ToString(VgsText.WatchThresholdFormat,
                                                            CultureInfo.InvariantCulture);

                    text = string.Format(VgsText.WatchStatusThresholdFormat, name, sign, value);
                }
                else
                {
                    text = string.Format(VgsText.WatchStatusPlainFormat, name);
                }

                // "OR" is the default and stays unwritten; only a non-default mode is worth a
                // line on the card.
                if (ingame.IsAndModeEnabled) text += VgsText.WatchStatusAndSuffix;

                return text;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("ec-describe", "could not describe event controller", e, 600);
                return null;
            }
        }

        /// <summary>
        /// Reads the blocks the controller is watching **as the game itself reports them**
        /// (ADR-0018): the ids in <c>SelectedBlocks</c>, taken from the block's own object
        /// builder.
        ///
        /// <b>Why the object builder and not a getter.</b> The controller keeps its list in
        /// private fields, and the engine type that owns them
        /// (<c>SpaceEngineers.Game.Entities.Blocks.MyEventControllerBlock</c>) is prohibited to a
        /// mod script. What is public is
        /// <c>VRage.Game.ModAPI.IMyCubeBlock.GetObjectBuilderCubeBlock</c>, and the controller's
        /// override of it fills <c>SelectedBlocks</c> from those very fields at the moment of the
        /// call — the IL is quoted in <c>docs/01-research/api-event-controller.md</c> §5. So this
        /// is not a save snapshot: it is the live selection, which is what the schema must show.
        ///
        /// Returns false only when the list could not be read at all (an exception, or a block
        /// that is not a controller). An empty list is a successful read of "watches nothing":
        /// the two must not be confused, or a transient failure would erase every watch port for
        /// one refresh.
        /// </summary>
        public static bool TryReadGameWatchIds(IMyTerminalBlock controller, List<long> into)
        {
            if (into == null) return false;
            into.Clear();
            if (controller == null) return false;

            try
            {
                var cube = controller as VRage.Game.ModAPI.IMyCubeBlock;
                if (cube == null) return false;

                // copy: false — the returned builder is a fresh object either way (the engine's
                // override allocates it), and asking for a copy would also deep-copy the
                // component stack on every refresh, which this path has no use for.
                var builder = cube.GetObjectBuilderCubeBlock(false)
                              as Sandbox.Common.ObjectBuilders.MyObjectBuilder_EventControllerBlock;
                if (builder == null) return false;

                // The property is a VRage collection whose type lives outside the script
                // whitelist, so it is read through IList — the same ids, no reference to a
                // prohibited type.
                var ids = builder.SelectedBlocks as System.Collections.IList;
                if (ids == null) return true;      // no list at all: nothing is watched

                for (int i = 0; i < ids.Count; i++)
                {
                    object raw = ids[i];
                    if (!(raw is long)) continue;

                    long id = (long)raw;
                    if (id > 0 && !into.Contains(id)) into.Add(id);
                }

                return true;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("ec-read-ids", "could not read the controller's selection", e, 120);
                into.Clear();
                return false;
            }
        }

        /// <summary>
        /// Identity of the event the controller currently checks, or 0 when none is selected.
        ///
        /// A watch added by the editor lives in the event *component*, and the game builds a new
        /// component when the player picks another event. Comparing this value across refreshes
        /// is how the editor notices that its own additions have to be replayed (ADR-0018).
        /// </summary>
        public static long SelectedEventId(IMyTerminalBlock block)
        {
            if (block == null) return 0;

            try
            {
                var controller = block as Sandbox.ModAPI.IMyEventControllerBlock;
                if (controller == null) return 0;

                Sandbox.ModAPI.IMyEventControllerEntityComponent selected = controller.SelectedEvent;
                return selected != null ? selected.UniqueSelectionId : 0;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("ec-eventid", "could not read the selected event id", e, 600);
                return 0;
            }
        }

        /// <summary>
        /// The name of the event the controller currently checks, as the game localises it, or
        /// an empty string when nothing is selected. Used both for the card's mode description
        /// and for the refusal message that has to name the event a block did not fit.
        /// </summary>
        public static string SelectedEventName(IMyTerminalBlock block)
        {
            if (block == null) return string.Empty;

            try
            {
                var controller = block as Sandbox.ModAPI.IMyEventControllerBlock;
                if (controller == null) return string.Empty;

                return EventName(controller.SelectedEvent);
            }
            catch (Exception e)
            {
                VgsLog.Throttled("ec-eventname", "could not read the selected event name", e, 600);
                return string.Empty;
            }
        }

        /// <summary>
        /// Checks whether this block may join the controller's list, without changing
        /// anything. The editor calls it before writing so that a refusal costs nothing and
        /// leaves no half-made state (ADR-0017 §5).
        ///
        /// The tests are the game's, in the game's order: a controller with no event checks
        /// nothing; an event that ignores blocks has no list; the block must be on the same
        /// construct (<c>IsInSameLogicalGroupAs</c> is what the engine's own
        /// <c>ValidateBlock</c> calls); and the event's own
        /// <c>IsBlockValidForList</c> decides the rest — that is where "not every block fits
        /// every event" really lives.
        ///
        /// <b>There is no self test here, on purpose</b> (ADR-0036). The engine's own
        /// <c>ValidateBlock</c> does not exclude the controller — verified by
        /// <c>tools/dump-se-il.ps1 -Assembly SpaceEngineers.Game.dll -Type
        /// 'SpaceEngineers.Game.Entities.Blocks.MyEventControllerBlock' -Method ValidateBlock</c>,
        /// whose whole body is "not null" plus <c>IsInSameLogicalGroupAs</c> — and the event
        /// component's <c>AddBlocks</c> subscribes a block without asking who owns the
        /// controller. A mod that refused a self-watch would forbid a configuration the game
        /// accepts; if the selected event itself rejects the block, the answer is
        /// <see cref="WatchResult.NotAccepted"/> and names the event.
        /// </summary>
        public static bool TryValidate(IMyTerminalBlock controller, IMyTerminalBlock target,
                                       out WatchResult reason)
        {
            reason = WatchResult.Ok;

            if (controller == null || target == null) { reason = WatchResult.BlockGone; return false; }

            try
            {
                var eventController = controller as Sandbox.ModAPI.IMyEventControllerBlock;
                if (eventController == null) { reason = WatchResult.BlockGone; return false; }

                Sandbox.ModAPI.IMyEventControllerEntityComponent selected = eventController.SelectedEvent;
                var gui = selected as Sandbox.ModAPI.IMyEventComponentWithGui;
                if (gui == null) { reason = WatchResult.NoEvent; return false; }

                if (!gui.IsBlocksListUsed) { reason = WatchResult.EventIgnoresBlocks; return false; }

                if (!controller.IsInSameLogicalGroupAs(target))
                {
                    reason = WatchResult.DifferentConstruct;
                    return false;
                }

                if (!gui.IsBlockValidForList(target)) { reason = WatchResult.NotAccepted; return false; }
            }
            catch (Exception e)
            {
                VgsLog.Throttled("ec-validate", "could not validate watch target", e, 120);
                reason = WatchResult.Rejected;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Attaches a block to the controller's list: the storage value and the live
        /// subscription are written together, and the storage is rolled back when the
        /// subscription fails.
        ///
        /// The order is deliberate. The storage is what survives a reload and what the graph is
        /// rebuilt from, so it is written first and undone on failure; the subscription is what
        /// makes the controller actually hear the block, and a failure there leaves nothing
        /// behind.
        ///
        /// <b>This method does not touch the graph.</b> The caller owns both sides: it reserves
        /// the port in the graph and calls <see cref="Vgs.Model.GridGraph.RemoveWatch"/> when
        /// this returns anything but <see cref="WatchResult.Ok"/>, so the picture and the block
        /// cannot disagree.
        /// </summary>
        public static WatchResult TryAddWatch(IMyTerminalBlock controller, IMyTerminalBlock target)
        {
            WatchResult reason;
            if (!TryValidate(controller, target, out reason)) return reason;

            if (!IsServer())
            {
                // Without a server there is no subscription, and a port over a block the game
                // does not listen to would be a lie (see the type comment).
                return WatchResult.ClientNotServer;
            }

            // The duplicate check spans both sources: the block may already be in the game's own
            // selection (ADR-0018), in which case it needs no editor-side addition at all.
            var gameIds = new List<long>();
            if (TryReadGameWatchIds(controller, gameIds) && gameIds.Contains(target.EntityId))
            {
                return WatchResult.Duplicate;
            }

            var list = new List<long>();
            if (!NodeStateStore.TryReadWatchList(controller, list)) list.Clear();

            if (list.Contains(target.EntityId)) return WatchResult.Duplicate;

            list.Add(target.EntityId);

            if (!NodeStateStore.SaveWatchList(controller, list))
            {
                VgsLog.Warn("watch list not saved: controller " + controller.EntityId);
                return WatchResult.Rejected;
            }

            if (!Subscribe(controller, target))
            {
                // Undo the storage: a block in the saved list that the game is not watching
                // would come back as a port on the next load, with nothing behind it.
                list.Remove(target.EntityId);
                NodeStateStore.SaveWatchList(controller, list);
                return WatchResult.Rejected;
            }

            // The subscription is in the event component; the controller's own selection is a
            // private collection that no public method writes. Re-reading it here is what makes
            // the difference visible in the log instead of only in the terminal — see
            // docs/01-research/api-event-controller.md §1c for the three levels of state.
            var after = new List<long>();
            bool inGameList = TryReadGameWatchIds(controller, after) && after.Contains(target.EntityId);

            VgsLog.Info("controller " + controller.EntityId + " now monitors block " + target.EntityId
                        + (inGameList
                            ? " (already in vanilla Selected Blocks)"
                            : " (event component only; vanilla Selected Blocks unchanged)"));
            return WatchResult.Ok;
        }

        /// <summary>
        /// Detaches a block from the controller's list.
        ///
        /// <b>Two owners, two answers (ADR-0018).</b> A block the editor added lives only in the
        /// event component and in the editor's storage, so the editor can take it out. A block
        /// the *player* selected in the vanilla terminal lives in the controller's own private
        /// list, which has no public setter — the mod can unsubscribe the component, but the game
        /// keeps the id and reports it again on the next read, so the port would come straight
        /// back. Pretending to remove it would be worse than refusing, hence
        /// <see cref="WatchResult.GameOwned"/>.
        ///
        /// For an editor-owned block the subscription goes first: if the game refuses it, the
        /// storage must keep the block, or the port would reappear on the next reload while the
        /// controller no longer listened to it. And if the storage write fails after the
        /// unsubscribe succeeded, the subscription is restored — otherwise the controller would
        /// stop hearing a block that storage still lists.
        /// </summary>
        public static WatchResult TryRemoveWatch(IMyTerminalBlock controller, IMyTerminalBlock target)
        {
            if (controller == null || target == null) return WatchResult.BlockGone;
            if (!IsServer()) return WatchResult.ClientNotServer;

            // The game's own selection first: if the id is there, nothing this method can do
            // removes it.
            var gameIds = new List<long>();
            if (TryReadGameWatchIds(controller, gameIds) && gameIds.Contains(target.EntityId))
            {
                return WatchResult.GameOwned;
            }

            var list = new List<long>();
            if (!NodeStateStore.TryReadWatchList(controller, list)) list.Clear();
            if (!list.Contains(target.EntityId)) return WatchResult.Ok;

            if (!Unsubscribe(controller, target)) return WatchResult.Rejected;

            list.Remove(target.EntityId);

            if (!NodeStateStore.SaveWatchList(controller, list))
            {
                VgsLog.Warn("watch list not saved after removal: controller " + controller.EntityId
                            + "; restoring the subscription");
                Subscribe(controller, target);
                return WatchResult.Rejected;
            }

            VgsLog.Info("controller " + controller.EntityId + " no longer watches " + target.EntityId);
            return WatchResult.Ok;
        }

        /// <summary>
        /// Drops an id from the controller's saved watch list without touching the live
        /// component. Used when the watched block no longer exists: there is nothing left to
        /// unsubscribe from, but the id must still leave the storage, or the next refresh would
        /// put the port back and the player could never get rid of it.
        /// </summary>
        public static bool ForgetWatch(IMyTerminalBlock controller, long targetEntityId)
        {
            if (controller == null || targetEntityId == 0) return false;
            if (!IsServer()) return false;

            var list = new List<long>();
            if (!NodeStateStore.TryReadWatchList(controller, list)) return false;
            if (!list.Remove(targetEntityId)) return true;

            return NodeStateStore.SaveWatchList(controller, list);
        }

        /// <summary>
        /// Replays the editor's own additions into the live controller: after a world load, and
        /// after the player switches the event in the terminal (ADR-0018).
        ///
        /// <b>Only the overlay is replayed, never the game's own selection.</b> The game stores
        /// that selection in the save and rebuilds the component from it itself; replaying it
        /// again would be pointless work. What the game does not store is what the editor added
        /// through <c>AddBlocks</c>, and that is what this method puts back into the component —
        /// the only place the controller actually listens from.
        ///
        /// <b>Idempotent, which is what makes a retry safe.</b> The component skips a block it
        /// already has (its generic event checks the key before subscribing), so replaying the
        /// whole list again after a partial attempt subscribes only what was missing. The keeper
        /// relies on that: on a large world load the controller can arrive before the blocks it
        /// watches do.
        ///
        /// Returns the number of blocks the controller was subscribed to. A block that no
        /// longer exists, or that the current event no longer accepts, is skipped rather than
        /// removed from storage: the player may switch the event back, and silently deleting
        /// their list would be a data loss nobody asked for.
        /// </summary>
        public static int ReapplyWatchList(IMyTerminalBlock controller)
        {
            if (controller == null) return 0;
            if (!IsServer()) return 0;

            var list = new List<long>();
            if (!NodeStateStore.TryReadWatchList(controller, list) || list.Count == 0) return 0;

            var gui = SelectedGui(controller);
            if (gui == null) return 0;

            var blocks = new List<IMyTerminalBlock>(list.Count);
            try
            {
                for (int i = 0; i < list.Count; i++)
                {
                    IMyTerminalBlock block;
                    if (!EntityResolver.TryResolveTerminalBlock(list[i], out block)) continue;
                    if (!controller.IsInSameLogicalGroupAs(block)) continue;
                    blocks.Add(block);
                }
            }
            catch (Exception e)
            {
                VgsLog.Throttled("ec-reapply-resolve", "could not resolve watched blocks", e, 300);
                return 0;
            }

            if (blocks.Count == 0) return 0;

            try
            {
                gui.AddBlocks(blocks);
                gui.RefreshDetailedInfo();
            }
            catch (Exception e)
            {
                VgsLog.Throttled("ec-reapply", "could not reapply watch list", e, 300);
                return 0;
            }

            VgsLog.Info("controller " + controller.EntityId + " resumed watching "
                        + blocks.Count + " block(s)");
            return blocks.Count;
        }

        // ------------------------------------------------------------------ internals

        private static bool Subscribe(IMyTerminalBlock controller, IMyTerminalBlock target)
        {
            var gui = SelectedGui(controller);
            if (gui == null) return false;

            try
            {
                gui.AddBlocks(new List<IMyTerminalBlock> { target });
                gui.RefreshDetailedInfo();
                return true;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("ec-subscribe", "could not add a block to the controller", e, 120);
                return false;
            }
        }

        private static bool Unsubscribe(IMyTerminalBlock controller, IMyTerminalBlock target)
        {
            var gui = SelectedGui(controller);
            if (gui == null) return false;

            try
            {
                gui.RemoveBlocks(new List<IMyTerminalBlock> { target });
                gui.RefreshDetailedInfo();
                return true;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("ec-unsubscribe", "could not remove a block from the controller", e, 120);
                return false;
            }
        }

        private static Sandbox.ModAPI.IMyEventComponentWithGui SelectedGui(IMyTerminalBlock controller)
        {
            try
            {
                var eventController = controller as Sandbox.ModAPI.IMyEventControllerBlock;
                if (eventController == null) return null;

                return eventController.SelectedEvent as Sandbox.ModAPI.IMyEventComponentWithGui;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("ec-event", "could not read the selected event", e, 600);
                return null;
            }
        }

        private static string EventName(Sandbox.ModAPI.IMyEventControllerEntityComponent selected)
        {
            try
            {
                // The game's own localised name for the event: the player must recognise it
                // from the terminal, so a name of our own would be worse than none.
                string text = MyTexts.GetString(selected.EventDisplayName);
                return text ?? string.Empty;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("ec-name", "could not read the event name", e, 600);
                return string.Empty;
            }
        }

        /// <summary>
        /// True on the machine that owns the controller's list — a single-player game or the
        /// host. Read from the ModAPI mirror of the multiplayer state, never from a config
        /// flag: a client cannot subscribe the controller to anything.
        /// </summary>
        public static bool IsServer()
        {
            try
            {
                IMyMultiplayer multiplayer = MyAPIGateway.Multiplayer;
                if (multiplayer == null) return true;      // no multiplayer layer: single player
                if (!multiplayer.MultiplayerActive) return true;
                return multiplayer.IsServer;
            }
            catch (Exception e)
            {
                // An unreadable multiplayer state is treated as "not the server": writing a
                // subscription that does not happen is worse than refusing with a message.
                VgsLog.Throttled("ec-server", "could not read multiplayer role", e, 600);
                return false;
            }
        }
    }
}
