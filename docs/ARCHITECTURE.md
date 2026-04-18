# Architecture

## 1. Состав решения

| Проект | Назначение |
|--------|-----------|
| `MessengerAPI` | REST API + SignalR, бизнес-логика, PostgreSQL |
| `MessengerDesktop` | Desktop-клиент (Avalonia, MVVM, SQLite) |
| `MessengerShared` | DTO, enum-ы, `ApiResponse<T>` |

---

## 2. Взаимодействие

- Desktop → API: HTTPS/REST, JSON `ApiResponse<T>`, JWT Bearer
- Desktop ↔ API: WebSocket/SignalR `/chatHub`, JWT
- API ↔ PostgreSQL: EF Core
- Desktop ↔ локальный кэш: SQLite (`LocalDatabase`)

---

## 3. Backend (MessengerAPI)

### Слои
```
Controllers → BaseController.ExecuteAsync → Result<T> → ApiResponse<T> + HTTP status
Services    → бизнес-логика, границы транзакций
Infrastructure → Cache, AccessControl, HubNotifier, HttpUrlBuilder
Model/DbContext → EF Core + PostgreSQL
```

### DI Lifetimes

| Lifetime | Сервисы |
|----------|---------|
| `Singleton` | `TimeProvider`, `AppDateTime`, `OnlineUserService` |
| `Scoped` | Все бизнес- и инфраструктурные сервисы |

> HostedService (`BackgroundService`) в проекте отсутствует.

### Карта сервисов

| Сервис | Ответственность |
|--------|-----------------|
| `ChatService` | CRUD чатов, аватары, инвалидация кэша |
| `ChatMemberService` | Участники: добавление/удаление/роли, права, системные сообщения |
| `SystemMessageService` | Системные сообщения (Added/Removed/Left/RoleChanged/ChatCreated). Не для Contact |
| `NotificationService` | Push через SignalR. Mute per-chat. Mention-уведомления (`@username`) |
| `MessageService` | CRUD сообщений, атомарное создание (Message+Voice+Files), закрепление/открепление, пагинация, поиск |
| `FileService` | Загрузка + конвертация изображений в WebP |
| `PollService` | Опросы, голосование |
| `ReadReceiptService` | `LastReadMessageId` в `ChatMember`, счётчики, `FirstUnreadMessageId` |
| `AuthService` | Логин, рефреш, отзыв токенов |
| `TokenService` | JWT генерация, Refresh Token (SHA-256 + FamilyId) |
| `UserService` | Профиль, аватар, смена пароля/username |
| `AdminService` | CRUD пользователей, бан/разбан, сброс пароля |
| `DepartmentService` | Иерархия отделов |
| `CacheService` | `IMemoryCache`, TTL 5/10 мин, явная инвалидация |
| `AccessControlService` | Проверка прав (Member/Admin/Owner), кэш на запрос (Scoped) |
| `OnlineUserService` | `ConcurrentDictionary` онлайн-статусов, несколько соединений на юзера |
| `HubNotifier` | Fire-and-forget отправка в SignalR, ошибки логируются |
| `HttpUrlBuilder` | Абсолютные URL через `HttpContext` |

### SignalR Hub (`/chatHub`)
- `[Authorize]`, endpoint `/chatHub`
- Auth: JWT через `AccessTokenProvider` (query string)
- Группы: `chat_{chatId}` (все участники), `user_{userId}` (персональная)
- `OnConnectedAsync`: регистрация онлайна, join групп чатов, broadcast `UserOnline`
- `OnDisconnectedAsync`: обновление `LastOnline`, broadcast `UserOffline`
- **RPC методы**: `JoinChat`, `LeaveChat`, `MarkAsRead`, `MarkMessageAsRead`,
  `GetReadInfo`, `GetUnreadCounts`, `SendTyping`, `GetOnlineUsersInChat`

### Middleware

