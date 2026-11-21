# 0012. Раскладка схемы хранится на блоке, а не в таблице «id → позиция»

- Status: accepted
- Date: 2026-11-21
- Supersedes: —
- Amends: — (решение не трогает [0011](0011-toolbar-is-the-connection.md): тулбар остаётся
  хранилищем связей; здесь речь о другом предмете — где нарисована карточка)
- Joint review: не требовалось — требования («позиции возвращаются при следующем открытии
  схемы», «и после загрузки сохранённой игры») пришли от владельца продукта. Факты API
  проверены до решения, см. §Verification
- Implementation: **done** (2026-11-21) — `Game/NodeStateStore.cs`, `Model/NodeStateFormat.cs`,
  `Node.Placed`, `Data/EntityComponents.sbc`; шлюзы зелёные, приёмка в игре — за человеком
  (§Verification)

## Context

### 1. Позиции сейчас не переживают закрытие экрана

Это не догадка, а чтение кода:

- `VgsEditorScreen.OpenFor` каждый раз вызывает `GridGraphBuilder.Build(grid)` — строит
  **новый** `GridGraph`; узлы создаются с `Position = Vector2.Zero` (`Node..ctor`), после чего
  `GridGraph.LayoutUnplaced()` раскладывает их «как корабль»;
- `GridGraphBuilder.Refresh` позиции выживших узлов сохраняет — поэтому в пределах одного
  открытия перетаскивание живёт;
- `OnClosed` выбрасывает граф: `_openEditor = null`, ссылки на узлы обнулены.

Следствие: позиция живёт ровно столько, сколько открыт экран. Сохранение игры тут ни при чём —
терять нечего.

### 2. Ключ «id блока» нестабилен там, где это важно

`EntityId` — первое, что приходит в голову как ключ таблицы. Он переживает сохранение, но
**не** переживает копирование, проектор и резку грида:

| Ключ | save/load | блюпринт/проектор | резка/слияние грида |
|---|---|---|---|
| `EntityId` | да | **нет** | **нет** |
| данные на самом блоке | да | да | да |

[ПРОВЕРЕНО] по IL установленной сборки:

- `MyObjectBuilder_CubeBlock.ShouldSerializeEntityId` → `EntityId > 0`; `MyEntity.Init(builder)`
  при `builder.EntityId != 0` делает `set_EntityId(builder.EntityId)`, иначе
  `AllocateEntityID()`. Значит, id **пишется в сейв и возвращается из него**;
- `MyObjectBuilder_CubeBlock.Remap` вызывает `IMyRemapHelper.RemapEntityId(builder.EntityId)`.
  Его вызывают `MyObjectBuilder_CubeGrid.Remap`, `MyObjectBuilder_ProjectorBase.Remap` и
  `MyCubeGrid.MoveBlocksByObjectBuilders` — то есть копирование, проекция и резка **переназначают
  id**.

Практический вывод: любая таблица «id → позиция» тихо рассыпается при первом же копировании
корабля, а ошибка невидима — раскладка просто «слетает» у игрока, который ничего не делал
неправильно.

### 3. Данные можно положить на сам блок

[ПРОВЕРЕНО] `tools/dump-se-api.ps1` по `Sandbox.Game.EntityComponents.MyModStorageComponent`,
`VRage.Game.Components.MyModStorageComponentBase`, `VRage.ModAPI.IMyEntity`,
`VRage.Game.Definitions.MyModStorageComponentDefinition`:

```csharp
public IReadOnlyDictionary<Guid, String> Storage { get; }   // MyModStorageComponent
public String  GetValue(Guid guid)
public Boolean TryGetValue(Guid guid, out String value)
public Void    SetValue(Guid guid, String value)
public Boolean RemoveValue(Guid guid)
public Boolean IsSerialized()                                // m_storageData.Count > 0
public MyModStorageComponentBase Storage { get; set; }        // IMyEntity
public Guid[] RegisteredStorageGuids                          // MyModStorageComponentDefinition
```

Поведение, которое важнее сигнатур:

- **компонент добавляется присваиванием свойства.** [ПРОВЕРЕНО]
  `dump-se-il.ps1 -Assembly 'VRage.Game.dll' -Type 'VRage.Game.Entity.MyEntity'
  -Method 'set_Storage'`: `set_Storage(value) => get_Components().Add(value)`, а
  `Components_ComponentAdded` при добавлении компонента типа
  `MyModStorageComponentBase` пишет его в поле `m_storage`. Поэтому
  `entity.Storage = new MyModStorageComponent()` — законный способ создать хранилище;
