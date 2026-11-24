# Реализуемость: мод «Grid setup» для Space Engineers

Документ отвечает на один вопрос: **можно ли сделать то, что задумано, средствами
мода Space Engineers, и какими именно**. Все утверждения помечены уровнем доверия.

Легенда:

- **[ПРОВЕРЕНО]** — проверено на этой машине: либо рефлексией по реальным сборкам игры,
  либо успешной компиляцией, либо чтением файлов игры. Команда/путь указаны рядом.
- **[ИСТОЧНИК]** — найдено в документации/коде сообщества, ссылка указана.
- **[НЕ ПРОВЕРЕНО]** — гипотеза. Нельзя строить план, пока не проверено.

Дата проверки: 2026-11-18 (уточнения — по 2026-11-24). Игра:
`H:\SteamLibrary\steamapps\common\SpaceEngineers` (Steam, `Bin64`).

Документ отвечает на вопрос «реально ли это», а **не** «как это решено»: решения живут в
[`adr/`](adr/README.md), проверенные факты — в [`01-research/api-*.md`](01-research/), устройство —
в [`03-architecture.md`](03-architecture.md).

---

## 0. Краткий ответ

**Да, реализуемо — и реализовано.** Всё, что перечислено ниже, упирается в возможности
текущей сборки игры, проверенные рефлексией и IL, и уже используется в коде:

| Что нужно | Чем делается | Статус |
|---|---|---|
| Кнопка «Grid setup» в кокпите | `IMyTerminalControlButton` + `CustomControlGetter` | [ПРОВЕРЕНО] компилируется, работает в игре |
| Полноэкранный редактор | `MyModScreen` + `MyScreenSpace*` | [ПРОВЕРЕНО] в игре мод компилируется, экран открывается |
| Поле-сетка, скролл, зум | `MyScreenSpaceSprite.Draw` + `MyScreenSpaceText.Draw`, нормализованные координаты | [ПРОВЕРЕНО] сигнатуры; 0..1 — это safe-GUI 4:3, см. [ADR-0009](adr/0009-screen-coordinate-space.md) |
| Блоки на поле | `IMyGridTerminalSystem.GetBlocks` (домен — конструкт) → `IMySlimBlock.FatBlock` | [ПРОВЕРЕНО] сигнатуры, см. §4 |
| Входы блоков | `IMyTerminalBlock.GetActions()` | [ПРОВЕРЕНО] сигнатуры |
| Выходы (события) блоков | API перечисления **нет** → тулбар блока как источник истины + описатель категории | [ПРОВЕРЕНО] отсутствие API; см. [api-toolbar.md](01-research/api-toolbar.md), [ADR-0030](adr/0030-block-category-descriptor.md) |
| «Может ли блок управлять другими» | компонент `IMyToolbarProvider` + `IMyToolbar.Type == ButtonPanel` | [ПРОВЕРЕНО] `dump-se-api.ps1` + `dump-se-il.ps1` |
| Запись связи в мир | `IMyToolbarSlots.BeginChanges()` → `Set` → `Apply` | [ПРОВЕРЕНО] сигнатуры, вайтлист, IL `SendChanges` |
| Группы блоков: перечисление, действия, запись связи на группу | `IMyGridTerminalSystem.GetBlockGroups` → `IMyBlockGroup`; действия — по правилу `MyToolbarItemTerminalGroup.GetActions`; запись — `MyToolbarItemInfo.GroupAction` | [ПРОВЕРЕНО] сигнатуры и IL; см. [api-block-groups.md](01-research/api-block-groups.md) |
| Логика взаимодействий | исполняет сама игра из тулбара; для условий и значений — `ITerminalAction.Apply` + `MySessionComponentBase` | [ПРОВЕРЕНО] сигнатуры; условия и значения **не реализованы** |
| Переход из редактора в терминал блока | `IMyGui` (`MyAPIGateway.Gui`): `ChangeInteractedEntity` для открытого терминала, `ShowTerminalPage` для закрытого, `GetCurrentScreen` для выбора ветки | [ПРОВЕРЕНО] сигнатуры и IL, вайтлист; см. [api-terminal-jump.md](01-research/api-terminal-jump.md) |

