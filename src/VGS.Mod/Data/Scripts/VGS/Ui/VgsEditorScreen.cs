using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;
using Vgs.Core;
using Vgs.Game;
using Vgs.Model;

namespace Vgs.Ui
{
    /// <summary>
    /// Full-screen grid logic editor: block cards, connections between them, and the binding
    /// gesture that creates those connections.
    ///
    /// <b>Base class.</b> This derives from <see cref="MyModScreen"/>, the API Keen added in
    /// 1.211 for mod-defined screens, not from <c>MyGuiScreenBase</c>. That is not a style
    /// choice: the game compiles mod sources itself and rejects every member outside its IL
    /// whitelist, and the engine GUI types are outside it. Deriving from
    /// <c>MyGuiScreenBase</c> compiles locally and then fails at load with
    /// "The type or member 'MyGuiScreenBase' is prohibited". <see cref="MyModScreen"/> lives
    /// in <c>Sandbox.ModAPI</c>, which is whitelisted wholesale.
    ///
    /// <b>Interaction model</b> (three modes, one pointer):
    ///
    ///  * <i>idle</i> — left button selects and drags cards, right/middle button pans, wheel
    ///    zooms. Clicking the round "+" on a card switches to bind mode.
    ///  * <i>bind</i> — a line follows the pointer from the source card's future output dot.
    ///    The button does not have to be held. Left click on another card opens the action
    ///    menu for it; right click cancels; left click elsewhere cancels.
    ///  * <i>menu</i> — a list of the target's terminal actions. Left click on a row creates
    ///    the connection and leaves bind mode; right click cancels bind mode; wheel scrolls
    ///    long lists; left click anywhere else closes the menu but keeps bind mode.
    ///
    /// The mode is explicit state (<see cref="_bindSource"/>, <see cref="_menuTarget"/>)
    /// rather than a boolean per button, because the three modes are mutually exclusive: a
    /// drag started while binding would fight the line for the pointer, and a pan started
    /// while the menu is open would move the world out from under it.
    ///
    /// <b>Client-area coordinates.</b> Every rectangle in this file is 0..1 of the window,
    /// which is what the player thinks of as the screen and what keeps the layout resolution
    /// independent. <see cref="VgsScreenDraw"/> converts to the space the screen-space API
    /// actually uses — the safe-GUI rectangle, a centred 4:3 box — so no code here deals with
    /// that difference (ADR-0009).
    ///
    /// <b>No per-frame world access, no per-frame allocation.</b> Block data is read once
    /// into a <see cref="GridGraph"/>; drawing only reads that snapshot. Text that changes
    /// rarely (status line, culling notice, menu title) is formatted when it changes and
    /// reused — <see cref="OnDraw"/> runs every frame.
    ///
    /// <b>Every override is guarded.</b> These methods are called by the game; an exception
    /// escaping one of them breaks the session, not just this screen.
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
        private static readonly Color ColNodeHover = new Color(110, 190, 255, 255);
        private static readonly Color ColText = new Color(226, 232, 240, 255);
        private static readonly Color ColTextDim = new Color(140, 152, 170, 255);
        private static readonly Color ColPortIn = new Color(110, 190, 255, 255);
        private static readonly Color ColPortInFill = new Color(28, 44, 62, 255);
        private static readonly Color ColPortOut = new Color(120, 220, 160, 255);
        private static readonly Color ColPortOutFill = new Color(26, 48, 38, 255);
        private static readonly Color ColLine = new Color(120, 220, 160, 190);
        private static readonly Color ColLinePending = new Color(255, 170, 60, 235);

        // The "+" button is drawn inverted: a near-white disc with the plus cut out in the
        // card's own colour (ColNode). White because a control that starts a modal gesture has
        // to be the brightest thing on the canvas; inverted because a light glyph on a dark
        // disc disappears at low zoom, while a dark glyph on white stays legible.
        private static readonly Color ColPlusFill = new Color(244, 247, 252, 255);
        private static readonly Color ColPlusFillHot = new Color(255, 214, 140, 255);
        private static readonly Color ColMenu = new Color(26, 32, 42, 250);
        private static readonly Color ColMenuBorder = new Color(90, 106, 130, 255);
        private static readonly Color ColMenuRowHot = new Color(52, 66, 88, 255);
        private static readonly Color ColBottomBar = new Color(24, 29, 38, 255);

        /// <summary>Colour of a failure message: warm enough to read as a problem, dim enough
        /// not to shout from a bar the user looks at only when something goes wrong.</summary>
        private static readonly Color ColWarning = new Color(255, 176, 120, 255);

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

        // Interaction state: idle (all null), bind, or menu.
        private Node _draggedNode;
        private Vector2 _dragGrabOffset;
        private bool _dragging;
        private bool _panning;
        private Vector2 _panAnchor;
        private Node _selected;

        /// <summary>Source card of a bind in progress. Non-null means bind mode is active.</summary>
        private Node _bindSource;

        /// <summary>
        /// The exact port the bind will write into. Chosen when the bind starts, because a
        /// yes/no toolbar offers two ports for the same event and the user picked one by
        /// clicking "T" or "F" — the choice must not be re-derived later from the node.
        /// </summary>
        private Port _bindPort;

        /// <summary>Fire group the current bind belongs to; drives the hint text.</summary>
        private PortGroup _bindGroup;

        /// <summary>Card whose action menu is open. Non-null implies bind mode.</summary>
        private Node _menuTarget;

        /// <summary>Actions offered for the open menu, in terminal order. Reused between opens.</summary>
        private readonly List<Port> _menuRows = new List<Port>();

        /// <summary>
        /// Row captions, parallel to <see cref="_menuRows"/>.
        ///
        /// Pre-formatted when the menu opens rather than in the draw pass: the caption carries
        /// the current fan-in count, and building it per frame would allocate once per row per
        /// frame for as long as the menu is open.
        /// </summary>
        private readonly List<string> _menuLabels = new List<string>();

        private int _menuScroll;
        private int _menuHoverRow = -1;
        private Vector2 _menuAnchorScreen;

