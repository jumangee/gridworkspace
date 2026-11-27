using System;
using System.Collections.Generic;
using VRageMath;

namespace Vgs.Ui
{
    /// <summary>
    /// How far the pointer is from a drawn line, in device pixels — the arithmetic behind "the
    /// pointer is over this connection" (ADR-0049).
    ///
    /// <b>Why this is a type and not a loop in the screen.</b> A hit test on a line has three
    /// failure modes that no compile can show and that the game shows only as "it does not react":
    /// a distance measured in layout units (so the target is an ellipse, not a band — the axes of
    /// this layout do not share a scale, ADR-0009), a distance measured to the <em>route</em>
    /// instead of to the drawn path (so a line that cuts its corners is unclickable at every bend,
    /// ADR-0046), and an endpoint that is not clamped (so the band grows past the end of the
    /// line). All three are decidable from numbers, so they are asserted by
    /// <c>tools/offline-check.ps1</c> instead of being judged in the game.
    ///
    /// <b>Both kinds of geometry are accepted, because the screen has both.</b> A connection is a
    /// polyline — the rounded path the rasteriser draws (<see cref="DistancePixels"/>). A pipe is
    /// still an orthogonal route: its bends are rounded by a staircase whose deviation from the
    /// route is a couple of pixels, well inside the tolerance (<see cref="RouteDistancePixels"/>).
    /// Measuring the pipe against its route is therefore the honest question, and it is also what
    /// lets the same hit test serve both without building a third representation.
    ///
    /// <b>Pixels, per axis.</b> One X unit of the layout is the window's width and one Y unit its
    /// height, so the conversion is a scale per axis and never a single number. Every method here
    /// takes <c>pixelsPerUnit</c> and does the conversion itself, so a caller cannot pass a
    /// tolerance in one space and a point in the other.
    ///
    /// No game references: this type is compiled into the offline gate.
    /// </summary>
    public static class LineHit
    {
        /// <summary>
        /// Shortest distance, in device pixels, from <paramref name="point"/> to the polyline
        /// <paramref name="path"/>. <c>float.MaxValue</c> when there is nothing to measure.
        ///
        /// The path is the same list the rasteriser draws (<see cref="LineRaster.Dotted"/>), so
        /// the target is the line the player sees rather than the route it was built from. A
        /// one-point path is a point and its distance is the distance to it; the ends of the path
        /// are clamped, so the band does not extend past the line.
        /// </summary>
        public static float DistancePixels(IList<Vector2> path, Vector2 point, Vector2 pixelsPerUnit)
        {
            if (path == null || path.Count == 0) return float.MaxValue;
            if (!HasPixels(pixelsPerUnit)) return float.MaxValue;

            Vector2 p = ToPixels(point, pixelsPerUnit);
            if (path.Count == 1) return Length(ToPixels(path[0], pixelsPerUnit), p);

            float best = float.MaxValue;
            for (int i = 1; i < path.Count; i++)
            {
                float distance = SegmentDistance(ToPixels(path[i - 1], pixelsPerUnit),
                                                 ToPixels(path[i], pixelsPerUnit), p);
                if (distance < best) best = distance;
            }

            return best;
        }

        /// <summary>
        /// True when the pointer is within <paramref name="tolerancePixels"/> of the polyline.
        /// The question the input pass asks once per candidate line.
        /// </summary>
        public static bool IsWithin(IList<Vector2> path, Vector2 point, Vector2 pixelsPerUnit,
                                    float tolerancePixels)
        {
            if (tolerancePixels <= 0f) return false;
            return DistancePixels(path, point, pixelsPerUnit) <= tolerancePixels;
        }

        /// <summary>
        /// Shortest distance, in device pixels, from <paramref name="point"/> to an orthogonal
        /// route — the segment list <see cref="EdgeRouting.Build"/> produces, each segment
        /// <c>(x0, y0, x1, y1)</c>.
        ///
        /// This is what a pipe is measured against: its rounded bends are a staircase around the
        /// route's corner, and the staircase stays within a couple of pixels of it, which is
        /// smaller than the tolerance the pointer is given anyway.
        /// </summary>
        public static float RouteDistancePixels(IList<Vector4> route, Vector2 point,
                                                Vector2 pixelsPerUnit)
        {
            if (route == null || route.Count == 0) return float.MaxValue;
            if (!HasPixels(pixelsPerUnit)) return float.MaxValue;

            Vector2 p = ToPixels(point, pixelsPerUnit);

            float best = float.MaxValue;
            for (int i = 0; i < route.Count; i++)
            {
                Vector4 segment = route[i];
                float distance = SegmentDistance(
                    ToPixels(new Vector2(segment.X, segment.Y), pixelsPerUnit),
                    ToPixels(new Vector2(segment.Z, segment.W), pixelsPerUnit), p);

                if (distance < best) best = distance;
            }

            return best;
        }

