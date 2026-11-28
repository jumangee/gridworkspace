# Параметры действий: проверенный контракт

Что игра публикует об аргументах действия и как их читать, проверять и писать в элемент
тулбара. Все утверждения получены на этой машине рефлексией и чтением IL установленной
сборки (`H:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64`), дата проверки: 2026-11-27.

Команды, которыми это добыто:

```powershell
& .\tools\dump-se-api.ps1 -Types 'Sandbox.ModAPI.Interfaces.ITerminalAction'
& .\tools\dump-se-api.ps1 -Types 'Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalAction'
& .\tools\dump-se-api.ps1 -Types 'Sandbox.ModAPI.Ingame.TerminalActionParameter','VRage.Game.MyObjectBuilder_ToolbarItemActionParameter'
& .\tools\dump-se-api.ps1 -Types 'Sandbox.Common.ObjectBuilders.MyObjectBuilder_ToolbarItemTerminal'
& .\tools\dump-se-api.ps1 -Types 'Sandbox.ModAPI.MyToolbarApiExtensions'
& .\tools\dump-se-il.ps1  -Type 'Sandbox.ModAPI.MyToolbarApiExtensions'
& .\tools\dump-se-il.ps1  -Assembly 'Sandbox.Common.dll' -Type 'Sandbox.ModAPI.Ingame.TerminalActionParameter'
& .\tools\dump-se-il.ps1  -Type 'Sandbox.Game.Entities.Cube.MyMotorStator' -Method 'CreateTerminalControls'
& .\tools\dump-se-il.ps1  -Type 'Sandbox.Game.Entities.Cube.MyMotorStator' -Method 'OnRotateToAngleApplied'
& .\tools\dump-se-il.ps1  -Type 'Sandbox.Game.Entities.Cube.MyMotorStator' -Method 'RequestRotationParametersAndLabel'
& .\tools\dump-se-il.ps1  -Type 'Sandbox.Game.Entities.Blocks.MyProgrammableBlock' -Method 'RequestRunArgumentAndTitle'
& .\tools\dump-se-il.ps1  -Type 'Sandbox.Game.Gui.MyTerminalAction`1' -Method 'Apply'
```

---

## 1. Как игра спрашивает параметры (то, что мод повторяет)

Действие с параметрами регистрируется как `MyTerminalAction<TBlock>` с двумя
делегатами (`dump-se-il.ps1 -Type 'Sandbox.Game.Entities.Cube.MyMotorStator' -Method
'CreateTerminalControls'`, участок вокруг `ldstr "RotateToAngle"`):

| Делегат | Что делает | Проверка |
|---|---|---|
| `ActionWithParameters` | исполняет действие, когда значения уже есть | `OnRotateToAngleApplied(IMyMotorStator, ListReader<TerminalActionParameter>)` |
| `DoUserParameterRequestWithItem` | спрашивает значения у игрока | `RequestRotationParametersAndLabel` / `RequestRunArgumentAndTitle` |
| `ParameterDefinitions` | объявляет **типы** аргументов | `get_ParameterDefinitions` + `TerminalActionParameter.Get(...)` в том же IL |

**Само окно ввода мод повторить не может.** `RequestRotationParametersAndLabel` создаёт
`Sandbox.Graphics.GUI.MyGuiScreenDialogAmount` и кладёт его через `MyGuiSandbox.AddScreen`;
`RequestRunArgumentAndTitle` — `Sandbox.Game.Screens.MyGuiScreenDialogText`. Оба типа живут в
пространствах имён, которых нет в вайтлисте скриптов (`Sandbox.Graphics`, `Sandbox.Game.Screens`),
поэтому **вызвать этот диалог из мода нельзя** — окно рисуется своё (ADR-0050).

**Ротор спрашивает три значения, а не два.** В `RequestRotationParametersAndLabel` диалог
открывается **один раз** на угол (`MyGuiScreenDialogAmount`), а `OnRotateToAngleApplied`
принимает **три** параметра: `Single` (угол, градусы) → `MathHelper.ToRadians`, `Single`
(скорость) → `Math.Abs`, `Int64` (направление) → `conv.i4` в `MyRotationDirection`. То есть
«2 параметра» из требования — это два *числовых* поля плюс направление, которое игра
подставляет сама. Мод объявляет ровно то, что объявила игра: список из `ParameterDefinitions`,
без домыслов.

## 2. Что игра публикует о параметрах: только типы — и для встроенных действий это заглушка

`IMyTerminalAction.ParameterDefinitions` — `List<TerminalActionParameter>`, и у
`TerminalActionParameter` есть ровно два поля (`dump-se-api.ps1 -Types
'Sandbox.ModAPI.Ingame.TerminalActionParameter'`):

```csharp
public readonly TypeCode TypeCode
public readonly Object   Value
public static readonly TerminalActionParameter Empty
public bool IsEmpty { get; }
public static TerminalActionParameter Deserialize(String serializedValue, TypeCode typeCode)
public static TerminalActionParameter Get(Object value)
public MyObjectBuilder_ToolbarItemActionParameter GetObjectBuilder()
```

**Имени, минимума, максимума и шага в метаданных нет.** То, что игрок видит в ванильном
диалоге как «от −360 до 360 с шагом 1», — это аргументы конструктора
`MyGuiScreenDialogAmount`, зашитые в C# блока (`ldc.r4` в
`RequestRotationParametersAndLabel`: `-360f`, `+360f`), а не свойство действия.

### 2.1. Тип в метаданных — заглушка, и это ломает запись

**Проверено по IL, и это была причина дефекта в игре.** Ротор объявляет три аргумента
`RotateToAngle` так (`dump-se-il.ps1 -Type
'Sandbox.Game.Entities.Cube.MyMotorStator' -Method 'CreateTerminalControls'`, участок вокруг
`ldstr "RotateToAngle"`):

```
0BE8: stfld   DoUserParameterRequestWithItem
0BED: callvirt get_ParameterDefinitions
0BF3: ldsfld  Empty                       <-- TerminalActionParameter.Empty
0BF8: call    TerminalActionParameter::Get
0BFD: callvirt List`1::Add                <-- и так три раза
```

