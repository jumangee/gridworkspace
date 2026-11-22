using System;
using System.Collections.Generic;

namespace Vgs.IconAtlasBuilder
{
    /// <summary>
    /// Turns an icon's flattened contours into an anti-aliased alpha cell for the atlas.
    ///
    /// Two decisions here decide whether the drawn icon looks smooth or pixelated, and both are
    /// made at build time so the game only has to sample a texture:
    ///
    ///  * <b>The cell is filled by the icon's ink, not by its viewBox.</b> Font Awesome draws
    ///    each icon centred in a viewBox of a different width (512, 448, 384 — always 512 tall),
    ///    so fitting the viewBox would give three different optical sizes for three icons that
    ///    are meant to look alike. The ink box is scaled to the cell and centred, and the
    ///    resulting aspect ratio travels with the icon so the mod can draw it at the right
    ///    width.
    ///
    ///  * <b>Coverage is computed per sub-scanline with exact horizontal span coverage.</b> Each
    ///    pixel row is sampled <c>subSamples</c> times vertically and the overlap between a
    ///    filled span and a pixel is measured exactly, which is what a text rasteriser does and
    ///    what makes a 15-pixel ring read as a ring. Filling uses the non-zero winding rule, so
    ///    the hole in <c>circle-dot</c> stays a hole without any boolean geometry.
    /// </summary>
    public static class IconRaster
    {
        /// <summary>One rasterised icon: a square alpha cell plus the shape of its ink.</summary>
        public sealed class Cell
        {
            public int Size;
            public byte[] Alpha;

            /// <summary>
            /// Cell side ÷ ink height, in cell pixels. The cell is square and the ink is inset,
            /// so this is slightly above 1 and tells the caller how much bigger than the wanted
            /// ink height the square destination has to be.
            /// </summary>
            public float TextureScale;

            /// <summary>Ink box in viewBox units, kept for the build report.</summary>
            public InkBox Ink;
        }

        /// <summary>
        /// Rasterises <paramref name="shape"/> into a square cell of <paramref name="cellSize"/>
        /// pixels, with <paramref name="inset"/> transparent pixels on every side so that
        /// bilinear filtering and the mip chain cannot sample the neighbouring cell.
        /// </summary>
        public static Cell Render(IconShape shape, int cellSize, int inset, int subSamples)
        {
            if (shape == null) throw new ArgumentNullException("shape");
            if (cellSize < 4) cellSize = 4;
            if (subSamples < 1) subSamples = 1;
            if (inset < 0) inset = 0;

            InkBox ink = shape.MeasureInk();
            if (ink.Width <= 0f || ink.Height <= 0f)
                throw new InvalidOperationException("icon has no ink");

            float usable = cellSize - 2f * inset;
            if (usable <= 1f) throw new InvalidOperationException("inset leaves no room in the cell");

            float scale = Math.Min(usable / ink.Width, usable / ink.Height);
            float centreX = (ink.MinX + ink.MaxX) * 0.5f;
            float centreY = (ink.MinY + ink.MaxY) * 0.5f;
            float offsetX = cellSize * 0.5f - centreX * scale;
            float offsetY = cellSize * 0.5f - centreY * scale;

            // ---- edge list, in cell pixels ----
            var edgeX0 = new List<float>(1024);
            var edgeY0 = new List<float>(1024);
            var edgeX1 = new List<float>(1024);
            var edgeY1 = new List<float>(1024);

            for (int c = 0; c < shape.Contours.Count; c++)
            {
                List<Vec2> contour = shape.Contours[c];
                if (contour.Count < 2) continue;

                for (int i = 0; i < contour.Count; i++)
                {
                    Vec2 a = contour[i];
                    Vec2 b = contour[(i + 1) % contour.Count];

                    float ax = offsetX + a.X * scale;
                    float ay = offsetY + a.Y * scale;
                    float bx = offsetX + b.X * scale;
                    float by = offsetY + b.Y * scale;

                    if (ay == by) continue; // horizontal edges never produce a crossing

                    edgeX0.Add(ax);
                    edgeY0.Add(ay);
                    edgeX1.Add(bx);
                    edgeY1.Add(by);
                }
            }

            var cell = new Cell
            {
                Size = cellSize,
                Alpha = new byte[cellSize * cellSize],
                TextureScale = cellSize / (ink.Height * scale),
                Ink = ink
            };

            int edgeCount = edgeX0.Count;
            if (edgeCount == 0) return cell;

            var coverage = new float[cellSize * cellSize];
            var crossX = new float[edgeCount];
            var crossDir = new int[edgeCount];
            float invSamples = 1f / subSamples;

            for (int row = 0; row < cellSize; row++)
            {
                int rowOffset = row * cellSize;

                for (int s = 0; s < subSamples; s++)
                {
                    float y = row + (s + 0.5f) * invSamples;
                    int count = 0;

                    for (int e = 0; e < edgeCount; e++)
                    {
                        float y0 = edgeY0[e];
                        float y1 = edgeY1[e];

                        // Half-open rule: an edge owns its top end and not its bottom end, so a
                        // vertex shared by two edges is crossed exactly once.
                        bool up = y0 <= y && y < y1;
                        bool down = y1 <= y && y < y0;
                        if (!up && !down) continue;

                        float x0 = edgeX0[e];
                        float x1 = edgeX1[e];
                        float x = x0 + (y - y0) * (x1 - x0) / (y1 - y0);

                        int at = count;
                        while (at > 0 && crossX[at - 1] > x)
                        {
                            crossX[at] = crossX[at - 1];
                            crossDir[at] = crossDir[at - 1];
                            at--;
                        }

                        crossX[at] = x;
                        crossDir[at] = up ? 1 : -1;
                        count++;
                    }

                    if (count < 2) continue;

                    int winding = 0;
                    float spanStart = 0f;
                    bool inside = false;

                    for (int k = 0; k < count; k++)
                    {
                        int previous = winding;
                        winding += crossDir[k];

                        if (!inside && previous == 0 && winding != 0)
                        {
                            spanStart = crossX[k];
                            inside = true;
                        }
                        else if (inside && previous != 0 && winding == 0)
                        {
                            AccumulateSpan(coverage, rowOffset, cellSize, spanStart, crossX[k], invSamples);
                            inside = false;
                        }
                    }
                }
            }

            for (int i = 0; i < coverage.Length; i++)
            {
                float value = coverage[i];
                if (value <= 0f) continue;
                cell.Alpha[i] = value >= 1f ? (byte)255 : (byte)(value * 255f + 0.5f);
            }

            return cell;
        }

        private static void AccumulateSpan(float[] coverage, int rowOffset, int width,
                                           float x0, float x1, float weight)
        {
            if (x1 <= x0) return;

            if (x0 < 0f) x0 = 0f;
            if (x1 > width) x1 = width;
            if (x1 <= x0) return;

            int first = (int)Math.Floor(x0);
            int last = (int)Math.Ceiling(x1) - 1;

            if (first < 0) first = 0;
            if (last >= width) last = width - 1;

            for (int i = first; i <= last; i++)
            {
                float left = i > x0 ? i : x0;
                float right = i + 1 < x1 ? i + 1 : x1;
                if (right > left) coverage[rowOffset + i] += (right - left) * weight;
            }
        }
    }
}
