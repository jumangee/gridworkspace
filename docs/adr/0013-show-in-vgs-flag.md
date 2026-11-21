# 0013. Видимость узла: флаг на блоке и чекбокс «Show in VGS» у всех терминальных блоков

- Status: accepted
- Date: 2026-11-21
- Supersedes: —
- Amends: [0004](0004-terminal-control-registration.md) — правило «всё, что зависит от блока,
  решается в колбэках» остаётся; добавляется второй контрол и уточняется, что для типа-предка
  регистрация **не доходит** до уже инициализированных типов, поэтому основной путь —
  `CustomControlGetter` (см. §Context 2)
- Joint review: не требовалось — требование («чекбокс у всех блоков, реализующих
  `IMyTerminalBlock`», «скрывать отдельные узлы, упрощая работу со схемой») пришло от владельца
  продукта. Факты API проверены до решения, см. §Verification
- Implementation: **done** (2026-11-21) — `Game/VgsTerminalControls.cs` (второй контрол),
  `Game/NodeStateStore.cs`, фильтр в `GridGraphBuilder.Refresh`; шлюзы зелёные, приёмка в
  игре — за человеком (§Verification)

## Context

### 1. Зачем флаг

На большом корабле схема — это сотни карточек. Большая часть из них (конвейеры, броня, дюбели)
нужна только как фон и мешает: линии тянутся через весь экран, карточки перекрывают друг друга.
Требование: у игрока должен быть способ **убрать конкретный блок со схемы**, не разбирая его на
корабле и не теряя связи.

Флаг — свойство блока, а не режим экрана: «я спрятал этот конвейер» должно переживать закрытие
редактора и загрузку мира. Отсюда сразу следует, что хранить его надо так же, как раскладку —
[ADR-0012](0012-layout-lives-on-the-block.md).

### 2. Чекбокс на все `IMyTerminalBlock` — возможно, но не через `AddControl`

Контракт есть и он полный. [ПРОВЕРЕНО] `tools/dump-se-api.ps1` по
`Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControlCheckbox`,
`IMyTerminalValueControl<TValue>`, `Sandbox.ModAPI.IMyTerminalControls`:

```csharp
public interface IMyTerminalControlCheckbox
    : IMyTerminalControl, IMyTerminalValueControl<Boolean>, IMyTerminalControlTitleTooltip
{
    MyStringId OnText  { get; set; }
    MyStringId OffText { get; set; }
}

public interface IMyTerminalValueControl<TValue>
{
    Func<IMyTerminalBlock, TValue> Getter { get; set; }
    Action<IMyTerminalBlock, TValue> Setter { get; set; }
}

// IMyTerminalControls
TControl CreateControl<TControl, TBlock>(String id);
Void AddControl<TBlock>(IMyTerminalControl item);
event CustomControlGetDelegate CustomControlGetter;   // (IMyTerminalBlock, List<IMyTerminalControl>)
```

`CreateControl` разрешает интерфейс в конкретный класс игры, а не ищет по имени. [ПРОВЕРЕНО]
`dump-se-il.ps1 -Type 'Sandbox.Game.Gui.MyTerminalControls' -Method 'CreateControl'`:
`GetProducedType` для интерфейса вызывает `FindTerminalTypeFromInterface`, тот сканирует
атрибуты `Sandbox.ModAPI.MyTerminalInterfaceAttribute` по сборке `Sandbox.Game` и находит пару
`IMyTerminalBlock → Sandbox.Game.Entities.Cube.MyTerminalBlock`; затем `CreateGenericControl`
делает `typeof(MyTerminalControlCheckbox<>).MakeGenericType(MyTerminalBlock)` и
`Activator.CreateInstance`. Мод получает настоящий `MyTerminalControlCheckbox<MyTerminalBlock>`.

**Ловушка — в том, куда попадает `AddControl`.** [ПРОВЕРЕНО]
`dump-se-il.ps1 -Type 'Sandbox.Game.Gui.MyTerminalControlFactory' -Method 'AddControl'`,
`-Method 'InitializeControls'`, `-Method 'AddBaseClass'`:

- контролы хранятся в `Dictionary<Type, BlockData> m_controls` — по записи на тип;
- `AddControl(Type, control)` добавляет контрол **только** в `BlockData` этого типа;
- наследование разворачивается **один раз**, в `InitializeControls(type)`: он идёт по цепочке
  `BaseType` и **копирует** контролы из уже существующего `BlockData` базового типа.

