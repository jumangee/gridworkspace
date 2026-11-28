# Мультиплеер: почему запись в тулбар — серверная и что нужно для полной совместимости

- Дата: 2026-11-28
- Статус: **исследование, не решение.** Здесь факты и цена вариантов; выбор — отдельный ADR.
- Связанные документы: [ADR-0011](../adr/0011-toolbar-is-the-connection.md) (связь — элемент
  тулбара), [ADR-0012](../adr/0012-layout-lives-on-the-block.md) (раскладка на блоке),
  [ADR-0017](../adr/0017-event-controller-watch-ports.md) (список контроллера — только сервер),
  [ADR-0022](../adr/0022-view-state-lives-on-the-grid.md) (состояние вида на гриде),
  [api-toolbar.md](api-toolbar.md) §4, [ROADMAP.md](../ROADMAP.md) §«Риски».

## 0. Короткий ответ

1. **Мод в сетевой игре загружается и работает.** Чтение схемы (тулбары, группы, состояние
   контроллера, раскладка из хранилища блока) доступно на удалённом клиенте: игра реплицирует
   эти данные. Ломается не «мод», а **запись**: создание и удаление связи, правка параметров,
   привязка контроллера, сохранение раскладки и флага видимости.
2. **Запись в тулбар через ModAPI — это не «правка блока», а рассылка от сервера всем
   клиентам.** `BeginChanges().Set().Apply()` упирается в `MyToolbar.SendChanges`, а тот
   проверяет `IsOwnedHere`, которое для тулбара блока равно `Sync.IsServer`, и дальше поднимает
   сетевое событие `ReceiveToolbarBatch` с атрибутом `[ServerInvoked][Broadcast]`. Инициировать
   такую рассылку может только сервер — иначе клиенты разойдутся.
3. **Фракция к этому отношения не имеет.** Она даёт *право игрока* (`HasPlayerAccess` →
   `GetUserRelationToOwner(...).IsFriendly()`), а мод упирается в *право машины*
   (`Sync.IsServer`). Ванильный экран тулбара спрашивает первое и поэтому у фракционного
   союзника работает; мод спрашивает второе и поэтому не работает даже у владельца блока.
4. **Полная совместимость достигается одним слоем:** клиент отправляет по своему каналу
   **намерение** («свяжи A с B действием X»), сервер проверяет право игрока и пишет тулбар
   существующим кодом; игра сама рассылает результат всем клиентам. Метаданные (раскладка,
   флаг) реплицируются тем же каналом. Потери функций при этом нет — есть потеря мгновенности
   (один RTT) и необходимость серверной ветки кода.

## 1. Что происходит при записи: цепочка вызовов

```
Vgs.Game.ToolbarWriter.TryWrite
  → IMyToolbar.BeginChanges()                    VRage.Game.ModAPI
  → IMyToolbarChangeBuilder.Set / Apply          VRage.Game.ModAPI
  → MyToolbarChangeBuilder.Apply
  → MyToolbar.SendChanges
        if (!IsOwnedHere) return false;          ← клиент останавливается здесь
  → IMyToolbarHost.SendToolbarBatch              (адаптер MyToolbarProviderComponent)
  → MyMultiplayer.RaiseEvent(...)                Sandbox.Engine.Multiplayer
  → [у всех клиентов] MyToolbarProviderComponent.ReceiveToolbarBatch
        [Event][Reliable][ServerInvoked][Broadcast]
  → MyToolbarBatch.Apply(..., batched: true)
  → MyToolbar.SetItemAtIndex(..., batched: true)
```

Проверено по IL установленной сборки:

```powershell
& .\tools\dump-se-il.ps1 -Type 'Sandbox.Game.Screens.Helpers.MyToolbar' -Method 'SendChanges'
& .\tools\dump-se-il.ps1 -Type 'Sandbox.Game.Screens.Helpers.MyToolbar' -Method 'get_IsOwnedHere'
& .\tools\dump-se-il.ps1 -Type 'Sandbox.Game.Screens.Helpers.MyToolbarChangeBuilder' -Method 'Apply'
& .\tools\dump-se-il.ps1 -Type 'Sandbox.Game.EntityComponents.MyToolbarProviderComponent' -Method '*'
```

`SendChanges` целиком:

