# Поиск блоков в терминале и ввод с клавиатуры: проверенный контракт

Два вопроса, на которые этот документ отвечает фактами, а не догадками:

1. **Каково правило фильтрации блоков в терминале игры** — то самое, которое владелец
   продукта потребовал повторить в редакторе («строку поиска блоков сделать по
   функциональности идентичной той, которая используется в терминале»).
2. **Как моду читать ввод с клавиатуры** в своём экране: посимвольно, без задержек и без
   потери символов, и какими клавишами при этом пользоваться.

Дата проверки: 2026-11-23. Игра: `H:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64`.
Всё, что помечено **[ПРОВЕРЕНО]**, получено рефлексией/IL установленной сборки или
компиляцией через шлюзы проекта; рядом стоит команда.

Документ — **факты**, а не решение. Принятое по ним решение —
[ADR-0023](../adr/0023-workbench-bar-modes-and-search.md).

---

## 0. Краткий ответ

| Вопрос | Ответ | Чем |
|---|---|---|
| Правило фильтра | `text.Split(' ')`; блок виден, если **каждый** терм встречается как подстрока в имени блока **или** в его отображаемом типе; сравнение без учёта регистра | [ПРОВЕРЕНО] §1 |
| Текст ищется по двум полям | имя блока (`Item.Text`) и **локализованное название типа** из тултипа (`ToolTip.ToolTips[0].Text`) | [ПРОВЕРЕНО] §1 |
| Чем взять название типа моду | `IMyCubeBlock.DefinitionDisplayNameText` — то же, что видит игрок в терминале | [ПРОВЕРЕНО] §1.1 |
| Пустой запрос | показывает всё | [ПРОВЕРЕНО] §1 |
| Чтение символов в моде | `IMyInput.TextInput` — `ListReader<char>` символов, набранных за последний кадр | [ПРОВЕРЕНО] §2 |
| Буфер обнуляется сам | да: игра отдаёт накопленное и подменяет буфер, поэтому каждый символ приходит ровно один раз | [ПРОВЕРЕНО] §2.2 |
| Клавиши редактирования | `IMyInput.IsNewKeyPressed(MyKeys.Back)` / `MyKeys.Enter` / стрелки / `Home` / `End`; модификаторы — `IsAnyCtrlKeyPressed`, `IsAnyShiftKeyPressed` | [ПРОВЕРЕНО] §2.3 |
| Текстовое поле игры | существует, но моду **запрещено**: `Sandbox.Graphics` нет в вайтлисте | [ПРОВЕРЕНО] §2.3а |
| Буфер обмена | **недоступен моду** (вердикт игры): `IVRageSystem`/`IVRagePlatform`/`MyVRage` — `is prohibited`; разрешение пространства имён `VRage` не покрывает типы прямо в нём | [ПРОВЕРЕНО] §2.3б |
| Счётчик кадров на паузе | `VgsSession.Frame` **не растёт**, пока открыт экран: пауза пропускает `UpdateAfterSimulation`, а `UpdateOnPause` у мода не выставлен. `MyModScreen.OnUpdate` при этом вызывается | [ПРОВЕРЕНО] §2.5 |
| Тип `ListReader<char>` разрешён моду | да, `VRage.Collections.ListReader\`1` в вайтлисте | [ПРОВЕРЕНО] §2.4 |

---

## 1. Правило фильтрации терминала

[ПРОВЕРЕНО] `tools/dump-se-il.ps1 -Type 'Sandbox.Game.Gui.MyTerminalControlPanel'
-Method 'blockSearch_TextChanged'` — обработчик строки поиска списка блоков терминала.
Он перегружен, и IL обеих версий читается так:

