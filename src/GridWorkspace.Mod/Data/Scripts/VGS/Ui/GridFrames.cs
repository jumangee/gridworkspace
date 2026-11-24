using System;
using System.Collections.Generic;
using VRageMath;
using Vgs.Core;
using Vgs.Model;

namespace Vgs.Ui
{
    /// <summary>
    /// The box around the cards of one grid (ADR-0032).
    ///
    /// A grid frame is <em>derived</em>, like a group frame: it is a function of where the cards
    /// of that grid are, so it follows them. It is not a node, it cannot be selected, dragged or
    /// connected to — a grid is not a thing the game can act on as a whole, and offering it as an
    /// object would be a lie about what a click does.
    ///
    /// <b>Why a list of these instead of a field somewhere.</b> A grid has no node to hang the
    /// rectangle on: there is one node per block. The rectangles are therefore computed once per
    /// layout pass and handed to the draw pass, which is also what keeps the arithmetic out of
    /// the per-frame path.
    /// </summary>
    public struct GridFrame
    {
        /// <summary>Entity id of the grid this frame encloses.</summary>
        public readonly long GridEntityId;

        /// <summary>Top-left corner in graph units.</summary>
        public readonly Vector2 TopLeft;

        /// <summary>Size in graph units.</summary>
        public readonly Vector2 Size;

        public GridFrame(long gridEntityId, Vector2 topLeft, Vector2 size)
        {
            GridEntityId = gridEntityId;
            TopLeft = topLeft;
            Size = size;
        }

        /// <summary>True when a graph-space point is inside the frame's box.</summary>
        public bool Contains(Vector2 point)
        {
            return point.X >= TopLeft.X && point.Y >= TopLeft.Y
                   && point.X <= TopLeft.X + Size.X && point.Y <= TopLeft.Y + Size.Y;
        }
    }

    /// <summary>
    /// Computes the frame of every grid in the graph (ADR-0032).
    ///
    /// <b>Which cards belong to which grid.</b> <see cref="Node.GridEntityId"/>, set by the graph
    /// builder from the block's own grid. Cards with 0 — a group, or a block whose grid could not
    /// be read — belong to no frame and are skipped: guessing a grid for them would draw a border
    /// around cards that are not necessarily together.
    ///
    /// <b>The padding is larger than a group's.</b> A grid frame encloses cards *and* the group
    /// frames around them, so it must clear the group padding plus its title strip, or the two
    /// borders would cross each other and read as a drawing defect rather than as nesting.
    ///
    /// <b>Sorted by grid id.</b> The order the nodes happen to be in is the terminal system's, and
    /// a frame drawn in a different order from one refresh to the next would flicker between two
    /// overlapping borders. Sorting costs one comparison per grid and removes the whole class.
    /// </summary>
    public static class GridFrames
    {
        /// <summary>
        /// Recomputes <paramref name="into"/> from the current card positions. The list is
        /// cleared and refilled, so the caller can reuse one buffer for the lifetime of the
        /// screen and the draw path allocates nothing.
        /// </summary>
        public static void Layout(GridGraph graph, List<GridFrame> into)
        {
            if (into == null) return;
            into.Clear();
            if (graph == null) return;

            // One pass to find the bounds per grid. The number of grids on a construct is small
            // (a ship with a dozen subgrids is already unusual), so a linear scan per new grid id
            // is cheaper than a dictionary plus its enumerator — and it allocates nothing.
            IList<Node> nodes = graph.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                Node node = nodes[i];
                if (node == null || node.IsGroup) continue;
                if (node.GridEntityId == 0) continue;

                Vector2 topLeft, size;
                if (!PortGeometry.TryBodyRect(node, out topLeft, out size)) continue;

                float right = topLeft.X + size.X;
                float bottom = topLeft.Y + size.Y;

                int index = IndexOf(into, node.GridEntityId);
                if (index < 0)
                {
                    into.Add(new GridFrame(node.GridEntityId, topLeft, size));
                    continue;
                }

                GridFrame frame = into[index];
                float minX = frame.TopLeft.X;
                float minY = frame.TopLeft.Y;
                float maxX = minX + frame.Size.X;
                float maxY = minY + frame.Size.Y;

                if (topLeft.X < minX) minX = topLeft.X;
                if (topLeft.Y < minY) minY = topLeft.Y;
                if (right > maxX) maxX = right;
                if (bottom > maxY) maxY = bottom;

                into[index] = new GridFrame(node.GridEntityId,
                                            new Vector2(minX, minY),
                                            new Vector2(maxX - minX, maxY - minY));
            }

