# Архитектура

Как устроен мод и почему именно так. Документ для тех, кто будет менять код; факты об API
и их проверка — в [02-feasibility.md](02-feasibility.md), сырые дампы API — в
[01-research/](01-research/), принятые решения — в [adr/](adr/), словарь понятий —
в [06-terminology.md](06-terminology.md).

> **Документ обязан соответствовать коду.** Если изменение делает диаграмму или правило
> неверными — диаграмма правится в том же изменении. Расхождение считается дефектом.

---

## 0. Принятые решения

Короткий список; полный текст и обоснование — в [adr/](adr/).

| ADR | Решение |
|---|---|
| [0008](adr/0008-mod-screen-api.md) | UI-стек: `MyModScreen` + экранное пространство мода (заменяет 0001) |
| [0009](adr/0009-screen-coordinate-space.md) | Раскладка — в клиентской области, отрисовка — в safe-GUI (исправляет 0008) |
| [0010](adr/0010-port-presentation-and-binding.md) | Карточка-метка, порты-стаканы под карточкой, режим привязки, fan-in разрешён |
| [0011](adr/0011-toolbar-is-the-connection.md) | Связь — элемент в тулбаре блока; «может управлять» = компонент `IMyToolbarProvider` + `ButtonPanel`; «да»/«нет» — две группы портов |
| [0012](adr/0012-layout-lives-on-the-block.md) | Раскладка схемы — на блоке (`MyModStorageComponent` + GUID, зарегистрированный SBC), не в таблице «`EntityId` → позиция» |
| [0013](adr/0013-show-in-vgs-flag.md) | Флаг видимости узла — на блоке; чекбокс «Show in Grid Workspace» у всех `IMyTerminalBlock` через `CustomControlGetter` |
| [0014](adr/0014-port-remove-button-and-port-look.md) | Кнопка «x» на порте удаляет все связи порта (сначала ячейка тулбара, потом ребро); точка — зеркало кнопки; подпись по центру и пропорционально зуму; рамка порта — цветом карточки |
| [0015](adr/0015-block-groups.md) | Группы блоков — узлы второго вида: рамка позади карточек, «Show in Grid Workspace» агрегируется по участникам, входы по правилу игры (пересечение действий, а не «общий предок»), связь — элемент `GroupAction` |
| [0016](adr/0016-group-drag.md) | Перенос группы — одним смещением по всем нарисованным участникам; ручка — полоса имени |
| [0017](adr/0017-event-controller-watch-ports.md) | Event controller: кнопка отслеживания, порты отслеживания (жёлтые, точечная линия в карточку цели — ADR-0046), отказ красной линией, описание режима на карточке (кнопка с ADR-0020 — колокольчик в общем ряду) |
| [0018](adr/0018-game-is-the-source-of-truth.md) | Список отслеживания читается из игры; хранилище мода — надстройка над игровым списком; игровые записи из редактора не удаляются |
| [0019](adr/0019-icons-atlas.md) | Иконки: отдельная PNG-текстура на иконку, `DrawPixels` в целых пикселях окна, иконка на любом зуме, откат на геометрию только при отсутствии текстуры |
| [0020](adr/0020-card-button-row.md) | Ряд кнопок карточки: все контролы карточки справа, в одну строку, одного размера, голые иконки; «T»/«F» → `lightbulb` Solid/Regular, «#» → `bell` Regular; каталог иконок со шлюзом |
| [0021](adr/0021-jump-to-block-terminal.md) | Четвёртая кнопка карточки (пятый вид) — переход в терминал блока (`share-from-square` Regular), первая в ряду; `Vgs.Game.TerminalJump` через `IMyGui`; диаметр кнопок 0,75 → 0,60 |
| [0022](adr/0022-view-state-lives-on-the-grid.md) | Состояние вида (камера, масштаб, режим «показывать скрытые») — на **гриде**, одним значением `"x y zoom hidden"`; открыть схему с другого кокпита того же грида — та же картина |
| [0023](adr/0023-workbench-bar-modes-and-search.md) | Компоновка окна: «Workspace» и статистика в шапке; нижняя панель — кнопка режимов с подменю «Show/hide invisible» и строка поиска блоков по правилу терминала; надпись «Показано X из Y» и постоянная подсказка убраны |
| [0024](adr/0024-text-field-caret-selection-clipboard.md) | Своё текстовое поле: каретка, выделение (якорь + каретка), Ctrl+A/C/X/V; движковое поле запрещено вайтлистом; рамка чинится устранением совпадения краёв (`Panel`) |
| [0025](adr/0025-clipboard-read-via-platform.md) | ~~Буфер обмена читается через `VRage.MyVRage.Platform.System.Clipboard`~~ — **вывод неверен**, игра отвергает эти типы как `is prohibited`; мод не загрузился. Заменён ADR-0027 | superseded by 0027 |
| [0026](adr/0026-clocks-in-the-mod.md) | Часы: `VgsSession.Frame` замирает на паузе, поэтому экран считает свои кадры в `OnUpdate`, статические помощники — попытки; `VgsLog.Throttled` на попытках; `UpdateOnPause` отклонён. **Пауза отменена ADR-0029**, правило осталось |
| [0028](adr/0028-block-damage-band.md) | Повреждение блока — красная полоса справа на карточке, ширина = 1 − целостность, прозрачность 50%, под рамкой/подписями/портами/кнопками; порога по величине нет, у группы полосы нет |
| [0029](adr/0029-editor-does-not-pause.md) | Редактор **не ставит игру на паузу**: пауза останавливала симуляцию, поэтому повреждение нельзя было увидеть растущим; часы экрана — свои кадры, `UpdateOnPause` не включается |
| [0030](adr/0030-block-category-descriptor.md) | Категория блока — enum + описатель (`ResolveCategory`, `Descriptor`: события + ключи метрик); специфика event controller уходит с `Node` в `ControllerState`, `IsEventController` производный |
| [0040](adr/0040-conveyor-network.md) | Конвейерная сеть — третье хранилище графа, выводимое из мира: узлы редукции — только блоки с карточкой, скрытые блоки и трубы проходятся насквозь; труба рисуется двумя линиями со скруглёнными изгибами под карточками; режим `Show conveyor network`, по умолчанию вкл |
| [0045](adr/0045-hotkey-opens-editor-from-cockpit.md) | Клавиша **M** — второй вход в тот же экран (`VgsEditorScreen.OpenFor`), опрос `IsNewKeyPressed` из сессионного компонента; работает только из кокпита и только когда клавиатурой не владеет другой экран (чат, игровой экран, терминал); привязку мод зарегистрировать не может |
| [0047](adr/0047-dotted-connection-lines.md) | Связь рисуется **плотными точками** 2 px через 1 px, линия отслеживания — 2 px через 2 px: у API нет антиалиасинга, и сплошной край читался как линия толще двух пикселей. Вес не меняется (точка — те же 2 × 2 px), меняется непрерывность; один растеризатор на оба вида (`LineRaster.Dotted` + `VgsScreenDraw.PathDots`), виды различаются ритмом (заменяет §2 ADR-0046) |
| [0046](adr/0046-curved-connection-lines.md) | Линия связи — плавная кривая толщиной ровно 2 px: маршрут остаётся ортогональным, а рисуется скруглённый путь (`EdgeRouting.Round`), растеризованный в пикселях safe-области (`Ui/LineRaster`); линия отслеживания — точки 2 px через 2 px (отменяет §6 ADR-0010 и пунктир §5 ADR-0017) |
| [0048](adr/0048-multi-node-selection.md) | Выделение — **набор** узлов (`Ui.NodeSelection`): Shift+клик переключает узел, клик по пустому месту очищает набор и начинает **пунктирную рамку 1 px** (`Ui.Marquee`, штрих и промежуток по 4 px при любом зуме), рамка выделяет накрытые карточки (не группы); перенос набора — одним смещением, как у группы (0016); **действие над набором не выполняется** |
| [0049](adr/0049-line-highlight.md) | **Подсветка линий** — второй слой, а не другая линия: связь и линия отслеживания получают под собой **белую сплошную линию** той же толщины (2 px, зазор 0) по тому же пути, видимую в зазорах самой линии; труба — **заливку** (третий выход `PipeRouter.Build`, полоса «зазор + две стенки», рельсы поверх). Условия: наведение на линию (одна) или выделение узла (все линии, где он любой из концов, в обе стороны). Допуск наведения — 4 device px, расстояние считается по скруглённому пути (`Ui.LineHit`), для трубы — по маршруту; порядок разрешения обратный порядку рисования (уточняет 0040, 0046, 0047, 0048) |
| [0027](adr/0027-clipboard-is-own-buffer-only.md) | Буфер обмена моду **недоступен** (вердикт игры), `TextClipboard` — только собственный буфер без ссылок на игру; правило «разрешение типа не выводится из разрешения пространства имён для корневого `VRage`»; самопроверка шлюза на вердиктах игры (заменяет ADR-0025) |
| [0001](adr/0001-ui-stack.md) | ~~`MyGuiScreenBase`~~ — отменено: игра отклоняет компиляцию |
| [0002](adr/0002-port-model.md) | Входы — из API, выходы — из курируемой таблицы |
| [0003](adr/0003-layering.md) | Слои; `Ui` не читает мир при рисовании |
| [0004](adr/0004-terminal-control-registration.md) | Регистрация контрола отложена в `UpdateAfterSimulation` |
| [0005](adr/0005-single-editor-screen.md) | Один экземпляр экрана редактора |
| [0006](adr/0006-verification-gates.md) | Шлюзы: компиляция, вайтлист, статическое ревью, офлайн-проверки |
| [0007](adr/0007-mutable-model-dtos.md) | `Vgs.Model` — изменяемые носители данных |


---

## 1. Общая картина

```
                    Space Engineers (C# / .NET Framework 4.x)
                                   │
              сам компилирует .cs из Data/Scripts/
                                   │
┌──────────────────────────────────▼──────────────────────────────────┐
│  VgsSessionComponent        [MySessionComponentDescriptor]           │
│  единственная точка входа: Init → BeforeStart → UpdateAfterSimulation│
│  → UnloadData                                                        │
└───────┬──────────────────────────────────────────────────────────────┘
        │
        ├──► Vgs.Core ──── VgsLog, VgsSession, VgsConstants, VgsText
        │                  (без игровой логики; всё логирование идёт сюда)
        │
        ├──► Vgs.Game ──── VgsTerminalControls   регистрирует кнопку в кокпите
        │                  EditorHotkey          клавиша M: схема из кокпита (ADR-0045)
        │                  GridGraphBuilder      читает грид → GridGraph
        │                  GridViewStore         состояние вида грида (камера, зум, режимы)
        │                  TextClipboard         собственный буфер поля поиска (системный недоступен)
        │                  TerminalJump          открывает терминал игры на блоке
        │                  EntityResolver        id сущности → живой блок (один поиск)
        │                  ConveyorProbe         линии конвейеров грида → пары блоков
        │                  EventControllerProbe  список отслеживания контроллера
        │                  EventControllerKeeper проигрывает список после загрузки
        │                  (единственное место, где трогаются живые сущности)
        │
        ├──► Vgs.Model ─── Node, Port, WatchTarget, Edge, GridGraph, BlockState,
        │                  BlockCategory + CategoryDescriptor, ControllerState,
        │                  StateWidget (kind, accent, reading),
        │                  BlockPortCatalog, ToolbarPlanner, NodeStateFormat, GridViewFormat,
        │                  ConveyorSegment, ConveyorLink, ConveyorNetwork,
        │                  BlockSearch, TextEditor
        │                  ЧИСТЫЕ ДАННЫЕ. Ни одного using Sandbox.*
        │
        └──► Vgs.Ui ────── VgsEditorScreen        экран: ввод, раскладка и режимы,
                           │                       читает только кэш модели
                           ├─ PortGeometry         геометрия карточки, портов и рамки группы
                           ├─ NodeSelection        набор выделенных узлов (ADR-0048)
                           ├─ Marquee              жест рамки выделения (ADR-0048)
                           ├─ GridFrames           рамка грида и вкладка её имени
                           ├─ EdgeRouting          маршрут линии и его скругление
                           ├─ LineRaster           путь линии в пиксельные точки, пунктир рамки
                           ├─ PipeRouter          две рельсы трубы и скругление изгибов
                           └─ VgsScreenDraw        единственное место, где
                                                   вызывается API отрисовки
```

Правило, которое нельзя нарушать: **`Vgs.Ui` не читает живые блоки во время рисования.**
Экран рисуется каждый кадр; обращение к `IMyTerminalBlock` — это поиск в мире, который
дороже любой арифметики и может упасть на уничтоженной сущности. Экран читает
`GridGraph`, обновляемый по таймеру.

Второе правило: **`Vgs.Ui` не обращается к `Sandbox.Graphics.*`.** У этой сборки нет
секции в вайтлисте скриптов, поэтому любой её тип — `MyGuiScreenBase`, `MyGuiManager`,
`MyScreenManager`, `MyGuiControl*` — роняет компиляцию мода в игре (см.
[ADR-0008](adr/0008-mod-screen-api.md)). Проверяется шлюзом `tools/check-whitelist.ps1`.

### 1.1. Диаграмма классов

Стрелка — направление зависимости («знает о»). `▲` — наследование от типа игры.

