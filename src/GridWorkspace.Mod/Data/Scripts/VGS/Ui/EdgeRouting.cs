using System;
using System.Collections.Generic;
using VRageMath;
using Vgs.Core;

namespace Vgs.Ui
{
    /// <summary>
    /// Turns two connection dots into an orthogonal route.
    ///
    /// A connection always runs from the dot on the right of an output bar to the dot on the
    /// left of an input bar, and the route is built so the last stretch arrives at the target
    /// dot <b>from the left, moving right</b>. That is not decoration: it is what makes the
    /// picture readable. Every line enters a card the same way, so a glance at a card's input
    /// bars tells you which side of the ship the signals come from.
    ///
    /// Routes are produced as axis-aligned segments into a caller-owned list. The list is
    /// cleared and refilled per edge — no allocation once it has grown to its final size,
    /// which matters because this runs for every edge in the graph on every frame.
    ///
    /// Segments are stored as <c>(x0, y0, x1, y1)</c>, each one horizontal or vertical.
    ///
    /// <b>Units.</b> The router works in whatever space the dots were given in, and the caller
    /// passes screen-space dots plus a screen-space gutter. The gutter has to be scaled by the
    /// caller's zoom, not fixed here: at zoom 3 a graph-space gutter would put the departure
    /// point inside the port bar, and at zoom 0.35 it would fling it away from the card.
    ///
    /// A connection whose two ends are on the same card — a block driving itself (ADR-0036) —
    /// is the one case the two shapes above cannot express; it gets its own route,
    /// <see cref="BuildSelfLoop"/>, which goes around the card instead of through it.
    /// </summary>
    public static class EdgeRouting
    {
        /// <summary>
        /// Builds the route from <paramref name="from"/> (an output dot) to
        /// <paramref name="to"/> (an input dot, or the cursor while binding).
        ///
        /// Two shapes. When the target is to the right, the route is a single Z whose bend sits
        /// at the horizontal midpoint of the whole line:
        ///
        /// <code>
        ///   o────────┐
        ///            │        ← bend halfway along the line, not next to the source
        ///            └────────o
        /// </code>
        ///
        /// The bend used to sit one gutter to the right of the source, which made every line
        /// look like it was pinned to the block it started from and then travelled at the
        /// target's height. Halfway along reads as a route between two peers.
        ///
        /// When the target is level with or left of the source, no single vertical can both
        /// leave the source and arrive from the left, so the route is an S: a short stub to
        /// clear the source bar, a long horizontal run in the lane between the two heights, and
        /// a short drop into the target.
        ///
        /// <code>
        ///   o──┐
        ///      └──────────┐
        ///                 └──o
        /// </code>
        /// </summary>
        public static void Build(Vector2 from, Vector2 to, float gutter, List<Vector4> into)
        {
            into.Clear();

            if (gutter < 0f) gutter = 0f;

            float departureX = from.X + gutter;
            float approachX = to.X - gutter;

            if (approachX > departureX)
            {
                // Halfway, but never so close to the source that the vertical run would be
                // hidden under the source bar: the dot is inside the bar, so the bend has to
                // clear the bar's right edge.
                float midX = (from.X + to.X) * 0.5f;
                if (midX < departureX) midX = departureX;
                if (midX > approachX) midX = approachX;

                AddHorizontal(into, from.X, midX, from.Y);
                AddVertical(into, midX, from.Y, to.Y);
                AddHorizontal(into, midX, to.X, to.Y);
                return;
            }

            // Target level with or left of the source: stub out of the source bar, run along
            // the middle lane, drop in from the left of the target.
            float midY = (from.Y + to.Y) * 0.5f;

            AddHorizontal(into, from.X, departureX, from.Y);
            AddVertical(into, departureX, from.Y, midY);
            AddHorizontal(into, departureX, approachX, midY);
            AddVertical(into, approachX, midY, to.Y);
            AddHorizontal(into, approachX, to.X, to.Y);
        }

