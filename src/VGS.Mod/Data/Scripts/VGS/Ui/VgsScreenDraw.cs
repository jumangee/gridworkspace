using System;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using Vgs.Core;

namespace Vgs.Ui
{
    /// <summary>
    /// Guarded wrapper over the screen-space drawing API a mod script is allowed to call.
    ///
    /// Why this type exists at all:
    ///
    ///  * <b>The engine's own GUI is off limits.</b> A mod ships only <c>.cs</c> files; the
    ///    game compiles them itself and rejects every member that is not on its IL
    ///    whitelist. <c>Sandbox.Graphics.*</c> — <c>MyGuiScreenBase</c>, <c>MyGuiManager</c>,
    ///    <c>MyScreenManager</c>, <c>MyGuiControlButton</c> — is not on that whitelist, and
    ///    <c>Sandbox.Graphics.dll</c> has no whitelist section at all. The supported way to
    ///    draw from a mod is <c>Sandbox.ModAPI.MyScreenSpace</c> and its Text/Sprite
    ///    companions, which live in <c>Sandbox.Game.dll</c> under the fully allowed
    ///    <c>Sandbox.ModAPI.*</c> namespace.
    ///
    ///  * <b>Everything here is called from a draw path.</b> The editor repaints every frame
    ///    while it is open, so no method may allocate per call and no exception may escape:
    ///    a broken draw would otherwise throw sixty times a second.
    ///
    ///  * <b>Filled rectangles need a texture.</b> The sprite API draws textured quads, not
    ///    coloured ones, so a plain white texture is generated once (1x1 scaled up) and
    ///    tinted per rectangle. <c>IMyGeneratedTextures</c> is whitelisted; a content
    ///    texture is tried as a fallback if the platform does not support generation.
    ///
    ///  * <b>Two coordinate spaces, bridged here.</b> The editor lays out in client-area
    ///    coordinates (0..1 of the window), but <c>MyScreenSpace*</c> resolves 0..1 against the
    ///    safe-GUI rectangle — a 4:3 box centred on a wide monitor. Every primitive below
    ///    converts through <see cref="SyncScreenSpace"/>'s transform, so callers never have to
    ///    know the difference. See ADR-0009.
    /// </summary>
    public static class VgsScreenDraw
    {
        /// <summary>
        /// Layer the editor paints into. It is a modal, full-screen screen, so its content
        /// must land above the in-game HUD.
        /// </summary>
        private const MyScreenSpaceLayer Layer = MyScreenSpaceLayer.AboveHud;

        private static readonly MyGuiDrawAlignEnum AlignTopLeft =
            MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP;

        private static readonly MyGuiDrawAlignEnum AlignCenter =
            MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER;

        /// <summary>
        /// Fonts tried in order; the first one the game reports as available wins. The names
        /// are the ones shipped in <c>Content\Fonts</c>; the capitalised form is what the game
        /// itself passes to the GUI text calls.
        /// </summary>
        private static readonly string[] FontCandidates = { "White", "white", "Monospace", "Debug" };

        private static IMyGeneratedTexture _solidTexture;
        private static MyStringId _solidMaterial;
        private static bool _solidReady;

        private static string _font;
        private static bool _resourcesReady;
        private static bool _resourcesFailed;

        // Affine mapping from client-area coordinates (what the layout uses) to the space
        // MyScreenSpace* really draws in: the safe-GUI rectangle, a 4:3 box centred on any
        // monitor wider than 4:3. Refreshed once per draw and once per input pass by
        // SyncScreenSpace; ADR-0009 explains why the two spaces are not the same one.
        private static float _apiScaleX = 1f;   // fullWidth / safeWidth
        private static float _apiOffsetX;       // -safe.Left / safeWidth
        private static float _apiScaleY = 1f;
        private static float _apiOffsetY;

        /// <summary>True once <see cref="EnsureResources"/> found a usable font and brush.</summary>
        public static bool ResourcesReady
        {
            get { return _resourcesReady; }
        }

