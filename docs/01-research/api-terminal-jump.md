# Переход из редактора в терминал блока: проверенный контракт

Документ отвечает на один вопрос: **можно ли из полноэкранного редактора мода закрыть схему и
открыть терминал игры на выбранном блоке** — и каким именно вызовом. Все факты получены из
сборок установленной игры; команда указана рядом. Дата проверки: 2026-11-22, игра
`H:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64`.

Связанные решения: [ADR-0021](../adr/0021-jump-to-block-terminal.md) (кнопка перехода),
[ADR-0008](../adr/0008-mod-screen-api.md) (стек экрана), [ADR-0009](../adr/0009-screen-coordinate-space.md)
(координаты, не относятся к переходу, но экран тот же).

---

## 0. Короткий ответ

**Да, реализуемо, и без единого запрещённого типа.** Маршрут — публичный интерфейс
`VRage.Game.ModAPI.IMyGui` (`MyAPIGateway.Gui`), который в вайтлисте мода разрешён целиком
(пространство имён `VRage.Game.ModAPI`).

| Что нужно | Чем делается | Статус |
|---|---|---|
| Открыть терминал на блоке, когда терминал закрыт | `IMyGui.ShowTerminalPage(ControlPanel, character, block, false)` | [ПРОВЕРЕНО] IL + вайтлист |
| Переключить **уже открытый** терминал на другой блок | `IMyGui.ChangeInteractedEntity(block, false)` | [ПРОВЕРЕНО] IL |
| Понять, открыт ли терминал | `IMyGui.GetCurrentScreen != MyTerminalPageEnum.None` | [ПРОВЕРЕНО] IL |
| Закрыть экран мода после перехода | `MyModScreen.Close()` | [ПРОВЕРЕНО] IL |

Главная ловушка, из-за которой «просто `ShowTerminalPage`» не работает: **`Show` ничего не
делает, если экран терминала уже существует** — он не переключает открытый терминал на другой
блок. А редактор мода открывается именно из терминала, поэтому терминал в этот момент открыт
(просто скрыт под модальным экраном). Отсюда две ветки, и выбор между ними делается по
`GetCurrentScreen`.

---

## 1. `IMyGui` — публичный контракт

[ПРОВЕРЕНО] `pwsh -File tools/dump-se-api.ps1 -Types 'VRage.Game.ModAPI.IMyGui'`:

```csharp
// assembly VRage.Game, namespace VRage.Game.ModAPI
public String ActiveGamePlayScreen { get; }
public IMyEntity InteractedEntity { get; }
public MyTerminalPageEnum GetCurrentScreen { get; }        // свойство, не метод
public Boolean ChatEntryVisible { get; }
public Boolean IsCursorVisible { get; }

public abstract Void ChangeInteractedEntity(IMyEntity interactedEntity, Boolean isRemote);
public abstract Void CloseTerminalScreen();
public abstract Void ShowTerminalPage(MyTerminalPageEnum page, IMyCharacter user,
                                     IMyEntity interactedEntity = null, Boolean isRemote = false);
```

`GetCurrentScreen` — **свойство**. Первая редакция кода вызывала его как метод
(`gui.GetCurrentScreen()`), и это поймал `verify-compile` (`CS1955: Non-invocable member`).
Это же и есть причина, по которой такие факты дампятся, а не пишутся по памяти.

[ПРОВЕРЕНО] вайтлистом: `MyAPIGateway.Gui` — поле типа `IMyGui` в `Sandbox.ModAPI.MyAPIGateway`
(`dump-se-api.ps1 -Types 'Sandbox.ModAPI.MyAPIGateway'`), а `VRage.Game.ModAPI` разрешено моду
точным совпадением пространства имён. `tools/check-whitelist.ps1` → `WHITELIST OK` после
реализации, `-SelfTest` → `WHITELIST SELF-TEST OK — 9 cases, both rules intact`.

