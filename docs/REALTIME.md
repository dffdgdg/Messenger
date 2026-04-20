# Realtime.md

## Инфраструктура

### ChatHub
- Hub: `ChatHub` (`[Authorize]`), endpoint `/chatHub`
- Auth: JWT через `AccessTokenProvider` (query string)
- Реконнект: `WithAutomaticReconnect()`
- Серверная отправка из сервисов: `HubNotifier` (fire-and-forget, ошибки логируются)
- Клиент: singleton `GlobalHubConnection`
- Группы: `chat_{chatId}` (все участники), `user_{userId}` (персональная)

### CallHub
- Hub: `CallHub` (`[Authorize]`), endpoint `/callHub`
- Auth: JWT через `AccessTokenProvider` (query string)
- Реконнект: `WithAutomaticReconnect()`
- Серверное состояние: `CallSessionService` — in-memory singleton (`ConcurrentDictionary`)
- Клиент: singleton `CallHubConnection`
- Группы: `user_{userId}` (персональная), `chat_{chatId}` (широковещательные события о звонке в чате)

---

## ChatHub — Client → Server

| Метод | Параметры | Возвращает | Описание |
|-------|-----------|------------|----------|
| `JoinChat` | `int chatId` | — | Присоединиться к chat-группе |
| `LeaveChat` | `int chatId` | — | Покинуть chat-группу |
| `MarkAsRead` | `int chatId, int? messageId` | — | Отметить чат прочитанным (`null` = весь чат) |
| `MarkMessageAsRead` | `int chatId, int messageId` | — | Отметить конкретное сообщение |
| `SendTyping` | `int chatId` | — | Индикатор "печатает..." |
| `GetUnreadCounts` | — | `AllUnreadCountsDto` | Все счётчики непрочитанных |
| `GetReadInfo` | `int chatId` | `ChatReadInfoDto?` | Read-state конкретного чата |
| `GetOnlineUsersInChat` | `int chatId` | `List<int>` (userIds) | Онлайн-участники чата |

### Дебаунс на клиенте (ChatHub)

| Метод | Константа | Дополнительное условие пропуска |
|-------|-----------|--------------------------------|
| `MarkMessageAsRead` | `AppConstants.MarkAsReadDebounceMs` | `messageId <= _lastSentReadMsgId` |
| `SendTyping` | `AppConstants.TypingSendDebounceMs` | — |

> `SetCurrentChat(int? chatId)` сбрасывает оба дебаунс-таймера и `_lastSentReadMsgId = 0`.

---

## CallHub — Client → Server

| Метод | Параметры | Возвращает | Описание |
|-------|-----------|------------|----------|
| `InitiateCall` | `int chatId` | — | Инициировать звонок. Рассылает `IncomingCall` всем участникам кроме инициатора |
| `JoinCall` | `string callId` | — | Принять входящий / присоединиться к активному групповому |
| `LeaveCall` | `string callId` | — | Покинуть звонок. Если последний участник — завершает для всех |
| `DeclineCall` | `string callId` | — | Отклонить входящий (только Contact 1:1, для группы игнорируется) |
| `CancelCall` | `string callId` | — | Инициатор отменяет до принятия (только Contact в статусе `Ringing`) |
| `SendSignal` | `WebRtcSignalDto signal` | — | Пересылка WebRTC/UDP-сигнала конкретному участнику |
| `ToggleMute` | `string callId, bool isMuted` | — | Обновить статус микрофона |
| `GetCallState` | `int chatId` | `CallStateDto?` | Получить состояние активного звонка в чате |

### Бизнес-правила CallHub методов

**`InitiateCall`:**
- Проверяет членство → иначе `CallError`
- Проверяет отсутствие активного звонка в чате → иначе `CallError`
- `Contact` → `isGroupCall=false`, остальные типы → `true`
- Инициатор сразу в `ActiveParticipants`
- Для Contact: запускает таймаут 60 сек → `Timeout` при истечении
- Рассылает `IncomingCall` через `user_{memberId}` всем участникам кроме инициатора
- Инициатору → `CallStateUpdated`, всему чату → `ActiveCallStarted`