            float pad = VgsConstants.GridFramePadding;
            for (int i = 0; i < into.Count; i++)
            {
                GridFrame frame = into[i];
                into[i] = new GridFrame(
                    frame.GridEntityId,
                    new Vector2(frame.TopLeft.X - pad, frame.TopLeft.Y - pad),
                    new Vector2(frame.Size.X + pad * 2f, frame.Size.Y + pad * 2f));
            }

            SortByGrid(into);
        }

        /// <summary>
        /// The frame of one grid, or false when that grid has no cards on the canvas. Used by the
        /// checks and by anything that needs one box without laying out all of them.
        /// </summary>
        public static bool TryGet(GridGraph graph, long gridEntityId,
                                  out Vector2 topLeft, out Vector2 size)
        {
            topLeft = Vector2.Zero;
            size = Vector2.Zero;
            if (graph == null || gridEntityId == 0) return false;

            bool any = false;
            float minX = 0f, minY = 0f, maxX = 0f, maxY = 0f;

            IList<Node> nodes = graph.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                Node node = nodes[i];
                if (node == null || node.IsGroup) continue;
                if (node.GridEntityId != gridEntityId) continue;

                Vector2 cardTopLeft, cardSize;
                if (!PortGeometry.TryBodyRect(node, out cardTopLeft, out cardSize)) continue;

                float right = cardTopLeft.X + cardSize.X;
                float bottom = cardTopLeft.Y + cardSize.Y;

                if (!any)
                {
                    minX = cardTopLeft.X;
                    minY = cardTopLeft.Y;
                    maxX = right;
                    maxY = bottom;
                    any = true;
                    continue;
                }

                if (cardTopLeft.X < minX) minX = cardTopLeft.X;
                if (cardTopLeft.Y < minY) minY = cardTopLeft.Y;
                if (right > maxX) maxX = right;
                if (bottom > maxY) maxY = bottom;
            }

            if (!any) return false;

