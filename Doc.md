```markdown
# Документация проекта ВнутрьСеть

> **Стек:** C# / .NET 10, ASP.NET Core, Entity Framework Core, PostgreSQL, SignalR, Avalonia UI, SQLite
> **Архитектура:** Слоёная (Entities → Repositories → Services → Controllers/Hubs)
> **Принцип ошибок:** Railway-Oriented Programming — все ошибки через `Result<T>`, исключения не используются в бизнес-логике
> **Авторизация:** JWT Bearer + WebSocket query token `?access_token=` для SignalR
> **Паттерн ответов:** `BaseController.Map(Result<T>)` → HTTP-статус автоматически по `ResultErrorType`

---

## БЫСТРЫЙ ПОИСК

| Что ищешь | Где |
|---|---|
| JWT, токены, авторизация | §1.1, §2.1, §7.2 (JwtSettings), §8.2 |
| Сущности БД | §1 |
| DTO | §2 |
| HTTP эндпоинты (маршруты) | §3 |
| SignalR события и методы | §4 |
| Middleware и порядок | §5 |
| Маппинг сущностей в DTO | §6 |
| Конфигурация, DI, настройки | §7 |
| Бизнес-сервисы API | §8 |
| Абстракции (интерфейсы) | §9 |
| Перечисления | §10 |
| Формат ответов API | §11 |
| Desktop: конвертеры Avalonia | §12 |
| Desktop: локальная БД SQLite | §13 |
| Desktop: инфраструктура | §14 |
| Desktop: сервисы | §15 |
| Desktop: абстракции | §16 |
| Desktop: фабрики ViewModel | §17 |
| Desktop: ViewModels | §18 |
| Desktop: Views | §20 |
| Типичные потоки данных | §19 |
| Известные проблемы и баги | §21 |

---

## СТРУКТУРА ПРОЕКТА

```
/API
  /Common              — AppDateTime, Result, ValidationHelper, StatusExtensions, UrlHelpers
  /Configuration       — DI, JWT, Kestrel, RateLimit, Swagger, StaticFiles
  /Controllers         — HTTP контроллеры
  /Data                — EF сущности, DbContext, Migrations, SeedData
  /Hubs                — MessengerHub (SignalR)
  /Mapping             — extension-методы ToDto()
  /Middleware          — ExceptionHandling, MissingFileCleanup
  /Repositories        — Abstractions, Base, Implementations, Projections
  /Services
    /Abstractions      — интерфейсы сервисов
    /Base              — BaseService
    /Core/Auth         — AuthService, TokenService
    /Features          — Call, Chat, Department, Messaging, ReadReceipt, User
    /Infrastructure    — Bundles, Cache, Database, Network, Security, Status

/Desktop
  /Converters          — Boolean, DateTime, Domain, Generic
  /Data                — LocalDatabase (SQLite), Models, Repositories, Mapping
  /Infrastructure      — Configuration, Diagnostics, Helpers, Media
  /Services
    /Abstractions
    /Core              — ApiClient, Auth, Realtime (GlobalHubConnection)
    /Features          — Call, Chat, Media
    /Platform          — Cache, Navigation, Network, OS, Storage, UI
  /ViewModels          — Auth, Call, Chat, ChatList, Admin, Dialog, Shell
  /Views               — Controls, Chat, Auth, Call, Admin, Dialog, Shell

/Shared
  /Dto                 — Auth, Call, Chat, Department, Message, Notification, Online, Poll, ReadReceipt, Search, User
  /Enum
  /Helpers             — SystemEventMeta
  /Hubs                — HubMethods (константы)
  /Response            — ApiResponse, ApiResponseHelper
```

**Точки входа:**
- API: `Program.cs` — порт **5274**, HTTP1+HTTP2
- Desktop: `App.axaml.cs`
- Тесты: `API.Tests/`

---

## ПОРЯДОК MIDDLEWARE (Program.cs)

```
ExceptionHandlingMiddleware        ← первым, ловит всё
MigrateAsync + SeedAsync           ← при старте
Swagger                            ← только Development
HttpsRedirection                   ← если !DisableHttpsRedirection
X-Content-Type-Options header
StaticFiles (/uploads, /avatars)
MissingFileCleanupMiddleware       ← после StaticFiles, на 404
CORS
CookiePolicy (SameSite=Lax, HttpOnly=Always)
WebSockets
RateLimiter
Authentication
Authorization
MapHub /chatHub
MapControllers
```

---

## КОНФИГУРАЦИЯ (appsettings.json)

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=...;Database=...;Username=...;Password=..."
  },
  "JwtSettings": {
    "Secret": "<минимум 32 символа>",
    "Issuer": "API",
    "Audience": "MessengerClient",
    "AccessTokenLifetimeMinutes": 15,
    "RefreshTokenLifetimeDays": 30
  },
  "CallSettings": {
    "RelayHost": "",
    "RelayPort": 5276,
    "MaxParticipants": 30
  },
  "MessengerSettings": {
    "AdminDepartmentId": 1,
    "MaxFileSizeBytes": 314572800,
    "BcryptWorkFactor": 12,
    "MaxImageDimension": 100,
    "ImageQuality": 85,
    "DefaultPageSize": 50,
    "MaxPageSize": 100
  },
  "DisableHttpsRedirection": false
}
```

---

## RATE LIMITING

| Политика | Лимит | Окно | Партиция |
|---|---|---|---|
| Global | 100 req | 10 сек | по IP |
| `login` | 5 req | 1 мин | по IP |
| `upload` | 10 req | 1 мин | по userId или IP |
| `search` | 15 req | 1 мин | по userId или IP |
| `messaging` | 30 req | 1 мин | по userId или IP |

При превышении → 429 + `Retry-After` header + JSON `{ success, error, retryAfterSeconds, timestamp }`.

---

# 1. СУЩНОСТИ (API/Data)

## 1.1 Пользователи и авторизация

### User
**Путь:** `API/Data/User.cs` + `API/Data/Partial.cs`

| Свойство | Тип | Назначение |
|---|---|---|
| `Id` | `int` | PK |
| `Username` | `string` | Уникальный логин |
| `Surname`, `Name`, `Midname` | `string?` | ФИО |
| `Password` | `UserPassword` | Owned-сущность (только BCrypt хэш) |
| `CreatedAt` | `DateTime?` | Дата регистрации |
| `LastOnline` | `DateTime?` | Последняя активность |
| `DepartmentId` | `int?` | FK → Department |
| `Avatar` | `string?` | Относительный путь |
| `IsBanned` | `bool` | Заблокирован |
| `StatusType` | `UserStatusType` | Online/Away/Busy/DND |
| `StatusExpiresAt` | `DateTime?` | Истечение статуса |
| `DisplayName` | `string?` | Вычисляемое: "Фамилия Имя Отчество" или Username |

**Методы:** `GetDisplayName()` → `DisplayName` ?? `Username` ?? заглушка
**Навигация:** `ChatMembers`, `Chats`, `Department`, `Departments` (где Head), `SentMessages` (`ICollection<UserMessage>`), `PollVotes`, `UserSetting` (1:1), `RefreshTokens`

---

### UserPassword
**Путь:** `API/Data/UserPassword.cs`
**Поле:** `Hash: string` (BCrypt, фактор из `MessengerSettings.BcryptWorkFactor=12`)
**Методы:** `SetPassword(string)`, `Verify(string) → bool`

---

### UserSetting
**Путь:** `API/Data/UserSetting.cs`

| Свойство | Тип | Default |
|---|---|---|
| `UserId` | `int` | PK = FK → User |
| `NotificationsEnabled` | `bool` | true |
| `Theme` | `Theme?` | null |

---

### RefreshToken
**Путь:** `API/Data/RefreshToken.cs`

| Поле | Тип | Назначение |
|---|---|---|
| `Id` | `int` | PK |
| `UserId` | `int` | FK → User |
| `TokenHash` | `string` | SHA-256 хэш токена |
| `JwtId` | `string` | Связанный JWT `jti` |
| `CreatedAt`, `ExpiresAt` | `DateTime` | Период действия |
| `UsedAt` | `DateTime?` | null = не использован |
| `RevokedAt` | `DateTime?` | null = активен |
| `ReplacedByTokenId` | `int?` | Ссылка на следующий токен в цепочке |
| `FamilyId` | `string` | Группа токенов одной сессии |
| `IsActive` | `bool` | Вычисляемое: !UsedAt && !RevokedAt && ExpiresAt > now |

**Механика:** повторное использование токена (UsedAt != null) → отзыв всей семьи через `FamilyId`.

---

### TokenPair
**Путь:** `API/Data/TokenPair.cs` (не персистируется)

| Поле | Тип |
|---|---|
| `AccessToken` | `string` (JWT) |
| `RefreshToken` | `string` (открытый текст) |
| `JwtId` | `string` |

---

## 1.2 Чаты и участники

### Chat
**Путь:** `API/Data/Chat.cs` + `Partial.cs`

| Свойство | Тип | Назначение |
|---|---|---|
| `Id` | `int` | PK |
| `Name` | `string?` | null для Contact-чатов |
| `Type` | `ChatType` | Chat/Department/Contact/DepartmentHeads |
| `CreatedAt` | `DateTime` | |
| `CreatedById` | `int?` | FK → User |
| `LastMessageTime` | `DateTime?` | |
| `Avatar` | `string?` | |
| `ShowHistoryForNewMembers` | `bool` | default: true |

**Навигация:** `ChatMembers`, `Messages` (`ICollection<Message>`), `CreatedBy`, `Department` (1:1)

---

### ChatMember
**Путь:** `API/Data/ChatMember.cs`

| Свойство | Тип | Назначение |
|---|---|---|
| `ChatId` | `int` | Составной PK |
| `UserId` | `int` | Составной PK |
| `Role` | `ChatRole` | Member/Admin/Owner |
| `JoinedAt` | `DateTime` | |
| `NotificationsEnabled` | `bool` | default: true |
| `LastReadMessageId` | `int?` | Для unread-счётчика |
| `LastReadAt` | `DateTime?` | |

---

### Department
**Путь:** `API/Data/Department.cs`

| Свойство | Тип | Назначение |
|---|---|---|
| `Id` | `int` | PK |
| `Name` | `string` | |
| `ParentDepartmentId` | `int?` | Self-referencing FK |
| `ChatId` | `int?` | FK → Chat (1:1, создаётся автоматически при создании отдела) |
| `HeadId` | `int?` | FK → User (уникальный индекс) |

---

## 1.3 Сообщения и вложения

