# Real-time / SignalR

## Инфраструктура
- Hub: `ChatHub` (`[Authorize]`), endpoint `/chatHub`
- Auth: JWT через `AccessTokenProvider` (query string)
- Реконнект: `WithAutomaticReconnect()`
- Серверная отправка из сервисов: `HubNotifier` (fire-and-forget, ошибки логируются)
- Клиент: singleton `GlobalHubConnection`
- Группы: `chat_{chatId}` (все участники), `user_{userId}` (персональная)

---

## Client → Server (Hub методы)

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

### Дебаунс на клиенте

| Метод | Константа | Дополнительное условие пропуска |
|-------|-----------|--------------------------------|
| `MarkMessageAsRead` | `AppConstants.MarkAsReadDebounceMs` | `messageId <= _lastSentReadMsgId` |
| `SendTyping` | `AppConstants.TypingSendDebounceMs` | — |

> `SetCurrentChat(int? chatId)` сбрасывает оба дебаунс-таймера и `_lastSentReadMsgId = 0`.

---

## Server → Client (события)

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

## Клиентская обработка

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

## Жизненный цикл (клиент)

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

### Gap-fill после reconnect
```
GapFillAfterReconnectAsync (в ChatMessageManager):
  → батчами GET /messages/chat/{id}/after/{newestId}
  → превышен лимит батчей → полный сброс к последним сообщениям
```

---

## Кэш при real-time событиях

| Событие | Операция SQLite |
|---------|----------------|
| `ReceiveMessageDto` | `UpsertMessageAsync` + `UpdateChatLastMessageAsync` |
| `MessageUpdated` | `UpsertMessageAsync` |
| `MessageDeleted` | `MarkMessageDeletedAsync` |
| `ReceivePollUpdate` | `UpsertMessageAsync` (обновлённый `poll_json`) |
| `MarkChatAsReadAsync` | `UpdateReadPointerAsync(chatId, null, 0)` |
```