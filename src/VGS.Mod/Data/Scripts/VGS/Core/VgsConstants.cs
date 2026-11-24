using System;

namespace Vgs.Core
{
    /// <summary>
    /// Runtime tuning values for the editor.
    ///
    /// Kept as plain constants rather than a config file on purpose: every value here is a
    /// compile-time decision that the roadmap commits to, and a mod that reads settings
    /// from disk needs a settings UI, a migration path and a schema — all of which is
    /// noise until the editor itself exists. When the project needs per-user settings,
    /// replace this class with a serialized <c>VgsSettings</c> loaded in
    /// <c>LoadData()</c>; nothing else should have to change.
    /// </summary>
    public static class VgsConstants
    {
        /// <summary>Prefix for every terminal control/action id, so we never collide with vanilla or other mods.</summary>
        public const string IdPrefix = "VGS_";

        /// <summary>Id of the terminal button that opens the editor.</summary>
        public const string TerminalButtonId = IdPrefix + "GridSetup";

        /// <summary>Id of the "Show in VGS" checkbox on terminal blocks (ADR-0013).</summary>
        public const string ShowInVgsControlId = IdPrefix + "ShowInVgs";

        /// <summary>Stable GUID used for world-storage keys and network channel registration.</summary>
        /// <remarks>
        /// Do not regenerate: it identifies this mod's persisted data across versions.
        /// </remarks>
        public const string ModGuid = "6f1c2a44-3d7e-4a1b-9c05-2b8e7d4f6a13";

        // ---- Persisted per-block state (ADR-0012, ADR-0013) ----
        //
        // Both keys live in the block's own MyModStorageComponent, so the data travels with
        // the block through save/load, blueprints, projection and grid splitting. They are
        // NOT keyed by EntityId, because that id is reassigned by
        // MyObjectBuilder_CubeBlock.Remap (copy, projector, grid split) while our strings
        // are not remapped.
        //
        // BOTH must be listed in Data/EntityComponents.sbc under RegisteredStorageGuids:
        // MyModStorageComponent.Serialize writes only registered keys to the save file and
        // drops the rest with "Not saving ModStorageComponent GUID: ... not claimed".

        /// <summary>
        /// Storage key of a node's canvas position, stored as "x y" in invariant culture.
        /// </summary>
        /// <remarks>Do not regenerate: changing it loses every layout in every existing world.</remarks>
        public static readonly Guid LayoutStorageKey = new Guid("0d3c1b7a-6a51-4c39-9d2e-7f8a1c4b5e60");

        /// <summary>
        /// Storage key of the "Show in VGS" flag: "0" hides the block from the schema, any
        /// other value (including a missing value) shows it.
        /// </summary>
        /// <remarks>Do not regenerate: changing it loses every flag in every existing world.</remarks>
        public static readonly Guid VisibilityStorageKey = new Guid("b1e4a2c8-77d3-4a0f-8c61-2f9e5b0d3a47");

        /// <summary>
        /// Storage key of the event controller's watch list: entity ids of the blocks the
        /// controller observes, space separated (ADR-0017).
        ///
        /// It lives on the controller because the game's own selection list has no public API
        /// — neither to read nor to persist (see docs/01-research/api-event-controller.md).
        /// Like the layout key it travels with the block instead of being keyed by an entity
        /// id, so a copied or split ship keeps its controllers wired.
        /// </summary>
        /// <remarks>Do not regenerate: changing it loses every watch list in every existing world.</remarks>
        public static readonly Guid WatchStorageKey = new Guid("4a7d2e19-8c63-4b0f-a51d-6e9f3c2b8d74");

        /// <summary>
        /// Storage key of the editor's view state — where the camera was, at what zoom, and
        /// whether blocks hidden from the schema were being shown (ADR-0022).
        ///
        /// <b>This one lives on the grid, not on a block.</b> The state describes how the
        /// *construct* is being looked at, so opening the schema from a second cockpit of the
        /// same grid must restore the same view; storing it per block would give every cockpit
        /// its own camera. The value is <c>"x y zoom hidden"</c>, parsed by
        /// <see cref="Vgs.Model.GridViewFormat"/>.
        /// </summary>
        /// <remarks>Do not regenerate: changing it loses every saved view in every existing world.</remarks>
        public static readonly Guid ViewStorageKey = new Guid("7c4b9e21-5f8d-4a63-b0e7-3d19c8a6f240");

        /// <summary>Network message id for future client/server sync. Must be unique per mod.</summary>
        public const ushort NetworkChannelId = 48123;

        // ---- Editor layout, in normalized screen units (0..1 across the client area) ----

        /// <summary>Height of the bottom bar holding the Close button.</summary>
        public const float BottomBarHeight = 0.085f;

        /// <summary>Height of the header strip holding the title and the grid name.</summary>
        public const float HeaderHeight = 0.055f;

        /// <summary>Left (and right) margin of the header's contents.</summary>
        public const float HeaderLeftMargin = 0.02f;

        /// <summary>Font scale of the header's title, "Workbench".</summary>
        public const float HeaderTitleScale = 0.9f;

        /// <summary>
        /// Top of the header's title text, in the layout's normalized units. Kept as a constant
        /// rather than derived from a measured line height: the header is one line at a fixed
        /// scale, and the value is the one the title has always been drawn at.
        /// </summary>
        public const float HeaderTitleTop = 0.014f;

        /// <summary>Font scale of the grid's name at the right end of the header.</summary>
        public const float HeaderGridNameScale = 0.6f;

        /// <summary>Top of the grid's name, slightly lower than the title so the two align optically.</summary>
        public const float HeaderGridNameTop = 0.018f;

        // ---- Close button, in normalized units, anchored to the bottom-right corner ----

        public const float CloseButtonWidth = 0.14f;
        public const float CloseButtonHeight = 0.046f;
        public const float CloseButtonRightMargin = 0.02f;

        /// <summary>Size of one grid cell at zoom 1, in normalized units.</summary>
        public const float BaseCellSize = 0.0125f;

