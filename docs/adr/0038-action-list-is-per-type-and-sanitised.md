# 0038. Список действий читается один раз на тип блока и чистится от чужих действий

- Status: accepted, §2 superseded by 0039
- Date: 2026-11-25
- Supersedes: — (уточняет ADR-0037, §4a ADR-0002)
- Joint review: —

## Context

После ADR-0037 подписи стали правильными, но пользователь увидел другое: у `LargeHinge`
**48** действий, среди них `Increase Font Size`, `Increase Text Padding`, `Increase Image
change interval`, `Preserve Aspect Ratio` — действия панели текста, которых у шарнира быть не
может. Тот же список у LCD содержит эти строки **дважды**. Лог меню (ADR-0037) дал точные
данные:

```
action menu for LargeHinge (80404546735036497): 48 action(s): OnOff, OnOff_On, OnOff_Off,
ShowOnHUD, ShowOnHUD_On, ShowOnHUD_Off, IncreaseFontSize, DecreaseFontSize,
IncreaseTextPaddingSlider, DecreaseTextPaddingSlider, IncreaseChangeIntervalSlider,
DecreaseChangeIntervalSlider, PreserveAspectRatio, IncreaseSafetyDetach, …
action menu for LargeDiagonalLCDPanel (95091124237273612): 22 action(s): OnOff, …, ShowOnHUD_Off,
IncreaseFontSize, …, PreserveAspectRatio, IncreaseRotate, DecreaseRotate,
IncreaseFontSize, …, PreserveAspectRatio
```

Проверено по сборкам установленной игры:

1. `IMyTerminalBlock.GetActions` **не смотрит на блок** — он берёт список действий по
   *типу*: `MyTerminalBlock.<Ingame.GetActions>` вызывает
   `MyTerminalControlFactoryHelper.Static.GetActions(block.GetType(), …)`, а тот —
   `MyTerminalControlFactory.GetActions(type, m_actionList)`:

   ```powershell
   & .\tools\dump-se-il.ps1 -Assembly 'Sandbox.Game.dll' `
       -Type 'Sandbox.Game.Entities.Cube.MyTerminalBlock' -Method '*Action*'
   & .\tools\dump-se-il.ps1 -Assembly 'Sandbox.Game.dll' `
       -Type 'Sandbox.ModAPI.MyTerminalControlFactoryHelper' -Method '*Actions*'
   ```

   Следствие: список — свойство **типа**, а не блока. Читать его на каждый блок на каждом
   структурном проходе незачем: до `VgsConstants.MaxGridBlocksScanned` блоков делят несколько
   типов.

2. Список типа **не гарантированно принадлежит этому типу**. Панель текста регистрирует свои
   контролы через компонент (`MyMultiTextPanelComponent.CreateTerminalControls`, контролы
   `Font`, `FontSize`, `FontColor`, `alignment`, `TextPaddingSlider`, `ChangeIntervalSlider`,
   `PreserveAspectRatio`, …), и в списке типа они оказываются и там, где компонента нет.
   Точный порядок регистрации в IL не восстановлен — это **[НЕ ПРОВЕРЕНО]**, и именно поэтому
   ниже чистится *результат*, а не предотвращается причина: предотвратить её мод не может,
   `MyTerminalControlFactory` ему недоступен (namespace `Sandbox.Game.Gui` вайтлистом запрещён).