- **`CustomData` блока лежит в этой же таблице**, под собственным GUID игры
  (`MyTerminalBlock.m_storageGuid`). [ПРОВЕРЕНО] `get_CustomData` →
  `get_Storage().TryGetValue(m_storageGuid, …)`; `SetCustomData_Internal` создаёт
  `new MyModStorageComponent()` тем же способом;
- **без регистрации GUID данные молча выбрасываются при сохранении.** [ПРОВЕРЕНО]
  `dump-se-il.ps1 -Type 'Sandbox.Game.EntityComponents.MyModStorageComponent'
  -Method 'Serialize'`: сериализатор собирает `RegisteredStorageGuids` из **всех**
  `MyModStorageComponentDefinition` и копирует в сейв только ключи из этого набора; для
  остальных пишет в лог `Not saving ModStorageComponent GUID: {0}, not claimed` и **не**
  сохраняет;
- **значения проходят remap неизменными.** [ПРОВЕРЕНО] `MyObjectBuilder_CubeBlock.Remap`
  вызывает `Remap` у каждого компонента контейнера;
  `MyObjectBuilder_ModStorageComponent` своего `Remap` не объявляет, а базовый
  `MyObjectBuilder_ComponentBase.Remap` — пустой (`ret`). Значит, данные едут с блоком, но и
  **id внутри строки никто не поправит** — ещё один довод не хранить в строке id;
- **компонент не синхронизируется сам.** `MyModStorageComponent` не является sync-компонентом
  (интерфейсы: `IMyComponentBase`, `IMyEntityComponentBase`, `IDictionary`). Для сравнения,
  `CustomData` синхронизируется **явно**: `SetCustomData_Internal(…, sync: true)` вызывает
  `RaiseCustomDataChanged` → `MyMultiplayer.RaiseEvent`. [ПРОВЕРЕНО] по IL. Следствие для
  мультиплеера — в §Consequences.

### 4. Альтернативы хранилища отпадают по своим причинам

- `CustomData` + `MyIni` — это **поле игрока**: он его видит, правит, копирует, и в него пишут
  скрипты Programmable Block. Раскладка, живущая там, будет стёрта первым же редактированием.
- `WriteFileInLocalStorage` / `WriteFileInWorldStorage` — файл не едет с миром, не едет с
  блюпринтом, не виден другим игрокам и **не попадает в бэкапы мира** (известный баг, см.
  [`web-research.md`](../01-research/web-research.md)). Для «восстанавливается после загрузки
  сохранённой игры» это неверный инструмент.
- `VgsSessionComponent.LoadData/SaveData` — сериализует состояние **сессии**, а не грида:
  привязать это к конкретному кораблю невозможно без своей таблицы ключей, то есть мы вернулись
  бы к пункту 2.

## Decision

### 1. Позиция хранится на блоке, одним значением

Один GUID на блок, одно значение — два числа в инвариантной культуре:

```
guid  = 0d3c1b7a-6a51-4c39-9d2e-7f8a1c4b5e60   (VgsConstants.LayoutKey, стабилен навсегда)
value = "<x> <y>"                               x, y — graph units, как Node.Position
```

Ключа нет вовсе, поэтому все три строки таблицы из §2 становятся «да» бесплатно: данные едут
внутри `ComponentContainer` блока при сохранении, копировании, проекции и резке.

Формат намеренно не JSON и не `MyIni`: две координаты должны читаться `float.TryParse` +
`Split(' ')` без разбора документа, потому что чтение происходит при **построении графа**, то
есть на каждый блок при каждом открытии редактора. `InvariantCulture` обязательна — иначе на
локали с десятичной запятой `0,5` не прочитается. Расширение формата (`"x y flag"`) делается
проверкой количества частей, а не миграцией INI-секций.

### 2. Регистрация GUID — часть контракта, а не формальность

Без неё данные исчезают при сохранении, и это видно только по строке в логе. Поэтому в моде
появляется `Data/EntityComponents.sbc`:

