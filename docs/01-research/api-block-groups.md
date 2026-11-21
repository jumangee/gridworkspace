# Блок-группы: как игра их хранит и как исполняет действие «на группу»

Дата проверки: 2026-11-21. Игра: `H:\SteamLibrary\steamapps\common\SpaceEngineers`
(Steam, `Bin64`). Все команды — из корня репозитория.

Вопрос, на который отвечает документ: **что именно игра делает, когда действие
назначается не блоку, а группе блоков**, и какими средствами мод может это повторить.
Это нужно для [ADR-0015](../adr/0015-block-groups.md): редактор показывает группы на схеме
и должен предлагать у группы ровно те действия, которые игра сумеет исполнить.

---

## 1. Группа — это именованный набор терминальных блоков одного конструкта

```powershell
& .\tools\dump-se-api.ps1 -Types 'Sandbox.ModAPI.IMyBlockGroup','Sandbox.ModAPI.Ingame.IMyBlockGroup',
    'Sandbox.ModAPI.IMyGridTerminalSystem','Sandbox.ModAPI.Ingame.IMyGridTerminalSystem'
```

`Sandbox.ModAPI.IMyBlockGroup` (сборка `Sandbox.Common`, разрешена моду):

```csharp
public interface IMyBlockGroup : IMyBlockGroup /* Ingame */
{
    void GetBlocks(List<IMyTerminalBlock> blocks, Func<IMyTerminalBlock, bool> collect = null);
    void GetBlocksOfType<T>(List<IMyTerminalBlock> blocks, Func<IMyTerminalBlock, bool> collect = null);
}
// Ingame-часть: string Name { get; }
```

`Sandbox.ModAPI.IMyGridTerminalSystem` (там же):

```csharp
void GetBlockGroups(List<IMyBlockGroup> blockGroups);
IMyBlockGroup GetBlockGroupWithName(string name);
event Action<IMyBlockGroup> GroupAdded;
event Action<IMyBlockGroup> GroupRemoved;
```

Терминальная система для грида берётся через `MyAPIGateway.TerminalActionsHelper`:

```powershell
& .\tools\dump-se-api.ps1 -Types 'Sandbox.ModAPI.IMyTerminalActionsHelper'
```

```csharp
IMyGridTerminalSystem GetTerminalSystemForGrid(IMyCubeGrid grid);
```

**Важная деталь.** [ПРОВЕРЕНО] `dump-se-il.ps1 -Type 'Sandbox.ModAPI.MyTerminalControlFactoryHelper'
-Method 'Sandbox.ModAPI.IMyTerminalActionsHelper.GetTerminalSystemForGrid'`:

```
MyGroups<MyCubeGrid, MyGridLogicalGroupData>.GetGroup(grid) → group.GroupData.TerminalSystem
```

То есть это терминальная система **логической группы гридов** (конструкта: грид и его
подгриды), а не одного грида. Следствие: в группу могут входить блоки с подгридов, которых
на схеме одного грида нет. Правило игры при этом применяет действие ко **всем** блокам
группы, поэтому набор действий группы обязан считаться по полному составу, а не по
нарисованному.

`GetBlocks` отдаёт блоки без фильтрации (не смотрит ни на `ShowInTerminal`, ни на доступ):

[ПРОВЕРЕНО] `dump-se-il.ps1 -Type 'Sandbox.Game.GameSystems.MyBlockGroup'
-Method 'Sandbox.ModAPI.IMyBlockGroup.GetBlocks'` — обход `HashSet<MyTerminalBlock> Blocks`
без проверок; `Name` — `StringBuilder.ToString()`.

Сам тип `Sandbox.Game.GameSystems.MyBlockGroup` моду **недоступен**: `Sandbox.Game.GameSystems` —
подпространство `Sandbox.Game`, а вайтлист разрешает `Sandbox.Game` точно. Работать можно
только через `Sandbox.ModAPI.IMyBlockGroup`.

## 2. Действие «на группу» — отдельный вид элемента тулбара

[ПРОВЕРЕНО] `dump-se-api.ps1 -Types 'VRage.Game.ModAPI.MyToolbarItemInfo','VRage.Game.ModAPI.MyToolbarItemKind'`:

```csharp
public enum MyToolbarItemKind { Empty, BlockAction, GroupAction, Definition }

public static MyToolbarItemInfo GroupAction(long blockEntityId, string groupName,
                                            string actionId, string label = null);
```