### Message (абстрактный)
**Путь:** `API/Data/Message.cs`
**Иерархия:** TPH, дискриминатор `message_type` (false = UserMessage, true = SystemMessage)

| Свойство | Тип | Назначение |
|---|---|---|
| `Id` | `int` | PK |
| `ChatId` | `int` | FK → Chat |
| `CreatedAt` | `DateTime` | |
| `IsDeleted` | `bool?` | Soft-delete |
| `PinnedAt` | `DateTime?` | Закреплено если не null |
| `PinnedByUserId` | `int?` | FK → User |

### UserMessage : Message

| Свойство | Тип | Назначение |
|---|---|---|
| `SenderId` | `int?` | FK → User |
| `Content` | `string?` | Текст (до 4000 символов) |
| `EditedAt` | `DateTime?` | |
| `ReplyToMessageId` | `int?` | Самореференс |
| `ForwardedFromMessageId` | `int?` | Самореференс |
| `VoiceMessage` | `VoiceMessage?` | 1:1 |
| `Poll` | `Poll?` | 1:1 |
| `IsVoiceMessage` | `bool` | Вычисляемое: VoiceMessage != null |
| `MessageFiles` | `ICollection<MessageFile>` | |

### SystemMessage : Message

| Свойство | Тип | Назначение |
|---|---|---|
| `InitiatorId` | `int?` | FK → User (кто инициировал). Хранится в колонке `sender_id` |
| `TargetUserId` | `int?` | FK → User (кого касается) |
| `SystemEventType` | `SystemEventType` | Тип события |
| `Content` | `string?` | Дополнительный текст |

---

### VoiceMessage
**Путь:** `API/Data/VoiceMessage.cs`
PK = FK → UserMessage (колонка `message_id`)

| Свойство | Тип |
|---|---|
| `MessageId` | `int` |
| `DurationSeconds` | `double` |
| `Waveform` | `string?` |
| `FilePath` | `string` |
| `FileSize` | `long` |

---

### MessageFile
**Путь:** `API/Data/MessageFile.cs`

| Свойство | Тип |
|---|---|
| `Id` | `int` (PK) |
| `MessageId` | `int` (FK → UserMessage) |
| `FileName` | `string` |
| `ContentType` | `string` |
| `Path` | `string?` |

---

## 1.4 Звонки (In-Memory, не персистируются)

### CallSession

| Свойство | Тип |
|---|---|
| `CallId` | `string` (GUID) |
| `ChatId` | `int` |
| `InitiatorId` | `int` |
| `StartedAt` | `DateTimeOffset` |
| `Status` | `CallStatus` |
| `Mode` | `CallMode` |
| `IsGroupCall` | `bool` |
| `PendingParticipants` | `ConcurrentDictionary<int, CallParticipant>` |
| `ActiveParticipants` | `ConcurrentDictionary<int, CallParticipant>` |
| `TimeoutCts` | `CancellationTokenSource` |

### CallParticipant

| Свойство | Тип |
|---|---|
| `UserId` | `int` |
| `ConnectionId` | `string` (SignalR) |
| `IsMuted` | `bool` |
| `IsSpeaking` | `bool` |
| `JoinedAt` | `DateTime` |

---

## 1.5 Опросы

### Poll

| Свойство | Тип |
|---|---|
| `Id` | `int` (PK) |
| `MessageId` | `int` (уникальный FK → UserMessage) |
| `IsAnonymous` | `bool?` |
| `AllowsMultipleAnswers` | `bool?` |
| `ClosesAt` | `DateTime?` |

### PollOption

| Свойство | Тип |
|---|---|
| `Id` | `int` |
| `PollId` | `int` (FK) |
| `OptionText` | `string` |
| `Position` | `int` |

### PollVote

| Свойство | Тип |
|---|---|
| `Id` | `int` |
| `PollId` | `int` (FK) |
| `OptionId` | `int` (FK) |
| `UserId` | `int` (FK) |
| `VotedAt` | `DateTime` |

**Constraint:** уникальный индекс `(PollId, UserId, OptionId)`

---

## 1.6 Прочие сущности

### SystemSetting
Key-Value: `Key: string (PK)`, `Value: string`

---

## 1.7 MessengerDbContext
**Путь:** `API/Data/MessengerDbContext.cs`

**DbSets:** `Messages`, `UserMessages`, `SystemMessages`, `Chats`, `RefreshTokens`, `ChatMembers`, `Departments`, `VoiceMessages`, `MessageFiles`, `Polls`, `PollOptions`, `PollVotes`, `SystemSettings`, `Users`, `UserSettings`

**Ключевые конфигурации:**
- PostgreSQL Enum: `theme`, `chat_role`, `chat_type`, `system_event_type`, `user_status_type`
- TPH: дискриминатор `message_type` (false/true) для UserMessage/SystemMessage
- `User.Password` → Owned Entity, колонка `password_hash`
- Все timestamp: `timestamp without time zone`
- Каскады: Chat→Messages (delete), User→RefreshTokens (delete), UserMessage→Poll (delete); SetNull для остальных FK
- Индекс `PinnedAt` с фильтром `WHERE pinned_at IS NOT NULL`
- `SystemMessage.InitiatorId` хранится в колонке `sender_id`
- `QuerySplittingBehavior.SplitQuery` глобально

---

## 1.8 Инициализация данных (Seed)

**Путь:** `API/Data/SeedData/DataSeeder.cs`
Обновляет пароли из `Data/SeedData/users.json`. Только хэширование, записей не создаёт.

**Формат `users.json`:** массив `UserSeedDto` — поля: `Id`, `Username`, `PasswordPlain`, `Name`, `Surname`, `Midname`, `DepartmentId`, `Avatar`, `StatusType`, `CreatedAt`

---

## 1.9 Проекции API

**Путь:** `API/Repositories/Projections/`

### UserWithSettingsProjection
Эффективная выборка пользователей с настройками (без загрузки полной сущности).

| Свойство | Тип |
|---|---|
| `Id`, `Username`, `Surname`, `Name`, `Midname` | базовые |
| `Avatar`, `DepartmentId`, `DepartmentName` | |
| `IsBanned`, `LastOnline`, `CreatedAt` | |
| `Theme`, `NotificationsEnabled` | из UserSetting |
| `StatusType`, `StatusExpiresAt` | |

### ChatProjections (`API/Repositories/Projections/ChatProjections.cs`)
- `LastMessageProjection` — данные последнего сообщения (Id, ChatId, CreatedAt, флаги голоса/опроса/файлов, SenderName)
- `DialogPartnerProjection` — партнёр для Contact-чата (UserId, ФИО, Avatar, статус)
- `ChatMemberProjection` — участник с ролью и онлайн-статусом
- `MemberNotificationProjection` — настройки уведомлений участника

---

# 2. СЛОЙ DTO (Shared/Dto)

## 2.1 Auth (`Shared/Dto/Auth/`)

| DTO | Поля |
|---|---|
| `LoginRequest` | `Username`, `Password` |
| `RefreshTokenRequest` | `AccessToken` |
| `AuthResponseDto` | `Id`, `Username`, `DisplayName`, `Token`, `Role` |
| `TokenResponseDto` | `Token`, `UserId`, `Role` |

**Важно:** Refresh-токен передаётся только через httpOnly cookie. В теле ответа его нет.
Серверные модели (не DTO): `AuthLoginResult` (DTO + RefreshToken), `AuthRefreshResult` (DTO + RefreshToken).

---

## 2.2 Call (`Shared/Dto/Call/`)

| DTO | Ключевые поля |
|---|---|
| `CallInviteDto` | `CallId`, `ChatId`, `ChatName`, `InitiatorId/Name/Avatar`, `ActiveParticipantsCount`, `IsGroupCall`, `Mode` |
| `CallStateDto` | `CallId`, `ChatId`, `Status`, `InitiatorId`, `StartedAt` (DateTimeOffset), `IsGroupCall`, `Mode`, `Participants` |
| `CallParticipantDto` | `UserId`, `DisplayName`, `AvatarUrl`, `IsMuted`, `IsSpeaking` |
| `RelayEndpointInfo` | `Host`, `Port`, `CallId` |
| `SignalDto` | `CallId`, `FromUserId`, `TargetUserId` (-1=broadcast), `Type`, `Payload` | Сигнальное сообщение. В проекте используется только тип udp-endpoint: Payload = строка вида "192.168.1.5:49200,10.0.0.3:49200" (список IP:port через запятую). |
| `CallChatMessageDto` | `CallId`, `SenderId`, `SenderName`, `SenderAvatar`, `Text`, `SentAt` |

---

## 2.3 Chat (`Shared/Dto/Chat/`)

| DTO | Ключевые поля |
|---|---|
| `ChatDto` | `Id`, `Name`, `Type`, `LastMessage*` (7 полей), `UnreadCount`, `Contact*` (4 поля), `CurrentUserRole` (ChatRole?, игнорируется при null), `ShowHistoryForNewMembers`, `HideSenderPrefix` [JsonIgnore] |
| `ChatMemberDto` | `ChatId`, `UserId`, `Role`, `JoinedAt`, `NotificationsEnabled`, `Username`, `DisplayName`, `Avatar` |
| `ChatUpdateEventDto` | `Id`, `Name`, `Type`, `CreatedById`, `Avatar`, `ShowHistoryForNewMembers` — отправляется через SignalR при обновлении метаданных чата |
| `UpdateChatDto` | `Id`, `Name?`, `ChatType?`, `ShowHistoryForNewMembers?` |
| `ChatNotificationSettingsDto` | `ChatId`, `NotificationsEnabled` |

---

## 2.4 Message (`Shared/Dto/Message/`)

| DTO | Назначение |
|---|---|
| `CreateMessageRequest` | `ChatId` [Required], `Content` [MaxLength 4000], `ReplyToMessageId?`, `ForwardedFromMessageId?`, `IsVoiceMessage`, `VoiceDurationSeconds`, `VoiceWaveform`, `VoiceFileSize`, `VoiceFileUrl`, `Files?` |
| `MessageDto` | Полное представление. `IsPinned` = `PinnedAt != null`. Многие поля с `JsonIgnore(WhenWritingNull)`. `Files` не сериализуется при null. |
| `MessageFileDto` | `Id`, `MessageId`, `FileName`, `ContentType`, `Url`, `PreviewType` (file/image/video), `FileSize` |
| `MessageReplyPreviewDto` | `Id`, `ChatId`, `SenderId?`, `SenderName?`, `Content?`, `CreatedAt`, `IsDeleted`, `IsVoiceMessage`, `HasPoll`, `FilesCount` |
| `MessageForwardInfoDto` | `OriginalMessageId`, `OriginalChatId`, `OriginalSenderId?`, `OriginalSenderName?`, `OriginalCreatedAt` |
| `PagedMessagesDto` | `Messages`, `HasMoreMessages`, `HasNewerMessages` |
| `UpdateMessageDto` | `Id`, `Content?` |
| `ChatCountsDto` | `MediaCount`, `FilesCount`, `PollsCount`, `PinnedCount` |