```xml
<EntityComponents>
  <EntityComponent xsi:type="MyObjectBuilder_ModStorageComponentDefinition">
    <Id>
      <TypeId>ModStorageComponent</TypeId>
      <SubtypeId>VGS</SubtypeId>
    </Id>
    <RegisteredStorageGuids>
      <guid>0d3c1b7a-6a51-4c39-9d2e-7f8a1c4b5e60</guid>
      <guid>b1e4a2c8-77d3-4a0f-8c61-2f9e5b0d3a47</guid>
    </RegisteredStorageGuids>
  </EntityComponent>
</EntityComponents>
```

[ПРОВЕРЕНО] чтением `Content/Data/EntityComponents.sbc`: этот формат — официальная инструкция
Keen, она лежит комментарием в шапке того же файла игры. [ПРОВЕРЕНО]
`dump-se-il.ps1 -Type 'Sandbox.Definitions.MyDefinitionManager' -Method 'GetDefinitionBuilders'`:
определения мода ищутся как `MyFileSystem.GetFiles(context.ModPathData, "*.sbc",
MySearchOption.AllDirectories)`, а `MySearchOption` — `TopDirectoryOnly, AllDirectories`
(`dump-se-api.ps1 -Types 'VRage.FileSystem.MySearchOption'`). Значит, файл кладётся в `Data/`
мода, глубина любая.

GUID фиксируются навсегда: смена GUID — потеря всех раскладок у существующих миров.

### 3. Чтение — при построении узла, запись — в конце перетаскивания

Чтение: `GridGraphBuilder.CreateNode`, там же, где сейчас читаются имя и тулбар. Блок уже под
рукой, лишнего поиска в мире нет, отдельного прохода не появляется.

Запись: `VgsEditorScreen.HandleCanvasInput`, ветка `leftReleased`, и только если перетаскивание
реально произошло (порог `DragThreshold` уже соблюдается):

```csharp
if (leftReleased)
{
    if (_dragging && _draggedNode != null) NodeStateStore.SavePosition(_draggedNode);
    _draggedNode = null;
    _dragging = false;
}
```

Почему не иначе:

- **не на каждом кадре перетаскивания** — это `SetValue` 60 раз в секунду: аллокация строки и
  запись в словарь на каждое движение мыши;
- **не только в `OnClosed`** — при выходе в главное меню или падении игры `OnClosed` не придёт,
  и раскладка потеряется именно там, где игрок её и заметит;
- **не для узлов, размещённых `LayoutUnplaced`** — их положение выводится детерминированно из
  координат блока, писать его в мир незачем.

### 4. `Node.Placed` вместо сравнения с нулём

`LayoutUnplaced` сейчас считает узел неразмещённым по `Position == Vector2.Zero`. Узел, который
игрок честно поставил в `(0, 0)`, будет переставляться при каждом обновлении — дефект, который
станет видимым ровно в момент, когда позиция начнёт восстанавливаться из мира.

Поэтому у `Node` появляется явный признак `Placed`, выставляемый при чтении позиции из
хранилища или при перетаскивании, и `LayoutUnplaced` смотрит только на него. Это новое
изменяемое поле в `Vgs.Model`, то есть оно обязано нести подавление
`// review:allow R009 <причина>` с указанием, кто его пишет ([ADR-0007](0007-mutable-model-dtos.md)).

**Реализовано иначе, и это лучше:** `Placed` не поле, а вычисляемый признак, а `Position` —
свойство, которое ставит его при любой записи. Подавление R009 не понадобилось вовсе: геттер
без сеттера не является публичным изменяемым полем. Причина, по которой свойство лучше пары
«поле + флаг»: флаг, выставляемый руками в каждом месте записи, забывается в первом же новом
месте, и симптом — карточка, прыгающая при обновлении, — неотличим от ошибки в самом
`LayoutUnplaced`.

### 4a. Автоматическая раскладка обязана быть детерминированной

Позиции, которые расставил `LayoutUnplaced`, **не сохраняются** (см. §Decision 3): они
выводятся заново при каждом открытии. Значит, вывод обязан зависеть только от корабля.

Прежний код раздавал места в порядке обхода `_nodeList`, то есть в порядке, в котором
`IMyCubeGrid.GetBlocks` вернул блоки. Постройка или разбор одного блока менял этот порядок и
перетасовывал **все** нетронутые карточки между двумя сессиями — игрок видел «редактор забыл
раскладку», хотя ничего сохранённого не было. Поэтому `LayoutUnplaced` сортирует неразмещённые
узлы по `GridPosition` (X, Y, Z), а при равенстве — по `EntityId`. Порядок становится функцией
корабля, и добавление блока сдвигает только новые карточки.

