# Real-time Communication (SignalR)

## Общая информация
- **Технология**: SignalR (ASP.NET Core)
- **Hub**: `ChatHub` — единственный хаб
- **URL подключения**: `{ApiUrl}/chatHub`
- **Аутентификация**: JWT через `AccessTokenProvider` (query string автоматически)
- **Реконнект**: `WithAutomaticReconnect()` (встроенная стратегия SignalR)

## Архитектура

### Серверная сторона
- **`ChatHub`** — SignalR Hub, обрабатывает входящие вызовы от клиентов
- **`HubNotifier`** — сервис для отправки событий из бизнес-логики (Scoped, инжектится в сервисы)

### Клиентская сторона
- **`GlobalHubConnection`** — Singleton, управляет единым SignalR-подключением
- **`ChatHubSubscriber`** — подписка на события для конкретного открытого чата (создаётся/уничтожается при открытии/закрытии чата)

---

## SignalR группы

Используются два типа групп:

| Группа | Формат | Описание |
|--------|--------|----------|
| Чат | `chat_{chatId}` | Все участники чата |
| Пользователь | `user_{userId}` | Персональная группа пользователя |

### HubNotifier — отправка из сервисов

```csharp
// Отправка всем участникам чата
await hubNotifier.SendToChatAsync(chatId, "ReceiveMessageDto", messageDto);

// Отправка конкретному пользователю
await hubNotifier.SendToUserAsync(userId, "UserProfileUpdated", userDto);
```

Ошибки отправки **логируются, но не пробрасываются** — fire-and-forget подход.

---

## Серверные методы (Client → Server)

Методы, которые клиент вызывает на Hub через `InvokeAsync`:

| Метод | Параметры | Описание |
|-------|-----------|----------|
| `MarkAsRead` | `int chatId, int? messageId` | Отметить чат/сообщение прочитанным |
| `MarkMessageAsRead` | `int chatId, int messageId` | Отметить конкретное сообщение прочитанным |
| `SendTyping` | `int chatId` | Отправить индикатор "печатает..." |
| `GetUnreadCounts` | — | Получить все счётчики непрочитанных (returns `AllUnreadCountsDto`) |
| `GetReadInfo` | `int chatId` | Получить информацию о прочтении чата (returns `ChatReadInfoDto?`) |

### Дебаунсинг на клиенте

| Операция | Дебаунс | Константа |
|----------|---------|-----------|
| MarkMessageAsRead | Не чаще чем раз в N мс | `AppConstants.MarkAsReadDebounceMs` |
| SendTyping | Не чаще чем раз в N мс | `AppConstants.TypingSendDebounceMs` |
| MarkMessageAsRead | Пропуск если `messageId <= _lastSentReadMessageId` | — |

---

## Клиентские методы (Server → Client)

Методы, которые сервер вызывает на клиенте:

### Сообщения

| Метод | Данные | Описание |
|-------|--------|----------|
| `ReceiveMessageDto` | `MessageDto` | Новое сообщение в чате |
| `MessageUpdated` | `MessageDto` | Сообщение отредактировано |
| `MessageDeleted` | `MessageDeletedEvent { MessageId, ChatId }` | Сообщение удалено |

### Уведомления и счётчики

| Метод | Данные | Описание |
|-------|--------|----------|
| `ReceiveNotification` | `NotificationDto` | Push-уведомление о новом сообщении |
| `UnreadCountUpdated` | `int chatId, int unreadCount` | Обновлённый счётчик непрочитанных |

### Статусы пользователей

| Метод | Данные | Описание |
|-------|--------|----------|
| `UserOnline` | `int userId` | Пользователь вошёл в сеть |
| `UserOffline` | `int userId` | Пользователь вышел из сети |
| `UserProfileUpdated` | `UserDto` | Профиль пользователя обновлён |

### Чат и участники

| Метод | Данные | Описание |
|-------|--------|----------|
| `UserTyping` | `int chatId, int userId` | Пользователь печатает |
| `MessageRead` | `int chatId, int userId, int? lastReadMessageId, DateTime? readAt` | Сообщение прочитано |
| `MemberJoined` | `int chatId, UserDto user` | Участник присоединился к чату |
| `MemberLeft` | `int chatId, int userId` | Участник покинул чат |

### Голосовые сообщения

