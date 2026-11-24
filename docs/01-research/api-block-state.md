# Состояние блока: откуда берётся «повреждение» и что ещё моду доступно

Дата проверки: 2026-11-23. Сборка игры: `1.211.017`
(`H:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64`).

Этот файл фиксирует факты, на которых стоит **проход состояния** (Шаг 2 плана организации
кода): как прочитать целостность живого блока и почему читается именно она. Всё ниже —
проверено `tools/dump-se-api.ps1` по сборкам установленной игры; рядом с каждым фактом стоит
команда. Ни один факт не взят из документации Keen или из malforge.

Решение, принятое на этих фактах, — [`03-architecture.md`](../03-architecture.md), раздел
«два прохода».

---

## 1. Целостность живёт на slim-блоке, а не на терминальном

`IMyTerminalBlock` не имеет ни `Integrity`, ни `Health`. Целостность — свойство **slim-блока**,
а к нему ведёт `IMyCubeBlock.SlimBlock`.

```powershell
pwsh -File tools/dump-se-api.ps1 -Types 'VRage.Game.ModAPI.IMyCubeBlock'
```

```
## `VRage.Game.ModAPI.IMyCubeBlock`
- assembly: `VRage.Game`
- kind: interface
- interfaces: `VRage.Game.ModAPI.Ingame.IMyCubeBlock`, `VRage.Game.ModAPI.Ingame.IMyEntity`, `VRage.ModAPI.IMyEntity`

### Properties
public IMySlimBlock SlimBlock { get; }
```

`Sandbox.ModAPI.IMyTerminalBlock` выводится из него, поэтому `block.SlimBlock` доступен без
приведения:

```powershell
pwsh -File tools/dump-se-api.ps1 -Types 'Sandbox.ModAPI.IMyTerminalBlock'
```

```
## `Sandbox.ModAPI.IMyTerminalBlock`
- assembly: `Sandbox.Common`
- interfaces: `VRage.Game.ModAPI.IMyCubeBlock`, `VRage.Game.ModAPI.Ingame.IMyCubeBlock`,
  `VRage.Game.ModAPI.Ingame.IMyEntity`, `VRage.ModAPI.IMyEntity`,
  `Sandbox.ModAPI.Ingame.IMyTerminalBlock`
```

## 2. Сама целостность объявлена на Ingame-интерфейсе

`VRage.Game.ModAPI.IMySlimBlock` не объявляет `Integrity` у себя: он наследует
`VRage.Game.ModAPI.Ingame.IMySlimBlock`, и члены живут там.

```powershell
pwsh -File tools/dump-se-api.ps1 -Types 'VRage.Game.ModAPI.Ingame.IMySlimBlock'
```

```
## `VRage.Game.ModAPI.Ingame.IMySlimBlock`
- assembly: `VRage.Game`
- kind: interface

### Properties
public Single AccumulatedDamage { get; }
public Single BuildIntegrity { get; }
public Single BuildLevelRatio { get; }
public Single CurrentDamage { get; }
public Single DamageRatio { get; }
public Boolean IsDestroyed { get; }
public Boolean IsFullIntegrity { get; }
public Single MaxIntegrity { get; }
```

```powershell
pwsh -File tools/dump-se-api.ps1 -Types 'VRage.Game.ModAPI.IMySlimBlock'
```

```
## `VRage.Game.ModAPI.IMySlimBlock`
- assembly: `VRage.Game`
- kind: interface
- interfaces: `VRage.Game.ModAPI.Ingame.IMySlimBlock`, `VRage.Game.ModAPI.Interfaces.IMyDestroyableObject`,
  `VRage.Game.ModAPI.Interfaces.IMyDecalProxy`
```

Наследование — это и есть ответ: `slim.BuildIntegrity`, `slim.MaxIntegrity`,
`slim.DamageRatio`, `slim.IsDestroyed`, `slim.IsFullIntegrity` доступны через
`VRage.Game.ModAPI.IMySlimBlock` без ссылки на `Ingame`-пространство имён.

## 3. Какое число брать

Мод берёт **`BuildIntegrity / MaxIntegrity`** и трактует результат как долю 0..1:

| Член | Что это | Почему не он |
|---|---|---|
| `BuildIntegrity` | текущая целостность блока | — (числитель) |
| `MaxIntegrity` | максимум для этого типа блока | — (знаменатель) |
| `DamageRatio` | доля повреждения | то же число наоборот; брать его — значит держать инверсию в голове в каждой точке чтения |
| `BuildLevelRatio` | доля **постройки** (сварен ли блок), не повреждения | отвечает на другой вопрос: «достроен ли», а не «цел ли» |
| `IsFullIntegrity` | булев «целый» | не даёт ни шкалы, ни порога; нужен для решения «рисовать ли индикатор» |
| `CurrentDamage` / `AccumulatedDamage` | абсолютное повреждение | требует деления на максимум в каждой точке чтения |

