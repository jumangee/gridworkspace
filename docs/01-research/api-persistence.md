# Сохранение схемы и флаг «Show in VGS»: проверенный контракт

Два вопроса, на которые этот документ отвечает фактами, а не догадками:

1. **Как сохранить расположение узлов на схеме** так, чтобы оно возвращалось при следующем
   открытии редактора **и** после загрузки сохранённой игры.
2. **Можно ли добавить всем терминальным блокам** (`IMyTerminalBlock`) чекбокс «Show in VGS»,
   которым игрок скрывает узел со схемы.

Дата проверки: 2026-11-21. Игра: `H:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64`.
Всё, что помечено **[ПРОВЕРЕНО]**, получено рефлексией/IL установленной сборки или компиляцией
пробного файла через шлюзы проекта; рядом стоит команда.

Документ — **факты**, а не решение. Принятые решения по ним:
[ADR-0012](../adr/0012-layout-lives-on-the-block.md) (раскладка хранится на блоке) и
[ADR-0013](../adr/0013-show-in-vgs-flag.md) (флаг видимости и чекбокс «Show in VGS»).

Реализация на момент написания: `Game/NodeStateStore.cs`, `Model/NodeStateFormat.cs`,
`Node.Placed`, `Data/EntityComponents.sbc`. Формат позиции и правило «размещён» проверяются
офлайн (`tools/offline-check.ps1`); остальное — приёмка в игре, §7.

---

## 0. Краткий ответ

| Вопрос | Ответ | Чем |
|---|---|---|
| Где хранить позиции | `MyModStorageComponent` на **самом блоке**, по одному числу на блок | [ПРОВЕРЕНО] §2.1, §4 |
| Переживает ли это сохранение игры | Да: компонент входит в `MyObjectBuilder_CubeBlock.ComponentContainer` и пишется в сейв | [ПРОВЕРЕНО] §2.1 |
| Нужен ли свой файл сохранения | Нет. Но **обязателен** SBC с регистрацией GUID, иначе игра молча выбросит данные при сохранении | [ПРОВЕРЕНО] §2.1 |
| Чекбокс на все `IMyTerminalBlock` | Да, возможно: `IMyTerminalControlCheckbox` + `CustomControlGetter` | [ПРОВЕРЕНО] §5, пробный файл скомпилирован и прошёл вайтлист |
| Главная ловушка чекбокса | Регистрация «на тип» не доходит до типов, чьи контролы уже инициализированы. Работает только путь `CustomControlGetter` | [ПРОВЕРЕНО] §5.1 |
| Где хранить флаг | Там же, где позицию — на блоке | §4, §5.3 |

Обе задачи решаются **одним механизмом**: данные живут на блоке, а не в отдельной таблице,
привязанной к `EntityId`.

---

## 1. Где позиции теряются сейчас

Это не гипотеза, а чтение кода:

- `VgsEditorScreen.OpenFor` каждый раз вызывает `GridGraphBuilder.Build(grid)`, то есть
  строит **новый** `GridGraph`; узлы создаются с `Position = Vector2.Zero`
  (`Node..ctor`), после чего `GridGraph.LayoutUnplaced()` раскладывает их «как корабль».
- `GridGraphBuilder.Refresh` позиции выживших узлов сохраняет — поэтому **в пределах одного
  открытия** перетаскивание живёт.
- На закрытии экрана граф выбрасывается: `OnClosed` чистит ссылки, `_openEditor = null`.

Следствие: позиция живёт ровно столько, сколько открыт экран.

Отдельный дефект, который всплывёт сразу, как только позиция станет сохраняемой:
`LayoutUnplaced` считает узел неразмещённым по `Position == Vector2.Zero`. Узел, который игрок
честно поставил в `(0, 0)`, будет переставлен при каждом обновлении. Нужен явный признак
«размещён» (`bool Placed` в `Node`), а не сравнение с нулём.

---

## 2. Чем можно хранить

### 2.1. `MyModStorageComponent` — единственный санкционированный путь

[ПРОВЕРЕНО] `tools/dump-se-api.ps1 -Types 'Sandbox.Game.EntityComponents.MyModStorageComponent',
'VRage.Game.Components.MyModStorageComponentBase','VRage.Game.Definitions.MyModStorageComponentDefinition',
'VRage.ModAPI.IMyEntity'`:

