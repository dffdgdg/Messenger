# Оценка работы Codex над REALTIME.md# Real-time / SignalR

## Инфраструктура
- Hub: `ChatHub` (`[Authorize]`), endpoint `/chatHub`
- Auth: JWT через `AccessTokenProvider` (query string)
- Реконнект: `WithAutomaticReconnect()`
- Серверная отправка из сервисов: `HubNotifier` (fire-and-forget, ошибки логируются)
- Клиент: singleton `GlobalHubConnection`
- Группы: `chat_{chatId}` (все участники), `user_{userId}` (персональная)

---

## Client → Server (Hub методы)

| Метод | Параметры | Описание |
|-------|-----------|----------|
| `JoinChat` | `int chatId` | Присоединиться к chat-группе |
| `LeaveChat` | `int chatId` | Покинуть chat-группу |
| `MarkAsRead` | `int chatId, int? messageId` | Отметить чат прочитанным |
| `MarkMessageAsRead` | `int chatId, int messageId` | Отметить конкретное сообщение |
| `SendTyping` | `int chatId` | Индикатор "печатает..." |
| `GetUnreadCounts` | — | → `AllUnreadCountsDto` |
| `GetReadInfo` | `int chatId` | → `ChatReadInfoDto?` |
| `GetOnlineUsersInChat` | `int chatId` | → список userId |

**Дебаунс на клиенте:**
- `MarkMessageAsRead`: не чаще `AppConstants.MarkAsReadDebounceMs`, пропуск если `messageId <= _lastSentReadMessageId`
- `SendTyping`: не чаще `AppConstants.TypingSendDebounceMs`

---

## Server → Client (события)

### Сообщения
| Событие | Payload | Описание |
|---------|---------|----------|
| `ReceiveMessageDto` | `MessageDto` | Новое сообщение |
| `MessageUpdated` | `MessageDto` | Отредактировано |
| `MessageDeleted` | `{ MessageId, ChatId }` | Удалено (soft) |
| `ReceivePollUpdate` | `PollDto` | Обновление результатов опроса после голосования/отмены |

### Уведомления
| Событие | Payload | Описание |
|---------|---------|----------|
| `ReceiveNotification` | `NotificationDto` | Push (message/mention/poll) |
| `UnreadCountUpdated` | `int chatId, int count` | Счётчик непрочитанных |

### Пользователи
| Событие | Payload |
|---------|---------|
| `UserOnline` | `int userId` |
| `UserOffline` | `int userId` |
| `UserProfileUpdated` | `UserDto` |

### Чат
| Событие | Payload |
|---------|---------|
| `UserTyping` | `int chatId, int userId` |
| `MessageRead` | `int chatId, int userId, int? lastReadMessageId, DateTime? readAt` |
| `MemberJoined` | `int chatId, UserDto user` |
| `MemberLeft` | `int chatId, int userId` |
---

## Клиентская обработка

### GlobalHubConnection — новое сообщение
1. `CacheIncomingMessageAsync` → SQLite
2. Поднять `MessageReceivedGlobally`
3. Чат не открыт + не свой → инкремент unread
4. Чат не открыт → desktop-уведомление

### GlobalHubConnection — уведомление
1. Проверить `SettingsService.NotificationsEnabled`
2. Текущий чат = чат уведомления → не показывать popup, но считать
3. Формат: `"{SenderName}: {Preview}"` (message/mention) или `"Новый опрос"` (poll)
4. `mention` — тот же маршрут, превью с сервера
5. Клик → `MainMenuViewModel.OpenNotificationAsync` → загрузка `ChatDto` + открытие с прокруткой к `MessageId`

### GlobalHubConnection — непрочитанные
- `Dictionary<int,int> _unreadCounts` + `_totalUnread`, thread-safe через `lock`
- При реконнекте: `LoadUnreadCountsAsync()` с сервера

### ChatHubSubscriber (текущий чат)
| Событие | Обработка |
|---------|-----------|
| `MessageReceivedGlobally` | `messageManager.AddReceivedMessage`  |
| `MessageUpdatedGlobally` | `messageManager.HandleMessageUpdated` |
| `MessageDeletedGlobally` | `messageManager.HandleMessageDeleted` |
| `PollUpdatedGlobally` | `messageManager.HandlePollUpdated` (обновляет poll state + SQLite cache) |
| `MessageRead` | обновить `IsRead` у сообщений с `Id <= lastReadId` |
| `UnreadCountChanged` | `onUnreadCountChanged(count)` |
| `Reconnected` | `onReconnected()` |

> typing и infoPanel подписываются в своих handler-ах отдельно

---

## Жизненный цикл (клиент)
```
Login → ConnectAsync → SubscribeHubEvents → LoadUnreadCountsAsync
         ↓                [автореконнект]
Logout → DisconnectAsync → Dispose → UnsubscribeHubEvents
```

**Реконнект:**
1. `Reconnecting` + 401 → refresh token
2. `Reconnected` → reload unread + reconciliation кеша
3. Событие `Reconnected` → ChatHubSubscriber перезагружает сообщения

**`SetCurrentChat(int? chatId)`** — текущий открытый чат:
- Подавляет уведомления для этого чата
- Сбрасывает дебаунс read/typing

**Кеш при real-time:**
- Новые/обновлённые → `UpsertMessageAsync`
- Удалённые → `MarkMessageDeletedAsync`
- Preview чата → `UpdateChatLastMessageAsync`