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

        /// <summary>Stable GUID used for world-storage keys and network channel registration.</summary>
        /// <remarks>
        /// Do not regenerate: it identifies this mod's persisted data across versions.
        /// </remarks>
        public const string ModGuid = "6f1c2a44-3d7e-4a1b-9c05-2b8e7d4f6a13";

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

        /// <summary>Size of a block card at zoom 1, in normalized units.</summary>
        public const float NodeWidth = 0.16f;
        public const float NodeHeight = 0.075f;
        public const float NodeCornerRadius = 0.004f;

        /// <summary>Distance in normalized units a pointer must travel before it counts as a drag.</summary>
        public const float DragThreshold = 0.0025f;

        /// <summary>Hard cap on blocks drawn at once; protects the frame budget on huge grids.</summary>
        public const int MaxRenderedBlocks = 400;

        /// <summary>How many grid blocks we pull from the grid in one refresh.</summary>
        public const int MaxGridBlocksScanned = 2000;
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

        // Editor chrome
        public const string EditorTitle = "Grid setup";
        public const string CloseButton = "Закрыть";
        public const string Hint = "ЛКМ — выбрать · перетащить · колесо — масштаб · ПКМ/средняя — панорама · Esc — закрыть";
        public const string EmptyGrid = "На этом гриде нет блоков для отображения";
        public const string Loading = "Чтение блоков грида…";
        public const string NoGrid = "Не удалось определить грид";
        public const string GridFallbackName = "Grid";

        // Status bar. Format arguments are documented at each use site: keeping the pattern
        // here means the wording is translated in one place, and the caller can cache the
        // formatted result instead of rebuilding it every frame.
        public const string StatusFormat = "Блоков: {0}   ·   Связей: {1}   ·   Масштаб: {2}%";
        public const string CullingFormat = "Показано {0} из {1} блоков — увеличьте масштаб";

        // Node subtitles
        public const string InputsLabel = "входы";
        public const string OutputsLabel = "выходы";
        public const string NoPorts = "нет терминала";

        // Errors
        public const string ErrOpenScreen = "Не удалось открыть редактор";
        public const string ErrNoAccess = "Нет доступа к этому блоку";
    }
}