```csharp
// Sandbox.Game.EntityComponents.MyModStorageComponent : MyModStorageComponentBase
public IReadOnlyDictionary<Guid, String> Storage { get; }
public virtual String  GetValue(Guid guid)
public virtual Boolean TryGetValue(Guid guid, out String value)
public virtual Void    SetValue(Guid guid, String value)
public virtual Boolean RemoveValue(Guid guid)
public virtual Boolean IsSerialized()          // m_storageData.Count > 0
public virtual MyObjectBuilder_ComponentBase Serialize(Boolean copy = false)

// VRage.ModAPI.IMyEntity
public MyModStorageComponentBase Storage { get; set; }

// VRage.Game.Definitions.MyModStorageComponentDefinition
public Guid[] RegisteredStorageGuids
```

Что важно по поведению, а не по сигнатурам:

**Компонент добавляется на сущность присваиванием свойства.** [ПРОВЕРЕНО]
`dump-se-il.ps1 -Assembly 'VRage.Game.dll' -Type 'VRage.Game.Entity.MyEntity' -Method 'set_Storage'`:

```csharp
set_Storage(value)  =>  get_Components().Add(value)
```

`MyEntity.Components_ComponentAdded` при добавлении компонента, реализующего
`MyModStorageComponentBase`, записывает его в поле `m_storage` — поэтому `get_Storage()`
сразу начинает его возвращать. Значит, создавать компонент вручную безопасно:

```csharp
IMyEntity entity = block;                       // IMyTerminalBlock наследует IMyEntity
var storage = entity.Storage as MyModStorageComponent;
if (storage == null)
{
    storage = new MyModStorageComponent();
    entity.Storage = storage;                   // сеттер сам добавит компонент в контейнер
}
storage.SetValue(guid, "…");
```

**`CustomData` блока живёт в этом же компоненте, под своим GUID.** [ПРОВЕРЕНО]
`dump-se-il.ps1 -Type 'Sandbox.Game.Entities.Cube.MyTerminalBlock' -Method 'get_CustomData'`:
геттер читает `get_Storage().TryGetValue(m_storageGuid, out …)`. `SetCustomData_Internal`
создаёт `new MyModStorageComponent()` тем же способом, что и выше. Значит: `Storage` — это
**одна** таблица `Guid → String` на сущность, и наш ключ в ней мирно сосуществует с
`CustomData`. Это же означает, что писать в `CustomData` «чтобы не заводить компонент» —
не отдельное хранилище, а тот же самый, только с ключом игры.

**GUID обязан быть зарегистрирован, иначе данные молча выбросятся при сохранении.**
[ПРОВЕРЕНО] `dump-se-il.ps1 -Type 'Sandbox.Game.EntityComponents.MyModStorageComponent'
-Method 'Serialize'`: сериализатор обходит
`MyDefinitionManager.Static.GetEntityComponentDefinitions<MyModStorageComponentDefinition>()`,
набирает `RegisteredStorageGuids` из **всех** таких определений, и копирует в сейв только
ключи из этого набора. Для остальных пишет в лог:

```
Not saving ModStorageComponent GUID: {0}, not claimed
```

и **не** сохраняет. Дубликаты GUID между модами дают вторую строку лога
(`Duplicate ModStorageComponent GUID`), то есть регистрация — это ещё и способ занять
пространство имён.

**Как зарегистрировать.** [ПРОВЕРЕНО] чтением самого файла игры
`Content/Data/EntityComponents.sbc`: в его шапке лежит готовый образец, и он же — официальная
инструкция Keen:

```xml
<EntityComponents>
  <EntityComponent xsi:type="MyObjectBuilder_ModStorageComponentDefinition">
    <Id>
      <TypeId>ModStorageComponent</TypeId>
      <SubtypeId>ModName</SubtypeId>          <!-- имя мода, чтобы не пересечься с другими -->
    </Id>
    <RegisteredStorageGuids>
      <guid>00000000-0000-0000-0000-000000000000</guid>
    </RegisteredStorageGuids>
  </EntityComponent>
</EntityComponents>
```

