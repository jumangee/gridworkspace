using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using Vgs.Core;
using Vgs.Game;
using Vgs.Model;

namespace Vgs.Ui
{
    /// <summary>
    /// Full-screen grid logic editor (roadmap stage 1, with the stage-2 canvas).
    ///
    /// <b>Base class.</b> This derives from <see cref="MyModScreen"/>, the API Keen added in
    /// 1.211 for mod-defined screens, not from <c>MyGuiScreenBase</c>. That is not a style
    /// choice: the game compiles mod sources itself and rejects every member outside its IL
    /// whitelist, and the engine GUI types are outside it. Deriving from
    /// <c>MyGuiScreenBase</c> compiles locally and then fails at load with
    /// "The type or member 'MyGuiScreenBase' is prohibited". <see cref="MyModScreen"/> lives
    /// in <c>Sandbox.ModAPI</c>, which is whitelisted wholesale.
    ///
    /// Design notes that matter for whoever extends this:
    ///
    ///  * <b>One screen, self-drawn canvas.</b> The canvas is painted in
    ///    <see cref="OnDraw"/> through <see cref="VgsScreenDraw"/>. There are no stock
    ///    controls to compose: the whitelisted screen-space API offers filled quads and text
    ///    only, so even the Close button is drawn and hit-tested here.
    ///
    ///  * <b>Client-area coordinates.</b> Every rectangle in this file is 0..1 of the window,
    ///    which is what the player thinks of as the screen and what keeps the layout
    ///    resolution independent. <see cref="VgsScreenDraw"/> converts to the space the
    ///    screen-space API actually uses — the safe-GUI rectangle, a centred 4:3 box — so no
    ///    code here deals with that difference (ADR-0009).
    ///
    ///  * <b>No per-frame world access, no per-frame allocation.</b> Block data is read once
    ///    into a <see cref="GridGraph"/>; drawing only reads that snapshot. Text that changes
    ///    rarely (status line, culling notice) is formatted once and reused — <see cref="OnDraw"/>
    ///    runs every frame, and rebuilding those strings there is a measurable cost on a
    ///    large grid.
    ///
    ///  * <b>Every override is guarded.</b> These methods are called by the game; an
    ///    exception escaping one of them breaks the session, not just this screen.
    /// </summary>
    public sealed class VgsEditorScreen : MyModScreen
    {
        // ---- Palette (ARGB via VRageMath.Color) ----
        private static readonly Color ColBackdrop = new Color(12, 15, 20, 235);
        private static readonly Color ColCanvas = new Color(18, 22, 29, 255);
        private static readonly Color ColGridMinor = new Color(30, 36, 46, 255);
        private static readonly Color ColGridMajor = new Color(44, 52, 66, 255);
        private static readonly Color ColNode = new Color(38, 45, 58, 255);
        private static readonly Color ColNodeBorder = new Color(70, 84, 105, 255);
        private static readonly Color ColNodeSelected = new Color(255, 170, 60, 255);
        private static readonly Color ColText = new Color(226, 232, 240, 255);
        private static readonly Color ColTextDim = new Color(140, 152, 170, 255);
        private static readonly Color ColPortIn = new Color(110, 190, 255, 255);
        private static readonly Color ColPortOut = new Color(120, 220, 160, 255);
        private static readonly Color ColBottomBar = new Color(24, 29, 38, 255);

        /// <summary>
        /// Thickness of a hairline, as a fraction of the client-area height (about 1 px at
        /// 1080p). Horizontal lines keep that thickness; vertical lines are drawn from a width,
        /// so the layout-to-API transform scales them by its horizontal factor (about 1.2 on a
        /// 16:10 window) and they read slightly thicker.
        /// </summary>
        private const float Hairline = 1f / 1080f;

        /// <summary>
        /// The editor currently on screen, if any.
        ///
        /// Tracked explicitly because the screen API offers no way to enumerate open screens,
        /// and <see cref="MyModScreen.IsOpen"/> only answers for an instance we already hold.
        /// </summary>
        private static VgsEditorScreen _openEditor;

        private readonly GridGraph _graph;
        private readonly IMyCubeGrid _grid;

        // Camera: _camera is the graph-space point shown at the canvas top-left corner.
        private Vector2 _camera = Vector2.Zero;
        private float _zoom = 1f;