Проверяется офлайн: `tests/OfflineChecks.cs`, секция «automatic layout is deterministic».

### 5. Доступ к хранилищу — через `Vgs.Game.NodeStateStore`

Чтение и запись живут в новом `Vgs.Game/NodeStateStore.cs`, рядом с `ToolbarWriter`:

- `Vgs.Model` не знает ни о `MyModStorageComponent`, ни о `IMyEntity` — он остаётся чистым
  ([ADR-0003](0003-layering.md));
- `Vgs.Ui` только вызывает `NodeStateStore`, но не трогает живые блоки: правило «Ui не читает
  мир при рисовании» держится тем, что чтение идёт в `GridGraphBuilder`, а запись — по клику,
  как и запись в тулбар;
- у `NodeStateStore` ровно две обязанности — «прочитать состояние блока» и «записать состояние
  блока»; он не решает, что считается скрытым и что размещённым.

Сериализация формата («`x y`» ↔ `Vector2`) вынесена в `Vgs.Model.NodeStateFormat` — не по
вкусовщине, а потому что это единственная часть сохранения, которая может тихо испортить
раскладку, и единственная, которую можно проверить без игры (`tools/offline-check.ps1`).
Культура, разделитель, отказ от `NaN`/`Infinity` и от лишних частей — утверждения там же.

**Чтение никогда не создаёт компонент.** `IsShown` и `TryReadPosition` вызываются для каждого
терминального блока при каждом обновлении графа; если бы чтение создавало
`MyModStorageComponent`, мод прикреплял бы компонент к каждому блоку в мире просто от
открытия редактора — то есть менял бы сейв игрока фактом просмотра. Создаёт компонент только
запись.

### 6. Запрещено: ключевать раскладку по `EntityId`

Ни таблица «id → позиция» на гриде, ни строка «id=… x=… y=…» внутри одного значения не
допускаются. Причина не стилистическая: `Remap` переназначает id и **не** правит содержимое
наших строк (см. §3), поэтому такая таблица становится неверной при копировании корабля, а
обнаруживается это только глазами игрока.

## Alternatives

| Вариант | Почему отклонён |
|---|---|
| Таблица «`EntityId` → позиция» в `MyModStorageComponent` грида | Ломается при копировании, проекции и резке: `Remap` меняет id, а строку не правит |
| То же, но с пересчётом id после `Remap` | Своего хука на remap у мода нет; `MyObjectBuilder_ComponentBase.Remap` пуст, переопределить его мод не может |
| `CustomData` + `MyIni` | Поле игрока: он его правит и копирует, в него пишут скрипты PB. Раскладка будет стёрта |
| Локальный/мировой файл мода | Не едет с миром и блюпринтом, не попадает в бэкапы мира |
| `VgsSessionComponent.LoadData/SaveData` | Это состояние сессии, не грида; привязка к кораблю потребовала бы своей таблицы ключей |
| Хранить позицию в координатах сетки блока | Раскладка перестала бы быть раскладкой: игрок двигает карточку, а не блок |
| Одна запись на грид со всей раскладкой (`MyIni`) | Дешевле по размеру сейва, но резка грида делит блоки между двумя гридами и раскладка теряется целиком |
| Не сохранять позиции, а раскладывать детерминированно | Это то, что есть сейчас; требование прямо противоположно |

## Consequences

Что становится легко:

- раскладка переживает сохранение, загрузку, блюпринт, проектор и резку грида — без единого
  хука сохранения;
- удалённый блок уносит свою запись с собой: мусор в хранилище не накапливается, чистка не
  нужна;
- формат расширяется (третье поле) без миграции.

Чем платим:

- **одна запись `Guid → String` на блок.** Для 500 терминальных блоков это ~500 коротких
  строк в `ComponentContainer` — заметно в размере сейва, не заметно в памяти;
- **мультиплеер требует своего канала.** `MyModStorageComponent` не синхронизируется сам
  (§3), в отличие от `CustomData`, который игра реплицирует явно. Значит: в одиночной игре и
  на хосте запись работает, а на удалённом клиенте останется локальной, пока не появится
  `RegisterSecureMessageHandler` (id канала уже зарезервирован —
  `VgsConstants.NetworkChannelId`). Это ограничение игры, а не мода; пользователю оно
  сообщается, а не замалчивается;