[ПРОВЕРЕНО] `dump-se-il.ps1 -Type 'Sandbox.Definitions.MyDefinitionManager' -Method
'GetDefinitionBuilders'`: мод-определения ищутся как
`MyFileSystem.GetFiles(context.ModPathData, "*.sbc", MySearchOption.AllDirectories)`, а
`MySearchOption` — `TopDirectoryOnly, AllDirectories` (`dump-se-api.ps1 -Types
'VRage.FileSystem.MySearchOption'`). То есть файл кладётся **в `Data/` мода** (глубина любая):
`src/VGS.Mod/Data/EntityComponents.sbc`.

**Как это попадает в сейв и возвращается из него.** [ПРОВЕРЕНО] по IL:

| Шаг | Доказательство |
|---|---|
| компонент лежит в `MyObjectBuilder_CubeBlock.ComponentContainer` | поле объявлено у `MyObjectBuilder_CubeBlock` (`dump-se-api.ps1`) |
| контейнер пишется, когда в нём что-то есть | `ShouldSerializeComponentContainer` → `Components.Count` |
| пустой компонент не пишется | `Serialize` возвращает `null`, если `Storage.Dictionary.Count == 0` |
| при загрузке компоненты восстанавливаются | `MyEntity.Init(builder)` → `InitComponentsExtCallback(Components, typeId, subtypeId, builder.ComponentContainer)` |
| значения возвращаются | `MyModStorageComponent.Deserialize` → `m_storageData = builder.Storage.Dictionary` |

Отдельный хук сохранения **не нужен**: `SetValue` меняет объект в памяти, а сериализует его
игра вместе с блоком.

### 2.2. `CustomData` + `MyIni`

`IMyTerminalBlock.CustomData` (`Sandbox.ModAPI.Ingame.IMyTerminalBlock`) и `MyIni`
(`VRage.Game.ModAPI.Ingame.Utilities`, [ПРОВЕРЕНО] `dump-se-api.ps1`) — рабочий путь, но это
**поле игрока**: он его видит, правит, копирует, и в него же пишут скрипты Programmable Block.
Для «где я мышкой поставил карточку» это неверный адрес: игрок сотрёт текст и потеряет
раскладку, а разбор `CustomData` в чужом скрипте сломается от нашего блока.

### 2.3. Файлы в `%APPDATA%\SpaceEngineers\Storage\…`

`MyAPIGateway.Utilities.WriteFileInLocalStorage/WorldStorage` — [ПРОВЕРЕНО] есть в дампе
(`docs/01-research/api-surface.md`). Годится только для **личных** настроек: файл не едет с
миром, не едет с блюпринтом, не виден другим игрокам и **не попадает в бэкапы мира**
(известный баг, см. `web-research.md` §«Save_and_Sync»). Для схемы, которая должна
восстанавливаться из сохранённой игры, это неверный инструмент.

---

## 3. Ключ, по которому хранится позиция

Если данные лежат в таблице «кто где», нужен ключ. Проверенные факты о ключах:

| Ключ | Переживает сохранение/загрузку | Переживает блюпринт/проектор | Переживает резку/слияние грида |
|---|---|---|---|
| `EntityId` | **да** | **нет** | **нет** |
| `Vector3I` позиция в гриде + подтип | да | да (относительная) | нет (сдвигается вместе с блоком) |
| **сам блок** (данные на блоке) | да | да | да |

Доказательства:

- **`EntityId` переживает save/load.** [ПРОВЕРЕНО]
  `MyObjectBuilder_CubeBlock.ShouldSerializeEntityId` → `EntityId > 0`, то есть id пишется в
  сейв; `MyEntity.Init(builder)` при `builder.EntityId != 0` делает `set_EntityId(builder.EntityId)`,
  иначе `AllocateEntityID()`. Команды: `dump-se-il.ps1 -Assembly 'VRage.Game.dll' -Type
  'VRage.Game.MyObjectBuilder_CubeBlock' -Method 'ShouldSerializeEntityId'` и
  `dump-se-il.ps1 -Assembly 'VRage.Game.dll' -Type 'VRage.Game.Entity.MyEntity' -Method 'Init'`.
- **`EntityId` НЕ переживает копирование и проектор.** [ПРОВЕРЕНО]
  `MyObjectBuilder_CubeBlock.Remap` вызывает `IMyRemapHelper.RemapEntityId(builder.EntityId)`;
  его вызывают `MyObjectBuilder_CubeGrid.Remap` и `MyObjectBuilder_ProjectorBase.Remap`.
