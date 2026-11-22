using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using Vgs.Core;
using Vgs.Model;

namespace Vgs.Game
{
    /// <summary>
    /// Replays the watch entries the *editor* added back into a live event controller after the
    /// world loads (ADR-0018).
    ///
    /// <b>What is replayed, and what is not.</b> The game stores its own selection in the save and
    /// restores it itself, so blocks the player picked in the vanilla terminal need nothing from
    /// this class. What the game does not store is what the editor added through the event
    /// component — that lives only in the block's mod storage, so it has to be put back into the
    /// component, which is the only place the controller actually listens from.
    ///
    /// <b>Why <c>OnEntityAdd</c>.</b> It fires for every way a block can appear with its storage
    /// already populated: loading a world, building a block, pasting a blueprint. A timer-based
    /// sweep would have to walk every grid in the world instead.
    ///
    /// <b>Why the work is deferred.</b> <c>OnEntityAdd</c> can fire before the block's components
    /// are attached, and then the storage reads back empty — and "empty" is indistinguishable
    /// from "this controller has no editor-added entries". So the entity id is queued and retried
    /// on later ticks for a bounded number of attempts; a block that genuinely has nothing
    /// retires on its own.
    ///
    /// Everything here runs on the server only: a client cannot subscribe a controller to
    /// anything (see <see cref="EventControllerProbe"/>), and it does not own the storage write
    /// either.
    /// </summary>
    public static class EventControllerKeeper
    {
        /// <summary>
        /// How many ticks one queued controller is retried before it is given up on.
        ///
        /// Generous on purpose: on a large world load the controller can arrive before the
        /// blocks it watches do, and a short budget would silently leave it watching a subset
        /// while the schema drew the whole list. 600 ticks is about ten seconds at 60 fps, and a
        /// replay is idempotent (the game's component skips a block it already has), so retrying
        /// costs a few reads.
        /// </summary>
        private const int MaxAttempts = 600;

        /// <summary>How many queued controllers are processed per tick.</summary>
        private const int MaxPerTick = 4;

        /// <summary>
        /// One queued controller and how many ticks it has been retried for.
        ///
        /// Immutable: a retry replaces the entry with a new value carrying the next attempt
        /// number, so a queued controller cannot be half-updated.
        /// </summary>
        private struct Pending
        {
            public readonly long EntityId;
            public readonly int Attempts;

            public Pending(long entityId, int attempts)
            {
                EntityId = entityId;
                Attempts = attempts;
            }

            public Pending Retried()
            {
                return new Pending(EntityId, Attempts + 1);
            }
        }

        private static readonly List<Pending> Queue = new List<Pending>(8);

        private static bool _installed;

        /// <summary>
        /// Starts listening for new entities. Safe to call repeatedly; does nothing when
        /// already installed or when the entity system is not available yet.
        /// </summary>
        public static void Install()
        {
            if (_installed) return;

            try
            {
                if (MyAPIGateway.Entities == null) return;

                MyAPIGateway.Entities.OnEntityAdd += OnEntityAdd;
                _installed = true;
                VgsLog.Debug("event controller keeper installed");
            }
            catch (Exception e)
            {
                VgsLog.Throttled("ec-keeper-install", "could not install the event controller keeper", e, 600);
            }
        }

        /// <summary>Stops listening and drops the queue. Called on session unload.</summary>
        public static void Uninstall()
        {
            try
            {
                if (_installed && MyAPIGateway.Entities != null)
                {
                    MyAPIGateway.Entities.OnEntityAdd -= OnEntityAdd;
                }
            }
            catch (Exception e)
            {
                VgsLog.Throttled("ec-keeper-uninstall", "could not uninstall the event controller keeper", e, 600);
            }

            _installed = false;
            Queue.Clear();
        }

