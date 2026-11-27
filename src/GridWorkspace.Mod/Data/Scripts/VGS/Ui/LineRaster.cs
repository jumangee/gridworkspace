using System;
using System.Collections.Generic;
using VRageMath;
using Vgs.Core;

namespace Vgs.Ui
{
    /// <summary>
    /// The client-area → pixel transform the draw path works in (ADR-0009, ADR-0032 §3).
    ///
    /// <b>Why a type and not two floats at every call site.</b> A line has to be rasterised in
    /// the same pixel space the grid borders are drawn in, and that space is not "client units
    /// times the window size": the safe-GUI rectangle is centred on the window and its origin is
    /// not the window's origin, so the transform is a scale <em>and</em> an offset per axis. The
    /// draw layer builds it once per frame (<c>VgsScreenDraw.PixelSpace</c>) from the values
    /// <see cref="GridFrames.CornerToApiPixels"/> already uses, and hands it to the rasteriser as
    /// a value — which is also what lets the rasteriser be checked offline with a transform the
    /// test chooses.
    ///
    /// <b>One unit is one device pixel, on both axes.</b> That is the whole point of the type:
    /// the layout's own axes do not share a scale (ADR-0009), so a "two pixel" line expressed in
    /// layout units comes out two pixels tall and <c>2 · height / width</c> wide. In this space a
    /// pixel is a pixel, and the thickness the rasteriser applies is the thickness the player
    /// sees.
    /// </summary>
    public struct PixelTransform
    {
        /// <summary>Client units to pixels, per axis: the client area's size in pixels.</summary>
        public readonly Vector2 Scale;

        /// <summary>Where the client area's origin lands in pixels: the safe area's own origin.</summary>
        public readonly Vector2 Offset;

        public PixelTransform(Vector2 scale, Vector2 offset)
        {
            Scale = scale;
            Offset = offset;
        }

        /// <summary>
        /// The transform from the same four values <see cref="GridFrames.CornerToApiPixels"/>
        /// takes, so that a line lands on the pixel grid the grid borders are snapped to.
        ///
        /// <b>Why this lives here and not in the draw layer.</b> The arithmetic is
        /// <c>pixel = client · clientPixels + apiOffset · safePixels</c>, and the second term is
        /// the one that is easy to get wrong: the safe-GUI rectangle is centred on the window
        /// (ADR-0009), so the pixel grid's origin is the safe area's left edge, not the window's.
        /// On a 16:9 window at 1080p that is 240 px, and a sign error there would shift every
        /// connection sideways while the frame around it stayed put — a defect no compile can
        /// show. Written once, in a type the offline gate compiles, the identity with
        /// <see cref="GridFrames.CornerToApiPixels"/> is asserted rather than reviewed.
        /// </summary>
        public static PixelTransform FromClientTransform(float apiScaleX, float apiOffsetX,
                                                         float clientWidthPixels,
                                                         float apiScaleY, float apiOffsetY,
                                                         float clientHeightPixels)
        {
            // safePixels = clientPixels / apiScale — the pixel grid the borders are snapped to
            // (GridFrames.CornerToApiPixels works it out the same way).
            float safeX = apiScaleX > 0f ? clientWidthPixels / apiScaleX : 0f;
            float safeY = apiScaleY > 0f ? clientHeightPixels / apiScaleY : 0f;

            return new PixelTransform(
                new Vector2(clientWidthPixels, clientHeightPixels),
                new Vector2(apiOffsetX * safeX, apiOffsetY * safeY));
        }

        /// <summary>
        /// True when the transform describes a real screen. False before the first
        /// <c>SyncScreenSpace</c>, when the safe area is unknown and every length would collapse
        /// to zero.
        /// </summary>
        public bool IsUsable
        {
            get { return Scale.X > 0f && Scale.Y > 0f; }
        }

        /// <summary>A client-area point in pixels. Value types only: no allocation.</summary>
        public Vector2 ToPixels(Vector2 point)
        {
            return new Vector2(point.X * Scale.X + Offset.X, point.Y * Scale.Y + Offset.Y);
        }
    }

    /// <summary>
    /// Rasterises a line into the axis-aligned rectangles the screen-space API can actually draw
    /// (ADR-0046, ADR-0047).
    ///
    /// <b>The API has no line, so a line is a row of dots.</b> Everything the editor draws is a
    /// textured quad with a colour: there is no line primitive, no arc, no dash and — the reason
    /// this type exists at all — <b>no antialiasing</b>. A "solid" two-pixel line is a rectangle
    /// whose edge is exactly one device pixel wide, and the eye reads that hard edge as a heavier
    /// line than the same weight drawn with a soft one: on a 1080p screen a solid 2 px line was
    /// reported as looking thicker than two pixels. A row of two-pixel squares with a one-pixel
    /// gap between them has the same ink height and the same nominal weight, but the eye averages
    /// the gaps into it and reads the line as lighter — the effect antialiasing would have given
    /// for free.
    ///
    /// <b>So every line in the editor is dotted, and only the rhythm differs.</b> A connection is
    /// <see cref="VgsConstants.LineDotPixels"/> of ink every
    /// <see cref="VgsConstants.LineDotPixels"/> + <see cref="VgsConstants.LineDotGapPixels"/> of
    /// length; a watch line is the same dot with a wider gap
    /// (<see cref="VgsConstants.WatchDotGapPixels"/>), which is what tells "this fires that" from
    /// "this watches that" (ADR-0017). One rasteriser serves both: the pattern is an argument,
    /// not a mode, so the two styles cannot drift apart.
    ///
    /// <b>The pattern is measured along the path, not along its segments.</b> The distance
    /// between two dots stays the same through a bend and a bend does not restart the rhythm —
    /// which is what makes a curved line read as one line rather than as a set of pieces.
    ///
    /// <b>Why the rasteriser is in this layer and not in the draw layer.</b> A gap between dots, a
    /// dot one pixel off, a rhythm that jumps at a bend — none of it is visible in a compile, and
    /// all of it is decidable from numbers. Keeping it here, with no reference to
    /// <c>Sandbox.*</c>, is what lets <c>tests/OfflineChecks.cs</c> assert it without the game.
    /// The draw layer only supplies the colour and the transform.
    /// </summary>
    public static class LineRaster
    {
        /// <summary>
        /// Upper bound on the dots of one line, so that a degenerate period cannot queue an
        /// unbounded number of quads in one frame. A line crosses at most the canvas, which at the
        /// shipped periods is a few hundred dots.
        /// </summary>
        private const int MaxDots = 4096;

