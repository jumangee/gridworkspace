namespace Vgs.Model
{
    /// <summary>
    /// What a block is doing <em>right now</em>, as opposed to what it <em>is</em>: a snapshot of
    /// the values that change while the editor is open, refreshed by a cheap pass of its own.
    ///
    /// <b>A snapshot, not a live reading.</b> Everything here is copied out of the world by
    /// <c>Vgs.Game</c> on a timer and then read by the draw path. The draw path never touches the
    /// block (ADR-0003), which is why this type has no game reference at all — it is plain data
    /// and is checked offline.
    ///
    /// <b>Memory, not a saved entity.</b> A state is never written to the block's
    /// <c>MyModStorageComponent</c> and never has a storage format: it is derived from the world,
    /// so a stale copy would be a lie and a saved one would be a second source of truth
    /// (ADR-0012 keeps only what the player chose — the position and the visibility).
    ///
    /// <b>Mutable on purpose.</b> The state pass rewrites the same object twice a second instead
    /// of building a new one per node per tick, because the draw path holds a reference to it
    /// through <see cref="Node.State"/> and because allocating on a 2 Hz pass for every visible
    /// card is exactly the kind of churn the editor is trying not to have. This is the documented
    /// exception of ADR-0007: model DTOs are mutable, and the pass that writes them is named.
    ///
    /// <b>Two parts, and they are different questions.</b> <see cref="Integrity"/> is damage —
    /// every block has it, and it is the first thing the live schema shows. <see cref="Metrics"/>
    /// is reserved for the values only some categories have (charge, fill level): which key means
    /// what is the category descriptor's business and is decided in Step 3 of the organisation
    /// plan, not here. <see cref="Power"/> is the block's own on/off switch (ADR-0035): a second
    /// value every functional block has, read on the same pass, and kept apart from the integrity
    /// because a block can be switched off without being damaged and damaged without being off.
    /// </summary>
    public sealed class BlockState
    {
        /// <summary>
        /// No metrics, shared by every block whose category publishes none — which is still most of
        /// them: five categories carry a state widget (ADR-0042) and the rest have no reading at
        /// all. Shared rather than allocated per node: an empty array per card would be pure waste
        /// on a five-hundred-block grid.
        /// </summary>
        private static readonly float[] NoMetrics = new float[0];

        /// <summary>
        /// Integrity as a fraction of the block's maximum, 0..1: 1 is undamaged, 0 is destroyed.
        ///
        /// Read from the block's slim block (<c>BuildIntegrity / MaxIntegrity</c>, verified in
        /// <c>docs/01-research/api-block-state.md</c>), never from the model. A block the game
        /// cannot answer for keeps its previous value rather than snapping to a default: a card
        /// that flickers between "damaged" and "fine" because one read failed would be worse than
        /// one that lags by half a second.
        ///
        /// Starts at 1 — "not known to be damaged" — because a node exists before the first state
        /// pass reaches it, and a card that shows full damage for its first two frames is a lie
        /// the player would notice.
        /// </summary>
        public float Integrity = 1f; // review:allow R009 rewritten in place by the state pass

        /// <summary>
        /// Category-specific readings, indexed by the category descriptor's key (Step 3): charge,
        /// fill level and whatever else a category publishes. Empty for a block whose category
        /// publishes none.
        ///
        /// The array is allocated once, when the node is built, and then written in place — the
        /// frame path only reads <c>Metrics[i]</c>, so it neither allocates nor searches. That is
        /// the whole reason it is an array and not a dictionary: a lookup by string key in the
        /// draw path would allocate an enumerator and hash a string sixty times a second.
        ///
        /// <b>Every value is a fraction 0..1.</b> That is the contract the descriptor's keys state
        /// ("charge", "fill"), and it is what lets one bar draw any of them; the clamping happens
        /// in <see cref="SetMetric"/> so a reader may pass whatever the game reported.
        /// </summary>
        public readonly float[] Metrics;

        /// <summary>
        /// The block's own on/off switch, as the card draws it (ADR-0035).
        ///
        /// <b>Read from the game, never from the graph.</b> The value is the block's
        /// <c>IMyFunctionalBlock.Enabled</c>, combined with <c>IMyCubeBlock.IsFunctional</c>: a
        /// block below its critical integrity reports enabled-and-not-working, and the switch has
        /// to say "you cannot switch this on" rather than "on". The two are read together by the
        /// same pass that reads the integrity, so the picture cannot show a block that is on and
        /// broken at the same time.
        ///
        /// <b>Starts at <see cref="BlockPower.Unknown"/>, which is not drawn.</b> Unlike the
        /// integrity there is no safe default: "on" and "off" are both claims, and half a second
        /// of a wrong claim on a block the player just looked at is exactly the lie
        /// <see cref="Integrity"/>'s comment refuses. The card's state column keeps its width
        /// regardless — that width comes from the block's type, not from this value — so nothing
        /// moves when the reading arrives.
        ///
        /// <b>Written only by the state pass</b> (<c>GridGraphBuilder.RefreshState</c> through
        /// <see cref="GridGraph.ApplySwitch"/>) and by the click that switches the block, which
        /// reads the value back from the block before writing it here. The object is reused
        /// rather than replaced, so a reference taken during drawing stays valid across passes.
        /// </summary>
        public BlockPower Power = BlockPower.Unknown; // review:allow R009 rewritten in place by the state pass

        /// <summary>
        /// The direction the block's <b>state widget</b> reports, or
        /// <see cref="WidgetAccent.None"/> when it reports none (ADR-0042).
        ///
        /// <b>A field on the common state, and why that is not the ADR-0030 mistake.</b>
        /// <c>ControllerState</c> was moved off the node because it is a list with invariants and
        /// methods, and because one category in eighty needed it. This is four bytes that say
        /// "the value is rising / falling / neither", it is rewritten by the same pass that
        /// rewrites the integrity, and the draw path reads it for the card it is already drawing.
        /// A nullable reference to a one-field object would cost a branch and an allocation to say
        /// the same thing.
        ///
        /// <b>It is not "the battery's mode".</b> It is the widget's accent: a second category
        /// that has a direction gets a member of <see cref="WidgetAccent"/>, not a second field
        /// here, and a widget that has no direction simply never leaves it at
        /// <see cref="WidgetAccent.None"/>.
        /// </summary>
        public WidgetAccent Accent = WidgetAccent.None; // review:allow R009 rewritten in place by the state pass

        /// <summary>
        /// True when the block reports that its widget has something to show right now (ADR-0042).
        ///
        /// <b>It is the widget's <see cref="BlockPower.Unknown"/>.</b> A card is built before the
        /// first state pass reaches it, and every metric starts at zero — so a bar drawn from the
        /// initial state would claim "empty container" about a block nobody has read. Starting
        /// false and drawing nothing until the first reading is the same rule
        /// <see cref="BlockState.Power"/> follows, and the state column keeps its width either way,
        /// because that width comes from the block's type.
        ///
        /// <b>It is also how "there is nothing to show" is said.</b> A timer that is not counting
        /// down reports <c>Active = false</c>, and the card draws no widget at all rather than an
        /// empty bar: a stopped timer's progress is not zero, it does not exist.
        ///
        /// A failed read leaves it alone, so the card keeps its previous picture instead of
        /// flickering to "nothing" — the same rule as every other value on this type.
        /// </summary>
        public bool WidgetActive; // review:allow R009 rewritten in place by the state pass

        /// <summary>A state with no metrics: the common case until a category publishes one.</summary>
        public BlockState()
        {
            Metrics = NoMetrics;
        }

        /// <summary>
        /// A state with room for <paramref name="metricCount"/> category readings. The count comes
        /// from the block's category, so the array is exactly as long as the descriptor says and
        /// an index cannot be out of range by accident.
        /// </summary>
        public BlockState(int metricCount)
        {
            Metrics = metricCount > 0 ? new float[metricCount] : NoMetrics;
        }

        /// <summary>How many category readings this state carries; 0 when the category has none.</summary>
        public int MetricCount
        {
            get { return Metrics.Length; }
        }

        /// <summary>
        /// True when the block is damaged at all, with a tolerance: integrity is a float read from
        /// the game, and a block welded to "almost full" would otherwise count as damaged forever.
        /// </summary>
        public bool IsDamaged
        {
            get { return Integrity < 1f - IntegrityTolerance; }
        }

        /// <summary>
        /// How far below 1 counts as damage. The game's own welding leaves the last fraction of a
        /// percent unwelded, and a card that reports damage on a pristine block is a false alarm
        /// the player cannot act on.
        /// </summary>
        public const float IntegrityTolerance = 0.001f;

        /// <summary>
        /// Stores an integrity reading, clamped to 0..1. Clamping here rather than at the reader
        /// is what lets every consumer treat the value as a fraction: the game can report a
        /// negative <c>BuildIntegrity</c> on a block that is mid-deconstruction, and a value above
        /// the maximum on one that was just over-welded by a mod.
        /// </summary>
        public void SetIntegrity(float value)
        {
            if (value < 0f) value = 0f;
            else if (value > 1f) value = 1f;
            Integrity = value;
        }

        /// <summary>
        /// Stores what the block's switch says, from the two facts the game answers separately:
        /// whether it is switched on and whether it is functional at all.
        ///
        /// <b>The functional flag wins.</b> A block below its critical integrity can still report
        /// <c>Enabled = true</c> — the game leaves the flag alone and stops the block instead — so
        /// taking <paramref name="enabled"/> at face value would paint a broken block green. The
        /// precedence is here rather than at each reader, so the drawing code cannot get it
        /// backwards in one place and right in another.
        /// </summary>
        public void SetPower(bool enabled, bool functional)
        {
            Power = !functional
                ? BlockPower.Unavailable
                : (enabled ? BlockPower.On : BlockPower.Off);
        }

        /// <summary>
        /// True when the card's switch is a control that can be clicked right now: the value is
        /// known and the block can be switched on. The red state and the unread one are not
        /// clickable — one because the game will refuse, the other because nobody has read it
        /// yet — and the caller turns the first of those into a sentence rather than into a
        /// silent no-op.
        /// </summary>
        public bool CanToggle
        {
            get { return Power == BlockPower.On || Power == BlockPower.Off; }
        }

        /// <summary>One category reading by index, or 0 when this state has no such metric.</summary>
        public float GetMetric(int index)
        {
            if (index < 0 || index >= Metrics.Length) return 0f;
            return Metrics[index];
        }

        /// <summary>
        /// Writes one category reading. Silently ignores an index the category does not publish:
        /// the descriptor and the state come from the same place, so a mismatch is a defect in
        /// that place, and throwing here would put an exception in the game loop.
        ///
        /// <b>Clamped to 0..1 here, not at the reader.</b> Every metric the descriptor names is a
        /// fraction, and the game can answer outside it: an inventory reports a fill factor above
        /// one while an item is being added, and a battery reports a charge slightly over its
        /// maximum after a mod change. Clamping at the single write is what makes the value a
        /// fraction for every consumer that trusts this setter.
        ///
        /// <b>The array is public, so the clamp is a guarantee about writes and not about reads.</b>
        /// Anything that assigns <c>Metrics[i]</c> directly bypasses it, and the geometry therefore
        /// re-clamps before multiplying — cheap, and the alternative is a bar that draws past the
        /// top of its frame the first time somebody writes the array directly.
        /// </summary>
        public void SetMetric(int index, float value)
        {
            if (index < 0 || index >= Metrics.Length) return;

            if (value < 0f) value = 0f;
            else if (value > 1f) value = 1f;

            Metrics[index] = value;
        }

        /// <summary>
        /// Writes one complete widget reading — the active flag, both numbers and the accent — in
        /// a single call (ADR-0042).
        ///
        /// <b>One call, because the four values are one reading.</b> Writing them separately would
        /// let the draw path run between two writes and show a bar of the new value with the old
        /// accent; a struct in, a copy out, is one atomic change of the picture. The alternative —
        /// four setters — is how a half-updated reading gets drawn once in a hundred frames and is
        /// never reproducible.
        ///
        /// A reading with <see cref="WidgetReading.Active"/> false clears the widget but keeps the
        /// numbers: nothing is drawn, and a block that starts counting again shows its new value
        /// from the first frame rather than interpolating from a stale one.
        /// </summary>
        public void SetWidget(WidgetReading reading)
        {
            WidgetActive = reading.Active;
            Accent = reading.Accent;

            SetMetric(0, reading.First);
            SetMetric(1, reading.Second);
        }

        public override string ToString()
        {
            return "integrity " + Integrity.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)
                   + ", power " + Power
                   + ", metrics " + Metrics.Length;
        }
    }
}
