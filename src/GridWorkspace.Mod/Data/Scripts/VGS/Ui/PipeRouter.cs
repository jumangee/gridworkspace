using System;
using System.Collections.Generic;
using VRageMath;

namespace Vgs.Ui
{
    /// <summary>
    /// Turns an orthogonal route into the two rails of a pipe drawn in cross-section
    /// (ADR-0040).
    ///
    /// <b>What a pipe looks like.</b> Two parallel lines with a fixed gap between them, the
    /// way a pipe reads in a section drawing. The gap is the bore and the lines are the walls;
    /// nothing is filled between them, so a pipe never looks like a solid wire and can never be
    /// mistaken for a logical connection, which is a single line (ADR-0010).
    ///
    /// <b>Why the corners are rounded here and not in the draw path.</b> The screen-space API
    /// has no line primitive at all, let alone an arc: everything is an axis-aligned rectangle
    /// (see <c>VgsScreenDraw.SegmentPixels</c>). A rounded bend is therefore a <em>staircase</em>
    /// of short axis-aligned pieces following a quarter arc — the same reduction
    /// <c>VgsScreenDraw.Disc</c> uses to draw a circle out of slabs, and the same one
    /// <c>LineRaster</c> uses to draw a connection's bend. Building it here keeps it a
    /// pure function of the route, which is what makes it assertable offline; building it in
    /// the draw path would put a per-frame loop of trigonometry where nothing can check it.
    ///
    /// <b>Why an instance and not a static class.</b> A pipe's geometry is derived from a route
    /// whose length is only known at call time, so the builder needs scratch space sized to it —
    /// and the draw path builds up to <c>VgsConstants.MaxRenderedConveyors</c> pipes every frame.
    /// A fresh array per call would be a few hundred Gen0 allocations per frame, which is
    /// exactly what rule R010 forbids. The router therefore owns its scratch and grows it once,
    /// like <c>VgsEditorScreen</c> owns the route and rail lists. Every other type in this layer
    /// is static because it needs no such state; this one does, and hiding that behind a static
    /// would mean allocating in the one place that must not.
    ///
    /// <b>Why the aspect ratio is a parameter.</b> One X unit of the layout is the window's
    /// width and one Y unit its height, so an equal offset in both axes is not an equal
    /// distance on screen (ADR-0009). A pipe must have the same visible width on every window
    /// shape, so the horizontal part of every offset and of every fillet radius is multiplied
    /// by the aspect — exactly the correction <c>PortGeometry</c> applies to round controls.
    /// This type does not read the aspect from the drawing layer: it takes it as an argument,
    /// which is what keeps it testable without the game.
    ///
    /// <b>Output.</b> A list of axis-aligned centre lines of the rails, each as
    /// <c>(x0, y0, x1, y1)</c> with either <c>y0 == y1</c> (horizontal) or <c>x0 == x1</c>
    /// (vertical). The caller draws each with a device-pixel thickness
    /// (<c>VgsScreenDraw.SegmentPixels</c>), so the walls stay one pixel wide at every zoom
    /// instead of turning translucent at low zoom — the rule ADR-0032 §3 established for the
    /// field's grid and the grid borders.
    ///
    /// The output list is cleared first, like every other route builder in this layer: the
    /// caller reuses one list for every pipe of every frame.
    /// </summary>
    public sealed class PipeRouter
    {
        /// <summary>
        /// Fewest samples a quarter-arc is split into, per rail. Two is the floor because one
        /// sample is not an arc at all: it would be a single chamfer, which reads as a square
        /// join with the corner cut off rather than as a bend.
        /// </summary>
        private const int MinCornerSteps = 2;

        /// <summary>
        /// Most samples a quarter-arc is split into, per rail. The staircase is two rectangles
        /// per sample, so this caps one bend at <c>2 × 2 × 8 = 32</c> pieces — which is already
        /// more than a bend can show at the maximum zoom, where the radius is about 18 pixels.
        /// Beyond it a larger radius is simply sampled more coarsely; nothing about the picture
        /// breaks.
        /// </summary>
        private const int MaxCornerSteps = 8;

        /// <summary>Below this a fillet is not worth building: the join is square instead.</summary>
        private const float MinFillet = 1e-5f;

        // ---- scratch, grown once and reused for every pipe ----

        private Vector2[] _points = new Vector2[8];
        private Vector2[] _cornerStart = new Vector2[8];
        private Vector2[] _cornerEnd = new Vector2[8];
        private float[] _cornerFillet = new float[8];