        /// <summary>
        /// How far off the canvas a coordinate may be before it is clamped, in pixels. A line at
        /// a high zoom on a distant card can legitimately land hundreds of thousands of pixels
        /// outside the canvas; this is far beyond that and still small enough that the conversion
        /// to a whole pixel cannot overflow.
        /// </summary>
        private const float PixelLimit = 1000000f;

        /// <summary>
        /// Fills <paramref name="into"/> with whole-pixel squares placed along
        /// <paramref name="path"/> every <paramref name="dotPixels"/> +
        /// <paramref name="gapPixels"/> of its length.
        ///
        /// <paramref name="path"/> is in client-area units and <paramref name="transform"/>
        /// converts it; <paramref name="clipPixels"/> is <c>(left, top, right, bottom)</c> in the
        /// same pixel space, and every emitted rectangle lies inside it. The list is cleared
        /// first — the caller reuses one buffer for every line of every frame.
        ///
        /// The first dot is centred half a dot from the start, so the line begins with ink rather
        /// than with a gap, and the last one may be cut by the clip or by the end of the path.
        /// </summary>
        public static void Dotted(IList<Vector2> path, PixelTransform transform,
                                  float dotPixels, float gapPixels,
                                  Vector4 clipPixels, List<Vector4> into)
        {
            if (into == null) return;
            into.Clear();

            if (path == null || path.Count < 2) return;
            if (dotPixels <= 0f) return;
            if (gapPixels < 0f) gapPixels = 0f;
            if (!transform.IsUsable) return;
            if (clipPixels.Z <= clipPixels.X || clipPixels.W <= clipPixels.Y) return;

            float period = dotPixels + gapPixels;
            if (period <= 0f) return;

            float halfDot = dotPixels * 0.5f;
            float consumed = 0f;
            int index = 0;

            Vector2 previous = transform.ToPixels(path[0]);
            for (int i = 1; i < path.Count; i++)
            {
                Vector2 current = transform.ToPixels(path[i]);

                float dx = current.X - previous.X;
                float dy = current.Y - previous.Y;
                float length = (float)Math.Sqrt(dx * dx + dy * dy);
                if (length <= 0f) continue;

                float ux = dx / length;
                float uy = dy / length;

                // Dot positions come from the index rather than from an accumulator: `at +=
                // period` drifts in single precision, and the drift shows up as the last few
                // dots crowding together — on a line whose whole purpose is a rhythm, that reads
                // as a defect rather than as rounding.
                while (index < MaxDots)
                {
                    float at = index * period + halfDot - consumed;
                    if (at > length) break;

                    AddDot(previous.X + ux * at, previous.Y + uy * at, dotPixels, clipPixels, into);
                    index++;
                }

                consumed += length;
                previous = current;
            }
        }

        /// <summary>
        /// One dot: a square of the dot's size centred on the point, snapped so that its edges
        /// are whole pixels. The size is rounded once and added to the rounded edge, so a dot is
        /// always the same number of pixels wide and tall.
        ///
        /// A square rather than a disc because the line has no angle to correct for: the dot is
        /// axis-aligned like every other shape this layer can draw, and at two pixels a square and
        /// a disc are the same four pixels.
        /// </summary>
        private static void AddDot(float x, float y, float dotPixels, Vector4 clip,
                                   List<Vector4> into)
        {
            float size = (float)Math.Round(dotPixels);
            if (size < 1f) size = 1f;

            float left = ToPixel(x - size * 0.5f);
            float top = ToPixel(y - size * 0.5f);
            float right = left + size;
            float bottom = top + size;

            if (left < clip.X) left = clip.X;
            if (top < clip.Y) top = clip.Y;
            if (right > clip.Z) right = clip.Z;
            if (bottom > clip.W) bottom = clip.W;

            if (right <= left || bottom <= top) return;

            into.Add(new Vector4(left, top, right, bottom));
        }

        /// <summary>
        /// A coordinate rounded to a whole pixel, with a guard against a value so far off screen
        /// that the conversion to <c>int</c> is undefined. The guard is not a clip: a dot that
        /// leaves the canvas is clipped by <see cref="AddDot"/>, in the same pixel space. It only
        /// stops a card dragged a hundred thousand screens away from producing a rectangle at a
        /// wrapped-around coordinate — which would land inside the canvas and paint a dot that is
        /// not there.
        /// </summary>
        private static int ToPixel(float value)
        {
            if (value <= -PixelLimit) return (int)-PixelLimit;
            if (value >= PixelLimit) return (int)PixelLimit;
            return (int)Math.Round(value);
        }
    }
}