**`JoinCall`:**
- Лимит: максимум 12 участников → иначе `CallError`
- Проверяет членство → иначе `CallError`
- Первый принявший: `Ringing → Active`, таймаут отменяется
- Новому участнику → `CallStateUpdated`
- Существующим участникам → `CallParticipantJoined` (персонально по ConnectionId)

**`LeaveCall`:**
- Уведомляет остальных `CallParticipantLeft`
- Если последний → `TerminateCall(Ended)`
- Для группы, если звонок продолжается → `ActiveCallUpdated` в чат

**`DeclineCall`:**
- Только для Contact (`IsGroupCall=false`), иначе ранний выход
- Если `PendingParticipants` пусты → `TerminateCall(Declined)`
- Если ещё есть pending → только вызывающему `CallEnded(Declined)`

**`CancelCall`:**
- Только инициатор → иначе `CallError`
- Для группы или статуса `Active` → делегирует в `LeaveCall`
- Для Contact в статусе `Ringing` → `TerminateCall(Cancelled)`

**`SendSignal`:**
- `FromUserId` перезаписывается сервером из JWT
- Пересылает по `ConnectionId` адресата из `ActiveParticipants`

---

## ChatHub — Server → Client

### Сообщения

| Событие | Payload | Описание |
|---------|---------|----------|
| `ReceiveMessageDto` | `MessageDto` | Новое сообщение |
| `MessageUpdated` | `MessageDto` | Отредактировано или изменено состояние закрепления |
| `MessageDeleted` | `MessageDeletedEvent { MessageId, ChatId }` | Удалено (soft delete) |
| `ReceivePollUpdate` | `PollDto` | Обновление результатов опроса после голосования |

### Уведомления

| Событие | Payload | Описание |
|---------|---------|----------|
| `ReceiveNotification` | `NotificationDto` | Push (message / mention / poll) |
| `UnreadCountUpdated` | `int chatId, int count` | Счётчик непрочитанных изменился |

### Пользователи

| Событие | Payload | Описание |
|---------|---------|----------|
| `UserOnline` | `int userId` | Пользователь онлайн |
| `UserOffline` | `int userId` | Пользователь оффлайн |
| `UserProfileUpdated` | `UserDto` | Профиль обновлён |

> На клиенте `UserOnline` и `UserOffline` объединены в одно событие:
> `UserStatusChanged(int userId, bool isOnline)`.

### Чат

| Событие | Payload | Описание |
|---------|---------|----------|
| `UserTyping` | `int chatId, int userId` | Пользователь печатает |
| `MessageRead` | `int chatId, int userId, int? lastReadMessageId, DateTime? readAt` | Сообщение прочитано |
| `MemberJoined` | `int chatId, UserDto user` | Участник добавлен |
| `MemberLeft` | `int chatId, int userId` | Участник покинул чат |

---

## CallHub — Server → Client

### Управление звонком

| Событие | Payload | Кому | Описание |
|---------|---------|------|----------|
| `IncomingCall` | `CallInviteDto` | `user_{memberId}` | Входящий звонок |
| `CallStateUpdated` | `CallStateDto` | Caller | Полное состояние (при join или инициации) |
| `CallParticipantJoined` | `string callId, CallParticipantDto` | Существующие участники | Новый участник подключился |
| `CallParticipantLeft` | `string callId, int userId` | Активные участники | Участник покинул |
| `CallEnded` | `string callId, CallEndReason` | Все pending + активные | Звонок завершён |
| `CallError` | `string message` | Caller | Ошибка операции |
| `ParticipantMuteChanged` | `string callId, int userId, bool isMuted` | Остальные активные | Изменён статус микрофона |
| `ReceiveSignal` | `WebRtcSignalDto` | Конкретный участник | WebRTC/UDP сигнал |

### Состояние звонка в чате (для баннера)

| Событие | Payload | Кому | Описание |
|---------|---------|------|----------|
| `ActiveCallStarted` | `CallStateDto` | `chat_{chatId}` | В чате начался звонок |
| `ActiveCallUpdated` | `CallStateDto` | `chat_{chatId}` | Изменился состав участников |
| `ActiveCallEnded` | `string callId` | `chat_{chatId}` | Звонок завершён |

### Завершение звонка (`TerminateCallAsync`)

