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
        /// What this session wrote into one controller's event component: the component itself and
        /// the blocks it was subscribed to.
        ///
        /// <b>The ids are kept, not re-read.</b> Releasing a subscription needs the blocks, and the
        /// stored list is exactly what may already be gone — the player can detach the last entry
        /// from the schema after switching the event, and then the storage is empty while the old
        /// component is still firing. What this session subscribed is known to this session, so it
        /// is remembered here rather than inferred from a value that may have been cleared.
        /// </summary>
        private sealed class Subscription
        {
            public readonly IMyEventComponentWithGui Component;
            public readonly List<long> Ids = new List<long>(4);

            public Subscription(IMyEventComponentWithGui component)
            {
                Component = component;
            }
        }

        /// <summary>
        /// The event component each controller was last subscribed into, by block entity id.
        ///
        /// <b>Why a reference has to be kept.</b> Switching the event in the terminal does not
        /// destroy the old component — it stays attached to the block, unselected, and keeps the
        /// subscriptions the editor made in it (see
        /// <c>docs/01-research/api-event-controller.md</c> §1f). The game only unsubscribes the
        /// blocks of its *own* list, so an editor-added block would go on firing the controller's
        /// toolbar under an event the player no longer checks. The selected component is readable
        /// through <c>SelectedEvent</c>; the old one is not reachable at all, so the only way to
        /// release it is to remember it when the subscription is made.
        ///
        /// <b>Session state, and cleared on unload.</b> The entries are game objects; a static
        /// map that outlived the session would be exactly the leak of R-002. The keeper clears it
        /// in <see cref="ReleaseSubscriptions"/>, and an entry is dropped as soon as the block is
        /// gone, the component is released, or the list it describes is emptied from the schema.
        /// </summary>
        private static readonly Dictionary<long, Subscription> _subscriptions =
            new Dictionary<long, Subscription>(4);

        /// <summary>
        /// Reused buffer of controller ids for the subscription poll: the loop removes entries as
        /// it goes, and a dictionary cannot be modified while it is enumerated.
        /// </summary>
        private static readonly List<long> _pollScratch = new List<long>(4);

        /// <summary>
        /// Reads the editor's own additions to a controller's list — the entries the game does not
        /// store — and reconciles them with the event they were added for (ADR-0018, ADR-0044).
        ///
        /// <b>The event decides whether the list still exists.</b> Picking another event in the
        /// terminal makes the game throw its own selection away, and a watch the editor added
        /// through the event component is subscribed to a component that is no longer selected. So
        /// the stored list carries the id of the event it was made for, and a list that belongs to
        /// another event is <b>dropped</b> here: released from the old component, emptied in
        /// storage, and returned empty, which removes its bars on the same refresh. This is the one
        /// place that decides it — the graph builder and the keeper both come through here, so the
        /// schema and the live component cannot disagree about a list that is gone.
        ///
        /// <b>Two writes, both on the server only.</b> A stale list is emptied, and a list written
        /// by an older version — which stored no event id — is stamped with the current one instead
        /// of being erased: its ids are a player's data, and "the file predates the field" is not
        /// evidence that the event changed. On a client nothing is written; the empty list is
        /// returned all the same, so the picture follows the game there too.
        ///
        /// Returns false only when the block stores nothing at all, which the keeper has to tell
        /// apart from "stores an empty list" (see <see cref="NodeStateStore.HasStorage"/>). The
        /// returned <paramref name="eventId"/> is the event the list now belongs to, so a caller
        /// that writes the list back — pruning an adopted id, say — cannot lose it.
        /// </summary>
        public static bool ReadOverlay(IMyTerminalBlock controller, List<long> into, out long eventId)
        {
            eventId = 0;
            if (into == null) return false;

            if (!NodeStateStore.TryReadWatchList(controller, into, out eventId))
            {
                into.Clear();
                eventId = 0;
                return false;
            }

            if (into.Count == 0) return true;

            // 0 is "the game has not said which event it checks", not "another event": an event
            // component that is not attached yet must not cost the player their list.
            long current = SelectedEventId(controller);
            if (current <= 0) return true;

            if (GridGraph.IsWatchListStale(eventId, current))
            {
                // Drop clears the list and writes the empty value itself; clearing it again here
                // would read as a second, separate decision.
                Drop(controller, into, eventId, current);
                eventId = current;
                return true;
            }

            if (eventId == 0)
            {
                // Written before the event id was part of the value. Claim it for the event the
                // controller checks now, which is the assumption the old code made every refresh.
                eventId = current;
                if (IsServer()) NodeStateStore.SaveWatchList(controller, into, eventId);
            }

            return true;
        }

        /// <summary>
        /// Drops a stored watch list whose event the controller no longer checks, without a caller
        /// having read it first: the same decision as <see cref="ReadOverlay"/>, made on a timer so
        /// the reset happens when the player switches the event, not when they next open the schema.
        ///
        /// <b>An emptied list is not a reason to skip the release.</b> The player can detach the
        /// last entry from the schema and then switch the event; the storage is empty, but the
        /// subscription made into the old component is still live. So the release happens on the
        /// strength of the remembered component alone, and the storage is only read to decide
        /// whether there is a list to erase as well.
        ///
        /// Returns true when something was released or dropped. Everything it touches is guarded:
        /// it runs from the session tick, and an exception there would reach the game loop.
        /// </summary>
        public static bool DropStaleWatchList(IMyTerminalBlock controller)
        {
            if (controller == null) return false;

            try
            {
                long current = SelectedEventId(controller);
                if (current <= 0) return false;              // no event: no news

                var list = new List<long>();
                long eventId;
                if (!NodeStateStore.TryReadWatchList(controller, list, out eventId) || list.Count == 0)
                {
                    return ReleaseSubscription(controller, current);
                }

                if (!GridGraph.IsWatchListStale(eventId, current))
                {
                    return ReleaseSubscription(controller, current);
                }

                Drop(controller, list, eventId, current);
                return true;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("ec-drop-stale", "could not drop a stale watch list", e, 300);
                return false;
            }
        }

        /// <summary>
        /// Forgets every remembered event component. Called by the keeper on unload, so no game
        /// object survives the session in a static map (R-002). Nothing is written to any block:
        /// a subscription that was still live dies with the session it belonged to.
        /// </summary>
        public static void ReleaseSubscriptions()
        {
            _subscriptions.Clear();
            _pollScratch.Clear();
        }

        /// <summary>
        /// Drops the watch lists of controllers whose event the player has switched, and releases
        /// the subscriptions this session made in the component that is no longer selected
        /// (ADR-0044). Called from the session tick, once a second.
        ///
        /// <b>The cheap question first.</b> The remembered component knows which event it is, so a
        /// controller whose event did not change costs two field reads and no storage access: the
        /// stored value is only parsed when there is something to drop. On a normal ship that is
        /// zero controllers, and on an edited one it is a handful of comparisons per second.
        ///
        /// <b>The decision itself is not repeated here.</b> Once the component's event differs from
        /// the controller's, the controller is handed to <see cref="DropStaleWatchList"/>, which
        /// owns the rule: it erases a list that belongs to another event and, in every case where
        /// the component is no longer the selected one, releases the subscription — including when
        /// the storage holds nothing, because the ids it needs come from the map and not from the
        /// value that may already be gone.
        ///
        /// A controller whose block is gone is forgotten rather than retried: the entity id is
        /// unique for the session, so the entry can never become valid again.
        /// </summary>
        public static void PollStaleSubscriptions()
        {
            if (_subscriptions.Count == 0) return;

            try
            {
                // Server only: the reset writes to the block's storage and calls the event
                // component; a client can do neither, and the schema there follows the game on its
                // own refresh.
                if (!IsServer()) return;

                // Collected first: the loop removes entries, and a dictionary cannot be modified
                // while it is enumerated. The list is reused, so the poll allocates nothing.
                _pollScratch.Clear();
                foreach (KeyValuePair<long, Subscription> entry in _subscriptions)
                {
                    _pollScratch.Add(entry.Key);
                }

                for (int i = 0; i < _pollScratch.Count; i++)
                {
                    long entityId = _pollScratch[i];

                    IMyTerminalBlock block;
                    if (!EntityResolver.TryResolveTerminalBlock(entityId, out block))
                    {
                        // The block is gone; the entity id is unique for the session, so the entry
                        // can never become valid again.
                        ForgetSubscription(entityId);
                        continue;
                    }

                    Subscription entry;
                    if (!_subscriptions.TryGetValue(entityId, out entry)) continue;

                    long current = SelectedEventId(block);
                    if (current <= 0) continue;                      // no event: no news
                    if (entry.Component != null && entry.Component.UniqueSelectionId == current)
                    {
                        continue;                                    // still the same component
                    }

                    // The event changed (or the component is unreadable): drop the list, and
                    // release the subscription even when there is no list left to drop.
                    DropStaleWatchList(block);
                }
            }
            catch (Exception e)
            {
                VgsLog.Throttled("ec-poll-stale", "could not poll watch subscriptions", e, 300);
            }
        }

        /// <summary>
        /// The whole reset: unsubscribe the remembered component, empty the storage, log it. Called
        /// only when the list is known to belong to another event.
        ///
        /// <b>The list is emptied before it is written back, and that order is the fix itself.</b>
        /// The ids are needed by <see cref="ReleaseSubscription"/> — it can only unsubscribe blocks
        /// it can name — and the value that survives must be the empty one. Writing the list back
        /// with the new event id would keep every entry and merely relabel it, which is the defect
        /// this method exists to remove, in its most durable form: it would come back after a
        /// reload.
        /// </summary>
        private static void Drop(IMyTerminalBlock controller, List<long> ids,
                                 long storedEventId, long currentEventId)
        {
            int dropped = ids.Count;

            ReleaseSubscription(controller, currentEventId);
            ids.Clear();

            if (IsServer()) NodeStateStore.SaveWatchList(controller, ids, currentEventId);

            // Throttled, not Info: on a client the storage is not written, so the condition stays
            // true on every refresh until the screen closes, and an unthrottled line would fill
            // the log with one sentence per frame pair.
            VgsLog.Throttled("ec-overlay-stale",
                             "controller " + controller.EntityId + ": the event changed ("
                             + storedEventId + " -> " + currentEventId + "), so " + dropped
                             + " editor-added watch(es) were dropped", 60);
        }

        /// <summary>
        /// Unsubscribes the blocks this session subscribed from the component it wrote them into,
        /// when that component is not the selected one any more (ADR-0044).
        ///
        /// <b>Why this is not optional.</b> The game unsubscribes only the blocks of its own
        /// selection when the event changes, so a component that is no longer selected keeps
        /// calling <c>TriggerAction</c> for the blocks the editor put there — the controller would
        /// go on firing its toolbar under an event it does not check, while the schema showed
        /// nothing. Dropping the picture without dropping the subscription is the lie this project
        /// forbids in the other direction.
        ///
        /// <b>The ids come from the map, not from the storage.</b> The stored list may have been
        /// emptied by the player detaching the last entry from the schema, and the subscription
        /// would then be unreleasable if the ids were read from it. What this session subscribed is
        /// what this session remembers.
        ///
        /// Returns true when an entry was released (or when there was nothing left to release).
        /// </summary>
        private static bool ReleaseSubscription(IMyTerminalBlock controller, long currentEventId)
        {
            if (!IsServer()) return false;

            Subscription entry;
            if (!_subscriptions.TryGetValue(controller.EntityId, out entry)) return false;

            // The component is still the selected one: this is not a switch, and the subscription
            // is live and wanted.
            if (entry.Component != null && entry.Component.UniqueSelectionId == currentEventId)
            {
                return false;
            }

            _subscriptions.Remove(controller.EntityId);
            if (entry.Component == null || entry.Ids.Count == 0) return true;

            try
            {
                var blocks = new List<IMyTerminalBlock>(entry.Ids.Count);
                for (int i = 0; i < entry.Ids.Count; i++)
                {
                    IMyTerminalBlock block;
                    if (EntityResolver.TryResolveTerminalBlock(entry.Ids[i], out block)) blocks.Add(block);
                }

                if (blocks.Count > 0) entry.Component.RemoveBlocks(blocks);
                entry.Component.RefreshDetailedInfo();

                VgsLog.Info("controller " + controller.EntityId + " released "
                            + blocks.Count + " block(s) of the previous event");
            }
            catch (Exception e)
            {
                VgsLog.Throttled("ec-release", "could not release the previous event's blocks", e, 300);
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

            // The overlay is read through the same rule the refresh uses (ADR-0044): a list that
            // belongs to another event is dropped here first, so a watch added right after the
            // player switched the event cannot adopt the stale entries by writing them back under
            // the new event id.
            var list = new List<long>();
            long listEventId;
            if (!ReadOverlay(controller, list, out listEventId)) list.Clear();

            if (list.Contains(target.EntityId)) return WatchResult.Duplicate;

            list.Add(target.EntityId);

            // The event the entries belong to is written with them (ADR-0044): a watch added for
            // this event must be dropped, not replayed, when the player picks another one.
            //
            // `listEventId` is that event whenever there was a value to read — ReadOverlay dropped
            // a stale list, stamped a legacy one, or found the stored id equal to the live one.
            // It is **0** when there was nothing to read: no storage on the block yet, or a value
            // that is already empty. That is the ordinary first attach, and writing it as-is would
            // produce exactly the headerless value the upgrade path has to adopt — a list whose
            // event nobody knows. So the live event is asked here, and only here: on the path where
            // ReadOverlay had nothing to report. `TryValidate` above has already refused a
            // controller without a selected event, so this cannot answer 0.
            long eventId = listEventId > 0 ? listEventId : SelectedEventId(controller);

            if (!NodeStateStore.SaveWatchList(controller, list, eventId))
            {
                VgsLog.Warn("watch list not saved: controller " + controller.EntityId);
                return WatchResult.Rejected;
            }

            if (!Subscribe(controller, target))
            {
                // Undo the storage: a block in the saved list that the game is not watching
                // would come back as a port on the next load, with nothing behind it.
                list.Remove(target.EntityId);
                NodeStateStore.SaveWatchList(controller, list, eventId);
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

            // The overlay is read through the same rule the refresh uses (ADR-0044). A list that
            // belongs to another event is dropped first — which also releases the subscription
            // this session made in the old component — and the removal then has nothing left to
            // do. Removing the last entry of a stale list without this would empty the storage
            // while the old component kept firing, and nothing could release it afterwards: the
            // ids would be gone from the only place that knew them.
            var list = new List<long>();
            long listEventId;
            if (!ReadOverlay(controller, list, out listEventId)) list.Clear();
            if (!list.Contains(target.EntityId)) return WatchResult.Ok;

            if (!Unsubscribe(controller, target)) return WatchResult.Rejected;

            list.Remove(target.EntityId);

            // The event id travels with the list: removing one block does not change which event
            // the remaining entries were added for (ADR-0044).
            if (!NodeStateStore.SaveWatchList(controller, list, listEventId))
            {
                VgsLog.Warn("watch list not saved after removal: controller " + controller.EntityId
                            + "; restoring the subscription");
                Subscribe(controller, target);
                return WatchResult.Rejected;
            }

            // The list is empty, so this session subscribes the controller to nothing: the entry
            // would otherwise be polled until the session ended, and it describes a subscription
            // that no longer exists. With entries left, only this id leaves — the rest are still
            // live in the component ReadOverlay just approved.
            ForgetSubscribedId(controller, target.EntityId);

            VgsLog.Info("controller " + controller.EntityId + " no longer watches " + target.EntityId);
            return WatchResult.Ok;
        }

        /// <summary>
        /// Drops an id from the controller's saved watch list without touching the live
        /// component. Used when the watched block no longer exists: there is nothing left to
        /// unsubscribe from, but the id must still leave the storage, or the next refresh would
        /// put the port back and the player could never get rid of it.
        ///
        /// The stored event id is written back with the list, and the remembered subscription
        /// forgets the id too — a block that is gone cannot be unsubscribed later, and keeping it
        /// in the map would make every release try to resolve an entity that no longer exists.
        /// </summary>
        public static bool ForgetWatch(IMyTerminalBlock controller, long targetEntityId)
        {
            if (controller == null || targetEntityId == 0) return false;
            if (!IsServer()) return false;

            var list = new List<long>();
            long listEventId;
            if (!NodeStateStore.TryReadWatchList(controller, list, out listEventId)) return false;

            ForgetSubscribedId(controller, targetEntityId);

            if (!list.Remove(targetEntityId)) return true;

            return NodeStateStore.SaveWatchList(controller, list, listEventId);
        }

        /// <summary>
        /// Replays the editor's own additions into the live controller after a world load
        /// (ADR-0018).
        ///
        /// <b>Only the overlay is replayed, never the game's own selection.</b> The game stores
        /// that selection in the save and rebuilds the component from it itself; replaying it
        /// again would be pointless work. What the game does not store is what the editor added
        /// through <c>AddBlocks</c>, and that is what this method puts back into the component —
        /// the only place the controller actually listens from.
        ///
        /// <b>A list that belongs to another event is never replayed</b> (ADR-0044). Picking a new
        /// event in the terminal makes the game clear its own selection, and the editor's entries
        /// for the old event describe a configuration that no longer exists; replaying them would
        /// restore exactly the watch the player has just discarded. The keeper drops such a list
        /// through <see cref="ReadOverlay"/> before it gets here, and the check is repeated at this
        /// write boundary so no future caller can subscribe a block under an event it was not
        /// added for.
        ///
        /// <b>Idempotent, which is what makes a retry safe.</b> The component skips a block it
        /// already has (its generic event checks the key before subscribing), so replaying the
        /// whole list again after a partial attempt subscribes only what was missing. The keeper
        /// relies on that: on a large world load the controller can arrive before the blocks it
        /// watches do.
        ///
        /// Returns the number of blocks the controller was subscribed to. A block that no
        /// longer exists, or that the current event no longer accepts, is skipped rather than
        /// removed from storage: the block may still arrive, and the next replay will subscribe it.
        /// </summary>
        public static int ReapplyWatchList(IMyTerminalBlock controller)
        {
            if (controller == null) return 0;
            if (!IsServer()) return 0;

            var list = new List<long>();
            long listEventId;
            if (!NodeStateStore.TryReadWatchList(controller, list, out listEventId) || list.Count == 0)
            {
                return 0;
            }

            if (GridGraph.IsWatchListStale(listEventId, SelectedEventId(controller))) return 0;

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

                // Remembered like every other subscription: a replay is a subscription too, and
                // the player can switch the event right after a world load (ADR-0044).
                for (int i = 0; i < blocks.Count; i++) Remember(controller, gui, blocks[i].EntityId);
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

        /// <summary>
        /// Records that this session subscribed a block into a component, so the subscription can
        /// be released if the player picks another event (ADR-0044).
        ///
        /// An entry for the same component gains the id; an entry for a **different** component is
        /// replaced, because that is what a switch looks like — and the old component is released
        /// before the new one is recorded, so its blocks do not fire under the new event.
        /// </summary>
        private static void Remember(IMyTerminalBlock controller, IMyEventComponentWithGui gui,
                                     long targetEntityId)
        {
            Subscription entry;
            if (_subscriptions.TryGetValue(controller.EntityId, out entry)
                && ReferenceEquals(entry.Component, gui))
            {
                if (!entry.Ids.Contains(targetEntityId)) entry.Ids.Add(targetEntityId);
                return;
            }

            if (entry != null) ReleaseSubscription(controller, gui.UniqueSelectionId);

            entry = new Subscription(gui);
            entry.Ids.Add(targetEntityId);
            _subscriptions[controller.EntityId] = entry;
        }

        /// <summary>Drops one id from the remembered subscription, for a block that is gone.</summary>
        private static void ForgetSubscribedId(IMyTerminalBlock controller, long targetEntityId)
        {
            Subscription entry;
            if (!_subscriptions.TryGetValue(controller.EntityId, out entry)) return;

            entry.Ids.Remove(targetEntityId);
            if (entry.Ids.Count == 0) _subscriptions.Remove(controller.EntityId);
        }

        /// <summary>Drops the remembered subscription of a block, without writing to it.</summary>
        private static void ForgetSubscription(long controllerEntityId)
        {
            _subscriptions.Remove(controllerEntityId);
        }

        private static bool Subscribe(IMyTerminalBlock controller, IMyTerminalBlock target)
        {
            var gui = SelectedGui(controller);
            if (gui == null) return false;

            try
            {
                gui.AddBlocks(new List<IMyTerminalBlock> { target });
                gui.RefreshDetailedInfo();

                // Remembered so the subscription can be released if the player picks another
                // event: the old component stays attached and is no longer reachable from the
                // block (ADR-0044, docs/01-research/api-event-controller.md §1f).
                Remember(controller, gui, target.EntityId);
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
