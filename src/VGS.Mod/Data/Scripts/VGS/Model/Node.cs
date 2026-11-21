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
    /// Which fire group of the source block's toolbar an outgoing port writes into.
    ///
    /// Space Engineers toolbars are not all "everything fires at once". Two shapes exist,
    /// and the game decides which one a block has (see <see cref="ToolbarLayout.YesNo"/>):
    ///
    ///  * a <b>single-group</b> toolbar (a timer: nine slots × ten pages) — every filled cell
    ///    is activated together when the block fires;
    ///  * a <b>yes/no</b> toolbar (a sensor, an event controller, a cockpit's "on lock"
    ///    toolbar: two slots × ten pages) — slot 0 of every page fires on "yes", slot 1 on
    ///    "no". The two groups are independent lists of actions.
    ///
    /// A yes/no block therefore exposes *two* ports per event: "Detected (yes)" and
    /// "Detected (no)". <see cref="Single"/> is the only group of a single-group toolbar and
    /// is never used on a yes/no one.
    /// </summary>
    public enum PortGroup
    {
        /// <summary>The block's toolbar has one fire group; every cell fires together.</summary>
        Single = 0,

        /// <summary>Slot 0 of every page: the "yes" / "true" / "detected" list.</summary>
        Yes = 1,

        /// <summary>Slot 1 of every page: the "no" / "false" / "not detected" list.</summary>
        No = 2
    }

    /// <summary>
    /// The shape of the toolbar a block drives other blocks with, read from the live block
    /// through the game's own interfaces (<c>VRage.Game.ModAPI.IMyToolbarProvider</c> →
    /// <c>IMyToolbar</c>). The engine types that do the same job
    /// (<c>Sandbox.Game.Screens.Helpers.IMyToolbarOwner</c> / <c>MyToolbar</c>) are prohibited
    /// to a mod script, so this layer only ever sees the ModAPI mirror.
    ///
    /// Nothing here is a table of block types: a timer reports nine slots and ten pages, a
    /// sensor reports two slots and ten pages, and the editor draws whatever it is told. A
    /// block that reports no usable toolbar simply cannot be a source, which is exactly the
    /// answer to "can this block drive other blocks".
    ///
    /// <b>Yes/no toolbars.</b> Some blocks keep two independent lists of actions — what happens
    /// on "yes" and what happens on "no" — and the editor has to see them as two groups or it
    /// writes into the wrong list. The game's own test for that is three conditions:
    /// the definition declares the background (<c>MyCubeBlockDefinition.YesNoToolbarBackground</c>,
    /// which <c>MyToolbar.Init</c> copies into <c>MyToolbar.YesNoBackground</c>), the toolbar
    /// allows it (<c>CanShowYesNoBackgrounds</c>, true for every <c>ButtonPanel</c> toolbar),
    /// and there are exactly two slots per page. The same test is what the vanilla toolbar
    /// screen makes before it draws the true/false icons.
    ///
    /// <see cref="YesNo"/> is therefore not derived from the subtype name, and not from a table:
    /// the same cockpit owns a manual toolbar (nine slots, no yes/no) and an "on lock" toolbar
    /// (two slots, yes/no), and both are described by this same type.
    /// </summary>
    public sealed class ToolbarLayout
    {
        /// <summary>Index of the toolbar this layout describes, as <c>IMyToolbarProvider</c> numbers it.</summary>
        public readonly int ToolbarIndex;

        /// <summary>Cells per page. Two for a yes/no toolbar, more for a single-group one.</summary>
        public readonly int SlotCount;

        /// <summary>Pages (the game calls them "toolbars" in the terminal UI).</summary>
        public readonly int PageCount;

        /// <summary>True when slot 0 fires on "yes" and slot 1 on "no", independently.</summary>
        public readonly bool YesNo;

        /// <summary>A block with no toolbar at all: no cells, no source ports, no "+".</summary>
        public static readonly ToolbarLayout None = new ToolbarLayout(-1, 0, 0, false);

        public ToolbarLayout(int toolbarIndex, int slotCount, int pageCount, bool yesNo)
        {
            ToolbarIndex = toolbarIndex;
            SlotCount = slotCount < 0 ? 0 : slotCount;
            PageCount = pageCount < 0 ? 0 : pageCount;
            YesNo = yesNo;
        }

        /// <summary>Total cells in this toolbar, i.e. <c>MyToolbar.ItemCount</c>.</summary>
        public int CellCount { get { return SlotCount * PageCount; } }

        /// <summary>True when the block can drive other blocks through this toolbar.</summary>
        public bool CanDrive { get { return SlotCount > 0 && PageCount > 0; } }

        /// <summary>
        /// Slot a port group writes into: 0 for <see cref="PortGroup.Yes"/>, 1 for
        /// <see cref="PortGroup.No"/>, and -1 for <see cref="PortGroup.Single"/>, meaning
        /// "any free slot of a free page".
        /// </summary>
        public int SlotFor(PortGroup group)
        {
            if (group == PortGroup.Yes) return 0;
            if (group == PortGroup.No) return 1;
            return -1;
        }

        /// <summary>Linear cell index of a (page, slot) pair, in the game's own order.</summary>
        public int CellIndex(int page, int slot)
        {
            return page * SlotCount + slot;
        }

        /// <summary>Page a linear cell index belongs to.</summary>
        public int PageOf(int cellIndex)
        {
            return SlotCount <= 0 ? 0 : cellIndex / SlotCount;
        }

        /// <summary>Slot a linear cell index occupies within its page.</summary>
        public int SlotOf(int cellIndex)
        {
            return SlotCount <= 0 ? 0 : cellIndex % SlotCount;
        }

        public override string ToString()
        {
            return "toolbar " + ToolbarIndex + ": " + SlotCount + "x" + PageCount
                   + (YesNo ? " (yes/no)" : string.Empty);
        }
    }

    /// <summary>
    /// One cell of a block's toolbar: a page and an index inside it.
    ///
    /// A connection is stored with the cell it occupies, because the toolbar *is* the
    /// connection: the editor does not keep a private wiring that the block does not have.
    /// Two connections that differ only by their cell are different connections — the game
    /// refuses the same action twice on one page and accepts it on another page.
    /// </summary>
    public struct ToolbarCell
    {
        public readonly int ToolbarIndex;
        public readonly int Index;

        /// <summary>The "no cell" value, used by edges that were never written.</summary>
        public static readonly ToolbarCell None = new ToolbarCell(-1, -1);

        public ToolbarCell(int toolbarIndex, int index)
        {
            ToolbarIndex = toolbarIndex;
            Index = index;
        }

        public bool IsValid { get { return ToolbarIndex >= 0 && Index >= 0; } }

        public bool SameAs(ToolbarCell other)
        {
            return ToolbarIndex == other.ToolbarIndex && Index == other.Index;
        }

        public override string ToString()
        {
            return "cell " + ToolbarIndex + "/" + Index;
        }
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

        /// <summary>
        /// Which fire group of the source's toolbar this port belongs to. Always
        /// <see cref="PortGroup.Single"/> for an input port: actions have no groups.
        /// </summary>
        public readonly PortGroup Group;

        /// <summary>Normalized vertical position on the node edge (0 = top, 1 = bottom).</summary>
        public readonly float Anchor;

        /// <summary>
        /// Drawing slot: index of the stacked port bar this port occupies under its card,
        /// counted from the card outwards. Negative means "not drawn" (no connection, so the
        /// bar would be noise). Assigned by <see cref="GridGraph.LayoutPorts"/>.
        /// </summary>
        public int Slot; // review:allow R009 assigned only by GridGraph.LayoutPorts, for the whole node at once

        public Port(string key, string displayName, PortKind kind, PortDirection direction, float anchor)
            : this(key, displayName, kind, direction, anchor, PortGroup.Single)
        {
        }

        public Port(string key, string displayName, PortKind kind, PortDirection direction,
                    float anchor, PortGroup group)
        {
            Key = key;
            DisplayName = displayName;
            Kind = kind;
            Direction = direction;
            Anchor = anchor;
            Group = group;
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
    ///    does not enumerate events (ADR-0002), and split by fire group when the block's
    ///    toolbar has two of them (ADR-0011).
    ///
    /// The card itself shows only the name and the subtype: ports are drawn as stacked bars
    /// under the card and exist only while something is connected to them
    /// (<see cref="GridGraph.LayoutPorts"/>). A node with 40 actions therefore looks exactly
    /// like a node with 4 until the user connects something.
    ///
    /// Mutable by design: the refresh pass rewrites the name and terminal flag, and the drag
    /// handler rewrites <see cref="Position"/> on every pointer move. Making it a value type
    /// or copying it on change would allocate per frame for no safety gain — there are no
    /// cross-field invariants to protect, and the one that did exist (position ↔ placed) is
    /// enforced by <see cref="Position"/> itself being a property.
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

        private Vector2 _position;
        private bool _placed;

        /// <summary>
        /// Position on the canvas in graph units (not pixels, not normalized).
        ///
        /// A property rather than a field so that "a position was written" and "the node has
        /// been placed" cannot disagree: every write, from the drag handler or from a saved
        /// layout, marks the node placed. The alternative — a field plus a flag set by hand at
        /// each call site — fails silently the first time a caller forgets, and the symptom is a
        /// card that jumps back to the automatic layout on the next refresh.
        /// </summary>
        public Vector2 Position
        {
            get { return _position; }
            set
            {
                _position = value;
                _placed = true;
            }
        }

        /// <summary>
        /// True once the node has a position of its own: dropped by the player, restored from
        /// the block's saved state, or seeded once by <see cref="GridGraph.LayoutUnplaced"/>.
        ///
        /// <see cref="GridGraph.LayoutUnplaced"/> uses this instead of comparing
        /// <see cref="Position"/> with <c>Vector2.Zero</c>, because <c>(0, 0)</c> is a legitimate
        /// place for a card and a node the player parked there would otherwise be moved on every
        /// refresh (ADR-0012).
        /// </summary>
        public bool Placed
        {
            get { return _placed; }
        }

        /// <summary>False when the block type exposes no terminal actions at all.</summary>
        public bool HasTerminal; // review:allow R009 rebuilt only when the action set changes

        /// <summary>
        /// The toolbar this block drives other blocks with, read from the live block by
        /// <c>Vgs.Game.ToolbarProbe</c>. <see cref="ToolbarLayout.None"/> when the block has
        /// no logic-driven toolbar — and then it cannot be a source of a connection at all.
        ///
        /// Read-only, and set through the constructor, because the game builds a block's
        /// toolbar once in its <c>Init</c> and never resizes it: a node whose toolbar changed
        /// is a different node, not an edited one. The refresh pass therefore never re-probes
        /// it, and the port list built from it stays stable for the block's lifetime.
        /// </summary>
        public readonly ToolbarLayout Toolbar;

        /// <summary>
        /// True when this block can drive other blocks: it owns a toolbar with at least one
        /// cell whose contents the block itself activates.
        ///
        /// This is the test behind the round "+" (or the "T"/"F" pair on a yes/no toolbar),
        /// and it is the project's answer to "a block that can drive other blocks". It is
        /// read from the game's interfaces, not from a table of subtypes: a timer, a sensor,
        /// an event controller, a cockpit's on-lock toolbar and anything else the game gives
        /// a logic-driven toolbar qualify; a conveyor tube does not.
        ///
        /// It is deliberately not the game's <c>ShowInToolbarConfig</c> flag: that flag says
        /// whether the block's *actions* are offered in the toolbar configuration, i.e.
        /// whether the block can be *driven*, which is the opposite relation (ADR-0010).
        ///
        /// Written once, by <c>Vgs.Game.ToolbarProbe</c>, together with <see cref="Toolbar"/>;
        /// read by the draw path for every visible card on every frame, which is why it is a
        /// cached flag rather than a re-probe.
        /// </summary>
        public bool CanDriveBlocks; // review:allow R009 set once by the probe, never re-read per frame

        /// <summary>Input ports: terminal actions this block can be told to perform.</summary>
        public readonly List<Port> Actions = new List<Port>();

        /// <summary>Output ports: events this block can announce, one per fire group.</summary>
        public readonly List<Port> Events = new List<Port>();

        public Node(long entityId, string subtypeId, string displayName, Vector3I gridPosition)
            : this(entityId, subtypeId, displayName, gridPosition, ToolbarLayout.None)
        {
        }

        public Node(long entityId, string subtypeId, string displayName, Vector3I gridPosition,
                    ToolbarLayout toolbar)
        {
            EntityId = entityId;
            SubtypeId = subtypeId;
            DisplayName = displayName;
            GridPosition = gridPosition;
            Toolbar = toolbar ?? ToolbarLayout.None;

            // Deliberately not `Position = Vector2.Zero`: the setter would mark the node as
            // placed, and the automatic layout would then never place it. A new node starts
            // unplaced at (0, 0) until something actually decides where it goes.
        }

        /// <summary>
        /// The event a bind started from this block connects from, for the given group.
        ///
        /// Bind mode is started from the card, not from a port, so the port has to be chosen
        /// for the user. The rule is "the block's own behaviour first, the fallback last":
        /// the first family-specific event of that group (a sensor's
        /// <c>DetectedChanged</c> on "yes", a timer's <c>Triggered</c>), otherwise the generic
        /// one. It is deterministic, which matters more than it being clever: the same click
        /// always produces the same port, so the picture is reproducible.
        ///
        /// Returns null when the block has no port of that group, in which case bind mode is
        /// not offered for it (<see cref="CanDriveBlocks"/>).
        /// </summary>
        public Port PrimaryEventFor(PortGroup group)
        {
            for (int i = 0; i < Events.Count; i++)
            {
                if (Events[i].Group == group) return Events[i];
            }
            return null;
        }

        /// <summary>
        /// The port the plain "+" starts from: the "yes" port on a yes/no toolbar, the only
        /// port otherwise.
        /// </summary>
        public Port PrimaryEvent
        {
            get
            {
                Port p = PrimaryEventFor(PortGroup.Single);
                if (p != null) return p;
                return PrimaryEventFor(PortGroup.Yes);
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
        public readonly long TargetEntityId;
        public readonly string TargetPortKey;

        /// <summary>Cell of the source block's toolbar that holds this connection's item.</summary>
        public readonly ToolbarCell Cell;

        public Edge(long sourceEntityId, string sourcePortKey, long targetEntityId, string targetPortKey)
            : this(sourceEntityId, sourcePortKey, targetEntityId, targetPortKey, ToolbarCell.None)
        {
        }

        public Edge(long sourceEntityId, string sourcePortKey, long targetEntityId, string targetPortKey,
                    ToolbarCell cell)
        {
            SourceEntityId = sourceEntityId;
            SourcePortKey = sourcePortKey;
            TargetEntityId = targetEntityId;
            TargetPortKey = targetPortKey;
            Cell = cell;
        }

        public bool SameAs(Edge other)
        {
            return other != null
                && SourceEntityId == other.SourceEntityId
                && TargetEntityId == other.TargetEntityId
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
            return SourceEntityId + "." + SourcePortKey + " -> " + TargetEntityId + "." + TargetPortKey
                   + " @ " + Cell;
        }
    }
}
