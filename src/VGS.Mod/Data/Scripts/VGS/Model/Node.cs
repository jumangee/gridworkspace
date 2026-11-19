using System;
using VRageMath;

namespace Vgs.Model
{
    /// <summary>
    /// What a port represents. The distinction matters for the evaluator (stage 3): an
    /// action is something you *invoke* on a block, an event is something that *happened*,
    /// and a value is something you read and compare.
    /// </summary>
    public enum PortKind
    {
        /// <summary>Terminal action, e.g. OnOff, TriggerNow. Consumed by a block.</summary>
        Action = 0,

        /// <summary>Something the block emits: state change, threshold crossing, occupancy.</summary>
        Event = 1,

        /// <summary>Readable scalar, e.g. integrity percentage, stored power.</summary>
        Value = 2
    }

    /// <summary>Direction of data flow for a port.</summary>
    public enum PortDirection
    {
        Input = 0,
        Output = 1
    }

    /// <summary>
    /// One connectable point on a node.
    ///
    /// <see cref="Key"/> is the stable identity used in save data and in the graph; it must
    /// never be renumbered, only added to. <see cref="DisplayName"/> is what the user sees.
    ///
    /// Immutable by design: a port is derived from the block type and never changes while
    /// the block exists, so it is safe to share and impossible to leave half-updated.
    /// </summary>
    public sealed class Port
    {
        public readonly string Key;
        public readonly string DisplayName;
        public readonly PortKind Kind;
        public readonly PortDirection Direction;

        /// <summary>Normalized vertical position on the node edge (0 = top, 1 = bottom).</summary>
        public readonly float Anchor;

        public Port(string key, string displayName, PortKind kind, PortDirection direction, float anchor)
        {
            Key = key;
            DisplayName = displayName;
            Kind = kind;
            Direction = direction;
            Anchor = anchor;
        }

        public override string ToString()
        {
            return Direction + ":" + Key;
        }
    }

    /// <summary>
    /// A block shown on the canvas.
    ///
    /// This is the *view model* of a grid block, not the block itself. Keeping it separate
    /// means the canvas never has to touch game entities while drawing, which is the single
    /// biggest performance trap in an in-game editor: resolving
    /// <c>IMyTerminalBlock</c> members per frame is far more expensive than reading a
    /// cached struct, and the block may be destroyed mid-frame.
    ///
    /// <see cref="EntityId"/> is the link back to the live block; it can become stale and is
    /// re-validated on refresh.
    ///
    /// Mutable by design: the refresh pass rewrites the name and terminal flag, and the drag
    /// handler rewrites <see cref="Position"/> on every pointer move. Making it a value type
    /// or copying it on change would allocate per frame for no safety gain — there are no
    /// cross-field invariants to protect.
    /// </summary>
    public sealed class Node
    {
        public readonly long EntityId;

        /// <summary>Block definition subtype, e.g. "LargeBlockCockpit".</summary>
        public readonly string SubtypeId;

        /// <summary>Grid coordinates of the block, used to seed a readable initial layout.</summary>
        public readonly Vector3I GridPosition;

        /// <summary>Player-visible block name (CustomName). Refreshed when the name changes.</summary>
        public string DisplayName; // review:allow R009 written only by the refresh pass

        /// <summary>
        /// One-line summary drawn under the title (subtype plus port counts).
        ///
        /// Cached on the node instead of being formatted in <c>Draw</c>: the canvas redraws
        /// every frame, and building this string per node per frame is thousands of
        /// allocations a second on a large grid. It only changes when the port list does.
        /// </summary>
        public string Subtitle; // review:allow R009 rebuilt only when ports change

        /// <summary>Position on the canvas in graph units (not pixels, not normalized).</summary>
        public Vector2 Position; // review:allow R009 rewritten by the drag handler every pointer move

        public readonly System.Collections.Generic.List<Port> Inputs =
            new System.Collections.Generic.List<Port>();

        public readonly System.Collections.Generic.List<Port> Outputs =
            new System.Collections.Generic.List<Port>();

        /// <summary>False when the block type exposes no terminal actions at all.</summary>
        public bool HasTerminal; // review:allow R009 rebuilt only when the action set changes

        public Node(long entityId, string subtypeId, string displayName, Vector3I gridPosition)
        {
            EntityId = entityId;
            SubtypeId = subtypeId;
            DisplayName = displayName;
            GridPosition = gridPosition;
            Position = Vector2.Zero;
            Subtitle = string.Empty;
        }

        public Port FindOutput(string key)
        {
            for (int i = 0; i < Outputs.Count; i++)
            {
                if (string.Equals(Outputs[i].Key, key, StringComparison.Ordinal)) return Outputs[i];
            }
            return null;
        }

        public Port FindInput(string key)
        {
            for (int i = 0; i < Inputs.Count; i++)
            {
                if (string.Equals(Inputs[i].Key, key, StringComparison.Ordinal)) return Inputs[i];
            }
            return null;
        }

        public override string ToString()
        {
            return DisplayName + " [" + SubtypeId + "]";
        }
    }

    /// <summary>
    /// A directed connection between an output port of one node and an input port of
    /// another.
    ///
    /// Connections are stored by port *key*, never by port index: adding a port to a block
    /// type must not silently rewire every existing graph.
    ///
    /// Immutable: a different connection is a different edge, so the graph can hold it in a
    /// list and compare by identity without worrying about a field changing underneath.
    /// </summary>
    public sealed class Edge
    {
        public readonly long SourceEntityId;
        public readonly string SourcePortKey;
        public readonly long TargetEntityId;
        public readonly string TargetPortKey;

        public Edge(long sourceEntityId, string sourcePortKey, long targetEntityId, string targetPortKey)
        {
            SourceEntityId = sourceEntityId;
            SourcePortKey = sourcePortKey;
            TargetEntityId = targetEntityId;
            TargetPortKey = targetPortKey;
        }

        public bool SameAs(Edge other)
        {
            return other != null
                && SourceEntityId == other.SourceEntityId
                && TargetEntityId == other.TargetEntityId
                && string.Equals(SourcePortKey, other.SourcePortKey, StringComparison.Ordinal)
                && string.Equals(TargetPortKey, other.TargetPortKey, StringComparison.Ordinal);
        }

        public override string ToString()
        {
            return SourceEntityId + "." + SourcePortKey + " -> " + TargetEntityId + "." + TargetPortKey;
        }
    }
}
