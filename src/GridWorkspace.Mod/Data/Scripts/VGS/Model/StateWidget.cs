namespace Vgs.Model
{
    /// <summary>
    /// What a block's <b>state widget</b> draws, if anything — the reading that lives in the lower
    /// band of the card's state column, under the power switch (ADR-0042).
    ///
    /// <b>A widget is a shape, not an icon.</b> The upper band of the column carries a Font Awesome
    /// icon, because "on / off" is one bit and an icon says it. What the lower band shows is a
    /// *quantity*: how full a container is, how charged a battery is, how far a timer has counted.
    /// A quantity needs a drawn shape whose size is the value, so the widget is geometry — a bar, a
    /// pair of bars, a clock — and its colour is a constant rather than a state (ADR-0042 §2).
    ///
    /// <b>Where the kind comes from.</b> The editor asks the <em>game</em> what a block is —
    /// <c>IMyCargoContainer</c>, <c>IMyBatteryBlock</c>, <c>IMyTimerBlock</c>, <c>IMyRefinery</c>,
    /// <c>IMyAssembler</c> — through <c>Vgs.Game.StateWidgetProbe.KindOf</c>, and passes the answer
    /// into the node's constructor (ADR-0042 §1, §7). A block's type never changes, so neither does
    /// the shape of its reading.
    ///
    /// <b>The category table is the fallback, not the source.</b> The first version derived the kind
    /// from <see cref="CategoryDescriptor.Widget"/>, and the category from a substring of the
    /// subtype that looked for "cargocontainer" — a word no real cargo container has. Every
    /// container fell through to <see cref="BlockCategory.Unknown"/> and drew no widget at all. The
    /// table still answers for a modded block the game's interfaces do not recognise, but it cannot
    /// be the source: ten of the game's twenty container subtypes do not contain "container" either.
    ///
    /// <b>Why not one member per block type.</b> Refinery and assembler show the same pair of bars
    /// and are one member (<see cref="Production"/>); a new block type that reads a fill level is a
    /// branch in <c>KindOf</c> plus, if its shape is new, a member. The members below are shapes, and
    /// a shape is what the geometry layer switches on.
    /// </summary>
    public enum StateWidgetKind
    {
        /// <summary>
        /// Nothing to draw: this block's category publishes no reading. A legal value, not an
        /// error — most categories are here, and so is every group node.
        /// </summary>
        None = 0,

        /// <summary>
        /// One vertical bar filled from the bottom up: a cargo container's occupancy.
        /// </summary>
        FillBar = 1,

        /// <summary>
        /// The same bar, read as a battery's charge, with the battery's charge <em>mode</em>
        /// drawn beside it as a bolt (see <see cref="WidgetAccent"/>).
        /// </summary>
        Battery = 2,

        /// <summary>
        /// Two hands and a face: one hand fixed at twelve o'clock, the other sweeping clockwise
        /// towards it. A timer's countdown, where arriving at twelve is the trigger (ADR-0042 §4).
        /// </summary>
        Clock = 3,

        /// <summary>
        /// Two narrow bars side by side — the input inventory on the leading side, the output one
        /// on the trailing side. A refinery and an assembler both show this.
        /// </summary>
        Production = 4
    }

    /// <summary>
    /// The one accent a widget can carry besides its base colour (ADR-0042 §2).
    ///
    /// <b>An accent is a direction, not a quantity.</b> The base colour of every element of every
    /// widget is one grey, because the shape already says how much; an accent says <em>which
    /// way</em> the value is moving, and only a battery has a direction it can report. Naming the
    /// member after the direction rather than after the block keeps the accent a property of the
    /// widget: a second category that needs one gets a member, not a second field.
    ///
    /// <b>Colour is decided at the drawing site, not here.</b> This type is model data with no
    /// game or palette reference; <c>VgsPalette</c> maps it to yellow and red, and the mapping is
    /// written in one place so the two cannot drift.
    /// </summary>
    public enum WidgetAccent
    {
        /// <summary>No accent: the block reports no direction. This is also the default.</summary>
        None = 0,

        /// <summary>The value is rising — a battery in <c>Recharge</c> mode.</summary>
        Recharge = 1,

        /// <summary>The value is falling — a battery in <c>Discharge</c> mode.</summary>
        Discharge = 2
    }

    /// <summary>
    /// One reading of a block's state widget, as the state pass hands it from the world to the
    /// model (ADR-0042).
    ///
    /// <b>A value type, and immutable.</b> It is built by the reader in <c>Vgs.Game</c> and copied
    /// into <see cref="BlockState"/> in one call; nothing holds onto it, so there is nothing to
    /// mutate and no allocation to avoid. That is why it is a struct with readonly fields rather
    /// than the mutable class the rest of the model uses: those are long-lived and rewritten in
    /// place (ADR-0007), and this one is not.
    ///
    /// <b>Two numbers, and the second is usually unused.</b> A bar reads one fraction; the
    /// production widget reads two (input and output). A clock reads one. Naming them
    /// <c>First</c>/<c>Second</c> rather than <c>Fill</c>/<c>Output</c> keeps the type shape-agnostic:
    /// which index means what is the category descriptor's business
    /// (<see cref="CategoryDescriptor.MetricKeys"/>), exactly as it is for <see cref="BlockState.Metrics"/>.
    /// </summary>
    public struct WidgetReading
    {
        /// <summary>
        /// False when the block reports that there is nothing to show right now — a timer that is
        /// not counting down is the one such case today. The card then draws no widget at all,
        /// which is the honest picture: an empty bar would claim the value is zero.
        /// </summary>
        public readonly bool Active;

        /// <summary>The reading at <see cref="CategoryDescriptor.MetricKeys"/> index 0, as 0..1.</summary>
        public readonly float First;

        /// <summary>The reading at index 1, as 0..1; 0 for every shape that reads one number.</summary>
        public readonly float Second;

        /// <summary>The direction the value is moving, or <see cref="WidgetAccent.None"/>.</summary>
        public readonly WidgetAccent Accent;

        public WidgetReading(bool active, float first, float second, WidgetAccent accent)
        {
            Active = active;
            First = first;
            Second = second;
            Accent = accent;
        }

        /// <summary>
        /// "Nothing to show", which is also what a block that has not been read yet reports: the
        /// two are the same picture on purpose, so a card cannot show a stale reading it does not
        /// have.
        /// </summary>
        public static WidgetReading None
        {
            get { return new WidgetReading(false, 0f, 0f, WidgetAccent.None); }
        }
    }

    /// <summary>
    /// What a <see cref="StateWidgetKind"/> is made of, as questions the geometry and the state
    /// pass both ask: how many bars, how many readings.
    ///
    /// <b>Why a helper and not fields on the enum.</b> C# has no enum members with data, and the
    /// project's rule is that numbers live in <c>VgsConstants</c> while per-category data lives in
    /// the descriptor — but "a battery widget has one bar" is neither: it is a property of the
    /// shape itself, the same for every block that uses it. Keeping it here means a new member is
    /// one case in two switches.
    ///
    /// <b>The compiler does not check that a new member was added to both.</b> Both switches end in
    /// <c>default: return 0</c>, so a member nobody handled compiles silently and draws nothing —
    /// and the count-versus-keys check cannot notice either, because a shape that reads nothing and
    /// a category that publishes nothing agree at 0. What does notice is the offline gate
    /// <c>StateWidgetChecks</c>: it walks every member of the enum, requires <c>None</c> to read
    /// nothing and every other member to read at least one number, and requires every member to be
    /// used by at least one category. A new shape with no row in the descriptor table therefore
    /// fails the gate rather than being drawn as an empty column.
    /// </summary>
    public static class StateWidget
    {
        /// <summary>
        /// How many bars the shape draws. A clock draws none — it has hands — and the production
        /// widget draws two.
        /// </summary>
        public static int BarCount(StateWidgetKind kind)
        {
            switch (kind)
            {
                case StateWidgetKind.None: return 0;
                case StateWidgetKind.Clock: return 0;
                case StateWidgetKind.FillBar: return 1;
                case StateWidgetKind.Battery: return 1;
                case StateWidgetKind.Production: return 2;
                default: return 0;
            }
        }

        /// <summary>
        /// How many readings the shape consumes, which is the number of keys its category's
        /// descriptor must publish. Equal to <see cref="BarCount"/> for every bar shape and 1 for
        /// the clock; 0 for <see cref="StateWidgetKind.None"/>.
        ///
        /// It exists as a separate question because a clock has one reading and no bars, and
        /// deriving the metric count from the bar count would give the timer a state with no room
        /// for its own value.
        /// </summary>
        public static int MetricCount(StateWidgetKind kind)
        {
            switch (kind)
            {
                case StateWidgetKind.None: return 0;
                case StateWidgetKind.Clock: return 1;
                case StateWidgetKind.FillBar: return 1;
                case StateWidgetKind.Battery: return 1;
                case StateWidgetKind.Production: return 2;
                default: return 0;
            }
        }

        /// <summary>
        /// True for the shapes that are drawn as bars, i.e. everything but the clock. Used by the
        /// draw path to choose between the two renderings without repeating the enum test, and by
        /// the offline gate to assert that the two questions agree.
        /// </summary>
        public static bool IsBar(StateWidgetKind kind)
        {
            return BarCount(kind) > 0;
        }

        /// <summary>
        /// True when the card of a node with this widget kind draws the widget <b>right now</b>
        /// (ADR-0042 §5).
        ///
        /// <b>This is the whole visibility rule, in one place, so the gate can check it.</b> The
        /// rule was born in the draw path, where no offline check can reach it — and it is exactly
        /// the kind of rule that gets inverted by a single edit: three conditions that look similar
        /// and mean different things. Moving it here makes it a function of two model values, and
        /// <c>StateWidgetChecks</c> pins all six combinations.
        ///
        /// The three conditions:
        ///
        ///  * <b>the shape exists</b> — a category without a widget has nothing to draw;
        ///  * <b>the block is not switched off</b> — and "switched off" includes a block that cannot
        ///    be switched on at all. The test is <paramref name="power"/> <c>!= On</c>, not
        ///    <c>== Off</c>, because <see cref="BlockPower.Unavailable"/> (damaged below the critical
        ///    integrity) and <see cref="BlockPower.Unknown"/> (nobody has read it) must both stay
        ///    silent: a reading from a block the game will not run is the same lie as a green switch
        ///    on a dead one (ADR-0035 §2);
        ///  * <b>the block reports a reading</b> — not read yet, or (a timer) not counting down.
        ///
        /// <b>A block with no switch is never asked about its power.</b> Its <c>Power</c> is
        /// <see cref="BlockPower.Unknown"/> forever, and that is not "off": a cargo container's fill
        /// level is visible whatever its (nonexistent) switch says. That is why
        /// <paramref name="canBeEnabled"/> is a parameter rather than something this method derives.
        /// </summary>
        public static bool ShowsWidget(StateWidgetKind kind, bool canBeEnabled, BlockPower power,
                                       bool active)
        {
            if (kind == StateWidgetKind.None) return false;
            if (canBeEnabled && power != BlockPower.On) return false;
            return active;
        }
    }
}
