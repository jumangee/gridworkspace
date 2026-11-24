# Цикл разработки

Как менять мод и проверять изменения. Всё выполняется из корня репозитория.

---

## 1. Компиляция, вайтлист и ревью — основные шлюзы

```powershell
pwsh -File tools/verify-compile.ps1
```

Ожидаемый вывод при успехе (число файлов растёт по мере разработки):

```
COMPILE OK — 49 file(s), 0 warning(s)
```

Что скрипт делает: запускает Roslyn (то же семейство, что и в игре) против **реальных**
сборок игры из `H:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64`, с
`-nostdlib+`, ссылками на .NET Framework 4.x и `-langversion:7.3`. Это не сборка
релиза — DLL в игру не поставляется, игра компилирует исходники сама.

Почему это работает: скрипт воспроизводит условия компиляции внутри игры, поэтому
перегрузка, разрешившаяся здесь, разрешится и там. Обратное неверно: зелёный прогон
доказывает вызываемость, а не поведение.

Полезные параметры:

```powershell
# Другая установка игры
pwsh -File tools/verify-compile.ps1 -Bin 'D:\Games\SpaceEngineers\Bin64'

# Только один файл, во время отладки
pwsh -File tools/verify-compile.ps1 -SourcePath src\VGS.Mod\Data\Scripts\VGS\Ui\VgsEditorScreen.cs
```

Второй шлюз — вайтлист. Компиляция доказывает вызываемость, но **не** допустимость:
типы `Sandbox.Graphics.*` компилируются локально и отклоняются игрой при загрузке
(«The type or member 'MyGuiScreenBase' is prohibited»). Этот шлюз закрывает разрыв:

```powershell
pwsh -File tools/check-whitelist.ps1
```

Ожидаемый вывод: `WHITELIST OK — every referenced type is allowed for a mod script`.
Скрипт разбирает собранную сборку и сверяет каждую ссылку на тип игры с вайтлистом,
**привязанным к сборке-владельцу типа**. `Sandbox.Graphics.dll` в вайтлисте отсутствует
целиком, поэтому любой её тип — ошибка.

Третий шлюз — статическое качество:

```powershell
pwsh -File tools/review-check.ps1
```

Ожидаемый вывод: `REVIEW OK — no findings`. Правила и их смысл:

| Правило | Что ловит |
|---|---|
| R001 | игровые `using` в `Vgs.Model` (нарушение слоя) |
| R002 | `async`/`await`/`Task` — вне вайтлиста игры |
| R003 | синтаксис новее C# 7.3 (строки и комментарии из строки вырезаны: текст — не код) |
| R004 | публичный тип без XML-документации |
| R005 | прямой `MyLog` вместо `VgsLog` |
| R006 | строковый литерал вне таблиц строк (`Core\VgsText.cs`, `Core\VgsStrings*.cs`) |
| R007 | пустой `catch` без объяснения |
| R008 | вход из игры без `try` |
| R009 | публичное изменяемое поле |
| R010 | аллокации и LINQ в методах `Draw*` |

Подавление одного правила на одной строке: `// review:allow R0NN причина`. Причина
обязательна — подавление без неё даёт находку R000.

```powershell
# Только слои и кадровые аллокации
pwsh -File tools/review-check.ps1 -Rules R001,R010
```

---

## 2. Деплой в игру

```powershell
pwsh -File tools/deploy-mod.ps1
tools\deploy-mod.cmd          # то же самое на cmd, если политика запрещает .ps1
```

Копирует `src\VGS.Mod` в `%APPDATA%\SpaceEngineers\Mods\VGS\`. Это **зеркало**: файлы,
удалённые из `src`, удаляются и в игре — иначе старый `.cs` продолжит компилироваться и
даст ошибку о дублирующемся типе.

Скрипт откажется работать, если запущен `SpaceEngineers.exe`.

`tools\deploy-mod.cmd` — побайтовый эквивалент на cmd: те же проверки (наличие
`Data\Scripts\VGS`, счётчик `.cs` до и после копирования, зеркальное удаление цели) и те же
коды возврата. Нужен там, где `Set-ExecutionPolicy`/групповая политика не даёт запускать
`.ps1` — например `& .\tools\deploy-mod.ps1` падает с «running scripts is disabled on this
system». Параметры: `deploy-mod.cmd [FolderName] [/force]`, путь к папке модов можно
переопределить переменной окружения `VGS_MODS_ROOT`.

После деплоя:

1. В игре: **Load Game** → мир → **Mods** → включить `VGS`.
2. Загрузить мир.
3. Сесть в кокпит, нажать **K**, найти кнопку **Grid Workspace**.

---

## 3. Чтение лога

```powershell
# Только строки мода из последнего лога
pwsh -File tools/tail-mod-log.ps1

