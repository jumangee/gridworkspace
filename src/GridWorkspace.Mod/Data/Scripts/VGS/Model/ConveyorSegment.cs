using System;

namespace Vgs.Model
{
    /// <summary>
    /// One raw conveyor line between two blocks, as the game serialises it.
    ///
    /// <b>What it is not.</b> This is not a connection on the schema and not a toolbar item: it
    /// is the world's own conveyor line, reduced to the only fact the editor needs — which two
    /// blocks its ends stand on. A line may run through any number of conveyor tubes and
    /// junctions; those are not vertices here, because the game has already collapsed them into
    /// one line (ADR-0040).
    ///
    /// <b>Why a separate type from <see cref="ConveyorLink"/>.</b> A segment is a fact read from
    /// one grid: two block ids and nothing else. Several segments may describe the same pair of
    /// blocks, a segment may end on a block that has no card, and a segment may be part of a
    /// longer path. The logical link the schema draws is the result of walking these segments,
    /// not the segment itself, and keeping the two apart is what lets the walk be checked
    /// offline.
    ///
    /// <b>Direction is deliberately dropped.</b> The game's line has two ends and a conductivity
    /// direction, but a pipe is a physical connection: the schema shows that two blocks are on
    /// one conveyor network, not which way items may flow through a sorter. Keeping direction
    /// would make the picture depend on sorter settings that the requirement does not mention.
    /// </summary>
    public struct ConveyorSegment
    {
        /// <summary>Entity id of the block at one end of the line.</summary>
        public readonly long A;

        /// <summary>Entity id of the block at the other end of the line.</summary>
        public readonly long B;

        public ConveyorSegment(long a, long b)
        {
            A = a;
            B = b;
        }

        public override string ToString()
        {
            return A + " -- " + B;
        }
    }
}
