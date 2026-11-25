# Event controller: что доступно моду и чем это проверено

Событийный контроллер (`MyObjectBuilder_EventControllerBlock`) — блок, который следит за
**списком выбранных блоков** (`selected blocks`) и запускает свой тулбар, когда состояние
одного из них (или всех, в режиме «И») удовлетворяет условию.

Этот файл фиксирует, что из его состояния видно моду и что можно изменить. Всё ниже —
**проверено чтением сборок установленной игры**; рядом с каждым фактом стоит команда.

## 1. Публичный контракт

```powershell
& .\tools\dump-se-api.ps1 -Types 'Sandbox.ModAPI.IMyEventControllerBlock','Sandbox.ModAPI.Ingame.IMyEventControllerBlock','Sandbox.ModAPI.IMyEventControllerEntityComponent','Sandbox.ModAPI.IMyEventComponentWithGui'
```

`Sandbox.ModAPI.IMyEventControllerBlock` (assembly `Sandbox.Common`) — **публичный и
разрешённый моду** тип, наследует `Sandbox.ModAPI.Ingame.IMyEventControllerBlock`:

```csharp
public IMyEventControllerEntityComponent SelectedEvent { get; }   // выбранное событие
public void TriggerAction(int startIndex);                        // запуск тулбара
```

`Sandbox.ModAPI.Ingame.IMyEventControllerBlock` — то, что видно и программируемому блоку:

```csharp
public float Threshold { get; set; }              // величина отслеживания, 0..1
public bool IsAndModeEnabled { get; set; }        // «И» вместо «ИЛИ» по списку блоков
public bool IsLowerOrEqualCondition { get; set; } // «меньше или равно» вместо «больше»
```

`Sandbox.ModAPI.IMyEventControllerEntityComponent` (компонент события):

```csharp
public long UniqueSelectionId { get; }
public MyStringId EventDisplayName { get; }        // локализованное имя события
public bool IsSelected { get; set; }
public string YesNoToolbarYesDescription { get; }
public string YesNoToolbarNoDescription { get; }
```

`Sandbox.ModAPI.IMyEventComponentWithGui` (уточнение компонента — **его реализуют все
конкретные события**, и именно к нему игра приводит `SelectedEvent`):

```csharp
public bool IsThresholdUsed { get; }        // у события есть порог
public bool IsConditionSelectionUsed { get; } // у события есть выбор «больше/меньше»
public bool IsBlocksListUsed { get; }       // событие вообще смотрит на список блоков
public void AddBlocks(List<IMyTerminalBlock> blocks);
public void RemoveBlocks(IEnumerable<IMyTerminalBlock> blocks);
public bool IsBlockValidForList(IMyTerminalBlock block);
public void RefreshDetailedInfo();
```

**Чего в контракте нет:** списка выбранных блоков. Ни `SelectedBlocks`, ни геттера на
`m_selectedBlocks`. Единственный публичный путь к списку — запись через
`AddBlocks`/`RemoveBlocks` компонента.

## 1a. Чтение игрового списка через объект-билдер

```powershell
& .\tools\dump-se-api.ps1 -Types 'VRage.Game.ModAPI.IMyCubeBlock','Sandbox.Common.ObjectBuilders.MyObjectBuilder_EventControllerBlock'
& .\tools\dump-se-il.ps1 -Assembly 'SpaceEngineers.Game.dll' -Type 'SpaceEngineers.Game.Entities.Blocks.MyEventControllerBlock' -Method 'GetObjectBuilderCubeBlock'
```

Геттера нет, но **чтение есть**, и оно публичное:

```csharp
// VRage.Game.ModAPI.IMyCubeBlock, namespace разрешён
public MyObjectBuilder_CubeBlock GetObjectBuilderCubeBlock(bool copy = false);
```

`MyObjectBuilder_EventControllerBlock` (namespace `Sandbox.Common.ObjectBuilders`, разрешён)
несёт `public MySerializableList<long> SelectedBlocks`.