# То же там, где политика запрещает .ps1 (ошибка «running scripts is disabled»)
tools\tail-mod-log.cmd
tools\tail-mod-log.cmd 1000
tools\tail-mod-log.cmd /all 400

# Больше контекста
pwsh -File tools/tail-mod-log.ps1 -Lines 1000

# Весь хвост лога — когда строк [VGS] нет вообще
pwsh -File tools/tail-mod-log.ps1 -All -Lines 400

# Следить в реальном времени, пока игра открыта
pwsh -File tools/tail-mod-log.ps1 -Follow
```

Лог игры: `%APPDATA%\SpaceEngineers\SpaceEngineers_<дата>_<время>.log`, по файлу на запуск.

Если `[VGS]` строк нет вообще — мод не загрузился. Проверять по порядку:

1. Папка называется `VGS` и лежит в `%APPDATA%\SpaceEngineers\Mods\`.
2. Мод включён в списке модов мира.
3. В `Data\Scripts\VGS\` действительно лежат `.cs` файлы.
4. Игра не показала свою ошибку компиляции — смотреть `-All -Lines 400`.

---

## 4. Разведка API

Прежде чем использовать незнакомый тип или метод:

```powershell
# Публичный контракт типа
pwsh -File tools/dump-se-api.ps1 -Types 'Sandbox.ModAPI.IMyCockpit'

# Маска по имени
pwsh -File tools/dump-se-api.ps1 -Types 'Sandbox.Game.Gui.MyTerminalControl*'

# Вместе с непубличными членами — чтобы понять, можно ли наследоваться
pwsh -File tools/dump-se-api.ps1 -Types 'Sandbox.ModAPI.MyModScreen' -IncludeNonPublic

# Найти, в какой сборке живёт тип
pwsh -File tools/find-se-type.ps1 -Name 'IMyCockpit'

# Что метод делает на самом деле (IL с разрешёнными токенами)
pwsh -File tools/dump-se-il.ps1 -Type 'Sandbox.ModAPI.MyScreenSpace' -Method 'GetSafeGuiArea'
pwsh -File tools/dump-se-il.ps1 -Assembly 'Sandbox.Graphics.dll' -Type 'Sandbox.Graphics.MyGuiManager' -Method 'UpdateScreenSize'
```

`dump-se-il.ps1` — не украшение. Именно он показал, что `MyScreenSpace*` считает
координаты в safe-GUI 4:3, а `Measure` возвращает не пиксели; без этого дефект R-006
выглядел как «непонятно, почему окно не на весь экран». Разбор —
[01-research/api-screen-space.md](01-research/api-screen-space.md).

Записывайте результат в `docs/01-research/` и ссылайтесь на него в коде — иначе через
месяц никто не вспомнит, откуда взялась сигнатура.

---

## 5. Где что лежит

```
src/VGS.Mod/                     ← это и есть мод
  metadata.mod                   версия мода
  Data/Scripts/VGS/              игра компилирует только это
    VgsSessionComponent.cs       точка входа
    Core/                        лог, сессия, константы, строки
    Model/                       данные графа, без игровых зависимостей
    Game/                        чтение мира, терминальные контролы, хранилища
    Ui/                          экран редактора (частичные классы), геометрия карточек,
                                 портов и рамок грида, маршрут линий, отрисовка