---

## 2.5 Прочие DTO

### Department (`Shared/Dto/Department/`)
- `DepartmentDto` — `Id`, `Name`, `ParentDepartmentId`, `Head`, `HeadName`, `UserCount`
- `UpdateDepartmentMemberDto` — `UserId`

### Notification (`Shared/Dto/Notification/`)
- `NotificationDto` — `Type` (message/mention/poll), `ChatId`, `ChatName`, `Avatar`, `MessageId`, `Sender*`, `Preview` (до 100 символов), `CreatedAt`

### Online (`Shared/Dto/Online/`)
- `UserStatusDto` — `UserId`, `IsOnline`, `LastOnline`, `StatusType`, `StatusExpiresAt`
- `OnlineUsersResponseDto` — `OnlineUserIds`, `TotalOnline`
- `SetStatusRequest` — `StatusType`, `Duration`

### Poll (`Shared/Dto/Poll/`)
- `CreatePollDto` — `ChatId`, `Question`, `IsAnonymous`, `AllowsMultipleAnswers`, `Options`
- `PollDto` — + `SelectedOptionIds`, `CanVote`
- `PollOptionDto` — + `VotesCount`, `Votes`
- `PollVoteDto`

### ReadReceipt (`Shared/Dto/ReadReceipt/`)
- `MarkAsReadDto` — `ChatId`, `MessageId`
- `ReadReceiptResponseDto`, `UnreadCountDto`, `AllUnreadCountsDto`
- `ChatReadInfoDto` — + `FirstUnreadMessageId`

### Search (`Shared/Dto/Search/`)
- `GlobalSearchMessageDto` — + `HighlightedContent`, `HasFiles/Voice/Poll`, `SenderId` nullable
- `GlobalSearchResponseDto`, `SearchMessagesResponseDto`
- `SearchMessagesQueryDto` / `GlobalSearchQueryDto`

### User (`Shared/Dto/User/`)
- `UserDto` — 16 полей
- `CreateUserDto` — ФИО + `DepartmentId`
- `ChangePasswordDto`, `ChangeUsernameDto`, `ResetPasswordAdminDto`
- `AvatarResponseDto`
- `UserBannedDto` — `Reason`
- `UserPermissionsChangedDto` — `UserId`, `Role`, `Reason`

---

# 3. КОНТРОЛЛЕРЫ (API/Controllers)

## BaseController
**Путь:** `API/Controllers/BaseController.cs`

Методы: `GetCurrentUserId()`, `IsCurrentUser(int)`, `Map(Result<T>)`, `ExecuteAsync(...)`

Маппинг `Result` → HTTP:
- `Unauthorized` → 401
- `Forbidden` → 403
- `NotFound` → 404
- `Conflict` → 409
- `Internal` → 500
- default → 400

---

## AuthController
**Путь:** `API/Controllers/AuthController.cs`
**Rate limit:** `login` на POST login

| Метод | Маршрут | Auth | Тело / Ответ |
|---|---|---|---|
| POST | `/api/auth/login` | Anonymous | `LoginRequest` → `AuthResponseDto` + cookie |
| POST | `/api/auth/refresh` | Anonymous | `RefreshTokenRequest` (AccessToken) + cookie → `TokenResponseDto` + новый cookie |
| POST | `/api/auth/revoke` | Authorized | — → отзыв refresh-токена из cookie |

---

## UsersController
**Путь:** `API/Controllers/UsersController.cs`

| Метод | Маршрут | Auth |
|---|---|---|
| GET | `/api/users` | Authorized |
| GET | `/api/users/{id}` | Authorized |
| PUT | `/api/users/{id}` | IsCurrentUser |
| GET | `/api/users/online` | Authorized |
| GET | `/api/users/status/batch` | Authorized |
| GET | `/api/users/{id}/status` | Authorized |
| PUT | `/api/users/{id}/status` | IsCurrentUser |
| POST | `/api/users/{id}/avatar` | IsCurrentUser |
| DELETE | `/api/users/{id}/avatar` | IsCurrentUser |
| PUT | `/api/users/{id}/username` | IsCurrentUser |
| PUT | `/api/users/{id}/password` | IsCurrentUser |

---

## ChatsController
**Путь:** `API/Controllers/ChatsController.cs`

| Метод | Маршрут | Auth |
|---|---|---|
| GET | `/api/chats/user/{userId}` | IsMember |
| GET | `/api/chats/user/{userId}/dialogs` | Authorized |
| GET | `/api/chats/user/{userId}/groups` | Authorized |
| GET | `/api/chats/user/{userId}/contact/{contactUserId}` | Authorized |
| POST | `/api/chats` | Authorized |
| GET | `/api/chats/{id}` | IsMember |
| PUT | `/api/chats/{id}` | IsAdmin |
| DELETE | `/api/chats/{id}` | IsOwner |
| POST | `/api/chats/{chatId}/avatar` | IsAdmin |
| GET | `/api/chats/{chatId}/members` | IsMember |
| GET | `/api/chats/{chatId}/members/detailed` | IsMember |
| POST | `/api/chats/{chatId}/members` | IsAdmin |
| DELETE | `/api/chats/{chatId}/members/{userId}` | IsAdmin или IsCurrentUser |
| PUT | `/api/chats/{chatId}/members/{userId}/role` | IsOwner |

---

## MessagesController
**Путь:** `API/Controllers/MessagesController.cs`
**Rate limit:** `messaging` на Create, `search` на Search

| Метод | Маршрут | Auth |
|---|---|---|
| POST | `/api/messages` | IsMember |
| GET | `/api/messages/{id}` | IsMember |
| PUT | `/api/messages/{id}` | IsCurrentUser (sender) |
| DELETE | `/api/messages/{id}` | IsCurrentUser или IsAdmin |
| POST | `/api/messages/{id}/pin` | IsAdmin |
| DELETE | `/api/messages/{id}/pin` | IsAdmin |
| GET | `/api/messages/chat/{chatId}/latest` | IsMember |
| GET | `/api/messages/chat/{chatId}/pinned` | IsMember |
| GET | `/api/messages/chat/{chatId}/counts` | IsMember |
| GET | `/api/messages/chat/{chatId}/before/{beforeId}` | IsMember |
| GET | `/api/messages/chat/{chatId}/after/{afterId}` | IsMember |
| GET | `/api/messages/chat/{chatId}/around/{messageId}` | IsMember |
| GET | `/api/messages/chat/{chatId}/search` | IsMember |
| GET | `/api/messages/user/{userId}/search` | IsCurrentUser |

---

## FilesController
**Путь:** `API/Controllers/FilesController.cs`
**Rate limit:** `upload`

| Метод | Маршрут | Auth |
|---|---|---|
| POST | `/api/files/upload?chatId={chatId}` | IsMember |

---

## DepartmentsController
**Путь:** `API/Controllers/DepartmentsController.cs`

| Метод | Маршрут | Auth |
|---|---|---|
| GET | `/api/departments` | Authorized |
| POST | `/api/departments` | Admin |
| GET | `/api/departments/{id}` | Authorized |
| PUT | `/api/departments/{id}` | Admin или Head |
| DELETE | `/api/departments/{id}` | Admin |
| GET | `/api/departments/{id}/members` | Authorized |
| POST | `/api/departments/{id}/members` | Admin или Head |
| DELETE | `/api/departments/{departmentId}/members/{userId}` | Admin или Head |
| GET | `/api/departments/{id}/can-manage` | Authorized |

---

## PollsController
**Путь:** `API/Controllers/PollsController.cs`

| Метод | Маршрут | Auth |
|---|---|---|
| POST | `/api/polls` | IsMember |
| GET | `/api/polls/{pollId}?userId={userId}` | IsMember |
| POST | `/api/polls/vote` | IsMember |
| POST | `/api/polls/{pollId}/close` | IsMember |

---

## ReadReceiptsController
**Путь:** `API/Controllers/ReadReceiptsController.cs`

| Метод | Маршрут | Auth |
|---|---|---|
| POST | `/api/readreceipts/mark-read` | Authorized |
| GET | `/api/readreceipts/chat/{chatId}/unread-count` | Authorized |
| GET | `/api/readreceipts/unread-counts` | Authorized |

---

## StatusController
**Путь:** `API/Controllers/StatusController.cs`

| Метод | Маршрут | Auth |
|---|---|---|
| PUT | `/api/users/{id}/status` | IsCurrentUser |
| GET | `/api/users/{id}/status` | Authorized |

---

## NotificationsController
**Путь:** `API/Controllers/NotificationsController.cs`

| Метод | Маршрут | Auth |
|---|---|---|
| GET | `/api/notifications/settings` | Authorized |
| GET | `/api/notifications/chat/{chatId}/settings` | Authorized |
| POST | `/api/notifications/chat/mute` | Authorized |

---

## AdminController
**Путь:** `API/Controllers/AdminController.cs`
**Auth:** только роль Admin

| Метод | Маршрут |
|---|---|
| GET | `/api/admin/users` |
| POST | `/api/admin/users` |
| PUT | `/api/admin/users/{userId}` |
| POST | `/api/admin/users/{userId}/toggle-ban` |
| POST | `/api/admin/users/{userId}/reset-password` |

---

# 4. ХАБЫ (SignalR)

## MessengerHub
**Путь:** `API/Hubs/MessengerHub.cs`
**Эндпоинт:** `/chatHub`
**Группы:** `user_{id}` (личные уведомления), `chat_{id}` (события чата)

### Жизненный цикл подключения
- `OnConnectedAsync` — добавление в группы чатов пользователя, публикация `UserStatusChanged` / `UserOnline`
- `OnDisconnectedAsync` — обновление `LastOnline`, рассылка `UserOffline`, выход из активных звонков

### Клиентские методы (вызываются с клиента → сервер)

**Чат:**

| Метод | Назначение |
|---|---|
| `JoinChat(chatId)` | Подписаться на группу чата |
| `LeaveChat(chatId)` | Отписаться от группы чата |
| `MarkAsRead(chatId, messageId)` | Отметить сообщение прочитанным |
| `MarkMessageAsRead(chatId, messageId)` | То же (алиас) |
| `GetUnreadCounts()` | Получить счётчики непрочитанных |
| `GetReadInfo(chatId)` | Информация о прочтении в чате |
| `SendTyping(chatId)` | Индикатор печати |
| `GetOnlineUsersInChat(chatId)` | Онлайн-пользователи чата |
| `SetStatus(statusType, duration)` | Установить статус |

