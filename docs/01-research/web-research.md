# Веб-ресёрч: Space Engineers modding для проекта «Grid Logic Editor»

> ## ⚠️ Это исторический документ, а не руководство
>
> Ресёрч сделан 2026-11-18, **до** первой строки кода и без доступа к сборкам игры. С тех пор
> мод написан и работает, API проверен по сборкам (`dump-se-api.ps1`, `dump-se-il.ps1`), и
> часть выводов ниже **опровергнута практикой**. Читать его стоит только ради «почему мы
> рассматривали именно это» и ради ссылок на внешние источники.
>
> **Истина — здесь:** [`api-*.md`](.) (проверенные факты игры), [`docs/adr/`](../adr/README.md)
> (принятые решения), [`docs/03-architecture.md`](../03-architecture.md) (как устроено).
>
> Что опровергнуто и чем заменено:
>
> | Утверждение ресёрча | Что верно на самом деле |
> |---|---|
> | `LangVersion 6` (§1.2, §9 п.1, §12) | **C# 7.3** — компилятор игры Roslyn 2.9, `-langversion:7.3` в `tools/verify-compile.ps1` |
> | `MyModScreen` недокументирован → обязателен план Б (Rich HUD), спайк 1–2 дня (§0, §3.2) | Спайк сделан, план Б не понадобился: экран на `MyModScreen` принят, сигнатуры сняты — [`api-modscreen.md`](api-modscreen.md), [ADR-0008](../adr/0008-mod-screen-api.md) |
> | `Sandbox.Game.*` разрешён целиком, значит `MyGuiScreenBase` технически доступен (§4, §9 п.16) | Вайтлист привязан к **сборке-владельцу**: `Sandbox.Graphics.dll` секции не имеет, четыре её типа отвергнуты игрой при загрузке — [ADR-0008](../adr/0008-mod-screen-api.md) |
> | «Буфер обмена ОС доступен» (образец Build Vision, §5) | **Недоступен**: `IVRageSystem`/`MyVRage` отвергнуты игрой, поле игры запрещено; своё поле и свой буфер — [ADR-0027](../adr/0027-clipboard-is-own-buffer-only.md), [ADR-0024](../adr/0024-text-field-caret-selection-clipboard.md) |
> | Пауза: «`HandleInput` работает на паузе, `UpdateAfterSimulation` — нет» (§2.3) | Верно, но главное — `MyModScreen.Open` сам ставил паузу в одиночной игре; пауза снята, часы экрана — свои кадры в `OnUpdate` — [ADR-0029](../adr/0029-editor-does-not-pause.md), [ADR-0026](../adr/0026-clocks-in-the-mod.md) |
> | «Данные графа — на грид/кокпит, синхронизировать пакетами» (§7.2) | Раскладка — на самом блоке ([ADR-0012](../adr/0012-layout-lives-on-the-block.md)), связь — элемент тулбара ([ADR-0011](../adr/0011-toolbar-is-the-connection.md)), состояние вида — на гриде ([ADR-0022](../adr/0022-view-state-lives-on-the-grid.md)); сетевых пакетов в коде нет |
> | «`IsServer`, `GetBlocks`, ownership не проверены» (§3.3, §7.3, §9 п.25) | Проверены и используются: `MyAPIGateway.Multiplayer.IsServer`, `GetTerminalSystemForGrid(grid).GetBlocks(...)` — [`api-grid-construct.md`](api-grid-construct.md), [`api-surface.md`](api-surface.md) |

**Дата ресёрча:** 2026-11-18
**Актуальная версия игры на момент ресёрча:** `1.211.017` («Warfare 3»)
**Предмет:** мод на C# для Space Engineers (Keen Software House, VRAGE/Unity, .NET Framework), который добавляет в кокпит кнопку «Grid setup» и открывает полноэкранный редактор графа (узлы блоков, поля, связи).

**Легенда маркеров:**
- ✅ **VERIFIED** — факт найден в источнике (ссылка рядом).
- ⚠️ **UNCERTAIN / UNVERIFIED** — не подтверждено первоисточником, требует проверки в декомпилированном коде/игре.
- ❌ **NOT AVAILABLE** — проверено, что так сделать нельзя.

**Методика и её ограничения (важно для доверия к отчёту).**
Ресёрч делался только веб-запросами (без запуска игры и без декомпиляции). Штатный инструмент веб-поиска в этой сессии был недоступен (ошибка API-баланса), поэтому использовались: MediaWiki API официальной вики (`action=raw`, `list=search`), GitHub REST API, `raw.githubusercontent.com`, официальный сайт поддержки Keen. Из-за этого **часть тем (например, точные сигнатуры методов) подтверждена косвенно** — через списки вайтлиста, примеры кода и патчноуты, а не через чтение исходников игры. Все такие места помечены ⚠️.

---

## 0. Главный вывод ресёрча (прочитать первым)

**В версии 1.211 (ноябрь 2026) Keen добавила официальный API для экранного UI из модов.** В официальном changelog перечислено:

> «Screen Space / Drawing ModAPIs — Screen Space Text (API responsible for drawing text), Screen Space Sprite (API responsible for drawing sprites/images), **Mod Screen — API for creating new UI screens**, Generated Texture (API for creating generated textures for mods)»