Значит: если тип блока уже инициализирован, контрол, добавленный позже в `MyTerminalBlock`, до
него **не дойдёт**. Регистрация «на предка» работает только для типов, которые игра
инициализирует после нас, — то есть непредсказуемо.

Надёжный путь — `CustomControlGetter`. [ПРОВЕРЕНО]
`dump-se-il.ps1 -Type 'Sandbox.Game.Gui.MyTerminalControls' -Method 'GetControls'`:
`GetControls(IMyTerminalBlock block)` строит список контролов типа блока и, если делегат не
`null`, вызывает его, передавая **этот блок** и **этот список**, после чего возвращает
дополненный список. Вызывается он из
`MyGuiControlGenericFunctionalBlock.RecreateBlockControls` при каждой пересборке панели
терминала. [ПРОВЕРЕНО] там же: после добавления контрола в список игра сама зовёт
`ITerminalControl.UpdateVisual()`, поэтому чекбокс сразу показывает значение из `Getter`.

Свойства контрола можно не назначать: конструктор `MyTerminalControl<TBlock>` инициализирует
`Visible` и `Enabled` кэшированными лямбдами, тело каждой — `ldc.i4.1; ret`, то есть всегда
`true`. [ПРОВЕРЕНО] `dump-se-il.ps1 -Type 'Sandbox.Game.Gui.MyTerminalControl`1' -Method '.ctor'`
и разбором вложенных `<.ctor>b__27_0` / `<.ctor>b__27_1`. А `ITerminalControl.IsVisible` — прямой
вызов `Visible(block)` без проверки на `null`, поэтому «оставить как есть» безопасно, а
«обнулить» — нет.

### 3. Чего это стоит

Контролы в игре регистрируются на тип, а `CustomControlGetter` вызывается для **любого**
терминального блока, который игрок открывает, — включая блоки других игроков и других модов.
То есть чекбокс появится в терминале каждого конвейера, дюбеля и панели брони. Это не дефект, а
прямое следствие формулировки «всем блокам, реализующим `IMyTerminalBlock`», но цену надо
назвать: одна лишняя строка в терминале и один вызов `Getter` на каждую перерисовку панели.

## Decision

### 1. Флаг — данные мира, на блоке

Второй GUID в том же `MyModStorageComponent`, что и раскладка:

```
guid  = b1e4a2c8-77d3-4a0f-8c61-2f9e5b0d3a47   (VgsConstants.ShowInVgsKey)
value = "0" | "1"                              отсутствие значения = "показывать"
```

Три правила, каждое — про поведение, а не про формат:

- **отсутствие значения означает «показывать»**. Иначе все существующие миры и все блоки,
  которых игрок не касался, исчезли бы со схемы после установки мода;
- **значение едет с блоком**: скрыл — флаг уехал в блюпринт, при копировании корабля скрытые
  блоки остаются скрытыми, при резке ничего не теряется (механика — ADR-0012 §Context 3);
- **флаг — часть схемы, а не личная настройка игрока**. Схема общая, и «этот конвейер не
  относится к логике» — утверждение о корабле, а не о моих глазах. Цена этого выбора — в
  §Consequences (мультиплеер).

### 2. Один контрол, зарегистрированный двумя путями

```csharp
_checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyTerminalBlock>(
    VgsConstants.ShowInVgsControlId);
_checkbox.Title   = MyStringId.GetOrCompute(VgsText.ShowInVgsTitle);
_checkbox.Tooltip = MyStringId.GetOrCompute(VgsText.ShowInVgsTooltip);
_checkbox.OnText  = MyStringId.GetOrCompute(VgsText.On);
_checkbox.OffText = MyStringId.GetOrCompute(VgsText.Off);
_checkbox.Getter  = NodeStateStore.IsShown;
_checkbox.Setter  = NodeStateStore.SetShown;

MyAPIGateway.TerminalControls.AddControl<IMyTerminalBlock>(_checkbox);   // типы, ещё не инициализированные
MyAPIGateway.TerminalControls.CustomControlGetter += AppendCheckbox;     // основной путь: на блок
```

`AddControl<IMyTerminalBlock>` остаётся, потому что он покрывает типы, которые игра
инициализирует **после** нас, и стоит одну строку. Основным путём считается
`CustomControlGetter`, потому что только он не зависит от порядка инициализации (§Context 2).