        /// <summary>Minimum and maximum zoom the user may reach.</summary>
        public const float MinZoom = 0.35f;
        public const float MaxZoom = 3.0f;

        /// <summary>Multiplicative zoom step per mouse-wheel notch.</summary>
        public const float ZoomStep = 1.12f;

        // ---- Block card ----
        //
        // The card is a label, not a panel: one line of name, one line of subtype, nothing
        // else. Ports live *outside* it, as bars stacked underneath (see below), which is why
        // the card can be this short and why a block with forty actions still looks small.

        /// <summary>Size of a block card at zoom 1, in normalized units.</summary>
        public const float NodeWidth = 0.16f;
        public const float NodeHeight = 0.036f;

        /// <summary>Text scales on the card. Small on purpose: the card is a label.</summary>
        public const float NodeTitleScale = 0.56f;
        public const float NodeSubtitleScale = 0.44f;

        // ---- Ports ----
        //
        // A port is a rectangle hanging under the card, one per connection group. Both
        // directions are the full width of the card and touch each other with no gap, so a
        // card with ports reads as one column: card, then its port bars, flush.
        //
        // Every bar carries two round controls, one at each end, and they are the same size:
        //
        //   * output bar — dot at the RIGHT end (lines leave from there), "x" at the left;
        //   * input bar  — dot at the LEFT end (lines arrive there), "x" at the right.
        //
        // Both are *inside* the bar, one four-pixel gap clear of its border. An overhanging
        // control looks like a separate element drawn outside the shape it belongs to; inside,
        // the bar's border closes around it and it reads as part of the port.

        /// <summary>
        /// Width of a port bar at zoom 1. Equal to the card's width: a port belongs to the
        /// block, so it is as wide as the block is.
        /// </summary>
        public const float PortBarWidth = NodeWidth;

        /// <summary>Height of a port bar at zoom 1.</summary>
        public const float PortBarHeight = 0.018f;

        /// <summary>
        /// Vertical gap between stacked bars. Zero: the bars are drawn as one flush column,
        /// which is what makes a two-port stack read as a stack rather than as two cards.
        /// </summary>
        public const float PortBarGap = 0f;

        /// <summary>
        /// Reference client-area height in pixels for the constants below that are written in
        /// pixels. The layout is normalized, so "four pixels" means nothing until a reference
        /// resolution is named; 1080 is the same reference the hairline thickness uses.
        /// </summary>
        public const float ReferenceHeightPixels = 1080f;

        /// <summary>
        /// Diameter of a port's two round controls — the connection dot and the remove "x" —
        /// as a fraction of the bar's height. One ratio for both on purpose: the remove button
        /// is the dot's mirror image, and giving them separate constants would let the two
        /// drift apart into two controls of different sizes on one bar (ADR-0014).
        /// </summary>
        public const float PortControlHeightRatio = 0.75f;

        /// <summary>
        /// Radius of a port's round controls, in the vertical units of the layout. The
        /// horizontal extent is derived from the client aspect ratio at draw time so the
        /// control is round on screen — see <c>VgsScreenDraw.Disc</c>,
        /// <c>PortGeometry.TryPortDot</c> and <c>PortGeometry.TryPortRemoveButton</c>.
        /// </summary>
        public const float PortControlRadius = PortBarHeight * PortControlHeightRatio * 0.5f;

        /// <summary>
        /// Gap between a port control and the bar's border, in pixels at
        /// <see cref="ReferenceHeightPixels"/>.
        /// </summary>
        /// <remarks>
        /// A vertical length, because that is the unit the layout's constants are written in.
        /// Where a horizontal gap is needed it is multiplied by the client aspect ratio
        /// (<c>PortGeometry</c> does this), which turns 4/1080 of the height into 4/1920 of the
        /// width — exactly four screen pixels on a 16:9 window. Using the constant as an X
        /// length directly would give seven pixels there, and the gap would change with the
        /// window's shape.
        /// </remarks>
        public const float PortControlMarginPixels = 4f;

        /// <summary>
        /// <see cref="PortControlMarginPixels"/> expressed in the layout's vertical units, the
        /// form the geometry code consumes.
        /// </summary>
        public const float PortControlMargin = PortControlMarginPixels / ReferenceHeightPixels;

        /// <summary>Vertical distance between an input stack and an output stack on one card.</summary>
        public const float PortStackSpacing = 0f;

        // ---- Event controller: watch ports (ADR-0017) ----
        //
        // The button that starts a watch lives in the card's button row like every other card
        // control (ADR-0020); only its icon differs, so nothing about it is sized here.

        /// <summary>
        /// Height of the extra card line that carries an event controller's mode description.
        /// The card grows by this much for a node that has one (ADR-0017).
        /// </summary>
        public const float NodeStatusLineHeight = 0.015f;

        /// <summary>Font scale of the mode description line.</summary>
        public const float NodeStatusScale = 0.40f;

        /// <summary>
        /// How long the refused watch line stays red, in frames — about one second at 60 fps.
        /// The line is the only feedback at the moment of the click, so it has to be long
        /// enough to be seen and short enough not to read as "this is how it looks".
        /// </summary>
        public const int WatchErrorFrames = 60;

        /// <summary>
        /// Dash and gap of a watch line, in layout units at zoom 1. Multiplied by the zoom at
        /// draw time like every other screen length, so the pattern shrinks with the canvas
        /// instead of turning into a solid line at low zoom.
        /// </summary>
        public const float WatchDashLength = 0.010f;

        /// <summary>Gap between two dashes of a watch line, in layout units at zoom 1.</summary>
        public const float WatchDashGap = 0.007f;

        // ---- Block group frame (ADR-0015) ----
        //
        // A group is not a card: it is a light frame drawn *behind* the cards of its members,
        // with the group's name in a strip along its top edge, and its incoming port bars hang
        // under the frame. The frame is derived from the members' positions, so these three
        // numbers are the whole of its look.