```csharp
void blockSearch_TextChanged(string text) => blockSearch_TextChanged(text, true);

void blockSearch_TextChanged(string text, bool scrollToTop)
{
    if (m_blockListbox == null) return;

    if (text != "")                              // пустой запрос — ветка "показать всё"
    {
        string[] terms = text.Split(' ');        // разделитель — ровно пробел

        foreach (MyGuiControlListbox.Item item in m_blockListbox.Items)
        {
            bool visible = true;

            // Блоки, у которых выключен ShowInTerminal, скрываются отдельным правилом
            // (m_showAllTerminalBlocks) — к строке поиска оно отношения не имеет.

            string name = item.Text.ToString().ToLower();
            string type = "";
            if (item.ToolTip.ToolTips.Count > 0)
                type = item.ToolTip.ToolTips[0].Text.ToString().ToLower();

            foreach (string raw in terms)
            {
                string term = raw.ToLower();
                if (!name.Contains(term) && !type.Contains(term))
                    visible = false;             // достаточно одного несовпавшего терма
            }

            item.Visible = visible;
        }
    }

    // вторая ветка (text == "") — показать всё, что разрешено ShowInTerminal

    if (scrollToTop) m_blockListbox.ScrollToolbarToTop();
}
```

Что из этого — правило, а что — деталь реализации:

- **AND по термам, OR по двум полям, подстрока, без учёта регистра.** Это и есть ответ на
  вопрос 1. `Contains` — подстрочный поиск, а не поиск по началу слова: `imer` найдёт
  `Timer`.
- **Разделитель — одиночный пробел**, не запятая и не `StringSplitOptions.RemoveEmptyEntries`:
  двойной пробел даёт пустой терм, а пустая строка — подстрока любой строки, поэтому на
  результат он не влияет. В редакторе пустые термы отбрасываются при разборе — это
  эквивалентно, а не упрощение.
- **`ToLower()` без культуры** — то есть с текущей культурой. В редакторе взято
  `OrdinalIgnoreCase`: разница проявляется только там, где культура переводит букву в другую
  букву (турецкая `i`), а имена блоков на практике ASCII, зато `OrdinalIgnoreCase` не
  аллоцирует строку на каждую карточку каждый кадр.
- **ScrollToTop не имеет аналога в редакторе**: там нет списка, а есть поле, и «прокрутка к
  началу» заменена переходом к первому совпадению (см. ADR-0023).

### 1.1. Чем моду взять «название типа» — и почему не подтипом

Второе поле поиска в игре — **локализованное название типа**, то есть та строка, которую игрок
видит в списке блоков терминала («Timer Block», «Таймер»), а не `SubtypeId` («LargeBlockTimer»).

[ПРОВЕРЕНО] `tools/dump-se-api.ps1 -Types 'VRage.Game.ModAPI.Ingame.IMyCubeBlock'`:

```csharp
public String DefinitionDisplayNameText { get; }
public String DisplayNameText { get; }
```

`DefinitionDisplayNameText` — название типа из определения блока, на языке игрока;
`DisplayNameText` — то же, но с учётом переименования. Пространство `VRage.Game.ModAPI.Ingame`
разрешено моду ([ПРОВЕРЕНО] `tools/check-whitelist.ps1 -Dump`).

Подтип для поиска не годится по двум причинам, и обе наблюдаемые:

- игрок не набирает «LargeBlockTimer» — он набирает «timer» или «таймер»;
- подтип не переводится: в русской игре у блока тип «Таймер», а подтип остаётся латинским.

Поэтому `Node` несёт `TypeDisplayName` (читается в `GridGraphBuilder` при построении графа),
и поиск ищет по нему; **это же поле рисуется второй строкой карточки**
([ADR-0043](../adr/0043-card-type-line-is-localized.md)) — подпись и поиск обязаны называть блок
одинаково. Откат при отказе чтения — подтип, а если и он пуст (у игры есть «default»-определения
с пустым `<SubtypeId>`), — переведённая строка `VgsText.BlockTypeUnknown`: пустая строка молча
убрала бы тип и из поиска, и с карточки, что хуже любого запасного ответа.

Проверка правила на примерах, которые стоит держать в голове при ревью. Колонка «Тип» — то
самое второе поле, то есть **отображаемое** название (см. §1.1), а не подтип:

| Запрос | Имя блока | Тип (отображаемый) | Виден | Почему |
|---|---|---|---|---|
| `tim` | `Timer 1` | `Timer Block` | да | подстрока в имени |
| `tim` | `Startup` | `Timer Block` | да | подстрока в типе |
| `таймер` | `Блок 1` | `Таймер` | да | подстрока в типе, на языке игрока |
| `timer block` | `Timer 1` | `Timer Block` | да | каждый терм — в своём поле |
| `timer block` | `Timer 1` | `Small Timer` | нет | `block` не найдено нигде |
| `z` (пробел в конце) | `Timer 1` | `Timer Block` | да | пустой терм ничего не скрывает |

