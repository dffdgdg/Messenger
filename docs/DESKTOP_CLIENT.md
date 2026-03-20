## DESKTOP_CLIENT.md

```markdown
# Desktop Client Architecture

## Обзор структуры
Приложение построено по паттерну **MVVM** на базе **Avalonia UI**. Основной стек технологий: `CommunityToolkit.Mvvm` (для ObservableObject/RelayCommand) и `Microsoft.Extensions.DependencyInjection` для DI.

## Жизненный цикл приложения

### 1. Инициализация (`App.axaml.cs`)
```csharp
Initialize():
  1. Build ServiceProvider (DI)
  2. Настроить AuthenticatedImageLoader
  3. Запустить InitDB (фоновая задача)
  4. Применить тему настроек
  5. Show MainWindow
```

### 2. Шелл (`MainWindow.axaml`)
Это контейнер для всего UI. Содержит:
- Меню (слева): Навигационный бар (Чаты, Контакты, Админ...).
- Основная область: Текущая View (динамически меняется).
- Верхняя панель: Поиск, Профиль, Переключение темы.

### 3. Виджет навигации (`MainMenuViewModel`)
Единая точка управления контентом. Содержит 4 состояния (вкладки):
- **Index 1/2**: Групповые чаты (`ChatsViewModel`).
- **Index 3**: Личные диалоги (`ChatsViewModel` с фильтром контактов).
- **Index 4**: Профиль пользователя.
- **Index 5**: Управление сотрудниками (Admin view).
- **Index 0**: Настройки.

**Управление историей**: Внутри `MainMenuViewModel` есть стеки `BackHistory` и `ForwardHistory` для кнопки "Назад" между вкладками.

### 4. Список чатов (`ChatsViewModel`)
- **Типы режима**: `IsGroupMode=true` (Группы) / `false` (Личные).
- **Нагрузка**:
  - Сначала показывает данные из SQLite (`ShowCachedChatsAsync`).
  - Затем параллельно грузит свежие данные с API.
  - Если сеть упала — оставляет кэш с ошибкой.
- **Unread Counts**: Подписан на события `GlobalHubConnection.TotalUnreadChanged` и обновляет бейджи в UI.
- **Сортировка**: По дате последнего сообщения.

---

## Открытие чата (`ChatViewModel`)

### Жизненный цикл открытия
1. Пользователь кликает на элемент из списка (`ChatsViewModel.SelectedChatChanged`).
2. `ChatViewModelFactory.Create(chatId)` создает новый экземпляр.
3. `ChatViewModel` запускает `InitializeAsync()`.
4. Загружает инфу о чате (Metadata).
5. Загружает список участников (`MemberLoader`).
6. Загружает историю сообщений (`MessageManager.LoadInitialMessagesAsync`).
7. Подписывается на реальное время через `ChatHubSubscriber.Subscribe()`.

### Архитектура внутри чата (Composite Pattern)
`ChatViewModel` не содержит всей логики. Он делегирует ответственность **Handlers** и **Managers**:

| Компонент | Ответственность |
|-----------|----------------|
| `ChatMessageManager` | CRUD сообщений, пагинация, буферизация истории |
| `ChatAttachmentManager` | Выбор файлов, предпросмотр, загрузка на сервер |
| `ChatVoiceHandler` | Запись микрофона, декодирование, траскрипция |
| `ChatInfoPanelHandler` | Управление правами участников, уведомления |
| `ChatSearchHandler` | Локальный поиск по истории |
| `ChatEditDeleteHandler` | Редактирование текста, удаление |
| `ChatReplyHandler` | Цитирование (Reply) |
| `ChatTypingHandler` | Отправка события "печатает..." |
| `ChatNotificationHandler` | Мут/Размут уведомлений для чата |

**Общий контекст**: `ChatContext` хранит состояние, разделяемое всеми компонентами:
- `ChatDto` объект.
- Список `Members`.
- `IApiClient`, `IGlobalHubConnection`.
- Event-ы на скролл (чтобы View знала когда подниматься).

---

## Система диалогов (`DialogService`)

### Принцип работы
Все модальные окна работают через стек (`DialogStack`).
- **Показать**: Добавляется в конец стека. Становится текущим.
- **Закрыть**: Удаляется верхний элемент. Активируется следующий (если есть) или скрывается окно.

### Анимация
Используется блокировка через `SemaphoreSlim` и `Channel<CloseRequest>`. Это позволяет гарантировать порядок закрытия окон, даже если пользователь многократно нажимал "Закрыть".

**Пример потока:**
1. `ShowAsync(PollDialog)`
2. Лок semaphore
3. Отписать старый диалог от событий
4. Добавить новый
5. Включить анимацию (fade-in)
6. Вызвать `NotifyAnimationComplete()` из ViewModel при готовности

**Правила безопасности**: Нельзя показать модальное окно поверх другого модального без явного разрешения (stack behavior).

---

## Локальное кеширование (SQLite)

### Схема
В `LocalDatabase.cs` определена схема версии 3. Основные таблицы:
- `messages` — история переписки.
- `chats` — список чатов.
- `users` — контакты.
- `chat_sync_state` — мета-данные синхронизации (какой последний message_id загружен).
- `messages_fts` — виртуальная таблица **FTS5** для текстового поиска.

### PRAGMAs оптимизации
Для скорости записи включены настройки:
```sql
PRAGMA journal_mode=WAL;          -- Write-Ahead Logging
PRAGMA synchronous=NORMAL;        -- Компромисс надежности и скорости
PRAGMA cache_size=-4000;          -- ~4MB RAM
PRAGMA mmap_size=33554432;        -- 32MB Memory-mapped I/O
```

### Стратегия загрузки
1. **Чтения**: Если `ChatSyncState` говорит, что есть сообщения > `lastReadAt`, показываем сразу.
2. **Фоновое обновление**: При открытии чата `MessageManager` проверяет наличие новых сообщений на сервере и догружает "на лету".
3. **Очистка**: Нет авто-очистки старых сообщений. Есть ручной VACUUM через консольные команды (Future TODO).

---

## Real-Time интеграция (`GlobalHubConnection`)

### Подключение
Создается один раз на уровне `App` (как Singleton в DI).
- Поддерживает `WithAutomaticReconnect()`.
- При потере соединения пытается обновить токен (если 401 Unauthorized).
- При успешном reconnect — запрашивает актуальные счетчики непрочитанных (`GetUnreadCountsAsync`).

### Обработка входящих событий
В `GlobalHubConnection` подписан на все события через `_hubSubscriptions.Add(...)`.
Каждое событие проходит через обработчик, который может:
1. Обновить кэш SQLite (`CacheIncomingMessageAsync`).
2. Триггерить UI событие (через `Dispatcher.UIThread.Post`).
3. Обновить локальные переменные (unreads).

### Перехватчик чата (`ChatHubSubscriber`)
Фильтрует глобальные события по конкретному открытому чату:
- Только события `messageReceivedGlobally` с `msg.ChatId == ctx.ChatId` попадают в UI этого чата.
- Остальные события (например, уведомление о новом сообщении в другом чате) поднимают общий счётчик, но не показываются визуально внутри окна чата.

---

## Утилиты и помощники

### AvatarHelper
Стандартизирует пути к аватаркам:
- Проверяет, начинается ли путь с `http`.
- Если нет — добавляет базовый URL API.
- Добавляет timestamp для сброса кеша браузера клиента (`cachebuster`).

### AsyncImageLoader
Асинхронная загрузка картинок (библиотека `AsyncImageLoader.Avalonia`):
- Предотвращает зависимость UI при загрузке больших картинок.
- Автоматически кеширует изображения в память (LruCache).
- Использует настроенный `AuthenticatedImageLoader` (добавляет Authorization header при запросе аватарок).

---

## Режим разработки (Debug)
В режиме разработки (`#if DEBUG`):
- Base API URL: `https://localhost:7190/`
- Enable Debug UI (Avalonia.Diagnostics)
- Sensitive Data Logging в Entity Framework

В Release:
- Base API URL: `https://localhost:5274/` (production proxy placeholder)
- Disable Debug UI
- Strict SSL validation включена (кроме localhost)

---

## Потенциальные проблемы (Known Issues in Implementation)
- **Leakage Context**: `ChatContext` хранит ссылки на API и Hub. При быстром закрытии чата (`DisposeAsync`) нужно гарантировать, что старые задачи завершения (`InitializationTask`) не начнут писать в disposed контекст. Сейчас реализовано через `CancellationTokenSource _lifetimeCts`.
- **Race Condition Edit**: При одновременном редактировании одного чата с двух устройств. Здесь нет оптимистических обновлений (оптимистичных UI), обновления происходят строго по серверным ответам или событиям.
- **Memory Leak**: `GlobalHubConnection` подписан на события в статике или синглтонах. Важно отписаться через `Dispose()`. Реализовано явно.
- **Thread Safety**: `ChatHubSubscriber` использует `Dispatcher.UIThread.Post` для всех изменений UI. Логики вне UI безопасны благодаря использованию `lock (_lock)` в `GlobalHubConnection`.
```