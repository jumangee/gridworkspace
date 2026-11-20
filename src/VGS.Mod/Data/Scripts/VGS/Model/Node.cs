using System;
using System.Collections.Generic;
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

    /// <summary>
    /// Direction of data flow for a port.
    ///
    /// This is the side of the node the port lives on: an <see cref="Input"/> is drawn as a
    /// bar under the card with its dot on the left, an <see cref="Output"/> as a bar with its
    /// dot on the right. Data always flows output → input, so a line always starts at a dot
    /// on the right of a source bar and ends at a dot on the left of a target bar.
    /// </summary>
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
    /// <see cref="Anchor"/> and <see cref="Slot"/> are two different things on purpose:
    ///
    ///  * <see cref="Anchor"/> is a *derived* position on the card edge, kept only so the
    ///    catalog can spread a long port list down the side of the card. It is never used to
    ///    place a connection dot — dots live on the port bars, not on the card.
    ///  * <see cref="Slot"/> is the port's *drawing slot*: which stacked bar under the card it
    ///    occupies. It is assigned by <see cref="GridGraph.LayoutPorts"/>, not by the catalog,
    ///    because it depends on what the user has actually connected. Two nodes can therefore
    ///    place the same port in different slots, and a port that was not drawn yesterday may
    ///    be drawn today.
    ///
    /// Everything except <see cref="Slot"/> is fixed for the lifetime of the block; the slot
    /// is rewritten only by the layout pass, and only as a whole (see the pass for why a
    /// half-updated slot map would draw two ports on top of each other).
    /// </summary>
    public sealed class Port
    {
        public readonly string Key;
        public readonly string DisplayName;
        public readonly PortKind Kind;
        public readonly PortDirection Direction;

        /// <summary>Normalized vertical position on the node edge (0 = top, 1 = bottom).</summary>
        public readonly float Anchor;

        /// <summary>
        /// Drawing slot: index of the stacked port bar this port occupies under its card,
        /// counted from the card outwards. Negative means "not drawn" (no connection, so the
        /// bar would be noise). Assigned by <see cref="GridGraph.LayoutPorts"/>.
        /// </summary>
        public int Slot; // review:allow R009 assigned only by GridGraph.LayoutPorts, for the whole node at once

        public Port(string key, string displayName, PortKind kind, PortDirection direction, float anchor)
        {
            Key = key;
            DisplayName = displayName;
            Kind = kind;
            Direction = direction;
            Anchor = anchor;
            Slot = -1;
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
    /// Two port lists, and they are not symmetric:
    ///
    ///  * <see cref="Actions"/> — what the block can be told to do. Read from the game, so it
    ///    is exact for every block including other mods' blocks.
    ///  * <see cref="Events"/> — what the block can announce. Curated table, because the API
    ///    does not enumerate events (ADR-0002).
    ///
    /// The card itself shows only the name and the subtype: ports are drawn as stacked bars
    /// under the card and exist only while something is connected to them
    /// (<see cref="GridGraph.LayoutPorts"/>). A node with 40 actions therefore looks exactly
    /// like a node with 4 until the user connects something.
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

        /// <summary>Position on the canvas in graph units (not pixels, not normalized).</summary>
        public Vector2 Position; // review:allow R009 rewritten by the drag handler every pointer move

        /// <summary>False when the block type exposes no terminal actions at all.</summary>
        public bool HasTerminal; // review:allow R009 rebuilt only when the action set changes

        /// <summary>
        /// True when this block can be the source of a connection: its type contributes at
        /// least one event of its own, so there is something to pick when a link starts here.
        ///
        /// This is the test behind the round "+" button, and it is the project's answer to
        /// "a block that can drive other blocks". It is deliberately not
        /// <c>Events.Count &gt; 0</c>: every block gets the shared events (ADR-0002), so that
        /// test would put a button on every card in the ship. A cockpit, a timer, a sensor, a
        /// battery or an event controller qualifies; a conveyor tube does not.
        ///
        /// It is also deliberately not the game's <c>ShowInToolbarConfig</c> flag: that flag
        /// says whether the block's *actions* are offered in the toolbar configuration, i.e.
        /// whether the block can be *driven*, which is the opposite relation. See ADR-0010.
        ///
        /// Written once, by <see cref="BlockPortCatalog.FillEvents"/>, together with the list
        /// it summarises; read by the draw path for every visible card on every frame, which
        /// is why it is a cached flag rather than a scan of the list.
        /// </summary>
        public bool CanSourceEvents; // review:allow R009 set once by BlockPortCatalog.FillEvents

        /// <summary>Input ports: terminal actions this block can be told to perform.</summary>
        public readonly List<Port> Actions = new List<Port>();

        /// <summary>Output ports: events this block can announce.</summary>
        public readonly List<Port> Events = new List<Port>();

        public Node(long entityId, string subtypeId, string displayName, Vector3I gridPosition)
        {
            EntityId = entityId;
            SubtypeId = subtypeId;
            DisplayName = displayName;
            GridPosition = gridPosition;
            Position = Vector2.Zero;
        }

        /// <summary>
        /// The event a bind started from this block connects from.
        ///
        /// Bind mode is started from the card, not from a port, so the port has to be chosen
        /// for the user. The rule is "the block's own behaviour first, the shared fallback
        /// last": the first family-specific event if there is one (a sensor's
        /// <c>DetectedChanged</c>, a timer's <c>Triggered</c>), otherwise the generic
        /// <c>StateChanged</c>. It is deterministic, which matters more than it being clever:
        /// the same click always produces the same port, so the picture is reproducible.
        ///
        /// Returns null only when the block exposes no events at all, in which case bind mode
        /// is not offered (<see cref="CanSourceEvents"/>).
        /// </summary>
        public Port PrimaryEvent
        {
            get
            {
                for (int i = 0; i < Events.Count; i++)
                {
                    if (!BlockPortCatalog.IsCommonEvent(Events[i].Key)) return Events[i];
                }
                return Events.Count > 0 ? Events[0] : null;
            }
        }

        /// <summary>Event port with this key, or null.</summary>
        public Port FindEvent(string key)
        {
            for (int i = 0; i < Events.Count; i++)
            {
                if (string.Equals(Events[i].Key, key, StringComparison.Ordinal)) return Events[i];
            }
            return null;
        }

        /// <summary>Action port with this key, or null.</summary>
        public Port FindAction(string key)
        {
            for (int i = 0; i < Actions.Count; i++)
            {
                if (string.Equals(Actions[i].Key, key, StringComparison.Ordinal)) return Actions[i];
            }
            return null;
        }

        public override string ToString()
        {
            return DisplayName + " [" + SubtypeId + "]";
        }
    }

    /// <summary>
    /// A directed connection between an event port of one node and an action port of another.
    ///
    /// Connections are stored by port *key*, never by port index: adding a port to a block
    /// type must not silently rewire every existing graph.
    ///
    /// Many edges may end on the *same* target port. That is the point of the "BlockOn"
    /// action: several blocks can all be told to switch the same reactor on, and the UI shows
    /// it as one input bar with several lines arriving at its dot. The inverse is not true —
    /// one event port is one source, and the graph keeps it unique so the evaluator never has
    /// to decide which of two contradictory signals won.
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