```il
ldarg.0; ldfld m_toolbarOwner; brfalse → return false
call get_IsOwnedHere; brtrue → продолжить; иначе return false
...
callvirt IMyToolbarHost::SendToolbarBatch(targets, items); return true
```

`get_IsOwnedHere`:

```il
call get_ToolbarType
brfalse → ldc.i4.1; ret        // ToolbarType.Character: тулбар игрока «свой» на любой машине
call Sync::get_IsServer; ret   // всё остальное — только на сервере
```

`MyToolbarProviderComponent.SendToolbarBatch` заканчивается вызовом
`Sandbox.Engine.Multiplayer.MyMultiplayer::RaiseEvent`, а `ReceiveToolbarBatch` помечен
`[Event][Reliable][ServerInvoked][Broadcast]` — то есть это **серверное событие, рассылаемое
всем клиентам**, а не локальная правка.

Отсюда и `WriteResult.Rejected` в `ToolbarWriter`: на удалённом клиенте `Apply()` честно
возвращает `false`, потому что рассылку инициирует не он.

## 2. У игры есть второй путь — и он клиентский

Ванильный экран настройки тулбара правит тулбар **не** через `SendChanges`. Он зовёт
`MyToolbar.SetItemAtIndex`, тот поднимает `ItemChanged`, а блок пересылает изменение своим
собственным сетевым методом:

```
MyGuiScreenToolbarConfigBase
  → MyToolbar.SetItemAtIndex
  → ItemChanged (Action<MyToolbar, IndexArgs, bool batched>)
  → MyTimerBlock.Toolbar_ItemChanged
        if (m_syncing) return;
        if (index.Batched) return;
        → MyMultiplayer.RaiseEvent(...)
  → [на сервере] MyTimerBlock.SendToolbarItemChanged
        [Event][Reliable][Server][Broadcast]
        m_syncing = true; Toolbar.SetItemAtIndex(...); m_syncing = false;
```

Проверено по IL:

```powershell
& .\tools\dump-se-il.ps1 -Type 'Sandbox.Game.Screens.Helpers.MyToolbar' -Method 'SetItemAtIndexInternal'
& .\tools\dump-se-il.ps1 -Assembly 'SpaceEngineers.Game.dll' `
    -Type 'SpaceEngineers.Game.Entities.Blocks.MyTimerBlock' -Method 'Toolbar_ItemChanged'
& .\tools\dump-se-il.ps1 -Assembly 'SpaceEngineers.Game.dll' `
    -Type 'SpaceEngineers.Game.Entities.Blocks.MyTimerBlock' -Method 'SendToolbarItemChanged'
```

Атрибуты `SendToolbarItemChanged` (`[Event][Reliable][Server][Broadcast]`) прочитаны рефлексией
по сборке.

**Почему мод не может пойти этим путём.** `SetItemAtIndex`, `MyToolbar`, `MyToolbarItem` и
`ToolbarItem` лежат в `Sandbox.Game.Screens.Helpers` и `Sandbox.Game.Entities.Blocks` —
пространствах, которые вайтлист IL-скриптов не разрешает (`Sandbox.Game` разрешён **точно**,
без подпространств). Именно на `MyToolbar` мод уже один раз не загрузился
([ADR-0011](../adr/0011-toolbar-is-the-connection.md) §5). Кроме того, `SendToolbarItemChanged`
есть у каждого типа блока свой, и общего «отправь элемент в тулбар» в ModAPI нет.

Итог: **у клиента нет разрешённого способа записать элемент в тулбар блока.** Не потому, что
ему не хватает прав на блок, а потому, что единственный разрешённый путь — серверная рассылка.

## 3. Два разных «можно»: право игрока и право машины

| Вопрос | Кто отвечает | Что учитывает | Где используется |
|---|---|---|---|
| «Может ли этот игрок трогать этот блок?» | `HasPlayerAccess(identityId)` → `GetUserRelationToOwner(...).IsFriendly()`; у ванильного экрана — `HasPlayerAccessToBlockWithoutOwnership(ownerId)` → `GetRelationPlayerPlayer(...)` | владелец, общая фракция, share mode, админ | ванильный экран тулбара (`OwnerHasAccess`), терминал, действия |
| «Может ли эта машина писать тулбар?» | `MyToolbar.IsOwnedHere` → `Sync.IsServer` | только «я сервер» | `BeginChanges().Apply()`, то есть мод |

