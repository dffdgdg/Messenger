# Desktop Client.md

## 1. Инициализация (`App.axaml.cs`)
```
Initialize():
  1. Build ServiceProvider (DI)
  2. Настроить AuthenticatedImageLoader (добавляет Authorization header)
  3. Запустить InitDB (фоновая задача)
  4. Применить тему из настроек
  5. Show MainWindow
```

---

## 2. Shell (`MainWindow` / `MainWindowViewModel`)
Содержит:
- Левый бар: навигация (Чаты, Контакты, Профиль, Админ, Настройки)
- Основная область: текущая View (динамически)
- Верхняя панель: поиск, профиль, тема

### MainMenuViewModel — вкладки
| Index | Содержимое |
|-------|-----------|
| 0 | Настройки |
| 1/2 | Групповые чаты (`ChatsViewModel`, `IsGroupMode=true`) |
| 3 | Личные диалоги (`ChatsViewModel`, `IsGroupMode=false`) |
| 4 | Профиль пользователя |
| 5 | Управление сотрудниками (Admin) |

`BackHistory` / `ForwardHistory` — стеки для навигации "Назад/Вперёд" между вкладками.

---

## 3. Список чатов (`ChatsViewModel`)
- `IsGroupMode=true` — группы, `false` — личные диалоги
- **Загрузка**: сначала SQLite (`ShowCachedChatsAsync`), затем фоновый запрос к API
- При недоступной сети — оставляет кэш, показывает ошибку
- Подписан на `GlobalHubConnection.TotalUnreadChanged` → обновляет бейджи
- Сортировка: по дате последнего сообщения

### Глобальный поиск (`GlobalSearchManager`)
Поддерживает переключаемые режимы:
- **Чаты** — поиск по названию/участникам
- **Контакты** — поиск пользователей
- **Сообщения** — поиск по всем доступным чатам (`GET /user/{id}/search`)
- **Локальный поиск** — по сообщениям текущего чата (доступен только при открытом чате)

> Переключатели режима видны только когда `IsSearchMode=true`.

---

## 4. ChatViewModel — открытие чата

### Жизненный цикл
1. Клик → `ChatsViewModel.SelectedChatChanged`
2. `ChatViewModelFactory.Create(chatId)` → новый экземпляр
3. `InitializeAsync()`:
   - Загрузить metadata чата
   - Загрузить участников (`ChatMemberLoader`)
   - Загрузить историю (`ChatMessageManager.LoadInitialMessagesAsync`)
   - Подписаться на SignalR (`ChatHubSubscriber.Subscribe()`)

### Composite Pattern — компоненты

| Компонент | Ответственность |
|-----------|----------------|
| `ChatMessageManager` | CRUD сообщений, пагинация, буферизация истории, gap-fill |
| `ChatAttachmentManager` | Выбор файлов, предпросмотр, загрузка на сервер |
| `ChatMemberLoader` | Загрузка списка участников |
| `ChatVoiceHandler` | Запись микрофона, отправка голосовых |
| `ChatInfoPanelHandler` | Управление участниками, права, инфопанель |
| `ChatSearchHandler` | Поиск по истории, навигация к сообщению |
| `ChatEditDeleteHandler` | Редактирование, удаление, копирование, закрепление/открепление |
| `ChatReplyHandler` | Цитирование (Reply) |
| `ChatTypingHandler` | Отправка события "печатает..." |
| `ChatNotificationHandler` | Mute/unmute уведомлений чата |

### ChatContext (разделяемое состояние)
Передаётся во все компоненты:
- `ChatDto`, список `Members`
- `IApiClientService`, `IGlobalHubConnection`
- `LifetimeToken` (отмена при закрытии чата)
- События координации: `ScrollToMessageRequested`, `CompositionModeReset`

### Mention-composer (`@username`)
- При вводе определяется mention-токен у каретки (`@...`)
- Список подсказок из `Members` (кроме текущего пользователя)
- Навигация: `Up`/`Down`/`Enter`/`Esc` + мышь
- При выборе — вставка `@username` на позицию токена

### Закреплённые сообщения
- Контекстное меню: **«Закрепить»** / **«Открепить»**
- UI опирается на `MessageDto.IsPinned` — иконка pin в мета-блоке
- Изменение состояния приходит через SignalR `MessageUpdated`

