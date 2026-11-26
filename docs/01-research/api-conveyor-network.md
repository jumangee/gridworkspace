# Конвейерная сеть: как её читать из мода

Дата проверки: 2026-11-25. Игра: `H:\SteamLibrary\steamapps\common\SpaceEngineers`
(Steam, `Bin64`). Все команды — из корня репозитория.

Вопрос: **как моду получить конвейерную сеть грида**, не ссылаясь на типы, которые игра
отвергает, и что именно означает каждая линия.

---

## 1. Реализация конвейеров закрыта для мода

```powershell
& .\tools\find-se-type.ps1 -Name 'IMyGridConveyorSystem'
& .\tools\find-se-type.ps1 -Name 'MyConveyor*'
& .\tools\find-se-type.ps1 -Name 'IMyConveyor*'
```

| Тип | Сборка | Пространство имён | В вайтлисте? |
|---|---|---|---|
| `IMyGridConveyorSystem` | `VRage.Game` | `VRage.ModAPI` | **да** — но только `PullItem`/`PushGenerateItem` |
| `MyGridConveyorSystem` | `Sandbox.Game` | `Sandbox.Game.GameSystems` | нет секции |
| `MyConveyorLine` | `Sandbox.Game` | `Sandbox.Game.GameSystems.Conveyors` | **нет** |
| `IMyConveyorEndpoint` | `Sandbox.Game` | `Sandbox.Game.GameSystems.Conveyors` | **нет** |
| `ConveyorLinePosition` | `Sandbox.Game` | `Sandbox.Game.GameSystems.Conveyors` | **нет** |
| `IMyConveyor`, `IMyConveyorTube`, `IMyConveyorSorter` | `Sandbox.Common` | `Sandbox.ModAPI` | да — но это **маркеры без данных** |

```powershell
& .\tools\dump-se-api.ps1 -Types 'VRage.ModAPI.IMyGridConveyorSystem','Sandbox.ModAPI.IMyConveyorTube'
```

`IMyGridConveyorSystem` — интерфейс из двух методов (`PullItem`, `PushGenerateItem`): это
**перемещение предметов**, а не описание сети. `IMyConveyorTube` не объявляет ни одного
собственного члена — это маркер, которым можно только спросить «это труба?».

Вайтлист разрешает пространство имён **точно** (ADR-0008): `Sandbox.Game` разрешён,
`Sandbox.Game.GameSystems.Conveyors` — нет. Значит `MyConveyorLine` и `IMyConveyorEndpoint`
моду недоступны: ссылка на них компилируется локально и отвергается игрой при загрузке.

**Следствие.** Обойти граф конвейеров через `ConveyorSystem.ConveyorEndpointBlocks` нельзя —
это `HashSetReader<IMyConveyorEndpointBlock>`, тип которого живёт в закрытом пространстве имён.

## 2. Линии есть в объект-билдере грида

```powershell
& .\tools\dump-se-api.ps1 -Types 'VRage.Game.MyObjectBuilder_CubeGrid','VRage.Game.MyObjectBuilder_ConveyorLine'
```

```csharp
// MyObjectBuilder_CubeGrid
public List<MyObjectBuilder_ConveyorLine> ConveyorLines;

// MyObjectBuilder_ConveyorLine
public SerializableVector3I StartPosition;
public Direction StartDirection;
public SerializableVector3I EndPosition;
public Direction EndDirection;
public List<MyObjectBuilder_ConveyorPacket> PacketsForward;
public List<MyObjectBuilder_ConveyorPacket> PacketsBackward;
public List<SerializableLineSectionInformation> Sections;
public LineType ConveyorLineType;
public LineConductivity ConveyorLineConductivity;
```

Заполняет их игра при сериализации грида:

```powershell
& .\tools\dump-se-il.ps1 -Type 'Sandbox.Game.Entities.Cube.MyCubeGridSystems' -Method 'GetObjectBuilder'
```

```il
0023: ldarg.0
0024: call   Sandbox.Game.Entities.Cube.MyCubeGridSystems::get_ConveyorSystem
0029: ldarg.1
002A: ldfld  ldfld ConveyorLines
002F: callvirt  MyGridConveyorSystem::SerializeLines
```

А `MyObjectBuilder_CubeGrid` приходит из разрешённого `IMyEntity.GetObjectBuilder()`:

```powershell
& .\tools\dump-se-il.ps1 -Type 'Sandbox.Game.Entities.MyCubeGrid' -Method 'GetObjectBuilder'
```

```il
0002: call  VRage.Game.Entity.MyEntity::GetObjectBuilder
0007: castclass VRage.Game.MyObjectBuilder_CubeGrid
0010: call  Sandbox.Game.Entities.MyCubeGrid::GetObjectBuilderInternal
```

`GetObjectBuilder` объявлен в `VRage.ModAPI.IMyEntity` — типе из разрешённого пространства
имён. Цепочка целиком легальна: `IMyCubeGrid` → `IMyEntity.GetObjectBuilder()` →
`MyObjectBuilder_CubeGrid` (пространство `VRage.Game`, разрешено) → `ConveyorLines`.