| Middleware | Назначение |
|-----------|-----------|
| `ExceptionHandlingMiddleware` | Глобальный обработчик → `ApiResponse` + 500 |
| `MissingFileCleanupMiddleware` | 404 на файл → удаление битой ссылки из БД |

### БД
- PostgreSQL, EF Core, миграции, Postgres enum-ы с `EnumNameTranslator`
- Модели: `Chat`, `ChatMember`, `Message`, `MessageFile`, `VoiceMessage`,
  `Poll`, `PollOption`, `PollVote`, `User`, `UserSetting`, `RefreshToken`,
  `Department`, `SystemSetting`

---

## 4. Desktop (MessengerDesktop)

### Ключевые сервисы

| Сервис | Lifetime | Ответственность |
|--------|----------|-----------------|
| `ApiClientService` | Singleton | HTTP, retry при 401 (auto-refresh), temp file >10MB |
| `FileDownloadService` | Singleton | Скачивание файлов через `HttpClient` |
| `GlobalHubConnection` | Singleton | Единый SignalR-клиент, события, `_unreadCounts`, дебаунс, reconnect |
| `AuthManager` | Singleton | Init → SecureStorage → refresh → logout. `_refreshLock` |
| `AuthService` | Singleton | Вызовы auth-эндпоинтов API |
| `SessionStore` | Singleton | Хранение текущей сессии в памяти |
| `SecureStorageService` | Singleton | Персистентное хранение токенов (OS keychain / файл) |
| `LocalCacheService` | Singleton | Фасад над SQLite: upsert/get/search сообщений, чатов, юзеров |
| `LocalDatabase` | Singleton | SQLite WAL + FTS5, схема v3, путь: `%LocalAppData%/MessengerDesktop/messenger_cache.db` |
| `MessageCacheRepository` | Singleton | CRUD сообщений в SQLite |
| `ChatCacheRepository` | Singleton | CRUD чатов в SQLite |
| `CacheMaintenanceService` | Singleton | Фоновый VACUUM SQLite |
| `ChatNotificationApiService` | Singleton | Mute/unmute через API |
| `ChatInfoPanelStateStore` | Singleton | Разделяемое состояние инфопанели чата |
| `AudioRecorderService` | Singleton | Запись аудио с микрофона |
| `AudioPlayerService` | Singleton | Воспроизведение аудио |
| `PlatformService` | Singleton | Платформо-специфичные операции (открытие файлов, папок) |
| `SettingsService` | Singleton | Чтение/запись настроек приложения |
| `NavigationService` | Singleton | Переключение основных вкладок |
| `DialogService` | Singleton | Стек модальных окон, SemaphoreSlim + Channel |
| `NotificationService` | Singleton | In-app уведомления (overlay) |

> ⚠️ `AudioRecorderService` регистрируется дважды в `ServiceCollectionExtensions` — баг,
> фактически используется последняя регистрация.

### ViewModel Lifetimes

| Lifetime | ViewModels |
|----------|-----------|
| `Singleton` | `MainWindowViewModel`, `ChatViewModelFactory`, `ChatsViewModelFactory` |
| `Transient` | `LoginViewModel`, `MainMenuViewModel`, `AdminViewModel`, `ProfileViewModel`, `DepartmentManagementViewModel`, `SettingsViewModel`, `UsersTabViewModel`, `DepartmentsTabViewModel` |

> Остальные ViewModel (`ChatViewModel`, `ChatsViewModel`, диалоги и т.д.)
> создаются через фабрики или `new` — не регистрируются в DI напрямую.

### HttpClient (настройки)
```csharp
new HttpClientHandler
{
    CheckCertificateRevocationList = false,
    UseProxy = false,
#if DEBUG
    // В Debug — принимать любой сертификат (для localhost dev)
    ServerCertificateCustomValidationCallback =
        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
#endif
}

new HttpClient(handler)
{
    BaseAddress = new Uri(apiBaseUrl),
    Timeout = TimeSpan.FromSeconds(30)
}
```

