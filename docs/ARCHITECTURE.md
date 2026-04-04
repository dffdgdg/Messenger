## Обзор решения
Решение состоит из трёх проектов:
1. **MessengerAPI** — ASP.NET Core Web API (REST + SignalR).
2. **MessengerDesktop** — Avalonia UI Desktop Client (MVVM, SQLite, Local DB).
3. **MessengerShared** — Общие DTO, Enum-ы и контракты ответов.

### Взаимодействие
| Направление | Протокол | Формат данных | Аутентификация |
|-------------|----------|---------------|----------------|
| Desktop → API | HTTPS / REST | JSON (`ApiResponse<T>`) | JWT Bearer Token |
| Desktop ↔ API | WebSocket | SignalR binary/text | JWT в `accessTokenProvider` |

---

## Backend Architecture (MessengerAPI)

### Слои
```
[Controllers]  →  BaseController.ExecuteAsync  →  Result<T> → HTTP + ApiResponse<T>
[Services]     →  Business Logic, Transaction boundaries
[Infrastructure] → Cache, AccessControl, HubNotifier, UrlBuilder
[Model / DbContext] → EF Core, PostgreSQL
```

### DI-регистрация (Lifetime)

**Singleton**: `OnlineUserService`, `AppDateTime`, `TimeProvider`.
**Scoped** (per-request): все бизнес-сервисы (`ChatService`, `MessageService`, `ChatMemberService`, `NotificationService`, `ReadReceiptService`, `PollService`, `UserService`, `AdminService`, `DepartmentService`, `AuthService`, `TokenService`, `SystemMessageService`), все инфраструктурные (`CacheService`, `AccessControlService`, `FileService`, `HubNotifier`, `HttpUrlBuilder`).

### Сервисы — карта

| Сервис | Файл | Ответственность |
|--------|------|-----------------|
| `ChatService` | `Services/Chat/ChatService.cs` | CRUD чатов, аватары, инвалидация кэша |
| `ChatMemberService` | `Services/Chat/ChatMemberService.cs` | Участники: добавление, удаление, смена ролей, выход. Проверяет права, создаёт системные сообщения |
| `SystemMessageService` | `Services/Chat/SystemMessageService.cs` | Системные сообщения (MemberAdded/Removed/Left/RoleChanged). Не создаёт для Contact. Отправляет в SignalR |
| `NotificationService` | `Services/Chat/NotificationService.cs` | Push-уведомления через SignalR. Mute/unmute per-chat, отдельный тип `mention` для `@username` |
| `MessageService` | `Services/Messaging/MessageService.cs` | CRUD сообщений, атомарное создание (Message+Voice+Files в одном SaveChanges). Пагинация (around/before/after), поиск (ILike). Уведомляет участников, обновляет непрочитанные, извлекает `@username` и запускает mention-уведомления |
| `FileService` | `Services/Messaging/FileService.cs` | Загрузка файлов/изображений. Конвертация в WebP (ImageSharp) |
| `PollService` | `Services/Messaging/PollService.cs` | Опросы, голосование |
| `ReadReceiptService` | `Services/ReadReceipt/ReadReceiptService.cs` | Прочитанные (`LastReadMessageId` в `ChatMember`), счётчики, `FirstUnreadMessageId` |
| `AuthService` | `Services/Auth/AuthService.cs` | Логин, рефреш, отзыв токенов |
| `TokenService` | `Services/Auth/TokenService.cs` | Генерация JWT, управление Refresh Token (SHA-256, FamilyId) |
| `UserService` | `Services/User/UserService.cs` | Профиль, аватар, смена пароля/логина |
| `AdminService` | `Services/User/AdminService.cs` | CRUD пользователей, блокировка |
| `DepartmentService` | `Services/Department/DepartmentService.cs` | Департаментская иерархия |
| `CacheService` | `Services/Infrastructure/CacheService.cs` | `IMemoryCache`, TTL 5/10 мин, явная инвалидация |
| `AccessControlService` | `Services/Infrastructure/AccessControlService.cs` | Проверка прав (Member/Admin/Owner), кэш на запрос + IMemoryCache. `GetUserChatIdsAsync` |
| `OnlineUserService` | `Services/Infrastructure/OnlineUserService.cs` | Потокобезопасный трекинг онлайна (ConcurrentDictionary), несколько соединений на юзера |
| `HubNotifier` | `Services/Infrastructure/HubNotifier.cs` | Fire-and-forget отправка в SignalR |
| `HttpUrlBuilder` | `Services/Infrastructure/HttpUrlBuilder.cs` | Абсолютные URL из относительных через HttpContext |