**Звонки:**

| Метод | Назначение |
|---|---|
| `InitiateCall(chatId)` | Начать звонок |
| `JoinCall(callId)` | Присоединиться |
| `LeaveCall(callId)` | Покинуть |
| `DeclineCall(callId)` | Отклонить |
| `CancelCall(callId)` | Отменить (для группового делегирует в LeaveCall) |
| `SendSignal(dto)` | Передача сигнального сообщения. В проекте: тип udp-endpoint, Payload = список IP:port локальных адресов отправителя |
| `ToggleMute(callId, isMuted)` | Мьют |
| `ToggleSpeaking(callId, isSpeaking)` | Индикатор речи |
| `SendCallMessage(callId, text)` | Сообщение в чате звонка |
| `GetCallState(callId)` | Текущее состояние звонка |

### Серверные события (сервер → клиент)

**Чат:**

| Событие | Данные |
|---|---|
| `UserStatusChanged` | `UserStatusDto` |
| `UserOnline` | `userId` |
| `UserOffline` | `userId` |
| `UserTyping` | `chatId`, `userId`, `userName` |
| `MessageRead` | `chatId`, `userId`, `messageId` |
| `UnreadCountUpdated` | `UnreadCountDto` |
| `ChatUpdated` | `ChatUpdateEventDto` |
| `ChatRemoved` | `chatId` — отправляется персонально удалённому участнику |
| `ReceiveMessage` | `MessageDto` |
| `MessageUpdated` | `MessageDto` |
| `MessageDeleted` | `messageId` |
| `PollUpdated` | `PollDto` |
| `ReceiveNotification` | `NotificationDto` |

**Звонки:**

| Событие | Данные |
|---|---|
| `IncomingCall` | `CallInviteDto` |
| `CallStateUpdated` | `CallStateDto` |
| `CallEnded` | `callId`, `reason` |
| `CallMessageReceived` | `CallChatMessageDto` |
| `CallParticipantJoined` | `CallParticipantDto` |
| `CallParticipantLeft` | `userId` |
| `ParticipantMuteChanged` | `userId`, `isMuted` |
| `ParticipantSpeakingChanged` | `userId`, `isSpeaking` |
| `ActiveCallStarted` | `CallStateDto` |
| `ActiveCallUpdated` | `CallStateDto` |
| `ActiveCallEnded` | `callId` |
| `CallError` | `message` |
| `RelayEndpoint` | `RelayEndpointInfo` |
| `ReceiveSignal` | `SignalDto` — используется для обмена UDP-эндпоинтами (тип `udp-endpoint`) |

### Особенности реализации
- Кэш имён/аватаров участников: `ConcurrentDictionary<int, Task<(string? Name, string? Avatar)>>`
- `JoinCall` обёрнут в try-catch с отправкой `CallError`
- Имена методов вынесены в константы `HubMethods` (`Shared/Hubs/HubMethods.cs`)

---

# 5. MIDDLEWARE

**Порядок регистрации** — см. секцию «Порядок middleware» в начале документа.

| Middleware | Путь | Назначение |
|---|---|---|
| `ExceptionHandlingMiddleware` | `API/Middleware/ExceptionHandlingMiddleware.cs` | Все исключения → 500 + `ApiResponse`. Dev: стектрейс, Prod: "Произошла внутренняя ошибка" |
| `MissingFileCleanupMiddleware` | `API/Middleware/MissingFileCleanupMiddleware.cs` | 404 на `/uploads` или `/avatars` → очистка ссылок в БД (только GET/HEAD, после StaticFiles) |

**CookiePolicy:** `SameSite=Lax`, `HttpOnly=Always`, `Secure` зависит от `DisableHttpsRedirection`

---

# 6. МАППИНГ (API/Mapping)

Ручной маппинг через extension-методы, без AutoMapper.

| Файл | Ключевые методы |
|---|---|
| `ChatMappings.cs` | `.ToDto(IUrlBuilder?)`, `.ToDto(User? contact, IUrlBuilder?)` |
| `FileMappings.cs` | `.ToDto()`, `DeterminePreviewType(contentType)` → file/image/video/audio |
| `MessageMappings.cs` | `.ToDto(currentUserId, urlBuilder)` — рекурсивный обход цепочки пересылки, заполняет `OriginalSenderId` |
| `PollMappings.cs` | `Poll.ToDto(currentUserId?)` (SelectedOptionIds, CanVote), `PollOption.ToDto(isAnonymous)` |
| `UserMappings.cs` | `.ToDto(urlBuilder, isOnline?)` — включает `StatusType` и `StatusExpiresAt` |

---

# 7. ИНФРАСТРУКТУРА API

## 7.1 Утилиты (API/Common)

| Класс | Путь | Назначение |
|---|---|---|
| `AppDateTime` | `API/Common/AppDateTime.cs` | Обёртка `TimeProvider`. **Возвращает `DateTimeKind.Unspecified`** (см. §21) |
| `Result<T>` / `Result` | `API/Common/Result.cs` | ROP: `IsSuccess`, `IsFailure`, `Error`, `ErrorType`. Фабрики: `Success()`, `Failure()`, `NotFound()`, `Forbidden()`, `Conflict()`, `Internal()` |
| `ResultExtensions` | `API/Common/Result.cs` | `UnwrapOrDefault`, `UnwrapOrFallback`, `TryUnwrap` |
| `ValidationHelper` | `API/Common/ValidationHelper.cs` | `ValidateUsername` (regex `^[a-z0-9_]{3,30}$`), `ValidatePassword` (≥6 символов) |
| `StatusExtensions` | `API/Common/StatusExtensions.cs` | `Parse(string?)` → TimeSpan: "15m"/"30m"/"1h"/"2h"/"4h"/"8h"/"24h" |
| `UrlHelpers` | `API/Common/UrlHelpers.cs` | `BuildFullUrl(string?, IUrlBuilder?)` |
| `HubMethods` | `Shared/Hubs/HubMethods.cs` | Константы имён хаб-методов. Вложенные классы: `Chat`, `Call`, `ChatInvoke`, `CallInvoke` |
| `SystemEventMeta` | `Shared/Helpers/SystemEventMeta.cs` | Форматирует системные сообщения, предоставляет префиксы/суффиксы для UI |

---

## 7.2 Конфигурация (API/Configuration)

### JwtSettings (`API/Configuration/JwtSettings.cs`)
`AccessTokenLifetimeMinutes=15`, `RefreshTokenLifetimeDays=30`, `Issuer="API"`, `Audience="MessengerClient"`

### MessengerSettings (`API/Configuration/MessengerSettings.cs`)
`AdminDepartmentId=1`, `MaxFileSizeBytes=300MB`, `BcryptWorkFactor=12`, `MaxImageDimension=100px`, `ImageQuality=85`, `DefaultPageSize=50`, `MaxPageSize=100`

### RateLimitKey (`API/Configuration/RateLimitKey.cs`)
- `GetIpPartitionKey(context)` — по IP
- `GetUserOrIpPartitionKey(context)` — по userId если авторизован, иначе по IP

### DependencyInjection (`API/Configuration/DependencyInjection.cs`)

| Метод | Что регистрирует |
|---|---|
| `AddMessengerDatabase` | DbContext + PostgreSQL enum mapping через `EnumTypeMappings` |
| `AddInfrastructureServices` | Репозитории (Scoped), `CacheService`, `AccessControlService`, `FileService`, `TokenService`, `HubNotifier`, `UrlBuilder`, `CallSessionService` (Singleton), `CallMixerService` (Singleton), `CallRelayService` (HostedService + Singleton), `OnlineUserService` (Singleton), бандлы |
| `AddBundles` | `TimeBundle`, `UrlBundle`, `CacheBundle`, `NotificationBundle`, `MediaBundle`, `PresenceBundle`, `ChatBundle` |
| `AddBusinessServices` | Все бизнес-сервисы (Scoped) + `StatusCleanupHostedService` |
| `AddMessengerJson` | `ReferenceHandler.IgnoreCycles`, `WriteIndented` в Dev |

### Бандлы (`API/Services/Infrastructure/Bundles/`)
Группировка зависимостей для упрощения DI в сервисах:
- `TimeBundle(AppDateTime)`
- `UrlBundle(IUrlBuilder)`
- `CacheBundle(IAccessControlService, ICacheService)`
- `NotificationBundle(IHubNotifier, INotificationService)`
- `MediaBundle(IFileService)`
- `PresenceBundle(IOnlineUserService)`
- `ChatBundle(ISystemMessageService, CacheBundle, NotificationBundle, TimeBundle)`

---

## 7.3 Инфраструктурные сервисы (API/Services/Infrastructure)

### База данных
- `EnumTypeMappings` — трансляторы имён enum для Npgsql (ChatRole, ChatType, UserStatusType)
- `EnumNameTranslator` — реализует `INpgsqlNameTranslator`, берёт маппинг из словаря, fallback на snake_case

### Безопасность
- `AccessControlService` (`API/Services/Infrastructure/Security/`) — проверка прав с двойным кэшем (MemoryCache + per-request). `IsSystemAdmin()` даёт bypass для роли Admin
- `AccessControlExtensions` — `EnsureMemberOfAsync`, `EnsureAdminOfAsync`, `EnsureOwnerOfAsync` → `Result`

### Остальные
- `OnlineUserService` — Singleton, `ConcurrentDictionary<userId, ConcurrentDictionary<connectionId, byte>>`, очистка каждые 5 мин
- `UserStatusService` — обновление статусов через `ExecuteUpdateAsync`, использует `IHubContext<MessengerHub>`
- `StatusCleanupHostedService` — фоновый сервис, очистка истёкших статусов каждую минуту
- `CacheService` — MemoryCache: чаты (TTL 5м, sliding 2м), членство (TTL 10м, sliding 3м)
- `HubNotifier` — `SendToChatAsync`, `SendToUserAsync` через `IHubContext<MessengerHub>`, глотает исключения
- `HttpUrlBuilder` — абсолютный URL через `IHttpContextAccessor`
- `UdpDiscoveryService` — UDP порт 5275. Запрос: `MESSENGER_DISCOVER`, ответ: `MESSENGER_HERE:PORT` или `MESSENGER_HERE:PORT:IP`

---

## 7.4 Репозитории (API/Repositories)