        /// <summary>
        /// Builds the route for a connection whose two ends are on <b>one</b> card: a block that
        /// drives itself (ADR-0036).
        ///
        /// <b>Why the ordinary route does not do.</b> The S built by <see cref="Build"/> puts its
        /// long horizontal run in the lane between the two dots, and on one card that lane is the
        /// seam between two port bars — which are drawn after the lines, and opaque. The
        /// connection would show as a stub on the right and an L on the left with a hole where
        /// the bars are: two pieces that do not read as one wire.
        ///
        /// <b>The shape.</b> The loop leaves the output dot to the right, climbs past the card's
        /// top edge, crosses above the card, comes down past its left edge and enters the input
        /// dot from the left — the same "arrive moving right" rule every other connection obeys:
        ///
        /// <code>
        ///        ┌──────────────┐
        ///   ┌────┘              └────┐
        ///   │                        │     ← above the card, which is drawn over the lanes
        ///   │   ┌────────────────┐   │
        ///   │   │     block      │   │
        ///   │   └────────────────┘   │
        ///   │   | (o) Triggered      │
        ///   │   +--------------------+
        ///   └──►(o) Trigger Now  (x)  │
        ///       +--------------------+
        /// </code>
        ///
        /// <b>The lanes are measured from the card, not from the dots.</b> The dots sit inside
        /// their bars, a fixed inset from the card's edges, while the gutter is a separate
        /// constant — and on one card a dot-relative lane only stays outside the card for as
        /// long as the gutter happens to be the larger of the two. That is a coincidence of two
        /// unrelated constants, and the inset grows with the client aspect and with the port
        /// control's size, so the coincidence is not something to rely on. Anchored to the card,
        /// the lanes stay outside it whatever the port controls look like — which is why the
        /// rectangle is a parameter rather than the single top coordinate: the sides carry the
        /// guarantee.
        ///
        /// The buffer is cleared first, like <see cref="Build"/>, and zero-length segments are
        /// dropped: a caller reuses one list for every line of every frame.
        /// </summary>
        public static void BuildSelfLoop(Vector2 from, Vector2 to, Vector2 cardTopLeft,
                                         Vector2 cardSize, float gutter, List<Vector4> into)
        {
            if (into == null) return;
            into.Clear();

            if (gutter < 0f) gutter = 0f;

            float cardLeft = cardTopLeft.X;
            float cardRight = cardTopLeft.X + cardSize.X;
            if (cardSize.X <= 0f || cardSize.Y <= 0f)
            {
                // No card to go around: fall back to the dots, which is what the ordinary route
                // would have done anyway.
                cardLeft = from.X < to.X ? from.X : to.X;
                cardRight = from.X < to.X ? to.X : from.X;
            }

            float departureX = from.X + gutter;
            float rightLane = cardRight + gutter;
            if (rightLane > departureX) departureX = rightLane;

            float approachX = to.X - gutter;
            float leftLane = cardLeft - gutter;
            if (leftLane < approachX) approachX = leftLane;

            float laneY = cardTopLeft.Y - gutter;

            AddHorizontal(into, from.X, departureX, from.Y);
            AddVertical(into, departureX, from.Y, laneY);
            AddHorizontal(into, departureX, approachX, laneY);
            AddVertical(into, approachX, laneY, to.Y);
            AddHorizontal(into, approachX, to.X, to.Y);
        }

        private static void AddHorizontal(List<Vector4> into, float x0, float x1, float y)
        {
            if (x0 == x1) return;
            into.Add(new Vector4(x0, y, x1, y));
        }

        private static void AddVertical(List<Vector4> into, float x, float y0, float y1)
        {
            if (y0 == y1) return;
            into.Add(new Vector4(x, y0, x, y1));
        }

        /// <summary>
        /// Turns one orthogonal segment into the dashes it is drawn as, replacing the contents of
        /// <paramref name="into"/> with <c>(x0, y0, x1, y1)</c> entries — one per dash.
        ///
        /// A watch line is dashed (ADR-0017) and the screen-space API has no dash primitive, so
        /// the pattern is built here — in the same layer as the route, in the same units, and
        /// therefore assertable without the game. The caller passes the dash and gap already
        /// scaled by its zoom, which keeps the pattern a constant length on screen instead of a
        /// constant fraction of the graph: a dashed line that turns solid at low zoom would stop
        /// being distinguishable from a real connection, which is the only reason it is dashed.
        ///
        /// <b>The list is cleared first</b>, like <see cref="Build"/> does and for the same
        /// reason: the caller reuses one buffer for every line of every frame, and appending
        /// would make each frame redraw every dash ever produced.
        ///
        /// The pattern starts at the segment's first end and is cut at the last: the final dash
        /// is shortened rather than allowed to overrun, so a dash never pokes out of the route.
        /// A segment shorter than one dash is drawn as one dash — an empty line would read as a
        /// gap in the route.
        /// </summary>
        public static void BuildDashes(Vector4 segment, float dashLength, float gapLength,
                                       List<Vector4> into)
        {
            if (into == null) return;
            into.Clear();

            float dx = segment.Z - segment.X;
            float dy = segment.W - segment.Y;
            float length = (float)Math.Sqrt(dx * dx + dy * dy);
            if (length <= 0f) return;

            if (dashLength <= 0f || gapLength < 0f)
            {
                into.Add(segment);
                return;
            }

            float ux = dx / length;
            float uy = dy / length;
            float step = dashLength + gapLength;

            // The dash positions are derived from the index rather than accumulated: `at += step`
            // drifts in single precision, and the drift shows up as a phantom dash just past the
            // end of the segment — a dash that pokes out of the line it belongs to.
            //
            // The stop is a hair short of the length for the same reason: a dash that *starts*
            // exactly at the end has nothing left to draw, and whether `n * step` lands just
            // above or just below the length is an accident of float rounding.
            float limit = length - step * 1e-4f;
            if (limit < 0f) limit = 0f;

            for (int i = 0; ; i++)
            {
                float at = i * step;
                if (at > limit) break;

                float end = at + dashLength;
                if (end > length) end = length;

                into.Add(new Vector4(segment.X + ux * at, segment.Y + uy * at,
                                     segment.X + ux * end, segment.Y + uy * end));
            }
        }
    }
}