`TerminalActionParameter.Empty` — это `default(TerminalActionParameter)`, у которого
`TypeCode == TypeCode.Empty` (`get_IsEmpty` сравнивает `TypeCode` с нулём). То есть в
метаданных **не тип аргумента, а отсутствие типа**. Наш же путь чтения
(`MyToolbarApiExtensions.GetActionParameterDefinitions`) берёт `TypeCode` из этих самых
заглушек и возвращает `Empty` для всех трёх.

Обработчик того же действия читает совсем другое
(`dump-se-il.ps1 -Method 'OnRotateToAngleApplied'`):

```
0026: call get_IsEmpty          ; если пусто — выйти
0038: ldc.i4.s 13               ; TypeCode.Single  — угол
0042: ldc.i4.s 14               ; TypeCode.Single  — скорость
007D: ldc.i4.s 9                ; TypeCode.Int64   — направление
0087: ldc.i4.s 11               ; ...либо TypeCode.UInt64
```

и на первом же несовпадении делает `ret`, то есть **молча ничего не делает**.

Отсюда правило мода (ADR-0051): тип аргумента берётся из **таблицы проверенных фактов по id
действия**, а не из `ParameterDefinitions`. Наивная запись «тип из метаданных» даёт элемент,
который в тулбаре есть, а действие по нему не срабатывает.

### 2.2. Какие типы на самом деле у проверенных действий

| Действие | Аргументы (проверено) | Чем проверено |
|---|---|---|
| `Run` (programmable block) | один `String` | `RequestRunArgumentAndTitle` → `MyGuiScreenDialogText`; `MyProgrammableBlock.Run(String argument, …)` |
| `RotateToAngle` (rotor, hinge) | `Single` (угол, °), `Single` (скорость, RPM), `Int64` (направление) | `OnRotateToAngleApplied`: `ToRadians` для первого, `Math.Abs` для второго, `conv.i4` в `MyRotationDirection` для третьего |
| `SetVelocity` (rotor) | один `Single`, RPM | `OnSetVelocityApplied` → `IMyMotorStator.set_TargetVelocityRPM(Single)` |
| `SetLowerLimit` (rotor) | один `Single`, ° | `OnSetLowerLimitApplied` → `set_LowerLimitDeg(Single)` |
| `SetUpperLimit` (rotor) | один `Single`, ° | `OnSetUpperLimitApplied` → `set_UpperLimitDeg(Single)` |