        /// <summary>Cursor position in client-area units, cached by the input pass for drawing.</summary>
        private Vector2 _mouse;

        private bool _mouseValid;
        private Node _hoverNode;
        private Node _hoverPlus;

        /// <summary>Which of the hovered node's bind buttons is under the pointer.</summary>
        private PortGroup _hoverGroup;

        /// <summary>
        /// Last failure to write a connection, and the frame it stops being shown.
        ///
        /// A connection is an item in the source block's toolbar (ADR-0011), and the game
        /// refuses some placements — the same action twice on one page, or a page list that is
        /// full. Silently doing nothing would read as a broken screen, so the reason is shown
        /// on the bottom bar for a few seconds.
        /// </summary>
        private string _warningText;

        private int _warningUntilFrame;

        /// <summary>
        /// Scratch buffer for wire routes. Reused so that drawing a connection allocates
        /// nothing once the list has grown to its longest route.
        /// </summary>
        private readonly List<Vector4> _route = new List<Vector4>(8);

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

        /// <summary>Menu title, formatted once when the menu opens. Draw only reads it.</summary>
        private string _menuTitle;

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
                // The only cached screen-space value is the menu anchor, and the menu rectangle
                // is re-clamped into the canvas on every access for exactly this reason.
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
                // Esc exits the deepest mode first — cancelling a bind is what the player
                // expects from Escape — and only closes the editor when there is none.
                if (_menuTarget != null || _bindSource != null)
                {
                    CancelBind();
                    return false;
                }

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
                _bindSource = null;
                _bindPort = null;
                _menuTarget = null;
                _menuRows.Clear();
                _menuLabels.Clear();
                _menuTitle = null;
                _statusText = null;
                _cullText = null;
                _gridNameCache = null;
                _hoverNode = null;
                _hoverPlus = null;
                _warningText = null;
                _mouseValid = false;
                _route.Clear();

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

        /// <summary>
        /// Rectangle of the action menu, in client-area coordinates.
        ///
        /// Computed on every access from the anchor and the current row count, and clamped
        /// into the canvas. Both the draw pass and the input pass call this one method, so the
        /// rows the user clicks are the rows that were drawn — the classic source of "the
        /// click lands one row off" is two copies of this arithmetic diverging.
        /// </summary>
        private void GetMenuRect(out Vector2 topLeft, out Vector2 size)
        {
            Vector2 canvasTopLeft, canvasSize;
            GetCanvasRect(out canvasTopLeft, out canvasSize);

            int rows = VisibleMenuRows;
            size = new Vector2(
                VgsConstants.ActionMenuWidth,
                VgsConstants.ActionMenuTitleHeight + rows * VgsConstants.ActionMenuRowHeight);

            const float margin = 0.01f;
            const float offset = 0.012f;

            float maxX = canvasTopLeft.X + canvasSize.X - size.X - margin;
            float maxY = canvasTopLeft.Y + canvasSize.Y - size.Y - margin;

            float x = _menuAnchorScreen.X + offset;
            float y = _menuAnchorScreen.Y + offset;

            if (x > maxX) x = maxX;
            if (y > maxY) y = maxY;
            if (x < canvasTopLeft.X + margin) x = canvasTopLeft.X + margin;
            if (y < canvasTopLeft.Y + margin) y = canvasTopLeft.Y + margin;

            topLeft = new Vector2(x, y);
        }

        private void GetMenuRowRect(int index, out Vector2 topLeft, out Vector2 size)
        {
            Vector2 menuTopLeft, menuSize;
            GetMenuRect(out menuTopLeft, out menuSize);

            size = new Vector2(menuSize.X, VgsConstants.ActionMenuRowHeight);
            topLeft = new Vector2(
                menuTopLeft.X,
                menuTopLeft.Y + VgsConstants.ActionMenuTitleHeight + index * VgsConstants.ActionMenuRowHeight);
        }