```mermaid
classDiagram
    direction TB

    class VgsSessionComponent {
        -bool _initialised
        -int _registrationAttempts
        +Init(MyObjectBuilder_SessionComponent)
        +BeforeStart()
        +UpdateAfterSimulation()
        #UnloadData()
    }

    class VgsSession {
        <<static>>
        +bool IsReady
        +bool IsDedicated
        +bool HasLocalPlayer
        +MySessionComponentBase Component
        ~Bind(component)
        ~Reset()
    }

    class VgsLog {
        <<static>>
        +string Tag
        +bool Verbose
        +Info(message)
        +Warn(message)
        +Error(message)
        +Error(message, exception)
        +Throttled(tag, message, intervalAttempts)
        +Throttled(tag, prefix, exception, intervalAttempts)
        +Debug(message)
        ~Reset()
    }

    class VgsConstants {
        <<static>>
        +string ModGuid
        +ushort NetworkChannelId
        +float BottomBarHeight
        +float BaseCellSize
        +float MinZoom
        +float MaxZoom
        +int MaxRenderedBlocks
        +int ViewSaveIntervalFrames
        +float SearchDimOpacity
    }

    class VgsText {
        <<static>>
        +VgsLanguage Language
        +bool UseLanguage(language)
        ~Reset()
        -string S(key)
        +string ButtonTitle
        +string CloseButton
        +string EditorTitle
        +string StatsFormat
        +string SearchPlaceholder
        +string ModesShowHidden
        +string ModesGlyph
        +string WatchSignLower
        +string WatchThresholdFormat
    }

    class VgsStrings {
        <<static>>
        +IReadOnlyDictionary~string,string~ English
        +IReadOnlyDictionary~string,string~ Russian
        -BuildEnglish() Dictionary
        -BuildRussian() Dictionary
    }

    class VgsLanguage {
        <<enum>>
        English
        Russian
    }

    class GameLanguage {
        <<static>>
        +bool Apply()
        -VgsLanguage Translate(MyLanguagesEnum)
    }

    VgsText --> VgsStrings : активная таблица
    VgsText ..> VgsLanguage
    GameLanguage ..> VgsText : UseLanguage
    GameLanguage ..> VgsLanguage

    class VgsTerminalControls {
        <<static>>
        +EnsureRegistered()
        +Unregister()
        -OnCustomControlGetter(block, controls)
        -CanOpenEditor(block) bool
        -OnButtonPressed(block)
    }

    class TerminalJump {
        <<static>>
        +TryOpen(entityId, out string failure) bool
    }

    class EditorHotkey {
        <<static>>
        -MyKeys OpenKey$
        +Poll()
    }

    class GridGraphBuilder {
        <<static>>
        +Build(IMyCubeGrid, bool includeHidden, bool includeSubgrids, bool includeConveyors) GridGraph
        +Refresh(GridGraph, IMyCubeGrid, bool includeHidden, bool includeSubgrids, bool includeConveyors)
        +RefreshState(GridGraph, IList~long~) int
        -TryReadIntegrity(long, out float) bool
        -CreateNode(IMyTerminalBlock) Node
        -UpdateNode(Node, IMyTerminalBlock)
        -TryReadActions(IMyTerminalBlock, List~string~, List~string~) bool
        -ReadActionSet(IMyTerminalBlock) ActionSet
        -BuildActionSet(IMyTerminalBlock, Type) ActionSet
        -TryAddAction(IMyTerminalBlock, ITerminalAction, List~string~, List~string~, List~string~, out bool) bool
        -CollectHiddenControls(IMyTerminalBlock, List~string~)
        -IsControlVisible(IMyTerminalControl, IMyTerminalBlock) bool
        -IsHiddenByControl(string, List~string~) bool
        -Contains(List~string~, string) bool
        -ActionName(ITerminalAction) string
        -ReadTerminalSystem(IMyCubeGrid) IMyGridTerminalSystem
        -SafeGridId(IMyTerminalBlock) long
        -ReadGroups(IMyCubeGrid) List~IMyBlockGroup~
        -SyncGroups(GridGraph, List~IMyBlockGroup~)
        -SyncOneGroup(GridGraph, IMyBlockGroup, string, List~long~, List~GroupMemberActions~, List~IMyTerminalBlock~, List~string~, List~string~, List~string~, ref int) GroupOutcome
        -ReadMemberActions(IMyTerminalBlock, List~string~, List~string~, List~string~)
        -SafeTypeKey(IMyTerminalBlock) string
        -IsFunctional(IMyTerminalBlock) bool
        -ReconcileToolbarEdges(GridGraph)
        -SyncCells(GridGraph, Node, IMyToolbar, ref int)
        -ResolveToolbar(long, int) IMyToolbar
        -SyncConveyors(GridGraph, bool)
    }

    class GridViewStore {
        <<static>>
        +TryRead(IMyCubeGrid, out Vector2, out float, out bool) bool
        +Save(IMyCubeGrid, Vector2, float, bool) bool
        -TryReadValue(IMyCubeGrid, out string) bool
    }

    class GridViewFormat {
        <<static>>
        +int FieldCount
        +Format(Vector2, float, bool) string
        +TryParse(string, out Vector2, out float, out bool) bool
    }

    class BlockSearch {
        <<static>>
        +char TermSeparator
        +SplitTerms(string, List~string~)
        +Matches(IList~string~, string, string) bool
    }

    class TextEditor {
        -int _maxLength
        -string _text
        -int _caret
        -int _anchor
        +string Text
        +int Length
        +int Caret
        +int Anchor
        +bool HasSelection
        +int SelectionStart
        +int SelectionEnd
        +string SelectedText
        +SetText(string)
        +Clear()
        +Insert(char) bool
        +Insert(string) bool
        +DeleteBackward() bool
        +DeleteForward() bool
        +MoveHorizontal(int, bool)
        +MoveTo(int, bool)
        +SelectAll()
    }

    class TextClipboard {
        <<static>>
        +string PasteText
        +Copy(string)
        +Release()
    }

    class NodeStateStore {
        <<static>>
        +IsShown(IMyTerminalBlock) bool
        +SetShown(IMyTerminalBlock, bool)
        +TryReadPosition(IMyTerminalBlock, out Vector2) bool
        +ApplyTo(Node, IMyTerminalBlock)
        +SavePosition(Node) bool
        +TryReadWatchList(IMyTerminalBlock, List~long~, out long) bool
        +SaveWatchList(IMyTerminalBlock, List~long~, long) bool
        +HasStorage(IMyTerminalBlock) bool
        -TryRead(IMyTerminalBlock, Guid, out string) bool
        -Write(IMyTerminalBlock, Guid, string) bool
    }

    class NodeStateFormat {
        <<static>>
        +FormatPosition(Vector2) string
        +TryParsePosition(string, out Vector2) bool
        +FormatWatchState(long, IList~long~) string
        +ParseWatchState(string, List~long~, out long) void
    }

    class EventControllerProbe {
        <<static>>
        +IsController(IMyTerminalBlock) bool
        +Describe(IMyTerminalBlock) string
        +SelectedEventName(IMyTerminalBlock) string
        +SelectedEventId(IMyTerminalBlock) long
        +TryReadGameWatchIds(IMyTerminalBlock, List~long~) bool
        +TryValidate(IMyTerminalBlock, IMyTerminalBlock, out WatchResult) bool
        +TryAddWatch(IMyTerminalBlock, IMyTerminalBlock) WatchResult
        +TryRemoveWatch(IMyTerminalBlock, IMyTerminalBlock) WatchResult
        +ForgetWatch(IMyTerminalBlock, long) bool
        +ReadOverlay(IMyTerminalBlock, List~long~, out long) bool
        +DropStaleWatchList(IMyTerminalBlock) bool
        +PollStaleSubscriptions()
        +ReleaseSubscriptions()
        +ReapplyWatchList(IMyTerminalBlock) int
        +IsServer() bool
    }

    class Subscription {
        <<nested in EventControllerProbe, private>>
        +IMyEventComponentWithGui Component
        +List~long~ Ids
    }

    class EventControllerKeeper {
        <<static>>
        +Install()
        +Uninstall()
        +Update()
    }

    class EntityResolver {
        <<static>>
        +TryResolveTerminalBlock(long, out IMyTerminalBlock) bool
        +TryResolveGrid(long, out IMyCubeGrid) bool
    }

    class WatchResult {
        <<enum, nested in EventControllerProbe>>
        Ok
        Duplicate
        NoEvent
        EventIgnoresBlocks
        DifferentConstruct
        NotAccepted
        ClientNotServer
        BlockGone
        Rejected
        GameOwned
    }

    class ToolbarProbe {
        <<static>>
        +Probe(IMyTerminalBlock) ToolbarLayout
    }

    class TerminalActionRunner {
        <<static>>
        +TryRun(Node, Port, long, out int) RunResult
        -TryRunBlock(long, Port, out int) RunResult
        -TryRunGroup(string, long, Port, out int) RunResult
        -TryReadGroupMembers(long, string, List~IMyTerminalBlock~) bool
        -TryFindAction(IMyTerminalBlock, string, out ITerminalAction) bool
        -IsFunctional(IMyTerminalBlock) bool
        -TryApply(ITerminalAction, IMyTerminalBlock) bool
    }

    class RunResult {
        <<enum, nested in TerminalActionRunner>>
        Ok
        BlockGone
        ActionGone
        GroupGone
        Refused
    }

    TerminalActionRunner ..> RunResult

    class ToolbarWriter {
        <<static>>
        +TryWrite(IMyTerminalBlock, Node, Port, IMyTerminalBlock, string, out ToolbarCell) WriteResult
        +TryWrite(IMyTerminalBlock, Node, Port, IMyTerminalBlock, string, ParameterSet, out ToolbarCell) WriteResult
        +TryWriteGroup(IMyTerminalBlock, Node, Port, string, string, out ToolbarCell) WriteResult
        +TryWriteGroup(IMyTerminalBlock, Node, Port, string, string, ParameterSet, out ToolbarCell) WriteResult
        +TryRewrite(IMyTerminalBlock, Node, ToolbarCell, string, ParameterSet, Func~CellContents,bool~) WriteResult
        +TryClear(IMyTerminalBlock, Node, ToolbarCell) bool
        +TryDescribe(IMyToolbar, int) CellContents
        -WriteItem(IMyTerminalBlock, Node, Port, MyToolbarItemInfo, ParameterSet, Func, Func, out ToolbarCell) WriteResult
        -ReadCell(IMyToolbar, int) CellContents
        -PageHolds(IMyToolbar, ToolbarLayout, int, Func) bool
        -ClearCell(IMyToolbar, int) bool
    }

    class ActionParameterDefaults {
        <<static>>
        +For(IMyTerminalBlock, string, IList~TypeCode~)$ ParameterSet
    }

    class ToolbarParameters {
        <<static>>
        +Definitions(IMyTerminalBlock, string) TypeCode[]
        +Read(IMyToolbar, int) ParameterSet
        +ToGame(ParameterSet) List~TerminalActionParameter~
        +Set(IMyToolbarChangeBuilder, int, MyToolbarItemInfo, ParameterSet) void
        +SameItem(CellContents, Edge) bool
    }

    class CellContents {
        +CellKind Kind
        +long BlockEntityId
        +string GroupName
        +string ActionId
        +ParameterSet Parameters
        +string Label
        +CellContents Empty$
        +bool IsEmpty
        +bool IsBlockAction
        +bool IsGroupAction
        +BlockAction(long, string, ParameterSet)$ CellContents
        +GroupAction(string, string, ParameterSet)$ CellContents
        +Other(string)$ CellContents
        +HoldsBlockAction(long, string, ParameterSet) bool
        +HoldsGroupAction(string, string, ParameterSet) bool
    }

    class ParameterSpec {
        +TypeCode Type
        +ParameterKind Kind
        +string Name
        +ParameterDefault DefaultFrom
        +string DefaultText
        +bool HasRange
        +float Min
        +float Max
        +string[] Choices
        +string[] ChoiceKeys
        +bool IsChoice
        +FromDeclared(TypeCode)$ ParameterSpec
        +Typed(TypeCode, ParameterKind, string, ParameterDefault)$ ParameterSpec
        +WithType(TypeCode) ParameterSpec
        +WithRange(float, float) ParameterSpec
        +WithDefault(string) ParameterSpec
        +WithDefault(ParameterDefault) ParameterSpec
        +AsChoice(string[], string[], string) ParameterSpec
        +WithSlots(int) ParameterSpec
        +int Slots
        +TryValidate(string, out ActionParameter, out ParameterError) bool
        +IsAllowedChoice(string) bool
        +ChoiceKeyOf(string) string
    }

    class ActionParameterHints {
        <<static>>
        +string RunAction
        +string RotateToAngleAction
        +For(string, IList~TypeCode~, VgsLanguage)$ ParameterSpec[]
        +At(string, int, TypeCode, VgsLanguage) ParameterSpec
        +Refine(string, int, ParameterSpec) ParameterSpec
        +IsDescribed(string) bool
        +HasKnownTypes(string) bool
        +Described$ IEnumerable~string~
    }

    class ActionParameterTable {
        <<static, generated>>
        +Get(string) Row
        +Ids$ IEnumerable~string~
    }

    class Row {
        +TypeCode[] Types
        +string[] NamesEn
        +string[] NamesRu
        +int[] GroupSizes
    }

    class ActionParameter {
        <<struct>>
        +TypeCode Code
        +string Text
        +bool IsEmpty
        +bool IsTextual
        +bool IsNumeric
        +TryCreate(TypeCode, string, out ActionParameter, out ParameterError)$ bool
        +FromValue(TypeCode, object)$ ActionParameter
        +Describe() string
    }

    class ParameterSet {
        +ParameterSet None$
        +int Count
        +bool IsEmpty
        +ActionParameter Item~int~
        +string Signature
        +string Display
        +Of(IList~ActionParameter~)$ ParameterSet
        +SameAs(ParameterSet) bool
    }

    class InputBar {
        +Port Port
        +ParameterSet Parameters
        +string Label
        +int Slot
        +bool HasParameters
    }

    class ParameterForm {
        +int Count
        +int Focus
        +ParameterError Error
        +int ErrorIndex
        +string ActionCaption
        +ParameterForm(IList~TypeCode~, ParameterSet)
        +ParameterForm(string, string, VgsLanguage, IList~TypeCode~, ParameterSet)
        +TypeAt(int) TypeCode
        +SpecAt(int) ParameterSpec
        +ValueAt(int) string
        +EditorAt(int) TextEditor
        +LabelAt(int) string
        +SetLabel(int, string) void
        +HintAt(int) string
        +SetHint(int, string) void
        +FocusAt(int) void
        +FocusNext() void
        +FocusPrevious() void
        +TryBuild(out ParameterSet) bool
    }

    class CellKind {
        <<enum>>
        Empty
        BlockAction
        GroupAction
        Other
    }

    class ToolbarPlanner {
        <<static>>
        +Plan(ToolbarLayout, PortGroup, Func~int,CellContents~, Func~int,bool~, out int) ToolbarPlacement
        +GroupOfCell(ToolbarLayout, int) PortGroup
    }

    class ToolbarLayout {
        +int ToolbarIndex
        +int SlotCount
        +int PageCount
        +bool YesNo
        +ToolbarLayout None$
        +int CellCount
        +bool CanDrive
        +SlotFor(PortGroup) int
        +CellIndex(int, int) int
        +PageOf(int) int
        +SlotOf(int) int
    }

    class ToolbarCell {
        +int ToolbarIndex
        +int Index
        +ToolbarCell None$
        +bool IsValid
        +SameAs(ToolbarCell) bool
    }

    class VgsEditorScreen {
        -VgsEditorScreen _openEditor$
        -GridGraph _graph
        -IMyCubeGrid _grid
        -Vector2 _camera
        -float _zoom
        -bool _showHidden
        -Vector2 _savedCamera
        -float _savedZoom
        -bool _savedShowHidden
        -int _lastViewSaveFrame
        -Node _draggedNode
        -NodeSelection _selection
        -List~Node~ _dragCards
        -Vector2 _selectionDragAnchor
        -Vector2 _selectionDragLast
        -Marquee _marquee
        -Node _draggedGroup
        -Vector2 _groupDragAnchor
        -Vector2 _groupDragLast
        -Node _bindSource
        -Port _bindPort
        -PortGroup _bindGroup
        -Node _watchSource
        -int _watchErrorUntilFrame
        -Node _hoverButton
        -CardButtonKind _hoverButtonKind
        -Node _hoverPowerSwitch
        -Edge _hoverEdge
        -ConveyorLink _hoverConveyor
        -Node _hoverWatchController
        -WatchTarget _hoverWatchTarget
        -Node _hoverWatchRemoveNode
        -WatchTarget _hoverWatchRemoveTarget
        -Node _menuTarget
        -ActionMenuMode _menuMode
        -Node _rightClickNode
        -Vector2 _rightClickAnchor
        -List~Port~ _menuRows
        -List~string~ _menuLabels
        -List~Vector4~ _route
        -List~Vector4~ _pipeBuffer
        -List~Vector4~ _pipeFillBuffer
        -Node _hoverRemoveNode
        -Port _hoverRemovePort
        -List~Edge~ _removeBuffer
        -float _portLabelHeight
        -string _statusText
        -string _warningText
        -bool _closeHot
        -bool _modesMenuOpen
        -bool _modesHot
        -bool _modesRowHot
        -bool _searchHot
        -bool _searchClearHot
        -TextEditor _searchEditor
        -List~string~ _searchTerms
        -int _screenFrame
        +bool IsEditorOpen$
        +OpenFor(IMyCockpit)$
        +CloseIfOpen()$
        #OnOpened()
        #OnUpdate(bool hasFocus)
        #OnHandleInput()
        #OnDraw(float transitionAlpha)
        #OnCancelling() bool
        #OnClosed(bool isUnloading)
        -HandleCanvasInput(Vector2) void
        -HandleSelectionPress(Node, Vector2) void
        -HandleMarqueeInput(Vector2) void
        -FinishMarquee() void
        -SaveCardPositions(List~Node~) void
        -DrawMarquee(Vector2, Vector2) void
        -PanWhileHeld(bool, Vector2, Vector2, Vector2) bool
        -PanWithMiddleButton(Vector2, Vector2, Vector2) bool
        -ClearHoverTargets() void
        -ResetPointerOverlay() void
        -ClearHoverHighlights() void
        -RefreshNodeState() void
        -CollectVisibleNodeIds(List~long~) void
        -IsCardOnCanvas(Node, Vector2, Vector2) bool
        -SaveGroupPositions(Node) void
        -HandleBindInput(Vector2) void
        -HandleWatchInput(Vector2) void
        -BeginWatch(Node) void
        -CancelWatch() void
        -TryAttachWatch(Node) void
        -RemoveWatchTarget(Node, WatchTarget) void
        -RefuseWatch(string) void
        -DescribeWatchFailure(WatchResult, IMyTerminalBlock, Node) string
        -HandleMenuInput(Vector2) void
        -TryRunManualAction(Port) void
        -OpenManualActionMenu(Node, Vector2) void
        -DismissActionMenu() void
        -GroupGridEntityId(Node) long
        -DescribeManualFailure(RunResult, Node, Port) string$
        -HandleManualActionClick(Vector2, Vector2, bool) bool
        -BeginBind(Node, PortGroup) void
        -CancelBind() void
        -OpenActionMenu(Node, Vector2, ActionMenuMode) void
        -CloseActionMenu() void
        -TryCompleteBind(Port) bool
        -RemovePortConnections(Node, Port) void
        -ReadCellState(IMyTerminalBlock, Edge) CellState
        -WarnRemoveRefused(Node, ref long) void
        -DescribeWriteFailure(WriteResult, Node, Node, Port) string
        -Warn(string) void
        -ValidateInteractionState() void
        -DropNodeReferences() void
        -RebuildGraph() void
        -RememberSavedView() void
        -SaveView() void
        -SaveViewIfChanged() void
        -HandleSearchKeyboard() void
        -HandleBarInput(Vector2) bool
        -ToggleShowHidden() void
        -SetSearchQuery(string) void
        -ClearSearch() void
        -AppendSearchChar(char) void
        -BackspaceSearch() void
        -IsDimmed(Node) bool
        -AlphaOf(Node) float
        -MaxAlpha(Node, Node) float
        -Fade(Color, float) Color$
        -JumpToFirstMatch() void
        -ScreenToGraph(Vector2, Vector2) Vector2
        -HitTest(Vector2) Node
        -HitTestCardButton(Vector2, out CardButtonKind) Node
        -HitTestWatchRemoveButton(Vector2, out WatchTarget) Node
        -HitTestRemoveButton(Vector2, out Port) Node
        -GetMenuRect(out Vector2, out Vector2) void
        -GetModesButtonRect(out Vector2, out Vector2) void$
        -GetSearchFieldRect(out Vector2, out Vector2) void$
        -TrySearchClearRect(out Vector2, out Vector2) bool$
        -GetModesMenuRect(out Vector2, out Vector2) void$
        -GetModesRowRect(out Vector2, out Vector2) void$
        -DrawGroupFrames(Vector2, Vector2) void
        -DrawGroupPorts(Vector2, Vector2) void
        -DrawNodes(Vector2, Vector2) void
        -DrawNode(Node, Vector2, Vector2, Vector2) void
        -DrawPortBars(Node, Color, float, Vector2, Vector2) void
        -DrawPortBar(Node, Port, Color, float, Vector2, Vector2) void
        -DrawWatchBars(Node, Color, float, Vector2, Vector2) void
        -DrawWatchLines(Vector2, Vector2) void
        -DrawPendingWatchLine(Vector2, Vector2) void
        -TryBuildEdgeRoute(Edge, Vector2, out Node, out Node) bool
        -TryBuildWatchRoute(Node, WatchTarget, Vector2, out Node) bool
        -TryBuildConveyorRoute(ConveyorLink, Vector2, out Node, out Node) bool
        -IsEdgeHighlighted(Edge, Node, Node) bool
        -IsWatchHighlighted(Node, WatchTarget, Node) bool
        -IsConveyorHighlighted(ConveyorLink, Node, Node) bool
        -UpdateHoveredLine(Vector2, Vector2) void
        -TryHitWatchLine(Vector2, Vector2, Vector2, float) bool
        -TryHitEdge(Vector2, Vector2, Vector2, float) bool
        -TryHitConveyor(Vector2, Vector2, Vector2, float) bool
        -RoundedPath() List~Vector2~
        -DrawDottedRoute(Color, float, bool, Vector2, Vector2) void
        -DrawPortRemoveButton(Vector2, Color, float, bool, Vector2, Vector2) void
        -PortLabelHeight float
        -DrawEdges(Vector2, Vector2) void
        -DrawConveyors(Vector2, Vector2) void
        -PipeAnchor(Node, bool, out Vector2) bool$
        -DrawRoundedRoute(Color, float, bool, Vector2, Vector2) void
        -DrawPendingLine(Vector2, Vector2) void
        -DrawActionMenu() void
        -DrawModesButton() void
        -DrawModesMenu() void
        -DrawSearchField() void
        -DrawCardButtons(Node, float, Vector2, Vector2) void
        -DrawCardButton(Node, CardButtonKind, Vector2, float, Vector2, Vector2) void
        +IconOf(CardButtonKind) VgsIcon
        +GlyphOf(CardButtonKind) string
        -TryJumpToTerminal(Node) void
        -GetStatusText() string
        -ResolveGridName()
    }

    class CellState {
        <<enum, nested in VgsEditorScreen>>
        HoldsThis
        HoldsOther
        Unreadable
    }

    VgsEditorScreen ..> CellState : классифицирует ячейку тулбара

    class ActionMenuMode {
        <<enum, nested in VgsEditorScreen>>
        None
        Bind
        Manual
    }

    VgsEditorScreen ..> ActionMenuMode : чем открыто меню действий

    class PointerGesture {
        <<static>>
        +ExceedsClickSlop(Vector2, float, float) bool
    }

    VgsEditorScreen --> PointerGesture : клик или перетаскивание (ADR-0041)

    class NodeSelection {
        -HashSet~Node~ _nodes
        -HashSet~long~ _memberIds
        -List~Node~ _stale
        +int Count
        +bool IsEmpty
        +Contains(Node) bool
        +Clear()
        +Select(Node)
        +Add(Node) bool
        +Remove(Node) bool
        +Toggle(Node) bool
        +SelectInRect(GridGraph, Vector2, Vector2, bool)
        +CollectDragCards(GridGraph, List~Node~)
        +RemoveMissing(GridGraph)
    }

    VgsEditorScreen --> NodeSelection : набор выделенных узлов (ADR-0048)

    class Marquee {
        <<struct>>
        -Vector2 _anchor
        -Vector2 _current
        -bool _armed
        -bool _active
        -bool _additive
        +bool IsArmed
        +bool IsActive
        +bool Additive
        +Vector2 Anchor
        +Vector2 Current
        +Begin(Vector2, bool)
        +Move(Vector2, float, float, float)
        +Cancel()
        +Normalize(out Vector2, out Vector2)
    }

    VgsEditorScreen --> Marquee : рамка выделения (ADR-0048)

    class PortGeometry {
        <<static>>
        +CardRect(Node, out Vector2, out Vector2)
        +CardHeight(Node) float
        +TryBodyRect(Node, out Vector2, out Vector2) bool
        +TryGroupFrame(Node, GridGraph, out Vector2, out Vector2) bool
        +TryGroupTitleRect(Node, out Vector2, out Vector2) bool
        +HitGroupTitle(Node, Vector2) bool
        +LayoutGroupFrames(GridGraph)
        +LayoutGroupFrame(GridGraph, Node)
        +LayoutFramesContaining(GridGraph, Node)
        +HasGroupButtons(Node) bool
        +MaxCardButtonCount int
        +CardButtonCount(Node) int
        +HasCardButton(Node, CardButtonKind) bool
        +CardButtonIndex(Node, CardButtonKind) int
        +TryCardButtonAt(Node, int, out CardButtonKind) bool
        +GetCardButtons(Node, CardButtonKind[]) int
        +TryGroupOf(CardButtonKind, out PortGroup) bool
        +CardButtonRectAt(Node, int, int, float, out Vector2, out Vector2)
        +TryCardButtonRect(Node, CardButtonKind, float, out Vector2, out Vector2) bool
        +TryCardButton(Node, CardButtonKind, float, out Vector2) bool
        +TryCardButtonRow(Node, float, out Vector2, out Vector2) bool
        +TryCardTextSpan(Node, float, out float, out float) bool
        +HasPowerSwitch(Node) bool
        +HasStateWidget(Node) bool
        +HasStateColumn(Node) bool
        +StateColumnWidth(Node) float
        +TryStateColumnRect(Node, float, out Vector2, out Vector2) bool
        +PowerSwitchInkHeight(Node) float
        +PowerSwitchInkWidth(Node) float
        +PowerSwitchInkAspect float
        +TryPowerSwitchRect(Node, float, out Vector2, out Vector2) bool
        +HitPowerSwitch(Node, Vector2, float) bool
        +TryStateWidgetArea(Node, float, out Vector2, out Vector2) bool
        +StateWidgetPadding(StateWidgetKind, out float, out float)
        +TryStateWidgetBar(Node, float, int, out Vector2, out Vector2, out Vector2, out Vector2) bool
        +TryStateWidgetClock(Node, float, out Vector2, out float) bool
        +TryStateWidgetBolt(Node, float, out Vector2, out float) bool
        +BoltInkAspect float
        +StateWidgetHandTip(Vector2, float, float, float, float) Vector2
        +TryDamageRect(Node, out Vector2, out Vector2) bool
        +FitDamageBand(ref Vector2, ref Vector2, Vector2, Vector2, float)
        +CardTextPaddingRatio float
        +HitCardButton(Node, Vector2, float, out CardButtonKind) bool
        +TryWatchButton(Node, float, out Vector2) bool
        +TryWatchBarRect(Node, WatchTarget, out Vector2, out Vector2) bool
        +TryWatchDot(Node, WatchTarget, float, out Vector2) bool
        +TryWatchRemoveButton(Node, WatchTarget, float, out Vector2) bool
        +TryWatchLabelRect(Node, WatchTarget, float, out Vector2, out Vector2) bool
        +HitWatchRemoveButton(Node, Vector2, float) WatchTarget
        +TryCardCentre(Node, out Vector2) bool
        +DrawnWatchCount(Node) int
        +ShouldDrawWatchLine(Node, WatchTarget) bool
        +WatchStackHeight(Node) float
        +TryPortBarRect(Node, Port, out Vector2, out Vector2) bool
        +TryPortDot(Node, Port, float aspectX, out Vector2) bool
        +TryPortRemoveButton(Node, Port, float aspectX, out Vector2) bool
        +HitPortRemoveButton(Node, Vector2, float aspectX) Port
        +TryPortLabelRect(Node, Port, float aspectX, out Vector2, out Vector2) bool
        +CentredTextTop(float boxTop, float boxHeight, float textHeight) float
        +TryPendingSourceDot(Node, Port, float aspectX, out Vector2) bool
        +OutputStackHeight(Node) float
        +InputStackHeight(Node) float
        +TotalHeight(Node) float
        +DrawnPortCount(Node, PortDirection) int
        +HitNode(Node, Vector2) bool
        +Contains(Vector2, Vector2, Vector2) bool
    }

    class EdgeRouting {
        <<static>>
        +Build(Vector2 from, Vector2 to, float gutter, List~Vector4~)
        +BuildSelfLoop(Vector2 from, Vector2 to, Vector2 cardTopLeft, Vector2 cardSize, float gutter, List~Vector4~)
        +Round(IList~Vector4~ route, Vector2 pixelsPerUnit, List~Vector2~)
        -AddBend(List~Vector2~, Vector2 from, Vector2 to, Vector2 corner, Vector2 pixelsPerUnit)
    }

    class LineRaster {
        <<static>>
        +Dotted(IList~Vector2~ path, PixelTransform, float dotPixels, float gapPixels, Vector4 clipPixels, List~Vector4~)
        +DashedRect(Vector2 topLeft, Vector2 size, PixelTransform, float dashPixels, float gapPixels, float thicknessPixels, Vector4 clipPixels, List~Vector4~)
        -AddDashRun(...) int
        -AddDot(...)
        -ToPixel(float) int
    }

    class PixelTransform {
        <<struct>>
        +Vector2 Scale
        +Vector2 Offset
        +FromClientTransform(...) PixelTransform$
        +bool IsUsable
        +ToPixels(Vector2) Vector2
    }

    class PipeRouter {
        +Build(IList~Vector4~ route, float cornerRadius, float halfWidth, float aspect, float stepLength, List~Vector4~) int
        +Build(IList~Vector4~ route, float cornerRadius, float halfWidth, float aspect, float stepLength, List~Vector4~ rails, List~Vector4~ fill) int
        -AddRail(...)
        -AddArc(...)
        -ArcPoint(...)
        -Direction(...)
        -Offset(...)
        -AxisScale(...)
        -AddSegment(...)
    }

    class LineHit {
        <<static>>
        +DistancePixels(IList~Vector2~ path, Vector2 point, Vector2 pixelsPerUnit) float
        +IsWithin(IList~Vector2~ path, Vector2 point, Vector2 pixelsPerUnit, float tolerancePixels) bool
        +RouteDistancePixels(IList~Vector4~ route, Vector2 point, Vector2 pixelsPerUnit) float
        +RouteWithin(IList~Vector4~ route, Vector2 point, Vector2 pixelsPerUnit, float tolerancePixels) bool
        +RouteBoundsReach(IList~Vector4~ route, Vector2 point, Vector2 pixelsPerUnit, float tolerancePixels) bool
        -SegmentDistance(Vector2 a, Vector2 b, Vector2 point) float
        -ToPixels(Vector2, Vector2) Vector2
        -HasPixels(Vector2) bool
    }

    class ConveyorSegment {
        <<struct>>
        +long A
        +long B
    }

    class ConveyorLink {
        +long SourceEntityId
        +long TargetEntityId
        +SameAs(ConveyorLink) bool
    }

    class ConveyorNetwork {
        <<static>>
        +Build(IList~ConveyorSegment~, IList~long~, IList~ConveyorLink~) int
        -AddNeighbour(...)
    }

    class ConveyorProbe {
        <<static>>
        +TryReadGrid(IMyCubeGrid, List~ConveyorSegment~) bool
        -ResolveEndpoint(IMyCubeGrid, SerializableVector3I) long
    }

    class VgsScreenDraw {
        <<static>>
        +bool ResourcesReady
        +float ClientAspect
        +float ClientWidth
        +EnsureResources()
        +Release()
        +SyncScreenSpace()
        +Rect(Vector2 pos, Vector2 size, Color c)
        +RectClipped(Vector2, Vector2, Vector2 clip, Vector2 clipSize, Color)
        +Border(Vector2 pos, Vector2 size, Color c, float t)
        +BorderClipped(Vector2, Vector2, Vector2 clip, Vector2 clipSize, Color, float)
        +SegmentPixels(Vector2 from, Vector2 to, float thicknessPixels, Vector2 clip, Vector2 clipSize, Color)
        +PathDots(IList~Vector2~ path, float dotPixels, float gapPixels, Vector2 clip, Vector2 clipSize, Color)
        +DashedRect(Vector2 pos, Vector2 size, Vector2 clip, Vector2 clipSize, Color, float dashPixels, float gapPixels, float thicknessPixels)
        +PixelTransform PixelSpace
        +Disc(Vector2 center, float r, Vector2 clip, Vector2 clipSize, Color)
        +ThickSegment(Vector2 from, Vector2 to, float t, Vector2 clip, Vector2 clipSize, Color)
        +Ring(Vector2 center, float r, float t, int segments, Vector2 clip, Vector2 clipSize, Color)
        +Text(string text, Vector2 pos, float scale, Color c)
        +Text(string text, Vector2 pos, float scale, Color c, float maxWidth)
        +TextCentered(string text, Vector2 center, float scale, Color c)
        +TextRight(string text, Vector2 rightTop, float scale, Color c)
        +Measure(string text, float scale) Vector2
        +TryCursor(out Vector2 clientArea) bool
    }

    class GridGraph {
        +int MaxPortsPerSide$
        +long GridEntityId
        +int NodeCount
        +int GroupCount
        +int EdgeCount
        +int ConveyorCount
        +IList~Node~ Nodes
        +IList~Node~ Groups
        +IList~Edge~ Edges
        +IList~Edge~ Lines
        +IList~ConveyorLink~ Conveyors
        +AddNode(Node) Node
        +RemoveNode(long) bool
        +GetGroup(string) Node
        +ContainsGroup(string) bool
        +ContainsNode(Node) bool
        +GetTargetNode(Edge) Node
        +SyncGroup(string, List~long~, IList~GroupMemberActions~) Node
        +OffsetGroupMembers(Node, Vector2) int
        +RemoveGroup(string) bool
        +HasWatch(Node, long) bool
        +TryAddWatch(Node, long, string) bool
        +RemoveWatch(Node, long) bool
        +CollectWatchedIds(Node, List~long~) void$
        +SyncWatched(Node, List~long~, Func~long,string~) void$
        +MergeWatchIds(List~long~, List~long~, List~long~) void$
        +PruneAdoptedWatchIds(List~long~, List~long~) bool$
        -RemoveEdgesToMissingGroupPorts(string)
        +TryConnect(long, string, long, string, ToolbarCell) bool
        +TryConnectToGroup(long, string, string, string, ToolbarCell) bool
        +IsCellValidFor(Node, Port, ToolbarCell) bool$
        +FindEdgeInCell(long, ToolbarCell) Edge
        +HasEdge(long, string, long, string) bool
        +HasEdgeToGroup(long, string, string, string) bool
        +CountEdgesToInput(long, string) int
        +CountEdgesToInput(Node, string) int
        +CountEdgesFromOutput(long, string) int
        +CollectEdgesForPort(long, string, PortDirection, List~Edge~) void
        +CollectEdgesForPort(Node, string, PortDirection, List~Edge~) void
        +CollectEdgesForBar(Node, InputBar, List~Edge~) void
        +TryReplaceEdgeParameters(Edge, ParameterSet) bool
        +TryConnect(long, string, long, string, ToolbarCell, ParameterSet) bool
        +TryConnectToGroup(long, string, string, string, ToolbarCell, ParameterSet) bool
        +RemoveEdge(Edge) bool
        +ClearConveyors() void
        +AddConveyor(ConveyorLink) void
        +ApplyIntegrity(long, float) bool
        +ApplySwitch(long, bool, bool) bool
        +LayoutPorts()
        +LayoutUnplaced()
    }

    class NodeKind {
        <<enum>>
        Block
        Group
    }

    class GroupMemberActions {
        +string TypeKey
        +bool IsFunctional
        +IList~string~ ActionIds
        +IList~string~ ActionNames
        +IList~string~ EnabledActionIds
    }

    class Node {
        +long EntityId
        +NodeKind Kind
        +string GroupName
        +string SubtypeId
        +string DisplayName
        +string TypeDisplayName
        +Vector2 Position
        +Vector2 FrameSize
        +bool Placed
        +bool IsGroup
        +Vector3I GridPosition
        +long GridEntityId
        +bool HasTerminal
        +ToolbarLayout Toolbar
        +bool CanDriveBlocks
        +bool CanBeEnabled
        +BlockCategory Category
        +StateWidgetKind Widget
        +ControllerState Controller
        +bool IsEventController
        +BlockState State
        +List~long~ Members
        +List~Port~ Actions
        +List~Port~ Events
        +List~InputBar~ InputBars
        +Port PrimaryEvent
        +SetFrame(Vector2, Vector2)
        +PrimaryEventFor(PortGroup) Port
        +FindAction(key) Port
        +FindEvent(key) Port
        +FindInputBar(key, ParameterSet) InputBar
    }

    class ControllerState {
        +string StatusText
        +List~WatchTarget~ Watched
        +bool IsFull
        +WatchTarget FindWatch(long)
    }

    class BlockCategory {
        <<enum>>
        Unknown
        EventController
        Cockpit
        Timer
        Sensor
        Light
    }

    class CategoryDescriptor {
        +string[] Events
        +string[] MetricKeys
        +StateWidgetKind Widget
        +int MetricCount
        +bool HasEvents
    }

    class StateWidget {
        <<static>>
        +BarCount(StateWidgetKind) int
        +MetricCount(StateWidgetKind) int
        +IsBar(StateWidgetKind) bool
        +ShowsWidget(StateWidgetKind, bool, BlockPower, bool) bool
    }

    class Port {
        +string Key
        +string DisplayName
        +PortKind Kind
        +PortDirection Direction
        +PortGroup Group
        +float Anchor
        +int Slot
        +IList~TypeCode~ ParameterTypes
        +bool HasParameters
    }

    class BlockState {
        +float Integrity
        +BlockPower Power
        +WidgetAccent Accent
        +bool WidgetActive
        +float[] Metrics
        +int MetricCount
        +bool IsDamaged
        +bool CanToggle
        +SetIntegrity(float)
        +SetPower(bool, bool)
        +GetMetric(int) float
        +SetMetric(int, float)
        +SetWidget(WidgetReading)
    }

    class BlockPower {
        <<enum>>
        Unknown
        On
        Off
        Unavailable
    }

    class StateWidgetKind {
        <<enum>>
        None
        FillBar
        Battery
        Clock
        Production
    }

    class WidgetAccent {
        <<enum>>
        None
        Recharge
        Discharge
    }

    class WidgetReading {
        +bool Active
        +float First
        +float Second
        +WidgetAccent Accent
    }

    class WatchTarget {
        +long EntityId
        +string DisplayName
        +int Slot
    }

    class Edge {
        +long SourceEntityId
        +string SourcePortKey
        +long TargetEntityId
        +string TargetGroupName
        +string TargetPortKey
        +ToolbarCell Cell
        +ParameterSet Parameters
        +bool TargetsGroup
        +ToGroup(long, string, string, string, ToolbarCell, ParameterSet)$ Edge
        +WithParameters(ParameterSet) Edge
        +SameAs(Edge) bool
        +SameCellAs(Edge) bool
    }

    class BlockPortCatalog {
        <<static>>
        +string GenericEventKey
        +string TrueKeySuffix
        +string FalseKeySuffix
        +ResolveCategory(subtypeId) BlockCategory
        +Descriptor(BlockCategory) CategoryDescriptor
        +FillActions(Node, IList~string~, IList~string~, IList~TypeCode[]~)
        +FillEvents(Node)
        +ComputeGroupActionIds(IList~GroupMemberActions~, List~string~, List~string~, List~TypeCode[]~)
        +EventKey(baseKey, PortGroup) string
        +EventDisplayName(baseKey, PortGroup) string
        +Humanize(key) string
        +ActionBelongsToControl(actionId, controlId) bool
        +IsActionHiddenBy(actionId, IList~string~) bool
    }

    class MySessionComponentBase {
        <<game>>
    }
    class MyModScreen {
        <<game>>
    }
    class MyScreenSpace {
        <<game>>
    }
    class IMyCockpit {
        <<game>>
    }
    class IMyCubeGrid {
        <<game>>
    }
    class IMyTerminalBlock {
        <<game>>
    }
    class IMyToolbarProvider {
        <<game>>
    }
    class IMyToolbar {
        <<game>>
    }
    class IMyBlockGroup {
        <<game>>
    }
    class IMyGui {
        <<game>>
    }
    class IMyGridTerminalSystem {
        <<game>>
    }
    class MyToolbarItemInfo {
        <<game>>
    }

    VgsSessionComponent --|> MySessionComponentBase : наследует
    VgsEditorScreen --|> MyModScreen : наследует
    VgsScreenDraw --> MyScreenSpace : рисует через

    VgsSessionComponent --> VgsSession
    VgsSessionComponent --> VgsTerminalControls
    VgsSessionComponent --> VgsLog
    VgsSessionComponent --> GameLanguage : язык до регистрации контролов
    VgsSessionComponent --> VgsText : Reset на выгрузке

    VgsTerminalControls --> VgsEditorScreen : OpenFor
    VgsTerminalControls --> IMyCockpit
    VgsTerminalControls --> VgsSession

    VgsSessionComponent --> EditorHotkey : опрос клавиши в UpdateAfterSimulation
    EditorHotkey --> VgsEditorScreen : IsEditorOpen, OpenFor
    EditorHotkey --> IMyCockpit : игрок сидит в кокпите
    EditorHotkey --> IMyControllableEntity : ControlledObject
    EditorHotkey --> IMyGui : ChatEntryVisible, ActiveGamePlayScreen, GetCurrentScreen
    EditorHotkey --> IMyInput : IsNewKeyPressed, модификаторы
    EditorHotkey --> VgsSession : есть ли локальный игрок
    EditorHotkey ..> MyKeys : константа клавиши

    VgsEditorScreen --> GridGraph
    VgsEditorScreen --> GridGraphBuilder : Build, Refresh (структура) и RefreshState (2 Гц)
    VgsEditorScreen --> ToolbarWriter : запись и очистка связи
    VgsEditorScreen --> TerminalActionRunner : ручное действие (ADR-0041)
    VgsEditorScreen --> NodeStateStore : сохранение позиции
    VgsEditorScreen --> GridViewStore : камера, масштаб, режим
    VgsEditorScreen --> BlockSearch : правило фильтра карточек
    VgsEditorScreen --> Node
    VgsEditorScreen --> PortGeometry : вся геометрия
    VgsEditorScreen --> EdgeRouting : маршрут линии и его скругление (ADR-0046)
    VgsEditorScreen --> PipeRouter : две рельсы трубы (ADR-0040)
    VgsEditorScreen --> IMyCubeGrid
    VgsEditorScreen --> IMyInput : TextInput и клавиши поиска (ADR-0023)
    VgsEditorScreen --> VgsScreenDraw : вся отрисовка
    VgsEditorScreen --> TerminalJump : переход в терминал блока (ADR-0021)
    VgsEditorScreen --> EntityResolver : id блока → живой блок (из ввода)
    VgsEditorScreen --> BlockSwitch : переключатель питания карточки (ADR-0035)

    TerminalJump --> IMyGui : ShowTerminalPage / ChangeInteractedEntity
    TerminalJump --> IMyTerminalBlock : проверка, что сущность терминальна
    TerminalJump --> IMyRadioAntenna : есть ли передающая антенна для далёкого блока
    TerminalJump --> VgsSession : есть ли локальный игрок

    GridGraphBuilder --> GridGraph
    GridGraphBuilder ..> BlockState : RefreshState пишет через ApplyIntegrity, ApplySwitch и ApplyWidget
    GridGraphBuilder --> BlockSwitch : читает и пишет флаг Enabled (ADR-0035)
    GridGraphBuilder --> Node
    GridGraphBuilder --> BlockPortCatalog
    GridGraphBuilder --> ToolbarProbe : форма тулбара
    GridGraphBuilder --> ToolbarWriter : чтение ячеек
    GridGraphBuilder --> ToolbarPlanner : группа ячейки
    GridGraphBuilder --> NodeStateStore : позиция и видимость
    GridGraphBuilder --> EventControllerProbe : событие и список отслеживания
    VgsSessionComponent --> EventControllerKeeper : проигрывание списка после загрузки
    EventControllerKeeper --> EventControllerProbe
    EventControllerKeeper --> NodeStateStore : сохранённый список
    EventControllerProbe --> NodeStateStore : список отслеживания
    EventControllerProbe --> IMyEventComponentWithGui : AddBlocks / RemoveBlocks
    GridGraphBuilder --> EntityResolver : id блока → живой блок
    EventControllerProbe --> EntityResolver : id блока → живой блок
    EventControllerKeeper --> EntityResolver : id блока → живой блок
    NodeStateStore --> EntityResolver : id блока → живой блок
    EntityResolver --> IMyTerminalBlock
    GridGraphBuilder --> IMyTerminalBlock
    GridGraphBuilder --> IMyToolbar
    GridGraphBuilder --> IMyBlockGroup
    GridGraphBuilder --> IMyGridTerminalSystem
    GridGraphBuilder --> ConveyorProbe : линии конвейеров грида (ADR-0040)
    ConveyorProbe --> ConveyorSegment
    ConveyorProbe --> EntityResolver : id блока → живой блок
    GridGraphBuilder --> ConveyorNetwork : редукция сети (ADR-0040)
    ConveyorNetwork --> ConveyorSegment
    ConveyorNetwork --> ConveyorLink
    GridGraphBuilder --> IMyCubeGrid

    NodeStateStore --> NodeStateFormat : формат позиции
    NodeStateStore --> IMyTerminalBlock
    VgsTerminalControls --> NodeStateStore : флаг «Show in Grid Workspace»

    GridViewStore --> GridViewFormat : формат состояния вида
    GridViewStore --> IMyCubeGrid
    GridViewFormat ..> VgsConstants : границы формата

    ToolbarProbe --> ToolbarLayout
    ToolbarProbe --> IMyToolbarProvider
    ToolbarProbe --> IMyToolbar
    ToolbarWriter --> ToolbarPlanner
    ToolbarWriter --> ToolbarLayout
    ToolbarWriter --> IMyToolbar
    ToolbarWriter --> MyToolbarItemInfo
    ToolbarWriter --> CellContents : читает ячейку в модель
    ToolbarPlanner --> CellContents : решает по содержимому
    CellContents --> CellKind

    GridGraph "1" *-- "0..*" Node : содержит блоки
    GridGraph "1" *-- "0..*" Node : содержит группы (по имени)
    GridGraph "1" *-- "0..*" Edge : содержит
    GridGraph "1" *-- "0..*" ConveyorLink : содержит (ADR-0040)
    Node "1" *-- "0..*" Port : действия/события
    Node "1" *-- "1" BlockState : снимок состояния
    Node --> ToolbarLayout
    Edge --> ToolbarCell : занимает ячейку
    Edge ..> Node : ссылается по EntityId или по имени группы
    GridGraph ..> Port : LayoutPorts назначает Slot (исходящие)
    GridGraph ..> InputBar : LayoutPorts собирает полосы входящих портов
    GridGraph ..> ToolbarLayout : проверяет ячейку
    GridGraph ..> GroupMemberActions : правило действий группы
    GridGraph ..> Node : OffsetGroupMembers двигает участников
    GridGraph ..> WatchTarget : LayoutPorts назначает Slot
    GridGraph ..> BlockState : ApplyIntegrity, ApplySwitch и ApplyWidget пишут снимок
    Node ..> NodeKind : различает блок и группу
    BlockState ..> BlockPower : переключатель питания блока (ADR-0035)
    BlockState ..> WidgetAccent : акцент виджета состояния (ADR-0042)
    BlockState ..> WidgetReading : SetWidget пишет одно чтение целиком
    Node ..> StateWidgetKind : форма виджета, прочитанная из игры (ADR-0042 §7)
    CategoryDescriptor ..> StateWidgetKind : форма по подтипу — запасной путь (ADR-0042 §7)
    GridGraphBuilder --> StateWidgetProbe : читает заполненность, заряд и отсчёт (ADR-0042)

    BlockPortCatalog --> Node
    BlockPortCatalog --> Port
    BlockPortCatalog ..> GroupMemberActions : ComputeGroupActionIds

    PortGeometry ..> Node : читает Position, FrameSize и Slot
    PortGeometry ..> Port : читает Slot и Direction
    EdgeRouting ..> VgsConstants
    LineRaster ..> VgsConstants : размер точки и шаг ритма
    VgsScreenDraw ..> LineRaster : путь → пиксельные точки (ADR-0046, ADR-0047)
    PipeRouter ..> VgsConstants : радиус изгиба и шаг лесенки
    LineHit ..> VgsConstants : допуск наведения (device px, ADR-0049)
    VgsEditorScreen --> LineHit : расстояние от указателя до линии (ADR-0049)

    ToolbarWriter --> MyToolbarItemInfo : BlockAction / GroupAction

    VgsEditorScreen ..> VgsLog : Throttled (tags)
    VgsEditorScreen --> VgsText
    VgsEditorScreen --> GameLanguage : язык при каждом открытии
    VgsEditorScreen --> VgsConstants
```