**Цена.** `GetObjectBuilderInternal` сериализует **каждый** блок грида
(`m_cubeBlocks` → `GetObjectBuilder`), а не только линии. Поэтому чтение — проход
структурного обновления, не кадровый путь, и только для гридов, у которых есть хотя бы две
нарисованные карточки.

## 3. Что означает конец линии

```powershell
& .\tools\dump-se-il.ps1 -Type 'Sandbox.Game.GameSystems.Conveyors.MyConveyorLine' -Method 'GetObjectBuilder'
```

```il
00C9: ldarg.0 / ldflda m_endpointPosition1
00D0: ldfld  LocalGridPosition
00D5: call   SerializableVector3I::op_Implicit
00DA: stfld  StartPosition
00DF: ...     m_endpointPosition1 / Direction
00F0: ldarg.0 / ldflda m_endpointPosition2
00FC: ldfld  LocalGridPosition
0101: stfld  EndPosition
0106: ...     Direction
```

`LocalGridPosition` — это `Vector3I` **самого блока**, а не клетки перед ним. Проверено по
`GetBlockLinePositions`:

```powershell
& .\tools\dump-se-il.ps1 -Type 'Sandbox.Game.GameSystems.Conveyors.MyConveyorLine' -Method 'GetBlockLinePositions'
```

```il
022E: ... ldflda 13 (ConveyorLinePosition)
02B7: ldloc.s 14                  ; клетка порта, посчитанная из dummy модели
02BA: callvirt MyCubeBlockDefinition::get_Center
02BF: call   Vector3I::op_Subtraction
02C4: stfld  LocalGridPosition
```

Из клетки порта вычитается `Center` определения блока — то есть результат снова попадает в
клетку блока. Для многоблочного блока (`Size` > 1×1×1) это одна из его клеток, поэтому
`grid.GetCubeBlock(position)` находит блок и для него.

**Следствие.** `IMyCubeGrid.GetCubeBlock(Vector3I)` (разрешён, `VRage.Game.ModAPI`) по каждому
концу линии даёт `IMySlimBlock`, а через `.FatBlock.EntityId` — id блока. `Direction` не нужен:
клетка уже однозначно определяет блок.

## 4. Линия сквозь трубы, а не труба за трубой

```powershell
& .\tools\dump-se-il.ps1 -Type 'Sandbox.Game.GameSystems.Conveyors.MyConveyorLine' -Method 'Init'
```

```il
0100: ldarg.0 / ldarg.1 / ldfld Sections / callvirt List`1::get_Count
010C: call   MyConveyorLine::InitializeSectionList
...
0187: ldarg.0 / ldfld m_length / brtrue.s
0190: ldarg.0 / ... RectangularDistance(endpoint2.LocalGridPosition, endpoint1.LocalGridPosition)
01AB: stfld m_length
```

`Init(MyObjectBuilder_ConveyorLine, MyCubeGrid)` восстанавливает **одну** линию из
`StartPosition`/`EndPosition`; промежуточные трубы в ней — `Sections` (длина и направление), а
не отдельные вершины. В графе конвейеров труба — это ребро, а не узел.

**Следствие.** Для редактора труба не существует как объект: линия соединяет два **блока с
конвейерным портом**, а всё между ними — деталь физики.

## 5. Что осталось непроверенным

- **[НЕ ПРОВЕРЕНО]** что `ConveyorLines` содержит линии **всех** гридов конструкта, а не только
  того, у которого вызван `GetObjectBuilder`. Проверка: собрать два состыкованных грида с трубой
  между ними и посмотреть, появились ли линии у обоих. По устройству (`MyCubeGridSystems` — часть
  одного грида) ожидается «только свои», поэтому редактор читает линии **каждого** грида, на
  котором есть карточки, а не одного.
- **[НЕ ПРОВЕРЕНО]** что `ConveyorLines` непуст для линии, у которой второй конец — блок
  другого грида (коннектор). Проверка: два грида, соединённых коннекторами, и труба между
  коннектором и контейнером.
- Обе проверки — в списке приёмки [ADR-0040](../adr/0040-conveyor-network.md); провал первой
  означает, что межгридовые линии не рисуются, и это записано как ограничение, а не как дефект.

## Итог для реализации

| Вопрос | Ответ |
|---|---|
| Чем читать конвейерную сеть | `((IMyEntity)grid).GetObjectBuilder()` → `MyObjectBuilder_CubeGrid.ConveyorLines` |
| Чем найти блок по концу линии | `grid.GetCubeBlock(new Vector3I(cell.X, cell.Y, cell.Z))` → `IMySlimBlock.FatBlock.EntityId` |
| Что такое узел сети | только блок **с карточкой** на схеме; труба, junction и скрытый блок — проходимые |
| Где это в коде | `Vgs.Game.ConveyorProbe` (чтение) + `Vgs.Model.ConveyorNetwork` (редукция) |
| Как часто читать | структурный проход, только для гридов с ≥ 2 карточками, только при включённом режиме |