            float pad = VgsConstants.GridFramePadding;
            topLeft = new Vector2(minX - pad, minY - pad);
            size = new Vector2(maxX - minX + pad * 2f, maxY - minY + pad * 2f);
            return true;
        }

        /// <summary>
        /// Converts one corner coordinate from client-area units to a whole <b>API pixel</b>
        /// (ADR-0032).
        ///
        /// <b>Corners, not lengths, and that is the whole point.</b> A border is four rectangles
        /// that must tile exactly: the vertical edges have to start on the same pixel row the top
        /// edge ends on, and stop on the same row the bottom edge starts on. Snapping each edge
        /// independently — which an earlier version did — rounds the same shared boundary twice,
        /// and the two roundings disagree by a pixel. The result is a border with a one-pixel
        /// spur sticking out of a corner, at every zoom, which is exactly how it was reported.
        ///
        /// Snapping the corners instead means the shared boundary is computed once and reused, so
        /// the four rectangles cannot disagree: tiling is a property of the construction rather
        /// than something the numbers happen to satisfy.
        ///
        /// <b>Why not in client units.</b> The conversion adds an offset (the side bars around the
        /// safe area), so a value rounded to a whole client pixel lands on a fraction of an API
        /// pixel.
        ///
        /// <b>Why not the API value itself.</b> The drawing API takes a normalised coordinate, 0..1
        /// across the safe area. Rounding *that* rounds to whole screens: an early version
        /// collapsed every border into a single corner pixel this way. The pixel grid lives one
        /// step further out, at <c>safePixels = clientPixels / apiScale</c>.
        /// </summary>
        public static float CornerToApiPixels(float apiScale, float apiOffset, float clientPixels,
                                              float clientCoord)
        {
            if (clientPixels <= 0f || apiScale <= 0f) return 0f;

            float safePixels = clientPixels / apiScale;
            if (safePixels <= 0f) return 0f;

            float api = clientCoord * apiScale + apiOffset;
            return (float)Math.Round(api * safePixels);
        }

        /// <summary>
        /// API pixels back to the normalised coordinate the drawing API expects.
        /// </summary>
        public static float ApiPixelsToApi(float pixels, float apiScale, float clientPixels)
        {
            if (clientPixels <= 0f || apiScale <= 0f) return 0f;

            float safePixels = clientPixels / apiScale;
            return safePixels <= 0f ? 0f : pixels / safePixels;
        }

        /// <summary>
        /// Appends the four edge rectangles of a border, in API pixels, as
        /// <c>(left, top, right, bottom)</c> tuples (ADR-0032).
        ///
        /// <b>All four edges are derived from the same four corners, and that is the point.</b>
        /// Snapping each edge independently — which an earlier version did — rounds a shared
        /// boundary twice, and the two roundings can disagree by a pixel. The visible result is a
        /// one-pixel spur sticking out of a corner, at every zoom, which is exactly how it was
        /// reported. Here the vertical edges start on the row the top edge ends on and stop on the
        /// row the bottom edge starts on, by construction, not by luck.
        ///
        /// The caller passes corners that are already whole pixels, so no rounding happens here at
        /// all: this function is pure arithmetic on integers, which is why it can be checked
        /// offline against the "no gap, no overshoot" property directly.
        /// </summary>
        public static void AppendBorderEdges(List<Vector4> into,
                                             float left, float top, float right, float bottom,
                                             float thickness)
        {
            if (into == null) return;
            if (right <= left || bottom <= top) return;
            if (thickness <= 0f) return;

            // A box thinner than two edges has no interior: one solid rectangle rather than a
            // guess about which edge wins.
            if (right - left <= thickness * 2f || bottom - top <= thickness * 2f)
            {
                into.Add(new Vector4(left, top, right, bottom));
                return;
            }

            float innerTop = top + thickness;
            float innerBottom = bottom - thickness;

            // Top and bottom span the full width; left and right fit exactly between them. They
            // therefore tile the outline once: no gap at a corner, and no overlap that would make
            // the corner twice as opaque as the edges.
            into.Add(new Vector4(left, top, right, innerTop));
            into.Add(new Vector4(left, innerBottom, right, bottom));
            into.Add(new Vector4(left, innerTop, left + thickness, innerBottom));
            into.Add(new Vector4(right - thickness, innerTop, right, innerBottom));
        }

        private static int IndexOf(List<GridFrame> frames, long gridEntityId)        {
            for (int i = 0; i < frames.Count; i++)
            {
                if (frames[i].GridEntityId == gridEntityId) return i;
            }
            return -1;
        }

        /// <summary>
        /// Insertion sort by grid id. The list is one entry per grid — a handful even on a large
        /// construct — so this avoids a comparer allocation on a path that runs after every drag.
        /// </summary>
        private static void SortByGrid(List<GridFrame> frames)
        {
            for (int i = 1; i < frames.Count; i++)
            {
                GridFrame current = frames[i];
                int j = i - 1;
                while (j >= 0 && frames[j].GridEntityId > current.GridEntityId)
                {
                    frames[j + 1] = frames[j];
                    j--;
                }
                frames[j + 1] = current;
            }
        }
    }
}
