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
        /// Storage key of the event controller's watch list: the id of the event the entries were
        /// added for, then the entity ids of the blocks the controller observes, space separated
        /// (ADR-0017, ADR-0044).
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

        /// <summary>
        /// Thickness of one line of the field's grid, in device pixels — one pixel at every zoom,
        /// the same rule as <see cref="GridBorderPixels"/> (ADR-0032 §3).
        ///
        /// <b>Not a layout-unit size, unlike <see cref="BaseCellSize"/>.</b> The cell size is a
        /// distance on the field and scales with the zoom; a line's thickness is not a distance on
        /// the field at all. Expressed in layout units it came out one pixel wide vertically and
        /// <c>height/width</c> wide horizontally (ADR-0009), and the every-fifth line, being
        /// lighter, read as a second, thicker kind of line. The two kinds now differ in colour
        /// only.
        /// </summary>
        public const float GridLinePixels = 1f;

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
        /// A dot of a watch line, in <b>device pixels</b> — two, at every zoom (ADR-0046 §3).
        ///
        /// A watch line is dotted rather than dashed: at the weight a connection now has, a dash
        /// long enough to read as a dash reads as a solid piece of wire, and the one thing this
        /// line must never be mistaken for is a connection. A dot is ink and nothing else, so the
        /// line reads as a rhythm of points even where it is dense.
        /// </summary>
        public const float WatchDotPixels = 2f;

        /// <summary>
        /// The clear space between two dots of a watch line, in device pixels (ADR-0046 §3).
        ///
        /// Equal to <see cref="WatchDotPixels"/>, so the period is four pixels: at 1080p that is
        /// a dotted line the eye reads as a line, not as a row of specks.
        /// </summary>
        public const float WatchDotGapPixels = 2f;

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
        /// Padding of a grid frame around the cards of that grid (ADR-0032).
        ///
        /// <b>Deliberately larger than <see cref="GroupFramePadding"/> plus
        /// <see cref="GroupTitleHeight"/>.</b> A grid frame encloses cards, and a group frame
        /// encloses cards too but extends beyond them by its own padding and title strip. If the
        /// grid padding were the same, the grid frame would cut through every group frame on that
        /// grid, and two overlapping frames that cross each other read as a drawing bug rather
        /// than as two levels of nesting. The extra margin is what makes "a grid contains groups
        /// contains cards" visible as nesting.
        /// </summary>
        public const float GridFramePadding = 0.048f;

        /// <summary>
        /// Font scale of a grid frame's name, drawn in its tab (ADR-0032).
        ///
        /// Smaller than <see cref="GroupTitleScale"/>: the grid name is an orienting label, not a
        /// title of an object the player acts on, and it must not compete with the group names
        /// inside the same frame.
        /// </summary>
        public const float GridTitleScale = 0.45f;

        /// <summary>
        /// Thickness of a grid border, in device pixels — deliberately not in layout units
        /// (ADR-0032).
        ///
        /// <b>One pixel at every zoom, and that is the requirement.</b> A border that grew with
        /// the zoom would compete with the cards it encloses; at 2x it would be twice as thick as
        /// a card's own border and read as a wall rather than a boundary. Because layout units are
        /// not square (ADR-0009), this number is converted to two different layout thicknesses,
        /// one per axis — see <c>GridFrames.BorderThickness</c>.
        /// </summary>
        public const float GridBorderPixels = 1f;

        /// <summary>Horizontal padding inside a grid's name tab (ADR-0032).</summary>
        public const float GridTabPaddingX = 0.008f;

        /// <summary>Vertical padding inside a grid's name tab (ADR-0032).</summary>
        public const float GridTabPaddingY = 0.004f;

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

        // ---- Card parts: the state column (ADR-0035) ----
        //
        // A card has three parts, and each has a name because "left" and "right" say nothing about
        // what is there (docs/06-terminology.md §4, «Три части карточки»):
        //
        //   * the STATE COLUMN (колонка состояния) — the strip on the card's leading edge, holding
        //     controls and readings about the block itself. Everything in this section is its.
        //   * the NAMEPLATE (шильдик) — the middle: name, subtype, mode description.
        //   * the BUTTON ROW (ряд кнопок) — the gestures along the trailing edge (ADR-0020).
        //
        // The column is split into an upper band — the on/off switch — and a lower one, which is
        // reserved and empty today. Both bands live inside the column's *inner* box: the card's
        // height less the vertical padding above and below (PowerSwitchPadding), so neither icon
        // touches a card edge.
        //
        // The column's width is not a constant. It follows from the icon: the switch fills the
        // band by its *height*, and the icon is wider than it is tall (576 × 384 ink, an aspect of
        // 1.5, measured by the atlas build), so the width comes out as 1.5 × the band.

        /// <summary>
        /// Height of the upper band of the card's <b>state column</b> — the power switch — as a
        /// fraction of the column's <b>inner</b> height.
        ///
        /// The switch's ink fills this band exactly, and the rest of the inner box belongs to the
        /// lower band, which is reserved for a second control and is deliberately empty.
        ///
        /// <b>Of the inner height, not of <see cref="NodeHeight"/>.</b> The inner height is the
        /// card's own height less <see cref="PowerSwitchPadding"/> above and below, so an event
        /// controller's taller card is split the same way a standard one is — the same rule the
        /// damage band follows (ADR-0028 §1), and for the same reason: a band measured from the
        /// standard height would leave the tallest cards with a switch that no longer looks like a
        /// fraction of the column.
        /// </summary>
        public const float PowerSwitchBandRatio = 0.40f;

        /// <summary>
        /// Vertical padding of the card's <b>state column</b>, in pixels at
        /// <see cref="ReferenceHeightPixels"/>: the gap between the card's top edge and the switch,
        /// and between the card's bottom edge and whatever the lower band will hold.
        ///
        /// <b>Why it exists.</b> The column already kept a horizontal margin from the leading edge
        /// (<see cref="CardButtonMarginPixels"/>), but nothing from the top or bottom, so the
        /// switch's ink sat *on* the card's top border. A control that touches the frame it belongs
        /// to reads as part of the frame rather than as a control on the card — the same complaint
        /// the button row would have if its icons were flush with the card's right edge.
        ///
        /// <b>Why both ends, when only the switch is drawn today.</b> The lower band is reserved for
        /// a second control; padding it now means that control lands with the same gap without a
        /// second decision, and it means the inner box is one rectangle rather than two rules.
        ///
        /// <b>Why the bands are a fraction of the inner height and not of the card.</b> "Available
        /// height" is what the requirement asks to shrink by the two paddings; splitting the card's
        /// height and then insetting would leave the lower band 4 px shorter than its share, which
        /// is exactly the kind of half-applied rule that later reads as a bug.
        ///
        /// <b>The cost, stated.</b> The switch is 1.5 × its band wide, so shrinking the band by the
        /// padding shrinks the switch with it: about 10% at 1080p, which makes it slightly narrower
        /// than a row button's diameter (23.3 px) — the figure ADR-0035 originally matched. That is
        /// a consequence of the gap, not a regression; raising <see cref="PowerSwitchBandRatio"/>
        /// is what would buy the size back, at the price of the 40/60 split the requirement names.
        /// </summary>
        public const float PowerSwitchPaddingPixels = 2f;

        /// <summary><see cref="PowerSwitchPaddingPixels"/> in the layout's vertical units.</summary>
        public const float PowerSwitchPadding = PowerSwitchPaddingPixels / ReferenceHeightPixels;

        /// <summary>
        /// Hit padding of the card's power switch, in pixels at
        /// <see cref="ReferenceHeightPixels"/>.
        ///
        /// A vertical length like <see cref="CardButtonMarginPixels"/>, multiplied by the client
        /// aspect where a horizontal gap is needed. The switch is a wide, short target, so it is
        /// easier to hit than a disc already; the padding exists so that a click on the card's very
        /// edge — where the switch's ink stops — still counts as a click on the control rather than
        /// starting a drag.
        /// </summary>
        public const float PowerSwitchHitPaddingPixels = 4f;

        /// <summary><see cref="PowerSwitchHitPaddingPixels"/> in the layout's vertical units.</summary>
        public const float PowerSwitchHitPadding = PowerSwitchHitPaddingPixels / ReferenceHeightPixels;

        // ---- The state widget (ADR-0042) ----
        //
        // The widget is what the LOWER band of the state column shows: a reading of the block —
        // how full a container is, how charged a battery is, how far a timer has counted. It is
        // geometry rather than an icon, so its numbers are the whole of its look:
        //
        //   * the widget AREA is the lower band of the column (the card's height less the column's
        //     padding, times 1 − PowerSwitchBandRatio) LESS WidgetTopGapPixels, so the switch and the
        //     widget do not touch. The column's width is 0.6 × the inner height and the band's height
        //     was 0.6 × the same, so the area used to be a square; it is now one gap wider than it is
        //     tall, and the shapes are inscribed in it rather than stretched (ADR-0042 §3a);
        //   * inside the area sits the widget's FRAME — one rectangle per bar, one circle for the
        //     clock — inset by the padding below, which differs per shape because a bar and a face
        //     want different margins;
        //   * inside a bar's frame sits the FILL, inset by WidgetInsetPixels, and its height is the
        //     reading: it grows from the bottom edge upwards.
        //
        // The paddings are shares of the area, *both sides together*: 0.15 means the frame loses 15%
        // of the area's width, 7.5% at each side. They are named as totals because that is how the
        // requirement stated them.

        /// <summary>
        /// Share of the widget area's width that a cargo container's bar gives up as padding, both
        /// sides together (ADR-0042 §3). The value the requirement named for the plainest widget.
        ///
        /// At 1080p the area is about 21 × 21 px, so the bar's frame comes out 17.8 px wide — wide
        /// enough for the fill inside it to read as a column rather than as a line.
        /// </summary>
        public const float WidgetPaddingFillWidth = 0.15f;

        /// <summary>
        /// Share of the widget area's height that a cargo container's bar gives up as padding, both
        /// sides together (ADR-0042 §3). The requirement's second number, and the reason the bar is
        /// taller than it is wide: a vertical bar needs the height more than it needs the width.
        /// </summary>
        public const float WidgetPaddingFillHeight = 0.05f;

        /// <summary>
        /// Share of the area's width given up by every widget that is not the plain fill bar —
        /// battery, refinery, assembler (ADR-0042 §3).
        ///
        /// Smaller than the cargo container's 15% because these shapes are already split: a battery
        /// reserves a column for its bolt, and a production block draws two bars side by side.
        /// Padding them by 15% would leave each of two bars about three pixels of fill.
        /// </summary>
        public const float WidgetPaddingWidth = 0.04f;

        /// <summary>Share of the area's height given up by the non-plain widgets, both sides together.</summary>
        public const float WidgetPaddingHeight = 0.04f;

        /// <summary>
        /// Share of the area's width and height given up by the clock, both sides together.
        ///
        /// The smallest of the three, because a clock is a circle and a circle inscribed in a square
        /// already leaves its corners empty: the same padding that suits a bar would shrink the
        /// hands' reach by a fifth, and the hands are the whole reading.
        /// </summary>
        public const float WidgetPaddingClock = 0.03f;

        /// <summary>
        /// Distance from a bar's frame to its fill, in pixels at
        /// <see cref="ReferenceHeightPixels"/>: the "two conditional pixels" the requirement asks
        /// for between the outer rectangle and the inner one.
        ///
        /// <b>It is the distance between the two rectangles, not the clear space beside the
        /// border.</b> The frame is drawn as a one-pixel border on its outer edge, so the visible
        /// gap is this value less that pixel — which is what "the distance between the outer and the
        /// inner rectangle" means, and what keeps the number checkable: the fill's edges are exactly
        /// two pixels inside the frame's edges.
        /// </summary>
        public const float WidgetInsetPixels = 2f;

        /// <summary><see cref="WidgetInsetPixels"/> in the layout's vertical units.</summary>
        public const float WidgetInset = WidgetInsetPixels / ReferenceHeightPixels;

        /// <summary>
        /// Clear space between the power switch and the state widget, in pixels at
        /// <see cref="ReferenceHeightPixels"/> (ADR-0042 §3a).
        ///
        /// <b>The switch used to lie on the widget.</b> The two bands met exactly at 40/60 of the
        /// column's inner height, and a wide icon whose ink fills its band therefore touched the
        /// widget's top edge — the reading looked like part of the control. This gap is taken out of
        /// the widget's band, not out of the switch's: the switch is a control the player clicks and
        /// its size is fixed by ADR-0035, while the widget is a reading and can be a little shorter.
        ///
        /// Two conditional pixels — the same value as <see cref="PowerSwitchPaddingPixels"/> and
        /// <see cref="WidgetInsetPixels"/>. One gap length for the whole column, so the strip reads
        /// as one grid rather than as three different margins.
        /// </summary>
        public const float WidgetTopGapPixels = 2f;

        /// <summary><see cref="WidgetTopGapPixels"/> in the layout's vertical units.</summary>
        public const float WidgetTopGap = WidgetTopGapPixels / ReferenceHeightPixels;

        /// <summary>
        /// Gap between the two bars of the production widget, in pixels at
        /// <see cref="ReferenceHeightPixels"/>.
        ///
        /// Two pixels, the same as the inset: the pair has to read as two readings of one block, not
        /// as one bar with a seam, and not as two unrelated columns. At the sizes involved a wider
        /// gap would eat the fill of both bars.
        /// </summary>
        public const float WidgetBarGapPixels = 2f;

        /// <summary><see cref="WidgetBarGapPixels"/> in the layout's vertical units.</summary>
        public const float WidgetBarGap = WidgetBarGapPixels / ReferenceHeightPixels;

        /// <summary>
        /// Share of a battery widget's width given to the column that holds the charge-mode bolt
        /// (ADR-0042 §4).
        ///
        /// The bolt sits <em>beside</em> the bar rather than on top of it, because the bar's height
        /// is the reading and a bolt drawn over the fill would hide the very number it explains.
        /// A third of the width is what the bolt needs to stay legible after the bar has taken its
        /// own share; the remaining two thirds are the bar.
        /// </summary>
        public const float WidgetBoltShare = 0.34f;

        /// <summary>
        /// Gap between a battery's bar and its bolt, in pixels at
        /// <see cref="ReferenceHeightPixels"/>.
        ///
        /// One pixel: at the sizes involved anything wider separates the bolt from the bar it
        /// belongs to, and the two would read as two widgets.
        /// </summary>
        public const float WidgetBoltGapPixels = 1f;

        /// <summary><see cref="WidgetBoltGapPixels"/> in the layout's vertical units.</summary>
        public const float WidgetBoltGap = WidgetBoltGapPixels / ReferenceHeightPixels;

        /// <summary>
        /// Thickness of a clock's face ring, in pixels at <see cref="ReferenceHeightPixels"/>.
        ///
        /// The ring is what makes two hands read as a clock at all: without a face, a hand pointing
        /// up and a hand pointing elsewhere are two lines. One and a half pixels is the thinnest
        /// that still closes into a circle after rounding at 1080p.
        /// </summary>
        public const float WidgetRingPixels = 1.5f;

        /// <summary><see cref="WidgetRingPixels"/> in the layout's vertical units.</summary>
        public const float WidgetRing = WidgetRingPixels / ReferenceHeightPixels;

        /// <summary>
        /// Thickness of a clock's hands, in pixels at <see cref="ReferenceHeightPixels"/>.
        ///
        /// Thicker than the ring on purpose: the moving hand is the reading, and it has to be the
        /// heaviest line inside the face. The screen-space API has no rotated quad, so a hand is
        /// rasterised as a staircase of one-pixel steps (see <c>VgsScreenDraw.ThickSegment</c>) and
        /// this value is the width of each step.
        /// </summary>
        public const float WidgetHandPixels = 1.5f;

        /// <summary><see cref="WidgetHandPixels"/> in the layout's vertical units.</summary>
        public const float WidgetHand = WidgetHandPixels / ReferenceHeightPixels;

        /// <summary>
        /// Number of chords the clock's face ring is drawn with (ADR-0042 §4).
        ///
        /// The screen-space API has no arc, so the ring is a closed polygon of straight pieces
        /// (<c>VgsScreenDraw.Ring</c>). The count is fixed rather than derived from the radius: at
        /// the size a widget is drawn — a face of about twenty pixels — two dozen chords already
        /// read as a circle, and a count that followed the zoom would make the ring's cost depend on
        /// how far the player scrolled.
        ///
        /// <b>It is a cost, and the cost is named.</b> Each chord is one <c>ThickSegment</c>, and a
        /// segment is a staircase of up to 64 squares; a face at high zoom is therefore a few hundred
        /// quads per card, against two for a bar. That is accepted for a widget of this size and is
        /// worth looking at in the game on a grid with many timers (ADR-0042 §Consequences).
        /// </summary>
        public const int WidgetRingSegments = 24;

        /// <summary>
        /// Length of the clock's <b>fixed</b> hand — the one that stays at twelve o'clock — as a
        /// fraction of the face's radius (ADR-0042 §4).
        ///
        /// Longer than the moving hand: the fixed one is the target, and the moving one has to be
        /// distinguishable from it at the moment they overlap, which is exactly when the timer
        /// fires and again when it starts.
        /// </summary>
        public const float WidgetHourHandRatio = 0.86f;

        /// <summary>
        /// Length of the clock's <b>moving</b> hand, as a fraction of the face's radius.
        ///
        /// Shorter than <see cref="WidgetHourHandRatio"/> so the two hands never merge into one
        /// line: at zero and at one they point the same way, and the only thing that tells the
        /// player "this just started" from "this is about to fire" is that the short hand sits
        /// inside the long one.
        /// </summary>
        public const float WidgetMinuteHandRatio = 0.60f;


        // ---- Connection lines ----

        /// <summary>
        /// Size of one dot of a connection line, in <b>device pixels</b> — two, at every zoom
        /// (ADR-0046, ADR-0047).
        ///
        /// <b>Why a pixel count and not a layout length.</b> This used to be
        /// <c>0.0016f</c>, a fraction of the client height, and that is exactly what made a
        /// horizontal line and a vertical one different weights: the layout's axes do not share
        /// a scale (ADR-0009), so one value came out 2 px tall and 1.1 px wide on a 16:9 window,
        /// and the same value was a different number of pixels at every zoom. The number here is
        /// what the player measures on screen, so it is expressed in what the player measures on
        /// screen.
        /// </summary>
        public const float LineDotPixels = 2f;

        /// <summary>
        /// The clear space between two dots of a connection line, in device pixels — one
        /// (ADR-0047).
        ///
        /// <b>Why a connection is dotted at all.</b> The screen-space API has no antialiasing, so
        /// a solid two-pixel line is a hard-edged rectangle, and the eye reads that edge as
        /// heavier than the two pixels it is: reported from the game as "lines look thicker than
        /// 2 px". A one-pixel gap every third pixel keeps the ink exactly two pixels tall — the
        /// measured weight is unchanged — while the eye averages the gaps in and reads the line as
        /// lighter, which is the effect antialiasing would have provided.
        ///
        /// <b>Why the gap is one and not two.</b> Two is the watch line's gap
        /// (<see cref="WatchDotGapPixels"/>), and that rhythm reads as "dotted" — a different
        /// meaning (ADR-0017). At one the connection still reads as a continuous line and the two
        /// kinds stay distinguishable at a glance: dense rhythm versus open rhythm.
        /// </summary>
        public const float LineDotGapPixels = 1f;

        /// <summary>
        /// Horizontal clearance between a dot and the vertical run of its route. The dot sits
        /// inside the bar, so the gutter has to clear the bar's edge as well, or the vertical
        /// run would be hidden under the card it belongs to. See <c>EdgeRouting</c>.
        /// </summary>
        public const float LineGutter = 0.014f;

        // ---- Line highlight (ADR-0049) ----
        //
        // A line is highlighted when the pointer is over it or when one of its ends is selected.
        // The highlight is not a second colour of the line: it is a second line drawn *under* the
        // first one, so the highlight never changes the geometry the player is reading, and a
        // dotted line shows it through its own gaps.

        /// <summary>
        /// How far from a line's centre the pointer may be and still be "over" it, in device
        /// pixels.
        ///
        /// <b>Measured from the centre, and small on purpose.</b> The ink is two pixels wide, so
        /// four pixels leave three pixels of slack on each side — enough that a two-pixel line can
        /// be caught with a mouse, small enough that two lines running close together stay two
        /// targets. The number is in device pixels for the reason ADR-0046 §3 gives for the dot
        /// size: the layout's axes do not share a scale (ADR-0009), so a tolerance in layout units
        /// would be a different physical distance per axis and would thin out as the player zoomed.
        /// <see cref="Ui.LineHit"/> converts it per axis.
        /// </summary>
        public const float LineHoverPixels = 4f;

        /// <summary>
        /// Ink of one dot of a highlight line, in device pixels — two, exactly the weight of the
        /// line it is drawn under (ADR-0049).
        ///
        /// <b>The same two pixels, not a thicker line.</b> A highlight is not an outline: it lies
        /// on the same path as the line it belongs to, so it cannot be wider without turning into
        /// a border that changes the line's apparent weight.
        /// </summary>
        public const float HighlightDotPixels = 2f;

        /// <summary>
        /// Clear space between two dots of a highlight line, in device pixels — zero (ADR-0049).
        ///
        /// <b>Zero, and that is the whole effect.</b> The connection line is dotted at two pixels
        /// of ink every three (ADR-0047), and the watch line every four (ADR-0017). A highlight
        /// with no gap is a continuous line of the same weight, drawn first: it shows through the
        /// one- and two-pixel gaps of the line above it and the line therefore reads as brighter
        /// and whiter without moving, thickening or losing its own rhythm. The requirement calls
        /// this "dotted with a zero distance" and accepts that it is no longer a dotted pattern.
        ///
        /// <b>Why not a solid line drawn over the line.</b> That would replace the line's colour
        /// with white — the player would lose the connection's own identity (green = "this fires
        /// that", amber = "this watches that") at exactly the moment the highlight is meant to
        /// help them follow it.
        /// </summary>
        public const float HighlightGapPixels = 0f;

        /// <summary>
        /// Hard cap on drawn connections; protects the frame budget on a dense graph.
        ///
        /// <b>The number is unchanged, but what it buys is not.</b> A connection used to be three
        /// to five rectangles, so 600 of them were a few thousand quads. Since ADR-0046/0047 a
        /// connection is a rounded path drawn as dots, and the measured cost of one is about 200
        /// to 300 pieces (<c>tools/line-preview/run.ps1</c> prints the table). The cap is
        /// therefore now the frame's main guard rather than a formality: it bounds the line pass
        /// at roughly a hundred and fifty thousand quads. A dense schema that drops frames there is
        /// answered by the levers named in ADR-0047 — the dot period and the bend's sample count,
        /// then this cap — not by making the dots smaller, which is a property the owner fixed at
        /// two pixels of ink.
        /// </summary>
        public const int MaxRenderedEdges = 600;

        // ---- Conveyor network (ADR-0040) ----
        //
        // A pipe is drawn "in section": two lines with a gap between them, never a filled
        // band. The numbers below are the whole of its look, and they are written in
        // *conditional* pixels at the reference height — the same unit the port controls and
        // the grid borders use — because "four pixels of bore" is a design proportion, not a
        // distance on the field.

        /// <summary>
        /// Bore of a pipe: the clear space between its two walls, in pixels at
        /// <see cref="ReferenceHeightPixels"/>.
        /// </summary>
        public const float ConveyorGapPixels = 4f;

        /// <summary>
        /// Thickness of one wall of a pipe, in device pixels. One pixel at every zoom, like
        /// <see cref="GridLinePixels"/> and <see cref="GridBorderPixels"/>: a wall that grew
        /// with the zoom would compete with the cards it runs past, and a wall expressed in
        /// layout units comes out one pixel tall and <c>height/width</c> wide (ADR-0009).
        /// </summary>
        public const float ConveyorWallPixels = 1f;

        /// <summary>
        /// Radius of a pipe's bend, in pixels at <see cref="ReferenceHeightPixels"/>.
        ///
        /// A bend is rounded (requirement), and the rounding is a staircase of axis-aligned
        /// pieces because the screen-space API has no arc (<see cref="Ui.PipeRouter"/>). The
        /// radius sets how many pixels the staircase spans; below a couple of pixels the bend
        /// would read as square anyway, so this is deliberately larger than the bore.
        /// </summary>
        public const float ConveyorCornerPixels = 6f;

        /// <summary>
        /// Half of a pipe's overall width, in the layout's vertical units: half the bore plus
        /// half of one wall. Horizontal extents multiply it by the client aspect, exactly as
        /// every other round or square control does (ADR-0009).
        /// </summary>
        public const float ConveyorHalfWidth =
            (ConveyorGapPixels + ConveyorWallPixels) * 0.5f / ReferenceHeightPixels;

        /// <summary>Radius of a pipe's bend in the layout's vertical units.</summary>
        public const float ConveyorCorner = ConveyorCornerPixels / ReferenceHeightPixels;

        /// <summary>
        /// Longest one piece of a pipe's bend may be, in the layout's vertical units: two device
        /// pixels at the reference height.
        ///
        /// <b>This is what makes the rounding look round.</b> The screen-space API has no arc, so
        /// a bend is a staircase of axis-aligned pieces (<see cref="Ui.PipeRouter"/>), and the
        /// number of steps is derived from the radius divided by this length. A fixed step count
        /// would be a chamfer at a small radius and a coarse stair at a large one; a step
        /// proportional to the radius keeps the rounding the same shape at every zoom, the rule
        /// <c>VgsScreenDraw.Disc</c> follows with one slab per screen pixel.
        ///
        /// <b>Two pixels, not one, and the reason is the budget.</b> A wall is one pixel thick,
        /// so a two-pixel step of a six-pixel bend is already below what the eye resolves on a
        /// line that thin — measured on the preview at 1080p, one and two pixels are
        /// indistinguishable — while halving the number of pieces a bend costs. The pieces are
        /// what the frame pays for: a bend is two rectangles per step per rail, so one pixel per
        /// step took about 230 pieces for an S-route, which at
        /// <see cref="MaxRenderedConveyors"/> links would be tens of thousands of quads a frame.
        /// The caller scales this value by the zoom, exactly as it scales the radius.
        /// </summary>
        public const float ConveyorStepPixels = 2f;

        /// <summary><see cref="ConveyorStepPixels"/> in the layout's vertical units.</summary>
        public const float ConveyorStep = ConveyorStepPixels / ReferenceHeightPixels;

        /// <summary>
        /// Width of a <b>highlighted</b> pipe's fill, in device pixels — the bore plus both walls
        /// (ADR-0049).
        ///
        /// <b>Derived, not chosen.</b> The highlight of a pipe is the same pipe with its bore
        /// filled in, so the fill has to reach exactly the outer edges of the two rails and no
        /// further: anything narrower leaves a seam of canvas inside the pipe, anything wider
        /// grows a fringe outside it. Both are visible at any zoom, because the walls and the bore
        /// are device-pixel sizes (ADR-0040).
        ///
        /// The fill is drawn as the pipe's <em>centre line</em> at this thickness (see
        /// <c>PipeRouter.Build</c>), which is why the number is the full width rather than half of
        /// it: the centre line has no offset of its own.
        /// </summary>
        public const float ConveyorFillPixels = ConveyorGapPixels + ConveyorWallPixels * 2f;

        /// <summary>
        /// Hard cap on drawn conveyor links; protects the frame budget on a cargo ship, where
        /// almost every container is on one network.
        ///
        /// <b>Lower than <see cref="MaxRenderedEdges"/> on purpose, and the arithmetic is the
        /// reason.</b> A pipe is two rails per straight plus a staircase per bend — about
        /// seventy pieces for a link with one bend and about a hundred and forty for the S-shape
        /// a backward link takes (measured by the harness that produced
        /// <c>docs/01-research/images/conveyor-pipe.png</c>). A hundred links is therefore about
        /// seven thousand quads, which sits alongside the cards and their controls rather than
        /// above them. (A connection line costs about the same per line since ADR-0046, but there
        /// can be six times as many of them — which is why the two caps differ.) Links past the
        /// cap stay in the graph and are not drawn, exactly like connections past
        /// <see cref="MaxRenderedEdges"/>.
        /// </summary>
        public const int MaxRenderedConveyors = 100;

        /// <summary>
        /// How many raw conveyor lines one structural refresh may read. The lines are cheap per
        /// line, but a very large cargo construct has thousands of them and the pass runs on a
        /// frame.
        /// </summary>
        public const int MaxConveyorSegments = 4000;

        /// <summary>
        /// How many grids of the construct one refresh reads conveyor lines from. Only a grid
        /// with at least two drawn cards can produce a link, which already excludes most
        /// subgrids; this is the backstop for a construct with dozens of them.
        /// </summary>
        public const int MaxConveyorGrids = 32;

        // ---- Action menu (opened by clicking a target while in bind mode, or by a right
        // click on a card in idle mode — ADR-0041) ----

        public const float ActionMenuWidth = 0.22f;
        public const float ActionMenuRowHeight = 0.023f;
        public const float ActionMenuTitleHeight = 0.026f;

        /// <summary>Rows shown at once. Longer action lists are scrolled with the wheel.</summary>
        public const int ActionMenuMaxRows = 9;

        /// <summary>
        /// How far the pointer may travel between a right press on a card and its release for
        /// that press to still count as a click — the click that opens the manual action menu
        /// (ADR-0041). Four pixels.
        ///
        /// <b>Why a distance and not "the press itself".</b> The right button also pans, and it
        /// has panned from anywhere on the canvas since stage 1. Opening the menu on the press
        /// would take that away from every card — on a dense schema the pan would start only in
        /// the gaps — so the press only *arms* a candidate and the release decides: within this
        /// distance it was a click, farther it was a pan and the candidate is dropped. The
        /// number is small on purpose: it absorbs the hand's tremor, not a deliberate move.
        ///
        /// <b>Pixels, not layout units.</b> One X unit of this layout is the window width and one
        /// Y unit is its height (ADR-0009), so a threshold in layout units is a different
        /// physical distance per axis. <see cref="Ui.PointerGesture.ExceedsClickSlop"/> converts
        /// each axis through the window size and compares against this value in pixels;
        /// <see cref="ClickSlop"/> below is only the fallback for a window that has not been
        /// measured yet, and nothing else should read this constant directly.
        /// </summary>
        public const float ClickSlopPixels = 4f;

        /// <summary>
        /// <see cref="ClickSlopPixels"/> in the layout's vertical units, used only when the window
        /// size is not known yet. See the constant above for why it is not the rule itself.
        /// </summary>
        public const float ClickSlop = ClickSlopPixels / ReferenceHeightPixels;

        /// <summary>Distance in normalized units a pointer must travel before it counts as a drag.</summary>
        public const float DragThreshold = 0.0025f;

        // ---- Multi-node selection and the marquee band (ADR-0048) ----
        //
        // The band is drawn with device-pixel sizes for the reason ADR-0046 §3 gives for the
        // connection line: one X unit of this layout is the window's width and one Y unit its
        // height (ADR-0009), so a dash expressed in layout units would be a different number of
        // pixels per axis and would thin out as the player zoomed.

        /// <summary>
        /// Length of one dash of the marquee band, in <b>device pixels</b> — four, at every zoom.
        ///
        /// <b>Equal to the gap, and that is the whole look.</b> A dash noticeably longer than its
        /// gap reads as a solid line with nicks in it, and a dash shorter than its gap reads as a
        /// dotted line — which this editor already uses for a watch link (ADR-0017). Four and four
        /// at one pixel of thickness is the smallest pattern that still reads as "dashed" when the
        /// band is drawn over a busy schema.
        /// </summary>
        public const float MarqueeDashPixels = 4f;

        /// <summary>Clear space between two dashes of the marquee band, in device pixels.</summary>
        public const float MarqueeGapPixels = 4f;

        /// <summary>
        /// Thickness of the marquee band, in device pixels — one, at every zoom.
        ///
        /// One pixel is the requirement's own number, and it is also the only weight that keeps the
        /// band from competing with the cards it encloses: it is an annotation, not a frame. A
        /// layout-unit thickness could not express it — it would be 1 px tall and <c>height/width</c>
        /// of a pixel wide (ADR-0009) — which is why the rasteriser takes a pixel count.
        /// </summary>
        public const float MarqueeThicknessPixels = 1f;

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

}

