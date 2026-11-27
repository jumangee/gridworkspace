# Горячая клавиша редактора: что проверено

Задача: схема должна открываться клавишей **M**, но только когда игрок сидит в кокпите и
ничего другого не владеет клавиатурой. Вопросы, на которые нужен ответ по сборкам игры:

1. как узнать, что игрок **в кокпите** (и что это вообще разрешено моду);
2. как узнать, что **другой экран** сейчас владеет клавиатурой;
3. как прочитать **нажатие клавиши** и можно ли вместо опроса зарегистрировать привязку;
4. в какой момент кадра читать клавишу, чтобы нажатие не потерялось и не сработало дважды.

Все команды выполнялись из корня репозитория против установленной игры
(`H:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64`).

## 1. «Игрок в кокпите» — `IMySession.ControlledObject`

```powershell
& .\tools\dump-se-api.ps1 -Types 'VRage.Game.ModAPI.IMySession','Sandbox.ModAPI.IMyCockpit'
```

```csharp
// VRage.Game.ModAPI.IMySession
public IMyControllableEntity ControlledObject { get; }

// Sandbox.ModAPI.IMyCockpit
- interfaces: Sandbox.ModAPI.IMyShipController, …, VRage.Game.ModAPI.Interfaces.IMyControllableEntity, …
```

```powershell
& .\tools\dump-se-il.ps1 -Assembly 'Sandbox.Game.dll' -Type 'Sandbox.Game.World.MySession' -Method '*Controlled*'
```

```il
## VRage.Game.ModAPI.IMySession.get_ControlledObject()
call Sandbox.Game.World.MySession::get_ControlledEntity
ret

## get_ControlledEntity()
call Sandbox.Game.World.MySession::get_LocalHumanPlayer
brtrue … (null → null)
call Sandbox.Game.World.MyPlayer::get_Controller
callvirt Sandbox.Game.World.MyEntityController::get_ControlledEntity
ret
```

**Вывод: [ПРОВЕРЕНО]** `MyAPIGateway.Session.ControlledObject` — это
«сущность, которой управляет локальный игрок»: `LocalHumanPlayer.Controller.ControlledEntity`.
Пешком это персонаж, в кокпите — блок кокпита, в отдалённом управлении — блок удалённого
управления. `Sandbox.ModAPI.IMyCockpit` реализует `VRage.Game.ModAPI.Interfaces.IMyControllableEntity`,
поэтому проверка «игрок в кокпите» — это приведение `ControlledObject as IMyCockpit`, без
таблиц подтипов и без обхода грида. Тот же интерфейс — тот, на который регистрируется
терминальная кнопка (`VgsTerminalControls`, `AddControl<IMyCockpit>`), то есть набор блоков
у клавиши и у кнопки совпадает по построению.

**Устаревания нет:** `[Obsolete]` на свойстве не стоит (проверено рефлексией —
`GetCustomAttributes($false)` на `IMySession.ControlledObject` пуст). Отдельный путь
`MyAPIGateway.Session.Player.Controller.ControlledEntity` даёт то же самое, но длиннее.

## 2. «Другой экран владеет клавиатурой» — четыре разных ответа

```powershell
& .\tools\dump-se-api.ps1 -Types 'VRage.Game.ModAPI.IMyGui'
```

```csharp
public String ActiveGamePlayScreen { get; }
public Boolean ChatEntryVisible { get; }
public MyTerminalPageEnum GetCurrentScreen { get; }   // свойство, не метод
public Boolean IsCursorVisible { get; }
```

```powershell
& .\tools\dump-se-il.ps1 -Assembly 'Sandbox.Game.dll' -Type 'Sandbox.ModAPI.MyGuiModHelpers' -Method 'get_ActiveGamePlayScreen'
```

```il
call Sandbox.Game.Gui.MyGuiScreenGamePlay::get_ActiveGameplayScreen
brtrue …                          // поле не null → ветка ниже возвращает его Name
call Sandbox.Game.Gui.MyGuiScreenTerminal::GetCurrentScreen
ldc.i4.s 254                      // MyTerminalPageEnum.None
beq …                             // терминал закрыт → ldnull
ldstr "MyGuiScreenTerminal"       // терминал ОТКРЫТ → эта строка
…
ldloc.0; callvirt MyGuiScreenBase::get_Name; ret
```

- **`ActiveGamePlayScreen`** — имя верхнего игрового экрана **или `"MyGuiScreenTerminal"`,
  когда открыт терминал**, или `null`, если не открыто ничего. **Терминал в это поле входит** —
  аксессор сам подставляет строку, если поле пусто, а `GetCurrentScreen` не `None`.
- **`GetCurrentScreen`** — `MyTerminalPageEnum`, ровно `None`, когда терминального экрана нет.
  В решении он **всё равно проверяется отдельно**, и это защита в глубину, а не незнание:
  во-первых, тем же признаком уже пользуется `TerminalJump` (ADR-0021), то есть у мода один
  способ спросить «открыт ли терминал»; во-вторых, первая проверка читает чужое поле, которое
  выставляет обработчик ввода, а вторая спрашивает сам экран.
