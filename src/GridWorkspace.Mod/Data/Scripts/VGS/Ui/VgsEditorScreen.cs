using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;
using Vgs.Core;
using Vgs.Game;
using Vgs.Model;
using static Vgs.Ui.VgsPalette;

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
    public sealed partial class VgsEditorScreen : MyModScreen
    {

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

        /// <summary>
        /// How the player is looking at the schema: camera, zoom, "show hidden", and the last
        /// values written to the grid (ADR-0022).
        ///
        /// One object instead of three fields plus three saved copies: "has the view changed" is
        /// a comparison of exactly these values, and the rule for when a write is due belongs
        /// with them rather than with the screen.
        /// </summary>
        private readonly ViewState _view;

        /// <summary>
        /// The grid frames of the current graph, recomputed on the structural pass and reused by
        /// the draw pass (ADR-0032).
        ///
        /// One buffer for the lifetime of the screen: the draw path must not allocate (R010), and
        /// a grid frame is a struct, so refilling this list is a memcpy rather than a collection
        /// of new objects.
        /// </summary>
        private readonly List<GridFrame> _gridFrames = new List<GridFrame>(4);

        /// <summary>
        /// Display names of the grids in the construct, keyed by grid entity id, refreshed on the
        /// structural pass (ADR-0032).
        ///
        /// A cache, because a grid's name is a live world read and the draw path is not allowed to
        /// touch the world (ADR-0003). Null until the first refresh; an empty name means the read
        /// failed and the border is drawn without a label.
        /// </summary>
        private Dictionary<long, string> _gridNames;

        /// <summary>
        /// True once the grid-border geometry has been written to the log for this session
        /// (ADR-0032). A diagnostic, so it is one line per session rather than per frame.
        /// </summary>
        private bool _gridGeometryReported;

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

        // ------------------------------------------------------------------ modes and search (ADR-0023)

        /// <summary>True while the modes popup is open.</summary>
        private bool _modesMenuOpen;

        /// <summary>True while the pointer is over the modes button; drives its highlight.</summary>
        private bool _modesHot;

        /// <summary>True while the pointer is over the modes menu's only row.</summary>
        /// <summary>
        /// Which modes-menu row the pointer is over, or -1. An index rather than a bool per row:
        /// the menu is a list of toggles, and one index keeps "which row is hot" and "which row
        /// was clicked" the same value instead of three flags that can all be true at once.
        /// </summary>
        private int _modesRowHot = -1;

        /// <summary>
        /// The search field: the query, the caret and the selection, the scroll that keeps the
        /// caret visible, and the cached positions the draw path reads (ADR-0024).
        ///
        /// One object instead of eight loose fields, because these values only mean something
        /// together and every edit has to invalidate the layout *and* restart the blink. That rule
        /// now lives in <see cref="SearchField.Touch"/>, so a new edit path cannot do half of it.
        /// </summary>
        private readonly SearchField _searchField =
            new SearchField(MeasureSearchText);

        /// <summary>
        /// Measures a query fragment in the field's units. Passed to <see cref="SearchField"/> so
        /// that type holds no game reference and its layout arithmetic is checked offline.
        /// </summary>
        private static float MeasureSearchText(string text)
        {
            return VgsScreenDraw.Measure(text, VgsConstants.SearchTextScale).X;
        }

        /// <summary>
        /// The query split into terms, rebuilt only when the field's text changes.
        /// <see cref="BlockSearch.Matches"/> runs for every drawn card on every frame, so
        /// splitting there would allocate a string per card per frame (R010).
        /// </summary>
        private readonly List<string> _searchTerms = new List<string>(4);

        /// <summary>
        /// The screen's own frame counter, advanced once per <see cref="OnUpdate"/>.
        ///
        /// <b>Not <c>VgsSession.Frame</c>, and this is the whole point.</b> The screen used to
        /// pause the game in single player, and a paused session reaches <c>UpdateComponents</c>
        /// only through <c>UpdateComponentsWhilePaused</c>, which calls <c>UpdateAfterSimulation</c>
        /// on a component only when its <c>UpdateOnPause</c> is set. This mod's session component
        /// never set it, so the session frame counter stood still for as long as the editor was
        /// open — verified by IL, see <c>docs/01-research/api-terminal-search.md</c> §2.5.
        /// Everything on this screen that counts time (caret blink, warning expiry, the refresh
        /// throttle, the view-save throttle) read that frozen counter, so none of them advanced:
        /// the caret never blinked and could start invisible, warnings never expired, and blocks
        /// built with the editor open never appeared.
        ///
        /// <b>The pause is gone (ADR-0029), and this counter stays.</b> With the world running the
        /// session frame advances again, so the original symptom would not recur — but the screen
        /// still must not depend on it: <c>OnUpdate</c> is called by the mod screen host without
        /// any pause gate, so it is the one clock that is correct whether the world is paused or
        /// not. Tying the screen's timers to a clock that can freeze is the defect, not the pause.
        /// </summary>
        private int _screenFrame;

        /// <summary>
        /// The frame of the last state pass (damage and, later, the category readings), so the
        /// cheap pass runs on <see cref="VgsConstants.StatePollIntervalFrames"/> rather than on
        /// every frame.
        ///
        /// Separate from <c>_lastRefreshFrame</c> on purpose: the structural pass and the state
        /// pass have different periods and different costs, and one counter for both would tie
        /// them together — the slow pass would drag the fast one down to its cadence, which is
        /// exactly the behaviour this split exists to avoid.
        /// </summary>
        private int _lastStatePollFrame;

        /// <summary>
        /// Entity ids of the cards currently on the canvas, reused by every state pass.
        ///
        /// Reused rather than rebuilt: the pass runs twice a second for as long as the editor is
        /// open, and a fresh list per pass would be an allocation on a timer. The list is only
        /// read by <c>GridGraphBuilder.RefreshState</c>, which does not keep it.
        /// </summary>
        private readonly List<long> _statePollIds = new List<long>(64);

        /// <summary>
        /// True once the state pass has reported what it found, so the log carries exactly one
        /// line per editor session instead of one per tick.
        ///
        /// The line exists because the damage band is the first thing the mod draws from a value
        /// read out of the game, and "I see no red" has three very different causes that look
        /// identical on screen: the block is not damaged, the state pass is not reaching the
        /// card, or the draw path is not drawing. One line separates them: it names how many of
        /// the visible cards came back damaged, so a report can be answered from the log rather
        /// than by guessing.
        /// </summary>
        private bool _statePollReported;

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
                                bool showHidden, bool showSubgrids, bool showGridBorders)
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
            _view = new ViewState(camera, zoom, showHidden, showSubgrids, showGridBorders);

            // The editor is a full-screen modal: Esc closes it, the pointer is visible.
            CloseOnEscape = true;
            CloseButtonEnabled = false;   // we draw and hit-test our own button
            DrawMouseCursor = true;
            EnabledBackgroundFade = true;

            // <b>The world keeps running while the editor is open.</b> This used to be `true`,
            // which paused the game in single player — and a paused world does not change, so
            // nothing the editor reads could ever move: damage could not be watched growing, a
            // block destroyed by combat never disappeared, a ship never drifted. The editor's
            // whole purpose in the live-schema stage is to show what the blocks are doing *now*,
            // and a pause makes that question unanswerable.
            //
            // It also made the editor behave differently in single player than in multiplayer,
            // where `MyModScreen.Open` never pauses (`PausePush` is gated on
            // `!Sync.MultiplayerActive`): the same screen, two different worlds. Unpausing makes
            // the single-player case match the one that already worked.
            //
            // The cost, accepted deliberately: the ship keeps flying while the editor is open, so
            // a construct under thrust or under fire keeps changing behind the schema. That is
            // the same exposure the player has while looking at the vanilla terminal, and the
            // editor survives it — the structural pass already drops nodes for blocks that are
            // gone and re-reads everything else. See ADR-0029.
            PausesGameInSinglePlayer = false;
        }

        /// <summary>
        /// Opens the editor for a cockpit's grid. Does nothing when a session cannot show a
        /// screen. This is the single entry point the terminal button calls.
        /// </summary>
        public static void OpenFor(IMyCockpit cockpit)
        {
            if (cockpit == null) return;
            if (!VgsSession.HasLocalPlayer) return;

            // The game's language is re-read here, not once per session (ADR-0033): a player who
            // changed it in the options must not have to restart the world to see the editor in
            // it. The screen is built below, so every cached string it takes is in the new table;
            // the terminal controls keep the wording they were registered with, which is the one
            // limitation of doing it this way and is recorded in the ADR.
            GameLanguage.Apply();

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

            // Subgrids are shown and grid borders drawn by default (ADR-0032): a connection from
            // the main grid to a subgrid block works in the game, so a schema that hid its card
            // by default would quietly disagree with the ship.
            bool showSubgrids = true;
            bool showGridBorders = true;

            Vector2 savedCamera;
            float savedZoom;
            bool savedShowHidden;
            bool savedShowSubgrids;
            bool savedShowGridBorders;
            if (GridViewStore.TryRead(grid, out savedCamera, out savedZoom, out savedShowHidden,
                                      out savedShowSubgrids, out savedShowGridBorders))
            {
                camera = savedCamera;
                zoom = savedZoom;
                showHidden = savedShowHidden;
                showSubgrids = savedShowSubgrids;
                showGridBorders = savedShowGridBorders;
            }

            GridGraph graph = GridGraphBuilder.Build(grid, showHidden, showSubgrids);

            // Group frames are derived from the cards of their members, so they are laid out
            // once the graph is complete — before the first frame is drawn, and outside any
            // draw path (ADR-0015).
            PortGeometry.LayoutGroupFrames(graph);

            var screen = new VgsEditorScreen(graph, grid, camera, zoom, showHidden,
                                             showSubgrids, showGridBorders);

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
                _searchField.Clear(_screenFrame);
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
                               ModesMenuRowCount * VgsConstants.ModesMenuRowHeight
                               + VgsConstants.ModesMenuPadding * 2f);

            topLeft = new Vector2(buttonTopLeft.X,
                                  buttonTopLeft.Y - size.Y - VgsConstants.ModesMenuGap);
        }

        /// <summary>
        /// How many toggle rows the modes menu has. A constant rather than a computed count: the
        /// rows are a fixed list of options, and the menu's height, its hit test and its drawing
        /// all have to agree about how many there are.
        /// </summary>
        private const int ModesMenuRowCount = 3;

        /// <summary>Rectangle of one row of the modes menu, by index from the top.</summary>
        private static void GetModesRowRect(int index, out Vector2 topLeft, out Vector2 size)
        {
            Vector2 menuTopLeft, menuSize;
            GetModesMenuRect(out menuTopLeft, out menuSize);

            size = new Vector2(menuSize.X, VgsConstants.ModesMenuRowHeight);
            topLeft = new Vector2(menuTopLeft.X,
                                  menuTopLeft.Y + VgsConstants.ModesMenuPadding
                                  + index * VgsConstants.ModesMenuRowHeight);
        }

        /// <summary>
        /// Which row the pointer is over, or -1. One hit test for all rows, so a click and the
        /// highlight it produces cannot disagree about which row it was.
        /// </summary>
        private static int HitModesRow(Vector2 mouse)
        {
            for (int i = 0; i < ModesMenuRowCount; i++)
            {
                Vector2 topLeft, size;
                GetModesRowRect(i, out topLeft, out size);
                if (PortGeometry.Contains(topLeft, size, mouse)) return i;
            }
            return -1;
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

                // The state pass, on its own much faster timer. It runs after the structural
                // pass on purpose: a structural refresh may have just created nodes, and their
                // state would otherwise stay at its default for another half second.
                if (_screenFrame - _lastStatePollFrame >= VgsConstants.StatePollIntervalFrames)
                {
                    _lastStatePollFrame = _screenFrame;
                    RefreshNodeState();
                }

                // The camera and the zoom change on pointer events, not on a timer, so the save
                // cannot live in the input pass: panning would write on every frame. It runs here
                // instead, throttled, and writes only when the value really changed (ADR-0022).
                SaveViewIfChanged();

                // The query's terms are rebuilt here, not at the edit sites and never in the draw
                // path: the field marks itself changed, and this is the one place that pays for
                // splitting (one string per term). A keystroke therefore reaches the filter at the
                // latest on the next frame — and `JumpToFirstMatch` syncs first, so Enter never
                // jumps using the previous query.
                SyncSearchTerms();
            }
            catch (Exception e)
            {
                VgsLog.Throttled("screenupdate", "editor update failed", e, 300);
            }
        }

        /// <summary>
        /// Reads the display name of every grid that has cards on the canvas (ADR-0032).
        ///
        /// Runs on the structural pass, never from the draw path: a grid's name is a live world
        /// read. A grid whose name cannot be read keeps an empty entry rather than being dropped,
        /// so the draw path does not have to distinguish "not read yet" from "has no name" — both
        /// mean "draw the border without a label".
        /// </summary>
        private void RefreshGridNames()
        {
            if (_gridNames == null) _gridNames = new Dictionary<long, string>(4);

            for (int i = 0; i < _gridFrames.Count; i++)
            {
                long gridId = _gridFrames[i].GridEntityId;
                if (_gridNames.ContainsKey(gridId)) continue;

                _gridNames[gridId] = ReadGridName(gridId);
            }

            // Drop entries for grids that no longer have cards: a construct can lose a subgrid,
            // and a dictionary that only grows is a leak that outlives the picture.
            if (_gridNames.Count > _gridFrames.Count)
            {
                var stale = new List<long>();
                foreach (KeyValuePair<long, string> pair in _gridNames)
                {
                    bool found = false;
                    for (int i = 0; i < _gridFrames.Count; i++)
                    {
                        if (_gridFrames[i].GridEntityId != pair.Key) continue;
                        found = true;
                        break;
                    }
                    if (!found) stale.Add(pair.Key);
                }
                for (int i = 0; i < stale.Count; i++) _gridNames.Remove(stale[i]);
            }
        }

        /// <summary>
        /// Display name of one grid, or empty when it cannot be read. The name is the grid's
        /// <c>DisplayName</c>, which the game keeps in sync with the terminal's ship name.
        /// </summary>
        private string ReadGridName(long gridEntityId)
        {
            try
            {
                if (MyAPIGateway.Entities == null) return string.Empty;

                VRage.ModAPI.IMyEntity entity;
                if (!MyAPIGateway.Entities.TryGetEntityById(gridEntityId, out entity)) return string.Empty;

                IMyCubeGrid cubeGrid = entity as IMyCubeGrid;
                if (cubeGrid == null) return string.Empty;

                string name = cubeGrid.DisplayName;
                return name ?? string.Empty;
            }
            catch (Exception e)
            {
                VgsLog.Throttled("grid-name", "could not read a grid's name", e, 600);
                return string.Empty;
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

                GridGraphBuilder.Refresh(_graph, _grid, _view.ShowHidden, _view.ShowSubgrids);

                // The refresh moves cards (LayoutUnplaced) and rebuilds the groups, so the
                // frames have to be recomputed before the next draw reads them.
                PortGeometry.LayoutGroupFrames(_graph);

                // Grid frames and their names follow the same rule: computed here, on the pass
                // that is allowed to touch the world, so the draw path only reads (ADR-0003).
                GridFrames.Layout(_graph, _gridFrames);
                RefreshGridNames();

                ValidateInteractionState();

                // Nodes the structural pass just created start at the default state, so their
                // damage is read now rather than on the next tick of the state timer. A card
                // that showed "undamaged" for half a second after appearing would be wrong for
                // exactly the blocks the player just placed.
                RefreshNodeState();
                _lastStatePollFrame = _screenFrame;
            }
            catch (Exception e)
            {
                VgsLog.Error("editor refresh failed", e);
            }
        }

        /// <summary>
        /// Reads the state of the cards on screen — damage today, the category's own readings
        /// later — and writes it into the graph.
        ///
        /// <b>The cheap half of the two-pass refresh.</b> It asks
        /// <see cref="GridGraphBuilder.RefreshState"/> for one value per <em>visible</em> block
        /// and rebuilds nothing: no ports, no groups, no toolbars, no edge reconciliation. The
        /// structural pass (<see cref="RefreshFromGrid"/>) does all of that, on a much slower
        /// timer, because none of it changes between two frames of combat.
        ///
        /// <b>Only what is on screen.</b> The ids come from the same culling arithmetic the draw
        /// path uses (<see cref="IsCardOnCanvas"/>), so the pass costs what the player can see
        /// rather than what the ship contains. On a large grid with the camera zoomed in that is
        /// a handful of blocks instead of hundreds.
        ///
        /// Guarded like every other entry from the game: this runs from <see cref="OnUpdate"/>,
        /// and an exception escaping it would reach the game loop.
        /// </summary>
        private void RefreshNodeState()
        {
            if (_grid == null || _graph == null) return;

            try
            {
                CollectVisibleNodeIds(_statePollIds);
                if (_statePollIds.Count == 0) return;

                int written = GridGraphBuilder.RefreshState(_graph, _statePollIds);
                ReportStatePassOnce(written);
            }
            catch (Exception e)
            {
                VgsLog.Throttled("screenstate", "could not refresh block state", e, 300);
            }
        }

        /// <summary>
        /// Writes one line about the first state pass of the session: how many visible cards were
        /// read, and how many of them came back damaged.
        ///
        /// Once per session, not per pass: the pass runs twice a second, and a line each time
        /// would be the noise the log is supposed to be free of. It is the answer to "the band
        /// does not appear", which has three indistinguishable causes — an undamaged block, a
        /// pass that never reached the card, and a draw path that never ran. The counts separate
        /// them: zero reads means the pass is not running, reads with zero damaged means the
        /// values are coming back clean, reads with damage but no band means the drawing.
        ///
        /// <b>Cheap and allocation-free in the steady state.</b> It returns immediately on every
        /// frame after the first, and only the one line it does write allocates a string.
        /// </summary>
        private void ReportStatePassOnce(int written)
        {
            if (_statePollReported) return;
            _statePollReported = true;

            int damaged = 0;
            for (int i = 0; i < _statePollIds.Count; i++)
            {
                Node node = _graph.GetNode(_statePollIds[i]);
                if (node != null && !node.IsGroup && node.State.IsDamaged) damaged++;
            }

            VgsLog.Info("state pass: " + written + " of " + _statePollIds.Count
                        + " visible card(s) read, " + damaged + " damaged");
        }

        /// <summary>
        /// Fills <paramref name="into"/> with the entity ids of the cards the draw path would
        /// draw right now, in draw order and under the same cap.
        ///
        /// Same predicate, same cap, same order as <see cref="DrawNodes"/> — deliberately, and
        /// not by accident: "visible" has to mean one thing, or the state pass would poll cards
        /// that are not drawn and miss cards that are. The list is cleared and reused, so this
        /// allocates nothing.
        /// </summary>
        private void CollectVisibleNodeIds(List<long> into)
        {
            into.Clear();
            if (_graph == null) return;

            Vector2 canvasTopLeft, canvasSize;
            GetCanvasRect(out canvasTopLeft, out canvasSize);

            IList<Node> nodes = _graph.Nodes;
            for (int i = 0; i < nodes.Count && into.Count < VgsConstants.MaxRenderedBlocks; i++)
            {
                if (!IsCardOnCanvas(nodes[i], canvasTopLeft, canvasSize)) continue;
                into.Add(nodes[i].EntityId);
            }
        }

        /// <summary>
        /// True when the node's card overlaps the canvas, i.e. when it is worth drawing and worth
        /// polling for state.
        ///
        /// The rectangle is the card plus its port stack (<see cref="PortGeometry.TotalHeight"/>),
        /// because the bars hang below the card and a card scrolled just off the top edge still
        /// has its ports on screen. Not named <c>Draw…</c> on purpose: it is asked from the update
        /// pass as well, and the review gate's allocation rule is about methods that draw.
        /// </summary>
        private bool IsCardOnCanvas(Node node, Vector2 canvasTopLeft, Vector2 canvasSize)
        {
            Vector2 screen = GraphToScreen(node.Position, canvasTopLeft);
            Vector2 card = new Vector2(VgsConstants.NodeWidth, PortGeometry.TotalHeight(node)) * _view.Zoom;

            if (screen.X > canvasSize.X || screen.Y > canvasSize.Y) return false;
            if (screen.X + card.X < 0f || screen.Y + card.Y < 0f) return false;
            return true;
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

            _graph = GridGraphBuilder.Build(_grid, _view.ShowHidden, _view.ShowSubgrids);
            PortGeometry.LayoutGroupFrames(_graph);
            GridFrames.Layout(_graph, _gridFrames);

            // The rebuild can bring in grids the previous graph never had (a subgrid was just
            // attached, or "show subgrids" was turned back on), so the name cache is rebuilt from
            // scratch rather than added to.
            _gridNames = null;
            RefreshGridNames();

            VgsLog.Info("schema rebuilt: " + _graph.NodeCount + " blocks, invisible "
                        + (_view.ShowHidden ? "shown" : "hidden"));
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
        /// <see cref="DrawNode"/> reads the field — for as long as the state lasted. The two
        /// search-field highlights go with them: they are the same fact on the bottom bar.
        /// </summary>
        private void ClearHoverHighlights()
        {
            ClearHoverTargets();
            _searchField.Hot = false;
            _searchField.ClearHot = false;
        }

        /// <summary>
        /// Drops the pointer's hover targets: the card, its button row, the port remove button and
        /// the watch remove button.
        ///
        /// Six fields, because they are one fact — what the pointer is over — and every place
        /// that recomputes it or leaves a mode has to forget it first. Keeping them in one method
        /// is what stops the next mode from clearing four of the six and leaving a highlight on a
        /// control it does not offer.
        /// </summary>
        private void ClearHoverTargets()
        {
            _hoverNode = null;
            _hoverButton = null;
            _hoverRemoveNode = null;
            _hoverRemovePort = null;
            _hoverWatchRemoveNode = null;
            _hoverWatchRemoveTarget = null;
        }

        /// <summary>
        /// Drops the whole pointer overlay: the hover targets and the "the middle button is
        /// holding a pan" flag.
        ///
        /// Called when a gesture ends, so the next frame starts from nothing rather than from a
        /// highlight or a drag that is no longer happening. A gesture's own state — the bind
        /// source and port, the watch source — is deliberately not touched: leaving the mode is
        /// the caller's business, and a shared reset that quietly dropped it would make
        /// <see cref="CancelBind"/> and <see cref="CancelWatch"/> mean the same thing.
        /// </summary>
        private void ResetPointerOverlay()
        {
            ClearHoverTargets();
            _panning = false;
        }

    }
}