        // Interaction state.
        private Node _draggedNode;
        private Vector2 _dragGrabOffset;
        private bool _dragging;
        private bool _panning;
        private Vector2 _panAnchor;
        private Node _selected;

        // Refresh throttling: the grid can change at any time, but re-reading it every
        // frame would dominate the frame budget on a large ship.
        private int _lastRefreshFrame;
        private const int RefreshIntervalFrames = 90;

        // Cached draw strings. Rebuilt only when the value they describe changes.
        private string _statusText;
        private int _statusKeyNodes = -1;
        private int _statusKeyEdges = -1;
        private int _statusKeyZoom = -1;

        private string _cullText;
        private int _cullKeyDrawn = -1;
        private int _cullKeyTotal = -1;

        /// <summary>
        /// Header label for the grid, resolved once by <see cref="ResolveGridName"/>.
        ///
        /// Seeded with the fallback rather than null: if <see cref="OnOpened"/> fails before the
        /// name is resolved, the header still shows a label instead of an empty right side, and
        /// the draw path never has to reach for the live grid to fill the gap. <see cref="OnClosed"/>
        /// clears it to null, and the header tolerates that for the closing frame — the two are a
        /// pair, so neither the seed nor the guard can be dropped alone.
        /// </summary>
        private string _gridNameCache = VgsText.GridFallbackName;

        /// <summary>True while the pointer is over the Close button; drives its highlight.</summary>
        private bool _closeHot;

        private VgsEditorScreen(GridGraph graph, IMyCubeGrid grid)
            : base(
                name: "VGS.GridSetup",
                position: new Vector2(0.5f, 0.5f),
                size: new Vector2(1f, 1f),
                // No host background on purpose: MyGuiScreenBase paints it inside the safe-GUI
                // rectangle, which would leave a visible 4:3 box underneath a full-window
                // editor. DrawBackdrop fills the client area itself (ADR-0009).
                backgroundColor: null,
                backgroundTexture: null,
                isTopMost: true)
        {
            _graph = graph;
            _grid = grid;

            // The editor is a full-screen modal: Esc closes it, the pointer is visible, and in
            // single player the world pauses behind it.
            CloseOnEscape = true;
            CloseButtonEnabled = false;   // we draw and hit-test our own button
            DrawMouseCursor = true;
            EnabledBackgroundFade = true;
            PausesGameInSinglePlayer = true;

            // Start with the graph roughly centred.
            _camera = new Vector2(-0.08f, -0.05f);
        }

        /// <summary>
        /// Opens the editor for a cockpit's grid. Does nothing when a session cannot show a
        /// screen. This is the single entry point the terminal button calls.
        /// </summary>
        public static void OpenFor(IMyCockpit cockpit)
        {
            if (cockpit == null) return;
            if (!VgsSession.HasLocalPlayer) return;

            IMyCubeGrid grid = null;
            try
            {
                grid = cockpit.CubeGrid;
            }
            catch (Exception e)
            {
                VgsLog.Error("could not resolve cockpit grid", e);
            }

            if (grid == null)
            {
                VgsLog.Info(VgsText.NoGrid);
                Notify(VgsText.NoGrid);
                return;
            }

            // Avoid stacking identical editors: bring the open one forward instead.
            if (_openEditor != null && _openEditor.IsOpen)
            {
                _openEditor.RaiseToTop();
                return;
            }

            GridGraph graph = GridGraphBuilder.Build(grid);
            var screen = new VgsEditorScreen(graph, grid);

            VgsScreenDraw.EnsureResources();
            if (!VgsScreenDraw.ResourcesReady)
            {
                // Drawing without a font or brush produces an empty black rectangle; say so
                // instead of leaving the player with a blank screen and no explanation.
                VgsLog.Error("editor not opened: screen resources unavailable");
                Notify(VgsText.ErrOpenScreen);
                return;
            }

            _openEditor = screen;
            screen.Open();
            VgsLog.Info("editor opened for grid " + grid.EntityId + " (" + graph.NodeCount + " blocks)");
        }

        private static void Notify(string message)
        {
            try
            {
                MyAPIGateway.Utilities.ShowNotification(message, 3000);
            }
            catch
            {
                // Intentionally ignored: the message is also in the log, and a missing HUD
                // notification is not worth an exception inside the click handler.
            }
        }

