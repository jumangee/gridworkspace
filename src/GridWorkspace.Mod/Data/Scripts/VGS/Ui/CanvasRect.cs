using VRageMath;
using Vgs.Core;

namespace Vgs.Ui
{
    /// <summary>
    /// The canvas rectangle — everything between the header and the bottom bar — and the one
    /// predicate that answers "is this box on the canvas?" (R-015).
    ///
    /// <b>Why the rectangle and the predicate are one type.</b> They are one question asked from
    /// four places — the draw pass, the state pass, the grid frames and the grid tabs — and the
    /// last time they disagreed the disagreement was invisible in the code: the predicate
    /// compared an <em>absolute</em> screen coordinate against the canvas <em>size</em>. The
    /// canvas does not start at the window's origin (the header sits above it), so that
    /// comparison cut a card one header height before it reached the canvas's bottom edge: on a
    /// 1200 px window the card vanished with about 66 px of canvas still below it, which is
    /// exactly how it was reported. Nothing above or beside the canvas was affected, because
    /// the canvas starts at x = 0 and its width is the window's width.
    ///
    /// So the rectangle and the predicate live together, in canvas-local coordinates, and the
    /// arithmetic is checked offline (`tests/OfflineChecks.cs`, "canvas culling").
    ///
    /// <b>Coordinates.</b> The editor's layout is in client-area units, 0..1 across the window:
    /// the canvas's top-left is (0, <see cref="VgsConstants.HeaderHeight"/>) and its size is the
    /// window minus both bars. A box passed to <see cref="Overlaps"/> is in the same units, with
    /// the same origin.
    /// </summary>
    public static class CanvasRect
    {
        /// <summary>
        /// The canvas rectangle in client-area coordinates. Recomputed rather than cached: the
        /// window can be resized while the editor is open, and both inputs are constants.
        /// </summary>
        public static void Get(out Vector2 topLeft, out Vector2 size)
        {
            topLeft = new Vector2(0f, VgsConstants.HeaderHeight);
            size = new Vector2(1f, 1f - VgsConstants.HeaderHeight - VgsConstants.BottomBarHeight);
        }

        /// <summary>
        /// True when <paramref name="boxTopLeft"/>/<paramref name="boxSize"/> overlaps the
        /// canvas, i.e. when any part of it would be visible. A box that merely touches an edge
        /// covers no pixel and counts as outside.
        ///
        /// Both arguments are in client-area coordinates. The box is moved into canvas-local
        /// space before it is compared with the canvas size — the step whose absence was R-015.
        /// </summary>
        public static bool Overlaps(Vector2 canvasTopLeft, Vector2 canvasSize,
                                    Vector2 boxTopLeft, Vector2 boxSize)
        {
            float left = boxTopLeft.X - canvasTopLeft.X;
            float top = boxTopLeft.Y - canvasTopLeft.Y;

            if (left > canvasSize.X || top > canvasSize.Y) return false;
            if (left + boxSize.X < 0f || top + boxSize.Y < 0f) return false;
            return true;
        }
    }
}