Три последних получены разрешением токена вызова в теле обработчика (скрипт на
`Module.ResolveMethod`), а не по имени: имя ничего не доказывает, а токен указывает ровно на
сеттер.

### 2.3. Направление ротора — перечисление, а не число

`Sandbox.ModAPI.Ingame.MyRotationDirection` (`dump-se-api.ps1`, значения — рефлексией по
загруженной сборке): **`AUTO = 0`, `CW = 1`, `CCW = 2`**.

Ванильный диалог показывает именно список (`MyGuiScreenDialogEnumDropdown<MyRotationDirection>`
в `<>c__DisplayClass74_1::<RequestRotationParametersAndLabel>b__2`), поэтому «выпадающий
список» из требования — это оно. В тулбар при этом пишется **число** (`Int64`), и это
единственная форма, которую принимает обработчик: значение вне 0…2 он превращает в
`(MyRotationDirection)value`, то есть в бессмысленное направление. Мод показывает те же три
значения с их смыслом и принимает только их (ADR-0051 §4).

### 2.4. Значения по умолчанию: их не публикуют, но они есть у блока

Игра предзаполняет свои диалоги (`<>c__DisplayClass74_0::b__0`):

- угол — **0** (`ldc.r4 0x00000000` в конструкторе диалога);
- скорость — `MaxRotorAngularVelocity * 60 / (2π)`, то есть максимум ротора в RPM
  (`ldc.r4 0x4118C9EB` = 9.5493 = 60/2π);
- аргумент скрипта — `IMyProgrammableBlock.TerminalRunArgument`, последнее, с чем скрипт
  запускали (ради него у блока есть второе действие, `RunWithDefaultArgument`).

Скорость и аргумент читаются из блока, а не из константы (ADR-0051 §5): максимум ротора
зависит от его типа, а последний аргумент — от того, что делал игрок.

### 2.5. Полный список действий, объявляющих аргументы — измерено

Проход по всем типам трёх сборок: у каждого типа взят `CreateTerminalControls`, из его IL
собраны id действий, рядом с которыми вызывается `get_ParameterDefinitions`. Команда —
`pwsh -File tools/scan-action-parameters.ps1`; тот же проход порождает
`Model/ActionParameterTable.g.cs`, а `offline-check.ps1` сравнивает результат с закоммиченным
файлом (ADR-0052).

**Всего 22 действия.** «Тип» — то, что делает `unbox.any` в обработчике; «—» значит, что
обработчик приводит значение иначе (неявным преобразованием в сеттер свойства), и тип из IL
так не читается.

| Действие | Блок | Тип из `unbox.any` | Подпись диалога (имя поля) |
|---|---|---|---|
| `RotateToAngle` | MyMotorStator | Single, Single, Int64 | Set Angle · Set Velocity · Set Rotation Direction |
| `SetColor` | MyLightingBlock | Int32 ×3 (одно поле) | Set Color |
| `SetChannelValue` | MySignalChannelEntityComponent | Int32 | Set Channel |
| `SetChannelValue_SendSignal` | то же | Int32 | Set Channel |
| `SetAndMove` | MyPistonBase | Single, Single | Set Distance · Set Velocity |
| `SetGravityAcceleration` | MyJumpDrive | Single | Set Jump Distance |
| `SetOverride` | MyThrust | Single | Set Override Percentage |
| `SetRadius` | MyBeacon | Single | Set Broadcast Radius |
| `SetSpeedLimit` | MyFlightMovementBlock | Single | Set Speed Limit |
| `SetAngleDeviation` | MyTurretControlBlock | Single | Set Angle Deviation |
| `SetVelocity` | MyMotorStator | Single | Set Velocity |
| `SetLowerLimit` | MyMotorStator | Single | Set Lower Limit |
| `SetUpperLimit` | MyMotorStator | Single | Set Upper Limit |
| `SetPropulsionOverride` | MyMotorSuspension | Single | Set Propulsion Override |
| `SetSteerOverride` | MyMotorSuspension | Single | Set Steer Override |
| `SetBlinkInterval` | MyLightingBlock | Single | Set Blink Interval |
| `SetDelay` | MyTimerBlock | Single | Set Delay |
| `SetColorMin` | MyHeatVentBlock | Int32 ×3 (одно поле) | Set Color |
| `SetColorMax` | MyHeatVentBlock | Int32 ×3 (одно поле) | Set Color |
| `SetHudText` | MyBeacon | String (сеттер `SetHudText(String)`) | Set HUD Text |
| `Run` | MyProgrammableBlock | String | Argument |
| `GpsList` | MyRemoteControl | **не выведен** — тип остаётся объявленным, действие названо в логе покрытия | GPS Locations |