**Это живое состояние, а не снимок сейва.** IL переопределения в контроллере:

```il
0062: newobj  HashSet<long>
0069: ldfld   m_selectedBlocks
006E: callvirt Dictionary`2::get_Keys        ; ключи словаря
00A5: ldfld   m_selectedBlockIds
00C4: callvirt HashSet`1::Add                ; плюс список
00E8: newobj  MySerializableList`1<long>
00ED: stfld   SelectedBlocks
```

То есть билдер собирает `SelectedBlocks` из **приватных полей в момент вызова**: объединение
`m_selectedBlocks.Keys` и `m_selectedBlockIds`. Вызов безопасен и на клиенте: это чтение полей
блока, без записи в мир.

Следствия:

- схема может показывать то, что игрок настроил в терминале, и подхватывать изменения;
- **записи редактора в этот список не попадают**: `IMyEventComponentWithGui.AddBlocks` наполняет
  компонент события, но не приватные поля блока. Поэтому редакторская часть списка хранится
  отдельно (надстройка) и проигрывается в компонент — [ADR-0018](../adr/0018-game-is-the-source-of-truth.md);
- `MySerializableList<T>` объявлен в `VRage.ObjectBuilder` (единственное число), которого нет в
  вайтлисте, поэтому читать его нужно через `System.Collections.IList`, не называя тип.

## 1b. Почему порт не появляется в терминальном `selected blocks` — проверено перебором

Три независимых факта, каждый проверен по IL установленной сборки.

**1. Терминал показывает только приватный словарь блока.**

```powershell
& .\tools\dump-se-il.ps1 -Assembly 'SpaceEngineers.Game.dll' -Type 'SpaceEngineers.Game.Entities.Blocks.MyEventControllerBlock' -Method 'GetSelectedBlocks,SelectButton'
```

- `GetSelectedBlocks` (наполняет listbox «Selected blocks») читает
  `m_selectedBlocks.Values` — приватный `Dictionary<long, IMyTerminalBlock>`;
- кнопка «добавить» в терминале (`SelectButton`) собирает id и вызывает
  `MyMultiplayer.RaiseEvent(this, <>9__62_0, list)`, то есть сетевой метод
  `AddBlocks(List<long>)` **самого блока**. Оба живут на типе
  `SpaceEngineers.Game.Entities.Blocks.MyEventControllerBlock`, чей namespace вайтлистом
  запрещён, а `Sandbox.Engine.Multiplayer` — тоже.

**2. Компонент события не трогает список блока.** Перебор всех классов в
`SpaceEngineers.Game.EntityComponents.Blocks.Events`, реализующих `IMyEventComponentWithGui`
(30 типов), по IL метода `AddBlocks`:

| Что вызывается у контроллера | Типы событий |
|---|---|
| `get_Threshold`, `get_IsLowerOrEqualCondition` | AngleChanged, BlockIntegrity, CargoFilled, GasTankFilled, PistonPosition, PowerOutput, StoredPower, ThrustPercentage |
| только `get_IsLowerOrEqualCondition` | DistanceToLockedTarget, NaturalGravityChanged, SurfaceHeight |

**Ни один тип события не вызывает `AddBlocks(List<long>)` блока.** Значит
`IMyEventComponentWithGui.AddBlocks` (единственная доступная моду дверь) наполняет
**компонент**: подписку на событие блока, словарь состояний и `OnClosing`. Приватный словарь
`m_selectedBlocks` остаётся пустым.

**3. Поэтому `selected blocks` в терминале остаётся пустым.** Следствие для схемы: порт
отслеживания, созданный из редактора, **работает** (контроллер реально слышит блок через
компонент и срабатывает), но в ванильном списке его нет и в сейв игры он не попадает —
его хранит надстройка мода ([ADR-0018](../adr/0018-game-is-the-source-of-truth.md)).

### 1c. Три уровня записи, которые нельзя смешивать

Формулировка «поле меняется только в `Init`» верна только для **ссылки** на коллекцию.
Если не разделить три уровня, получается противоречие: `AddBlocks(List<long>)` действительно
меняет содержимое, но не то, о котором говорит перебор `stfld`. Уровни такие:

| Уровень | Что именно меняется | Где это происходит | Доступно моду |
|---|---|---|---|
| **ссылка** на коллекцию (`stfld`) | `m_selectedBlockIds = builder.SelectedBlocks`; `= null` | `Init`; `ProcessSelectedBlocksFinished` | нет |
| **элементы** `m_selectedBlockIds` (`List<long>`) | рабочий буфер восстановления из сейва | `ProcessSelectedBlocks` забирает id и снимает их; `Init` заполняет | нет |
| **элементы** `m_selectedBlocks` (`Dictionary<long, IMyTerminalBlock>`) | живой выбор — то, что показывает терминал | `AddBlocks(List<long>)` добавляет, `OnSelectedEventIdChanged` чистит, `OnBlockClosing`/`RemoveBlocks` удаляют | нет |
| **состояние компонента события** | подписка на событие блока | `IMyEventComponentWithGui.AddBlocks` / `RemoveBlocks` | **да** |

Ссылка на `m_selectedBlocks` не переприсваивается нигде: перебор `stfld` по всей сборке даёт
для `m_selectedBlock*` ровно два места, и оба — `m_selectedBlockIds`. Словарь создаётся в
инициализаторе поля и дальше только наполняется.

Отсюда точная формулировка того, что делает мод: он пишет **только в четвёртую строку**.
Первые три строки меняет исключительно сам блок — его приватными методами.

### 1d. Внутренние пути существуют, но закрыты вайтлистом

Утверждение «другого пути нет» неверно: пути есть, их использует, например,
Multigrid Projector (publicizer открывает `m_selectedBlockIds`/`m_selectedBlocks`). Для VGS
они закрыты, и это проверяется шлюзом, а не принимается на веру:

| Путь | Что нужно сослаться | Вердикт |
|---|---|---|
| reflection (`Type.GetField(...).GetValue`) | `System.Reflection.FieldInfo` | **запрещено**: в вайтлисте есть `BindingFlags` и `MemberInfo`, но нет `FieldInfo`/`MethodInfo`/`PropertyInfo` |
| publicizer / прямая ссылка на поле | `SpaceEngineers.Game.Entities.Blocks.MyEventControllerBlock` | **запрещено**: из `SpaceEngineers.*` разрешены только `SpaceEngineers.Game.ModAPI*` и один тип определений |
| повторный `Init(builder, grid)` | `VRage.Game.ModAPI.IMyCubeBlock` | тип разрешён, шлюз пропустит — отвергнуто по поведению, а не по вайтлисту (ниже) |

Проверка вайтлиста:

```powershell
& .\tools\read-se-whitelist.ps1 | Select-String -Pattern 'Reflection|SpaceEngineers\.Game\.Entities'
```

Повторный `Init` с билдером, где дописан `SelectedBlocks`, действительно записал бы id в
игровой список (`Init` присваивает `m_selectedBlockIds = builder.SelectedBlocks`). Это
**переинициализация живого блока**: `MyCubeBlock.Init()` заново создаёт модель, позицию и
инвентарь, `Init` контроллера заново создаёт компонент события, `ResourceSink` и
`MyGridColorHelper`, а подписки на `OnGridSplit`/`OnBlockRemoved`/`OnConnectionChanged`
регистрируются повторно. Старый компонент события при этом остаётся подписанным на `OnClosing`
отслеживаемых блоков. Мод так не делает: испортить блок игрока ради отображения одного списка
— несоразмерная цена.

### 1e. Про официальную документацию `AddBlocks`

Официальная страница `Sandbox.ModAPI.IMyEventComponentWithGui` говорит:

> `AddBlocks` — *Adds the specified block to the monitoring of the event*.
> `RemoveBlocks` — *Removes the specified block from the monitoring*.

Это **точное** описание того, что делает метод, и код мода делает именно это. Расхождение —
не в методе, а в том, что считать «списком»:

| Что | Кто владеет | Что показывает терминал | Что делает `AddBlocks` |
|---|---|---|---|
| **мониторинг события** — подписка компонента на событие блока | компонент события (`MyEventBlockOnOff` и др.) | **не показывает** | **пишет сюда** |
| **`selected blocks`** — приватный `m_selectedBlocks` блока | сам блок контроллера | показывает именно его | не трогает |

То есть после `AddBlocks` контроллер **действительно следит** за блоком и срабатывает от него
(это и есть «monitoring of the event»), но в listbox «Selected blocks» запись не появляется:
его наполняет только `GetSelectedBlocks` из приватного словаря, куда пишет только сам блок
(§1b). Документация описывает первое, пользователь ожидает увидеть второе.

**Перебор по всей сборке** `SpaceEngineers.Game.dll` (все методы всех типов, разрешение
токенов вызовов) даёт жёсткую границу: вызовы `MyEventControllerBlock.AddBlocks(List<long>)`
встречаются **ровно один раз** — это делегат `<>9__62_0`, который `SelectButton` передаёт в
`MyMultiplayer.RaiseEvent`. Ни компонент события, ни какой-либо другой тип его не вызывает.

Значит живой выбор (`m_selectedBlocks`) наполняется только приватными методами самого блока,
и единственный внешний вход в них — сетевой обработчик кнопки терминала. Уровни записи и
доступность каждого разобраны в §1c, закрытые внутренние пути — в §1d.

Проверить, что мониторинг работает, можно по логу мода: строка
`now monitors block <id> (event component only; ...)` пишется, когда запись ушла в компонент,
и `(already in vanilla Selected Blocks)` — когда блок уже был выбран в терминале.




## 2. Как игра сама добавляет блок

```powershell
& .\tools\dump-se-il.ps1 -Assembly 'SpaceEngineers.Game.dll' -Type 'SpaceEngineers.Game.Entities.Blocks.MyEventControllerBlock' -Method 'AddBlocks,ValidateBlock,ProcessSelectedBlocks,UpdateAfterSimulation10'
& .\tools\dump-se-il.ps1 -Assembly 'SpaceEngineers.Game.dll' -Type 'SpaceEngineers.Game.Entities.EntityComponents.Blocks.Events.MyEventControllerGenericBooleanEvent`1' -Method 'AddBlocks'
```

`MyEventControllerBlock.AddBlocks(List<long> toSync)` — **внутренний** путь (тип лежит в
`SpaceEngineers.Game.Entities.Blocks`, которого нет в вайтлисте скриптов):

1. `RaiseEvent` в мультиплеере (синхронизация списка на сервер);
2. `ValidateBlock(block)` — блок обязан быть **в том же логическом гриде**
   (`MyCubeGrid.IsInSameLogicalGroupAs`), иначе отказ;
3. подписка на `OnClosing` блока, добавление в `m_selectedBlocks`;
4. `(SelectedEvent as IMyEventComponentWithGui).AddBlocks(blocks)` + `RefreshDetailedInfo()`;
5. `RaisePropertiesChanged()`.

`ProcessSelectedBlocks()` (вызывается из `UpdateAfterSimulation10`, то есть примерно раз в
секунду) разрешает `m_selectedBlockIds` в живые блоки и делает те же шаги 3–5. Именно поэтому
сохранённый в сейве список восстанавливается после загрузки мира.

`MyEventControllerGenericBooleanEvent.AddBlocks` подписывается на событие блока **только на
сервере**:

```il
0044: call Sandbox.Game.Multiplayer.Sync::get_IsServer
0049: brfalse.s 12            ; на клиенте подписка пропускается
```

## 2a. Собственного запрета «не себя» у игры нет

```powershell
& .\tools\dump-se-il.ps1 -Assembly 'SpaceEngineers.Game.dll' `
    -Type 'SpaceEngineers.Game.Entities.Blocks.MyEventControllerBlock' -Method 'ValidateBlock'
```

