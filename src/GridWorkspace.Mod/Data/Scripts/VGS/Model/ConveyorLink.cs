using System;

namespace Vgs.Model
{
    /// <summary>
    /// One logical conveyor connection the schema draws: two blocks that are on the same
    /// conveyor network, with no third block <em>that is drawn on the schema</em> between them
    /// (ADR-0040).
    ///
    /// <b>Not a connection.</b> A connection is an item in a toolbar: it has a source port, a
    /// target port and a toolbar cell, the player can create and remove it, and the game
    /// executes it (ADR-0011). A conveyor link has none of that. It is derived from the world,
    /// read-only, never written to a block, and never persisted: the pipes exist because the
    /// blocks are physically connected, not because the editor remembered anything.
    ///
    /// <b>Undirected, and stored once.</b> A pipe has no source and target in the game's sense;
    /// the fields are named <see cref="SourceEntityId"/> and <see cref="TargetEntityId"/> only
    /// because the draw path needs two ends. The producer normalises the pair so that the
    /// smaller id is the source, which is what makes the list deterministic and lets the
    /// producer use the pair itself as a de-duplication key.
    ///
    /// <b>Why a class and not a struct.</b> The list is rebuilt on the structural pass and read
    /// by the draw path, like <see cref="Edge"/>; a class keeps the two ends together without
    /// copying them at every access and matches the rest of the graph's DTOs.
    /// </summary>
    public sealed class ConveyorLink
    {
        /// <summary>Entity id of one end of the link; always the smaller of the pair.</summary>
        public readonly long SourceEntityId;

        /// <summary>Entity id of the other end of the link; always the larger of the pair.</summary>
        public readonly long TargetEntityId;

        /// <summary>
        /// Creates a link. The pair is normalised here, so a caller cannot accidentally produce
        /// two links for the same pair by passing the ends in the other order.
        /// </summary>
        public ConveyorLink(long sourceEntityId, long targetEntityId)
        {
            if (sourceEntityId <= targetEntityId)
            {
                SourceEntityId = sourceEntityId;
                TargetEntityId = targetEntityId;
            }
            else
            {
                SourceEntityId = targetEntityId;
                TargetEntityId = sourceEntityId;
            }
        }

        public bool SameAs(ConveyorLink other)
        {
            return other != null
                && SourceEntityId == other.SourceEntityId
                && TargetEntityId == other.TargetEntityId;
        }

        public override string ToString()
        {
            return SourceEntityId + " == " + TargetEntityId;
        }
    }
}