**Все 22 имени — из игры.** Ни одного придуманного: подпись — это `MyStringId`, который игра
грузит либо в методе запроса (§2.7), либо в самой регистрации действия.

**Тип выведен у 21 из 22.** Источники, в порядке точности: `unbox.any` обработчика →
хелпер, который он зовёт → `Convert.ToXxx` → одноимённый сеттер (`SetHudText(String)`) →
«обработчик не читает список параметров, значит значение не важно». У `GpsList` ни один не
дал ответа (его `OnAction` читает список, но передаёт его в `FillGpsList`, а собственный
`unbox.any` — это его же enum `Direction`), поэтому тип остаётся **объявленным**, и действие
честно попадает в лог покрытия. Догадка вместо него не подставляется: неверный тип — это
мёртвая связь, ровно тот дефект, из-за которого всё это делается.
### 2.6. Action Relay: подпись есть, и она «Set Channel»

`RequestChannelArgumentAndTitle` строит диалог с подписью `SignalChannel_SetChannel_Action`
(«Set Channel»). В первой версии сканера её не было, потому что подпись загружается внутри
метода, а связана была только с делегатом `DoUserParameterRequestWithItem`. Теперь обход
транзитивный (§2.7) и подпись находится.

Проверка, что имя параметра **не совпадает** с именем действия — то, на что указал владелец
продукта:

| Что | Ключ | Текст |
|---|---|---|
| действие | `SignalChannel_SetChannelAndSend_Action` | Set Channel and Send Signal |
| параметр | `SignalChannel_SetChannel_Action` | **Set Channel** |
### 2.7. Откуда берётся НАЗВАНИЕ каждого параметра — цепочка диалогов

Это главное, чего не хватало ADR-0051. Игра не публикует имена параметров списком; она строит
**по диалогу на параметр**, и подпись диалога — это и есть имя параметра на экране.

Ключевое: диалоги строятся **цепочкой замыканий**, а не одним методом.

```
RequestRotationParametersAndLabel        (метод запроса действия)
  ├─ newobj MyGuiScreenDialogAmount      подпись ldsfld BlockActionTitle_SetAngle
  └─ add_OnConfirmed → b__0              (замыкание первого диалога)
       ├─ newobj MyGuiScreenDialogAmount подпись ldsfld BlockActionTitle_SetVelocity
       └─ add_OnConfirmed → b__2         (замыкание второго диалога)
            ├─ newobj MyGuiScreenDialogEnumDropdown<MyRotationDirection>
            │                        подпись ldsfld BlockActionTitle_SetRotationDirection
            └─ add_OnConfirmed → b__5    (третий диалог подтверждён → метка)
                 └─ CreateLabelDialog    подпись ldsfld SetValue_Action_SetLabel
```

Отсюда три правила чтения, без которых получаются ровно те заглушки, на которые указал
владелец продукта:

1. **Обход транзитивный.** Подпись второго и третьего параметра лежит не в методе запроса, а в
   замыканиях его диалогов. Одного метода недостаточно — именно поэтому второй и третий
   параметр ротора оказывались «Number 2» и «Number 3».