        /// <summary>
        /// Gap between the frame's edge and the outermost member card, in layout units.
        ///
        /// Small on purpose: the frame has to read as "these blocks belong together" without
        /// becoming a second, louder shape on a canvas whose cards are already rectangles.
        /// </summary>
        public const float GroupFramePadding = 0.012f;

        /// <summary>
        /// Height of the strip along the frame's top edge that carries the group's name.
        ///
        /// The strip is also the group's click target: a group has no card of its own, and the
        /// frame's interior belongs to its members (a click there must select the member).
        /// </summary>
        public const float GroupTitleHeight = 0.028f;

        /// <summary>Font scale of the group's name inside the title strip.</summary>
        public const float GroupTitleScale = 0.5f;

        /// <summary>
        /// How many block groups one refresh reads from the grid's terminal system. Groups are
        /// cheap (a name and a member set), but the pass runs on a frame and a pathological
        /// ship should not be able to make it unbounded.
        /// </summary>
        public const int MaxGroupsScanned = 64;

        /// <summary>
        /// How many group members one refresh may inspect while computing a group's action
        /// list. Members that are drawn on the canvas reuse their node's port list and cost
        /// nothing; the budget is spent on members that are hidden or live on a subgrid and
        /// have to be read from the world.
        /// </summary>
        public const int MaxGroupMembersScanned = 512;

        /// <summary>Padding between a port label and the control next to it.</summary>
        public const float PortLabelPadding = 0.006f;

        /// <summary>
        /// Font scale of a port's label at zoom 1. Unlike the card's text this is not clamped:
        /// it is multiplied by the zoom directly, so the label shrinks and grows with the bar
        /// it sits in instead of stopping at a floor and overflowing a bar that kept shrinking
        /// (ADR-0014).
        /// </summary>
        public const float PortLabelScale = 0.36f;

        /// <summary>
        /// Font scale of the "x" glyph inside a port's remove button at zoom 1, used only when
        /// the icon atlas is unavailable (ADR-0019). Smaller than the T/F glyph because the disc
        /// it sits in is smaller: 75% of a port bar's height, not 44% of a card's.
        /// </summary>
        public const float PortRemoveGlyphScale = 0.40f;

        // ---- icon atlas (ADR-0019, ADR-0020) ----
        //
        // The editor draws its controls as Font Awesome icons whenever the atlas is loaded, at
        // every zoom: the port dot, the port's "x", and the four card buttons (bind, "yes",
        // "no", watch). There is deliberately no per-icon size floor here: one was implemented
        // and removed after acceptance in the game.
        //
        // The floor existed so that a control would fall back to its old geometry once the icon
        // became too small to read. In the game that read as a bug, not as a safeguard: the port
        // dot was a ring at 300% and a plain disc at 112%, and the bind button was a square with a
        // plus at 300% and a circled "+" at 56%. The shape changing under zoom is more confusing
        // than an icon that is merely small, and a simulation of the real sampler (bilinear, no
        // mips) shows the shapes survive far below any floor worth setting: the ring keeps its
        // hole at 5 px, and the square-plus still shows a plus at 9 px. So the rule is now the
        // simple one — icon if there is a texture, and only the degenerate 1-pixel case is
        // refused by IconPlacement.

        /// <summary>
        /// Height of the "x" icon inside a port's remove button, as a fraction of the button's
        /// diameter.
        ///
        /// The cross's ink is square, so its tips reach the corners of its box; 0.66 leaves a
        /// small rim against the port bar without shrinking the mark more than it has to.
        /// </summary>
        public const float PortRemoveIconRatio = 0.66f;

        /// <summary>
        /// Height of a card button's icon ink, as a fraction of the button's diameter.
        ///
        /// Every card button is a bare icon — there is no disc behind it (ADR-0020) — so this is
        /// the whole control, not a mark on one. 0.92 is as large as the icon can be without the
        /// buttons of one row touching: the pitch is diameter + gap, and the gap is a fifth of a
        /// diameter. It is also still inside the hit target: a square outline's corners reach
        /// 0.92·√2 ≈ 1.30 radii from the centre, and the tolerance is 1.35.
        ///
        /// Large on purpose. Three of the five icons (`bell`, the outlined `lightbulb` and
        /// `share-from-square`) are thin Regular outlines, and at the minimum zoom a button is
        /// about 7,5 px across — an outline that did not fill its button would blur into a smudge
        /// before it became unreadable.
        /// </summary>
        public const float CardButtonIconRatio = 0.92f;

        /// <summary>
        /// Hit tolerance of a card button, as a multiple of its radius.
        ///
        /// One value for all five kinds, because they are one kind of control now: same size,
        /// same row, same shape of target. 1.35 covers the corners of the square bind icon
        /// (1.22 radii) with a margin. Buttons of one row overlap at this tolerance, which is
        /// harmless by construction: <c>PortGeometry.HitCardButton</c> resolves the overlap by
        /// nearest centre, so a click in the gap between two buttons lands on the closer one
        /// rather than on whichever happens to be tested first.
        /// </summary>
        public const float CardButtonHitTolerance = 1.35f;

        /// <summary>
        /// Fallback line height as a fraction of the bar's height, used only when the font
        /// refuses to measure (no font resource, or a measurement that returned zero). Keeps the
        /// label centred on a plausible line instead of pinning it to the bar's top edge.
        /// </summary>
        public const float PortLabelFallbackHeightRatio = 0.62f;

        /// <summary>
        /// Hit tolerance of a port's remove button, as a multiple of its radius. Slightly larger
        /// than the disc: a ~15-pixel circle is a hard target with a mouse, and a near-miss that
        /// does nothing is cheaper than a hit that requires pixel accuracy. Deliberately a
        /// little looser than the bind button's 1.2 — the button is smaller than the card's
        /// disc, so the same multiplier would give a smaller target in absolute terms.
        /// </summary>
        public const float PortControlHitTolerance = 1.25f;

