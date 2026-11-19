# Экранное пространство мода: проверенная семантика `MyScreenSpace*`

Дата проверки: 2026-11-19. Сборка игры: `1.211.017`
(`H:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64`).

Источник фактов — IL установленных сборок, прочитанный `tools/dump-se-il.ps1` (дизассемблер
методов с разрешёнными токенами; код игры не исполняется). Здесь только то, что проверено;
`[НЕ ПРОВЕРЕНО]` помечено отдельно.

Решение, принятое на этих фактах, — [ADR-0009](../adr/0009-screen-coordinate-space.md).

---

## 1. Главный факт: 0..1 — это safe-GUI 4:3, а не окно

`Sandbox.Graphics.MyGuiManager.UpdateScreenSize`:

```csharp
int safeWidth  = Math.Min(screenWidth, (int)(screenHeight * 1.3333334f));  // 4:3
int safeHeight = screenHeight;

m_fullscreenRectangle      = new Rectangle(0, 0, screenWidth, screenHeight);
m_safeGuiRectangle         = new Rectangle(screenWidth / 2 - safeWidth / 2, 0, safeWidth, safeHeight);
m_safeFullscreenRectangle  = new Rectangle(0, 0, screenWidth, screenHeight);   // в трёхмониторном режиме — screenWidth / 3
m_safeScreenScale          = screenHeight / 1080f;
```

`safeFullscreenRectangle` — это вся клиентская область (в трёхмониторном режиме — одна треть
по центру). Именно её выбирает параметр `useFullClientArea = true`, и именно её
`MyScreenSpace*` **не** использует.

`Sandbox.ModAPI.MyScreenSpace` **всегда** передаёт `useFullClientArea = false`:

| Член | Вызов | Следствие |
|---|---|---|
| `NormalizedToPixels` | `GetScreenCoordinateFromNormalizedCoordinate(pos, false)` | пиксели от `m_safeGuiRectangle` |
| `NormalizedSizeToPixels` | `GetScreenSizeFromNormalizedSize(size, false)` | размеры от `m_safeGuiRectangle` |
| `PixelsToNormalized` | `GetNormalizedCoordinateFromScreenCoordinate` | делит на `m_safeGuiRectangle` |
| `PixelSizeToNormalized` | `GetNormalizedSizeFromScreenSize` | делит на `m_safeGuiRectangle` |
| `GetSafeGuiArea` | `NormalizedToPixels(0)` + `NormalizedSizeToPixels(1)` | `RectangleF(left, 0, safeWidth, safeHeight)` **в пикселях** |
| `TryGetCursorPosition` | `MyGuiManager.MouseCursorPosition` | курсор в safe-GUI |
| `UsingScissorRectangle` | те же два преобразования | scissor в пикселях |

`MyScreenSpaceSprite.Draw` — позиция и размер в safe-GUI (`GetScreenCoordinateFromNormalizedCoordinate(pos, false)`,
`GetScreenSizeFromNormalizedSize(size, false)`). `MyScreenSpaceText.Draw` кладёт нормализованную
позицию в очередь; очередь рисует через `MyGuiManager.DrawString(..., useFullClientArea: false)`.

Курсор: `MyDX9Gui.MouseCursorPosition` → `MyGuiManager.GetNormalizedMousePosition` →
`GetNormalizedCoordinateFromScreenCoordinate` → та же safe-GUI система.

**Числа для экрана из лога игрока (1920×1200):** `safeWidth = 1600`, полосы по 160 px,
видимая область — 1600×1200, то есть 4:3. Именно это выглядело как «окно 4:3».

## 2. Единицы `MyScreenSpaceText.Measure`

```
MyScreenSpaceText.Measure(font, text, scale)
  → MyGuiManager.MeasureString(font, text, scale)
      → MyFontDefinition.MeasureStringRaw(font, text, scale * m_safeScreenScale, true)   // пиксели
      → GetNormalizedSizeFromScreenSize(...)                                             // → safe-GUI 0..1
```