        /// <summary>Closes the editor if one is open. Called when the session unloads.</summary>
        public static void CloseIfOpen()
        {
            VgsEditorScreen screen = _openEditor;
            _openEditor = null;
            if (screen == null) return;

            try
            {
                screen.Close();
            }
            catch (Exception e)
            {
                VgsLog.Error("could not close the editor screen", e);
            }
        }

        private void RaiseToTop()
        {
            try
            {
                // The screen API has no z-order control, so a second open request while the
                // editor is up is simply ignored: the existing screen already has focus.
                VgsLog.Info("editor already open; reusing it");
            }
            catch (Exception e)
            {
                VgsLog.Throttled("raisetop", "could not raise editor", e, 300);
            }
        }

        // ------------------------------------------------------------------ lifecycle

        protected override void OnOpened()
        {
            try
            {
                VgsScreenDraw.EnsureResources();

                // The grid name is resolved here rather than lazily from DrawHeader: CustomName
                // is a live game property, and the UI layer must not read the world from a draw
                // path (ADR-0003). OnOpened is the one place the screen is guaranteed to pass
                // through before its first frame, so one read here covers every frame after it.
                ResolveGridName();

                _lastRefreshFrame = VgsSession.Frame;
            }
            catch (Exception e)
            {
                VgsLog.Error("editor OnOpened failed", e);
            }
        }

        protected override void OnLayoutChanged()
        {
            try
            {
                // Nothing is laid out in advance: every rectangle is derived from the current
                // client area at draw time, so a resolution change needs no recalculation.
            }
            catch (Exception e)
            {
                VgsLog.Error("editor OnLayoutChanged failed", e);
            }
        }

        protected override bool OnCancelling()
        {
            try
            {
                // Returning true lets the screen close on Esc.
                return true;
            }
            catch (Exception e)
            {
                VgsLog.Error("editor OnCancelling failed", e);
                return true;
            }
        }

        protected override void OnClosed(bool isUnloading)
        {
            try
            {
                if (ReferenceEquals(_openEditor, this)) _openEditor = null;

                _draggedNode = null;
                _selected = null;
                _statusText = null;
                _cullText = null;
                _gridNameCache = null;

                if (!isUnloading) VgsLog.Info("editor closed");
            }
            catch (Exception e)
            {
                VgsLog.Error("editor OnClosed failed", e);
            }
        }

        // ------------------------------------------------------------------ layout

        /// <summary>Canvas rectangle in normalized coordinates: everything above the bottom bar.</summary>
        private static void GetCanvasRect(out Vector2 topLeft, out Vector2 size)
        {
            topLeft = new Vector2(0f, VgsConstants.HeaderHeight);
            size = new Vector2(1f, 1f - VgsConstants.HeaderHeight - VgsConstants.BottomBarHeight);
        }

        private static void GetBottomBarRect(out Vector2 topLeft, out Vector2 size)
        {
            topLeft = new Vector2(0f, 1f - VgsConstants.BottomBarHeight);
            size = new Vector2(1f, VgsConstants.BottomBarHeight);
        }

        private static void GetCloseButtonRect(out Vector2 topLeft, out Vector2 size)
        {
            size = new Vector2(VgsConstants.CloseButtonWidth, VgsConstants.CloseButtonHeight);
            topLeft = new Vector2(
                1f - VgsConstants.CloseButtonRightMargin - size.X,
                1f - VgsConstants.BottomBarHeight * 0.5f - size.Y * 0.5f);
        }

        // ------------------------------------------------------------------ update

        protected override void OnUpdate(bool hasFocus)
        {
            try
            {
                if (!hasFocus) return;

                if (VgsSession.Frame - _lastRefreshFrame >= RefreshIntervalFrames)
                {
                    _lastRefreshFrame = VgsSession.Frame;
                    RefreshFromGrid();
                }
            }
            catch (Exception e)
            {
                VgsLog.Throttled("screenupdate", "editor update failed", e, 300);
            }
        }