3. Есть признак, отличающий «чужое действие» от «своё, но выключенное»:
   `MyTerminalAction<TBlock>.IsEnabled` приводит блок к типу, для которого действие
   зарегистрировано (`unbox.any TBlock`), и только потом спрашивает у контрола:

   ```powershell
   & .\tools\dump-se-il.ps1 -Assembly 'Sandbox.Game.dll' `
       -Type 'Sandbox.Game.Gui.MyTerminalAction`1' -Method 'IsEnabled'
   ```

   Значит для чужого действия вызов **бросает** `InvalidCastException`, а для своего —
   честно отвечает `true`/`false`. `false` при этом означает «контрол выключен», и такой
   вариант трогать нельзя: игрок видит выключенный контрол и может его включить.

4. Дубликат в списке LCD (одни и те же id дважды) — тоже наблюдение из лога, а не теории:
   в тулбар одно и то же действие на одну страницу игра всё равно дважды не положит
   (`MyGuiScreenToolbarConfigBase.DropGridItemToToolbar` удаляет дубликаты страницы,
   `docs/01-research/api-toolbar.md` §5), так что второй такой порт — обещание, которое мод
   не выполнит.

## Decision

1. **Список действий читается один раз на тип блока и кэшируется** в `Vgs.Game`
   (`GridGraphBuilder.ActionSet`, `Dictionary<Type, ActionSet>`): id и подпись (ADR-0037)
   рядом. Действия регистрируются игрой при загрузке, поэтому в пределах сессии список типа не
   меняется; меняется только признак «включено», и он по-прежнему спрашивается у **блока**
   (правило группы, ADR-0015).
2. **Действие, которое нельзя применить к этому блоку, в список не попадает.** Признак —
   `InvalidCastException` из `ITerminalAction.IsEnabled(block)` (факт 3). Любой другой отказ
   `IsEnabled` действию не вредит: «не смог ответить» — не «чужое», и такой порт остаётся,
   а в лог уходит строка `action-foreign` / `action-enabled`.
3. **Один id — один порт.** Дубликат отбрасывается при чтении, до построения портов и до
   правила группы: две строки с одним id — одно действие.
4. Всё это делается в `Vgs.Game`, до `FillActions`: модель по-прежнему получает готовый
   список id и подписей и ничего не знает про `ITerminalAction`.

## Alternatives

- **Блэклист известных «чужих» id (`IncreaseFontSize`, `PreserveAspectRatio`, …).** Отклонено:
  это список из одного наблюдения, он устареет на первом же блоке другого мода и уже сегодня
  неверен для LCD, у которого эти действия настоящие.
- **Фильтровать по `IsEnabled == false`.** Отклонено: `false` — это выключенный контрол,
  законное состояние, которое игрок видит и может переключить. Фильтр по нему убрал бы,
  например, `Detach` у непристыкованного шарнира и заставил бы список меняться от состояния
  блока.
- **Отказаться от `GetActions` и вести таблицу действий по подтипам.** Отклонено: это
  возвращает проект к курируемой таблице вместо «реального списка из игры» — ровно то, от чего
  он ушёл, и то, что не работает для блоков других модов.
- **Читать список на каждый блок, без кэша.** Отклонено: это один вызов игры на блок на
  каждый структурный проход (до 2000 блоков), при том что список — свойство типа. Кэш ещё и
  делает фильтрацию одноразовой: чужие id отбрасываются один раз на тип.
- **Пытаться не дать игре испортить список (порядок инициализации).** Отклонено: нужный
  `MyTerminalControlFactory` моду запрещён, а полагаться на порядок вызовов игры — значит
  зависеть от того, что игрок открыл первым.

## Consequences

- Легко: у блока видно только его действия; LCD больше не показывает каждое действие дважды;
  структурный проход перестаёт дёргать `GetActions` на каждый блок.
- Трудно: кэш живёт до конца сессии. Мод, который добавит действия **после** первого
  обращения к типу, в редакторе не появится; действия, зарегистрированные при загрузке (все
  ванильные и обычные модовые), появляются как раньше.
- Запрещено: считать `GetActions` списком конкретного блока; строить порт из повторяющегося id;
  фильтровать действия по `IsEnabled == false`.
- Если окажется, что чужие действия не бросают `InvalidCastException` (зарегистрированы на
  базовом классе), кэш и дедупликация остаются, а фильтр придётся заменить: список id придётся
  сверять с контролами, которые игра показывает этому блоку, — а это уже другой, более
  дорогой путь, и он потребует отдельного ADR.

## Verification

Офлайн (уже в шлюзе):

```powershell
pwsh -File tools/verify-compile.ps1
pwsh -File tools/check-whitelist.ps1   # InvalidCastException — System, разрешён
pwsh -File tools/review-check.ps1
pwsh -File tools/offline-check.ps1     # «action captions (ADR-0037)» + дедупликация списка
```

В игре (человек) — на том же мире, где снят скриншот:

1. `LargeHinge`: в меню **нет** `Increase Font Size`, `Increase Text Padding`,
   `Increase Image change interval`, `Preserve Aspect Ratio`; есть `Increase Torque`,
   `Decrease Torque`, `Detach`, `Attach`, `Reverse`, `Hinge Lock On/Off`.
2. `LargeDiagonalLCDPanel`: ни одно действие не повторяется дважды, `Increase Rotation` и
   `Decrease Rotation` на месте.
3. В логе — строки `[VGS] action menu for LargeHinge (…): N action(s): …`, где N заметно
   меньше 48, и (при первом чтении типа) `skipping action '…' reported for …` с чужими id.
4. Открыть терминал шарнира в ванильном UI и сравнить список действий — он должен совпасть с
   тем, что показывает редактор.