tools/                           скрипты разработки
data/                            снимок каталога блоков
docs/                            документация
skills/                          чеклисты ролей
agents/                          пресеты агентов для DSH
```

---

## 6. Типичные проблемы

| Симптом | Причина | Решение |
|---|---|---|
| `COMPILE OK`, но в игре кнопки нет | мод не включён в мире, или контрол зарегистрировался до определений | проверить лог на `terminal control registered`; перезагрузить мир |
| Сотни ошибок `CS0012 ... netstandard` | потеряна ссылка на `netstandard.dll` | скрипт уже включает её; проверить, что запускается `tools/verify-compile.ps1`, а не самописная команда |
| `error CS0246: MyObjectBuilder_SessionComponent` | неверный namespace | `using VRage.Game;`, не `VRage.Game.Components` |
| `error CS0019: != cannot be applied to SerializableDefinitionId` | `BlockDefinition` — структура | проверять `SubtypeName`, не ссылку |
| `dotnet` падает с `Access ... toolpath.sentinel` | sandbox не даёт писать в профиль | скрипт переносит `DOTNET_CLI_HOME` во временную папку |
| `Program 'pwsh.exe' failed to run: pipe ... denied` | вложенный запуск в песочнице | вызывать скрипт через `& .\tools\x.ps1`, не `pwsh -File` |
| `running scripts is disabled on this system` | политика выполнения запрещает `.ps1` | `tools\deploy-mod.cmd` (деплой), либо `powershell -ExecutionPolicy Bypass -File ...` |
| Редактор занимает 4:3 по центру, по бокам полосы | раскладка считает 0..1 клиентской областью, а `MyScreenSpace*` — safe-GUI | пересчитывать преобразование через `VgsScreenDraw.SyncScreenSpace()` (ADR-0009) |
| Текст, выровненный по правому краю, уезжает за панель | к `Measure` применён `PixelSizeToNormalized` | `Measure` уже нормализован (ADR-0009) |
| Связь есть в графе, но линии и портов нет | `Port.Slot` не пересчитан: полоса рисуется только при слоте ≥ 0 | вызвать `GridGraph.LayoutPorts()` после изменения связей (ADR-0010) |
| Порт нарисован в одном месте, а кликается в другом | геометрия продублирована в рисовании и в hit-test | брать прямоугольники только из `PortGeometry` (ADR-0010) |
| Подпись порта вылезает за свою полосу | `maxWidth` у `MyScreenSpaceText.Draw` передан без `_apiScaleX` | ширина — это размер, а не точка (ADR-0009, `VgsScreenDraw.Text`) |
| Линия уходит в сторону при зуме | «зазор» маршрута задан в единицах графа, а точки — в экранных | `EdgeRouting.Build` принимает зазор уже в экранных единицах (ADR-0010) |

---

## 7. Перед коммитом

- [ ] `tools/verify-compile.ps1` → `COMPILE OK`;
- [ ] `tools/check-whitelist.ps1` → `WHITELIST OK`;
- [ ] `tools/review-check.ps1` → `REVIEW OK`;
- [ ] `tools/offline-check.ps1` → `OFFLINE OK`; новое правило графа или геометрии покрыто
      утверждением в `tests/OfflineChecks.cs`;
- [ ] `tools/check-icon-catalog.ps1` → `ICON CATALOG OK` (входит в `offline-check.ps1`): набор
      иконок и `docs/07-icon-catalog.md` описывают одно и то же;
- [ ] в `Vgs.Model` нет `using Sandbox.*` / `VRage.Game.ModAPI`;
- [ ] в `Vgs.Ui` нет чтения живых блоков в путях рисования;
- [ ] нигде нет `Sandbox.Graphics.*` (вайтлист запрещает всю сборку);
- [ ] новые пользовательские строки — ключ и значение в обеих таблицах `Core/VgsStrings.cs`
      плюс свойство фасада `VgsText`; глифы отката и форматы чисел не переводятся (ADR-0033);
- [ ] новые числа — в `VgsConstants`;
- [ ] новое понятие описано в `docs/06-terminology.md`;
- [ ] новый публичный тип имеет XML-комментарий с объяснением роли;
- [ ] если поведение видно игроку — критерии приёмки в `docs/ROADMAP.md` обновлены;
- [ ] если решение изменилось — ADR в `docs/adr/` добавлен или заменён, а
      `docs/03-architecture.md` соответствует коду;
- [ ] если код разошёлся с ADR — проведено совместное ревью
      (`docs/review/joint-review.md`) и записан вердикт.