        private void RefreshFromGrid()
        {
            if (_grid == null) return;

            try
            {
                // The grid may have been removed while the screen was open (ship destroyed,
                // block ground down, session ending).
                if (MyAPIGateway.Entities == null) return;

                VRage.ModAPI.IMyEntity entity;
                if (!MyAPIGateway.Entities.TryGetEntityById(_graph.GridEntityId, out entity) || entity == null)
                {
                    VgsLog.Info("grid disappeared, closing editor");
                    Close();
                    return;
                }

                GridGraphBuilder.Refresh(_graph, _grid);
            }
            catch (Exception e)
            {
                VgsLog.Error("editor refresh failed", e);
            }
        }

        // ------------------------------------------------------------------ input

        protected override void OnHandleInput()
        {
            try
            {
                // TryCursor reports in client-area coordinates only while this transform is
                // current, and the hit tests below assume exactly those units (ADR-0009).
                VgsScreenDraw.SyncScreenSpace();

                if (MyAPIGateway.Input == null) return;

                Vector2 mouse;
                if (!VgsScreenDraw.TryCursor(out mouse)) return;

                if (HandleCloseButton(mouse)) return;
                HandleCanvasInput(mouse);
            }
            catch (Exception e)
            {
                VgsLog.Throttled("screeninput", "input handling failed", e, 300);
            }
        }

        /// <summary>
        /// Click handling for the self-drawn Close button. Returns true when the click was
        /// consumed, so it cannot also start a drag on the canvas underneath.
        /// </summary>
        private bool HandleCloseButton(Vector2 mouse)
        {
            Vector2 buttonTopLeft, buttonSize;
            GetCloseButtonRect(out buttonTopLeft, out buttonSize);

            _closeHot =
                mouse.X >= buttonTopLeft.X && mouse.X <= buttonTopLeft.X + buttonSize.X &&
                mouse.Y >= buttonTopLeft.Y && mouse.Y <= buttonTopLeft.Y + buttonSize.Y;

            if (!_closeHot || !MyAPIGateway.Input.IsNewLeftMousePressed()) return false;

            Close();
            return true;
        }

        private void HandleCanvasInput(Vector2 mouse)
        {
            Vector2 canvasTopLeft, canvasSize;
            GetCanvasRect(out canvasTopLeft, out canvasSize);

            bool insideCanvas =
                mouse.X >= canvasTopLeft.X && mouse.X <= canvasTopLeft.X + canvasSize.X &&
                mouse.Y >= canvasTopLeft.Y && mouse.Y <= canvasTopLeft.Y + canvasSize.Y;

            // ---- zoom ----
            if (insideCanvas)
            {
                int wheel = MyAPIGateway.Input.DeltaMouseScrollWheelValue();
                if (wheel != 0)
                {
                    // Zoom about the cursor so the point under the pointer stays put.
                    Vector2 graphUnderCursor = ScreenToGraph(mouse, canvasTopLeft);
                    float factor = wheel > 0 ? VgsConstants.ZoomStep : 1f / VgsConstants.ZoomStep;
                    float newZoom = _zoom * factor;
                    if (newZoom < VgsConstants.MinZoom) newZoom = VgsConstants.MinZoom;
                    if (newZoom > VgsConstants.MaxZoom) newZoom = VgsConstants.MaxZoom;

                    if (newZoom != _zoom)
                    {
                        _zoom = newZoom;
                        _camera = graphUnderCursor - (mouse - canvasTopLeft) / _zoom;
                    }
                }
            }

            bool leftDown = MyAPIGateway.Input.IsLeftMousePressed();
            bool leftNew = MyAPIGateway.Input.IsNewLeftMousePressed();
            bool leftReleased = MyAPIGateway.Input.IsNewLeftMouseReleased();
            bool rightDown = MyAPIGateway.Input.IsRightMousePressed();
            bool middleDown = MyAPIGateway.Input.IsMiddleMousePressed();

            // ---- pan (right or middle button) ----
            if (insideCanvas && (rightDown || middleDown))
            {
                if (!_panning)
                {
                    _panning = true;
                    _panAnchor = mouse;
                }
                else
                {
                    Vector2 delta = (mouse - _panAnchor) / _zoom;
                    _camera -= delta;
                    _panAnchor = mouse;
                }
                return;
            }

            if (_panning && !rightDown && !middleDown) _panning = false;

            // ---- selection / drag (left button) ----
            if (leftNew && insideCanvas)
            {
                Vector2 graphPos = ScreenToGraph(mouse, canvasTopLeft);
                Node hit = HitTest(graphPos);
                _selected = hit;

                if (hit != null)
                {
                    _draggedNode = hit;
                    _dragGrabOffset = graphPos - hit.Position;
                    _dragging = false;
                }
            }

            if (leftDown && _draggedNode != null)
            {
                Vector2 graphPos = ScreenToGraph(mouse, canvasTopLeft);
                Vector2 target = graphPos - _dragGrabOffset;

                if (!_dragging)
                {
                    if ((target - _draggedNode.Position).Length() > VgsConstants.DragThreshold) _dragging = true;
                }

                if (_dragging) _draggedNode.Position = target;
            }

            if (leftReleased)
            {
                _draggedNode = null;
                _dragging = false;
            }
        }

