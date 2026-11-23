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
    /// <b>Interaction model</b> (four modes, one pointer):
    ///
    ///  * <i>idle</i> — left button selects and drags cards, right/middle button pans, wheel
    ///    zooms. Clicking one of the card's right-hand buttons switches to bind or watch mode
    ///    (ADR-0020), except the first one, which leaves the schema for the block's terminal
    ///    (ADR-0021); clicking the "x" on a port deletes that port's connections. A group's
    ///    title strip is a drag handle: grabbing it moves every member card by the same offset,
    ///    keeping the cluster's internal layout (ADR-0016).
    ///  * <i>bind</i> — a line follows the pointer from the source card's future output dot.
    ///    The button does not have to be held. Left click on another card opens the action
    ///    menu for it; right click cancels; left click elsewhere cancels.
    ///  * <i>watch</i> — the same gesture for observation (ADR-0017): a dashed line follows the
    ///    pointer from the controller's bell, and the block clicked joins its watch list.
    ///  * <i>menu</i> — a list of the target's terminal actions. Left click on a row creates
    ///    the connection and leaves bind mode; right click cancels bind mode; wheel scrolls
    ///    long lists; left click anywhere else closes the menu but keeps bind mode.
    ///
    /// The mode is explicit state (<see cref="_bindSource"/>, <see cref="_watchSource"/>,
    /// <see cref="_menuTarget"/>) rather than a boolean per button, because the modes are
    /// mutually exclusive: a drag started while binding would fight the line for the pointer,
    /// and a pan started while the menu is open would move the world out from under it.
    ///
    /// <b>Client-area coordinates.</b> Every rectangle in this file is 0..1 of the window,
    /// which is what the player thinks of as the screen and what keeps the layout resolution
    /// independent. <see cref="VgsScreenDraw"/> converts to the space the screen-space API
    /// actually uses — the safe-GUI rectangle, a centred 4:3 box — so no code here deals with
    /// that difference (ADR-0009).
    ///
    /// <b>The view is remembered, and it belongs to the grid.</b> Camera, zoom and the "show
    /// invisible" toggle are read from the grid's own storage when the screen opens and written
    /// back while they change and when it closes (ADR-0022, <see cref="GridViewStore"/>). They
    /// live on the construct, not on the cockpit the screen was opened from, so a second cockpit
    /// of the same ship shows the same view.
    ///
    /// <b>No per-frame world access, no per-frame allocation.</b> Block data is read once
    /// into a <see cref="GridGraph"/>; drawing only reads that snapshot. Text that changes
    /// rarely (the header statistics, the search terms, menu captions) is formatted when it
    /// changes and reused — <see cref="OnDraw"/> runs every frame.
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

        // A watch line is a third kind of line: the controller *observes* a block instead of
        // calling it (ADR-0017). It is drawn in a warm yellow, dashed, so it cannot be confused
        // with a connection at a glance — the fill is the same family as the watch bar's, which
        // is what ties the line to the port it leaves from.
        private static readonly Color ColWatch = new Color(240, 200, 90, 255);
        private static readonly Color ColWatchFill = new Color(62, 52, 24, 255);
        private static readonly Color ColWatchLine = new Color(240, 200, 90, 210);

        /// <summary>
        /// Colour of a refused watch line: the same line the player is dragging, turned red for
        /// about a second (ADR-0017 §5). Red because the failure is not "you missed", it is "this
        /// block cannot be attached" — and the line itself is the only thing under the cursor.
        /// </summary>
        private static readonly Color ColWatchError = new Color(255, 90, 90, 245);

        // A group is a light frame *behind* its members' cards (ADR-0015). Light, because it
        // has to be readable on the dark canvas without competing with the cards it contains:
        // a card is what the player acts on, the frame only says "these belong together".
        //
        // The fill is semi-transparent, so what the player sees is the fill composited over
        // the canvas: (150,158,170) at alpha 110 over (18,22,29) lands near (75,80,88), which
        // is clearly lighter than a card (38,45,58) — the first value tried here, (96,104,118)
        // at alpha 70, composited to (39,45,53) and was indistinguishable from the card.
        // The title strip is deliberately *darker* than the fill: it carries the group's name
        // in the editor's light text colour, and light text on the light fill would not read.
        private static readonly Color ColGroupFill = new Color(150, 158, 170, 110);
        private static readonly Color ColGroupBorder = new Color(196, 206, 220, 220);
        private static readonly Color ColGroupTitle = new Color(90, 98, 112, 240);
        private static readonly Color ColGroupSelected = new Color(255, 170, 60, 255);

        // A card's buttons are bare icons in the light "control" colour, warming to ColPlusFillHot
        // under the pointer. Light, because a control that starts a modal gesture has to be the
        // brightest thing on the canvas; bare, because an icon carries its own shape and a disc
        // behind it would be a second, louder shape (ADR-0020, and ADR-0019 §5 for the same
        // reversal on the port's "x").
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
        ///
        /// The reference height is shared with the port-control gap
        /// (<see cref="VgsConstants.ReferenceHeightPixels"/>): both are "N pixels at 1080p", and
        /// two different references would make the editor's hairlines and its port gaps disagree
        /// on every other resolution.
        /// </summary>
        private const float Hairline = 1f / VgsConstants.ReferenceHeightPixels;

        /// <summary>
        /// The editor currently on screen, if any.
        ///
        /// Tracked explicitly because the screen API offers no way to enumerate open screens,
        /// and <see cref="MyModScreen.IsOpen"/> only answers for an instance we already hold.
        /// </summary>
        private static VgsEditorScreen _openEditor;

        /// <summary>
        /// The graph the screen draws. Not readonly: the "show invisible" mode (ADR-0023) decides
        /// whether blocks with the "Show in VGS" flag off are part of the graph at all, so
        /// switching the mode rebuilds it. Everything that holds a node across that rebuild is
        /// re-validated by <see cref="ValidateInteractionState"/>.
        /// </summary>
        private GridGraph _graph;

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

        /// <summary>
        /// Group being dragged by its title strip, or null (ADR-0016).
        ///
        /// A group has no card of its own — its frame is derived from its members' cards — so it
        /// cannot be dragged the way a block is (write a position, let the picture follow).
        /// Dragging a group means translating every member card by the same offset, which
        /// <see cref="GridGraph.OffsetGroupMembers"/> does; the frame then follows by itself on
        /// the next layout pass.
        ///
        /// Kept apart from <see cref="_draggedNode"/> rather than folded into it, because the
        /// two drags write to different things: one position on one node, versus one offset on
        /// every member. A shared field would need a branch at each of the four places the drag
        /// is used (start, threshold, move, save).
        /// </summary>
        private Node _draggedGroup;

        /// <summary>
        /// Pointer position, in graph units, where the group drag started and where it last was.
        ///
        /// Both are needed and they are not the same point: the anchor measures the drag
        /// threshold ("has the pointer moved far enough to count as a drag"), while the last
        /// position measures this frame's movement. Using the anchor for both would apply the
        /// whole displacement again on every pointer move.
        /// </summary>
        private Vector2 _groupDragAnchor;
        private Vector2 _groupDragLast;

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

        /// <summary>
        /// Controller whose watch list is being extended, or null. Non-null means watch mode is
        /// active (ADR-0017).
        ///
        /// A field of its own rather than a flag on the bind: the two gestures are mutually
        /// exclusive and they write to different things — a bind writes an item into a toolbar,
        /// a watch writes an id into a controller's list. Sharing one field would force every
        /// input path to ask which kind of gesture it is looking at.
        /// </summary>
        private Node _watchSource;

        /// <summary>
        /// Frame until which the watch line is drawn red — the refusal of the last click.
        ///
        /// The line is the only feedback under the cursor at the moment of a refused attach, so
        /// it turns red instead of the mode ending: the next click can still land on a block
        /// that fits, and the player does not have to press the bell again.
        /// </summary>
        private int _watchErrorUntilFrame;

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

        /// <summary>
        /// Node whose card button row is under the pointer, or null, and which button of it.
        ///
        /// One pair of fields for the whole row rather than one per gesture: the row is a single
        /// control group (ADR-0020), and a highlight that could name two buttons at once — one
        /// "bind" field and one "watch" field disagreeing about the same pointer — is exactly the
        /// defect the pair replaces.
        /// </summary>
        private Node _hoverButton;

        private CardButtonKind _hoverButtonKind;

        /// <summary>
        /// Reusable buffer for one card's button row while drawing (ADR-0021).
        ///
        /// A field rather than a local array because <see cref="OnDraw"/> runs every frame for
        /// every visible card: allocating five enums per card per frame is exactly the kind of
        /// garbage R010 forbids. Nothing outside <see cref="DrawCardButtons"/> may rely on its
        /// contents — it is overwritten by the next card.
        /// </summary>
        private readonly CardButtonKind[] _cardButtonRow =
            new CardButtonKind[PortGeometry.MaxCardButtonCount];

        /// <summary>
        /// Node whose port carries the remove button under the pointer, or null. Tracked
        /// separately from <see cref="_hoverNode"/> because the button belongs to a *port*,
        /// not to the card: the highlight has to land on the small disc, not on the whole bar.
        /// </summary>
        private Node _hoverRemoveNode;

        /// <summary>Port whose remove button is under the pointer, or null.</summary>
        private Port _hoverRemovePort;

        /// <summary>Controller whose watch port carries the "x" under the pointer, or null.</summary>
        private Node _hoverWatchRemoveNode;

        /// <summary>Watch target whose "x" is under the pointer, or null.</summary>
        private WatchTarget _hoverWatchRemoveTarget;

        /// <summary>
        /// Edges collected for a removal click. Reused so that clicking a remove button does
        /// not allocate: the list is emptied before each collection and after each use.
        /// </summary>
        private readonly List<Edge> _removeBuffer = new List<Edge>();

        /// <summary>
        /// Height of one line of port-label text at <see cref="_portLabelHeightZoom"/>, in
        /// client-area units. Cached per zoom level: see <see cref="PortLabelHeight"/>.
        /// </summary>
        private float _portLabelHeight;

        private float _portLabelHeightZoom = -1f;

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
        private int _statusKeyGroups = -1;
        private int _statusKeyEdges = -1;
        private int _statusKeyZoom = -1;

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

        // ------------------------------------------------------------------ view state (ADR-0022)

        /// <summary>
        /// Camera and zoom last written to the grid, and the frame of that write.
        ///
        /// Kept so the throttled save in <see cref="OnUpdate"/> can tell "nothing changed" from
        /// "the player is still panning": comparing against the last written value costs two
        /// float comparisons and saves a string allocation plus a dictionary write every frame.
        /// </summary>
        private Vector2 _savedCamera;
        private float _savedZoom = -1f;
        private bool _savedShowHidden;
        private int _lastViewSaveFrame;

        // ------------------------------------------------------------------ modes and search (ADR-0023)

        /// <summary>
        /// True while blocks whose "Show in VGS" flag is off are drawn as well. Part of the view
        /// state, so it is saved with the camera and comes back with it (ADR-0022).
        /// </summary>
        private bool _showHidden;

        /// <summary>True while the modes popup is open.</summary>
        private bool _modesMenuOpen;

        /// <summary>True while the pointer is over the modes button; drives its highlight.</summary>
        private bool _modesHot;

        /// <summary>True while the pointer is over the modes menu's only row.</summary>
        private bool _modesRowHot;

        /// <summary>True while the pointer is over the search field.</summary>
        private bool _searchHot;

        /// <summary>True while the pointer is over the search field's clear button.</summary>
        private bool _searchClearHot;

        /// <summary>
        /// The search field's editing state: the query, the caret and the selection (ADR-0024).
        ///
        /// The state lives in <see cref="TextEditor"/> rather than in three fields here because
        /// "where the caret is" and "what is selected" have to agree at every keystroke, and the
        /// rules for that are the part worth testing without the game.
        /// </summary>
        private readonly TextEditor _searchEditor = new TextEditor(VgsConstants.SearchMaxLength);

        /// <summary>
        /// The query split into terms, rebuilt only when the editor's text changes.
        /// <see cref="BlockSearch.Matches"/> runs for every drawn card on every frame, so
        /// splitting there would allocate a string per card per frame (R010).
        /// </summary>
        private readonly List<string> _searchTerms = new List<string>(4);

        /// <summary>
        /// Where the caret and the selection edges sit horizontally, in client-area units relative
        /// to the field's text origin, and the horizontal scroll that keeps the caret visible.
        ///
        /// Measured when the text, the caret or the field's width changes and reused across frames:
        /// the draw path runs every frame, while these numbers only move when the player types,
        /// clicks or holds an arrow key. Measuring them per frame would call into the font metrics
        /// for a caret that has not moved.
        ///
        /// <see cref="_fieldLayoutValid"/> false means "recompute on the next draw".
        /// </summary>
        private bool _fieldLayoutValid;
        private float _fieldCaretX;
        private float _fieldSelectionStartX;
        private float _fieldSelectionEndX;
        private float _fieldScrollX;

        /// <summary>Width the layout was computed for; a change (the "x" appearing) invalidates it.</summary>
        private float _fieldAreaWidth = -1f;

        /// <summary>
        /// The part of the query that is actually painted, and the index it starts at.
        ///
        /// The screen-space API has no scissor a mod may safely use here, so the left end of an
        /// overflowed query is handled by not drawing it at all: the visible tail is sliced once
        /// when the layout changes and reused every frame. Slicing in the draw path would allocate
        /// a string per frame for as long as the query was too long.
        ///
        /// The field is the full query reference when nothing is scrolled off, so the common case
        /// allocates nothing at all.
        /// </summary>
        private string _fieldDisplayText;
        private int _fieldFirstVisible;

        /// <summary>
        /// X of the first painted character, in the same units as the other cached positions.
        ///
        /// Cached rather than measured in the draw path: the painted string's origin is a function
        /// of the scroll, which only moves when the player edits.
        /// </summary>
        private float _fieldFirstVisibleX;

        /// <summary>
        /// The caret is drawn on alternating halves of <see cref="VgsConstants.CaretBlinkFrames"/>,
        /// and the phase restarts whenever the player edits: a caret that keeps blinking through a
        /// keystroke can be invisible exactly when the player looks for it after typing.
        /// </summary>
        private int _caretBlinkBaseFrame;

        /// <summary>
        /// The screen's own frame counter, advanced once per <see cref="OnUpdate"/>.
        ///
        /// <b>Not <c>VgsSession.Frame</c>, and this is the whole point.</b> The editor pauses the
        /// game (<c>PausesGameInSinglePlayer = true</c>), and a paused session reaches
        /// <c>UpdateComponents</c> only through <c>UpdateComponentsWhilePaused</c>, which calls
        /// <c>UpdateAfterSimulation</c> on a component only when its <c>UpdateOnPause</c> is set.
        /// This mod's session component does not set it, so the session frame counter stands still
        /// for as long as the editor is open — verified by IL, see
        /// <c>docs/01-research/api-terminal-search.md</c> §2.5. Everything on this screen that
        /// counts time (caret blink, warning expiry, the refresh throttle, the view-save throttle)
        /// read that frozen counter, so none of them advanced: the caret never blinked and could
        /// start invisible, warnings never expired, and blocks built with the editor open never
        /// appeared.
        ///
        /// The screen's own counter is the fix, because <c>OnUpdate</c> is called by the mod screen
        /// host without any pause gate — a GUI screen keeps updating while the world is paused,
        /// which is how the pause menu works at all.
        /// </summary>
        private int _screenFrame;

        /// <summary>
        /// Line heights of the bottom bar's text scales, measured once each.
        ///
        /// Each control centres its text vertically, which needs the height of a line, and
        /// measuring per frame would call into the font metrics for a value that only changes
        /// with the resolution — and the editor is a modal screen, so it does not.
        ///
        /// Three fields, not one: the search field, the modes menu's label and the hint line are
        /// drawn at three different scales, so one cache would hand the first measured height to
        /// the other two and mis-centre them. A negative value means "not measured yet", which is
        /// why these cannot be simple value caches with a 0 default.
        /// </summary>
        private float _searchLineHeight = -1f;
        private float _modesLineHeight = -1f;
        private float _hintLineHeight = -1f;

        private VgsEditorScreen(GridGraph graph, IMyCubeGrid grid, Vector2 camera, float zoom,
                                bool showHidden)
            : base(
                name: "VGS.Workbench",
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
            _camera = camera;
            _zoom = zoom;
            _showHidden = showHidden;

            // The editor is a full-screen modal: Esc closes it, the pointer is visible, and in
            // single player the world pauses behind it.
            CloseOnEscape = true;
            CloseButtonEnabled = false;   // we draw and hit-test our own button
            DrawMouseCursor = true;
            EnabledBackgroundFade = true;
            PausesGameInSinglePlayer = true;
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

            // The view the player left this construct in (ADR-0022). Read here, on the open path,
            // and not from the draw path: it is a live grid read, and the default view is used
            // when nothing is stored or the stored value does not parse.
            Vector2 camera = new Vector2(-0.08f, -0.05f);
            float zoom = 1f;
            bool showHidden = false;

            Vector2 savedCamera;
            float savedZoom;
            bool savedShowHidden;
            if (GridViewStore.TryRead(grid, out savedCamera, out savedZoom, out savedShowHidden))
            {
                camera = savedCamera;
                zoom = savedZoom;
                showHidden = savedShowHidden;
            }

            GridGraph graph = GridGraphBuilder.Build(grid, showHidden);

            // Group frames are derived from the cards of their members, so they are laid out
            // once the graph is complete — before the first frame is drawn, and outside any
            // draw path (ADR-0015).
            PortGeometry.LayoutGroupFrames(graph);

            var screen = new VgsEditorScreen(graph, grid, camera, zoom, showHidden);

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
            VgsLog.Info("editor opened for grid " + grid.EntityId + " (" + graph.NodeCount + " blocks, "
                        + (showHidden ? "invisible blocks shown" : "invisible blocks hidden") + ")");
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

                _lastRefreshFrame = _screenFrame;

                // The view is already what the screen was constructed with; recording it as
                // "written" is what stops the throttled save in OnUpdate from writing an
                // identical value on the first frame.
                RememberSavedView();
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
                // Esc exits the deepest mode first — cancelling a bind or a watch is what the
                // player expects from Escape — and only closes the editor when there is none.
                if (_menuTarget != null || _bindSource != null)
                {
                    CancelBind();
                    return false;
                }

                if (_watchSource != null)
                {
                    CancelWatch();
                    return false;
                }

                // The modes popup is the shallowest thing on the screen, so Esc closes it before
                // it closes the editor.
                if (_modesMenuOpen)
                {
                    _modesMenuOpen = false;
                    return false;
                }

                // A search is a view of the schema, not a mode with a gesture to cancel: Esc
                // clears the query first, so the player can always get back to the full picture
                // without closing and reopening the editor.
                if (SearchHasText)
                {
                    ClearSearch();
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

                // The last chance to remember the view. Skipped while unloading: the session is
                // being torn down and touching a live grid there is not worth a camera position
                // — the throttled save in OnUpdate has already written the latest one.
                if (!isUnloading) SaveView();

                // Selection, drag, hover and both gestures in one call: they are the same set of
                // references the rebuild drops, and a second hand-written list here is a list that
                // goes stale the moment a field is added.
                DropNodeReferences();
                _menuTarget = null;
                _menuRows.Clear();
                _menuLabels.Clear();
                _menuTitle = null;
                _statusText = null;
                _titleWidth = -1f;
                _titleWidthClientWidth = -1f;
                _gridNameWidth = -1f;
                _gridNameWidthFor = null;
                _gridNameWidthClientWidth = -1f;
                _gridNameCache = null;
                _warningText = null;
                _mouseValid = false;
                _route.Clear();
                _removeBuffer.Clear();
                _searchTerms.Clear();
                _searchEditor.Clear();
                _fieldLayoutValid = false;
                _fieldAreaWidth = -1f;
                _fieldCaretX = 0f;
                _fieldSelectionStartX = 0f;
                _fieldSelectionEndX = 0f;
                _fieldScrollX = 0f;
                _fieldDisplayText = null;
                _fieldFirstVisible = 0;
                _searchLineHeight = -1f;
                _modesLineHeight = -1f;
                _hintLineHeight = -1f;
                _modesMenuOpen = false;
                _portLabelHeight = 0f;
                _portLabelHeightZoom = -1f;

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
        /// Vertical position of the bottom bar's control row.
        ///
        /// <b>Centred in the bar, on the Close button's own centre line.</b> Close is centred in
        /// the bar, so a controls row that was bottom-aligned instead would sit about twenty
        /// pixels lower at 1080p — two rows of chrome in one bar, which reads as a layout mistake
        /// rather than as a choice. Centring them together is also what makes the bar behave at
        /// any height: the row keeps its distance from both edges.
        ///
        /// The hint line then lives in the strip above the row, where it cannot collide with it
        /// whatever the font's line height turns out to be.
        /// </summary>
        private static float BarControlTop
        {
            get { return 1f - VgsConstants.BottomBarHeight * 0.5f - VgsConstants.BarControlHeight * 0.5f; }
        }

        /// <summary>Rectangle of the modes button: the leftmost control of the bottom bar.</summary>
        private static void GetModesButtonRect(out Vector2 topLeft, out Vector2 size)
        {
            size = new Vector2(VgsConstants.ModesButtonWidth, VgsConstants.BarControlHeight);
            topLeft = new Vector2(VgsConstants.BarLeftMargin, BarControlTop);
        }

        /// <summary>Rectangle of the block search field, immediately right of the modes button.</summary>
        private static void GetSearchFieldRect(out Vector2 topLeft, out Vector2 size)
        {
            Vector2 buttonTopLeft, buttonSize;
            GetModesButtonRect(out buttonTopLeft, out buttonSize);

            size = new Vector2(VgsConstants.SearchFieldWidth, VgsConstants.BarControlHeight);
            topLeft = new Vector2(buttonTopLeft.X + buttonSize.X + VgsConstants.BarControlGap, BarControlTop);
        }

        /// <summary>
        /// Rectangle of the search field's clear button, or false when it cannot be laid out.
        ///
        /// <b>One rectangle, not a centre plus a radius.</b> The draw pass needs the mark's centre
        /// and the input pass needs a box to hit-test, and deriving them from the same rectangle
        /// is what keeps them from drifting apart — the mark is small, so an error of one radius
        /// puts half of the visible "x" outside its own click target. That is a defect this
        /// editor had in an earlier revision of this very control, and the fix is that the
        /// arithmetic exists once.
        ///
        /// Whether the button is *drawn* depends on the query, which the caller decides: the
        /// geometry itself is a property of the field alone.
        /// </summary>
        private static bool TrySearchClearRect(out Vector2 topLeft, out Vector2 size)
        {
            topLeft = Vector2.Zero;
            size = Vector2.Zero;

            Vector2 fieldTopLeft, fieldSize;
            GetSearchFieldRect(out fieldTopLeft, out fieldSize);

            // The button's diameter is the field's height less the padding on both sides, so it
            // can never overflow the field at any resolution.
            float diameter = fieldSize.Y - VgsConstants.SearchFieldPadding * 2f;
            if (diameter <= 0f) return false;

            size = new Vector2(diameter, diameter);
            topLeft = new Vector2(fieldTopLeft.X + fieldSize.X - VgsConstants.SearchFieldPadding - diameter,
                                  fieldTopLeft.Y + (fieldSize.Y - diameter) * 0.5f);
            return true;
        }

        /// <summary>
        /// Rectangle of the modes popup. Anchored above the modes button and left-aligned with it,
        /// because the button sits in the bottom bar and a menu hanging below would be off-screen.
        /// </summary>
        private static void GetModesMenuRect(out Vector2 topLeft, out Vector2 size)
        {
            Vector2 buttonTopLeft, buttonSize;
            GetModesButtonRect(out buttonTopLeft, out buttonSize);

            size = new Vector2(VgsConstants.ModesMenuWidth,
                               VgsConstants.ModesMenuRowHeight + VgsConstants.ModesMenuPadding * 2f);

            topLeft = new Vector2(buttonTopLeft.X,
                                  buttonTopLeft.Y - size.Y - VgsConstants.ModesMenuGap);
        }

        /// <summary>Rectangle of the modes menu's single row.</summary>
        private static void GetModesRowRect(out Vector2 topLeft, out Vector2 size)
        {
            Vector2 menuTopLeft, menuSize;
            GetModesMenuRect(out menuTopLeft, out menuSize);

            size = new Vector2(menuSize.X, VgsConstants.ModesMenuRowHeight);
            topLeft = new Vector2(menuTopLeft.X, menuTopLeft.Y + VgsConstants.ModesMenuPadding);
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
            // Advanced before anything else, and outside the focus check: the caret must keep
            // blinking and warnings must keep expiring even on a frame where the host reports no
            // focus. It cannot throw, so it needs no guard of its own.
            _screenFrame++;

            try
            {
                if (!hasFocus) return;

                if (_screenFrame - _lastRefreshFrame >= RefreshIntervalFrames)
                {
                    _lastRefreshFrame = _screenFrame;
                    RefreshFromGrid();
                }

                // The camera and the zoom change on pointer events, not on a timer, so the save
                // cannot live in the input pass: panning would write on every frame. It runs here
                // instead, throttled, and writes only when the value really changed (ADR-0022).
                SaveViewIfChanged();
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

                GridGraphBuilder.Refresh(_graph, _grid, _showHidden);

                // The refresh moves cards (LayoutUnplaced) and rebuilds the groups, so the
                // frames have to be recomputed before the next draw reads them.
                PortGeometry.LayoutGroupFrames(_graph);

                ValidateInteractionState();
            }
            catch (Exception e)
            {
                VgsLog.Error("editor refresh failed", e);
            }
        }

        /// <summary>
        /// Rebuilds the graph after the "show invisible" mode changed (ADR-0023).
        ///
        /// A rebuild rather than a flag on the nodes: whether a block is part of the schema is
        /// decided while the graph is built (<see cref="GridGraphBuilder"/>), because that is
        /// where the block is read. The cost is one walk of the grid on a click, which is the
        /// same walk the periodic refresh does anyway.
        ///
        /// Saved card positions are read again from the blocks, so nothing the player placed
        /// moves; a block that was hidden until now has no saved position and is laid out by the
        /// deterministic pass, exactly as it would be on the first open.
        ///
        /// <b>Every node reference is dropped first.</b> A rebuild replaces every
        /// <see cref="Node"/> object, including the groups — and unlike a refresh, it does not
        /// update the existing ones in place. <see cref="ValidateInteractionState"/> alone is not
        /// enough here: it asks the graph by name for a group, so a stale group object with a
        /// name that still exists would be declared alive, and the drag or selection holding it
        /// would then act on a node that is no longer drawn.
        /// </summary>
        private void RebuildGraph()
        {
            if (_grid == null) return;

            DropNodeReferences();

            _graph = GridGraphBuilder.Build(_grid, _showHidden);
            PortGeometry.LayoutGroupFrames(_graph);

            VgsLog.Info("schema rebuilt: " + _graph.NodeCount + " blocks, invisible "
                        + (_showHidden ? "shown" : "hidden"));
        }

        /// <summary>
        /// Drops every reference the pointer holds into the current graph: selection, drag,
        /// hover and the two gestures that aim at a node.
        ///
        /// Cancelling the gestures rather than re-aiming them is deliberate: after a rebuild
        /// there is no node to re-aim at, and a bind that silently pointed at a block the player
        /// can no longer see would write a connection to it.
        /// </summary>
        private void DropNodeReferences()
        {
            CancelBind();
            CancelWatch();

            _selected = null;
            _draggedNode = null;
            _draggedGroup = null;
            _dragging = false;
            _watchErrorUntilFrame = 0;
            ClearHoverHighlights();
        }

        /// <summary>
        /// Drops every "the pointer is over this" highlight.
        ///
        /// Needed whenever the canvas stops receiving pointer input while staying on screen: a
        /// rebuild replaces the nodes, and the modes popup covers the canvas. In both cases the
        /// hover fields would otherwise freeze on whatever was under the pointer at that moment,
        /// and the card would keep its hover border — drawn every frame, because
        /// <see cref="DrawNode"/> reads the field — for as long as the state lasted.
        /// </summary>
        private void ClearHoverHighlights()
        {
            _hoverNode = null;
            _hoverButton = null;
            _hoverRemoveNode = null;
            _hoverRemovePort = null;
            _hoverWatchRemoveNode = null;
            _hoverWatchRemoveTarget = null;
            _searchHot = false;
            _searchClearHot = false;
        }

        // ------------------------------------------------------------------ view state (ADR-0022)

        /// <summary>
        /// Records the view currently on screen as "already saved", so the throttled save does
        /// not write a value identical to what the grid already holds.
        /// </summary>
        private void RememberSavedView()
        {
            _savedCamera = _camera;
            _savedZoom = _zoom;
            _savedShowHidden = _showHidden;
            _lastViewSaveFrame = _screenFrame;
        }

        /// <summary>True when the camera, the zoom or the mode differs from what was last written.</summary>
        private bool ViewChanged
        {
            get
            {
                return _savedZoom != _zoom
                       || _savedShowHidden != _showHidden
                       || _savedCamera != _camera;
            }
        }

        /// <summary>
        /// Writes the view to the grid and records it as written. Returns whether the write went
        /// through; a refusal is a normal outcome (the grid may be mid-teardown).
        /// </summary>
        private bool SaveView()
        {
            if (_grid == null) return false;
            if (!GridViewStore.Save(_grid, _camera, _zoom, _showHidden)) return false;

            RememberSavedView();
            return true;
        }

        /// <summary>
        /// Writes the view when it changed and the throttle interval has passed. Called from
        /// <see cref="OnUpdate"/> once per frame, so it must be free when nothing changed.
        ///
        /// <b>The throttle is advanced on the attempt, not on the success.</b> A failed write
        /// leaves <see cref="_savedCamera"/> untouched — so "changed" stays true — and if the
        /// interval were only reset by a successful write, a grid that refuses the write would
        /// make this method format a string and try a dictionary write on every single frame for
        /// as long as the refusal lasted. Retrying at the normal cadence is enough: the value is
        /// a camera position, and the next attempt is two seconds away.
        /// </summary>
        private void SaveViewIfChanged()
        {
            if (!ViewChanged) return;
            if (_screenFrame - _lastViewSaveFrame < VgsConstants.ViewSaveIntervalFrames) return;

            _lastViewSaveFrame = _screenFrame;
            SaveView();
        }

        // ------------------------------------------------------------------ block search (ADR-0023)

        /// <summary>True while a query is being filtered by.</summary>
        private bool SearchActive
        {
            get { return _searchTerms.Count > 0; }
        }

        /// <summary>The query as typed. Never null.</summary>
        private string SearchText
        {
            get { return _searchEditor.Text; }
        }

        /// <summary>True while the search field holds any text, which is when "x" is drawn.</summary>
        private bool SearchHasText
        {
            get { return _searchEditor.Length > 0; }
        }

        /// <summary>
        /// Re-splits the terms after the editor's text changed, and invalidates the caret layout.
        ///
        /// One place for both, because they are the same event: the text is what decides the
        /// filter and where the caret is drawn. Called on every edit, never from the draw path —
        /// <see cref="BlockSearch.SplitTerms"/> allocates one string per term, and the layout
        /// measures the text.
        /// </summary>
        private void OnSearchTextChanged()
        {
            BlockSearch.SplitTerms(_searchEditor.Text, _searchTerms);
            _fieldLayoutValid = false;
        }

        /// <summary>Drops the query, which brings every card back to full opacity.</summary>
        private void ClearSearch()
        {
            _searchEditor.Clear();
            OnSearchTextChanged();
            ResetCaretBlink();
        }

        /// <summary>
        /// Inserts one typed character at the caret. Control characters are dropped: the game
        /// hands over the raw character stream, which includes the carriage return of Enter, the
        /// backspace and whatever a Ctrl shortcut produces (ADR-0024).
        /// </summary>
        private void AppendSearchChar(char c)
        {
            if (char.IsControl(c)) return;

            if (!_searchEditor.Insert(c)) return;

            // The blink restarts so the caret is visible right after a keystroke — otherwise the
            // character appears with no visible insertion point for up to half a period.
            ResetCaretBlink();
            OnSearchTextChanged();
        }

        /// <summary>Restarts the caret's blink cycle at the current frame.</summary>
        private void ResetCaretBlink()
        {
            _caretBlinkBaseFrame = _screenFrame;
        }

        /// <summary>
        /// Copies the selection (or the whole query when nothing is selected) to the clipboard.
        /// With <paramref name="cut"/> it also removes what it copied.
        ///
        /// With no selection a copy takes the whole query — what a player expects from a one-line
        /// field — but a *cut* with no selection does nothing. Cutting everything when the player
        /// only pressed Ctrl+X in passing would empty the field they were still reading; the
        /// standard behaviour of every text field is the safer one here.
        /// </summary>
        private void CopySearchText(bool cut)
        {
            if (!_searchEditor.HasSelection && cut) return;

            string text = _searchEditor.HasSelection
                ? _searchEditor.SelectedText
                : _searchEditor.Text;
            if (string.IsNullOrEmpty(text)) return;

            TextClipboard.Copy(text);

            if (!cut) return;

            _searchEditor.DeleteForward();

            ResetCaretBlink();
            OnSearchTextChanged();
        }

        /// <summary>
        /// Pastes the editor's own clipboard at the caret.
        ///
        /// The buffer is this session's own last copy or cut. The operating system's clipboard is
        /// not reachable from a mod — measured, not assumed: the game refuses to load
        /// `IVRageSystem`/`IVRagePlatform`/`MyVRage` as "is prohibited" (ADR-0027). So a paste can
        /// only reproduce text this field itself produced.
        ///
        /// Nothing is shown when the buffer is empty: the player pressed a key that does nothing,
        /// which needs no explanation on a bar whose only free line belongs to failures.
        /// </summary>
        private void PasteSearchText()
        {
            string text = TextClipboard.PasteText;
            if (string.IsNullOrEmpty(text)) return;
            if (!_searchEditor.Insert(text)) return;

            ResetCaretBlink();
            OnSearchTextChanged();
        }

        /// <summary>
        /// True when the card must be drawn at <see cref="VgsConstants.SearchDimOpacity"/>.
        ///
        /// The two fields are the ones the terminal searches: the block's name and its
        /// <em>displayed type</em> (ADR-0023). A group is dimmed only when none of its drawn
        /// members match: the frame is a backdrop for its members, so it has to follow them
        /// rather than the query — a frame that disappeared while one of its cards stayed would
        /// break the picture apart.
        /// </summary>
        private bool IsDimmed(Node node)
        {
            if (!SearchActive || node == null) return false;

            if (!node.IsGroup)
            {
                return !Matches(node);
            }

            for (int i = 0; i < node.Members.Count; i++)
            {
                Node member = _graph.GetNode(node.Members[i]);
                if (member == null) continue;
                if (Matches(member)) return false;
            }
            return true;
        }

        /// <summary>
        /// True when one node matches the current query: every term must occur in its name or in
        /// its displayed type.
        ///
        /// A single place for the two-field call, so the draw path, the group walk and the Enter
        /// jump cannot end up searching slightly different things — the classic way a search
        /// starts disagreeing with itself.
        /// </summary>
        private bool Matches(Node node)
        {
            if (node == null) return false;
            return BlockSearch.Matches(_searchTerms, node.DisplayName, node.TypeDisplayName);
        }

        /// <summary>Alpha multiplier for one node's drawing: 1 for a match, the dim value otherwise.</summary>
        private float AlphaOf(Node node)
        {
            return IsDimmed(node) ? VgsConstants.SearchDimOpacity : 1f;
        }

        /// <summary>
        /// Alpha of a line between two nodes: the <b>higher</b> of the two.
        ///
        /// A line stays visible while <em>at least one</em> of its ends matches the query, because
        /// the whole point of finding a block is seeing what it is wired to: ghosting the wires of
        /// the card the player just found would hide the one thing they searched for. Only a line
        /// whose both ends are excluded becomes a ghost itself (ADR-0023 §4, and the rejected
        /// alternative there is exactly this mistake).
        /// </summary>
        private float MaxAlpha(Node a, Node b)
        {
            float first = AlphaOf(a);
            float second = AlphaOf(b);
            return second > first ? second : first;
        }

        /// <summary>
        /// Applies <see cref="AlphaOf"/> to a colour. A struct copy, so this is safe in the draw
        /// path: nothing is allocated and no cached colour is mutated.
        /// </summary>
        private static Color Fade(Color color, float alpha)
        {
            if (alpha >= 1f) return color;

            float a = (color.A / 255f) * alpha;
            if (a < 0f) a = 0f;
            return new Color(color, a);
        }

        /// <summary>
        /// Moves the camera so the first card matching the query is centred on the canvas.
        ///
        /// "First" is the graph's own order, which is stable for a given construct — the same
        /// order the cards are drawn in. Nothing happens when nothing matches: the player is
        /// mid-query and the picture must not jump away from them.
        ///
        /// A card whose centre cannot be computed is <em>skipped</em>, not treated as the end of
        /// the walk: one degenerate node must not make Enter a no-op while a perfectly good
        /// match sits further down the list.
        /// </summary>
        private void JumpToFirstMatch()
        {
            if (!SearchActive) return;

            Vector2 canvasTopLeft, canvasSize;
            GetCanvasRect(out canvasTopLeft, out canvasSize);

            IList<Node> nodes = _graph.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                Node node = nodes[i];
                if (!Matches(node)) continue;

                Vector2 centre;
                if (!PortGeometry.TryCardCentre(node, out centre)) continue;

                // Aim the camera so the card's centre lands at the centre of the canvas:
                // screen = (graph - camera) * zoom + canvasTopLeft, and the screen point we want
                // is the canvas centre, so camera = graph - canvasSize / (2 * zoom).
                _camera = centre - (canvasSize * 0.5f) / _zoom;
                _selected = node;

                VgsLog.Debug("search '" + SearchText + "' jumped to block " + node.EntityId);
                return;
            }
        }

        /// <summary>
        /// Drops references to nodes the refresh has removed.
        ///
        /// A refresh can delete the block the user is currently dragging, binding from, or
        /// picking an action for — and it can delete a *group* the user is aiming at, because a
        /// group disappears as soon as its last drawn member is hidden or destroyed. Those
        /// fields are plain references, so they stay valid as objects after the node leaves the
        /// graph — which means the draw path would happily keep painting a card that no longer
        /// exists. The check is by node identity, not by entity id: a group has no id to ask
        /// about.
        /// </summary>
        private void ValidateInteractionState()
        {
            if (_selected != null && !_graph.ContainsNode(_selected)) _selected = null;
            if (_draggedNode != null && !_graph.ContainsNode(_draggedNode)) _draggedNode = null;

            // A refresh can dissolve the group the user is dragging — its last drawn member was
            // hidden, or the group was renamed in the terminal. The reference stays valid as an
            // object, so without this the drag would keep translating a frame that is no longer
            // on the canvas.
            if (_draggedGroup != null && !_graph.ContainsNode(_draggedGroup))
            {
                _draggedGroup = null;
                _dragging = false;
            }

            if (_hoverRemoveNode != null)
            {
                // The refresh can remove the block *and* rebuild its port list (a changed action
                // set replaces every Port object). Re-resolve by key instead of comparing
                // references: the gesture is still meaningful as long as the port survives, and
                // a stale object would leave the highlight on a bar that no longer exists.
                if (!_graph.ContainsNode(_hoverRemoveNode))
                {
                    _hoverRemoveNode = null;
                    _hoverRemovePort = null;
                }
                else if (_hoverRemovePort != null)
                {
                    Port live = _hoverRemovePort.Direction == PortDirection.Output
                        ? _hoverRemoveNode.FindEvent(_hoverRemovePort.Key)
                        : _hoverRemoveNode.FindAction(_hoverRemovePort.Key);
                    _hoverRemovePort = live;
                    if (live == null) _hoverRemoveNode = null;
                }
            }

            if (_bindSource != null && !_graph.ContainsNode(_bindSource)) CancelBind();

            // A controller can be destroyed, or stop being one, while its watch mode is open.
            // The mode is cancelled rather than silently re-aimed: the port it was going to
            // write into no longer exists.
            if (_watchSource != null && !_graph.ContainsNode(_watchSource)) CancelWatch();

            if (_hoverButton != null && !_graph.ContainsNode(_hoverButton)) _hoverButton = null;
            if (_hoverWatchRemoveNode != null && !_graph.ContainsNode(_hoverWatchRemoveNode))
            {
                _hoverWatchRemoveNode = null;
                _hoverWatchRemoveTarget = null;
            }

            // The refresh rebuilds the watch list, so the hovered target can be a stale object
            // even while its controller is still there. Re-resolve by id rather than by
            // reference: the highlight must follow the entry, not the object that used to be it.
            if (_hoverWatchRemoveNode != null && _hoverWatchRemoveTarget != null)
            {
                _hoverWatchRemoveTarget =
                    _hoverWatchRemoveNode.FindWatch(_hoverWatchRemoveTarget.EntityId);
                if (_hoverWatchRemoveTarget == null) _hoverWatchRemoveNode = null;
            }

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
                if (!_graph.ContainsNode(_menuTarget) || !SamePorts(_menuRows, _menuTarget.Actions))
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

                // Typing is handled before the pointer, and before the "no cursor" early return:
                // the search field is the editor's only keyboard target, and it must keep working
                // when the pointer happens to be outside the window.
                HandleSearchKeyboard();

                Vector2 mouse;
                _mouseValid = VgsScreenDraw.TryCursor(out mouse);
                if (!_mouseValid) return;

                _mouse = mouse;

                if (HandleCloseButton(mouse)) return;

                // The bottom bar's controls are hit-tested before every mode: they belong to the
                // chrome, and a click on the search field must not also pan the canvas or pick a
                // binding target.
                if (HandleBarInput(mouse)) return;

                if (_menuTarget != null) { HandleMenuInput(mouse); return; }
                if (_bindSource != null) { HandleBindInput(mouse); return; }
                if (_watchSource != null) { HandleWatchInput(mouse); return; }

                HandleCanvasInput(mouse);
            }
            catch (Exception e)
            {
                VgsLog.Throttled("screeninput", "input handling failed", e, 300);
            }
        }

        /// <summary>
        /// Feeds the typed characters, the editing keys and the clipboard shortcuts into the
        /// search field (ADR-0023, ADR-0024).
        ///
        /// <b>The field is always the keyboard target.</b> The editor has no other text input and
        /// no keyboard shortcut but Esc, so there is nothing to take focus from it and no focus
        /// ring to explain. A click on the canvas does not blur it, which is what makes "type
        /// anywhere to filter" work.
        ///
        /// <b>The character stream is per frame.</b> The game hands over the characters typed
        /// since the last input update and swaps its buffer, so reading
        /// <see cref="VRage.ModAPI.IMyInput.TextInput"/> once per frame consumes each character
        /// exactly once (verified by IL, see <c>docs/01-research/api-terminal-search.md</c>).
        ///
        /// <b>Order matters.</b> Control keys are resolved before the character stream, because
        /// Ctrl+C also produces a character (0x03) and Backspace produces 0x08: both would be
        /// dropped by the control-character filter anyway, but resolving the shortcut first is what
        /// makes the *one* intent produce the *one* effect. And the stream is consumed last and
        /// unconditionally — every character must be read out of the buffer each frame whether or
        /// not some other key was handled, or the game would carry it into the next frame.
        /// </summary>
        private void HandleSearchKeyboard()
        {
            VRage.ModAPI.IMyInput input = MyAPIGateway.Input;
            if (input == null) return;

            // While the modes popup is open it owns the keyboard: typing must not edit the query
            // behind it, and Ctrl+V must not paste into a field the player cannot see past the
            // menu. This follows the same rule the canvas already follows (ADR-0023 §4: nothing
            // behind the popup takes input) and the Esc order — each step cancels exactly what the
            // player started last, and the menu is what was started last.
            //
            // The character stream is still drained, and that is the whole reason this is a
            // branch and not an early return: an undrained character is carried into the next
            // frame, so it would appear in the field the moment the menu closed — the keystroke
            // would seem to arrive late rather than to be ignored.
            if (_modesMenuOpen)
            {
                VRage.Collections.ListReader<char> swallowed = input.TextInput;
                if (swallowed.Count > 0)
                {
                    VgsLog.Debug("modes menu swallowed " + swallowed.Count +
                                 " typed character(s)");
                }
                return;
            }

            bool control = input.IsAnyCtrlKeyPressed();
            bool shift = input.IsAnyShiftKeyPressed();

            // ---- clipboard ----
            if (control)
            {
                if (input.IsNewKeyPressed(VRage.Input.MyKeys.C)) CopySearchText(false);
                if (input.IsNewKeyPressed(VRage.Input.MyKeys.X)) CopySearchText(true);
                if (input.IsNewKeyPressed(VRage.Input.MyKeys.V)) PasteSearchText();
                if (input.IsNewKeyPressed(VRage.Input.MyKeys.A)) SelectAllSearchText();
            }

            // ---- caret movement and deletion ----
            //
            // Shift extends the selection, a bare arrow collapses it: the rule lives in
            // TextEditor.MoveHorizontal, so these four lines only carry the intent.
            if (input.IsNewKeyPressed(VRage.Input.MyKeys.Left))
            {
                _searchEditor.MoveHorizontal(-1, shift);
                ResetCaretBlink();
                _fieldLayoutValid = false;
            }

            if (input.IsNewKeyPressed(VRage.Input.MyKeys.Right))
            {
                _searchEditor.MoveHorizontal(1, shift);
                ResetCaretBlink();
                _fieldLayoutValid = false;
            }

            if (input.IsNewKeyPressed(VRage.Input.MyKeys.Home))
            {
                _searchEditor.MoveTo(0, shift);
                ResetCaretBlink();
                _fieldLayoutValid = false;
            }

            if (input.IsNewKeyPressed(VRage.Input.MyKeys.End))
            {
                _searchEditor.MoveTo(_searchEditor.Length, shift);
                ResetCaretBlink();
                _fieldLayoutValid = false;
            }

            if (input.IsNewKeyPressed(VRage.Input.MyKeys.Delete))
            {
                if (_searchEditor.DeleteForward())
                {
                    ResetCaretBlink();
                    OnSearchTextChanged();
                }
            }

            if (input.IsNewKeyPressed(VRage.Input.MyKeys.Back))
            {
                if (_searchEditor.DeleteBackward())
                {
                    ResetCaretBlink();
                    OnSearchTextChanged();
                }
            }

            if (input.IsNewKeyPressed(VRage.Input.MyKeys.Enter))
            {
                JumpToFirstMatch();
            }

            // The stream is always drained, even when a shortcut above already acted on it.
            VRage.Collections.ListReader<char> typed = input.TextInput;
            for (int i = 0; i < typed.Count; i++)
            {
                AppendSearchChar(typed[i]);
            }
        }

        /// <summary>Selects the whole query (Ctrl+A).</summary>
        private void SelectAllSearchText()
        {
            if (_searchEditor.Length == 0) return;

            _searchEditor.SelectAll();
            ResetCaretBlink();
            _fieldLayoutValid = false;
        }

        /// <summary>
        /// Click handling for the bottom bar: the modes button, its popup, and the search field
        /// with its clear button. Returns true when the click was consumed by the bar.
        /// </summary>
        private bool HandleBarInput(Vector2 mouse)
        {
            bool leftNew = MyAPIGateway.Input.IsNewLeftMousePressed();

            Vector2 buttonTopLeft, buttonSize;
            GetModesButtonRect(out buttonTopLeft, out buttonSize);
            _modesHot = PortGeometry.Contains(buttonTopLeft, buttonSize, mouse);

            // ---- modes popup (drawn above the button, so it is resolved first) ----
            if (_modesMenuOpen)
            {
                // The canvas and the search field are not reachable while the popup is up, so
                // every "pointer is over this" highlight is dropped here rather than left on from
                // before the menu opened: a card that kept its hover border behind an open menu
                // reads as a stuck control.
                ClearHoverHighlights();

                Vector2 rowTopLeft, rowSize;
                GetModesRowRect(out rowTopLeft, out rowSize);
                _modesRowHot = PortGeometry.Contains(rowTopLeft, rowSize, mouse);

                if (leftNew)
                {
                    // The button toggles the popup even while it is open: that is what a menu
                    // button does everywhere else, and without this a second click on it would
                    // fall through to "close on any outside click" and look the same — until the
                    // pointer happens to be over the button and the menu seems to ignore it.
                    if (_modesHot)
                    {
                        _modesMenuOpen = false;
                        return true;
                    }

                    if (_modesRowHot)
                    {
                        ToggleShowHidden();
                        return true;
                    }

                    Vector2 menuTopLeft, menuSize;
                    GetModesMenuRect(out menuTopLeft, out menuSize);

                    // A click inside the popup but not on its row is a miss on the popup, not a
                    // command: the menu stays open. A click anywhere else closes it.
                    if (PortGeometry.Contains(menuTopLeft, menuSize, mouse)) return true;

                    _modesMenuOpen = false;
                    return true;
                }

                return true;
            }

            _modesRowHot = false;

            // ---- modes button ----
            if (leftNew && _modesHot)
            {
                _modesMenuOpen = true;
                return true;
            }

            // ---- search field ----
            Vector2 fieldTopLeft, fieldSize;
            GetSearchFieldRect(out fieldTopLeft, out fieldSize);
            _searchHot = PortGeometry.Contains(fieldTopLeft, fieldSize, mouse);

            // The clear button exists only while there is something to clear, and it is
            // resolved before the field for the same reason the port's "x" is resolved before its
            // card: the smaller target on top of the bigger one has to win.
            Vector2 clearTopLeft, clearSize;
            _searchClearHot = SearchHasText
                              && TrySearchClearRect(out clearTopLeft, out clearSize)
                              && PortGeometry.Contains(clearTopLeft, clearSize, mouse);

            if (leftNew && _searchClearHot)
            {
                ClearSearch();
                return true;
            }

            // A click inside the field belongs to the field, never to the canvas underneath it.
            // It also decides where the caret goes, which is the one thing a click in a text field
            // is *for*: without this the field had no way to place the caret except by typing.
            if (leftNew && _searchHot)
            {
                PlaceCaretAtPointer(mouse, fieldTopLeft);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Puts the caret at the character boundary nearest the pointer, and drops any selection.
        ///
        /// The inverse of <see cref="EnsureFieldLayout"/>: it walks the character boundaries that
        /// layout already measured and picks the closest one. Walking measured boundaries rather
        /// than dividing by an average character width is what makes a click land between the two
        /// glyphs the player aimed at, whatever the font's spacing is.
        ///
        /// With Shift held it extends the selection instead, which is how a click completes a
        /// selection that was started by keyboard.
        /// </summary>
        private void PlaceCaretAtPointer(Vector2 mouse, Vector2 fieldTopLeft)
        {
            float textLeft = fieldTopLeft.X + VgsConstants.SearchFieldPadding;
            // A click anywhere in the field (including on the padding, or on the right half of the
            // space the "x" does not occupy) scrolls the text, so the pointer has to be converted
            // through the current scroll to reach a text coordinate.
            float targetX = mouse.X - textLeft + _fieldScrollX;

            string text = _searchEditor.Text;
            int nearest = 0;
            float nearestDistance = float.MaxValue;

            for (int i = 0; i <= text.Length; i++)
            {
                float distance = Math.Abs(PrefixWidth(i) - targetX);
                if (distance >= nearestDistance) continue;
                nearestDistance = distance;
                nearest = i;
            }

            bool extend = MyAPIGateway.Input.IsAnyShiftKeyPressed();
            _searchEditor.MoveTo(nearest, extend);

            ResetCaretBlink();
            _fieldLayoutValid = false;
        }

        /// <summary>
        /// Switches the "show invisible" mode: rebuilds the graph, since whether a hidden block is
        /// part of the schema is decided while it is built (ADR-0023), and writes the new view so
        /// the mode survives closing the editor (ADR-0022).
        /// </summary>
        private void ToggleShowHidden()
        {
            _showHidden = !_showHidden;

            RebuildGraph();
            SaveView();

            VgsLog.Info("show invisible blocks: " + (_showHidden ? "on" : "off"));
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

            // ---- hover: the remove button, the card's button row and the card ----
            UpdateHover(mouse, canvasTopLeft, canvasSize);

            if (leftNew && insideCanvas)
            {
                Vector2 graphPos = ScreenToGraph(mouse, canvasTopLeft);

                // The remove button is the smallest target on the canvas and it sits on a bar
                // that otherwise belongs to the card, so it is resolved before anything else:
                // a click meant to delete a connection must not select or drag the block.
                Port removePort;
                Node removeNode = HitTestRemoveButton(graphPos, out removePort);
                if (removeNode != null)
                {
                    RemovePortConnections(removeNode, removePort);
                    return;
                }

                // A watch port's "x" is the same kind of target on the same kind of bar, and it
                // is resolved before the card for the same reason (ADR-0017).
                WatchTarget watchRemove;
                Node watchRemoveNode = HitTestWatchRemoveButton(graphPos, out watchRemove);
                if (watchRemoveNode != null)
                {
                    RemoveWatchTarget(watchRemoveNode, watchRemove);
                    return;
                }

                // The card's button row is resolved before the card itself: every button sits on
                // the card, and starting a gesture instead of a drag is the whole point of it
                // (ADR-0020). Which button it was decides what happens — the jump leaves the
                // schema for the block's own terminal (ADR-0021), a bell starts a watch, a
                // lightbulb or a plus starts a bind.
                CardButtonKind button;
                Node buttonNode = HitTestCardButton(graphPos, out button);
                if (buttonNode != null)
                {
                    PortGroup group;
                    switch (button)
                    {
                        case CardButtonKind.Jump:
                            TryJumpToTerminal(buttonNode);
                            break;

                        case CardButtonKind.Watch:
                            BeginWatch(buttonNode);
                            break;

                        default:
                            // Everything else is a bind, and the switch above names the two kinds
                            // that are not: a new kind of button added to the enum lands here as
                            // a bind, which is the safe default — it starts a gesture the player
                            // can cancel rather than doing something irreversible.
                            if (PortGeometry.TryGroupOf(button, out group))
                            {
                                BeginBind(buttonNode, group);
                            }
                            break;
                    }
                    return;
                }

                Node hit = HitTest(graphPos);
                _selected = hit;

                // Only one drag can be in progress: a card drag and a group drag write to
                // different things (one position versus one offset on every member), and both
                // running at once would move the same card twice per frame. Clearing the other
                // field here is cheaper than defending every use site against a stale one.
                _draggedNode = null;
                _draggedGroup = null;

                // Two kinds of drag, and which one starts is decided by what was grabbed:
                //
                //  * a card is dragged by its body, and the node's own position follows the
                //    pointer;
                //  * a group is dragged by its title strip only (ADR-0016). The interior of the
                //    frame belongs to the member cards and the gaps between them stay canvas, so
                //    a click there still selects nothing and pans — a frame that swallowed those
                //    clicks would make the cluster impossible to pan across.
                if (hit != null && hit.IsGroup)
                {
                    if (PortGeometry.HitGroupTitle(hit, graphPos))
                    {
                        _draggedGroup = hit;
                        _groupDragAnchor = graphPos;
                        _groupDragLast = graphPos;
                        _dragging = false;
                    }
                }
                else if (hit != null)
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

                if (_dragging)
                {
                    _draggedNode.Position = target;

                    // The frames of the groups this card belongs to follow it. Recomputed here,
                    // on the pointer move that changed the position, and not in the draw path:
                    // the same reason the graph itself is never read while drawing. Only the
                    // groups containing this card can have changed, so a drag costs a scan of
                    // the group list and one frame per group the card is in.
                    if (_graph.GroupCount > 0) PortGeometry.LayoutFramesContaining(_graph, _draggedNode);
                }
            }

            if (leftDown && _draggedGroup != null)
            {
                Vector2 graphPos = ScreenToGraph(mouse, canvasTopLeft);

                // The threshold is measured from where the strip was grabbed, not from the
                // previous frame: a slow drag would otherwise never accumulate enough movement
                // to start.
                if (!_dragging && (graphPos - _groupDragAnchor).Length() > VgsConstants.DragThreshold)
                {
                    _dragging = true;
                }

                if (_dragging)
                {
                    Vector2 delta = graphPos - _groupDragLast;
                    _groupDragLast = graphPos;

                    // One offset for the whole cluster: the members keep their positions
                    // relative to each other, which is the entire point of dragging a group
                    // rather than its cards one by one.
                    if (_graph.OffsetGroupMembers(_draggedGroup, delta) > 0)
                    {
                        // Every frame is derived from member cards, so all of them are
                        // recomputed: a card may belong to more than one group, and the frames
                        // of the others have to follow it too. Bounded by the group count and
                        // only while the pointer is actually moving.
                        PortGeometry.LayoutGroupFrames(_graph);
                    }
                }
            }

            if (leftReleased)
            {
                // Where the user dropped the card is written to the block here, on release.
                // Not per pointer move (sixty writes a second), and not in OnClosed: closing
                // can be a session unload, which never reaches OnClosed at all, and that is
                // exactly when a player would notice the layout was lost (ADR-0012).
                if (_dragging && _draggedNode != null) NodeStateStore.SavePosition(_draggedNode);
                if (_dragging && _draggedGroup != null) SaveGroupPositions(_draggedGroup);

                _draggedNode = null;
                _draggedGroup = null;
                _dragging = false;
            }
        }

        /// <summary>
        /// Writes the new position of every member card of a dragged group to its block
        /// (ADR-0012, ADR-0016).
        ///
        /// A group has nowhere to store a position: it is not an entity and has no mod storage.
        /// The group's layout *is* its members' layouts, so saving the group means saving each
        /// member — and that is also what makes a dragged group survive a reload exactly like a
        /// dragged card.
        ///
        /// Members whose block disappeared mid-drag are skipped: <see cref="NodeStateStore.SavePosition"/>
        /// reports that as false, which is a normal outcome, not an error.
        /// </summary>
        private void SaveGroupPositions(Node group)
        {
            if (group == null) return;

            int saved = 0;
            for (int i = 0; i < group.Members.Count; i++)
            {
                Node member = _graph.GetNode(group.Members[i]);
                if (member == null || member.IsGroup) continue;
                if (NodeStateStore.SavePosition(member)) saved++;
            }

            VgsLog.Info("group '" + group.GroupName + "' moved: " + saved + " card position(s) saved");
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
            _hoverButton = null;

            // The remove button is a click target of the idle mode only: while binding, every
            // click picks the target, and a bar (with its button) counts as its block. Clearing
            // the highlight here is what stops a disc from looking clickable in a mode where it
            // is not.
            _hoverRemoveNode = null;
            _hoverRemovePort = null;
            _hoverWatchRemoveNode = null;
            _hoverWatchRemoveTarget = null;

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

        // ------------------------------------------------------------------ watch mode

        /// <summary>
        /// Input while a watch is being attached to a controller (ADR-0017).
        ///
        /// The gesture is the bind's twin and deliberately so: a line follows the pointer from
        /// the controller's bell, the left button picks the block, the right button and Esc
        /// cancel, the middle button still pans so a target off-screen can be reached without
        /// losing the gesture. What differs is what a click *means*: no action menu opens,
        /// because there is no action to choose — the controller only observes.
        /// </summary>
        private void HandleWatchInput(Vector2 mouse)
        {
            if (MyAPIGateway.Input.IsNewRightMousePressed())
            {
                CancelWatch();
                return;
            }

            Vector2 canvasTopLeft, canvasSize;
            GetCanvasRect(out canvasTopLeft, out canvasSize);
            bool insideCanvas = PortGeometry.Contains(canvasTopLeft, canvasSize, mouse);

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

            _hoverNode = insideCanvas ? HitTest(graphPos) : null;
            _hoverButton = null;

            // The port controls are idle-mode targets only: while a watch is being attached,
            // every click picks a block, and a bar counts as its block.
            _hoverRemoveNode = null;
            _hoverRemovePort = null;
            _hoverWatchRemoveNode = null;
            _hoverWatchRemoveTarget = null;

            if (!MyAPIGateway.Input.IsNewLeftMousePressed()) return;

            Node target = _hoverNode;
            if (target == null)
            {
                // Clicking the void abandons the gesture, exactly like a bind.
                CancelWatch();
                return;
            }

            TryAttachWatch(target);
        }

        /// <summary>
        /// Starts watch mode from a controller's watch button. Refuses on a node that is not a
        /// controller, and on one whose list cannot be written from this machine — telling the
        /// player before they aim at a block rather than after.
        /// </summary>
        private void BeginWatch(Node controller)
        {
            if (controller == null || !controller.IsEventController) return;

            _watchSource = controller;
            _watchErrorUntilFrame = 0;
            _selected = controller;
            _dragging = false;
            _draggedNode = null;
            _draggedGroup = null;
            _panning = false;
            CancelBind();

            VgsLog.Debug("watch mode started on controller " + controller.EntityId);
        }

        /// <summary>
        /// Leaves watch mode. Deliberately does not touch the selection or the camera: the
        /// player is looking at the controller they just edited.
        /// </summary>
        private void CancelWatch()
        {
            if (_watchSource != null) VgsLog.Debug("watch mode cancelled");

            _watchSource = null;
            _hoverButton = null;
            _hoverNode = null;
            _hoverRemoveNode = null;
            _hoverRemovePort = null;
            _hoverWatchRemoveNode = null;
            _hoverWatchRemoveTarget = null;
            _panning = false;
        }

        /// <summary>
        /// Attaches one block to the controller being edited, or refuses and turns the line red.
        ///
        /// <b>Order: reserve in the graph, write the block, keep the picture only on success.</b>
        /// The block is the record — the game's selection plus the overlay, never the graph
        /// (ADR-0018) — and the port is only its picture. The reservation is what makes a refusal
        /// cost nothing: it answers the cheap questions first (is this a controller, is the block
        /// already watched, is the stack full) and rolls back without a trace when the world
        /// refuses. `LayoutPorts` is not called between the two, so no port is ever drawn for a
        /// watch that did not happen.
        /// </summary>
        private void TryAttachWatch(Node target)
        {
            Node controller = _watchSource;
            if (controller == null || target == null) return;

            // A group is not an entity, so it has no id to store and nothing the controller
            // could subscribe to (ADR-0015, ADR-0017). Refused explicitly: falling through to
            // "the block is gone" would be a lie about a block the player can see.
            if (target.IsGroup)
            {
                RefuseWatch(VgsText.WarnWatchGroup);
                return;
            }

            IMyTerminalBlock controllerBlock = ResolveBlock(controller.EntityId);
            if (controllerBlock == null)
            {
                RefuseWatch(VgsText.WarnWatchBlockGone);
                return;
            }

            IMyTerminalBlock targetBlock = ResolveBlock(target.EntityId);
            if (targetBlock == null)
            {
                RefuseWatch(VgsText.WarnWatchBlockGone);
                return;
            }

            if (ReferenceEquals(target, controller))
            {
                RefuseWatch(VgsText.WarnWatchSelf);
                return;
            }

            if (_graph.HasWatch(controller, target.EntityId))
            {
                RefuseWatch(string.Format(VgsText.WarnWatchDuplicateFormat, target.DisplayName));
                return;
            }

            // The stack is capped, and the cap is enforced before the write so the refusal costs
            // nothing. A bar with no drawing slot would have no "x", and that button is the only
            // way to detach a block.
            if (controller.Watched.Count >= GridGraph.MaxPortsPerSide)
            {
                RefuseWatch(string.Format(VgsText.WarnWatchTooManyFormat, GridGraph.MaxPortsPerSide));
                return;
            }

            // The graph is the reservation: it answers the cheap questions (is this a
            // controller, is the block already watched) before a single read of the world.
            if (!_graph.TryAddWatch(controller, target.EntityId, target.DisplayName))
            {
                RefuseWatch(VgsText.WarnWatchRejected);
                return;
            }

            EventControllerProbe.WatchResult result =
                EventControllerProbe.TryAddWatch(controllerBlock, targetBlock);

            if (result != EventControllerProbe.WatchResult.Ok)
            {
                _graph.RemoveWatch(controller, target.EntityId);
                RefuseWatch(DescribeWatchFailure(result, controllerBlock, target));
                return;
            }

            _graph.LayoutPorts();
            if (_graph.GroupCount > 0) PortGeometry.LayoutGroupFrames(_graph);

            VgsLog.Info("controller " + controller.EntityId + " now watches " + target.EntityId);
        }

        /// <summary>
        /// Removes a block from a controller's watch list from the port's "x".
        ///
        /// <b>Order: block first, graph second</b> — the mirror of attaching, and for the same
        /// reason: the block is the record, the port is its picture (ADR-0018). A refused removal
        /// keeps the port and says why, because dropping the bar while the block stayed watched
        /// would hide a live watch — and a block the player selected in the vanilla terminal
        /// cannot be removed from here at all, so it answers with that instead.
        /// </summary>
        private void RemoveWatchTarget(Node controller, WatchTarget watch)
        {
            if (controller == null || watch == null) return;

            IMyTerminalBlock controllerBlock = ResolveBlock(controller.EntityId);
            if (controllerBlock == null)
            {
                Warn(VgsText.WarnWatchBlockGone);
                return;
            }

            IMyTerminalBlock targetBlock = ResolveBlock(watch.EntityId);

            // A block that no longer exists has nothing left to unsubscribe from: the entry is
            // dropped from the storage and the bar goes with it. Refusing here would leave the
            // player with a port they can never remove.
            EventControllerProbe.WatchResult result;
            if (targetBlock == null)
            {
                result = EventControllerProbe.ForgetWatch(controllerBlock, watch.EntityId)
                    ? EventControllerProbe.WatchResult.Ok
                    : EventControllerProbe.WatchResult.Rejected;
            }
            else
            {
                result = EventControllerProbe.TryRemoveWatch(controllerBlock, targetBlock);
            }

            if (result != EventControllerProbe.WatchResult.Ok)
            {
                // A client is not a refusal of the game's — it is a different machine. Saying
                // "the game did not accept it" there would be untrue and would send the player
                // looking for a problem that does not exist.
                if (result == EventControllerProbe.WatchResult.ClientNotServer)
                {
                    Warn(VgsText.WarnWatchClient);
                    return;
                }

                // A block the player selected in the vanilla terminal is not ours to remove: the
                // controller's own list has no public setter, so the port would return on the
                // next refresh anyway (ADR-0018). Sending them to the terminal is the truth.
                if (result == EventControllerProbe.WatchResult.GameOwned)
                {
                    Warn(string.Format(VgsText.WarnWatchGameOwnedFormat, watch.DisplayName));
                    return;
                }

                Warn(string.Format(VgsText.WarnWatchRemoveRejectedFormat, watch.DisplayName));
                return;
            }

            _graph.RemoveWatch(controller, watch.EntityId);
            _hoverWatchRemoveNode = null;
            _hoverWatchRemoveTarget = null;

            _graph.LayoutPorts();
            if (_graph.GroupCount > 0) PortGeometry.LayoutGroupFrames(_graph);

            VgsLog.Info("controller " + controller.EntityId + " no longer watches " + watch.EntityId);
        }

        /// <summary>
        /// Reports a refused attach: the line turns red for
        /// <see cref="VgsConstants.WatchErrorFrames"/>, the reason goes to the bottom bar, and
        /// watch mode stays on so the next click can land on a block that fits.
        /// </summary>
        private void RefuseWatch(string reason)
        {
            _watchErrorUntilFrame = _screenFrame + VgsConstants.WatchErrorFrames;
            Warn(reason);
        }

        /// <summary>
        /// Turns a refusal into the sentence the player sees. Each reason has its own wording
        /// because they call for different actions: a different block, a different event, or a
        /// different machine.
        /// </summary>
        private string DescribeWatchFailure(EventControllerProbe.WatchResult result,
                                            IMyTerminalBlock controllerBlock, Node target)
        {
            switch (result)
            {
                case EventControllerProbe.WatchResult.IsSelf:
                    return VgsText.WarnWatchSelf;
                case EventControllerProbe.WatchResult.Duplicate:
                    return string.Format(VgsText.WarnWatchDuplicateFormat, target.DisplayName);
                case EventControllerProbe.WatchResult.NoEvent:
                    return VgsText.WarnWatchNoEvent;
                case EventControllerProbe.WatchResult.EventIgnoresBlocks:
                    return VgsText.WarnWatchEventIgnoresBlocks;
                case EventControllerProbe.WatchResult.DifferentConstruct:
                    return VgsText.WarnWatchDifferentConstruct;
                case EventControllerProbe.WatchResult.NotAccepted:
                    return string.Format(VgsText.WarnWatchNotAcceptedFormat, target.DisplayName,
                                         EventControllerProbe.SelectedEventName(controllerBlock));
                case EventControllerProbe.WatchResult.ClientNotServer:
                    return VgsText.WarnWatchClient;
                case EventControllerProbe.WatchResult.BlockGone:
                    return VgsText.WarnWatchBlockGone;
                default:
                    return VgsText.WarnWatchRejected;
            }
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

        // ------------------------------------------------------------------ jump to the block

        /// <summary>
        /// Leaves the schema for the block's own terminal (ADR-0021): switches the game's terminal
        /// to this block and closes the editor.
        ///
        /// <b>Close only on success.</b> This is the one card action that destroys the screen the
        /// player is looking at, so a refusal — the block was destroyed since the last refresh, or
        /// there is no local character to open a terminal for — has to leave the schema where it
        /// was and say why in the bottom bar. Closing first and reporting afterwards would cost
        /// the player their place over a block that no longer exists.
        ///
        /// <b>Switch the terminal before closing.</b> Closing is a transition that finishes over
        /// the next frames, while the terminal's tab rebuild is immediate; in this order the
        /// terminal already shows the right block by the time the closing screen uncovers it.
        ///
        /// A group is refused rather than attempted: it has no entity, so there is no terminal it
        /// could stand for. The button is not drawn on a group's frame either
        /// (<see cref="PortGeometry.HasCardButton"/>), so this is only a guard.
        /// </summary>
        private void TryJumpToTerminal(Node node)
        {
            if (node == null || node.IsGroup) return;

            string failure;
            if (!TerminalJump.TryOpen(node.EntityId, out failure))
            {
                // Warn both shows the sentence and writes it to the log — one line per event.
                Warn(failure ?? VgsText.WarnJumpFailed);
                return;
            }

            VgsLog.Info("terminal jump to block " + node.EntityId + ", closing the editor");

            // Closing is a separate call into the game, and the terminal has already been switched
            // by now. A throw here would reach the caller's throttled handler and read as "input
            // handling failed", which is not what happened — so it is caught here, where the state
            // is known: the jump succeeded, only the close did not, and the player can press Esc.
            try
            {
                Close();
            }
            catch (Exception e)
            {
                VgsLog.Error("terminal jump succeeded but the editor could not close", e);
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
            _hoverButton = null;
            _hoverRemoveNode = null;
            _hoverRemovePort = null;
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
            _hoverButton = null;
            _hoverRemoveNode = null;
            _hoverRemovePort = null;

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
                int fanIn = _graph.CountEdgesToInput(target, action.Key);
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
        /// A group target is written as a group item, not as one item per member: that is the
        /// whole point of a group in this editor (ADR-0015). One cell, one item, and the game
        /// applies the action to every functional member when it fires. The two kinds share
        /// every step except the item itself, so they are branches of one method rather than
        /// two methods that would drift apart.
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
            if (sourceBlock == null)
            {
                Warn(VgsText.WarnBlockGone);
                return false;
            }

            ToolbarCell cell;
            ToolbarWriter.WriteResult result;
            bool toGroup = target.IsGroup;

            if (toGroup)
            {
                result = ToolbarWriter.TryWriteGroup(sourceBlock, source, port, target.GroupName,
                                                     action.Key, out cell);
            }
            else
            {
                IMyTerminalBlock targetBlock = ResolveBlock(target.EntityId);
                if (targetBlock == null)
                {
                    Warn(VgsText.WarnBlockGone);
                    return false;
                }

                result = ToolbarWriter.TryWrite(sourceBlock, source, port, targetBlock, action.Key, out cell);
            }

            if (result != ToolbarWriter.WriteResult.Ok)
            {
                Warn(DescribeWriteFailure(result, source, target, action));
                return false;
            }

            bool connected = toGroup
                ? _graph.TryConnectToGroup(source.EntityId, port.Key, target.GroupName, action.Key, cell)
                : _graph.TryConnect(source.EntityId, port.Key, target.EntityId, action.Key, cell);

            if (!connected)
            {
                // The graph refused an item the block now holds: put the cell back the way it
                // was rather than leaving wiring the editor does not show.
                VgsLog.Warn("bind rejected by graph: " + port.Key + " -> " + action.Key + " @ " + cell);
                ToolbarWriter.TryClear(sourceBlock, source, cell);
                Warn(VgsText.WarnRejected);
                return false;
            }

            // The new connection changes which port bars exist, so the stacks are recomputed.
            // A group's frame has to grow with its bars, so the frames follow.
            _graph.LayoutPorts();
            if (_graph.GroupCount > 0) PortGeometry.LayoutGroupFrames(_graph);

            VgsLog.Info("connected " + source.EntityId + " (" + port.Key + ") -> "
                        + (toGroup ? "group:" + target.GroupName : target.EntityId.ToString())
                        + " (" + action.Key + ") @ " + cell);
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
                    return target.IsGroup
                        ? string.Format(VgsText.WarnGroupGoneFormat, target.GroupName)
                        : string.Format(VgsText.WarnActionGoneFormat, target.DisplayName, action.DisplayName);
                default:
                    return VgsText.WarnRejected;
            }
        }

        /// <summary>
        /// Removes every connection the given port carries, together with the items the source
        /// blocks hold in their toolbars.
        ///
        /// A port carries a set, not a single connection: an output may drive several targets
        /// (fan-out) and an input may be driven by several sources (fan-in). The button on the
        /// port therefore clears the whole set — that is what "delete this port's connections"
        /// means, and leaving some of them behind would keep the bar on screen after the click.
        ///
        /// <b>Order: toolbar first, graph second.</b> The toolbar is the connection (ADR-0011),
        /// and the refresh pass rebuilds edges from it. An edge dropped from the graph while its
        /// item stayed in the block would come back on the next refresh — the line would
        /// reappear under the user's cursor with no explanation. When the game refuses the clear
        /// (a remote client does not own the block's toolbar), that connection is kept and the
        /// user is told why, which is the same honesty the write path applies to a refused
        /// write.
        ///
        /// A destroyed block needs no clearing: its toolbar went with it, and the stale edge is
        /// dropped from the graph here.
        ///
        /// The other end of each connection needs no special handling:
        /// <see cref="GridGraph.LayoutPorts"/> gives a port a drawing slot only while it carries
        /// at least one edge, so a port that has just lost its last connection — the source's
        /// output or the target's input — disappears by itself.
        /// </summary>
        private void RemovePortConnections(Node node, Port port)
        {
            if (node == null || port == null) return;

            _removeBuffer.Clear();
            _graph.CollectEdgesForPort(node, port.Key, port.Direction, _removeBuffer);
            if (_removeBuffer.Count == 0) return;

            int removed = 0;
            int changedUnderEditor = 0;
            long warnedAbout = 0;

            for (int i = 0; i < _removeBuffer.Count; i++)
            {
                Edge edge = _removeBuffer[i];

                Node source = _graph.GetNode(edge.SourceEntityId);
                IMyTerminalBlock sourceBlock = ResolveBlock(edge.SourceEntityId);

                // Only a cell that really holds this connection needs clearing. An edge without a
                // valid cell cannot be expressed in a toolbar at all (TryConnect refuses one), so
                // it is dropped without a warning rather than kept forever as an undeletable line.
                if (edge.Cell.IsValid && sourceBlock != null && source != null)
                {
                    CellState state = ReadCellState(sourceBlock, edge);

                    // The cell no longer holds this connection: it is empty, or the player
                    // replaced it by hand in the terminal during the up-to-one-refresh window when
                    // the graph lags the world. Either way it is not ours to clear, so it is left
                    // alone; the reconcile pass then turns whatever is there now into its own edge.
                    if (state == CellState.HoldsOther)
                    {
                        changedUnderEditor++;
                        if (_graph.RemoveEdge(edge)) removed++;
                        continue;
                    }

                    // HoldsThis is the only state that may be cleared. Unreadable is NOT "safe to
                    // clear": the player's edit cannot be ruled out, so the connection is kept and
                    // the reason is shown — the same answer as a refused clear. Dropping the edge
                    // here would make the line reappear at the next reconcile pass with no
                    // explanation, which is the defect this whole method exists to avoid.
                    if (state != CellState.HoldsThis
                        || !ToolbarWriter.TryClear(sourceBlock, source, edge.Cell))
                    {
                        WarnRemoveRefused(source, ref warnedAbout);
                        continue;
                    }
                }

                if (_graph.RemoveEdge(edge)) removed++;
            }

            _removeBuffer.Clear();
            _hoverRemoveNode = null;
            _hoverRemovePort = null;

            // The stacks are a derived view of the edges: the port that just lost its last
            // connection must lose its bar, and the stack under it must close up.
            _graph.LayoutPorts();
            if (_graph.GroupCount > 0) PortGeometry.LayoutGroupFrames(_graph);

            if (changedUnderEditor > 0)
            {
                VgsLog.Info(changedUnderEditor + " cell(s) no longer held their connection and were "
                            + "left untouched; the reconcile pass re-reads them");
            }

            if (removed > 0)
            {
                VgsLog.Info("removed " + removed + " connection(s) at " + node.EntityId
                            + " (" + port.Key + ")");
            }
        }

        /// <summary>
        /// What a connection's toolbar cell currently holds, as far as the editor can tell.
        ///
        /// Three outcomes, and they must not be collapsed into a bool: the caller's answer to
        /// "holds this connection" is *clear the cell*, its answer to "holds something else" is
        /// *leave it alone*, and its answer to "cannot tell" is *do nothing and say so*. A bool
        /// would make the third indistinguishable from the second and the removal would silently
        /// do nothing (the line then reappears at the next reconcile pass).
        /// </summary>
        private enum CellState
        {
            /// <summary>The cell holds exactly this connection: safe to clear.</summary>
            HoldsThis = 0,

            /// <summary>
            /// The cell no longer holds this connection: it is empty, or it holds another item.
            /// Either way it is not ours to clear — an empty cell has nothing to clear, and the
            /// other item is the player's edit.
            /// </summary>
            HoldsOther = 1,

            /// <summary>The toolbar could not be read. Neither clearing nor dropping is safe.</summary>
            Unreadable = 2
        }

        /// <summary>
        /// Reads the cell this edge occupies and reports which of the three cases it is: the same
        /// target block and action, a different item, or nothing readable.
        ///
        /// A read, not a write, so it is safe on a remote client too — which is the point: the
        /// check is what stops a removal from destroying an edit the player made in the terminal
        /// during the up-to-one-refresh window when the graph is behind the world.
        /// </summary>
        private static CellState ReadCellState(IMyTerminalBlock sourceBlock, Edge edge)
        {
            try
            {
                IMyToolbar toolbar = ToolbarProbe.GetToolbar(sourceBlock, edge.Cell.ToolbarIndex);
                if (toolbar == null) return CellState.Unreadable;

                CellContents contents = ToolbarWriter.TryDescribe(toolbar, edge.Cell.Index);
                if (contents.IsEmpty) return CellState.HoldsOther;

                bool holdsThis = edge.TargetsGroup
                    ? contents.HoldsGroupAction(edge.TargetGroupName, edge.TargetPortKey)
                    : contents.HoldsBlockAction(edge.TargetEntityId, edge.TargetPortKey);

                return holdsThis ? CellState.HoldsThis : CellState.HoldsOther;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("cell-state", "could not read toolbar cell", e, 300);
                return CellState.Unreadable;
            }
        }

        /// <summary>
        /// Reports that a connection could not be removed from its block's toolbar, once per
        /// source block.
        ///
        /// Once, because a fan-in of three connections from the same block would otherwise queue
        /// the same sentence three times into one bottom bar, which shows only the last copy — the
        /// user would see one message and no indication that three attempts were made.
        /// <paramref name="warnedAbout"/> is the caller's running record, so the deduplication
        /// survives the loop without the caller keeping a set.
        /// </summary>
        private void WarnRemoveRefused(Node source, ref long warnedAbout)
        {
            if (source == null) return;
            if (warnedAbout == source.EntityId) return;

            warnedAbout = source.EntityId;
            Warn(string.Format(VgsText.WarnRemoveRefusedFormat, source.DisplayName));
        }

        /// <summary>Shows a failure on the bottom bar and records it in the log.</summary>
        private void Warn(string text)
        {
            _warningText = text;
            _warningUntilFrame = _screenFrame + VgsConstants.WarningFrames;
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

        /// <summary>
        /// Topmost node under a graph-space point, or null. Ports count as the node.
        ///
        /// Blocks are tested before groups: the frame of a group surrounds its members, so a
        /// click on a card must select the card and not the group that contains it. Groups are
        /// only hit through their title strip and their port bars
        /// (<see cref="PortGeometry.HitNode"/>), which is what makes that ordering sufficient.
        /// </summary>
        private Node HitTest(Vector2 graphPoint)
        {
            IList<Node> nodes = _graph.Nodes;
            // Iterate backwards so the visually topmost (last drawn) node wins.
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                if (PortGeometry.HitNode(nodes[i], graphPoint)) return nodes[i];
            }

            IList<Node> groups = _graph.Groups;
            for (int i = groups.Count - 1; i >= 0; i--)
            {
                if (PortGeometry.HitNode(groups[i], graphPoint)) return groups[i];
            }
            return null;
        }

        /// <summary>
        /// Topmost node whose card button row is under the pointer, and which of its buttons.
        ///
        /// One test for the whole row, because the row is one control group (ADR-0020): the jump
        /// button, the watch button, the yes/no pair and the single bind button differ only in
        /// what they do, so resolving them separately would mean five loops over the same nodes
        /// and an ordering question between them.
        /// </summary>
        private Node HitTestCardButton(Vector2 graphPoint, out CardButtonKind kind)
        {
            kind = CardButtonKind.Jump;
            float aspect = VgsScreenDraw.ClientAspect;
            IList<Node> nodes = _graph.Nodes;
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                CardButtonKind hit;
                if (PortGeometry.HitCardButton(nodes[i], graphPoint, aspect, out hit))
                {
                    kind = hit;
                    return nodes[i];
                }
            }
            return null;
        }

        /// <summary>Topmost controller whose watch port carries the "x" under the pointer.</summary>
        private Node HitTestWatchRemoveButton(Vector2 graphPoint, out WatchTarget watch)
        {
            watch = null;
            float aspect = VgsScreenDraw.ClientAspect;

            IList<Node> nodes = _graph.Nodes;
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                WatchTarget hit = PortGeometry.HitWatchRemoveButton(nodes[i], graphPoint, aspect);
                if (hit == null) continue;
                watch = hit;
                return nodes[i];
            }
            return null;
        }

        /// <summary>Topmost node whose port remove button is under the pointer, and which port.</summary>
        private Node HitTestRemoveButton(Vector2 graphPoint, out Port port)
        {
            port = null;
            float aspect = VgsScreenDraw.ClientAspect;

            // Blocks first, for the same reason as in HitTest: a group's frame contains its
            // members, and a member's own remove button is the more specific target.
            IList<Node> nodes = _graph.Nodes;
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                Port hit = PortGeometry.HitPortRemoveButton(nodes[i], graphPoint, aspect);
                if (hit == null) continue;
                port = hit;
                return nodes[i];
            }

            IList<Node> groups = _graph.Groups;
            for (int i = groups.Count - 1; i >= 0; i--)
            {
                Port hit = PortGeometry.HitPortRemoveButton(groups[i], graphPoint, aspect);
                if (hit == null) continue;
                port = hit;
                return groups[i];
            }
            return null;
        }

        /// <summary>
        /// Refreshes the hover targets used for the highlight of a card, of the buttons on it and
        /// of the remove button on one of its ports.
        ///
        /// Hover is recomputed from the pointer every input pass rather than cached until a
        /// move event: the canvas pans and zooms under a still pointer, so "under the pointer"
        /// changes without the pointer moving.
        /// </summary>
        private void UpdateHover(Vector2 mouse, Vector2 canvasTopLeft, Vector2 canvasSize)
        {
            _hoverRemoveNode = null;
            _hoverRemovePort = null;
            _hoverWatchRemoveNode = null;
            _hoverWatchRemoveTarget = null;

            if (!PortGeometry.Contains(canvasTopLeft, canvasSize, mouse))
            {
                _hoverNode = null;
                _hoverButton = null;
                return;
            }

            Vector2 graphPos = ScreenToGraph(mouse, canvasTopLeft);

            // The remove button is the smallest target on the canvas, and it is drawn on top of
            // a bar that belongs to the card. Resolving it first is what makes the highlight
            // land on the disc instead of on the whole port.
            Port removePort;
            Node removeNode = HitTestRemoveButton(graphPos, out removePort);
            if (removeNode != null)
            {
                _hoverRemoveNode = removeNode;
                _hoverRemovePort = removePort;
                _hoverNode = null;
                _hoverButton = null;
                return;
            }

            // A watch port's "x" is the same kind of target (ADR-0017).
            WatchTarget watchRemove;
            Node watchRemoveNode = HitTestWatchRemoveButton(graphPos, out watchRemove);
            if (watchRemoveNode != null)
            {
                _hoverWatchRemoveNode = watchRemoveNode;
                _hoverWatchRemoveTarget = watchRemove;
                _hoverNode = null;
                _hoverButton = null;
                return;
            }

            // The card's button row before the card: a button sits on the card, and a card
            // highlight under a button would suggest a drag where a click starts a gesture.
            CardButtonKind kind;
            _hoverButton = HitTestCardButton(graphPos, out kind);
            _hoverButtonKind = kind;

            // A card under a control is not "hovered": the control is the thing about to be
            // clicked, and highlighting the card as well would suggest a drag.
            _hoverNode = _hoverButton == null ? HitTest(graphPos) : null;
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

                // Order matters: group frames are backdrops for their members' cards, so they
                // go first of all; then lines, so a connection never crosses a block's text;
                // then cards, then the port bars of both kinds of node, so a bar is never
                // hidden under a line that ends on its dot.
                DrawGroupFrames(canvasTopLeft, canvasSize);
                DrawEdges(canvasTopLeft, canvasSize);
                DrawWatchLines(canvasTopLeft, canvasSize);
                DrawPendingLine(canvasTopLeft, canvasSize);
                DrawPendingWatchLine(canvasTopLeft, canvasSize);
                DrawNodes(canvasTopLeft, canvasSize);
                DrawGroupPorts(canvasTopLeft, canvasSize);

                DrawBottomBar();
                DrawHeader();

                if (_menuTarget != null) DrawActionMenu();

                // The modes popup hangs above the bar's top edge and overlaps the canvas, so it
                // is drawn after everything it covers.
                if (_modesMenuOpen) DrawModesMenu();
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
                Node target = _graph.GetTargetNode(edge);
                if (source == null || target == null) continue;

                Port outPort = source.FindEvent(edge.SourcePortKey);
                Port inPort = target.FindAction(edge.TargetPortKey);
                if (outPort == null || inPort == null) continue;

                Vector2 from, to;
                float aspect = VgsScreenDraw.ClientAspect;
                if (!PortGeometry.TryPortDot(source, outPort, aspect, out from)) continue;
                if (!PortGeometry.TryPortDot(target, inPort, aspect, out to)) continue;

                // A line belongs to both of its ends: it is a ghost only when *both* are excluded
                // by the search, so a matching block keeps its wiring visible — otherwise the
                // player could not tell what the block they found is connected to (ADR-0023).
                DrawRoute(GraphToScreen(from, topLeft), GraphToScreen(to, topLeft),
                          Fade(ColLine, MaxAlpha(source, target)), topLeft, size);
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
            VgsScreenDraw.Disc(end, VgsConstants.PortControlRadius * _zoom, topLeft, size, ColLinePending);
        }

        /// <summary>
        /// Draws every watch link: a dashed line from a watch port's dot to the watched block's
        /// card (ADR-0017).
        ///
        /// It ends at the card's centre, not at a port: the watched block has no "observe me"
        /// port, and a line into one of its real ports would claim a call the controller never
        /// makes. Cards are drawn after lines, so the visible end of the line is the card's
        /// border — which reads as "the line ends in the block".
        ///
        /// Dashed, unlike a connection, because the two mean different things: a solid line is
        /// "this fires that", a dashed one is "this watches that".
        /// </summary>
        private void DrawWatchLines(Vector2 topLeft, Vector2 size)
        {
            float aspect = VgsScreenDraw.ClientAspect;
            float dash = VgsConstants.WatchDashLength * _zoom;
            float gap = VgsConstants.WatchDashGap * _zoom;

            IList<Node> nodes = _graph.Nodes;
            int drawn = 0;

            for (int i = 0; i < nodes.Count; i++)
            {
                Node controller = nodes[i];
                if (controller.Watched.Count == 0) continue;

                for (int w = 0; w < controller.Watched.Count; w++)
                {
                    // The same cap a connection line obeys: a canvas full of controllers must not
                    // be able to queue an unbounded number of dash quads per frame.
                    if (drawn >= VgsConstants.MaxRenderedEdges) return;

                    WatchTarget watch = controller.Watched[w];
                    if (watch.Slot < 0) continue;

                    Vector2 from;
                    if (!PortGeometry.TryWatchDot(controller, watch, aspect, out from)) continue;

                    Node target = _graph.GetNode(watch.EntityId);
                    Vector2 to;

                    if (target == null || !PortGeometry.TryCardCentre(target, out to))
                    {
                        // The watched block is hidden from the schema or gone: the port still
                        // exists (the list is the player's data), but there is nowhere on the
                        // canvas to draw its line to.
                        continue;
                    }

                    DrawDashedRoute(GraphToScreen(from, topLeft), GraphToScreen(to, topLeft),
                                    Fade(ColWatchLine, MaxAlpha(controller, target)), dash, gap,
                                    topLeft, size);
                    drawn++;
                }
            }
        }

        /// <summary>
        /// Draws the watch line from the controller's bell to the pointer while a watch is being
        /// attached, and the red refusal flash (ADR-0017 §5).
        ///
        /// The line starts at the button's centre because the port does not exist yet: the point
        /// the line leaves from is the point the player pressed, and the moment the port appears
        /// the line moves to its dot.
        /// </summary>
        private void DrawPendingWatchLine(Vector2 topLeft, Vector2 size)
        {
            if (_watchSource == null || !_mouseValid) return;

            Vector2 from;
            if (!PortGeometry.TryWatchButton(_watchSource, VgsScreenDraw.ClientAspect, out from)) return;

            bool refused = _screenFrame < _watchErrorUntilFrame;
            Color colour = refused ? ColWatchError : ColWatchLine;

            Vector2 start = GraphToScreen(from, topLeft);
            Vector2 end = _mouse;

            DrawDashedRoute(start, end, colour,
                            VgsConstants.WatchDashLength * _zoom,
                            VgsConstants.WatchDashGap * _zoom,
                            topLeft, size);

            VgsScreenDraw.Disc(end, VgsConstants.PortControlRadius * _zoom, topLeft, size, colour);
        }

        /// <summary>
        /// Draws an orthogonal route with every segment dashed. The route itself comes from
        /// <see cref="EdgeRouting.Build"/>, the same router a connection uses, so a watch line
        /// bends and arrives exactly like a wire.
        /// </summary>
        private void DrawDashedRoute(Vector2 from, Vector2 to, Color color, float dash, float gap,
                                     Vector2 clipTopLeft, Vector2 clipSize)
        {
            EdgeRouting.Build(from, to, VgsConstants.LineGutter * _zoom, _route);

            for (int i = 0; i < _route.Count; i++)
            {
                Vector4 segment = _route[i];
                VgsScreenDraw.SegmentDashed(
                    new Vector2(segment.X, segment.Y),
                    new Vector2(segment.Z, segment.W),
                    VgsConstants.LineThickness, dash, gap,
                    clipTopLeft, clipSize, color);
            }
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
                // A grid with no visible block is only "empty" when it has no visible group
                // either: a group whose members are all hidden is not in the graph at all, so
                // the two counts are both zero exactly when there is nothing to draw.
                if (_graph.GroupCount == 0)
                {
                    VgsScreenDraw.TextCentered(VgsText.EmptyGrid, topLeft + size * 0.5f, 0.8f, ColTextDim);
                    return;
                }
            }

            // Cheap frustum culling: only draw what is on screen. On a large ship this is
            // the difference between a smooth canvas and a slideshow. There is deliberately no
            // "shown N of M" notice for the blocks this drops: the header already carries the
            // totals, and a line under the canvas that appears and disappears as the player zooms
            // was noise on top of a picture that is self-explanatory (ADR-0023).
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
        }

        /// <summary>
        /// Draws one card: name, subtype, the optional status line, its button row and its port
        /// bars.
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

            // A card the search excludes is still drawn — as a ghost at
            // VgsConstants.SearchDimOpacity — so the player keeps the shape of the whole schema
            // while looking for one block (ADR-0023). Every colour of the card and of everything
            // hanging off it goes through this one multiplier.
            float alpha = AlphaOf(node);

            Vector2 cardSize = new Vector2(VgsConstants.NodeWidth, PortGeometry.CardHeight(node)) * _zoom;

            // The card is a single filled rectangle. The port bars are separate rectangles
            // below it and must *not* be covered by this fill, or the stack would read as one
            // tall block instead of as ports attached to the card's bottom edge.
            VgsScreenDraw.RectClipped(screenPos, cardSize, clipTopLeft, clipSize, Fade(ColNode, alpha));

            Color border = selected ? ColNodeSelected : (hovered ? ColNodeHover : ColNodeBorder);
            border = Fade(border, alpha);
            VgsScreenDraw.BorderClipped(screenPos, cardSize, clipTopLeft, clipSize, border,
                                        selected ? Hairline * 2f : Hairline);

            float zoomText = CardTextScale;
            float pad = cardSize.X * PortGeometry.CardTextPaddingRatio;
            float textRight = cardSize.X - pad;
            float textLeft = pad;

            // The card's buttons occupy the right end of the card — all of them, in one row
            // (ADR-0020) — so the labels stop before the row instead of running underneath
            // icons that are drawn on top of them.
            //
            // The span comes from `PortGeometry`, in graph units, and it is already
            // aspect-corrected there: a button is round on *screen*, so its horizontal extent is
            // radius × aspect, and the gap before the text is corrected the same way. Reserving
            // the vertical radius would leave the text running under the icons on every window
            // that is not square.
            float spanLeft, spanRight;
            if (PortGeometry.TryCardTextSpan(node, VgsScreenDraw.ClientAspect, out spanLeft, out spanRight))
            {
                textLeft = (spanLeft - node.Position.X) * _zoom;
                textRight = (spanRight - node.Position.X) * _zoom;
            }
            else if (PortGeometry.CardButtonCount(node) > 0)
            {
                // The row fills the card and the span has collapsed: this is the portrait window
                // the four-button row cannot leave room in (ADR-0021 §Consequences). Drawing the
                // name anyway would put it *under* the icons, which reads as a rendering fault;
                // a card that shows its controls and no text is the honest degradation.
                textRight = textLeft;
            }

            float textWidth = textRight - textLeft;
            if (textWidth < 0f) textWidth = 0f;

            // Lines are placed by dividing the card's height into as many bands as it has
            // lines, rather than by fixed fractions of the card: a controller's card carries a
            // third line, and fixed fractions would leave that line half outside its own band.
            int lines = node.HasStatusLine ? 3 : 2;
            float band = cardSize.Y / lines;

            // A zero span means the row has filled the card (the portrait window of ADR-0021
            // §Consequences). The lines are skipped rather than drawn with maxWidth = 0: what a
            // renderer does with a zero width is not something this project has verified, and
            // "the name is not drawn" is a claim the ADR makes and the acceptance list checks.
            if (textWidth > 0f)
            {
                VgsScreenDraw.Text(node.DisplayName,
                                   screenPos + new Vector2(textLeft, band * 0.10f),
                                   VgsConstants.NodeTitleScale * zoomText, Fade(ColText, alpha),
                                   textWidth);

                VgsScreenDraw.Text(node.SubtypeId,
                                   screenPos + new Vector2(textLeft, band * 1.10f),
                                   VgsConstants.NodeSubtitleScale * zoomText, Fade(ColTextDim, alpha),
                                   textWidth);

                // The mode description: what an event controller actually checks. A third line, in
                // the watch colour, because it is the block's *state* rather than its identity —
                // and it is the only place the player can see the threshold without the terminal.
                if (node.HasStatusLine && !string.IsNullOrEmpty(node.StatusText))
                {
                    VgsScreenDraw.Text(node.StatusText,
                                       screenPos + new Vector2(textLeft, band * 2.10f),
                                       VgsConstants.NodeStatusScale * zoomText, Fade(ColWatch, alpha),
                                       textWidth);
                }
            }

            // The alpha is computed once here and passed down. Recomputing it inside
            // DrawPortBar and DrawCardButton would run the search rule once per port and once
            // per button of the card, on every frame — same picture, a dozen times the string
            // scans.
            DrawPortBars(node, border, alpha, clipTopLeft, clipSize);

            // The card's buttons are drawn last so they are never covered by a port bar of a
            // neighbouring card that happens to be drawn later.
            DrawCardButtons(node, alpha, clipTopLeft, clipSize);
        }

        /// <summary>
        /// Draws the watch bars of one controller: the yellow bars under the card, each with the
        /// watched block's name, a dot on the right (the line leaves from there) and an "x" on
        /// the left (removes the block from the list).
        ///
        /// They are drawn with the node's port bars, not in a pass of their own, because they are
        /// part of the same column: a watch bar that was drawn after the next card would cover
        /// it. The frame colour is the card's, like every other bar of the node (ADR-0014) —
        /// only the fill differs, and that is what says "this is a watch, not a call".
        /// </summary>
        private void DrawWatchBars(Node node, Color border, float alpha, Vector2 clipTopLeft,
                                   Vector2 clipSize)
        {
            for (int i = 0; i < node.Watched.Count; i++)
            {
                WatchTarget watch = node.Watched[i];
                if (watch.Slot < 0) continue;

                Vector2 topLeft, size;
                if (!PortGeometry.TryWatchBarRect(node, watch, out topLeft, out size)) continue;

                Vector2 screenTopLeft = GraphToScreen(topLeft, clipTopLeft);
                Vector2 screenSize = size * _zoom;

                VgsScreenDraw.RectClipped(screenTopLeft, screenSize, clipTopLeft, clipSize,
                                          Fade(ColWatchFill, alpha));
                VgsScreenDraw.BorderClipped(screenTopLeft, screenSize, clipTopLeft, clipSize, border,
                                            Hairline);

                Vector2 labelTopLeft, labelSize;
                if (PortGeometry.TryWatchLabelRect(node, watch, VgsScreenDraw.ClientAspect,
                                                   out labelTopLeft, out labelSize))
                {
                    Vector2 labelScreen = GraphToScreen(labelTopLeft, clipTopLeft);
                    float textHeight = PortLabelHeight;
                    if (textHeight <= 0f) textHeight = screenSize.Y * VgsConstants.PortLabelFallbackHeightRatio;

                    VgsScreenDraw.Text(watch.DisplayName,
                                       new Vector2(labelScreen.X,
                                                   PortGeometry.CentredTextTop(labelScreen.Y,
                                                                               screenSize.Y, textHeight)),
                                       VgsConstants.PortLabelScale * _zoom,
                                       Fade(ColText, alpha), labelSize.X * _zoom);
                }

                float radius = VgsConstants.PortControlRadius * _zoom;

                Vector2 dotCentre;
                if (PortGeometry.TryWatchDot(node, watch, VgsScreenDraw.ClientAspect, out dotCentre))
                {
                    DrawPortDot(GraphToScreen(dotCentre, clipTopLeft), radius, Fade(ColWatch, alpha),
                                clipTopLeft, clipSize);
                }

                Vector2 removeCentre;
                if (PortGeometry.TryWatchRemoveButton(node, watch, VgsScreenDraw.ClientAspect,
                                                      out removeCentre))
                {
                    bool hot = ReferenceEquals(node, _hoverWatchRemoveNode)
                               && ReferenceEquals(watch, _hoverWatchRemoveTarget);

                    DrawPortRemoveButton(GraphToScreen(removeCentre, clipTopLeft),
                                         ColWatch, alpha, hot, clipTopLeft, clipSize);
                }
            }
        }

        /// <summary>
        /// Draws the frame of every group: a light-grey fill, a border, and a title strip with
        /// the group's name (ADR-0015).
        ///
        /// The frame is drawn <em>before</em> the cards, so it reads as a backdrop: the members
        /// are still the thing the player acts on, and the frame only says which blocks belong
        /// together. A group is not a card and deliberately does not look like one — it has no
        /// "+", no subtype line and no shadow, because it has no toolbar and nothing to fire.
        ///
        /// The frame has no card of its own to be selected by, so its title strip carries the
        /// selection and hover highlight. That is also its only click target: the interior
        /// belongs to the members.
        /// </summary>
        private void DrawGroupFrames(Vector2 clipTopLeft, Vector2 clipSize)
        {
            IList<Node> groups = _graph.Groups;
            for (int i = 0; i < groups.Count; i++)
            {
                Node group = groups[i];

                Vector2 topLeft, size;
                if (!PortGeometry.TryBodyRect(group, out topLeft, out size)) continue;

                Vector2 screen = GraphToScreen(topLeft, clipTopLeft);
                Vector2 screenSize = size * _zoom;

                // Cull against the canvas the same way cards are culled, so a group scrolled
                // off-screen costs two comparisons instead of a rectangle and a border.
                if (screen.X > clipSize.X || screen.Y > clipSize.Y) continue;
                if (screen.X + screenSize.X < 0f || screen.Y + screenSize.Y < 0f) continue;

                bool selected = ReferenceEquals(group, _selected);
                bool hovered = ReferenceEquals(group, _hoverNode);

                // A group follows its members through the search: it is a backdrop for them, so
                // it is dimmed only when none of its drawn members matches (ADR-0023).
                float alpha = AlphaOf(group);

                VgsScreenDraw.RectClipped(screen, screenSize, clipTopLeft, clipSize,
                                          Fade(ColGroupFill, alpha));

                Vector2 titleTopLeft, titleSize;
                if (PortGeometry.TryGroupTitleRect(group, out titleTopLeft, out titleSize))
                {
                    VgsScreenDraw.RectClipped(GraphToScreen(titleTopLeft, clipTopLeft),
                                              titleSize * _zoom, clipTopLeft, clipSize,
                                              Fade(ColGroupTitle, alpha));
                }

                Color border = selected ? ColGroupSelected : (hovered ? ColNodeHover : ColGroupBorder);
                VgsScreenDraw.BorderClipped(screen, screenSize, clipTopLeft, clipSize, Fade(border, alpha),
                                            selected ? Hairline * 2f : Hairline);

                if (titleSize.Y > 0f)
                {
                    VgsScreenDraw.Text(group.DisplayName,
                                       GraphToScreen(titleTopLeft, clipTopLeft)
                                           + new Vector2(screenSize.X * 0.012f, titleSize.Y * _zoom * 0.18f),
                                       VgsConstants.GroupTitleScale * CardTextScale,
                                       Fade(ColText, alpha), screenSize.X * 0.976f);
                }
            }
        }

        /// <summary>
        /// Draws the port bars of every group.
        ///
        /// A group has incoming ports only — what may be done to its members — so this is a
        /// short pass and it never has an output stack to skip past. It runs after the cards
        /// because a group's bars hang under its frame, which is under its members, so nothing
        /// is covered; and after the lines, because a line ends on the bar's dot and has to be
        /// under it.
        /// </summary>
        private void DrawGroupPorts(Vector2 clipTopLeft, Vector2 clipSize)
        {
            IList<Node> groups = _graph.Groups;
            for (int i = 0; i < groups.Count; i++)
            {
                Node group = groups[i];
                bool selected = ReferenceEquals(group, _selected);

                // One alpha per group: its bars belong to the frame and follow it (ADR-0023).
                // The border is faded here, not inside DrawPortBar, for the same reason a block's
                // is faded in DrawNode: a bar is drawn in its node's own frame colour, so a ghost
                // node whose bars kept a full-opacity frame would be the brightest thing on the
                // canvas — exactly what the search is supposed to de-emphasise.
                float alpha = AlphaOf(group);
                Color border = Fade(selected ? ColGroupSelected : ColGroupBorder, alpha);

                for (int p = 0; p < group.Actions.Count; p++)
                {
                    DrawPortBar(group, group.Actions[p], border, alpha, clipTopLeft, clipSize);
                }
            }
        }

        /// <summary>
        /// Draws the card's button row: one icon per button, in the order the row is defined in
        /// <see cref="PortGeometry"/> (ADR-0020, ADR-0021).
        ///
        /// <b>One shape for every button.</b> All of them are bare icons of one size — no disc,
        /// no square, no letter. Which icon it is says what the button does: the square with an
        /// arrow leaving it jumps to the block's terminal, a bell starts a watch, a filled
        /// lightbulb binds the "yes" list, an outlined one the "no" list, and a square-plus the
        /// single list of a block with one toolbar.
        ///
        /// <b>One colour too.</b> Every button of the row is near-white at rest and warms to
        /// <see cref="ColPlusFillHot"/> under the pointer — the same two states the "+" has
        /// always had. The watch button used to be drawn in the watch yellow, on the theory that
        /// colour should separate observation from action; in the game that made it the one
        /// permanently yellow control on the canvas and it read as a state ("watching") rather
        /// than as a button, at every zoom and whether or not the controller was watching
        /// anything. Colour now marks what *happened* — the watch bars and the dashed line are
        /// yellow — while a control's colour marks what the pointer is doing to it.
        ///
        /// When a texture is missing the button falls back to its old glyph — "&gt;", "#", "T",
        /// "F" or "+" — in the same colour, so a missing atlas costs the shape of the mark and
        /// never the control (ADR-0019 §6).
        ///
        /// <b>One pass over the row.</b> The kinds are collected into
        /// <see cref="_cardButtonRow"/> and each button's rectangle is computed from its position
        /// in that row, so this runs once per card instead of searching the row once per button.
        /// The buffer is a field because <see cref="OnDraw"/> runs every frame for every visible
        /// card and allocating here would break R010.
        /// </summary>
        private void DrawCardButtons(Node node, float alpha, Vector2 clipTopLeft, Vector2 clipSize)
        {
            int count = PortGeometry.GetCardButtons(node, _cardButtonRow);
            float aspect = VgsScreenDraw.ClientAspect;

            for (int i = 0; i < count; i++)
            {
                Vector2 topLeft, size;
                PortGeometry.CardButtonRectAt(node, i, count, aspect, out topLeft, out size);
                DrawCardButton(node, _cardButtonRow[i], topLeft + size * 0.5f, alpha,
                               clipTopLeft, clipSize);
            }
        }

        /// <summary>Draws one button of a card's row: its icon, or its glyph when the icon is unavailable.</summary>
        private void DrawCardButton(Node node, CardButtonKind kind, Vector2 center, float alpha,
                                    Vector2 clipTopLeft, Vector2 clipSize)
        {
            Vector2 screenCenter = GraphToScreen(center, clipTopLeft);

            bool hot = ReferenceEquals(node, _hoverButton) && _hoverButtonKind == kind;
            Color color = Fade(hot ? ColPlusFillHot : ColPlusFill, alpha);

            float diameter = VgsConstants.CardButtonDiameter * _zoom;
            if (VgsScreenDraw.Icon(IconOf(kind), screenCenter,
                                   diameter * VgsConstants.CardButtonIconRatio, color,
                                   clipTopLeft, clipSize))
            {
                return;
            }

            // No texture: the glyph the button carried before it became an icon, drawn in the
            // same colour so the control is still there and still says the same thing.
            VgsScreenDraw.TextCentered(GlyphOf(kind), screenCenter,
                                       VgsConstants.CardButtonGlyphScale * CardTextScale, color);
        }

        /// <summary>
        /// The icon each button draws. The mapping lives here rather than in
        /// <see cref="PortGeometry"/> because it is a presentation decision: geometry says where
        /// a button is, this says what it looks like. Which icon is which is recorded in
        /// `docs/07-icon-catalog.md`, and `tools/check-icon-catalog.ps1` fails when this table
        /// and the catalogue disagree.
        /// </summary>
        private static VgsIcon IconOf(CardButtonKind kind)
        {
            switch (kind)
            {
                case CardButtonKind.Jump: return VgsIcon.ShareFromSquare;
                case CardButtonKind.Watch: return VgsIcon.Bell;
                case CardButtonKind.BindYes: return VgsIcon.LightbulbSolid;
                case CardButtonKind.BindNo: return VgsIcon.LightbulbRegular;
                default: return VgsIcon.SquarePlus;
            }
        }

        /// <summary>
        /// The glyph a button falls back to when its texture is unavailable: the mark it carried
        /// before it became an icon.
        /// </summary>
        private static string GlyphOf(CardButtonKind kind)
        {
            switch (kind)
            {
                case CardButtonKind.Jump: return VgsText.JumpGlyph;
                case CardButtonKind.Watch: return VgsText.WatchGlyph;
                case CardButtonKind.BindYes: return VgsText.TrueButtonGlyph;
                case CardButtonKind.BindNo: return VgsText.FalseButtonGlyph;
                default: return VgsText.SingleButtonGlyph;
            }
        }

        /// <summary>
        /// Zoom factor for the card's text (name, subtype, the status line).
        ///
        /// Clamped on both ends and, crucially, <b>below</b> the old floor of 0.75: the card is
        /// now half as tall as it was, and two lines of text at the old floor overflow it once
        /// the user zooms out to <see cref="VgsConstants.MinZoom"/>. Text that is unreadable but
        /// inside its box is a better failure than text that is unreadable and spills over its
        /// neighbours.
        ///
        /// Port labels deliberately do <b>not</b> use this clamp: they are multiplied by the
        /// zoom directly (<see cref="VgsConstants.PortLabelScale"/>), because a port bar shrinks
        /// with the canvas and its label has to shrink with it instead of overflowing a bar that
        /// kept shrinking (ADR-0014).
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
        /// below them, each with its dot, its label and its remove button.
        ///
        /// <paramref name="border"/> is the card's own frame colour, passed in rather than
        /// re-derived: a port belongs to the block, so it must follow the card's state too —
        /// a selected block whose ports stayed grey would break the column into two figures at
        /// exactly the moment the user is looking at it (ADR-0014).
        /// </summary>
        private void DrawPortBars(Node node, Color border, float alpha, Vector2 clipTopLeft,
                                  Vector2 clipSize)
        {
            for (int i = 0; i < node.Events.Count; i++)
                DrawPortBar(node, node.Events[i], border, alpha, clipTopLeft, clipSize);
            for (int i = 0; i < node.Actions.Count; i++)
                DrawPortBar(node, node.Actions[i], border, alpha, clipTopLeft, clipSize);

            // The watch stack follows the input stack in the same column (ADR-0017).
            DrawWatchBars(node, border, alpha, clipTopLeft, clipSize);
        }

        /// <summary>
        /// Draws one port bar: its background, its frame, its label, the connection dot and the
        /// remove button.
        ///
        /// <b>The frame is the card's frame.</b> A port belongs to the block, so it is drawn in
        /// the card's own border colour — including the selected and hovered variants — and only
        /// its background differs. That is what makes the column read as one block with two
        /// backgrounds rather than as a card with three separate boxes hanging off it (ADR-0014).
        /// The dot keeps its own colour, because it is what tells an input from an output at a
        /// glance and it matches the line that leaves it.
        ///
        /// <b>The label is centred on the bar and scales with the zoom.</b> Both controls sit at
        /// the bar's ends, so the label's span is the same for an input and an output; it is
        /// positioned by its measured line height so that its centre line is the bar's centre
        /// line, not its top edge.
        /// </summary>
        private void DrawPortBar(Node node, Port port, Color border, float alpha, Vector2 clipTopLeft,
                                 Vector2 clipSize)
        {
            if (port.Slot < 0) return;

            Vector2 topLeft, size;
            if (!PortGeometry.TryPortBarRect(node, port, out topLeft, out size)) return;

            Vector2 screenTopLeft = GraphToScreen(topLeft, clipTopLeft);
            Vector2 screenSize = size * _zoom;

            bool output = port.Direction == PortDirection.Output;

            // The port follows the card through the search: a bar that stayed bright under a
            // ghost card would be the brightest thing on the canvas and read as a match.
            Color fill = Fade(output ? ColPortOutFill : ColPortInFill, alpha);
            Color accent = output ? ColPortOut : ColPortIn;
            Color accentFaded = Fade(accent, alpha);

            VgsScreenDraw.RectClipped(screenTopLeft, screenSize, clipTopLeft, clipSize, fill);
            VgsScreenDraw.BorderClipped(screenTopLeft, screenSize, clipTopLeft, clipSize, border,
                                        Hairline);

            // The label is clipped to the span between the two controls, so a long action name
            // can neither spill over the next card nor run under a disc.
            Vector2 labelTopLeft, labelSize;
            if (PortGeometry.TryPortLabelRect(node, port, VgsScreenDraw.ClientAspect,
                                              out labelTopLeft, out labelSize))
            {
                Vector2 labelScreen = GraphToScreen(labelTopLeft, clipTopLeft);
                float textHeight = PortLabelHeight;
                if (textHeight <= 0f) textHeight = screenSize.Y * VgsConstants.PortLabelFallbackHeightRatio;

                VgsScreenDraw.Text(port.DisplayName,
                                   new Vector2(labelScreen.X,
                                               PortGeometry.CentredTextTop(labelScreen.Y,
                                                                           screenSize.Y, textHeight)),
                                   VgsConstants.PortLabelScale * _zoom,
                                   Fade(ColText, alpha), labelSize.X * _zoom);
            }

            float radius = VgsConstants.PortControlRadius * _zoom;

            // The dot: where the line attaches, one control-width and one four-pixel gap inside
            // the edge the line uses — right for an output, left for an input. Same shape both
            // ways on purpose; the side it sits on is the property the router relies on.
            Vector2 dotCenter;
            if (PortGeometry.TryPortDot(node, port, VgsScreenDraw.ClientAspect, out dotCenter))
            {
                DrawPortDot(GraphToScreen(dotCenter, clipTopLeft), radius, accentFaded, clipTopLeft,
                            clipSize);
            }

            // The remove button: the dot's mirror image, on the other side of the same bar.
            Vector2 removeCenter;
            if (PortGeometry.TryPortRemoveButton(node, port, VgsScreenDraw.ClientAspect, out removeCenter))
            {
                bool hot = ReferenceEquals(node, _hoverRemoveNode)
                           && ReferenceEquals(port, _hoverRemovePort);

                // Accent, not the bar's fill: the mark is now bare, so its colour has to carry
                // the contrast instead of the white disc that used to sit behind it. The alpha is
                // passed separately rather than pre-applied, because the hot colour is chosen
                // inside — a caller-side fade would leave the hover state at full opacity on a
                // ghost bar.
                DrawPortRemoveButton(GraphToScreen(removeCenter, clipTopLeft), accent, alpha, hot,
                                     clipTopLeft, clipSize);
            }
        }

        /// <summary>
        /// Draws a port's connection dot: the Font Awesome <c>circle-dot</c> icon at the size the
        /// dot has always had, or a filled disc when the icon cannot be drawn (ADR-0019).
        ///
        /// The shape changes on purpose — a ring instead of a disc — but nothing else does: the
        /// diameter, the colour, the position and the hit-test all come from the same constants
        /// they came from before, because the icon *is* the dot rather than a mark on it.
        ///
        /// The disc is a fallback for a missing texture only. It used to also cover small sizes;
        /// that is gone, because a dot that is a ring at one zoom and a disc at the next reads as
        /// a fault (ADR-0019 §6).
        ///
        /// A watch port uses the same call with its own colour. Two different shapes for the same
        /// meaning on one card would be a language nobody reads.
        /// </summary>
        private void DrawPortDot(Vector2 screenCenter, float radius, Color color,
                                 Vector2 clipTopLeft, Vector2 clipSize)
        {
            if (VgsScreenDraw.Icon(VgsIcon.CircleDot, screenCenter, radius * 2f, color,
                                   clipTopLeft, clipSize))
            {
                return;
            }

            VgsScreenDraw.Disc(screenCenter, radius, clipTopLeft, clipSize, color);
        }

        /// <summary>
        /// Draws a port's remove button: the bare <c>xmark</c> icon, in the port's accent colour
        /// (ADR-0019).
        ///
        /// It used to be an inverted white disc with a dark mark on it (ADR-0014), and that was
        /// right while the mark was a font glyph that had no shape of its own. Now that the dot
        /// beside it is a bare ring, the disc is the odd one out: the pair reads as "ring =
        /// connection, cross = removal" only if neither has a filled backdrop. The colour carries
        /// the affordance instead — accent for the mark, the bar's own fill behind it — and hover
        /// brightens the mark, which is the same feedback the dot gets.
        ///
        /// <paramref name="accent"/> is passed <em>unfaded</em>, together with
        /// <paramref name="alpha"/>, because the hover colour is chosen here: fading at the call
        /// site would fade the resting mark and leave the hot one at full brightness, so a ghost
        /// bar's "x" would light up the moment the pointer touched it. The card's buttons solve
        /// the same problem the same way (<see cref="DrawCardButton"/>).
        ///
        /// The hit target is unchanged: it comes from the radius in `PortGeometry`, not from what
        /// is painted.
        /// </summary>
        private void DrawPortRemoveButton(Vector2 screenCenter, Color accent, float alpha, bool hot,
                                          Vector2 clipTopLeft, Vector2 clipSize)
        {
            float radius = VgsConstants.PortControlRadius * _zoom;
            float diameter = radius * 2f;

            Color mark = Fade(hot ? ColPlusFillHot : accent, alpha);

            if (VgsScreenDraw.Icon(VgsIcon.Xmark, screenCenter,
                                   diameter * VgsConstants.PortRemoveIconRatio, mark,
                                   clipTopLeft, clipSize))
            {
                return;
            }

            // Fallback: the font's "x", in the same colour the icon would have used.
            VgsScreenDraw.TextCentered(VgsText.PortRemoveGlyph, screenCenter,
                                       VgsConstants.PortRemoveGlyphScale * _zoom, mark);
        }

        /// <summary>
        /// Height of one line of port-label text at the current zoom, in client-area units.
        ///
        /// Measured once per zoom level rather than per label. The height of a line is a
        /// property of the font and the scale, not of the string, while
        /// <see cref="DrawPortBar"/> runs for every drawn port of every visible card on every
        /// frame — measuring there would call into the font metrics thousands of times a second.
        /// The measurement is what puts the label's *centre* on the bar's centre line;
        /// positioning text by its top edge would leave it sitting low by half a line.
        ///
        /// The cache key is the zoom alone. That is sound because the editor is a modal,
        /// full-screen screen that pauses the game, so the resolution cannot change underneath
        /// it; a font that refused to measure caches a zero, which the caller replaces with
        /// <see cref="VgsConstants.PortLabelFallbackHeightRatio"/> instead of retrying every
        /// frame. If the screen ever stops being modal, the key has to gain the client height.
        /// </summary>
        private float PortLabelHeight
        {
            get
            {
                if (_portLabelHeightZoom == _zoom) return _portLabelHeight;

                _portLabelHeightZoom = _zoom;
                _portLabelHeight =
                    VgsScreenDraw.Measure(LabelProbeText, VgsConstants.PortLabelScale * _zoom).Y;
                return _portLabelHeight;
            }
        }

        /// <summary>
        /// Text measured to find a line's height. Both cases are needed so the measurement
        /// covers the font's full line box: "A" gives the ascender, "g" the descender.
        /// </summary>
        private const string LabelProbeText = "Ag";

        /// <summary>
        /// Draws the action menu: the target's terminal actions, one row each, in the order
        /// the terminal shows them.
        /// </summary>
        private void DrawActionMenu()
        {
            Vector2 topLeft, size;
            GetMenuRect(out topLeft, out size);

            // Panel rather than Rect + Border: the fill is inset under the frame, so it cannot
            // round a row past the menu's bottom edge (ADR-0024 §5).
            VgsScreenDraw.Panel(topLeft, size, ColMenu, ColMenuBorder, Hairline);

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

        /// <summary>
        /// Draws the bottom bar: the modes button, the block search field, and Close (ADR-0023).
        ///
        /// The bar no longer carries the statistics — they moved to the header — and no longer
        /// carries the one-line help text, which is gone. What is left is the chrome the player
        /// actually uses while looking at the schema. The failure message keeps its line above the
        /// controls: it is the one thing that must not be pushed off the bar by anything.
        /// </summary>
        private void DrawBottomBar()
        {
            Vector2 barTopLeft, barSize;
            GetBottomBarRect(out barTopLeft, out barSize);

            VgsScreenDraw.Rect(barTopLeft, barSize, ColBottomBar);
            VgsScreenDraw.Rect(barTopLeft, new Vector2(1f, Hairline), ColGridMajor);

            DrawModesButton();
            DrawSearchField();

            // The hint line has two jobs and one slot: it either explains the gesture under the
            // pointer, or it says why the last click did not produce a connection. The failure
            // wins, because the user is looking at a line that did not appear.
            Vector2 closeTopLeft, closeSize;
            GetCloseButtonRect(out closeTopLeft, out closeSize);
            float hintWidth = closeTopLeft.X - VgsConstants.BarLeftMargin - VgsConstants.CardButtonTextGap;

            string warning = ActiveWarning;
            string hint = warning ?? BottomBarHint;
            Color hintColor = warning != null ? ColWarning : ColTextDim;

            // The hint occupies the strip above the control row, vertically centred in it, so it
            // cannot collide with the controls whatever the font's line height is. Clamped to the
            // bar's own top edge so a very tall line still starts inside the bar.
            float hintHeight = HintLineHeight;
            float stripTop = barTopLeft.Y;
            float stripBottom = BarControlTop;
            float hintTop = stripTop + ((stripBottom - stripTop) - hintHeight) * 0.5f;
            if (hintTop < stripTop) hintTop = stripTop;

            if (hint.Length > 0)
            {
                VgsScreenDraw.Text(hint, new Vector2(VgsConstants.BarLeftMargin, hintTop),
                                   VgsConstants.HintTextScale, hintColor, hintWidth);
            }

            DrawCloseButton();
        }

        /// <summary>
        /// Draws the modes button: a bare <c>sliders</c> icon in the same light colour as the
        /// card's buttons, warming under the pointer, with the glyph "=" when the atlas is
        /// unavailable (ADR-0019 §6, ADR-0023).
        /// </summary>
        private void DrawModesButton()
        {
            Vector2 topLeft, size;
            GetModesButtonRect(out topLeft, out size);

            Color color = _modesHot || _modesMenuOpen ? ColPlusFillHot : ColPlusFill;
            Vector2 center = topLeft + size * 0.5f;

            // The icon is sized by the control's height less a hair of padding, so it never
            // touches the bar's edges at any resolution.
            float ink = size.Y * 0.72f;
            if (VgsScreenDraw.Icon(VgsIcon.Sliders, center, ink, color, Vector2.Zero,
                                   new Vector2(1f, 1f)))
            {
                return;
            }

            VgsScreenDraw.TextCentered(VgsText.ModesGlyph, center, VgsConstants.ModesMenuLabelScale,
                                       color);
        }

        /// <summary>
        /// Draws the search field: a bordered box, the query, the selection wash, the blinking
        /// caret, and the clear button at the right end while there is something to clear
        /// (ADR-0023, ADR-0024).
        ///
        /// <b>Layout before painting.</b> Everything positional — where the caret is, where the
        /// selection edges are, how far the text is scrolled — comes from
        /// <see cref="EnsureFieldLayout"/>, which recomputes only when something moved. Painting
        /// is then pure reads, so the per-frame cost of an idle field is a rectangle, a string and
        /// two comparisons.
        /// </summary>
        private void DrawSearchField()
        {
            Vector2 topLeft, size;
            GetSearchFieldRect(out topLeft, out size);

            bool empty = !SearchHasText;

            // The fill is inset by the frame's thickness: a fill and a frame on the same nominal
            // rectangle are two quads rasterised independently, and at a fractional position the
            // fill could cover a row past the frame — the stray line under the field that this
            // replaces (see VgsScreenDraw.Panel).
            VgsScreenDraw.Panel(topLeft, size, ColNode, _searchHot ? ColNodeHover : ColNodeBorder,
                                Hairline);

            float clearReserve = 0f;
            Vector2 clearTopLeft, clearSize;
            if (!empty && TrySearchClearRect(out clearTopLeft, out clearSize))
            {
                clearReserve = size.X - (clearTopLeft.X - topLeft.X);
            }

            float textAreaWidth = size.X - VgsConstants.SearchFieldPadding * 2f - clearReserve;
            if (textAreaWidth <= 0f) return;

            EnsureFieldLayout(textAreaWidth);

            float textLeft = topLeft.X + VgsConstants.SearchFieldPadding;
            float textRight = textLeft + textAreaWidth;
            float textTop = topLeft.Y + (size.Y - SearchLineHeight) * 0.5f;
            if (textTop < topLeft.Y) textTop = topLeft.Y;

            // ---- selection wash ----
            //
            // Drawn before the text so the glyphs stay readable on top of it. Both edges are
            // clamped into the text area, which is what keeps the wash from painting over the
            // field's frame when the selection is scrolled partly out of view.
            if (_searchEditor.HasSelection)
            {
                float selectionLeft = textLeft + _fieldSelectionStartX - _fieldScrollX;
                float selectionRight = textLeft + _fieldSelectionEndX - _fieldScrollX;

                if (selectionLeft < textLeft) selectionLeft = textLeft;
                if (selectionRight > textRight) selectionRight = textRight;

                if (selectionRight > selectionLeft)
                {
                    Color wash = new Color(ColNodeHover, VgsConstants.SelectionFillOpacity);
                    VgsScreenDraw.Rect(new Vector2(selectionLeft, textTop),
                                       new Vector2(selectionRight - selectionLeft, SearchLineHeight),
                                       wash);
                }
            }

            // ---- text or placeholder ----
            if (empty)
            {
                VgsScreenDraw.Text(VgsText.SearchPlaceholder, new Vector2(textLeft, textTop),
                                   VgsConstants.SearchTextScale, ColTextDim, textAreaWidth);
            }
            else if (_fieldDisplayText != null && _fieldDisplayText.Length > 0)
            {
                // The painted string starts at the first character that is not scrolled off the
                // left edge; its origin is that character's own x, minus the scroll. Both numbers
                // come from the cached layout — measuring here would call the font metrics every
                // frame for a position that only moves when the player types (R010).
                float displayLeft = textLeft + _fieldFirstVisibleX - _fieldScrollX;

                float displayWidth = textRight - displayLeft;
                if (displayWidth > 0f)
                {
                    VgsScreenDraw.Text(_fieldDisplayText, new Vector2(displayLeft, textTop),
                                       VgsConstants.SearchTextScale, ColText, displayWidth);
                }
            }

            // ---- caret ----
            //
            // Drawn last so nothing covers it, and only on the lit half of the blink cycle. The
            // period restarts on every edit (ResetCaretBlink), so a keystroke never lands on a
            // dark half and looks like it did nothing.
            if (CaretVisible)
            {
                float caretX = textLeft + _fieldCaretX - _fieldScrollX;
                if (caretX < textLeft) caretX = textLeft;
                if (caretX > textRight) caretX = textRight;

                VgsScreenDraw.Rect(new Vector2(caretX - CaretThickness * 0.5f, textTop),
                                   new Vector2(CaretThickness, SearchLineHeight), ColText);
            }

            if (empty) return;
            if (!TrySearchClearRect(out clearTopLeft, out clearSize)) return;

            // The mark is drawn from the same rectangle the input pass hit-tests, so the visible
            // "x" and its click target cannot disagree.
            Vector2 clearCenter = clearTopLeft + clearSize * 0.5f;
            float mark = clearSize.Y * VgsConstants.PortRemoveIconRatio;

            if (VgsScreenDraw.Icon(VgsIcon.Xmark, clearCenter, mark,
                                   _searchClearHot ? ColPlusFillHot : ColTextDim,
                                   Vector2.Zero, new Vector2(1f, 1f)))
            {
                return;
            }

            VgsScreenDraw.TextCentered(VgsText.PortRemoveGlyph, clearCenter,
                                       VgsConstants.PortRemoveGlyphScale, ColTextDim);
        }

        /// <summary>
        /// Width of the caret in the layout's units, from its pixel size at the reference height.
        ///
        /// <b>Divided by the client WIDTH, not by the reference height.</b> The two axes of this
        /// layout do not share a scale: one X unit is the full window width in pixels and one Y
        /// unit is the safe-GUI height (ADR-0009). A horizontal size therefore has to convert
        /// through the horizontal scale, or the caret comes out roughly a third too wide on a 16:9
        /// window — which is exactly what dividing by the height did. The same aspect correction is
        /// what `PortGeometry` applies to its horizontal extents.
        ///
        /// Falls back to the vertical reference before the first sync (client width still 0),
        /// which keeps the caret drawable rather than zero-width for one frame.
        /// </summary>
        private static float CaretThickness
        {
            get
            {
                float width = VgsScreenDraw.ClientWidth;
                if (width <= 0f) return VgsConstants.CaretWidth / VgsConstants.ReferenceHeightPixels;
                return VgsConstants.CaretWidth / width;
            }
        }

        /// <summary>
        /// True on the lit half of the blink cycle.
        ///
        /// A caret that is always on is hard to find against a glyph; one that is always off is
        /// not a caret. The half-period comes from <see cref="VgsConstants.CaretBlinkFrames"/>, and
        /// the frame counter is the editor's own — no timer, so this costs one subtraction.
        /// </summary>
        private bool CaretVisible
        {
            get
            {
                int elapsed = _screenFrame - _caretBlinkBaseFrame;
                if (elapsed < 0) elapsed = 0;
                return elapsed % (VgsConstants.CaretBlinkFrames * 2) < VgsConstants.CaretBlinkFrames;
            }
        }

        /// <summary>
        /// Recomputes the caret and selection positions, the scroll, and the slice of the query
        /// that is painted.
        ///
        /// Runs when the text, the caret or the field's width changed — never per frame — because
        /// it calls the font metrics once per character boundary. This is the whole reason the
        /// positions are cached: measuring a caret that has not moved, sixty times a second, is
        /// work whose result is already known.
        /// </summary>
        private void EnsureFieldLayout(float areaWidth)
        {
            if (_fieldLayoutValid && _fieldAreaWidth == areaWidth) return;

            _fieldAreaWidth = areaWidth;

            string text = _searchEditor.Text;
            int caret = _searchEditor.Caret;

            _fieldCaretX = PrefixWidth(caret);

            if (_searchEditor.HasSelection)
            {
                _fieldSelectionStartX = PrefixWidth(_searchEditor.SelectionStart);
                _fieldSelectionEndX = PrefixWidth(_searchEditor.SelectionEnd);
            }
            else
            {
                _fieldSelectionStartX = _fieldCaretX;
                _fieldSelectionEndX = _fieldCaretX;
            }

            // ---- keep the caret inside the visible span ----
            float total = PrefixWidth(text.Length);
            float scroll = _fieldScrollX;

            if (_fieldCaretX - scroll > areaWidth) scroll = _fieldCaretX - areaWidth;
            if (_fieldCaretX - scroll < 0f) scroll = _fieldCaretX;
            if (scroll > total - areaWidth) scroll = total - areaWidth;
            if (scroll < 0f) scroll = 0f;

            _fieldScrollX = scroll;

            // ---- the slice that is painted ----
            //
            // Sliced here rather than in the draw path: the visible tail is the same string on
            // every frame until the text scrolls again, and a Substring per frame would be an
            // allocation per frame for a field nobody is typing into.
            if (scroll <= 0f)
            {
                _fieldDisplayText = text;
                _fieldFirstVisible = 0;
                _fieldFirstVisibleX = 0f;
            }
            else
            {
                int first = 0;
                while (first < text.Length && PrefixWidth(first) < scroll) first++;

                _fieldFirstVisible = first;
                _fieldFirstVisibleX = PrefixWidth(first);
                _fieldDisplayText = first == 0 ? text : text.Substring(first);
            }
        }

        /// <summary>
        /// Width of the first <paramref name="count"/> characters of the query, in client-area
        /// units.
        ///
        /// A guarded call into the font metrics: it is the only way to know where a character
        /// boundary lies, and it is used only when the layout is rebuilt. The whole string needs no
        /// slice, which is the case a caret at the end of the query hits on every keystroke.
        /// </summary>
        private float PrefixWidth(int count)
        {
            if (count <= 0) return 0f;

            string text = _searchEditor.Text;
            if (count >= text.Length)
            {
                return VgsScreenDraw.Measure(text, VgsConstants.SearchTextScale).X;
            }

            return VgsScreenDraw.Measure(text.Substring(0, count), VgsConstants.SearchTextScale).X;
        }

        /// <summary>
        /// Draws the modes popup above its button: one toggle row with a check mark, the way a
        /// menu of view options reads everywhere else (ADR-0023).
        ///
        /// Drawn after the canvas and after the bar, because it overlaps both: it hangs above the
        /// bar's top edge, and anything drawn later would cover it.
        /// </summary>
        private void DrawModesMenu()
        {
            Vector2 menuTopLeft, menuSize;
            GetModesMenuRect(out menuTopLeft, out menuSize);

            // Panel rather than Rect + Border, for the same reason as the action menu: the fill
            // must not be able to round a row past the frame (ADR-0024 §5).
            VgsScreenDraw.Panel(menuTopLeft, menuSize, ColMenu, ColMenuBorder, Hairline);

            Vector2 rowTopLeft, rowSize;
            GetModesRowRect(out rowTopLeft, out rowSize);

            if (_modesRowHot) VgsScreenDraw.Rect(rowTopLeft, rowSize, ColMenuRowHot);

            float checkLeft = rowTopLeft.X + VgsConstants.ModesMenuPadding;
            float checkTop = rowTopLeft.Y + (rowSize.Y - VgsConstants.ModesMenuCheckSize) * 0.5f;

            VgsScreenDraw.Border(new Vector2(checkLeft, checkTop),
                                 new Vector2(VgsConstants.ModesMenuCheckSize,
                                             VgsConstants.ModesMenuCheckSize),
                                 _showHidden ? ColNodeSelected : ColMenuBorder, Hairline);

            if (_showHidden)
            {
                // A filled square inside the frame rather than a tick glyph: the editor's font
                // may be any of several, and a tick that is missing from one of them draws as a
                // box — the same reasoning that made the port's remove mark a Latin "x".
                float inset = VgsConstants.ModesMenuCheckSize * 0.25f;
                VgsScreenDraw.Rect(new Vector2(checkLeft + inset, checkTop + inset),
                                   new Vector2(VgsConstants.ModesMenuCheckSize - inset * 2f,
                                               VgsConstants.ModesMenuCheckSize - inset * 2f),
                                   ColNodeSelected);
            }

            float labelLeft = checkLeft + VgsConstants.ModesMenuCheckSize + VgsConstants.ModesMenuPadding;
            float labelWidth = rowTopLeft.X + rowSize.X - labelLeft - VgsConstants.ModesMenuPadding;
            if (labelWidth <= 0f) return;

            float labelTop = rowTopLeft.Y + (rowSize.Y - ModesMenuLabelHeight) * 0.5f;
            if (labelTop < rowTopLeft.Y) labelTop = rowTopLeft.Y;

            VgsScreenDraw.Text(VgsText.ModesShowHidden, new Vector2(labelLeft, labelTop),
                               VgsConstants.ModesMenuLabelScale, ColText, labelWidth);
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
                if (_screenFrame > _warningUntilFrame) { _warningText = null; return null; }
                return _warningText;
            }
        }

        /// <summary>
        /// The bar's hint line. Bind mode and watch mode replace the idle hint, because while
        /// those modes are on the only thing the user needs to know is how to finish or cancel —
        /// and on a yes/no toolbar, which of the two lists the connection is going into.
        ///
        /// A pointer over the jump button, the modes button or the search field replaces it too
        /// (ADR-0021, ADR-0023): each of those either closes the screen or changes what the whole
        /// canvas shows, and that belongs in front of the player before the click.
        /// </summary>
        private string BottomBarHint
        {
            get
            {
                if (_watchSource != null) return VgsText.WatchHint;
                if (_bindSource == null)
                {
                    if (_hoverButton != null && _hoverButtonKind == CardButtonKind.Jump)
                    {
                        return VgsText.JumpHint;
                    }

                    if (_modesHot) return VgsText.ModesHint;
                    if (_searchHot || _searchClearHot) return VgsText.SearchHint;

                    // No idle hint. The editor's gestures are carried by the controls
                    // themselves — an arrow, a bell, a lightbulb, an "x" — and a permanent
                    // sentence describing them was the first thing every player had to read
                    // past (ADR-0023).
                    return string.Empty;
                }
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

            // Panel rather than Rect + Border: the fill sits under the frame instead of sharing
            // its nominal bottom edge, so it cannot round a row lower than the border (ADR-0024 §5).
            VgsScreenDraw.Panel(topLeft, size, _closeHot ? ColNodeBorder : ColNode, ColNodeSelected,
                                Hairline);

            VgsScreenDraw.TextCentered(VgsText.CloseButton,
                                       topLeft + new Vector2(size.X * 0.5f, size.Y * 0.5f),
                                       0.7f, ColText);
        }

        /// <summary>
        /// Draws the header: "Workbench" and, on the same line, the counts of what is on the
        /// canvas (ADR-0023).
        ///
        /// The statistics used to be the first line of the bottom bar, in Russian. They moved up
        /// here and into English because the header is what reads as a tool's title bar: the name
        /// of the window and what it currently contains, in one line, and the bottom bar left for
        /// the controls the player operates. The grid's name stays right-aligned on the same line,
        /// so the header is one row at every resolution.
        /// </summary>
        private void DrawHeader()
        {
            VgsScreenDraw.Rect(Vector2.Zero, new Vector2(1f, VgsConstants.HeaderHeight), ColBottomBar);

            VgsScreenDraw.Text(VgsText.EditorTitle,
                               new Vector2(VgsConstants.HeaderLeftMargin, VgsConstants.HeaderTitleTop),
                               VgsConstants.HeaderTitleScale, ColText);

            string gridName = GetGridName();
            float gridNameWidth = gridName != null ? GridNameWidth : 0f;
            float gridNameLeft = 1f - VgsConstants.HeaderLeftMargin - gridNameWidth;

            // A name wider than the header would otherwise be drawn starting left of the margin,
            // on top of the title. Clamping it is the honest degradation: the name is clipped by
            // its own left edge rather than painted over "Workbench".
            if (gridNameLeft < VgsConstants.HeaderLeftMargin) gridNameLeft = VgsConstants.HeaderLeftMargin;

            // The counts follow the title on the same baseline. Their X comes from the title's
            // measured width, which is the only way to keep the two from colliding when the font
            // differs: the screen-space API has no text flow, so a second string is placed by hand.
            //
            // They are also clipped to the span left of the grid's name. Without that, a ship with
            // a long name and a dense schema would draw the two strings over each other in the
            // middle of the header — the one place in this window where two pieces of text share a
            // line and neither can move.
            float statsLeft = VgsConstants.HeaderLeftMargin + TitleWidth;
            float statsWidth = gridNameLeft - VgsConstants.CardButtonTextGap - statsLeft;

            if (TitleWidth > 0f && statsWidth > 0f)
            {
                VgsScreenDraw.Text(GetStatusText(),
                                   new Vector2(statsLeft, VgsConstants.HeaderTitleTop),
                                   VgsConstants.HeaderTitleScale, ColTextDim, statsWidth);
            }

            if (gridName == null) return;

            VgsScreenDraw.Text(gridName,
                               new Vector2(gridNameLeft, VgsConstants.HeaderGridNameTop),
                               VgsConstants.HeaderGridNameScale, ColTextDim,
                               1f - VgsConstants.HeaderLeftMargin - gridNameLeft);
        }

        /// <summary>
        /// Width of the header's title, measured once.
        ///
        /// Cached because <see cref="DrawHeader"/> runs every frame and the title is a constant
        /// string at a constant scale: measuring it per frame would call into the font metrics
        /// sixty times a second for a number that cannot change while the editor is open. The
        /// cache is keyed by the client width, which is the only input that can move the answer —
        /// the editor is modal, so a resolution change closes and reopens it.
        /// </summary>
        private float TitleWidth
        {
            get
            {
                float width = VgsScreenDraw.ClientWidth;
                if (_titleWidth > 0f && _titleWidthClientWidth == width) return _titleWidth;

                // The title is a constant string, so a zero here means the font refused to
                // measure (no font resource yet, or a measurement that threw). That is not cached:
                // caching it would hide the header's statistics for the rest of the session over
                // one bad frame, and the next frame costs a single measurement to retry.
                float measured = VgsScreenDraw.Measure(VgsText.EditorTitle, VgsConstants.HeaderTitleScale).X;
                if (measured <= 0f) return 0f;

                _titleWidthClientWidth = width;
                _titleWidth = measured;
                return _titleWidth;
            }
        }

        /// <summary>Cached title width, and the client width it was measured at.</summary>
        private float _titleWidth = -1f;
        private float _titleWidthClientWidth = -1f;

        /// <summary>
        /// Width of the grid's name in the header, measured once per name and client width.
        ///
        /// The header draws the name right-aligned, which means it has to know how wide it is; and
        /// it also needs that width to know how much room is left for the counts. Measuring it
        /// once here serves both and keeps the per-frame cost of a header with a long ship name at
        /// two float comparisons.
        ///
        /// The name is compared by reference: <see cref="ResolveGridName"/> assigns the cache once
        /// in <see cref="OnOpened"/> and nothing else writes it, so a new string there is a new
        /// measurement, and the same string is free.
        /// </summary>
        private float GridNameWidth
        {
            get
            {
                string name = GetGridName();
                if (name == null) return 0f;

                float clientWidth = VgsScreenDraw.ClientWidth;
                if (_gridNameWidth > 0f
                    && ReferenceEquals(_gridNameWidthFor, name)
                    && _gridNameWidthClientWidth == clientWidth)
                {
                    return _gridNameWidth;
                }

                float measured = VgsScreenDraw.Measure(name, VgsConstants.HeaderGridNameScale).X;
                if (measured <= 0f) return 0f;   // not cached: a failed measure must be retried

                _gridNameWidthFor = name;
                _gridNameWidthClientWidth = clientWidth;
                _gridNameWidth = measured;
                return _gridNameWidth;
            }
        }

        /// <summary>Cached grid-name width, the string it was measured for, and the client width.</summary>
        private float _gridNameWidth = -1f;
        private string _gridNameWidthFor;
        private float _gridNameWidthClientWidth = -1f;

        // ------------------------------------------------------------------ cached strings

        /// <summary>
        /// Builds the statistics line only when a value it shows actually changed. Called from
        /// <see cref="OnDraw"/>, i.e. every frame, so an unconditional rebuild here would
        /// allocate a string per frame for the whole time the editor is open.
        /// </summary>
        private string GetStatusText()
        {
            int zoomPercent = (int)(_zoom * 100);
            int nodeCount = _graph.NodeCount;
            int groupCount = _graph.GroupCount;
            int edgeCount = _graph.EdgeCount;

            if (_statusText != null
                && _statusKeyNodes == nodeCount
                && _statusKeyGroups == groupCount
                && _statusKeyEdges == edgeCount
                && _statusKeyZoom == zoomPercent)
            {
                return _statusText;
            }

            _statusKeyNodes = nodeCount;
            _statusKeyGroups = groupCount;
            _statusKeyEdges = edgeCount;
            _statusKeyZoom = zoomPercent;
            _statusText = string.Format(VgsText.StatsFormat, nodeCount, groupCount, edgeCount, zoomPercent);
            return _statusText;
        }

        /// <summary>
        /// Line height of the search field's text, measured once and cached.
        ///
        /// The field centres its text vertically, which needs the height of a line, and
        /// <see cref="DrawSearchField"/> runs every frame. Measured on first use rather than in
        /// the constructor: the font is resolved by <see cref="VgsScreenDraw.EnsureResources"/>,
        /// which may not have run when the screen object is created.
        /// </summary>
        private float SearchLineHeight
        {
            get
            {
                if (_searchLineHeight < 0f)
                {
                    _searchLineHeight =
                        VgsScreenDraw.Measure(LabelProbeText, VgsConstants.SearchTextScale).Y;
                    if (_searchLineHeight <= 0f) _searchLineHeight = 0f;
                }
                return _searchLineHeight;
            }
        }

        /// <summary>
        /// Line height of the modes menu's label, measured once and cached.
        ///
        /// A field of its own, not shared with <see cref="SearchLineHeight"/> or
        /// <see cref="HintLineHeight"/>: the three are drawn at three different font scales, and
        /// one cache served by three scales would return the first one measured to the other two.
        /// </summary>
        private float ModesMenuLabelHeight
        {
            get
            {
                if (_modesLineHeight < 0f)
                {
                    _modesLineHeight =
                        VgsScreenDraw.Measure(LabelProbeText, VgsConstants.ModesMenuLabelScale).Y;
                    if (_modesLineHeight <= 0f) _modesLineHeight = 0f;
                }
                return _modesLineHeight;
            }
        }

        /// <summary>
        /// Line height of the bottom bar's hint line, measured once and cached. Used to centre the
        /// hint in the strip above the bar's controls.
        /// </summary>
        private float HintLineHeight
        {
            get
            {
                if (_hintLineHeight < 0f)
                {
                    _hintLineHeight =
                        VgsScreenDraw.Measure(LabelProbeText, VgsConstants.HintTextScale).Y;
                    if (_hintLineHeight <= 0f) _hintLineHeight = 0f;
                }
                return _hintLineHeight;
            }
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
