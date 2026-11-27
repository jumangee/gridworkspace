using System;
using System.Collections.Generic;

namespace Vgs.Core
{
    /// <summary>
    /// The editor's text, read through whichever language is active (ADR-0033).
    ///
    /// <b>One facade, two jobs.</b> It names every string once — a call site says
    /// <c>VgsText.CloseButton</c> and never a literal — and it holds the language for the whole
    /// mod. The strings themselves live in <see cref="VgsStrings"/>, one key → value table per
    /// language; the tables are data, so adding a language or moving the wording into a file
    /// does not change this class's surface and does not touch a single call site.
    ///
    /// <b>Why a property per string and not <c>VgsText.Get("CLOSE_BUTTON")</c>.</b> A method call
    /// would put the key in every call site, which is exactly the scattering the facade exists to
    /// prevent: a typo in a key at a call site compiles and shows the key on screen, and the
    /// compiler cannot help because the parameter is a string. With a property, the key appears
    /// once, next to its name, and a renamed or removed key breaks in one file.
    ///
    /// <b>Missing entries do not throw.</b> <see cref="S"/> falls back to English and then to the
    /// key itself. A key on screen is ugly and unmistakable — it is an all-caps mark no sentence
    /// looks like — and it is strictly better than an exception in a draw path, or a blank
    /// control that reads as a layout bug. The offline gate asserts that no property ever
    /// returns a key, so the fallback is a safety net rather than a state the mod ships in.
    ///
    /// <b>Cost.</b> One dictionary lookup per read (about twenty nanoseconds, no allocation). The
    /// draw path reads a handful of strings per frame, so this is not measured; if it ever were,
    /// the tables are read-only and a snapshot could be cached on <see cref="UseLanguage"/>.
    ///
    /// <b>Not thread safe, and does not need to be:</b> the game calls session components on the
    /// main thread, and the language is only switched from there.
    /// </summary>
    public static class VgsText
    {
        /// <summary>
        /// The active table. Starts as English: before a session exists there is no config to ask,
        /// and English is the language this repository and the game's default are written in.
        /// </summary>
        private static IReadOnlyDictionary<string, string> _table = VgsStrings.English;

        /// <summary>The language currently in use.</summary>
        public static VgsLanguage Language
        {
            get { return _language; }
        }

        private static VgsLanguage _language = VgsLanguage.English;

        /// <summary>
        /// Switches the language. Returns true when the table actually changed, so the caller can
        /// log a real change instead of every attempt.
        ///
        /// A language without a table is <b>not</b> an error: the mod translates two of the game's
        /// twenty-seven languages, and everything else falls back to English. That is the same
        /// rule <c>Vgs.Game.GameLanguage</c> applies, kept here as well so that a direct call
        /// cannot end up pointing at a table that does not exist.
        /// </summary>
        public static bool UseLanguage(VgsLanguage language)
        {
            if (language == _language) return false;

            _language = language;
            _table = language == VgsLanguage.Russian ? VgsStrings.Russian : VgsStrings.English;
            return true;
        }

        /// <summary>
        /// Drops the language back to the default. Called on session unload, so a language read in
        /// one world cannot outlive it: the next session asks the game again, and a failed read
        /// leaves the fallback rather than the previous world's choice.
        /// </summary>
        internal static void Reset()
        {
            _language = VgsLanguage.English;
            _table = VgsStrings.English;
        }

        /// <summary>
        /// The one lookup: the active language, then English, then the key.
        ///
        /// The second fallback is not redundancy — it is what keeps a half-translated language from
        /// showing a key. A table may be missing a key (the gate forbids it, but the runtime must
        /// not depend on the gate), and the English word is always a better answer than
        /// <c>WARN_WATCH_BLOCK_GONE</c> in the middle of a sentence.
        /// </summary>
        private static string S(string key)
        {
            string value;
            if (_table.TryGetValue(key, out value) && !string.IsNullOrEmpty(value)) return value;
            if (VgsStrings.English.TryGetValue(key, out value) && !string.IsNullOrEmpty(value)) return value;

            // Last resort: the key itself. Visible, greppable, and never an exception in a draw
            // path. The offline gate asserts this never happens for any property.
            return key;
        }