### SignalR Hub (`Hubs/ChatHub.cs`)

Авторизованный хаб, `IServiceScopeFactory` для scoped-сервисов.

- `OnConnectedAsync`: регистрация в `OnlineUserService`, join всех групп чатов, broadcast `UserOnline`.
- `OnDisconnectedAsync`: если нет других соединений — обновление `LastOnline`, broadcast `UserOffline`.
- RPC: `JoinChat`, `LeaveChat`, `MarkAsRead`, `MarkMessageAsRead`, `GetReadInfo`, `GetUnreadCounts`, `SendTyping`, `GetOnlineUsersInChat`.

### Middleware

| Middleware | Файл | Назначение |
|-----------|------|-----------|
| `ExceptionHandlingMiddleware` | `Middleware/ExceptionHandlingMiddleware.cs` | Глобальный обработчик → ApiResponse с 500 |
| `MissingFileCleanupMiddleware` | `Middleware/MissingFileCleanupMiddleware.cs` | При 404 на статический файл удаляет битые ссылки из БД |

### База данных
- PostgreSQL, EF Core 9 с миграциями.
- Postgres enum-ы с `EnumNameTranslator`.
- Модели: `Model/` — `Chat`, `ChatMember`, `Message`, `MessageFile`, `VoiceMessage`, `Poll`, `PollOption`, `PollVote`, `User`, `UserSetting`, `RefreshToken`, `Department`, `SystemSetting`.

### Безопасность
- BCrypt.Net-Next (пароли), JWT (access), SHA-256 + FamilyId (refresh).
- Rate Limiting: Sliding Window (Global + `login`, `messaging`, `search`).

---

## Desktop Client Architecture (MessengerDesktop)

### DI-регистрация (все Singleton, кроме помеченных)

**Core Services** (`Infrastructure/ServiceCollectionExtensions.cs`):
`LocalDatabase`, `ILocalCacheService`, `ICacheMaintenanceService`, `IPlatformService`, `ISettingsService`, `IGlobalHubConnection`, `IChatNotificationApiService`, `IChatInfoPanelStateStore`, `IAudioPlayerService`, `HttpClient`, `IAuthService`, `ISessionStore`, `ISecureStorageService`, `IAuthManager`, `IApiClientService`, `INavigationService`, `IDialogService`, `INotificationService`, `IFileDownloadService`, `IAudioRecorderService`.

**Factories** (Singleton): `IChatViewModelFactory`, `IChatsViewModelFactory`.

**ViewModels**: `MainWindowViewModel` (Singleton), остальные — **Transient** (`LoginViewModel`, `MainMenuViewModel`, `AdminViewModel`, `ProfileViewModel`, `SettingsViewModel`, `DepartmentManagementViewModel`).

### Архитектура ChatViewModel — Handler Composition

`ChatViewModel` (`ViewModels/Chat/ChatViewModel.cs`) — композитный ViewModel:

- **`ChatContext`** (`ChatViewModel/ChatContext.cs`) — разделяемое состояние (ChatId, CurrentUserId, Chat, Members), зависимости (Api, Hub, Dialogs, Notifications), события координации (`CompositionModeReset`, `ScrollToMessageRequested`), `LifetimeToken`.
- **`ChatFeatureHandler`** (`ChatViewModel/ChatFeatureHandler.cs`) — базовый класс: `Ctx`, `IsAlive`, `Dispose`. Наследует `ObservableObject`.

