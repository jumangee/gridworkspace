using System;
using System.Collections.Generic;

namespace Vgs.Model
{
    /// <summary>
    /// Turns the game's raw conveyor lines into the logical links the schema draws
    /// (ADR-0040).
    ///
    /// <b>The problem.</b> A conveyor network is many-to-many: one line may run through any
    /// number of tubes, a junction joins several lines, and loops are normal. Connecting every
    /// pair of terminal blocks in one connected component would draw n² lines and say nothing:
    /// on a cargo ship almost every container is reachable from every other one.
    ///
    /// <b>The rule.</b> The nodes of the picture are the blocks that are <em>already drawn on
    /// the schema</em>. The walk starts at one such node, follows the raw lines, and
    /// <b>stops at the next drawn node</b>: it records a link and does not continue through it.
    /// Everything else — conveyor tubes, junctions, cargo containers hidden with "Show in Grid
    /// Workspace", blocks of a subgrid the player took out of the picture — is traversed
    /// through. The result is the adjacency graph of the drawn nodes through the conveyor
    /// network: each link is a maximal conveyor path whose interior contains no drawn node.
    ///
    /// <b>Why this is the right reduction.</b> It is exactly the "points of intersection are
    /// the blocks already on the schema" rule: a junction that has a card becomes a hub with
    /// links to its neighbours, and a junction without a card is invisible, so its neighbours
    /// become directly linked. The picture therefore says "these two are next to each other on
    /// the conveyor network" rather than "these two are somewhere on the same ship".
    ///
    /// <b>Hidden terminal blocks are pass-through, not cut vertices.</b> Hiding a card is a
    /// decision about the picture, not about the ship: the conveyor line through that block
    /// still works in the game, so breaking the visible link at it would make the schema lie.
    /// The same rule already governs a hidden block's toolbar edges (ADR-0013) and a hidden
    /// watched block (ADR-0017).
    ///
    /// <b>Loops, duplicates and self-links.</b> The walk marks visited blocks, so a loop cannot
    /// hang it. A pair is recorded once, even when several paths or several parallel lines join
    /// the same two nodes. A path that returns to its source without passing another drawn node
    /// is a loop, not a link, and is dropped: a pipe from a block to itself would carry no
    /// information.
    ///
    /// <b>Order is deterministic.</b> Sources are visited in the caller's order (the graph's
    /// node order), neighbours in the order the game serialised the lines, and a pair is
    /// recorded only from its smaller entity id. The same ship therefore produces the same list
    /// on every refresh, which is what keeps the picture from reshuffling between passes.
    ///
    /// <b>No game references.</b> This type is pure data, so the reduction can be exercised
    /// without starting Space Engineers — the only practical way to check a graph rule whose
    /// failure is a wrong picture rather than an exception.
    /// </summary>
    public static class ConveyorNetwork
    {
        /// <summary>
        /// Fills <paramref name="into"/> with the logical conveyor links between the visible
        /// nodes, and returns how many links were written. The list is cleared first; it belongs
        /// to the caller and is reused across refreshes.
        ///
        /// <paramref name="segments"/> are the raw lines read from the world, in any order.
        /// <paramref name="visibleIds"/> are the entity ids of the nodes that are drawn on the
        /// schema, in the order they should be used as sources. An id that is not in the raw
        /// lines simply contributes no link.
        ///
        /// Returns zero for every degenerate input — no segments, fewer than two drawn nodes, a
        /// null list — and leaves the output empty, because "nothing to draw" is the same answer
        /// in all of them and the caller treats it as such.
        /// </summary>
        public static int Build(IList<ConveyorSegment> segments, IList<long> visibleIds,
                                IList<ConveyorLink> into)
        {
            if (into == null) return 0;
            into.Clear();

            if (segments == null || segments.Count == 0) return 0;
            if (visibleIds == null || visibleIds.Count < 2) return 0;

            // Adjacency of the raw line graph. Both directions: a pipe is a physical
            // connection, not a one-way item route (see ConveyorSegment).
            var adjacency = new Dictionary<long, List<long>>(segments.Count * 2);
            for (int i = 0; i < segments.Count; i++)
            {
                ConveyorSegment segment = segments[i];
                if (segment.A <= 0 || segment.B <= 0) continue;
                if (segment.A == segment.B) continue;

                AddNeighbour(adjacency, segment.A, segment.B);
                AddNeighbour(adjacency, segment.B, segment.A);
            }
            if (adjacency.Count == 0) return 0;

            var visible = new HashSet<long>();
            for (int i = 0; i < visibleIds.Count; i++)
            {
                long id = visibleIds[i];
                if (id > 0) visible.Add(id);
            }
            if (visible.Count < 2) return 0;

            // One pair is recorded once. The set is keyed by the normalised pair, and the
            // normalisation is the same one ConveyorLink performs, so the two cannot disagree.
            var pairs = new HashSet<Pair>();
            var visited = new HashSet<long>();
            var queue = new Queue<long>();

            for (int s = 0; s < visibleIds.Count; s++)
            {
                long source = visibleIds[s];
                if (source <= 0) continue;
                if (!adjacency.ContainsKey(source)) continue;

                visited.Clear();
                queue.Clear();
                visited.Add(source);
                queue.Enqueue(source);

                while (queue.Count > 0)
                {
                    long current = queue.Dequeue();

                    List<long> neighbours;
                    if (!adjacency.TryGetValue(current, out neighbours)) continue;

                    for (int n = 0; n < neighbours.Count; n++)
                    {
                        long neighbour = neighbours[n];
                        if (neighbour == source) continue; // a loop back to the start is not a link

                        if (visible.Contains(neighbour))
                        {
                            // A drawn node is a point of intersection: record the link and
                            // stop. Only the smaller id records it, which is what makes the
                            // list deterministic and free of mirrored duplicates.
                            if (source >= neighbour) continue;

                            if (pairs.Add(new Pair(source, neighbour)))
                            {
                                into.Add(new ConveyorLink(source, neighbour));
                            }
                            continue;
                        }

                        // A hidden block, a tube or a junction: walk through it.
                        if (visited.Add(neighbour)) queue.Enqueue(neighbour);
                    }
                }
            }

            return into.Count;
        }

        private static void AddNeighbour(Dictionary<long, List<long>> adjacency, long from, long to)
        {
            List<long> list;
            if (!adjacency.TryGetValue(from, out list))
            {
                list = new List<long>(4);
                adjacency.Add(from, list);
            }

            // Parallel lines between the same two blocks are one edge of the raw graph. The
            // check is linear, but a conveyor endpoint has a handful of ports, not hundreds.
            if (!list.Contains(to)) list.Add(to);
        }

        /// <summary>
        /// A normalised unordered pair, used only to de-duplicate links. A struct so the walk
        /// allocates one set entry per distinct pair rather than one object per comparison.
        /// </summary>
        private struct Pair : IEquatable<Pair>
        {
            private readonly long _a;
            private readonly long _b;

            public Pair(long a, long b)
            {
                if (a <= b) { _a = a; _b = b; }
                else { _a = b; _b = a; }
            }

            public bool Equals(Pair other)
            {
                return _a == other._a && _b == other._b;
            }

            public override bool Equals(object obj)
            {
                return obj is Pair && Equals((Pair)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (_a.GetHashCode() * 397) ^ _b.GetHashCode();
                }
            }
        }
    }
}
