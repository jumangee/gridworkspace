namespace Vgs.Model
{
    /// <summary>
    /// What kind of block a node is, as far as the editor's own knowledge goes.
    ///
    /// <b>Why an enum and not a string.</b> The editor used to classify a block by a family key
    /// returned as a string ("cockpit", "timer", …) and matched it against a dictionary. A
    /// mistyped key was a silent <c>Unknown</c>, and a category that existed in the classifier
    /// but not in the table was a silent gap. With an enum the compiler asks the question for
    /// us: a new member that no table row covers is visible in the offline gate, and a mistyped
    /// name does not compile at all.
    ///
    /// <b>Order matters when reading this list.</b> The members are declared in the order
    /// <see cref="BlockPortCatalog.ResolveCategory"/> tests them, which is from the most specific
    /// subtype token to the least. Several subtype names contain more than one token —
    /// "LargeBlockButtonPanel" contains both "buttonpanel" and "panel",
    /// "LargeBlockHydrogenThruster" contains both "hydrogen" and "thrust" — and the order is what
    /// decides. Do not reorder this list to tidy it up.
    ///
    /// <b>What a category is not.</b> It does not decide whether a block can drive others
    /// (that is the game's <c>IMyToolbarProvider</c> + <c>ButtonPanel</c>, ADR-0011) and it does
    /// not decide whether a block is an event controller (that is the game's
    /// <c>IMyEventControllerBlock</c>, ADR-0017). A block from another mod that implements those
    /// interfaces is classified by its subtype and still behaves correctly, which is the whole
    /// point of keeping the two facts separate from the category.
    /// </summary>
    public enum BlockCategory
    {
        /// <summary>
        /// No category matched: the subtype is not one the editor knows. A legal value, not an
        /// error — a modded block lands here and still gets its toolbar-driven ports, because
        /// actions come from the game and events degrade to the generic
        /// <see cref="BlockPortCatalog.GenericEventKey"/>.
        /// </summary>
        Unknown = 0,

        /// <summary>Event controller: owns a list of blocks it observes (ADR-0017).</summary>
        EventController = 1,

        /// <summary>Programmable block.</summary>
        Programmable = 2,

        /// <summary>Button panel — the block whose whole purpose is being pressed.</summary>
        ButtonPanel = 3,

        /// <summary>Landing gear.</summary>
        LandingGear = 4,

        /// <summary>Cargo container, small or large.</summary>
        CargoContainer = 5,

        /// <summary>Solar panel.</summary>
        SolarPanel = 6,

        /// <summary>Jump drive.</summary>
        JumpDrive = 7,

        /// <summary>Safe zone.</summary>
        SafeZone = 8,

        /// <summary>Air vent.</summary>
        AirVent = 9,

        /// <summary>Oxygen tank, generator or farm.</summary>
        Oxygen = 10,

        /// <summary>Hydrogen tank, engine or thruster — tested before <see cref="Thruster"/>.</summary>
        Hydrogen = 11,

        /// <summary>Cockpit or seat.</summary>
        Cockpit = 12,

        /// <summary>Timer block.</summary>
        Timer = 13,

        /// <summary>Sensor.</summary>
        Sensor = 14,

        /// <summary>Battery.</summary>
        Battery = 15,

        /// <summary>Thruster.</summary>
        Thruster = 16,

        /// <summary>Gyroscope.</summary>
        Gyro = 17,

        /// <summary>Connector.</summary>
        Connector = 18,

        /// <summary>Warhead.</summary>
        Warhead = 19,

        /// <summary>Door.</summary>
        Door = 20,

        /// <summary>Piston.</summary>
        Piston = 21,

        /// <summary>Rotor.</summary>
        Rotor = 22,

        /// <summary>Hinge.</summary>
        Hinge = 23,

        /// <summary>Reactor.</summary>
        Reactor = 24,

        /// <summary>Turret, gatling or missile launcher.</summary>
        Turret = 25,

        /// <summary>Drill.</summary>
        Drill = 26,

        /// <summary>Welder.</summary>
        Welder = 27,

        /// <summary>Grinder.</summary>
        Grinder = 28,

        /// <summary>Assembler.</summary>
        Assembler = 29,

        /// <summary>Refinery.</summary>
        Refinery = 30,

        /// <summary>Projector.</summary>
        Projector = 31,

        /// <summary>Antenna.</summary>
        Antenna = 32,

        /// <summary>Beacon.</summary>
        Beacon = 33,

        /// <summary>
        /// Light, LCD or generic panel. Tested last on purpose: "buttonpanel" and "solarpanel"
        /// both contain "panel", and they are their own categories.
        /// </summary>
        Light = 34
    }

    /// <summary>
    /// What the editor knows about one <see cref="BlockCategory"/> — the single place where
    /// per-category knowledge lives.
    ///
    /// <b>Three questions, one table.</b> A category answers what fires its toolbar
    /// (<see cref="Events"/>), what state readings it publishes (<see cref="MetricKeys"/>) and
    /// which shape draws those readings (<see cref="Widget"/>).
    /// They used to be two different mechanisms — a dictionary of event names and nothing at all
    /// for state — and the second one is where the specificity (charge, fill level) landed. Keeping
    /// them in one row means a new category is one line, not three tables that drift apart.
    ///
    /// <b>Immutable.</b> A descriptor is shared by every node of its category and never changes
    /// after start-up, so it can be handed out by reference without copying.
    /// </summary>
    public sealed class CategoryDescriptor
    {
        /// <summary>
        /// Events that make this block's own toolbar fire, in the order the ports are laid out.
        ///
        /// Empty means "this category has no curated event": the block still gets the generic
        /// <see cref="BlockPortCatalog.GenericEventKey"/>, because a toolbar-driven block with no
        /// entry has to stay usable (ADR-0002). Only events that actually fire the toolbar belong
        /// here — a battery announcing a charge change to C# code cannot put anything on its own
        /// toolbar and is not a source in this editor.
        /// </summary>
        public readonly string[] Events;

        /// <summary>
        /// Keys of the category's own state readings — charge, fill level and so on — as the
        /// positions of <c>BlockState.Metrics</c>.
        ///
        /// <b>The order is the contract</b>: index <c>i</c> of the array is slot <c>i</c> of the
        /// state, and it must never be reordered once a build has shipped, for the same reason port
        /// keys are never indices (ADR-0002).
        ///
        /// <b>Populated for the five categories that carry a state widget</b> (ADR-0042): a cargo
        /// container publishes <c>Fill</c>, a battery <c>Charge</c>, a timer <c>Elapsed</c>, and a
        /// refinery or an assembler <c>InputFill</c> and <c>OutputFill</c>. The number of keys must
        /// equal <see cref="StateWidget.MetricCount"/> of the row's <see cref="Widget"/>; the
        /// offline gate asserts it, because a mismatch would give the reader a slot that does not
        /// exist or a shape that reads a number nobody writes.
        ///
        /// Every key names a <b>fraction 0..1</b>. That is what lets one bar draw any of them, and
        /// it is clamped at <c>BlockState.SetMetric</c> so a reader may pass what the game reported.
        /// </summary>
        public readonly string[] MetricKeys;

        /// <summary>
        /// The shape this category's <b>state widget</b> draws in the lower band of the card's
        /// state column, or <see cref="StateWidgetKind.None"/> (ADR-0042).
        ///
        /// <b>Data about the category, in the descriptor</b> — the rule ADR-0030 set and ADR-0031
        /// restated: a shape that every block of a category shows identically is a table row, not a
        /// type and not a field on the node. The node copies it once, when it is built, because a
        /// subtype never changes.
        ///
        /// <b>It says what to draw, not whether to draw it.</b> Whether the widget is visible right
        /// now is a fact about the block — switched off, not read yet, nothing to show — and lives
        /// in <c>BlockState</c>. A category that publishes a shape still shows nothing on a block
        /// that is switched off (ADR-0042 §5).
        /// </summary>
        public readonly StateWidgetKind Widget;

        public CategoryDescriptor(string[] events, string[] metricKeys)
            : this(events, metricKeys, StateWidgetKind.None)
        {
        }

        public CategoryDescriptor(string[] events, string[] metricKeys, StateWidgetKind widget)
        {
            Events = events ?? NoStrings;
            MetricKeys = metricKeys ?? NoStrings;
            Widget = widget;
        }

        private static readonly string[] NoStrings = new string[0];

        /// <summary>How many state readings this category publishes; 0 when it publishes none.</summary>
        public int MetricCount
        {
            get { return MetricKeys.Length; }
        }

        /// <summary>True when the category has at least one curated event of its own.</summary>
        public bool HasEvents
        {
            get { return Events.Length > 0; }
        }
    }
}