### 1.2. Правила зависимостей

Разрешено (стрелка — «может ссылаться на»):

```
Vgs.Core   →  Vgs.Core
Vgs.Model  →  Vgs.Model            (и VRageMath, и VgsLanguage из Core)
Vgs.Game   →  Vgs.Model, Vgs.Core, Vgs.Ui (открыть экран и спросить, открыт ли он)
Vgs.Ui     →  Vgs.Model, Vgs.Core, Vgs.Game (из ввода и обновления, никогда из отрисовки)
```

Стрелка `Ui → Game` разрешена всегда; запрещён вызов `Vgs.Game` из `OnDraw` и всего, что он
зовёт. Экран не умеет писать в мир сам, поэтому запись связи (`ToolbarWriter`), сохранение
позиции (`NodeStateStore`), отслеживание (`EventControllerProbe`) и переход в терминал
(`TerminalJump`) — это вызовы `Game` из `OnHandleInput`, то есть по действию игрока, а не из
кадрового пути картинки (ADR-0003).

Обратное ребро — **`Game → Ui`** — тоже разрешено, но только как «открыть экран и спросить,
открыт ли он»: `VgsTerminalControls` (кнопка в терминале) и `EditorHotkey` (клавиша M) зовут
`VgsEditorScreen.OpenFor`; клавиша ещё и спрашивает `IsEditorOpen`. Мир и состояние экрана по
этому ребру не читаются: `IsEditorOpen` — статический признак из одного поля и `MyModScreen.IsOpen`,
без обращения к блокам, без аллокаций и без чтения картинки. Читается он **в тиковом пути**
(`EditorHotkey.Poll` из `UpdateAfterSimulation`), и это допустимо именно потому, что предмет
чтения — не мир и не состояние экрана, а «есть ли экран»; кадровый путь (`OnDraw`) по этому
ребру не ходит вовсе.

Запрещено, и это проверяется шлюзом:

| Запрет | Почему | Правило |
|---|---|---|
| `Vgs.Model` → `Sandbox.*`, `VRage.Game.ModAPI`, `VRage.ModAPI` | Модель должна оставаться проверяемой без запуска игры | R001 |
| `Vgs.Ui` → чтение `IMyTerminalBlock`/`IMyCubeGrid` из `Draw*` | Стоимость кадра и падение на уничтоженной сущности | ADR-0003, R010 |
| `Vgs.Model` → `Vgs.Ui` / `Vgs.Game` | Инверсия слоёв | ревью |
| Любой слой → `UnityEngine`/`UnityEditor` | Невозможно в моде | ADR-0008 |
| Любой слой → `Sandbox.Graphics.*` | У сборки нет секции в вайтлисте скриптов: игра отклоняет компиляцию мода | ADR-0008, `check-whitelist.ps1` |

Исключение, зафиксированное в ADR-0003: `VRageMath` разрешён в `Vgs.Model` — это
библиотека значимых типов (`Vector2`, `Vector3I`) без доступа к сущностям.

