# Architecture

## 1. Состав решения
| Проект | Назначение |
|--------|-----------|
| `MessengerAPI` | REST API + SignalR, бизнес-логика, PostgreSQL |
| `MessengerDesktop` | Desktop-клиент (Avalonia, MVVM, SQLite) |
| `MessengerShared` | DTO, enum-ы, `ApiResponse<T>` |

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
Infrastructure → Cache, AccessControl, HubNotifier, UrlBuilder
Model/DbContext → EF Core + PostgreSQL
```

### DI Lifetimes
- **Singleton**: `OnlineUserService`, `AppDateTime`, `TimeProvider`
- **Scoped**: все сервисы (бизнес + инфраструктурные)

### Карта сервисов

| Сервис | Ответственность |
|--------|-----------------|
| `ChatService` | CRUD чатов, аватары, инвалидация кэша |
| `ChatMemberService` | Участники: добавление/удаление/роли. Права + системные сообщения |
| `SystemMessageService` | Системные сообщения (Added/Removed/Left/RoleChanged). Не для Contact-чатов |
| `NotificationService` | Push через SignalR. Mute per-chat. Mention-уведомления (`@username`) |
| `MessageService` | CRUD сообщений, атомарное создание (Message+Voice+Files). Пагинация, ILike-поиск, mentions |
| `FileService` | Загрузка + конвертация в WebP |
| `PollService` | Опросы, голосование |
| `ReadReceiptService` | `LastReadMessageId` в `ChatMember`, счётчики, `FirstUnreadMessageId` |
| `AuthService` | Логин, рефреш, отзыв токенов |
| `TokenService` | JWT генерация, Refresh Token (SHA-256 + FamilyId) |
| `UserService` | Профиль, аватар, смена пароля/username |
| `AdminService` | CRUD пользователей, бан |
| `DepartmentService` | Иерархия отделов |
| `CacheService` | IMemoryCache, TTL 5/10 мин, явная инвалидация |
| `AccessControlService` | Проверка прав (Member/Admin/Owner), кэш на запрос |
| `OnlineUserService` | ConcurrentDictionary онлайн-статусов, несколько соединений на юзера |
| `HubNotifier` | Fire-and-forget отправка в SignalR |
| `HttpUrlBuilder` | Абсолютные URL через HttpContext |

### SignalR Hub (`/chatHub`)
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

| Сервис | Ответственность |
|--------|-----------------|
| `ApiClientService` | HTTP, retry при 401 (auto-refresh), temp file >10MB |
| `GlobalHubConnection` | Единый SignalR-клиент. События, `_unreadCounts`, дебаунс read/typing, reconnect, desktop-уведомления, `SetCurrentChat` |
| `AuthManager` | Init → SecureStorage → refresh если истёк → logout. Refresh Lock |
| `LocalCacheService` | Фасад над SQLite: upsert/get/search сообщений, чатов, пользователей |
| `LocalDatabase` | SQLite WAL + FTS5, схема v3, индексы, триггеры |

### ChatViewModel — Handler Composition

**`ChatContext`** — разделяемое состояние: ChatId, CurrentUserId, зависимости, события (`CompositionModeReset`, `ScrollToMessageRequested`), `LifetimeToken`

**Managers** (данные):
- `ChatMessageManager` — пагинация (before/after/around), cache-first, gap-fill, группировка, trim
- `ChatAttachmentManager` — выбор файлов, thumbnail, upload
- `ChatMemberLoader` — загрузка участников

**Handlers** (поведение):
- `ChatEditDeleteHandler` — редактирование, удаление, копирование
- `ChatReplyHandler` — ответ, прокрутка к оригиналу
- `ChatForwardHandler` — пересылка через ChatPickerDialog
- `ChatTypingHandler` — индикатор набора
- `ChatVoiceHandler` — запись/отправка голосовых
- `ChatInfoPanelHandler` — инфопанель, статусы, участники
- `ChatSearchHandler` — навигация к сообщению, подсветка
- `ChatNotificationHandler` — mute/unmute

**`ChatHubSubscriber`** — подписка на `GlobalHubConnection`, фильтрация по ChatId, делегирование в `ChatMessageManager`

> `ChatViewModel` проксирует свойства handler'ов через `ForwardProperties()`. View биндится только к `ChatViewModel`.

### Локальный кэш (SQLite)
- Таблицы: `CachedMessage`, `CachedChat`, `CachedUser`, `CachedReadPointer`, `ChatSyncState`
- FTS5 `messages_fts` с триггерами INSERT/UPDATE/DELETE
- `ChatSyncState`: `OldestLoadedId`, `NewestLoadedId`, `HasMoreOlder/Newer`

---

## 5. Ключевые потоки

**Auth / Login**:
Запуск → `AuthManager.InitializeAsync` → SecureStorage
→ access истёк → `POST /auth/refresh` → сохранить
→ refresh невалиден → LoginView
→ нет токенов → LoginView
В runtime: 401 → `ApiClientService.TryRefreshTokenAsync` → повтор запроса
`_refreshLock` предотвращает параллельные рефреши

**Отправка сообщения**:
`ChatViewModel.SendAsync` → валидация → `POST /api/messages`
→ успех → `ChatMessageManager.AddLocal` → SignalR broadcast → другие клиенты

**Cache-first загрузка**:
`LoadInitialCoreAsync` → SQLite есть? → рендер + фоновая ревалидация
→ SQLite пуст → сервер → рендер → сохранение в SQLite

**Gap-fill после reconnect**:
`GapFillAfterReconnectAsync` → батчами `after newest`
→ превышен лимит → полный сброс к последним сообщениям

**Read Receipts**:
`OnMessageVisibleAsync` при скролле → дебаунс → `MarkMessageAsRead`
`GlobalHubConnection._unreadCounts` (Lock) → счётчики
`FirstUnreadMessageId` → начальная позиция скролла

**Уведомления**:
`OnNotificationReceived` → не текущий чат + не muted
→ `NotificationService.Show()` → callback → `MainMenuViewModel.OpenNotificationAsync`

---

## 6. Технический долг

| Область | Ограничение |
|---------|-------------|
| Оффлайн | Чтение из кэша есть, очереди отправки нет |
| Поиск | FTS5 + ILike. По содержимому файлов — не поддерживается |
| Медиа | Нет локального кеша изображений |
| Кэш | LRU не реализован, автоочистки старых данных нет |
| Конкурентность | Нет Optimistic Locking — last-write-wins |
| Gap Fill | При долгом оффлайне часть истории теряется из view |
| Аудио | macOS не тестировался |

## 7. Структура папок
### MessengerAPI
```
MessengerAPI/
├── Common/                          # Result<T>, AppDateTime, ValidationHelper, UrlHelpers
├── Configuration/                   # DI регистрация, JWT, RateLimit, Swagger, StaticFiles
├── Controllers/                     # BaseController + все контроллеры
├── Hubs/                            # ChatHub.cs
├── Mapping/                         # UserMappings, ChatMappings, MessageMappings, FileMappings, PollMappings
├── Middleware/                      # ExceptionHandlingMiddleware, MissingFileCleanupMiddleware
├── Model/                           # EF-модели + MessengerDbContext + Partial.cs
├── Services/
│   ├── Auth/                        # AuthService, TokenService
│   ├── Base/                        # BaseService
│   ├── Chat/                        # ChatService, ChatMemberService, NotificationService, SystemMessageService
│   ├── Department/                  # DepartmentService
│   ├── Infrastructure/              # CacheService, AccessControlService, HubNotifier, HttpUrlBuilder, OnlineUserService
│   │   └── Postgres/                # EnumNameTranslator, EnumTypeMappings
│   ├── Messaging/                   # MessageService, FileService, PollService
│   ├── ReadReceipt/                 # ReadReceiptService
│   └── User/                        # UserService, AdminService
└── Program.cs
```

### MessengerDesktop
```
MessengerDesktop/
├── Converters/                      # Value converters для Avalonia bindings
│   ├── Base/                        # ConverterBase
│   ├── Boolean/                     # BoolToBrush, BooleanAnd/Or, EnumEquals
│   ├── Comparison/                  # ComparisonConverter
│   ├── DateTime/                    # DateTimeFormat, LastMessageDate, LastSeenText
│   ├── Domain/                      # Initials, ThemeToDisplay
│   ├── Enum/                        # UserRoleToVisibility
│   └── Generic/                     # Pluralize, PercentToWidth, IndexToText
├── Data/                            # SQLite локальный кэш
│   ├── Entities/                    # CachedMessage, CachedChat, CachedUser, CachedReadPointer, ChatSyncState
│   ├── Mappers/                     # CacheMapper (DTO ↔ Entity)
│   ├── Repositories/                # ILocalCacheService, LocalCacheService, Chat/MessageCacheRepository
│   └── LocalDatabase.cs             # SQLite init, схема v3, FTS5, PRAGMA
├── Helpers/                         # ChatPreviewFormatter
├── Infrastructure/
│   ├── Configuration/               # ApiEndPoints.cs, AppConstants.cs
│   ├── AuthenticatedImageLoader.cs  # AsyncImageLoader с Authorization header
│   ├── AvatarHelper.cs              # URL нормализация + cachebuster
│   └── ServiceCollectionExtensions.cs
├── Services/
│   ├── Api/                         # ApiClientService (HTTP + 401 retry)
│   ├── Audio/                       # AudioPlayerService, NAudioRecorderService
│   ├── Auth/                        # AuthManager, AuthService, SecureStorage, SessionStore
│   ├── Cache/                       # CacheMaintenanceService (VACUUM)
│   ├── Navigation/                  # NavigationService, DialogService
│   ├── Platform/                    # PlatformService
│   ├── Realtime/                    # GlobalHubConnection
│   ├── Storage/                     # SettingsService
│   └── UI/                          # NotificationService
├── ViewModels/
│   ├── Admin/                       # AdminViewModel, UsersTabViewModel, DepartmentsTabViewModel
│   ├── Auth/                        # LoginViewModel
│   ├── Chat/                        # ChatViewModel + все компоненты
│   │   ├── Handlers/                # ChatEditDeleteHandler, ChatReplyHandler, ChatForwardHandler,
│   │   │                            # ChatTypingHandler, ChatVoiceHandler, ChatInfoPanelHandler,
│   │   │                            # ChatSearchHandler, ChatNotificationHandler
│   │   ├── Managers/                # ChatMessageManager, ChatAttachmentManager, ChatMemberLoader
│   │   ├── Messages/                # MessageViewModel, MessageFileViewModel
│   │   ├── Polls/                   # PollViewModel, PollOptionViewModel
│   │   └── Voice/                   # VoiceRecordingViewModel
│   ├── ChatList/                    # ChatsViewModel, ChatListItemViewModel, GlobalSearchManager
│   ├── Department/                  # DepartmentManagementViewModel
│   ├── Dialog/                      # все DialogViewModel + UserListItemViewModel
│   ├── Factories/                   # ChatViewModelFactory, ChatsViewModelFactory
│   ├── Shell/                       # MainWindowViewModel, MainMenuViewModel
│   ├── BaseViewModel.cs
│   ├── ProfileViewModel.cs
│   └── SettingsViewModel.cs
├── Views/                           # Зеркалит структуру ViewModels (*.axaml + *.axaml.cs)
│   ├── Chat/
│   │   └── MessageParts/            # Компоненты рендеринга сообщений (Text, Voice, Poll, File, System, Reply, Forward)
│   ├── Controls/
│   │   ├── Shared/                  # AvatarControl, RichMessageTextBlock, CircularProgress
│   │   └── Skeleton/                # Skeleton-экраны загрузки
│   └── Shell/                       # MainWindow, MainMenu
├── App.axaml / App.axaml.cs
└── ViewLocator.cs                   # Авто-маппинг ViewModel → View