        // ---- Card button row (ADR-0020, ADR-0021) ----
        //
        // Every control that belongs to the *card* — the jump-to-terminal button, the bind
        // button, the yes/no pair and the controller's watch button — sits in one horizontal row
        // along the card's right edge, vertically centred. The row is right-anchored: buttons are
        // packed from the card's right border leftwards, so a card with one button keeps that
        // button exactly where the "+" has always been, and a card with four grows leftwards.
        //
        // Why a row and not a column: a card is much wider than it is tall, so its right edge
        // has room in only one direction. The old yes/no pair stacked two small discs 44% of the
        // card's height apart, which forced them to be half the size of the "+" beside them and
        // made the card's control area read as two different things.

        /// <summary>
        /// Diameter of a card button, as a fraction of the standard card height.
        ///
        /// <b>0.60, down from 0.75 (ADR-0021).</b> The row gained a fifth kind of button — the
        /// jump to the block's terminal — and a controller now carries four of them. The row is
        /// right-anchored and grows leftwards, so the fourth button is paid for by the card's
        /// name: at the old diameter the four-button row filled the card in a square window and
        /// overflowed it in a portrait one. Shrinking the button is the price the product owner
        /// chose over widening the card, which would have changed the layout of every saved
        /// schema.
        ///
        /// The budget, as fractions of the card's width, for the widest row (jump, watch, "yes",
        /// "no") — the row's width grows with the client aspect, because a button is round on
        /// screen: the row takes 33% at 16:9, 44% at 4:3 and 59% in a square window, leaving the
        /// name's span 58%, 46% and 30%. The span, not the "width minus the row", is what these
        /// numbers describe: it also loses the card's inner text padding (a ratio owned by
        /// <c>PortGeometry</c>), and quoting the wider figure would overstate the margin by five
        /// points — which matters in the square window, where the span sits exactly on the 30%
        /// floor the offline check asserts.
        ///
        /// Beyond an aspect of about 1.46 (a window noticeably taller than it is wide) the row
        /// fills the card and the name has nowhere left to go; that is recorded as a limitation,
        /// not as a case the row is designed for.
        /// </summary>
        public const float CardButtonHeightRatio = 0.60f;

        /// <summary>
        /// Diameter of every card button, in the layout's vertical units.
        ///
        /// One value for all five kinds and for every block type. The button belongs to the
        /// *card*, not to a particular toolbar shape, so two cards side by side must not show
        /// two different button sizes — including a controller, whose card is one line taller
        /// because it carries a description, not because its buttons are more important.
        /// </summary>
        public const float CardButtonDiameter = NodeHeight * CardButtonHeightRatio;

        /// <summary>
        /// Radius of a card button. The horizontal half-extent is this times the client aspect,
        /// exactly like every other round control: the layout's X and Y units are different
        /// lengths, so a button with equal extents in both axes draws as an ellipse.
        /// </summary>
        public const float CardButtonRadius = CardButtonDiameter * 0.5f;

        /// <summary>
        /// Gap from the card's right border to the rightmost button's right edge, in pixels at
        /// <see cref="ReferenceHeightPixels"/>. A vertical length like
        /// <see cref="PortControlMarginPixels"/>, multiplied by the client aspect where a
        /// horizontal gap is needed — otherwise the same constant would be four pixels on one
        /// monitor and seven on another.
        /// </summary>
        public const float CardButtonMarginPixels = 4f;

        /// <summary><see cref="CardButtonMarginPixels"/> in the layout's vertical units.</summary>
        public const float CardButtonMargin = CardButtonMarginPixels / ReferenceHeightPixels;

        /// <summary>
        /// Gap between two neighbouring buttons of one row, in pixels at the reference height.
        ///
        /// Tighter than <see cref="CardButtonMarginPixels"/>: the margin separates a button from
        /// the card's border, while this gap separates two buttons that do different things. It is
        /// small on purpose — an icon already carries a transparent margin inside its own texture,
        /// so the clear space a player sees is this gap plus the two icons' margins, not this gap
        /// alone.
        ///
        /// The budget is what set the number. A row of four buttons takes about 33% of the
        /// card's width at 16:9 and 44% at 4:3, leaving the name 58% and 46% — the span the name
        /// actually gets, which is less than "the card minus the row" because the text keeps an
        /// inset of its own on both sides (the same accounting as <see
        /// cref="CardButtonHeightRatio"/>); at six pixels it took 44% and 58%, and the row visibly
        /// drifted towards the card's left edge. The numbers are quoted for the widest row (jump,
        /// watch, "yes", "no") and for the diameter the row has carried since ADR-0021 — a
        /// narrower button makes the same gap look relatively wider, which is why the row did not
        /// have to be tightened again.
        ///
        /// Going below three pixels buys almost nothing: the row shrinks by about one percent of
        /// the card's width, while the gap stops reading as a gap. What is left of the airiness in
        /// a row of <c>bell</c> and <c>lightbulb</c> is not this constant — those icons are tall,
        /// and normalising by ink height makes them narrow (their ink is 73–88% of the button's
        /// width), so two of them side by side show a wider clearance than two square icons would.
        /// Spacing such a row optically means a per-icon pitch, which would make geometry depend on
        /// which icon a button draws; that trade is recorded in ADR-0020 and not taken here.
        /// </summary>
        public const float CardButtonGapPixels = 3f;

        /// <summary><see cref="CardButtonGapPixels"/> in the layout's vertical units.</summary>
        public const float CardButtonGap = CardButtonGapPixels / ReferenceHeightPixels;

        /// <summary>
        /// Gap between the row's leftmost button and the card's text, in pixels at the reference
        /// height. The text is clipped to the span left of it, so a long block name can neither
        /// run under a button nor spill over the card.
        /// </summary>
        public const float CardButtonTextGapPixels = 6f;

        /// <summary><see cref="CardButtonTextGapPixels"/> in the layout's vertical units.</summary>
        public const float CardButtonTextGap = CardButtonTextGapPixels / ReferenceHeightPixels;