        /// <summary>
        /// Resolves the font and the solid brush. Called once when the screen opens; safe to
        /// call again after <see cref="Release"/>.
        /// </summary>
        public static void EnsureResources()
        {
            if (_resourcesReady || _resourcesFailed) return;

            // The font and the brush are prepared independently on purpose: a platform that
            // refuses to generate a texture must still get a usable editor, and a bad font
            // name must not cost the rectangles. Only a failure of both leaves nothing to
            // draw with, and that is reported rather than silently opening a black screen.
            ResolveFont();
            EnsureSolidBrush();

            if (_font == null && !_solidReady)
            {
                _resourcesFailed = true;
                VgsLog.Error("could not prepare screen resources: no font and no brush");
                return;
            }

            _resourcesReady = true;
            VgsLog.Info("editor resources ready (font '" + (_font ?? "none") + "', solid brush " +
                        (_solidReady ? "ready" : "unavailable") + ")");
        }

        private static void ResolveFont()
        {
            try
            {
                for (int i = 0; i < FontCandidates.Length; i++)
                {
                    if (MyScreenSpaceText.IsFontAvailable(FontCandidates[i]))
                    {
                        _font = FontCandidates[i];
                        return;
                    }
                }

                // Keep drawing: text in an unknown font is better than a blank editor, and
                // the log line says which names were rejected.
                _font = FontCandidates[0];
                VgsLog.Warn("no known screen font is available; trying '" + _font + "'");
            }
            catch (Exception e)
            {
                VgsLog.Error("font resolution failed; labels will be skipped", e);
            }
        }