Второе исключение, зафиксированное в ADR-0052: `Vgs.Model` читает **`Vgs.Core.VgsLanguage`** —
чистое перечисление без единого игрового типа. Оно нужно потому, что подпись аргумента
действия существует в двух языках (`ActionParameterTable`), и выбирать её должен тот, кто знает
язык, — модель, а не экран: альтернатива (передавать в модель уже выбранные строки) вынесла бы
работу с таблицей аргументов в слой отрисовки, где её нельзя проверить офлайн. Проверяемость
модели не страдает: `offline-check.ps1` компилирует `Model` вместе с `Core` и запускает без игры.

---

## 2. Слои и что им разрешено

### `Vgs.Core` — инфраструктура

| Файл | Ответственность |
|---|---|
| `VgsLog.cs` | единая точка логирования; префикс `[VGS]`; `Throttled` для условий, истинных каждый кадр |
| `VgsSession.cs` | фасад сессии: номер кадра, признак «есть локальный игрок», признак dedicated |
| `VgsConstants.cs` | все числа и id в одном месте; `ModGuid` для хранилища |
| `VgsText.cs` | фасад текста: свойство на каждую строку (`S("КЛЮЧ")`), активная таблица (`Language`, `UseLanguage`, `Reset`) и всё, что **не** переводится — глифы отката, знаки сравнения, форматы чисел |
| `VgsStrings.cs` | две таблицы «ключ → строка» (`English`, `Russian`); данные, а не типы: добавление языка — словарь, а не класс |
| `VgsLanguage.cs` | языки, которые мод знает (`English`, `Russian`) — **список мода**, не `MyLanguagesEnum` |

`VgsConstants` и `VgsText` жили в одном файле, пока таблица строк не переросла класс констант.
Файл с именем `VgsConstants.cs` — неправильное место, чтобы искать в нём текст предупреждения;
правило R006 указывает теперь на `Core/VgsStrings.cs`.

Почему строки — таблица на C#, а не `MyTexts.resx`: таблица компилируется в
`tools/offline-check.ps1`, и шлюз проверяет, что у обоих языков один набор ключей, что
плейсхолдеры совпадают, что каждая строка форматируется и что ни одно свойство фасада не
возвращает ключ, — **без запуска игры**. С `.resx` эта же проверка требует человека в игре.
Решение и его цена — [ADR-0033](adr/0033-localization.md).

Почему ключ, а не класс на язык: строки — данные, и [ADR-0031](adr/0031-specificity-inheritance-vs-typed-state.md)
запрещает заводить тип под данные. Полнота таблиц поэтому обеспечивается шлюзом, а не
компилятором — это записанная цена, а не недосмотр.

`VgsText` — фасад, а не хранилище: он держит ссылку на активную таблицу и одно свойство на
строку, поэтому вызов `VgsText.CloseButton` не изменился ни в одном из 86 мест, и ни смена
формы хранения строк, ни новый язык не двигают вызывающий код.

Почему фасад, а не передача компонента по конструкторам: у мода нет DI-контейнера, а
подсистемам нужно 3–4 факта о сессии. Протаскивание компонента через каждый конструктор
заставило бы каждый тест строить сессию.

`VgsLog` существует потому, что `MyLog.Default.WriteLine` напрямую в каждом месте — это
отсутствие троттлинга (редактор перерисовывается каждый кадр), отсутствие общего префикса
для grep и невозможность отключить шум одним переключателем.

### `Vgs.Model` — данные и правила графа

| Файл | Ответственность |
|---|---|
| `Node.cs` | `Node` — карточка блока или группы: идентичность (включая грид, ADR-0032), порты, тулбар, позиция, снимок состояния, категория |
| `Port.cs` | `Port` и три перечисления порта: `PortKind`, `PortDirection`, `PortGroup` |
| `ToolbarLayout.cs` | `ToolbarLayout` (что игра дала блоку: группы, ячейки, страницы, «да/нет») и `ToolbarCell` (координата ячейки) |
| `NodeKind.cs` | `NodeKind` — блок это или группа |
| `GroupMemberActions.cs` | `GroupMemberActions` — действие группы и то, включено ли оно на каждом участнике |
| `WatchTarget.cs` | `WatchTarget` — одна запись списка отслеживания контроллера |
| `Edge.cs` | `Edge` — связь между двумя портами |
| `GridGraph.cs` | контейнер узлов, групп и связей, инварианты связей, раскладка портов по слотам, запись снимка состояния (`ApplyIntegrity`, `ApplySwitch`) |
| `BlockState.cs` | снимок состояния блока: `Integrity` 0..1 (общее для всех), `Power` — переключатель питания (ADR-0035), `Accent` + `WidgetActive` и `Metrics` — чтение **виджета состояния** (ADR-0042; ключи задаёт описатель категории, значение — доля 0..1, зажимается при записи). `SetWidget` пишет одно чтение целиком, чтобы кадр не увидел половину. Не сохраняется |
| `BlockPower.cs` | `BlockPower` — что говорит переключатель блока: `Unknown` (ещё не прочитан), `On`, `Off`, `Unavailable` (повреждён ниже критической целостности, включить нельзя). Три читаемых состояния, а не два: сломанный блок репортит `Enabled == true`, и флаг в лоб сделал бы его зелёным |
| `BlockCategory.cs` | `BlockCategory` (enum, порядок членов = порядок проверок классификатора) и `CategoryDescriptor` (что знает категория: события, ключи метрик и форма виджета состояния — ADR-0042) |
| `ControllerState.cs` | специфика event controller: описание режима и список отслеживания. `Node.Controller` ссылается сюда только у контроллера — эта ссылка и есть признак |
| `StateWidget.cs` | форма **виджета состояния** (ADR-0042): `StateWidgetKind` (четыре формы + `None`), `WidgetAccent` (направление заряда), `WidgetReading` (неизменяемая структура «активно / два числа / акцент») и `StateWidget` — сколько баров и сколько чисел читает форма. Форма приходит **из игры** (интерфейс блока), а `CategoryDescriptor.Widget` остаётся запасным путём для подтипа — §7 ADR-0042 объясняет, почему и что из этого проверяется шлюзом |
| `BlockPortCatalog.cs` | какие события у какого семейства блоков, как они делятся по группам «да»/«нет», и какие действия принимает группа блоков |
| `ToolbarPlanner.cs` | правило размещения: в какую ячейку тулбара кладётся связь и когда её класть некуда |
| `NodeStateFormat.cs` | формат сохранённой позиции: «`x y`» ↔ `Vector2`, инвариантная культура, отказ от мусора |
| `GridViewFormat.cs` | формат состояния вида: «`x y zoom hidden subgrids borders conveyors`» ↔ камера, масштаб и четыре режима показа; три поля (старый формат) принимаются как значение прежней сборки, недостающие режимы берутся по умолчанию |
| `ConveyorSegment.cs` | `ConveyorSegment` — одна сырая конвейерная линия грида, сведённая к двум id блоков. Не связь и не ребро: факт из мира (ADR-0040) |
| `ConveyorLink.cs` | `ConveyorLink` — логическая конвейерная связь на схеме: пара блоков, между которыми на сети нет другого **нарисованного** блока. Ненаправленная, пара нормализована |
| `ConveyorNetwork.cs` | правило редукции: обход от нарисованного узла по сырым линиям с остановкой на первом нарисованном; скрытые блоки и трубы проходятся насквозь; петли и дубликаты не дают лишних связей |
| `BlockSearch.cs` | правило фильтрации карточек: AND по термам, подстрока в имени или отображаемом типе, без учёта регистра — то же, что делает список блоков терминала |
| `TextEditor.cs` | состояние однострочного поля: текст, каретка, якорь выделения и все правила правки (вставка, удаление, стрелки, Home/End, SelectAll) — ADR-0024 |

Слой **не содержит** ссылок на `Sandbox.*` и `VRage.Game.ModAPI`. Это не стилистика:
это единственная часть проекта, которую можно проверить без запуска игры, а запуск игры —
самая дорогая операция в цикле разработки.

Инварианты `GridGraph`, на которые опираются и UI, и будущий вычислитель:

- связи хранятся по **ключу порта**, а не по индексу — добавление порта в тип блока не
  должно перекоммутировать существующие графы;
- **каждая связь занимает ячейку тулбара** (`Edge.Cell`), и две связи не могут занимать одну
  ячейку; ячейка обязана лежать внутри тулбара источника и попадать в слот своей группы
  ([ADR-0011](adr/0011-toolbar-is-the-connection.md));
- **исходящий порт уникален**: одна связь — одно событие одного блока; два разных события
  в один исходящий порт слить нельзя (fan-out из одной точки разрешён);
- **входящий порт общий**: несколько связей могут приходить в один вход, потому что
  «включить» — одно действие, и просить его могут разные блоки (fan-in разрешён, см.
  [ADR-0010](adr/0010-port-presentation-and-binding.md));
- связь на несуществующий порт, ячейка вне тулбара и занятая ячейка отклоняются
  (`TryConnect` возвращает `false` и ничего не меняет);
- **петля на себя разрешена** ([ADR-0036](adr/0036-self-connection.md)): источник и цель
  могут быть одним блоком — и в тулбаре, и в списке отслеживания контроллера. Запрета нет,
  потому что его нет у игры (`MyEventControllerBlock.ValidateBlock` проверяет только «тот же
  логический конструкт»); рисунок — единственное место с особым случаем: связь обходит
  карточку петлёй (`EdgeRouting.BuildSelfLoop`), а самопросмотр контроллера рисуется одной
  жёлтой полосой без линии;
- **«дубликат» — это дубликат ячейки, а не связи**: то же действие на том же блоке на другой
  странице — законная отдельная связь, игра её принимает и исполняет. Линия при этом одна:
  `GridGraph.Lines` (пересчитывается в `LayoutPorts`) отдаёт по одному ребру на пару
  (исходящий порт, входящий порт), иначе повтор рисовался бы поверх себя;
- удаление узла удаляет все его связи;
- **группа — узел второго вида** ([ADR-0015](adr/0015-block-groups.md)): у неё нет `EntityId`,
  её идентификатор — имя; она живёт, пока в графе есть хотя бы один её участник, и у неё не
  бывает исходящих портов. Рёбра на группу хранят `Edge.TargetGroupName` вместо
  `Edge.TargetEntityId`, и подсчёт fan-in различает эти два случая — иначе группа и блок с тем
  же номером участника делили бы один счётчик;
- **порт рисуется только при наличии связи**: `LayoutPorts()` назначает `Port.Slot` из
  рёбер, `-1` — «не рисовать». Проход обходит и блоки, и группы: полоса порта группы теряется
  по тому же правилу, что и полоса блока.
- **аргумент действия описывается генерируемой таблицей** ([ADR-0052](adr/0052-derived-action-arguments.md)):
  `Port.ParameterTypes` — то, что объявила игра, и для встроенных действий это заглушка
  (`TypeCode.Empty`); настоящий тип и подпись берутся из `ActionParameterTable`, которую
  порождает `tools/scan-action-parameters.ps1` и проверяет `offline-check.ps1`. Число
  аргументов всегда равно объявленному: таблица описывает, но не добавляет. **Имя аргумента —
  подпись диалога, который игра строит для этого аргумента**; откатов и подстановок нет.
  **Число полей — число диалогов, а не число аргументов**: один диалог может заполнять
  несколько параметров (цвет — три `Int32` за одним выбором), и тогда поле одно, а значения
  вводятся через запятую;
- **полоса входящего порта — на запрос, а не на порт** ([ADR-0050](adr/0050-action-parameters.md)):
  `LayoutPorts()` собирает `Node.InputBars` по рёбрам, и два запроса одного действия
  (`Run` со строкой `scan` и `Run` со строкой `build`) дают две полосы, две линии и два
  независимых «x». `Port.Slot` у входящего порта всегда `-1`; геометрия спрашивается у бара
  (`PortGeometry.TryInputBarRect`). Одна линия — по-прежнему на пару (исходящий порт, запрос).

Последний пункт — производное состояние, и это единственное место в модели, где поле
переписывается не при создании. Поэтому `LayoutPorts()` вызывается **после любого изменения
связей** (`GridGraphBuilder.Refresh`, `TryCompleteBind`), а не из UI: слот зависит от рёбер,
а рёбрами владеет только граф.

### Специфика категории: `ControllerState` ([ADR-0030](adr/0030-block-category-descriptor.md))

`Node` — общий узел для всех блоков и групп, и он нёс на себе специфику event controller:
описание режима, список отслеживания, идентификатор события. Восемьдесят категорий платили за
одну. Теперь специфика живёт в `ControllerState`, а `Node` держит ссылку:

```csharp
public readonly ControllerState Controller;   // null у всех, кроме event controller
public bool IsEventController { get { return Controller != null; } }
```

**Ссылка и есть флаг.** Второго поля «это контроллер» нет, поэтому два источника истины об
одном факте не разойдутся. Проверка в кадровом пути стоит столько же, сколько чтение `bool`.

Правило для следующей специфики (заряд, заполненность, меню узла): **новый тип рядом с
`ControllerState`, ссылка с `Node` по тому же образцу**. Общий узел не растёт, а читатель,
которому специфика нужна, проверяет null там, где читает.

Что осталось на `Node` и почему:

| Поле | Почему общее |
|---|---|
| `Category` | выводится из подтипа один раз в конструкторе; подтип блока не меняется |
| `State` (`BlockState`) | повреждение есть у каждого блока (Шаг 2) |
| `CanDriveBlocks`, `Toolbar` | факты из игры (ADR-0011), читаются каждый кадр |
| `IsEventController` | производное от `Controller`, не хранится |

`GridGraph` продолжает владеть операциями над списком (`TryAddWatch`, `RemoveWatch`,
`SyncWatched`, `MergeWatchIds`, `PruneAdoptedWatchIds`, `CollectWatchedIds`): они меняют
инварианты графа — кап стека, раскладку слотов, — но работают через `node.Controller` и
отказывают, если его нет.

### `Vgs.Game` — мост к игре

| Файл | Ответственность |
|---|---|
| `VgsTerminalControls.cs` | регистрация кнопки «Grid Workspace» и чекбокса «Show in Grid Workspace», проверка прав, открытие экрана |
| `EditorHotkey.cs` | **второй вход** в тот же экран — клавиша **M** (ADR-0045): опрос `IsNewKeyPressed` раз в тик и все условия допуска (локальный игрок, экран не открыт, без модификаторов, не чат, не другой экран, не терминал, игрок **в кокпите**). Открывает через `VgsEditorScreen.OpenFor`, поэтому всё, что происходит после входа, у кнопки и у клавиши одно и то же |
| `GridGraphBuilder.cs` | построение и обновление `GridGraph` из `IMyCubeGrid`, сверка связей с тулбарами, фильтр скрытых блоков |
| `ToolbarProbe.cs` | форма тулбара блока: компонент `IMyToolbarProvider` → `ToolbarLayout` |
| `ToolbarWriter.cs` | запись и очистка элемента в реальном тулбаре блока, включая аргументы действия (ADR-0050): `TryWrite`/`TryWriteGroup` с `ParameterSet`, `TryRewrite` — перезапись ячейки новыми аргументами одним пакетом (без окна, в котором ячейка пуста), чтение аргументов вместе с элементом в `TryDescribe` |
| `ToolbarParameters.cs` | единственное место, где словарь аргументов игры встречается со словарём модели (ADR-0050): `Definitions` — типы аргументов действия из `GetActionParameterDefinitions`; `Read` — значения из ячейки через `GetParameters`; `ToGame` — обратно в `TerminalActionParameter`; `Set` — запись через `MyToolbarApiExtensions.Set`; `SameItem` — «эта ячейка всё ещё держит эту связь» вместе с аргументами. **Объявленные типы — только для чтения**; для записи настоящий тип берётся из `ActionParameterTable` (ADR-0052) |
| `ActionParameterDefaults.cs` | значения по умолчанию для окна аргументов, прочитанные **у блока** (ADR-0052): `TerminalRunArgument` programmable block, `TargetVelocityRPM` и `LowerLimitDeg`/`UpperLimitDeg` ротора. Умолчание, которое нельзя прочитать, — это пустое поле, а не выдуманное число |
| `TerminalActionRunner.cs` | разовый запуск действия блока или группы **сейчас**, без связи и без записи в тулбар (ADR-0041): поиск действия по id на блоке → `ITerminalAction.Apply`, для группы — по каждому функциональному члену из терминальной системы. Повторяет ровно то, что делает игра при срабатывании элемента тулбара, включая отсутствие проверки `IsEnabled` (`01-research/api-toolbar.md` §4.3) |
| `NodeStateStore.cs` | позиция карточки, флаг видимости и список отслеживания контроллера в `MyModStorageComponent` блока |
| `GridViewStore.cs` | камера, масштаб и четыре режима показа в `MyModStorageComponent` **грида** (ADR-0022, ADR-0040): состояние общее для всех кокпитов конструкта |
| `TextClipboard.cs` | буфер обмена поля поиска — **только собственный**: системный буфер моду недоступен (ADR-0027), поэтому файл не ссылается ни на один тип игры и сломать загрузку не может |
| `EventControllerProbe.cs` | что проверяет event controller, кому из блоков он это разрешает и запись его списка |
| `EventControllerKeeper.cs` | проигрывание сохранённого списка контроллера в живой блок после загрузки мира |
| `TerminalJump.cs` | переход из редактора в терминал игры на выбранном блоке: выбор ветки `ShowTerminalPage`/`ChangeInteractedEntity`, флаг `remote` по дистанции взаимодействия, проверка передающей антенны, отказы (ADR-0021) |
| `EntityResolver.cs` | единственная реализация «id сущности → живой `IMyTerminalBlock`» (и «→ живой `IMyCubeGrid`» для конвейерного прохода): `try/catch`, один троттлинг-тег `resolve-block`, отсутствие блока — штатный ответ `false`, а не ошибка |
| `ConveyorProbe.cs` | линии конвейеров одного грида из `MyObjectBuilder_CubeGrid.ConveyorLines` (реализация конвейеров моду недоступна — ADR-0040), сведение концов линий к id блоков через `GetCubeBlock` |
| `BlockSwitch.cs` | переключатель питания блока (ADR-0035): `CanBeEnabled` (есть ли у блока `IMyFunctionalBlock`), `TryRead` (`Enabled` + `IsFunctional` за одно чтение) и `TrySetEnabled` (сеттер `Enabled` с проверкой, что флаг действительно принял значение) |
| `StateWidgetProbe.cs` | **чтение виджета состояния** (ADR-0042): `KindOf` отвечает на вопрос «какая это форма» по интерфейсу игры, `TryRead` читает заполненность инвентаря, заряд и `ChargeMode` батареи, `CurrentTime / Delay` таймера, две инвентари производства. `TryRead` получает **форму** от узла, а не подтип; каждое чтение — в своём `try`; отказ возвращает `false`, и карточка сохраняет прошлую картинку |
| `GameLanguage.cs` | **единственное** место, которое знает `MyLanguagesEnum`: читает `MyAPIGateway.Session.Config.Language` и переключает таблицу `VgsText`; всё, что ниже, говорит на `VgsLanguage` (ADR-0033) |

Все методы — границы, на которых исключение из игры превращается в запись в лог.
`Refresh` сохраняет позиции выживших узлов, удаляет узлы уничтоженных блоков, **сверяет
связи с тем, что реально лежит в тулбарах** (`ReconcileToolbarEdges`) и пересчитывает слоты
портов: именно это делает редактор устойчивым, пока игрок режет корабль и правит тулбары
руками. Блок, у которого выключен флаг «Show in Grid Workspace», в граф не попадает вовсе — поэтому
его узел снимается тем же проходом удаления, а связи к нему и от него исчезают из картинки,
оставаясь при этом в тулбарах и работая в игре ([ADR-0013](adr/0013-show-in-vgs-flag.md)).

У того же фильтра есть второй режим: `Build`/`Refresh` принимают `includeHidden`, и при
включённом режиме «показывать скрытые» (ADR-0023) блок с выключенным флагом попадает в граф
наравне с остальными. Флаг на блоке при этом не трогается — режим меняет только то, что
видно. Смена режима пересобирает граф целиком, потому что «в схеме ли блок» решается здесь,
где блок и читается; экран перед этим сбрасывает все ссылки указателя на узлы, так как
пересборка заменяет объекты `Node`, а не обновляет их.

`Refresh` делает **два прохода по миру, а не один**: сначала блоки, потом группы
(`ReadGroups` → `SyncGroups`). Порядок обязателен — группа существует только при видимом
участнике, а «видимый» определяется первым проходом. Правило «Show in Grid Workspace» для группы
поэтому не имеет собственного кода: `SyncGroups` передаёт в `GridGraph.SyncGroup` лишь тех
участников, что уже попали в граф, а группа без них удаляется вместе с входящими рёбрами.
Набор действий группы при этом считается по **всей** группе, включая скрытых участников и
блоки подгридов: игра применяет действие ко всем им, и список, посчитанный по видимым,
обещал бы меньше, чем связь делает ([ADR-0015](adr/0015-block-groups.md)).

**«Не удалось прочитать» — не «не существует».** `SyncGroups` снимает группу только тогда,
когда она действительно прочитана и не имеет ни одного нарисованного участника. Исчерпанный
бюджет участников, группа за пределом `MaxGroupsScanned` и исключение при чтении оставляют
узел как есть: снятая группа уносит свои рёбра, а сверка их не вернёт — узла уже нет. Внутри
одной группы проход тоже двойной: сначала полный (и бесплатный) обход ради нарисованных
участников, который решает, жива ли группа, и только потом бюджет на чтение действий. Обратный
порядок — реальный дефект этой реализации: `GetBlocks` отдаёт `HashSet`, обрыв на бюджете мог
не увидеть нарисованного участника, и живая группа объявлялась мёртвой.

`NodeStateStore` — единственное место, где мод читает и пишет **своё** состояние блока:
позицию карточки, флаг видимости и список отслеживания event controller. Все три лежат в
`MyModStorageComponent` самого блока, а не в
таблице по `EntityId`, потому что `MyObjectBuilder_CubeBlock.Remap` переназначает id при
копировании, проекции и резке грида ([ADR-0012](adr/0012-layout-lives-on-the-block.md)).
Чтение никогда не создаёт компонент: иначе простой просмотр схемы прикреплял бы хранилище к
каждому блоку мира и менял сейв. Формат значения вынесен в `Vgs.Model.NodeStateFormat`, чтобы
его можно было проверить офлайн — это единственная часть сохранения, которая может тихо
испортить раскладку.