Проверено по IL:

```powershell
& .\tools\dump-se-il.ps1 -Type 'Sandbox.Game.Gui.MyGuiScreenToolbarConfigBase' -Method 'OwnerHasAccess'
& .\tools\dump-se-il.ps1 -Type 'Sandbox.Game.Entities.Cube.MyTerminalBlock' -Method 'HasPlayerAccess*'
& .\tools\dump-se-api.ps1 -Types 'Sandbox.ModAPI.Ingame.IMyTerminalBlock'
```

`OwnerHasAccess` — это проверка ванильного экрана: она спрашивает отношение владельца тулбара
к добавляемому блоку (`HasPlayerAccessToBlockWithoutOwnership` → `GetRelationPlayerPlayer`), то
есть учитывает фракцию и share mode. Поэтому член общей фракции с правом на терминал **может**
положить элемент в тулбар блока через терминал. Мод в той же ситуации получает `Rejected`,
потому что его путь вообще не задаёт вопрос о правах игрока — он задаёт вопрос «я сервер?».

Поэтому «блоки принадлежат общей фракции» ничего не меняет: фракция — это ответ на первый
вопрос, а мод упирается во второй. И наоборот: владелец блока на удалённом клиенте тоже
получает отказ. Это не про собственность, а про то, кто считает симуляцию.

## 4. Что работает и что нет на удалённом клиенте

| Функция | Механизм | Удалённый клиент | Причина |
|---|---|---|---|
| Показать схему (блоки, тулбары, группы, состояние контроллера) | чтение реплицированных объектов | **работает** (в игре ждёт проверки, ROADMAP) | `TryGetItem` не имеет гейта (проверено по IL) |
| Создать / удалить связь, правка параметров | `ToolbarWriter` → `Apply` | **отказ** `Rejected` | `IsOwnedHere == false` |
| Привязка блока к контроллеру | `EventControllerProbe` → `AddBlocks` | **отказ** `ClientNotServer` | подписка компонента только под `Sync.IsServer` (ADR-0017) |
| Переместить карточку, скрыть блок | `MyModStorageComponent` | **локально**, до перезахода | компонент не синхронизируется (ADR-0012 §3) |
| Камера, зум, режимы показа | `MyModStorageComponent` на гриде | **локально** | то же; в MP это ещё и «кто последний записал» |
| Открыть редактор, панорама, меню, поиск, переход в терминал | клиентский UI | **работает** | экран мода — клиентский |
| Ручной запуск действия (ПКМ) | `ITerminalAction.Apply` | **не проверено** | `Apply` — локальный вызов; у части действий есть свой sync |

Сообщение об отказе сейчас общее: `WARN_REJECTED` — «игра не приняла действие в тулбар». На
клиенте это вводит в заблуждение: причина не в действии, а в том, что запись инициирует не
сервер. Дешёвое исправление — отдельная строка, как `WARN_WATCH_CLIENT` у контроллера.

## 5. Что требуется для полной совместимости

В любом случае мод должен стоять **и на сервере, и на клиенте** (в Space Engineers состав
модов должен совпадать, иначе клиент не войдёт в мир), а версии — совпадать: старый клиент с
новым сервером должен получать отказ, а не молчание.

### 5.1 Канал намерений (клиент → сервер)

Разрешённый примитив уже есть в ModAPI:

```csharp
MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(
    channelId, (id, payload, senderSteamId, fromServer) => { ... });
MyAPIGateway.Multiplayer.SendMessageToServer(channelId, payload, reliable: true);
```

`IMyMultiplayer` (`VRage.Game.ModAPI`) содержит `RegisterSecureMessageHandler`,
`UnregisterSecureMessageHandler`, `SendMessageToServer`, `SendMessageTo`, `SendMessageToOthers`,
`IsServer`, `MultiplayerActive`, `MyId` — [ПРОВЕРЕНО] `docs/01-research/api-surface.md`.

Правила, без которых канал опасен:

- **регистрация и снятие** — в `VgsSessionComponent` (`Init`/`BeforeStart` и обязательно
  `UnloadData`); иначе после выхода в меню обработчик смотрит в мёртвую сессию;
- **id канала глобальный**: `VgsConstants.NetworkChannelId = 48123` уже зарезервирован, но
  коллизия с другим модом возможна; регистрация должна быть в `try/catch` и сообщать об
  ошибке, а не молчать;