### Базовый класс
`RepositoryBase<TEntity>` (`API/Repositories/Base/`) — `FindByIdAsync`, `ExistsAsync`, `Add`, `Remove`

### Реализации

| Интерфейс | Реализация | Путь | Особенности |
|---|---|---|---|
| `IUserRepository` | `UserRepository` | `Implementations/UserRepository.cs` | `FindByUsernameAsync`, `FindByIdWithPasswordAsync`, `GetAllWithSettingsAsync`, `GetWithSettingsAsync`, `UsernameExistsByOtherUserAsync` |
| `IRefreshTokenRepository` | `RefreshTokenRepository` | `Implementations/RefreshTokenRepository.cs` | Отзыв семейства, отзыв всех для пользователя, удаление истёкших, активные семьи |
| `IChatRepository` | `ChatRepository` | `Implementations/ChatRepository.cs` | `GetLastMessagesAsync` разрешает цепочку пересылки |
| `IMessageRepository` | `MessageRepository` | `Implementations/MessageRepository.cs` | `GetLatestAsync` выбирает ID → раздельно UserMessages и SystemMessages → сортирует по карте порядка. `GetChatCountsAsync` считает файлы. `LightQuery` загружает `Poll.PollOptions.PollVotes` |
| `IReadReceiptRepository` | `ReadReceiptRepository` | `Implementations/ReadReceiptRepository.cs` | Все операции с отметками о прочтении |
| `IPollRepository` | `PollRepository` | `Implementations/PollRepository.cs` | Управление опросами и голосами |

---

# 8. СЛОЙ СЕРВИСОВ (API/Services)

## 8.1 BaseService
**Путь:** `API/Services/Base/BaseService.cs`
Обрабатывает `DbUpdateException`: Concurrency → Conflict, UniqueViolation → Conflict, остальные → Internal.
Методы: `FindEntityAsync`, `Paginate`, `NormalizePagination`

---

## 8.2 AuthService + TokenService
**Путь:** `API/Services/Core/Auth/`

**AuthService:**
- `LoginAsync` — timing-safe проверка пароля, проверка бана, определение роли (Admin если `DepartmentId == AdminDepartmentId`, Head если руководит отделом, иначе User), ограничение сессий (MaxActiveSessions=5, удаление старых)
- `RefreshTokenAsync` — обнаружение повторного использования токена, ротация, возврат `AuthRefreshResult`

**TokenService:**
- JWT: HMAC-SHA256, ClockSkew=Zero
- Refresh-токен: 64 байта Base64
- `GenerateTokenPair`, `ValidateToken`, `GetPrincipalFromExpiredToken`, `HashToken`

---

## 8.3 Бизнес-сервисы (API/Services/Features)

| Сервис | Путь | Строк | Ключевое поведение |
|---|---|---|---|
| `CallSessionService` | `Features/Call/` | 150 | Singleton, `ConcurrentDictionary`. Не масштабируется горизонтально |
| `CallMixerService` | `Features/Call/` | 184 | Микширование Opus-потоков на сервере для ServerMixed. Аудио от всех участников смешивается в один поток для каждого |
| `CallRelayService` | `Features/Call/` | 128 | UDP relay для групповых звонков (порт 5276). Передаёт аудио между клиентами и CallMixerService |
| `ChatService` | `Features/Chat/` | ~486 | `BuildChatDto` принимает словарь ролей, устанавливает `CurrentUserRole`. События отправляются через `IHubContext<MessengerHub>` |
| `ChatMemberService` | `Features/Chat/` | 136 | При удалении участника отправляет `ChatRemoved` персонально через `HubNotifier.SendToUserAsync` |
| `DepartmentService` | `Features/Department/` | 378 | Автоуправление связанными чатами при CRUD отделов. BFS для проверки циклов в иерархии |
| `FileService` | `Features/Messaging/` | 105 | Изображения → WebP. Путь: `wwwroot/uploads/chats/{chatId}/{guid}{ext}` |
| `MessageService` | `Features/Messaging/` | ~516 | `GetMessagesAroundAsync` загружает якорное сообщение через `FindUserMessageWithIncludesNoTrackingAsync`, затем UserMessages и SystemMessages последовательно. `PinMessageAsync` проверяет дубликат закрепления |
| `NotificationService` | `Features/Chat/` | 108 | Для Contact-чата: `ChatName` = имя отправителя. Preview ≤100 символов |
| `PollService` | `Features/Messaging/` | 144 | `ClosesAt` **не сохраняется** при создании (намеренно — поле зарезервировано для будущего) |
| `ReadReceiptService` | `Features/ReadReceipt/` | 114 | Использует `IReadReceiptRepository` |
| `AdminService` | `Features/User/` | 229 | Маппит `UserWithSettingsProjection` → `UserDto` |
| `UserService` | `Features/User/` | 189 | `GetAllUsersAsync`/`GetUserAsync` используют проекции. `ChangeUsernameAsync` проверяет уникальность |
| `SystemMessageService` | `Features/Chat/` | 43 | Создаёт `SystemMessage` с `InitiatorId`. Отправка через `HubMethods.Chat.ReceiveMessage` |

---

# 9. АБСТРАКЦИИ (интерфейсы)

## API сервисы (`API/Services/Abstractions/`)

| Интерфейс | Ключевые методы |
|---|---|
| `IAuthService` | `LoginAsync → Result<AuthLoginResult>`, `RefreshTokenAsync → Result<AuthRefreshResult>`, `RevokeRefreshTokenAsync` |
| `ITokenService` | `GenerateTokenPair`, `ValidateToken`, `GetPrincipalFromExpiredToken`, `HashToken` |
| `ICallSessionService` | `CreateCallAsync`, `JoinCall`, `LeaveCall`, `EndCallAsync`, `ToStateDto` |
| `IChatService` | `GetUserChatsAsync`, `GetContactChatAsync`, `CreateChatAsync`, `UpdateChatAsync`, `DeleteChatAsync` |
| `IChatMemberService` | `AddMemberAsync`, `RemoveMemberAsync`, `UpdateRoleAsync`, `GetMembersAsync` |
| `IMessageService` | `CreateMessageAsync`, `GetLatestMessagesAsync`, `GetMessagesAroundAsync`, `SearchMessagesAsync`, `GlobalSearchAsync`, `PinMessageAsync`, `GetChatCountsAsync` |
| `IPollService` | `CreatePollAsync`, `VoteAsync`, `ClosePollAsync`, `GetPollAsync` |
| `IReadReceiptService` | `MarkAsReadAsync`, `GetUnreadCountAsync`, `GetAllUnreadCountsAsync`, `GetChatReadInfoAsync` |
| `IDepartmentService` | `GetDepartmentsAsync`, `CreateDepartmentAsync`, `UpdateDepartmentAsync`, `DeleteDepartmentAsync` |
| `IFileService` | `SaveImageAsync`, `SaveMessageFileAsync`, `DeleteFile`, `IsValidImage` |
| `IUserService` | `GetAllUsersAsync`, `GetUserAsync`, `UpdateUserAsync`, `UploadAvatarAsync`, `RemoveAvatarAsync`, `ChangeUsernameAsync`, `ChangePasswordAsync` |
| `IAdminService` | `GetUsersAsync`, `CreateUserAsync`, `UpdateUserAsync`, `ToggleBanAsync`, `ResetPasswordAsync` |
| `INotificationService` | `SendNotificationAsync`, `SendMentionNotificationAsync`, `SetChatMuteAsync` |
| `ISystemMessageService` | `CreateAsync`, `CreateCallStartedMessageAsync`, `CreateCallEndedMessageAsync` |
| `IUserStatusService` | `SetStatusAsync`, `GetStatusAsync`, `CleanupExpiredStatusesAsync` |
| `IOnlineUserService` | `UserConnected`, `UserDisconnected`, `IsOnline`, `GetOnlineUserIds`, `FilterOnline` |
| `IHubNotifier` | `SendToChatAsync(chatId, method, args)`, `SendToUserAsync(userId, method, args)` |
| `ICacheService` | `GetUserChatIdsAsync`, `GetMembershipAsync`, `InvalidateUserChats`, `InvalidateMembership`, `InvalidateChat` |
| `IAccessControlService` | `IsMemberAsync`, `IsAdminAsync`, `IsOwnerAsync`, `EnsureMemberOfAsync`, `GetChatMemberIdsAsync` |
| `IUrlBuilder` | `BuildUrl(string?)` |

## Репозитории (`API/Repositories/Abstractions/`)

| Интерфейс | Ключевые методы |
|---|---|
| `IUserRepository` | `FindByUsernameAsync`, `FindByIdAsync`, `FindByIdWithPasswordAsync`, `UsernameExistsAsync`, `Add`, `GetAllWithSettingsAsync`, `GetWithSettingsAsync` |
| `IRefreshTokenRepository` | `RevokeByFamilyIdAsync`, `RevokeAllForUserAsync`, `DeleteExpiredAsync`, `GetActiveFamiliesAsync` |
| `IChatRepository` | `FindByIdAsync`, `FindByIdWithMembersAsync`, `IsMemberAsync`, `GetByIdsLightAsync`, `GetLastMessagesAsync`, `GetDialogPartnersAsync`, `UpdateLastMessageTimeAsync`, `GetMembersWithUsersAsync`, `GetVoiceFilePathsAsync`, `GetChatTypeAsync`, `GetShowHistoryForNewMembersAsync`, `GetContactChatsWithMembersAsync`, `SearchGroupChatsAsync`, `GetMembersForNotificationAsync`, `GetHistoryRestrictionsAsync`, `Add`, `AddMember`, `RemoveMember` |
| `IMessageRepository` | `FindUserMessageByIdAsync`, `FindUserMessageWithIncludesAsync`, `FindUserMessageForDeleteAsync`, `FindForBroadcastAsync`, `GetWithIncludesAsync`, `GetBeforeAsync`, `GetAfterAsync`, `GetUserMessagesForMixedAsync`, `GetSystemMessagesAsync`, `GetPinnedAsync`, `CountAsync`, `HasOlderAsync`, `HasNewerAsync`, `ExistsInChatAsync`, `ExistsAsync`, `SearchInChatAsync`, `SearchGlobalAsync`, `GetForwardedToChatIdsAsync`, `SoftDeleteAsync`, `PinAsync`, `UnpinAsync`, `Add`, `RemoveVoiceMessage`, `FindUserMessageWithIncludesNoTrackingAsync`, `GetLatestAsync`, `GetChatCountsAsync` |
| `IReadReceiptRepository` | `FindMemberAsync`, `FindMemberReadonlyAsync`, `UpdateReadPointerAsync`, `CountUnreadAsync`, `GetUnreadInfoAsync`, `GetAllUnreadCountsAsync`, `GetUnreadCountsAsync`, `MessageExistsAsync`, `GetLastMessageIdAsync` |
| `IPollRepository` | `FindByIdWithDetailsAsync`, `Add(Poll)`, `AddOption`, `AddVote`, `GetUserVotesAsync`, `RemoveVotes`, `CloseAsync` |