Оба пути безопасны вместе: список контролов типа — `MyUniqueList`, повторное добавление
игнорируется, а наш делегат дополнительно проверяет `Contains`. Тот же приём уже используется
для кнопки кокпита, и там он работает.

`Visible`/`Enabled` **не назначаются**: значения по умолчанию — `true`, а `IsVisible` вызывает
делегат без проверки на `null`. Обнулять их нельзя: `ITerminalControl.IsVisible` упадёт.

### 3. `Getter` дешёвый, `Setter` обновляет визуал

`Getter` вызывается при каждой перерисовке панели терминала, поэтому он делает ровно один
`TryGetValue` в словарь компонента. Поиск блока по `EntityId` не нужен: блок приходит
аргументом — это и есть причина, по которой выбран `CustomControlGetter`, а не своя таблица.

`Setter` пишет значение и вызывает `UpdateVisual()`, чтобы галочка отражала новое состояние
немедленно, не дожидаясь следующей пересборки панели.

### 4. Скрытый узел не попадает в граф

`GridGraphBuilder.Refresh` пропускает блоки с выключенным флагом, а узел, который стал скрытым,
удаляется из графа через `RemoveNode`. Следствия, которые надо принять сознательно:

- линии скрытого узла **не рисуются**, в том числе если он был источником. Это и есть смысл
  флага, но пользователь должен об этом знать — поэтому в подсказке чекбокса написано, что
  блок исчезает со схемы;
- **скрытие ничего не удаляет из тулбаров.** Связи продолжают работать в игре: тулбар —
  источник истины ([ADR-0011](0011-toolbar-is-the-connection.md)), а редактор лишь его
  отражение. Когда блок снова показан, `ReconcileToolbarEdges` восстановит его линии из
  тулбара. Поэтому «скрыть» — безопасная операция, а не удаление;
- «скрыть узел с сохранением его линий» не делается: узел либо в графе, и тогда он нарисован,
  либо его в графе нет. Промежуточное состояние («в графе, но не нарисован») сломало бы
  hit-testing и маршруты линий, которые считаются по `GridGraph.Lines`.

### 5. Запрещено

- хранить флаг в `CustomData` (поле игрока) или в личном файле мода (не едет с кораблём);
- рисовать скрытый узел «полупрозрачным» вместо исключения из графа;
- проверять флаг в пути отрисовки: решение принимается при обновлении графа, иначе
  `Vgs.Ui` начнёт читать живые блоки во время рисования ([ADR-0003](0003-layering.md));
- переиспользовать игровой `ShowInTerminal`: он прячет блок из списков терминала и имеет свои
  побочные эффекты, к схеме он отношения не имеет.

## Alternatives

| Вариант | Почему отклонён |
|---|---|
| Только контекстное меню карточки в редакторе, без чекбокса | Требование называет именно чекбокс в терминале; меню — отдельный шаг, а не замена |
| Флаг в локальном файле игрока | Тогда «схема» перестаёт быть свойством корабля: другой игрок видит другие узлы, блюпринт флаг не несёт |
| Флаг в `CustomData` | Поле игрока: он его правит и копирует, скрипты PB его разбирают |
| Регистрация только `AddControl<IMyTerminalBlock>` | Не доходит до уже инициализированных типов: чекбокс появился бы у части блоков (см. §Context 2) |
| Регистрация только `CustomControlGetter` | Работает, но `AddControl` бесплатен и покрывает типы, инициализированные позже; отказываться не за чем |
| Свой список скрытых блоков в файле мода, ключ — `EntityId` | Ломается при копировании и резке грида, см. ADR-0012 §Context 2 |
| Прятать блок через игровой `ShowInTerminal` | Другой предмет: убирает блок из терминала, а не со схемы; побочные эффекты на других игроков |
| Фильтр по типу блока («скрыть все конвейеры») | Не то, что просили: решение принимается про конкретный блок; типовой фильтр — возможное расширение позже |

## Consequences

Что становится легко:

- схему можно привести в читаемый вид, не разбирая корабль и не теряя связи;
- скрытие неразрушающе: тулбар не трогается, линии возвращаются сами;
- флаг и раскладка — один механизм хранения, один `NodeStateStore`, одна регистрация GUID.

Чем платим:

- **чекбокс появляется у всех терминальных блоков мира**, включая чужие и модовые. Это
  неизбежно при выбранной формулировке; на большом корабле это заметный шум в терминале;
- **`Getter` работает на каждой перерисовке панели** — поэтому он ограничен одним
  `TryGetValue` и не ищет блоки в мире;
- **мультиплеер**: `MyModStorageComponent` не синхронизируется сам (ADR-0012 §Context 3).
  На удалённом клиенте включение/выключение флага останется локальным, пока не появится
  `RegisterSecureMessageHandler` и серверная авторизация записи. До этого флаг — корректно
  работающая функция одиночной игры и хоста, и это надо сказать пользователю, а не делать вид,
  что он работает везде;
- **скрытый источник прячет свои линии** — поведение намеренное, но его надо объяснить
  подсказкой, иначе выглядит как «связь пропала».

Если решение окажется неверным (флаг должен быть личным), откат дешёвый: меняется реализация
`NodeStateStore.IsShown/SetShown` — с компонента сущности на локальный файл; ни редактор, ни
граф не трогаются. Дороже станет только миграция уже расставленных флагов.

## Verification

Проверено на этой машине (2026-11-21), команды и результаты — в
[`docs/01-research/api-persistence.md`](../01-research/api-persistence.md) §5:

```powershell
& .\tools\dump-se-api.ps1 -Types 'Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControlCheckbox',
    'Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalValueControl`1','Sandbox.ModAPI.IMyTerminalControls'
& .\tools\dump-se-il.ps1 -Type 'Sandbox.Game.Gui.MyTerminalControls' -Method 'CreateControl'
& .\tools\dump-se-il.ps1 -Type 'Sandbox.Game.Gui.MyTerminalControls' -Method 'GetControls'
& .\tools\dump-se-il.ps1 -Type 'Sandbox.Game.Gui.MyTerminalControlFactory' -Method 'AddControl'
& .\tools\dump-se-il.ps1 -Type 'Sandbox.Game.Gui.MyTerminalControlFactory' -Method 'InitializeControls'
& .\tools\dump-se-il.ps1 -Type 'Sandbox.Game.Gui.MyTerminalControl`1' -Method '.ctor'
& .\tools\dump-se-il.ps1 -Type 'Sandbox.Graphics.GUI.MyGuiControlGenericFunctionalBlock' -Method 'RecreateBlockControls'
```

Создание чекбокса, `CustomControlGetter` и запись/чтение `MyModStorageComponent` проверены
компиляцией пробного файла вне мода: `COMPILE OK`, `WHITELIST OK` (`WHITELIST OK` важен:
`Sandbox.Game.EntityComponents` разрешён, `Sandbox.Game.Screens.Helpers` — нет).

Шлюзы проекта после реализации:

```powershell
& .\tools\verify-compile.ps1        # COMPILE OK
& .\tools\check-whitelist.ps1       # WHITELIST OK
& .\tools\review-check.ps1          # без находок
& .\tools\offline-check.ps1         # «скрытый узел не попадает в граф» — новое утверждение в tests/OfflineChecks.cs
```

Человек в игре (без этого «работает» не считается):

1. Открыть терминал конвейера, дюбеля, реактора, кокпита — чекбокс `Show in VGS` есть у всех,
   включая блоки, добавленные другим модом.
2. Выключить флаг у конвейера → закрыть и открыть редактор → карточки нет.
3. Включить обратно → карточка вернулась; если у блока были связи, линии вернулись сами.
4. Скрыть блок-источник → его линии исчезли; запустить событие в игре → получатель **всё равно**
   срабатывает (тулбар не тронут).
5. Скопировать корабль в блюпринт, вставить → скрытые блоки остались скрытыми.
6. Загрузить сохранённый мир → флаги на месте.
7. В мультиплеере: хост выключил флаг → клиент видит блок скрытым после перезахода; клиент
   выключил флаг → проверить, что именно происходит (ожидаемо: локально, без репликации).

Не проверено и требует игры: что `CreateControl<IMyTerminalControlCheckbox, IMyTerminalBlock>`
действительно рисует чекбокс в терминале (по IL — да, но это не запуск игры); что чекбокс не
конфликтует с терминалами других модов, которые переопределяют `CustomControlGetter`; поведение
при мультивыделении блоков (ожидаемо: значение первого блока, как у ванильных контролов).