**Managers** (данные):

| Manager | Файл | Ответственность |
|---------|------|-----------------|
| `ChatMessageManager` | `Managers/ChatMessageManager.cs` | Загрузка, пагинация (before/after/around), cache-first, gap-fill после reconnect, группировка, разделители дат, trim старых |
| `ChatAttachmentManager` | `Managers/ChatAttachmentManager.cs` | Выбор файлов (IStorageProvider), thumbnail, upload |
| `ChatMemberLoader` | `Managers/ChatMemberLoader.cs` | Загрузка участников через API |

**Handlers**:

| Handler | Файл | Ответственность |
|---------|------|-----------------|
| `ChatEditDeleteHandler` | `Handlers/ChatEditDeleteHandler.cs` | Редактирование, удаление, копирование |
| `ChatReplyHandler` | `Handlers/ChatReplyHandler.cs` | Ответ на сообщение, прокрутка к оригиналу |
| `ChatForwardHandler` | `Handlers/ChatForwardHandler.cs` | Пересылка через ChatPickerDialog |
| `ChatTypingHandler` | `Handlers/ChatTypingHandler.cs` | Индикатор набора (отправка/отображение) |
| `ChatVoiceHandler` | `Handlers/ChatVoiceHandler.cs` | Запись/отправка голосовых (auto-stop, min duration) |
| `ChatInfoPanelHandler` | `Handlers/ChatInfoPanelHandler.cs` | Инфопанель, статусы, участники, контактный профиль |
| `ChatSearchHandler` | `Handlers/ChatSearchHandler.cs` | Навигация к сообщению, подсветка |
| `ChatNotificationHandler` | `Handlers/ChatNotificationHandler.cs` | Mute/unmute уведомлений |

**`ChatHubSubscriber`** (`ChatViewModel/ChatHubSubscriber.cs`) — подписывается на `GlobalHubConnection`, фильтрует по ChatId, делегирует в `ChatMessageManager`.

**Property Forwarding**: `ChatViewModel` проксирует свойства handler'ов через `ForwardProperties()`. View биндится только к `ChatViewModel`.

**Фабрики**: `ChatViewModelFactory` (`Factories/ChatViewModelFactory.cs`), `ChatsViewModelFactory` (`Factories/ChatsViewModelFactory.cs`).

### Сервисы — карта

| Сервис | Файл | Ответственность |
|--------|------|-----------------|
| `ApiClientService` | `Services/Api/ApiClientService.cs` | HTTP-клиент, retry при 401 (refresh), temp file для >10MB |
| `GlobalHubConnection` | `Services/Realtime/GlobalHubConnection.cs` | Единственный SignalR-клиент. Подписка на события, in-memory непрочитанные, дебаунс read/typing, кэширование входящих, reconnect с refresh, desktop-уведомления, `SetCurrentChat` |
| `AuthManager` | `Services/Auth/AuthManager.cs` | SecureStorage → refresh если истёк → logout. Refresh Lock |
| `AuthService` | `Services/Auth/AuthService.cs` | REST-вызовы login/refresh/revoke |
| `SessionStore` | `Services/Auth/SessionStore.cs` | In-memory хранение UserId + Token |
| `SecureStorageService` | `Services/Auth/SecureStorage.cs` | Персистентное хранение токенов (файл) |
| `LocalCacheService` | `Data/Repositories/LocalCacheService.cs` | Фасад над SQLite. Работает с DTO. Upsert/Get/Search сообщений/чатов/пользователей |
| `LocalDatabase` | `Data/LocalDatabase.cs` | SQLite (WAL, FTS5). Версионирование схемы (v3), индексы, триггеры |
| `FileDownloadService` | `Services/IFileDownloadService.cs` | Скачивание в Downloads, прогресс, кросс-платформа |
| `AudioPlayerService` | `Services/Audio/AudioPlayerService.cs` | NAudio воспроизведение, Play/Pause/Seek, потокобезопасен |
| `NAudioRecorderService` | `Services/Audio/NAudioRecorderService.cs` | NAudio запись → WAV MemoryStream |
| `NotificationService` | `Services/UI/NotificationService.cs` | Desktop overlay-уведомления |
| `DialogService` | `Services/Navigation/DialogService.cs` | Показ диалоговых окон |
| `NavigationService` | `Services/Navigation/NavigationService.cs` | Навигация между ViewModel |
| `SettingsService` | `Services/Storage/SettingsService.cs` | Key-value настройки |
| `PlatformService` | `Services/Platform/PlatformService.cs` | Доступ к MainWindow, StorageProvider |
| `ChatNotificationApiService` | `Services/ChatNotificationApiService.cs` | REST-обёртка для mute/unmute |
| `ChatInfoPanelStateStore` | `Services/ChatInfoPanelStateStore.cs` | Персистентное состояние инфопанели |
| `CacheMaintenanceService` | `Services/Cache/CacheMaintenanceService.cs` | Фоновый VACUUM, проверка размера |
| `ThemeService` | `Services/ThemeService.cs` | Управление темами |