---

# 10. ПЕРЕЧИСЛЕНИЯ (Shared/Enum)

| Enum | Значения | Примечание |
|---|---|---|
| `CallEndReason` | Ended, Cancelled, Timeout, Declined | |
| `CallStatus` | Ringing, Active, Ended | |
| `CallMode` | PeerToPeer, ServerMixed | Определяет режим передачи аудио: напрямую между клиентами или через серверный микшер |
| `ChatRole` | Member, Admin, Owner | |
| `ChatType` | Chat, Department, Contact, DepartmentHeads | `EnumMember`: `"chat"`, `"department"`, `"contact"`, `"department_heads"` |
| `SystemEventType` | ChatCreated, MemberAdded, MemberRemoved, MemberLeft, RoleChanged, CallStarted, CallEnded, MessagePinned, MessageUnpinned, ChatAvatarUpdated | `[JsonStringEnumConverter]` |
| `Theme` | light, dark, system | `[JsonStringEnumConverter]` |
| `UserRole` | User, Head, Admin | |
| `UserStatusType` | Online=0, Away=1, Busy=2, DoNotDisturb=3 | |

---

# 11. ОТВЕТЫ API (Shared/Response)

## ApiResponse\<T\>

| Поле | Тип |
|---|---|
| `Success` | `bool` |
| `Data` | `T?` |
| `Message` | `string?` |
| `Error` | `string?` |
| `Details` | `string?` |
| `Timestamp` | `DateTime` (UTC) |

**Фабрики:** `Ok(data, message?)`, `Fail(error, details?)`

## ApiResponseHelper (`Shared/Response/ApiResponseHelper.cs`)
`Success<T>(data, message?)`, `Error<T>(error, details?)`, `Error(error, details?)`

---

# 12. DESKTOP — КОНВЕРТЕРЫ (Desktop/Converters)

## Инфраструктура
- `ConverterLocator` (`ConverterLocator.cs`) — Singleton, регистрирует 60+ конвертеров
- `Converter` / `MultiConverter` (`ConverterExtension.cs`) — MarkupExtension для XAML
- `ConverterBase<TIn, TOut>` (`Base/ConverterBase.cs`) — `AllowNull`, `DefaultValue`, защита от исключений

## Группы конвертеров

**Boolean** (`Converters/Boolean/`):
`BoolToString`, `BoolToGeometry`, `BoolToDouble`, `BoolToColor`, `BoolToHAlignment`, `BoolToBrush`, `BoolToThickness`, `BooleanAnd`, `BooleanOr`, `EnumEquals`, `EnumNotEquals`, `UserRoleToVisibility`

**DateTime** (`Converters/DateTime/`):
- `DateTimeFormatConverter` — форматы: Time/Date/ShortDate/DateTime/Chat/Relative
- `LastMessageDateConverter`
- `LastSeenTextConverter` — Multi; обрабатывает `DateTimeOffset`, извлекает `UtcDateTime`

**Domain** (`Converters/Domain/`):
`ChatRoleToDisplay`, `ContentFilterToLabel`, `InitialsConverter`, `LevelToMargin` (20px×level), `LevelToVisibility`, `SearchScopeToTitle/Watermark/Hint/MessagesHeader`, `ThemeToDisplay`

**Generic** (`Converters/Generic/`):
`ComparisonConverter`, `IndexToText`, `HasContentConverter`, `HasTextOrAttachmentsMultiConverter`, `MultiplyConverter`, `PercentToWidthConverter` (Multi, min 8px), `PluralizeConverter`, `ResourceKeyToGeometryConverter`, `FractionToGridLengthConverter`

---

# 13. DESKTOP — ЛОКАЛЬНАЯ БД (Desktop/Data, SQLite)

## LocalDatabase (`Desktop/Data/LocalDatabase.cs`)
- WAL-режим, `synchronous=NORMAL`, `cache_size=-4000`, `mmap_size=33554432`
- Миграции через `PRAGMA user_version` (текущая версия: 2)
- Потокобезопасность: `SemaphoreSlim`
Индексы: `idx_msg_chat_id` (chat_id, id DESC), `idx_msg_chat_id_asc` (chat_id, id ASC), `idx_chats_last_msg`, `idx_chats_type_date`, `idx_messages_chat_id`

## Cached-модели (`Desktop/Data/Models/Cache/`)

| Модель | Таблица | Особенности |
|---|---|---|
| `CachedMessage` | `messages` | 30+ колонок, `poll_json`/`files_json`, даты в Ticks. `sender_id` — `int?`. Поля `reply_is_voice`, `reply_has_poll`, `reply_files_count` |
| `CachedChat` | `chats` | 18 колонок, `contact_*`, даты в Ticks |
| `CachedUser` | `users` | id, username, display_name, avatar, cached_at |
| `CachedReadPointer` | `read_pointers` | chat_id (PK), last_read, first_unread, unread_count |
| `ChatSyncState` | `chat_sync_state` (`Desktop/Data/Models/Sync/`) | OldestLoadedId, NewestLoadedId, has_more_older/newer |
| `CachedDownloadedFile` | `downloaded_files` | file_id (PK), message_id, local_path, file_name, file_size, downloaded_at, content_type |

## Репозитории и сервисы (`Desktop/Data/Repositories/`)

| Класс | Назначение |
|---|---|
| `MessageCacheRepository` | CRUD сообщений. `TrimOldMessagesAsync` обрабатывает каждый чат отдельно, держит последние 200 сообщений. `MarkDeletedAsync` очищает поля reply и forward |
| `ChatCacheRepository` | Upsert, `UpdateLastMessageAsync` |
| `LocalCacheService` | `GetMessagesBeforeAsync` определяет достижение начала истории через сравнение с `OldestLoadedId`. `PatchChatMetaAsync` для точечного обновления метаданных чата. Поиск по кэшу отсутствует — всегда идёт через API |
| `CacheMapper` | `MessageDto↔CachedMessage`, `ChatDto↔CachedChat`. Source Generated JSON (`CacheJsonContext`). `ToEntity` использует отдельные `PollJsonOpts` с CamelCase |
| `DownloadedFileRepository` | Управление записями о скачанных файлах |

---

# 14. DESKTOP — ИНФРАСТРУКТУРА

## Конфигурация (`Desktop/Infrastructure/Configuration/`)

| Класс | Назначение |
|---|---|
| `ApiEndpoints` | Статический билдер URL всех эндпоинтов API |
| `AppConstants` | `MaxFileSizeBytes=300MB`, `DefaultPageSize=50`, `LoadMorePageSize=30`, `SearchPageSize=20`, `TypingIndicatorDurationMs=3500`, `HighlightDurationMs` |

## Хелперы (`Desktop/Infrastructure/Helpers/`)

| Класс | Назначение |
|---|---|
| `AvatarHelper` | `GetSafeUri`, `GetUriWithCacheBuster`, `WithFreshCacheBuster` |
| `MimeTypeHelper` | `GetMimeType(extension)` |
| `ChatPreviewFormatter` | `BuildPreview`, `BuildReplyPreview`, `Pluralize` (публичный), делегирует системные сообщения `SystemEventMeta` |
| `HttpResponseHelper` | `TryExtractErrorMessage` |
| `PasswordHelper` | `CalculateStrength(0–4)`, `ToStrengthLabel` |
| `RangeObservableCollection<T>` | `AddRange`, `InsertRange`, `RemoveRange` |

## Медиа (`Desktop/Infrastructure/Media/`)

| Класс | Назначение |
|---|---|
| `AuthenticatedImageLoader` | LRU RAM (80 items/30MB), LOH-защита, дедупликация, дисковый кэш. Методы `InvalidateUrl`, `InvalidateByRelativePath`, `IsCached`. Поддержка `CancellationToken` |
| `RemoteImage` | Attached Property для Avalonia Image. `CurrentUrlProperty` публичное. Оптимизация повторной загрузки |
| `MemoryDiagnostics` | Счётчики ChatVM/MessageVM/Bitmap/RemoteImage, LOH, дамп GC. `OnMessageVmDisposed` |

## DI (`Desktop/Infrastructure/Extensions/ServiceCollectionExtensions.cs`)
Регистрирует `CookieContainer`, `ICookieStorageService`, `HttpClient` с `UseCookies=true` и общим `CookieContainer`.

---

# 15. DESKTOP — СЕРВИСЫ

## Auth (`Desktop/Services/Core/Auth/`)

| Сервис | Назначение |
|---|---|
| `AuthService` | `LoginAsync`, `RefreshTokenAsync` (без явного refresh-токена — через cookie), `RevokeAsync`, `Ping`, `IsAccessTokenValid` |
| `SessionStore` | In-Memory: Token, UserId, UserRole, события. Методы: `SetSession(token, userId, role)`, `UpdateTokens(token)`. Свойство `RefreshToken` **отсутствует** |
| `SecureStorageService` | DPAPI/KeyChain/AES в зависимости от платформы |
| `CookieStorageService` | Сохраняет/восстанавливает cookies из `CookieContainer` через `ISecureStorageService`. `PersistAsync`, `RestoreAsync`, `ClearAsync` |
| `AuthManager` | `InitializeAsync` — при запуске сначала восстанавливает cookie, затем пробует refresh. `LoginAsync`, `TryRefreshTokenAsync`, `LogoutAsync` — при логауте очищает cookie и secure storage |

## API & Realtime (`Desktop/Services/Core/`)

| Сервис | Назначение |
|---|---|
| `ApiClientService` | HTTP + авто-рефреш при 401. Cookie прикрепляются автоматически через `CookieContainer` |
| `GlobalHubConnection` | SignalR `/chatHub`, 15+ событий. Retry при 503. События `ChatRemoved`, `ChatUpdated` (через `ChatUpdateEventDto`) |
| `CallHubConnection` | SignalR `/chatHub`, 12 событий. Retry при 503 |

## Звонки (`Desktop/Services/Features/Call/`)

