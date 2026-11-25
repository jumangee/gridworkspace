# 0039. Действие показывается, только если игра показывает его контрол этому блоку

- Status: accepted
- Date: 2026-11-25
- Supersedes: ADR-0038 §2 (фильтр по `InvalidCastException` не сработал)
- Joint review: —

## Context

ADR-0038 добавил фильтр «чужое действие бросает `InvalidCastException`». Проверка в игре
показала, что он не срабатывает: у `LargeHinge` по-прежнему 48 действий, среди них
`Increase Font Size`, `Decrease Font Size`, `Increase Text Padding`. Отсюда вывод: эти действия
зарегистрированы не на «чужой» тип, а на **базовый класс самого шарнира**, поэтому приведение
типа проходит.

Причина найдена в IL установленной игры. `MyFunctionalBlock.CreateTerminalControls` в конце
регистрирует контролы компонента панели текста **для всего типа**:

```powershell
& .\tools\dump-se-il.ps1 -Assembly 'Sandbox.Game.dll' `
    -Type 'Sandbox.Game.Entities.Cube.MyFunctionalBlock' -Method 'CreateTerminalControls'
```

```il
00A1: ldc.i4.1
00A2: newobj       MyTerminalControlSeparator`1<MyFunctionalBlock>
00A7: call         MyTerminalControlFactory::AddControl
00AC: call         MyMultiTextPanelComponent::CreateTerminalControls     ; <T> = MyFunctionalBlock
00B1: ldarg.0
00B2: ldfld        m_createControlsForComponents
...
```

То есть у **каждого** функционального блока в списке действий есть действия панели текста —
их скрывает не регистрация, а колбэк `Visible` каждого контрола: «на этом блоке компонента
панели нет → контрол не показывать». Панель терминала этот колбэк спрашивает, а список
действий — нет:

1. `IMyTerminalBlock.GetActions` → `MyTerminalControlFactory.GetActions(type)` → список типа
   без всякой проверки видимости [ПРОВЕРЕНО по IL, ADR-0038 §1];
2. `MyTerminalBlock.GetProperties` → `MyTerminalControlFactory.GetValueControls(type)` — тоже
   список типа;
3. `MyToolbarItemTerminalBlock.GetActions` (то, из чего берёт список ванильный тулбар) —
   `MyTerminalControls.GetActions(m_block)`, то есть тот же список типа, отфильтрованный только
   по `IsEnabled` и `IsValidForToolbarType`:

   ```powershell
   & .\tools\dump-se-il.ps1 -Assembly 'Sandbox.Game.dll' `
       -Type 'Sandbox.Game.Screens.Helpers.MyToolbarItemTerminalBlock' -Method 'GetActions'
   & .\tools\dump-se-il.ps1 -Assembly 'Sandbox.Game.dll' `
       -Type 'Sandbox.Game.Screens.Helpers.MyToolbarItemTerminalBlock' -Method 'AddActions'
   ```

   `IsEnabled` для этих действий возвращает `true` (проверено в игре по ADR-0038), поэтому
   ванильный список тоже содержит их у шарнира. Это дефект игры, а не только наша ошибка
   чтения: терминал показывает шарнир без слайдера шрифта, а тулбар предлагает шарниру шрифт.