        /// <summary>
        /// Font scale of the glyph a card button falls back to when its texture is missing
        /// (ADR-0019). The glyph is the mark the button had before it became an icon — "+",
        /// "T", "F", "#" or "&gt;" — drawn in the button's own colour and without a disc, so the
        /// fallback is the same bare control rather than a different one.
        ///
        /// Follows the button's diameter: it was 0.44 while the button was 0.75 of the standard
        /// card height, and 0.60 of that height is exactly 80% of the old diameter, so the glyph
        /// is scaled by the same 0.8 rather than left to overflow a smaller button.
        /// </summary>
        public const float CardButtonGlyphScale = 0.35f;

        // ---- Connection lines ----

        /// <summary>Thickness of a connection line.</summary>
        public const float LineThickness = 0.0016f;

        /// <summary>
        /// Horizontal clearance between a dot and the vertical run of its route. The dot sits
        /// inside the bar, so the gutter has to clear the bar's edge as well, or the vertical
        /// run would be hidden under the card it belongs to. See <c>EdgeRouting</c>.
        /// </summary>
        public const float LineGutter = 0.014f;

        /// <summary>Hard cap on drawn connections; protects the frame budget on a dense graph.</summary>
        public const int MaxRenderedEdges = 600;

        // ---- Action menu (opened by clicking a target while in bind mode) ----

        public const float ActionMenuWidth = 0.22f;
        public const float ActionMenuRowHeight = 0.023f;
        public const float ActionMenuTitleHeight = 0.026f;

        /// <summary>Rows shown at once. Longer action lists are scrolled with the wheel.</summary>
        public const int ActionMenuMaxRows = 9;

        /// <summary>Distance in normalized units a pointer must travel before it counts as a drag.</summary>
        public const float DragThreshold = 0.0025f;

        /// <summary>Hard cap on blocks drawn at once; protects the frame budget on huge grids.</summary>
        public const int MaxRenderedBlocks = 400;

        /// <summary>
        /// "No entity" — the id a node carries when its block could not be identified. Named
        /// rather than written as a bare <c>0L</c> because the jump refuses it explicitly
        /// (ADR-0021): a card can outlive the block it stands for, and zero is how a missing id
        /// arrives here, not a real id of the world.
        /// </summary>
        public const long NoEntityId = 0L;

        /// <summary>How many grid blocks we pull from the grid in one refresh.</summary>
        public const int MaxGridBlocksScanned = 2000;

        /// <summary>
        /// How many toolbar cells one refresh may read while reconciling the graph with the
        /// blocks' toolbars. A timer has ninety cells and a cockpit three toolbars, so this is
        /// a few dozen blocks' worth of reading per refresh — cheap, but bounded, because the
        /// refresh runs on a frame.
        /// </summary>
        public const int MaxToolbarCellsScanned = 20000;

        /// <summary>How long a failure message stays on the bottom bar, in frames.</summary>
        public const int WarningFrames = 360;

        /// <summary>
        /// How often the view state is written to the grid while the camera or the zoom is being
        /// changed (ADR-0022). Two seconds at 60 fps.
        ///
        /// Panning and zooming change the camera on every frame, so writing on every change would
        /// be sixty string allocations and dictionary writes a second. Writing only on close is
        /// the other extreme: leaving to the main menu or a crash never reaches <c>OnClosed</c>,
        /// which is exactly when the player would notice the view was forgotten. A throttled
        /// write plus one on close covers both.
        /// </summary>
        public const int ViewSaveIntervalFrames = 120;

        /// <summary>
        /// How often the <em>state</em> of the visible cards is re-read from the world: damage
        /// today, charge and fill level later. Half a second at 60 fps.
        ///
        /// Deliberately much faster than the structural refresh
        /// (<c>VgsEditorScreen.RefreshIntervalFrames</c>, ninety frames): a card's damage changes
        /// while the player watches a ship being shot, and a schema that only noticed every
        /// second and a half would be a picture of the past. The pass is cheap enough to run at
        /// this rate because it reads one float per visible block and rebuilds nothing — no
        /// ports, no groups, no toolbars.
        ///
        /// Counted in frames of the screen (<c>VgsEditorScreen._screenFrame</c>), not of the
        /// session: the editor pauses the game, and a session frame counter stops while paused
        /// (ADR-0026).
        /// </summary>
        public const int StatePollIntervalFrames = 30;

        // ---- Bottom bar: modes menu and block search (ADR-0023) ----
        //
        // The bar used to carry a statistics line and a one-line help text. The statistics moved
        // to the header, the help line is gone, and what is left is two controls the player uses
        // while looking at the schema — the modes menu and the block search — plus Close.

        /// <summary>Height of one control of the bottom bar (the modes button, the search field).</summary>
        public const float BarControlHeight = 0.034f;

        /// <summary>Left margin of the bottom bar's controls.</summary>
        public const float BarLeftMargin = 0.02f;

        /// <summary>Width of the square modes button.</summary>
        public const float ModesButtonWidth = 0.05f;

        /// <summary>Gap between two controls of the bottom bar.</summary>
        public const float BarControlGap = 0.008f;

        /// <summary>Width of the block search field.</summary>
        public const float SearchFieldWidth = 0.34f;

        /// <summary>
        /// Horizontal padding inside the search field: before the text, and between the text and
        /// the clear button. One value for both ends, so the text looks centred in the space it
        /// actually has.
        /// </summary>
        public const float SearchFieldPadding = 0.008f;

        /// <summary>Font scale of the search field's text, in the layout's normalized units.</summary>
        public const float SearchTextScale = 0.62f;

        /// <summary>
        /// Font scale of the bottom bar's hint line. Smaller than the search field's text: the
        /// hint is a sentence read once, while the query is a value the player is typing.
        /// </summary>
        public const float HintTextScale = 0.5f;

        /// <summary>
        /// Width of the text caret, in pixels at the reference height. Two, not one: a single
        /// pixel at 1080p disappears against a bright glyph on some panels, and the caret is the
        /// only thing that tells the player the field is ready to receive typing (ADR-0024).
        /// </summary>
        public const float CaretWidth = 2f;