        private static void EnsureSolidBrush()
        {
            if (_solidReady) return;

            // Generation is the good path; a content texture is the fallback. Each is guarded
            // separately so an exception in one cannot skip the other.
            try
            {
                IMyGeneratedTextures generated = MyAPIGateway.GeneratedTextures;
                if (generated != null && generated.IsSupported)
                {
                    _solidTexture = generated.Create("VGS.Solid", new Vector2I(4, 4), MyGeneratedTextureFormat.Srgb);
                    if (_solidTexture != null)
                    {
                        byte[] pixels = _solidTexture.BeginUpdate();
                        if (pixels != null)
                        {
                            // White in every channel, so the per-rectangle colour tint decides
                            // the visible colour; the channel order then does not matter.
                            for (int i = 0; i < pixels.Length; i++) pixels[i] = 255;
                            _solidTexture.EndUpdate(pixels);
                            _solidMaterial = _solidTexture.MaterialId;
                            _solidReady = true;
                            return;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                VgsLog.Error("could not generate the solid brush; trying a content texture", e);
                _solidTexture = null;
            }

            // Fallback: a content texture the sprite API can resolve. The name differs between
            // builds, so it is probed rather than assumed.
            try
            {
                MyStringId fallback = MyStringId.GetOrCompute("SquareSimple");
                if (MyScreenSpaceSprite.IsMaterialAvailable(fallback))
                {
                    _solidMaterial = fallback;
                    _solidReady = true;
                    return;
                }
            }
            catch (Exception e)
            {
                VgsLog.Error("could not resolve a fallback brush", e);
            }

            VgsLog.Warn("no solid brush available: filled rectangles will be skipped");
        }

        /// <summary>Releases the generated texture. Called when the session unloads.</summary>
        public static void Release()
        {
            try
            {
                if (_solidTexture != null) _solidTexture.Dispose();
            }
            catch (Exception e)
            {
                VgsLog.Error("could not release the solid brush", e);
            }

            _solidTexture = null;
            _solidReady = false;
            _font = null;
            _resourcesReady = false;
            _resourcesFailed = false;

            // Nothing is on screen after a release, so the next session starts from the
            // identity transform rather than from a stale safe area.
            ResetApiTransform();
        }

        // ------------------------------------------------------- client area <-> API space

        /// <summary>
        /// Recomputes the mapping from client-area coordinates to the coordinate space the
        /// screen-space API actually uses.
        ///
        /// <c>MyScreenSpace*</c> resolves 0..1 against the safe-GUI rectangle, not the window:
        /// <c>MyGuiManager.UpdateScreenSize</c> makes it <c>min(W, 4H/3)</c> wide and centres
        /// it, so everything drawn in 0..1 lands in a 4:3 box with bars on both sides of a
        /// 16:9 or 16:10 screen. The layout stays in window coordinates; this is the single
        /// place that bridges the two (ADR-0009).
        ///
        /// Called once per frame by whoever draws and once per input pass by whoever reads the
        /// pointer. It is deliberately not cached across frames: the safe area changes with the
        /// resolution, and one rectangle read per frame is cheaper than tracking when it does.
        /// </summary>
        public static void SyncScreenSpace()
        {
            try
            {
                RectangleF safe = MyScreenSpace.GetSafeGuiArea();
                if (safe.Width <= 0f || safe.Height <= 0f)
                {
                    // No usable GUI: a dedicated server, or a screen drawn before the GUI is
                    // up. The identity transform keeps drawing possible — the editor then
                    // degrades to the old 4:3 look instead of disappearing or throwing.
                    ResetApiTransform();
                    VgsLog.Throttled("screenspace.empty", "safe GUI area unavailable; drawing in API space", 600);
                    return;
                }

                // The safe rectangle is centred horizontally and spans the full height, so the
                // window is that rectangle plus one bar on each side (UpdateScreenSize).
                float fullWidth = safe.Width + 2f * safe.X;
                _apiScaleX = fullWidth / safe.Width;
                _apiOffsetX = -safe.X / safe.Width;

                // Height needs no scale: the safe rectangle is as tall as the window, so only
                // a vertical offset (normally zero) is involved.
                _apiScaleY = 1f;
                _apiOffsetY = -safe.Y / safe.Height;
            }
            catch (Exception e)
            {
                ResetApiTransform();
                VgsLog.Throttled("screenspace.read", "could not read the safe GUI area", e, 600);
            }
        }

        private static void ResetApiTransform()
        {
            _apiScaleX = 1f;
            _apiOffsetX = 0f;
            _apiScaleY = 1f;
            _apiOffsetY = 0f;
        }

        /// <summary>Client-area point to API point. Value types only: no allocation.</summary>
        private static Vector2 ToApi(Vector2 layoutPoint)
        {
            return new Vector2(layoutPoint.X * _apiScaleX + _apiOffsetX,
                               layoutPoint.Y * _apiScaleY + _apiOffsetY);
        }

        /// <summary>Client-area size to API size. A size has no origin, so it takes no offset.</summary>
        private static Vector2 ToApiSize(Vector2 layoutSize)
        {
            return new Vector2(layoutSize.X * _apiScaleX, layoutSize.Y * _apiScaleY);
        }

        /// <summary>
        /// API point back to client-area coordinates: the cursor is reported in API space while
        /// the hit tests run in layout space, so this is what keeps them aligned.
        /// </summary>
        private static Vector2 FromApi(Vector2 apiPoint)
        {
            // The scale is positive in every state SyncScreenSpace can produce, but a division
            // by zero in the per-frame path would take the screen down with it. Returning the
            // point unchanged keeps the frame alive.
            if (_apiScaleX == 0f || _apiScaleY == 0f) return apiPoint;

            return new Vector2((apiPoint.X - _apiOffsetX) / _apiScaleX,
                               (apiPoint.Y - _apiOffsetY) / _apiScaleY);
        }

        // ------------------------------------------------------------------ primitives

        /// <summary>Fills a rectangle given in client-area coordinates.</summary>
        public static void Rect(Vector2 position, Vector2 size, Color color)
        {
            if (!_solidReady || size.X <= 0f || size.Y <= 0f) return;

            try
            {
                MyScreenSpaceSprite.Draw(_solidMaterial, ToApi(position), ToApiSize(size), color,
                                         AlignTopLeft, 0f, Layer);
            }
            catch (Exception e)
            {
                VgsLog.Throttled("drawrect", "filled rectangle failed", e, 600);
            }
        }

        /// <summary>
        /// Draws a one-pixel-ish frame around a client-area rectangle. There is no border
        /// primitive in the screen-space API, so it is four filled rectangles.
        ///
        /// The coordinates are passed through unchanged on purpose: <see cref="Rect"/> converts
        /// them, and converting here as well would apply the transform twice.
        /// </summary>
        public static void Border(Vector2 position, Vector2 size, Color color, float thickness)
        {
            if (thickness <= 0f || size.X <= 0f || size.Y <= 0f) return;

            Rect(position, new Vector2(size.X, thickness), color);
            Rect(new Vector2(position.X, position.Y + size.Y - thickness), new Vector2(size.X, thickness), color);
            Rect(position, new Vector2(thickness, size.Y), color);
            Rect(new Vector2(position.X + size.X - thickness, position.Y), new Vector2(thickness, size.Y), color);
        }

        /// <summary>Draws text with its top-left corner at a client-area position.</summary>
        public static void Text(string text, Vector2 position, float scale, Color color)
        {
            if (_font == null || string.IsNullOrEmpty(text)) return;

            try
            {
                MyScreenSpaceText.Draw(_font, text, ToApi(position), scale, color, AlignTopLeft,
                                       float.PositiveInfinity, Layer);
            }
            catch (Exception e)
            {
                VgsLog.Throttled("drawtext", "text draw failed", e, 600);
            }
        }

        /// <summary>Draws text centred on a client-area position.</summary>
        public static void TextCentered(string text, Vector2 center, float scale, Color color)
        {
            if (_font == null || string.IsNullOrEmpty(text)) return;

            try
            {
                MyScreenSpaceText.Draw(_font, text, ToApi(center), scale, color, AlignCenter,
                                       float.PositiveInfinity, Layer);
            }
            catch (Exception e)
            {
                VgsLog.Throttled("drawtextcenter", "centred text draw failed", e, 600);
            }
        }

        /// <summary>
        /// Draws text so that it ends at <paramref name="rightTop"/>'s X. Measured through the
        /// same API that draws it, so the two cannot disagree.
        /// </summary>
        public static void TextRight(string text, Vector2 rightTop, float scale, Color color)
        {
            Vector2 size = Measure(text, scale);

            // A zero width means either "there was nothing to draw" (an empty string, which
            // Text would skip anyway) or "the measurement failed". Those are not the same
            // outcome, and the second one must not fall through to drawing at the right margin:
            // that is exactly the defect ADR-0009 fixed, with the string starting at the right
            // alignment point and running off the panel. Skipping is the honest failure.
            if (size.X <= 0f) return;

            Text(text, new Vector2(rightTop.X - size.X, rightTop.Y), scale, color);
        }

        /// <summary>Measures text and returns its size in client-area coordinates.</summary>
        public static Vector2 Measure(string text, float scale)
        {
            if (_font == null || string.IsNullOrEmpty(text)) return Vector2.Zero;

            try
            {
                // Measure ends in GetNormalizedSizeFromScreenSize, so its result is already a
                // normalized size in safe-GUI space — the same units Draw consumes. Running it
                // through MyScreenSpace.PixelSizeToNormalized (as this used to) divided by the
                // safe width a second time, which shrank the text 1600-fold on a 1920-wide
                // screen and pushed right-aligned strings outside the panel (ADR-0009).
                //
                // Dividing by the axis scale is the inverse of what Draw applies, so callers
                // receive the size in the layout's own units.
                Vector2 size = MyScreenSpaceText.Measure(_font, text, scale);
                if (_apiScaleX == 0f || _apiScaleY == 0f) return size;

                return new Vector2(size.X / _apiScaleX, size.Y / _apiScaleY);
            }
            catch (Exception e)
            {
                VgsLog.Throttled("measuretext", "text measurement failed", e, 600);
                return Vector2.Zero;
            }
        }

        /// <summary>
        /// Current cursor position in client-area coordinates, when the game reports one.
        ///
        /// The caller must have called <see cref="SyncScreenSpace"/> in the same frame: the
        /// inverse transform it installs is what turns the API's safe-GUI point into these
        /// coordinates, and a stale transform puts every hit test off by the width of the
        /// side bars (ADR-0009).
        /// </summary>
        public static bool TryCursor(out Vector2 clientArea)
        {
            clientArea = Vector2.Zero;

            try
            {
                Vector2 api;
                if (!MyScreenSpace.TryGetCursorPosition(out api)) return false;

                // The API reports the cursor in safe-GUI space. Without the inverse transform
                // clicks would land towards the centre of the screen while the rectangles they
                // hit are drawn across the whole window (ADR-0009).
                clientArea = FromApi(api);
                return true;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("cursor", "could not read the cursor position", e, 600);
                return false;
            }
        }
    }
}