2. **Диалог метки надо отбросить.** У каждого параметризованного действия есть ещё диалог
   **метки тулбара** с подписью `SetValue_Action_SetLabel` («Set label»). Он не параметр, и
   если его посчитать, третьим параметром ротора станет «Set label». Отличается он
   поведением: его обработчик подтверждения вызывает `SetCustomIconTitle`, обработчики
   параметров — нет. Проверено: `<>c__DisplayClass74_3::<RequestRotationParametersAndLabel>b__8`
   → `SetCustomIconTitle`.
3. **`add_OnCancelled` идёт ПЕРЕД `add_OnConfirmed`.** Поиск «обработчик подтверждения — это
   последний `ldftn` перед `add_OnConfirmed`» обязан игнорировать `add_OnCancelled`, иначе
   диалог метки выглядит как диалог без обработчика подтверждения и не отбрасывается.

Второй источник подписи — **сама регистрация действия**. Часть действий (маяк, таймер,
подъёмник, трап, скорость полёта) спрашивает единственный параметр через общий
`MyGuiSandbox.ShowSetValueActionDialog(..., caption, ...)`, и `MyStringId` подписи грузится
прямо в `CreateTerminalControls`, а не в методе запроса. Пример:

```
0c31: ldstr 'SetVelocity'
0c36: ldsfld BlockActionTitle_SetVelocity   ← подпись параметра
0c41: ldftn OnSetVelocityApplied
```

Поэтому имя берётся так: сначала диалоги метода запроса, а если их нет — подпись из регистрации.

### 2.8. Сколько параметров заполняет один диалог — тоже факт из игры

`SetColor` объявляет **три** параметра (`Int32, Int32, Int32`), но спрашивает их **одним**
диалогом выбора цвета с подписью «Set Color». Проверено по `CreateTerminalControls` у
`MyLightingBlock`: три вызова `get_ParameterDefinitions` подряд после `ldftn ShowSetColorActionDialog`,
и у действия ровно одна подпись.

Значит форма обязана рисовать **одно поле**, а не три: три поля вынудили бы придумать два
названия, которых в игре нет. Значения вводятся через запятую, и число значений проверяется.

Разбиение выводится из двух счётчиков, которые игра даёт сама: если число подписей равно числу
параметров — по одному на параметр; если подпись одна, а параметров несколько — она называет
всё значение.

### 2.9. Действия, которые параметр объявляют, но не читают

`GpsList` у `MyRemoteControl` регистрирует параметр, а обработчик — `OnAction`, который список
параметров не смотрит вообще (ни `get_Count`, ни `get_Item`, ни `FirstOrDefault`). Значение всё
равно нужно, чтобы элемент тулбара был корректным; игра в таком случае хранит пустую строку.
Это тоже выведено, а не предположено: обработчик, который не трогает список, — это обработчик,
для которого значение не важно.

## 3. Где значения живут: внутри элемента тулбара

```csharp
// Sandbox.Common.ObjectBuilders.MyObjectBuilder_ToolbarItemTerminal
public String _Action
public List<MyObjectBuilder_ToolbarItemActionParameter> Parameters
```

```csharp
// VRage.Game.MyObjectBuilder_ToolbarItemActionParameter
public TypeCode TypeCode
public String   Value
```

Значение сериализуется **текстом** и в инвариантной культуре:
`TerminalActionParameter.GetObjectBuilder` делает
`Convert.ToString(Value, CultureInfo.InvariantCulture)`, а `Deserialize` — обратный
`Convert.ChangeType(serializedValue, typeCode, InvariantCulture)`. Отсюда правило мода:
значение хранится и сравнивается **как текст в инвариантной культуре**, а не как `object`
(ADR-0050 §1).

`TypeCode` ограничен примитивами: `AssertTypeCodeValidity` бросает
`ArgumentException("Only primitive types are allowed for action parameters")` для
`TypeCode <= 2` (`Empty`, `Object`, `DBNull`), а `Deserialize` для неизвестного кода
возвращает `Empty`. Поэтому `TypeCode.Empty` из заглушки (§2.1) записать в элемент
**нельзя** — `Deserialize` вернёт `Empty`, и `GetObjectBuilder` запишет `TypeCode = 0`.
Такой элемент игра не исполнит никогда.

