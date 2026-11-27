# Состояние блока для виджета: заполненность, заряд, отсчёт таймера

Дата проверки: 2026-11-26. Сборка игры: `1.211.017`
(`H:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64`).

Этот файл фиксирует факты, на которых стоит **виджет состояния** карточки (ADR-0042): как
прочитать пять значений, которые он рисует. Всё ниже проверено `tools/dump-se-api.ps1`,
`tools/find-se-type.ps1` и `tools/dump-se-il.ps1` по сборкам установленной игры; рядом с каждым
фактом стоит команда. Ни один факт не взят из документации Keen или из malforge.

---

## 1. Заполненность инвентаря

`IMyInventory` (Ingame-интерфейс) объявляет три числа: текущий объём, максимальный и готовый
коэффициент заполнения.

```powershell
& .\tools\dump-se-api.ps1 -Types 'VRage.Game.ModAPI.Ingame.IMyInventory'
```

```
### Properties
public Boolean IsFull { get; }
public MyFixedPoint CurrentMass { get; }
public MyFixedPoint MaxVolume { get; }
public MyFixedPoint CurrentVolume { get; }
public Int32 ItemCount { get; }
public Single VolumeFillFactor { get; }
public Boolean CanPutItems { get; }
```

**Берётся `CurrentVolume / MaxVolume`, а не `VolumeFillFactor`.** Готовый коэффициент возвращает
ноль, когда инвентарь не ограничен по объёму, — это другой вопрос («есть ли у инвентаря максимум»),
и он нарисовал бы неограниченный инвентарь пустым:

```powershell
& .\tools\dump-se-il.ps1 -Assembly 'Sandbox.Game.dll' -Type 'Sandbox.Game.MyInventory' -Method 'get_VolumeFillFactor'
```

```il
0000: ldarg.0
0001: call         call Sandbox.Game.MyInventory::get_IsConstrained
0006: brtrue.s     6
0008: ldc.r4       token 0x00000000      <- ноль, если инвентарь не ограничен
000D: ret
000E: ldarg.0
000F: callvirt     callvirt VRage.Game.Entity.MyInventoryBase::get_CurrentVolume
0014: call         call VRage.MyFixedPoint::op_Explicit
0019: conv.r4
001A: ldarg.0
001B: callvirt     callvirt VRage.Game.Entity.MyInventoryBase::get_MaxVolume
0020: call         call VRage.MyFixedPoint::op_Explicit
0025: conv.r4
0026: div
0027: ret
```

То есть игра считает ровно `CurrentVolume / MaxVolume`, когда инвентарь ограничен. Мод повторяет
эту формулу и сам решает, что делать с `MaxVolume == 0` (считать, что читать нечего).

`MyFixedPoint` имеет **явный** оператор приведения к `Single` — именно его использует IL выше:

```powershell
& .\tools\dump-se-il.ps1 -Assembly 'VRage.Library.dll' -Type 'VRage.MyFixedPoint' -Method 'op_Explicit'
```

```il
## op_Explicit(MyFixedPoint fp)
0000: ldarg.0
0001: ldfld        ldfld RawValue
0006: conv.r4
0007: ldc.r4       token 0x49742400    (1 000 000)
000C: div
000D: ret
```

## 2. Где взять инвентарь у блока

`IMyCubeBlock` наследует `VRage.ModAPI.IMyEntity`, у которого есть `GetInventory()` и
`GetInventory(int)`:

```powershell
& .\tools\dump-se-api.ps1 -Types 'VRage.Game.ModAPI.Ingame.IMyEntity'
```

```
### Properties
public Boolean HasInventory { get; }
public Int32 InventoryCount { get; }
### Methods
public abstract IMyInventory GetInventory()
public abstract IMyInventory GetInventory(Int32 index)
```

У `IMyProductionBlock` инвентари названы прямо:

```powershell
& .\tools\dump-se-api.ps1 -Types 'Sandbox.ModAPI.IMyProductionBlock'
```

```
### Properties
public IMyInventory InputInventory { get; }
public IMyInventory OutputInventory { get; }
```

То же объявлено и на Ingame-варианте (`Sandbox.ModAPI.Ingame.IMyProductionBlock`), который
выводится из `Sandbox.ModAPI.IMyProductionBlock`? — **нет, наоборот**: `Sandbox.ModAPI.IMyProductionBlock`
перечисляет `Sandbox.ModAPI.Ingame.IMyProductionBlock` среди своих интерфейсов, поэтому члены
`InputInventory`/`OutputInventory` доступны через Ingame-интерфейс, а `IsProducing`/`IsQueueEmpty`
живут только там:

```
## `Sandbox.ModAPI.Ingame.IMyProductionBlock`
### Properties
public IMyInventory InputInventory { get; }
public IMyInventory OutputInventory { get; }
public Boolean IsProducing { get; }
public Boolean IsQueueEmpty { get; }
public UInt32 NextItemId { get; }
public Boolean UseConveyorSystem { get; set; }
```

Мод читает `InputInventory`/`OutputInventory` через `Sandbox.ModAPI.IMyProductionBlock` (тип
`Sandbox.ModAPI` разрешён вайтлистом) и приводит объём к `float` оператором из §1.