- **версия протокола** в каждом сообщении: сервер отвергает чужую версию, клиент показывает
  «версия мода на сервере другая»;
- **сериализация** — `MyAPIGateway.Utilities.SerializeToBinary<T>` /
  `SerializeFromBinary<T>` с DTO, помеченным `[ProtoContract]`/`[ProtoMember]`.
  `System.IO.MemoryStream` в вайтлисте **отсутствует**, поэтому свой `BinaryWriter` над
  `MemoryStream` не подходит; `ProtoBuf.ProtoContractAttribute` и `ProtoMemberAttribute`
  вайтлист разрешает ([ПРОВЕРЕНО] `tools/check-whitelist.ps1 -Dump`);
- **не доверять клиенту**: сервер сам находит блоки по id, сам проверяет право и сам считает
  свободную ячейку. Присланный клиентом номер ячейки — подсказка, а не факт.

### 5.2 Серверная сторона: тот же код, но без графа

Серверу не нужен экран и не нужен `GridGraph`. Ему нужны:

1. `MyAPIGateway.Players.TryGetIdentityId(senderSteamId)` — кто просит
   ([ПРОВЕРЕНО] `& .\tools\dump-se-api.ps1 -Types 'VRage.Game.ModAPI.IMyPlayerCollection'`);
2. `ToolbarProbe.Probe(source)` — форма тулбара живого блока (уже работает без `Node`);
3. `source.HasPlayerAccess(identityId)` — право игрока на блок (тот же примитив, которым
   пользуются терминал и ванильный экран);
4. `ToolbarWriter` — но сейчас он принимает `Node` ради `ToolbarLayout`. Нужен рефакторинг:
   `TryWrite(source, layout, portGroup, target, actionId, parameters, out cell)`. Это
   разделение полезно и само по себе: «запись в тулбар» перестаёт зависеть от модели экрана;
5. ответ клиенту: `{requestId, code, toolbarIndex, cellIndex}`.

Важно: **результат записи игра рассылает сама.** Серверный `Apply()` поднимает
`ReceiveToolbarBatch` (`[Broadcast]`), и все клиенты, включая инициатора, применяют элемент.
Свой канал не должен пересылать связь — только намерение и результат. Это следствие из §1 и
главный аргумент, почему схема остаётся «тулбар — единственное хранилище» (ADR-0011) и в MP.

### 5.3 Метаданные: раскладка и флаг

`MyModStorageComponent` не реплицируется (ADR-0012 §3, проверено: у компонента нет sync-
интерфейсов). Значит для «без потери функционала»:

- запись раскладки и флага — тоже **намерение** на сервер; сервер пишет в хранилище блока;
- при входе клиент должен получить хранилище вместе с блоком: репликация мира несёт
  объект-билдер блока с компонентами, но что `MyModStorageComponent` доезжает до клиента —
  **[НЕ ПРОВЕРЕНО]**, это спайк;
- изменения в рантайме сервер рассылает тем же каналом, клиенты применяют к своей копии;
- конфликт «двое тянут одну карточку» решается «последний принятый сервером побеждает» —
  это надо назвать вслух, а не оставлять на волю случая.

### 5.4 Состояние вида — отдельный вопрос

Камера и зум — свойство **игрока**, а не корабля. Сегодня они лежат на гриде (ADR-0022), и в
MP это означает «кто последний записал, тот и задал камеру всем после перезагрузки». Варианты:

- оставить как есть и честно назвать это ограничением (камера в MP — локальная, после
  перезахода — серверная);
- ключевать значение по `IdentityId` внутри того же хранилища (растёт с числом игроков);
- хранить локально у игрока (противоречит ADR-0022, зато честно per-player).

Это продуктовое решение, а не техническое: «общая камера» не нужна никому.

### 5.5 Что останется локальным навсегда

Язык (ADR-0033), буфер обмена (ADR-0027), состояние экрана, наведение, поиск. Это свойства
клиента, и реплицировать их не надо.

### 5.6 Порядок работ, если решаем делать

1. **Честный отказ (дёшево, без канала).** На клиенте `ToolbarWriter` не вызывается вовсе:
   отдельный `WriteResult.ClientNotServer` и строка «связи создаются на сервере», как
   `WARN_WATCH_CLIENT`. Это не совместимость, но перестаёт врать.