Формула «доля = `BuildIntegrity / MaxIntegrity`» сходится с поведением самой игры:
`IsFullIntegrity` истинно ровно тогда, когда эти два числа равны.

**Сверено с реализацией игры, а не только с именами.** `MySlimBlock.DamageRatio` считает ровно
эту формулу — то есть мод повторяет то, что игра называет «долей повреждения»:

```powershell
pwsh -File tools/dump-se-il.ps1 -Type 'Sandbox.Game.Entities.Cube.MySlimBlock' -Method 'get_DamageRatio'
```

```il
0000: ldc.r4       1
0005: ldarg.0
0006: ldfld        m_componentStack
000B: callvirt     MyComponentStack::get_BuildIntegrity
0010: ldarg.0
0011: call         MySlimBlock::get_MaxIntegrity
0016: div
0017: sub
0018: ret
```

То есть `DamageRatio = 1 − BuildIntegrity / MaxIntegrity`, и `Integrity` в терминах мода — это
`1 − DamageRatio`.

**Урон действительно уменьшает `BuildIntegrity`.** Цепочка из IL:

```powershell
pwsh -File tools/dump-se-il.ps1 -Type 'Sandbox.Game.Entities.MyComponentStack' -Method 'ApplyDamage'
```

`ApplyDamage` вызывает `UnmountInternal`, затем `UpdateBuildIntegrityDown(ratio)`, а тот
(`... -Method 'UpdateBuildIntegrityDown'`) пишет `set_BuildIntegrity(Integrity × ratio)` — то
есть новое значение пропорционально остатку. Это и есть уменьшение `BuildIntegrity` от урона;
отдельное поле `m_integrity` (свойство `Integrity`) — ёмкость стека компонентов, и мод его не
читает.

**Что это значит для «половины построенного блока».** У блока, который ещё не достроен,
`BuildIntegrity` меньше `MaxIntegrity`, и мод покажет его как повреждённый. Это не ошибка мода:
игра считает так же (`DamageRatio` — та же формула, и терминал показывает ту же полосу
прочности). Разделять «недостроен» и «разбит» — отдельная задача (для неё есть
`BuildLevelRatio`), и она не входила в требование.

**Деление на ноль.** `MaxIntegrity` равен нулю у блока, у которого целостности нет как
понятия (например, пока блок — стройплощадка без функциональной части). Мод считает такой
ответ отсутствием данных, а не «нулевой целостностью»: `TryReadIntegrity` возвращает `false`,
и карточка сохраняет предыдущее значение.

**Выход за диапазон.** `BuildIntegrity` может быть отрицательным у блока в процессе
разборки и больше максимума у переваренного модом блока. Значение зажимается в 0..1 в
`BlockState.SetIntegrity`, а не в каждом читателе.

## 4. Что этим НЕ проверяется

- **Не проверено:** меняется ли `BuildIntegrity` на клиенте в мультиплеере так же, как на
  сервере. Проход состояния — чтение, а не запись, и худшее, что может случиться на клиенте, —
  устаревшее число на карточке. Проверять это отдельным запуском игры ради индикатора, который
  ещё не нарисован, — не сейчас; когда индикатор появится (Этап 5.1), проверка нужна.
- **Не проверено:** есть ли у `IMySlimBlock` событие изменения целостности, доступное моду.
  `IMyCubeGrid.OnBlockIntegrityChanged` в дампе есть, но подписка на него означала бы чтение
  мира из колбэка на каждый удар — это отдельное решение, а не деталь этого шага. Поэтому
  взят опрос по таймеру.

## 5. Вайтлист

`VRage.Game.ModAPI` — разрешённое **пространство имён** (не поимённый список типов), поэтому
`IMySlimBlock` проходит шлюз; `Sandbox.ModAPI` — тоже. Проверяется не рассуждением, а
`pwsh -File tools/check-whitelist.ps1` на собранной сборке мода: после Шага 2 шлюз печатает
`WHITELIST OK` при 158 ссылках на типы (было 156; +`IMySlimBlock` и +`BlockState`).

---

## 6. Флаг питания блока (`Enabled` / `IsFunctional`) — проверено 2026-11-24

Второе значение того же прохода состояния (ADR-0035): включён ли блок и можно ли его вообще
включить. Это **две разные величины**, и их легко перепутать.

