using System;
using System.Collections.Generic;
using VRageMath;

namespace Vgs.Model
{
    /// <summary>
    /// A block shown on the canvas — or a named block group of the grid, which is drawn as a
    /// light frame around the cards of its members (ADR-0015).
    ///
    /// The two kinds share this type because almost everything the editor does with a node
    /// (draw it, hit-test it, hang port bars under it, end an edge on its input port, offer
    /// its actions in the menu) is the same for both. What differs is stated in one place
    /// each: <see cref="Kind"/>, <see cref="GroupName"/>, <see cref="Members"/>,
    /// <see cref="FrameSize"/>, and the fact that a group has no toolbar and no outputs.
    ///
    /// This is the *view model* of a grid block, not the block itself. Keeping it separate
    /// means the canvas never has to touch game entities while drawing, which is the single
    /// biggest performance trap in an in-game editor: resolving
    /// <c>IMyTerminalBlock</c> members per frame is far more expensive than reading a
    /// cached struct, and the block may be destroyed mid-frame.
    ///
    /// <see cref="EntityId"/> is the link back to the live block; it can become stale and is
    /// re-validated on refresh. A group has no entity at all — see <see cref="EntityId"/>.
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
        /// <summary>
        /// Entity id of the block, or zero for a group. A group is not an entity and has no
        /// id; its identity is <see cref="GroupName"/>. Never use this as a key for a group:
        /// <see cref="GridGraph"/> keeps groups in a map of their own, keyed by name.
        /// </summary>
        public readonly long EntityId;

        /// <summary>Which of the two things this node is. See <see cref="NodeKind"/>.</summary>
        public readonly NodeKind Kind;

        /// <summary>
        /// Name of the block group in the grid's terminal system, or null for a block.
        ///
        /// This is the identity of a group node: the game addresses a group by name (its
        /// toolbar item stores the name, not an id), so a renamed group is a different group
        /// and its connections stop resolving. That is the game's behaviour, not ours.
        /// </summary>
        public readonly string GroupName;

        /// <summary>Block definition subtype, e.g. "LargeBlockCockpit". Empty for a group.</summary>
        public readonly string SubtypeId;

        /// <summary>Grid coordinates of the block, used to seed a readable initial layout.</summary>
        public readonly Vector3I GridPosition;

        /// <summary>
        /// Entity id of the grid the block stands on, or 0 for a group and for a block whose
        /// grid could not be read (ADR-0032).
        ///
        /// <b>Why the node carries it at all.</b> The graph covers a whole construct — the grid
        /// the editor was opened on plus its subgrids — so "which ship part is this" is no longer
        /// the same question as "which graph is this in". Without this field the picture cannot
        /// say that two cards belong to one subgrid, and the grid frames of
        /// <c>PortGeometry</c> have nothing to group by.
        ///
        /// <b>Why it is not readonly.</b> A block usually keeps its grid for life, but a grid
        /// split hands part of a ship to a new grid with a new entity id, and the block then
        /// reports the new one. A readonly field would keep the old id and the card would be
        /// framed with the wrong ship part until the editor was reopened. Refreshed by
        /// <c>GridGraphBuilder.UpdateNode</c>, which is where every other world-derived value on
        /// a surviving node is refreshed.
        ///
        /// <b>Why 0 is not an error.</b> A group has no grid of its own — its members may sit on
        /// several — and a block whose <c>CubeGrid</c> read threw keeps its previous value rather
        /// than being dropped: "could not read" is never "does not exist" (the same rule the
        /// group sync follows). A card with 0 is simply drawn without a grid frame.
        /// </summary>
        public long GridEntityId; // review:allow R009 written by GridGraphBuilder.UpdateNode on refresh

        /// <summary>Player-visible block name (CustomName), or the group's name. Refreshed when the name changes.</summary>
        public string DisplayName; // review:allow R009 written only by the refresh pass

        /// <summary>
        /// The block's <b>type</b> as the player sees it — the localized definition name, e.g.
        /// "Timer Block" or "Таймер" (ADR-0023). Empty for a group.
        ///
        /// It exists for one reason: the terminal's block search looks in the block's name
        /// <em>and in its type</em> (`MyTerminalControlPanel.blockSearch_TextChanged`, verified
        /// by IL — see <c>docs/01-research/api-terminal-search.md</c> §1), and the editor's search
        /// has to match it. <see cref="SubtypeId"/> is not a substitute: it is the engine id
        /// ("LargeBlockTimer"), which a player never types and which does not exist in the
        /// player's language at all. Refreshed with the name.
        /// </summary>
        public string TypeDisplayName; // review:allow R009 written only by the refresh pass

