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

        /// <summary>Network message id for future client/server sync. Must be unique per mod.</summary>
        public const ushort NetworkChannelId = 48123;

        // ---- Editor layout, in normalized screen units (0..1 across the client area) ----

        /// <summary>Height of the bottom bar holding the Close button.</summary>
        public const float BottomBarHeight = 0.085f;

        /// <summary>Height of the header strip holding the title and the grid name.</summary>
        public const float HeaderHeight = 0.055f;

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
        /// Font scale of the "x" glyph inside a port's remove button at zoom 1. Smaller than
        /// the T/F glyph because the disc it sits in is smaller: 75% of a port bar's height,
        /// not 44% of a card's.
        /// </summary>
        public const float PortRemoveGlyphScale = 0.40f;

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

        // ---- "+" button (bind mode) ----
        //
        // Drawn inverted: a solid white disc with the plus knocked out in the card's own
        // colour. Inverted because it must not read as part of the card, and because a dark
        // glyph on white stays legible at every zoom while a light glyph on dark does not.

        /// <summary>Button diameter as a fraction of the card height.</summary>
        public const float PlusButtonHeightRatio = 0.75f;

        /// <summary>Radius of the round "+" button, derived from the card height.</summary>
        public const float PlusButtonRadius = NodeHeight * PlusButtonHeightRatio * 0.5f;

        /// <summary>
        /// Distance from the card's right edge to the button's right edge, so the circle sits
        /// inside the card instead of straddling its border.
        /// </summary>
        public const float PlusButtonRightMargin = 0.006f;

        /// <summary>Gap between the "+" button and the card's text.</summary>
        public const float PlusButtonTextGap = 0.004f;

        /// <summary>Arm length of the plus, as a fraction of the button diameter.</summary>
        public const float PlusArmRatio = 0.75f;

        /// <summary>Stroke thickness of the plus, as a fraction of the button diameter.</summary>
        public const float PlusStrokeRatio = 0.14f;

        // ---- "true" / "false" buttons (yes/no toolbars) ----
        //
        // A block whose toolbar has two fire groups cannot offer one "+": the user has to say
        // which list the connection goes into. Two smaller discs stack vertically on the
        // card's right edge — vertically, because the card's width belongs to the text, while
        // its height is otherwise empty. Radius 22% of the card height leaves a hairline gap
        // between them, which is what makes them read as two controls rather than one blob.

        /// <summary>Radius of one group button, as a fraction of the card height.</summary>
        public const float GroupButtonHeightRatio = 0.22f;

        public const float GroupButtonRadius = NodeHeight * GroupButtonHeightRatio;

        /// <summary>Vertical centre of the "true" button, as a fraction of the card height.</summary>
        public const float GroupButtonTrueY = 0.27f;

        /// <summary>Vertical centre of the "false" button, as a fraction of the card height.</summary>
        public const float GroupButtonFalseY = 0.73f;

        /// <summary>Text scale of the T/F glyph inside a group button.</summary>
        public const float GroupButtonGlyphScale = 0.46f;

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
        public const string EditorTitle = "Grid setup";
        public const string CloseButton = "Закрыть";
        public const string Hint =
            "ЛКМ — выбрать · перетащить · «+» или «T»/«F» — режим привязки · «x» на порте — удалить связь · ПКМ — отмена · колесо — масштаб · Esc — закрыть";
        public const string EmptyGrid = "На этом гриде нет блоков для отображения";
        public const string Loading = "Чтение блоков грида…";
        public const string NoGrid = "Не удалось определить грид";
        public const string GridFallbackName = "Grid";

        // Bind mode: the round "+" turns the pointer into a live line from a source port
        // until the user picks a target or cancels with the right mouse button.
        public const string BindHint = "Режим привязки: нажмите блок-получатель · ПКМ — отмена";

        /// <summary>
        /// The two buttons of a yes/no toolbar. A block like a sensor or an event controller
        /// keeps two independent lists of actions — what happens on "yes" and what happens on
        /// "no" — so the user has to choose one before the target.
        /// </summary>
        public const string TrueButtonGlyph = "T";

        public const string FalseButtonGlyph = "F";

        /// <summary>
        /// Glyph on a port's remove button. The Latin letter, not the multiplication sign: the
        /// glyph is drawn by the game's font, and the plain letter exists in every font the
        /// editor may fall back to, while U+00D7 is not guaranteed in any of them.
        /// </summary>
        public const string PortRemoveGlyph = "x";

        public const string BindHintTrue =
            "Привязка к «да»: нажмите блок-получатель · ПКМ — отмена";

        public const string BindHintFalse =
            "Привязка к «нет»: нажмите блок-получатель · ПКМ — отмена";

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

        // Status bar. Format arguments are documented at each use site: keeping the pattern
        // here means the wording is translated in one place, and the caller can cache the
        // formatted result instead of rebuilding it every frame.
        public const string StatusFormat =
            "Блоков: {0}   ·   Групп: {1}   ·   Связей: {2}   ·   Масштаб: {3}%";
        public const string CullingFormat = "Показано {0} из {1} блоков — увеличьте масштаб";

        // Errors
        public const string ErrOpenScreen = "Не удалось открыть редактор";
        public const string ErrNoAccess = "Нет доступа к этому блоку";
    }
}