| Сервис | Назначение |
|---|---|
| `CallService` | Оркестратор P2P и ServerMixed звонков. Управляет UDP-сокетом (порты 5275/5276). При получении `RelayEndpoint` переключается в режим серверного микшера. Отправляет аудио на relay или напрямую peers. |
| `CallAudioService` | PortAudio 48kHz/моно/20ms. Opus 32kbps, VBR, VOIP-режим, complexity=5. VAD адаптивный: noiseFloor обновляется α=0.005, порог = max(0.008, noiseFloor×2.5). Hold 1200ms, debounce 150ms. Поддерживает режимы: P2P (микширует индивидуальные очереди) и ServerMixed (плейбек из одного `_serverMixedPlaybackQueue`, заполняемого через `ReceiveMixedAudio`). |
| `NoiseReducer` | FFT → Wiener Filter → Gate. Decision-Directed SNR α=0.96. Включается/выключается через NoiseSuppressionEnabled. |
| `CallHubConnection` | SignalR-соединение к /chatHub. Используется только для сигнализации: передача UDP-эндпоинтов через SendSignalAsync (тип udp-endpoint), управление состоянием звонка (join/leave/mute). Подписывается на `RelayEndpoint` для получения адреса relay. |
| `ActiveCallStore` | ObservableObject: `ActiveCall`, `IsCallUiOpen`, `IsInCall` |

### AudioRecordingState (`Desktop/Services/Features/Media/Audio/`)
Enum: `Idle`, `Recording`, `Sending`, `Error`

### PortAudioLifetime
Singleton, владеет `PortAudio.Initialize()` / `Terminate()`. `EnsureInitialized()`, `IsAvailable`.

### WavData
Загружает 16-битный PCM WAV из потока. `short[] Samples`, `Duration`, `SampleRate`.

## Медиа (`Desktop/Services/Features/Media/`)

| Сервис | Назначение |
|---|---|
| `AudioPlayerService` | WAV через PortAudio. Play/Pause/Resume/Stop/Seek |
| `AudioRecorderService` | 16kHz/моно/16-bit PCM, WAV + Waveform (100 баров) |
| `FileDownloadService` | Скачивание + прогресс, открытие через OS |
| `FileDownloadStateService` | Состояние скачанных файлов, взаимодействует с `IDownloadedFileRepository` |

## Platform (`Desktop/Services/Platform/`)

| Сервис | Назначение |
|---|---|
| `PlatformService` | Clipboard |
| `SettingsService` | JSON в AppData |
| `ThemeService` | `Application.RequestedThemeVariant` |
| `NavigationService` | Стек истории, проверка авторизации |
| `ServerDiscoveryService` | UDP 5275, парсит `MESSENGER_HERE:PORT:IP`. Если IP отсутствует, используется `127.0.0.1` |
| `NotificationService` | Стек ≤3, анимация прогресс-бара |
| `DialogService` | Стек диалогов, `Channel<CloseRequest>`, анимация |
| `CacheMaintenanceService` | Trim, VACUUM, очистка |
| `ChatNotificationApiService` | GET/POST настройки уведомлений чата |
| `ChatInfoPanelStateStore` | `IsOpen` ↔ `ISettingsService["ChatInfoPanelIsOpen"]` |

---

# 16. DESKTOP — АБСТРАКЦИИ (`Desktop/Services/Abstractions/`)

| Интерфейс | Ключевые члены |
|---|---|
| `IApiClientService` | `GetAsync<T>`, `PostAsync`, `PutAsync`, `DeleteAsync`, `UploadFileAsync`, `GetStreamAsync` |
| `IAudioPlayerService` | `Play`, `Pause`, `Resume`, `Stop`, `Seek`, события Position/Started/Stopped |
| `IAudioRecorderService` | `StartAsync`, `StopAsync → AudioRecordingResult?`, `CancelAsync` |
| `IAuthManager` | `LoginAsync`, `LogoutAsync`, `TryRefreshTokenAsync`, `WaitForInitializationAsync` |
| `IAuthService` (клиент) | `LoginAsync(username, password)`, `RefreshTokenAsync(accessToken, refreshToken?=null)` |
| `ICallHubConnection` | 10 методов + 12 событий (включая `RelayEndpoint`) |
| `ICallService` | `StartCallAsync`, `JoinCallAsync`, `LeaveCallAsync`, `ToggleMuteAsync`, события |
| `IDialogService` | `ShowAsync<T>`, `CloseAsync`, `CloseAllAsync` |
| `IFileDownloadService` | `DownloadFileAsync(progress?)`, `OpenFileAsync`, `OpenFolderAsync` |
| `IFileDownloadStateService` | `GetStateAsync(MessageFileDto)`, `RegisterDownloadAsync`, `ResetAsync` |
| `IGlobalHubConnection` | `ConnectAsync`, `DisconnectAsync`, 15+ событий включая `ChatRemoved`, `ChatUpdated` как `Action<ChatUpdateEventDto>` |
| `INavigationService` | `NavigateToLogin`, `NavigateToMainMenu`, `NavigateTo<T>`, `GoBack` |
| `ISecureStorageService` | `SaveAsync<T>`, `GetAsync<T>`, `RemoveAsync` |
| `ISessionStore` | Token, UserId, UserRole, `HasRole(UserRole)`, `SetSession(token, userId, role)`, `UpdateTokens(token)`, события |
| `ISettingsService` | `Get<T>(key)`, `Set<T>(key, value)` |
| `IThemeService` | `Toggle`, `LoadFromSettings`, `SaveTheme` |
| `ICookieStorageService` | `PersistAsync()`, `RestoreAsync()`, `ClearAsync()` |
| Остальные | `INotificationService`, `IPlatformService`, `IServerDiscoveryService`, `ICacheMaintenanceService`, `IChatNotificationApiService`, `IChatInfoPanelStateStore` |

---

# 17. DESKTOP — ФАБРИКИ (`Desktop/ViewModels/ChatList/Factories/`)

| Класс | Назначение |
|---|---|
| `CacheServices` | `ILocalCacheService` + `ICacheMaintenanceService` |
| `CallServices` | `ICallService` + `ActiveCallStore` |
| `ChatCoreServices` | `IChatService` + `IMessageService` + `IPollService` |
| `MediaServices` | `IAudioPlayerService` + `IAudioRecorderService` + `IFileDownloadService` + `IFileDownloadStateService` |
| `ChatViewModelDependencies` | Полный набор зависимостей для `ChatViewModel` |
| `ChatViewModelFactory` | Фабрика: `ChatViewModel(chatId, targetMessageId?)` |
| `ChatsViewModelFactory` | Фабрика `ChatsViewModel` |

---

# 18. DESKTOP — VIEW MODELS

## Базовые классы (`Desktop/ViewModels/Shared/`)

### BaseViewModel (`BaseViewModel.cs`)
`IsBusy`, `ErrorMessage`, `SuccessMessage`.
- `SafeExecuteAsync` — обёртка с обработкой исключений и отмены
- `GetCancellationToken` — создаёт новый `CancellationTokenSource`
- `ClearMessages`
- Виртуальные: `OnIsBusyUpdated`, `OnErrorMessageUpdated`, `OnSuccessMessageUpdated`

### IRefreshable (`IRefreshable.cs`)
`IAsyncRelayCommand RefreshCommand`

---

## Chat Core (`Desktop/ViewModels/Chat/`)

### ChatContext (`Context/ChatContext.cs`)
Центральный объект, содержит все зависимости для `ChatViewModel`. События скролла: `ScrollToMessageRequested`, `ScrollToIndexRequested`, `ScrollToBottomRequested`. Хранит `CurrentUserRole`, `IsSystemAdmin`, список участников. `IDisposable` с токеном отмены.

### ChatCommands (`Commands/ChatCommands.cs`)
Mutable-контейнер команд (Edit, Copy, Delete, TogglePin, Reply, Forward, ShowPollResults и др.), разделяемый между всеми `MessageViewModel` через `ChatMessageManager`.

### ChatFeatureHandler (`Shared/ChatFeatureHandler.cs`)
Базовый класс для обработчиков фич. Содержит ссылку на `ChatContext`, виртуальный `DisposeManaged`.

### IChatNavigator (`Navigation/IChatNavigator.cs`)
Показать диалог опроса, редактирования группы, перейти в чат по пересылке, открыть профиль, открыть интерфейс звонка.

### ChatViewModel (`Core/ChatViewModel.cs`)
**Строк: ~1446.** Основная логика чата: инициализация, загрузка сообщений, управление правами. Принимает `targetMessageId` для начальной навигации. Обрабатывает `ChatUpdatedEvent`.
Состоит из обработчиков: `MessageManager`, `Attachments`, `MemberLoader`, `EditDelete`, `Reply`, `Forward`, `Typing`, `Voice`, `InfoPanel`, `Search`, `Notification`.

### ChatMessageManager (`Managers/ChatMessageManager.cs`)
**Строк: ~612.** Загрузка сообщений (кэш → сервер). `LoadAroundCoreAsync`. При получении сообщения вызывает `ctx.RequestRefreshCounters?.Invoke()`.

### ChatHubSubscriber (`Core/ChatHubSubscriber.cs`)
Подписки на SignalR события для чата.

### MessageViewModel (`Messages/MessageViewModel.cs`)
**Строк: ~556.** Представление сообщения: файлы, голос, опросы. `SystemMessageTime` для системных сообщений. Создаёт `PollViewModel` с проверкой userId.

### Вспомогательные модели
- `LocalFileAttachment` (`Managers/`) — локальное вложение перед отправкой: `MemoryStream`, `Thumbnail`, форматированный размер
- `MessageGroupPosition` (`Messages/`) — enum `Alone, First, Middle, Last` для радиуса пузырей
- `ChatInfoPanelItems` (`Context/`) — `ChatInfoPanelMediaItem`, `ChatInfoPanelFileItem`

---

## ChatList (`Desktop/ViewModels/ChatList/`)

### ChatsViewModel (`Core/ChatsViewModel.cs`)
**Строк: ~603.**
- `IsChatMatchingCurrentTab`: `type is not ChatType.Contact` для групп
- `UpdateChatMeta` — обновляет только метаданные чата
- Отложенная прокрутка: `_pendingScrollToMessageId` — если чат уже открыт, вызывает `ScrollToMessageAsync` немедленно; иначе передаёт id в конструктор `ChatViewModel`

### MainMenuViewModel (`Shell/MainMenuViewModel.cs`)
**Строк: ~833.**
- Группы/контакты: `chat.Type is not ChatType.Contact`
- Управление жизненным циклом CallHub при инициализации и переподключении

---

## Опросы (`Desktop/ViewModels/Chat/Polls/`)

