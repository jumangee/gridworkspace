using System;
using System.Collections.Generic;
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

        /// <summary>
        /// Client-area height ÷ width, both in pixels. One X unit of the layout is the window's
        /// width in pixels; one Y unit is its height. On any window that is not square those are
        /// different lengths, so a shape that must look round has to compensate — see
        /// <see cref="Disc"/> and <see cref="AspectX"/>.
        /// </summary>
        private static float _clientAspect = 1f;

        /// <summary>Client-area height in pixels; 0 when the safe area is unavailable.</summary>
        private static float _clientHeightPixels;

        /// <summary>
        /// Client-area width in pixels; 0 when the safe area is unavailable. Needed only by
        /// <see cref="Icon"/>, which hands window pixels to `DrawPixels` and therefore has to
        /// convert the layout's X axis itself.
        /// </summary>
        private static float _clientWidthPixels;

        /// <summary>
        /// Client-area height ÷ width. 1 when unknown, which degrades circles to the old
        /// unstretched look rather than to something worse.
        /// </summary>
        public static float ClientAspect
        {
            get { return _clientAspect; }
        }

        /// <summary>
        /// Converts a length measured along Y into the X length that is the same distance on
        /// screen. Everything round or square uses this; everything else uses plain layout
        /// units, because a card *should* be wider than it is tall.
        /// </summary>
        public static float AspectX(float yExtent)
        {
            return yExtent * _clientAspect;
        }

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
            VgsIcons.Ensure();

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

            // The atlas itself belongs to the game (it is a shipped texture), so there is nothing
            // to dispose — only the material ids this session resolved have to be forgotten.
            VgsIcons.Release();

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

                // The client area is fullWidth × safe.Height pixels, so one X unit of the
                // layout is fullWidth pixels and one Y unit is safe.Height. Round shapes need
                // that ratio; see Disc and AspectX.
                _clientHeightPixels = safe.Height;
                _clientWidthPixels = fullWidth;
                _clientAspect = fullWidth > 0f ? safe.Height / fullWidth : 1f;

                // Icon textures are resolved here rather than only in EnsureResources, because
                // EnsureResources latches: it runs once and never again, so a texture that was
                // not yet streamed at that moment would never be picked up. This runs every
                // frame; a resolved set costs one bool test.
                VgsIcons.Ensure();
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

            // No known aspect: assume square units. Circles then come out unstretched, which
            // is the least wrong of the available answers.
            _clientAspect = 1f;
            _clientHeightPixels = 0f;
            _clientWidthPixels = 0f;
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

        /// <summary>
        /// <see cref="Border"/> clipped to the canvas. Used for anything that scrolls, because
        /// an unclipped frame paints over the header and the bottom bar — those are drawn
        /// afterwards and would cover the overdraw, but relying on draw order to hide geometry
        /// breaks the moment something is drawn later than them.
        /// </summary>
        public static void BorderClipped(Vector2 position, Vector2 size, Vector2 clipTopLeft,
                                        Vector2 clipSize, Color color, float thickness)
        {
            if (thickness <= 0f || size.X <= 0f || size.Y <= 0f) return;

            RectClipped(position, new Vector2(size.X, thickness), clipTopLeft, clipSize, color);
            RectClipped(new Vector2(position.X, position.Y + size.Y - thickness),
                        new Vector2(size.X, thickness), clipTopLeft, clipSize, color);
            RectClipped(position, new Vector2(thickness, size.Y), clipTopLeft, clipSize, color);
            RectClipped(new Vector2(position.X + size.X - thickness, position.Y),
                        new Vector2(thickness, size.Y), clipTopLeft, clipSize, color);
        }

        /// <summary>
        /// Fills the part of a rectangle that lies inside a clipping rectangle, given in
        /// client-area coordinates.
        ///
        /// This is how the canvas clips itself. The screen-space API does have a scissor
        /// (<c>MyScreenSpace.UsingScissorRectangle</c>), but it pushes and pops state into the
        /// shared draw queue, and a mod that gets the nesting wrong floods the log with
        /// "left N scissor rectangle(s) open". Intersecting each rectangle costs two
        /// comparisons and cannot leave the queue unbalanced — the right trade for a canvas
        /// whose content is small rectangles to begin with.
        /// </summary>
        public static void RectClipped(Vector2 position, Vector2 size, Vector2 clipTopLeft,
                                       Vector2 clipSize, Color color)
        {
            if (size.X <= 0f || size.Y <= 0f) return;

            float left = position.X > clipTopLeft.X ? position.X : clipTopLeft.X;
            float top = position.Y > clipTopLeft.Y ? position.Y : clipTopLeft.Y;
            float right = position.X + size.X;
            float bottom = position.Y + size.Y;

            float clipRight = clipTopLeft.X + clipSize.X;
            float clipBottom = clipTopLeft.Y + clipSize.Y;
            if (right > clipRight) right = clipRight;
            if (bottom > clipBottom) bottom = clipBottom;

            if (right <= left || bottom <= top) return;

            Rect(new Vector2(left, top), new Vector2(right - left, bottom - top), color);
        }

        /// <summary>
        /// Draws an axis-aligned segment as a filled rectangle, clipped to the canvas.
        ///
        /// The screen-space API has no line primitive: <c>MyScreenSpaceSprite.Draw</c> takes a
        /// rotation, but its pivot semantics are not documented for mods and a wrong guess
        /// rotates every connection line around the wrong point. Axis-aligned rectangles need
        /// no such guess, and the routes drawn here are orthogonal anyway.
        /// </summary>
        public static void Segment(Vector2 from, Vector2 to, float thickness, Vector2 clipTopLeft,
                                   Vector2 clipSize, Color color)
        {
            if (thickness <= 0f) return;

            // Decided by magnitude rather than by exact equality: the router produces segments
            // that are *meant* to be axis-aligned, and a value that came out a hair off must
            // still be drawn along its dominant axis instead of being dropped.
            float dx = Math.Abs(to.X - from.X);
            float dy = Math.Abs(to.Y - from.Y);

            if (dx >= dy)
            {
                float x = from.X < to.X ? from.X : to.X;
                RectClipped(new Vector2(x, (from.Y + to.Y) * 0.5f - thickness * 0.5f),
                            new Vector2(dx, thickness), clipTopLeft, clipSize, color);
                return;
            }

            float y = from.Y < to.Y ? from.Y : to.Y;
            RectClipped(new Vector2((from.X + to.X) * 0.5f - thickness * 0.5f, y),
                        new Vector2(thickness, dy), clipTopLeft, clipSize, color);
        }

        /// <summary>
        /// Draws a dashed axis-aligned segment: the watch line of an event controller
        /// (ADR-0017).
        ///
        /// The dash pattern is built by <see cref="EdgeRouting.BuildDashes"/> — pure geometry,
        /// assertable offline — and each dash is drawn with the same rectangle primitive as a
        /// solid segment. The screen-space API has no line style, so "dashed" has to be "many
        /// short solid segments", and the only question is where the pattern is computed.
        /// Doing it here would put a per-frame loop with float arithmetic in the draw path where
        /// nothing can check it; doing it in the router keeps the pattern a value the offline
        /// gate can measure.
        ///
        /// <paramref name="dashLength"/> and <paramref name="gapLength"/> are in client-area
        /// units, already scaled by the caller's zoom.
        /// </summary>
        public static void SegmentDashed(Vector2 from, Vector2 to, float thickness,
                                         float dashLength, float gapLength,
                                         Vector2 clipTopLeft, Vector2 clipSize, Color color)
        {
            if (thickness <= 0f) return;

            // BuildDashes replaces the buffer's contents, so nothing accumulates between the
            // lines of one frame or between frames. That is load-bearing: the buffer is static,
            // and appending would make every frame redraw every dash ever produced.
            var segment = new Vector4(from.X, from.Y, to.X, to.Y);
            EdgeRouting.BuildDashes(segment, dashLength, gapLength, _dashBuffer);

            for (int i = 0; i < _dashBuffer.Count; i++)
            {
                Vector4 dash = _dashBuffer[i];
                Segment(new Vector2(dash.X, dash.Y), new Vector2(dash.Z, dash.W),
                        thickness, clipTopLeft, clipSize, color);
            }
        }

        /// <summary>
        /// Scratch list for dash patterns. Shared because this runs per watch line per frame, and
        /// a list allocated per call would be an allocation in the draw path.
        /// </summary>
        private static readonly List<Vector4> _dashBuffer = new List<Vector4>(64);

        /// <summary>
        /// Draws a filled disc, clipped to the canvas. <paramref name="radius"/> is measured
        /// along Y; the horizontal extent is derived so the result is round on screen.
        ///
        /// There is no circle primitive, so the disc is a stack of horizontal slabs whose
        /// half-width follows the circle equation. Two things make it look like a circle
        /// rather than a blob:
        ///
        ///  * <b>Aspect correction.</b> One X unit of the layout is the window's width, one Y
        ///    unit its height. A shape with equal extents in both is therefore an ellipse —
        ///    that is why the old dots read as ovals. The horizontal half-width is multiplied
        ///    by <see cref="ClientAspect"/>.
        ///  * <b>One slab per screen pixel.</b> The slab count comes from the disc's height in
        ///    pixels, so each step is a single pixel and the edge reads as a curve. The old
        ///    fixed six slabs gave steps several pixels tall, which is what looked pixelated.
        ///    The count is bounded so a huge disc cannot cost hundreds of quads, and it falls
        ///    back to a small fixed number when the safe area is unknown.
        /// </summary>
        public static void Disc(Vector2 center, float radius, Vector2 clipTopLeft, Vector2 clipSize,
                                Color color)
        {
            if (radius <= 0f) return;

            float xRadius = radius * _clientAspect;
            if (xRadius <= 0f) return;

            int slabs;
            if (_clientHeightPixels > 0f)
            {
                // Height in pixels, one slab per pixel: the edge is then a staircase of
                // single pixels, which the eye reads as a curve.
                float pixels = radius * 2f * _clientHeightPixels;
                slabs = (int)(pixels + 0.5f);
                if (slabs < 4) slabs = 4;
                if (slabs > MaxDiscSlabs) slabs = MaxDiscSlabs;
            }
            else
            {
                slabs = 16;
            }

            float slabHeight = (radius * 2f) / slabs;

            // Slabs overlap by a hair. Adjacent slabs share an exact boundary in the
            // arithmetic, but in single precision they can leave a sub-pixel seam, and a seam
            // through a white button reads as a crack in it.
            const float overlap = 1.02f;
            float drawHeight = slabHeight * overlap;

            for (int i = 0; i < slabs; i++)
            {
                // Centre of the slab relative to the disc centre, in radius units.
                float dy = -radius + slabHeight * (i + 0.5f);
                float t = dy / radius;
                float halfWidth = xRadius * (float)Math.Sqrt(Math.Max(0.0, 1.0 - t * t));
                if (halfWidth <= 0f) continue;

                RectClipped(
                    new Vector2(center.X - halfWidth, center.Y + dy - drawHeight * 0.5f),
                    new Vector2(halfWidth * 2f, drawHeight),
                    clipTopLeft, clipSize, color);
            }
        }

        /// <summary>
        /// Upper bound on slabs per disc. A port dot needs a handful; the "+" button needs
        /// about thirty at 1080p. The cap keeps a very large disc — or a very tall window —
        /// from turning one circle into hundreds of queued quads.
        /// </summary>
        private const int MaxDiscSlabs = 48;

        // ------------------------------------------------------------------ icons (ADR-0019)

        /// <summary>
        /// Draws an icon so that its ink is <paramref name="inkHeight"/> layout units tall,
        /// centred on <paramref name="center"/> (both in client-area coordinates).
        ///
        /// This is the only place a sprite is drawn from a texture, and it differs from
        /// <see cref="Rect"/> in three ways that all matter:
        ///
        ///  * <b>It uses <c>DrawPixels</c>, not <c>Draw</c>.</b> `Draw` converts a normalised
        ///    position from the safe-GUI 4:3 box; `DrawPixels` converts nothing, so the caller
        ///    must hand it window pixels. <see cref="IconPlacement"/> does that conversion with
        ///    the same transform the rest of this class uses for the pointer.
        ///  * <b>It snaps to whole pixels.</b> A sprite on a half-pixel boundary is resampled,
        ///    which is the blur the textures were built to avoid.
        ///  * <b>It clips by cutting the source as well.</b> A card that scrolls under the header
        ///    is cut on both rectangles, so the icon does not slide inside its own box.
        ///
        /// Returns false when nothing was drawn: no such texture, or it lies entirely outside the
        /// canvas. The caller then draws the old shape, which is why this reports instead of just
        /// returning.
        ///
        /// There is no size condition. An earlier version refused icons below a per-icon
        /// legibility floor and let the caller fall back to geometry; in the game that made the
        /// control change shape as the player zoomed out, which reads as a bug. ADR-0019 §6 has
        /// the reversal and the measurement behind it.
        ///
        /// One residual risk, recorded rather than hidden: <c>DrawPixels</c> drops a sprite whose
        /// texture is not resident, and says nothing. <see cref="VgsIcons"/> checks that the
        /// material resolves and that its file reads as an image, but the only API that answers
        /// "is it uploaded yet" is <c>MyRenderProxy.IsTextureLoaded</c>, which the mod whitelist
        /// does not allow. So on the first frame or two after a world loads, this can return true
        /// for a sprite the renderer then discards, and the control is blank for those frames
        /// instead of showing its old shape. Every later frame is covered.
        /// </summary>
        public static bool Icon(VgsIcon icon, Vector2 center, float inkHeight, Color color,
                                Vector2 clipTopLeft, Vector2 clipSize)
        {
            MyStringId material;
            if (!VgsIcons.TryGetMaterial(icon, out material)) return false;

            try
            {
                Rectangle destination, clip;
                if (!IconPlacement.TryPlace(center, inkHeight, VgsIconAtlas.TextureScale(icon),
                                            _clientWidthPixels, _clientHeightPixels,
                                            clipTopLeft, clipSize, out destination, out clip))
                {
                    return false;
                }

                Rectangle source;
                if (!VgsIconAtlas.TryGetSource(icon, out source)) return false;

                Rectangle visibleDestination, visibleSource;
                if (!IconPlacement.TryClip(destination, source, clip,
                                           out visibleDestination, out visibleSource))
                {
                    return false;
                }

                MyScreenSpaceSprite.DrawPixels(material, visibleDestination, visibleSource, color,
                                               0f, Layer);

                return true;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("drawicon", "icon draw failed", e, 600);
                return false;
            }
        }

        /// <summary>Draws text with its top-left corner at a client-area position.</summary>
        public static void Text(string text, Vector2 position, float scale, Color color)
        {
            Text(text, position, scale, color, float.PositiveInfinity);
        }

        /// <summary>
        /// Draws text with its top-left corner at a client-area position, clipped to
        /// <paramref name="maxWidth"/> (client-area units).
        ///
        /// The engine passes the width straight to <c>MyGuiManager.DrawString</c>, which
        /// truncates to it, so a port label never spills over its bar. Clipping is the reason
        /// this overload exists: measuring the string instead would call into the font
        /// metrics for every port of every visible card, every frame.
        /// </summary>
        public static void Text(string text, Vector2 position, float scale, Color color, float maxWidth)
        {
            if (_font == null || string.IsNullOrEmpty(text)) return;

            try
            {
                // maxWidth is a *width*, so it takes the same axis scale as a size and none of
                // the position offset. Passing a client-area width straight through would
                // clip the label at 1/scaleX of its bar (ADR-0009).
                float apiMaxWidth = float.IsPositiveInfinity(maxWidth)
                    ? float.PositiveInfinity
                    : maxWidth * _apiScaleX;

                MyScreenSpaceText.Draw(_font, text, ToApi(position), scale, color, AlignTopLeft,
                                       apiMaxWidth, Layer);
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