Для сравнения, тот же запрос по подтипу дал бы другой ответ, и именно поэтому подтип не годится:
запрос `таймер` не нашёл бы ничего ни в `LargeBlockTimer`, ни в `SmallBlockTimer`.

---

## 2. Ввод с клавиатуры в моде

### 2.1. Где он есть в API

[ПРОВЕРЕНО] `tools/dump-se-api.ps1 -Types 'VRage.ModAPI.IMyInput'`:

```csharp
public ListReader<Char> TextInput { get; }
public Boolean IsNewKeyPressed(MyKeys key)
public Boolean IsKeyPress(MyKeys key)
```

`IMyInput` — это `MyAPIGateway.Input`, тип разрешён моду (пространство `VRage.ModAPI` есть в
вайтлисте). `TextInput` — **свойство**, а не событие: символы накапливаются и забираются
чтением.

### 2.2. Буфер подменяется при чтении, поэтому символ не теряется и не дублируется

Это главный факт, без которого посимвольный ввод либо дублировал бы символы, либо съедал их.

[ПРОВЕРЕНО] `tools/dump-se-il.ps1 -Assembly 'VRage.Input.dll' -Type 'VRage.Input.MyVRageInput'
-Method 'Update'` — последние инструкции метода:

```csharp
UpdateStates();
return m_platformInput.GetBufferedTextInput(ref m_currentTextInput);
```

[ПРОВЕРЕНО] `tools/dump-se-il.ps1 -Assembly 'VRage.Platform.Windows.dll'
-Type 'VRage.Platform.Windows.Forms.MyGameForm' -Method 'GetBufferedTextInput'`:

```csharp
swappedBuffer.Clear();
using (m_bufferedCharsLock.AcquireExclusiveUsing())
{
    List<char> mine = swappedBuffer;
    swappedBuffer = m_bufferedChars;   // отдать накопленное
    m_bufferedChars = mine;            // себе — освобождённый список
}
```

То есть: игра отдаёт **всё, что набрано с прошлого кадра**, и подменяет свой буфер на пустой.
Следствия:

- читать `TextInput` можно **раз в кадр** — каждый символ придёт ровно однажды;
- никто не обязан «очищать» буфер, и мод, который этого не делает, не получит символ дважды;
- порядок символов — порядок нажатий, что позволяет просто дописывать их в конец строки.

[ПРОВЕРЕНО] `tools/dump-se-il.ps1 -Assembly 'VRage.Input.dll' -Type 'VRage.Input.MyVRageInput'
-Method 'get_TextInput'`: геттер — `new ListReader<char>(m_currentTextInput)`, то есть
`ListReader` — обёртка над тем же списком, а не копия. Читать его нужно в том же кадре, в
котором он получен.

### 2.3. Клавиши редактирования

[ПРОВЕРЕНО] `tools/dump-se-api.ps1 -Types 'VRage.Input.MyKeys'` — перечисление содержит
`Enter`, `Back`, `Delete`, `Escape`, `Space`, `Home`, `End`, `Left`, `Right`, `A`, `C`, `V`, `X`.
Для строки поиска нужны `Enter` (перейти к первому совпадению), `Back`/`Delete` (стирать),
`Left`/`Right`/`Home`/`End` (каретка) и `A`/`C`/`V`/`X` под Ctrl (выделение и буфер).

[ПРОВЕРЕНО] `tools/dump-se-api.ps1 -Types 'VRage.ModAPI.IMyInput'` — модификаторы читаются как
`IsAnyCtrlKeyPressed()`, `IsAnyShiftKeyPressed()`, `IsAnyAltKeyPressed()`. Это «зажат», а не
«нажат в этом кадре», что и нужно для Ctrl+C.

`IsNewKeyPressed` — «нажата в этом кадре», а не «зажата»: удержание Back не стирает символы
потоком, что для одного-двух символов запроса достаточно и избавляет от собственного
повторителя.