        /// <summary>Converts a normalized screen point to graph space.</summary>
        private Vector2 ScreenToGraph(Vector2 screenPoint, Vector2 canvasTopLeft)
        {
            return (screenPoint - canvasTopLeft) / _zoom + _camera;
        }

        /// <summary>Converts a graph-space point to normalized screen space.</summary>
        private Vector2 GraphToScreen(Vector2 graphPoint, Vector2 canvasTopLeft)
        {
            return (graphPoint - _camera) * _zoom + canvasTopLeft;
        }

        /// <summary>Topmost node under a graph-space point, or null.</summary>
        private Node HitTest(Vector2 graphPoint)
        {
            IList<Node> nodes = _graph.Nodes;
            // Iterate backwards so the visually topmost (last drawn) node wins.
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                Node n = nodes[i];
                if (graphPoint.X >= n.Position.X && graphPoint.X <= n.Position.X + VgsConstants.NodeWidth &&
                    graphPoint.Y >= n.Position.Y && graphPoint.Y <= n.Position.Y + VgsConstants.NodeHeight)
                {
                    return n;
                }
            }
            return null;
        }

        // ------------------------------------------------------------------ drawing

        protected override void OnDraw(float transitionAlpha)
        {
            try
            {
                // The layout below is in client-area coordinates and every primitive converts
                // at draw time, so the transform has to be current before the first one. It is
                // a per-frame cache, not a constant: the safe area changes with the resolution
                // (ADR-0009).
                VgsScreenDraw.SyncScreenSpace();

                Vector2 canvasTopLeft, canvasSize;
                GetCanvasRect(out canvasTopLeft, out canvasSize);

                DrawBackdrop(canvasTopLeft, canvasSize);
                DrawGrid(canvasTopLeft, canvasSize);
                DrawNodes(canvasTopLeft, canvasSize);
                DrawBottomBar();
                DrawHeader();
            }
            catch (Exception e)
            {
                // Swallowing here keeps the screen alive so the user can still close it.
                VgsLog.Throttled("screendraw", "editor draw failed", e, 300);
            }
        }

        private static void DrawBackdrop(Vector2 topLeft, Vector2 size)
        {
            VgsScreenDraw.Rect(Vector2.Zero, new Vector2(1f, 1f), ColBackdrop);
            VgsScreenDraw.Rect(topLeft, size, ColCanvas);
        }

        /// <summary>
        /// Draws the "infinite" field. The spacing is derived from zoom so the grid stays
        /// legible at any scale; lines are skipped when they would be denser than a few
        /// pixels, which is what keeps zoomed-out views cheap.
        /// </summary>
        private void DrawGrid(Vector2 topLeft, Vector2 size)
        {
            float step = VgsConstants.BaseCellSize * _zoom;

            // Below this the lines merge into a solid wash; draw nothing instead.
            const float minVisibleStep = 0.006f;
            if (step < minVisibleStep) return;

            // Every 5th line is emphasised to give a sense of scale.
            const int majorEvery = 5;

            float startX = -_camera.X * _zoom;
            int firstCol = (int)Math.Floor(-startX / step);

            for (int i = 0; ; i++)
            {
                int col = firstCol + i;
                float x = startX + col * step;
                if (x > size.X) break;
                if (x < 0f) continue;

                Color c = (col % majorEvery == 0) ? ColGridMajor : ColGridMinor;
                VgsScreenDraw.Rect(
                    new Vector2(topLeft.X + x, topLeft.Y),
                    new Vector2(Hairline, size.Y),
                    c);
            }

            float startY = -_camera.Y * _zoom;
            int firstRow = (int)Math.Floor(-startY / step);

            for (int i = 0; ; i++)
            {
                int row = firstRow + i;
                float y = startY + row * step;
                if (y > size.Y) break;
                if (y < 0f) continue;

                Color c = (row % majorEvery == 0) ? ColGridMajor : ColGridMinor;
                VgsScreenDraw.Rect(
                    new Vector2(topLeft.X, topLeft.Y + y),
                    new Vector2(size.X, Hairline),
                    c);
            }
        }