То есть `Measure` возвращает **нормализованный** размер в safe-GUI, а не пиксели.
`MeasurePixels` возвращает пиксели (`MeasureStringRaw` без деления). `PixelSizeToNormalized`
применять к `Measure` нельзя — получится деление на ширину safe-GUI второй раз.

Тот же масштаб используется и при рисовании: `MyGuiManager.DrawString` умножает `scale` на
`m_safeScreenScale`. Поэтому `scale` в `MyScreenSpaceText.Draw` и `Measure` — одна и та же
величина (доля от высоты 1080), и совпадение размеров гарантировано.

Мелкая особенность движка: `GetScreenSizeFromNormalizedSize` для X умножает на
`m_safeGuiRectangle.Width + 1` (целочисленный прямоугольник плюс один), а
`GetScreenCoordinateFromNormalizedCoordinate` — на `Width` без `+1`. Из-за этого
`GetSafeGuiArea().Width` на пиксель больше реальной safe-ширины, и выведенное из неё
преобразование даёт на правом краю на 1 px больше окна. Для заливки это безвредно
(последний столбец пикселей обрезается), для текста — расхождение меньше пикселя.

## 3. Порядок отрисовки очереди

`MyScreenSpaceDrawQueue` — три слоя (`BelowHud`, `AboveHud`, `AboveEverything`), внутри слоя
порядок вызовов сохраняется (очередь текст/спрайт/scissor). Сбрасывается:

- `MyGuiScreenGamePlay.ScreenManagerOnBeforeScreenDraw` → `Flush(AboveHud)` перед отрисовкой
  каждого экрана;
- `MyGuiScreenGamePlay.ScreenManagerOnEndOfDraw` → `Flush(AboveHud)` + `Flush(AboveEverything)`
  + `BalanceImmediateScissors`.

`ClearAll` вызывается при закрытии игрового экрана (`MyGuiScreenGamePlay.OnClosed`).
Глобального scissor вокруг `Flush` нет — рисуется весь прямоугольник, включая координаты за
пределами 0..1 (это и позволяет закрасить полосы, выйдя за safe-GUI).

## 4. Фон экрана-хоста

`MyGuiScreenBase.Draw` рисует фон только если `m_backgroundColor.HasValue && m_size.HasValue`
— и рисует его в safe-GUI. Поэтому `MyModScreen` с `backgroundColor` оставляет видимый
4:3-прямоугольник под содержимым; редактор передаёт `null` и рисует свой фон сам.
Затемнение мира (`EnabledBackgroundFade`) рисует `MyScreenManager.DrawScreens`, оно не
привязано к прямоугольнику экрана.

## 5. Что осталось непроверенным

- **Трёхмониторный режим (`isTriple`).** `m_safeGuiRectangle` считается одинаково в обоих
  режимах (`X = screenWidth / 2 - safeWidth / 2`, ширина `safeWidth = min(W, 4H/3)`); в
  трёхмониторном режиме меняется только `m_safeFullscreenRectangle` (`screenWidth / 3`).
  Значит формула ADR-0009 (`fullWidth = safe.Width + 2 * safe.X`) для трёх мониторов даст
  полную ширину всех трёх: 3×1920 при 1080 по высоте → `safeWidth = 1440`, `X = 2160`,
  `fullWidth = 5760`. Редактор растянется на все три монитора, а не останется на центральном.
  Это ограничение принято осознанно (проверить не на чем, а одномониторный случай — основной);
  если понадобится — делить на `isTriple` через `MyAPIGateway.Session.Config` не выйдет, нужен
  другой источник ширины.
- `[НЕ ПРОВЕРЕНО]` Кто и когда вызывает `UpdateScreenSize` при смене разрешения на лету;
  поэтому преобразование пересчитывается каждый кадр, а не кэшируется по событию.
- `[НЕ ПРОВЕРЕНО]` Гейтит ли `MyModScreen`/`MyGuiScreenBase` вызов `OnHandleInput` прямоугольником
  экрана-хоста (он в safe-GUI). Если гейтит, клики в полосах не дойдут до редактора при
  корректной отрисовке; проверяется в игре нажатием кнопки «Закрыть» у самого правого края.