### Обновление опросов в real-time
1. `GlobalHubConnection` получает `ReceivePollUpdate` → поднимает `PollUpdatedGlobally`
2. `ChatHubSubscriber` делегирует в `ChatMessageManager.HandlePollUpdated`
3. `ChatMessageManager` обновляет `MessageViewModel.UpdatePoll(...)` + сохраняет `poll_json` в SQLite
4. После `POST /api/polls/vote` `PollViewModel` немедленно обновляет `MessageViewModel`
   и сохраняет в кэш — не ожидая SignalR-события

---

## 5. DialogService

Все модальные окна через стек (`DialogStack`):
- **Показать**: добавляется в конец, становится текущим
- **Закрыть**: удаляется верхний, активируется следующий

**Порядок показа** (через `SemaphoreSlim` + `Channel<CloseRequest>`):
1. `ShowAsync(dialog)` → лок semaphore
2. Отписать старый диалог
3. Добавить новый
4. Fade-in анимация
5. `NotifyAnimationComplete()` из ViewModel

**Переиспользуемые диалоги:**
- `UserPickerDialog` — single/multi-select пользователя
- `UserListDialog` — список с режимами просмотра и редактирования
- `UserListItemViewModel` — общий item для всех пользовательских списков
  (переиспользовать, не создавать новые)

---

## 6. Локальный кэш (SQLite)

### Путь к файлу
```
%LocalAppData%/MessengerDesktop/messenger_cache.db
```

### Таблицы (схема v3)
| Таблица | Назначение |
|---------|-----------|
| `messages` | История переписки |
| `chats` | Список чатов |
| `users` | Контакты |
| `chat_sync_state` | Метаданные синхронизации (`OldestLoadedId`, `NewestLoadedId`, `HasMoreOlder`, `HasMoreNewer`) |
| `messages_fts` | FTS5 — полнотекстовый поиск |

### PRAGMA настройки
```sql
PRAGMA journal_mode=WAL;       -- Write-Ahead Logging
PRAGMA synchronous=NORMAL;     -- баланс надёжности и скорости
PRAGMA cache_size=-4000;       -- ~4MB RAM
PRAGMA mmap_size=33554432;     -- 32MB Memory-mapped I/O
```

### Стратегия загрузки
1. `ChatSyncState` есть → показать сразу из SQLite
2. Фоновая ревалидация с сервером при открытии чата
3. Авто-очистки нет (TODO: LRU policy)

### Операции кэша при real-time событиях
| Событие | Операция |
|---------|---------|
| Новое сообщение | `UpsertMessageAsync` + `UpdateChatLastMessageAsync` |
| Сообщение обновлено | `UpsertMessageAsync` |
| Сообщение удалено | `MarkMessageDeletedAsync` |
| Чат прочитан | `UpdateReadPointerAsync(chatId, null, 0)` |
| Poll обновлён | `UpsertMessageAsync` (с обновлённым `poll_json`) |

---

## 7. GlobalHubConnection — детали реализации

### SetCurrentChat
```csharp
SetCurrentChat(int? chatId)
```
При смене открытого чата:
- Устанавливает `_openChatId`
- Сбрасывает `_lastSentReadMsgId = 0`
- Сбрасывает дебаунс-таймеры read и typing (`DateTime.MinValue`)

### Дебаунс
| Операция | Константа | Дополнительное условие |
|---------|-----------|------------------------|
| `MarkMessageAsRead` | `AppConstants.MarkAsReadDebounceMs` | Пропуск если `messageId <= _lastSentReadMsgId` |
| `SendTyping` | `AppConstants.TypingSendDebounceMs` | — |

### Unread counters
- `Dictionary<int, int> _unreadCounts` + `_totalUnread`
- Thread-safe через `Lock _unreadLock`
- `UpdateUnread(chatId, newCount)` — установить конкретное значение
- `IncrementUnread(chatId)` — +1 при входящем сообщении не в текущем чате