`GridViewStore` — тот же механизм, но **носитель другой**: камера, масштаб и режим
«показывать скрытые» лежат в `MyModStorageComponent` **грида**
([ADR-0022](adr/0022-view-state-lives-on-the-grid.md)). Это не непоследовательность, а
требование продукта: состояние описывает, как смотрят на конструкт, поэтому открытие схемы с
другого кокпита того же грида обязано дать ту же картину; на блоке оно размножилось бы по
числу кокпитов. Формат (`"x y zoom hidden"`) вынесен в `Vgs.Model.GridViewFormat` по той же
причине, что и формат позиции: испорченный разбор открывает схему не там, где работал игрок.
Запись — троттлингом по изменению и один раз в `OnClosed`; чтение не создаёт компонент.

`ToolbarProbe` и `ToolbarWriter` — единственные места, где мод трогает тулбар. Первый
отвечает на вопрос «может ли этот блок управлять другими» (компонент `IMyToolbarProvider` +
`ToolbarType == ButtonPanel`, а не таблица подтипов), второй — единственный, кто пишет в
мир по действию пользователя. Разделение намеренное: чтение происходит при каждом
обновлении, запись — только по клику, и смешивать их значило бы дать пути обновления
возможность менять блоки.

### Два прохода обновления: структура и состояние

Чтение мира разделено на два прохода с разной ценой и разной частотой. Это не оптимизация
ради оптимизации: карточка должна показывать повреждение живым, а полная перестройка схемы
дважды в секунду пересоздавала бы порты и рамки под указателем игрока.

| Проход | Что читает | Что делает | Частота |
|---|---|---|---|
| **структурный** — `GridGraphBuilder.Refresh` | блоки грида, тулбары, группы, список отслеживания | добавляет и снимает узлы, пересобирает порты, сверяет связи с тулбарами, `LayoutPorts`, рамки групп | `VgsEditorScreen.RefreshIntervalFrames` = 90 кадров |
| **состояния** — `GridGraphBuilder.RefreshState` | только целостность и флаг питания перечисленных блоков | пишет `Node.State` через `GridGraph.ApplyIntegrity` и `GridGraph.ApplySwitch` | `VgsConstants.StatePollIntervalFrames` = 30 кадров |

Правила, которые делают разделение работающим:

- **проход состояния ничего не перестраивает.** Он не трогает порты, группы, тулбары и рёбра
  и не вызывает `LayoutPorts`. Инвариант проверяется офлайн: после записи состояния объекты
  `Port` те же по ссылке, их слоты не изменились, число узлов и рёбер прежнее
  (`tests/OfflineChecks.cs`, раздел «block state snapshot»);
- **набор id — это набор видимых карточек.** Экран считает его тем же предикатом, что и
  отрисовка (`CanvasRect.Overlaps`, он же — culling в `DrawNodes` через `IsCardOnCanvas`), и тем
  же потолком `MaxRenderedBlocks`. Одно определение «видимого» на оба прохода: иначе проход
  состояния опрашивал бы карточки, которых не видно, и пропускал бы те, что видно. Предикат
  живёт в `CanvasRect`, а не в экране, потому что он же отвечает за рамки гридов, их вкладки и
  рамки групп — и потому что его арифметика проверяется офлайн: холст начинается **под шапкой**,
  и сравнение абсолютной координаты с размером холста убирало карточку на высоту шапки раньше,
  чем она доходила до нижнего края (R-015);
- **«не прочиталось» ≠ «не повреждено».** Блок, который исчез или не ответил, сохраняет
  предыдущее значение: сброс в 1 превратил бы одну неудачную читку в ложное «блок починили»,
  а карточка мигала бы между повреждённой и целой;
- **состояние — снимок, а не сохранённая сущность.** Оно живёт только в памяти и никогда не
  попадает в `MyModStorageComponent`; в сохранении по-прежнему только то, что выбрал игрок —
  позиция и видимость ([ADR-0012](adr/0012-layout-lives-on-the-block.md));
- **счётчики разные.** `_lastRefreshFrame` и `_lastStatePollFrame` — два независимых
  счётчика кадров экрана (ADR-0026: не `VgsSession.Frame`, он замирает на паузе). Общий
  счётчик связал бы проходы и опустил бы частый до частоты редкого.

Факты о том, откуда берётся целостность (`IMyCubeBlock.SlimBlock` →
`IMySlimBlock.BuildIntegrity / MaxIntegrity`) и почему читается именно эта пара чисел, —
[`01-research/api-block-state.md`](01-research/api-block-state.md).

### Список отслеживания event controller

У event controller есть третье состояние, которого нет ни у одного другого блока: список
блоков, за которыми он **наблюдает** ([ADR-0017](adr/0017-event-controller-watch-ports.md)).
Оно не является связью: у него нет ячейки тулбара и нет действия получателя, поэтому в
модели это не `Edge`, а `WatchTarget` в `Node.Watched`, и в UI — не полоса входящего порта, а
жёлтая полоса с точкой **справа**.

**Источник истины — игра** ([ADR-0018](adr/0018-game-is-the-source-of-truth.md)). Показывается
объединение двух источников, и порядок в нём — ответ на вопрос «кто главный»:

```
игровой список  = IMyCubeBlock.GetObjectBuilderCubeBlock().SelectedBlocks   (первые слоты)
надстройка      = MyModStorageComponent контроллера                        (следом)
```

Игровой список читается у живого блока на каждом обновлении, поэтому выбор, сделанный в
терминале, появляется в редакторе сам. Надстройка нужна ровно для одного: то, что редактор
добавил через `IMyEventComponentWithGui.AddBlocks`, игра в свой список не кладёт и в сейв не
пишет. Когда игра начинает отдавать тот же id, надстройка вычищает его у себя — иначе блок
считался бы дважды.

**Надстройка помнит своё событие** ([ADR-0044](adr/0044-event-change-resets-editor-watches.md)).
Идентификатор события (`UniqueSelectionId`) записывается вместе со списком в одно значение:

```
"e12 34 56"   событие 12, блоки 34 и 56
"34 56"       событие не записано (значение прежних версий)
""            привязок нет
```

Без этого числа смену события нельзя заметить: игра чистит свой список сразу и сама, а
редактор может быть в этот момент закрыт. Значение без заголовка не удаляется, а помечается
текущим событием — «файл старше поля» не доказательство смены события; `0` с любой стороны
означает «неизвестно», а не «другое событие».

Три следствия, которые надо держать в голове, меняя этот код:

- **игровые записи из редактора не удаляются.** Приватный список контроллера не имеет
  публичного сеттера, а `RemoveBlocks` снимает только подписку компонента — `ProcessSelectedBlocks`
  вернёт запись через секунду. Поэтому «x» на таком порте отвечает сообщением и ничего не
  меняет (`WatchResult.GameOwned`), а редакторская запись снимается нормально;
- **смена события — это сброс, а не проигрывание.** Игра выбирает другой компонент события и
  выбрасывает свой список; подписки редактора к новому компоненту не переходят, а привязки
  были сделаны для события, которого больше нет. Поэтому надстройка **удаляется**: хранилище
  очищается, порты исчезают на том же обновлении, ничего не проигрывается. Решение принимает
  `EventControllerProbe.ReadOverlay` — через него проходят и обновление схемы, и восстановление
  после загрузки мира, — а `ReapplyWatchList` повторяет ту же проверку на своей границе записи.
  Сброс **снимает и подписку**: старый компонент после смены события остаётся живым и
  подписанным (`OnSelectedEventIdChanged` снимает только ванильный выбор), поэтому
  `EventControllerProbe` запоминает компонент, в который писал, и вызывает на нём `RemoveBlocks`.
  Без этого порт исчез бы, а контроллер продолжал бы запускать тулбар;
- **«не отображается» ≠ «удалить».** Скрытый флагом «Show in Grid Workspace» блок не имеет карточки, его
  линия не рисуется, но порт и игровые данные остаются. Схема скрывает то, что не может
  нарисовать, и не правит данные игры ради порядка на экране.

Запись — единственное место, где мод меняет контроллер: `EventControllerProbe.TryAddWatch` /
`TryRemoveWatch`, через `IMyEventComponentWithGui.AddBlocks` / `RemoveBlocks` — тот же метод,
которым пользуется ванильный терминал. Порядок записи асимметричен и обязателен: добавление —
проверка → резерв в графе → хранилище (список вместе с текущим событием) → подписка (с откатом
резерва), удаление — отписка → хранилище → граф (с восстановлением подписки, если запись не
удалась).

`EventControllerKeeper` проигрывает надстройку в живой компонент **после загрузки мира**:
слушает `OnEntityAdd`, откладывает работу на несколько тиков (компоненты блока появляются не
мгновенно) и вызывает тот же `AddBlocks`. Список, записанный для другого события, он не
проигрывает, а выбрасывает — через тот же `ReadOverlay`. Игровую часть списка игра
восстанавливает сама.

Тот же `EventControllerKeeper.Update` раз в секунду опрашивает подписки, которые сделала эта
сессия (`EventControllerProbe.PollStaleSubscriptions`), и сбрасывает те, чьё событие сменилось.
Опрос нужен потому, что смену события игрок делает в терминале, а не в редакторе: без него сброс
случался бы только при открытии схемы, а до тех пор контроллер срабатывал бы по старому условию.
Сначала он спрашивает у запомненного компонента его событие и сверяет с текущим — это два чтения
поля, — и только при расхождении читает хранилище и сбрасывает список. Запоминается не только
компонент, но и id блоков, которые в него подписаны (`Subscription`): список в хранилище к
моменту сброса может быть уже пуст (игрок снял последнюю привязку «x»), и брать id оттуда значило
бы оставить подписку, которую больше нечем снять. Карта — игровые объекты, поэтому `Uninstall`
её очищает (та же причина, что в R-002), а запись выбывает, когда блок исчез, подписка снята или
список опустел.

### Конвейерная сеть ([ADR-0040](adr/0040-conveyor-network.md))

У грида есть **вторая** сеть, которая тоже связывает блоки: конвейеры. Она не выражается
`Edge` и не живёт рядом с ним — у неё нет ни порта, ни ячейки тулбара, ни направления, она
не создаётся и не удаляется из редактора. Поэтому в графе это **третье хранилище**:

```csharp
public IList<ConveyorLink> Conveyors { get; }   // GridGraph
```

Три типа, и каждый отвечает на свой вопрос:

| Тип | Вопрос | Слой |
|---|---|---|
| `ConveyorSegment` | «какие два блока соединяет одна линия игры?» | `Model`, struct |
| `ConveyorLink` | «какие две **карточки** соединены на схеме?» | `Model` |
| `ConveyorNetwork` | «как из первого получить второе?» | `Model`, static |

**Почему реализация конвейеров не используется.** `MyConveyorLine`, `IMyConveyorEndpoint` и
`ConveyorLinePosition` живут в `Sandbox.Game.GameSystems.Conveyors`, а вайтлист разрешает
пространство имён точно — подпространства нет (ADR-0008). Мод с такой ссылкой компилируется и
не загружается; это уже случалось дважды. Разрешённый `IMyGridConveyorSystem` — про
перемещение предметов, а не про сеть, и `IMyConveyorTube` не объявляет ни одного члена.
Единственный доступный вид сети — **сериализованный**: `IMyEntity.GetObjectBuilder()` →
`MyObjectBuilder_CubeGrid.ConveyorLines`, заполняемый `MyGridConveyorSystem.SerializeLines`.
Проверено по IL, см. [`01-research/api-conveyor-network.md`](01-research/api-conveyor-network.md).

**Правило редукции** — то, ради чего этот раздел существует. Узлы схемы — только блоки **с
карточкой**. Обход от такого блока идёт по сырым линиям и останавливается на первом
нарисованном: пара записывается, дальше через неё не идут. Всё остальное проходится насквозь.

| Что встретилось | Что делает обход |
|---|---|
| нарисованный блок | **останавливается**: пара записывается, дальше не идёт |
| скрытый блок («Show in Grid Workspace» выключен) | проходит насквозь |
| конвейерная труба, junction | проходит насквозь (труба — ребро, а не узел) |
| блок подгрида, убранного из схемы | проходит насквозь |
| уже посещённый в этом обходе | не повторяет (петля не вешает) |
| источник обхода | не записывает как пару (петля в себя — не связь) |

Отсюда три следствия, и все они — требование:

- **скрытие не рвёт сеть.** Скрытие карточки — решение про картинку, а не про корабль; труба
  через этот блок продолжает работать в игре. То же правило уже действует для тулбара
  скрытого блока (ADR-0013) и для скрытого наблюдаемого блока (ADR-0017);
- **нарисованный junction — точка пересечения.** Он становится хабом, соседи соединены с ним,
  но не друг с другом — это и есть «точками пересечения станут блоки, уже представленные на
  схеме»;
- **ненарисованный junction невидим**, поэтому все его нарисованные листья соединяются попарно.
  Многие ко многим сводятся к минимальному набору, который ещё говорит правду, — вместо n²
  линий на грузовом корабле.

Направление отбрасывается: труба — физическое соединение, и показывать, куда именно сортер
пропускает предметы, требование не просит. Пара нормализуется (меньший id первый), поэтому
дубликаты и параллельные линии дают одну связь, а порядок списка детерминирован.

**Чтение** — `GridGraphBuilder.SyncConveyors`, в конце структурного прохода (после того как
набор нарисованных узлов окончателен). Три ограничения, и все три — про цену:

- `GetObjectBuilder` сериализует **все** блоки грида, поэтому это проход ~1.5 с, а не кадровый
  путь;
- читаются только гриды с **≥ 2 карточками**: линия не покидает свой грид, и грид с одной
  карточкой связи не даст;
- читается только при включённом режиме. Это и есть причина, по которой настройка —
  **режим схемы**, а не режим рисования: draw-time фильтр купил бы бесплатный переключатель
  ценой сериализации каждого грида дважды в секунду.

**«Не прочиталось» ≠ «нет линий».** Исчезнувший грид или исключение оставляют прежнюю сеть —
то же правило, что у прохода состояния для целостности.

**Рисунок** — труба в разрезе, и это не украшение: две линии с зазором 4 px и стенкой 1 px
(`ColConveyorPipe`, `#444455`) не могут быть спутаны с логической связью, которая есть **одна**
линия (ADR-0010). Маршрут — тот же `EdgeRouting.Build`, от правого края карточки-источника к
левому краю карточки-цели, поэтому оба вида линий выходят из карточки одинаково. Труба рисуется
**под** карточками и под логическими линиями: она физическая подложка, а не сигнал, — и концы
её маршрута лежат внутри карточек, поэтому труба выглядит входящей в блок.

Изгибы скруглены, и это единственная часть рисунка, которой нужна собственная геометрия.
Дуги в API нет вообще (`MyScreenSpaceSprite.Draw` — прямоугольники), поэтому `PipeRouter`
строит **лесенку** осевых кусков по четверть-дуге — ровно как `VgsScreenDraw.Disc` строит круг
из полос. Шаг лесенки пропорционален радиусу (`ConveyorStep`), иначе при малом радиусе
получилась бы фаска, а при большом — грубая ступень; радиус зажимается половиной прилегающего
отрезка, иначе две рельсы пересекли бы себя на коротком плече. Пропорции экрана передаются
параметром, а не читаются из слоя отрисовки, — поэтому геометрия проверяется офлайн.

Толщина стенки — **device pixels** (`VgsScreenDraw.SegmentPixels`), а не единицы раскладки:
у раскладки оси не равны (ADR-0009), и стенка в единицах была бы в `height/width` раз толще по
горизонтали, а труба не сошлась бы на изгибе. Это то же правило, что у сетки поля и границ
гридов (ADR-0032 §3).

Потолок — `MaxRenderedConveyors` (100 связей на кадр). Он ниже, чем у связей (600), и посчитан:
одна труба — это ~70 кусков на изгиб, то есть сотня труб ≈ семь тысяч квадов.

### `Vgs.Ui` — экран

| Файл | Ответственность |
|---|---|
| `VgsEditorScreen.cs` | **частичный класс**: поля, палитра, конструктор, жизненный цикл (`OnOpened`/`OnClosed`/`OpenFor`), раскладка окна (прямоугольники полос), `OnUpdate` и проходы обновления |
| `VgsEditorScreen.View.cs` | **частичный класс**: запись вида на грид — когда и куда; сами значения и правило «изменилось ли» живут в `ViewState` |
| `VgsEditorScreen.Search.cs` | **частичный класс**: что запрос *значит* для картинки — затемнение карточек, видимость линий, переход по Enter; сам ввод поля — `SearchField` |
| `VgsEditorScreen.Input.cs` | **частичный класс**: ввод — четыре режима указателя (обычный, привязка, отслеживание, меню), панорама и зум, перетаскивание, hit-test; развод правой кнопки между кликом (ручное действие) и панорамой (ADR-0041); **наведение на линию** — какая линия под указателем ([ADR-0049](adr/0049-line-highlight.md)) |
| `VgsEditorScreen.Draw.cs` | **частичный класс**: отрисовка — холст, карточки, полосы портов, рамки групп, линии, шапка и нижняя панель; ничего не читает из мира (ADR-0003). Здесь же **подсветка линий** — второй слой под связью и линией отслеживания и заливка трубы ([ADR-0049](adr/0049-line-highlight.md)) |
| `PointerGesture.cs` | единственное правило «это клик или перетаскивание»: порог `ClickSlopPixels` по каждой оси в пикселях окна (ADR-0041). **Без** ссылок на `Sandbox.*` — проверяется офлайн на четырёх формах окна |
| `NodeSelection.cs` | набор выделенных узлов: клик заменяет, Shift+клик переключает, рамка выделяет накрытые **карточки**, перенос раскрывает набор в список карточек (группа → её участники, карточка один раз). Тождество ссылок, а не равенство. **Действий над набором нет** — это состояние картинки (ADR-0048). **Без** ссылок на `Sandbox.*` — проверяется офлайн |
| `Marquee.cs` | жест рамки выделения: точка нажатия, текущая точка, порог «это уже не клик» в пикселях и нормализация прямоугольника в любом из четырёх направлений (ADR-0048). **Без** ссылок на `Sandbox.*` — проверяется офлайн |
| `SearchField.cs` | поле поиска как самостоятельный тип: запрос, каретка, выделение, прокрутка и кэш позиций; шрифт подаётся делегатом, поэтому арифметика проверяется офлайн |
| `VgsEditorScreen.Parameters.cs` | **частичный класс**: окно параметров действия (ADR-0050) — открытие из привязки и из кнопки-пера, подтверждение (проверка формы → запись в тулбар → связь в графе), правка существующей связи (`TryRewrite`), отмена, удаление одного запроса (`RemoveBarConnections`), клавиатура окна, **отчёт о покрытии** — строка в логе с id действия, аргументы которого этот билд не описывает (ADR-0052 §4) |
| `VgsEditorScreen.ParameterDialog.cs` | **частичный класс**: прямоугольники окна параметров (окно, строки полей, кнопки), его отрисовка и обработка указателя. Один метод на прямоугольник, потому что рисование и hit-test обязаны спрашивать одно и то же |
| `ViewState.cs` | состояние вида: камера, зум, четыре режима показа (скрытые блоки, субгриды, границы гридов, конвейерная сеть) и последние записанные значения; правило «когда писать» — здесь, сам вызов грида — в экране |
| `VgsPalette.cs` | палитра: все цвета редактора в одном месте. Файлы отрисовки импортируют её через `using static`, поэтому имена остались прежними (`ColNode`, `ColDamage`, …) |
| `GridFrames.cs` | рамка грида и её вкладка с именем: какие карточки охватывает, где углы в пикселях окна, как из них выводятся четыре края. **Без** ссылок на `Sandbox.*` — проверяется офлайн ([ADR-0032](adr/0032-construct-graph.md)) |
| `CanvasRect.cs` | прямоугольник холста (окно минус шапка и нижняя панель) и единственный предикат «этот прямоугольник на холсте?» для отрисовки, прохода состояния, рамок гридов и их вкладок. Координаты внутри — **относительно холста**, а не окна: сравнение абсолютной координаты с *размером* холста резало низ на высоту шапки (R-015). **Без** ссылок на `Sandbox.*` — проверяется офлайн |
| `PortGeometry.cs` | геометрия карточки, её ряда кнопок, полос портов (исходящих — по порту, входящих — по запросу, ADR-0050), кнопки отслеживания и рамки группы — единственный источник правды для отрисовки **и** hit-testing; объявляет `CardButtonKind`; решает, рисуется ли линия отслеживания (`ShouldDrawWatchLine` — самопросмотр её не рисует, ADR-0036) |
| `EdgeRouting.cs` | маршрут линии: ортогональные отрезки, приход в цель слева; петля вокруг карточки для связи блока с собой (ADR-0036); скругление маршрута в путь связи — изгиб от середины отрезка до середины следующего (ADR-0046) |
| `LineRaster.cs` | путь линии → точки в пикселях safe-области: 2 × 2 px на обеих осях и на любом зуме, ритм отсчитывается по длине пути, поэтому изгиб его не сбивает. Один растеризатор на оба вида линии — связь (зазор 1 px) и наблюдение (зазор 2 px). Там же пунктирный прямоугольник рамки выделения: четыре ребра, каждое начинается и заканчивается штрихом, шаг считается из длины ребра. **Без** ссылок на `Sandbox.*` — проверяется офлайн ([ADR-0046](adr/0046-curved-connection-lines.md), [ADR-0047](adr/0047-dotted-connection-lines.md), [ADR-0048](adr/0048-multi-node-selection.md)) |
| `PipeRouter.cs` | две рельсы трубы по тому же маршруту: зазор, пропорции, скругление изгиба лесенкой осевых кусков (ADR-0040); третьим выходом — **центральная линия** для заливки подсвеченной трубы ([ADR-0049](adr/0049-line-highlight.md)). **Без** ссылок на `Sandbox.*` — проверяется офлайн |
| `LineHit.cs` | расстояние от указателя до линии **в device pixels**: по скруглённому пути (связь, линия отслеживания) и по маршруту (труба), с зажатой проекцией и габаритным предтестом. Единственное правило «указатель на линии» ([ADR-0049](adr/0049-line-highlight.md)). **Без** ссылок на `Sandbox.*` — проверяется офлайн |
| `IconPlacement.cs` | где иконка ляжет в пикселях окна: перевод из единиц раскладки, снап до целых пикселей, обрезка по краю полотна — **без** ссылок на `Sandbox.*`, поэтому проверяется офлайн |
| `VgsIcons.cs` | разрешение материалов иконок на сессию и решение «иконка или прежняя геометрия»; порога размера нет — форма контрола не зависит от зума |
| `VgsIconAtlas.g.cs` | **сгенерированная** таблица: перечисление `VgsIcon`, имена материалов, размер текстуры, `TextureScale`. Правится только `tools/build-icon-atlas.ps1` |
| `VgsScreenDraw.cs` | единственная точка вызова API отрисовки; ресурсы; перевод координат клиента ↔ safe-GUI; `Icon` — единственное место, где рисуется спрайт из текстуры |

