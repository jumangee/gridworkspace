using System;
using System.Collections.Generic;
using VRageMath;

namespace Vgs.Model
{
    /// <summary>
    /// A directed connection between an event port of one node and an action port of another.
    ///
    /// The target is either a block (<see cref="TargetEntityId"/>) or a named block group
    /// (<see cref="TargetGroupName"/>) — never both. A group is a legitimate target because
    /// the game has a toolbar item for it (`MyToolbarItemKind.GroupAction`) that applies one
    /// action to every functional member (ADR-0015). The source is always a block: a group
    /// has no toolbar, so nothing can fire from it.
    ///
    /// Connections are stored by port *key*, never by port index: adding a port to a block
    /// type must not silently rewire every existing graph.
    ///
    /// A connection also carries the <see cref="Cell"/> of the source block's toolbar it
    /// occupies. That is not bookkeeping — it is the connection. The editor writes the item
    /// into the block's real toolbar (ADR-0011), and the cell is what distinguishes two
    /// connections of the same event to the same action: the game refuses the same action
    /// twice on one toolbar page and accepts it on another page.
    ///
    /// Many edges may end on the *same* target port. That is the point of the "BlockOn"
    /// action: several blocks can all be told to switch the same reactor on, and the UI shows
    /// it as one input bar with several lines arriving at its dot. The inverse is not true —
    /// one event port of a given group is one source.
    ///
    /// Immutable: a different connection is a different edge, so the graph can hold it in a
    /// list and compare by identity without worrying about a field changing underneath.
    /// </summary>
    public sealed class Edge
    {
        public readonly long SourceEntityId;
        public readonly string SourcePortKey;

        /// <summary>Entity id of the target block, or zero when the target is a group.</summary>
        public readonly long TargetEntityId;

        /// <summary>Name of the target group, or null when the target is a block.</summary>
        public readonly string TargetGroupName;

        public readonly string TargetPortKey;

        /// <summary>Cell of the source block's toolbar that holds this connection's item.</summary>
        public readonly ToolbarCell Cell;

        /// <summary>True when this connection ends on a group instead of a single block.</summary>
        public bool TargetsGroup
        {
            get { return TargetGroupName != null; }
        }

        public Edge(long sourceEntityId, string sourcePortKey, long targetEntityId, string targetPortKey)
            : this(sourceEntityId, sourcePortKey, targetEntityId, targetPortKey, ToolbarCell.None)
        {
        }

        public Edge(long sourceEntityId, string sourcePortKey, long targetEntityId, string targetPortKey,
                    ToolbarCell cell)
            : this(sourceEntityId, sourcePortKey, targetEntityId, targetPortKey, cell, null)
        {
        }

        private Edge(long sourceEntityId, string sourcePortKey, long targetEntityId, string targetPortKey,
                     ToolbarCell cell, string targetGroupName)
        {
            SourceEntityId = sourceEntityId;
            SourcePortKey = sourcePortKey;
            TargetEntityId = targetEntityId;
            TargetGroupName = targetGroupName;
            TargetPortKey = targetPortKey;
            Cell = cell;
        }

        /// <summary>
        /// A connection whose target is the named block group. The group is identified by name
        /// because that is how the game's own toolbar item identifies it: a group has no entity
        /// and no id.
        /// </summary>
        public static Edge ToGroup(long sourceEntityId, string sourcePortKey, string groupName,
                                   string targetPortKey, ToolbarCell cell)
        {
            return new Edge(sourceEntityId, sourcePortKey, 0, targetPortKey, cell, groupName);
        }

        public bool SameAs(Edge other)
        {
            return other != null
                && SourceEntityId == other.SourceEntityId
                && TargetEntityId == other.TargetEntityId
                && string.Equals(TargetGroupName, other.TargetGroupName, StringComparison.Ordinal)
                && string.Equals(SourcePortKey, other.SourcePortKey, StringComparison.Ordinal)
                && string.Equals(TargetPortKey, other.TargetPortKey, StringComparison.Ordinal)
                && Cell.SameAs(other.Cell);
        }

        /// <summary>True when both edges describe the same item in the same toolbar cell.</summary>
        public bool SameCellAs(Edge other)
        {
            return other != null
                && SourceEntityId == other.SourceEntityId
                && Cell.SameAs(other.Cell);
        }

        public override string ToString()
        {
            string target = TargetsGroup ? "group:" + TargetGroupName : TargetEntityId.ToString();
            return SourceEntityId + "." + SourcePortKey + " -> " + target + "." + TargetPortKey
                   + " @ " + Cell;
        }
    }

}