Источник: [Update 1.211 — Warfare 3 (официальный анонс Keen)](https://support.keenswh.com/spaceengineers/pc/announcement/update-1-211-warfare-3), дублируется в вики: [Modding/Reference/Patch Changes/1.211](https://spaceengineers.wiki.gg/wiki/Modding/Reference/Patch_Changes/1.211) — там перечислены новые типы `MyModScreen`, `MyScreenSpace`, `MyScreenSpaceText`, `MyScreenSpaceSprite`, `MyScreenSpaceLayer`, `IMyGeneratedTexture`, `MyGeneratedTextureFormat` и свойство `MyAPIGateway.GeneratedTextures`.

Практический смысл: **это ровно та функциональность, которой не хватало для Stage 1–2** (полноэкранный экран мода + рисование текста/спрайтов). До 1.211 единственным рабочим путём был «рисовать HUD-билбордами самим» (так делает Rich HUD Framework) — см. §3.

⚠️ **Но:** публичной документации/примеров на `MyModScreen` пока нет (страница патчноутов — единственный найденный источник; в вики нет ни одной статьи по этим типам). То есть API есть, но он **свежий и недокументированный** → это одновременно и возможность, и риск. План Б (Rich HUD Framework) обязателен.

---

## 1. Официальные точки входа и SDK

### 1.1. Структура мода и обязательные файлы

✅ **VERIFIED.** Каноническая структура локального мода:

```
%APPDATA%\SpaceEngineers\Mods\<ModName>\
├── Data\                      ← ОБЯЗАТЕЛЬНА (даже пустая), иначе мод не загрузится
│   ├── Scripts\
│   │   └── <ModName>\         ← ОБЯЗАТЕЛЬНА подпапка с именем мода
│   │       └── *.cs           ← компилируется игрой в отдельную сборку
│   ├── Localization\
│   │   ├── MyTexts.resx
│   │   └── MyTexts.sbl
│   └── *.sbc                  ← контент/определения
├── thumb.jpg | thumb.png      ← превью (для mod.io обязательно)
└── modinfo.sbmi               ← ID в Workshop/mod.io (создаётся при первой публикации)
```

Источники: [Modding/Tutorials/Creating And Uploading Mods](https://spaceengineers.wiki.gg/wiki/Modding/Tutorials/Creating_And_Uploading_Mods), [Modding/Reference/Programming/Intro](https://spaceengineers.wiki.gg/wiki/Modding/Reference/Programming/Intro).

Ключевые детали, подтверждённые дословно:

- ✅ «Every mod **must** have a `Data` folder, even if it's just empty. Otherwise, the game will fail to load the mod.»
- ✅ Скрипты **должны** лежать в `<ModName>\Data\Scripts\<ModName>\`; «Every file or folder directly in the Scripts folder will be compiled as a separate assembly, which will cause your scripts to not see each other» → **ровно одна папка первого уровня внутри `Scripts`**, внутри неё — любая вложенность.
- ✅ Имя этой папки используется как имя сборки и как имя папки в `%APPDATA%\SpaceEngineers\Storage\<workshopId>.sbm_<SomeName>`.
- ✅ `thumb.jpg` **или** `thumb.png`; для Steam — опционально, но < 1 MB; для mod.io — обязательно, ≥ 512×288 и < 8 MB.
- ✅ `modinfo.sbmi` нужен для **обновления** уже опубликованного мода; без него публикация создаст новый мод. Внутри — `MyObjectBuilder_ModInfo` с `<WorkshopIds>` (Steam / mod.io).
- ✅ **`metadata.mod` — устаревший артефакт.** Дословно: «There might also be a metadata.mod file, but it's **not used and can be ignored**». ⚠️ Это прямо противоречит распространённому представлению о «обязательном metadata.mod» — на 1.211 он не нужен.
- ✅ Порядок загрузки: «Mods are loaded from the bottom to the top meaning the mods at the top have the last say» (важно для конфликтов SBC-переопределений).

### 1.2. Как игра компилирует код мода

✅ **VERIFIED** ([Modding/Reference/Programming/Intro](https://spaceengineers.wiki.gg/wiki/Modding/Reference/Programming/Intro)):

- «The game only cares about .cs files, which it will compile on its own while checking the code against the whitelist».
- «**You cannot publish a .dll/.exe**, and even if you did, the game wouldn't use them anyway.»
- Есть вайтлист IL-проверки: [Modding/Reference/Programming/Whitelist](https://spaceengineers.wiki.gg/wiki/Modding/Reference/Programming/Whitelist) (обновлён для `v1.211.017`).
- ⚠️ Рекомендация: не держать `Ingame`-неймспейсы в обычных `using` — конфликты имён с ModAPI, ошибки проявляются только в игре (там игра добавляет свои `using`). Пример обхода: `using MyShipConnectorStatus = Sandbox.ModAPI.Ingame.MyShipConnectorStatus;`.

**Следствие для проекта:** всё, что мы хотим переиспользовать (графовые библиотеки, UI-фреймворки), должно быть **исходниками .cs**, а не DLL. Это отсекает почти весь NuGet-экосистем (см. §6).

### 1.3. Актуальная документация и API reference

| Ресурс | Что это | Статус |
|---|---|---|
| [spaceengineers.wiki.gg/wiki/Modding](https://spaceengineers.wiki.gg/wiki/Modding) | Главная точка входа: туториалы, reference, tools | ✅ актуальна (правки 2025–2026) |
| [spaceengineers.wiki.gg/wiki/Modding/Reference](https://spaceengineers.wiki.gg/wiki/Modding/Reference) | Индекс reference-страниц | ✅ |
| [keensoftwarehouse.github.io/SpaceEngineersModAPI](https://keensoftwarehouse.github.io/SpaceEngineersModAPI/api/index.html) | **Официальный** API reference (DocFX) | ✅ существует, но ⚠️ **неполный** (см. ниже) |
| [malforge.github.io/spaceengineers/modapi/](https://malforge.github.io/spaceengineers/modapi/) | Неофициальный/community API reference | ✅ «should be complete» |
| [Modding/Reference/Programming/Whitelist](https://spaceengineers.wiki.gg/wiki/Modding/Reference/Programming/Whitelist) | Полный вайтлист (включая .NET-типы) | ✅ «Last checked for v1.211.017» |
| [github.com/THDigi/SE-ModScript-Examples](https://github.com/THDigi/SE-ModScript-Examples) | Эталонные шаблоны и примеры кода модов | ✅ активно обновляется (последние коммиты окт. 2026) |
| [github.com/malforge/mdk2](https://github.com/malforge/mdk2) / [docs](https://malforge.github.io/spaceengineers/mdk2/) | **MDK²** — актуальный тулкит (шаблоны проектов, анализатор вайтлиста) | ✅ актуален |
| [github.com/malware-dev/MDK-SE](https://github.com/malware-dev/MDK-SE) | Старый MDK | ❌ **DEPRECATED**: в README прямо сказано «This API documentation is out of date and no longer maintained» → переходить на MDK² |

Важные оговорки из вики про официальные доки (✅ дословно с [Modding/Reference/Programming/Intro](https://spaceengineers.wiki.gg/wiki/Modding/Reference/Programming/Intro)):
- официальный сайт **неполный** — часть whitelisted-типов там отсутствует (например `MyGunBase`);
- ни официальный, ни community-сайт **не показывают whitelisted .NET-типы**;
- «the API has a severe lack of documentation on various methods» → придётся читать декомпилированный код (см. [Exploring the game code](https://spaceengineers.wiki.gg/wiki/Modding/Tutorials/Programming/Exploring_the_game_code)).

### 1.4. Версия игры и target framework

✅ **VERIFIED: `net48`** (`.NET Framework 4.8`), платформа `x64`, `LangVersion` = **6** в шаблоне
(но компилятор игры принимает **7.3** — см. правку ниже).

Канонический `.csproj` из официальной вики ([Convert old projects to new SDK](https://spaceengineers.wiki.gg/wiki/Modding/Tutorials/Programming/Convert_old_projects_to_new_SDK)):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <Platforms>x64</Platforms>
    <LangVersion>6</LangVersion>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <GenerateNeutralResourcesLanguageAttribute>false</GenerateNeutralResourcesLanguageAttribute>
    <GenerateTargetFrameworkAttribute>false</GenerateTargetFrameworkAttribute>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Mal.Mdk2.ModAnalyzers" Version="*" />
    <PackageReference Include="Mal.Mdk2.References" Version="*" />
  </ItemGroup>
</Project>
```

Что это значит практически:

- ✅ `net48` — можно пользоваться `System.Collections.Immutable`, `System.Linq`, `Span`-а нет.
- ✅ **`LangVersion 6`** — это уровень языка **в шаблоне**, а не предел компилятора игры.
  **Проверено практикой (позже этого ресёрча):** компилятор игры — Roslyn 2.9, и он принимает
  **C# 7.3**; проект собирается с `-langversion:7.3` в `tools/verify-compile.ps1` и использует
  `out var`, локальные функции и pattern matching. Более новый синтаксис запрещён правилом
  `R003` шлюза `tools/review-check.ps1` — не потому, что игра не примет, а потому что
  `verify-compile.ps1` воспроизводит именно 7.3.
- ✅ **`System.Threading.Tasks` в вайтлисте отсутствует** (в `mscorlib.dll` разрешены только `AutoResetEvent`, `Interlocked`, `ManualResetEvent`, `Monitor`; в `System.dll` — `System.Timers.*`, `System.Collections.Concurrent.*`) → **`async/await` в модах недоступен**. Асинхронность — только через `MyAPIGateway.Utilities.InvokeOnGameThread`, корутины-подобные паттерны на тиках, `ParallelTasks.Task` (ограниченно, см. вайтлист).
- ✅ `System.Text.RegularExpressions.*` разрешён (в 1.211 ему добавили таймаут 250 мс — «Added a 250 ms time limit to regular expressions»).

---

## 2. Кодовые точки входа для мода

### 2.1. `MySessionComponentBase` + `MySessionComponentDescriptor`

✅ **VERIFIED** — эталонный пример: [`BasicExample_GameLogicAndSession/Session.cs`](https://github.com/THDigi/SE-ModScript-Examples/blob/master/Data/Scripts/Examples/BasicExample_GameLogicAndSession/Session.cs).

```csharp
[MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation | MyUpdateOrder.AfterSimulation)]
public class Example_Session : MySessionComponentBase
{
    public static Example_Session Instance;   // единственное допустимое статическое поле
    public override void LoadData() { ... }
    public override void BeforeStart() { ... }
    protected override void UnloadData() { ... }
    public override void HandleInput() { ... }
    public override void UpdateBeforeSimulation() { ... }
    public override void Simulate() { ... }
    public override void UpdateAfterSimulation() { ... }
    public override void Draw() { ... }
    public override void SaveData() { ... }
    public override MyObjectBuilder_SessionComponent GetObjectBuilder() { ... }
    public override void UpdatingStopped() { ... }
}
```

Подтверждённые комментариями в исходнике факты (✅):

- «This object is always present, from the world load to world unload.»
- «**all clients and server run mod scripts**» — ключ к мультиплееру (см. §7).
- «this and gamelogic comp's update methods run on the **main game thread**» — тяжёлую работу в тик делать нельзя.
- «The `MyUpdateOrder` arg determines what update overrides are actually called» — переопределять только нужные методы.
- `HandleInput()` — «gets called 60 times a second before all other update methods, **regardless of framerate, game pause** or MyUpdateOrder» и **только на клиентах** (подтверждено в [`Example_InputReading.cs`](https://github.com/THDigi/SE-ModScript-Examples/blob/master/Data/Scripts/Examples/Example_InputReading.cs): «this method however only runs on players (not DS-side)»).
- `Draw()` — «the only place where the camera matrix (`MyAPIGateway.Session.Camera.WorldMatrix`) is accurate, everywhere else it's 1 frame behind».
- `SaveData()` — «executed **AFTER** world was saved»; `GetObjectBuilder()` — «during world save, most likely before entities».
- `UpdateBeforeSimulation/UpdateAfterSimulation/Simulate` — 60 Гц, только когда игра не на паузе.

**Важный паттерн обработки ошибок (✅ из того же файла):** в `LoadData()` обязателен try/catch с `throw new ModCrashedException(e, ModContext)` через `MyAPIGateway.Utilities.InvokeOnGameThread(...)`, иначе игра «съест» исключение и не вызовет `UnloadData()` у других модов.

### 2.2. `MyAPIGateway` — главная точка входа API

✅ **VERIFIED** (перечислено в комментариях `Session.cs` и подтверждается примерами):
`MyAPIGateway.Utilities`, `MyAPIGateway.Session`, `MyAPIGateway.Entities`, `MyAPIGateway.TerminalControls`, `MyAPIGateway.Input`, `MyAPIGateway.Gui`, `MyAPIGateway.Multiplayer`, `MyAPIGateway.Physics`, `MyAPIGateway.GridGroups`, `MyAPIGateway.TerminalActionsHelper`, `MyAPIGateway.GeneratedTextures` (новое в 1.211).
Рядом: `MyDefinitionManager.Static`, `MyGamePruningStructure`, `MyTransparentGeometry`, `MySimpleObjectDraw`, `MyVisualScriptLogicProvider`.

### 2.3. `Sandbox.ModAPI.Ingame` vs `Sandbox.ModAPI.Interfaces` — разделение

✅ По вайтлисту ([Whitelist](https://spaceengineers.wiki.gg/wiki/Modding/Reference/Programming/Whitelist)) полностью разрешены неймспейсы:

| Неймспейс | Назначение | Для нас |
|---|---|---|
| `Sandbox.ModAPI.*` | «взрослый» ModAPI: полные интерфейсы блоков, `IMyTerminalBlock`, `IMyCubeGrid` | основной инструмент мода |
| `Sandbox.ModAPI.Ingame.*` | урезанные интерфейсы для Programmable Block | ❌ не использовать в моде (конфликты имён, см. §1.2) |
| `Sandbox.ModAPI.Interfaces.*` | `ITerminalAction`, `ITerminalProperty`, `TerminalPropertyExtensions` | для actions/properties |
| `Sandbox.ModAPI.Interfaces.Terminal.*` | `IMyTerminalControl*` — контролы терминала | Stage 1 (кнопка) |
| `Sandbox.Game.*` (полностью!) | вся `Sandbox.Game.dll`, включая `Sandbox.Game.Gui.*` | ⚠️ сюда попадает `MyGuiScreenBase` и прочий GUI |
| `VRage.Game.ModAPI.*`, `VRage.ModAPI.*`, `VRageMath.*`, `VRage.Utils.*`, `VRageRender.MyBillboard` | сущности, математика, билборды | рисование |
| `VRage.Input.MyKeys`, `MyMouseButtonsEnum`, `VRage.Input.MyInputExtensions` | ввод | хоткеи |

⚠️ Важное следствие: `Sandbox.Game.*` разрешён целиком, значит **технически** `Sandbox.Game.Gui.MyGuiScreenBase` доступен из мода. Но см. §3.2 — публичной документации и открытых примеров не найдено.

### 2.4. Клиент/сервер

✅ **VERIFIED:**
- Скрипты мода выполняются **и на клиенте, и на сервере** (`Session.cs`, комментарий; и весь раздел [Save and Sync](https://spaceengineers.wiki.gg/wiki/Modding/Reference/Programming/Save_and_Sync)).
- `MyAPIGateway.Utilities.InvokeOnGameThread(...)` — маршалинг в главный поток.
- Признак сервера: ⚠️ `MyAPIGateway.Multiplayer.IsServer` / `MyAPIGateway.Session.IsServer` — точное имя в этой сессии не проверено (в примерах THDigi встречается `MyAPIGateway.Multiplayer.IsServer`; **проверить перед использованием**).
- Для UI: `HandleInput()` не выполняется на DS — это удобная «естественная» клиентская граница.

### 2.5. `LoadData` / `SaveData` / персистентность — см. §7.3 (там же про синхронизацию).

---

## 3. Кастомный полноэкранный GUI внутри мода

### 3.1. Что реально делают моды: HUD-билборды, а не GuiScreen

✅ **VERIFIED, и это ключевой факт.** Автор главного UI-фреймворка формулирует прямо:

> «A **retained-mode GUI framework** for creating fully mod-defined UI in C# Space Engineers workshop mods. It enables the creation of scalable, resolution-independent, and fully interactive user interfaces **using the game's billboard system, in lieu of support in the mod API**.»

Источник: [RichHudFramework.Client/README.md](https://github.com/ZachHembree/RichHudFramework.Client) (лицензия **MIT**, C#, обновлялся 2026-08-14).

То есть исторически (до 1.211) **в mod API не было API экранов**, и все «окна» рисовались билбордами (`VRageRender.MyBillboard`, `MyTransparentGeometry`, `MySimpleObjectDraw`) поверх HUD.

**Rich HUD Framework** — самый близкий к нашей задаче готовый стек (✅ из README):
- persistent **node-based UI tree** с автосортировкой по глубине, роутингом ввода, иерархией parent-child;
- поддержка **screen-space (2D HUD) и world-space (3D)**;
- собственный **bitmap-рендер текста** (rich-text, автоперенос, загрузка шрифтов в рантайме);
- централизованная **сериализуемая система кейбиндов** с конфликт-группами и подавлением дефолтного ввода игры;
- **готовый «терминал настроек» мода**: страницы, категории, слайдеры, чекбоксы, дропдауны, ребинд клавиш, rich-text help/changelog;
- библиотека готовых контролов.

Архитектурная деталь, важная для нас (✅): фреймворк разделён на **три модуля — Master / Client / Shared**, потому что «Space Engineers mods are compiled to independent assemblies. Data must be shared at runtime using common types and primitives». На Workshop публикуется **Master**, в свой мод встраивается **Client**.

Примеры реального кода на нём:
- ✅ [TextEditorExample](https://github.com/ZachHembree/TextEditorExample) — полноценный **текстовый редактор внутри мода** (скриншот в README): выделение, ввод, каретка. Лучший референс по «как обрабатывать ввод в собственном UI».
- ✅ [BuildVision2](https://github.com/ZachHembree/BuildVision2) (**MIT**) — «quick access context menu» для блока: колесо/список свойств, **скролл колесом мыши с множителями** (`MultX/MultY/MultZ` — Ctrl/Shift/Ctrl+Shift), числовые поля со скроллом, текстовые поля с Ctrl+A/X/C/V, режим дублирования настроек, настройки через Rich HUD Terminal. README прямо описывает работу с `MouseWheelUp/Down`, `LeftButton/RightButton`, «peek»-биндами. **Это почти точный аналог того, что нам нужно на Stage 2.**

Вывод: **паттерн «своё окно на билбордах + свой input-routing» проверен боем** (Build Vision, Text Editor, Rich HUD Terminal). Это работающий, но трудоёмкий путь.

### 3.2. `MyGuiScreenBase` — «настоящий» экран игры

⚠️ **UNCERTAIN.** Что удалось установить:

- ✅ Класс существует и доступен по вайтлисту: неймспейс `Sandbox.Game.Gui` входит в разрешённый `Sandbox.Game.*` ([Whitelist](https://spaceengineers.wiki.gg/wiki/Modding/Reference/Programming/Whitelist)). Класс фигурирует в патчноутах игры (например, «Fixed a crash at `MyGuiScreenBase.GetExclusiveInputHandler`», [Version/1.192.1](https://spaceengineers.wiki.gg/wiki/Version/1.192.1)).
- ⚠️ **Как его открыть из мода — не подтверждено.** Ожидаемый паттерн (`new MyGuiScreenX(...)` + регистрация через `MyGuiSandbox`/`MyGuiManager`) **не найден ни в одном открытом источнике**. Утверждать имена методов (`MyGuiSandbox.AddScreen`) я не буду — это ровно тот случай, где нельзя выдумывать API.
- ❌ **Открытых примеров `MyGuiScreenBase`-наследников в модах не найдено.** Проверены: GitHub-поиск репозиториев (`MyGuiScreenBase in:readme` → 0 результатов), `topic:spaceengineers` (31 C#-репозиторий, просмотрены топ-8), репозитории Rich HUD / Build Vision 2 / Text Editor, вики (страницы `Modding/Tutorials/*` — туториала по GUI нет).

**Практический вывод:** заявленные в задании кандидаты («Build Vision, Paint Gun, Text HUD API, Rich HUD Master, ALE, SE GUI framework») в проверенных источниках оказываются **HUD-based**, а не `MyGuiScreenBase`-based. Если `MyGuiScreenBase` и используется какими-то модами, то это закрытый код. **Проверять надо декомпиляцией** (`Bin64/Sandbox.Game.dll`, классы `MyGuiScreenBase`, `MyGuiSandbox`, `MyGuiManager`) — это единственный надёжный путь, см. [Exploring the game code](https://spaceengineers.wiki.gg/wiki/Modding/Tutorials/Programming/Exploring_the_game_code).

### 3.3. Новый официальный путь (1.211): Mod Screen API

✅ **VERIFIED по патчноутам** ([1.211](https://spaceengineers.wiki.gg/wiki/Modding/Reference/Patch_Changes/1.211), [официальный анонс](https://support.keenswh.com/spaceengineers/pc/announcement/update-1-211-warfare-3)):

| Тип | Назначение (формулировка Keen) |
|---|---|
| `MyModScreen` | «API for creating new UI screens» |
| `MyScreenSpace` | корень экранного рендера |
| `MyScreenSpaceText` | «API responsible for drawing text» |
| `MyScreenSpaceSprite` | «API responsible for drawing sprites/images» |
| `MyScreenSpaceLayer` | слои |
| `IMyGeneratedTexture`, `MyGeneratedTextureFormat`, `MyAPIGateway.GeneratedTextures` | генерация текстур из мода |

⚠️ **Что неизвестно:** сигнатуры, жизненный цикл, порядок регистрации, поддержка ввода (мышь/клавиатура), скролла, фокуса, z-order, поведение в мультиплеере, требования (экспериментальный режим?). **Ни одного примера кода не найдено.** Это первый кандидат на проверку «руками» — API ровно под нашу задачу.

### 3.4. Альтернативные UI-стеки: реалистичная оценка

| Стек | Вердикт | Обоснование |
|---|---|---|
| **Rich HUD Framework** (MIT) | ✅ **жизнеспособен** | работает на билбордах, есть примеры и документация: [docs](https://zachhembree.github.io/RichHudFramework.Client/) |
| **Mod Screen API** (1.211, официальный) | ⚠️ вероятно лучший путь, но недокументирован | см. §3.3 |
| **MyGuiScreenBase** (движковый) | ⚠️ неизвестно | см. §3.2 |
| **EmptyKeys.UserInterface / MVVM** | ❌ **не подтверждено, скорее нет** | библиотека не входит в вайтлист ([Whitelist](https://spaceengineers.wiki.gg/wiki/Modding/Reference/Programming/Whitelist)); DLL нельзя поставлять с модом (§1.2). ⚠️ Требует отдельной проверки, но выглядит тупиком |
| **ImGui / ImGui.NET** | ❌ **невозможно** | требует нативного `cimgui.dll`; моды не могут поставлять DLL («You cannot publish a .dll/.exe»), плюс вайтлист IL |
| **Unity UI (uGUI/UIElements/IMGUI)** | ❌ **невозможно** | `UnityEngine.*`/`UnityEditor.*` отсутствуют в вайтлисте; мод компилируется против DLL игры, а не Unity-проекта |
| **WPF/WinForms** | ❌ **невозможно** | нет в вайтлисте, нет окна, нет доступа к Win32 |

### 3.5. Ввод, скролл, локализация — конкретика

✅ **Ввод** ([`Example_InputReading.cs`](https://github.com/THDigi/SE-ModScript-Examples/blob/master/Data/Scripts/Examples/Example_InputReading.cs)):
- `MyAPIGateway.Input.IsNewGameControlPressed(MyControlsSpace.USE)` — «new» = только в кадр нажатия;
- `MyAPIGateway.Input.IsGameControlPressed(...)` — удержание;
- `MyAPIGateway.Input.IsControl(contextId, MyControlsSpace.USE, MyControlStateType.NEW_PRESSED)` — с геймпадом;
- **«all input methods will read regardless of being in menus or having chat open»** → обязательны проверки `MyAPIGateway.Gui.IsCursorVisible` и `MyAPIGateway.Gui.ChatEntryVisible`, иначе наш редактор будет «просвечивать» ввод в игру. Это же — готовый признак «открыт ли сейчас UI».
- ⚠️ Про мышь/скролл: `MyAPIGateway.Input.GetMouseScroll`/`IsNewLeftMousePressed` — имена в этой сессии не проверены; в Build Vision используются бинды `MouseWheelUp/Down`, т.е. подход через `MyControlsSpace`.

✅ **Локализация** ([Modding/Tutorials/Localization](https://spaceengineers.wiki.gg/wiki/Modding/Tutorials/Localization)):
- файлы `Data/Localization/MyTexts.resx` + `MyTexts.sbl`, переводы — `MyTexts.ru.resx` и т.д.;
- в SBC: `{LOC:DisplayName_MyString}` (в отличие от ванильных SBC, где обёртка не нужна);
- в коде: `MyStringId.GetOrCompute("...")` (✅ используется в примере терминальных контролов), `MySpaceTexts.*` для ванильных строк, `MyAPIGateway.Utilities.GetStringFromLocalizationTable` (⚠️ не проверено);
- ⚠️ Известный баг Keen: «If you have the modded block on your toolbar, then save and reload the save, you will see its localization key instead of the localized name» — обход: скрипт SISK ([LocalizationComponent.cs](https://github.com/SiskSjet/SE_Mod_Utils/blob/master/Localization/LocalizationComponent.cs)).
- ✅ Для нашего UI: **русский есть в списке** (`ru` — Русский), т.е. локализация редактора возможна штатно.

---

## 4. Терминальный контрол/кнопка/экшен на **существующих** блоках (кокпит)

### 4.1. API

✅ **VERIFIED** — полный эталонный пример: [`TerminalControls/Adding/GyroTerminalControls.cs`](https://github.com/THDigi/SE-ModScript-Examples/blob/master/Data/Scripts/Examples/TerminalControls/Adding/GyroTerminalControls.cs).

```csharp
var c = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyCockpit>("YourMod_GridSetup");
c.Title   = MyStringId.GetOrCompute("Grid setup");
c.Tooltip = MyStringId.GetOrCompute("...");
c.SupportsMultipleBlocks = true;
c.Visible = (b) => ...;          // фильтр по инстансу блока
c.Enabled = (b) => ...;          // «серый» вид
c.Action  = (b) => { ... };      // клик
MyAPIGateway.TerminalControls.AddControl<IMyCockpit>(c);
```

Полный список типов контролов, подтверждённый примером (✅): `IMyTerminalControlSeparator`, `IMyTerminalControlLabel`, `IMyTerminalControlOnOffSwitch`, `IMyTerminalControlCheckbox`, `IMyTerminalControlSlider` (`SetLimits`, `SetDualLogLimits`, `SetLogLimits`, `Writer`), `IMyTerminalControlButton`, `IMyTerminalControlColor`, `IMyTerminalControlCombobox` (`MyTerminalControlComboBoxItem`), `IMyTerminalControlListbox` (`MyTerminalControlListBoxItem`, `VisibleRowsCount`, `Multiselect`, `ListContent`, `ItemSelected`), `IMyTerminalControlTextbox`.

Actions (тулбар): ✅ `MyAPIGateway.TerminalControls.CreateAction<IMyGyro>(id)` → `Name` (`StringBuilder`), `Icon`, `Action`, `Writer`, `ValidForGroups`, `Enabled`, `InvalidToolbarTypes` (список `MyToolbarType`), `MyAPIGateway.TerminalControls.AddAction<TBlock>(a)`.

Properties (для PB/модов, без GUI): ✅ `CreateProperty<Vector3, IMyGyro>(id)` + `Getter`/`Setter`; чтение — `block.GetValue<Vector3>("YourMod_SampleProp")`.

### 4.2. Гочи (✅ дословно из шапки примера — это самое ценное)

1. **«They are global per block type! Not per block instance, not per block type+subtype.»** → контрол создаётся один раз на **тип блока**, а не на кокпит. Состояние хранить в gamelogic/`Storage`, не в статике.
2. **«Should only be retrieved/edited/added after the block type fully spawned because of game bugs. Simplest way is with a gamelogic component via first update (**not `Init()`, that's too early**).»**
3. **«They're only UI! They do not save nor sync anything, they only read and call things locally.»** → сохранение и синхронизацию писать самим (§7.3).
4. **«`GetControls()`/`GetActions()` cause bugs with vanishing vanilla controls. Ideally avoid using these entirely»** — и обязательно тестировать с другими модами, которые тоже добавляют контролы на тот же тип блока.
5. ✅ «all these can also be called by mods and PBs, which also includes being called **dedicated-server-side**. Make sure your backend code does all the checks, including ensuring limits for sliders» — значение слайдера **не клампится игрой**.
6. ✅ Рекомендация: префикс `YourMod_` в id, чтобы не коллидировать с другими модами.

### 4.3. Кнопка именно в кокпите — да, это работает

✅ **VERIFIED, есть готовый пример ровно под нашу задачу** — [`TerminalControls/Events/TerminalEventsExamples.cs`](https://github.com/THDigi/SE-ModScript-Examples/blob/master/Data/Scripts/Examples/TerminalControls/Events/TerminalEventsExamples.cs):

```csharp
public override void BeforeStart()
{
    MyAPIGateway.TerminalControls.CustomControlGetter += CustomControlGetter;
    MyAPIGateway.TerminalControls.CustomActionGetter += CustomActionGetter;
}

void CustomControlGetter(IMyTerminalBlock block, List<IMyTerminalControl> controls)
{
    if(block is IMyCockpit)
    {
        if(SampleButton == null)
        {
            SampleButton = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyCockpit>("YourMod_SomeUniqueId");
            SampleButton.Action = (b) => { /* ... */ };
            SampleButton.Title  = MyStringId.GetOrCompute("Clickbait!");
            // Don't use AddControl() on controls meant to be added using this event.
        }
        controls.AddOrInsert(SampleButton, 4);   // позиция 4
    }
}
```

Гочи этого пути (✅ из комментариев):
- `CustomControlGetter` вызывается **каждый раз**, когда игроку нужно показать контролы, **и на каждый выделенный блок** при мультивыделении;
- **нельзя создавать контролы каждый вызов** — «mods could be storing the references … effectively leading to memory leaks» → кэшировать в поле;
- **нельзя мутировать существующие инстансы** из списка — «they're still the same global controls and doing so will affect any block that uses those controls»;
- ⚠️ контролы, добавленные через этот event, **PB не видит** (моды — видят, если сами подпишутся).

**Ответ на прямой вопрос из задания:** да, мод может добавить кнопку в терминал кокпита, и это документированный community-паттерн с рабочим примером. Работает на клиенте (UI); `Action` выполняется локально — если действие меняет состояние мира, его надо отправлять на сервер пакетом (✅ пример это прямо оговаривает на `cockpit.RemovePilot()`: «its xmldoc says "call on server" so it wouldn't work outside of singleplayer … you have to synchronize it manually by sending a packet to server»).

---

## 5. Перечисление блоков грида и модель «событий»

### 5.1. Как получить доступ к блокам

✅ **VERIFIED** ([Patch Changes/1.200](https://spaceengineers.wiki.gg/wiki/Modding/Reference/Patch_Changes/1.200)):

- `IMyGridTerminalSystem` в моде получается так: **`MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid)`** — это указано прямо в тексте патчноутов. У него есть события `GroupAdded`/`GroupRemoved` (1.200).
- `IMyCubeGrid` (1.200) получил: `GetGridGroup()`, **`GetFatBlocks<T>()`**, `ApplyDeformation()`, `OnGridMerge`, `ResourceDistributor`, `ConveyorSystem`, `WeaponSystem`, `ControlSystem`, `JumpSystem`, `GasSystem`, `IsConnectedTo()`, `GetConnectedGrids()`.
- `IMyGridGroups` (`MyAPIGateway.GridGroups`): `OnGridGroupCreated`, `OnGridGroupDestroyed`, `GetGridGroup()`, `GetGridGroups<T>()`, `AddGridGroupLogic<T>()`.
- `IMyCubeBlock` (1.202) получил `CubeGridChanged` ([1.202](https://spaceengineers.wiki.gg/wiki/Modding/Reference/Patch_Changes/1.202)).

⚠️ **UNVERIFIED:** `IMyCubeGrid.GetBlocks(...)` и `IMyGridGroupData` — в этой сессии подтверждения не нашёл. `GetFatBlocks<T>()` подтверждён патчноутами, `GetBlocks` — нет. Для тонких блоков (армор) в моде обычно идут через `grid.GetCubeBlock(MyCubeBlockDefinition)`/`grid.GetAllBlockPositions()` — **проверить декомпиляцией**.

✅ Для Ingame-подобного перебора есть стандартный `IMyGridTerminalSystem.GetBlocksOfType<T>(List<T>)`, `GetBlockWithName`, `GetBlocks(List<IMyTerminalBlock>, Func<IMyTerminalBlock,bool>)` — это описано в вики для PB ([Scripting/The Grid Terminal System](https://spaceengineers.wiki.gg/wiki/Scripting/The_Grid_Terminal_System)); ⚠️ применимость тех же сигнатур в моде — высокая, но формально не проверена.

### 5.2. Actions vs Events, и «есть ли API для перечисления событий»

✅ **VERIFIED:**

- **Actions** (то, что можно повесить на тулбар) — `ITerminalAction` (`Sandbox.ModAPI.Interfaces`), с `Id`, `Name`, `Icon`, `Writer`, `Apply(IMyTerminalBlock)`. Перечисление: через `CustomActionGetter` event (список, который **игра сама** наполнила для конкретного блока — самый надёжный способ узнать, что блок умеет) или `GetActions()` (⚠️ с оговоркой про исчезающие ванильные контролы, §4.2).
- **Properties** — `ITerminalProperty`/`ITerminalProperty<TValue>` + `TerminalPropertyExtensions` (`GetValue<T>`, `SetValue<T>`), см. [Scripting/Terminal Properties And Actions](https://spaceengineers.wiki.gg/wiki/Scripting/Terminal_Properties_And_Actions).
- **Actions с параметрами — новое в 1.211:** `IMyTerminalAction` получил `ParameterDefinitions`, `ActionWithParameters`, `DoUserParameterRequestWithItem` ([1.211](https://spaceengineers.wiki.gg/wiki/Modding/Reference/Patch_Changes/1.211)). Это очень важно для Stage 3: «Set Value»-экшены теперь типизированы параметрами.
- **1.211 развёл On/Off экшены:** у кокпитов появились `Dampeners On/Off`, `Park On/Off`, `Handbrake On/Off`; у колёс — `Steering On/Off`, `Propulsion On/Off`, `Brake On/Off`; у Landing Gear — `Autolock On/Off`; у Antenna — `Broadcast On/Off`. Плюс «Additional Set Value actions»: Event Controller `Set Threshold`, Light `Set Intensity/Blink Offset/Blink Length/Falloff`, Wheels `Set Strength/Friction/Height/Max Steering Angle`, Gravity Generator `Set Acceleration`, Timer Block `Set Delay` (источник: [официальный анонс 1.211](https://support.keenswh.com/spaceengineers/pc/announcement/update-1-211-warfare-3)). **Это буквально готовый «словарь выходов» для нашего графа.**

❌ **API для перечисления C#-событий (`event`) блока не существует.** Ни в вайтлисте, ни в патчноутах, ни в примерах нет механизма «дай мне список event'ов блока». Что делают моды/скрипты:

1. **Поллинг состояния в тике** и сравнение с предыдущим значением — стандарт для PB-скриптов и большинства модов («event» = изменение наблюдаемого свойства).
2. **Подписка на конкретные интерфейсные события**, которые Keen добавляет точечно: `IMyCubeBlock.CubeGridChanged` (1.202), `IMyCubeGrid.OnGridMerge` (1.200), `IMyGridTerminalSystem.GroupAdded/GroupRemoved` (1.200), `IMyGridGroups.OnGridGroupCreated/Destroyed` (1.200), `IMyEntity.RemovedFromScene` (1.200), `MyExplosions.OnExplosion` (1.200), `IMySession`/`IMyPlayerCollection.ItemConsumed` (1.200). Список растёт от патча к патчу — его надо вести вручную.
3. **Хук на уровне движка** (см. `ExampleWorkaround_GridLogicHook.cs`) для «на грид»-логики.

✅ Практический вывод для Stage 3: **«выходы» блока придётся моделировать самим** — как (a) список `ITerminalAction` + (b) список `ITerminalProperty` + (c) наш собственный набор «наблюдаемых» (поллинг `IMyFunctionalBlock.Enabled`, `IMyDoor`, `IMySensorBlock`, инвентари, `DetailedInfo`) с изменением-детекцией. Готового «графа событий блока» в API нет.

### 5.3. Как хостить «логику на грид»

✅ **VERIFIED и это критично для Stage 3** — [`ExampleWorkaround_GridLogicHook.cs`](https://github.com/THDigi/SE-ModScript-Examples/blob/master/Data/Scripts/Examples/ExampleWorkaround_GridLogicHook.cs), дословно:

> «This shows how to find all grids and execute code on them as if it was a gamelogic component. This is needed because **attaching gamelogic to grids does not work reliably, like not working at all for clients in MP**.»

Паттерн: `MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)` → в `LoadData()` подписка `MyAPIGateway.Entities.OnEntityAdd`, фильтр `ent as IMyCubeGrid`, словарь `Dictionary<long, IMyCubeGrid>`, подписка `grid.OnMarkForClose`, в `UpdateBeforeSimulation()` обход словаря с проверкой `grid.MarkedForClose`.

**Следствие:** «движок графа» должен жить в **session component** (один на мир), а не в gamelogic-компоненте на блоке/гриде.

---

## 6. Готовый переиспользуемый код: что реально можно взять

### 6.1. Шаблоны/каркасы мода

| Ресурс | Лицензия | Комментарий |
|---|---|---|
| [malforge/mdk2](https://github.com/malforge/mdk2) (MDK²) | ⚠️ не проверял | **Актуальный** тулкит: шаблоны проектов (PB и Mod), анализатор вайтлиста, интеграция с VS/VS Code/Rider. Вики прямо говорит: «This is not necessary to do manually if you use MDK2» |
| [THDigi/SE-ModScript-Examples](https://github.com/THDigi/SE-ModScript-Examples) | ⚠️ не проверял (есть `Examples.csproj`/`sln`) | Эталонные примеры: session/gamelogic, TerminalControls, network, storage, input, drawing |
| [viktor-ferenczi/se-mod-template](https://github.com/viktor-ferenczi/se-mod-template) | **MIT** | Шаблон проекта мода (обновлён 2026-09), но язык репозитория — Python (вероятно скрипты сборки/деплоя) |
| [jamac-dumas/SE-Mod-Template](https://github.com/jamac-dumas/SE-Mod-Template) | нет файла лицензии | Просто структура папок, 2022 |
| [PhoenixTheSage/ModTemplate_SE](https://github.com/PhoenixTheSage/ModTemplate_SE) | нет файла лицензии | «Lightweight ModAPI C# mod template with Rich Hud Hello World» — полезен как пример связки с RHF |
| [Intueor/SETestMod](https://github.com/Intueor/SETestMod) | **Apache-2.0** | «Template mod for Space Engineers with user interaction support» |

### 6.2. UI-фреймворки для SE

| Ресурс | Лицензия | Runtime? | Готовность |
|---|---|---|---|
| [RichHudFramework.Client](https://github.com/ZachHembree/RichHudFramework.Client) / [Master](https://github.com/ZachHembree/RichHudFramework.Master) / [Shared](https://github.com/ZachHembree/RichHudFramework.Shared) | **MIT** (все три) | ✅ да (билборды) | ⭐ Высокая: контролы, текст, кейбинды, настройки |
| [BuildVision2](https://github.com/ZachHembree/BuildVision2) | **MIT** | ✅ | ⭐ Пример «панель свойств блока» — почти наш Stage 2 |
| [TextEditorExample](https://github.com/ZachHembree/TextEditorExample) | ⚠️ не проверял | ✅ | Пример работы с вводом/кареткой/скроллом |

### 6.3. Node/graph-редакторы на C#: **главный вывод — почти всё непригодно**

Причина одна и она жёсткая: **мод компилируется против DLL игры и проходит IL-вайтлист**, в котором нет ни `UnityEngine.*`, ни `UnityEditor.*` ([Whitelist](https://spaceengineers.wiki.gg/wiki/Modding/Reference/Programming/Whitelist)). Плюс нельзя поставлять DLL (§1.2).

| Библиотека | Лицензия | Runtime (в Unity) | Пригодность для SE-мода |
|---|---|---|---|
| [Siccity/xNode](https://github.com/Siccity/xNode) | **MIT** | ⚠️ «Lightweight in runtime», «Strong separation of editor and runtime code», «No runtime reflection (unless you need to edit/build node graphs at runtime)» | ❌ **UI нельзя** (редактор — окно `UnityEditor`). ✅ **Можно взять идею/структуру** `Node`/`NodePort`/`NodeGraph` как образец модели данных, но код придётся переписать без `UnityEngine` (`ScriptableObject`, `[SerializeField]` — недоступны) |
| [alelievr/NodeGraphProcessor](https://github.com/alelievr/NodeGraphProcessor) | ⚠️ не проверял (есть package.json) | частично (обработка графа) | ❌ UI построен на **Unity UIElements + GraphView** → `UnityEditor`-only. Требует Unity 2020.2+. Непригодно |
| [Seneral/Node_Editor_Framework](https://github.com/Seneral/Node_Editor_Framework) | **MIT** | ✅ «**Complete runtime support** (see WebGL demo)», зум/панорамирование, сохранение в XML | ❌ технически «runtime» = «не UnityEditor», но реализация на **UnityEngine IMGUI (`OnGUI`)** → в SE не скомпилируется. ✅ Полезен как **референс архитектуры** (canvas, zoom/pan, XML-сериализация) |
| **Unity `UnityEditor.Experimental.GraphView`** | Unity EULA | ❌ **только редактор** | ❌ **Категорически нельзя** в рантайме/моде: это сборка `UnityEditor`, её нет в игре |
| **NodeCanvas** (ParadoxNotion) | платная, closed-source | ✅ в Unity | ❌ закрытый код + платно + Unity-зависимости |
| **Odin Inspector / Odin Serializer** (Sirenix) | платная, closed-source | ❌ (редактор: `Sirenix.OdinInspector.Editor.*`, окна инспектора) | ❌ нерелевантно |

**Формулировка для архитектора:** *UnityEditor-based graph views (включая `UnityEditor.Experimental.GraphView`, на котором построены NodeGraphProcessor и Shader Graph) **физически не могут** работать в рантайме мода Space Engineers — это отдельная редакторская сборка Unity, которой в игре нет.* Единственный реалистичный вариант — **свой минимальный графовый UI** поверх `MyModScreen`/`MyScreenSpace` (1.211) или поверх Rich HUD Framework, а из готового кода брать только **идеи** (порты, ноды, сериализация, zoom/pan).

---

## 7. Жёсткие ограничения и риски

### 7.1. Unity Editor UI — нельзя

❌ См. §6.3 и вайтлист. Мод не является Unity-проектом: он компилируется игрой из `.cs` против DLL игры (`Sandbox.Game.dll`, `VRage*.dll`, …) с IL-проверкой.

### 7.2. Рисование UI: только средства игры

Варианты (по убыванию «официальности»):
1. ✅ `MyModScreen` / `MyScreenSpace*` / `IMyGeneratedTexture` (1.211) — **новое, недокументированное**.
2. ⚠️ `MyGuiScreenBase`-наследник (движковый GUI) — доступен по вайтлисту, но без документации.
3. ✅ Билборды: `VRageRender.MyBillboard`, `MyTransparentGeometry.AddBillboard/AddQuad`, `MySimpleObjectDraw` (whitelisted) + свой input-routing. Так делает Rich HUD Framework. Референс-пример: [`Example_DrawingIn3D.cs`](https://github.com/THDigi/SE-ModScript-Examples/blob/master/Data/Scripts/Examples/Example_DrawingIn3D.cs), [`Example_AsyncDrawHook.cs`](https://github.com/THDigi/SE-ModScript-Examples/blob/master/Data/Scripts/Examples/Example_AsyncDrawHook.cs), [`Example_TSS.cs`](https://github.com/THDigi/SE-ModScript-Examples/blob/master/Data/Scripts/Examples/Example_TSS.cs).
4. ❌ ImGui / WPF / uGUI — невозможно (§3.4).

### 7.3. Сохранение и синхронизация состояния (мультиплеер)

✅ **VERIFIED, эталонная страница:** [Modding/Reference/Programming/Save_and_Sync](https://spaceengineers.wiki.gg/wiki/Modding/Reference/Programming/Save_and_Sync). Сводка вариантов:

| Механизм | Save | Sync | Ограничения |
|---|---|---|---|
| `IMyTerminalBlock.CustomData` | ✅ | ✅ | player-facing; нужен `MyIni`; ⚠️ `CustomDataChanged` **не срабатывает у того, кто изменил** → сравнивать через `object.ReferenceEquals` |
| **`MyModStorageComponent`** (`Entity.Storage[guid]`) | ✅ | ✅ (едет с сущностью при стриминге) | **GUID надо «застолбить» SBC-файлом** (`MyObjectBuilder_ModStorageComponentDefinition` + `<RegisteredStorageGuids>`), иначе игра вычистит незаявленные GUID'ы. Данные едут в копии/блюпринты/другим игрокам. Общий инстанс на сущность — «try not to break other mods» |
| `MyObjectBuilder_ModCustomComponent` | ✅ | ❌ | ⚠️ раздел помечен как stub, «Not tested» |
| **`MySync<T, SyncDirection>`** | ❌ | ✅ | только blittable-типы (`bool, int, float, double, enum, Color, Vector3, MyFixedPoint`); **лимит 64 sync-свойств на инстанс блока**, общий с самим блоком (у роторов уже ~30) |
| **Пакеты** `MyAPIGateway.Multiplayer.RegisterSecureMessageHandler/SendMessageToServer/SendMessageTo` | ❌ | ✅ | channelId **глобальный** → коллизии с другими модами; обязателен `UnregisterSecureMessageHandler`; «не доверять клиенту» |
| `MyAPIGateway.Utilities.SetVariable<T>/GetVariable<T>` | ✅ (в `sandbox.sbc`) | ✅ (приезжает с миром) | для серверных конфигов, доступно раньше всего |
| Файлы `WriteFileInGlobal/Local/World/ModLocation/GameContent` | ✅ | ❌ | без подпапок (кроме `ModLocation`/`GameContent`); ⚠️ **бэкапы мира не копируют `WorldStorage`** (баг) |

**Рекомендация под наш кейс:** граф — это данные **на грид** (или на кокпит) → `MyModStorageComponent` (сериализовать protobuf/JSON → base64-строка) + точечная синхронизация пакетами при редактировании. Готовый образец: [GravityCollector](https://github.com/THDigi/GravityCollector) (protobuf-класс на storage+sync, `IsSerialized`, дебаунс сохранения, `update10`) и [Example_NetworkProtobuf](https://github.com/THDigi/SE-ModScript-Examples/tree/master/Data/Scripts/Examples/Example_NetworkProtobuf).

⚠️ Протобуф: `ProtoBuf.Net.Core.dll` есть в игре, но в вайтлисте разрешены **только атрибуты** (`ProtoContract`, `ProtoMember`, …) — значит protobuf-net доступен, но, судя по вайтлисту, **без методов сериализации**; в примерах THDigi сериализация делается через `MyAPIGateway.Utilities.SerializeToBinary/SerializeToXML` (⚠️ точные имена методов не проверял — смотреть `GravityCollector`/`Example_NetworkProtobuf`).

### 7.4. Права/владение

⚠️ **UNCERTAIN.** В вайтлисте и патчноутах подтверждается наличие `OwnerId`/проверок доступа фрагментами (`MyJumpDrive.CanJumpIfFullAndHasAccess()`, `IMySession.TryGetAdminSettings()`, `MyAPIGateway.Session.Player`), но **единого проверенного рецепта «может ли этот игрок редактировать граф» я не нашёл**. Ожидаемые инструменты (требуют проверки): `IMyCubeBlock.OwnerId`, `IMyTerminalBlock.GetUserRelationToOwner(long playerId)`, `MyIDModule`, `MyAPIGateway.Session.Player`. **Вывод: делать проверку прав на сервере и не полагаться на клиент** (это ✅ прямо рекомендуется в Save_and_Sync: «you should have server-side checks for important stuff like whether the sender is an admin»).

### 7.5. Производительность

✅ **VERIFIED** ([Injected Profiler](https://spaceengineers.wiki.gg/wiki/Modding/Reference/Programming/Injected_Profiler)):
- игра **инжектит профайлер во все методы мода**, включая конструкторы структур, кастомные get/set-свойства, лямбды и анонимные методы (но **не** в auto-properties);
- «this injected code is very fast, but it can still add up if called thousands of times» → в горячем пути (тик графа) минимизировать количество собственных методов, использовать auto-properties, `Array.Sort(keys, values)` вместо компараторов, `while`+`Stack<T>` вместо рекурсии;
- результаты видны в `Shift+F1` (performance warnings), но метрика включает время загрузки/JIT → «potentially unfair assessment»;
- профилирующая сборка игры — `Bin64_Profile` (§8).

Практические правила (✅ из `Session.cs`): 60 Гц главный поток, «don't do too much in a tick or you'll lower sim speed», «avoid realtime allocations, re-use collections».

**Для нашего графа:** вычислять только «грязные» узлы, кэшировать, дебаунсить пересчёт, не аллоцировать в тике, не подписываться на события per-block (см. §7.6), ограничивать частоту (например, каждые N тиков).

### 7.6. Известные ловушки, специфичные для нашей идеи

| Риск | Детали | Источник |
|---|---|---|
| Gamelogic на грид ненадёжен в MP | «not working at all for clients in MP» → использовать session component + `MyAPIGateway.Entities.OnEntityAdd` | ✅ [GridLogicHook](https://github.com/THDigi/SE-ModScript-Examples/blob/master/Data/Scripts/Examples/ExampleWorkaround_GridLogicHook.cs) |
| Терминальные контролы — только UI | не сохраняют и не синхронизируют; состояние — наше | ✅ [GyroTerminalControls.cs](https://github.com/THDigi/SE-ModScript-Examples/blob/master/Data/Scripts/Examples/TerminalControls/Adding/GyroTerminalControls.cs) |
| `GetControls()`/`GetActions()` ломают ванильные контролы | избегать; тестировать с другими модами | ✅ там же |
| Ввод «протекает» в игру | проверять `MyAPIGateway.Gui.IsCursorVisible` / `ChatEntryVisible` | ✅ [Example_InputReading.cs](https://github.com/THDigi/SE-ModScript-Examples/blob/master/Data/Scripts/Examples/Example_InputReading.cs) |
| Конфликты id с другими модами | префикс `YourMod_` в id контролов/экшенов/проперти | ✅ |
| `channelId` пакетов глобальный | коллизии между модами | ✅ Save_and_Sync |
| `metadata.mod` не нужен | не тратить время | ✅ Creating And Uploading Mods |
| Бэкапы мира теряют `WorldStorage` | не хранить критичное только там | ✅ Save_and_Sync |
| Кнопка в кокпите: контрол общий на тип | фильтровать `Visible` по инстансу (например, по наличию нашего компонента) | ✅ |
| API 1.211 может требовать определённых условий | ⚠️ неизвестно (экспериментальный режим? сервер?) | ⚠️ |
| Обратная совместимость | мод с 1.211-API не загрузится на старых клиентах; для мультиплеера версии должны совпадать | ⚠️ логическое следствие, требует проверки |

---

## 8. `Bin64_Profile`, папки игры и деплой модов

### 8.1. Что такое `Bin64_Profile`

✅ **VERIFIED** ([Modding/OfficialTools](https://spaceengineers.wiki.gg/wiki/Modding/OfficialTools), [Scripting/Profiling the Game](https://spaceengineers.wiki.gg/wiki/Scripting/Profiling_the_Game)):

> `Bin64_Profile` — «a special build of the game that enables **developer screen** and **advanced profiling**».

- Это часть **Space Engineers ModSDK** (бесплатный в Steam → Tools), вместе с `OriginalContent` (исходные `.fbx/.hkt/.xml` моделей, включая DLC — читать `OriginalContent/Models/readme.txt` о правилах DLC) и `Tools`.
- Установка: скопировать папку `Bin64_Profile` **как есть в корень папки игры**. ⚠️ «**Do not paste into the game's `Bin64` folder.**»
- Даёт: developer screen + расширенные метрики производительности ([Modding/Debug_Views](https://spaceengineers.wiki.gg/wiki/Modding/Debug_Views)) и данные инжектированного профайлера (§7.5).
- Исторический нюанс: был баг «SDK bin64_profile does not have profiling or dev menu» ([Version/1.187.2](https://spaceengineers.wiki.gg/wiki/Version/1.187.2)).

**Это не «папка модов»** — это отдельный билд игры для профилирования. Для деплоя модов она не используется.

### 8.2. Где лежат моды

✅ **VERIFIED** ([Creating And Uploading Mods](https://spaceengineers.wiki.gg/wiki/Modding/Tutorials/Creating_And_Uploading_Mods)):

- Локальные моды: **`%APPDATA%\SpaceEngineers\Mods\<ModName>\`** (обычно `C:\Users\<User>\AppData\Roaming\SpaceEngineers\Mods`). Если папки `Mods` нет — создать.
- ✅ «There's also a Mods folder in the game's **install folder**, but that is **not used**.» → **не** `Content/Mods`, не `Bin64/Mods`.
- Локальный мод помечается «домиком» в списке модов; порядок загрузки — снизу вверх.
- ✅ Workshop-моды (подписки) игра держит в `%APPDATA%\SpaceEngineers\Mods\<workshopId>` (⚠️ либо в `Content/…` — точный путь подписок в этой сессии не проверял; для деплоя нашего мода это не важно).
- ✅ Хранилище мода: `%APPDATA%\SpaceEngineers\Storage\<workshopId>.sbm_<ScriptFolderName>\` (подтверждено и вики, и README Build Vision 2: `%AppData%\Roaming\SpaceEngineers\Storage\1697184408.sbm_BuildVision2`).
- ✅ Мир: `<WorldFolder>\Storage\<ModId.sbm>_<ModScriptFolder>\` (⚠️ не попадает в бэкапы мира).

### 8.3. Про «SDL mod folder»

⚠️ **НЕ НАЙДЕНО.** Ни в вики, ни в примерах, ни в документации нет сущности «SDL» применительно к модам Space Engineers. Наиболее вероятно — опечатка/сокращение от **SDK** (Space Engineers **ModSDK**, §8.1) либо от **SBC** (XML-определения). Отдельной папки `SDL` в modding-экосистеме SE не существует — **это утверждение об отсутствии, а не подтверждённый факт**; если у вас есть конкретный путь/упоминание, пришлите — проверю точечно.

---

## 9. Сводная таблица «что проверено / что нет»

| # | Утверждение | Статус |
|---|---|---|
| 1 | Актуальная версия `1.211.017`, target `net48`, `x64`, `LangVersion 6` | ✅ |
| 2 | `Data/` обязательна; скрипты в `Data/Scripts/<ModName>/`; одна папка = одна сборка | ✅ |
| 3 | `metadata.mod` не используется; `modinfo.sbmi` — для обновлений; `thumb.jpg/png` | ✅ |
| 4 | Мод не может поставлять `.dll/.exe`; компиляция игрой + IL-вайтлист | ✅ |
| 5 | `MySessionComponentBase` + `MySessionComponentDescriptor`, полный список хуков | ✅ |
| 6 | `MyAPIGateway` — главная точка входа; `InvokeOnGameThread` для маршалинга | ✅ |
| 7 | Терминальные контролы: полный API + 6 гочей (global per block type, не в `Init()`, UI-only, `GetControls` багованный) | ✅ |
| 8 | Кнопку в терминал **кокпита** можно добавить через `CustomControlGetter` | ✅ (пример есть) |
| 9 | `MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid)` → `IMyGridTerminalSystem` | ✅ (патчноут 1.200) |
| 10 | `IMyCubeGrid.GetFatBlocks<T>()`, `GetGridGroup()`, `IMyGridGroups.*` | ✅ (1.200) |
| 11 | API для перечисления C#-событий блока отсутствует | ❌ (проверено отсутствие) |
| 12 | `IMyTerminalAction` с параметрами (`ParameterDefinitions`, `ActionWithParameters`) | ✅ (1.211) |
| 13 | Новый официальный **Mod Screen API** (`MyModScreen`, `MyScreenSpace*`) | ✅ (анонс + патчноут), ⚠️ без документации |
| 14 | Rich HUD Framework: MIT, runtime, node-based UI tree, screen+world space, свои шрифты/кейбинды | ✅ |
| 15 | UnityEditor/UnityEngine/ImGui/WPF в моде невозможны | ✅ (вайтлист + запрет DLL) |
| 16 | `MyGuiScreenBase` доступен по вайтлисту, но паттерн открытия не подтверждён | ⚠️ |
| 17 | `EmptyKeys.UserInterface` в моде — нежизнеспособен | ⚠️ (высокая уверенность) |
| 18 | Сохранение/синхронизация: 6 механизмов с ограничениями (64 sync-свойства и т.д.) | ✅ |
| 19 | `MyModStorageComponent` требует «застолбить» GUID через SBC | ✅ |
| 20 | Gamelogic на грид не работает у клиентов в MP → session component + `OnEntityAdd` | ✅ |
| 21 | Инжектированный профайлер на каждый метод; оптимизации | ✅ |
| 22 | `Bin64_Profile` = профилирующий билд из ModSDK, копировать в корень игры | ✅ |
| 23 | Моды: `%APPDATA%\SpaceEngineers\Mods`; `Mods` в папке игры не используется | ✅ |
| 24 | `async/await` недоступен (нет `System.Threading.Tasks` в вайтлисте) | ✅ (по вайтлисту) |
| 25 | `IMyCubeGrid.GetBlocks`, `IMyGridGroupData`, `IsServer`, ownership-API | ⚠️ не проверено |
| 26 | Папка/сущность «SDL» | ❌ не найдено |

---

## TL;DR для архитектора

- **Ставка №1 — новый `Mod Screen API` из 1.211** (`MyModScreen`, `MyScreenSpace`, `MyScreenSpaceText/Sprite`, `IMyGeneratedTexture`): это официальный ответ на «как рисовать UI из мода», появился ровно в нужный момент. Но он **без документации и примеров** → первый шаг проекта — спайк на 1–2 дня: открыть пустой экран + текст + спрайт, проверить ввод/скролл/мультиплеер. Если не взлетит — переключаемся на план Б.
- **План Б — Rich HUD Framework (MIT)**: единственный проверенный в бою путь. Даёт готовые «retained-mode» контролы, свой bitmap-текст, кейбинды, настройки. Референсы под наши Stage 1–2: [BuildVision2](https://github.com/ZachHembree/BuildVision2) (панель свойств блока со скроллом) и [TextEditorExample](https://github.com/ZachHembree/TextEditorExample) (ввод/каретка).
- **Стек зафиксирован жёстко: C#, `net48`, `x64`, `LangVersion 6`.** Никаких Unity-библиотек, никаких DLL, никакого ImGui/WPF/EmptyKeys. Всё переиспользуемое — только исходниками `.cs`.
- **Графовых редакторов «из коробки» нет и быть не может**: xNode/NodeGraphProcessor/GraphView — UnityEditor-only, Node_Editor_Framework — Unity IMGUI. Из них берём **только идеи** (модель Node/Port, zoom/pan, XML-сериализация), UI пишем сами.
- **Кнопка «Grid setup» в кокпите — решённая задача**: `MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlButton, IMyCockpit>(...)` + `CustomControlGetter` (готовый пример в THDigi). Кэшировать контрол, не мутировать чужие, префикс `YourMod_`.
- **Контролы терминала — это только UI**: ни сохранения, ни синхронизации. Всё состояние графа — наша ответственность.
- **Данные графа хранить в `MyModStorageComponent` на гриде** (GUID «застолбить» SBC-файлом!), синхронизировать пакетами (`RegisterSecureMessageHandler`, свой channelId, серверная валидация). Образец: [GravityCollector](https://github.com/THDigi/GravityCollector).
- **Логика графа живёт в `MySessionComponentBase`**, а не в gamelogic на гриде: gamelogic на грид «не работает у клиентов в MP» (подтверждено примером-обходом). Обход графов — через `MyAPIGateway.Entities.OnEntityAdd` + `OnMarkForClose`.
- **«События/выходы» блока придётся моделировать самим**: API перечисления `event`'ов нет. База — `ITerminalAction` (+ новые параметризованные экшены 1.211: `Set Threshold/Intensity/Delay/...`) + `ITerminalProperty` + собственный поллинг с change-detection.
- **Производительность — реальный риск**: главный поток 60 Гц, профайлер инжектится в каждый наш метод. Инкрементальный пересчёт «грязных» узлов, дебаунс, zero-alloc, лимит N тиков между пересчётами.
- **Дисциплина по API**: `Sandbox.Game.*` в вайтлисте целиком (значит `MyGuiScreenBase` технически доступен), но «доступен» ≠ «поддерживается». Любой недокументированный вызов — только после проверки декомпиляцией `Bin64/Sandbox.Game.dll`.
- **Мелочи, которые сэкономят дни**: `metadata.mod` не нужен; `Data/` обязательна; моды деплоить в `%APPDATA%\SpaceEngineers\Mods`; `Bin64_Profile` — отдельный профилирующий билд из ModSDK, кладётся в корень игры; `async/await` недоступен.

---

## Список ссылок

### Официальные (Keen)
- Update 1.211 — Warfare 3 (changelog): <https://support.keenswh.com/spaceengineers/pc/announcement/update-1-211-warfare-3>
- Официальный API reference (ModAPI): <https://keensoftwarehouse.github.io/SpaceEngineersModAPI/api/index.html>
- ModSDK / Official Tools (вики): <https://spaceengineers.wiki.gg/wiki/Modding/OfficialTools>
- Официальный Mod Policy: <https://spaceengineers.wiki.gg/wiki/Modding/Official_Mod_Policy>
- Профилирование игры (вики): <https://spaceengineers.wiki.gg/wiki/Scripting/Profiling_the_Game>
- Debug views (developer screen): <https://spaceengineers.wiki.gg/wiki/Modding/Debug_Views>

### Вики (основные страницы)
- Modding (хаб): <https://spaceengineers.wiki.gg/wiki/Modding>
- Modding/Reference (индекс): <https://spaceengineers.wiki.gg/wiki/Modding/Reference>
- Mod Programming Intro: <https://spaceengineers.wiki.gg/wiki/Modding/Reference/Programming/Intro>
- **Whitelist** (v1.211.017): <https://spaceengineers.wiki.gg/wiki/Modding/Reference/Programming/Whitelist>
- **Save and Sync**: <https://spaceengineers.wiki.gg/wiki/Modding/Reference/Programming/Save_and_Sync>
- Injected Profiler: <https://spaceengineers.wiki.gg/wiki/Modding/Reference/Programming/Injected_Profiler>
- Creating And Uploading Mods: <https://spaceengineers.wiki.gg/wiki/Modding/Tutorials/Creating_And_Uploading_Mods>
- Setting Up a Modding Environment: <https://spaceengineers.wiki.gg/wiki/Modding/Tutorials/Setting_Up_a_Modding_Environment>
- Convert old projects to new SDK (`net48`): <https://spaceengineers.wiki.gg/wiki/Modding/Tutorials/Programming/Convert_old_projects_to_new_SDK>
- Localization: <https://spaceengineers.wiki.gg/wiki/Modding/Tutorials/Localization>
- Exploring the game code (декомпиляция): <https://spaceengineers.wiki.gg/wiki/Modding/Tutorials/Programming/Exploring_the_game_code>
- Scripting: The Grid Terminal System: <https://spaceengineers.wiki.gg/wiki/Scripting/The_Grid_Terminal_System>
- Scripting: Terminal Properties And Actions: <https://spaceengineers.wiki.gg/wiki/Scripting/Terminal_Properties_And_Actions>
- Patch Changes 1.200 (TerminalActionsHelper, GetFatBlocks, GridGroups): <https://spaceengineers.wiki.gg/wiki/Modding/Reference/Patch_Changes/1.200>
- Patch Changes 1.202 (CubeGridChanged, DetailedInfo): <https://spaceengineers.wiki.gg/wiki/Modding/Reference/Patch_Changes/1.202>
- **Patch Changes 1.211 (Mod Screen API)**: <https://spaceengineers.wiki.gg/wiki/Modding/Reference/Patch_Changes/1.211>

### Инструменты
- MDK² (актуальный тулкит): <https://github.com/malforge/mdk2> · docs: <https://malforge.github.io/spaceengineers/mdk2/>
- MDK-SE (устарел): <https://github.com/malware-dev/MDK-SE>
- Community API reference: <https://malforge.github.io/spaceengineers/modapi/>
- SEWorkshopTool: <https://github.com/Gwindalmir/SEWorkshopTool>
- XML-схемы SBC: <https://github.com/Equinox-/unofficial-keen-schemas>

### Примеры кода (THDigi)
- Репозиторий: <https://github.com/THDigi/SE-ModScript-Examples>
- Session/gamelogic: <https://github.com/THDigi/SE-ModScript-Examples/blob/master/Data/Scripts/Examples/BasicExample_GameLogicAndSession/Session.cs>
- Terminal controls (add): <https://github.com/THDigi/SE-ModScript-Examples/blob/master/Data/Scripts/Examples/TerminalControls/Adding/GyroTerminalControls.cs>
- Terminal controls (events, **кокпит**): <https://github.com/THDigi/SE-ModScript-Examples/blob/master/Data/Scripts/Examples/TerminalControls/Events/TerminalEventsExamples.cs>
- Grid logic hook (MP-обход): <https://github.com/THDigi/SE-ModScript-Examples/blob/master/Data/Scripts/Examples/ExampleWorkaround_GridLogicHook.cs>
- Input reading: <https://github.com/THDigi/SE-ModScript-Examples/blob/master/Data/Scripts/Examples/Example_InputReading.cs>
- Network protobuf: <https://github.com/THDigi/SE-ModScript-Examples/tree/master/Data/Scripts/Examples/Example_NetworkProtobuf>
- Drawing in 3D: <https://github.com/THDigi/SE-ModScript-Examples/blob/master/Data/Scripts/Examples/Example_DrawingIn3D.cs>
- Async draw hook: <https://github.com/THDigi/SE-ModScript-Examples/blob/master/Data/Scripts/Examples/Example_AsyncDrawHook.cs>
- Storage+sync образец (GravityCollector): <https://github.com/THDigi/GravityCollector>

### UI в модах
- Rich HUD Framework (Client, MIT): <https://github.com/ZachHembree/RichHudFramework.Client>
- Rich HUD Framework (Master): <https://github.com/ZachHembree/RichHudFramework.Master>
- Rich HUD Framework (Shared): <https://github.com/ZachHembree/RichHudFramework.Shared>
- Rich HUD Framework docs: <https://zachhembree.github.io/RichHudFramework.Client/>
- Text Editor Example: <https://github.com/ZachHembree/TextEditorExample>
- Build Vision 2 (MIT): <https://github.com/ZachHembree/BuildVision2>
- Build Vision (оригинал, Jimmacle, Workshop): <https://steamcommunity.com/sharedfiles/filedetails/?id=756792814>
- SISK localization script: <https://github.com/SiskSjet/SE_Mod_Utils/blob/master/Localization/LocalizationComponent.cs>

### Node/graph-редакторы
- xNode (MIT): <https://github.com/Siccity/xNode>
- NodeGraphProcessor (UIElements/GraphView): <https://github.com/alelievr/NodeGraphProcessor>
- Node Editor Framework (MIT, runtime): <https://github.com/Seneral/Node_Editor_Framework>
- Unity GraphView (редакторский API): <https://docs.unity3d.com/ScriptReference/Experimental.GraphView.GraphView.html>
- Odin Inspector (редакторский, платный): <https://odininspector.com/>

### Шаблоны проектов
- viktor-ferenczi/se-mod-template (MIT): <https://github.com/viktor-ferenczi/se-mod-template>
- PhoenixTheSage/ModTemplate_SE (Rich HUD Hello World): <https://github.com/PhoenixTheSage/ModTemplate_SE>
- Intueor/SETestMod (Apache-2.0): <https://github.com/Intueor/SETestMod>
- jamac-dumas/SE-Mod-Template: <https://github.com/jamac-dumas/SE-Mod-Template>

---

## Приложение: что делать дальше (проверки, которые нельзя сделать веб-ресёрчем)

1. **Спайк Mod Screen API (1.211).** Декомпилировать `Bin64/Sandbox.Game.dll` + `VRage*.dll`, найти `MyModScreen`, `MyScreenSpace`, `MyScreenSpaceText`, `MyScreenSpaceSprite`, `MyScreenSpaceLayer`, `IMyGeneratedTexture`; выяснить: как создать/показать экран, как приходит ввод, как рисовать скролл-контейнер, что с z-order и фокусом, работает ли на DS.
2. **`MyGuiScreenBase`.** Проверить в декомпиляции: конструктор, `MyGuiSandbox`/`MyGuiManager` методы добавления экрана, `HandleInput`, `Draw`. Только после этого рассматривать как альтернативу.
3. **Точные сигнатуры для перебора блоков:** `IMyCubeGrid.GetBlocks`, `GetAllBlockPositions`, `IMyGridGroupData`; `MyAPIGateway.Multiplayer.IsServer`; ownership (`OwnerId`, `GetUserRelationToOwner`, `MyIDModule`).
4. **Точный уровень C# компилятора игры** (проверить `LangVersion` выше 6 на практике: `out var`, локальные функции).
5. **Точные имена** `MyAPIGateway.Utilities.SerializeToBinary/SerializeToXML` и `MyAPIGateway.Input.GetMouseScroll`/mouse-методов.
6. **Проверить `metadata.mod`** на реальном локальном моде (вики говорит «не используется», но поведение стоит подтвердить на 1.211.017).