### ChatViewModel — Handler Composition

**`ChatContext`** — разделяемое состояние: `ChatDto`, список `Members`,
`IApiClient`, `IGlobalHubConnection`, `LifetimeToken`,
события (`CompositionModeReset`, `ScrollToMessageRequested`)

**Managers** (данные):
- `ChatMessageManager` — пагинация (before/after/around), cache-first, gap-fill, группировка, trim
- `ChatAttachmentManager` — выбор файлов, thumbnail, upload
- `ChatMemberLoader` — загрузка участников

**Handlers** (поведение):
- `ChatEditDeleteHandler` — редактирование, удаление, копирование, закрепление/открепление
- `ChatReplyHandler` — ответ, прокрутка к оригиналу
- `ChatForwardHandler` — пересылка через `ChatPickerDialog`
- `ChatTypingHandler` — индикатор набора
- `ChatVoiceHandler` — запись/отправка голосовых
- `ChatInfoPanelHandler` — инфопанель, статусы, участники
- `ChatSearchHandler` — навигация к сообщению, подсветка
- `ChatNotificationHandler` — mute/unmute

**`ChatHubSubscriber`** — подписка на `GlobalHubConnection`,
фильтрация по `ChatId`, делегирование в `ChatMessageManager`

> `ChatViewModel` проксирует свойства handler'ов через `ForwardProperties()`.
> View биндится только к `ChatViewModel`.

### Локальный кэш (SQLite)

- Путь: `%LocalAppData%/MessengerDesktop/messenger_cache.db`
- Таблицы: `messages`, `chats`, `users`, `chat_sync_state`, `messages_fts`
- FTS5 `messages_fts` с триггерами INSERT/UPDATE/DELETE
- `ChatSyncState`: `OldestLoadedId`, `NewestLoadedId`, `HasMoreOlder`, `HasMoreNewer`

**PRAGMA настройки:**
```sql
PRAGMA journal_mode=WAL;
PRAGMA synchronous=NORMAL;
PRAGMA cache_size=-4000;    -- ~4MB RAM
PRAGMA mmap_size=33554432;  -- 32MB Memory-mapped I/O
```

---

## 5. Ключевые потоки

### Auth / Login
```
Запуск → AuthManager.InitializeAsync → SecureStorage
  → access истёк → POST /auth/refresh → сохранить
  → refresh невалиден → LoginView
  → нет токенов → LoginView

Runtime: 401 → ApiClientService.TryRefreshTokenAsync → повтор
  _refreshLock предотвращает параллельные рефреши
```

### Отправка сообщения
```
ChatViewModel.SendAsync → валидация
  → POST /api/messages
  → успех → ChatMessageManager.AddLocal
  → SignalR broadcast → другие клиенты
```

### Cache-first загрузка
```
LoadInitialCoreAsync
  → SQLite есть? → рендер + фоновая ревалидация
  → SQLite пуст → сервер → рендер → сохранение в SQLite
```

### Gap-fill после reconnect
```
GapFillAfterReconnectAsync
  → батчами after/{newestId}
  → превышен лимит → полный сброс к последним сообщениям
```

### Read Receipts
```
OnMessageVisibleAsync при скролле
  → дебаунс → MarkMessageAsRead
  → GlobalHubConnection._unreadCounts (lock) → счётчики
  → FirstUnreadMessageId → начальная позиция скролла
```

### Уведомления
```
OnNotificationReceived
  → не текущий чат + не muted
  → NotificationService.Show()
  → callback → MainMenuViewModel.OpenNotificationAsync
```

---

## 6. Технический долг

