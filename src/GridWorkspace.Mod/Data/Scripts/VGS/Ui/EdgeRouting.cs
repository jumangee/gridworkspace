using System;
using System.Collections.Generic;
using VRageMath;
using Vgs.Core;

namespace Vgs.Ui
{
    /// <summary>
    /// Turns two connection dots into an orthogonal route, and that route into the smooth path
    /// a connection is actually drawn as.
    ///
    /// A connection always runs from the dot on the right of an output bar to the dot on the
    /// left of an input bar, and the route is built so the last stretch arrives at the target
    /// dot <b>from the left, moving right</b>. That is not decoration: it is what makes the
    /// picture readable. Every line enters a card the same way, so a glance at a card's input
    /// bars tells you which side of the ship the signals come from.
    ///
    /// <b>Two representations, and they are both needed.</b> <see cref="Build"/> and
    /// <see cref="BuildSelfLoop"/> produce the orthogonal route — the corners are the shape of
    /// the route, and the conveyor pipe still consumes exactly that (<c>PipeRouter</c>). A
    /// connection, however, is not drawn as the route: <see cref="Round"/> replaces every corner
    /// with a bend that runs from the middle of one segment to the middle of the next, so the
    /// line reads as a curve instead of as a staircase of right angles (ADR-0046). The route
    /// stays the single source of the shape; the rounded path is derived from it.
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
        /// Longest chord one bend is flattened into, in screen pixels.
        ///
        /// The rounded path is a polyline, and how fine that polyline has to be is a question
        /// about what the eye can resolve, not about the geometry: a chord of length <c>c</c> on
        /// a bend of radius <c>R</c> leaves the true curve by <c>c² / (8R)</c>, which for the
        /// bends this route produces — hundreds of pixels across — is a fraction of a pixel even
        /// for a chord ten pixels long. Two pixels is therefore generous, and it is the value the
        /// conveyor's bend staircase uses (<c>ConveyorStepPixels</c>), so both kinds of line
        /// break a curve into pieces of the same size.
        /// </summary>
        private const float BendChordPixels = 2f;

        /// <summary>
        /// Fewest samples a bend is flattened into. A bend below this is a corner with a chip
        /// taken out of it rather than a curve; four samples already read as a rounded corner at
        /// the smallest size a bend can have (a stub of a few pixels).
        /// </summary>
        private const int MinBendSamples = 4;

        /// <summary>
        /// Most samples a bend is flattened into.
        ///
        /// <b>A cap, not a smoothness setting.</b> The deviation of a chord from the curve falls
        /// with the square of the bend's size, so past a few dozen samples the polyline is
        /// already indistinguishable from the curve at any zoom the editor allows; what keeps
        /// growing is the number of points the rasteriser has to walk. The cap is what makes the
        /// cost of a connection bounded by the size of its bends rather than by the size of the
        /// screen.
        ///
        /// <b>The number is measured, in both directions.</b> A quadratic Bézier with control
        /// arms of 1000 px leaves the true curve by about 0.2 px at a chord of 34 px, which is
        /// what 48 samples give on a full-screen bend — far below what the pixel grid can show.
        /// And the cost is not monotone in the sample count: the preview harness
        /// (<c>tools/line-preview/run.ps1</c>) measures 135 rectangles for a connection to the
        /// right and below at 48 samples against 174 at 96, because a long chord's per-column
        /// slope is constant and merges well, while a finely sampled curve changes its extent
        /// from column to column. Finer is therefore both invisible and more expensive.
        /// </summary>
        private const int MaxBendSamples = 48;