`blockEntityId` — это **не** «блок группы», а «любой блок, через который найти грид»:
[ПРОВЕРЕНО] `dump-se-il.ps1 -Type 'Sandbox.Game.Screens.Helpers.MyToolbarItemTerminalGroup'
-Method 'GetBlocks'`:

```
MyEntities.TryGetEntityById(BlockEntityId) → block.CubeGrid.GridSystems.TerminalSystem
    → .BlockGroups → поиск по Name == m_groupName → блоки группы
```

Если блок по id не найден или группа с таким именем не найдена — пустой список, элемент
ничего не делает. Значит `blockEntityId` должен быть живым блоком того же конструкта.

[ПРОВЕРЕНО] `dump-se-il.ps1 -Type 'Sandbox.ModAPI.MyToolbarApiExtensions' -Method 'GroupAction'` —
ModAPI сам передаёт туда `contextBlock.EntityId`:

```csharp
MyToolbarApiExtensions.GroupAction(builder, index, IMyTerminalBlock contextBlock,
                                   string groupName, string actionId,
                                   List<TerminalActionParameter> parameters = null, string label = null)
// → MyToolbarItemInfo.GroupAction(contextBlock.EntityId, groupName, actionId, label)
```

Это официальный путь записи; он сводится к `builder.Set(index, MyToolbarItemInfo.GroupAction(...))`,
поэтому мод может обойтись без самого расширения (и без нового пространства имён).

Чтение обратно: [ПРОВЕРЕНО] `dump-se-il.ps1 -Type 'Sandbox.Game.Screens.Helpers.MyToolbarItemTerminalGroup'
-Method 'GetInfo'` — элемент отдаёт `MyToolbarItemInfo.GroupAction(BlockEntityId, GroupName,
ActionId, CustomIconTitle)`, то есть `TryGetItem` возвращает `Kind == GroupAction`,
`GroupName`, `ActionId`, `BlockEntityId`.

## 3. Какие действия игра считает доступными для группы

[ПРОВЕРЕНО] `dump-se-il.ps1 -Type 'Sandbox.Game.Screens.Helpers.MyToolbarItemTerminalGroup'
-Method 'GetActions'` и `-Method 'GetValidActions'`, `-Method '<GetValidActions>g__CollectValidActions|13_0'`.

Правило целиком, восстановленное по IL:

```
типы  = множество GetType() всех блоков группы
всеФункциональные = у каждого блока (block as MyFunctionalBlock) != null

если типов ровно один:
    действия = GetValidActions(этот тип, ВСЕ блоки группы)
иначе если типов ноль или не всеФункциональные:
    действия = ПУСТО
иначе:
    действия = пересечение по Id: GetValidActions(тип каждого блока, ТОЛЬКО ЭТОТ блок),
               в порядке первого блока

GetValidActions(тип, блоки):
    действия = MyTerminalControlFactory.GetActions(тип)   // действия типа, а не блока
    + действия компонентов-провайдеров (по Id, без дублей)
    оставить те, у которых IsValidForGroups() == true
                  и IsEnabled(блок) == true хотя бы у одного блока ИЗ ПЕРЕДАННОГО СПИСКА
```

Обратите внимание на асимметрию, которую легко потерять при пересказе: в однотипной ветви
передаются **все** блоки группы, поэтому действие выживает, если оно включено **хотя бы у
одного**; в смешанной ветви каждый вызов получает **ровно один** блок, поэтому пересечение
требует, чтобы действие было включено у **каждого**. Это не описка: так написано в
`GetActions`, и это влияет на список, который видит игрок.

Три следствия, важных для редактора:

1. **Никакого «общего предка» игра не ищет.** Гипотеза «доступные действия определяются
   самым общим предком» **неверна**. Игра берёт действия **типа** блока (а не базового
   класса) и, если типы разные, оставляет **пересечение по id**. Класс-предок появляется
   только в другом месте — при активации (`GetMatchingGenericAction`), где действие
   применяется к блоку, если тип, на который действие зарегистрировано, является
   предком этого блока.
2. **Смешанная группа с не-функциональным блоком не имеет действий вообще.** Один конвейер
   в группе с таймером и сенсором — и группа перестаёт предлагать что-либо в ванильном
   тулбаре. Это проверка `isinst MyFunctionalBlock` по типу, а не по состоянию.