[ПРОВЕРЕНО] и в обратную сторону — двумя пробными компиляциями во временном каталоге (не в
репозитории):

- проба, которая зовёт `MyAPIGateway.Gui.GetCurrentScreen`, `ChangeInteractedEntity`,
  `ShowTerminalPage`, `CloseTerminalScreen`, `IMyPlayer.Character` → `COMPILE OK` и
  `WHITELIST OK`;
- проба, которая ссылается на `Sandbox.Game.Gui.MyGuiScreenTerminal` → `WHITELIST FAILED —
  4 prohibited reference(s)`, в том числе `namespace 'Sandbox.Game.Gui' is not whitelisted for
  mods`.

То есть обойти `IMyGui` и дотянуться до экрана терминала напрямую нельзя: мод просто не
загрузится. Это и есть причина, по которой переход реализован через интерфейс, хотя
`MyGuiScreenTerminal.SwitchToControlPanelBlock` сделал бы то же самое точнее.

Правило «пространство имён точно» подтверждается IL самой игры:
`VRage.Scripting.TypeKeyExtensions.GetWhitelistKey` строит ключ как
`ToDisplayString() + ".*, " + assembly`, а `MyScriptWhitelist.IsWhitelisted` сначала проверяет
пространство имён типа и при попадании разрешает **любой** его член — поэтому `VRage.Game.ModAPI`
целиком доступно, а `Sandbox.Game.Gui` — нет.

Единственная реализация интерфейса в сборках игры — `Sandbox.ModAPI.MyGuiModHelpers`
(найдено перебором `GetTypes()` по всем `Bin64/*.dll` на `IsAssignableFrom(IMyGui)`).

[ПРОВЕРЕНО] `MyAPIGateway.Gui` — **поле**, а не свойство (`dump-se-api.ps1` печатает его в
разделе Fields; рефлексией: `IMyGui Gui static=True initonly=False`), инициализируется в
`Sandbox.ModAPI.MyModAPIHelper.Initialize` (`newobj MyGuiModHelpers; stsfld Gui`).

[ПРОВЕРЕНО] `MyAPIGateway.Clean` обнуляет `Session`, `Entities`, `Players`, `Input`,
`Utilities`, `TerminalControls` и ещё десяток полей, но **`Gui` в этом списке нет**. Значит после
выхода из мира ссылка может остаться непустой и «протухшей»: проверка на `null` нужна, но она не
доказывает, что сессия жива. Поэтому в `TerminalJump` перед вызовом стоят ещё две проверки —
`VgsSession.HasLocalPlayer` и наличие персонажа у локального игрока.

## 2. Что делает `ShowTerminalPage`

[ПРОВЕРЕНО] `pwsh -File tools/dump-se-il.ps1 -Type 'Sandbox.ModAPI.MyGuiModHelpers' -Method 'ShowTerminalPage'`:

```il
ldarg.1                        ; page
ldarg.2                        ; user
castclass Sandbox.Game.Entities.Character.MyCharacter
ldarg.3                        ; interactedEntity
castclass VRage.Game.Entity.MyEntity
ldarg.s 4                      ; isRemote
ldc.i4.0
call Sandbox.Game.Gui.MyGuiScreenTerminal::Show
ret
```

То есть это тонкая обёртка над `MyGuiScreenTerminal.Show(page, user, entity, isRemote,
remoteAccessMenu: false)`. Никакого переключения уже открытого терминала здесь нет.

[ПРОВЕРЕНО] `dump-se-il.ps1 -Type 'Sandbox.Game.Gui.MyGuiScreenTerminal' -Method 'Show'` —
начало метода:

```il
0000: ldsfld   TerminalEnabled
0005: brfalse.s   -> ret
0007: ldsflda  MyConfig.EnableTerminalScreen
0011: brtrue.s    -> continue
0013: ret                       ; экран отключён конфигом
0014: ldsfld   m_instance
0019: brfalse.s   -> newobj MyGuiScreenTerminal
001B: ret                       ; <-- ТЕРМИНАЛ УЖЕ ЕСТЬ: выход, ничего не делая
001C: newobj   Sandbox.Game.Gui.MyGuiScreenTerminal
```