```
1. Всем PendingParticipants → user_{id} → CallEnded(reason)
2. Всем ActiveParticipants  → по ConnectionId → CallEnded(reason)
3. chat_{chatId}            → ActiveCallEnded(callId)
4. EndCallAsync             → удалить из _calls + _chatCallIndex → вернуть duration
5. Если reason != Cancelled && reason != Declined && duration > 1s:
   → SystemMessageService.CreateCallEndedMessageAsync
   → только для не-Contact чатов
   → SystemEventType.CallEnded
   → контент: "Звонок завершён · {M:SS}" или "Звонок завершён · {H:MM:SS}"
   → рассылается как ReceiveMessageDto в чат
```

---

## Клиентская обработка — ChatHub

### GlobalHubConnection — новое сообщение (`ReceiveMessageDto`)
1. `CacheIncomingMessageAsync`:
   - `UpsertMessageAsync(msg)` → SQLite
   - `UpdateChatLastMessageAsync` → превью в списке чатов
2. `MessageReceivedGlobally?.Invoke(msg)` → UI thread
3. Чат не открыт (`_openChatId != msg.ChatId`) + не своё → `IncrementUnread(chatId)`

### GlobalHubConnection — уведомление (`ReceiveNotification`)
Условия подавления (любое → skip):
- `_disposed == true`
- `!settingsService.NotificationsEnabled`
- `_openChatId == n.ChatId` (чат сейчас открыт)

Формат уведомления:
```
type == "poll" → текст: n.Preview ?? "Новый опрос"
иначе          → текст: "{n.SenderName}: {n.Preview}"
заголовок всегда: n.ChatName ?? "Новое сообщение"
```

Клик → `MainMenuViewModel.OpenNotificationAsync(n)` → открыть чат + прокрутка к `MessageId`

### GlobalHubConnection — непрочитанные
- `Dictionary<int, int> _unreadCounts` + `_totalUnread`, thread-safe через `Lock _unreadLock`
- `UpdateUnread(chatId, newCount)` — установить точное значение (от сервера)
- `IncrementUnread(chatId)` — +1 при входящем сообщении не в текущем чате
- При реконнекте: `LoadUnreadCountsAsync()` → `GetUnreadCounts` с сервера → полная перезагрузка

### ChatHubSubscriber (текущий открытый чат)

| Событие | Обработка |
|---------|-----------|
| `MessageReceivedGlobally` | `messageManager.AddReceivedMessage` |
| `MessageUpdatedGlobally` | `messageManager.HandleMessageUpdated` |
| `MessageDeletedGlobally` | `messageManager.HandleMessageDeleted` |
| `PollUpdatedGlobally` | `messageManager.HandlePollUpdated` → обновить VM + SQLite |
| `MessageRead` | обновить `IsRead` у сообщений с `Id <= lastReadId` |
| `UnreadCountChanged` | `onUnreadCountChanged(count)` |
| `Reconnected` | `onReconnected()` → `GapFillAfterReconnectAsync` |

> `UserTyping` и инфопанель подписываются в своих handler'ах отдельно.

---

## Клиентская обработка — CallHub

### CallHubConnection
Singleton `CallHubConnection` (`ICallHubConnection`) — транспортный слой:
- Только SignalR send/receive
- `SafeInvokeAsync` — пропускает вызов если не подключён, логирует ошибки
- `GetCallStateAsync` — единственный invocation (возвращает `CallStateDto?`)

### CallService
Singleton `CallService` (`ICallService`) — orchestrator:

| Событие от CallHubConnection | Обработка в CallService |
|------------------------------|------------------------|
| `CallStateUpdated` | Добавить участников в аудио буфер, анонсировать UDP endpoint |
| `CallParticipantJoined` | `_audio.AddParticipant` + `AnnounceUdpEndpointAsync` |
| `CallParticipantLeft` | `_audio.RemoveParticipant` + очистить `_peerEndpoints` |
| `CallEnded` | `Cleanup()` → `CallEnded?.Invoke()` |
| `SignalReceived` (type=`udp-endpoint`) | Зарегистрировать `IPEndPoint` для peer |

> `CallService` отвечает **только за аудио/UDP логику**.
> Показ UI (`IncomingCallDialog`, `CallView`) — ответственность `MainMenuViewModel`.

### MainMenuViewModel — подписки на CallHub