        // ---- Language-independent marks ----
        //
        // These are ASCII on purpose, and they are not translated. The editor falls back between
        // several fonts, and a glyph missing from one of them draws as a box; a letter from
        // another alphabet is the most likely glyph to be missing. The full reasoning for each
        // mark is in ADR-0019 (icon fallbacks) and ADR-0014 (the port's "x").

        /// <summary>
        /// Fallback glyph of the modes button, used only when the icon atlas is unavailable
        /// (ADR-0019). The button is drawn as the <c>sliders</c> icon; "=" is the ASCII mark
        /// closest to three sliders.
        /// </summary>
        public const string ModesGlyph = "=";

        /// <summary>
        /// Fallback glyph of the single-group bind button (ADR-0019, ADR-0020). The button is
        /// drawn as the <c>square-plus</c> icon.
        /// </summary>
        public const string SingleButtonGlyph = "+";

        /// <summary>
        /// Fallback glyph of the "true" button of a yes/no toolbar. The button is drawn as a
        /// filled <c>lightbulb</c> icon; "T" is what it said before it became an icon, so a
        /// reader of the old screenshots still recognises it.
        /// </summary>
        public const string TrueButtonGlyph = "T";

        /// <summary>Fallback glyph of the "false" button; see <see cref="TrueButtonGlyph"/>.</summary>
        public const string FalseButtonGlyph = "F";

        /// <summary>
        /// Glyph on a port's remove button, used only when the icon atlas is unavailable
        /// (ADR-0019). The Latin letter, not the multiplication sign: U+00D7 is not guaranteed
        /// in any of the fonts the editor may fall back to.
        /// </summary>
        public const string PortRemoveGlyph = "x";

        /// <summary>
        /// Fallback glyph of the jump button, used only when the icon atlas is unavailable
        /// (ADR-0019). The button is drawn as a <c>share-from-square</c> icon; "&gt;" is the
        /// ASCII mark that says the same thing.
        /// </summary>
        public const string JumpGlyph = ">";

        /// <summary>
        /// Fallback glyph of the watch button, used only when the icon atlas is unavailable
        /// (ADR-0019). The button is drawn as a <c>bell</c> icon; "#" is the mark it carried
        /// before it became an icon.
        /// </summary>
        public const string WatchGlyph = "#";

        /// <summary>
        /// Fallback glyph of the card's power switch while the block is on (ADR-0035), used only
        /// when the icon atlas is unavailable (ADR-0019). The switch is drawn as a <c>toggle-on</c>
        /// icon — a filled pill with its knob at the right — and "|" is the ASCII mark that keeps
        /// the one thing that matters: the two states are still distinguishable, because the mark
        /// for "off" is a different character.
        /// </summary>
        public const string PowerOnGlyph = "|";

        /// <summary>
        /// Fallback glyph of the card's power switch while the block is off or cannot be switched
        /// on. "o" for the empty half of the pill, which is what the knob has left.
        /// </summary>
        public const string PowerOffGlyph = "o";

        /// <summary>
        /// Fallback glyph of a battery widget's charge-mode bolt (ADR-0042), used only when the icon
        /// atlas is unavailable (ADR-0019 §6). "z" is the ASCII stand-in for a lightning bolt, and it
        /// is drawn in the accent's own colour, so "recharging" and "discharging" stay
        /// distinguishable even without the texture.
        /// </summary>
        public const string BatteryAccentGlyph = "z";


        /// <summary>
        /// Comparison sign for "less or equal" (the game's inverted condition). ASCII, see above.
        /// </summary>
        public const string WatchSignLower = "<=";

        /// <summary>Comparison sign for "greater" (the default condition). ASCII, see above.</summary>
        public const string WatchSignHigher = ">=";

        /// <summary>Number format of the threshold, invariant culture.</summary>
        public const string WatchThresholdFormat = "0.00";

        // ---- Terminal controls ----

        public static string ButtonTitle { get { return S("BUTTON_TITLE"); } }
        public static string ButtonTooltip { get { return S("BUTTON_TOOLTIP"); } }
        public static string ShowInVgsTitle { get { return S("SHOW_IN_VGS_TITLE"); } }
        public static string ShowInVgsTooltip { get { return S("SHOW_IN_VGS_TOOLTIP"); } }
        public static string ShowInVgsOn { get { return S("SHOW_IN_VGS_ON"); } }
        public static string ShowInVgsOff { get { return S("SHOW_IN_VGS_OFF"); } }