Всё тело `ValidateBlock` — это «не null» плюс «тот же логический конструкт»:

```il
0000: ldarg.1
0001: brtrue.s     2
0003: ldc.i4.0
0004: ret
0005: ldarg.0
0006: call         Sandbox.Game.Entities.MyCubeBlock::get_CubeGrid
000B: ldarg.1
000C: callvirt     VRage.Game.ModAPI.IMyCubeBlock::get_CubeGrid
0011: callvirt     Sandbox.Game.Entities.MyCubeGrid::IsInSameLogicalGroupAs
0016: brtrue.s     2
0018: ldc.i4.0
0019: ret
001A: ldc.i4.1
001B: ret
```

`AddBlocks` (`MyEventControllerBlock`) проводит блок через этот `ValidateBlock`, вешает
`OnClosing` и кладёт id в `m_selectedBlocks`, а подписку делает компонент события
(`MyEventControllerGenericEvent.AddBlocks`: он берёт компонент наблюдаемого блока, проверяет
«уже в словаре?», кладёт его туда и подписывается только при `Sync.IsServer`) — ни один из
шагов не сравнивает наблюдаемый блок с контроллером. Значит контроллер может отслеживать
сам себя; редактор это разрешает ([ADR-0036](../adr/0036-self-connection.md)).