        /// <summary>
        /// Builds both rails of a pipe along <paramref name="route"/>.
        ///
        /// <paramref name="route"/> is the output of <see cref="EdgeRouting.Build"/>: orthogonal
        /// segments in the same space as <paramref name="halfWidth"/> and
        /// <paramref name="cornerRadius"/>. <paramref name="halfWidth"/> is half the pipe's
        /// overall width measured <b>vertically</b> (the caller converts the bore and the wall
        /// thickness to layout units); <paramref name="aspect"/> is the client height ÷ width,
        /// applied to every horizontal extent.
        ///
        /// <paramref name="stepLength"/> is the longest one piece of a bend's staircase may be,
        /// in the layout's vertical units. It is what makes the rounding <b>look</b> round: a
        /// fixed number of samples would be a chamfer at a small radius and a coarse stair at a
        /// large one, and the caller is the only layer that knows how many pixels a layout unit
        /// is at the current zoom. It is the same rule <c>VgsScreenDraw.Disc</c> follows with one
        /// slab per screen pixel. A non-positive value falls back to
        /// <see cref="MinCornerSteps"/>, so a caller that forgets it still gets a bend.
        ///
        /// <paramref name="fill"/> is the <b>highlight</b> of the pipe (ADR-0049): the same route,
        /// with the same fillets and the same staircase, but as the pipe's <em>centre line</em>
        /// instead of its two walls. The caller draws each piece of it at the pipe's full width
        /// (<c>VgsConstants.ConveyorFillPixels</c>), which fills the bore and stops exactly at the
        /// outer edge of each rail. It is an output of this call rather than a second pass so that
        /// the fill cannot disagree with the walls it sits between: one fillet, one radius, one
        /// step count. Null means "no highlight", and then nothing extra is built.
        ///
        /// Returns the number of rail segments written. A route with fewer than one segment
        /// produces nothing, which the caller treats as "nothing to draw" — the same answer a
        /// connection gets from <c>PortGeometry.TryPortDot</c> when its port has no slot.
        /// </summary>
        public int Build(IList<Vector4> route, float cornerRadius, float halfWidth,
                         float aspect, float stepLength, List<Vector4> into)
        {
            return Build(route, cornerRadius, halfWidth, aspect, stepLength, into, null);
        }

