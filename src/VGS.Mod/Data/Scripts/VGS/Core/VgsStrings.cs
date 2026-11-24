using System;
using System.Collections.Generic;

namespace Vgs.Core
{
    /// <summary>
    /// The string tables: one key → value dictionary per language (ADR-0033).
    ///
    /// <b>Why a dictionary and not a class per language.</b> Strings are data, and this project
    /// already has a rule for that ([ADR-0031](../adr/0031-specificity-inheritance-vs-typed-state.md)):
    /// data goes into a descriptor, a type is for behaviour. A language has no behaviour — it is
    /// fifty-nine values. A class per language would also mean a class per language forever, while
    /// a table can move to a file without the mod changing shape.
    ///
    /// <b>Completeness is enforced by a gate, not by the compiler.</b> That is the price of this
    /// shape, and it is paid in <c>tests/OfflineChecks.cs</c> (section "localization tables"):
    /// the two tables must have exactly the same keys, every value must be non-empty, the
    /// placeholders of a key must agree between languages, and every key must be reachable from
    /// <c>VgsText</c> — a key nobody reads is a string the player will never see translated.
    /// A missing value cannot hide either: <see cref="VgsText"/> returns the key itself, which is
    /// an all-caps mark no real string looks like, and the gate asserts that no property returns
    /// one.
    ///
    /// <b>The keys are the contract.</b> They are written once, in <c>VgsText</c>, next to the
    /// property that reads them, so the name of a key and the name of its property are always one
    /// screen apart. Renaming a key is a rename in this file and in <c>VgsText</c> — the gate
    /// names the key that went missing.
    ///
    /// <b>Ordinal comparison.</b> Keys are ASCII identifiers, so culture-aware comparison would
    /// only add a way for the lookup to depend on the player's locale.
    /// </summary>
    public static class VgsStrings
    {
        /// <summary>The English table — the default and the fallback for every other language.</summary>
        public static readonly IReadOnlyDictionary<string, string> English = BuildEnglish();

        /// <summary>The Russian table.</summary>
        public static readonly IReadOnlyDictionary<string, string> Russian = BuildRussian();

        /// <summary>
        /// The English wording.
        ///
        /// English is the fallback for two reasons: it is the language the game itself defaults
        /// to, and it is the language this repository is written in, so a string added here first
        /// is one the author can proof-read in place.
        /// </summary>
        private static Dictionary<string, string> BuildEnglish()
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // ---- Terminal controls (survive in the world) ----
                { "BUTTON_TITLE", "Grid Workspace" },
                { "BUTTON_TOOLTIP", "Open the grid logic editor" },
                { "SHOW_IN_VGS_TITLE", "Show in Grid Workspace" },
                { "SHOW_IN_VGS_TOOLTIP",
                  "Draw this block on the grid schema. Turning it off hides the block and its lines; "
                  + "connections keep working" },
                { "SHOW_IN_VGS_ON", "On" },
                { "SHOW_IN_VGS_OFF", "Off" },

                // ---- Editor chrome ----
                { "EDITOR_TITLE", "Workspace" },
                { "STATS_FORMAT", " // blocks: {0} · groups: {1} · links: {2} · zoom: {3}%" },
                { "CLOSE_BUTTON", "Close" },
                { "EMPTY_GRID", "This grid has no blocks to display" },
                { "LOADING", "Reading grid blocks…" },
                { "NO_GRID", "Could not determine the grid" },
                { "GRID_FALLBACK_NAME", "Grid" },

                // ---- Bottom bar: modes menu and block search (ADR-0023) ----
                { "SEARCH_PLACEHOLDER", "Search blocks" },
                { "MODES_SHOW_HIDDEN", "Show/hide invisible" },
                { "MODES_SHOW_SUBGRIDS", "Show subgrids" },
                { "MODES_SHOW_GRID_BORDERS", "Show grid borders" },
                { "MODES_HINT", "View modes: show hidden blocks" },
                { "SEARCH_HINT",
                  "Block search · Enter — first match · \"x\" — clear · "
                  + "Ctrl+A / C / X / V — select, copy, cut, paste "
                  + "(the buffer is the field's own)" },