Проверка «тот же конструкт» на блоке — это сравнение гридов, и для одного блока она истинна:

```powershell
& .\tools\dump-se-il.ps1 -Assembly 'Sandbox.Game.dll' `
    -Type 'Sandbox.Game.Entities.Cube.MyTerminalBlock' `
    -Method 'Sandbox.ModAPI.IMyTerminalBlock.IsInSameLogicalGroupAs'
& .\tools\dump-se-il.ps1 -Assembly 'Sandbox.Game.dll' `
    -Type 'Sandbox.Game.Entities.MyCubeGrid' -Method 'IsInSameLogicalGroupAs'
```

```il
; MyTerminalBlock
0000: ldarg.0
0001: call         Sandbox.Game.Entities.MyCubeBlock::get_CubeGrid
0006: ldarg.1
0007: callvirt     VRage.Game.ModAPI.IMyCubeBlock::get_CubeGrid
000C: callvirt     Sandbox.Game.Entities.MyCubeGrid::IsInSameLogicalGroupAs
0011: ret

; MyCubeGrid — первая же инструкция: тот же грид → true
0000: ldarg.0
0001: ldarg.1
0002: beq.s        40        ; → 0x2C: ldc.i4.1; ret
```

То есть `controller.IsInSameLogicalGroupAs(controller)` возвращает `true`, и самопросмотр
проходит проверку конструкта.

## 3. Что из этого следует для мода

| Вопрос | Ответ | Почему |
|---|---|---|
| Можно ли **добавить** блок в `selected blocks`? | Да, через `IMyEventComponentWithGui.AddBlocks` — **на сервере** | Тип компонента и метод в `Sandbox.ModAPI`, namespace разрешён; на клиенте игра сама не подписывает |
| Можно ли **убрать** блок? | Только если его добавил мод (`RemoveBlocks` снимает подписку) | Запись, сделанную в терминале, `RemoveBlocks` не удаляет: `ProcessSelectedBlocks` вернёт её из приватного словаря через секунду |
| Можно ли **прочитать** список? | **Да** — через `GetObjectBuilderCubeBlock().SelectedBlocks` (§1a) | Живые приватные поля блока, а не снимок сейва |
| Сохранится ли добавленное модом? | **Нет, само по себе** | `GetObjectBuilderCubeBlock` собирает `SelectedBlocks` из приватных `m_selectedBlocks`/`m_selectedBlockIds`, куда мод писать не может |
| Совпадает ли `IsBlockValidForList` с «тип блока зависит от события»? | Да, это и есть правило игры | `MyEventBlockOnOff`: `block is IMyFunctionalBlock`; `MyEventBlockIntegrity`: `true` (любой блок) |
| Может ли контроллер наблюдать **сам себя**? | **Да** — своего запрета у игры нет | `ValidateBlock` (§2a) проверяет только «не null» и `IsInSameLogicalGroupAs`; `AddBlocks` и `MyEventControllerGenericEvent.AddBlocks` владельца не спрашивают |

Вывод: игровой список — **источник истины для чтения**, а то, что мод добавляет сам, живёт
только в компоненте и в хранилище мода. Отсюда схема из [ADR-0018](../adr/0018-game-is-the-source-of-truth.md):
показывается объединение, игровая часть первой; игровые записи из редактора не удаляются.

## 4. Вайтлист

```powershell
& .\tools\check-whitelist.ps1 -Dump
```

Разрешённые namespace, которые здесь нужны, — точным совпадением:

- `Sandbox.ModAPI` → `IMyEventControllerBlock`, `IMyEventControllerEntityComponent`,
  `IMyEventComponentWithGui`;
- `Sandbox.ModAPI.Ingame` → `IMyEventControllerBlock` (порог, режим, направление сравнения);
- `VRage.ModAPI` → `IMyEntities` (хук «сущность добавлена», нужен для восстановления списка
  после загрузки мира);
- `VRage.Utils` + `VRage.MyTexts` → `MyStringId` и `MyTexts.GetString` (имя события).

**Запрещено:** `SpaceEngineers.Game.Entities.Blocks.MyEventControllerBlock`
(`AddBlocks(List<long>)`, `ValidateBlock`) — namespace не разрешён, и это ровно тот случай,
когда компиляция проходит, а игра отклоняет мод при загрузке.

## 5. Переживают ли добавления мода секундный проход игры

```powershell
& .\tools\dump-se-il.ps1 -Assembly 'SpaceEngineers.Game.dll' -Type 'SpaceEngineers.Game.Entities.Blocks.MyEventControllerBlock' -Method 'ProcessSelectedBlocks'
```

**Проверено.** `ProcessSelectedBlocks` (его зовёт `UpdateAfterSimulation10`, то есть примерно
раз в секунду) собирает список блоков **только из `m_selectedBlockIds`**, вызывает
`AddBlocks`/`RefreshDetailedInfo` на нём и вычищает `m_selectedBlockIds` из `m_selectedBlocks`
лишь при расхождении счётчиков. **`RemoveBlocks` он не вызывает и компонент из
`m_selectedBlocks` не пересобирает** — значит добавление, сделанное модом на уровне
компонента, этот проход не стирает.

Следствие: список, записанный модом в компонент, живёт до конца сессии и работает; но в
`MyObjectBuilder_EventControllerBlock.SelectedBlocks` он не попадает (см. §1a), поэтому после
загрузки мира его проигрывает `EventControllerKeeper`, а список терминала игра восстанавливает
сама.

## 6. Открытые вопросы

- `[НЕ ПРОВЕРЕНО]` Порядок `OnEntityAdd` относительно загрузки `MyModStorageComponent`
  контроллера. Проверка: построить контроллер с сохранённым списком, загрузить мир, посмотреть
  в логе строку восстановления.
- `[НЕ ПРОВЕРЕНО]` Влияние `AddBlocks` на клиенте мультиплеера: подписки нет, но запись в
  внутренний словарь компонента есть. Решение мода — не писать на клиенте вообще (§3).
- `[НЕ ПРОВЕРЕНО]` Стоимость `GetObjectBuilderCubeBlock` на контроллере в большом мире:
  вызов аллоцирует билдер и список. Проверка: профилировщик на гриде с десятком контроллеров
  при открытом редакторе.
