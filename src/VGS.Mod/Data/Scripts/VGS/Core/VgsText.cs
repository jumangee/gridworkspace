using System;

namespace Vgs.Core
{
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
