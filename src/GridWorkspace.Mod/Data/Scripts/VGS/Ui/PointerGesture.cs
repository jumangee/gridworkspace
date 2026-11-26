using System;
using VRageMath;
using Vgs.Core;

namespace Vgs.Ui
{
    /// <summary>
    /// The one decision that separates a click from a drag, in device pixels (ADR-0041).
    ///
    /// <b>Why it is its own type.</b> The editor uses the same right button for two things — a
    /// click on a card opens the manual action menu, a drag pans the canvas — so "did the pointer
    /// move?" decides between a menu and a camera move. Getting it wrong is silent in both
    /// directions: too tight and no menu ever opens, because the hand always moves a pixel; too
    /// loose and a short pan opens a menu instead of moving the world. That is exactly the kind
    /// of rule this project checks offline instead of judging in the game, and it is a pure
    /// function of a delta and the window size, so it can be.
    ///
    /// <b>Pixels, not layout units, and not a distance.</b> One X unit of this layout is the
    /// window width and one Y unit is its height (ADR-0009), so the two axes are different
    /// lengths on any non-square window: a single threshold in layout units is a different
    /// physical distance per axis, and the length of a mixed vector is a number with no meaning
    /// at all. Converting each axis to pixels first makes the allowance the same physical
    /// movement in every direction, whatever the window's shape — which is what "the hand did not
    /// move" actually means.
    /// </summary>
    public static class PointerGesture
    {
        /// <summary>
        /// True when the pointer moved far enough between the press and this frame to count as a
        /// drag rather than a click.
        ///
        /// Each axis is compared to <see cref="VgsConstants.ClickSlopPixels"/> separately, so the
        /// allowance is a square in screen pixels. A diagonal movement may therefore travel up to
        /// the diagonal of that square, which is the honest reading of "did not move": the strict
        /// alternative — Euclidean distance in pixels — would reject a deliberate one-pixel step
        /// up and one to the right as a drag.
        ///
        /// <paramref name="clientWidth"/> and <paramref name="clientHeight"/> are the window's
        /// size in pixels, as <see cref="VgsScreenDraw"/> reports it. Before the screen has been
        /// measured they are zero; the comparison then falls back to the same allowance expressed
        /// in layout units, which keeps the answer defined instead of treating an unmeasured
        /// window as "did not move" and blocking the pan forever.
        /// </summary>
        public static bool ExceedsClickSlop(Vector2 delta, float clientWidth, float clientHeight)
        {
            if (clientWidth > 0f && clientHeight > 0f)
            {
                return Math.Abs(delta.X) * clientWidth > VgsConstants.ClickSlopPixels
                    || Math.Abs(delta.Y) * clientHeight > VgsConstants.ClickSlopPixels;
            }

            return Math.Abs(delta.X) > VgsConstants.ClickSlop
                || Math.Abs(delta.Y) > VgsConstants.ClickSlop;
        }
    }
}