```
_callHub.IncomingCall     += OnIncomingCall
_callHub.CallStateUpdated += OnCallStateUpdated
```

**Входящий звонок:**
```
OnIncomingCall(invite):
  → UIThread.Post:
    → new IncomingCallViewModel(callService, invite)
    → vm.Accepted += acceptedInvite => OnCallAcceptedAsync(acceptedInvite)
    → mainWindowVm.ShowDialogAsync(vm)

OnCallAcceptedAsync(invite):
  → _ = OpenCallChatAsync(invite)   ← фоновое открытие чата (без await)
  → state = await callHub.GetCallStateAsync(chatId)
  → _callViewShown = true
  → ShowCallViewAsync(state, chatName, isGroupCall)
```

**Инициатор (автоматический показ CallView):**
```
OnCallStateUpdated(state):
  → если _callViewShown → skip
  → если callService.ActiveCallId != state.CallId → skip
  → _callViewShown = true
  → UIThread.Post → ShowCallViewAsync(state, chatName, isGroupCall)
```

**ShowCallViewAsync:**
```
→ new CallViewModel(callService, callHub)
→ callVm.Initialize(state, chatName, isGroupCall)
→ callVm.CallFinished += () → { _callViewShown = false; CloseDialogAsync() }
→ mainWindowVm.ShowDialogAsync(callVm)
```

---

## Жизненный цикл (клиент)

### ChatHub
```
Login
  → ConnectAsync
    → SubscribeHubEvents
    → LoadUnreadCountsAsync

[автореконнект при обрыве]
  Reconnecting + 401 → TryRefreshTokenAsync
  Reconnected:
    1. LoadUnreadCountsAsync
    2. ReconcileAfterReconnectAsync  (логирование gap)
    3. Reconnected?.Invoke()
       → ChatHubSubscriber.onReconnected()
       → ChatMessageManager.GapFillAfterReconnectAsync

Logout
  → DisconnectAsync
  → DisposeAsync
    → UnsubscribeHubEvents
    → hub.StopAsync + hub.DisposeAsync
```

### CallHub
```
Login (Session.IsAuthenticated)
  → CallHubConnection.ConnectAsync

Reconnected:
  → логирование (gap-fill для звонков не реализован)

Logout / Dispose
  → CallHubConnection.DisconnectAsync
  → CallHubConnection.DisposeAsync
```

### CallService — жизненный цикл звонка
```
StartCallAsync(chatId):
  → InitUdp()        — UdpClient на случайном порту + ReceiveLoopAsync
  → _audio.Start()   — PortAudio + Opus
  → hub.InitiateCallAsync(chatId)

JoinCallAsync(callId, chatId):
  → если IsInCall → LeaveCallAsync()
  → InitUdp()
  → _audio.Start()
  → hub.JoinCallAsync(callId)
  → CallStarted?.Invoke()

LeaveCallAsync():
  → hub.LeaveCallAsync(callId)
  → Cleanup()
  → CallEnded?.Invoke()

CancelCallAsync():
  → hub.CancelCallAsync(callId)
  → Cleanup()
  → CallEnded?.Invoke()

Cleanup():
  → _receiveCts.Cancel() + Dispose
  → _udpClient.Dispose()
  → _peerEndpoints.Clear()
  → _endpointAnnounced.Clear()
  → _audio.Stop()
```

### Gap-fill после reconnect (ChatHub)
```
GapFillAfterReconnectAsync (в ChatMessageManager):
  → батчами GET /messages/chat/{id}/after/{newestId}
  → превышен лимит батчей → полный сброс к последним сообщениям
```

---

## Кэш при real-time событиях (ChatHub)

| Событие | Операция SQLite |
|---------|----------------|
| `ReceiveMessageDto` | `UpsertMessageAsync` + `UpdateChatLastMessageAsync` |
| `MessageUpdated` | `UpsertMessageAsync` |
| `MessageDeleted` | `MarkMessageDeletedAsync` |
| `ReceivePollUpdate` | `UpsertMessageAsync` (обновлённый `poll_json`) |
| `MarkChatAsReadAsync` | `UpdateReadPointerAsync(chatId, null, 0)` |

> CallHub события SQLite не кэшируют — состояние звонков хранится только in-memory на сервере.

---