- **`ChatEntryVisible`** — видимость поля ввода чата (`MyGuiScreenChat.ChatTextbox.Visible`).
  Отдельная проверка нужна потому, что ввод в чат — не полноэкранный экран.
- **`IsCursorVisible`** — геттер `MySandboxGame.IsCursorVisible`, то есть «нарисован ли
  указатель». **Модом не используется** и в решении не участвует: это ответ на другой вопрос
  («виден ли курсор»), а не «владеет ли экран клавиатурой», и у мода-экрана указатель свой.

**Инвентарь — это тот же терминальный экран, а не отдельный.**

```powershell
& .\tools\dump-se-il.ps1 -Assembly 'Sandbox.Game.dll' -Type 'Sandbox.Game.Gui.MyGuiScreenTerminal' -Method 'SwitchToInventory'
& .\tools\dump-se-il.ps1 -Assembly 'Sandbox.Game.dll' -Type 'Sandbox.Game.Gui.MyGuiScreenTerminal' -Method 'GetCurrentScreen'
```

```il
## SwitchToInventory
ldsfld m_instance; ldfld m_terminalTabs; ldc.i4.0; callvirt set_SelectedPage   // та же вкладка терминала

## GetCurrentScreen()
call MyGuiScreenTerminal::get_IsOpen
brfalse.s 16
ldsfld m_instance; ldfld m_terminalTabs; callvirt get_SelectedPage            // открыт → текущая вкладка
ret
ldc.i4.s 254; ret                                                             // закрыт → None
```

**Вывод: [ПРОВЕРЕНО, по IL]** инвентарь (`I`) — это вкладка того же `MyGuiScreenTerminal`, а
`GetCurrentScreen` возвращает `None` только когда экран закрыт. Значит проверка «терминал не
открыт» накрывает и инвентарь, отдельного признака для него не нужно.

Почему `ActiveGamePlayScreen` можно читать как «открыт ли экран»:

```powershell
& .\tools\dump-se-il.ps1 -Assembly 'Sandbox.Game.dll' -Type 'Sandbox.Game.Gui.MyGuiScreenGamePlay' -Method 'HandleUnhandledInput'
```

Поле `MyGuiScreenGamePlay.ActiveGameplayScreen` — статическое, и **единственный писатель** в
этом типе — `HandleUnhandledInput`, тот самый обработчик, который открывает экраны по клавишам
(например, справку F1):

```il
03F5: call get_ActiveGameplayScreen
03FA: brtrue.s 38                  // экран уже открыт → ничего не открывать
…
0417: dup
0418: call set_ActiveGameplayScreen   // иначе — записать открытый экран
041D: call MyGuiSandbox::AddScreen
```

**Вывод: [ПРОВЕРЕНО, по IL]** поле равно `null`, когда никакой игровой экран не открыт, и игра
сама пользуется им как проверкой «экран уже открыт». **Но пишет его только этот обработчик** —
значит проверка означает «экран не открывался этим путём», а не «экрана нет вообще». Что этим
путём **не** покрывается, см. §5. Следствие, принятое в ADR-0045: клавиша работает только при
`ActiveGamePlayScreen == null` **и** `GetCurrentScreen == None` **и** `!ChatEntryVisible`.

## 3. Чтение клавиши: опрос, а не привязка

```powershell
& .\tools\dump-se-api.ps1 -Types 'VRage.ModAPI.IMyInput'
```

`IMyInput` — **только геттеры**: `IsNewKeyPressed(MyKeys)`, `IsKeyPress`, `GetControl`,
`GetGameControl`, `IsControl(…)`. Ни `AddControl`, ни регистрации привязки в интерфейсе нет;
схема управления читается игрой из конфигурации
(`& .\tools\dump-se-api.ps1 -Types 'VRage.Input.MyVRageInput' -IncludeNonPublic` →
`LoadControls(controlsGeneral, controlsButtons, controllerBindings)`).

**Вывод: [ПРОВЕРЕНО]** мод не может добавить себе привязку в настройки игры; остаётся опрос
клавиши. `VRage.Input.MyKeys` разрешён моду **поимённо** (`& .\tools\check-whitelist.ps1 -Dump`,
раздел `=== types ===`), а `VRage.Input` как пространство имён — **нет**, поэтому в коде
используется только тип `MyKeys`, а не другие типы этого пространства.

Семантика «нового нажатия»:

```powershell
& .\tools\dump-se-il.ps1 -Assembly 'VRage.Input.dll' -Type 'VRage.Input.MyVRageInput' -Method 'IsNewKeyPressed'
& .\tools\dump-se-il.ps1 -Assembly 'VRage.Input.dll' -Type 'VRage.Input.MyVRageInput' -Method 'Update'
```