        // ---- Editor chrome ----

        public static string EditorTitle { get { return S("EDITOR_TITLE"); } }
        public static string StatsFormat { get { return S("STATS_FORMAT"); } }
        public static string CloseButton { get { return S("CLOSE_BUTTON"); } }
        public static string EmptyGrid { get { return S("EMPTY_GRID"); } }
        public static string Loading { get { return S("LOADING"); } }
        public static string NoGrid { get { return S("NO_GRID"); } }
        public static string GridFallbackName { get { return S("GRID_FALLBACK_NAME"); } }

        // ---- Bottom bar: modes menu and block search (ADR-0023) ----

        public static string SearchPlaceholder { get { return S("SEARCH_PLACEHOLDER"); } }
        public static string ModesShowHidden { get { return S("MODES_SHOW_HIDDEN"); } }
        public static string ModesShowSubgrids { get { return S("MODES_SHOW_SUBGRIDS"); } }
        public static string ModesShowGridBorders { get { return S("MODES_SHOW_GRID_BORDERS"); } }
        public static string ModesShowConveyors { get { return S("MODES_SHOW_CONVEYORS"); } }
        public static string ModesHint { get { return S("MODES_HINT"); } }
        public static string SearchHint { get { return S("SEARCH_HINT"); } }

        // ---- Pointer modes ----

        public static string BindHint { get { return S("BIND_HINT"); } }
        public static string BindHintTrue { get { return S("BIND_HINT_TRUE"); } }
        public static string BindHintFalse { get { return S("BIND_HINT_FALSE"); } }

        // ---- Jump to the block's terminal (ADR-0021) ----

        public static string WarnJumpBlockGone { get { return S("WARN_JUMP_BLOCK_GONE"); } }
        public static string WarnJumpNoPlayer { get { return S("WARN_JUMP_NO_PLAYER"); } }
        public static string WarnJumpFailed { get { return S("WARN_JUMP_FAILED"); } }
        public static string WarnJumpNotTerminal { get { return S("WARN_JUMP_NOT_TERMINAL"); } }
        public static string WarnJumpNoAntenna { get { return S("WARN_JUMP_NO_ANTENNA"); } }
        public static string JumpHint { get { return S("JUMP_HINT"); } }

        // ---- Block power switch (ADR-0035) ----

        /// <summary>Hint for a card's power switch while the pointer is over it.</summary>
        public static string PowerHint { get { return S("POWER_HINT"); } }

        /// <summary>
        /// Hint for the switch of a block that is too damaged to be switched on. A different
        /// sentence, not a variant of the first: the player is looking at a control that will
        /// refuse, and the only useful thing to say is why.
        /// </summary>
        public static string PowerUnavailableHint { get { return S("POWER_UNAVAILABLE_HINT"); } }

        /// <summary>Shown when a switch-on is refused because of the block's damage.</summary>
        public static string WarnPowerDamaged { get { return S("WARN_POWER_DAMAGED"); } }

        /// <summary>Shown when the block did not accept the switch at all.</summary>
        public static string WarnPowerRefused { get { return S("WARN_POWER_REFUSED"); } }


        // ---- Event controller watch mode (ADR-0017, ADR-0020) ----