        /// <summary>
        /// Entity ids of the members of a group that are drawn on the canvas, in the order the
        /// game returned them. Empty for a block.
        ///
        /// Only members present in the graph are listed, because the frame is drawn around
        /// them; the group's *action list*, on the other hand, is computed from the whole
        /// group, including members on subgrids and members hidden with "Show in VGS"
        /// (ADR-0015). A group with no listed member is not in the graph at all.
        /// </summary>
        public readonly List<long> Members = new List<long>();

        private Vector2 _position;
        private bool _placed;
        private Vector2 _frameSize;

        /// <summary>
        /// Position on the canvas in graph units (not pixels, not normalized).
        ///
        /// For a block it is the card's top-left corner. For a group it is the top-left corner
        /// of the group's frame, which <c>PortGeometry</c> derives from the members' cards and
        /// is therefore never dragged: <see cref="GridGraph"/> owns it through
        /// <see cref="SetFrame"/>.
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
        /// Size of a group's frame in graph units; zero for a block, whose body is the card
        /// (<c>VgsConstants.NodeWidth</c> × <c>VgsConstants.NodeHeight</c>).
        ///
        /// The frame is derived state, recomputed from the members' cards and port stacks
        /// whenever a card moves, so it is not persisted and not editable. Written only by
        /// <see cref="SetFrame"/>.
        /// </summary>
        public Vector2 FrameSize
        {
            get { return _frameSize; }
        }

        /// <summary>True for a group node. Groups have no entity, no toolbar and no outputs.</summary>
        public bool IsGroup
        {
            get { return Kind == NodeKind.Group; }
        }