        /// <summary>
        /// Turns an orthogonal route into the smooth path a connection is drawn as (ADR-0046).
        ///
        /// <b>The shape, in the owner's words.</b> The route already gives the straight pieces:
        /// a line to a corner, then the next straight. What a connection should look like is
        /// that route with the corners taken out — a straight run to the <em>middle</em> of the
        /// first segment, then a bend to the <em>middle</em> of the next one, then a straight
        /// from the last middle to the target:
        ///
        /// <code>
        ///   o─────────╮
        ///              ╰──────╮
        ///                     ╰─────────o
        /// </code>
        ///
        /// Every pair of neighbouring segments therefore gets exactly one bend, and a route of
        /// three segments becomes: straight, bend, bend, straight. The bends meet at the middle
        /// of the shared segment, and both of them leave it in the same direction there, so the
        /// two joins are smooth — the path has no corners left at all, only curvature.
        ///
        /// <b>Why a quadratic Bézier and not an arc.</b> The bend has to be tangent to both
        /// segments at the points the owner named, and those points are the middles of two
        /// segments of <em>different</em> lengths. A circular arc can only be tangent at two
        /// points equidistant from the corner; here the two distances are independent, so the
        /// arc exists only in the special case. A quadratic Bézier with the corner as its
        /// control point is tangent to the incoming segment at the start and to the outgoing one
        /// at the end <b>by construction</b>, for any two distances, and it stays inside the
        /// triangle of the corner — it cannot overshoot into a neighbouring card.
        ///
        /// <b>Sampling is in pixels, not in layout units.</b> One X unit of the layout is the
        /// window's width and one Y unit its height (ADR-0009), so a sample count derived from
        /// layout units would draw a bend on a wide window as coarsely as on a square one.
        /// <paramref name="pixelsPerUnit"/> is the client-area size in pixels, which the draw
        /// layer already knows (<c>VgsScreenDraw.ClientWidth</c>/<c>ClientHeight</c>); the bend is
        /// sampled so that its longest chord is about <see cref="BendChordPixels"/> long on
        /// screen, whatever the zoom.
        ///
        /// The list is cleared first, like <see cref="Build"/>: the caller reuses one buffer for
        /// every line of every frame, and appending would make each frame redraw every path ever
        /// produced.
        /// </summary>
        public static void Round(IList<Vector4> route, Vector2 pixelsPerUnit, List<Vector2> into)
        {
            if (into == null) return;
            into.Clear();
            if (route == null || route.Count == 0) return;

            if (pixelsPerUnit.X <= 0f) pixelsPerUnit.X = 1f;
            if (pixelsPerUnit.Y <= 0f) pixelsPerUnit.Y = 1f;

            Vector4 first = route[0];
            into.Add(new Vector2(first.X, first.Y));

            Vector2 previousMid = Vector2.Zero;

            for (int i = 0; i < route.Count; i++)
            {
                Vector4 segment = route[i];
                Vector2 mid = new Vector2((segment.X + segment.Z) * 0.5f,
                                          (segment.Y + segment.W) * 0.5f);

                if (i == 0)
                {
                    // The straight run out of the dot, up to the middle of the first segment.
                    into.Add(mid);
                }
                else
                {
                    // The corner the two segments share is the bend's control point: the route's
                    // segments meet end to end, so the previous segment's end is this one's start.
                    Vector4 before = route[i - 1];
                    AddBend(into, previousMid, mid, new Vector2(before.Z, before.W), pixelsPerUnit);
                }

                previousMid = mid;
            }

            Vector4 last = route[route.Count - 1];
            into.Add(new Vector2(last.Z, last.W));
        }

        /// <summary>
        /// Appends the bend from <paramref name="from"/> to <paramref name="to"/> around
        /// <paramref name="corner"/>, sampled into chords.
        ///
        /// The start point is <b>not</b> appended: it is the previous point of the path already,
        /// and a duplicate would make the rasteriser draw a zero-length chord. The end point is
        /// appended, because it is the next segment's middle and the next straight starts there.
        /// </summary>
        private static void AddBend(List<Vector2> into, Vector2 from, Vector2 to, Vector2 corner,
                                    Vector2 pixelsPerUnit)
        {
            // Collinear ends — the route's zero-length segment was dropped, so two of its
            // segments lie on one line — are one straight, not a bend. Sampling it would add
            // dozens of points that all lie on the line the two straights already cover.
            float inX = corner.X - from.X;
            float inY = corner.Y - from.Y;
            float outX = to.X - corner.X;
            float outY = to.Y - corner.Y;

            if ((inX == 0f && inY == 0f) || (outX == 0f && outY == 0f)
                || (inX * outY - inY * outX == 0f && inX * outX + inY * outY >= 0f))
            {
                into.Add(to);
                return;
            }

            float spanX = Math.Abs(to.X - from.X) * pixelsPerUnit.X;
            float spanY = Math.Abs(to.Y - from.Y) * pixelsPerUnit.Y;
            float span = spanX > spanY ? spanX : spanY;

            int samples = (int)Math.Ceiling(span / BendChordPixels);
            if (samples < MinBendSamples) samples = MinBendSamples;
            if (samples > MaxBendSamples) samples = MaxBendSamples;

            for (int i = 1; i <= samples; i++)
            {
                float t = (float)i / samples;
                float rest = 1f - t;

                into.Add(new Vector2(rest * rest * from.X + 2f * rest * t * corner.X + t * t * to.X,
                                     rest * rest * from.Y + 2f * rest * t * corner.Y + t * t * to.Y));
            }
        }
    }
}