        private static void OnEntityAdd(IMyEntity entity)
        {
            try
            {
                if (entity == null) return;

                // Only the server can subscribe a controller to anything (see
                // EventControllerProbe), so a client must not even queue the work.
                if (!EventControllerProbe.IsServer()) return;

                // The interface test is the same one the graph uses to decide whether a block
                // is a controller: no table of subtypes, and modded controllers are included.
                var block = entity as IMyTerminalBlock;
                if (block == null) return;
                if (!EventControllerProbe.IsController(block)) return;

                // Deduplicated: the game can report the same entity more than once, and a second
                // replay of the same controller would add a second subscription (the component
                // skips a block it already has, but the engine's own AddBlocks path also attaches
                // an OnClosing handler per call — the cheapest answer is not to queue it twice).
                if (Contains(entity.EntityId)) return;

                Queue.Add(new Pending(entity.EntityId, 0));
            }
            catch (Exception e)
            {
                // An exception escaping a game event would reach the session loop.
                VgsLog.Throttled("ec-keeper-add", "could not queue an event controller", e, 600);
            }
        }

        /// <summary>
        /// Processes the queue. Called once per tick by the session component, before the game
        /// has had a chance to finish loading a controller's storage.
        /// </summary>
        public static void Update()
        {
            if (Queue.Count == 0) return;

            try
            {
                if (!EventControllerProbe.IsServer())
                {
                    // A client has nothing to replay, and the write would be refused anyway.
                    Queue.Clear();
                    return;
                }

                int processed = 0;
                for (int i = 0; i < Queue.Count && processed < MaxPerTick; )
                {
                    Pending pending = Queue[i].Retried();

                    if (TryApply(pending.EntityId) || pending.Attempts >= MaxAttempts)
                    {
                        Queue.RemoveAt(i);
                    }
                    else
                    {
                        Queue[i] = pending;
                        i++;
                    }
                    processed++;
                }
            }
            catch (Exception e)
            {
                // Throttled, and the queue is kept: one bad tick must not silently drop every
                // controller that is still waiting for its blocks. Entries retire on their own
                // retry budget.
                VgsLog.Throttled("ec-keeper-update", "event controller keeper failed", e, 300);
            }
        }

        private static bool Contains(long entityId)
        {
            for (int i = 0; i < Queue.Count; i++)
            {
                if (Queue[i].EntityId == entityId) return true;
            }
            return false;
        }

        /// <summary>
        /// Applies one controller's stored list. Returns true when the entry is finished with —
        /// because every stored id was replayed, or because there is nothing to replay at all.
        ///
        /// <b>Partial is not done.</b> <see cref="EventControllerProbe.ReapplyWatchList"/> skips
        /// blocks that do not exist yet, which on a world load is the normal case: the
        /// controller can arrive before half the blocks it watches. Treating any progress as
        /// success would leave the controller watching a subset while the schema drew the whole
        /// list — a picture that lies. So the entry stays queued until the replayed count covers
        /// the stored list, or the retry budget runs out (and then the shortfall is logged).
        /// </summary>
        private static bool TryApply(long entityId)
        {
            try
            {
                var block = MyAPIGateway.Entities.GetEntityById(entityId) as IMyTerminalBlock;
                if (block == null) return true;

                var ids = new List<long>();
                if (!NodeStateStore.TryReadWatchList(block, ids))
                {
                    // No value. Either the block never stored anything (the common case: the
                    // editor was never used on it) or the component is not attached yet. A
                    // missing component means "not yet" — retry; an attached component without
                    // our key means "nothing to replay" — done.
                    return !NodeStateStore.HasStorage(block);
                }

                if (ids.Count == 0) return true;

                int applied = EventControllerProbe.ReapplyWatchList(block);
                if (applied >= ids.Count) return true;

                VgsLog.Throttled("ec-keeper-partial",
                    "controller " + entityId + " replayed " + applied + " of " + ids.Count
                    + " watch entr(ies); retrying", 120);
                return false;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("ec-keeper-apply", "could not replay a watch list", e, 300);
                return true;
            }
        }
    }
}