                // ---- Pointer modes ----
                { "BIND_HINT", "Bind mode: click the target block · right-click to cancel" },
                { "BIND_HINT_TRUE", "Binding to \"yes\": click the target block · right-click to cancel" },
                { "BIND_HINT_FALSE", "Binding to \"no\": click the target block · right-click to cancel" },

                // ---- Jump to the block's terminal (ADR-0021) ----
                { "WARN_JUMP_BLOCK_GONE", "Jump failed: the block is gone" },
                { "WARN_JUMP_NO_PLAYER", "Jump failed: the player is not in the world yet" },
                { "WARN_JUMP_FAILED", "Jump failed: the terminal is unavailable" },
                { "WARN_JUMP_NOT_TERMINAL", "Jump failed: the block has no terminal" },
                { "WARN_JUMP_NO_ANTENNA",
                  "Jump failed: the block is out of reach and no antenna is transmitting" },
                { "JUMP_HINT", "This block's terminal · the editor will close" },

                // ---- Event controller watch mode (ADR-0017, ADR-0020) ----
                { "WATCH_HINT",
                  "Watch mode: click the block the controller should watch · right-click to cancel" },
                { "WATCH_STATUS_THRESHOLD_FORMAT", "{0} · {1} {2}" },
                { "WATCH_STATUS_PLAIN_FORMAT", "{0}" },
                { "WATCH_STATUS_NO_EVENT", "No event selected" },
                { "WATCH_STATUS_AND_SUFFIX", " · AND" },
                { "WARN_WATCH_SELF", "Block not added: a controller cannot watch itself" },
                { "WARN_WATCH_DUPLICATE_FORMAT", "Block \"{0}\" is already watched by this controller" },
                { "WARN_WATCH_NO_EVENT", "Block not added: the controller has no event selected" },
                { "WARN_WATCH_EVENT_IGNORES_BLOCKS",
                  "Block not added: the selected event does not watch blocks" },
                { "WARN_WATCH_DIFFERENT_CONSTRUCT",
                  "Block not added: it is on another construct, and the controller only sees its own" },
                { "WARN_WATCH_NOT_ACCEPTED_FORMAT", "Block \"{0}\" does not fit the event \"{1}\"" },
                { "WARN_WATCH_CLIENT",
                  "Block not added: the controller's list is stored on the server" },
                { "WARN_WATCH_REJECTED",
                  "Block not added: the game did not accept the change to the controller's list" },
                { "WARN_WATCH_BLOCK_GONE", "Block not added: the block is gone" },
                { "WARN_WATCH_GROUP",
                  "Block not added: a controller watches individual blocks, not a group" },
                { "WARN_WATCH_TOO_MANY_FORMAT",
                  "Block not added: the controller already watches {0} blocks, which is the limit" },
                { "WARN_WATCH_REMOVE_REJECTED_FORMAT",
                  "Block \"{0}\" was not removed: the game did not accept the change to the "
                  + "controller's list" },
                { "WARN_WATCH_GAME_OWNED_FORMAT",
                  "Block \"{0}\" was chosen in the controller's terminal — remove it there, "
                  + "the editor does not change that list" },

                // ---- Action menu ----
                { "ACTION_MENU_TITLE_FORMAT", "Action for \"{0}\"" },
                { "ACTION_MENU_EMPTY", "No actions available" },
                { "ACTION_MENU_FAN_IN_FORMAT", "  ·  sources: {0}" },

