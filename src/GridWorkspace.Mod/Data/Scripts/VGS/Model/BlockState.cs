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
    /// plan, not here.
    /// </summary>
    public sealed class BlockState
    {
        /// <summary>
        /// No metrics, shared by every block whose category has none — and by every block today,
        /// because no category publishes a metric yet. Shared rather than allocated per node: an
        /// empty array per card would be pure waste on a five-hundred-block grid.
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
        /// </summary>
        public readonly float[] Metrics;

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
        /// </summary>
        public void SetMetric(int index, float value)
        {
            if (index < 0 || index >= Metrics.Length) return;
            Metrics[index] = value;
        }

        public override string ToString()
        {
            return "integrity " + Integrity.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)
                   + ", metrics " + Metrics.Length;
        }
    }
}
