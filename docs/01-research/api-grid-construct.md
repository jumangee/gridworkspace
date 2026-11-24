# Конструкт: логическая группа гридов и её терминальная система

Дата проверки: 2026-11-24. Игра: `H:\SteamLibrary\steamapps\common\SpaceEngineers`
(Steam, `Bin64`). Все команды — из корня репозитория.

Вопрос, на который отвечает документ: **что игра считает «одним кораблём»**, и каким набором
блоков это выражается через API. Это нужно для [ADR-0032](../adr/0032-construct-graph.md):
схема редактора должна показывать подгриды, и надо решить, по какой границе их собирать.

---

## 1. Терминальная система грида — это система конструкта

```powershell
& .\tools\dump-se-api.ps1 -Types 'Sandbox.ModAPI.IMyTerminalActionsHelper'
& .\tools\dump-se-il.ps1 -Type 'Sandbox.ModAPI.MyTerminalControlFactoryHelper' `
    -Method 'Sandbox.ModAPI.IMyTerminalActionsHelper.GetTerminalSystemForGrid'
```

Контракт:

```csharp
IMyGridTerminalSystem GetTerminalSystemForGrid(IMyCubeGrid grid);
```

IL:

```
MyGroups<MyCubeGrid, MyGridLogicalGroupData>.GetGroup(grid) → group.GroupData.TerminalSystem
```

**Следствие.** `GetTerminalSystemForGrid(grid)` возвращает терминальную систему **логической
группы** (конструкта: грид и его подгриды), а не одного грида. Это же записано в
[`api-block-groups.md`](api-block-groups.md) §1 и используется в
`GridGraphBuilder.ReadGroups`.

## 2. `IMyGridGroupData` — интерфейс группы, `GetGrids` наполняет переданную коллекцию

```powershell
& .\tools\dump-se-api.ps1 -Types 'VRage.Game.ModAPI.IMyGridGroupData'
```

```csharp
public interface IMyGridGroupData
{
    GridLinkTypeEnum LinkType { get; }
    T GetGrids<T>(T grids);                  // generic: T — коллекция, которую наполнят
    T GetVariable<T>(Guid key);
    Boolean TryGetVariable<T>(Guid key, out T variable);
    Void SetVariable(Guid key, Object data);
    Boolean RemoveVariable(Guid key);
    event Action<IMyGridGroupData, IMyCubeGrid, IMyGridGroupData> OnGridAdded;
    event Action<IMyGridGroupData, IMyCubeGrid, IMyGridGroupData> OnGridRemoved;
    event Action<IMyGridGroupData> OnReleased;
}
```

`GetGrids<T>` возвращает `T`, а не `List<IMyCubeGrid>`: сигнатура обобщённая, и настоящий тип
надо смотреть в реализации. Реализация — `Sandbox.Game.Entities.Cube.MyGridGroupData\`1`:

```powershell
& .\tools\dump-se-il.ps1 -Type 'Sandbox.Game.Entities.Cube.MyGridGroupData`1' -Method 'GetGrids'
```

```
ldarg.1 ... constrained. ICollection`1[IMyCubeGrid]::Add     // в цикле по гридам группы
...
ldarg.1
ret                                                          // возвращает ПЕРЕДАННЫЙ объект
```

**Следствие.** `GetGrids(list)` наполняет `list` и возвращает его же. Вызывать надо с уже
созданной коллекцией, например `List<IMyCubeGrid>`; `T` выводится из аргумента.

## 3. `GridLinkTypeEnum` — какие связи игра различает

```powershell
& .\tools\dump-se-api.ps1 -Types 'VRage.Game.ModAPI.GridLinkTypeEnum'
```

Значения: `Logical`, `Physical`, `NoContactDamage`, `Mechanical`, `Electrical`.

| Значение | Что означает | Годится для «одного корабля»? |
|---|---|---|
| `Logical` | конструкт: грид и его подгриды, общий терминал | **да** |
| `Physical` | физически связанные гриды, включая состыкованные коннекторами корабли | нет: терминалы разные |
| `Mechanical` | сочленение (ротор, поршень, колесо) | нет: это отношение между гридами, а не набор |
| `Electrical` | общая энергосеть | нет |
| `NoContactDamage` | гриды, не наносящие друг другу урон контактом | нет |

## 4. `MyGridLogicalGroupData.Root` — главный грид конструкта

```powershell
& .\tools\dump-se-api.ps1 -Types 'Sandbox.Game.Entities.MyGridLogicalGroupData'
```

```csharp
public class MyGridLogicalGroupData : MyGridGroupData<MyGridLogicalGroupData>
{
    public MyCubeGrid Root { get; set; }
}
```

**Следствие.** У конструкта есть главный грид. Это полезно для подписи рамки и для ответа на
вопрос «какой грид открыл редактор», но **не** для определения домена: домен — все блоки
терминальной системы, а `Root` — один грид из них.

## 5. Резолв цели элемента тулбара — глобальный, без проверки конструкта

```powershell
& .\tools\dump-se-il.ps1 -Type 'Sandbox.Game.Screens.Helpers.MyToolbarItemTerminalBlock' `
    -Method 'TryGetBlock'
```

```
ldarg.0 / ldfld m_blockEntityId
ldarg.0 / ldflda m_block
ldc.i4.0
call MyEntities::TryGetEntityById            // глобальный поиск по id
dup / brfalse.s
ldarg.0 / call RegisterEvents
ret
```

**Следствие.** Элемент тулбара, ссылающийся на блок, резолвится по **глобальному** id и
**не проверяет**, принадлежит ли цель тому же конструкту. Проверки конструкта здесь нет вовсе.

Практически это значит: элемент, указывающий на блок другого корабля, продолжает работать,
если тот блок загружен. Планировщик тулбара игры предлагает только блоки конструкта, поэтому
обычным путём такой элемент не создать — но скрипт или перенос тулбара между блоками это
делает.

Для редактора следствие важное: связь может указывать **вне** домена графа, и это не ошибка
данных. Текущий код обрабатывает это правильно — `GridGraphBuilder.SyncCells` вызывает
`graph.TryConnect`, который проверяет существование целевого узла и молча пропускает элемент.

## 6. Состояние блока уже межгридовое

`Game/NodeStateStore.cs`: позиция карточки и «Show in VGS» лежат в компоненте **блока**
(`MyModStorageComponent`), а не в таблице на гриде. Ключа нет, поэтому переезд блока на другой
грид (сборка/разборка, разделение грида) ничего не ломает, и миграции для ADR-0032 не нужно.

## Итог для реализации

| Вопрос | Ответ |
|---|---|
| Чем перечислять блоки конструкта | `MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid).GetBlocks(list)` — **один** вызов, тот же, что уже даёт группы |
| Чем перечислять гриды конструкта (для рамок) | `grid.GetGridGroup(GridLinkTypeEnum.Logical)` → `GetGrids(new List<IMyCubeGrid>())` |
| Какой `GridLinkTypeEnum` | только `Logical` |
| Грид блока | `IMyCubeBlock.CubeGrid` |
| Главный грид конструкта | `MyGridLogicalGroupData.Root` (для подписи, не для домена) |
| Может ли связь вести вне домена | да; обрабатывается существующей проверкой `TryConnect` |