### MessengerShared
```
MessengerShared/
├── DTO/
│   ├── Auth/                        # LoginRequest, AuthResponseDto, TokenResponseDto, RefreshTokenRequest
│   ├── Chat/                        # ChatDto, ChatMemberDto, UpdateChatDto, ChatNotificationSettingsDto
│   ├── Department/                  # DepartmentDto, UpdateDepartmentMemberDto
│   ├── Message/                     # MessageDto, CreateMessageRequest, UpdateMessageDto, PagedMessagesDto,
│   │                                # MessageFileDto, MessageReplyPreviewDto, MessageForwardInfoDto
│   ├── Notification/                # NotificationDto
│   ├── Online/                      # OnlineStatusDto
│   ├── Poll/                        # CreatePollDto, PollDto, PollOptionDto, PollVoteDto
│   ├── ReadReceipt/                 # MarkAsReadDto, ReadReceiptResponseDto, AllUnreadCountsDto, ChatReadInfoDto
│   ├── Search/                      # GlobalSearchDto, GlobalSearchResponseDto, SearchMessagesDto
│   └── User/                        # UserDto, CreateUserDto, ChangePasswordDto, ChangeUsernameDto, AvatarResponseDto
├── Enum/                            # ChatRole, ChatType, Theme, SystemEventTypes, UserRoles
└── Response/                        # ApiResponse<T>, ApiResponseHelper
```