## 3. Заряд и режим батареи

```powershell
& .\tools\dump-se-api.ps1 -Types 'Sandbox.ModAPI.Ingame.IMyBatteryBlock','Sandbox.ModAPI.Ingame.ChargeMode'
```

```
## `Sandbox.ModAPI.Ingame.IMyBatteryBlock`
### Properties
public Boolean HasCapacityRemaining { get; }
public Single CurrentStoredPower { get; }
public Single MaxStoredPower { get; }
public Single CurrentInput { get; }
public Single MaxInput { get; }
public Boolean IsCharging { get; }
public ChargeMode ChargeMode { get; set; }
public Boolean OnlyRecharge { get; set; }
public Boolean OnlyDischarge { get; set; }
public Boolean SemiautoEnabled { get; set; }

## `Sandbox.ModAPI.Ingame.ChargeMode`
- values: `Auto`, `Recharge`, `Discharge`
```

Доля заряда — `CurrentStoredPower / MaxStoredPower`; направление — **`ChargeMode`**, а не
сравнение с прошлым чтением: `IsCharging` истинно только в `Recharge`, а `Auto` может и заряжать,
и разряжать в зависимости от сети, поэтому «направление» честно берётся из режима, который выбрал
игрок.

## 4. Отсчёт таймера

`IMyTimerBlock` (пространство `SpaceEngineers.Game.ModAPI`, разрешено вайтлистом) **не отдаёт,
сколько осталось**:

```powershell
& .\tools\dump-se-api.ps1 -Types 'SpaceEngineers.Game.ModAPI.Ingame.IMyTimerBlock'
```

```
### Properties
public Boolean IsCountingDown { get; }
public Single TriggerDelay { get; set; }
public Boolean Silent { get; set; }
### Methods
public abstract Void StartCountdown()
public abstract Void StopCountdown()
public abstract Void Trigger()
```

Есть `IsCountingDown` и `TriggerDelay` (в секундах), но нет текущего положения. Оно есть в
собственном object builder блока:

```powershell
& .\tools\dump-se-api.ps1 -Types 'Sandbox.Common.ObjectBuilders.MyObjectBuilder_TimerBlock'
```

```
### Fields
public MyObjectBuilder_Toolbar Toolbar
public Boolean JustTriggered
public Int32 Delay
public Int32 CurrentTime
public Boolean IsCountingDown
public Boolean Silent
```

И игра заполняет их именно так:

```powershell
& .\tools\dump-se-il.ps1 -Assembly 'SpaceEngineers.Game.dll' -Type 'SpaceEngineers.Game.Entities.Blocks.MyTimerBlock' -Method 'GetObjectBuilderCubeBlock'
```

```il
003A: ldarg.0
003B: ldfld        ldfld m_countdownMsStart
0040: stfld        stfld Delay
0045: dup
0046: ldarg.0
0047: ldfld        ldfld m_countdownMsCurrent
004C: stfld        stfld CurrentTime
0051: dup
0052: ldarg.0
0053: call         call ...::get_IsCountingDown
0058: stfld        stfld IsCountingDown
```

То есть `Delay` — длительность отсчёта в миллисекундах, `CurrentTime` — **сколько осталось** в тех
же миллисекундах: `Start()` кладёт в `m_countdownMsCurrent` полную длительность
(`m_countdownMsCurrent = m_countdownMsStart`, проверено `dump-se-il.ps1 ... -Method 'Start'`), а
`UpdateAfterSimulation10` вычитает из неё и на нуле срабатывает.

**Сколько именно вычитается — не «десять миллисекунд за тик».** В IL стоит
`ldc.i4 0x000000A6` = **166 мс за вызов** `UpdateAfterSimulation10`, а вызов происходит раз в
десять тиков симуляции: при 60 тиках в секунду это шесть вызовов, то есть ~996 мс за секунду
(проверено `dump-se-il.ps1 ... -Method 'UpdateAfterSimulation10'`; там же `rem 1000` — остаток для
порогов звука, и `ldc.i4 0x00000320` = 800 мс — порог последнего бипа). Первая редакция этого
файла говорила «10 мс за тик» — это было неверно, и потому записано здесь, а не молча исправлено:
на прогресс бара это не влияет (читается остаток, а не шаг), но влияет на ожидание «как часто
двигается стрелка» — значение обновляется ~6 раз в секунду, а проход состояния читает его ~2.

**Отсюда доля прошедшего = `1 − CurrentTime / Delay`**, и мод считает именно её: стрелка начинает
на 12 часах и возвращается на 12 к срабатыванию. Знаменатель берётся из `Delay` (не из
`TriggerDelay`): `TriggerDelay` — секунды с плавающей точкой после `ValidateRange` (сеттер
умножает на 1000, `dump-se-il.ps1 ... -Method 'set_TriggerDelay'`), а `Delay` — те же
миллисекунды, что и `CurrentTime`, поэтому деление не смешивает единицы.

Чтение идёт через `IMyCubeBlock.GetObjectBuilderCubeBlock(false)` — тот же путь, которым
`EventControllerProbe` уже читает список отслеживания (ADR-0018).