        /// <summary>
        /// The same build, with the pipe's <b>fill</b> as an extra output — see the overload above.
        /// One implementation, because a highlight that computed its own fillets would be a second
        /// answer to "where does this pipe bend", and the two would drift apart at the first
        /// change of radius or step.
        /// </summary>
        public int Build(IList<Vector4> route, float cornerRadius, float halfWidth,
                         float aspect, float stepLength, List<Vector4> into, List<Vector4> fill)
        {
            if (into == null) return 0;
            into.Clear();
            if (fill != null) fill.Clear();

            if (route == null || route.Count == 0) return 0;
            if (halfWidth <= 0f) return 0;
            if (aspect <= 0f) aspect = 1f;
            if (cornerRadius < 0f) cornerRadius = 0f;
            if (stepLength <= 0f) stepLength = float.MaxValue;

            int count = route.Count;
            Reserve(count);

            // Vertices of the centre line. The segments of a route are connected end to end by
            // construction, so the vertices are the first segment's start and every segment's
            // end; a route that turns out not to be connected simply produces a pipe with a
            // jump in it, which is a drawing artefact rather than a crash.
            _points[0] = new Vector2(route[0].X, route[0].Y);
            for (int i = 0; i < count; i++)
            {
                Vector4 segment = route[i];
                _points[i + 1] = new Vector2(segment.Z, segment.W);
            }

            // ---- corners: where each fillet starts and ends ----
            //
            // A fillet is described by its tangent points on the two adjacent segments, so the
            // straights can stop short of the corner without knowing anything else about it.
            for (int i = 1; i < count; i++)
            {
                _cornerFillet[i] = 0f;
                _cornerStart[i] = _points[i];
                _cornerEnd[i] = _points[i];

                Vector2 u = Direction(_points[i - 1], _points[i]);
                Vector2 w = Direction(_points[i], _points[i + 1]);

                // Collinear (or degenerate) neighbours are one straight, not a corner.
                if (u.X == 0f && u.Y == 0f) continue;
                if (w.X == 0f && w.Y == 0f) continue;
                if (u.X == w.X && u.Y == w.Y) continue;

                float su = AxisScale(u, aspect);
                float sw = AxisScale(w, aspect);

                float lengthIn = Length(_points[i - 1], _points[i]);
                float lengthOut = Length(_points[i], _points[i + 1]);

                // A fillet may use at most half of each adjacent segment: two corners of one
                // short segment must not overlap, or the rails would cross themselves.
                float radius = cornerRadius;
                float limitIn = lengthIn / (2f * su);
                float limitOut = lengthOut / (2f * sw);
                if (radius > limitIn) radius = limitIn;
                if (radius > limitOut) radius = limitOut;

                if (radius <= MinFillet) continue;

                _cornerFillet[i] = radius;
                _cornerStart[i] = new Vector2(_points[i].X - u.X * radius * su,
                                              _points[i].Y - u.Y * radius * su);
                _cornerEnd[i] = new Vector2(_points[i].X + w.X * radius * sw,
                                            _points[i].Y + w.Y * radius * sw);
            }

            // ---- straights: each centre-line segment becomes two rails ----
            for (int i = 0; i < count; i++)
            {
                Vector2 d = Direction(_points[i], _points[i + 1]);
                if (d.X == 0f && d.Y == 0f) continue;

                Vector2 start = i == 0 ? _points[0] : _cornerEnd[i];
                Vector2 end = i == count - 1 ? _points[count] : _cornerStart[i + 1];

                AddRail(into, start, end, d, halfWidth, aspect, 1f);
                AddRail(into, start, end, d, halfWidth, aspect, -1f);

                // The fill runs down the centre line, so a straight is one piece with no offset.
                if (fill != null) AddSegment(fill, start, end);
            }

            // ---- corners: two quarter arcs, each drawn as a staircase ----
            for (int i = 1; i < count; i++)
            {
                if (_cornerFillet[i] <= MinFillet) continue;

                Vector2 u = Direction(_points[i - 1], _points[i]);
                Vector2 w = Direction(_points[i], _points[i + 1]);
                if (u.X == 0f && u.Y == 0f) continue;
                if (w.X == 0f && w.Y == 0f) continue;

                float su = AxisScale(u, aspect);
                float sw = AxisScale(w, aspect);
                float radius = _cornerFillet[i];

                // Centre of the fillet: the point at distance `radius` from both lines.
                Vector2 centre = new Vector2(_points[i].X - u.X * radius * su + w.X * radius * sw,
                                             _points[i].Y - u.Y * radius * su + w.Y * radius * sw);

                float radiusU = radius * su;
                float radiusW = radius * sw;
                float halfU = (u.X != 0f) ? halfWidth * aspect : halfWidth;
                float halfW = (w.X != 0f) ? halfWidth * aspect : halfWidth;

                // One sample per `stepLength` of arc, on the outer rail (the longer one), so
                // both rails of a bend get the same count and their staircases stay parallel.
                float arcLength = (radius + halfWidth) * 1.5707963f;
                int steps = (int)Math.Ceiling(arcLength / stepLength);
                if (steps < MinCornerSteps) steps = MinCornerSteps;
                if (steps > MaxCornerSteps) steps = MaxCornerSteps;

                // An odd count would put a sample on neither the start, the 45° point nor the
                // end of the arc, which makes the staircase visibly asymmetric about the bend's
                // diagonal. Rounding up to even costs one piece and removes the asymmetry.
                if ((steps & 1) != 0) steps++;

                AddArc(into, centre, u, w, radiusU + halfU, radiusW + halfW, steps);
                AddArc(into, centre, u, w, radiusU - halfU, radiusW - halfW, steps);

                // The fill's bend is the same staircase at the centre radius: the two walls stay
                // parallel to it, so the band the caller draws between them closes the bend
                // instead of stepping across it.
                if (fill != null) AddArc(fill, centre, u, w, radiusU, radiusW, steps);
            }

            return into.Count;
        }

        /// <summary>
        /// Grows the scratch so a route of <paramref name="count"/> segments fits. Never shrinks:
        /// the longest route of a session is a good size for all of them, and a shrink would
        /// just mean growing again on the next frame.
        /// </summary>
        private void Reserve(int count)
        {
            if (_points.Length >= count + 1) return;

            int size = _points.Length;
            while (size < count + 1) size *= 2;

            _points = new Vector2[size];
            _cornerStart = new Vector2[size];
            _cornerEnd = new Vector2[size];
            _cornerFillet = new float[size];
        }

        /// <summary>
        /// Adds one rail of a straight run: the centre line shifted along the segment's normal
        /// by <paramref name="halfWidth"/>, times <paramref name="side"/>.
        /// </summary>
        private static void AddRail(List<Vector4> into, Vector2 start, Vector2 end, Vector2 direction,
                                    float halfWidth, float aspect, float side)
        {
            Vector2 offset = Offset(direction, halfWidth * side, aspect);
            AddSegment(into, start + offset, end + offset);
        }