## 4. Чтение и запись: только через ModAPI

Движковые `MyToolbarItemActions.Parameters` живут в `Sandbox.Game.Screens.Helpers` и моду
**запрещены** (та же причина, что у `MyToolbar`, ADR-0011). Разрешённый путь — расширения
`Sandbox.ModAPI.MyToolbarApiExtensions` (`dump-se-api.ps1 -Types
'Sandbox.ModAPI.MyToolbarApiExtensions'`):

```csharp
public static void GetActionParameterDefinitions(IMyTerminalBlock block, String actionId, List<TypeCode> output)
public static void GetParameters(IMyToolbarSlots slots, Int32 index, List<TerminalActionParameter> output)
public static IMyToolbarChangeBuilder Set(IMyToolbarChangeBuilder builder, Int32 index, MyToolbarItemInfo item, List<TerminalActionParameter> parameters)
public static IMyToolbarChangeBuilder BlockAction(IMyToolbarChangeBuilder builder, Int32 index, IMyTerminalBlock block, String actionId, List<TerminalActionParameter> parameters = null, String label = null)
public static IMyToolbarChangeBuilder GroupAction(IMyToolbarChangeBuilder builder, Int32 index, IMyTerminalBlock contextBlock, String groupName, String actionId, List<TerminalActionParameter> parameters = null, String label = null)
```

По IL (`dump-se-il.ps1 -Type 'Sandbox.ModAPI.MyToolbarApiExtensions'`):

- `GetParameters` делает `isinst IMyToolbarSlotItems` → `GetSlotItem(index)` →
  `isinst MyToolbarItemActions` → `get_Parameters`, и **молча возвращает пустой список**,
  если элемент не того типа. Значит «нет параметров» и «элемент не параметризованный» —
  одно и то же наблюдение, и различать их моду нечем.
- `Set` делает `isinst MyToolbarChangeBuilder` и при промахе вызывает `Reject` → `Clear(-1)`.
  То есть неверный builder **не бросает**, а отклоняет пакет: `Apply()` вернёт `false`.
- `BlockAction`/`GroupAction` без `parameters` пишут элемент **без** параметров. Именно так
  писал предыдущий код мода — для действия без аргументов это верно, для `Run` — нет.

## 5. Что осталось непроверенным

- **Диапазоны конкретных действий** — в метаданных их нет, а читать их из IL каждого блока
  значит завести таблицу подтипов. Мод проверяет то, что проверяемо: тип значения и (для
  проверенных действий) границы, взятые из того же IL, что и типы (ADR-0051 §3).
- **Поведение `GetParameters` на клиенте в мультиплеере** — чтение тулбара работает
  везде, где работает `TryGetItem` (ADR-0011), но отдельного прогона в сети не было.
  Помечено `[НЕ ПРОВЕРЕНО]`; на запись это не влияет — запись уже ограничена `IsOwnedHere`.
- **Что игра делает с элементом, у которого параметров меньше, чем объявлено.** По IL
  `OnRotateToAngleApplied` выходит, если `parameters.Count < 3`, а `OnRunApplied` берёт
  первый и подставляет `string.Empty`, если его нет. То есть недобор — не исключение, а
  «действие ничего не делает». Поэтому мод требует все значения до записи (ADR-0050 §3).
- **Действия других модов.** Их id не описаны в таблице (ADR-0051), поэтому для них
  действует прежнее правило: тип из метаданных. Если чужое действие объявляет заглушку, его
  элемент может не сработать — но и угадать его типы мод не может. `[НЕ ПРОВЕРЕНО]` в части
  того, объявляют ли сторонние моды настоящие типы; проверить это можно только на конкретном
  моде.
- **`RunWithDefaultArgument`** — второе действие programmable block, без аргументов: оно
  запускает скрипт с `TerminalRunArgument`. Существует по IL (`ldstr
  "RunWithDefaultArgument"` в `CreateTerminalControls`); в таблицу не внесено, потому что
  аргументов у него нет, но именно оно подтверждает, что последний аргумент — устойчивое
  свойство блока, а не догадка.