        /// <summary>True when the pointer is within the tolerance of an orthogonal route.</summary>
        public static bool RouteWithin(IList<Vector4> route, Vector2 point, Vector2 pixelsPerUnit,
                                       float tolerancePixels)
        {
            if (tolerancePixels <= 0f) return false;
            return RouteDistancePixels(route, point, pixelsPerUnit) <= tolerancePixels;
        }

        /// <summary>
        /// True when the pointer is close enough to the route's bounding box that the route itself
        /// may be within the tolerance.
        ///
        /// <b>A conservative pre-test, and exact as a rejection.</b> A rounded path is a convex
        /// combination of points that all lie on the route, so it cannot leave the route's
        /// bounding box: a pointer outside that box by more than the tolerance is farther than the
        /// tolerance from every point of the line, whichever shape the line has. The box is
        /// computed from the segment ends, which is a handful of comparisons — cheaper than walking
        /// a hundred-point path for a line that is nowhere near the pointer, and the reason the hit
        /// test can afford to look at every drawn line once per frame.
        /// </summary>
        public static bool RouteBoundsReach(IList<Vector4> route, Vector2 point,
                                            Vector2 pixelsPerUnit, float tolerancePixels)
        {
            if (route == null || route.Count == 0) return false;
            if (!HasPixels(pixelsPerUnit)) return false;

            Vector2 p = ToPixels(point, pixelsPerUnit);

            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;

            for (int i = 0; i < route.Count; i++)
            {
                Vector4 segment = route[i];

                Vector2 a = ToPixels(new Vector2(segment.X, segment.Y), pixelsPerUnit);
                Vector2 b = ToPixels(new Vector2(segment.Z, segment.W), pixelsPerUnit);

                if (a.X < minX) minX = a.X;
                if (a.Y < minY) minY = a.Y;
                if (a.X > maxX) maxX = a.X;
                if (a.Y > maxY) maxY = a.Y;

                if (b.X < minX) minX = b.X;
                if (b.Y < minY) minY = b.Y;
                if (b.X > maxX) maxX = b.X;
                if (b.Y > maxY) maxY = b.Y;
            }

            if (p.X < minX - tolerancePixels || p.X > maxX + tolerancePixels) return false;
            if (p.Y < minY - tolerancePixels || p.Y > maxY + tolerancePixels) return false;

            return true;
        }

        /// <summary>
        /// A client-area point in pixels. The transform is a scale and no offset: the editor's
        /// layout already starts at the client area's origin (ADR-0009), and only the drawing layer
        /// adds the safe area's own offset.
        /// </summary>
        private static Vector2 ToPixels(Vector2 point, Vector2 pixelsPerUnit)
        {
            return new Vector2(point.X * pixelsPerUnit.X, point.Y * pixelsPerUnit.Y);
        }

        /// <summary>
        /// True when the client area has been measured, i.e. when a layout unit has a length in
        /// pixels at all.
        ///
        /// <b>An unmeasured screen has no pixels, so nothing is within a distance of anything.</b>
        /// Without this test every point would scale to zero, every line would be zero pixels long
        /// and the pointer would be "over" all of them at once — a screen that had not read its
        /// safe area yet would highlight an arbitrary line. The draw layer refuses to paint in the
        /// same state, so the hit test has to refuse it too.
        /// </summary>
        private static bool HasPixels(Vector2 pixelsPerUnit)
        {
            return pixelsPerUnit.X > 0f && pixelsPerUnit.Y > 0f;
        }

        /// <summary>
        /// Distance from a point to a segment, both already in pixels, with the projection clamped
        /// to the segment.
        ///
        /// The clamp is the part that matters: without it the distance is the distance to the
        /// segment's infinite line, and the band around a line would extend past both of its ends —
        /// so a pointer a pixel beyond a port dot would "hover" the line it is not on, and the
        /// nearest line would win over the line actually under the pointer.
        /// </summary>
        private static float SegmentDistance(Vector2 a, Vector2 b, Vector2 point)
        {
            float abX = b.X - a.X;
            float abY = b.Y - a.Y;

            float apX = point.X - a.X;
            float apY = point.Y - a.Y;

            float lengthSquared = abX * abX + abY * abY;

            // A degenerate segment is a point: the answer is the distance to it, not a division by
            // zero. Routes drop zero-length segments, but a path built by a caller may not.
            float t = lengthSquared > 0f ? (apX * abX + apY * abY) / lengthSquared : 0f;
            if (t < 0f) t = 0f;
            else if (t > 1f) t = 1f;

            float dx = apX - abX * t;
            float dy = apY - abY * t;

            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private static float Length(Vector2 a, Vector2 b)
        {
            float dx = b.X - a.X;
            float dy = b.Y - a.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