        /// <summary>
        /// Frames between caret blinks — half of the on/off cycle. At 60 fps this is a 500 ms
        /// period, the cadence every text field in the world uses.
        ///
        /// Counted in frames rather than seconds because the editor screen already counts its own
        /// frames in <c>OnUpdate</c>, and a timer added to a draw path would be a second clock.
        /// It is deliberately **not** <c>VgsSession.Frame</c>: that counter only advances from
        /// `UpdateAfterSimulation`, which a paused session skips, and the editor pauses the game —
        /// so a caret driven by it never blinked (ADR-0026).
        /// </summary>
        public const int CaretBlinkFrames = 30;

        /// <summary>
        /// Opacity of the selection highlight behind the query text. A wash rather than a solid
        /// block: the text has to stay readable on top of it.
        /// </summary>
        public const float SelectionFillOpacity = 0.30f;

        /// <summary>
        /// Longest query the search field accepts, in characters. A cap rather than a scrollbar:
        /// the field is one line wide and a query longer than a block name cannot match anything
        /// the field would not already match.
        /// </summary>
        public const int SearchMaxLength = 48;

        /// <summary>
        /// Opacity a card (and its ports, lines and buttons) falls to when the search query
        /// excludes it. Ten percent: still visible as a ghost, so the player keeps the context of
        /// the whole schema, but clearly not a hit.
        /// </summary>
        public const float SearchDimOpacity = 0.10f;

        /// <summary>Width of the modes popup menu.</summary>
        public const float ModesMenuWidth = 0.24f;

        /// <summary>Height of one row of the modes popup menu.</summary>
        public const float ModesMenuRowHeight = 0.028f;

        /// <summary>Vertical padding of the modes popup menu above and below its rows.</summary>
        public const float ModesMenuPadding = 0.006f;

        /// <summary>Gap between the modes button and the popup it opens.</summary>
        public const float ModesMenuGap = 0.006f;

        /// <summary>Side of the checkbox square inside a modes menu row.</summary>
        public const float ModesMenuCheckSize = 0.017f;

        /// <summary>Font scale of a modes menu row's label.</summary>
        public const float ModesMenuLabelScale = 0.6f;
    }

    /// <summary>
    /// All user-facing strings, in one place.
    ///
    /// Space Engineers localizes through <c>MyStringId</c> and a localization dictionary
    /// shipped as content. Until the project ships translations, an explicit table beats
    /// scattering literals: it keeps the Russian and English text adjacent, makes it
    /// obvious which strings still need translating, and lets the QA checklist reference a
    /// single file.
    /// </summary>
    public static class VgsText
    {
        // Terminal (survives in-game, so it is bilingual from day one)
        public const string ButtonTitle = "Grid setup";
        public const string ButtonTooltip = "Open the grid logic editor";

        /// <summary>
        /// The checkbox that removes a block from the schema without touching the ship
        /// (ADR-0013). Wording matters: the flag does not delete anything, it only stops the
        /// editor from drawing the card, so the tooltip says so explicitly.
        /// </summary>
        public const string ShowInVgsTitle = "Show in VGS";
        public const string ShowInVgsTooltip =
            "Draw this block on the grid schema. Turning it off hides the block and its lines; connections keep working";
        public const string ShowInVgsOn = "On";
        public const string ShowInVgsOff = "Off";

        // Editor chrome
        public const string EditorTitle = "Workbench";

        /// <summary>
        /// Header line, drawn right after <see cref="EditorTitle"/>: the window's name and the
        /// counts of what is on the canvas, in one string (ADR-0023).
        ///
        /// It used to be the first line of the bottom bar, in Russian. It moved to the header and
        /// into English because the header is the part of the editor that reads as a tool's title
        /// bar; the "//" is the separator the mock-up used. Format arguments: blocks, groups,
        /// links, zoom in percent.
        /// </summary>
        public const string StatsFormat = " // blocks: {0} · groups: {1} · links: {2} · zoom: {3}%";

        public const string CloseButton = "Закрыть";

        public const string EmptyGrid = "На этом гриде нет блоков для отображения";
        public const string Loading = "Чтение блоков грида…";
        public const string NoGrid = "Не удалось определить грид";
        public const string GridFallbackName = "Grid";

        // ---- Bottom bar: modes menu and block search (ADR-0023) ----
        //
        // Two controls that act on what the schema *shows*, not on the wiring: the modes menu
        // toggles whether blocks hidden from the schema are drawn, and the search field dims
        // everything the query does not match.

        /// <summary>Placeholder of the search field while it is empty. English, like the header.</summary>
        public const string SearchPlaceholder = "Search blocks";

        /// <summary>
        /// Fallback glyph of the modes button, used only when the icon atlas is unavailable
        /// (ADR-0019). The button is drawn as the <c>sliders</c> icon; "=" is the ASCII mark
        /// closest to three sliders and exists in every font the editor may fall back to.
        /// </summary>
        public const string ModesGlyph = "=";

        /// <summary>
        /// The single row of the modes menu. It is a toggle, not a command: the check mark says
        /// whether blocks with "Show in VGS" turned off are drawn as well.
        /// </summary>
        public const string ModesShowHidden = "Show/hide invisible";

        /// <summary>
        /// Hover hint of the modes button, shown in the bottom bar's second line. Same role as
        /// <see cref="JumpHint"/>: an icon alone does not say what pressing it will do, and this
        /// one changes what the whole canvas shows.
        /// </summary>
        public const string ModesHint = "Режимы просмотра: показывать скрытые блоки";

        /// <summary>
        /// Hover hint of the search field. Names the keys that are not discoverable from the field
        /// itself: Enter, the clear button, and the selection/clipboard shortcuts (ADR-0024).
        ///
        /// It says the clipboard is the field's own. The platform gives a mod no access to the
        /// system clipboard (ADR-0027, measured by a failed load), so a player who expects to
        /// paste a block name from Notepad must be told here rather than left to conclude the
        /// feature is broken.
        /// </summary>
        public const string SearchHint =
            "Поиск блоков · Enter — к первому совпадению · «x» — сбросить · "
            + "Ctrl+A / C / X / V — выделить, копировать, вырезать, вставить "
            + "(буфер — только внутри поля)";