- **`EntityId` НЕ переживает резку/слияние грида.** [ПРОВЕРЕНО]
  `MyCubeGrid.MoveBlocksByObjectBuilders` вызывает `MyObjectBuilder_CubeBlock.Remap`.
  (Найдено обходом IL всех сборок на ссылки на эти методы; отдельного сканера в `tools/`
  пока нет — команда в §6.)

Вывод, который и есть ответ на вопрос 1: **не храните таблицу «id → позиция»**. Положите
позицию на сам блок. Тогда ключа нет вовсе, и все три строки таблицы становятся «да»
бесплатно: при копировании, разрезании и проецировании данные едут внутри
`ComponentContainer` блока, потому что `MyObjectBuilder_CubeBlock.Remap` вызывает
`Remap` у каждого компонента, а `MyObjectBuilder_ModStorageComponent` своего `Remap` не
переопределяет — то есть данные проходят через remap неизменными.

Побочный, но приятный факт: `MyObjectBuilder_CubeBlock.SetupForProjector` вычищает из
контейнера **только** инвентари (`MyObjectBuilder_Inventory` / агрегаты), поэтому раскладка
переезжает в проекции вместе с кораблём. [ПРОВЕРЕНО] по IL.

---

## 4. Рекомендуемая схема для позиций

Один GUID на блок, одно значение, формат — два числа в инвариантной культуре:

```
guid  = 0d3c1b7a-…            (в VgsConstants, зарегистрирован в Data/EntityComponents.sbc)
value = "<x> <y>"             x, y — graph units (те же, что Node.Position)
```

Почему не JSON и не `MyIni`:

- строка из двух чисел парсится `float.TryParse` + `Split(' ')` без зависимостей и без
  культуры по умолчанию (`InvariantCulture` обязательна, иначе на локали с запятой `0,5`
  не прочитается);
- `MyIni` на два числа — это разбор документа на каждое чтение при построении графа, то есть
  на каждый блок при каждом открытии; для 500 блоков это 500 разборов текста ради двух чисел;
- расширяемость: если понадобится третье поле (например, «свёрнута»), формат меняется на
  `"x y flag"` с проверкой количества частей — это дешевле, чем миграция INI-секций.

Чтение — в `GridGraphBuilder.CreateNode`, там же, где сейчас читается имя и тулбар: блок под
рукой, лишнего поиска в мире нет. Запись — **в конце перетаскивания**, а не на каждом кадре и
не только в `OnClosed`:

```csharp
// VgsEditorScreen.HandleCanvasInput, ветка leftReleased
if (leftReleased)
{
    if (_dragging && _draggedNode != null) NodeStateStore.SavePosition(_draggedNode);
    _draggedNode = null;
    _dragging = false;
}
```

Причины именно так: писать на каждом кадре перетаскивания — это `SetValue` 60 раз в секунду
(аллокации строки и запись в словарь); писать только в `OnClosed` — потерять раскладку при
выходе в главное меню или падении игры, потому что `OnClosed` тогда не придёт.

Размещение по слоям (ADR-0003): чтение и запись живут в `Vgs.Game` (новый
`NodeStateStore`, рядом с `ToolbarWriter`), `Vgs.Ui` их только вызывает. `Node` остаётся
чистой моделью и получает два поля — `Position` и признак «размещён».

Ёмкость: одна запись `Guid → String` на блок. Для 500 терминальных блоков это ~500 коротких
строк в `ComponentContainer` каждого блока. Это заметно в размере сейва, но не в памяти;
если понадобится экономия — данные можно перенести на грид (одна запись) ценой того, что
резка грида раскладку потеряет.

---

## 5. Вопрос 2: чекбокс «Show in VGS» на всех терминальных блоках

### 5.1. Да, это возможно. Механика — проверенная

[ПРОВЕРЕНО] `dump-se-api.ps1 -Types 'Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControlCheckbox',
'Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalValueControl`1','Sandbox.ModAPI.IMyTerminalControls'`:

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