4. Видимость контрола доступна моду: `IMyTerminalControl.Visible` — это
   `Func<IMyTerminalBlock, bool>`, тот самый делегат, которым панель терминала решает, рисовать
   ли контрол. Объекты контролов приходят из `IMyTerminalBlock.GetProperties(List<ITerminalProperty>)`:
   каждый элемент — value-контрол (слайдер, чекбокс, комбобокс), который реализует и
   `ITerminalProperty`, и `IMyTerminalControl`
   [ПРОВЕРЕНО: `Sandbox.ModAPI.Interfaces.Terminal.IMyTerminalControlProperty`1 : IMyTerminalControl, ITerminalProperty`].

5. Id действия выводится из id контрола, и это проверено по IL: `EnableAction` даёт id
   контрола, `EnableOnOffActions` — `control.Id + "_On"` / `"_Off"`, `EnableActions` —
   `"Increase"` / `"Decrease"` + `control.Id`:

   ```powershell
   & .\tools\dump-se-il.ps1 -Assembly 'Sandbox.Game.dll' `
       -Type 'Sandbox.Game.Gui.MyTerminalControlExtensions' -Method 'EnableActions'
   ```

   Значит «какому контролу принадлежит действие» решается без таблицы имён действий.

## Decision

Действие попадает в порты, только если игра **показывает его контрол этому блоку**.

1. При чтении списка действий типа (`GridGraphBuilder.BuildActionSet`, один раз на тип)
   собирается набор id контролов, которые для этого блока скрыты: у каждого значения из
   `block.GetProperties(...)`, приведённого к `IMyTerminalControl`, вызывается его собственный
   `Visible(block)`; `false` — контрол скрыт.
2. Действие отбрасывается, если его id принадлежит скрытому контролу:
   `BlockPortCatalog.ActionBelongsToControl` — точное совпадение, `<id>_On` / `<id>_Off`,
   `Increase<id>` / `Decrease<id>`. Никаких списков имён действий и никаких признаков вида
   «есть ли у блока дисплей».
3. Контрол, который не смог ответить (нет делегата или он бросил), считается **видимым**:
   спрятать действие из-за сбоя колбэка — значит отнять у игрока реальное действие.
4. Повторяющийся id по-прежнему отбрасывается (ADR-0038 §1 сохраняется: на LCD каждое действие
   приходило дважды). Фильтр по `InvalidCastException` (ADR-0038 §2) остаётся как дешёвая
   страховка от действительно чужого действия, но он **не является** решением задачи.

## Alternatives

- **Список «действий LCD» и проверка «есть ли у блока дисплей».** Отклонено как хардкод: это
  правда об одном конкретном компоненте, а не правило; следующий мод с компонентными
  контролами сломал бы его, а LCD-панель со своими настоящими действиями пришлось бы
  перечислять отдельно.
- **`IsEnabled == false` как признак.** Отклонено: `false` — выключенный контрол, законное
  состояние, которое игрок видит и переключает (ADR-0038, тот же довод).
- **Список действий по подтипам блоков.** Отклонено: возвращает курируемую таблицу вместо
  данных игры и не работает для блоков других модов.
- **Сверять с контролами типа через `MyAPIGateway.TerminalControls.GetControls<TBlock>`.**
  Отклонено: метод обобщён по типу, известному на этапе компиляции, а во время выполнения тип
  блока доступен только как `System.Type`; рефлексия над обобщёнными методами вайтлистом не
  разрешена (`System.Reflection.MethodInfo` в нём нет). `GetProperties` даёт те же объекты без
  рефлексии — этого достаточно, потому что все «шумные» действия приходят от value-контролов.
- **Читать контролы через `CustomControlGetter`, когда игрок откроет терминал блока.**
  Отклонено: результат зависел бы от того, открывал ли игрок терминал этого блока, то есть
  список в редакторе менялся бы от постороннего действия.

## Consequences

- Легко: список в редакторе совпадает с тем, что игрок видит в терминале блока; шарнир больше
  не предлагает шрифт, LCD не показывает действия дважды, а модовые компонентные контролы
  обрабатываются тем же правилом без правок кода.
- Трудно: правило опирается на соглашение об id (`Increase`/`Decrease`/`_On`/`_Off`). Оно
  проверено по IL и покрыто офлайн-проверками, но если игра сменит схему имён, действия
  перестанут скрываться (лишние, а не пропавшие) — это безопасное направление отказа.
- Запрещено: перечислять «чужие» действия по именам; прятать действие по `IsEnabled == false`;
  прятать действие, когда контрол не смог ответить.
- Если окажется, что видимость контрола у игры зависит от состояния блока, а не от его состава,
  кэш на тип (ADR-0038 §1) станет источником расхождения: список будет снят с первого блока
  типа. Тогда фильтр придётся считать на блок, а кэш — только на «список типа без видимости»;
  это отдельное изменение и отдельный ADR.

## Verification

Офлайн (уже в шлюзе):

```powershell
pwsh -File tools/offline-check.ps1   # «action ids name their controls (ADR-0039)»:
                                     # _On/_Off, Increase/Decrease, отказы на чужих парах
pwsh -File tools/verify-compile.ps1
pwsh -File tools/check-whitelist.ps1 # GetProperties и IMyTerminalControl.Visible разрешены
pwsh -File tools/review-check.ps1
```

В игре (человек), тот же мир:

1. `LargeHinge`: в меню нет `Increase Font Size`, `Decrease Font Size`, `Increase Text
   Padding`, `Decrease Text Padding`, `Increase Image change interval`,
   `Preserve Aspect Ratio`; `Increase Safety Detach`, `Increase Torque`, `Detach`, `Reverse`,
   `Hinge Lock On/Off` на месте.
2. `LargeDiagonalLCDPanel`: каждое действие по одному разу, действия шрифта и картинок на месте.
3. В терминале шарнира (ванильный UI) нет контролов шрифта — список редактора должен совпадать
   с терминалом.
4. В логе при первом чтении типа — строки `skipping action '…' — its control is not shown on …`
   с этими id.