        // Bind mode: the bind button turns the pointer into a live line from a source port
        // until the user picks a target or cancels with the right mouse button.
        public const string BindHint = "Режим привязки: нажмите блок-получатель · ПКМ — отмена";

        /// <summary>
        /// Fallback glyph of the single-group bind button (ADR-0019, ADR-0020). The button is
        /// drawn as the <c>square-plus</c> icon; this string is what it falls back to when the
        /// icon textures are unavailable, and it is the same bare mark in the same colour.
        /// </summary>
        public const string SingleButtonGlyph = "+";

        /// <summary>
        /// Fallback glyph of the "true" button of a yes/no toolbar. The button is drawn as a
        /// filled <c>lightbulb</c> icon; "T" is what it falls back to — and what the button said
        /// before it became an icon, so a reader of the old screenshots still recognises it.
        /// </summary>
        public const string TrueButtonGlyph = "T";

        /// <summary>Fallback glyph of the "false" button; see <see cref="TrueButtonGlyph"/>.</summary>
        public const string FalseButtonGlyph = "F";

        /// <summary>
        /// Glyph on a port's remove button, used only when the icon atlas is unavailable
        /// (ADR-0019). The Latin letter, not the multiplication sign: the glyph is drawn by the
        /// game's font, and the plain letter exists in every font the editor may fall back to,
        /// while U+00D7 is not guaranteed in any of them.
        /// </summary>
        public const string PortRemoveGlyph = "x";

        public const string BindHintTrue =
            "Привязка к «да»: нажмите блок-получатель · ПКМ — отмена";

        public const string BindHintFalse =
            "Привязка к «нет»: нажмите блок-получатель · ПКМ — отмена";

        // ---- Jump to the block's terminal (ADR-0021) ----
        //
        // The one card button that is not a gesture on the schema: it closes the editor and
        // opens the game's terminal on the block whose card was clicked. It therefore has no
        // mode and no hint — it does something immediately — and it is the first button of the
        // row, because "go to this block" is what a reader of a card looks for first.

        /// <summary>
        /// Fallback glyph of the jump button, used only when the icon atlas is unavailable
        /// (ADR-0019). The button is drawn as a <c>share-from-square</c> icon — a square with an
        /// arrow leaving it; "&gt;" is the ASCII mark that says the same thing, and it is
        /// guaranteed to exist in every font the editor may fall back to.
        /// </summary>
        public const string JumpGlyph = ">";

        /// <summary>
        /// The card's block no longer exists. The editor works on a snapshot, so the block can be
        /// gone between two refreshes; the jump is refused instead of closing the schema onto a
        /// terminal that would show nothing.
        /// </summary>
        public const string WarnJumpBlockGone = "Переход не выполнен: блок исчез";

        /// <summary>
        /// There is no local character to open a terminal for (spectator, or a session that is
        /// still loading). Refused rather than attempted: the game's terminal screen expects a
        /// character and would fail on a null one.
        /// </summary>
        public const string WarnJumpNoPlayer = "Переход не выполнен: игрок ещё не в мире";

        /// <summary>
        /// The game did not accept the request: the terminal screen is not available (no GUI, or
        /// the interaction was refused). The editor stays open.
        /// </summary>
        public const string WarnJumpFailed = "Переход не выполнен: терминал недоступен";

        /// <summary>
        /// The node resolved to an entity that is not a terminal block. Practically unreachable —
        /// a node is built only from <c>IMyTerminalBlock</c> — but "the block is gone" would be a
        /// lie about a block that exists, so the case gets its own sentence.
        /// </summary>
        public const string WarnJumpNotTerminal = "Переход не выполнен: у блока нет терминала";

        /// <summary>
        /// The jump refused because the block is out of the game's interactive reach and no antenna
        /// on the construct is broadcasting. The game's terminal re-checks the connection every
        /// frame: within reach it wants the player next to the block, beyond it, a remote session
        /// carried by an antenna (ADR-0021). With neither, the terminal would open and then hide
        /// its tabs, so the jump is refused before the screen is disturbed.
        /// </summary>
        public const string WarnJumpNoAntenna = "Переход не выполнен: блок далеко, антенна не передаёт";

        /// <summary>
        /// Hover hint of the jump button (ADR-0021). The jump is the one control on the canvas
        /// that closes the screen it sits on, so it says what it will do before it is pressed —
        /// the icon alone ("a square with an arrow leaving it") is a fair description of the
        /// destination but not of the cost.
        /// </summary>
        public const string JumpHint = "Терминал этого блока · редактор закроется";

        // ---- Event controller watch mode (ADR-0017, ADR-0020) ----
        //
        // The watch button attaches a block to the controller's watch list. It is a third kind
        // of gesture: not a connection (nothing is written into a toolbar) and not a selection
        // (the block stays where it is), so it gets its own icon — a bell — in the card's button
        // row, and its own hint.

        /// <summary>
        /// Fallback glyph of the watch button, used only when the icon atlas is unavailable
        /// (ADR-0019). The button is drawn as a <c>bell</c> icon; "#" is the mark it carried
        /// before it became an icon.
        /// </summary>
        public const string WatchGlyph = "#";

        public const string WatchHint =
            "Режим отслеживания: нажмите блок, который должен отслеживать контроллер · ПКМ — отмена";

        /// <summary>
        /// Mode description on the controller's card: event, comparison sign, threshold.
        /// Built by <c>Vgs.Game.EventControllerProbe</c>, drawn by the card.
        /// </summary>
        public const string WatchStatusThresholdFormat = "{0} · {1} {2}";

        /// <summary>Mode description for an event that has no threshold (only a name).</summary>
        public const string WatchStatusPlainFormat = "{0}";