`VgsEditorScreen` наследует `MyModScreen` и переопределяет его хуки
(`OnOpened`/`OnUpdate`/`OnHandleInput`/`OnDraw`/`OnClosed`). `VgsScreenDraw` изолирует
`MyScreenSpace*`, держит текстуру-кисть и переводит координаты: если завтра понадобится
другая реализация отрисовки, меняется один файл.

#### Компоновка окна ([ADR-0023](adr/0023-workbench-bar-modes-and-search.md))

Три полосы, и у каждой одна работа:

```
шапка          Workspace  // blocks: N · groups: N · links: N · zoom: N%   <имя грида справа>
холст          карточки, порты, линии (отсекается по экрану, но молча)
нижняя панель  ┌ подсказка/отказ — одна строка над контролами
               └ [sliders] [Search blocks ........... x]                     [Закрыть]
```

Точнее, снизу вверх: полоса подсказки (одна строка, 0.5 кегля) занимает верх нижней панели, а
кнопка режимов, поле поиска и «Закрыть» стоят одной строкой, отцентрованной в панели так же,
как «Закрыть». Холст — всё, что выше шапки и ниже панели.

- **Шапка** несёт имя окна (`Workspace`) и счётчики — оба на одной базовой линии; ширина
  заголовка измеряется один раз и кэшируется по клиентской ширине, потому что текстового
  потока в API нет и вторую строку приходится ставить руками, а мерить шрифт каждый кадр
  незачем.
- **Нижняя панель** — управление, а не описание: кнопка режимов, поле поиска, «Закрыть».
  Единственная строка текста над ними: подсказка к тому, что под указателем, объяснение
  отказа, состояние привязки/отслеживания — или ничего.
- **Кнопка режимов** открывает меню **над собой** (внизу места нет) с тремя переключателями:
  «Show/hide invisible» — показывает блоки с выключенным «Show in Grid Workspace», не меняя флаг на блоке;
  «Show subgrids» — включает блоки субгридов в граф; «Show grid borders» — рисует рамки гридов
  с вкладками имён; «Show conveyor network» — строит и рисует конвейерную сеть (ADR-0040).
  Все четыре живут в `ViewState` и сохраняются вместе с камерой
  ([ADR-0022](adr/0022-view-state-lives-on-the-grid.md), [ADR-0032](adr/0032-construct-graph.md),
  [ADR-0040](adr/0040-conveyor-network.md)).
- **Поиск** фильтрует карточки правилом терминала (`Model/BlockSearch`): AND по термам,
  подстрока в **имени блока или в его отображаемом типе**. Несовпавшая карточка рисуется с
  альфой `VgsConstants.SearchDimOpacity` вместе со своими полосами и подписями; линия гаснет,
  только если затемнены **оба** её конца. Таблица `_searchTerms` — разбор термов —
  пересобирается на изменение запроса, а не на кадр (R010). Enter ставит камеру на первое
  совпадение; нет совпадения — ничего не происходит.
- **Два поля — это поля терминала, а не подтип.** Терминал ищет по имени и по *локализованному
  названию типа* («Timer Block», «Таймер»), которое берёт из тултипа строки списка. Поэтому
  `Node` несёт `TypeDisplayName`, прочитанный у блока при построении графа
  (`IMyCubeBlock.DefinitionDisplayNameText`), а не `SubtypeId`: подтип — идентификатор движка
  («LargeBlockTimer»), которого игрок не набирает и которого нет в его языке. **Это же поле
  рисуется второй строкой карточки** ([ADR-0043](adr/0043-card-type-line-is-localized.md)):
  подпись и поиск обязаны называть блок одинаково, поэтому второго источника названия типа нет.
- **Клавиатура**: поле всегда является целью ввода; символы читаются из `IMyInput.TextInput`
  раз в кадр (игра подменяет буфер при чтении, поэтому каждый символ приходит ровно однажды —
  [`01-research/api-terminal-search.md`](01-research/api-terminal-search.md) §2). Поверх этого —
  своё текстовое поле (ADR-0024): каретка с миганием, выделение (якорь + каретка),
  стрелки/Home/End/Shift, Ctrl+A/C/X/V. Раскладка каретки и выделения считается **при
  изменении**, а не каждый кадр: это вызовы метрик шрифта.

#### Выделение узлов ([ADR-0048](adr/0048-multi-node-selection.md))

Выделение — **набор** узлов (`Ui.NodeSelection`), а не одна ссылка. Набор меняется левой
кнопкой в обычном режиме и никуда не сохраняется: это состояние картинки.

```
клик по карточке        ──► набор = {эта карточка}
Shift+клик по узлу      ──► узел переключается в наборе
клик по пустому месту   ──► набор = {}            + Marquee.Begin(additive: false)
Shift+клик по пустому   ──► набор не меняется      + Marquee.Begin(additive: true)
протяжка (порог 4 px)   ──► DrawMarquee: пунктирный прямоугольник 1 px
отпускание              ──► SelectInRect: в набор попадают накрытые карточки
Esc                     ──► сначала снимает набор, потом закрывает редактор
```

Попадание считается по **прямоугольнику карточки** (`PortGeometry.CardRect`), строгим
пересечением: касание края не выделяет. Группа рамкой не выделяется — её рамка нарисована
вокруг карточек участников, поэтому накрывается всегда, и выделять её значило бы, что любой
маркиз над сгруппированной карточкой переносит весь кластер. Группа выделяется Shift+кликом
по полосе имени — своему единственному адресуемому месту (ADR-0016).

Перенос набора — то же правило, что у группы (ADR-0016): нажатие на **любой** выделенный узел
при `Count > 1` начинает перенос всего набора одним смещением. Список карточек
(`NodeSelection.CollectDragCards`) собирается **один раз, в начале переноса**: выделенная
группа даёт своих нарисованных участников, карточка, выбранная и сама, и как участник
группы, попадает в список один раз (иначе смещение удвоилось бы). Позиции пишутся на блоки
при отпускании (ADR-0012).

**Набор — не цель действия.** Bind, watch, jump, переключатель питания и ручное меню
действий работают с одним узлом под указателем; массового выполнения над набором нет и не
планируется (ADR-0048 §Decision).

Рамка рисуется в **пикселях**: толщина 1 px, штрих и промежуток по 4 px при любом зуме —
по той же причине, что и линия связи (ADR-0046 §3): оси раскладки не равны по длине
(ADR-0009). Размер прямоугольника при этом в единицах графа и масштабируется вместе со схемой.

#### Подсветка линий ([ADR-0049](adr/0049-line-highlight.md))

Подсветка отвечает на вопрос «какие линии относятся к тому, на что я смотрю», поэтому она —
**второй слой под линией**, а не другая линия: цвет, вес и ритм линии не меняются.

```
указатель на линии ──► UpdateHoveredLine ──► _hoverEdge / _hoverConveyor / _hoverWatch*
узел в выделении  ──► NodeSelection.Contains(конец) ──► IsEdgeHighlighted / …
                                                        │
                        DrawRoundedRoute / DrawDottedRoute: белая линия (2 px, зазор 0)
                        по тому же пути — ДО линии; линия ложится поверх, белое видно в зазорах
                        DrawConveyors: заливка трубы (центральная линия, 6 px) — ДО рельсов
```

Три правила, которые нельзя нарушить по отдельности:

- **наведение считается по тому, что нарисовано.** Связь и линия отслеживания рисуются по
  скруглённому пути (`EdgeRouting.Round`), а не по маршруту, и на изгибе они расходятся на
  полсегмента. Поэтому `LineHit.DistancePixels` меряет путь; по маршруту меряется только труба,
  у которой изгиб — лесенка в паре пикселей от него.
- **допуск — в device pixels** (`LineHoverPixels` = 4 px), потому что оси раскладки не равны
  (ADR-0009): допуск в единицах раскладки был бы эллипсом и «худел» бы с зумом.
- **одна геометрия на два прохода.** `TryBuildEdgeRoute`, `TryBuildWatchRoute`,
  `TryBuildConveyorRoute` строят маршрут в `_route` для рисования и для наведения; копия
  «откуда выходит линия» в hit-test разошлась бы с картинкой при первой же правке петли.

Порядок разрешения обратный порядку рисования — отслеживание, связь, труба, — и линия
разрешается только когда под указателем нет карточки или контрола: всё это нарисовано поверх
линий, и подсветка линии под карточкой читалась бы как подсветка карточки.

Подсветка по выделению — **в обе стороны**: у выделенного узла подсвечены и линии, которые он
запускает, и линии, которые запускают его. Группа участвует на равных (её конец — имя группы),
конвейерная связь — тоже: она физически принадлежит блоку.

#### Текстовое поле (ADR-0024)

Движковое поле взять нельзя: `Sandbox.Graphics.GUI.MyGuiControlTextbox` живёт в
`Sandbox.Graphics`, которого нет в вайтлисте (проверено `check-whitelist.ps1 -Dump`), — ссылка
компилируется и отклоняется игрой при загрузке. Поэтому поле своё, но его **логика** вынесена в
`Vgs.Model.TextEditor`, где проверяется офлайн.

```
клавиши/клик ──► VgsEditorScreen ──► TextEditor (текст, каретка, якорь)
                      │                    │
                      │                    └─► OnSearchTextChanged ──► BlockSearch.SplitTerms
                      ├─► TextClipboard (Ctrl+C/X/V)
                      └─► EnsureFieldLayout ──► метрики шрифта (только при изменении)
                                │
                                └─► DrawSearchField: заливка, выделение, текст, каретка
```

Буфер обмена моду **недоступен**, и это проверено отказом загрузки, а не рассуждением:
`VRage.MyVRage.Platform.System.Clipboard` выглядит легально (всё в разрешённом пространстве имён
`VRage`), но игра отвергает `IVRageSystem`, `IVRagePlatform` и `MyVRage` как `is prohibited`, и мод
не грузится целиком. Прямые пути (`VRage.Platform.Windows.Forms`, `Sandbox.Graphics`) закрыты
своими пространствами имён. Поэтому `TextClipboard` держит строку в памяти и **не ссылается ни на
один тип игры** — ломаться нечему ([ADR-0027](adr/0027-clipboard-is-own-buffer-only.md)).

Общее правило, выведенное из этого: **разрешение типа не выводится из разрешения пространства
имён для корневого `VRage`** — типы там перечислены поимённо. Подпространства (`VRage.Utils`)
ведут себя иначе и разрешены как пространства.

`PortGeometry` существует потому, что прямоугольник рисуется и проверяется на попадание в
двух разных местах. Две копии этой арифметики расходятся, и тогда порт рисуется в одном
месте, а ловится в другом — дефект, невидимый в коде и очевидный в игре. Поэтому геометрия
живёт в одном типе, и оба потребителя зовут его.

По той же причине там лежит `CentredTextTop`: «подпись порта всегда по центру высоты полосы» —
это требование к поведению, а не украшение, и путь отрисовки офлайн не проверяется. Одна
формула, вынесенная из `DrawPortBar`, делает его утверждением в `tests/OfflineChecks.cs`.

Так же устроена **рамка группы** ([ADR-0015](adr/0015-block-groups.md)).
`PortGeometry.TryGroupFrame` — чистая функция от карточек участников: наименьший
прямоугольник, содержащий их и их стаки портов, плюс отступ и полоса имени.

### Иконки контролов ([ADR-0019](adr/0019-icons-atlas.md), [ADR-0020](adr/0020-card-button-row.md), [ADR-0021](adr/0021-jump-to-block-terminal.md), [ADR-0023](adr/0023-workbench-bar-modes-and-search.md))

Восемь контролов — точка порта, **пять** кнопок карточки (переход в терминал, привязка, «да»,
«нет», отслеживание), «x» удаления на полосе порта и кнопка режимов в нижней панели —
рисуются иконками Font Awesome из текстур `Textures/VGS/Icons/<Id>.png`, по файлу на иконку.
Разделение ответственности здесь такое же, как у геометрии:

```
tools/build-icon-atlas.ps1 ──► Icons/*.png + GuiTextures.sbc + VgsIconAtlas.g.cs + docs/icons/*.png
                                        (сборка, вне мода)

VgsEditorScreen ──► VgsScreenDraw.Icon(VgsIcon.Xmark, центр, высота, цвет, ...)
                              │
                              ├─► VgsIcons          материал разрешён и загружен?
                              ├─► IconPlacement     единицы раскладки → целые пиксели окна
                              └─► MyScreenSpaceSprite.DrawPixels
```

**Какая иконка где** записано в каталоге [`07-icon-catalog.md`](07-icon-catalog.md): таблица
«изображение · описание · идентификатор · где используется». Каталог — не источник истины о
наборе (источник — `tools/icon-atlas/icons.txt`), а его отражение с ответом «где
используется»; расхождение ловит `tools/check-icon-catalog.ps1`, входящий в
`offline-check.ps1`. Причина, по которой каталогу нужен шлюз, та же, по которой шлюзу
вайтлиста нужна самопроверка: документ, который никто не сверяет, расходится с кодом за один
этап.

Почему `IconPlacement` — отдельный тип, а не пять строк внутри `VgsScreenDraw`: это
единственная часть рисования иконки, которую можно проверить без игры, и единственная, где
ошибка не падает, а тихо сдвигает картинку. Офлайн-шлюз утверждает три вещи: центр попадает
в пиксели **окна**, а не safe-GUI; дробный центр всё равно даёт целый прямоугольник; обрезка
по краю режет `destination` и `source` одной и той же долей — и по горизонтали, и по
вертикали отдельными случаями.

`VgsIcons` держит единственное решение «иконка или прежняя фигура». Оно принимается по двум
причинам — имя материала не разрешилось (не подхватился `GuiTextures.sbc`) или текстура ещё не
загружена рендерером, — и в обоих случаях вызывающий код просто рисует старое. Поэтому
отсутствие текстур не может убрать контрол с экрана. Проверка загрузки — не педантизм:
`DrawPixels` отбрасывает спрайт с незагруженной текстурой молча, и без неё контрол на несколько
первых кадров исчез бы.

Размера в этом решении нет намеренно: третье условие («контрол меньше своего порога
читаемости») существовало и было убрано после приёмки в игре. Оно заставляло контрол менять
форму при зуме — кольцо превращалось в диск, «квадрат с плюсом» в «+» в круге, — и это
читалось как дефект. Теперь иконка рисуется на любом масштабе; подробно —
[ADR-0019](adr/0019-icons-atlas.md) §6.

Почему по файлу на иконку, а не общий атлас, — измерение, а не вкус: на грубых мипах
промежуток между ячейками схлопывается и соседняя иконка подмешивается. Числа и решение —
в [ADR-0019](adr/0019-icons-atlas.md) §4 контекста.

### Ряд кнопок карточки ([ADR-0020](adr/0020-card-button-row.md), [ADR-0021](adr/0021-jump-to-block-terminal.md))

Все контролы, принадлежащие **карточке**, живут в одном горизонтальном ряду вдоль её правого
края, вертикально центрированном, с одним диаметром на всех. Набор кнопок — функция от того,
что блок умеет: кнопка перехода есть у любого блока (у группы её нет — у группы нет терминала),
остальные — по тулбару (умеет управлять? одна группа → одиночная привязка, две → пара «да»/«нет»)
и по типу блока (event controller → отслеживание). Порядок задан **данными** — массив
`PortGeometry.RowOrder`: переход, отслеживание, «да», «нет», одиночная привязка. Карточка
показывает подпоследовательность этого массива, поэтому кнопка не переезжает, когда у карточки
появляется соседняя, а новая кнопка добавляется одной строкой в массив и одной веткой в
`HasCardButton`.

Количество, позиция и «какая кнопка на позиции i» — три обхода этого массива, а не три копии
порядка: `CardButtonCount`, `CardButtonIndex`, `TryCardButtonAt`. Для пути отрисовки есть
однопроходная форма — `GetCardButtons` заполняет буфер вызывающего, `CardButtonRectAt` ставит
кнопку по её позиции; это единственное место, где ряд обходится один раз на карточку, а не
`n + 1` раз.

Три следствия, важные для слоёв:

- **геометрия ряда — в `PortGeometry`**, как и у портов: `TryCardButtonRect` (где кнопка),
  `CardButtonRectAt` (то же по позиции в ряду), `TryCardButtonRow` (где ряд), `TryCardTextSpan`
  (докуда доходит подпись карточки), `HitCardButton` (что под указателем). Отрисовка и hit-test
  берут прямоугольники оттуда же, поэтому разойтись не могут. `TryCardTextSpan` существует
  отдельно, потому что «подпись обрезается по ряду минус зазор» — это арифметика с осью: зазор
  задан вертикальной длиной и обязан пройти через поправку на пропорции, иначе на 16:9 он в 1,8
  раза шире, чем задумано;
- **иконка выбирается в UI**, а не в геометрии: `VgsEditorScreen.IconOf` — таблица
  «кнопка → иконка», и именно её сверяет шлюз каталога. Геометрия не знает про `VgsIcon`;
- **hit-test один на ряд.** Кнопки одного ряда при допуске нажатия перекрываются, поэтому
  побеждает **ближайший центр**, а не первая проверенная кнопка: иначе клик в зазор попадал бы
  в кнопку, зависящую от порядка обхода.

Что осталось от прежней модели: `PortGroup` (какую группу тулбара заполняет связь) и
`Node.CanDriveBlocks` (есть ли вообще кнопки привязки). Что появилось: `CardButtonKind` —
пять видов, названных по действию, а не по глифу; четыре из них жесты на схеме (привязка, «да»,
«нет», отслеживание), пятый — переход в терминал блока, который схему закрывает (ADR-0021).
Именно это позволяет менять иконку, не трогая ни геометрию, ни режимы указателя, — то, что и
требовалось.

Почему PNG, а не DDS, — тоже измерение, и оно противоречит первой реализации: DXT5-файлы были
корректны на диске по двум независимым декодерам и всё равно рисовались в спрайте непрозрачным
квадом, а PNG того же содержания работает. Таблица проверок — в
[ADR-0019](adr/0019-icons-atlas.md) §1. Практическое следствие для этого слоя: `VgsIcons` не
знает о формате ничего, он разрешает материал по имени, поэтому смена контейнера не тронула ни
одной строки рисования — изменились только сборщик ассетов и `GuiTextures.sbc`.

### Повреждение на карточке ([ADR-0028](adr/0028-block-damage-band.md))

Единственный пока потребитель снимка состояния — красная полоса на карточке. Правило: **ширина
полосы — это повреждение, а не целостность**, она прижата к правому краю и занимает всю высоту
карточки.

| Целостность | Красная часть карточки |
|---|---|
| 100% (в пределах допуска `BlockState.IntegrityTolerance`) | нет полосы |
| 70% | правые 30% |
| 50% | правая половина |
| 10% | правые 90% |
| 0% | вся карточка |

Геометрия — `PortGeometry.TryDamageRect` (графовые единицы, как `CardRect`) плюс
`PortGeometry.FitDamageBand` (нижняя граница ширины в один пиксель экрана и зажим по карточке —
в графовых единицах, потому что слой отрисовки один знает, сколько стоит пиксель), значение —
`Node.State` (никогда не из мира), порог «повреждён» — `BlockState.IsDamaged`, тот же, что у
остального кода. Отрисовка в `DrawNode` идёт в фиксированном порядке: заливка карточки →
**полоса** → рамка → подписи → полосы портов → кнопки. Так полоса композитится поверх
непрозрачной заливки (иначе её 50% прозрачности смешивались бы с холстом по-разному), не
перекрывает рамку выбора/наведения и не мешает читать подписи и контролы. Полосы портов под
карточкой не красятся: карточка — то, чем блок *является*, полосы — то, что он *делает*.
У группы полосы нет: у неё нет целостности.

**Порога по величине повреждения нет** — сколько процентов, столько и красного. «70%» и «50%» в
требовании были примерами доли, а не уровнями включения. Первая версия кода рисовала полосу на
всю ширину экрана: защита от субпиксельной ширины сравнивала нормализованную величину с `1f`,
то есть с целым экраном. Правило вынесено в `FitDamageBand` именно поэтому — его можно
проверить офлайн, а в слое отрисовки оно было непроверяемым.

### Переключатель питания блока ([ADR-0035](adr/0035-block-power-toggle.md))

Второй потребитель снимка состояния и **третья часть карточки**. У карточки три части, и у
каждой есть имя в словаре (`docs/06-terminology.md` §4, «Три части карточки») — по вопросу, на
который она отвечает, а не по стороне, на которой стоит:

| Часть | Вопрос | Что в ней | Геометрия |
|---|---|---|---|
| **колонка состояния** | что блок делает **сейчас** | тумблер питания в верхней полосе (40% внутренней высоты, с зазором 2 px от граней) и **виджет состояния** в нижней (60% минус зазор 2 px под тумблером) | `PortGeometry.TryStateColumnRect`, `TryPowerSwitchRect`, `TryStateWidgetArea` |
| **шильдик** | что блок **есть** | имя, отображаемый тип (локализованный, ADR-0043), описание режима | `PortGeometry.TryCardTextSpan` |
| **ряд кнопок** | что можно **сделать** со схемой | переход, отслеживание, привязки | `PortGeometry.TryCardButtonRow` |

