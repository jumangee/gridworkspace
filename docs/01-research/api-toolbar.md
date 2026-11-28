# Тулбары блоков: проверенный контракт

Как блок Space Engineers управляет другими блоками и что из этого доступно моду.
Все утверждения получены на этой машине рефлексией и чтением IL установленной сборки
(`H:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64`), дата проверки: 2026-11-21.

Команды, которыми это добыто:

```powershell
& .\tools\dump-se-api.ps1 -Types 'VRage.Game.ModAPI.IMyToolbarProvider'
& .\tools\dump-se-api.ps1 -Types 'VRage.Game.ModAPI.IMyToolbar','VRage.Game.ModAPI.IMyToolbarSlots'
& .\tools\dump-se-api.ps1 -Types 'VRage.Game.ModAPI.MyToolbarItemInfo','VRage.Game.ModAPI.MyToolbarItemKind'
& .\tools\dump-se-il.ps1  -Type 'Sandbox.Game.Screens.Helpers.MyToolbar' -Method 'SendChanges'
& .\tools\dump-se-il.ps1  -Type 'Sandbox.Game.Screens.Helpers.MyToolbar' -Method 'get_IsOwnedHere'
& .\tools\dump-se-il.ps1  -Type 'Sandbox.Game.Screens.Helpers.MyToolbarChangeBuilder' -Method 'Build'
& .\tools\dump-se-il.ps1  -Assembly 'SpaceEngineers.Game.dll' -Type 'SpaceEngineers.Game.Entities.Blocks.MyTimerBlock' -Method 'Init'
& .\tools\dump-se-il.ps1  -Type 'Sandbox.Game.Entities.Blocks.MySensorBlock' -Method 'Init'
& .\tools\check-whitelist.ps1 -Dump
```