2. **Канал + серверная запись связи.** Рефакторинг `ToolbarWriter` на `ToolbarLayout`,
   `Vgs.Net` с регистрацией/снятием, обработчик на сервере, ответ клиенту, ожидание в UI.
   Закрывает создание, удаление и правку параметров связи — три четверти функционала.
3. **Метаданные.** Раскладка и флаг через тот же канал, рассылка принятых изменений клиентам,
   проверка «хранилище доезжает при входе».
4. **Состояние вида.** Решение «общая камера или per-player»; если per-player — ключ по
   `IdentityId` или локальное хранение, и это правка ADR-0022.
5. **Приёмка:** выделенный сервер + два клиента, по одному сценарию на каждую строку таблицы
   §4.

## 6. Цена и риски

| Риск | Почему | Смягчение |
|---|---|---|
| UI становится асинхронным | запись подтверждает сервер, а не клик | состояние «ожидание сервера» на связи/карточке; запрет повторной отправки; отказ приходит словами |
| Право на правку чужого корабля | сейчас его никто не проверяет, потому что пишет только сервер | `HasPlayerAccess` на источник (и решить про цель); админ — отдельно |
| Злоупотребление каналом | клиент может спамить запросами | лимит запросов на игрока, журнал `[VGS]` |
| Коллизия id канала | id глобальный | фиксированный id + `try/catch` + сообщение в лог |
| Расхождение версий | сервер и клиент считают по-разному | версия протокола в сообщении |
| Серверный путь не тестировался | выделенный сервер раньше вообще не исполнял логику мода | прогон «DS + два клиента» как отдельный сценарий приёмки |
| Гонка двух клиентов | оба видят свободную ячейку | планировщик и запись — только на сервере, ответ — факт |
| `MySync<T,TDir>` как альтернатива каналу | тип разрешён вайтлистом, но контракт регистрации не проверен | [НЕ ПРОВЕРЕНО]; спайк до решения |

## 7. Что проверить до реализации (спайк)

1. Регистрация/снятие канала в одиночной игре и в MP; поведение при занятом id.
2. `SerializeToBinary`/`SerializeFromBinary` для `[ProtoContract]`-DTO в моде (не в консоли).
3. `ToolbarProbe.Probe` и `ToolbarWriter` на выделенном сервере: есть ли компонент
   `IMyToolbarProvider` и работает ли `Apply()` без GUI.
4. `HasPlayerAccess` на сервере для члена фракции: возвращает ли `true` при `FactionShare`.
5. Доходит ли `ReceiveToolbarBatch` до инициатора (по атрибуту `[Broadcast]` — да; в игре не
   проверялось).
6. Приходит ли `MyModStorageComponent` клиенту при входе в мир вместе с блоком.
7. `MySync<T,TDir>`: как он регистрируется и годится ли для раскладки.

## 8. Команды проверки

```powershell
& .\tools\dump-se-il.ps1 -Type 'Sandbox.Game.Screens.Helpers.MyToolbar' -Method 'SendChanges'
& .\tools\dump-se-il.ps1 -Type 'Sandbox.Game.Screens.Helpers.MyToolbar' -Method 'get_IsOwnedHere'
& .\tools\dump-se-il.ps1 -Type 'Sandbox.Game.EntityComponents.MyToolbarProviderComponent' -Method '*'
& .\tools\dump-se-il.ps1 -Type 'Sandbox.Game.Screens.Helpers.MyToolbarBatch' -Method 'Apply'
& .\tools\dump-se-il.ps1 -Type 'Sandbox.Game.Gui.MyGuiScreenToolbarConfigBase' -Method 'OwnerHasAccess'
& .\tools\dump-se-il.ps1 -Type 'Sandbox.Game.Entities.Cube.MyTerminalBlock' -Method 'HasPlayerAccess*'
& .\tools\dump-se-il.ps1 -Assembly 'SpaceEngineers.Game.dll' `
    -Type 'SpaceEngineers.Game.Entities.Blocks.MyTimerBlock' -Method 'Toolbar_ItemChanged'
& .\tools\dump-se-api.ps1 -Types 'VRage.Game.ModAPI.IMyMultiplayer','VRage.Game.ModAPI.IMyPlayerCollection'
& .\tools\check-whitelist.ps1 -Dump
```