| Метод | Данные | Описание |
|-------|--------|----------|
| `TranscriptionStatusChanged` | `VoiceTranscriptionDto` | Статус транскрипции изменился |
| `TranscriptionCompleted` | `VoiceTranscriptionDto` | Транскрипция завершена |

---

## Клиентская обработка событий

### GlobalHubConnection (глобальные)

Все входящие события диспатчатся в UI-поток через `Dispatcher.UIThread.Post()`.

**Логика обработки новых сообщений:**
1. Кешировать в SQLite (`CacheIncomingMessageAsync`)
2. Поднять событие `MessageReceivedGlobally`
3. Если чат **не открыт** и отправитель **не текущий пользователь** → инкрементировать unread count
4. Если чат **не открыт** → показать desktop-уведомление

**Логика уведомлений:**
1. Проверить `SettingsService.NotificationsEnabled`
2. Если текущий открытый чат = чат уведомления → не показывать popup, но инкрементировать счётчик
3. Формат: `"{SenderName}: {Preview}"` или `"Новый опрос"` для poll-типа
4. `ShowWindow(..., onClick: ...)` передает callback, который использует текущий `MainMenuViewModel` и вызывает `OpenNotificationAsync(notification)`
5. `MainMenuViewModel.OpenNotificationAsync` при необходимости догружает `ChatDto`, выбирает нужную вкладку (группы/контакты) и, если в `NotificationDto` есть `MessageId`, открывает чат с прокруткой к конкретному сообщению

**Логика непрочитанных:**
- `Dictionary<int, int> _unreadCounts` — локальный кеш счётчиков
- `_totalUnread` — суммарный счётчик
- Thread-safe через `lock (_lock)`
- При реконнекте загружаются заново с сервера (`LoadUnreadCountsAsync`)

### ChatHubSubscriber (для открытого чата)

Фильтрует события по `chatId` текущего чата:

| Событие | Обработка |
|---------|-----------|
| `MessageReceivedGlobally` | → `messageManager.AddReceivedMessage(msg)` + запуск polling транскрипции |
| `MessageUpdatedGlobally` | → `messageManager.HandleMessageUpdated(msg)` |
| `MessageDeletedGlobally` | → `messageManager.HandleMessageDeleted(messageId)` |
| `MessageRead` | → обновить `IsRead` у своих сообщений с `Id <= lastReadId` |
| `UnreadCountChanged` | → callback `onUnreadCountChanged(count)` |
| `TranscriptionStatusChanged` | → `message.UpdateTranscription(status, text)` + polling |
| `TranscriptionCompleted` | → `message.UpdateTranscription(status, text)` |
| `Reconnected` | → callback `onReconnected()` |

**Подписки не дублируют:** typing и infoPanel подписываются отдельно в своих handler-ах.

---

## Управление подключением

### Жизненный цикл
```
Login → ConnectAsync() → SubscribeHubEvents() → LoadUnreadCountsAsync()
                                    ↓
                    [работа, автореконнект]
                                    ↓
Logout → DisconnectAsync() → Dispose() → UnsubscribeHubEvents()
```

### Реконнект
1. `WithAutomaticReconnect()` — стандартная стратегия SignalR
2. `Reconnecting` → если 401 → попытка refresh token
3. `Reconnected` → перезагрузка unread counts + reconciliation кеша
4. Поднятие события `Reconnected` → ChatHubSubscriber перезагружает сообщения

### Текущий чат
`SetCurrentChat(int? chatId)` — устанавливает текущий открытый чат:
- Влияет на подавление уведомлений
- Сбрасывает дебаунс-счётчики (read, typing)

### Кеширование при real-time
При получении сообщений через SignalR:
- Новые сообщения → `cacheService.UpsertMessageAsync()`
- Обновлённые → `cacheService.UpsertMessageAsync()`
- Удалённые → `cacheService.MarkMessageDeletedAsync()`
- Обновление preview чата → `cacheService.UpdateChatLastMessageAsync()`

---

## IDisposable / IAsyncDisposable

`GlobalHubConnection` реализует оба интерфейса:
- Отписка от всех hub-событий через сохранённые `IDisposable` подписки
- Отписка от lifecycle-событий (`Reconnecting`, `Reconnected`)
- Остановка и dispose HubConnection
- Best-effort — ошибки при dispose игнорируются