        /// <summary>
        /// Adds a quarter arc as an axis-aligned staircase.
        ///
        /// The arc runs from the tangent point on the incoming segment to the tangent point on
        /// the outgoing one, centred on the fillet's centre. Because the two directions are
        /// orthogonal and unit-length, the arc is the set
        /// <c>C + (-w)·Rw·cos φ + u·Ru·sin φ</c> for φ in <c>0..π/2</c>, which reduces to plain
        /// axis-aligned steps when sampled. A non-positive radius means the rail has collapsed
        /// onto the centre point (the bore is wider than the fillet); that arc is skipped, and
        /// the two straights simply meet there — a corner with no visible inner rounding, which
        /// is the honest picture of a bend tighter than the pipe.
        /// </summary>
        private static void AddArc(List<Vector4> into, Vector2 centre, Vector2 u, Vector2 w,
                                   float radiusU, float radiusW, int steps)
        {
            if (radiusU <= 0f || radiusW <= 0f) return;
            if (steps < 1) return;

            Vector2 previous = ArcPoint(centre, u, w, radiusU, radiusW, 0f);

            for (int step = 1; step <= steps; step++)
            {
                float angle = (float)(Math.PI * 0.5 * step / steps);
                Vector2 current = ArcPoint(centre, u, w, radiusU, radiusW, angle);

                // Horizontal first, then vertical: a deterministic staircase, so the same route
                // always produces the same picture.
                if (current.X != previous.X)
                {
                    into.Add(new Vector4(previous.X, previous.Y, current.X, previous.Y));
                }
                if (current.Y != previous.Y)
                {
                    into.Add(new Vector4(current.X, previous.Y, current.X, current.Y));
                }

                previous = current;
            }
        }

        private static Vector2 ArcPoint(Vector2 centre, Vector2 u, Vector2 w,
                                        float radiusU, float radiusW, float angle)
        {
            float cos = (float)Math.Cos(angle);
            float sin = (float)Math.Sin(angle);

            return new Vector2(centre.X - w.X * radiusW * cos + u.X * radiusU * sin,
                               centre.Y - w.Y * radiusW * cos + u.Y * radiusU * sin);
        }

        /// <summary>
        /// Unit direction from one point to the next, or zero for a degenerate segment. The
        /// routes this type consumes are orthogonal, so the result is always one of the four
        /// axis directions; a value that is a hair off is snapped to the dominant axis rather
        /// than propagated, which is what keeps the offsets exactly axis-aligned.
        /// </summary>
        private static Vector2 Direction(Vector2 from, Vector2 to)
        {
            float dx = to.X - from.X;
            float dy = to.Y - from.Y;

            if (Math.Abs(dx) >= Math.Abs(dy))
            {
                if (Math.Abs(dx) <= MinFillet) return Vector2.Zero;
                return new Vector2(dx > 0f ? 1f : -1f, 0f);
            }

            if (Math.Abs(dy) <= MinFillet) return Vector2.Zero;
            return new Vector2(0f, dy > 0f ? 1f : -1f);
        }

        /// <summary>
        /// Normal of a unit axis direction, with the extent converted to the layout's units:
        /// the vertical part of an offset needs no correction, the horizontal part does
        /// (ADR-0009).
        /// </summary>
        private static Vector2 Offset(Vector2 direction, float halfWidth, float aspect)
        {
            // The normal of (dx, dy) is (-dy, dx). A vertical normal is a horizontal distance
            // on screen, so it takes the aspect; a horizontal normal is already vertical.
            float nx = -direction.Y;
            float ny = direction.X;

            return new Vector2(nx * halfWidth * aspect, ny * halfWidth);
        }

        /// <summary>
        /// How many layout units one <paramref name="halfWidth"/> vertical unit is along this
        /// axis: 1 for the vertical axis, the aspect for the horizontal one.
        /// </summary>
        private static float AxisScale(Vector2 direction, float aspect)
        {
            return direction.X != 0f ? aspect : 1f;
        }

        private static float Length(Vector2 from, Vector2 to)
        {
            float dx = to.X - from.X;
            float dy = to.Y - from.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// Appends one axis-aligned rail piece, dropping a zero-length one. The staircase of an
        /// arc legitimately produces empty steps when a sample lands on the previous one, and a
        /// zero-length rectangle would be a wasted draw call per frame.
        /// </summary>
        private static void AddSegment(List<Vector4> into, Vector2 from, Vector2 to)
        {
            if (from.X == to.X && from.Y == to.Y) return;

            if (from.X != to.X && from.Y != to.Y)
            {
                // Never produced by this type — offsets are axis-aligned by construction — but
                // a diagonal piece drawn by the rectangle primitive would be a silent lie, so
                // it is split into its two legs instead.
                into.Add(new Vector4(from.X, from.Y, to.X, from.Y));
                into.Add(new Vector4(to.X, from.Y, to.X, to.Y));
                return;
            }

            into.Add(new Vector4(from.X, from.Y, to.X, to.Y));
        }
    }
}