Документация сообщества, которой пользовались для сверки (не источник истины):
[KeenSoftwareHouse/SpaceEngineersModAPI](https://github.com/KeenSoftwareHouse/SpaceEngineersModAPI)
и [malforge modapi](https://malforge.github.io/spaceengineers/modapi/index.html).

---

## 0. Главное, что было выяснено позже всего: два разных API и вайтлист

Тулбар в игре описан **дважды**:

| | Движковый слой | ModAPI-слой |
|---|---|---|
| Интерфейс владельца | `Sandbox.Game.Screens.Helpers.IMyToolbarOwner` | `VRage.Game.ModAPI.IMyToolbarProvider` |
| Тулбар | `Sandbox.Game.Screens.Helpers.MyToolbar` | `VRage.Game.ModAPI.IMyToolbar` |
| Элемент | `MyToolbarItem`, `MyToolbarItemInfo` | `VRage.Game.ModAPI.MyToolbarItemInfo` (struct) |
| Запись | `MyToolbar.SetItemAtIndex(...)` | `IMyToolbarSlots.BeginChanges().Apply()` |
| Разрешён моду | **нет** | **да** |

Вайтлист IL-скриптов разрешает пространство имён **точно**. `Sandbox.Game` разрешён,
`Sandbox.Game.Screens.Helpers` — нет, поэтому **всё** в левой колонке для мода запрещено.
Проверка:

```powershell
& .\tools\check-whitelist.ps1 -SelfTest
# WHITELIST SELF-TEST OK — 9 cases, both rules intact
```

Это не теория: первая версия мода использовала `MyToolbar` и `IMyToolbarOwner`, локально
компилировалась, проходила тогдашний шлюз (он был рукописной таблицей и трактовал
разрешение пространства имён как префикс) и **не загружалась** в игре:

```
MOD_ERROR: VGS
  ...\VGS\Game\ToolbarWriter.cs(178,39): Error: The type or member 'MyToolbar' is prohibited
  ...\VGS\Game\ToolbarProbe.cs(62,16): Error: The type or member 'IMyToolbarOwner' is prohibited
```

**Шлюз сверен с игрой на этом же отказе.** Игра перечислила 20 различных запрещённых членов;
новый шлюз на той же сборке даёт ровно тот же набор типов (`MyToolbar`, `IMyToolbarOwner`,
`MyToolbarItem`, `MyToolbarItemActions`, `MyToolbarItemFactory`,
`MyToolbarItemTerminalBlock`, `MyTerminalBlock`) — ни одного лишнего и ни одного
пропущенного. `MyToolbarType` игра **не** отклонила, и шлюз его разрешает: он в `VRage.Game`.
Команда воспроизведения:

```powershell
Select-String -Path "$env:APPDATA\SpaceEngineers\SpaceEngineers_*.log" -Pattern 'is prohibited'
```

Поэтому единственный путь для мода — правая колонка. Дальше в этом документе описан именно
он; движковые имена упоминаются только как объяснение поведения.

## 1. Кто вообще может управлять другими блоками

Блок управляет другими, если у него есть **тулбар, который игра запускает сама**. Это не
таблица подтипов и не флаг `ShowInToolbarConfig` (тот отвечает на обратный вопрос — «можно
ли управлять этим блоком»).

Признак в API: у сущности есть **компонент**, реализующий
`VRage.Game.ModAPI.IMyToolbarProvider` [ПРОВЕРЕНО, `dump-se-api.ps1`]:

```csharp
public interface IMyToolbarProvider
{
    Int32 ToolbarCount { get; }
    IMyToolbar GetToolbar(Int32 toolbarIndex);
}
```

Движковый класс, который это реализует, — `Sandbox.Game.EntityComponents.MyToolbarProviderComponent`
(он же реализует `IMyToolbarOwner` и `IMyToolbarHost` и просто переадресует вызовы своему
источнику). Мод на него **не ссылается**: компонент ищется перебором контейнера по
интерфейсу, поэтому переименование класса в новой версии игры ничего не ломает.

Достаётся он так [ПРОВЕРЕНО]:

```csharp
var entity = block as VRage.Game.Entity.MyEntity;          // блок и есть MyEntity
var components = entity.Components;                        // IMyEntityComponentContainer
foreach (MyComponentBase c in components)
{
    var provider = c as IMyToolbarProvider;                // ищем по интерфейсу
    if (provider != null) return provider;
}
```

Типы `VRage.Game.Entity.MyEntity`, `VRage.Game.Components.MyComponentBase`,
`VRage.Game.Components.Interfaces.IMyEntityComponentContainer` и
`VRage.Game.ModAPI.IMyToolbarProvider` — все в разрешённых пространствах имён.

Компонент добавляют себе [ПРОВЕРЕНО по IL `Init`]: `MyTimerBlock`, `MyEventControllerBlock`,
`MySensorBlock`, `MyShipController`, `MyTargetDummyBlock`. Блок без компонента не может
управлять ничем и остаётся на канвасе только как получатель.

## 2. Форма тулбара

ModAPI-интерфейс (всё, что нужно моду) [ПРОВЕРЕНО, `dump-se-api.ps1`]:

```csharp
public interface IMyToolbarSlots
{
    Int32 SlotCount { get; }     // ячеек на страницу
    Int32 PageCount { get; }     // страниц
    Int32 ItemCount { get; }     // SlotCount * PageCount
    IMyToolbarChangeBuilder BeginChanges();
    void GetItems(List<MyToolbarItemInfo> output);
    Boolean Trigger(Int32 index);
    Boolean TryGetItem(Int32 index, out MyToolbarItemInfo info);
}

public interface IMyToolbar : IMyToolbarSlots
{
    MyToolbarType Type { get; }  // ButtonPanel — тулбар, который блок запускает сам
    IMyToolbarSlots GamepadSlots { get; }
    MyObjectBuilder_Toolbar GetObjectBuilder();
}

public interface IMyToolbarChangeBuilder
{
    IMyToolbarChangeBuilder Set(Int32 index, MyToolbarItemInfo item);
    IMyToolbarChangeBuilder Clear(Int32 index);
    Boolean Apply();
}
```

`MyToolbarType` (`VRage.Game`): `Character`, `SmallCockpit`, `LargeCockpit`, `Ship`, `Seat`,
**`ButtonPanel`** (значение 5), `Spectator`, `None`, `BuildCockpit`.

Только `ButtonPanel` — тулбар, который блок активирует своей логикой. `Ship` /
`LargeCockpit` срабатывают по нажатию клавиши пилотом, `BuildCockpit` — режим строительства.
Запись события в них дала бы кнопку на клавише, а не на событии.

Фактические размеры, прочитанные из `Init` самих блоков [ПРОВЕРЕНО по IL]:

| Блок | Создание тулбара | Ячеек |
|---|---|---|
| `MyTimerBlock` | `new MyToolbar(ButtonPanel, 9, 10)` | 90 |
| `MySensorBlock` | `new MyToolbar(ButtonPanel, 2, 10)` | 20 |
| `MyEventControllerBlock` | `new MyToolbar(ButtonPanel, 2, 10)` | 20 |
| `MyShipController` | три: `(ToolbarType, 9, 9)`, `(BuildCockpit, 9, 9)`, `OnLockedToolbar = (ButtonPanel, 2, 10)` | 81 / 81 / 20 |

Числа из таблицы — **справка для человека**, а не константы мода: `Vgs.Game.ToolbarProbe`
читает `SlotCount`, `PageCount` и `Type` у живого объекта, поэтому таймер, сенсор, кокпит и
любой модовый блок работают без единой строки, знающей о конкретном подтипе.

`ItemCount == SlotCount * PageCount`, адресация линейная: `index = page * SlotCount + slot`
[ПРОВЕРЕНО по IL `get_ItemCount`, `SlotToIndex`, `IndexToSlot`].

## 3. Тулбар «да/нет»

Признак «две группы» складывается из **трёх** условий [ПРОВЕРЕНО по IL
`MyGuiControlToolbar.SetGridItemAt`, `MyToolbar..ctor`, `MyToolbar.Init`,
`MyShipController.Init`]:

| Условие | Откуда | Значение |
|---|---|---|
| `MyToolbar.YesNoBackground` | `MyToolbar.Init` копирует из `MyCubeBlockDefinition.YesNoToolbarBackground` | определение блока само заявляет две группы |
| `MyToolbar.CanShowYesNoBackgrounds` | `= true` в конструкторе; **единственный** писатель — `MyShipController.Init`, ставит `false` своим ручному и строительному тулбарам | «этому тулбару разрешено показывать да/нет» |
| `SlotCount == 2` | конструктор блока | группы — это слоты 0 и 1 страницы |

Через ModAPI видны только `Type` и `SlotCount`; флага `YesNoBackground` в `IMyToolbar` нет.
Поэтому мод берёт его **из определения блока**, а не угадывает:

```csharp
var cubeBlock = block as VRage.Game.ModAPI.IMyCubeBlock;
var definition = cubeBlock.SlimBlock.BlockDefinition as Sandbox.Definitions.MyCubeBlockDefinition;
bool yesNo = definition.YesNoToolbarBackground;
```

Это не обходной путь, а первоисточник: `MyToolbar.Init` копирует ровно это поле. Условие 2
для любого `ButtonPanel`-тулбара истинно — `false` ставится только ручному и строительному
тулбарам кокпита, а они отсеяны проверкой `Type == ButtonPanel`. `Sandbox.Definitions` и
`VRage.Game.ModAPI` разрешены вайтлистом.

Доказательства из IL установленной сборки [ПРОВЕРЕНО]:

* `MyToolbar..ctor`: `CanPlayerActivateItems = true`, `CanShowYesNoBackgrounds = true`,
  `DrawNumbers = true`.
* `MyToolbar.Init`: `YesNoBackground = cubeBlock.BlockDefinition.YesNoToolbarBackground`; если
  флаг истинен, ещё и `SetToolbarYesNoDescription(...)` с подсказками «да»/«нет».
* `MyShipController.Init`: `(ToolbarType, 9, 9)` и `(BuildCockpit, 9, 9)` с
  `CanShowYesNoBackgrounds = false`, `OnLockedToolbar = (ButtonPanel, 2, 10)`.
* `MySensorBlock.OnFirstEnter` → `Toolbar.UpdateAllItemsAtSlot(0)` и
  `ActivateAllItemsAtSlot(0, …)`; `OnLastLeave` → те же вызовы со слотом **1**.
* `MyEventControllerBlock.TriggerAction(Int32 startIndex)` перебирает
  `Toolbar.GetItemAtIndex(i)` с шагом 2 от `startIndex` и требует `Toolbar.ItemCount >= 2`.
  Значит чётные ячейки — одно событие, нечётные — другое.

В ванильных определениях блоков (`Content/Data/CubeBlocks/*.sbc`) флаг
`<YesNoToolbarBackground>true</YesNoToolbarBackground>` объявлен у 52 определений
[ПРОВЕРЕНО чтением файлов]: кокпиты (30), вентиляции (8), сенсоры (4), event controller (2),
турельные контроллеры (2), remote control (2), оборонительные и наступательные боевые блоки
(по 2). У таймера флага нет — у него один список.

## 4. Как записать действие в тулбар

### 4.1. Единственный разрешённый путь

```csharp
IMyToolbar toolbar = provider.GetToolbar(index);          // Type == ButtonPanel
IMyToolbarChangeBuilder change = toolbar.BeginChanges();
change.Set(cellIndex, MyToolbarItemInfo.BlockAction(targetEntityId, actionId));
bool ok = change.Apply();
```

`MyToolbarItemInfo` — структура (`VRage.Game.ModAPI`) с полями `Kind`, `BlockEntityId`,
`GroupName`, `ActionId`, `DefinitionId`, `Label` и фабриками `BlockAction`,
`GroupAction`, `Definition`. `MyToolbarItemKind`: `Empty`, `BlockAction`, `GroupAction`,
`Definition`.

`Apply()` возвращает `bool` и **не бросает**. Внутри: `MyToolbarChangeBuilder.Apply` →
`MyToolbar.SendChanges`, который отклоняет всю пачку, если `m_toolbarOwner == null` или
`!IsOwnedHere`; `IsOwnedHere` = `ToolbarType == Character || Sync.IsServer`
[ПРОВЕРЕНО по IL]. Для тулбара блока это значит: **запись работает в одиночной игре и на
хосте, а на удалённом клиенте мультиплеера отклоняется**, потому что тулбар блока принадлежит
серверу. `Apply()` честно возвращает `false`, и `Vgs.Game.ToolbarWriter` превращает это в
`WriteResult.Rejected` с сообщением пользователю, а не в мнимую связь.

Чтение (`TryGetItem`, `GetItems`, `SlotCount`, `PageCount`, `Type`) работает везде, поэтому
клиент видит связи, записанные сервером.

**Куда идёт запись.** `SendChanges` не правит тулбар локально: он передаёт пачку в
`IMyToolbarHost.SendToolbarBatch`, а `MyToolbarProviderComponent.SendToolbarBatch` поднимает
сетевое событие `ReceiveToolbarBatch` с атрибутами `[Event][Reliable][ServerInvoked][Broadcast]`,
которое применяют **все клиенты**, включая инициатора. То есть `Apply()` — это серверная
рассылка, а не правка блока; поэтому её и может инициировать только сервер. У игры есть второй,
клиентский путь (`SetItemAtIndex` → `ItemChanged` → `SendToolbarItemChanged`), но он лежит в
запрещённых пространствах имён. Разбор и следствия для мультиплеера —
[multiplayer.md](multiplayer.md).

Элемент валидируется на стороне игры: `MyToolbarChangeBuilder.Build` для
`Kind == BlockAction` создаёт `MyObjectBuilder_ToolbarItemTerminalBlock`, кладёт
`BlockEntityId` и через `Fill` — `_Action`; если `ActionId` пуст или действие не найдено,
`Build` возвращает `null`, `Set` помечает пачку `m_rejected`, и `Apply()` вернёт `false`.
Поэтому мод дополнительно проверяет `target.GetActionWithName(actionId) != null` до записи и
**перечитывает ячейку** после неё.

### 4.2. Запрещённый путь (для истории)

`MyToolbar.SetItemAtIndex(int, MyToolbarItem, bool, bool)` — то, что вызывает ванильный экран;
он ставит элемент локально и поднимает `ItemChanged`, который блок пересылает своей
синхронизацией. Работал бы и на клиенте, но `MyToolbar` и `MyToolbarItemFactory` лежат в
`Sandbox.Game.Screens.Helpers`, который моду запрещён. Именно на нём мод однажды не загрузился.

### 4.3. Что делает элемент, когда срабатывает, и как это повторить вручную

Вопрос из [ADR-0041](../adr/0041-manual-action-menu.md): редактор умеет запускать действие
**сейчас**, без записи в тулбар, — и для этого нужно повторить ровно то, что делает игра,
когда срабатывает записанный элемент, а не придумать своё правило.

[ПРОВЕРЕНО по IL] `MyToolbarItemTerminalBlock.Activate()` целиком:

```il
action = GetCurrentAction();                       // MyToolbarItemActions.GetActionOrNull(ActionId)
if (m_block != null && action != null)
    action.Apply(m_block, m_parameters);           // Sandbox.Game.Gui.ITerminalAction::Apply
return m_block != null && action != null;
```

```powershell
& .\tools\dump-se-il.ps1 -Type 'Sandbox.Game.Screens.Helpers.MyToolbarItemTerminalBlock' -Method 'Activate'
& .\tools\dump-se-il.ps1 -Type 'Sandbox.Game.Screens.Helpers.MyToolbarItemActions' -Method 'GetActionOrNull'
```

`GetActionOrNull(String id)` обходит `AllActions` и сравнивает `Id` строкой — то есть действие
выбирается **по id из списка, который игра собрала для этого блока**, а не по чему-то ещё.

Три следствия, которые нельзя вывести «по аналогии»:

1. **`IsEnabled` в этом пути не проверяется вообще.** Значит «кнопка серая» — свойство
   *терминальной панели*, а не исполнения: игра применит действие, даже если контрол сейчас
   выключен (для этого блока), и это её ответ. Мод, отказавший по `IsEnabled`, разошёлся бы с
   игрой и с собственным меню привязки, которое такие строки показывает (ADR-0038, ADR-0039).
2. **Одиночный блок не проверяется на `IsFunctional`.** Проверка есть только у группового
   элемента (пункт 4), то есть у блока, чьё состояние игра считает по-другому.
3. **`IMyTerminalBlock.GetActionWithName(id)` — это поиск по типу блока, а не по блоку:**
   `MyTerminalBlock.GetActionWithName` → `MyTerminalControlFactoryHelper.Static.GetActionWithName(name, block.GetType())`.
   Поэтому «действие исчезло» здесь означает «его больше нет у типа», а не «оно выключено».
   Это тот же факт, что ADR-0038 записал про кэш по типу.

[ПРОВЕРЕНО по IL] `MyToolbarItemTerminalGroup.Activate()` — групповой путь:

```il
blocks  = GetBlocks();                                  // по имени группы через грид контекстного блока
actions = GetActionsWithGenericDuplicates(blocks);
found   = FindActions(ActionId);                        // по Id, пусто → return false
if (found.Count == 0) return false;
foreach (block in blocks)
    if (block != null && block.IsFunctional)
    {
        a = GetMatchingGenericAction(found, block);      // по предку типа
        if (a != null) a.Apply(block, m_parameters);
    }
return true;                                            // ← всегда, даже если не применилось ни к кому
```

```powershell
& .\tools\dump-se-il.ps1 -Type 'Sandbox.Game.Screens.Helpers.MyToolbarItemTerminalGroup' -Method 'Activate'
```

4. **У группы два пропуска — нефункциональный блок и блок без подходящего действия**, — и
   возвращает она `true` независимо от того, применилось ли что-нибудь. Мод, повторяющий
   групповое действие вручную, обязан считать применённые блоки сам: иначе «сработало» будет
   означать «игра не упала».

Тип `MyToolbarItemTerminalBlock`/`MyToolbarItemTerminalGroup` моду запрещён (namespace
`Sandbox.Game.Screens.Helpers`) — дампился только как факт о поведении. Разрешённый путь,
которым мод делает то же самое, — `IMyTerminalBlock.GetActionWithName` +
`Sandbox.ModAPI.Interfaces.ITerminalAction.Apply(IMyCubeBlock)`; обе сигнатуры проверены
`dump-se-api.ps1 -Types 'Sandbox.ModAPI.Ingame.IMyTerminalBlock','Sandbox.ModAPI.Interfaces.ITerminalAction'`.

## 5. Ограничение «одно действие — один раз на странице»

Ванильный экран, когда игрок бросает действие в ячейку, сначала **удаляет** все остальные
ячейки **текущей страницы** с таким же элементом
(`MyGuiScreenToolbarConfigBase.DropGridItemToToolbar` → лямбда: цикл по `SlotCount`,
`GetSlotItem(i).Equals(item)` → `SetItemAtSlot(i, null)`) [ПРОВЕРЕНО по IL].

Следствие, которое и есть правило проекта: одинаковое действие на одном и том же блоке
может стоять только **по одному на странице**. Дубликат на другой странице игра разрешает и
запускает отдельно: `MyTimerBlock.UpdateOnceBeforeFrame` обходит **весь** тулбар
(`for i in 0..ItemCount`) и активирует каждую непустую ячейку, а
`MyToolbar.ActivateAllItemsAtSlot` обходит все страницы и активирует одну и ту же ячейку на
каждой. Поэтому повтор связи нужно класть на следующую страницу, а когда страницы
кончились — сообщать пользователю, что связь невозможна.

Ёмкость: таймер — 10 страниц × 9 ячеек, значит одно и то же действие на одном блоке можно
повесить не более 10 раз; одиннадцатый раз — отказ, а не тихая перезапись.

## 6. Что из этого следует для мода

1. «Может ли блок управлять другими» = компонент `IMyToolbarProvider` + тулбар с
   `Type == ButtonPanel`. Проверяется у живого блока, а не таблицей подтипов.
2. Связь — это элемент в реальном тулбаре исходного блока. Редактор пишет его через
   `BeginChanges().Set(...).Apply()` и хранит номер ячейки.
3. Группа срабатывания («да»/«нет») — свойство ячейки (слот 0 или 1), поэтому порт события
   у блока с двумя группами раздваивается. Признак берётся из определения блока.
4. Отказ игры (нет свободной ячейки нужного слота, действие уже на всех страницах, запись на
   клиенте мультиплеера) — штатная ситуация, о которой надо сказать словами, а не молча
   ничего не делать.
5. **Вайтлист — часть контракта.** Тип, которого нет в `check-whitelist.ps1`, не существует,
   даже если он есть в сборке.
6. **Цель элемента может быть его же источником.** `MyToolbarItemTerminalBlock.Activate`
   применяет текущее действие к `m_block`, не сравнивая его с владельцем тулбара, а `Update`
   сверяет с владельцем **грид** (`HasSameGroup`), а не сам блок; `MyEventControllerBlock.
   TriggerAction` берёт блок из `BlockEntityId`, проверяет тот же логический грид и права
   (`GetUserRelationToOwner` / `ShareMode`), но цель с владельцем не сравнивает.
   Проверено по IL:

   ```powershell
   & .\tools\dump-se-il.ps1 -Assembly 'Sandbox.Game.dll' `
       -Type 'Sandbox.Game.Screens.Helpers.MyToolbarItemTerminalBlock' -Method 'Activate'
   & .\tools\dump-se-il.ps1 -Assembly 'Sandbox.Game.dll' `
       -Type 'Sandbox.Game.Screens.Helpers.MyToolbarItemTerminalBlock' -Method 'Update'
   & .\tools\dump-se-il.ps1 -Assembly 'SpaceEngineers.Game.dll' `
       -Type 'SpaceEngineers.Game.Entities.Blocks.MyEventControllerBlock' -Method 'TriggerAction'
   ```

   Две команды для одного типа, потому что `-Method` — фильтр по одному имени (`-like`):
   `'Activate,Update'` он не понимает и падает с «No methods matching».

   Сам тип `MyToolbarItemTerminalBlock` моду запрещён (namespace
   `Sandbox.Game.Screens.Helpers`), дампился только как факт о поведении. Отсюда «блок может
   управлять сам собой» ([ADR-0036](../adr/0036-self-connection.md)).