- **новое изменяемое поле в `Vgs.Model`** (`Node.Placed`) требует подавления R009 с причиной;
- **`Data/EntityComponents.sbc` становится частью контракта**: забытый GUID = молча потерянные
  данные, и единственный симптом — строка `Not saving ModStorageComponent GUID` в логе.

Что запрещено:

- ключевать раскладку по `EntityId` (§Decision 6);
- писать раскладку в `CustomData`;
- менять GUID после первого релиза;
- создавать `MyModStorageComponent` через `Components.Add` в обход `entity.Storage = …`:
  сеттер — то, что делает игра сама, и единственный путь, который обновляет `m_storage`.

Если решение окажется неверным (например, размер сейва станет проблемой), откат дешёвый:
раскладка переезжает на грид одной записью, `NodeStateStore` меняет реализацию, остальной код
не трогается — но резка грида начнёт терять раскладку, и это надо будет принять явно.

## Verification

Проверено на этой машине (2026-11-21), команды и результаты — в
[`docs/01-research/api-persistence.md`](../01-research/api-persistence.md) §2–§3:

```powershell
& .\tools\dump-se-api.ps1 -Types 'Sandbox.Game.EntityComponents.MyModStorageComponent',
    'VRage.Game.Components.MyModStorageComponentBase',
    'VRage.Game.Definitions.MyModStorageComponentDefinition','VRage.ModAPI.IMyEntity'
& .\tools\dump-se-il.ps1 -Assembly 'VRage.Game.dll' -Type 'VRage.Game.Entity.MyEntity' -Method 'set_Storage'
& .\tools\dump-se-il.ps1 -Assembly 'VRage.Game.dll' -Type 'VRage.Game.Entity.MyEntity' -Method 'Components_ComponentAdded'
& .\tools\dump-se-il.ps1 -Assembly 'VRage.Game.dll' -Type 'VRage.Game.Entity.MyEntity' -Method 'Init'
& .\tools\dump-se-il.ps1 -Type 'Sandbox.Game.EntityComponents.MyModStorageComponent' -Method 'Serialize'
& .\tools\dump-se-il.ps1 -Type 'Sandbox.Game.Entities.Cube.MyTerminalBlock' -Method 'get_CustomData'
& .\tools\dump-se-il.ps1 -Assembly 'VRage.Game.dll' -Type 'VRage.Game.MyObjectBuilder_CubeBlock' -Method 'Remap'
& .\tools\dump-se-il.ps1 -Assembly 'VRage.Game.dll' -Type 'VRage.Game.MyObjectBuilder_CubeBlock' -Method 'ShouldSerializeEntityId'
& .\tools\dump-se-il.ps1 -Type 'Sandbox.Definitions.MyDefinitionManager' -Method 'GetDefinitionBuilders'
```

Компиляция и вайтлист проверены пробным файлом вне мода: `COMPILE OK`, `WHITELIST OK`
(`MyModStorageComponent` требует `using Sandbox.Game.EntityComponents;` — пространство имён
разрешено, в отличие от `Sandbox.Game.Screens.Helpers`).

Шлюзы проекта после реализации:

```powershell
& .\tools\verify-compile.ps1        # COMPILE OK
& .\tools\check-whitelist.ps1       # WHITELIST OK
& .\tools\review-check.ps1          # без находок, включая R009 на Node.Placed
& .\tools\offline-check.ps1         # правило «размещён» — новое утверждение в tests/OfflineChecks.cs
```

Человек в игре (без этого «работает» не считается):

1. Поставить два блока, перетащить карточки, закрыть редактор, открыть снова — карточки на
   своих местах.
2. Выйти в главное меню, загрузить сохранённый мир, открыть редактор — карточки на своих
   местах.
3. После сохранения в логе **нет** строки `Not saving ModStorageComponent GUID: … not claimed`.
4. Скопировать корабль в блюпринт, вставить — раскладка сохранилась.
5. Разрезать корабль пополам, открыть редактор на каждой половине — карточки не «прыгнули».
6. Поставить карточку ровно в `(0, 0)`, переоткрыть редактор — она осталась там (проверка
   `Node.Placed`).

Не проверено и требует игры: что запись действительно доезжает до сейва и возвращается;
что делать с записью на удалённом клиенте (нужен ли канал синхронизации и в какую сторону);
что именно делает `MyObjectBuilder_CubeGrid.SetupForGridPaste` с компонентами блока при
вставке — блочный `SetupForGridPaste` пуст, гридовый разобран не полностью.