Дальше (когда экрана нет) метод создаёт его и:

```il
stfld  m_user
stsfld IsRemote
stsfld m_openInventoryInteractedEntity
; if (ENABLE_TERMINAL_PROPERTIES) m_initialPage = isRemote ? -1 : page;
ldarg.2  -> MyGuiScreenTerminal::set_InteractedEntity
ldc.i4.1 -> MyGuiScreenBase::RecreateControls(true)
stsfld  MyGuiScreenGamePlay::ActiveGameplayScreen
call    MyGuiSandbox::AddScreen
call    MyGuiScreenTerminal::set_IsOpen(true)
```

Следствие: `Show` годится ровно для одного случая — терминала нет. Для «терминал есть,
покажи другой блок» нужен второй вызов.

## 3. Что делает `ChangeInteractedEntity`

[ПРОВЕРЕНО] `dump-se-il.ps1 -Type 'Sandbox.ModAPI.MyGuiModHelpers' -Method 'ChangeInteractedEntity'`:

```il
ldarg.1  castclass VRage.Game.Entity.MyEntity
ldarg.2  call Sandbox.Game.Gui.MyGuiScreenTerminal::ChangeInteractedEntity
```

[ПРОВЕРЕНО] `dump-se-il.ps1 -Type 'Sandbox.Game.Gui.MyGuiScreenTerminal' -Method 'ChangeInteractedEntity'`:

```il
ldarg.1  stsfld IsRemote
ldarg.0  call set_InteractedEntity
```

[ПРОВЕРЕНО] `dump-se-il.ps1 -Type 'Sandbox.Game.Gui.MyGuiScreenTerminal' -Method 'set_InteractedEntity'`:

```il
ldsfld m_interactedEntity
brfalse  -> пропустить отписку
...      remove_OnClose у старой сущности
m_instance?.m_controllerControlPanel?.ClearBlockList()
stsfld m_interactedEntity = value
...      add_OnClose у новой сущности
ldsfld m_interactedEntity
ldsfld m_openInventoryInteractedEntity
beq      -> если тот же блок, страница НЕ переставляется
m_instance.m_initialPage = 1              ; MyTerminalPageEnum.ControlPanel
call get_IsOpen
brfalse  -> экран закрыт, дальше ничего
m_instance.RecreateTabs()
```

Два вывода, и оба важны:

1. **Смена блока пересоздаёт вкладки** (`RecreateTabs`) — то есть уже открытый и даже скрытый
   терминал переезжает на новый блок. Именно это и нужно переходу.
2. **Стартовая страница сбрасывается на Control Panel только при смене блока.** Сравнение идёт
   не с предыдущим значением, а с `m_openInventoryInteractedEntity` — блоком, с которым терминал
   **открывали** (пишется один раз в `Show`, обнуляется только в `OnClosed`). Переход на тот самый
   блок оставляет `m_initialPage` таким, каким его поставил `Show`.

[ПРОВЕРЕНО] полным сканом записей в `m_initialPage`: писателей ровно три — `Show` (дважды, ветка
`ENABLE_TERMINAL_PROPERTIES`), `set_InteractedEntity` (одно место) и `set_IsOpen` (сброс в
`OnClosed`). `tabs_OnPageChanged` его **не** обновляет, поэтому «запомнить вкладку игрока» этот
механизм не умеет.

[ПРОВЕРЕНО] чем игра открывает терминал в разных путях: `MyCockpit::ShowTerminal`,
`MyLargeTurretBase::ShowTerminal`, `MyUseObjectTerminal::Use` передают `page = 1`
(`ControlPanel`); `MyCockpit::ShowInventory` и `MyUseObjectInventory::Use` — `page = 0`
(`Inventory`).