**Порядок важен.** Ctrl+C тоже порождает символ (0x03), а Backspace — 0x08: оба отсеиваются
фильтром управляющих символов, но разбирать сочетания надо **до** потока символов, а поток
читать **всегда** и последним — иначе необработанный символ переживёт кадр и придёт дважды
(§2.2).

**Escape не нужен:** экран объявляет `CloseOnEscape`, и игра сама зовёт
`MyModScreen.OnCancelling()`, где мод решает, закрывать ли экран.

### 2.3а. Текстовое поле игры моду недоступно

[ПРОВЕРЕНО] `tools/find-se-type.ps1 -Name 'MyGuiControlTextbox*'`:

```
Sandbox.Graphics  class  Sandbox.Graphics.GUI.MyGuiControlTextbox
```

[ПРОВЕРЕНО] `tools/check-whitelist.ps1 -Dump` — `Sandbox.Graphics` **отсутствует** среди 57
разрешённых пространств имён. Значит ссылка на поле компилируется и отклоняется игрой при
загрузке. Внутри поля есть готовое `MyGuiControlTextboxSelection.PasteText`, то есть игра так
умеет — но не для мода.

### 2.3б. Буфер обмена моду недоступен (проверено игрой)

Здесь я ошибался дважды, и оба раза — на выводе, а не на факте. Записано целиком, потому что
это самый поучительный кусок в заметке.

**Факт 1.** `VRage.Platform.Windows.Forms.MyClipboardHelper` — public, и у него есть
`GetClipboardText()`. Его пространство имён моду не разрешено.

**Факт 2.** `VRage.MyVRage.Platform` → `IVRagePlatform.System` → `IVRageSystem.Clipboard
{ get; set; }` существуют, и геттер действительно читает буфер:

```il
get_Clipboard  ->  call MyClipboardHelper::GetClipboardText
GetClipboardText -> call System.Windows.Forms.Clipboard::GetText
```

**Неверный вывод (ADR-0025).** «Тип лежит в разрешённом пространстве имён `VRage` ⇒ мод может его
использовать».

**Вердикт игры (2026-11-23).** Мод не загрузился:

```
TextClipboard.cs(159,16): Error: The type or member 'IVRageSystem' is prohibited
TextClipboard.cs(207,42): Error: The type or member 'MyVRage' is prohibited
TextClipboard.cs(207,16): Error: The type or member 'IVRagePlatform' is prohibited
```

**Правило, которое из этого следует.** Для **корневого** `VRage` разрешение пространства имён не
покрывает типы: те, что можно, перечислены поимённо (`VRage.MyTexts`, `VRage.MyTuple`,
`VRage.MyLanguagesEnum`, `VRage.Input.MyKeys`, …). Подпространства ведут себя иначе —
`VRage.Utils` разрешено как пространство, и `VRage.Utils.MyLog` (логирование мода) работает.

**Итог:** читать системный буфер мод не может. Писать — через
`VRage.Utils.MyClipboardHelper.SetClipboard` — **не проверено**, поэтому считается запрещённым
(ошибка в сторону «нельзя» стоит ложной тревоги, ошибка в сторону «можно» стоит неработающего
мода). Действующая норма — [ADR-0027](../adr/0027-clipboard-is-own-buffer-only.md); буфер только
собственный, `Game/TextClipboard.cs` не ссылается ни на один тип игры.

**Почему шлюз это пропустил.** `check-whitelist.ps1` печатал `WHITELIST OK` на том же билде.
Декодер вайтлиста определял цель записи (`ModApi`/`Ingame`/`Both`) эвристикой «два целых числа
назад от `newarr`», а когда массив строится через `GetTypes().Where(...).ToArray()`, `newarr` в IL
нет и цель бралась от предыдущего вызова — записи, разрешённые только Ingame-скриптам, попадали
в набор для модов. Исправлено: корневое `VRage` теперь требует поимённой записи, а `-SelfTest`
содержит вердикты игры (включая эти три отказа) и падает, если шлюз перестал с ними совпадать.
Подробности — ADR-0027 §3.


### 2.4. Вайтлист

[ПРОВЕРЕНО] `tools/check-whitelist.ps1 -Dump` — среди разрешённых для мода:

```
TYPE  VRage.Collections.ListReader`1
TYPE  VRage.ModAPI.IMyInput
TYPE  VRage.Input.MyKeys
```

То есть тип, который возвращает `TextInput`, и перечисление клавиш разрешены; компиляция и
вайтлист проверены на самом моде (`COMPILE OK`, `WHITELIST OK`).

### 2.5. Экран на паузе: `VgsSession.Frame` не растёт, `OnUpdate` — растёт

Это самый дорогой по последствиям факт из всей страницы, и найден он был поздно — только когда
каретка поля поиска перестала мигать.

Цепочка, каждое звено проверено по IL:

| Шаг | Проверка |
|---|---|
| Экран ставит игру на паузу | `MyModScreen.Open`: `m_pausesGameInSinglePlayer && !MultiplayerActive → PausePush()`, затем `m_pausePushed = true` |
| Симуляция на паузе пропускается | `MySession.Update`: `if (IsPaused) ret` **до** `UpdateComponents` |
| Вместо неё — только opt-in компоненты | `MySession.UpdateComponentsWhilePaused` вызывает `UpdateBeforeSimulation` / `Simulate` / `UpdateAfterSimulation` только если `get_UpdateOnPause()` истинно |
| Мод не opt-in | `VgsSessionComponent` объявлен `[MySessionComponentDescriptor(AfterSimulation)]` и `UpdateOnPause` не выставляет; конструктор базового класса его не копирует (единственный писатель `set_UpdateOnPause` во всех сборках — `MySectorWeatherComponent.Init`) |
| Значит счётчик кадров мода замер | `VgsSession.AdvanceFrame()` имел ровно одного вызывающего — `UpdateAfterSimulation` (метод с тех пор удалён, см. ниже) |

**Следствие.** Пока открыт редактор, `VgsSession.Frame` был постоянен. Всё, что на нём висело,
переставало идти: мигание каретки, истечение предупреждений, троттлинг обновления схемы, троттлинг
сохранения вида, повтор иконочных текстур, троттлинг лога.

**Гейт паузы обходит только GUI-хост экрана:**

[ПРОВЕРЕНО] `MyGuiScreenModHost.Update` — `MyGuiScreenBase.Update`, затем `if (IsCurrentHost)
m_owner.OnUpdate(hasFocus)`, без каких-либо проверок паузы. Поэтому `OnUpdate` экрана на паузе
вызывается, и собственный счётчик кадров там — правильное лекарство.

`UpdateOnPause = true` у сессионного компонента тоже «починил» бы счётчик, но заставил бы весь мод
работать в приостановленной сессии и в меню паузы; это изменение шире, чем нужно, и потому
отклонено ([ADR-0025](../adr/0025-clipboard-read-via-platform.md) §2а).

**Что с этим сделано.** Счётчик `VgsSession.Frame` и его `AdvanceFrame()` удалены вместе с
последним читателем: после перевода всех потребителей на другие часы читателей не осталось, а
счётчик, который кто-то увеличивает и никто не читает, — приглашение снова на него опереться.
Нормативное правило (какие часы где допустимы) живёт в
[ADR-0026](../adr/0026-clocks-in-the-mod.md); здесь — только запись исследования, чтобы было
видно, откуда правило взялось.

---

## 3. Где ещё это пригодилось: состояние вида на гриде

Отдельный факт, нужный соседнему решению ([ADR-0022](../adr/0022-view-state-lives-on-the-grid.md)):
камеру, зум и переключатель «показывать скрытые» нужно хранить **на гриде**, а не на кокпите.

[ПРОВЕРЕНО] `tools/dump-se-api.ps1 -Types 'VRage.Game.ModAPI.IMyCubeGrid'`: интерфейс
наследует `VRage.ModAPI.IMyEntity`, а `IMyEntity.Storage` — это
`MyModStorageComponentBase { get; set; }`. Значит, у грида есть та же таблица
`Guid → String`, что и у блока.

[ПРОВЕРЕНО] `tools/dump-se-api.ps1 -Types 'VRage.ObjectBuilders.MyObjectBuilder_EntityBase'`:
поле `MyObjectBuilder_ComponentContainer ComponentContainer` объявлено на базовом билдере
сущности, а `MyObjectBuilder_CubeGrid` от него наследует. Значит, содержимое
`MyModStorageComponent` грида попадает в сейв тем же механизмом, что и у блока, и
регистрация GUID в `Data/EntityComponents.sbc` для него так же обязательна (см.
[api-persistence.md](api-persistence.md) §2.1).

---

## 4. Чем проверено

```powershell
# правило фильтрации терминала
& .\tools\dump-se-il.ps1 -Type 'Sandbox.Game.Gui.MyTerminalControlPanel' -Method 'blockSearch_TextChanged'

