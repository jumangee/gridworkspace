# 0008. UI-стек: `MyModScreen` и экранное пространство мода

- Status: accepted
- Date: 2026-11-19
- Supersedes: [0001](0001-ui-stack.md)

## Context

ADR-0001 выбрал `Sandbox.Graphics.GUI.MyGuiScreenBase` с ручным холстом на
`Sandbox.Graphics.MyGuiManager`. Основание было таким: неймспейс `Sandbox.Game.*` целиком
разрешён вайтлистом, значит `Sandbox.Game.Gui.MyGuiScreenBase` технически доступен из мода.

**Это решение неверно, и это доказано запуском игры.**

Мод собрался локально (`verify-compile.ps1` → `COMPILE OK`), загрузился в мир — и игра
отказалась его компилировать (лог `SpaceEngineers_20261119_095026410.log`, строки 634–795):

```
MOD_ERROR: VGS
  Compilation of ...\Mods\VGS_VGS failed:
  ...VgsEditorScreen.cs(41,42): Error: The type or member 'MyGuiScreenBase' is prohibited
  ...VgsEditorScreen.cs(100,16): Error: The type or member 'MyGuiControlButton' is prohibited
  ...VgsEditorScreen.cs(184,16): Error: The type or member 'MyScreenManager' is prohibited
  ...VgsEditorScreen.cs(420,29): Error: The type or member 'MyGuiManager' is prohibited
  ... (всего 43 строки, все — из одного файла)
```

Поскольку ни один файл, кроме `VgsEditorScreen.cs`, не дал ошибок, остальной стек мода
(`Sandbox.ModAPI.*`, `VRage.Game.Components.*`, `VRageMath.*`, `VRage.Utils.*`) вайтлист
проходит.

### Почему исследование ошиблось

1. Вайтлист **привязан к сборке-владельцу типа, а не к имени неймспейса**. Все
   запрещённые типы лежат в `Sandbox.Graphics.dll`:

   | Тип | Сборка |
   |---|---|
   | `MyGuiScreenBase` | `Sandbox.Graphics` |
   | `MyGuiControlButton` | `Sandbox.Graphics` |
   | `MyScreenManager` | `Sandbox.Graphics` |
   | `MyGuiManager` | `Sandbox.Graphics` |

   У `Sandbox.Graphics.dll` **нет секции в вайтлисте вообще** — ни одного разрешённого
   неймспейса, ни одного типа. Неймспейс `Sandbox.Game.Gui` разрешён, но `MyGuiScreenBase`
   в нём не живёт.

2. `tools/read-se-whitelist.ps1` недосчитывал: его рукописная таблица опкодов обрывала
   обход IL на первом незнакомом опкоде. Скрипт печатал **63** записи вместо **441**.
   Разрешённые неймспейсы (среди них `Sandbox.ModAPI.*` от `Sandbox.Game.dll`) в отчёт не
   попадали, а «`MyGuiScreenBase` не найден» читалось как «значит, разрешён по неймспейсу».
   Дефект исправлен: таблица опкодов берётся из рантайма.

3. `verify-compile.ps1` проверяет **вызываемость**, а не **допустимость**: компилятор видит
   все публичные типы игры, а вайтлист применяется позже и только в игре. Локально зелёный
   шлюз не означал загружаемости — этого шлюза в проекте не было.

## Decision

Экран редактора строится на API, который Keen добавил в 1.211 именно для экранов модов:

- базовый класс — `Sandbox.ModAPI.MyModScreen` (сборка `Sandbox.Game.dll`, неймспейс
  `Sandbox.ModAPI` → разрешён целиком);
- отрисовка — `Sandbox.ModAPI.MyScreenSpace`, `MyScreenSpaceText`, `MyScreenSpaceSprite`
  (тот же неймспейс);
- заливка прямоугольников — своя текстура 4×4 белого цвета через
  `MyAPIGateway.GeneratedTextures` (`IMyGeneratedTextures`/`IMyGeneratedTexture`, сборка
  `VRage.Game.dll`), тонируемая цветом на каждый прямоугольник;
- слои — `MyScreenSpaceLayer.AboveHud`: редактор модальный и обязан быть поверх HUD.

Вся работа с этим API изолирована в одном типе `Vgs.Ui.VgsScreenDraw`; `VgsEditorScreen`
знает только его методы (`Rect`, `Border`, `Text`, `TextCentered`, `TextRight`, `Measure`,
`TryCursor`).

