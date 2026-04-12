# Desktop Client Architecture

## 1. Инициализация (`App.axaml.cs`)
```
Initialize():
  1. Build ServiceProvider (DI)
  2. Настроить AuthenticatedImageLoader (добавляет Authorization header)
  3. Запустить InitDB (фоновая задача)
  4. Применить тему из настроек
  5. Show MainWindow
```

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

## 3. Список чатов (`ChatsViewModel`)
- `IsGroupMode=true` — группы, `false` — личные диалоги
- **Загрузка**: сначала SQLite (`ShowCachedChatsAsync`), затем фоновый запрос к API
- При недоступной сети — оставляет кэш, показывает ошибку
- Подписан на `GlobalHubConnection.TotalUnreadChanged` → обновляет бейджи
- Сортировка: по дате последнего сообщения
- Глобальный поиск поддерживает переключаемые режимы: **Чаты**, **Контакты**, **Сообщения открытого чата** (локальный поиск в текущем чате доступен только при выбранном чате)

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
| `ChatMessageManager` | CRUD сообщений, пагинация, буферизация истории |
| `ChatAttachmentManager` | Выбор файлов, предпросмотр, загрузка на сервер |
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
- `IApiClient`, `IGlobalHubConnection`
- `LifetimeToken` (отмена при закрытии чата)
- События координации: `ScrollToMessageRequested`, `CompositionModeReset`

### Mention-composer (`@username`)
- При вводе определяется mention-токен у каретки (`@...`)
- Список подсказок из `Members` (кроме текущего пользователя)
- Навигация: `Up`/`Down`/`Enter`/`Esc` + мышь
- При выборе — вставка `@username` на позицию токена

- Переключатели режима показываются только в состоянии активного поиска (когда `IsSearchMode=true`)

### Закрепленные сообщения
- В контекстном меню сообщения доступны действия **«Закрепить»** / **«Открепить»**.
- UI опирается на `MessageDto.IsPinned`; закрепленные сообщения помечаются иконкой pin в мета-блоке сообщения.
- Изменение состояния приходит через стандартное SignalR-событие `MessageUpdated`.

### Обновление опросов в real-time
- `GlobalHubConnection` принимает событие `ReceivePollUpdate` (`PollDto`) и пробрасывает его как `PollUpdatedGlobally`.
- `ChatHubSubscriber` делегирует событие в `ChatMessageManager.HandlePollUpdated`.
- `ChatMessageManager` обновляет `MessageViewModel.UpdatePoll(...)` и сохраняет актуальный `poll_json` в SQLite, чтобы после перезапуска не показывались устаревшие результаты.
- После успешного `POST /api/polls/vote` `PollViewModel` дополнительно уведомляет `MessageViewModel`, и состояние опроса сразу сохраняется в локальный кэш даже до прихода SignalR-события.

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
- `UserListItemViewModel` — общий item для всех пользовательских списков (переиспользовать, не создавать новые)

---

## 6. Локальный кэш (SQLite)

### Таблицы (схема v3)
| Таблица | Назначение |
|---------|-----------|
| `messages` | История переписки |
| `chats` | Список чатов |
| `users` | Контакты |
| `chat_sync_state` | Метаданные синхронизации (последний загруженный `message_id`) |
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

---

## 7. In-app уведомления

Используется **собственный overlay** внутри `MainWindow` (не нативные toast), это обеспечивает одинаковое поведение на Windows и Linux.

**Схема:**
- `INotificationService` → observable-коллекция активных уведомлений
- `MainWindow` хостит `NotificationOverlay` (`ItemsControl`, правый верхний угол)
- Каждое уведомление: `ActivateCommand` + `CloseCommand`
- Клик → `GlobalHubConnection` → `MainMenuViewModel.OpenNotificationAsync()` → нужная вкладка + прокрутка к `MessageId`

**Правила:**
- Максимум 3 одновременно
- Автоскрытие по таймеру
- Если чат уже открыт — popup не показывается, только unread-счётчик

---

## 8. Утилиты

### AvatarHelper
- Если путь не начинается с `http` → добавить базовый URL API
- Добавляет `?t={timestamp}` для сброса кэша (cachebuster)

### AsyncImageLoader (`AsyncImageLoader.Avalonia`)
- Асинхронная загрузка без блокировки UI
- LruCache в памяти
- `AuthenticatedImageLoader` добавляет `Authorization` header

---

## 9. Debug / Release конфигурация

| | Debug | Release |
|-|-------|---------|
| API URL | `https://localhost:7190/` | `https://localhost:5274/` |
| Avalonia Diagnostics | ✅ | ❌ |
| SSL validation | Relaxed | Strict |

---

## 10. Known Issues

| Проблема | Детали | Решение |
|---------|--------|---------|
| **Context Leak** | `ChatContext` держит ссылки на API/Hub; при быстром закрытии старые задачи могут писать в disposed контекст | `CancellationTokenSource _lifetimeCts` |
| **Race Condition** | Одновременное редактирование с двух устройств — нет optimistic locking | Last-write-wins по серверным событиям |
| **Memory Leak** | `GlobalHubConnection` подписан на события синглтонов | Явный `Dispose()` с отпиской |
| **Thread Safety** | UI-изменения только через `Dispatcher.UIThread.Post` | `lock (_lock)` для non-UI логики |