Следствие для перехода: редактор открывается кнопкой «Grid setup» в терминале кокпита, а такой
терминал игра открыла с `page = 1`. Значит и при смене блока, и при переходе на сам кокпит
стартовая страница — Control Panel. Inventory остаётся только в случае «терминал открыт
клавишей инвентаря на этом же блоке», то есть в сценарии, в котором редактор не открывают.

Форсировать вкладку через `IMyGui` нельзя: сеттера вкладок в интерфейсе нет, а
`MyGuiScreenTerminal.SwitchToControlPanelBlock` запрещён вайтлистом (см. §1). Разрешённый обход —
`CloseTerminalScreen()`, дождаться `GetCurrentScreen == None` (закрытие отложенное, §5) и
`ShowTerminalPage(ControlPanel, …)` — требует конечного автомата на несколько кадров ради случая,
который игрок исправляет одним кликом. В ADR-0021 это записано ограничением, а не обходом.

## 4. Как блок попадает в панель управления

[ПРОВЕРЕНО] `RecreateTabs` → `CreateTabs`; в конце `CreateTabs`:

```il
m_controllerControlPanel = new MyTerminalControlPanel()   ; если ещё нет
m_controllerControlPanel.SetTerminalScreen(this)
...
get_InteractedEntity() isinst MyTerminalBlock -> currentBlock
m_controllerControlPanel.Init(tabPage, MySession.Static.LocalHumanPlayer, grid, currentBlock, m_colorHelper)
m_terminalTabs.set_SelectedPage(m_initialPage)
```

[ПРОВЕРЕНО] `dump-se-il.ps1 -Type 'Sandbox.Game.Gui.MyTerminalControlPanel' -Method 'Init'`:

```il
stfld m_controlsParent / m_controller / m_colorHelper
ldarg.3  brtrue  -> блок есть, идём дальше
...      блок null: показать "ShipNotConnected" и выйти
set_TerminalSystem(grid.GridSystems.TerminalSystem)
new MyBlockGroup() -> m_tmpGroup
...
ldarg.4  stfld m_originalBlock
ldsfld SelectedEntity isinst MyTerminalBlock
RefreshBlockList(new[]{ SelectedEntity ?? m_originalBlock })
stsfld SelectedEntity = null
```

[ПРОВЕРЕНО] `RefreshBlockList` → `ClearBlockList` + `PopulateBlockList`, а
`PopulateBlockList` в конце вызывает `SelectBlocks(selectedBlocks)`; `SelectBlocks` кладёт блок в
`CurrentBlocks`, затем `SelectBlocks()` создаёт `MyGuiControlGenericFunctionalBlock` — то есть
панель показывает свойства именно этого блока, а в списке он выделен.

Значит после `ChangeInteractedEntity(block, false)` открытый терминал стоит на вкладке Control
Panel и показывает нужный блок.

## 5. Порядок «переключить, потом закрыть» и почему он обязателен

[ПРОВЕРЕНО] `dump-se-il.ps1 -Type 'Sandbox.ModAPI.MyModScreen' -Method 'Close'`:

```il
m_host.CloseScreen(false)
```

[ПРОВЕРЕНО] `dump-se-il.ps1 -Type 'Sandbox.Graphics.GUI.MyGuiScreenBase' -Assembly 'Sandbox.Graphics.dll' -Method 'CloseScreen'`:

```il
if (State == 2 || State == 3) return false;   ; уже закрывается/закрыт
State = 2                                      ; Closing — начинается переход
m_lastTransitionTime = TotalTimeInMilliseconds
return true
```

То есть закрытие редактора **не мгновенное**: экран уходит переходом, и в этот момент он ещё
на экране. Поэтому терминал переключается первым: к моменту, когда экран мода растает,
терминал уже показывает нужный блок.

