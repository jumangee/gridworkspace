# VGS — Grid setup для Space Engineers

Внутриигровой редактор связей блоков грида. Не работа с blueprint-файлами, как в
HTML-демке, а работа с блоками **живого грида** — поэтому это игровой мод на C#.

```
Этап 1  кнопка «Grid setup» в кокпите → полноэкранный редактор с полем-сеткой
Этап 2  блоки текущего грида на поле, перетаскивание, порты по типу блока
Этап 3  связи между блоками: связь записывается в тулбар блока и работает в игре
```

## Быстрый старт

```powershell
# Проверить, что код компилируется против реальных сборок игры
pwsh -File tools/verify-compile.ps1        # → COMPILE OK

# Выложить мод в папку модов игры
pwsh -File tools/deploy-mod.ps1
tools\deploy-mod.cmd                       # то же самое, если PowerShell блокирует скрипты

# Смотреть, что мод пишет в лог
pwsh -File tools/tail-mod-log.ps1
```

Дальше — в игре: **Load Game** → мир → **Mods** → включить `VGS` → загрузить мир →
сесть в кокпит → **K** → кнопка **Grid setup**.

## Что здесь есть

| Путь | Что |
|---|---|
| [`docs/02-feasibility.md`](docs/02-feasibility.md) | **Начните отсюда.** Что реально можно сделать в моде SE и чем именно |
| [`docs/ROADMAP.md`](docs/ROADMAP.md) | Три этапа с критериями приёмки |
| [`docs/03-architecture.md`](docs/03-architecture.md) | Слои, потоки данных, точки расширения |
| [`docs/05-agents.md`](docs/05-agents.md) | Роли: архитектор, программист, ревьюер |
| [`docs/04-dev-workflow.md`](docs/04-dev-workflow.md) | Цикл разработки и типичные ошибки |
| [`docs/README.md`](docs/README.md) | Индекс всей документации |
| `src/VGS.Mod/` | Исходники мода (16 файлов, компилируются) |
| `tools/` | 9 скриптов разработки: компиляция, ревью-гейт, деплой (`.ps1` и `.cmd`), лог, дамп API, поиск типа, вайтлист, каталог |
| `data/block-catalog.json` | 1559 подтипов блоков, выгружены из игры |
| `skills/` | Чеклисты трёх ролей: architect, programmer, reviewer |
| `agents/` | Пресеты агентов для DeepSeek Harness |

## Ключевые ограничения (из них следует всё остальное)

- Мод **не поставляет DLL** — игра сама компилирует `.cs` из `Data/Scripts/`.
- Отсюда: **C# 7.3, .NET Framework 4.x**, без `async/await`, без внешних библиотек, без
  `UnityEngine`/`UnityEditor`/WPF/ImGui.
- Готовые графовые редакторы на C# переиспользовать **нельзя** — они editor-only или
  тянут Unity.
- Терминальные контролы регистрируются **на тип блока**, не на экземпляр.
- **Входы** блока перечисляются API (`GetActions`). **Выходы** — не API-перечисляемые:
  сущность выхода определяется тулбаром блока (компонент `IMyToolbarProvider` + `ButtonPanel`), а имена
  событий — курируемой таблицей. Связь записывается в реальный тулбар исходного блока, и
  это её единственное хранилище ([ADR-0011](docs/adr/0011-toolbar-is-the-connection.md)).

Подробнее с уровнем доверия каждого утверждения — в
[`docs/02-feasibility.md`](docs/02-feasibility.md).

## Что было проверено на этой машине

Все факты об API получены рефлексией по реальным сборкам игры
(`H:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64`), а не из документации:

- `MyGuiScreenBase` наследуем, конструктор `protected` — [дамп](docs/01-research/api-surface.md);
- `MyModScreen` / `MyScreenSpace*` существуют в этой сборке — [дамп](docs/01-research/api-modscreen.md);
- `MyTerminalControlFactory.AddControl<TBlock>` и `CustomControlGetter` — есть;
- тулбар блока: `IMyToolbarProvider`, `IMyToolbarSlots.BeginChanges()`, `MyToolbarItemInfo`;
  слоты «да»/«нет» у сенсора и event controller — [дамп](docs/01-research/api-toolbar.md);
- каталог из 1559 подтипов блоков, из них 50 кокпитов/сидений — `data/block-catalog.json`;
- весь мод компилируется: `COMPILE OK — 16 file(s), 0 warning(s)`.

## Источники информации по модам

Порядок доверия: сборки установленной игры (дампы) → официальная документация Keen
([SpaceEngineersModAPI](https://github.com/KeenSoftwareHouse/SpaceEngineersModAPI),
[DocFX](https://keensoftwarehouse.github.io/SpaceEngineersModAPI/)) → неофициальный
сгенерированный справочник [malforge modapi](https://malforge.github.io/spaceengineers/modapi/index.html)
→ вики и код сообщества. Внешние страницы называют члены API, но не доказывают их наличие в
текущей сборке — доказательство даёт только дамп.

## Структура репозитория

```
E:\Projects\!VGS
├── src/VGS.Mod/          мод: metadata.mod + Data/Scripts/VGS/*.cs
├── tools/                компиляция, деплой, лог, дамп API, каталог блоков
├── data/                 снимок каталога блоков игры
├── docs/                 документация (см. docs/README.md)
├── skills/               чеклисты ролей: architect, programmer, qa
├── agents/               пресеты агентов для DSH
├── html/                 исходная техно-демка (blueprint-редактор), не развивается
└── vgs/                  материалы игры: Bin64_Profile из ModSDK
```

`html/` и `vgs/` — исходные материалы, из которых вырос проект. Мод их не использует;
`vgs/Bin64_Profile` — профилировочная сборка ModSDK, а не рабочая установка игры.



