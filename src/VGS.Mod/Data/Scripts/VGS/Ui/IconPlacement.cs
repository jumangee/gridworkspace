using System;
using VRageMath;

namespace Vgs.Ui
{
    /// <summary>
    /// Where an atlas icon lands on screen, and how it is clipped to the canvas.
    ///
    /// This lives in its own type, away from the draw call, for one reason: it is the only part
    /// of icon drawing that can be wrong in a way a human would not notice until the icon is in
    /// the wrong place, and it is decidable without the game. `offline-check.ps1` asserts it
    /// (ADR-0019).
    ///
    /// Three conversions happen here, and each is a way to get it wrong:
    ///
    ///  * <b>Layout units to window pixels.</b> The layout is normalised: one X unit is the
    ///    window's width, one Y unit its height. Icons are drawn with `DrawPixels`, which takes
    ///    window pixels and converts nothing (unlike `Draw`, which converts from the safe-GUI
    ///    4:3 box). Feeding it normalised coordinates shifts every icon by the width of the side
    ///    bars on a 16:9 screen.
    ///  * <b>Fractional to whole pixels.</b> A sprite drawn on a half-pixel boundary is
    ///    resampled by the filter, which is exactly the blur the atlas exists to avoid.
    ///  * <b>Destination to source.</b> When a card scrolls under the header, the destination is
    ///    cut to the canvas and the source must be cut by the same fraction, or the icon slides
    ///    inside its own box.
    /// </summary>
    public static class IconPlacement
    {
        /// <summary>
        /// Computes the pixel rectangle for an icon whose ink should be
        /// <paramref name="inkHeight"/> layout units tall, centred on <paramref name="center"/>.
        ///
        /// <paramref name="textureScale"/> is how much larger the texture is than the ink inside
        /// it (1 for an icon whose ink fills the texture vertically, which is the normal case for
        /// Font Awesome). The texture is square, so the destination is square too; the ink inside
        /// it keeps its own proportions.
        ///
        /// Returns false when the icon is degenerate, when the client size is not known yet, or
        /// when the icon falls entirely outside <paramref name="clipSize"/> — in all three cases
        /// there is nothing to draw and the caller should not enqueue a sprite.
        /// </summary>
        public static bool TryPlace(Vector2 center, float inkHeight, float textureScale,
                                    float clientWidthPx, float clientHeightPx,
                                    Vector2 clipTopLeft, Vector2 clipSize,
                                    out Rectangle destination, out Rectangle clip)
        {
            destination = new Rectangle(0, 0, 0, 0);
            clip = new Rectangle(0, 0, 0, 0);

            if (inkHeight <= 0f || textureScale <= 0f) return false;
            if (clientWidthPx <= 0f || clientHeightPx <= 0f) return false;

            int side = (int)Math.Round(inkHeight * textureScale * clientHeightPx);
            if (side < 1) return false;

            int centreX = (int)Math.Round(center.X * clientWidthPx);
            int centreY = (int)Math.Round(center.Y * clientHeightPx);

            destination = new Rectangle(centreX - side / 2, centreY - side / 2, side, side);
            clip = ToPixels(clipTopLeft, clipSize, clientWidthPx, clientHeightPx);

            return Intersects(destination, clip);
        }

        /// <summary>
        /// Cuts a destination rectangle to a clipping rectangle and cuts the source by the same
        /// fraction, so the visible part of the icon stays where it was.
        ///
        /// Returns false when nothing of the icon is left; the caller must not draw in that case,
        /// because a zero-width rectangle is not "nothing" to every renderer.
        /// </summary>
        public static bool TryClip(Rectangle destination, Rectangle source, Rectangle clip,
                                   out Rectangle clippedDestination, out Rectangle clippedSource)
        {
            clippedDestination = new Rectangle(0, 0, 0, 0);
            clippedSource = new Rectangle(0, 0, 0, 0);

            if (destination.Width <= 0 || destination.Height <= 0) return false;
            if (source.Width <= 0 || source.Height <= 0) return false;

            int left = Math.Max(destination.X, clip.X);
            int top = Math.Max(destination.Y, clip.Y);
            int right = Math.Min(destination.X + destination.Width, clip.X + clip.Width);
            int bottom = Math.Min(destination.Y + destination.Height, clip.Y + clip.Height);

            if (right <= left || bottom <= top) return false;

            int cutLeft = left - destination.X;
            int cutTop = top - destination.Y;
            int cutRight = destination.X + destination.Width - right;
            int cutBottom = destination.Y + destination.Height - bottom;

            int sourceX = source.X + Scale(cutLeft, source.Width, destination.Width);
            int sourceY = source.Y + Scale(cutTop, source.Height, destination.Height);
            int sourceRight = source.X + source.Width - Scale(cutRight, source.Width, destination.Width);
            int sourceBottom = source.Y + source.Height - Scale(cutBottom, source.Height, destination.Height);

            if (sourceRight <= sourceX || sourceBottom <= sourceY) return false;

            clippedDestination = new Rectangle(left, top, right - left, bottom - top);
            clippedSource = new Rectangle(sourceX, sourceY, sourceRight - sourceX, sourceBottom - sourceY);
            return true;
        }

        /// <summary>
        /// A layout rectangle in window pixels. Rounded outward: a clip is a region that must not
        /// shrink, or the outermost row of a scrolled card would lose a pixel it should keep.
        /// </summary>
        public static Rectangle ToPixels(Vector2 topLeft, Vector2 size,
                                         float clientWidthPx, float clientHeightPx)
        {
            if (clientWidthPx <= 0f || clientHeightPx <= 0f) return new Rectangle(0, 0, 0, 0);

            int x = (int)Math.Floor(topLeft.X * clientWidthPx);
            int y = (int)Math.Floor(topLeft.Y * clientHeightPx);
            int right = (int)Math.Ceiling((topLeft.X + size.X) * clientWidthPx);
            int bottom = (int)Math.Ceiling((topLeft.Y + size.Y) * clientHeightPx);

            if (right < x) right = x;
            if (bottom < y) bottom = y;

            return new Rectangle(x, y, right - x, bottom - y);
        }

        private static int Scale(int value, int sourceExtent, int destinationExtent)
        {
            if (destinationExtent <= 0) return 0;
            int scaled = (int)Math.Round(value * (double)sourceExtent / destinationExtent);
            if (scaled < 0) return 0;
            if (scaled > sourceExtent) return sourceExtent;
            return scaled;
        }

        private static bool Intersects(Rectangle a, Rectangle b)
        {
            if (a.Width <= 0 || a.Height <= 0) return false;
            if (b.Width <= 0 || b.Height <= 0) return false;

            return a.X < b.X + b.Width && b.X < a.X + a.Width
                && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;
        }
    }
}