### PollViewModel (`PollViewModel.cs`)
`TotalVotesFormatted` с плюрализацией. `TotalVotes` вычисляется до заполнения `Options`. `ApplyDto` сбрасывает и обновляет. `UpdateOptions` устойчива к null-списку.

### PollOptionViewModel (`PollOptionViewModel.cs`)
`NotifyTotalVotesChanged` — временный сброс/восстановление для гарантированного обновления привязанного процента.

---

# 19. ТИПИЧНЫЕ ПОТОКИ ДАННЫХ

## Отправка сообщения
```
ChatViewModel.SendMessage()
  → загрузка файлов (FileService)
  → POST /api/messages (CreateMessageRequest)
  → MessageService.CreateMessageAsync()
      → AccessControlService.EnsureMemberOfAsync()
      → извлечение @mentions
      → сохранение в PostgreSQL
      → HubNotifier.SendToChatAsync("ReceiveMessage", MessageDto)
  → MessengerHub → все клиенты группы chat_{id}
  → ChatHubSubscriber.OnReceiveMessage()
  → ChatMessageManager.AddReceivedMessage()
  → RequestRefreshCounters?.Invoke()
  → обновление UI
```

## Звонок (P2P UDP)

```
CallService.StartCallAsync(chatId)
  → InitUdp(): UdpClient(0) → случайный локальный порт
  → CallAudioService.Start(): PortAudio input + output streams
  → CallHubConnection.InitiateCallAsync(chatId)
  → сервер: CallSessionService.CreateCallAsync()
            → рассылка IncomingCall всем участникам чата

Принятие → CallService.JoinCallAsync(callId, chatId)
  → InitUdp() + CallAudioService.Start()
  → CallHubConnection.JoinCallAsync(callId)
  → сервер: CallSession.Status = Active
  → рассылка CallStateUpdated всем участникам

Обмен эндпоинтами:
  → CallService.OnCallStateUpdated() / OnParticipantJoined()
  → AnnounceUdpEndpointAsync(targetUserId)
      → SendSignalAsync(SignalDto { Type="udp-endpoint",
          Payload="192.168.1.5:49200,10.0.0.3:49200" })
  → OnSignalReceived() → выбор лучшего эндпоинта по совпадению подсети
      → _peerEndpoints[fromUserId] = bestEndpoint

Аудио (реальное время):
  PortAudio InputCallback → Opus кодирование → OnEncodedFrame
      → SendAudioToAllPeers(): пакет [userId(4) | seq(4) | opusData]
        на каждый _peerEndpoints[peerId]
  ReceiveLoopAsync → ProcessUdpPacket → CallAudioService.ReceiveEncodedAudio
      → декодирование в очередь воспроизведения
  PortAudio OutputCallback → микширование всех _playbackQueues
```

## Звонок (ServerMixed UDP)

```
При инициализации группового звонка:
  → сервер устанавливает CallSession.Mode = ServerMixed
  → вызывает CallRelayService.RegisterCall + AddParticipant
  → отправляет инициатору RelayEndpoint (RelayEndpointInfo)
  → CallService.OnRelayEndpoint переключает _mode = ServerMixed
      и CallAudioService.SetMode(CallMode.ServerMixed)

Аудио (клиент → сервер):
  CallService.SendAudioToRelay(userId, opusData)
      → пакет: [callIdLen(1) | callId(UTF8) | userId(4) | seq(4) | opusData]
      → UdpClient.Send на _relayEndpoint

Серверный relay (CallRelayService):
  → ReceiveLoop → ProcessPacket
      → валидация участника через CallSessionService
      → запись эндпоинта, обновление участника
      → CallMixerService.ReceiveAudio(callId, userId, opusData)
  CallMixerService каждые 20ms:
      → микширует все последние кадры (исключая говорящего)
      → нормализация громкости
      → кодирование в Opus
      → CallRelayService.SendMixedAudio для каждого участника

Аудио (сервер → клиент):
  CallRelayService.SendMixedAudio(userId, opusData)
      → пакет: [seq(4) | opusData]
      → отправка на сохранённый IPEndPoint участника
  Клиент: ProcessUdpPacket → ReceiveMixedAudio → ServerMixed плейбек
```

## Обновление токенов
```
ApiClientService получает 401
  → AuthManager.TryRefreshTokenAsync()
  → POST /api/auth/refresh (AccessToken в теле, refresh-токен в cookie)
  → AuthService.RefreshTokenAsync()
      → проверка refresh-токена из cookie
      → обнаружение reuse → отзыв семьи
      → ротация токенов
      → новый refresh-токен в httpOnly cookie
      → TokenResponseDto в теле (без refresh-токена)
  → CookieStorageService.PersistAsync()
  → SessionStore.UpdateTokens(newAccessToken)
  → повтор оригинального запроса
```

## Загрузка чатов при старте Desktop
```
AuthManager.InitializeAsync()
  → CookieStorageService.RestoreAsync()
  → TryRefreshTokenAsync()
  → NavigationService.NavigateToMainMenu()

MainMenuViewModel.InitializeAsync()
  → LocalCacheService.GetChatsAsync() → мгновенный показ из SQLite
  → GET /api/chats/user/{userId} → обновление из сети
  → merge: LocalCacheService.UpsertChatsAsync()
  → GlobalHubConnection.ConnectAsync()
```

## Обновление метаданных чата
```
ChatService → BuildUpdateEvent(chatEntity) → ChatUpdateEventDto
  → HubNotifier.SendToChatAsync("ChatUpdated", dto)
  → GlobalHubConnection → ChatsViewModel.UpdateChatMeta()
                        → ChatViewModel.OnChatUpdated()
```

## Удаление участника из чата
```
ChatMemberService.RemoveMemberAsync()
  → HubNotifier.SendToUserAsync(userId, "ChatRemoved", chatId)
  → GlobalHubConnection.ChatRemoved
  → ChatsViewModel удаляет чат из списка, сбрасывает SelectedChat
```

---

# 20. DESKTOP — VIEWS

## 20.1 Ресурсы и стили
- `App.axaml`: подключает `Icons.axaml`, `Animations.axaml`, `MainStyle.axaml`, `MessageStyles.axaml`
- `MainStyle.axaml`: `ToggleButton.SwitchSmall:checked` — фон `PrimaryBG`
- `MessageStyles.axaml`: стили опросов — классы `PollOptionButton`, `PollOptionBorder`, `PollRadioOuter/Inner`, `PollCheckboxOuter/Tick`, `PollResultContainer`, `PollProgressBarBackground/Bar`, `PollResultText`, `PollPercentage`

## 20.2 Главное окно (`Views/Shell/MainWindow.axaml.cs`)
- Адаптивный режим при ширине ≤800px
- Анимация открытия/закрытия диалогов с защитой от гонок

## 20.3 ChatView (`Views/Chat/ChatView.axaml` + `.cs`)
- `VirtualizingStackPanel CacheLength="2"`
- `VisibilityCheckDelayMs=300`; таймер откладывается если с последнего скролла прошло <200мс
- При подгрузке старых сообщений: сохранение якорного сообщения → корректировка смещения скролла
- Центрирование при программном скролле к сообщению: `ScrollIntoView` → отложенный `TransformToVisual` → `ScrollViewer.Offset`. До трёх повторных попыток если контейнер не готов. При флаге `highlight` — `IsHighlighted` на `AppConstants.HighlightDurationMs` мс
- `ShouldDeferScrollRequest(isExplicitMessageNavigation)`: явная навигация (включая `HasInitialMessageTarget`) не блокируется; обычная инициализация откладывается до восстановления состояния скролла

## 20.4 Информационная панель чата (`Views/Chat/ChatInfoPanel.axaml.cs`)
- Кнопки редактирования/удаления/выхода управляются `CanEditGroupChat`/`CanLeaveChat`
- Секции «Медиа» и «Опросы» скрываются при нулевых счётчиках
- `LastSeen` для контакта и участников через `MultiBinding` с `LastSeenTextConverter`

## 20.5 Сообщения
- Опросы: `PollMessagePart.axaml` с `ItemsControl` + `FractionToGridLengthConverter`
- Системные сообщения: бейдж `SystemMessageTime` справа
- Контекстное меню: пункт «Результаты опроса» управляется видимостью корректно

## 20.6 Кастомные контролы (`Views/Controls/`)

### CircularProgress (`Shared/CircularProgress.cs`)
Круговой прогресс-бар. Determinate (0–Maximum) и indeterminate (анимированная дуга). Свойства: `Value`, `StrokeWidth`, `Foreground`, `BackgroundTrack`. Рисуется через `StreamGeometry`.

### PasswordStrengthControl (`Shared/PasswordStrengthControl.axaml.cs`)
4 сегмента сложности + проверка совпадения паролей. Привязки: `StrengthLabel`, `PasswordsMatch`.

### RichMessageTextBlock (`Shared/RichMessageTextBlock.cs`)
`TextBlock` с распознаванием URL и @упоминаний. Клики: открытие ссылок, `MentionClickCommand`.

### WaveformView (`Shared/WaveformView.cs`)
Визуализация волны из Base64 `Waveform`. `Progress`, перетаскивание → `SeekCommand`. Прореживание пиков под ширину.

### FilterAutocomplete (`Controls/FilterAutocomplete.axaml.cs`)
Выпадающий список с автодополнением. `SearchText`, `Suggestions`, `OverlayLayer`. Поддерживает аватары.

### SearchBox (`Controls/SearchBox.axaml.cs`)
Поле поиска с кнопкой очистки. Событие `SearchFocused`. Метод `FocusInput()`.

## 20.7 Template Selectors (`Views/Chat/`)

| Класс | Логика выбора |
|---|---|
| `MessageBodyTemplateSelector` | `IsDeleted` → удалённое; `HasPoll` → опрос; `ShowVoiceMessage` → голосовое; иначе → текст |
| `MessageContentTemplateSelector` | Для пересланных: `OriginalIsVoiceMessage`, `OriginalHasPoll` |
| `MessagePartSelector` | Аналогично, для встроенного контента |

---

# 21. ИЗВЕСТНЫЕ ПРОБЛЕМЫ

## Баги

| # | Баг | Где воспроизводится | Детали |
|---|---|---|---|
| 1 | UDP-эндпоинты не верифицируются | `CallService.HandleUdpEndpointSignal` | Любой участник звонка может объявить произвольный IP:port, сервер пересылает без проверки |
| 2 | UDP-пакеты не аутентифицированы | `CallService.ProcessUdpPacket` | `fromUserId` берётся из пакета без верификации |
| 3 | Нет шифрования аудио | `CallService.SendAudioToAllPeers` | Opus-данные передаются открытым текстом по UDP |
```