`Sandbox.Graphics.*` в моде не используется **нигде и никогда**.

## Alternatives

| Вариант | Почему отклонён |
|---|---|
| Остаться на `MyGuiScreenBase` | Невозможно: игра отклоняет компиляцию мода. Не вопрос выбора |
| Билборды (`MyTransparentGeometry`, `MyBillboard`) + свой input-routing, как Rich HUD Framework | Работоспособно и разрешено, но требует своего retained-mode дерева, своего рендера текста и ручного подавления ввода игры. Это недели работы вместо одного класса. Остаётся планом Б, если `MyModScreen` упрётся в потолок |
| Экран на `IMyTextSurface` (LCD-панель блока) | Не полноэкранный, нет мыши и колеса, привязан к блоку. Не подходит для редактора графа |
| Пустой мод без UI | Обесценивает этап 1 |

## Consequences

Стало легко:

- экран, жизненный цикл, фокус, Esc, пауза в одиночной игре и курсор — на стороне игры
  (`CloseOnEscape`, `PausesGameInSinglePlayer`, `DrawMouseCursor`);
- `MyScreenSpace` даёт нормализованные 0..1 координаты, курсор и scissor — то, что раньше
  приходилось брать из запрещённого `MyGuiManager`. **Важно:** эти 0..1 — не клиентская
  область, а safe-GUI прямоугольник 4:3 по центру экрана; см. [0009](0009-screen-coordinate-space.md);
- `MyScreenSpaceText.Measure` меряет текст тем же API, которым он рисуется, поэтому
  выравнивание не может разойтись с отрисовкой — при условии, что единицы не перепутаны
  (`Measure` возвращает нормализованный размер, а не пиксели; см. 0009).

Стало трудно, и это надо учитывать при доработках:

- **Штатных контролов нет.** Ни кнопки, ни скроллбара, ни поля ввода. Кнопка «Закрыть»
  рисуется прямоугольником и проверяется на попадание вручную
  (`HandleCloseButton`); `CloseButtonEnabled = false` — иначе игра нарисует свою кнопку
  поверх.
- **Заливка требует текстуры.** Sprite-API рисует тонированные квады, а не цветные
  прямоугольники; нужна своя белая текстура. Если платформа не поддерживает
  `GeneratedTextures`, `VgsScreenDraw` пробует контентную текстуру и в крайнем случае
  отключает заливку с предупреждением в лог.
- **Одновременно можно открыть не более `MAX_OPEN_SCREENS = 4` экранов мода** (поле
  `MyModScreen.m_openScreenCount`). Для одного редактора это не ограничение, но второй
  экран (например, настройки) обязан учитывать лимит.
- **Нет управления z-порядком.** Повторное открытие редактора при уже открытом экране
  игнорируется (`_openEditor` + `IsOpen`), а не поднимает экран наверх.
- Позиция `MyModScreen.Position`/`Size` — доля клиентской области; экран растянут на 1×1,
  поэтому вся раскладка выводится из текущего размера клиента при отрисовке. Но **рисование
  через `MyScreenSpace*` идёт не в этих координатах**, а в safe-GUI 4:3 по центру; перевод
  делает `VgsScreenDraw`, см. [0009](0009-screen-coordinate-space.md).

## Verification

Статически (шлюз, без запуска игры):

```powershell
pwsh -File tools/verify-compile.ps1     # COMPILE OK
pwsh -File tools/check-whitelist.ps1    # WHITELIST OK
pwsh -File tools/review-check.ps1       # без находок
```

`check-whitelist.ps1` — новый шлюз, которого не хватало: он разбирает собранную сборку и
сверяет каждую ссылку на тип игры с вайтлистом, привязанным к сборке-владельцу. На коде до
этого решения он даёт ровно четыре запрещённых типа из лога игры (`MyGuiScreenBase`,
`MyGuiControlButton`, `MyScreenManager`, `MyGuiManager`) — то есть воспроизводит отказ игры
офлайн.

В игре (проверяет человек, см. критерии этапа 1 в [ROADMAP](../ROADMAP.md)): кнопка
`Grid setup` в терминале кокпита, открытие редактора, панорама ПКМ/СКМ, зум колесом,
кнопка «Закрыть», Esc.