                // ---- Write failures ----
                { "WARN_NO_FREE_CELL_FORMAT",
                  "Link not added: the toolbar of \"{0}\" has no free cell" },
                { "WARN_EVERY_PAGE_FORMAT",
                  "Link not added: \"{0}\" already stands on every page of the toolbar of "
                  + "\"{1}\" — the game does not allow one action twice on a page" },
                { "WARN_ACTION_GONE_FORMAT",
                  "Link not added: block \"{0}\" no longer has the action \"{1}\"" },
                { "WARN_NOT_DRIVABLE_FORMAT",
                  "Link not added: block \"{0}\" does not drive other blocks" },
                { "WARN_REJECTED",
                  "Link not added: the game did not accept the action into the toolbar" },
                { "WARN_BLOCK_GONE", "Link not added: the block is gone" },
                { "WARN_GROUP_GONE_FORMAT", "Link not added: the group \"{0}\" no longer exists" },
                { "WARN_REMOVE_REFUSED_FORMAT",
                  "Link not removed: the game did not allow changing the toolbar of block \"{0}\"" },

                // ---- Errors ----
                { "ERR_OPEN_SCREEN", "Could not open the editor" },
                { "ERR_NO_ACCESS", "No access to this block" }
            };
        }

        /// <summary>
        /// The Russian wording.
        ///
        /// The strings that were already Russian before localization existed are moved over
        /// unchanged, and only the handful that were written in English (the header, the counts,
        /// the modes menu, the search placeholder) are translated. Deliberate: a localization
        /// change must not also be a rewording, or the player cannot tell which of the two they
        /// are looking at.
        /// </summary>
        private static Dictionary<string, string> BuildRussian()
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // ---- Terminal controls ----
                { "BUTTON_TITLE", "Центр Управления Гридом" },
                { "BUTTON_TOOLTIP", "Открыть редактор логики грида" },
                { "SHOW_IN_VGS_TITLE", "Показывать в ЦУГ" },
                { "SHOW_IN_VGS_TOOLTIP",
                  "Рисовать этот блок на схеме грида. Выключение скрывает блок и его линии; "
                  + "связи продолжают работать" },
                { "SHOW_IN_VGS_ON", "Вкл" },
                { "SHOW_IN_VGS_OFF", "Выкл" },

                // ---- Editor chrome ----
                { "EDITOR_TITLE", "Центр Управления Гридом" },
                { "STATS_FORMAT", " // блоков: {0} · групп: {1} · связей: {2} · зум: {3}%" },
                { "CLOSE_BUTTON", "Закрыть" },
                { "EMPTY_GRID", "На этом гриде нет блоков для отображения" },
                { "LOADING", "Чтение блоков грида…" },
                { "NO_GRID", "Не удалось определить грид" },
                { "GRID_FALLBACK_NAME", "Грид" },

                // ---- Bottom bar ----
                { "SEARCH_PLACEHOLDER", "Поиск блока" },
                { "MODES_SHOW_HIDDEN", "Показать/скрыть игнорируемые" },
                { "MODES_SHOW_SUBGRIDS", "Показывать субгриды" },
                { "MODES_SHOW_GRID_BORDERS", "Показывать границы гридов" },
                { "MODES_HINT", "Режимы просмотра: показывать скрытые блоки" },
                { "SEARCH_HINT",
                  "Поиск блоков · Enter — к первому совпадению · «x» — сбросить · "
                  + "Ctrl+A / C / X / V — выделить, копировать, вырезать, вставить "
                  + "(буфер — только внутри поля)" },

                // ---- Pointer modes ----
                { "BIND_HINT", "Режим привязки: нажмите блок-получатель · ПКМ — отмена" },
                { "BIND_HINT_TRUE", "Привязка к «да»: нажмите блок-получатель · ПКМ — отмена" },
                { "BIND_HINT_FALSE", "Привязка к «нет»: нажмите блок-получатель · ПКМ — отмена" },

                // ---- Jump to the block's terminal ----
                { "WARN_JUMP_BLOCK_GONE", "Переход не выполнен: блок исчез" },
                { "WARN_JUMP_NO_PLAYER", "Переход не выполнен: игрок ещё не в мире" },
                { "WARN_JUMP_FAILED", "Переход не выполнен: терминал недоступен" },
                { "WARN_JUMP_NOT_TERMINAL", "Переход не выполнен: у блока нет терминала" },
                { "WARN_JUMP_NO_ANTENNA", "Переход не выполнен: блок далеко, антенна не передаёт" },
                { "JUMP_HINT", "Терминал этого блока · редактор закроется" },

                // ---- Event controller watch mode ----
                { "WATCH_HINT",
                  "Режим отслеживания: нажмите блок, который должен отслеживать контроллер · "
                  + "ПКМ — отмена" },
                { "WATCH_STATUS_THRESHOLD_FORMAT", "{0} · {1} {2}" },
                { "WATCH_STATUS_PLAIN_FORMAT", "{0}" },
                { "WATCH_STATUS_NO_EVENT", "Событие не выбрано" },
                { "WATCH_STATUS_AND_SUFFIX", " · И" },
                { "WARN_WATCH_SELF", "Блок не добавлен: контроллер не может отслеживать сам себя" },
                { "WARN_WATCH_DUPLICATE_FORMAT", "Блок «{0}» уже отслеживается этим контроллером" },
                { "WARN_WATCH_NO_EVENT", "Блок не добавлен: у контроллера не выбрано событие" },
                { "WARN_WATCH_EVENT_IGNORES_BLOCKS",
                  "Блок не добавлен: выбранное событие не следит за блоками" },
                { "WARN_WATCH_DIFFERENT_CONSTRUCT",
                  "Блок не добавлен: он на другом конструкте, а контроллер видит только свой" },
                { "WARN_WATCH_NOT_ACCEPTED_FORMAT", "Блок «{0}» не подходит для события «{1}»" },
                { "WARN_WATCH_CLIENT", "Блок не добавлен: список контроллера хранится на сервере" },
                { "WARN_WATCH_REJECTED",
                  "Блок не добавлен: игра не приняла изменение списка контроллера" },
                { "WARN_WATCH_BLOCK_GONE", "Блок не добавлен: блок исчез" },
                { "WARN_WATCH_GROUP",
                  "Блок не добавлен: контроллер следит за отдельными блоками, а не за группой" },
                { "WARN_WATCH_TOO_MANY_FORMAT",
                  "Блок не добавлен: у контроллера уже {0} отслеживаемых блоков — это предел" },
                { "WARN_WATCH_REMOVE_REJECTED_FORMAT",
                  "Блок «{0}» не убран: игра не приняла изменение списка контроллера" },
                { "WARN_WATCH_GAME_OWNED_FORMAT",
                  "Блок «{0}» выбран в терминале контроллера — уберите его там, редактор "
                  + "этот список не меняет" },

                // ---- Action menu ----
                { "ACTION_MENU_TITLE_FORMAT", "Действие для «{0}»" },
                { "ACTION_MENU_EMPTY", "Нет доступных действий" },
                { "ACTION_MENU_FAN_IN_FORMAT", "  ·  источников: {0}" },

                // ---- Write failures ----
                { "WARN_NO_FREE_CELL_FORMAT",
                  "Связь не добавлена: в тулбаре «{0}» нет свободной ячейки" },
                { "WARN_EVERY_PAGE_FORMAT",
                  "Связь не добавлена: «{0}» уже стоит на всех страницах тулбара «{1}» — игра "
                  + "не разрешает одно действие дважды на одной странице" },
                { "WARN_ACTION_GONE_FORMAT",
                  "Связь не добавлена: у блока «{0}» больше нет действия «{1}»" },
                { "WARN_NOT_DRIVABLE_FORMAT",
                  "Связь не добавлена: блок «{0}» не управляет другими блоками" },
                { "WARN_REJECTED", "Связь не добавлена: игра не приняла действие в тулбар" },
                { "WARN_BLOCK_GONE", "Связь не добавлена: блок исчез" },
                { "WARN_GROUP_GONE_FORMAT", "Связь не добавлена: группы «{0}» больше нет" },
                { "WARN_REMOVE_REFUSED_FORMAT",
                  "Связь не удалена: игра не разрешила изменить тулбар блока «{0}»" },

                // ---- Errors ----
                { "ERR_OPEN_SCREEN", "Не удалось открыть редактор" },
                { "ERR_NO_ACCESS", "Нет доступа к этому блоку" }
            };
        }
    }
}