        /// <summary>
        /// Rows the menu shows at once — including the single informational row drawn when the
        /// target has no actions at all.
        /// </summary>
        private int VisibleMenuRows
        {
            get
            {
                int count = _menuRows.Count;
                if (count < 1) return 1;
                return count < VgsConstants.ActionMenuMaxRows ? count : VgsConstants.ActionMenuMaxRows;
            }
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
                ValidateInteractionState();
            }
            catch (Exception e)
            {
                VgsLog.Error("editor refresh failed", e);
            }
        }

        /// <summary>
        /// Drops references to nodes the refresh has removed.
        ///
        /// A refresh can delete the block the user is currently dragging, binding from, or
        /// picking an action for. Those fields are plain references, so they stay valid as
        /// objects after the node leaves the graph — which means the draw path would happily
        /// keep painting a card that no longer exists. The check is by entity id because that
        /// is the graph's own identity for a node.
        /// </summary>
        private void ValidateInteractionState()
        {
            if (_selected != null && !_graph.Contains(_selected.EntityId)) _selected = null;
            if (_draggedNode != null && !_graph.Contains(_draggedNode.EntityId)) _draggedNode = null;

            if (_bindSource != null && !_graph.Contains(_bindSource.EntityId)) CancelBind();

            // A bind holds a specific port object, chosen when it started. A refresh that
            // rebuilt the source's event list (its action set changed, or the block was
            // rebuilt) leaves that object orphaned, and the line would then point at a port
            // the node no longer has. Re-resolve by key instead of cancelling: the gesture is
            // still meaningful as long as the port survives.
            if (_bindSource != null && _bindPort != null)
            {
                Port live = _bindSource.FindEvent(_bindPort.Key);
                if (live == null) CancelBind();
                else _bindPort = live;
            }

            if (_menuTarget != null)
            {
                if (!_graph.Contains(_menuTarget.EntityId) || !SamePorts(_menuRows, _menuTarget.Actions))
                {
                    // The menu is bound to specific port objects; if the target's action list
                    // was rebuilt they are stale, and a click would connect the wrong action.
                    CancelBind();
                    return;
                }

                // The fan-in counts in the captions are a snapshot. A refresh can change them
                // (another block was ground down), and a caption that lies about how many
                // sources an action already has is worse than a caption built twice a second
                // while a menu is open. This runs in Update, never in Draw.
                FillMenuRows(_menuTarget);
            }
        }

        private static bool SamePorts(List<Port> rows, List<Port> ports)
        {
            if (rows.Count != ports.Count) return false;
            for (int i = 0; i < rows.Count; i++)
            {
                if (!ReferenceEquals(rows[i], ports[i])) return false;
            }
            return true;
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
                _mouseValid = VgsScreenDraw.TryCursor(out mouse);
                if (!_mouseValid) return;

                _mouse = mouse;

                if (HandleCloseButton(mouse)) return;

                if (_menuTarget != null) { HandleMenuInput(mouse); return; }
                if (_bindSource != null) { HandleBindInput(mouse); return; }

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

            _closeHot = PortGeometry.Contains(buttonTopLeft, buttonSize, mouse);

            if (!_closeHot || !MyAPIGateway.Input.IsNewLeftMousePressed()) return false;

            Close();
            return true;
        }

        private void HandleCanvasInput(Vector2 mouse)
        {
            Vector2 canvasTopLeft, canvasSize;
            GetCanvasRect(out canvasTopLeft, out canvasSize);

            bool insideCanvas = PortGeometry.Contains(canvasTopLeft, canvasSize, mouse);

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

            // ---- hover: the bind button and the card under the pointer ----
            UpdateHover(mouse, canvasTopLeft, canvasSize);

            if (leftNew && insideCanvas)
            {
                Vector2 graphPos = ScreenToGraph(mouse, canvasTopLeft);

                // The bind button wins over the card underneath: it sits on the card's right
                // edge and starting a drag instead of a bind would be maddening.
                PortGroup group;
                Node plus = HitTestBindButton(graphPos, out group);
                if (plus != null)
                {
                    BeginBind(plus, group);
                    return;
                }

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

        /// <summary>
        /// Input while a bind is in progress: the pointer picks the target instead of dragging
        /// or panning. The right button cancels — which is why the right button cannot pan
        /// here; the middle button still does.
        /// </summary>
        private void HandleBindInput(Vector2 mouse)
        {
            bool rightNew = MyAPIGateway.Input.IsNewRightMousePressed();
            if (rightNew)
            {
                CancelBind();
                return;
            }

            Vector2 canvasTopLeft, canvasSize;
            GetCanvasRect(out canvasTopLeft, out canvasSize);
            bool insideCanvas = PortGeometry.Contains(canvasTopLeft, canvasSize, mouse);

            // Middle-button pan keeps working so the user can reach a target off-screen
            // without losing the bind.
            bool middleDown = MyAPIGateway.Input.IsMiddleMousePressed();
            if (insideCanvas && middleDown)
            {
                if (!_panning)
                {
                    _panning = true;
                    _panAnchor = mouse;
                }
                else
                {
                    _camera -= (mouse - _panAnchor) / _zoom;
                    _panAnchor = mouse;
                }
                return;
            }
            if (_panning && !middleDown) _panning = false;

            Vector2 graphPos = ScreenToGraph(mouse, canvasTopLeft);

            // A pointer over the header or the bottom bar is not over a card, even if a card
            // happens to be scrolled underneath: the bars are chrome, and the click there is
            // a click on chrome.
            _hoverNode = insideCanvas ? HitTest(graphPos) : null;
            _hoverPlus = null;

            if (!MyAPIGateway.Input.IsNewLeftMousePressed()) return;

            Node target = _hoverNode;
            if (target == null)
            {
                // Clicking the void abandons the bind, the same way a rubber band in any
                // editor is dropped by clicking on nothing.
                CancelBind();
                return;
            }

            if (ReferenceEquals(target, _bindSource))
            {
                VgsLog.Debug("bind rejected: source and target are the same block");
                return;
            }

            OpenActionMenu(target, mouse);
        }

        /// <summary>Input while the action menu is open.</summary>
        private void HandleMenuInput(Vector2 mouse)
        {
            if (MyAPIGateway.Input.IsNewRightMousePressed())
            {
                CancelBind();
                return;
            }

            int hoverRow = MenuRowAt(mouse);
            _menuHoverRow = hoverRow;

            int wheel = MyAPIGateway.Input.DeltaMouseScrollWheelValue();
            if (wheel != 0) ScrollMenu(wheel > 0 ? -1 : 1);

            if (!MyAPIGateway.Input.IsNewLeftMousePressed()) return;

            Vector2 menuTopLeft, menuSize;
            GetMenuRect(out menuTopLeft, out menuSize);
            bool insideMenu = PortGeometry.Contains(menuTopLeft, menuSize, mouse);

            if (!insideMenu)
            {
                // A click next to the menu is a miss, not a decision: close the menu but keep
                // the bind, so a near-miss does not cost the user the source they picked.
                CloseActionMenu();
                return;
            }

            if (_menuRows.Count == 0)
            {
                // The target offers no actions at all, so this bind can never complete. Say so
                // and drop the whole gesture rather than leaving an unclickable menu on screen.
                VgsLog.Debug("bind cancelled: target has no terminal actions");
                CancelBind();
                return;
            }

            if (hoverRow < 0) return;   // title strip

            Port action = _menuRows[hoverRow];
            if (TryCompleteBind(action))
            {
                CloseActionMenu();
                CancelBind();
            }
        }

        // ------------------------------------------------------------------ bind mode

        private void BeginBind(Node source, PortGroup group)
        {
            if (source == null) return;

            Port port = source.PrimaryEventFor(group);
            if (port == null)
            {
                VgsLog.Debug("bind not started: block has no port for group " + group);
                return;
            }

            _bindSource = source;
            _bindPort = port;
            _bindGroup = group;
            _selected = source;
            _dragging = false;
            _draggedNode = null;
            _panning = false;
            CloseActionMenu();

            VgsLog.Debug("bind started from " + source.EntityId + " via " + port.Key);
        }

        private void CancelBind()
        {
            if (_bindSource != null) VgsLog.Debug("bind cancelled");
            _bindSource = null;
            _bindPort = null;
            _hoverNode = null;
            _panning = false;
            CloseActionMenu();
        }

        private void OpenActionMenu(Node target, Vector2 mouse)
        {
            if (target == null) return;

            _menuTarget = target;
            _menuAnchorScreen = mouse;
            _menuScroll = 0;
            _menuHoverRow = -1;

            // The target card keeps the hover outline while its menu is open, so it is obvious
            // which of several similar blocks the list belongs to.
            _hoverNode = target;
            _hoverPlus = null;

            _menuRows.Clear();
            _menuLabels.Clear();

            FillMenuRows(target);

            // Formatted once, here, because DrawActionMenu runs every frame and string.Format
            // in a draw path is an allocation per frame for as long as the menu is open.
            _menuTitle = string.Format(VgsText.ActionMenuTitleFormat, target.DisplayName);

            VgsLog.Debug("action menu for " + target.EntityId + ": " + _menuRows.Count + " action(s)");
        }

        /// <summary>
        /// Fills the menu rows and their captions for one target.
        ///
        /// Called when the menu opens and again on every grid refresh while it stays open,
        /// because the captions carry the current fan-in count. Both call sites are outside the
        /// draw path on purpose: <see cref="DrawActionMenu"/> must only read these lists.
        /// </summary>
        private void FillMenuRows(Node target)
        {
            _menuRows.Clear();
            _menuLabels.Clear();
            if (target == null) return;

            for (int i = 0; i < target.Actions.Count; i++)
            {
                Port action = target.Actions[i];
                _menuRows.Add(action);

                // Input ports are shared, so an action that already has sources is still a
                // valid choice — the row says how many lines are already there instead of
                // being disabled. A disabled row would also make the menu jump as soon as the
                // user connected something.
                int fanIn = _graph.CountEdgesToInput(target.EntityId, action.Key);
                _menuLabels.Add(fanIn > 0
                    ? action.DisplayName + string.Format(VgsText.ActionMenuFanInFormat, fanIn)
                    : action.DisplayName);
            }

            // The row count can shrink under an open menu; the scroll offset must not point
            // past the end, or the last rows become unreachable.
            int maxScroll = _menuRows.Count - VgsConstants.ActionMenuMaxRows;
            if (maxScroll < 0) maxScroll = 0;
            if (_menuScroll > maxScroll) _menuScroll = maxScroll;
        }

        private void CloseActionMenu()
        {
            _menuTarget = null;
            _menuRows.Clear();
            _menuLabels.Clear();
            _menuTitle = null;
            _menuHoverRow = -1;
            _menuScroll = 0;
        }

        private void ScrollMenu(int delta)
        {
            int maxScroll = _menuRows.Count - VgsConstants.ActionMenuMaxRows;
            if (maxScroll < 0) maxScroll = 0;

            _menuScroll += delta;
            if (_menuScroll > maxScroll) _menuScroll = maxScroll;
            if (_menuScroll < 0) _menuScroll = 0;
        }

        /// <summary>
        /// Index of the action row under the pointer, or -1. Returns an index into
        /// <see cref="_menuRows"/> (already offset by the scroll position).
        /// </summary>
        private int MenuRowAt(Vector2 mouse)
        {
            if (_menuRows.Count == 0) return -1;

            Vector2 menuTopLeft, menuSize;
            GetMenuRect(out menuTopLeft, out menuSize);
            if (!PortGeometry.Contains(menuTopLeft, menuSize, mouse)) return -1;

            float local = mouse.Y - menuTopLeft.Y - VgsConstants.ActionMenuTitleHeight;
            if (local < 0f) return -1;

            int row = (int)(local / VgsConstants.ActionMenuRowHeight);
            if (row < 0 || row >= VisibleMenuRows) return -1;

            int index = row + _menuScroll;
            return index < _menuRows.Count ? index : -1;
        }

        /// <summary>
        /// Creates the connection the bind was aiming at.
        ///
        /// The order is deliberate: the item goes into the source block's real toolbar first,
        /// and only then into the graph. The toolbar is what makes the connection work
        /// (ADR-0011), so a graph edge without an item would be a line that does nothing. If
        /// the game refuses the placement — every page already holds that action, or the page
        /// list is full — the user is told why and nothing is changed.
        ///
        /// Returns true only when the block really holds the connection afterwards. A click
        /// that could not be satisfied returns false, so the menu stays open and the user can
        /// pick another action instead of wondering whether the click registered.
        /// </summary>
        private bool TryCompleteBind(Port action)
        {
            Node source = _bindSource;
            Node target = _menuTarget;
            Port port = _bindPort;
            if (source == null || target == null || action == null || port == null) return false;

            IMyTerminalBlock sourceBlock = ResolveBlock(source.EntityId);
            IMyTerminalBlock targetBlock = ResolveBlock(target.EntityId);
            if (sourceBlock == null || targetBlock == null)
            {
                Warn(VgsText.WarnBlockGone);
                return false;
            }

            ToolbarCell cell;
            ToolbarWriter.WriteResult result =
                ToolbarWriter.TryWrite(sourceBlock, source, port, targetBlock, action.Key, out cell);

            if (result != ToolbarWriter.WriteResult.Ok)
            {
                Warn(DescribeWriteFailure(result, source, target, action));
                return false;
            }

            if (!_graph.TryConnect(source.EntityId, port.Key, target.EntityId, action.Key, cell))
            {
                // The graph refused an item the block now holds: put the cell back the way it
                // was rather than leaving wiring the editor does not show.
                VgsLog.Warn("bind rejected by graph: " + port.Key + " -> " + action.Key + " @ " + cell);
                ToolbarWriter.TryClear(sourceBlock, source, cell);
                Warn(VgsText.WarnRejected);
                return false;
            }

            // The new connection changes which port bars exist, so the stacks are recomputed.
            _graph.LayoutPorts();

            VgsLog.Info("connected " + source.EntityId + " (" + port.Key + ") -> "
                        + target.EntityId + " (" + action.Key + ") @ " + cell);
            return true;
        }

        /// <summary>
        /// Turns a write failure into the sentence the user sees. Each reason has its own
        /// wording because they call for different actions: a full toolbar needs a page freed,
        /// a duplicate needs a different target action, a vanished action needs a refresh.
        /// </summary>
        private static string DescribeWriteFailure(ToolbarWriter.WriteResult result, Node source,
                                                   Node target, Port action)
        {
            switch (result)
            {
                case ToolbarWriter.WriteResult.NotDrivable:
                    return string.Format(VgsText.WarnNotDrivableFormat, source.DisplayName);
                case ToolbarWriter.WriteResult.ActionOnEveryPage:
                    return string.Format(VgsText.WarnEveryPageFormat, action.DisplayName, source.DisplayName);
                case ToolbarWriter.WriteResult.NoFreeCell:
                    return string.Format(VgsText.WarnNoFreeCellFormat, source.DisplayName);
                case ToolbarWriter.WriteResult.ActionUnavailable:
                    return string.Format(VgsText.WarnActionGoneFormat, target.DisplayName, action.DisplayName);
                default:
                    return VgsText.WarnRejected;
            }
        }

        /// <summary>Shows a failure on the bottom bar and records it in the log.</summary>
        private void Warn(string text)
        {
            _warningText = text;
            _warningUntilFrame = VgsSession.Frame + VgsConstants.WarningFrames;
            VgsLog.Warn(text);
        }

        /// <summary>
        /// Resolves a node back to its live block. The entity id can be stale — the block may
        /// have been destroyed since the last refresh — so the result is checked, never assumed.
        /// </summary>
        private static IMyTerminalBlock ResolveBlock(long entityId)
        {
            try
            {
                return MyAPIGateway.Entities.GetEntityById(entityId) as IMyTerminalBlock;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("resolve-block", "could not resolve block", e, 120);
                return null;
            }
        }

        // ------------------------------------------------------------------ hit testing

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

        /// <summary>Topmost node under a graph-space point, or null. Ports count as the node.</summary>
        private Node HitTest(Vector2 graphPoint)
        {
            IList<Node> nodes = _graph.Nodes;
            // Iterate backwards so the visually topmost (last drawn) node wins.
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                if (PortGeometry.HitNode(nodes[i], graphPoint)) return nodes[i];
            }
            return null;
        }

        /// <summary>Topmost node whose bind button is under the pointer, and which one.</summary>
        private Node HitTestBindButton(Vector2 graphPoint, out PortGroup group)
        {
            group = PortGroup.Single;
            float aspect = VgsScreenDraw.ClientAspect;
            IList<Node> nodes = _graph.Nodes;
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                PortGroup hit;
                if (PortGeometry.HitBindButton(nodes[i], graphPoint, aspect, out hit))
                {
                    group = hit;
                    return nodes[i];
                }
            }
            return null;
        }

        /// <summary>
        /// Refreshes the hover targets used for the highlight of a card and of its bind button.
        ///
        /// Hover is recomputed from the pointer every input pass rather than cached until a
        /// move event: the canvas pans and zooms under a still pointer, so "under the pointer"
        /// changes without the pointer moving.
        /// </summary>
        private void UpdateHover(Vector2 mouse, Vector2 canvasTopLeft, Vector2 canvasSize)
        {
            if (!PortGeometry.Contains(canvasTopLeft, canvasSize, mouse))
            {
                _hoverNode = null;
                _hoverPlus = null;
                return;
            }

            Vector2 graphPos = ScreenToGraph(mouse, canvasTopLeft);

            PortGroup group;
            _hoverPlus = HitTestBindButton(graphPos, out group);
            _hoverGroup = group;

            // A card under the button is not "hovered": the button is the thing about to be
            // clicked, and highlighting the card as well would suggest a drag.
            _hoverNode = _hoverPlus == null ? HitTest(graphPos) : null;
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

                // Order matters: lines go under the cards so a connection never crosses a
                // block's text; cards go under their own port bars.
                DrawEdges(canvasTopLeft, canvasSize);
                DrawPendingLine(canvasTopLeft, canvasSize);
                DrawNodes(canvasTopLeft, canvasSize);

                DrawBottomBar();
                DrawHeader();

                if (_menuTarget != null) DrawActionMenu();
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

                Color c = (row % 5 == 0) ? ColGridMajor : ColGridMinor;
                VgsScreenDraw.Rect(
                    new Vector2(topLeft.X, topLeft.Y + y),
                    new Vector2(size.X, Hairline),
                    c);
            }
        }

        /// <summary>
        /// Draws every connection as an orthogonal route from an output dot to an input dot.
        ///
        /// It walks <see cref="GridGraph.Lines"/>, not <see cref="GridGraph.Edges"/>: the same
        /// event may drive the same action several times (one toolbar page each), and those
        /// repeats are separate connections but the same line between the same two dots. The
        /// de-duplication is done once by <see cref="GridGraph.LayoutPorts"/>, so this per-frame
        /// path stays a single pass.
        ///
        /// A connection whose ports have no drawing slot (the block has more ports than
        /// <see cref="GridGraph.MaxPortsPerSide"/>) is skipped: it exists in the graph but has
        /// no dot to attach to, and drawing it from nowhere would be a lie.
        /// </summary>
        private void DrawEdges(Vector2 topLeft, Vector2 size)
        {
            IList<Edge> lines = _graph.Lines;
            int drawn = 0;

            for (int i = 0; i < lines.Count && drawn < VgsConstants.MaxRenderedEdges; i++)
            {
                Edge edge = lines[i];

                Node source = _graph.GetNode(edge.SourceEntityId);
                Node target = _graph.GetNode(edge.TargetEntityId);
                if (source == null || target == null) continue;

                Port outPort = source.FindEvent(edge.SourcePortKey);
                Port inPort = target.FindAction(edge.TargetPortKey);
                if (outPort == null || inPort == null) continue;

                Vector2 from, to;
                float aspect = VgsScreenDraw.ClientAspect;
                if (!PortGeometry.TryPortDot(source, outPort, aspect, out from)) continue;
                if (!PortGeometry.TryPortDot(target, inPort, aspect, out to)) continue;

                DrawRoute(GraphToScreen(from, topLeft), GraphToScreen(to, topLeft),
                          ColLine, topLeft, size);
                drawn++;
            }
        }

        /// <summary>
        /// Draws the live line from the source card to the pointer while binding.
        ///
        /// It starts at the dot the connection *will* get, not at the pointer's origin, so the
        /// moment the connection is made nothing jumps: the port bar appears exactly where the
        /// line already was.
        /// </summary>
        private void DrawPendingLine(Vector2 topLeft, Vector2 size)
        {
            if (_bindSource == null || _bindPort == null || !_mouseValid) return;

            Vector2 from;
            if (!PortGeometry.TryPendingSourceDot(_bindSource, _bindPort,
                                                  VgsScreenDraw.ClientAspect, out from)) return;

            Vector2 start = GraphToScreen(from, topLeft);
            Vector2 end = _mouse;

            DrawRoute(start, end, ColLinePending, topLeft, size);

            // A dot under the cursor makes the line read as "attached to the pointer" rather
            // than as a line that happens to end there.
            VgsScreenDraw.Disc(end, VgsConstants.PortDotRadius * _zoom, topLeft, size, ColLinePending);
        }

        private void DrawRoute(Vector2 from, Vector2 to, Color color, Vector2 clipTopLeft, Vector2 clipSize)
        {
            // The dots are already in screen space, so the gutter has to be too. It is a graph
            // constant scaled by zoom, which keeps the clearance between a dot and its vertical
            // run the same fraction of a card at every zoom level (ADR-0010).
            EdgeRouting.Build(from, to, VgsConstants.LineGutter * _zoom, _route);

            for (int i = 0; i < _route.Count; i++)
            {
                Vector4 segment = _route[i];
                VgsScreenDraw.Segment(
                    new Vector2(segment.X, segment.Y),
                    new Vector2(segment.Z, segment.W),
                    VgsConstants.LineThickness,
                    clipTopLeft, clipSize, color);
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
                Vector2 nodeSize = new Vector2(VgsConstants.NodeWidth, PortGeometry.TotalHeight(n)) * _zoom;

                if (screen.X > size.X || screen.Y > size.Y) continue;
                if (screen.X + nodeSize.X < 0f || screen.Y + nodeSize.Y < 0f) continue;

                DrawNode(n, screen, topLeft, size);
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

        /// <summary>
        /// Draws one card: name, subtype, the optional "+", and its port bars.
        ///
        /// The card is deliberately just a label. Ports are drawn *outside* it, as bars
        /// hanging underneath, and only the ones the user has actually connected — which is
        /// what keeps a block with forty terminal actions the same size as a block with four
        /// (see <see cref="PortGeometry"/>).
        /// </summary>
        private void DrawNode(Node node, Vector2 screenPos, Vector2 clipTopLeft, Vector2 clipSize)
        {
            bool selected = ReferenceEquals(node, _selected);
            bool hovered = ReferenceEquals(node, _hoverNode);

            Vector2 cardSize = new Vector2(VgsConstants.NodeWidth, VgsConstants.NodeHeight) * _zoom;

            // The card is a single filled rectangle. The port bars are separate rectangles
            // below it and must *not* be covered by this fill, or the stack would read as one
            // tall block instead of as ports attached to the card's bottom edge.
            VgsScreenDraw.RectClipped(screenPos, cardSize, clipTopLeft, clipSize, ColNode);

            Color border = selected ? ColNodeSelected : (hovered ? ColNodeHover : ColNodeBorder);
            VgsScreenDraw.BorderClipped(screenPos, cardSize, clipTopLeft, clipSize, border,
                                        selected ? Hairline * 2f : Hairline);

            float zoomText = CardTextScale;
            float pad = cardSize.X * 0.05f;
            float textRight = cardSize.X - pad;

            // The bind button occupies the right end of the card, so the labels must stop
            // before it instead of running underneath a circle that is drawn on top of them.
            //
            // The button is round on *screen*, which makes it an ellipse in layout units: its
            // horizontal half-extent is radius × aspect, not radius. Reserving the vertical
            // radius would leave the text running under the circle on any window that is not
            // square — which is every window.
            if (node.CanDriveBlocks)
            {
                float radius = PortGeometry.HasGroupButtons(node)
                    ? VgsConstants.GroupButtonRadius
                    : VgsConstants.PlusButtonRadius;

                Vector2 buttonCenter;
                if (PortGeometry.TryBindButton(node, FirstGroupOf(node), VgsScreenDraw.ClientAspect,
                                               out buttonCenter))
                {
                    float halfWidth = VgsScreenDraw.AspectX(radius);
                    float leftInCard = buttonCenter.X - node.Position.X - halfWidth;
                    textRight = (leftInCard - VgsConstants.PlusButtonTextGap) * _zoom;
                }
            }

            float textWidth = textRight - pad;
            if (textWidth < 0f) textWidth = 0f;

            VgsScreenDraw.Text(node.DisplayName,
                               screenPos + new Vector2(pad, cardSize.Y * 0.08f),
                               VgsConstants.NodeTitleScale * zoomText, ColText, textWidth);

            VgsScreenDraw.Text(node.SubtypeId,
                               screenPos + new Vector2(pad, cardSize.Y * 0.52f),
                               VgsConstants.NodeSubtitleScale * zoomText, ColTextDim, textWidth);

            DrawPortBars(node, clipTopLeft, clipSize);

            // The bind buttons are drawn last so they are never covered by a port bar of a
            // neighbouring card that happens to be drawn later.
            if (node.CanDriveBlocks) DrawBindButtons(node, clipTopLeft, clipSize);
        }

        /// <summary>The group whose button sits highest on the card; used to reserve text width.</summary>
        private static PortGroup FirstGroupOf(Node node)
        {
            return PortGeometry.HasGroupButtons(node) ? PortGroup.Yes : PortGroup.Single;
        }

        /// <summary>
        /// Draws the control that starts bind mode.
        ///
        /// Two shapes, and the difference is not decoration:
        ///
        ///  * a single-group toolbar (a timer) gets one round "+" centred on the card;
        ///  * a yes/no toolbar (a sensor, an event controller) gets a "T" and an "F" stacked
        ///    on the card's right edge, because the game keeps two independent lists of
        ///    actions and a connection has to say which one it joins.
        ///
        /// Round, not square, because it must not read as part of the card: it is a control
        /// that starts a modal gesture, and the one thing the user must never do is drag the
        /// card when they meant to drag a connection.
        ///
        /// The plus is <b>geometry, not a glyph</b>. A font glyph is positioned by its baseline
        /// and carries side bearings, so "centred" text is never exactly centred — which is the
        /// defect this replaces. Two rectangles, both built around the same centre, are centred
        /// by construction at every zoom and cost no font lookup. The T/F buttons carry a
        /// letter because the two groups are otherwise indistinguishable; a letter is the one
        /// thing a player already reads on the vanilla toolbar-config screen.
        /// </summary>
        private void DrawBindButtons(Node node, Vector2 clipTopLeft, Vector2 clipSize)
        {
            if (!PortGeometry.HasGroupButtons(node))
            {
                DrawPlusButton(node, PortGroup.Single, clipTopLeft, clipSize);
                return;
            }

            DrawGroupButton(node, PortGroup.Yes, VgsText.TrueButtonGlyph, clipTopLeft, clipSize);
            DrawGroupButton(node, PortGroup.No, VgsText.FalseButtonGlyph, clipTopLeft, clipSize);
        }

        private void DrawPlusButton(Node node, PortGroup group, Vector2 clipTopLeft, Vector2 clipSize)
        {
            Vector2 center;
            if (!PortGeometry.TryBindButton(node, group, VgsScreenDraw.ClientAspect, out center)) return;

            Vector2 screenCenter = GraphToScreen(center, clipTopLeft);
            float radius = VgsConstants.PlusButtonRadius * _zoom;
            float diameter = radius * 2f;

            bool hot = ReferenceEquals(node, _hoverPlus) && _hoverGroup == group;
            VgsScreenDraw.Disc(screenCenter, radius, clipTopLeft, clipSize,
                               hot ? ColPlusFillHot : ColPlusFill);

            // The plus is cut out of the disc in the card's colour, so it reads as a hole in a
            // white button rather than as a second element on top of one.
            Color glyph = ColNode;

            float arm = diameter * VgsConstants.PlusArmRatio;
            float stroke = diameter * VgsConstants.PlusStrokeRatio;

            // Horizontal arm. Its length is a Y measurement, so its X width goes through the
            // aspect correction; without it the plus would be stretched across the card.
            float armWidth = VgsScreenDraw.AspectX(arm);
            float strokeWidth = VgsScreenDraw.AspectX(stroke);

            VgsScreenDraw.RectClipped(
                new Vector2(screenCenter.X - armWidth * 0.5f, screenCenter.Y - stroke * 0.5f),
                new Vector2(armWidth, stroke),
                clipTopLeft, clipSize, glyph);

            VgsScreenDraw.RectClipped(
                new Vector2(screenCenter.X - strokeWidth * 0.5f, screenCenter.Y - arm * 0.5f),
                new Vector2(strokeWidth, arm),
                clipTopLeft, clipSize, glyph);
        }

        /// <summary>
        /// One of the two discs of a yes/no toolbar: the same inverted disc as the "+", with
        /// "T" or "F" in the card's colour.
        ///
        /// The letter is drawn by the text primitive rather than as geometry. That is the one
        /// place in this file where a glyph is preferred: a plus is two rectangles, but a
        /// letter is not, and the two groups need a label a player recognises.
        /// </summary>
        private void DrawGroupButton(Node node, PortGroup group, string glyph, Vector2 clipTopLeft,
                                     Vector2 clipSize)
        {
            Vector2 center;
            if (!PortGeometry.TryBindButton(node, group, VgsScreenDraw.ClientAspect, out center)) return;

            Vector2 screenCenter = GraphToScreen(center, clipTopLeft);
            float radius = VgsConstants.GroupButtonRadius * _zoom;

            bool hot = ReferenceEquals(node, _hoverPlus) && _hoverGroup == group;
            VgsScreenDraw.Disc(screenCenter, radius, clipTopLeft, clipSize,
                               hot ? ColPlusFillHot : ColPlusFill);

            VgsScreenDraw.TextCentered(glyph, screenCenter,
                                       VgsConstants.GroupButtonGlyphScale * CardTextScale, ColNode);
        }

        /// <summary>
        /// Zoom factor for text that lives on the canvas (card labels, port labels).
        ///
        /// Clamped on both ends and, crucially, <b>below</b> the old floor of 0.75: the card is
        /// now half as tall as it was, and two lines of text at the old floor overflow it once
        /// the user zooms out to <see cref="VgsConstants.MinZoom"/>. Text that is unreadable but
        /// inside its box is a better failure than text that is unreadable and spills over its
        /// neighbours.
        /// </summary>
        private float CardTextScale
        {
            get
            {
                if (_zoom < 0.55f) return 0.55f;
                if (_zoom > 1.6f) return 1.6f;
                return _zoom;
            }
        }

        /// <summary>
        /// Draws the port bars of one card: outputs first (flush with the card), then inputs
        /// (inset), each with its dot and its label.
        /// </summary>
        private void DrawPortBars(Node node, Vector2 clipTopLeft, Vector2 clipSize)
        {
            for (int i = 0; i < node.Events.Count; i++) DrawPortBar(node, node.Events[i], clipTopLeft, clipSize);
            for (int i = 0; i < node.Actions.Count; i++) DrawPortBar(node, node.Actions[i], clipTopLeft, clipSize);
        }

        private void DrawPortBar(Node node, Port port, Vector2 clipTopLeft, Vector2 clipSize)
        {
            if (port.Slot < 0) return;

            Vector2 topLeft, size;
            if (!PortGeometry.TryPortBarRect(node, port, out topLeft, out size)) return;

            Vector2 screenTopLeft = GraphToScreen(topLeft, clipTopLeft);
            Vector2 screenSize = size * _zoom;

            bool output = port.Direction == PortDirection.Output;
            Color fill = output ? ColPortOutFill : ColPortInFill;
            Color edge = output ? ColPortOut : ColPortIn;

            VgsScreenDraw.RectClipped(screenTopLeft, screenSize, clipTopLeft, clipSize, fill);
            VgsScreenDraw.BorderClipped(screenTopLeft, screenSize, clipTopLeft, clipSize, edge, Hairline);

            // Label, clipped to the bar so a long action name cannot spill over the next card.
            // The dot sits *inside* the bar now, so the label has to keep clear of it: an
            // output bar's text is pushed in from the right, an input bar's from the left.
            float dotSpace = VgsConstants.PortDotRadius * VgsScreenDraw.ClientAspect * 2f;
            float pad = VgsConstants.PortLabelPadding * _zoom;
            float dotPad = dotSpace * _zoom + pad;

            float labelLeft = output ? pad : dotPad;
            float labelWidth = screenSize.X - labelLeft - (output ? dotPad : pad);
            if (labelWidth > 0f)
            {
                VgsScreenDraw.Text(port.DisplayName,
                                   screenTopLeft + new Vector2(labelLeft, screenSize.Y * 0.16f),
                                   VgsConstants.NodeSubtitleScale * 0.82f * CardTextScale,
                                   ColText, labelWidth);
            }

            // The dot: a circle inside the bar, tangent to the edge the line uses — right for
            // an output, left for an input. Same shape both ways on purpose; what tells them
            // apart is the side they sit on, which is exactly the property the router relies
            // on, so an extra shape difference would only add a thing to learn.
            //
            // Drawn last so it sits on top of the bar's border and of its own label's edge.
            Vector2 dotCenter;
            if (!PortGeometry.TryPortDot(node, port, VgsScreenDraw.ClientAspect, out dotCenter)) return;
            VgsScreenDraw.Disc(GraphToScreen(dotCenter, clipTopLeft),
                               VgsConstants.PortDotRadius * _zoom, clipTopLeft, clipSize, edge);
        }

        /// <summary>
        /// Draws the action menu: the target's terminal actions, one row each, in the order
        /// the terminal shows them.
        /// </summary>
        private void DrawActionMenu()
        {
            Vector2 topLeft, size;
            GetMenuRect(out topLeft, out size);

            VgsScreenDraw.Rect(topLeft, size, ColMenu);
            VgsScreenDraw.Border(topLeft, size, ColMenuBorder, Hairline);

            VgsScreenDraw.Text(_menuTitle,
                               topLeft + new Vector2(VgsConstants.PortLabelPadding * 1.6f,
                                                     VgsConstants.ActionMenuTitleHeight * 0.22f),
                               VgsConstants.NodeTitleScale * 0.85f, ColText,
                               size.X - VgsConstants.PortLabelPadding * 3.2f);

            if (_menuRows.Count == 0)
            {
                Vector2 rowTopLeft, rowSize;
                GetMenuRowRect(0, out rowTopLeft, out rowSize);
                VgsScreenDraw.Text(VgsText.ActionMenuEmpty,
                                   rowTopLeft + new Vector2(VgsConstants.PortLabelPadding * 1.6f,
                                                            rowSize.Y * 0.22f),
                                   VgsConstants.NodeSubtitleScale * 0.9f, ColTextDim,
                                   rowSize.X - VgsConstants.PortLabelPadding * 3.2f);
                return;
            }

            for (int i = 0; i < VisibleMenuRows; i++)
            {
                int index = i + _menuScroll;
                if (index >= _menuRows.Count) break;

                Vector2 rowTopLeft, rowSize;
                GetMenuRowRect(i, out rowTopLeft, out rowSize);

                if (index == _menuHoverRow)
                {
                    VgsScreenDraw.Rect(rowTopLeft, rowSize, ColMenuRowHot);
                }

                VgsScreenDraw.Text(_menuLabels[index],
                                   rowTopLeft + new Vector2(VgsConstants.PortLabelPadding * 1.6f,
                                                            rowSize.Y * 0.22f),
                                   VgsConstants.NodeSubtitleScale * 0.95f, ColText,
                                   rowSize.X - VgsConstants.PortLabelPadding * 3.2f);
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

            // The hint line has two jobs and one slot: it either explains the gesture, or it
            // says why the last click did not produce a connection. The failure wins, because
            // the user is looking at a line that did not appear and needs to know why.
            string warning = ActiveWarning;
            if (warning != null)
            {
                VgsScreenDraw.Text(warning,
                                   new Vector2(0.02f, barTopLeft.Y + barSize.Y * 0.54f), 0.55f,
                                   ColWarning);
            }
            else
            {
                VgsScreenDraw.Text(BottomBarHint,
                                   new Vector2(0.02f, barTopLeft.Y + barSize.Y * 0.54f), 0.55f, ColTextDim);
            }

            DrawCloseButton();
        }

        /// <summary>
        /// The warning to show, or null once it has expired. Expiry is checked here rather
        /// than by a timer so nothing has to run between frames.
        /// </summary>
        private string ActiveWarning
        {
            get
            {
                if (_warningText == null) return null;
                if (VgsSession.Frame > _warningUntilFrame) { _warningText = null; return null; }
                return _warningText;
            }
        }

        /// <summary>
        /// Second line of the bottom bar. Bind mode replaces the static hint, because while
        /// binding the only thing the user needs to know is how to finish or cancel — and on
        /// a yes/no toolbar, which of the two lists the connection is going into.
        /// </summary>
        private string BottomBarHint
        {
            get
            {
                if (_bindSource == null) return VgsText.Hint;
                if (_bindGroup == PortGroup.Yes) return VgsText.BindHintTrue;
                if (_bindGroup == PortGroup.No) return VgsText.BindHintFalse;
                return VgsText.BindHint;
            }
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