[ПРОВЕРЕНО] `dump-se-il.ps1 -Type 'Sandbox.Graphics.GUI.MyScreenManager' -Assembly 'Sandbox.Graphics.dll' -Method 'AddScreens'`:
экран с `IsTopMostScreen == true` добавляется в конец списка, а предыдущий экран при
`CanHideOthers` (по умолчанию `true`, см. `.ctor` `MyGuiScreenBase`) **скрывается**
(`HideScreen`), а не закрывается. `MyGuiScreenTerminal.Hide()` — это `CloseScreen(false)`, но
именно `MyScreenManager` скрывает его как предыдущий экран, не вызывая `OnClosed`.

[ПРОВЕРЕНО] `get_IsOpen` сбрасывается только в `OnClosed` (`ldnull stsfld m_instance`;
`set_IsOpen(false)`), а `HideScreen`/`UnhideScreen` — это переходы состояния
`HIDING → HIDDEN` и `UNHIDING → OPENED`, завершаемые в `UpdateTransition`. То есть скрытие не
закрывает экран и не сбрасывает `IsOpen`.

Отсюда: пока открыт редактор, `MyGuiScreenTerminal.IsOpen` остаётся `true`, поэтому
`GetCurrentScreen` возвращает реальную страницу, а не `None`. Это и есть нормальный случай:
редактор открыт из терминала кокпита, терминал под ним.

**Скрытость не мешает пересозданию вкладок.** [ПРОВЕРЕНО] `RecreateTabs` работает только с
деревом контролов (`RemoveControlByName("TerminalTabs")` → `CreateTabs` → `CreateProperties`) и
нигде не обращается к `Visible`/`State`; `CreateTabs` при отсутствии страницы создаёт её
(`GetTabSubControl` → `new MyGuiControlTabPage`). `RecreateControls(bool)` при этом не
вызывается, поэтому фиксированные элементы и `m_properties*Parent` (создаются в
`CreateFixedTerminalElements`) сохраняются.

**Два остаточных риска, видимых в IL.** `tabs_OnPageChanged` разыменовывает
`m_propertiesTableParent` без проверки на null (а `CreateProperties` проверяет) — в нормальном
пути он непустой, потому что создаётся в `Show → RecreateControls`. И `MyScreenManager.CheckThread`
**не бросает**, а только пишет `MyLog` `Error("Thread unsafe access to GUI screens: …")`: вызов
не с update-потока не упадёт, а тихо испортит состояние. Поэтому переход вызывается только из
обработчика ввода экрана, то есть с update-потока, и целиком обёрнут в `try/catch`.

## 6. Как отличить «терминала нет»

[ПРОВЕРЕНО] `dump-se-il.ps1 -Type 'Sandbox.Game.Gui.MyGuiScreenTerminal' -Method 'GetCurrentScreen'`:

```il
call get_IsOpen
brfalse -> ldc.i4.s 254 ; ret
m_instance.m_terminalTabs.get_SelectedPage
```

`ldc.i4.s` расширяет знак 8-битного операнда, поэтому `0xFE` — это `-2`, то есть
`MyTerminalPageEnum.None`. Значения перечисления [ПРОВЕРЕНО] рефлексией:

```
Inventory = 0, ControlPanel = 1, Production = 2, Info = 3,
Factions = 4, Comms = 5, Gps = 6, None = -2, Properties = -1
```

Проверка в коде поэтому — сравнение с `None`, а не «больше нуля» и не «не `Properties`».

## 7. Что терминал перепроверяет каждый кадр (дистанция и антенна)

Этот раздел появился после дефекта в игре: переход работал у всех блоков, кроме одного, и на нём
терминал показывал «Ship has been disconnected from terminal» без вкладок, а после Esc игрок
оказывался в кокпите. Причина — не в нашем вызове, а в собственной проверке игры.

[ПРОВЕРЕНО] `dump-se-il.ps1 -Type 'Sandbox.Game.Gui.MyGuiScreenTerminal' -Method 'Update'`:

```il
if (m_connected) {
    if (m_terminalTabs.SelectedPage != -1 && !m_controllerProperties.TestConnection()) {
        m_connected = false;  ShowDisconnectScreen();
    }
} else {
    if (m_controllerProperties.TestConnection()) { m_connected = true; ShowConnectScreen(); }
}
```

`ShowDisconnectScreen` [ПРОВЕРЕНО] прячет `m_terminalTabs` и показывает `m_terminalNotConnected` —
то есть ровно «нет вкладок, вместо них сообщение». Текст берётся из ресурса
`ScreenTerminalError_ShipHasBeenDisconnected` = «Ship has been disconnected from terminal»
(`Content\Data\Localization\MyTexts.resx`); не путать с `ScreenTerminalError_ShipNotConnected`
(«Terminal is not connected to any ship») — это другая ветка.

[ПРОВЕРЕНО] `dump-se-il.ps1 -Type 'Sandbox.Game.Screens.Terminal.MyTerminalPropertiesController'
-Method 'TestConnection'` — решает две ветки:

```il
if (m_openInventoryInteractedEntityRepresentative.EntityId != m_interactedEntityRepresentative.EntityId)
    goto antenna;                       // разные гриды -> только через антенну
if (m_isRemote) goto antenna;
character = MySession.Static.LocalCharacter;
if (m_interactedEntity == null || character == null) goto antenna;
distanceSq = BoundingBoxD.DistanceSquared(m_interactedEntity.PositionComp.WorldAABB, character.position);
return distanceSq < DEFAULT_INTERACTIVE_DISTANCE * DEFAULT_INTERACTIVE_DISTANCE;   // локальная ветка
antenna:
return !(m_interactedEntityRepresentative is MyCubeGrid) ||
       GetShipStatus(rep) == Connected;                                             // ветка антенны
```

Следствия, которые надо знать при вызове `ChangeInteractedEntity`:

| Факт | Значение |
|---|---|
| `DEFAULT_INTERACTIVE_DISTANCE` | **5.0** — `VRage.Game.MyConstants`, поле `public static Single` [ПРОВЕРЕНО] рефлексией. Это та же дистанция, на которой блок можно открыть клавишей F |
| Что меряется | `BoundingBoxD.DistanceSquared(WorldAABB блока, позиция персонажа)`, не центр блока |
| `isRemote = false` | Дистанционная проверка. Блок дальше 5 м от персонажа → разрыв на следующем кадре, **даже если переход только что сработал** |
| `isRemote = true` | Дистанция не важна, но нужен работающий передатчик: `GetShipStatus` возвращает `Connected` только если у грида (логической группы) есть `MyDataBroadcaster`, проходящий `CheckConnection` и `CanBeUsedByPlayer` |
| `GetShipStatus` значения | `PhysicallyConnected=0, Connected=1, OutOfBroadcastingRange=2, OutOfReceivingRange=3, Me=4, IsPreviewGrid=5` |
| Что делает `isRemote` ещё | Ничего: [ПРОВЕРЕНО] скан всех читателей/писателей `MyGuiScreenTerminal::IsRemote` — пишут `Show` и `ChangeInteractedEntity`, читает только `CreateProperties`, передавая в `Init` контроллера свойств; в контроллере `m_isRemote` читает только `TestConnection` |
| Флаг действительно доходит | [ПРОВЕРЕНО] `ChangeInteractedEntity` = `ldarg.1; stsfld IsRemote; set_InteractedEntity(entity)`, то есть пишет **аргумент**, а не константу, и `set_InteractedEntity` его не сбрасывает. `ShowTerminalPage(page, user, entity, isRemote)` = `Show(page, user, entity, isRemote, remoteAccessMenu: false)`, и `Show` тоже делает `stsfld IsRemote` из своего аргумента |
| Репрезентатив грида | [ПРОВЕРЕНО] `GetLogicalGroupRepresentative(grid)` = `AntennaSystem.Logical.GetGroup(grid)`, затем грид из группы с **наибольшим числом блоков** (`GetBlocks().Count`); если группы нет — сам грид. Поэтому у подгрида (поршень/ротор) репрезентатив тот же, что у основного грида, и сравнение на равенство проходит: ветка выбирается по дистанции и `isRemote`, а не по «свой/чужой грид». Ветка антенны включается, только когда блок терминала и цель — из **разных** логических групп (например, два отдельных корабля); схема редактора одного грида такого не даёт |