`CreateControl<IMyTerminalControlCheckbox, IMyTerminalBlock>` работает не «по имени», а
разрешением интерфейса в конкретный класс игры. [ПРОВЕРЕНО] по IL
`dump-se-il.ps1 -Type 'Sandbox.Game.Gui.MyTerminalControls' -Method 'CreateControl'`:
`GetProducedType` для интерфейса вызывает `FindTerminalTypeFromInterface`, тот сканирует
атрибуты `Sandbox.ModAPI.MyTerminalInterfaceAttribute` по сборке `Sandbox.Game` и находит
пару `IMyTerminalBlock → Sandbox.Game.Entities.Cube.MyTerminalBlock`. Затем
`CreateGenericControl` делает `typeof(MyTerminalControlCheckbox<>).MakeGenericType(MyTerminalBlock)`
и `Activator.CreateInstance` с аргументами `(id, "", "", "", "", false, false, false, 1f)`.
То есть мод получает настоящий `MyTerminalControlCheckbox<MyTerminalBlock>`.

Ключевой факт про порядок, из-за которого «просто `AddControl`» — ловушка:

[ПРОВЕРЕНО] `dump-se-il.ps1 -Type 'Sandbox.Game.Gui.MyTerminalControlFactory' -Method
'AddControl'` и `-Method 'InitializeControls'`:

- контролы хранятся в `Dictionary<Type, BlockData>` (`m_controls`), по одной записи на тип;
- `AddControl(Type, control)` кладёт контрол **только** в `BlockData` этого типа;
- наследование разворачивается **один раз**, в `InitializeControls(type)`: он идёт по цепочке
  `BaseType` и **копирует** контролы из уже существующего `BlockData` базового типа.

Значит: если тип блока уже инициализирован (а игра делает это при загрузке для многих типов),
контрол, добавленный позже в `MyTerminalBlock`, до него **не дойдёт**. Поэтому путь «на тип»
работает не всегда, и именно поэтому в `VgsTerminalControls` уже есть и
`AddControl<IMyCockpit>`, и `CustomControlGetter`.

Надёжный путь — `CustomControlGetter`. [ПРОВЕРЕНО]
`dump-se-il.ps1 -Type 'Sandbox.Game.Gui.MyTerminalControls' -Method 'GetControls'`:
`MyTerminalControls.GetControls(IMyTerminalBlock block)` строит список контролов типа блока и
затем, если делегат не `null`, вызывает его, передавая **этот блок** и **этот список**, после
чего возвращает дополненный список. Вызывается он из
`MyGuiControlGenericFunctionalBlock.RecreateBlockControls` при каждой пересборке панели
терминала. То есть:

- вызов привязан к конкретному блоку, а не к типу;
- добавление всегда актуально, порядок инициализации не важен;
- после добавления игра сама зовёт `UpdateVisual()` у контрола, поэтому чекбокс сразу
  показывает значение из `Getter`.

```csharp
_checkbox = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlCheckbox, IMyTerminalBlock>(
    VgsConstants.ShowInVgsControlId);
_checkbox.Title   = MyStringId.GetOrCompute(VgsText.ShowInVgsTitle);
_checkbox.Tooltip = MyStringId.GetOrCompute(VgsText.ShowInVgsTooltip);
_checkbox.OnText  = MyStringId.GetOrCompute(VgsText.On);
_checkbox.OffText = MyStringId.GetOrCompute(VgsText.Off);
_checkbox.Getter  = block => NodeStateStore.IsShown(block);
_checkbox.Setter  = (block, value) => NodeStateStore.SetShown(block, value);
_checkbox.Visible = block => block != null;

MyAPIGateway.TerminalControls.AddControl<IMyTerminalBlock>(_checkbox);   // для типов, ещё не инициализированных
MyAPIGateway.TerminalControls.CustomControlGetter += AppendCheckbox;     // основной путь
```

`Visible`/`Enabled` можно не назначать: конструктор `MyTerminalControl<TBlock>` уже
проставляет обе лямбды, и обе возвращают `true`. [ПРОВЕРЕНО] по IL: в `.ctor` поля
инициализируются кэшированными делегатами `<.ctor>b__27_0` / `<.ctor>b__27_1`, тело каждого —
`ldc.i4.1; ret`. А `ITerminalControl.IsVisible` — это прямой вызов `Visible(block)` без
проверки на `null`, поэтому «оставить как есть» безопасно, а «обнулить» — нет.

**Пробный файл проверен шлюзами.** [ПРОВЕРЕНО] Собран вне мода (временная папка) и прогнан:

```
COMPILE OK — 1 file(s), 0 warning(s)
WHITELIST OK — every referenced type is allowed for a mod script
```

Пробник использовал ровно тот набор, что нужен обеим задачам: `CreateControl` чекбокса,
`CustomControlGetter`, `MyModStorageComponent` (создание и `SetValue`/`TryGetValue`), `MyIni`
как альтернативу. Ссылки пробника: `mscorlib, Sandbox.Common, Sandbox.Game, VRage.Game,
VRage.Library, VRage`. Существенно: `MyModStorageComponent` требует
`using Sandbox.Game.EntityComponents;` — это пространство имён **разрешено** вайтлистом
(в отличие от `Sandbox.Game.Screens.Helpers`, см. [api-toolbar.md](api-toolbar.md) §0).

### 5.2. Чего это стоит

Чекбокс получит **каждый** терминальный блок в мире, включая блоки других игроков и других
модов: контролы в игре — это UI, они не знают про «наш» грид. Это не ошибка, а цена
формулировки «всем блокам, которые реализуют `IMyTerminalBlock`». Практические следствия:

- в терминале каждого конвейера, дюбеля, брони-панели появится ещё одна строка; на большом
  корабле это заметный шум;
- контрол один на всех (как и любой ванильный), состояние берётся из `Getter` для
  `TargetBlocks`; при мультивыделении чекбокс покажет значение первого блока — это поведение
  ванильных контролов, не наше;
- `Getter` вызывается при каждой перерисовке панели, поэтому он должен быть дешёвым:
  один `TryGetValue` в словарь, без поиска по миру. Поиск блока по `EntityId` тут не нужен —
  блок приходит аргументом.

### 5.3. Где хранить флаг

Там же, где позицию, — на блоке, вторым ключом в том же компоненте:

```
guid  = b1e4a2c8-…            (второй GUID, тоже в RegisteredStorageGuids)
value = "0" | "1"             отсутствие значения = "показывать"
```

Это даёт согласованное поведение: скрыл блок — флаг уехал с блоком в блюпринт, при
копировании корабля скрытые блоки остаются скрытыми, при разрезании ничего не теряется.

Альтернатива, которую стоит держать в голове: если «Show in VGS» — это **личное**
предпочтение игрока (я не хочу видеть конвейеры, а мой друг хочет), то место ему — не в мире,
а в локальном файле мода (§2.3), потому что запись в компонент сущности — это данные мира,
и на выделенном сервере клиент их менять не сможет (та же причина, по которой
`ToolbarWriter` на удалённом клиенте получает `Apply() == false`, см.
[api-toolbar.md](api-toolbar.md) §4.1). Решать это надо явно: «схема общая» или «схема у
каждого своя». Позиции, кстати, относятся к тому же выбору.

### 5.4. Альтернатива без терминала

Если цель — «упростить пользователю работу со схемой», чекбокс в терминале каждого блока —
не единственный и, вероятно, не лучший путь. Тот же флаг можно ставить из самого редактора
(контекстное меню карточки → «Скрыть» / «Показать все»), и тогда:

- не нужен `CustomControlGetter` на всех блоках;
- не появляется строка в терминале у чужих блоков;
- решение остаётся там, где игрок им пользуется.

Хранилище при этом то же самое. Терминальный чекбокс имеет смысл как **дополнение** — когда
игрок правит корабль и хочет спрятать блок, не открывая редактор.

---

## 6. Чем проверено

```powershell
# состав типов и членов
& .\tools\dump-se-api.ps1 -Types 'Sandbox.Game.EntityComponents.MyModStorageComponent',
    'VRage.Game.Components.MyModStorageComponentBase',
    'VRage.Game.Definitions.MyModStorageComponentDefinition','VRage.ModAPI.IMyEntity'

& .\tools\dump-se-api.ps1 -Types 'Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControlCheckbox',
    'Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalValueControl`1','Sandbox.ModAPI.IMyTerminalControls'

