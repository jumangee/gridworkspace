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
    }
}
