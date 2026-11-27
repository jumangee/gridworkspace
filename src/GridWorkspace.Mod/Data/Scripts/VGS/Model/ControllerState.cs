using System.Collections.Generic;

namespace Vgs.Model
{
    /// <summary>
    /// Everything the editor knows about an event controller that no other block has: the line
    /// describing what it checks and the blocks it watches (ADR-0017, ADR-0018).
    ///
    /// <b>Which event the watches belong to is not kept here.</b> The player can switch the event
    /// in the terminal with the editor closed, so the question has to be answerable from the
    /// block: the watch list in storage carries the event id it was written for, and
    /// <c>GridGraph.IsWatchListStale</c> compares it with the live one on every refresh
    /// (ADR-0044). A copy of that number on the node would be a second truth that starts at
    /// "unknown" each time the screen opens — the very blind spot the stored id closes.
    ///
    /// <b>Why this type exists.</b> These fields used to live on <see cref="Node"/> itself, so
    /// every block in the grid — a conveyor tube, a reactor, eighty categories that have nothing
    /// to do with observing anything — paid for an empty watch list and three fields, and the
    /// geometry asked <c>HasStatusLine</c> of every card to be told "no" almost every time. The
    /// plan's Step 3 moves the specific off the general: a node carries a
    /// <see cref="Node.Controller"/> reference when, and only when, the game says its block is a
    /// controller, and that reference *is* the flag (ADR-0030).
    ///
    /// <b>Mutable, and reused across refreshes</b> (ADR-0007). The refresh pass rewrites the same
    /// object instead of replacing it, because the draw path and the hover state hold
    /// <see cref="WatchTarget"/> objects by reference — replacing the list wholesale would make a
    /// highlight jump to whatever took the same slot (ADR-0018).
    ///
    /// <b>Never null-checked where it is created.</b> The node creates it once, when the block is
    /// known to be a controller; every reader still checks, because most nodes have none.
    /// </summary>
    public sealed class ControllerState
    {
        /// <summary>
        /// One line describing what the controller checks — event name, comparison direction and
        /// threshold, whatever the selected event uses — or null when nothing is known yet.
        ///
        /// Written by <c>Vgs.Game.EventControllerProbe</c> from the live block and drawn as a
        /// third line of the card. A snapshot: the game's threshold and event can change between
        /// two refreshes, and the card follows on the next one.
        /// </summary>
        public string StatusText; // review:allow R009 written only by the refresh pass

        /// <summary>
        /// Blocks this controller watches: its "selected blocks" list, in the order the player
        /// added them (ADR-0017).
        ///
        /// Rebuilt from the controller's own storage by the refresh pass, and edited by the watch
        /// gesture through <see cref="GridGraph.TryAddWatch"/> / <see cref="GridGraph.RemoveWatch"/>.
        /// A watch target is drawn as a yellow bar under the card; the list *is* the connection,
        /// so a member is always drawn.
        /// </summary>
        public readonly List<WatchTarget> Watched = new List<WatchTarget>();

        /// <summary>The watch target for this block, or null when the controller does not watch it.</summary>
        public WatchTarget FindWatch(long entityId)
        {
            for (int i = 0; i < Watched.Count; i++)
            {
                if (Watched[i].EntityId == entityId) return Watched[i];
            }
            return null;
        }

        /// <summary>
        /// True when the watch stack is full: the cap is enforced here rather than at draw time,
        /// because a bar with no drawing slot has no "x" and the player could never detach that
        /// block (ADR-0017).
        /// </summary>
        public bool IsFull
        {
            get { return Watched.Count >= GridGraph.MaxPortsPerSide; }
        }
    }
}