## 5. Что этим НЕ проверяется

- **Не проверено:** обновляется ли `CurrentTime` в object builder на клиенте мультиплеера так же,
  как на сервере. Проход состояния — чтение, худшее последствие — стрелка отстаёт на полсекунды;
  отдельного запуска игры ради этого не делалось.
- **Не проверено:** поведение `CurrentTime` у таймера, поставленного на паузу в момент срабатывания
  (значение между нулём и `Delay`). Карточка покажет прочитанное; ошибкой это не считается.
- **Не проверено:** как ведёт себя `ChargeMode` у батареи, которой управляет скрипт другого мода
  (`SemiautoEnabled`, `OnlyRecharge`). Мод читает режим, а не вычисляет его, поэтому расхождение
  возможно только если игра репортит режим, отличный от фактического.

## 6. Вайтлист

Все использованные типы проходят шлюз (`pwsh -File tools/check-whitelist.ps1` → `WHITELIST OK`,
209 ссылок на типы):

| Тип | Пространство | Как разрешено |
|---|---|---|
| `IMyInventory` | `VRage.Game.ModAPI` | пространство разрешено целиком |
| `MyFixedPoint` | `VRage` | **поимённо** (корневое `VRage` разрешено поимённо, не как пространство) |
| `IMyBatteryBlock`, `ChargeMode` | `Sandbox.ModAPI.Ingame` | пространство разрешено |
| `IMyProductionBlock` | `Sandbox.ModAPI` | пространство разрешено |
| `IMyTimerBlock` | `SpaceEngineers.Game.ModAPI.Ingame` | пространство разрешено |
| `MyObjectBuilder_TimerBlock` | `Sandbox.Common.ObjectBuilders` | пространство разрешено |
| `IMyFunctionalBlock` | `Sandbox.ModAPI` | пространство разрешено |
## 7. Как отличить блок по интерфейсу, а не по подтипу

Проверено `tools\dump-se-api.ps1` и шлюзом вайтлиста (все пять типов разрешены моду; ссылок в
сборке стало 209 против 204 — ровно на пять).

| Форма | Интерфейс | Сборка / пространство имён |
|---|---|---|
| `FillBar` | `IMyCargoContainer` | `Sandbox.ModAPI` |
| `Battery` | `IMyBatteryBlock` | `Sandbox.ModAPI` |
| `Clock` | `IMyTimerBlock` | `SpaceEngineers.Game.ModAPI` — **не** `Sandbox.ModAPI` |
| `Production` | `IMyRefinery`, `IMyAssembler` | `Sandbox.ModAPI` |

`IMyTimerBlock` лежит в `SpaceEngineers.Game.ModAPI`, и это единственный из пяти не в
`Sandbox.ModAPI`; остальные четыре подключаются через уже импортированный `Sandbox.ModAPI`.

**Подтип для этой задачи не годится, и это измерено.** В `data/block-catalog.json` под
`typeId = MyObjectBuilder_CargoContainer` лежат двадцать блоков: `LargeBlockLargeContainer`,
`SmallBlockSmallContainer`, `LargeBlockBulkContainerA`, `LargeBlockLockerRoom`, `LargeBlockLockers`,
`LargeBlockInsetBookshelf`, `LargeBlockWeaponRack`, `LargeBlockCargoTerminal`, `LargeBlockLabCabinet`
и другие. Подстроки `cargocontainer` нет ни в одном; подстрока `container` есть ровно в десяти из
двадцати, то есть у половины её нет. Команда, которой это получено:

```powershell
$lines = Get-Content data\block-catalog.json
$pairs = for ($i = 0; $i -lt $lines.Count; $i++) {
  if ($lines[$i] -match '"subtypeId":\s*"([^"]+)"') {
    $sub = $Matches[1]
    for ($j = $i + 1; $j -lt [Math]::Min($i + 4, $lines.Count); $j++) {
      if ($lines[$j] -match '"typeId":\s*"([^"]+)"') {
        [pscustomobject]@{ Sub = $sub; Type = $Matches[1] }; break
      }
    }
  }
}
$cargo = $pairs | Where-Object { $_.Type -eq 'MyObjectBuilder_CargoContainer' }
@($cargo).Count                                                              # 20
@($cargo | Where-Object { $_.Sub -notmatch 'container' }).Count              # 10
```

Первый, более простой вариант команды — `[regex]::Matches($raw, '"subtypeId":…')` — возвращает все
1559 подтипов файла и на вопрос о контейнерах не отвечает; он и был причиной неверного счёта в
первой редакции этого раздела.

Поэтому форма берётся из интерфейса, а таблица подтипов остаётся запасным путём для
модифицированных блоков.

**Не проверено:** что `IMyCargoContainer` реализуют ровно те двадцать подтипов каталога — это
следует из `typeId`, но в игре не сверялось; и что модифицированный блок, реализующий
`IMyCargoContainer`, но не имеющий инвентаря, вернёт `false` из чтения (тогда карточка просто
сохранит прошлую картинку, а не нарисует пустой бар).
