# Язык игры из мода: что проверено

Задача: редактор должен говорить на языке, выбранном в настройках игры. Вопрос один —
**какое поле это значение хранит и разрешено ли моду его прочитать**. Ниже — проверенные
факты с командами и то, что осталось непроверенным.

Все команды выполнялись из корня репозитория против установленной игры
(`H:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64`).

## 1. Разрешённая точка входа — `IMyConfig.Language`

```powershell
& .\tools\dump-se-api.ps1 -Types 'VRage.Game.ModAPI.IMyConfig'
```

```csharp
public MyLanguagesEnum Language { get; }
```

- `VRage.Game.ModAPI.IMyConfig` — интерфейс, реализуемый `Sandbox.Engine.Utils.MyConfig`
  (проверено: `& .\tools\dump-se-api.ps1 -Types 'Sandbox.Engine.Utils.MyConfig'` →
  `interfaces: VRage.Game.ModAPI.IMyConfig`).
- Доступен как `MyAPIGateway.Session.Config`:
  ```powershell
  & .\tools\dump-se-api.ps1 -Types 'VRage.Game.ModAPI.IMySession'   # public IMyConfig Config { get; }
  & .\tools\dump-se-api.ps1 -Types 'Sandbox.ModAPI.MyAPIGateway'    # public static IMySession Session { get; set; }
  ```
- Пространство имён `VRage.Game.ModAPI` разрешено моду как пространство (в вайтлисте ровно
  это имя): `& .\tools\check-whitelist.ps1 -Dump`. Тип `VRage.MyLanguagesEnum` разрешён
  **поимённо** (там же, в списке `=== types ===`) — это важно, потому что корневое `VRage`
  не даёт разрешения на свои типы.

**Вывод: [ПРОВЕРЕНО]** Мод может прочитать `MyAPIGateway.Session.Config.Language` и получить
`MyLanguagesEnum`. Шлюз `check-whitelist.ps1` на собранной сборке подтверждает это на
реальном IL: `WHITELIST OK`.

## 2. Что именно возвращает `MyConfig.Language` — и почему это правильное значение

Это главный вопрос: «выбор игрока» или «язык ОС». Ответ — в IL геттера.

```powershell
& .\tools\dump-se-il.ps1 -Assembly 'Sandbox.Game.dll' -Type 'Sandbox.Engine.Utils.MyConfig' -Method 'get_Language'
```

Разбор:

| Шаг IL | Что делает |
|---|---|
| `ldfld LANGUAGE` + `MyConfigBase::GetParameterValue` | читает сохранённый параметр `LANGUAGE` из файла конфигурации |
| `MyUtils::GetByteFromString` | превращает его в `byte?` |
| `get_HasValue` / `Enum.IsDefined` | проверяет, что значение вообще есть и является членом `MyLanguagesEnum` |
| ветка «нет значения»: `MyLanguage::get_Instance` → `IMyLanguage::GetOsLanguageCurrentOfficial()` | **если игрок язык не выбирал, берётся официальный язык ОС** |
| ветка «есть значение»: `get_Value` | возвращает сохранённый выбор |

**Вывод: [ПРОВЕРЕНО]** `Config.Language` — это «выбор игрока, а если его нет — язык ОС»,
то есть ровно то значение, которое игра сама показывает в настройках. Отдельного решения
«спросить у ОС» моду принимать не нужно, и это хорошо: `MyLanguage` живёт в
`VRage.Game.Localization`, а это пространство имён моду **не** разрешено.

Сеттер (для полноты, мод его не вызывает):

```powershell
& .\tools\dump-se-il.ps1 -Assembly 'Sandbox.Game.dll' -Type 'Sandbox.Engine.Utils.MyConfig' -Method 'set_Language'
```

```il
ldfld LANGUAGE; ldarg.1; MyConfigBase::SetParameterValue; ret
```

**Вывод: [ПРОВЕРЕНО]** выбор игрока **сохраняется в конфигурации**, то есть переживает
перезапуск игры, и `Config.Language` читается одинаково до и после загрузки мира.

## 3. Смена языка без перезапуска

```powershell
& .\tools\find-se-type.ps1 -Name 'MyGuiManager'
& .\tools\dump-se-il.ps1 -Assembly 'Sandbox.Graphics.dll' -Type 'Sandbox.Graphics.MyGuiManager' -Method 'set_CurrentLanguage'
```

```il
ldsfld m_currentLanguage; ldarg.0; bne.un.s 1; ret
ldarg.0; stsfld m_currentLanguage
Sandbox.Graphics.MyGuiManager::InitFonts; ret
```

**Вывод: [ПРОВЕРЕНО, по IL]** у игры есть статическое поле текущего языка, которое
настройки меняют на месте, переинициализируя шрифты. Само поле моду недоступно
(`Sandbox.Graphics` нет в вайтлисте целиком), но из этого следует важное для нас:
**язык может измениться в любой момент сессии**, поэтому читать `Config.Language` один раз
при старте мода недостаточно, если экран открывается после смены языка.

**Вывод: [НЕ ПРОВЕРЕНО]** пишет ли игра `Config.Language` в тот же момент, когда меняет
`MyGuiManager.CurrentLanguage`, или только при сохранении конфигурации. Практическое
следствие, принятое в ADR-0033: мод перечитывает язык **при каждом открытии экрана**, и это
дёшево (одно свойство интерфейса); если игра пишет конфигурацию с задержкой, редактор
подхватит новое значение на следующем открытии, а не в тот же кадр. Проверяется человеком
в игре: сменить язык в настройках, открыть редактор, увидеть новый язык без перезапуска.

## 4. Почему не `MyTexts.resx` и не `MyStringId` для строк редактора

Ванильный путь локализации — `Data/Localization/MyTexts.resx` + `MyTexts.sbl` и
`MyStringId.GetOrCompute` (см. [`web-research.md`](web-research.md) §3.5). Он **не**
выбран для строк самого редактора, и причина не в невозможности, а в проверяемости:
таблица строк на C# компилируется вместе с моделью в `tools/offline-check.ps1`, и шлюз
проверяет, что каждая строка есть в каждом языке и что плейсхолдеры совпадают. С `.resx`
та же проверка требует запуска игры.

Для строк, которые попадают в **мир** (заголовок терминальной кнопки и чекбокса), путь
остаётся ванильным: `MyStringId.GetOrCompute` уже используется
(`Game/VgsTerminalControls.cs`), и это не меняется — см. ADR-0033 §Consequences.

**Проверено:** `MyStringId` разрешён моду — `VgsTerminalControls` использует его с первой
версии и мод грузится (`& .\tools\check-whitelist.ps1` → `WHITELIST OK`;
`VRage.Utils.MyStringId` в разрешённом пространстве `VRage.Utils`).

## 5. Что осталось непроверенным

| Утверждение | Как проверить |
|---|---|
| Смена языка в настройках доходит до `Config.Language` без перезапуска игры | В игре: сменить язык, открыть редактор, увидеть новый язык. Если нет — перезапустить и проверить снова; разница между «не дошло» и «дошло с задержкой» видна по логу `[VGS] language ...` |
| Русский шрифт редактора содержит все глифы перевода | В игре: открыть редактор на русском, осмотреть все надписи на предмет «квадратов». Шрифт выбирается в `VgsScreenDraw`, а не модом |
| Терминальная кнопка переводится без перезапуска мира | Ожидаемо **нет**: `MyStringId` резолвится при регистрации контрола. Записано как ограничение в ADR-0033 |