3. **Фильтр `IsEnabled` — «хотя бы у одного» в однотипной ветви и «у каждого» в смешанной**,
   потому что в первой передаются все блоки, а во второй — по одному. Список действий группы
   не схлопывается от того, что один блок выключен, если все блоки одного типа; и схлопывается,
   если типы разные.

### 3.1. `IsValidForGroups` моду недоступен, и это проверено

`IsValidForGroups()` — член **внутреннего** интерфейса `Sandbox.Game.Gui.ITerminalAction`
(подпространство `Sandbox.Game.Gui`, вайтлист его не разрешает). ModAPI-зеркало
`Sandbox.ModAPI.Interfaces.ITerminalAction` содержит только `Id`, `Icon`, `Name`, `Apply`,
`IsEnabled`, `WriteValue`.

[ПРОВЕРЕНО] значение по умолчанию — `true`: `dump-se-il.ps1 -Type 'Sandbox.Game.Gui.MyTerminalAction`1'
-Method '.ctor'` содержит `ldc.i4.1; stfld ValidForGroups` в каждом конструкторе.

[ПРОВЕРЕНО] **ни один метод игры не выставляет его в `false`**: скан IL всех методов
`Sandbox.Game.dll` и `SpaceEngineers.Game.dll` на вызовы `set_ValidForGroups` даёт 0
совпадений (контрольный прогон того же сканера по `set_Visible` даёт 312 совпадений, то
есть сканер работает). Значит для ванильных блоков и блоков DLC фильтр не отсекает ничего,
и его отсутствие в моде не меняет список действий. Отсекать может только действие,
добавленное другим модом.

## 4. Что происходит при срабатывании

[ПРОВЕРЕНО] `dump-se-il.ps1 -Type 'Sandbox.Game.Screens.Helpers.MyToolbarItemTerminalGroup'
-Method 'Activate'`:

```
блоки = GetBlocks()
действия = GetActionsWithGenericDuplicates(блоки)      // то же правило, что в §3
найденные = действия с Id == ActionId
если пусто → false (элемент ничего не делает)
для каждого блока группы:
    если block.IsFunctional:
        action = GetMatchingGenericAction(найденные, block)   // по предку типа
        если action != null → action.Apply(block, parameters)
```

То есть действие применяется к каждому **функциональному** блоку группы, у которого есть
подходящее действие. Одна связь «таймер → группа, OnOff» включает все члены группы.

`AllowedInToolbarType` у группового элемента: `type != null && type != MyToolbarType.Spectator`
(значение `Spectator` = 6) — то есть писать его можно в любой тулбар, включая `ButtonPanel`
таймера. [ПРОВЕРЕНО] тем же дампом.

## 5. Итог для мода

| Вопрос | Ответ | Чем проверено |
|---|---|---|
| Как перечислить группы? | `MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid).GetBlockGroups(list)` | `dump-se-api.ps1`, `dump-se-il.ps1` |
| Группа — сущность? | Нет. Идентификатор — имя (строка), стабильного id нет | `dump-se-api.ps1` |
| Как записать связь «блок → группа»? | `MyToolbarItemInfo.GroupAction(contextBlock.EntityId, groupName, actionId)` через `IMyToolbarChangeBuilder.Set` | `dump-se-il.ps1` (GroupAction, Build) |
| Как прочитать обратно? | `TryGetItem` → `Kind == GroupAction`, `GroupName`, `ActionId` | `dump-se-il.ps1` (GetInfo) |
| Какие действия у группы? | Пересечение по id действий типов её блоков; пусто, если типы разные и хоть один блок не функциональный | `dump-se-il.ps1` (GetActions, GetValidActions) |
| Есть ли у группы события? | Нет: групповой элемент не запускает тулбар, он только применяет действие | `dump-se-il.ps1` (Activate) |
| Что с `IsValidForGroups`? | Моду недоступен; по умолчанию `true`; игра его нигде не выключает | `dump-se-il.ps1`, скан IL |

Непроверенное (требует игры, помечено в ADR-0015 §Verification): что записанный модом
`GroupAction` действительно срабатывает в игре; что группа с блоками подгридов ведёт себя
так, как описано в §3 (состав группы через `GetBlocks` проверен по IL, но не запуском).