# ввод с клавиатуры: буфер подменяется при чтении
& .\tools\dump-se-il.ps1 -Assembly 'VRage.Input.dll' -Type 'VRage.Input.MyVRageInput' -Method 'Update'
& .\tools\dump-se-il.ps1 -Assembly 'VRage.Input.dll' -Type 'VRage.Input.MyVRageInput' -Method 'get_TextInput'
& .\tools\dump-se-il.ps1 -Assembly 'VRage.Platform.Windows.dll' -Type 'VRage.Platform.Windows.Forms.MyGameForm' -Method 'GetBufferedTextInput'
& .\tools\dump-se-il.ps1 -Assembly 'VRage.Platform.Windows.dll' -Type 'VRage.Platform.Windows.Forms.MyGameForm' -Method 'VRage.IVRageInput.AddChar'

# состав API и вайтлист
& .\tools\dump-se-api.ps1 -Types 'VRage.ModAPI.IMyInput','VRage.Input.MyKeys','VRage.Collections.ListReader`1'
& .\tools\dump-se-api.ps1 -Types 'VRage.Game.ModAPI.IMyCubeGrid','VRage.ObjectBuilders.MyObjectBuilder_EntityBase'
& .\tools\check-whitelist.ps1 -Dump

# текстовое поле и буфер обмена (ADR-0024)
& .\tools\find-se-type.ps1 -Name 'MyGuiControlTextbox*'
& .\tools\dump-se-api.ps1 -Types 'VRage.Utils.MyClipboardHelper' -IncludeNonPublic
& .\tools\dump-se-api.ps1 -Types 'Sandbox.Graphics.MyClipboardService'

# шлюзы после реализации
& .\tools\verify-compile.ps1        # COMPILE OK
& .\tools\check-whitelist.ps1       # WHITELIST OK
& .\tools\review-check.ps1          # без находок
& .\tools\offline-check.ps1         # OFFLINE OK — правило поиска и формат вида проверяются там
```

---

## 5. Что осталось непроверенным

| Утверждение | Почему не проверено | Чем проверять |
|---|---|---|
| Символы доходят до экрана `MyModScreen` и не съедаются игрой по дороге | Нужен запуск игры | человек: открыть схему, набрать `tim` — в поле появляется `tim`, карточки гаснут |
| Ввод не «проваливается» в игру под экраном | Нужен запуск игры | то же: набирать имя блока, корабль при этом не должен реагировать на WASD (в одиночной игре мир на паузе) |
| Раскладка клавиатуры не влияет на `TextInput` | В IL видно, что символы берутся из буфера WM_CHAR платформы, но поведение для не-латинских раскладок не проверялось | человек: переключиться на русскую раскладку и набрать имя блока латиницей/кириллицей |
| Одновременное нажатие Enter и печать символа в одном кадре | Нужен запуск игры | человек: быстро набрать и нажать Enter |
| Ctrl+клавиши не перехватываются игрой раньше экрана | Нужен запуск игры | человек: Ctrl+A в поле выделяет запрос, а не «выделяет всё» в игре |
| Запись в системный буфер доходит до ОС | Нужен запуск игры | человек: Ctrl+C в поле, затем вставка в блокнот Windows |
| Чтение системного буфера работает на потоке игры | Нужен запуск игры; главный поток может оказаться MTA, тогда `Clipboard.GetText()` бросает `ThreadStateException` на каждой вставке | человек: скопировать в блокноте, Ctrl+V в поле — если не вставилось, посмотреть лог на строку `clipboard.read` |
| `OnUpdate` экрана идёт с частотой кадров (от этого зависит период мигания) | Нужен запуск игры; хост может троттлить | человек: каретка мигает с периодом ~1 с |

Ни одно из этих «не проверено» не мешает принять решение: все они — про наблюдаемое
поведение уже существующего механизма.