| Область | Ограничение |
|---------|-------------|
| Оффлайн | Чтение из кэша есть, очереди отправки нет |
| Поиск | FTS5 + ILike. По содержимому файлов — не поддерживается |
| Медиа | Нет локального кэша изображений (только LruCache в памяти AsyncImageLoader) |
| Кэш | LRU не реализован, автоочистки старых данных нет |
| Конкурентность | Нет Optimistic Locking — last-write-wins |
| Gap Fill | При долгом оффлайне часть истории теряется из view |
| Аудио | macOS не тестировался |
| DI | `AudioRecorderService` зарегистрирован дважды (дублирующая регистрация) |

---

## 7. Структура папок

### MessengerAPI
```
MessengerAPI/
├── Common/          # Result<T>, AppDateTime, ValidationHelper, UrlHelpers
├── Configuration/   # DI регистрация, JWT, RateLimit, Swagger, StaticFiles
├── Controllers/     # BaseController + все контроллеры
├── Hubs/            # ChatHub.cs
├── Mapping/         # UserMappings, ChatMappings, MessageMappings, FileMappings, PollMappings
├── Middleware/      # ExceptionHandlingMiddleware, MissingFileCleanupMiddleware
├── Model/           # EF-модели + MessengerDbContext + Partial.cs
├── Services/
│   ├── Auth/        # AuthService, TokenService
│   ├── Base/        # BaseService
│   ├── Chat/        # ChatService, ChatMemberService, NotificationService, SystemMessageService
│   ├── Department/  # DepartmentService
│   ├── Infrastructure/  # CacheService, AccessControlService, HubNotifier, HttpUrlBuilder, OnlineUserService
│   │   └── Postgres/    # EnumNameTranslator, EnumTypeMappings
│   ├── Messaging/   # MessageService, FileService, PollService
│   ├── ReadReceipt/ # ReadReceiptService
│   └── User/        # UserService, AdminService
└── Program.cs
```