```powershell
pwsh -File tools/dump-se-api.ps1 -Types 'Sandbox.ModAPI.IMyFunctionalBlock','Sandbox.ModAPI.Ingame.IMyFunctionalBlock','VRage.Game.ModAPI.Ingame.IMyCubeBlock'
```

```
## `Sandbox.ModAPI.IMyFunctionalBlock`        (assembly Sandbox.Common)
- interfaces: ... Sandbox.ModAPI.IMyTerminalBlock, VRage.Game.ModAPI.IMyCubeBlock, ...
### Properties
public Boolean IsUpdateTimerCreated { get; }
public Boolean IsUpdateTimerEnabled { get; }
### Events
public event Action<IMyTerminalBlock> EnabledChanged

## `Sandbox.ModAPI.Ingame.IMyFunctionalBlock`
### Properties
public Boolean Enabled { get; set; }
### Methods
public abstract Void RequestEnable(Boolean enable)

## `VRage.Game.ModAPI.Ingame.IMyCubeBlock`
### Properties
public Boolean IsFunctional { get; }
public Boolean IsWorking { get; }
```

Три вывода, и все три проверены, а не выведены из названий:

1. **`Enabled` объявлен на Ingame-интерфейсе**, а `Sandbox.ModAPI.IMyFunctionalBlock` из него
   выводится, поэтому `((IMyFunctionalBlock)block).Enabled` доступен без приведения к
   `Ingame`. `IsFunctional` живёт на `IMyCubeBlock`, то есть доступен у любого блока, а не
   только у функционального.
2. **`Enabled` — это `Sync<bool, BothWays>`**, а не поле: `MyFunctionalBlock.set_Enabled`
   (`dump-se-il.ps1 ... -Type 'Sandbox.Game.Entities.Cube.MyFunctionalBlock' -Method 'set_Enabled'`)
   — это `ldfld m_enabled; ldarg.1; callvirt Sync`2::set_Value`. Следствие: на удалённом клиенте
   запись уходит на сервер, и мод не может отличить «принято» от «отправлено» — поэтому после
   записи блок **перечитывается** (ADR-0035 §Decision 4).
3. **`RequestEnable` помечен `[Obsolete("Use the setter of Enabled")]`** и делает то же самое:
   `MyFunctionalBlock.Sandbox.ModAPI.Ingame.IMyFunctionalBlock.RequestEnable`
   = `ldarg.1; callvirt set_Enabled`. Мод использует сеттер, чтобы не тащить предупреждение,
   которое спрятало бы следующее, настоящее.

### Почему `IsFunctional`, а не `Enabled`, отвечает на «включить нельзя»

```powershell
pwsh -File tools/dump-se-il.ps1 -Assembly 'Sandbox.Game.dll' -Type 'Sandbox.Game.Entities.MyComponentStack' -Method 'get_IsFunctional'
```

```
0000: ldarg.0
0001: call    MyComponentStack::get_IsBuilt
0006: brfalse.s 27
0008: ldarg.0
0009: call    MyComponentStack::get_Integrity
000E: ldarg.0
000F: call    MyComponentStack::get_MaxIntegrity
0014: ldarg.0
0015: ldfld   m_blockDefinition
001A: ldfld   CriticalIntegrityRatio
001F: mul
0020: cgt
0022: ret
0023: ldc.i4.0
0024: ret
```

То есть `IsFunctional == IsBuilt && Integrity > MaxIntegrity * CriticalIntegrityRatio`. Отсюда:
**блок ниже критической целостности продолжает репортить `Enabled == true`** и просто не
работает. Если карточка берёт флаг питания в лоб, сломанный блок выглядит зелёным. Поэтому
`BlockState.SetPower(enabled, functional)` даёт функциональному флагу приоритет и рисует третий
цвет — `ColPowerUnavailable` (ADR-0035 §Decision 2).

Свой порог по целостности мод **не** заводит: `CriticalIntegrityRatio` — свойство определения
блока, у разных блоков разное, и копия этого числа в моде разошлась бы с игрой на первой правке
баланса.

### Что этим НЕ проверяется

- **Не проверено:** приходит ли `EnabledChanged` на клиенте при выключении блока сервером.
  Подписки на него мод не делает — проход состояния читает флаг сам (ADR-0035 §Decision 3), —
  так что проверять это отдельным запуском игры не требуется.
- **Не проверено:** как ведёт себя `IsWorking` у блока с включённым `Enabled`, но не
  работающим по другой причине (нет энергии, нет топлива). Мод его не читает: «включён и
  работает» и «включён и не работает» на карточке различить нечем, и требование этого не
  просило.