        public static string WatchHint { get { return S("WATCH_HINT"); } }
        public static string WatchStatusThresholdFormat { get { return S("WATCH_STATUS_THRESHOLD_FORMAT"); } }
        public static string WatchStatusPlainFormat { get { return S("WATCH_STATUS_PLAIN_FORMAT"); } }
        public static string WatchStatusNoEvent { get { return S("WATCH_STATUS_NO_EVENT"); } }
        public static string WatchStatusAndSuffix { get { return S("WATCH_STATUS_AND_SUFFIX"); } }
        public static string WarnWatchDuplicateFormat { get { return S("WARN_WATCH_DUPLICATE_FORMAT"); } }
        public static string WarnWatchNoEvent { get { return S("WARN_WATCH_NO_EVENT"); } }
        public static string WarnWatchEventIgnoresBlocks { get { return S("WARN_WATCH_EVENT_IGNORES_BLOCKS"); } }
        public static string WarnWatchDifferentConstruct { get { return S("WARN_WATCH_DIFFERENT_CONSTRUCT"); } }
        public static string WarnWatchNotAcceptedFormat { get { return S("WARN_WATCH_NOT_ACCEPTED_FORMAT"); } }
        public static string WarnWatchClient { get { return S("WARN_WATCH_CLIENT"); } }
        public static string WarnWatchRejected { get { return S("WARN_WATCH_REJECTED"); } }
        public static string WarnWatchBlockGone { get { return S("WARN_WATCH_BLOCK_GONE"); } }
        public static string WarnWatchGroup { get { return S("WARN_WATCH_GROUP"); } }
        public static string WarnWatchTooManyFormat { get { return S("WARN_WATCH_TOO_MANY_FORMAT"); } }
        public static string WarnWatchRemoveRejectedFormat { get { return S("WARN_WATCH_REMOVE_REJECTED_FORMAT"); } }
        public static string WarnWatchGameOwnedFormat { get { return S("WARN_WATCH_GAME_OWNED_FORMAT"); } }

        // ---- Action menu ----

        public static string ActionMenuTitleFormat { get { return S("ACTION_MENU_TITLE_FORMAT"); } }
        public static string ActionMenuEmpty { get { return S("ACTION_MENU_EMPTY"); } }
        public static string ActionMenuFanInFormat { get { return S("ACTION_MENU_FAN_IN_FORMAT"); } }

        /// <summary>
        /// "rows 1-9 / 15" for an action list longer than the menu can show. Numbers and a slash
        /// in both languages: what needs translating is the word "rows", and the counter is
        /// deliberately wordless so a truncated list is visible without a new sentence.
        /// </summary>
        public static string ActionMenuRangeFormat { get { return S("ACTION_MENU_RANGE_FORMAT"); } }

        // ---- Manual action (ADR-0041) ----

        /// <summary>
        /// Heading of the menu a right click on a card opens. It names the mode, not the target:
        /// the target is the card under the pointer, and the rows are its actions.
        /// </summary>
        public static string ManualActionTitle { get { return S("MANUAL_ACTION_TITLE"); } }

        /// <summary>Bottom-bar line while the manual action menu is open.</summary>
        public static string ManualActionHint { get { return S("MANUAL_ACTION_HINT"); } }

        /// <summary>Shown when the block the menu was opened for is gone.</summary>
        public static string WarnManualBlockGone { get { return S("WARN_MANUAL_BLOCK_GONE"); } }

        /// <summary>Shown when the chosen row is no longer an action of the target.</summary>
        public static string WarnManualActionGoneFormat { get { return S("WARN_MANUAL_ACTION_GONE_FORMAT"); } }

        /// <summary>Shown when the group the frame stands for is no longer in the terminal system.</summary>
        public static string WarnManualGroupGoneFormat { get { return S("WARN_MANUAL_GROUP_GONE_FORMAT"); } }

        /// <summary>Shown when the game threw while applying the action.</summary>
        public static string WarnManualRejected { get { return S("WARN_MANUAL_REJECTED"); } }

        // ---- Write failures ----

        public static string WarnNoFreeCellFormat { get { return S("WARN_NO_FREE_CELL_FORMAT"); } }
        public static string WarnEveryPageFormat { get { return S("WARN_EVERY_PAGE_FORMAT"); } }
        public static string WarnActionGoneFormat { get { return S("WARN_ACTION_GONE_FORMAT"); } }
        public static string WarnNotDrivableFormat { get { return S("WARN_NOT_DRIVABLE_FORMAT"); } }
        public static string WarnRejected { get { return S("WARN_REJECTED"); } }
        public static string WarnBlockGone { get { return S("WARN_BLOCK_GONE"); } }
        public static string WarnGroupGoneFormat { get { return S("WARN_GROUP_GONE_FORMAT"); } }
        public static string WarnRemoveRefusedFormat { get { return S("WARN_REMOVE_REFUSED_FORMAT"); } }

        // ---- Errors ----

        public static string ErrOpenScreen { get { return S("ERR_OPEN_SCREEN"); } }
        public static string ErrNoAccess { get { return S("ERR_NO_ACCESS"); } }
    }
}