### MessengerDesktop
```
MessengerDesktop/
├── Converters/      # Value converters для Avalonia bindings
│   ├── Base/        # ConverterBase
│   ├── Boolean/     # BoolToBrush, BooleanAnd/Or, EnumEquals, BoolToThickness
│   ├── Comparison/  # ComparisonConverter
│   ├── DateTime/    # DateTimeFormat, LastMessageDate, LastSeenText
│   ├── Domain/      # Initials, ThemeToDisplay, SearchScope, ContentFilterToLabel
│   ├── Enum/        # UserRoleToVisibility
│   └── Generic/     # Pluralize, PercentToWidth, IndexToText, Multiply, ResourceKeyToGeometry
├── Data/            # SQLite локальный кэш
│   ├── Entities/    # CachedMessage, CachedChat, CachedUser, CachedReadPointer, ChatSyncState
│   ├── Mappers/     # CacheMapper (DTO ↔ Entity)
│   ├── Repositories/# ILocalCacheService, LocalCacheService, Chat/MessageCacheRepository
│   └── LocalDatabase.cs
├── Helpers/         # ChatPreviewFormatter
├── Infrastructure/
│   ├── Configuration/   # ApiEndpoints.cs, AppConstants.cs
│   ├── AuthenticatedImageLoader.cs
│   ├── AvatarHelper.cs
│   └── ServiceCollectionExtensions.cs
├── Services/
│   ├── Api/         # ApiClientService
│   ├── Audio/       # AudioPlayerService, AudioRecorderService, WavData
│   ├── Auth/        # AuthManager, AuthService, SecureStorage, SessionStore
│   ├── Cache/       # CacheMaintenanceService
│   ├── Navigation/  # NavigationService, DialogService
│   ├── Platform/    # PlatformService
│   ├── Realtime/    # GlobalHubConnection
│   ├── Storage/     # SettingsService
│   └── UI/          # NotificationService
├── ViewModels/
│   ├── Admin/       # AdminViewModel, UsersTabViewModel, DepartmentsTabViewModel
│   ├── Auth/        # LoginViewModel
│   ├── Chat/        # ChatViewModel + все компоненты
│   │   ├── Handlers/    # ChatEditDeleteHandler, ChatReplyHandler, ChatForwardHandler,
│   │   │                # ChatTypingHandler, ChatVoiceHandler, ChatInfoPanelHandler,
│   │   │                # ChatSearchHandler, ChatNotificationHandler
│   │   ├── Managers/    # ChatMessageManager, ChatAttachmentManager, ChatMemberLoader
│   │   ├── Messages/    # MessageViewModel, MessageFileViewModel, MessageGroupPosition
│   │   ├── Polls/       # PollViewModel, PollOptionViewModel
│   │   └── Voice/       # VoiceRecordingViewModel
│   ├── ChatList/    # ChatsViewModel, ChatListItemViewModel, GlobalSearchManager
│   ├── Department/  # DepartmentManagementViewModel, DepartmentMemberViewModel
│   ├── Dialog/      # все DialogViewModel + UserListItemViewModel
│   ├── Factories/   # ChatViewModelFactory, ChatsViewModelFactory
│   ├── Shell/       # MainWindowViewModel, MainMenuViewModel
│   ├── BaseViewModel.cs
│   ├── ProfileViewModel.cs
│   └── SettingsViewModel.cs
├── Views/           # Зеркалит структуру ViewModels (*.axaml + *.axaml.cs)
│   ├── Chat/
│   │   └── MessageParts/  # Text, Voice, Poll, File, System, Reply, Forward, DateSeparator
│   ├── Controls/
│   │   ├── Admin/         # DepartmentCardView, DepartmentGroupView, UserCardView
│   │   ├── Chat/          # ChatItemView
│   │   ├── Shared/        # AvatarControl, RichMessageTextBlock, CircularProgress,
│   │   │                  # PasswordStrengthControl, ThemeSelectorControl
│   │   ├── Skeleton/      # ChatInfoSkeleton, ChatListSkeleton, ChatMessagesSkeleton
│   │   ├── FilterAutocomplete
│   │   └── SearchBox
│   └── Shell/         # MainWindow, MainMenuView
├── App.axaml / App.axaml.cs
└── ViewLocator.cs   # Авто-маппинг ViewModel → View

### MessengerShared
```
MessengerShared/
├── DTO/
│   ├── Auth/        # LoginRequest, AuthResponseDto, TokenResponseDto, RefreshTokenRequest
│   ├── Chat/        # ChatDto, ChatMemberDto, UpdateChatDto, ChatNotificationSettingsDto, UpdateChatMemberDto
│   ├── Department/  # DepartmentDto, UpdateDepartmentMemberDto
│   ├── Message/     # MessageDto, CreateMessageRequest, UpdateMessageDto, PagedMessagesDto,
│   │                # MessageFileDto, MessageReplyPreviewDto, MessageForwardInfoDto
│   ├── Notification/# NotificationDto
│   ├── Online/      # OnlineStatusDto
│   ├── Poll/        # CreatePollDto, PollDto, PollOptionDto, PollVoteDto
│   ├── ReadReceipt/ # MarkAsReadDto, ReadReceiptResponseDto, AllUnreadCountsDto, ChatReadInfoDto
│   ├── Search/      # GlobalSearchDto, GlobalSearchResponseDto, SearchMessagesDto
│   └── User/        # UserDto, CreateUserDto, ChangePasswordDto, ChangeUsernameDto,
│                    # AvatarResponseDto, ResetPasswordAdminDto
├── Enum/            # ChatRole, ChatType, Theme, SystemEventTypes, UserRoles
└── Response/        # ApiResponse<T>, ApiResponseHelper
```

---

## 8. Debug / Release конфигурация

| | Debug | Release |
|-|-------|---------|
| API URL | `https://localhost:7190/` | `https://localhost:5274/` |
| Avalonia Diagnostics | ✅ | ❌ |
| SSL validation | Relaxed (`DangerousAcceptAnyServerCertificateValidator`) | Strict |
| EF SensitiveDataLogging | ✅ | ❌ |
| JSON WriteIndented | ✅ | ❌ |
```