### Локальный кэш (SQLite)

**Таблицы**: `CachedMessage`, `CachedChat`, `CachedUser`, `CachedReadPointer`, `ChatSyncState`.
**FTS5**: `messages_fts` с триггерами INSERT/UPDATE/DELETE.
**`ChatSyncState`**: `OldestLoadedId`, `NewestLoadedId`, `HasMoreOlder/Newer` — для инкрементальной синхронизации.
**`ILocalCacheService`**: фасад, маппинг DTO↔Entity через `CacheMapper`.

### Ключевые потоки

**Login / Auth**: Запуск → AuthManager.InitializeAsync → SecureStorage.GetTokens 
→ токены есть + access истёк → POST /auth/refresh → сохранить 
→ refresh невалиден → очистить всё → LoginView 
→ токенов нет → LoginView. 
При 401 в runtime → ApiClientService вызывает TryRefreshTokenAsync → повтор. 
Refresh Lock (_refreshLock + _activeRefreshTask) предотвращает параллельные рефреши.

**Send Message**: ChatViewModel.SendAsync → валидация → ApiClient.POST /messages 
→ 401 → TryRefresh → повтор → успех → ChatMessageManager.AddLocal 
→ SignalR broadcast → другие клиенты получают

**Cache-first загрузка сообщений**: `ChatMessageManager.LoadInitialCoreAsync` → SQLite → если есть, рендерим, фоново ревалидируем с сервера. Если нет — сервер → рендер → сохранение в SQLite.

**Gap-fill после reconnect**: `ChatMessageManager.GapFillAfterReconnectAsync` → батчами загружает новые сообщения after newest. При превышении лимита — полный сброс к последним.

**Read Receipts**: `GlobalHubConnection` хранит `_unreadCounts` (Lock), дебаунсит `MarkMessageAsRead`. `ChatViewModel.OnMessageVisibleAsync` маркирует при скролле. `FirstUnreadMessageId` определяет начальную позицию.

**Уведомления**: `GlobalHubConnection.OnNotificationReceived` → если не текущий чат и включены → `NotificationService.Show()` с callback → `MainMenuViewModel.OpenNotificationAsync`.

---

## API Routes (Desktop → Backend)

Определены в `Infrastructure/Configuration/ApiEndPoints.cs`:

| Группа | Prefix | Ключевые маршруты |
|--------|--------|-------------------|
| `Auth` | `api/auth` | `login`, `refresh`, `revoke` |
| `Users` | `api/users` | `{id}`, `{id}/avatar`, `{id}/username`, `{id}/password`, `online`, `status/batch` |
| `Chats` | `api/chats` | `{id}`, `{chatId}/members`, `{chatId}/members/{userId}/role`, `{chatId}/leave`, `{chatId}/avatar`, `user/{userId}`, `user/{userId}/dialogs`, `user/{userId}/groups`, `user/{userId}/contact/{contactId}` |
| `Messages` | `api/messages` | `{id}`, `chat/{chatId}`, `chat/{chatId}/around/{msgId}`, `chat/{chatId}/before/{id}`, `chat/{chatId}/after/{id}`, `chat/{chatId}/search`, `user/{userId}/search` |
| `Files` | `api/files` | `upload?chatId={chatId}` |
| `Polls` | `api/polls` | create, `vote`, `{pollId}` |
| `Departments` | `api/departments` | `{id}`, `{id}/members`, `{id}/can-manage` |
| `Notifications` | `api/notifications` | `settings`, `chat/mute`, `chat/{chatId}/settings` |
| `ReadReceipts` | `api/readreceipts` | `mark-read`, `unread-counts`, `chat/{chatId}/unread-count` |
| `Admin` | `api/admin` | `users`, `users/{id}`, `users/{id}/toggle-ban` |

---

## Shared DTO (MessengerShared)

| Группа | Ключевые DTO | Назначение |
|--------|-------------|-----------|
| `Auth/` | `LoginRequest`, `RefreshTokenRequest`, `AuthResponseDto`, `TokenResponseDto` | Контракты авторизации |
| `Chat/` | `ChatDto`, `ChatMemberDto`, `UpdateChatDto`, `UpdateChatMemberDto`, `ChatNotificationSettingsDto` | Чаты и участники |
| `Message/` | `MessageDto`, `CreateMessageRequest`, `UpdateMessageDto`, `MessageFileDto`, `MessageForwardInfoDto`, `MessageReplyPreviewDto`, `PagedMessagesDto` | Сообщения. `CreateMessageRequest` — входной контракт (отдельный от `MessageDto`) |
| `Poll/` | `CreatePollDto`, `PollDto`, `PollOptionDto`, `PollVoteDto` | Опросы |
| `ReadReceipt/` | `MarkAsReadDto`, `ReadReceiptResponseDto`, `AllUnreadCountsDto`, `UnreadCountDto`, `ChatReadInfoDto` | Прочитанные |
| `Search/` | `GlobalSearchDTO`, `GlobalSearchResponseDTO`, `SearchMessagesDTO` | Поиск |
| `User/` | `UserDto`, `CreateUserDto`, `ChangePasswordDto`, `ChangeUsernameDto`, `AvatarResponseDto` | Пользователи |
| `Notification/` | `NotificationDto` | Push-уведомления |
| `Online/` | `OnlineStatusDto` | Статус онлайн |
| `Enum/` | `ChatRole`, `ChatType`, `SystemEventTypes`, `Theme`, `UserRoles` | Перечисления |
| `Response/` | `ApiResponse<T>`, `ApiResponseHelper` | Обёртка ответов |

---

## Технический долг и ограничения

| Область | Текущее состояние | Риск / TODO |
|---------|-------------------|-------------|
| **Оффлайн** | Чтение из кэша возможно, отправка — нет | Нет очереди оффлайн-запросов |
| **Поиск** | FTS5 локально + ILike на сервере. По файлам — только метаданные | Полный поиск по содержимому файлов не поддерживается |
| **Медиа** | Сервер конвертирует в WebP | Нет локального кеширования изображений |
| **Кэш** | SQLite без автоочистки старых данных | LRU policy не реализован |
| **Конкурентность** | Обновление через SignalR, нет Optimistic Locking | Последний записавший побеждает |
| **Gap Fill** | Лимит батчей, затем полный сброс | При длительном оффлайне часть истории теряется из view |
| **Аудио** | NAudio — Windows + Linux | macOS не тестировался |