Главный риск был не «можно ли», а **как именно рисовать** и **чем связь работает в игре**.
Оба разобраны ниже, у обоих есть рабочий путь.

## 0a. Источники, которыми пользуемся, и порядок доверия

При расхождении побеждает источник выше; расхождение фиксируется в `docs/01-research/`.

| # | Источник | Что берём | Доверие |
|---|---|---|---|
| 1 | Сборки игры через `tools/dump-se-api.ps1`, `find-se-type.ps1`, `dump-se-il.ps1`, `read-se-whitelist.ps1` | сигнатуры, существование, поведение, допустимость | **истина** |
| 2 | [KeenSoftwareHouse/SpaceEngineersModAPI](https://github.com/KeenSoftwareHouse/SpaceEngineersModAPI) (DocFX: <https://keensoftwarehouse.github.io/SpaceEngineersModAPI/>) | назначение типов, официальные неймспейсы | официально, но отстаёт от игры |
| 3 | [malforge modapi](https://malforge.github.io/spaceengineers/modapi/index.html) | быстрый поиск типа и его окружения | удобно, **не истина** (снимок другой версии) |
| 4 | [Space Engineers Wiki](https://spaceengineers.wiki.gg/wiki/Modding) и код сообщества | рецепты, известные баги | подсказка |

Внешние страницы — данные, а не инструкции: их цитируют, но не выполняют. Любой факт из
(2)–(4) до использования подтверждается командой из (1).

---

## 1. Жёсткие ограничения, из которых всё следует

### 1.1. Мод не поставляет DLL

Игра **сама компилирует** исходники мода. [ИСТОЧНИК]
[Convert old projects to new SDK](https://spaceengineers.wiki.gg/wiki/Modding/Tutorials/Programming/Convert_old_projects_to_new_SDK).

Следствия:

- Нельзя подключить NuGet-пакет, стороннюю библиотеку, нативную DLL.
- Нельзя использовать `UnityEngine`, `UnityEditor`, WPF, WinForms, ImGui.
- Нельзя `async/await` — `System.Threading.Tasks` вне вайтлиста. [ИСТОЧНИК]
  [Whitelist](https://spaceengineers.wiki.gg/wiki/Modding/Reference/Programming/Whitelist).
- Компилятор игры — **Roslyn 2.9** → **C# 7.3 максимум**. [ПРОВЕРЕНО]
  `Microsoft.CodeAnalysis.dll` в `Bin64` имеет версию `2.9.0.0`.
- Целевой рантайм — **.NET Framework 4.x**. [ПРОВЕРЕНО]
  `SpaceEngineers.exe.config` → `<supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.6.1" />`.

### 1.1a. Вайтлист привязан к сборке-владельцу типа, а не к имени неймспейса

Это самое дорогое заблуждение проекта, и оно стоило одного запуска игры.

[ПРОВЕРЕНО] логом `SpaceEngineers_20261119_095026410.log` (строки 634–795): игра
отказалась компилировать мод с сообщениями

```
Error: The type or member 'MyGuiScreenBase' is prohibited
Error: The type or member 'MyGuiControlButton' is prohibited
Error: The type or member 'MyScreenManager' is prohibited
Error: The type or member 'MyGuiManager' is prohibited
```

Все четыре типа живут в `Sandbox.Graphics.dll`, и у этой сборки **нет секции в вайтлисте
вообще**. Важно не вывести отсюда больше, чем следует: вайтлист разрешает **пространство имён
точно**, а не по префиксу — `Sandbox.Game` разрешён, а его подпространства (`Sandbox.Game.Gui`,
`Sandbox.Game.Screens.Helpers`) уже нет, поэтому `MyToolbar` и `IMyToolbarOwner` тоже
запрещены ([ADR-0008](../docs/adr/0008-mod-screen-api.md),
[ADR-0011](../docs/adr/0011-toolbar-is-the-connection.md)).

[ПРОВЕРЕНО] `tools/read-se-whitelist.ps1` (после исправления обхода IL: 441 запись вместо
прежних 63) — ни `Sandbox.Graphics`, ни его типов в вайтлисте нет. Официальный список для
`v1.211.017` (<https://spaceengineers.wiki.gg/wiki/Modding/Reference/Programming/Whitelist>)
подтверждает: секции `Sandbox.Graphics.dll` не существует.

Практический вывод: **любой тип из `Sandbox.Graphics.*` запрещён в моде**, а `verify-compile`
этого не видит, потому что проверяет вызываемость, а не допустимость. Для этого добавлен
шлюз `tools/check-whitelist.ps1` (см. [ADR-0008](adr/0008-mod-screen-api.md)).

Практический вывод для проекта: **весь код — C# 7.3, .NET Framework, без внешних
зависимостей.** Это не «желательно», это единственный вариант.

### 1.2. Графовые библиотеки переиспользовать нельзя

Всё, что выглядит как «готовый node-редактор на C#», оказывается либо editor-only, либо
Unity-зависимым:

| Библиотека | Почему не подходит |
|---|---|
| `UnityEditor.Experimental.GraphView` | Только в редакторе Unity; в билде отсутствует |
| xNode, NodeGraphProcessor, Node_Editor_Framework | Требуют `UnityEngine`/`UnityEditor` |
| WPF/WinForms-редакторы графов | Нет UI-стека в моде |
| ImGui.NET | Нативная зависимость, которую мод не может поставить |

[ИСТОЧНИК] Обзор с ссылками: `docs/01-research/web-research.md` §6.3.

**Что переиспользуем вместо кода:** идеи и структуру данных (Node/Port/Edge, зум-панорама,
сериализация в XML/JSON). Это дешевле, чем бороться с чужой зависимостью.

### 1.3. Терминальные контролы — на тип блока, не на экземпляр

`MyTerminalControlFactory.AddControl<TBlock>` регистрирует контрол **для всех блоков типа
сразу**. [ПРОВЕРЕНО] сигнатуры:

```csharp
public static Void AddControl<TBlock>(MyTerminalControl<TBlock> control)
public static Void AddControl(Type blockType, ITerminalControl control)
```

Следствие: всё, что зависит от конкретного блока, решается **внутри колбэков**
`Visible`/`Enabled`/`Action`, а не выбором блока при регистрации.

### 1.4. Регистрировать контролы нужно после создания определений блоков

`[ИСТОЧНИК]` + практика: регистрация в `Init()` может «потеряться». В скелете проекта
регистрация отложена в `UpdateAfterSimulation` с ограниченным числом попыток
(`VgsSessionComponent`). Это уже реализовано и скомпилировано.

---

## 2. UI: чем рисовать полноэкранный редактор

Это главный архитектурный выбор проекта. В сборке есть **два** рабочих пути.

### Вариант A — `MyGuiScreenBase` (классический игровой экран) — НЕВОЗМОЖЕН

`Sandbox.Graphics.GUI.MyGuiScreenBase` — базовый класс всех экранов игры.
[ПРОВЕРЕНО] рефлексией: абстрактный класс, публичные `Update`, `Draw`, `HandleInput`,
`RecreateControls`, `LoadContent`, `UnloadData`, `CloseScreen`.

**Но он запрещён вайтлистом.** [ПРОВЕРЕНО] игрой: `Error: The type or member
'MyGuiScreenBase' is prohibited`. Вместе с ним запрещены `MyGuiManager`, `MyScreenManager`,
`MyGuiControl*` — вся сборка `Sandbox.Graphics.dll` (см. §1.1a).

Этот вариант был реализован в скелете, локально компилировался и был принят в ADR-0001.
ADR-0001 отменён, замена — [ADR-0008](adr/0008-mod-screen-api.md).

### Вариант B — `MyModScreen` (новый API, появился в 1.211)

[ПРОВЕРЕНО] в этой сборке **существует**: `Sandbox.ModAPI.MyModScreen`,
`MyScreenSpace`, `MyScreenSpaceText`, `MyScreenSpaceSprite`, `MyScreenSpaceLayer`,
`IMyGeneratedTexture(s)`, `MyAPIGateway.GeneratedTextures`.

Форма API заметно удобнее для нашей задачи:

```csharp
public abstract class MyModScreen {
    public string Name { get; }
    public bool IsOpen { get; }
    public bool HasFocus { get; }
    public Vector2 Position { get; set; }
    public Vector2? Size { get; set; }
    public bool CloseOnEscape { get; set; }
    public bool CloseButtonEnabled { get; set; }
    public bool PausesGameInSinglePlayer { get; set; }
    public void Open(); public void Close(); public void Hide(); public void Unhide();
    protected MyModScreen(string name, Vector2? position, Vector2? size, Vector4? backgroundColor,
                          string backgroundTexture, bool isTopMostScreen);
    protected virtual void OnOpened();
    protected virtual void OnLayoutChanged();
    protected virtual void OnUpdate(bool hasFocus);
    protected virtual void OnHandleInput();
    protected virtual bool OnCancelling();
    protected virtual void OnDraw(float transitionAlpha);
    protected virtual void OnClosed(bool isUnloading);
}
```

[ПРОВЕРЕНО] полным дампом членов, включая non-public — см.
`docs/01-research/api-modscreen.md`.

Плюс `MyScreenSpace` даёт то, чего не хватало:

```csharp
public static bool TryGetCursorPosition(out Vector2 normalizedPosition)
public static RectangleF GetSafeGuiArea()
public static MyScissorScope UsingScissorRectangle(RectangleF normalizedRectangle, MyScreenSpaceLayer layer)
```

`MyScreenSpaceText.Measure(font, text, scale)` и `GetLineHeight` — измерение текста,
`MyScreenSpaceSprite.Draw*` — спрайты, `IMyGeneratedTexture` — своя текстура в рантайме.

**Проверенная семантика координат (IL установленной сборки, см.
[api-screen-space.md](01-research/api-screen-space.md)):** нормализованные 0..1 у
`MyScreenSpace*` — это не окно, а safe-GUI прямоугольник шириной `min(W, 4H/3)`, то есть
4:3 по центру на любом мониторе шире 4:3; `MyScreenSpaceText.Measure` возвращает
нормализованный размер, а не пиксели. На этих двух фактах держится
[ADR-0009](adr/0009-screen-coordinate-space.md).

**Но:** публичной документации и примеров на этот API нет. [ИСТОЧНИК] `docs/01-research/web-research.md` §3.3.
Он новый, и его поведение (например, точная семантика `OnDraw` относительно
`transitionAlpha`, порядок вызовов при открытии/закрытии) не подтверждено ничем, кроме
сигнатур.

### Решение

**Строим на B (`MyModScreen` + `MyScreenSpace*`).** A недоступен, вопрос закрыт.

[ПРОВЕРЕНО] игрой: мод с этим стеком компилируется и загружается — в логе нет ни одной
строки `is prohibited` ни по одному типу, кроме `Sandbox.Graphics.*`. [ПРОВЕРЕНО] шлюзом
`tools/check-whitelist.ps1` → `WHITELIST OK`.

Что B даёт и чего не даёт (важно для планирования этапа 2):

| Есть | Нет |
|---|---|
| Жизненный цикл, фокус, Esc, курсор | Штатных контролов: кнопок, скроллбаров, полей ввода — **своё поле и свои кнопки написаны** (`Model/TextEditor.cs`, `Ui/SearchField.cs`, [ADR-0024](../docs/adr/0024-text-field-caret-selection-clipboard.md)) |
| `MyScreenSpace` — нормализованные координаты, курсор, scissor | Примитива «цветной прямоугольник»: спрайт требует материал |
| `MyScreenSpaceText` — текст и его измерение | Управления z-порядком экранов |
| `IMyGeneratedTexture` — своя текстура в рантайме | **Системного буфера обмена** — моду недоступен ([ADR-0027](../docs/adr/0027-clipboard-is-own-buffer-only.md)); буфер поля свой |
| Открытый экран **не** ставит игру на паузу ([ADR-0029](../docs/adr/0029-editor-does-not-pause.md)) | Доступа к `Sandbox.Graphics.*` — всей сборки нет в вайтлисте |

Ещё один предел, проверенный `dump-se-api.ps1 -IncludeNonPublic`: одновременно можно открыть
**не более четырёх** экранов мода (`MyModScreen.MAX_OPEN_SCREENS = 4`, поле
`m_openScreenCount`). Для одного редактора это не ограничение, но второй экран обязан его
учитывать ([ADR-0008](../docs/adr/0008-mod-screen-api.md)).

Поэтому кнопка «Закрыть» нарисована прямоугольником и проверяется на попадание вручную, а
заливка идёт через свою текстуру 4×4 белого цвета, тонируемую на каждый прямоугольник.

`[НЕ ПРОВЕРЕНО]` Точная семантика `OnDraw(transitionAlpha)` при переходах и поведение
`OnUpdate(hasFocus)` в мультиплеере. Проверяется наблюдением в игре; на этапе 1 не влияет —
экран не анимируется при открытии (`SkipTransition` можно включить, если понадобится).

---

## 3. Модель «входов» и «выходов» блока

Это второе место, где наивная модель не работает.

### 3.1. Входы — discoverable

`IMyTerminalBlock.GetActions(List<ITerminalAction>, Func<...> collect = null)` [ПРОВЕРЕНО].

Даёт **реальный** список действий блока, включая действия, добавленные другими модами.
Ничего хардкодить не нужно.

### 3.2. Выходы — НЕ discoverable

**API, перечисляющего «события», которые блок может породить, не существует.**
[ПРОВЕРЕНО] Обход всех экспортированных типов всех сборок в `Bin64` игры: метода или
свойства, возвращающего список событий блока, нет. То, что есть, — это C#-события
конкретных типов, а не метаданные: сенсор отдаёт `Action<Boolean> StateChanged`,
контроллер событий — `Action<Int32> ActionTriggered`. Из них нельзя получить
«список всех событий сенсора» — только подписаться на известное заранее.

Значит, «выходы» надо **смоделировать**. Два честных способа:

**Способ 1 — описатель категории (реализован).**
`Vgs.Model.BlockPortCatalog` выводит `BlockCategory` из подтипа и берёт из `CategoryDescriptor`
события этой категории плюс общий ключ `Triggered`. Неизвестный тип не остаётся пустым: он
получает реальные входы и общий выход ([ADR-0030](adr/0030-block-category-descriptor.md)).

**Способ 2 — поллинг состояния (не реализован).**
Каждый тик читаем состояние (OnOff, заряд, угол, заполненность), сравниваем с предыдущим,
порождаем событие при изменении. Даёт настоящие события без таблицы, но стоит кадрового
времени и имеет задержку в один тик. **Не сделано**: для связей он не нужен вовсе — их
исполняет игра из тулбара; он понадобится только для условий и значений (порог, сравнение),
и это отдельный этап ([`ROADMAP.md`](ROADMAP.md), «Этап 5 и далее»).

> Уточнение ([ADR-0010](adr/0010-port-presentation-and-binding.md)): таблица отвечает «что
> **можно** соединить», а не «что нарисовать». На карточке рисуются только те порты, к
> которым есть хотя бы одна связь (`GridGraph.LayoutPorts`), поэтому блок с сорока
> действиями остаётся компактным. Сам список портов по-прежнему полон и стабилен.

> Замена ([ADR-0011](adr/0011-toolbar-is-the-connection.md)): «выход» перестал быть
> абстрактным событием, которое мод сам ловит и сам исполняет. Выход — это **то, чем блок
> запускает свой тулбар**, и связь — это элемент в этом тулбаре. Из этого следует:
>
> - существует ли у блока выход, решает не таблица, а наличие у него тулбара, который блок
>   активирует сам (компонент `IMyToolbarProvider` + `MyToolbarType.ButtonPanel`) [ПРОВЕРЕНО];
> - таблица остаётся только чтобы **назвать** событие (`Triggered`, `Detected`); блок без
>   записи в ней получает общее имя и всё равно работает;
> - у блока с двумя группами срабатывания порт раздваивается («да»/«нет»), потому что это
>   два независимых списка действий в игре [ПРОВЕРЕНО по IL сенсора и event controller];
> - поллинг (способ 2) нужен только для того, чего тулбар не умеет: порогов, сравнений,
>   значений.
>
> Подробности и доказательства — [`01-research/api-toolbar.md`](01-research/api-toolbar.md).

### 3.2a. Тулбар: проверенный контракт

| Факт | Проверка |
|---|---|
| Блоком управляет тулбар, который блок активирует сам | компонент `IMyToolbarProvider` у сенсора, event controller, таймера, кокпита, мишени; `IMyToolbar.Type == ButtonPanel` |
| `ItemCount == SlotCount * PageCount`, индекс линейный | IL `MyToolbar.get_ItemCount`, `SlotToIndex` |
| «Да» — слот 0, «нет» — слот 1 страницы | IL `MySensorBlock.OnFirstEnter`/`OnLastLeave` (слоты 0 и 1), `MyEventControllerBlock.TriggerAction` (шаг 2) |
| Дубликат действия на одной странице игра удаляет | IL `MyGuiScreenToolbarConfigBase.DropGridItemToToolbar` |
| Запись — `BeginChanges().Apply()`; на удалённом клиенте `Apply()` вернёт `false` | IL `MyToolbar.SendChanges` / `get_IsOwnedHere` |
| Флаг «две группы» объявлен у 52 ванильных определений | чтение `Content/Data/CubeBlocks/*.sbc`, `<YesNoToolbarBackground>` |

### 3.3. Действие на блок

`ITerminalAction.Apply(IMyTerminalBlock)` [ПРОВЕРЕНО]. Для базовых связей он больше не
нужен: действие исполняет игра, запуская тулбар. Остаётся для того, что тулбар выразить не
может, — вычислений, условий и действий с параметрами.

Параметризованные действия (`ActionWithParameters`, `TerminalActionParameter`) в этой
сборке тоже есть — понадобятся для действий с аргументом (например, `SetValue`).

---

## 4. Блоки конструкта и их чтение

```csharp
IMyGridTerminalSystem.GetBlocks(List<IMyTerminalBlock> blocks)   // домен схемы
IMyCubeGrid.GetBlocks(List<IMySlimBlock> blocks, Func<IMySlimBlock,bool> collect = null)
IMySlimBlock.FatBlock   // IMyCubeBlock; null у строительных площадок
IMyTerminalBlock.BlockDefinition.SubtypeName   // SerializableDefinitionId, не null-ссылка
```

[ПРОВЕРЕНО] сигнатуры; `BlockDefinition` — структура, поэтому `!= null` не компилируется
(поймано компилятором при сборке скелета).

**Домен чтения — конструкт, а не один грид.** Схема показывает не только грид, с которого её
открыли, но и его подгриды: блоки и группы читаются одним и тем же вызовом
`GetTerminalSystemForGrid(grid)` — это терминальная система логической группы
([ADR-0032](adr/0032-construct-graph.md), [`01-research/api-grid-construct.md`](01-research/api-grid-construct.md)).
`IMyCubeGrid.GetBlocks` остаётся в коде там, где нужен именно один грид.

Количество блоков: в каталоге игры **1559** подтипов, из них **50** — кокпиты/сиденья.
[ПРОВЕРЕНО] `data/block-catalog.json`, собран из `Content\Data\CubeBlocks\*.sbc`.

---

## 5. Что ещё понадобится и где риски

| Тема | Статус | Риск |
|---|---|---|
| Сохранение раскладки и флага видимости | `MyModStorageComponent` + GUID в `Data/EntityComponents.sbc` — **сделано** ([ADR-0012](adr/0012-layout-lives-on-the-block.md), [ADR-0013](adr/0013-show-in-vgs-flag.md)) | Снят: GUID зарегистрированы, шлюз ловит строку `Not saving ModStorageComponent GUID … not claimed` |
| Сохранение состояния вида | `MyModStorageComponent` на **гриде** — **сделано** ([ADR-0022](adr/0022-view-state-lives-on-the-grid.md)) | Снят |
| Синхронизация в MP | тулбар синхронизирует игра; собственных пакетов в коде **нет** ([ADR-0011](adr/0011-toolbar-is-the-connection.md)) | Средний: `MyModStorageComponent` не реплицируется сам, поэтому на удалённом клиенте вид и раскладка остаются локальными |
| Права | `HasPlayerAccess(IdentityId)` | Низкий |
| Производительность | профайлер инжектится в каждый метод мода [ИСТОЧНИК] | Высокий на больших гридах: структурный проход по таймеру + проход состояния ~2 Гц по видимым узлам |
| Конфликты с другими модами | id контролов с префиксом `VGS_` | Низкий |
| Выделенный сервер | GUI не создаётся | Низкий: код это уже учитывает |

---

## 6. Про `Bin64_Profile` и папку модов

`Bin64_Profile` — профилировочная сборка из Keen ModSDK; её нет в репозитории и не нужно
держать: это **не** рабочая установка игры и **не** папка модов.

- Моды игра читает из `%APPDATA%\SpaceEngineers\Mods\<папка>\`, а **не** из
  `Content\Mods` установки. [ПРОВЕРЕНО] на этой машине: в `Content\Mods` пусто, а в
  `%APPDATA%\SpaceEngineers\Mods` лежат `WeldPlanner`, `SpaceJS`, `MoreEnemies`.
- `Bin64_Profile` из репозитория не годится для проверок API: все проверки выполнены по
  рабочей установке `H:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64`.

---

## 7. Вердикт по этапам

| Этап | Реализуемость | Главный риск | Состояние |
|---|---|---|---|
| 1. Кнопка + полноэкранный редактор с полем и «Закрыть» | **Высокая** | что экран не конфликтует с открытым терминалом; шрифт и кисть отрисовки | реализован, ждёт приёмки в игре |
| 2. Блоки на поле, перетаскивание, порты по типу | **Высокая** | производительность на больших гридах | реализован, ждёт приёмки в игре |
| 3. Связи и логика | **Средняя** | модель событий (см. §3.2) | связи, тулбар, группы, event controller, переход в терминал, поиск и субгриды реализованы; вычислитель и источники событий — нет |

Приёмочные критерии и то, что ещё не сделано, — [`ROADMAP.md`](ROADMAP.md).

Реализация компилируется против реальных сборок игры и проходит вайтлист:

```powershell
pwsh -File tools/verify-compile.ps1     # COMPILE OK
pwsh -File tools/check-whitelist.ps1    # WHITELIST OK
```

Загрузка в игре подтверждена логом: мод компилируется игрой без `is prohibited`. Приёмочные
проверки в игре выполняет человек — см. [ROADMAP.md](ROADMAP.md).