        /// <summary>
        /// Mode description of a controller with no event selected. Written on the card rather
        /// than left blank: the watch button is still there, and a player who presses it
        /// deserves to know why nothing can be attached.
        /// </summary>
        public const string WatchStatusNoEvent = "Событие не выбрано";

        /// <summary>
        /// Appended when the controller requires *every* watched block to satisfy the condition
        /// (the game's "AND gate"). "OR" is the default and is not written: a line on every
        /// card stating the default would only take space.
        /// </summary>
        public const string WatchStatusAndSuffix = " · И";

        /// <summary>
        /// Comparison sign for "less or equal" (the game's inverted condition).
        ///
        /// ASCII on purpose, like <see cref="PortRemoveGlyph"/>: the editor falls back between
        /// several fonts, and a glyph that is missing from one of them draws as a box. "&lt;="
        /// reads the same and is guaranteed to exist in every font the editor may pick.
        /// </summary>
        public const string WatchSignLower = "<=";

        /// <summary>Comparison sign for "greater" (the default condition). ASCII, see above.</summary>
        public const string WatchSignHigher = ">=";

        /// <summary>Number format of the threshold, invariant culture.</summary>
        public const string WatchThresholdFormat = "0.00";

        // Refusals of a watch attachment. Each reason gets its own sentence for the same
        // reason a toolbar refusal does: they call for different actions from the player.
        public const string WarnWatchSelf =
            "Блок не добавлен: контроллер не может отслеживать сам себя";

        public const string WarnWatchDuplicateFormat =
            "Блок «{0}» уже отслеживается этим контроллером";

        public const string WarnWatchNoEvent =
            "Блок не добавлен: у контроллера не выбрано событие";

        public const string WarnWatchEventIgnoresBlocks =
            "Блок не добавлен: выбранное событие не следит за блоками";

        public const string WarnWatchDifferentConstruct =
            "Блок не добавлен: он на другом конструкте, а контроллер видит только свой";

        public const string WarnWatchNotAcceptedFormat =
            "Блок «{0}» не подходит для события «{1}»";

        public const string WarnWatchClient =
            "Блок не добавлен: список контроллера хранится на сервере";

        public const string WarnWatchRejected =
            "Блок не добавлен: игра не приняла изменение списка контроллера";

        public const string WarnWatchBlockGone =
            "Блок не добавлен: блок исчез";

        /// <summary>
        /// A group cannot be watched: the controller's list holds block entity ids, and a group
        /// is not an entity at all (ADR-0015). Said explicitly, because a click on a group's
        /// title strip would otherwise fall through to "the block is gone", which is untrue.
        /// </summary>
        public const string WarnWatchGroup =
            "Блок не добавлен: контроллер следит за отдельными блоками, а не за группой";

        /// <summary>
        /// The watch stack is capped like every other stack on a card
        /// (<see cref="Vgs.Model.GridGraph.MaxPortsPerSide"/>). The cap is enforced when a block
        /// is attached, not when the bars are drawn: a bar that exists in the list but is not
        /// drawn would have no "x", and the player could never remove it.
        /// </summary>
        public const string WarnWatchTooManyFormat =
            "Блок не добавлен: у контроллера уже {0} отслеживаемых блоков — это предел";

        public const string WarnWatchRemoveRejectedFormat =
            "Блок «{0}» не убран: игра не приняла изменение списка контроллера";

        /// <summary>
        /// A watch the *player* selected in the vanilla terminal. The controller keeps that list
        /// in private fields with no public setter, so the editor can show it and cannot remove
        /// it (ADR-0018). Saying so is the only honest answer; silently pretending would bring
        /// the port back on the next refresh.
        /// </summary>
        public const string WarnWatchGameOwnedFormat =
            "Блок «{0}» выбран в терминале контроллера — уберите его там, редактор этот список не меняет";

        // Action menu
        public const string ActionMenuTitleFormat = "Действие для «{0}»";
        public const string ActionMenuEmpty = "Нет доступных действий";

        /// <summary>
        /// Suffix on a menu row whose action already has sources. Input ports are shared, so
        /// the row must stay usable — this only tells the user that choosing it adds another
        /// line to an input that is already driven.
        /// </summary>
        public const string ActionMenuFanInFormat = "  ·  источников: {0}";

        // Write failures. A connection is an item in the block's toolbar (ADR-0011), and the
        // game refuses some placements; saying which one failed is the whole point of the
        // warning, so each reason has its own sentence.
        public const string WarnNoFreeCellFormat =
            "Связь не добавлена: в тулбаре «{0}» нет свободной ячейки";

        public const string WarnEveryPageFormat =
            "Связь не добавлена: «{0}» уже стоит на всех страницах тулбара «{1}» — игра не разрешает одно действие дважды на одной странице";

        public const string WarnActionGoneFormat =
            "Связь не добавлена: у блока «{0}» больше нет действия «{1}»";

        public const string WarnNotDrivableFormat =
            "Связь не добавлена: блок «{0}» не управляет другими блоками";

        public const string WarnRejected = "Связь не добавлена: игра не приняла действие в тулбар";

        public const string WarnBlockGone = "Связь не добавлена: блок исчез";

        /// <summary>
        /// A group is addressed by name (ADR-0015): if the player renamed or dissolved it
        /// between two refreshes, the connection cannot be written at all, and that is a
        /// different sentence from "the block is gone".
        /// </summary>
        public const string WarnGroupGoneFormat = "Связь не добавлена: группы «{0}» больше нет";

        /// <summary>
        /// Removing a connection goes the other way through the same writer, so it can fail
        /// for the same reason a write can: the machine does not own the block's toolbar (a
        /// remote multiplayer client). The connection is kept in that case, and this says why.
        /// </summary>
        public const string WarnRemoveRefusedFormat =
            "Связь не удалена: игра не разрешила изменить тулбар блока «{0}»";

        // Errors
        public const string ErrOpenScreen = "Не удалось открыть редактор";
        public const string ErrNoAccess = "Нет доступа к этому блоку";
    }
}