Три части не смешиваются: у каждой своё правило размера. Поэтому добавление тумблера не сдвинуло
ряд кнопок ни на пиксель, а шильдик просто начался правее — и поэтому же имя «колонка состояния»
не привязано к слову «питание»: нижнюю полосу занял **виджет состояния**, который к питанию
отношения не имеет (ADR-0042, ниже).

| Состояние `BlockState.Power` | Иконка | Цвет |
|---|---|---|
| `Unknown` — проход состояния ещё не дошёл | не рисуется | — |
| `On` | `toggle-on` | `ColPowerOn`, зелёный |
| `Off` | `toggle-off` | `ColPowerOff`, серый `#777777` |
| `Unavailable` — повреждён ниже критической целостности | `toggle-off` | `ColPowerUnavailable`, тёмно-красный `#770000` |

**Третье читаемое состояние — не «выключен».** Блок ниже критической целостности продолжает
репортить `Enabled == true` и просто не работает (`MyComponentStack.get_IsFunctional` =
`IsBuilt && Integrity > MaxIntegrity * CriticalIntegrityRatio`, прочитано IL); взяв флаг в лоб,
схема показала бы сломанный блок зелёным. Поэтому `BlockState.SetPower(enabled, functional)`
даёт функциональному флагу приоритет, а не флагу питания.

**Колонка отступает от карточки со всех четырёх сторон.** По горизонтали — `CardButtonMargin`
(4 px, как ряд кнопок от противоположного края), по вертикали — `PowerSwitchPadding` (2 px
сверху и снизу). Внутренняя коробка колонки — `PortGeometry.PowerSwitchContentHeight` — это
высота карточки минус оба отступа, и полосы 40/60 делят именно её: без этого нижняя полоса
оказалась бы короче своей доли, а иконка легла бы на верхнюю грань.

**Ширина не константа.** `PortGeometry.PowerSwitchInkWidth` = высота полосы × `InkAspect`
иконки, а `InkAspect` — новое число, которое сборка иконок пишет в сгенерированную таблицу
рядом с `TextureScale` (чернила тумблера 576 × 384 → 1,5). Отсюда полоса в 40% внутренней высоты
даёт 60% её же ширины, и перерисованная иконка меняет колонку вместе с собой, а не расходится с
литералом. Отступы при этом отнимают у тумблера около 10% размера: 20,9 × 14,0 px вместо
23,3 × 15,6 px при 1080p.

**Шильдик начинается за колонкой, и это одна функция.** `PortGeometry.TryCardTextSpan` уже
отвечал «где начинается имя»; теперь он же учитывает колонку. Второй копии этого счёта нет — иначе имя
оказалось бы на полконтрола левее нарисованного. У карточки без `IMyFunctionalBlock` колонки
нет и имя стоит на старом месте.

**Статус читается тем же проходом, что и повреждение** (`GridGraphBuilder.RefreshState`, ~2 Гц
по видимым карточкам): блок резолвится один раз на узел, оба значения читаются из него и
пишутся раздельно. Поэтому внешнее событие — выключение блоком-таймером, скриптом, другим
игроком — отражается на схеме без переоткрытия редактора. Запись из редактора идёт в обратном
порядке: сначала отказ (`Unavailable` → фраза, `Unknown` → тихий выход), затем сеттер `Enabled`
блока, затем **перечитывание блока** и запись прочитанного в карточку. Переключить кэш вместо
перечитывания значило бы догадаться о мире, а запрос на удалённом клиенте может быть отвергнут
сервером (ADR-0018).

**Цена — имя, и она посчитана.** У четырёхкнопочной карточки контроллера с колонкой состояния
имя держит 50% ширины на 16:9, 45% на 16:10 и 34% на 4:3; аспект, при котором держится пол 30%,
падает с 1,00 до 0,79. Квадратное окно пол не держит (11%) — это записано как цена колонки, а не
спрятано. Числа печатает `tools/icon-atlas/card_layout_preview.py` из тех же констант, что и
рисует [`01-research/images/card-layout.png`](01-research/images/card-layout.png).

**«Шире — значит больше» не подтвердилось.** Тумблер на 9% шире кнопки ряда, но на 28% ниже
её (23,3 × 15,6 px против 21,5 × 21,5 px), то есть по площади меньше. Это следствие 40% и
аспекта 1,5, а не ошибка; картинка с измерением —
[`01-research/images/power-switch-preview.png`](01-research/images/power-switch-preview.png).


### Виджет состояния ([ADR-0042](adr/0042-state-widget.md))

Четвёртый потребитель снимка состояния и **содержимое нижней полосы** колонки, которую ADR-0035
оставил пустой. Виджет — не иконка: величину несёт **размер**, поэтому у него своя геометрия и
свой словарь (три формы нарисованы, четвёртая — часы — состоит из стрелок).

| Форма `StateWidgetKind` | Категории | Что читает `StateWidgetProbe` |
|---|---|---|
| `FillBar` | грузовой контейнер | `GetInventory(0).CurrentVolume / MaxVolume` |
| `Battery` | батарея | `CurrentStoredPower / MaxStoredPower` + `ChargeMode` |
| `Clock` | таймер | `1 − CurrentTime / Delay` из object builder блока |
| `Production` | refinery, assembler | две инвентари, `InputInventory` и `OutputInventory` |

**Форма приходит из игры, а не из имени подтипа.** `StateWidgetProbe.KindOf(block)` отвечает по
интерфейсу — `IMyCargoContainer`, `IMyBatteryBlock`, `IMyTimerBlock`, `IMyRefinery`, `IMyAssembler` —
и `GridGraphBuilder.CreateNode` передаёт ответ в конструктор узла. Это то же правило, что для
«может ли блок управлять другими» (`IMyToolbarProvider`, ADR-0011) и «это контроллер событий»
(`IMyEventControllerBlock`, ADR-0017).

Первая редакция брала форму из `CategoryDescriptor.Widget`, а категорию — из подстроки подтипа,
и искала `"cargocontainer"`; **ни один** грузовой контейнер в игре так не называется
(`LargeBlockLargeContainer`, `SmallBlockSmallContainer`, …), поэтому все двадцать подтипов с
`typeId = MyObjectBuilder_CargoContainer` попадали в `Unknown` и виджета не имели
(ADR-0042 §1, §7). Таблица описателей осталась **запасным путём** для модифицированного блока,
чей подтип таблица знает, а интерфейсы не входят в пятёрку.

**Форма входит в узел через конструктор, потому что по ней выделяется состояние.** `BlockState`
создаётся с `StateWidget.MetricCount(widget)` слотами; форма, пришедшая сеттером, писала бы в
массив без места, а `SetWidget` молча игнорирует индекс, для которого нет слота. Новый блок с
заполненностью — по-прежнему строка таблицы и, если у него свой интерфейс, одна ветка в `KindOf`.

**«Колонка есть» отделено от «тумблер есть».** `HasStateColumn` = тумблер **или** виджет; именно
он сдвигает шильдик (`TryCardTextSpan` спрашивает `TryStateColumnRect`). Грузовой контейнер —
не функциональный блок, тумблера у него нет, а колонка есть; пока эти два вопроса были одним,
его имя начиналось бы под собственным виджетом. Ширина колонки — `StateColumnWidth` =
max(ширина иконки тумблера, `0,6` внутренней высоты): нижняя полоса — `0,6` той же высоты.

**Зазор под тумблером берётся из полосы виджета, а не из полосы тумблера.** `TryStateWidgetArea`
вычитает `WidgetTopGap` (2 px) из высоты полосы, поэтому два прямоугольника не соприкасаются:
раньше иконка тумблера, чья краска занимает свою полосу целиком, ложилась на верхнюю грань
виджета. Тумблер — контрол с зафиксированным размером и hit-test'ом (ADR-0035), виджет — чтение;
уменьшается чтение. Область становится чуть шире, чем выше (20,9 × 18,9 px при 1080p), и формы
**вписываются** в неё: лицо часов берёт меньшую сторону, бары — данную высоту.

**Чтение — одним типом в `Vgs.Game`.** `StateWidgetProbe.TryRead(block, kind, out reading)`
получает **форму** от узла, а не подтип, и по ней выбирает члены API. Каждое чтение — в своём
`try`; отказ возвращает `false`, и карточка сохраняет прошлую картинку, потому что ноль соврал бы
про контейнер, который не удалось прочитать. «Показывать нечего» — это **успешное** чтение с
`Active == false` (остановленный таймер), и оно отличается от отказа.

**Запись — один вызов.** `BlockState.SetWidget(reading)` пишет активность, акцент и оба числа
сразу: раздельные сеттеры позволили бы кадру нарисовать новый бар со старым акцентом. Значение
зажимается в 0..1 в `SetMetric`, а не у читателя, — тогда геометрия просто умножает.

**Не рисуется в трёх случаях, и колонка при этом не двигается:** у категории нет формы; блок
выключен (`CanBeEnabled && Power != On` — у блока без тумблера `Power` навсегда `Unknown`, и это
не «выключен»); блок не сообщает чтения. Ширина колонки выводится из типа блока, а не из
состояния, поэтому имя не прыгает вбок, когда контейнер опустел.

**Два новых примитива отрисовки.** Экрана нет ни у `Segment` (только осевые отрезки), ни у
`Disc` для контура, а поворот квада моду недоступен (пивот не документирован). Поэтому стрелка
часов — `VgsScreenDraw.ThickSegment`: лесенка квадратов по одному на пиксель вдоль длинной оси,
тот же способ, что круг в `Disc` и изгиб трубы в `PipeRouter`. Циферблат — `VgsScreenDraw.Ring`,
кольцо из 24 таких отрезков.

Числа и «что править, чтобы сдвинуть» — [`08-card-layout.md`](08-card-layout.md) §2.3; рисунок
четырёх форм — [`01-research/images/card-widget-zoom.png`](01-research/images/card-widget-zoom.png),
полный лист — [`01-research/images/card-layout.png`](01-research/images/card-layout.png).


### Пауза ([ADR-0029](adr/0029-editor-does-not-pause.md))

Экран **не** ставит игру на паузу (`PausesGameInSinglePlayer = false`). Пауза останавливает
симуляцию (`MySession.Update` выходит до `UpdateComponents`), а значит при открытом редакторе
блоки не получают урон, не строятся и не разрушаются — то есть «живая схема» физически не
может показать изменение. В мультиплеере паузы не было никогда, так что один экран вёл себя
по-разному в двух режимах.

Следствие для часов: экран по-прежнему считает свои кадры в `OnUpdate` (`_screenFrame`), и это
теперь единственная верная привязка — она работает и с паузой, и без неё (ADR-0026 остаётся в
силе, но перестаёт зависеть от паузы). `UpdateOnPause` у сессионного компонента **не**
включается: это вернуло бы работу мода за меню паузы.

`PortGeometry.LayoutGroupFrames` применяет результат к узлам. Вызывается после построения
графа, после каждого `Refresh` и после изменения связей — и никогда из `OnDraw`: рамка
производна от позиций, но пересчитывать её каждый кадр значило бы делать работу, которую никто
не просил. При перетаскивании карточки пересчитываются только рамки групп, в которые она
входит (`LayoutFramesContaining`), иначе стоимость одного движения указателя росла бы с числом
групп на корабле. При переносе группы пересчитываются **все** рамки (`LayoutGroupFrames`):
участник может входить в несколько групп, и рамки остальных обязаны сдвинуться вместе с ним;
стоимость ограничена `VgsConstants.MaxGroupsScanned` и платится только пока указатель движется.

**Рамка не хранит позиции** — хранить её негде, у группы нет сущности. Поэтому перенос группы
([ADR-0016](adr/0016-group-drag.md)) сделан не как перенос рамки, а как **одно смещение,
применённое ко всем нарисованным участникам** (`GridGraph.OffsetGroupMembers`): взаимное
расположение карточек сохраняется точно, рамка встаёт на новое место сама, а позиции
записываются на блоки при отпускании кнопки — по одной на участника, как у карточки
([ADR-0012](adr/0012-layout-lives-on-the-block.md)). Ручка переноса — только полоса имени
(`PortGeometry.HitGroupTitle`); внутренность рамки и промежутки между карточками остаются
холстом.

`HitNode` для группы смотрит **только полосу имени и полосы портов**, не внутренность рамки:
внутренность принадлежит карточкам участников, а промежутки между ними — холсту. Иначе
панорамирование по сгруппированному узлу стало бы невозможным, а клик по карточке выбирал бы
группу вместо блока.

`SyncScreenSpace()` вызывается первой строкой `OnDraw` и `OnHandleInput` и обновляет
аффинное преобразование `layout → API` (формула — [ADR-0009](adr/0009-screen-coordinate-space.md)).
Пропустить его нельзя: `Measure` и `TryCursor` работают в тех же единицах, что и примитивы.

Почему холст рисуется вручную, а не собирается из контролов: штатных контролов в
разрешённом API нет вообще, а графу нужны панорама, зум и hit-testing в **пространстве
графа**. Даже кнопка «Закрыть» — прямоугольник с проверкой попадания
(`HandleCloseButton`), потому что `CloseButtonEnabled = false` отключает встроенную кнопку
игры.

Координаты: раскладка редактора — нормализованные 0..1 **клиентской области** (по ширине
окна и по высоте окна). `MyScreenSpace*` же работает в safe-GUI прямоугольнике
`min(W, 4H/3)` по центру, поэтому `VgsScreenDraw` переводит координаты в него и обратно
(курсор), а `MyScreenSpaceText.Measure` возвращает нормализованный размер, который делится
на тот же масштаб. Детали и формула — [ADR-0009](adr/0009-screen-coordinate-space.md),
проверенные факты об API — [01-research/api-screen-space.md](01-research/api-screen-space.md).

Отсечение по холсту сделано пересечением прямоугольников (`RectClipped`), а не
`MyScreenSpace.UsingScissorRectangle`: scissor пишет push/pop в общую очередь отрисовки, и
ошибка вложенности заставляет игру писать в лог «left N scissor rectangle(s) open».

### Круглые вещи: пропорции экрана

Один X-юнит раскладки — это ширина клиентской области, один Y-юнит — её высота. На
неквадратном окне это **разные длины**, поэтому фигура с равными размерами по обеим осям
рисуется эллипсом. Отсюда правило, которое нужно соблюдать всем, кто рисует что-то круглое
или квадратное:

- размер по Y задаётся в единицах раскладки как обычно;
- размер по X получается умножением на `VgsScreenDraw.ClientAspect` (высота ÷ ширина);
- `VgsScreenDraw.Disc` компенсирует пропорции сам и рисует по одной полосе на пиксель
  высоты — ступенька в один пиксель читается как кривая.

`VgsScreenDraw.ClientAspect` вычисляется в `SyncScreenSpace` из тех же
пикселей safe-GUI, что и преобразование координат, поэтому отдельного источника правды не
появляется. `PortGeometry` принимает пропорции **параметром**, а не читает их из слоя
отрисовки: так модель геометрии остаётся чистой и проверяемой офлайн.

---

## 3. Поток данных

### Открытие редактора

```
игрок жмёт «Grid Workspace» в терминале кокпита
   └─► VgsTerminalControls.OnButtonPressed(block)
        ├─ проверка: есть локальный игрок? (на сервере GUI нет)
        ├─ cockpit.CubeGrid
        ├─ GridGraphBuilder.Build(grid)      ← однократное чтение мира
        │     └─ на каждый блок: NodeStateStore.IsShown (скрыт → пропустить)
        │                        NodeStateStore.TryReadPosition (позиция из блока)
        ├─ VgsScreenDraw.EnsureResources()   ← шрифт и кисть; без них экран не открывается
        └─ new VgsEditorScreen(graph, grid).Open()
```

Второй вход — клавиша **M** (ADR-0045) — идёт в тот же `OpenFor`, поэтому дальше схема
неотличима от открытой кнопкой:

```
каждый тик симуляции
   └─► VgsSessionComponent.UpdateAfterSimulation → EditorHotkey.Poll()
        ├─ есть локальный игрок? редактор уже открыт? (VgsEditorScreen.IsEditorOpen)
        ├─ IsNewKeyPressed(M) и не нажат Ctrl/Alt/Shift
        ├─ не чат (ChatEntryVisible), не экран (ActiveGamePlayScreen == null),
        │  не терминал (GetCurrentScreen == None)
        ├─ Session.ControlledObject as IMyCockpit — игрок в кокпите; иначе тихий отказ
        └─ VgsEditorScreen.OpenFor(cockpit)  ← дальше как у кнопки
```

### Сохранение позиции

```
игрок отпускает ЛКМ после перетаскивания карточки
   └─► VgsEditorScreen.HandleCanvasInput → leftReleased
        └─► NodeStateStore.SavePosition(node)
             ├─ Entities.GetEntityById(node.EntityId)   ← блок мог исчезнуть
             └─ Storage[LayoutKey] = "x y"              ← компонент создаётся при первой записи

следующая сессия
   └─► GridGraphBuilder.CreateNode → NodeStateStore.ApplyTo
        └─ Node.Position = сохранённая (свойство ставит Placed = true)
             └─ LayoutUnplaced больше не двигает эту карточку
```

### Чекбокс «Show in Grid Workspace»

```
игрок снимает галочку в терминале любого блока
   └─► VgsTerminalControls.OnShowInVgsChanged(block, false)
        ├─ NodeStateStore.SetShown → Storage[VisibilityKey] = "0"
        └─ _checkbox.UpdateVisual()          ← галочка отражает сохранённое значение

следующее обновление графа
   └─► GridGraphBuilder.Refresh: блок не попадает в live
        └─ узел удаляется вместе с рёбрами; тулбары не трогаются
```

### Жизненный цикл экрана

```
OnOpened()    ресурсы отрисовки (шрифт, кисть, материалы иконок),
              чтение имени грида (вне пути рисования),
              отсчёт интервала обновления

OnUpdate()    каждые RefreshIntervalFrames кадров:
                перечитать грид → GridGraphBuilder.Refresh
                (добавить новые блоки, убрать уничтоженные, обновить имена,
                 пересобрать порты и группы, сверить тулбары)
                └─ и сразу после него — проход состояния (RefreshNodeState)
              каждые StatePollIntervalFrames кадров (30, т.е. ~2 Гц):
                собрать id видимых карточек (CollectVisibleNodeIds)
                → GridGraphBuilder.RefreshState: только целостность,
                  без портов/групп/тулбаров/LayoutPorts
              если грид исчез → Close()

OnDraw()      SyncScreenSpace(); затем только чтение GridGraph:
                фон → сетка → рамки гридов (с вкладками имён) → рамки групп → трубы
                → линии (скруглённый путь) → точки отслеживания → карточки
                → полосы портов групп → нижняя панель → шапка → меню действий
                → меню режимов
                Порядок — это вложенность конструкта (ADR-0032): грид содержит группы,
                группы содержат карточки; обратный порядок закрасил бы рамку грида
                полупрозрачной заливкой группы.

OnHandleInput() SyncScreenSpace(); кнопка «Закрыть» (своя, hit-test),
              зум колесом вокруг курсора,
              далее — один из трёх взаимоисключающих режимов:
                обычный  — панорама ПКМ/СКМ, выбор и перетаскивание ЛКМ;
                           отпускание после перетаскивания пишет позицию в блок
                           (NodeStateStore.SavePosition); клик по «x» на полосе
                           снимает связи этого порта (RemovePortConnections);
                           **правый клик по карточке открывает «Manual action»**
                           — нажатие фиксирует кандидата, движение дальше
                           ClickSlopPixels сбрасывает его в панораму,
                           отпускание открывает меню (ADR-0041).
                           Группа перетаскивается за полосу имени: одно смещение
                           на всех нарисованных участников, рамка следует за ними
                           (ADR-0016); внутренность рамки остаётся холстом
                привязка — линия за курсором, ПКМ/void сбрасывают,
                           клик по карточке или по полосе имени группы открывает
                           меню действий
                меню     — строки действий, колесо прокручивает, ПКМ сбрасывает;
                           строка значит «связать» (меню привязки) или
                           «выполнить сейчас» (ручное меню, ADR-0041) —
                           это решает _menuMode, а не _bindSource

OnClosed()    сброс кэшей, ссылок на узлы и статического указателя
```

Позиция пишется **на отпускании кнопки**, а не на каждом движении и не в `OnClosed`. На каждом
движении это шестьдесят записей в секунду; в `OnClosed` — потеря раскладки при выходе в главное
меню и при выгрузке сессии, когда `OnClosed` не приходит вовсе. Это ровно тот случай, который
игрок замечает. Для группы это N записей — по одной на участника, потому что собственной
позиции у группы нет ([ADR-0016](adr/0016-group-drag.md)).

Три режима — явные поля (`_bindSource`, `_menuTarget`, `_watchSource`), а не набор флагов
кнопок, потому что режимы именно взаимоисключающие: перетаскивание, начатое во время привязки,
дралось бы с линией за указатель, а панорама, начатая с открытым меню, уводила бы мир из-под
него. Каждый обработчик получает управление целиком и не смешивается с остальными. Подробности
и обоснование — [ADR-0010](adr/0010-port-presentation-and-binding.md).

**Меню действий — один контрол с двумя смыслами строки** ([ADR-0041](adr/0041-manual-action-menu.md)).
Открытое меню носит признак режима (`ActionMenuMode.Bind` | `Manual`), а не выводит его из
наличия привязки: геометрия, прокрутка, подписи и счётчики общие, различается только то, что
делает выбор строки — пишет связь или применяет действие к блоку сейчас. Выход один:
`DismissActionMenu()`, и он же снимает привязку, если меню было её частью.

Esc обрабатывает сам `MyModScreen` (`CloseOnEscape`), вызывая `OnCancelling`. Реализация
выходит из самого глубокого режима первым: в привязке — сбрасывает её и возвращает `false`
(экран остаётся), в обычном режиме — возвращает `true` и закрывает редактор. При выгрузке
сессии `VgsSessionComponent.UnloadData` вызывает `VgsEditorScreen.CloseIfOpen()` и
`VgsScreenDraw.Release()` — иначе экран остался бы на экране, а сгенерированная текстура
пережила бы сессию. Тот же `Release` забывает разрешённые материалы иконок: сами текстуры
принадлежат игре (они поставляются модом и регистрируются в `GuiTextures.sbc`), поэтому
освобождать нечего — забыть нужно только имена, чтобы следующая сессия разрешила их заново.

