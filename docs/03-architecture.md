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
| [0017](adr/0017-event-controller-watch-ports.md) | Event controller: кнопка отслеживания, порты отслеживания (жёлтые, пунктир в карточку цели), отказ красной линией, описание режима на карточке (кнопка с ADR-0020 — колокольчик в общем ряду) |
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
        │                  BlockPortCatalog, ToolbarPlanner, NodeStateFormat, GridViewFormat,
        │                  ConveyorSegment, ConveyorLink, ConveyorNetwork,
        │                  BlockSearch, TextEditor
        │                  ЧИСТЫЕ ДАННЫЕ. Ни одного using Sandbox.*
        │
        └──► Vgs.Ui ────── VgsEditorScreen        экран: ввод, раскладка и режимы,
                           │                       читает только кэш модели
                           ├─ PortGeometry         геометрия карточки, портов и рамки группы
                           ├─ GridFrames           рамка грида и вкладка её имени
                           ├─ EdgeRouting          маршрут линии
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
        +TryReadWatchList(IMyTerminalBlock, List~long~) bool
        +SaveWatchList(IMyTerminalBlock, List~long~) bool
        +HasStorage(IMyTerminalBlock) bool
        -TryRead(IMyTerminalBlock, Guid, out string) bool
        -Write(IMyTerminalBlock, Guid, string) bool
    }

    class NodeStateFormat {
        <<static>>
        +FormatPosition(Vector2) string
        +TryParsePosition(string, out Vector2) bool
        +FormatIds(IList~long~) string
        +ParseIds(string, List~long~) void
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
        +ReapplyWatchList(IMyTerminalBlock) int
        +IsServer() bool
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

    class ToolbarWriter {
        <<static>>
        +TryWrite(IMyTerminalBlock, Node, Port, IMyTerminalBlock, string, out ToolbarCell) WriteResult
        +TryWriteGroup(IMyTerminalBlock, Node, Port, string, string, out ToolbarCell) WriteResult
        +TryClear(IMyTerminalBlock, Node, ToolbarCell) bool
        +TryDescribe(IMyToolbar, int) CellContents
        -WriteItem(IMyTerminalBlock, Node, Port, MyToolbarItemInfo, Func, Func, out ToolbarCell) WriteResult
        -ReadCell(IMyToolbar, int) CellContents
        -PageHolds(IMyToolbar, ToolbarLayout, int, Func) bool
        -ClearCell(IMyToolbar, int) bool
    }

    class CellContents {
        +CellKind Kind
        +long BlockEntityId
        +string GroupName
        +string ActionId
        +string Label
        +CellContents Empty$
        +bool IsEmpty
        +bool IsBlockAction
        +bool IsGroupAction
        +BlockAction(long, string)$ CellContents
        +GroupAction(string, string)$ CellContents
        +Other(string)$ CellContents
        +HoldsBlockAction(long, string) bool
        +HoldsGroupAction(string, string) bool
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
        -Node _selected
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
        -Node _hoverWatchRemoveNode
        -WatchTarget _hoverWatchRemoveTarget
        -Node _menuTarget
        -List~Port~ _menuRows
        -List~string~ _menuLabels
        -List~Vector4~ _route
        -List~Vector4~ _pipeBuffer
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
        +OpenFor(IMyCockpit)$
        +CloseIfOpen()$
        #OnOpened()
        #OnUpdate(bool hasFocus)
        #OnHandleInput()
        #OnDraw(float transitionAlpha)
        #OnCancelling() bool
        #OnClosed(bool isUnloading)
        -HandleCanvasInput(Vector2) void
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
        -BeginBind(Node, PortGroup) void
        -CancelBind() void
        -OpenActionMenu(Node, Vector2) void
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
        -DrawDashedRoute(Vector2, Vector2, Color, float, float, Vector2, Vector2) void
        -DrawPortRemoveButton(Vector2, Color, bool, Vector2, Vector2) void
        -PortLabelHeight float
        -DrawEdges(Vector2, Vector2) void
        -DrawConveyors(Vector2, Vector2) void
        -PipeAnchor(Node, bool, out Vector2) bool$
        -DrawSelfRoute(Vector2, Vector2, Vector2, Vector2, Color, Vector2, Vector2) void
        -DrawRouteBuffer(Color, Vector2, Vector2) void
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
        +PowerSwitchInkHeight(Node) float
        +PowerSwitchInkWidth(Node) float
        +PowerSwitchInkAspect float
        +TryPowerSwitchRect(Node, float, out Vector2, out Vector2) bool
        +HitPowerSwitch(Node, Vector2, float) bool
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
        +BuildDashes(Vector4 segment, float dashLength, float gapLength, List~Vector4~)
    }

    class PipeRouter {
        <<static>>
        +Build(IList~Vector4~ route, float cornerRadius, float halfWidth, float aspect, float stepLength, List~Vector4~) int
        -AddRail(...)
        -AddArc(...)
        -ArcPoint(...)
        -Direction(...)
        -Offset(...)
        -AxisScale(...)
        -AddSegment(...)
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
        +Segment(Vector2 from, Vector2 to, float t, Vector2 clip, Vector2 clipSize, Color)
        +SegmentPixels(Vector2 from, Vector2 to, float thicknessPixels, Vector2 clip, Vector2 clipSize, Color)
        +SegmentDashed(Vector2 from, Vector2 to, float t, float dash, float gap, Vector2 clip, Vector2 clipSize, Color)
        +Disc(Vector2 center, float r, Vector2 clip, Vector2 clipSize, Color)
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
        +ControllerState Controller
        +bool IsEventController
        +BlockState State
        +List~long~ Members
        +List~Port~ Actions
        +List~Port~ Events
        +Port PrimaryEvent
        +SetFrame(Vector2, Vector2)
        +PrimaryEventFor(PortGroup) Port
        +FindAction(key) Port
        +FindEvent(key) Port
    }

    class ControllerState {
        +string StatusText
        +List~WatchTarget~ Watched
        +long WatchEventId
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
        +int MetricCount
        +bool HasEvents
    }

    class Port {
        +string Key
        +string DisplayName
        +PortKind Kind
        +PortDirection Direction
        +PortGroup Group
        +float Anchor
        +int Slot
    }

    class BlockState {
        +float Integrity
        +BlockPower Power
        +float[] Metrics
        +int MetricCount
        +bool IsDamaged
        +bool CanToggle
        +SetIntegrity(float)
        +SetPower(bool, bool)
        +GetMetric(int) float
        +SetMetric(int, float)
    }

    class BlockPower {
        <<enum>>
        Unknown
        On
        Off
        Unavailable
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
        +bool TargetsGroup
        +ToGroup(long, string, string, string, ToolbarCell)$ Edge
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
        +FillActions(Node, IList~string~, IList~string~)
        +FillEvents(Node)
        +ComputeGroupActionIds(IList~GroupMemberActions~, List~string~, List~string~)
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

    VgsEditorScreen --> GridGraph
    VgsEditorScreen --> GridGraphBuilder : Build, Refresh (структура) и RefreshState (2 Гц)
    VgsEditorScreen --> ToolbarWriter : запись и очистка связи
    VgsEditorScreen --> NodeStateStore : сохранение позиции
    VgsEditorScreen --> GridViewStore : камера, масштаб, режим
    VgsEditorScreen --> BlockSearch : правило фильтра карточек
    VgsEditorScreen --> Node
    VgsEditorScreen --> PortGeometry : вся геометрия
    VgsEditorScreen --> EdgeRouting : маршрут линии
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
    GridGraphBuilder ..> BlockState : RefreshState пишет через ApplyIntegrity и ApplySwitch
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
    GridGraph ..> Port : LayoutPorts назначает Slot
    GridGraph ..> ToolbarLayout : проверяет ячейку
    GridGraph ..> GroupMemberActions : правило действий группы
    GridGraph ..> Node : OffsetGroupMembers двигает участников
    GridGraph ..> WatchTarget : LayoutPorts назначает Slot
    GridGraph ..> BlockState : ApplyIntegrity и ApplySwitch пишут снимок
    Node ..> NodeKind : различает блок и группу
    BlockState ..> BlockPower : переключатель питания блока (ADR-0035)

    BlockPortCatalog --> Node
    BlockPortCatalog --> Port
    BlockPortCatalog ..> GroupMemberActions : ComputeGroupActionIds

    PortGeometry ..> Node : читает Position, FrameSize и Slot
    PortGeometry ..> Port : читает Slot и Direction
    EdgeRouting ..> VgsConstants
    PipeRouter ..> VgsConstants : радиус изгиба и шаг лесенки

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
Vgs.Model  →  Vgs.Model            (и VRageMath)
Vgs.Game   →  Vgs.Model, Vgs.Core
Vgs.Ui     →  Vgs.Model, Vgs.Core, Vgs.Game (из ввода и обновления, никогда из отрисовки)
```

Стрелка `Ui → Game` разрешена всегда; запрещён вызов `Vgs.Game` из `OnDraw` и всего, что он
зовёт. Экран не умеет писать в мир сам, поэтому запись связи (`ToolbarWriter`), сохранение
позиции (`NodeStateStore`), отслеживание (`EventControllerProbe`) и переход в терминал
(`TerminalJump`) — это вызовы `Game` из `OnHandleInput`, то есть по действию игрока, а не из
кадрового пути картинки (ADR-0003).

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
| `BlockState.cs` | снимок состояния блока: `Integrity` 0..1 (общее для всех), `Power` — переключатель питания (ADR-0035) и `Metrics` — расширяемая часть под частные показатели категорий (заряд, заполненность; ключи задаёт описатель категории). Не сохраняется |
| `BlockPower.cs` | `BlockPower` — что говорит переключатель блока: `Unknown` (ещё не прочитан), `On`, `Off`, `Unavailable` (повреждён ниже критической целостности, включить нельзя). Три читаемых состояния, а не два: сломанный блок репортит `Enabled == true`, и флаг в лоб сделал бы его зелёным |
| `BlockCategory.cs` | `BlockCategory` (enum, порядок членов = порядок проверок классификатора) и `CategoryDescriptor` (что знает категория: события + ключи метрик) |
| `ControllerState.cs` | специфика event controller: описание режима, список отслеживания, идентификатор события. `Node.Controller` ссылается сюда только у контроллера — эта ссылка и есть признак |
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
  жёлтой полосой без пунктира;
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
| `GridGraphBuilder.cs` | построение и обновление `GridGraph` из `IMyCubeGrid`, сверка связей с тулбарами, фильтр скрытых блоков |
| `ToolbarProbe.cs` | форма тулбара блока: компонент `IMyToolbarProvider` → `ToolbarLayout` |
| `ToolbarWriter.cs` | запись и очистка элемента в реальном тулбаре блока |
| `NodeStateStore.cs` | позиция карточки, флаг видимости и список отслеживания контроллера в `MyModStorageComponent` блока |
| `GridViewStore.cs` | камера, масштаб и четыре режима показа в `MyModStorageComponent` **грида** (ADR-0022, ADR-0040): состояние общее для всех кокпитов конструкта |
| `TextClipboard.cs` | буфер обмена поля поиска — **только собственный**: системный буфер моду недоступен (ADR-0027), поэтому файл не ссылается ни на один тип игры и сломать загрузку не может |
| `EventControllerProbe.cs` | что проверяет event controller, кому из блоков он это разрешает и запись его списка |
| `EventControllerKeeper.cs` | проигрывание сохранённого списка контроллера в живой блок после загрузки мира |
| `TerminalJump.cs` | переход из редактора в терминал игры на выбранном блоке: выбор ветки `ShowTerminalPage`/`ChangeInteractedEntity`, флаг `remote` по дистанции взаимодействия, проверка передающей антенны, отказы (ADR-0021) |
| `EntityResolver.cs` | единственная реализация «id сущности → живой `IMyTerminalBlock`» (и «→ живой `IMyCubeGrid`» для конвейерного прохода): `try/catch`, один троттлинг-тег `resolve-block`, отсутствие блока — штатный ответ `false`, а не ошибка |
| `ConveyorProbe.cs` | линии конвейеров одного грида из `MyObjectBuilder_CubeGrid.ConveyorLines` (реализация конвейеров моду недоступна — ADR-0040), сведение концов линий к id блоков через `GetCubeBlock` |
| `BlockSwitch.cs` | переключатель питания блока (ADR-0035): `CanBeEnabled` (есть ли у блока `IMyFunctionalBlock`), `TryRead` (`Enabled` + `IsFunctional` за одно чтение) и `TrySetEnabled` (сеттер `Enabled` с проверкой, что флаг действительно принял значение) |
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
  отрисовка (`VgsEditorScreen.IsCardOnCanvas`, он же — culling в `DrawNodes`), и тем же
  потолком `MaxRenderedBlocks`. Одно определение «видимого» на оба прохода: иначе проход
  состояния опрашивал бы карточки, которых не видно, и пропускал бы те, что видно;
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

Три следствия, которые надо держать в голове, меняя этот код:

- **игровые записи из редактора не удаляются.** Приватный список контроллера не имеет
  публичного сеттера, а `RemoveBlocks` снимает только подписку компонента — `ProcessSelectedBlocks`
  вернёт запись через секунду. Поэтому «x» на таком порте отвечает сообщением и ничего не
  меняет (`WatchResult.GameOwned`), а редакторская запись снимается нормально;
- **смена события пересоздаёт компонент.** Подписки, сделанные модом, к новому компоненту не
  переходят, поэтому при изменении `SelectedEventId` надстройка проигрывается заново. Иначе
  порты остались бы, а контроллер перестал бы их слушать;
- **«не отображается» ≠ «удалить».** Скрытый флагом «Show in Grid Workspace» блок не имеет карточки, его
  линия не рисуется, но порт и игровые данные остаются. Схема скрывает то, что не может
  нарисовать, и не правит данные игры ради порядка на экране.

Запись — единственное место, где мод меняет контроллер: `EventControllerProbe.TryAddWatch` /
`TryRemoveWatch`, через `IMyEventComponentWithGui.AddBlocks` / `RemoveBlocks` — тот же метод,
которым пользуется ванильный терминал. Порядок записи асимметричен и обязателен: добавление —
проверка → резерв в графе → хранилище → подписка (с откатом резерва), удаление — отписка →
хранилище → граф (с восстановлением подписки, если запись не удалась).

`EventControllerKeeper` проигрывает надстройку в живой компонент после загрузки мира и при
смене события: слушает `OnEntityAdd`, откладывает работу на несколько тиков (компоненты блока
появляются не мгновенно) и вызывает тот же `AddBlocks`. Игровую часть списка игра
восстанавливает сама.

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
| `VgsEditorScreen.Input.cs` | **частичный класс**: ввод — четыре режима указателя (обычный, привязка, отслеживание, меню), панорама и зум, перетаскивание, hit-test |
| `VgsEditorScreen.Draw.cs` | **частичный класс**: отрисовка — холст, карточки, полосы портов, рамки групп, линии, шапка и нижняя панель; ничего не читает из мира (ADR-0003) |
| `SearchField.cs` | поле поиска как самостоятельный тип: запрос, каретка, выделение, прокрутка и кэш позиций; шрифт подаётся делегатом, поэтому арифметика проверяется офлайн |
| `ViewState.cs` | состояние вида: камера, зум, четыре режима показа (скрытые блоки, субгриды, границы гридов, конвейерная сеть) и последние записанные значения; правило «когда писать» — здесь, сам вызов грида — в экране |
| `VgsPalette.cs` | палитра: все цвета редактора в одном месте. Файлы отрисовки импортируют её через `using static`, поэтому имена остались прежними (`ColNode`, `ColDamage`, …) |
| `GridFrames.cs` | рамка грида и её вкладка с именем: какие карточки охватывает, где углы в пикселях окна, как из них выводятся четыре края. **Без** ссылок на `Sandbox.*` — проверяется офлайн ([ADR-0032](adr/0032-construct-graph.md)) |
| `PortGeometry.cs` | геометрия карточки, её ряда кнопок, портов, кнопки отслеживания и рамки группы — единственный источник правды для отрисовки **и** hit-testing; объявляет `CardButtonKind`; решает, рисуется ли пунктир отслеживания (`ShouldDrawWatchLine` — самопросмотр его не рисует, ADR-0036) |
| `EdgeRouting.cs` | маршрут линии: ортогональные отрезки, приход в цель слева; петля вокруг карточки для связи блока с собой (ADR-0036); разбиение отрезка на штрихи для пунктира |
| `PipeRouter.cs` | две рельсы трубы по тому же маршруту: зазор, пропорции, скругление изгиба лесенкой осевых кусков (ADR-0040). **Без** ссылок на `Sandbox.*` — проверяется офлайн |
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
  («LargeBlockTimer»), которого игрок не набирает и которого нет в его языке.
- **Клавиатура**: поле всегда является целью ввода; символы читаются из `IMyInput.TextInput`
  раз в кадр (игра подменяет буфер при чтении, поэтому каждый символ приходит ровно однажды —
  [`01-research/api-terminal-search.md`](01-research/api-terminal-search.md) §2). Поверх этого —
  своё текстовое поле (ADR-0024): каретка с миганием, выделение (якорь + каретка),
  стрелки/Home/End/Shift, Ctrl+A/C/X/V. Раскладка каретки и выделения считается **при
  изменении**, а не каждый кадр: это вызовы метрик шрифта.

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
| **колонка состояния** | что блок делает **сейчас** | тумблер питания в верхней полосе (40% внутренней высоты, с зазором 2 px от граней); нижние 60% зарезервированы и пусты | `PortGeometry.TryPowerSwitchRect`, `HitPowerSwitch` |
| **шильдик** | что блок **есть** | имя, подтип, описание режима | `PortGeometry.TryCardTextSpan` |
| **ряд кнопок** | что можно **сделать** со схемой | переход, отслеживание, привязки | `PortGeometry.TryCardButtonRow` |

Три части не смешиваются: у каждой своё правило размера. Поэтому добавление тумблера не сдвинуло
ряд кнопок ни на пиксель, а шильдик просто начался правее — и поэтому же имя «колонка состояния»
не привязано к слову «питание»: в нижней полосе появится второй контрол, и он тоже будет
относиться к блоку, а не к схеме.

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
                → линии → пунктир отслеживания → карточки → полосы портов групп
                → нижняя панель → шапка → меню действий → меню режимов
                Порядок — это вложенность конструкта (ADR-0032): грид содержит группы,
                группы содержат карточки; обратный порядок закрасил бы рамку грида
                полупрозрачной заливкой группы.

OnHandleInput() SyncScreenSpace(); кнопка «Закрыть» (своя, hit-test),
              зум колесом вокруг курсора,
              далее — один из трёх взаимоисключающих режимов:
                обычный  — панорама ПКМ/СКМ, выбор и перетаскивание ЛКМ;
                           отпускание после перетаскивания пишет позицию в блок
                           (NodeStateStore.SavePosition); клик по «x» на полосе
                           снимает связи этого порта (RemovePortConnections).
                           Группа перетаскивается за полосу имени: одно смещение
                           на всех нарисованных участников, рамка следует за ними
                           (ADR-0016); внутренность рамки остаётся холстом
                привязка — линия за курсором, ПКМ/void сбрасывают,
                           клик по карточке или по полосе имени группы открывает
                           меню действий
                меню     — строки действий, колесо прокручивает,
                           ПКМ сбрасывает привязку

OnClosed()    сброс кэшей, ссылок на узлы и статического указателя
```

Позиция пишется **на отпускании кнопки**, а не на каждом движении и не в `OnClosed`. На каждом
движении это шестьдесят записей в секунду; в `OnClosed` — потеря раскладки при выходе в главное
меню и при выгрузке сессии, когда `OnClosed` не приходит вовсе. Это ровно тот случай, который
игрок замечает. Для группы это N записей — по одной на участника, потому что собственной
позиции у группы нет ([ADR-0016](adr/0016-group-drag.md)).

Три режима — явные поля (`_bindSource`, `_menuTarget`), а не набор флагов кнопок, потому
что режимы именно взаимоисключающие: перетаскивание, начатое во время привязки, дралось бы
с линией за указатель, а панорама, начатая с открытым меню, уводила бы мир из-под него.
Каждый обработчик получает управление целиком и не смешивается с остальными. Подробности и
обоснование — [ADR-0010](adr/0010-port-presentation-and-binding.md).

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
| Кривые линии и стиль ребра от состояния концов | `Ui/EdgeRouting.Build` + функция стиля поверх `VgsScreenDraw.Segment` | Слой менять не нужно: маршрут — чистая функция, источник условий — снимок `BlockState` |

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
| Линия — ортогональные прямоугольники | Нет скруглений и диагоналей; зато нет угадывания семантики `rotation` | [0010](adr/0010-port-presentation-and-binding.md) |
| Отсечение пересечением, не scissor | Лишние вызовы `Rect` для далёких прямоугольников; зато очередь отрисовки не разбалансируется | [0010](adr/0010-port-presentation-and-binding.md) |
| Связь — элемент в тулбаре блока | Предел ёмкости стал видимым (90 ячеек у таймера); используются типы `VRage.Game.ModAPI`, привязанные к версии игры; движковые `MyToolbar*` моду запрещены | [0011](adr/0011-toolbar-is-the-connection.md) |
| «Может управлять» = `IMyToolbarProvider` + `ButtonPanel` | Нужно читать тулбар при обновлении; зато ответ точный для блоков других модов | [0011](adr/0011-toolbar-is-the-connection.md) |
| «Да»/«нет» — две группы портов | Порт раздваивается, на карточке две кнопки вместо одной | [0011](adr/0011-toolbar-is-the-connection.md) |
| Одно действие — один раз на страницу | Повтор уходит на следующую страницу, после последней — отказ с сообщением | [0011](adr/0011-toolbar-is-the-connection.md) |
| Раскладка — на блоке, не таблицей по `EntityId` | Одна запись на блок: сейв растёт; мультиплееру нужен свой канал синхронизации | [0012](adr/0012-layout-lives-on-the-block.md) |
| Видимость узла — флаг на блоке, чекбокс у всех терминальных блоков | Лишняя строка в терминале каждого блока мира, включая чужие и модовые | [0013](adr/0013-show-in-vgs-flag.md) |
| Чтение состояния блока не создаёт компонент | Запись в хранилище возможна только из действия пользователя, не из просмотра | [0012](adr/0012-layout-lives-on-the-block.md) |
| Автоматическая раскладка сортирует по позиции блока | Сортировка на каждом обновлении; зато карточки не перетасовываются между сессиями | [0012](adr/0012-layout-lives-on-the-block.md) |

---

## 8. Что делать при изменении архитектуры

1. Проверить факт об API (`tools/dump-se-api.ps1`) — не проектировать на догадке.
2. Если решение дорого откатывать — написать ADR по [шаблону](adr/_template.md).
3. Обновить этот документ: диаграмму классов, правила зависимостей, таблицу решений.
4. Если код уже расходится с решением — запустить
   [совместное ревью](review/joint-review.md), а не править документ под код.