```il
## IsNewKeyPressed(MyKeys key)
ldfld m_keyboardState; callvirt MyGuiLocalizedKeyboardState::IsKeyDown
brfalse.s 13
ldfld m_keyboardState; callvirt MyGuiLocalizedKeyboardState::IsPreviousKeyUp
ret
ldc.i4.0; ret

## Update(Boolean gameFocused) — состояние на кадр
call MyVRageInput::UpdateStates        // при смене фокуса окна
…
call MyVRageInput::UpdateStates
```

**Вывод: [ПРОВЕРЕНО, по IL]** «новое нажатие» — это `IsKeyDown(key) && IsPreviousKeyUp(key)` по
состоянию, собранному в `MyVRageInput.Update → UpdateStates` (а не в `MyGuiSandbox.HandleInput`).
Пока клавиша удерживается, `IsPreviousKeyUp` ложно, поэтому `IsNewKeyPressed` истинно ровно на
одном кадре нажатия. Удержание M не переоткрывает закрытый редактор — это то, что нужно.

## 4. Порядок кадра: ввод обновлён раньше, чем идёт симуляция

```powershell
& .\tools\dump-se-il.ps1 -Assembly 'Sandbox.Game.dll' -Type 'Sandbox.MySandboxGame' -Method 'Update'
```

Порядок вызовов в одном кадре (сокращённо, по смещениям IL):

| Смещение | Вызов |
|---|---|
| `0710` | `MyGuiSandbox::HandleInput` — ввод этого кадра раздаётся экранам |
| `0728` | `MySession::HandleInput` — обработка ввода игрой |
| `07AA` | `MySession::Update` — здесь игра вызывает сессионные компоненты мода |

Само состояние клавиатуры на кадр собирается раньше, в `MyVRageInput.Update → UpdateStates`
(§3), и `IsNewKeyPressed` — это сравнение текущего состояния с предыдущим. Поэтому нажатие не
теряется, даже если бы мод прочитал его позже: `IsPreviousKeyUp` относится к **предыдущему**
кадру, а не к предыдущему читателю.

**Вывод: [ПРОВЕРЕНО, по IL]** к моменту `UpdateAfterSimulation` состояние клавиатуры на кадр уже
прочитано, поэтому опрос из сессионного компонента видит нажатие в том же кадре, в котором его
сделал игрок, и не «съедает» его у игры (мод ничего не потребляет — он только читает).

**Вывод: [ПРОВЕРЕНО, по IL]** `MySession.Update` первым делом выходит, если игра на паузе
(`MySandboxGame.IsPaused` → `ret`, смещение `007F`). Значит в одиночной игре меню паузы
останавливает и опрос клавиши — «M поверх меню паузы» там невозможно by construction, а не
потому, что мод это проверил.

**Вывод: [ПРОВЕРЕНО косвенно]** пока открыт мод-экран, симуляция не останавливается
(ADR-0029: `PausesGameInSinglePlayer = false`), значит `UpdateAfterSimulation` продолжает
вызываться и опрос не «замирает» под открытым редактором. Именно поэтому в опросе есть явная
проверка «редактор уже открыт», а не расчёт на то, что опрос под экраном не идёт.

## 5. Что осталось непроверенным

| Утверждение | Как проверить |
|---|---|
| Клавиша **M** не занята игрой по умолчанию | В игре: сесть в кокпит, не открывая ничего, нажать M — открывается редактор. Если M что-то делает в игре, это видно сразу; тогда клавиша меняется одной константой `EditorHotkey.OpenKey` |
| `ActiveGameplayScreen` действительно `null` в обычном полёте (а не держит какой-нибудь HUD-экран) | В игре: нажать M в полёте — редактор открылся. Если нет — в логе `[VGS]` нет строки `hotkey M: opening the editor`; первым подозреваемым будет эта проверка |
| **Экраны, которые пишут `ActiveGameplayScreen` не сами, а другой код**: меню паузы в мультиплеере, экраны других модов, чат в «развёрнутом» виде | Поле выставляет только `MyGuiScreenGamePlay.HandleUnhandledInput`, поэтому проверка честно означает «экран не открывался этим путём». В одиночной игре пауза закрывает вопрос сама (`MySession.Update` выходит на паузе, §4). В мультиплеере паузы нет: открыть меню паузы (Esc) при двух клиентах и нажать M. Ожидается отказ; если редактор открывается поверх — это **принятое ограничение**, а не поломка: экран мода верхний и закрывается по Esc. Записано в ADR-0045 §Consequences |
| Редактор, открытый клавишей, ведёт себя ровно как открытый кнопкой | В игре: открыть клавишей, подвигать камеру, закрыть, открыть снова — та же картина (ADR-0022); открыть клавишей с другого кокпита того же грида — тоже |
| Клавиша не срабатывает в мультиплеере на удалённом кокпите | Не проверялось: кокпит, которым игрок управляет удалённо, — всё равно `ControlledObject`, поэтому ожидается срабатывание; проверять в игре |
| Shift не нажат случайно в полёте | Модификатор отключает клавишу (ADR-0045 п.4), а Shift в игре — это ускорение/спринт. Игрок, жмущий M с зажатым Shift, получит «ничего»; это следует из решения, но ощущается как «иногда не работает» |