Отсюда правило, реализованное в `TerminalJump`: флаг считается по измеренной дистанции
(локально в пределах 5 м, иначе удалённо), а удалённый режим требует передающей антенны — без неё
переход отказывает заранее, потому что иначе игрок получит тот же экран разрыва.

Проверка антенны [ПРОВЕРЕНО] рефлексией `Sandbox.ModAPI.Ingame.IMyRadioAntenna`: свойство
`IsBroadcasting` реализовано в `MyRadioAntenna` как «есть `RadioBroadcaster` и он включён», то есть
уже включает `Enabled`, `IsWorking` и `EnableBroadcasting`. Доступ к блокам —
`IMyCubeGrid.GetFatBlocks<T>()`; логическая группа — `GetGridGroup(GridLinkTypeEnum.Logical)` и
`IMyGridGroupData.GetGrids<T>(T)`.

## 8. Чего в этом маршруте нет и что осталось за проверкой в игре

| Что | Почему |
|---|---|
| Типы `Sandbox.Graphics.*`, `MyGuiScreenTerminal`, `MyScreenManager` напрямую | Запрещены вайтлистом (ADR-0008); используются только через `IMyGui`. [ПРОВЕРЕНО] пробой: ссылка на `Sandbox.Game.Gui.MyGuiScreenTerminal` даёт `WHITELIST FAILED — namespace 'Sandbox.Game.Gui' is not whitelisted for mods` |
| Гарантия «всегда Control Panel» | [ОПРОВЕРГНУТО] как гарантия: страница зависит от того, с каким блоком терминал открывали (§3). В нормальном пути редактора (кнопка в терминале кокпита, `page = 1`) получается Control Panel; Inventory остаётся только для «открыт клавишей инвентаря на том же блоке» |
| Гарантия, что терминал **не** перехватит фокус до закрытия редактора | `RecreateTabs` работает на скрытом экране, ввод получает только фокусный экран; поведение проверяется человеком в игре |
| Поведение на выделенном сервере | GUI нет вовсе; `VgsSession.HasLocalPlayer` отсекает вызов раньше |
| Поведение на удалённом клиенте мультиплеера | `ChangeInteractedEntity` — локальный UI-вызов, но флаг `isRemote` теперь вычисляется по дистанции; на клиенте без антенны переход откажет. Проверяется человеком |
| Переход на блок, у которого нет вкладки Control Panel | У любого `IMyTerminalBlock` она есть: `Init` вызывается для любого `MyTerminalBlock`, а целевой блок всегда добавляется в список (`AddBlockToList` с `visible: true`) и выделяется (`SelectBlocks`) |
| Требование update-потока | `MyScreenManager.CheckThread` только логирует ошибку, не бросает: вызов не с того потока испортит GUI молча. Переход поэтому вызывается только из обработчика ввода экрана |
| Точное совпадение с `CanBeUsedByPlayer` | Наша проверка антенны смотрит `IsBroadcasting`, но не отношения владельца и игрока. Антенна, передающая для чужой фракции, пройдёт нашу проверку и упрётся в `GetShipStatus`; тогда игрок увидит сообщение игры. Сужение до `CanBeUsedByPlayer` потребовало бы повторить логику фракций |

**[НЕ ПРОВЕРЕНО]** поведение в игре: что после нажатия кнопки редактор закрывается, терминал
показывает именно этот блок, вкладка — Control Panel, что скрытый терминал не «мигает» при
пересоздании вкладок и что переход на далёкий блок в кокпите больше не даёт экран разрыва. Это
пункты приёмки ADR-0021, их делает человек.