        private void DrawNodes(Vector2 topLeft, Vector2 size)
        {
            IList<Node> nodes = _graph.Nodes;
            if (nodes.Count == 0)
            {
                VgsScreenDraw.TextCentered(VgsText.EmptyGrid, topLeft + size * 0.5f, 0.8f, ColTextDim);
                return;
            }

            // Cheap frustum culling: only draw what is on screen. On a large ship this is
            // the difference between a smooth canvas and a slideshow.
            int drawn = 0;
            for (int i = 0; i < nodes.Count && drawn < VgsConstants.MaxRenderedBlocks; i++)
            {
                Node n = nodes[i];
                Vector2 screen = GraphToScreen(n.Position, topLeft);
                Vector2 nodeSize = new Vector2(VgsConstants.NodeWidth * _zoom, VgsConstants.NodeHeight * _zoom);

                if (screen.X > size.X || screen.Y > size.Y) continue;
                if (screen.X + nodeSize.X < 0f || screen.Y + nodeSize.Y < 0f) continue;

                DrawNode(n, screen, nodeSize);
                drawn++;
            }

            if (nodes.Count > drawn)
            {
                VgsScreenDraw.TextCentered(
                    GetCullText(drawn, nodes.Count),
                    new Vector2(topLeft.X + size.X * 0.5f, topLeft.Y + size.Y - 0.04f),
                    0.62f, ColTextDim);
            }
        }

        private void DrawNode(Node node, Vector2 screenPos, Vector2 size)
        {
            bool selected = ReferenceEquals(node, _selected);

            VgsScreenDraw.Rect(screenPos, size, ColNode);
            VgsScreenDraw.Border(screenPos, size, selected ? ColNodeSelected : ColNodeBorder,
                                 selected ? Hairline * 2f : Hairline);

            // Ports: a short tick per port on the left (inputs) and right (outputs) edge.
            // Drawn before the text so the label sits on top.
            DrawPorts(node, screenPos, size);

            // Title.
            float titleScale = 0.62f * Math.Min(1.6f, Math.Max(0.75f, _zoom));
            VgsScreenDraw.Text(node.DisplayName, screenPos + new Vector2(size.X * 0.06f, size.Y * 0.18f),
                               titleScale, ColText);

            // Subtitle: subtype plus a port summary, which is what tells the user at a glance
            // whether this block can participate in the logic at all. Precomputed by the
            // graph builder — formatting it here would allocate per node per frame.
            VgsScreenDraw.Text(node.Subtitle, screenPos + new Vector2(size.X * 0.06f, size.Y * 0.52f),
                               titleScale * 0.78f, ColTextDim);
        }

        private static void DrawPorts(Node node, Vector2 screenPos, Vector2 size)
        {
            float tick = Math.Max(0.0035f, 0.006f * size.X / VgsConstants.NodeWidth);

            for (int i = 0; i < node.Inputs.Count; i++)
            {
                float y = screenPos.Y + size.Y * node.Inputs[i].Anchor;
                VgsScreenDraw.Rect(new Vector2(screenPos.X - tick * 0.5f, y - tick * 0.5f),
                                   new Vector2(tick, tick), ColPortIn);
            }

            for (int i = 0; i < node.Outputs.Count; i++)
            {
                float y = screenPos.Y + size.Y * node.Outputs[i].Anchor;
                VgsScreenDraw.Rect(new Vector2(screenPos.X + size.X - tick * 0.5f, y - tick * 0.5f),
                                   new Vector2(tick, tick), ColPortOut);
            }
        }