# поведение
& .\tools\dump-se-il.ps1 -Assembly 'VRage.Game.dll' -Type 'VRage.Game.Entity.MyEntity' -Method 'set_Storage'
& .\tools\dump-se-il.ps1 -Assembly 'VRage.Game.dll' -Type 'VRage.Game.Entity.MyEntity' -Method 'Components_ComponentAdded'
& .\tools\dump-se-il.ps1 -Assembly 'VRage.Game.dll' -Type 'VRage.Game.Entity.MyEntity' -Method 'Init'
& .\tools\dump-se-il.ps1 -Type 'Sandbox.Game.EntityComponents.MyModStorageComponent' -Method 'Serialize'
& .\tools\dump-se-il.ps1 -Type 'Sandbox.Game.EntityComponents.MyModStorageComponent' -Method 'Deserialize'
& .\tools\dump-se-il.ps1 -Type 'Sandbox.Game.Entities.Cube.MyTerminalBlock' -Method 'get_CustomData'
& .\tools\dump-se-il.ps1 -Type 'Sandbox.Game.Entities.Cube.MyTerminalBlock' -Method 'SetCustomData_Internal'
& .\tools\dump-se-il.ps1 -Assembly 'VRage.Game.dll' -Type 'VRage.Game.MyObjectBuilder_CubeBlock' -Method 'ShouldSerializeEntityId'
& .\tools\dump-se-il.ps1 -Assembly 'VRage.Game.dll' -Type 'VRage.Game.MyObjectBuilder_CubeBlock' -Method 'Remap'
& .\tools\dump-se-il.ps1 -Assembly 'VRage.Game.dll' -Type 'VRage.Game.MyObjectBuilder_CubeBlock' -Method 'SetupForProjector'
& .\tools\dump-se-il.ps1 -Type 'Sandbox.Game.Gui.MyTerminalControls' -Method 'CreateControl'
& .\tools\dump-se-il.ps1 -Type 'Sandbox.Game.Gui.MyTerminalControls' -Method 'GetControls'
& .\tools\dump-se-il.ps1 -Type 'Sandbox.Game.Gui.MyTerminalControlFactory' -Method 'AddControl'
& .\tools\dump-se-il.ps1 -Type 'Sandbox.Game.Gui.MyTerminalControlFactory' -Method 'InitializeControls'
& .\tools\dump-se-il.ps1 -Type 'Sandbox.Game.Gui.MyTerminalControl`1' -Method '.ctor'
& .\tools\dump-se-il.ps1 -Type 'Sandbox.Definitions.MyDefinitionManager' -Method 'GetDefinitionBuilders'

# регистрация GUID и раздача контролов проверены компиляцией пробного файла
& .\tools\verify-compile.ps1 -SourcePath <probe> -Out <probe-out>   # COMPILE OK
& .\tools\check-whitelist.ps1 -Assembly <probe-out>\VGS.verify.dll  # WHITELIST OK
```

Ссылки на методы `MyObjectBuilder_CubeBlock.Remap` / `MyCubeGrid.MoveBlocksByObjectBuilders`
найдены разовым обходом IL всех типов сборки (скрипт в репозиторий не добавлен). Если факт
понадобится перепроверить штатно — это кандидат в `tools/`: «кто вызывает метод X».

Файл игры, из которого взят формат регистрации GUID:
`H:\SteamLibrary\steamapps\common\SpaceEngineers\Content\Data\EntityComponents.sbc`
(комментарий в шапке файла — официальная инструкция Keen).

---

## 7. Что осталось непроверенным

| Утверждение | Почему не проверено | Чем проверять |
|---|---|---|
| Данные `MyModStorageComponent` реально доезжают до сейва и возвращаются | Нужен запуск игры: сохранить мир, перезагрузить, открыть редактор | человек: поставить два блока, выйти в меню, загрузить мир, открыть схему |
| SBC с `RegisteredStorageGuids` подхватывается именно модом | Нужен запуск игры | в логе не должно быть `Not saving ModStorageComponent GUID: … not claimed` после сохранения |
| Запись в компонент с клиента в мультиплеере | Нужны два клиента | хост ставит флаг → клиент видит; клиент ставит → проверять, дойдёт ли |
| Чекбокс не конфликтует с терминалами других модов | Нужны моды, добавляющие свои контролы | открыть терминал блока из мода, где контролы переопределены |
| Поведение `OnClosed` при выходе в главное меню | Нужен запуск игры | перетащить блок, выйти в меню, зайти снова |

Ни одно из этих «не проверено» не мешает принять решение: все они — про наблюдаемое
поведение уже существующего механизма, а не про его наличие.
