# 0001. UI-стек: `MyGuiScreenBase` с ручным холстом

- Status: superseded by [0008](0008-mod-screen-api.md)
- Date: 2026-11-18
- Supersedes: —

> **Отменено.** Решение ниже невыполнимо: игра отклоняет компиляцию мода с типами
> `Sandbox.Graphics.*` («The type or member 'MyGuiScreenBase' is prohibited», см. лог
> `SpaceEngineers_20261119_095026410.log`). Замена — [ADR-0008](0008-mod-screen-api.md),
> `MyModScreen` + экранное пространство мода. Текст сохранён как история: он объясняет,
> какое именно допущение оказалось ложным.

## Context

Редактору нужен полноэкранный экран с полем-сеткой, панорамой, зумом и карточками блоков.

Проверено рефлексией по сборкам игры
(`pwsh -File tools/dump-se-api.ps1 -Types ...`, дампы в `docs/01-research/`):

- `Sandbox.Graphics.GUI.MyGuiScreenBase` — абстрактный класс, публичные `Update`, `Draw`,
  `HandleInput`, `RecreateControls`, `UnloadData`. Единственный достижимый конструктор —
  `protected MyGuiScreenBase(Vector2?, Vector4?, Vector2?, bool, string, float, float, MyGuiSlot?)`.
- `Sandbox.Graphics.MyGuiManager` даёт `DrawRectangle`, `DrawBorders`, `DrawString` в
  нормализованных координатах 0..1.
- В сборке также есть новый `Sandbox.ModAPI.MyModScreen` с `MyScreenSpace`,
  `MyScreenSpaceText/Sprite` и `IMyGeneratedTexture`.

Ограничение: мод не может поставлять DLL и не может использовать `UnityEngine`,
`UnityEditor`, WPF, WinForms, ImGui — игра сама компилирует `.cs` из `Data/Scripts/`.

## Decision

Строим экран на `MyGuiScreenBase`, а холст рисуем сами через `MyGuiManager`. Штатные
`MyGuiControl*` используем только для кнопок вне холста (кнопка «Закрыть»).

`MyModScreen` не используем на этом этапе, но изолируем зависимость: вся отрисовка идёт
через методы одного класса `VgsEditorScreen`, чтобы миграция затронула один файл.

## Alternatives

| Вариант | Почему отклонён |
|---|---|
| `MyModScreen` сразу | Публичной документации и примеров нет; поведение `OnDraw`/`OnUpdate` известно только по сигнатурам. Риск несоизмерим выигрышу на этапе 1 |
| `MyGuiControl*` для каждого узла | Графу нужны панорама, зум и hit-testing в пространстве графа; контролы живут в экранном пространстве и требуют пересчёта на каждом шаге зума |
| HUD-билборды (подход Rich HUD Framework) | Годится для наложения на мир, но не для модального полноэкранного редактора с вводом |
| Unity GraphView / xNode / ImGui | Editor-only или требуют нативную библиотеку, которую мод не может поставить |

## Consequences

- Легко: полный контроль над отрисовкой, зумом и попаданием мыши; работает на любой
  версии игры, где есть `MyGuiScreenBase`.
- Трудно: каждый визуальный элемент рисуем вручную; измерение текста идёт через
  `MyScreenSpaceText.Measure` в пикселях и требует пересчёта в нормализованные координаты.
- Запрещено: читать живые сущности в пути отрисовки (см. ADR-0003).
- Если решение окажется неверным, меняется базовый класс одного файла.

## Verification

`pwsh -File tools/verify-compile.ps1` → `COMPILE OK` (наследование и конструктор
разрешаются компилятором против реальной `Sandbox.Graphics.dll`).
В игре: кнопка «Grid setup» открывает полноэкранный редактор, зум и панорама работают.