        /// <summary>
        /// Sets the body rectangle of a group: where the frame starts and how big it is.
        ///
        /// Unlike <see cref="Position"/> this does not touch the placed flag, because a group
        /// is never "placed" by the player — its frame follows its members, and marking it
        /// placed would suggest a layout decision that does not exist.
        /// </summary>
        public void SetFrame(Vector2 topLeft, Vector2 size)
        {
            _position = topLeft;
            _frameSize = size;
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

        /// <summary>
        /// True when this block has an on/off switch of its own, i.e. when the game reports it as
        /// a functional block (<c>Sandbox.ModAPI.IMyFunctionalBlock</c>, ADR-0035).
        ///
        /// <b>It is a fact about the type, not about the state.</b> A conveyor tube has no switch
        /// and never will; a timer has one for its whole life. That is why the value is read once,
        /// when the node is built, and why the card's state column is sized from it: whether the
        /// column is reserved at all must not flicker with the block's power, or the block's name
        /// would jump sideways every time somebody flipped the switch.
        ///
        /// <b>It is not the same question as "is it switched on".</b> The reading lives in
        /// <see cref="BlockState.Power"/> and is refreshed by the state pass; this field only says
        /// that the question can be asked. A block that is too damaged to switch on still has a
        /// switch — it is drawn in the red "unavailable" colour, which is the whole point of
        /// ADR-0035: the player learns *why* the block is dark without opening the terminal.
        ///
        /// Written once, by <c>Vgs.Game.GridGraphBuilder.CreateNode</c> through
        /// <c>Vgs.Game.BlockSwitch.CanBeEnabled</c>; read by the geometry and the draw path for
        /// every visible card on every frame, which is why it is a cached flag.
        /// </summary>
        public bool CanBeEnabled; // review:allow R009 set once when the node is built, never re-read per frame

        /// <summary>
        /// What the block is doing right now — damage, and later the category's own readings
        /// (<see cref="BlockState"/>).
        ///
        /// <b>Always present, never null.</b> The draw path reads it for every visible card on
        /// every frame, and a null check there would be one branch per card per frame forever in
        /// exchange for saving a small object per node once. A group gets one too: it has no
        /// integrity of its own and never reads it, but "every node has a state" is a rule the
        /// renderer can rely on without asking what kind of node it is holding.
        ///
        /// <b>Written only by the state pass</b> (<c>GridGraphBuilder.RefreshState</c> through
        /// <see cref="GridGraph.ApplyIntegrity"/>), which is the only code allowed to read the
        /// world on its behalf. The object is reused rather than replaced, so a reference taken
        /// during drawing stays valid across passes.
        /// </summary>
        public readonly BlockState State;

        /// <summary>
        /// What kind of block this is, as far as the editor's curated table knows (ADR-0030).
        ///
        /// Derived once, in the constructor, from <see cref="SubtypeId"/>: a block's subtype never
        /// changes during its life, so neither does its category. A group gets
        /// <see cref="BlockCategory.Unknown"/>, because a group is not a block and has no subtype.
        ///
        /// Read by <c>BlockPortCatalog.FillEvents</c> (which events the toolbar fires) and by the
        /// state pass (how many metric slots the node's <see cref="State"/> needs). It is
        /// deliberately **not** what decides whether a block can drive others or whether it is an
        /// event controller — those are facts from the game (ADR-0011, ADR-0017), and deriving
        /// them from a subtype would break every block from another mod.
        /// </summary>
        public readonly BlockCategory Category;

        /// <summary>
        /// Everything specific to an event controller — the mode description, the watch list and
        /// the identity of the event those watches belong to — or <b>null for every other block</b>
        /// (ADR-0030).
        ///
        /// <b>This reference is the flag.</b> <see cref="IsEventController"/> is a property over
        /// it, so there is one truth and not two: a node either has controller state or is not a
        /// controller, and the two cannot disagree. The check costs the same as reading a bool.
        ///
        /// The specific used to live on the node itself, which made every conveyor tube pay for an
        /// empty watch list and made the geometry ask every card whether it carried a status line
        /// to be told "no". A new kind of specificity gets its own type next to this one, and the
        /// common node does not grow.
        /// </summary>
        public readonly ControllerState Controller;

        /// <summary>
        /// True when this node's block is an event controller — the one block that owns a list
        /// of blocks it *observes* (ADR-0017).
        ///
        /// Read from the game's own interface (<c>Sandbox.ModAPI.IMyEventControllerBlock</c>)
        /// when the node is built, never from the subtype name: a modded block that implements
        /// the interface is a controller too, and a block that merely *looks* like one is not.
        ///
        /// It gates the watch button and the watch mode, nothing else: a controller is still an
        /// ordinary source of connections and keeps its bind buttons (ADR-0020).
        /// </summary>
        public bool IsEventController
        {
            get { return Controller != null; }
        }

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
            : this(entityId, subtypeId, displayName, gridPosition, toolbar, false)
        {
        }

        public Node(long entityId, string subtypeId, string displayName, Vector3I gridPosition,
                    ToolbarLayout toolbar, bool isEventController)
            : this(entityId, subtypeId, displayName, gridPosition, toolbar, isEventController, 0)
        {
        }

        /// <summary>
        /// The full form, with the grid the block stands on (ADR-0032). The overloads above pass
        /// 0, which is what a group and an unreadable grid both mean: "no grid frame".
        /// </summary>
        public Node(long entityId, string subtypeId, string displayName, Vector3I gridPosition,
                    ToolbarLayout toolbar, bool isEventController, long gridEntityId)
        {
            EntityId = entityId;
            Kind = NodeKind.Block;
            GroupName = null;
            SubtypeId = subtypeId;
            DisplayName = displayName;
            TypeDisplayName = null;
            GridPosition = gridPosition;
            GridEntityId = gridEntityId;
            Toolbar = toolbar ?? ToolbarLayout.None;

            // Classified once: a block's subtype never changes, so neither does its category.
            Category = BlockPortCatalog.ResolveCategory(subtypeId);

            // The controller state is created only for a controller, and the reference *is* the
            // flag: there is no second field that could disagree with it (ADR-0030).
            Controller = isEventController ? new ControllerState() : null;

            // Sized by the category's descriptor, so a category that publishes readings gets a
            // state with room for them without any new wiring (ADR-0030). Today every descriptor
            // publishes none, and the shared empty array is what everyone gets.
            State = new BlockState(BlockPortCatalog.Descriptor(Category).MetricCount);

            // Deliberately not `Position = Vector2.Zero`: the setter would mark the node as
            // placed, and the automatic layout would then never place it. A new node starts
            // unplaced at (0, 0) until something actually decides where it goes.
        }

        /// <summary>
        /// A node standing for a named block group. It has no entity id, no toolbar and no
        /// output ports; its <see cref="Members"/> and its input ports are filled by the graph
        /// as it is refreshed.
        /// </summary>
        public Node(string groupName, string displayName)
        {
            EntityId = 0;
            Kind = NodeKind.Group;
            GroupName = groupName;
            SubtypeId = string.Empty;
            DisplayName = displayName;
            TypeDisplayName = null;
            GridPosition = Vector3I.Zero;

            // A group is not on a grid: its members may sit on several subgrids at once
            // (ADR-0015), so there is no single grid to name. 0 means "no grid frame" (ADR-0032).
            GridEntityId = 0;

            Toolbar = ToolbarLayout.None;

            // A group has nothing that fires, so it cannot drive anything: no "+" button, no
            // bind mode, no event ports. Filling this in explicitly is cheaper than defending
            // every caller against the opposite assumption. It is also never an event
            // controller: the watch list belongs to one block, and a group is not a block.
            CanDriveBlocks = false;

            // A group has no block, so it has no switch: the card's state column does not exist on
            // a group's frame, and nothing can be clicked there (ADR-0035).
            CanBeEnabled = false;

            // A group is not a block: it has no subtype to classify, and no controller state. The
            // null reference is what makes IsEventController false here, without a second flag.
            Category = BlockCategory.Unknown;
            Controller = null;

            // A group has no block and therefore no state of its own; it carries the default one
            // only so that every node answers the same question the same way.
            State = new BlockState();
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

}