Обновление по интервалу, а не каждый кадр, — осознанный компромисс: список блоков может
отставать на ~1.5 секунды при 60 FPS, зато открытие редактора на большом корабле не
роняет кадры.

---

## 4. Модель портов

Асимметрия, определяющая дизайн:

- **Входы** (действия) — реальные, из `IMyTerminalBlock.GetActions()`. Работают и для блоков
  других модов.
- **Выходы** (события) — синтезированные. API их перечисления не существует
  (см. [02-feasibility.md](02-feasibility.md) §3.2). Синтезируются они не «вообще», а как
  **то, чем блок запускает свой тулбар**: связь — это элемент в тулбаре
  ([ADR-0011](adr/0011-toolbar-is-the-connection.md)).

`BlockPortCatalog` решает это так:

1. `ResolveCategory(subtype)` — по подстроке определяет **категорию** (`BlockCategory.Cockpit`,
   `BlockCategory.Sensor`, …), см. [ADR-0030](adr/0030-block-category-descriptor.md). Порядок
   проверок от специфичного к общему и **является правилом**: `EventController` раньше `Cockpit`,
   `ButtonPanel` и `SolarPanel` раньше `Light` (`panel`), `AirVent` раньше `Oxygen`, `Hydrogen`
   раньше `Thruster`. Порядок проверяется офлайн на реальных подтипах, включая все эти ловушки.
2. `Descriptor(category)` — описатель категории: `Events` (что запускает тулбар) и `MetricKeys`
   (какие частные показатели состояния читать; сегодня пусто у всех — это задел Этапа 5.2).
   Одна таблица на категорию вместо двух, которые разъехались бы.
3. `FillEvents(node)` смотрит на `Node.Toolbar`. Если тулбара нет — **портов нет вообще**:
   связи некуда лечь, и показывать её было бы обещанием, которое мод не выполнит.
4. Если тулбар есть — для категории с событиями берутся её события, иначе общее `Triggered`.
   Деградация обязательна: категория без записи — не ошибка, а блок, о котором таблица ничего
   не знает (в том числе блок другого мода).
5. Если у тулбара две группы (`YesNo`) — каждое событие превращается в два порта,
   `… #true` и `… #false`. Ключ порта содержит группу, потому что связь хранится по ключу и
   должна помнить, в какой из двух списков она легла.

Ключи портов (`Triggered`, `Detected#true`, `TriggerNow`) — стабильные идентификаторы для
сохранения. Отображаемые имена действий берутся **у игры**: `ITerminalAction.Name` — то же
имя, что показывает ванильный тулбар («Toggle block On/Off», «Increase Text Padding»),
прочитанное в `Vgs.Game` и переданное в `FillActions` параллельно id (ADR-0037). `Humanize`
(`TriggerNow` → `Trigger Now`) остаётся запасным вариантом для действия, которое имени не
сообщило, и основным — для событий, которых игра не именует. У групповых портов добавляется
`· true` / `· false`.

**Список действий — свойство типа, а не блока, и он шире того, что блок умеет** (ADR-0038,
ADR-0039). `IMyTerminalBlock.GetActions` разрешается через
`MyTerminalControlFactory.GetActions(block.GetType())`, поэтому `GridGraphBuilder` читает список
**один раз на тип** (`ActionSet` в словаре по `Type`) и держит его до конца сессии.

Сам список содержит лишнее: `MyFunctionalBlock.CreateTerminalControls` регистрирует контролы
компонента панели текста на весь тип, а прячет их колбэк `Visible` каждого контрола, которого
`GetActions` не спрашивает. Поэтому из списка отбрасывается действие, чей **контрол игра этому
блоку не показывает**: `GetProperties` даёт объекты value-контролов (они же
`IMyTerminalControl`), у каждого вызывается его собственный `Visible(block)`, а id действия
сопоставляется с id контрола по трём формам игры — `<id>`, `<id>_On` / `<id>_Off`,
`Increase` / `Decrease` + `<id>` (`BlockPortCatalog.ActionBelongsToControl`, проверяется офлайн).
Плюс отбрасывается повторяющийся id. `IsEnabled == false` основанием не является: это
выключенный контрол, законное состояние. Модель получает уже очищенный список и про
`ITerminalAction` не знает.

`FillEvents` заодно выставляет `Node.CanDriveBlocks` и `ToolbarLayout.CanDrive` — признак
«блок может управлять другими» и единственное условие существования кнопки привязки.

Считать ответом на тот же вопрос флаг игры `ShowInToolbarConfig` нельзя: он говорит, что
блок можно **приводить в действие**, то есть описывает противоположное отношение (блок как
получатель, а не как источник). Ответ даёт компонент `IMyToolbarProvider` вместе с типом
тулбара: `ButtonPanel` — тулбар, который блок активирует сам; `Ship`/`LargeCockpit` и
`BuildCockpit` у кокпита — панели игрока, в них писать событие нельзя.

### Как связь попадает в тулбар

Правило размещения — `Vgs.Model.ToolbarPlanner`, чистая функция от формы тулбара и
**содержимого ячеек**:

1. группа «да» пишет слот 0 страницы, «нет» — слот 1; одногрупповой тулбар берёт любую
   свободную ячейку;
2. страницы перебираются по порядку и берётся первая, где нужный слот свободен **и** это
   действие ещё не стоит: игра удаляет дубликат действия на одной странице, поэтому повтор
   кладётся на следующую;
3. если действие стоит на всех страницах — связь невозможна (`ActionOnEveryPage`);
   если свободных ячеек нужного слота нет — тоже (`NoFreeCell`). Это разные сообщения,
   потому что требуют разных действий от пользователя.

**Планировщик получает содержимое ячейки, а не готовый ответ «свободна ли она».** Это не
вкусовое решение: раньше он принимал `Func<int, bool> isCellEmpty`, игровой слой передавал
туда `!ReadCell(...).IsEmpty` (остаток от той поры, когда чтение возвращало `bool` в смысле
«ячейка занята»), и **пустой тулбар считался полным** — связь не создавалась ни у одного
блока, а на непустом тулбаре мод писал поверх чужого элемента и стирал его. Шлюзы этого не
видели: правила были верны, расходился смысл аргумента между слоями
([R-009](review/regressions.md)). Теперь через границу идёт `Vgs.Model.CellContents` —
«пусто / действие блока / действие группы / чужой элемент», — и вопрос «что считать
свободным» имеет ровно одно определение, в `ToolbarPlanner`, проверяемое офлайн.

Запись — `Vgs.Game.ToolbarWriter.TryWrite`: `BeginChanges()` → `Set(cell,
MyToolbarItemInfo.BlockAction(blockId, actionId))` → `Apply()`, затем **чтение ячейки
обратно** через `TryGetItem`. Путь через `MyToolbar.SetItemAtIndex` делает то же самое, но
`MyToolbar` живёт в `Sandbox.Game.Screens.Helpers`, которое вайтлист IL-скриптов не разрешает
(разрешён `Sandbox.Game` точно, без подпространств) — мод с такой ссылкой не загружается.
`Apply()` возвращает `false`, когда машина не владеет тулбаром блока
(`MyToolbar.SendChanges` → `IsOwnedHere` → `Sync.IsServer`): в одиночной игре и на хосте
запись проходит, на удалённом клиенте — нет, и это сообщается пользователю.

### Как связь попадает в группу

У игры есть **второй вид элемента тулбара**: `MyToolbarItemInfo.GroupAction(contextBlockId,
groupName, actionId)` (`MyToolbarItemKind.GroupAction`). Он применяет одно действие к
**каждому функциональному** блоку группы, когда элемент срабатывает, — это и есть причина,
по которой группа вообще стоит на схеме ([ADR-0015](adr/0015-block-groups.md)).

Отсюда всё остальное:

- **группа — только получатель.** Тулбара у неё нет, сработать ей нечем, поэтому исходящих
  портов у группы не бывает, а `Node.CanDriveBlocks` для неё `false` с самого конструктора;
- **`contextBlockId` — блок-источник.** Игра находит грид по этому блоку и по нему же ищет
  группу в терминальной системе; источник по построению на том же конструкте и живёт ровно
  столько, сколько элемент;
- **правило действий группы — правило игры**, а не наше: `BlockPortCatalog.
  ComputeGroupActionIds` повторяет три ветви `MyToolbarItemTerminalGroup.GetActions` —
  один тип → его действия, включённые хотя бы у одного участника; разные типы и все
  функциональные → пересечение действий, включённых у каждого; разные типы и хоть один не
  функциональный → пусто. «Общего предка» игра не ищет (проверено по IL,
  см. [01-research/api-block-groups.md](01-research/api-block-groups.md) §3);
- **чтение обратно и сверка** различают два вида элемента через `Vgs.Model.CellContents`
  (игровое `MyToolbarItemKind` отображается на `CellKind` в `ToolbarWriter.ReadCell`),
  поэтому элемент, поставленный руками в ванильном терминале, превращается в линию так же,
  как записанный модом.

### Как выполняется ручное действие ([ADR-0041](adr/0041-manual-action-menu.md))

Обратная сторона той же монеты: то же меню, те же строки, но строка **выполняет** действие, а
не связывает. Поток целиком:

```
правый клик по карточке (обычный режим)
   └─► PointerGesture.ExceedsClickSlop(Δ, ширина, высота)
        ├─ Δ больше 4 px по любой оси → это панорама, кандидат сброшен
        └─ Δ в пределах             → открыть меню с _menuMode = Manual
   └─► OpenActionMenu(target, mouse, ActionMenuMode.Manual)
        ├─ строки = target.Actions       ← те же, что при привязке (ADR-0038, ADR-0039)
        └─ заголовок = MANUAL_ACTION_TITLE
   └─► клик по строке → TerminalActionRunner.TryRun(node, action, gridId, out applied)
        ├─ блок  → GetActionWithName(id) → action.Apply(block)
        └─ группа → члены из терминальной системы → каждому IsFunctional
                    → у кого есть действие → action.Apply(block)
        ├─ Ok    → меню закрывается, в лог: id действия и число блоков
        └─ отказ → меню остаётся, причина в нижнюю панель
```

Три вещи, которые здесь важны и которые нельзя «улучшить» по дороге:

- **никакой связи не появляется**: ни `ToolbarWriter`, ни `TryConnect`, ни `LayoutPorts`.
  Ручное действие не ребро и не ячейка ([ADR-0011](adr/0011-toolbar-is-the-connection.md));
- **проверок «можно ли» мы не добавляем**: `IsEnabled` игра в этом пути не спрашивает, а
  `IsFunctional` спрашивает только у группы — проверено по IL
  ([`01-research/api-toolbar.md`](01-research/api-toolbar.md) §4.3). Свой порог разошёлся бы с
  игрой и с меню привязки, которое эти строки показывает;
- **карточка не правится**: после запуска состояние обновляет обычный проход
  (ADR-0018), потому что действие могло быть отвергнуто или сделать не то, что мы предполагаем.

### Как порт попадает на экран

Каталог отвечает «что **можно** соединить». Что **нарисовать** — решает
`GridGraph.LayoutPorts()` по связям:

1. у порта без связи слот `-1`, полоса не рисуется — поэтому блок с сорока действиями
   выглядит как блок с четырьмя, пока к нему ничего не подключено;
2. слоты раздаются от нуля наружу, поэтому новая полоса добавляется в конец стакана, а не
   переставляет существующие;
3. порядок — порядок каталога, поэтому одна и та же схема всегда рисуется одинаково;
4. входящий порт, к которому пришло несколько связей, — **одна** полоса: первая связь даёт
   слот, остальные только добавляют линии к его точке;
5. сверх `MaxPortsPerSide` (6) порт не рисуется; связи при этом остаются в графе.

Геометрия полос — `Vgs.Ui.PortGeometry`, словарь понятий — [06-terminology.md](06-terminology.md)
§2.

### Как связь снимается с экрана

Обратная операция — `VgsEditorScreen.RemovePortConnections`, вызываемая кнопкой «x» на полосе
([ADR-0014](adr/0014-port-remove-button-and-port-look.md)). Порядок обратный созданию и по той
же причине: **сначала ячейка тулбара, потом ребро**.

```
щелчок по «x» на полосе (обычный режим)
   └─► GridGraph.CollectEdgesForPort(EntityId, Port.Key, Port.Direction, buffer)
        ├─ исходящий порт → все рёбра, выходящие из него (fan-out)
        └─ входящий порт  → все рёбра, входящие в него (fan-in)
   └─► на каждое ребро: ReadCellState(блок-источник, ребро)
        ├─ HoldsThis   → ToolbarWriter.TryClear
        │                 ├─ удалось    → GridGraph.RemoveEdge(ребро)
        │                 └─ отказ игры → ребро остаётся, причина — в нижнюю панель
        │                                 (WarnRemoveRefusedFormat, один раз на блок)
        ├─ HoldsOther  → ячейка НЕ трогается, ребро снимается из графа
        └─ Unreadable  → ребро остаётся, причина — в нижнюю панель
   └─► GridGraph.LayoutPorts()
```

Четыре следствия, на которых держится поведение:

- **порт несёт набор рёбер**, поэтому кнопка удаляет все его связи: половина оставила бы полосу
  на экране после щелчка;
- **связанный порт не трогается руками**: `LayoutPorts` снимает слот с любого порта без рёбер,
  поэтому полоса на другом конце — и стакан под ней — исчезает сама;
- **ячейка читается перед очисткой**, и три исхода различаются (`CellState`), а не сводятся к
  `bool`: «держит это» → очистить, «держит другое» → не трогать (это правка игрока в терминале,
  граф отстаёт до одного обновления), «не читается» → не делать ничего и сказать. Свёрнутый в
  `bool` третий исход стал бы неотличим от второго, и линия вернулась бы при следующей сверке —
  то есть «кнопка не работает»;
- **отказ записи не проглатывается**: удалить ребро, оставив элемент в тулбаре, значило бы
  показать «удалено» и вернуть линию при следующей сверке.

---

## 5. Обработка ошибок

Мод не имеет права бросать исключение в игровой цикл: это ломает сессию, а не мод.

Правила:

- каждый вход из игры (`Update`, `Draw`, `HandleInput`, колбэки контролов,
  `CustomControlGetter`) обёрнут в `try/catch` с `VgsLog.Error`;
- чтение сущности всегда защищено: блок мог быть уничтожен между кадрами;
- `VgsLog.Throttled` — для условий, истинных каждый кадр (иначе лог вырастает до мегабайт
  за минуту). Троттлинг **ключуется тегом**: общий таймер заставил бы одну сломанную ветку
  глушить все остальные;
- у `Throttled` есть перегрузка, принимающая исключение вместо готовой строки — в кадровом
  пути нельзя собирать сообщение до проверки троттлинга;
- `catch { }` без логирования допустим только там, где падение хуже отсутствия данных;
  такой `catch` обязан нести комментарий с причиной (правило R007).

---

## 6. Точки расширения

Связи, тулбар, раскладка, группы и субгриды реализованы — их точки подключения перечислены в
своих разделах выше. Здесь остались только места, которых в коде **нет**, и то, куда они
встанут, когда появятся (порядок направлений — в [`ROADMAP.md`](ROADMAP.md), «Этап 5 и далее»).

| Что понадобится | Куда встанет | Почему там |
|---|---|---|
| Условия и значения (порог, сравнение) | новый `Vgs.Game.GraphEvaluator` в `UpdateAfterSimulation` | Базовые связи исполняет игра из тулбара — вычислитель нужен только для того, чего тулбар не выражает |
| События, которых тулбар не умеет | новый `Vgs.Game.EventSources` | Событие тулбара — это факт его активации; порог и значение им не выражаются |
| Скрытие отдельного грида | `Game`-хранилище на `IMyCubeGrid.Storage` по образцу `GridViewStore` | Остаток Этапа 4 (5.3): две готовые опции режимов действуют на все гриды сразу, а эта — на один ([ADR-0032](adr/0032-construct-graph.md) §4) |
| Новые виджеты (скроллбар, второе поле ввода) | новые методы `VgsScreenDraw` | Штатных контролов в разрешённом API нет: `Sandbox.Graphics` вне вайтлиста ([ADR-0008](adr/0008-mod-screen-api.md)) |
| Стиль ребра от состояния концов (цвет, прозрачность) | функция стиля поверх `VgsScreenDraw.PathDots` | Слой менять не нужно: и маршрут, и путь — чистые функции, а цвет уже приходит из `BlockState` через `Fade`/`MaxAlpha` |

Собственный формат сохранения **связей** намеренно не вводится: их носитель — тулбар. Своё
хранилище появляется только для того, чего тулбар выразить не может: раскладка карточек, флаг
видимости узла и состояние вида. Всё это живёт **на сущности** (`MyModStorageComponent`, по
одному короткому значению на GUID), а не в таблице «`EntityId` → …», потому что `EntityId`
меняется при копировании, проекции и резке грида — [ADR-0012](adr/0012-layout-lives-on-the-block.md),
[ADR-0013](adr/0013-show-in-vgs-flag.md), [ADR-0022](adr/0022-view-state-lives-on-the-grid.md).

---

## 7. Решения и их цена

| Решение | Взамен | ADR |
|---|---|---|
| `MyModScreen`, ручной холст | Нет штатных контролов: кнопки, скролл и поля ввода рисуем сами | [0008](adr/0008-mod-screen-api.md) |
| Нормализованные координаты клиента | Safe-GUI примиряется в одном месте (`VgsScreenDraw`), но каждый кадр нужен `SyncScreenSpace` | [0008](adr/0008-mod-screen-api.md), [0009](adr/0009-screen-coordinate-space.md) |
| Своя текстура-кисть 4×4 | Заливка требует материала; при недоступности `GeneratedTextures` — деградация | [0008](adr/0008-mod-screen-api.md) |
| Обновление по интервалу | Список блоков отстаёт до ~1.5 с | [0003](adr/0003-layering.md) |
| Курируемая таблица событий | Неизвестный тип блока показывает общий набор, а не точный | [0002](adr/0002-port-model.md) |
| `Vgs.Model` без игровых ссылок | Нужны конвертеры на границе `Vgs.Game` | [0003](adr/0003-layering.md) |
| Изменяемые поля в `Vgs.Model` | Новое mutable-поле требует подавления правила с причиной | [0007](adr/0007-mutable-model-dtos.md) |
| Один экран на мод (`_openEditor`) | Нельзя открыть два грида одновременно (осознанно) | [0005](adr/0005-single-editor-screen.md) |
| Порт рисуется только при связи | Слот — производное состояние: забытый `LayoutPorts()` даёт полосу без линии | [0010](adr/0010-port-presentation-and-binding.md) |
| Fan-in разрешён | Один вход может иметь несколько источников — вычислитель обязан это учитывать | [0010](adr/0010-port-presentation-and-binding.md) |
| Линия — скруглённый путь, растеризованный в пиксельные точки | Путь считается по длине в пикселях, и на нём ставится точка на каждые три: на кадр уходит больше прямоугольников, чем у трёх отрезков (замерено ~250 против трёх-пяти), зато вес ровно 2 px на обеих осях и на любом зуме, а жёсткого края нет — сглаживания API не даёт | [0046](adr/0046-curved-connection-lines.md), [0047](adr/0047-dotted-connection-lines.md) |
| Отсечение пересечением, не scissor | Лишние вызовы `Rect` для далёких прямоугольников; зато очередь отрисовки не разбалансируется | [0010](adr/0010-port-presentation-and-binding.md) |
| Связь — элемент в тулбаре блока | Предел ёмкости стал видимым (90 ячеек у таймера); используются типы `VRage.Game.ModAPI`, привязанные к версии игры; движковые `MyToolbar*` моду запрещены | [0011](adr/0011-toolbar-is-the-connection.md) |
| «Может управлять» = `IMyToolbarProvider` + `ButtonPanel` | Нужно читать тулбар при обновлении; зато ответ точный для блоков других модов | [0011](adr/0011-toolbar-is-the-connection.md) |
| «Да»/«нет» — две группы портов | Порт раздваивается, на карточке две кнопки вместо одной | [0011](adr/0011-toolbar-is-the-connection.md) |
| Одно действие — один раз на страницу | Повтор уходит на следующую страницу, после последней — отказ с сообщением | [0011](adr/0011-toolbar-is-the-connection.md) |
| Раскладка — на блоке, не таблицей по `EntityId` | Одна запись на блок: сейв растёт; мультиплееру нужен свой канал синхронизации | [0012](adr/0012-layout-lives-on-the-block.md) |
| Видимость узла — флаг на блоке, чекбокс у всех терминальных блоков | Лишняя строка в терминале каждого блока мира, включая чужие и модовые | [0013](adr/0013-show-in-vgs-flag.md) |
| Чтение состояния блока не создаёт компонент | Запись в хранилище возможна только из действия пользователя, не из просмотра | [0012](adr/0012-layout-lives-on-the-block.md) |
| Автоматическая раскладка сортирует по позиции блока | Сортировка на каждом обновлении; зато карточки не перетасовываются между сессиями | [0012](adr/0012-layout-lives-on-the-block.md) |
| Клавиша **M** открывает схему из кокпита | Клавиша занята модом в полёте: конфликт с игрой или другим модом решается правкой константы `EditorHotkey.OpenKey`, а не настройкой. Проверка «экран не открыт» опирается на поле, которое пишет один обработчик игры, поэтому меню паузы в мультиплеере и экраны других модов ею не покрыты — в одиночной игре вопрос закрыт паузой | [0045](adr/0045-hotkey-opens-editor-from-cockpit.md) |

---

## 8. Что делать при изменении архитектуры

1. Проверить факт об API (`tools/dump-se-api.ps1`) — не проектировать на догадке.
2. Если решение дорого откатывать — написать ADR по [шаблону](adr/_template.md).
3. Обновить этот документ: диаграмму классов, правила зависимостей, таблицу решений.
4. Если код уже расходится с решением — запустить
   [совместное ревью](review/joint-review.md), а не править документ под код.



