        private void DrawBottomBar()
        {
            Vector2 barTopLeft, barSize;
            GetBottomBarRect(out barTopLeft, out barSize);

            VgsScreenDraw.Rect(barTopLeft, barSize, ColBottomBar);
            VgsScreenDraw.Rect(barTopLeft, new Vector2(1f, Hairline), ColGridMajor);

            VgsScreenDraw.Text(GetStatusText(),
                               new Vector2(0.02f, barTopLeft.Y + barSize.Y * 0.18f), 0.7f, ColText);
            VgsScreenDraw.Text(VgsText.Hint,
                               new Vector2(0.02f, barTopLeft.Y + barSize.Y * 0.54f), 0.55f, ColTextDim);

            DrawCloseButton();
        }

        /// <summary>
        /// Draws the Close button. The screen-space API has no controls, so this is a filled
        /// rectangle with a border and a centred label; the click is handled in
        /// <see cref="HandleCloseButton"/> against the same rectangle.
        /// </summary>
        private void DrawCloseButton()
        {
            Vector2 topLeft, size;
            GetCloseButtonRect(out topLeft, out size);

            VgsScreenDraw.Rect(topLeft, size, _closeHot ? ColNodeBorder : ColNode);
            VgsScreenDraw.Border(topLeft, size, ColNodeSelected, Hairline);

            VgsScreenDraw.TextCentered(VgsText.CloseButton,
                                       topLeft + new Vector2(size.X * 0.5f, size.Y * 0.5f),
                                       0.7f, ColText);
        }

        private void DrawHeader()
        {
            VgsScreenDraw.Rect(Vector2.Zero, new Vector2(1f, VgsConstants.HeaderHeight), ColBottomBar);
            VgsScreenDraw.Text(VgsText.EditorTitle, new Vector2(0.02f, 0.014f), 0.9f, ColText);

            string gridName = GetGridName();
            if (gridName == null) return;

            // Right-aligned: measured so the text ends at the margin.
            VgsScreenDraw.TextRight(gridName, new Vector2(0.98f, 0.018f), 0.6f, ColTextDim);
        }

        // ------------------------------------------------------------------ cached strings

        /// <summary>
        /// Builds the status line only when a value it shows actually changed. Called from
        /// <see cref="OnDraw"/>, i.e. every frame, so an unconditional rebuild here would
        /// allocate a string per frame for the whole time the editor is open.
        /// </summary>
        private string GetStatusText()
        {
            int zoomPercent = (int)(_zoom * 100);
            int nodeCount = _graph.NodeCount;
            int edgeCount = _graph.EdgeCount;

            if (_statusText != null
                && _statusKeyNodes == nodeCount
                && _statusKeyEdges == edgeCount
                && _statusKeyZoom == zoomPercent)
            {
                return _statusText;
            }

            _statusKeyNodes = nodeCount;
            _statusKeyEdges = edgeCount;
            _statusKeyZoom = zoomPercent;
            _statusText = string.Format(VgsText.StatusFormat, nodeCount, edgeCount, zoomPercent);
            return _statusText;
        }

        /// <summary>Builds the culling notice only when the drawn/total counts change.</summary>
        private string GetCullText(int drawn, int total)
        {
            if (_cullText != null && _cullKeyDrawn == drawn && _cullKeyTotal == total) return _cullText;

            _cullKeyDrawn = drawn;
            _cullKeyTotal = total;
            _cullText = string.Format(VgsText.CullingFormat, drawn, total);
            return _cullText;
        }

        /// <summary>
        /// Reads the grid name once, outside the draw path. Called from <see cref="OnOpened"/>.
        ///
        /// CustomName is a live game property, so it is read here and cached; drawing must only
        /// consume the snapshot (ADR-0003). The read is guarded because the grid can be gone by
        /// the time the screen opens — the ship may have been destroyed between the terminal
        /// click and this call — and a missing name is not worth an exception.
        /// </summary>
        private void ResolveGridName()
        {
            string name = null;
            try
            {
                name = _grid != null ? _grid.CustomName : null;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("gridname", "could not read grid name", e, 600);
            }

            _gridNameCache = string.IsNullOrEmpty(name) ? VgsText.GridFallbackName : name;
        }

        /// <summary>
        /// Grid name for the header. A cache read only — see <see cref="ResolveGridName"/> for
        /// why the world is not touched here.
        /// </summary>
        private string GetGridName()
        {
            return _gridNameCache;
        }
    }
}