### Уведомления — логика показа
```
OnNotificationReceived(NotificationDto n):
  disposed == true        → skip
  !settings.NotificationsEnabled → skip
  _openChatId == n.ChatId → skip (чат открыт)
  
  type == "poll" → заголовок: n.ChatName, текст: n.Preview ?? "Новый опрос"
  иначе          → заголовок: n.ChatName, текст: "{n.SenderName}: {n.Preview}"
  
  Клик → nav.CurrentViewModel is MainMenuViewModel vm
       → vm.OpenNotificationAsync(n)
```

### Reconnect
```
Reconnecting + 401/Unauthorized → _auth.TryRefreshTokenAsync()
Reconnected:
  1. LoadUnreadCountsAsync()      — перезагрузить счётчики
  2. ReconcileAfterReconnectAsync() — проверить gap (логирование)
  3. Reconnected?.Invoke()        — ChatHubSubscriber перезагружает сообщения
```

> `ReconcileAfterReconnectAsync` только логирует состояние.
> Реальный gap-fill выполняется в `ChatMessageManager.GapFillAfterReconnectAsync`
> по событию `Reconnected` из `ChatHubSubscriber`.

### Dispose
- `Dispose()` — синхронный, запускает `DisposeHubAsync` в `Task.Run`
- `DisposeAsync()` — асинхронный, awaits `DisposeHubAsync`
- Оба защищены от двойного вызова через `Interlocked.Exchange(ref _disposed, 1)`

---

## 8. In-app уведомления

Используется **собственный overlay** внутри `MainWindow` (не нативные toast).
Одинаковое поведение на Windows и Linux.

**Схема:**
- `INotificationService` → observable-коллекция активных уведомлений
- `MainWindow` хостит `NotificationOverlay` (`ItemsControl`, правый верхний угол)
- Каждое уведомление: `ActivateCommand` + `CloseCommand`
- Клик → `MainMenuViewModel.OpenNotificationAsync(n)` → нужная вкладка + прокрутка к `MessageId`

**Правила:**
- Максимум 3 одновременно
- Автоскрытие по таймеру (5000 мс по умолчанию)
- Если `_openChatId == n.ChatId` — popup не показывается

---

## 9. Утилиты

### AvatarHelper
- Если путь не начинается с `http` → добавить базовый URL API
- Добавляет `?t={timestamp}` для сброса кэша браузера (cachebuster)

### ChatPreviewFormatter
- `BuildPreviewWithMeta(msg, userId)` → `(preview, meta)` для превью чата в списке
- `FormatSenderName(senderName, senderId, currentUserId)` → имя отправителя для превью

### AsyncImageLoader (`AsyncImageLoader.Avalonia`)
- Асинхронная загрузка без блокировки UI
- LruCache в памяти
- `AuthenticatedImageLoader` добавляет `Authorization` header к каждому запросу

---

## 10. Debug / Release конфигурация

| | Debug | Release |
|-|-------|---------|
| API URL | `https://localhost:7190/` | `https://localhost:5274/` |
| Avalonia Diagnostics | ✅ | ❌ |
| SSL validation | `DangerousAcceptAnyServerCertificateValidator` | Strict |
| EF SensitiveDataLogging | ✅ | ❌ |
| JSON WriteIndented | ✅ | ❌ |

---

## 11. Known Issues

| Проблема | Детали | Решение/Статус |
|---------|--------|----------------|
| **Context Leak** | `ChatContext` держит ссылки на API/Hub; при быстром закрытии старые задачи могут писать в disposed контекст | `CancellationTokenSource _lifetimeCts` |
| **Race Condition** | Одновременное редактирование с двух устройств — нет optimistic locking | Last-write-wins по серверным событиям |
| **Memory Leak** | `GlobalHubConnection` подписан на события синглтонов | Явный `Dispose()` с отпиской через `_subs` |
| **Thread Safety** | UI-изменения только через `Dispatcher.UIThread.Post` | `lock (_unreadLock)` для non-UI логики |
| **Double Registration** | `AudioRecorderService` зарегистрирован дважды в DI | Баг, фактически используется последняя регистрация |
| **Gap Fill** | При долгом оффлайне часть истории теряется из view | `GapFillAfterReconnectAsync` в `ChatMessageManager` |
| **Аудио macOS** | Не тестировалось | TODO |
```