using System;
using System.Collections.Generic;
using VRageMath;

namespace Vgs.IconProto
{
    /// <summary>
    /// Turns flattened icon contours into an anti-aliased alpha mask of an exact pixel size.
    ///
    /// This is deliberately not "render once, scale later": the mask is rasterised at the size
    /// it will be drawn at, so every edge is anti-aliased against the real pixel grid and no
    /// resampling ever touches the result. That is the whole point of the exercise — a baked
    /// bitmap would have to be scaled, and scaling is what makes an icon look pixelated.
    ///
    /// Coverage is computed per sub-scanline with exact horizontal span coverage: each pixel row
    /// is sampled <c>subSamples</c> times vertically, and within a sub-scanline the overlap
    /// between a filled span and a pixel is measured exactly. The result is the quality of a
    /// normal text rasteriser (smooth curves, no staircase) at a fraction of the cost of a full
    /// analytic area rasteriser. Filling follows the non-zero winding rule, which is what makes
    /// the hole in <c>circle-dot</c> a hole instead of a second disc.
    /// </summary>
    public static class IconRaster
    {
        /// <summary>
        /// Rasterises <paramref name="shape"/> into a square mask of <paramref name="boxPx"/>
        /// pixels. The viewBox is fitted into the square and centred, so icons with different
        /// aspect ratios keep their proportions (Font Awesome is always 512 units tall and
        /// narrower than that, which is why fitting by height is the right default).
        /// </summary>
        public static byte[] RenderAlpha(IconShape shape, int boxPx, int subSamples)
        {
            if (shape == null) throw new ArgumentNullException("shape");
            if (boxPx < 1) boxPx = 1;
            if (subSamples < 1) subSamples = 1;

            // ---- transform viewBox units to pixels (fit, centred) ----
            float scale = Math.Min(boxPx / shape.ViewWidth, boxPx / shape.ViewHeight);
            float offsetX = (boxPx - shape.ViewWidth * scale) * 0.5f;
            float offsetY = (boxPx - shape.ViewHeight * scale) * 0.5f;

            // ---- build the edge list once ----
            var edgeX0 = new List<float>(1024);
            var edgeY0 = new List<float>(1024);
            var edgeX1 = new List<float>(1024);
            var edgeY1 = new List<float>(1024);

            for (int c = 0; c < shape.Contours.Count; c++)
            {
                List<Vector2> contour = shape.Contours[c];
                if (contour.Count < 2) continue;

                for (int i = 0; i < contour.Count; i++)
                {
                    Vector2 a = contour[i];
                    Vector2 b = contour[(i + 1) % contour.Count];

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

            int edgeCount = edgeX0.Count;
            var coverage = new float[boxPx * boxPx];

            if (edgeCount == 0) return new byte[boxPx * boxPx];

            var crossX = new float[edgeCount];
            var crossDir = new int[edgeCount];
            float invSamples = 1f / subSamples;

            // ---- scanline coverage ----
            for (int row = 0; row < boxPx; row++)
            {
                int rowOffset = row * boxPx;

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

                        // insertion sort: crossing counts per scanline are tiny (2..40)
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
                            AccumulateSpan(coverage, rowOffset, boxPx, spanStart, crossX[k], invSamples);
                            inside = false;
                        }
                    }
                }
            }

            var alpha = new byte[boxPx * boxPx];
            for (int i = 0; i < coverage.Length; i++)
            {
                float value = coverage[i];
                if (value <= 0f) continue;
                if (value >= 1f) { alpha[i] = 255; continue; }
                alpha[i] = (byte)(value * 255f + 0.5f);
            }

            return alpha;
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

        /// <summary>
        /// Renders a white RGBA8 texture with straight (not premultiplied) alpha, ready for
        /// <c>IMyGeneratedTexture.EndUpdate</c>. The sprite shader premultiplies on its own, so
        /// storing premultiplied bytes here would darken every edge.
        /// </summary>
        public static byte[] RenderWhiteRgba(IconShape shape, int boxPx, int subSamples)
        {
            byte[] alpha = RenderAlpha(shape, boxPx, subSamples);
            var rgba = new byte[boxPx * boxPx * 4];

            for (int i = 0; i < alpha.Length; i++)
            {
                int p = i * 4;
                rgba[p] = 255;
                rgba[p + 1] = 255;
                rgba[p + 2] = 255;
                rgba[p + 3] = alpha[i];
            }

            return rgba;
        }
    }
}
