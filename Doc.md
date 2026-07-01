Прошу прощения за недопонимание. Вот полностью актуализированная документация проекта. Я исправил все неточности и добавил информацию, которая была упущена.

---

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
| JWT, токены, авторизация | §1.1, §2.1, §7.2 (JwtSettings), §8.1 |
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

---

## СТРУКТУРА ПРОЕКТА

```
/API
  /Common              — AppDateTime, Result, ValidationHelper, StatusExtensions, UrlHelpers
  /Configuration       — DI, JWT, RateLimit, Swagger, StaticFiles, TurnSettings
  /Controllers         — HTTP контроллеры
  /Data                — EF сущности, DbContext, Migrations, SeedData
  /Hubs                — MessengerHub (SignalR)
  /Mapping             — extension-методы ToDto()
  /Middleware          — ExceptionHandling, MissingFileCleanup
  /Repositories        — Abstractions, Base, Implementations, Projections
  /Services
    /Abstractions      — интерфейсы сервисов
    /Auth              — AuthService, TokenService
    /Base              — BaseService
    /Call              — CallSessionService, CallMixerService, CallRelayService, ChannelExtensions, ParticipantCodec, TurnCredentialService
    /Chat              — ChatService, ChatMemberService, NotificationService, SystemMessageService, SystemMessageFormatter
    /Department        — DepartmentService
    /Messaging         — MessageService, FileService, PollService
    /ReadReceipt       — ReadReceiptService
    /User              — UserService, AdminService
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
  "TurnSettings": {
    "Enabled": false,
    "Host": "",
    "Port": 3478,
    "TlsPort": 5349,
    "SharedSecret": "",
    "CredentialTtlSeconds": 86400
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
| `messaging` | 30 req | 1 мин | по userId или IP (закомментирован) |

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
| `ReplacedByTokenId` | `int?` | FK → RefreshToken (следующий в цепочке) |
| `ReplacedByToken` | `RefreshToken?` | Навигационное свойство на следующий токен |
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
- Индексы: `idx_messages_chatid_createdat`, `idx_messages_chatid_pinnedat` (с фильтром `WHERE pinned_at IS NOT NULL`), `idx_chat_members_last_read_message_id`, `idx_chat_members_user_id`, `idx_messages_reply_to_message_id`, `idx_messages_forwarded_from_message_id`, `idx_messages_target_user_id`, `idx_departments_head_id`, `idx_polls_message_id`, `idx_poll_votes_user_id`, `idx_refresh_tokens_*`, `idx_users_department_id`
- `SystemMessage.InitiatorId` хранится в колонке `sender_id`
- `RefreshToken.ReplacedByTokenId` → FK на `RefreshToken.Id`
- `QuerySplittingBehavior.SplitQuery` глобально
- `MaxBatchSize(100)` для Npgsql

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
- `LastMessageProjection` — данные последнего сообщения (Id, ChatId, CreatedAt, Content, SenderId, SenderName, TargetUserName, флаги IsSystemMessage, IsVoiceMessage, HasPoll, HasFiles, SystemEventType, TargetUserId)
- `DialogPartnerProjection` — партнёр для Contact-чата (UserId, ChatId, ФИО, Avatar, StatusType, StatusExpiresAt, LastOnline)
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
| `CallStateDto` | `CallId`, `ChatId`, `Status`, `InitiatorId`, `StartedAt` (DateTimeOffset), `IsGroupCall`, `Mode`, `Participants`, `ElapsedSeconds` |
| `CallParticipantDto` | `UserId`, `DisplayName`, `AvatarUrl`, `IsMuted`, `IsSpeaking` |
| `RelayEndpointInfo` | `Host`, `Port`, `CallId`, `Turn` (TurnCredentials?) |
| `TurnCredentials` | `Urls`, `Username`, `Credential`, `ExpiresAt` |
| `SignalDto` | `CallId`, `FromUserId`, `TargetUserId`, `Type` (offer/answer/ice-candidate/udp-endpoint), `Payload` |
| `CallChatMessageDto` | `CallId`, `SenderId`, `SenderName`, `SenderAvatar`, `Text`, `SentAt` |

---

## 2.3 Chat (`Shared/Dto/Chat/`)

| DTO | Ключевые поля |
|---|---|
| `ChatDto` | `Id`, `Name`, `Type`, `CreatedById`, `LastMessageDate`, `Avatar`, `LastMessagePreView`, `LastMessageSenderName`, `UnreadCount`, `LastMessageSenderId`, `LastMessageIsSystem`, `LastMessageIsPoll`, `LastMessageIsVoice`, `LastMessageHasFilesOnly`, `CurrentUserRole` (JsonIgnore WhenWritingNull), `HideSenderPrefix` (JsonIgnore), `ShowHistoryForNewMembers`, `ContactUserId`, `ContactIsOnline`, `ContactStatusType`, `ContactStatusExpiresAt` |
| `ChatMemberDto` | `ChatId`, `UserId`, `Role`, `JoinedAt`, `NotificationsEnabled`, `Username`, `DisplayName`, `Avatar` |
| `ChatUpdateEventDto` | `Id`, `Name`, `Type`, `CreatedById`, `Avatar`, `ShowHistoryForNewMembers`, `CurrentUserRole` (JsonIgnore WhenWritingNull, заполняется персонально при смене роли) |
| `UpdateChatDto` | `Id`, `Name?`, `ChatType?`, `ShowHistoryForNewMembers?` |
| `ChatNotificationSettingsDto` | `ChatId`, `NotificationsEnabled` |

---

## 2.4 Message (`Shared/Dto/Message/`)

| DTO | Назначение |
|---|---|
| `CreateMessageRequest` | `ChatId` [Required], `Content` [MaxLength 4000], `ReplyToMessageId?`, `ForwardedFromMessageId?`, `IsVoiceMessage`, `VoiceFileUrl`, `VoiceFileName`, `VoiceContentType`, `VoiceFileSize`, `VoiceDurationSeconds`, `VoiceWaveform`, `Files?` |
| `MessageDto` | Полное представление (33 поля). `IsOwn`, `IsPrevSameSender`, `IsEdited`, `IsDeleted`, `IsPinned`, `IsSystemMessage`, `IsVoiceMessage`. Поля `EditedAt`, `PinnedAt`, `PinnedByUserId`, `ReplyToMessageId`, `ReplyToMessage`, `ForwardedFromMessageId`, `ForwardedFrom`, `SystemEventType`, `TargetUserId`, `TargetUserName`, `VoiceDurationSeconds`, `VoiceWaveform`, `VoiceFileUrl`, `VoiceFileSize`, `Poll`, `Files` — с `JsonIgnore(WhenWritingNull)`. |
| `MessageFileDto` | `Id`, `MessageId`, `FileName`, `ContentType`, `Url`, `PreViewType` (file/image/video/audio), `FileSize` |
| `MessageReplyPreViewDto` | `Id`, `ChatId`, `SenderId?`, `SenderName?`, `Content?`, `CreatedAt`, `IsDeleted`, `IsVoiceMessage`, `HasPoll`, `FilesCount` |
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
- `NotificationDto` — `Type` (message/mention/poll), `ChatId`, `ChatName`, `Avatar`, `MessageId`, `SenderId`, `SenderName`, `SenderAvatar`, `PreView` (до 100 символов), `CreatedAt`

### Online (`Shared/Dto/Online/`)
- `UserStatusDto` — record: `UserId`, `IsOnline`, `LastOnline`, `StatusType`, `StatusExpiresAt`
- `OnlineUsersResponseDto` — `OnlineUserIds`, `TotalOnline`
- `SetStatusRequest` — `StatusType`, `Duration`

### Poll (`Shared/Dto/Poll/`)
- `CreatePollDto` — `ChatId`, `Question`, `IsAnonymous`, `AllowsMultipleAnswers`, `Options`
- `PollDto` — `Id`, `MessageId`, `IsAnonymous`, `AllowsMultipleAnswers`, `ClosesAt`, `Options`, `SelectedOptionIds`, `CanVote`
- `PollOptionDto` — + `VotesCount`, `Votes`
- `PollVoteDto` — `PollId`, `OptionId?`, `OptionIds?`, `UserId`

### ReadReceipt (`Shared/Dto/ReadReceipt/`)
- `MarkAsReadDto` — `ChatId`, `MessageId?`
- `ReadReceiptResponseDto` — `ChatId`, `LastReadMessageId`, `LastReadAt`, `UnreadCount`
- `UnreadCountDto` — record: `ChatId`, `UnreadCount`
- `AllUnreadCountsDto` — `Chats`, `TotalUnread`
- `ChatReadInfoDto` — + `FirstUnreadMessageId`

### Search (`Shared/Dto/Search/`)
- `GlobalSearchMessageDto` — `Id`, `ChatId`, `ChatName`, `ChatAvatar`, `ChatType`, `SenderId`, `SenderName`, `Content`, `CreatedAt`, `HighlightedContent`, `HasFiles`, `HasVoice`, `HasPoll`
- `GlobalSearchResponseDto` — `Chats`, `Messages`, `TotalChatsCount`, `TotalMessagesCount`, `CurrentPage`, `HasMoreMessages`
- `SearchMessagesQueryDto` — `Query`, `Page`, `PageSize`, `SenderId?`, `HasFiles?`, `HasVoice?`, `HasPoll?`, `OnlyText?`, `DateFrom?`, `DateTo?`, `OldestFirst`
- `GlobalSearchQueryDto` : `SearchMessagesQueryDto` — + `FilterChatId?`

### User (`Shared/Dto/User/`)
- `UserDto` — `Id`, `Username`, `DisplayName`, `Name`, `Midname`, `Surname`, `Department`, `DepartmentId`, `Avatar`, `IsOnline`, `IsBanned`, `LastOnline`, `Theme`, `NotificationsEnabled`, `StatusType`, `StatusExpiresAt`, `CanManage` (JsonIgnore, default true)
- `CreateUserDto` — `Username`, `Password`, `Surname`, `Name`, `Midname?`, `DepartmentId?`
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
| POST | `/api/users/{id}/avatar` | IsCurrentUser |
| DELETE | `/api/users/{id}/avatar` | IsCurrentUser |
| PUT | `/api/users/{id}/username` | IsCurrentUser |
| PUT | `/api/users/{id}/password` | IsCurrentUser |
| GET | `/api/users/online` | Authorized |
| GET | `/api/users/{id}/status` | Authorized |
| POST | `/api/users/status/batch` | Authorized |

---

## ChatsController
**Путь:** `API/Controllers/ChatsController.cs`

| Метод | Маршрут | Auth |
|---|---|---|
| GET | `/api/chats/user/{userId}` | Authorized |
| GET | `/api/chats/user/{userId}/dialogs` | Authorized |
| GET | `/api/chats/user/{userId}/groups` | Authorized |
| GET | `/api/chats/user/{userId}/contact/{contactUserId}` | Authorized |
| POST | `/api/chats` | Authorized |
| GET | `/api/chats/{id}` | IsMember |
| PUT | `/api/chats/{id}` | IsAdmin |
| DELETE | `/api/chats/{id}` | IsOwner |
| POST | `/api/chats/{chatId}/avatar` | IsAdmin |
| DELETE | `/api/chats/{chatId}/avatar` | IsAdmin |
| GET | `/api/chats/{chatId}/members` | IsMember |
| GET | `/api/chats/{chatId}/members/detailed` | IsMember |
| POST | `/api/chats/{chatId}/members` | IsAdmin |
| DELETE | `/api/chats/{chatId}/members/{userId}` | IsAdmin или IsCurrentUser |
| PUT | `/api/chats/{chatId}/members/{userId}/role` | IsOwner |

---

## MessagesController
**Путь:** `API/Controllers/MessagesController.cs`
**Rate limit:** `messaging` на Create (закомментирован), `search` на Search

| Метод | Маршрут | Auth |
|---|---|---|
| POST | `/api/messages` | IsMember |
| PUT | `/api/messages/{id}` | IsCurrentUser (sender) |
| DELETE | `/api/messages/{id}` | IsCurrentUser или IsAdmin |
| POST | `/api/messages/{id}/pin` | IsAdmin |
| DELETE | `/api/messages/{id}/pin` | IsAdmin |
| GET | `/api/messages/chat/{chatId}/pinned` | IsMember |
| GET | `/api/messages/chat/{chatId}/latest` | IsMember |
| GET | `/api/messages/chat/{chatId}/around/{messageId}` | IsMember |
| GET | `/api/messages/chat/{chatId}/before/{messageId}` | IsMember |
| GET | `/api/messages/chat/{chatId}/after/{messageId}` | IsMember |
| POST | `/api/messages/chat/{chatId}/search` | IsMember |
| GET | `/api/messages/chat/{chatId}/counts` | IsMember |
| POST | `/api/messages/user/{userId}/search` | IsCurrentUser |

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
| POST | `/api/status` | Authorized |
| GET | `/api/status/current` | Authorized |
| GET | `/api/status/user/{userId}` | Authorized |

---

## NotificationsController
**Путь:** `API/Controllers/NotificationsController.cs`

| Метод | Маршрут | Auth |
|---|---|---|
| GET | `/api/notifications/chat/{chatId}/settings` | Authorized |
| POST | `/api/notifications/chat/mute` | Authorized |
| GET | `/api/notifications/settings` | Authorized |

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

| Метод | Константа HubMethods | Назначение |
|---|---|---|
| `JoinChat(chatId)` | `ChatInvoke.JoinChat` | Подписаться на группу чата |
| `LeaveChat(chatId)` | `ChatInvoke.LeaveChat` | Отписаться от группы чата |
| `MarkAsRead(chatId, messageId)` | `ChatInvoke.MarkAsRead` | Отметить сообщение прочитанным |
| `MarkMessageAsRead(chatId, messageId)` | `ChatInvoke.MarkMessageAsRead` | То же (алиас) |
| `GetUnreadCounts()` | `ChatInvoke.GetUnreadCounts` | Получить счётчики непрочитанных |
| `GetReadInfo(chatId)` | `ChatInvoke.GetReadInfo` | Информация о прочтении в чате |
| `SendTyping(chatId)` | `ChatInvoke.SendTyping` | Индикатор печати |
| `GetOnlineUsersInChat(chatId)` | `ChatInvoke.GetOnlineUsers` | Онлайн-пользователи чата |
| `SetStatus(statusType, duration)` | `ChatInvoke.SetStatus` | Установить статус |

**Звонки:**

| Метод | Константа HubMethods | Назначение |
|---|---|---|
| `InitiateCall(chatId)` | `CallInvoke.InitiateCall` | Начать звонок |
| `JoinCall(callId)` | `CallInvoke.JoinCall` | Присоединиться |
| `LeaveCall(callId)` | `CallInvoke.LeaveCall` | Покинуть |
| `DeclineCall(callId)` | `CallInvoke.DeclineCall` | Отклонить |
| `CancelCall(callId)` | `CallInvoke.CancelCall` | Отменить |
| `SendSignal(dto)` | `CallInvoke.SendSignal` | Передача сигнального сообщения |
| `ToggleMute(callId, isMuted)` | `CallInvoke.ToggleMute` | Мьют |
| `ToggleSpeaking(callId, isSpeaking)` | `CallInvoke.ToggleSpeaking` | Индикатор речи |
| `SendCallMessage(callId, text)` | `CallInvoke.SendCallMessage` | Сообщение в чате звонка |
| `GetCallState(callId)` | `CallInvoke.GetCallState` | Текущее состояние звонка |

### Серверные события (сервер → клиент)

**Чат:**

| Событие | Константа | Данные |
|---|---|---|
| `ReceiveMessageDto` | `Chat.ReceiveMessage` | `MessageDto` |
| `MessageUpdated` | `Chat.MessageUpdated` | `MessageDto` |
| `MessageDeleted` | `Chat.MessageDeleted` | `{ MessageId, ChatId }` |
| `ReceivePollUpdate` | `Chat.PollUpdated` | `PollDto` |
| `MessageRead` | `Chat.MessageRead` | `chatId`, `userId`, `messageId` |
| `UnreadCountUpdated` | `Chat.UnreadCountUpdated` | `chatId`, `unreadCount` |
| `UserOnline` | `Chat.UserOnline` | `userId` |
| `UserOffline` | `Chat.UserOffline` | `userId` |
| `UserStatusChanged` | `Chat.UserStatusChanged` | `UserStatusDto` |
| `UserTyping` | `Chat.UserTyping` | `chatId`, `userId`, `userName` |
| `ChatUpdated` | `Chat.ChatUpdated` | `ChatUpdateEventDto` |
| `ChatRemoved` | `Chat.ChatRemoved` | `chatId` |
| `ReceiveNotification` | `Chat.ReceiveNotification` | `NotificationDto` |
| `UserProfileUpdated` | `Chat.UserProfileUpdated` | — |
| `UserRoleUpdated` | `Chat.UserRoleUpdated` | `UserRole` |
| `UserPermissionsChanged` | `Chat.UserPermissionsChanged` | `UserPermissionsChangedDto` |

**Звонки:**

| Событие | Константа | Данные |
|---|---|---|
| `IncomingCall` | `Call.IncomingCall` | `CallInviteDto` |
| `CallStateUpdated` | `Call.CallStateUpdated` | `CallStateDto` |
| `CallEnded` | `Call.CallEnded` | `callId`, `reason` |
| `CallMessageReceived` | `Call.CallMessageReceived` | `CallChatMessageDto` |
| `CallParticipantJoined` | `Call.CallParticipantJoined` | `CallParticipantDto` |
| `CallParticipantLeft` | `Call.CallParticipantLeft` | `userId` |
| `ParticipantMuteChanged` | `Call.ParticipantMuteChanged` | `userId`, `isMuted` |
| `ParticipantSpeakingChanged` | `Call.ParticipantSpeakingChanged` | `userId`, `isSpeaking` |
| `ActiveCallStarted` | `Call.ActiveCallStarted` | `CallStateDto` |
| `ActiveCallUpdated` | `Call.ActiveCallUpdated` | `CallStateDto` |
| `ActiveCallEnded` | `Call.ActiveCallEnded` | `callId` |
| `CallError` | `Call.CallError` | `message` |
| `RelayEndpoint` | `Call.RelayEndpoint` | `RelayEndpointInfo` |
| `ReceiveSignal` | `Call.ReceiveSignal` | `SignalDto` |

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
| `FileMappings.cs` | `.ToDto()`, `DeterminePreViewType(contentType)` → file/image/video/audio |
| `MessageMappings.cs` | `.ToDto(currentUserId, urlBuilder)` — рекурсивный обход цепочки пересылки, заполняет `OriginalSenderId` |
| `PollMappings.cs` | `Poll.ToDto(currentUserId?)` (SelectedOptionIds, CanVote), `PollOption.ToDto(isAnonymous)` |
| `UserMappings.cs` | `.ToDto(urlBuilder, isOnline?)` — включает `StatusType` и `StatusExpiresAt` |

---

# 7. ИНФРАСТРУКТУРА API

## 7.1 Утилиты (API/Common)

| Класс | Путь | Назначение |
|---|---|---|
| `AppDateTime` | `API/Common/AppDateTime.cs` | Обёртка `TimeProvider`. **Возвращает `DateTimeKind.Unspecified`** |
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

### TurnSettings (`API/Configuration/TurnSettings.cs`)
`Enabled=false`, `Host=""`, `Port=3478`, `TlsPort=5349`, `SharedSecret=""`, `CredentialTtlSeconds=86400`

### RateLimitKey (`API/Configuration/RateLimitKey.cs`)
- `GetIpPartitionKey(context)` — по IP
- `GetUserOrIpPartitionKey(context)` — по userId если авторизован, иначе по IP

### DependencyInjection (`API/Configuration/DependencyInjection.cs`)

| Метод | Что регистрирует |
|---|---|
| `AddMessengerDatabase` | DbContext + PostgreSQL enum mapping через `EnumTypeMappings`, `MaxBatchSize(100)`, `UseQuerySplittingBehavior(SplitQuery)` |
| `AddInfrastructureServices` | `MemoryCache`, `HttpContextAccessor`, `TimeProvider.System` (Singleton), `AppDateTime` (Singleton), `CallMixerService` (Singleton), `CallRelayService` (Singleton + HostedService), `CallSessionService` (Singleton), `OnlineUserService` (Singleton), `TurnCredentialService` (Singleton), репозитории (Scoped), `CacheService`, `AccessControlService`, `FileService`, `TokenService`, `HubNotifier`, `UrlBuilder`, бандлы |
| `AddBundles` | `TimeBundle`, `UrlBundle`, `CacheBundle`, `NotificationBundle`, `MediaBundle`, `PresenceBundle`, `ChatBundle` (все Scoped) |
| `AddBusinessServices` | Все бизнес-сервисы (Scoped) + `StatusCleanupHostedService` |
| `AddMessengerJson` | `ReferenceHandler.IgnoreCycles`, `WriteIndented` в Dev |

---

## 7.3 Бандлы (API/Services/Infrastructure/Bundles)

Группировка зависимостей для упрощения DI в сервисах:

| Бандл | Зависимости |
|---|---|
| `TimeBundle` | `AppDateTime` |
| `UrlBundle` | `IUrlBuilder` |
| `CacheBundle` | `IAccessControlService`, `ICacheService` |
| `NotificationBundle` | `IHubNotifier`, `INotificationService` |
| `MediaBundle` | `IFileService` |
| `PresenceBundle` | `IOnlineUserService` |
| `ChatBundle` | `ISystemMessageService`, `CacheBundle`, `NotificationBundle`, `TimeBundle` |

---

## 7.4 Инфраструктурные сервисы (API/Services/Infrastructure)

### База данных
- `EnumTypeMappings` — трансляторы имён enum для Npgsql (ChatRole, ChatType, UserStatusType, Theme, SystemEventType)
- `EnumNameTranslator` — реализует `INpgsqlNameTranslator`, берёт маппинг из словаря, fallback на snake_case

### Безопасность
- `AccessControlService` (`API/Services/Infrastructure/Security/`) — проверка прав с двойным кэшем (MemoryCache + per-request словарь). `IsSystemAdmin()` даёт bypass для роли Admin. Методы: `GetChatMemberIdsAsync`, `GetChatTypeAsync`, `GetRoleAsync`, `GetChatMemberAsync`, `InvalidateSystemAdminCache`
- `AccessControlExtensions` — `EnsureMemberOfAsync`, `EnsureAdminOfAsync`, `EnsureOwnerOfAsync` → `Result`

### Кэш
- `CacheService` — MemoryCache: user_chats (TTL 5м, sliding 2м), membership (TTL 10м, sliding 3м). Методы инвалидации: `InvalidateUserChats`, `InvalidateMembership`, `InvalidateChat`, `InvalidateChatMembers`

### Статусы
- `OnlineUserService` — Singleton, `ConcurrentDictionary<userId, ConcurrentDictionary<connectionId, byte>>`, очистка пустых записей каждые 5 мин. Методы: `GetConnectionIds`, `OnlineCount`
- `UserStatusService` — обновление статусов через `ExecuteUpdateAsync`, рассылка `UserStatusChanged` через `IHubContext<MessengerHub>`
- `StatusCleanupHostedService` — фоновый сервис, очистка истёкших статусов каждую минуту

### Остальные
- `HubNotifier` — `SendToChatAsync`, `SendToUserAsync` через `IHubContext<MessengerHub>`, глотает исключения
- `HttpUrlBuilder` — абсолютный URL через `IHttpContextAccessor`
- `UdpDiscoveryService` — UDP порт 5275. Запрос: `MESSENGER_DISCOVER`, ответ: `MESSENGER_HERE:PORT` или `MESSENGER_HERE:PORT:IP`

---

## 7.5 Репозитории (API/Repositories)

### Базовый класс
`RepositoryBase<TEntity>` (`API/Repositories/Base/`) — `FindByIdAsync`, `ExistsAsync`, `Add`, `Remove`

### Реализации

| Интерфейс | Реализация | Путь | Особенности |
|---|---|---|---|
| `IUserRepository` | `UserRepository` | `Implementations/UserRepository.cs` | `FindByUsernameAsync`, `FindByIdWithPasswordAsync`, `GetAllWithSettingsAsync`, `GetWithSettingsAsync`, `UsernameExistsByOtherUserAsync` |
| `IRefreshTokenRepository` | `RefreshTokenRepository` | `Implementations/RefreshTokenRepository.cs` | `FindByHashAsync`, `RevokeByFamilyIdAsync`, `RevokeByFamilyIdsAsync`, `RevokeAllForUserAsync`, `DeleteExpiredAsync`, `GetActiveFamiliesAsync` |
| `IChatRepository` | `ChatRepository` | `Implementations/ChatRepository.cs` | `FindContactChatAsync`, `GetDialogPartnersAsync`, `GetHistoryRestrictionsAsync`, `SearchGroupChatsAsync` |
| `IMessageRepository` | `MessageRepository` | `Implementations/MessageRepository.cs` | `GetLatestAsync` выбирает ID → раздельно UserMessages и SystemMessages → сортирует по карте порядка. `GetChatCountsAsync` считает файлы. `LightQuery` загружает `Poll.PollOptions.PollVotes` |
| `IReadReceiptRepository` | `ReadReceiptRepository` | `Implementations/ReadReceiptRepository.cs` | Все операции с отметками о прочтении, `GetUnreadCountsForUsersAsync` |
| `IPollRepository` | `PollRepository` | `Implementations/PollRepository.cs` | Управление опросами и голосами, `CloseAsync` |

---

# 8. СЛОЙ СЕРВИСОВ (API/Services)

## 8.1 BaseService
**Путь:** `API/Services/Base/BaseService.cs`
Обрабатывает `DbUpdateException`: Concurrency → Conflict, UniqueViolation → Conflict (проверка по "duplicate"/"unique"/"23505"), остальные → Internal.
Методы: `FindEntityAsync`, `Paginate`, `NormalizePagination`

---

## 8.2 AuthService + TokenService
**Путь:** `API/Services/Auth/`

**AuthService (partial class с source-generated логгерами):**
- Конструктор принимает `TimeBundle`, `IUserRepository`, `IRefreshTokenRepository`
- `LoginAsync` — timing-safe проверка пароля (dummy hash при отсутствии пользователя), проверка бана, определение **флаговой** роли (Admin если `DepartmentId == AdminDepartmentId`, Head если руководит любым отделом, иначе User), ограничение сессий (MaxActiveSessions=5, отзыв старых семей через `RevokeByFamilyIdsAsync`)
- `RefreshTokenAsync` — проверка JTI, обнаружение повторного использования токена (UsedAt/RevokedAt → отзыв всей семьи), ротация с сохранением `FamilyId`, `ReplacedByToken` связь
- `SaveRefreshTokenAsync` — `EnforceSessionLimitAsync` + `CleanupExpiredTokensAsync` (удаление токенов старше 60 дней)

**TokenService:**
- JWT: HMAC-SHA256, ClockSkew=Zero
- Refresh-токен: 64 байта Base64
- `GenerateTokenPair`, `ValidateToken`, `GetPrincipalFromExpiredToken`, `HashToken`

---

## 8.3 Бизнес-сервисы

### Auth (`API/Services/Auth/`)
| Сервис | Строк | Ключевое поведение |
|---|---|---|
| `AuthService` | ~207 | Флаговые роли, лимит 5 сессий, очистка истёкших токенов, source-generated логгеры |
| `TokenService` | ~136 | JWT HMAC-SHA256, ClockSkew=Zero, рефреш 64 байта Base64 |

### Call (`API/Services/Call/`)
| Сервис | Строк | Ключевое поведение |
|---|---|---|
| `CallSessionService` | ~158 | Singleton, `ConcurrentDictionary`. `CreateCallAsync` определяет Mode: Contact → PeerToPeer, иначе ServerMixed. Индексация `_chatCallIndex`. `ToStateDto` с `ElapsedSeconds` |
| `CallMixerService` | ~266 | Микширование Opus-потоков на сервере для ServerMixed |
| `CallRelayService` | ~126 | UDP relay для групповых звонков (порт 5276), получает аудио, отправляет в микшер, рассылает смешанный поток |
| `TurnCredentialService` | ~52 | Генерация временных TURN-credentials по RFC 8489 |

### Chat (`API/Services/Chat/`)
| Сервис | Строк | Ключевое поведение |
|---|---|---|
| `ChatService` | ~486 | Принимает `ChatBundle`, `MediaBundle`, `PresenceBundle`, `UrlBundle`, `IHubContext<MessengerHub>`. `GetUserChatsAsync` — загрузка с `ChatWithLastMessage`, `BuildLastMessagePreView` (опросы/голосовые/файлы). `CreateChatAsync` — транзакция, валидация Contact через `FindContactChatAsync`, авто-добавление в SignalR группы. `UpdateChatAsync` — проверка Owner для смены типа. `DeleteChatAsync` — удаление голосовых файлов, инвалидация кэша. `UploadChatAvatarAsync` / `RemoveChatAvatarAsync` |
| `ChatMemberService` | ~136 | Принимает `ChatBundle`, `IOnlineUserService`, `IHubContext<MessengerHub>`. `AddMemberAsync` — отправка `ChatUpdated` с `CurrentUserRole` новому участнику. `RemoveMemberAsync` — запрет удаления Owner, отправка `ChatRemoved`. `UpdateRoleAsync` — персональная отправка `ChatUpdated` с новой ролью. `LeaveAsync` |
| `NotificationService` | ~108 | Принимает `IHubNotifier`, `IUrlBuilder`. `GetChatNotificationSettingsAsync`, `SetChatMuteAsync`, `GetAllChatSettingsAsync`. Для Contact-чата: `ChatName` = имя отправителя, `ChatAvatar` = аватар отправителя. PreView ≤100 символов |
| `SystemMessageService` | ~43 | Принимает `IHubNotifier`, `IUrlBuilder`, `AppDateTime`. Создаёт `SystemMessage`, игнорирует Contact-чаты. `CreateCallEndedMessageAsync` форматирует длительность |
| `SystemMessageFormatter` | ~12 | Статический форматтер, делегирует `SystemEventMeta.Format` |

### Department (`API/Services/Department/`)
| Сервис | Строк | Ключевое поведение |
|---|---|---|
| `DepartmentService` | ~434 | Принимает `AppDateTime`, `IHubNotifier`, `IOnlineUserService`, `IHubContext<MessengerHub>`, `ICacheService`. `CreateDepartmentAsync` — создаёт связанный чат "Отдел {Name}", синхронизация членства начальника. `UpdateDepartmentAsync` — BFS проверка циклов, смена начальника с переносом между отделами, обновление имени чата. `DeleteDepartmentAsync` — запрет при наличии дочерних отделов/сотрудников. `AddUserToDepartmentAsync` / `RemoveUserFromDepartmentAsync` с синхронизацией чатов. `SyncDepartmentChatMembershipAsync` — публичный метод. Автоматическая синхронизация чата начальников (`SystemSettings["heads_chat_id"]`). `NotifyUserRoleAsync` — пересчёт флаговой роли |

### Messaging (`API/Services/Messaging/`)
| Сервис | Строк | Ключевое поведение |
|---|---|---|
| `MessageService` | ~523 | partial class с `[GeneratedRegex]` для @упоминаний. Принимает бандлы: `ChatBundle`, `MediaBundle`, `UrlBundle`, `IMemoryCache`. `CreateMessageAsync` — `ResolveRootForwardedMessageIdAsync` (обход цепочки пересылки до корня), валидация "сообщение должно содержать текст или файлы", копирование Content при пересылке без текста. `GetMessagesAroundAsync` — загрузка before+after+anchor, группировка по Id, построение окна вокруг якоря. `GetLatestMessagesAsync` — кэширование на 1 сек. `GetHistoryCutoffAsync` — ограничение истории для новых участников (проверка `ShowHistoryForNewMembers`). `PinMessageAsync`/`UnpinMessageAsync` — создают SystemMessage. `UpdateMessageAsync` — запрет редактирования пересланных/голосовых/с опросами. `GlobalSearchAsync` — поиск чатов + сообщений, `BuildGlobalHistoryFilterAsync`. `NotifyAndUpdateUnreadAsync` — извлечение @упоминаний, выборочная отправка mention-уведомлений |
| `FileService` | ~105 | Принимает `IAccessControlService`, `IWebHostEnvironment`, `IUrlBuilder`. `SaveImageAsync` — конвертация в WebP (SixLabors.ImageSharp), ресайз до `MaxImageDimension`. `SaveMessageFileAsync` — проверка прав через `EnsureMemberOfAsync`, лимит `MaxFileSizeBytes`. `DeleteFile` — удаление с диска |
| `PollService` | ~155 | Принимает `IPollRepository`, `IMessageRepository`, `IAccessControlService`, `IHubNotifier`, `IUrlBuilder`, `TimeBundle`. `CreatePollAsync` — транзакция, создание UserMessage + Poll + PollOptions. `VoteAsync` — удаление старых голосов, проверка `ClosesAt`. `ClosePollAsync` — проверка прав (автор/админ/владелец), `BroadcastPollUpdateAsync` обновляет опрос во всех чатах (включая пересылки) |

### ReadReceipt (`API/Services/ReadReceipt/`)
| Сервис | Строк | Ключевое поведение |
|---|---|---|
| `ReadReceiptService` | ~114 | Принимает `IReadReceiptRepository`, `TimeBundle`. `MarkAsReadAsync` — `DetermineTargetMessageIdAsync` (messageId или последнее). `MarkMessageAsReadAsync`. `GetChatReadInfoAsync` — с `FirstUnreadMessageId`. `GetAllUnreadCountsAsync` — с `TotalUnread`. `GetUnreadCountsForChatsAsync`, `GetUnreadCountsForUsersInChatAsync`, `MarkAllAsReadAsync` |

### User (`API/Services/User/`)
| Сервис | Строк | Ключевое поведение |
|---|---|---|
| `AdminService` | ~235 | Принимает `TimeBundle`, `IUserRepository`, `IRefreshTokenRepository`, `IHubContext<MessengerHub>`, `IDepartmentService`. `CreateUserAsync` — синхронизация с отделом. `UpdateUserAsync` — отслеживание изменения роли (был/стал Head/Admin), отправка `UserPermissionsChanged`/`UserRoleUpdated`. `ToggleBanAsync` — отзыв токенов при бане. `ResetPasswordAsync` — отзыв токенов. Дебаунс уведомлений 3 сек через `ConcurrentDictionary` |
| `UserService` | ~189 | Принимает `MediaBundle`, `PresenceBundle`, `UrlBundle`, `IUserRepository`. `GetAllUsersAsync`/`GetUserAsync` — маппинг проекций с онлайн-статусом. `UploadAvatarAsync`/`RemoveAvatarAsync`. `GetOnlineUsersAsync`/`GetOnlineStatusAsync`/`GetOnlineStatusesAsync`. `ChangeUsernameAsync` — проверка уникальности. `ChangePasswordAsync` — проверка текущего пароля |

---

# 9. АБСТРАКЦИИ (интерфейсы)

## API сервисы (`API/Services/Abstractions/`)

| Интерфейс | Ключевые методы |
|---|---|
| `IAuthService` | `LoginAsync → Result<AuthLoginResult>`, `RefreshTokenAsync → Result<AuthRefreshResult>`, `RevokeRefreshTokenAsync` |
| `ITokenService` | `GenerateTokenPair`, `ValidateToken`, `GetPrincipalFromExpiredToken`, `HashToken` |
| `ICallSessionService` | `CreateCallAsync`, `JoinCall`, `LeaveCall`, `EndCallAsync`, `ToStateDto`, `GetCall`, `GetActiveCallInChat`, `SetMuted`, `SetSpeaking` |
| `IChatService` | `GetUserChatsAsync`, `GetChatForUserAsync`, `GetUserDialogsAsync`, `GetUserGroupsAsync`, `GetContactChatAsync`, `CreateChatAsync`, `UpdateChatAsync`, `DeleteChatAsync`, `UploadChatAvatarAsync`, `RemoveChatAvatarAsync` |
| `IChatMemberService` | `AddMemberAsync`, `RemoveMemberAsync`, `UpdateRoleAsync`, `GetMembersAsync`, `LeaveAsync` |
| `IMessageService` | `CreateMessageAsync`, `GetLatestMessagesAsync`, `GetMessagesAroundAsync`, `GetMessagesBeforeAsync`, `GetMessagesAfterAsync`, `SearchMessagesAsync`, `GlobalSearchAsync`, `PinMessageAsync`, `UnpinMessageAsync`, `GetPinnedMessagesAsync`, `GetChatCountsAsync` |
| `IPollService` | `CreatePollAsync`, `VoteAsync`, `ClosePollAsync`, `GetPollAsync` |
| `IReadReceiptService` | `MarkAsReadAsync`, `MarkMessageAsReadAsync`, `GetUnreadCountAsync`, `GetAllUnreadCountsAsync`, `GetChatReadInfoAsync`, `MarkAllAsReadAsync`, `GetUnreadCountsForChatsAsync`, `GetUnreadCountsForUsersInChatAsync` |
| `IDepartmentService` | `GetDepartmentsAsync`, `GetDepartmentAsync`, `CreateDepartmentAsync`, `UpdateDepartmentAsync`, `DeleteDepartmentAsync`, `AddUserToDepartmentAsync`, `RemoveUserFromDepartmentAsync`, `CanManageDepartmentAsync`, `SyncDepartmentChatMembershipAsync`, `GetDepartmentMembersAsync` |
| `IFileService` | `SaveImageAsync`, `SaveMessageFileAsync`, `DeleteFile`, `IsValidImage` |
| `IUserService` | `GetAllUsersAsync`, `GetUserAsync`, `UpdateUserAsync`, `UploadAvatarAsync`, `RemoveAvatarAsync`, `GetOnlineUsersAsync`, `GetOnlineStatusAsync`, `GetOnlineStatusesAsync`, `ChangeUsernameAsync`, `ChangePasswordAsync` |
| `IAdminService` | `GetUsersAsync`, `CreateUserAsync`, `UpdateUserAsync`, `ToggleBanAsync`, `ResetPasswordAsync` |
| `INotificationService` | `SendNotificationAsync`, `SendMentionNotificationAsync`, `GetChatNotificationSettingsAsync`, `SetChatMuteAsync`, `GetAllChatSettingsAsync` |
| `ISystemMessageService` | `CreateAsync`, `CreateCallStartedMessageAsync`, `CreateCallEndedMessageAsync` |
| `IUserStatusService` | `SetStatusAsync`, `GetStatusAsync`, `CleanupExpiredStatusesAsync` |
| `IOnlineUserService` | `UserConnected`, `UserDisconnected`, `IsOnline`, `GetOnlineUserIds`, `FilterOnline`, `GetConnectionIds`, `OnlineCount` |
| `IHubNotifier` | `SendToChatAsync(chatId, method, arg)`, `SendToUserAsync(userId, method, arg1, arg2)` |
| `ICacheService` | `GetUserChatIdsAsync`, `GetMembershipAsync`, `InvalidateUserChats`, `InvalidateMembership`, `InvalidateChat`, `InvalidateChatMembers` |
| `IAccessControlService` | `IsMemberAsync`, `IsAdminAsync`, `IsOwnerAsync`, `GetRoleAsync`, `GetChatMemberAsync`, `GetChatMemberIdsAsync`, `GetChatTypeAsync`, `GetUserChatIdsAsync`, `InvalidateSystemAdminCache` |
| `IUrlBuilder` | `BuildUrl(string?)` |

## Репозитории (`API/Repositories/Abstractions/`)

| Интерфейс | Ключевые методы |
|---|---|
| `IUserRepository` | `FindByUsernameAsync`, `FindByIdAsync`, `FindByIdWithPasswordAsync`, `UsernameExistsAsync`, `UsernameExistsByOtherUserAsync`, `Add`, `GetAllWithSettingsAsync`, `GetWithSettingsAsync` |
| `IRefreshTokenRepository` | `FindByHashAsync`, `RevokeByFamilyIdAsync`, `RevokeByFamilyIdsAsync`, `RevokeAllForUserAsync`, `DeleteExpiredAsync`, `GetActiveFamiliesAsync` |
| `IChatRepository` | `FindByIdAsync`, `FindByIdWithMembersAsync`, `FindContactChatAsync`, `GetByIdsAsync`, `GetLastMessagesAsync`, `GetDialogPartnersAsync`, `UpdateLastMessageTimeAsync`, `GetMembersWithUsersAsync`, `GetVoiceFilePathsAsync`, `GetChatTypeAsync`, `GetShowHistoryForNewMembersAsync`, `GetContactChatsWithMembersAsync`, `SearchGroupChatsAsync`, `GetMembersForNotificationAsync`, `GetHistoryRestrictionsAsync`, `Add`, `AddMember`, `RemoveMember` |
| `IMessageRepository` | `FindUserMessageByIdAsync`, `FindUserMessageWithIncludesAsync`, `FindUserMessageForDeleteAsync`, `FindForBroadcastAsync`, `GetWithIncludesAsync`, `GetBeforeAsync`, `GetAfterAsync`, `GetUserMessagesForMixedAsync`, `GetSystemMessagesAsync`, `GetPinnedAsync`, `CountAsync`, `HasOlderAsync`, `HasNewerAsync`, `ExistsInChatAsync`, `ExistsAsync`, `SearchInChatAsync`, `SearchGlobalAsync`, `GetForwardedToChatIdsAsync`, `SoftDeleteAsync`, `PinAsync`, `UnpinAsync`, `Add`, `RemoveVoiceMessage`, `FindUserMessageWithIncludesNoTrackingAsync`, `GetLatestAsync`, `GetChatCountsAsync` |
| `IReadReceiptRepository` | `FindMemberAsync`, `FindMemberReadonlyAsync`, `UpdateReadPointerAsync`, `CountUnreadAsync`, `GetUnreadInfoAsync`, `GetAllUnreadCountsAsync`, `GetUnreadCountsAsync`, `GetUnreadCountsForUsersAsync`, `MessageExistsAsync`, `GetLastMessageIdAsync` |
| `IPollRepository` | `FindByIdWithDetailsAsync`, `Add(Poll)`, `AddOption`, `AddVote`, `GetUserVotesAsync`, `RemoveVotes`, `CloseAsync` |

---

# 10. ПЕРЕЧИСЛЕНИЯ (Shared/Enum)

| Enum | Значения | Примечание |
|---|---|---|
| `CallEndReason` | Ended, Cancelled, Timeout, Declined | |
| `CallStatus` | Ringing, Active, Ended | |
| `CallMode` | PeerToPeer, ServerMixed | Определяет режим передачи аудио |
| `ChatRole` | Member, Admin, Owner | |
| `ChatType` | Chat, Department, Contact, DepartmentHeads | `EnumMember`: `"chat"`, `"department"`, `"contact"`, `"department_heads"` |
| `SystemEventType` | ChatCreated, MemberAdded, MemberRemoved, MemberLeft, RoleChanged, CallStarted, CallEnded, MessagePinned, MessageUnpinned, ChatAvatarUpdated | `[JsonStringEnumConverter]` |
| `Theme` | light, dark, system | `[JsonStringEnumConverter]` |
| `UserRole` | User=0, Head=1, Admin=2 | `[Flags]`, `[JsonStringEnumConverter]` |
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
| `ChatPreViewFormatter` | `BuildPreView`, `BuildReplyPreView`, `Pluralize` (публичный), делегирует системные сообщения `SystemEventMeta` |
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

## Звонки (`Desktop/Services/Features/Call/`)

### Архитектура звонков
Звонки построены на **WebRTC** (библиотека SIPSorcery) для peer-to-peer аудио с резервным использованием **TURN-сервера** для обхода NAT. Для групповых звонков используется серверный микшер (`ServerMixed` режим). SignalR используется только для сигнализации (offer/answer/ICE). Данные аудио передаются через WebRTC DataChannel "audio", закодированные в Opus.

| Сервис | Назначение |
|---|---|
| `CallService` | Оркестратор WebRTC-звонков. При старте/принятии звонка создаёт `WebRtcManager`. Поддерживает TURN через `IceServerConfig` (`_pendingIceConfig`). Аудио через `ICallAudioService`. Определяет, кто инициирует WebRTC-соединение: если `myUserId > peerId`, создаётся offer. События: `CallStarted`, `CallEnded`, `MuteChanged`, `ParticipantSpeakingChanged` |
| `CallAudioService` | Работа с PortAudio через OpenAL (48kHz/моно/20ms кадры). Кодирование/декодирование Opus (Concentus): 32kbps, VBR, VOIP-режим, complexity=2. VAD с адаптивным порогом (noiseFloor α=0.005, порог = max(0.008, noiseFloor×2.5), hold 1200ms, debounce 150ms). Шумоподавление через `NoiseReducer`. Два режима микширования: PeerToPeer (миксует все `_playbackQueues`) и ServerMixed (один поток из `_serverMixedPlaybackQueue`). Принимает Opus-пакеты через `ReceiveEncodedAudio` и `ReceiveMixedAudio` |
| `WebRtcManager` | Управление WebRTC peer connections. `ConcurrentDictionary<int, WebRtcPeerConnection>`. Методы: `InitiateAsync(peerId)` — создаёт offer, `HandleSignalAsync(SignalDto)` — обрабатывает offer/answer/ice, `SendAudioToAll(opusData)` — рассылает аудио через DataChannel, `RemovePeerAsync`. События: `PeerReady`, `AudioReceived`, `SignalingReady` |
| `WebRtcPeerConnection` | Обёртка над `RTCPeerConnection` (SIPSorcery.Net). Создаёт DataChannel "audio" (неупорядоченный, без ретрансмитов). `IceServerConfig`: приоритет TURN-credentials, fallback на Google STUN. ICE gathering с таймаутом 8 сек. События: `Ready`, `AudioReceived`, `SignalingMessageReady`. **Платформенная адаптация:** `#if !ANDROID` — SIPSorcery.Net; `#else` — заглушка |
| `IceServerConfig` | Конфигурация ICE-серверов: `StunUrls`, `Turn` (с `Urls`, `Username`, `Credential`, `ExpiresAt`), `TurnOnly`. Статический метод `FromRelayEndpoint(RelayEndpointInfo)`: если есть TURN-credentials — использует их, иначе Google STUN. Проверяет `ExpiresAt` для TURN |
| `NoiseReducer` | FFT → Wiener Filter → Gate. Decision-Directed SNR α=0.96 |
| `CallHubConnection` | SignalR `/chatHub`, 12 событий + 10 методов. Retry при 503. Автопереподключение. `SafeInvokeAsync` с проверкой `IsConnected`. События: `IncomingCall`, `CallStateUpdated`, `CallEnded`, `SignalReceived`, `RelayEndpoint`, `ActiveCallStarted/Updated/Ended`, `ParticipantJoined/Left/MuteChanged/SpeakingChanged`, `CallMessageReceived`, `CallError` |
| `ActiveCallStore` | ObservableObject: `ActiveCall`, `IsCallUiOpen`, `IsInCall` |

### Поток звонка (WebRTC)

```
CallService.StartCallAsync(chatId)
  → CallAudioService.Start()
  → CallHubConnection.InitiateCallAsync(chatId)
  → сервер: CallSessionService.CreateCallAsync()
            → рассылка IncomingCall + RelayEndpoint

Принятие → CallService.JoinCallAsync(callId, chatId)
  → WebRtcManager() — с _pendingIceConfig если был получен ранее
  → CallAudioService.Start()
  → CallHubConnection.JoinCallAsync(callId)

WebRTC установка соединения:
  → OnCallStateUpdated: для каждого участника где myUserId > peerId → InitiateAsync
  → OnParticipantJoined: если myId > participant.UserId → InitiateAsync
  → WebRtcManager.InitiateAsync: создаёт RTCPeerConnection + DataChannel "audio"
  → offer → SendSignalAsync("webrtc-offer")
  → ответный answer → SendSignalAsync("webrtc-answer")
  → ICE candidates → SendSignalAsync("webrtc-ice")

Аудио:
  → CallAudioService.OnEncodedFrame → OnEncodedFrame
  → WebRtcManager.SendAudioToAll(opusData) — отправка через DataChannel
  → WebRtcManager.AudioReceived → OnWebRtcAudioReceived
  → CallAudioService.ReceiveEncodedAudio(peerId, opusData)

Завершение:
  → LeaveCallAsync / CancelCallAsync / OnCallEndedRemotely
  → WebRtcManager.DisposeAsync() — закрытие всех peer connections
  → CallAudioService.Stop()
```

### Поток звонка (ServerMixed UDP)

```
При инициализации группового звонка:
  → сервер устанавливает CallSession.Mode = ServerMixed
  → вызывает CallRelayService.RegisterCall + AddParticipant
  → отправляет инициатору RelayEndpoint (RelayEndpointInfo)
  → CallService.OnRelayEndpointReceived → _pendingIceConfig если TURN != null

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
  Клиент: ProcessUdpPacket → CallAudioService.ReceiveMixedAudio(opusData)
```

---

## Медиа (`Desktop/Services/Features/Media/`)

| Сервис | Назначение |
|---|---|
| `AudioPlayerService` | WAV через PortAudio. Play/Pause/Resume/Stop/Seek |
| `AudioRecorderService` | Запись голосовых сообщений: 16kHz/моно/16-bit PCM, WAV + Waveform (100 баров). Реализация через `IAudioCaptureDevice` с событием `SamplesAvailable` |
| `FileDownloadService` | Скачивание + прогресс, открытие через OS |
| `FileDownloadStateService` | Состояние скачанных файлов, взаимодействует с `IDownloadedFileRepository` |

### Инфраструктура аудио

| Класс | Назначение |
|---|---|
| `PortAudioLifetime` | Singleton, владеет `PortAudio.Initialize()` / `Terminate()`. `EnsureInitialized()`, `IsAvailable` |
| `WavData` | Загружает 16-битный PCM WAV из потока. `short[] Samples`, `Duration`, `SampleRate` |
| `AudioRecordingState` | Enum: `Idle`, `Recording`, `Sending`, `Error` |

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
| `ICallHubConnection` | Методы: `ConnectAsync`, `DisconnectAsync`, `InitiateCallAsync(chatId)`, `JoinCallAsync(callId)`, `LeaveCallAsync(callId)`, `DeclineCallAsync(callId)`, `CancelCallAsync(callId)`, `SendSignalAsync(SignalDto)`, `SendCallMessageAsync(callId, text)`, `ToggleMuteAsync(callId, isMuted)`, `ToggleSpeakingAsync(callId, isSpeaking)`, `GetCallStateAsync(chatId) → CallStateDto?`. События: `IncomingCall`, `CallStateUpdated`, `CallEnded`, `SignalReceived`, `CallParticipantJoined`, `CallParticipantLeft`, `ParticipantMuteChanged`, `ParticipantSpeakingChanged`, `ActiveCallStarted`, `ActiveCallUpdated`, `ActiveCallEnded`, `CallError`, `CallMessageReceived`, `RelayEndpoint`. Свойство: `IsConnected` |
| `ICallService` | `StartCallAsync(chatId)`, `JoinCallAsync(callId, chatId)`, `LeaveCallAsync()`, `DeclineCallAsync(callId)`, `CancelCallAsync()`, `ToggleMuteAsync()`. События: `CallStarted`, `CallEnded`, `MuteChanged(bool)`, `ParticipantSpeakingChanged(int userId, bool isSpeaking)`. Свойства: `IsInCall`, `IsMuted`, `ActiveCallId`, `ActiveChatId` |
| `ICallAudioService` | `Start()`, `Stop()`, `SetMuted(bool)`, `SetMode(CallMode)`, `AddParticipant(int userId)`, `RemoveParticipant(int userId)`, `ReceiveEncodedAudio(int fromUserId, byte[] opusData, int length)`, `ReceiveMixedAudio(byte[] opusData)`. События: `OnEncodedFrame(Action<byte[], int>)`, `SpeakingStateChanged(Action<bool>)`. Свойства: `IsRunning`, `IsMuted`, `HasParticipant(int)`, `NoiseSuppressionEnabled` |
| `IAudioCaptureDevice` | `StartAsync(int sampleRate, CancellationToken ct)`, `StopAsync()`, `IsAvailable`, событие `SamplesAvailable(short[])` |
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
| `ChatListViewModelFactory` | Фабрика `ChatListViewModel` |

---

# 18. DESKTOP — View MODELS

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

### ChatViewModel (`Core/ChatViewModel.cs`)
**Основной координатор чата.** Содержит публичные свойства-обработчики, вынесенные в отдельные классы для управления конкретными аспектами чата. Свойства пробрасываются автоматически через `ChatPropertyRelay`, который подписывается на `PropertyChanged` дочерних компонентов и вызывает `OnPropertyChanged` на `ChatViewModel`.

**Обработчики:**
- **`Composer` (`ChatComposer`)** — логика поля ввода: текст, эмодзи, упоминания (@username), отправка сообщений
- **`Scroll` (`ChatScrollCoordinator`)** — координация скролла и отметки прочитанными
- **`Permissions` (`ChatPermissionsManager`)** — расчёт прав: `CanEditGroupChat`, `CanLeaveChat`, `CanLeaveChatVisible`
- **`Sections` (`ChatSectionManager`)** — счётчики и загрузка контента для секций инфо-панели (фото, файлы, опросы)
- **`Pinned` (`ChatPinnedHandler`)** — управление закреплёнными сообщениями, баннер с превью
- **`Call` (`ChatCallHandler`)** — состояние активного звонка в чате, баннер, команда `StartOrJoinCall`
- Остальные: `MessageManager`, `Attachments`, `MemberLoader`, `EditDelete`, `Reply`, `Forward`, `Typing`, `Voice`, `InfoPanel`, `Search`, `Notification`

**Ключевые вычисляемые свойства:**
- `HasContactInfoMediaSection` / `HasGroupInfoMediaSection` — видимость секций в инфо-панели
- `MemberCountText` — строка с количеством участников и онлайн-статусом
- `ShowSendMessageButton`, `ShowVoiceMessageButton`, `CanSendMessageNow` — управление кнопками отправки (делегировано в `Composer`)
- `ShowScrollToBottom` — кнопка прокрутки вниз

### ChatContext (`Context/ChatContext.cs`)
Центральный объект с зависимостями. Хранит `CurrentUserRole`, `IsSystemAdmin`, `Members`, `Chat`. Предоставляет события скролла и `RequestRefreshCounters`.

### ChatCommands (`Commands/ChatCommands.cs`)
Mutable-контейнер команд (Edit, Copy, Delete, TogglePin, Reply, Forward, ShowPollResults), разделяемый между `MessageViewModel`.

### ChatFeatureHandler (`Shared/ChatFeatureHandler.cs`)
Базовый класс для обработчиков фич с виртуальным `DisposeManaged`.

### ChatPropertyRelay (`Relay/ChatPropertyRelay.cs`)
Автоматическое пробрасывание свойств от дочерних компонентов к `ChatViewModel`. Поддерживает:
- `Forward(source, mappings)` — проброс скалярных свойств (подписка на `PropertyChanged`)
- `ForwardCollection(collection, callback)` — реакция на `CollectionChanged`

### ChatComposer (`Composer/ChatComposer.cs`)
Управление текстовым вводом и отправкой сообщений. Обрабатывает:
- Состояния отправки (текст, вложения, пересылка)
- Упоминания (`@username`): анализ текста, показ подсказок, навигация по списку (стрелки, Enter, Escape)
- Вставку эмодзи
- Отправку с прикреплёнными файлами

### ChatScrollCoordinator (`Scroll/ChatScrollCoordinator.cs`)
Координация скролла и прочтения. Обрабатывает:
- Отметку сообщений прочитанными при скролле (`OnMessageVisibleAsync`)
- Пакетную отметку чата прочитанным (`MarkMessagesAsReadAsync`) с учётом кулдауна
- Сброс счётчика непрочитанных

### ChatPermissionsManager (`Permissions/ChatPermissionsManager.cs`)
Расчёт прав для чата. Обновляет `CanEditGroupChat`, `CanLeaveChat`, `CanLeaveChatVisible`. Учитывает тип чата (контакт/группа/отдел), роль участника и системную роль администратора.

### ChatSectionManager (`Sections/ChatSectionManager.cs`)
Загрузка и обновление контента секций информационной панели:
- Фото (`PhotosItems`, `PhotosCount`, `HasPhotos`)
- Файлы (`FilesItems`, `FilesCount`, `HasFiles`)
- Опросы (`PollMessages`, `PollsCount`, `HasPolls`)
- Автоматическое обновление счётчиков через API при открытии секции
- `RefreshCountsAsync` для обновления всех счётчиков

### ChatPinnedHandler (`Features/Pinned/ChatPinnedHandler.cs`)
Управление закреплёнными сообщениями:
- Загрузка при инициализации (`LoadInitialAsync`)
- Обновление при изменении состояния пина
- `PinnedBannerMessage` — отображаемое в банере сообщение
- `PinnedBannerPreViewText` — формат "Имя: превью"
- `PinnedMessages` — коллекция для отображения в секции

### ChatCallHandler (`Features/Call/ChatCallHandler.cs`)
Логика звонков в интерфейсе чата:
- Подписка на события `ICallHubConnection` и `ICallService`
- Свойства: `HasActiveCall`, `IsInActiveCall`, `ActiveCallParticipantsCount`, `ActiveCallBannerText`
- Команда `StartOrJoinCallAsync`: если уже в звонке этого чата — открывает UI; иначе выходит из текущего, присоединяется к активному или начинает новый
- `InitAsync(ct)` — проверка состояния звонка при открытии чата

### ChatMessageManager (`Managers/ChatMessageManager.cs`)
Загрузка и управление сообщениями:
- Кэширование в SQLite + загрузка с сервера
- `LoadAroundCoreAsync` — смешанная загрузка до и после
- При получении новых сообщений вызывает `RequestRefreshCounters`

### ChatHubSubscriber (`Core/ChatHubSubscriber.cs`)
Подписки на SignalR события для чата.

### MessageViewModel (`Messages/MessageViewModel.cs`)
Представление сообщения: файлы, голос, опросы. `SystemMessageTime` для системных сообщений. Создаёт `PollViewModel`.

### Вспомогательные модели
- `LocalFileAttachment` (`Managers/`) — локальное вложение: `MemoryStream`, `Thumbnail`, форматированный размер
- `MessageGroupPosition` (`Messages/`) — enum `Alone, First, Middle, Last` для радиуса пузырей
- `ChatInfoPanelItems` (`Context/`) — `ChatInfoPanelMediaItem`, `ChatInfoPanelFileItem`

---

## Call ViewModel (`Desktop/ViewModels/Call/CallViewModel.cs`)

### CallViewModel
Реализует `IActiveCall`. Управляет UI звонка.

**Свойства:** `CallId`, `ChatId`, `ChatName`, `IsMuted`, `IsGroupCall`, `DurationText` (таймер каждую секунду), `IsChatPanelOpen`, `SidePanelMode` (`CallSidePanelMode` — Chat или Participants), `MessageInput`, `UnreadChatCount`, `NoiseSuppressionEnabled` (прокси на `ICallAudioService`).

**Коллекции:**
- `Participants` (`ObservableCollection<CallParticipantViewModel>`) — адаптивная сетка (1/2/3 колонки в зависимости от числа участников)
- `ChatMessages` (`ObservableCollection<CallChatMessageViewModel>`)

**Команды:** `ToggleMute`, `LeaveCall`, `CloseUi`, `ToggleChatPanel`, `OpenParticipantsPanel`, `SendChatMessage`, `InsertEmoji`

**События:**
- Hub: `CallParticipantJoined`, `CallParticipantLeft`, `ParticipantMuteChanged`, `CallEnded`, `ActiveCallUpdated`, `CallMessageReceived`
- Сервис: `ParticipantSpeakingChanged`, `MuteChanged`, `SpeakingStateChanged`

**Логика:**
- При сворачивании чата накапливает `UnreadChatCount`
- Адаптивный размер аватара в зависимости от числа участников
- Локальный индикатор речи через `_audioService.SpeakingStateChanged`

---

## ChatList (`Desktop/ViewModels/ChatList/`)

### ChatListViewModel (`Core/ChatListViewModel.cs`)
- `IsChatMatchingCurrentTab`: `type is not ChatType.Contact` для групп
- `UpdateChatMeta` — обновляет только метаданные чата
- Отложенная прокрутка: `_pendingScrollToMessageId` — если чат уже открыт, вызывает `ScrollToMessageAsync` немедленно; иначе передаёт id в конструктор `ChatViewModel`

### MainMenuViewModel (`Shell/MainMenuViewModel.cs`)
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
ChatComposer.SendMessageAsync()
  → загрузка файлов (FileService)
  → POST /api/messages (CreateMessageRequest)
  → MessageService.CreateMessageAsync()
      → AccessControlService.EnsureMemberOfAsync()
      → ResolveRootForwardedMessageIdAsync (для пересылок)
      → извлечение @mentions
      → сохранение в PostgreSQL
      → BroadcastToMembersAsync("ReceiveMessageDto", MessageDto)
      → NotifyAndUpdateUnreadAsync (mention-уведомления)
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
  Клиент: ProcessUdpPacket → CallAudioService.ReceiveMixedAudio(opusData)
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
  → GlobalHubConnection → ChatListViewModel.UpdateChatMeta()
                        → ChatViewModel.OnChatUpdated()
```

## Удаление участника из чата
```
ChatMemberService.RemoveMemberAsync()
  → HubNotifier.SendToUserAsync(userId, "ChatRemoved", chatId)
  → GlobalHubConnection.ChatRemoved
  → ChatListViewModel удаляет чат из списка, сбрасывает SelectedChat
```

## Смена роли участника
```
ChatMemberService.UpdateRoleAsync(chatId, userId, newRole, updatedByUserId)
  → accessControl.EnsureOwnerOfAsync
  → systemMessages.CreateAsync(RoleChanged)
  → hubNotifier.SendToUserAsync(userId, "ChatUpdated", dto с CurrentUserRole = newRole)
  → клиент: GlobalHubConnection.ChatUpdated → ChatViewModel обновляет CurrentUserRole
```

---

# 20. DESKTOP — ViewS

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
- Баннеры и кнопки звонков используют свойства `Call.HasActiveCall`, `Call.IsInActiveCall`, `Call.ActiveCallBannerText`
- Баннер закреплённых сообщений использует `Pinned.IsPinnedBannerVisible`, `Pinned.PinnedBannerPreViewText`, `Pinned.HasMultiplePinned`, `Pinned.PinnedCount`

## 20.4 Информационная панель чата (`Views/Chat/ChatInfoPanel.axaml.cs`)
- Кнопки редактирования/удаления/выхода управляются `CanEditGroupChat`/`CanLeaveChat`
- Секции «Медиа» и «Опросы» скрываются при нулевых счётчиках
- `LastSeen` для контакта и участников через `MultiBinding` с `LastSeenTextConverter`
- Закреплённые сообщения используют `Pinned.PinnedMessages` и `Pinned.PinnedCount`

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

# 21. ИЗВЕСТНЫЕ ПРОБЛЕМЫ И БАГИ

## Актуальные проблемы

### 1. Серверный микшер (CallMixerService)
- **Код:** `API/Services/Call/CallMixerService.cs`, строка ~182, вызов `Normalize(_mixBuffer)`
- **Проблема:** при синхронизации `_mixBuffer` и `_personalBuffer` потенциальная гонка, что приводит к мерцанию в аудиопотоке
- **Статус:** анализ продолжается

### 2. Остановка записи голосового сообщения
- **Код:** `Desktop/Services/Features/Media/AudioRecorderService.cs`, метод `StopAsync()`
- **Проблема:** `_captureDevice.StopAsync()` не дожидается завершения последних буферов → обрезание до 0.5 сек
- **Обход:** искусственная задержка 200мс перед финализацией
- **Статус:** требуется переработка на событийную модель (подписка на окончание буфера)

### 3. Счётчик непрочитанных сообщений
- **Код:** `Desktop/Data/Repositories/LocalCacheService.cs`, строка ~52
- **Проблема:** `GetAllUnreadCountsAsync` не обновляется при `MarkAllAsReadAsync` → stale данные
- **Статус:** требуется broadcast через GlobalHub

### 4. Звонок при свёрнутом окне
- **Код:** `Desktop/Services/Features/Call/CallService.cs`, `OnIncomingCall`
- **Проблема:** если `NavigationService` ещё не готов, `CallViewModel` не создаётся
- **Статус:** требуется отложенная инициализация или глобальный диспатчер UI

### 5. Конфликты ролей ChatRole
- **Код:** `Desktop/ViewModels/Chat/Permissions/ChatPermissionsManager.cs`
- **Проблема:** флаг `IsSystemAdmin` в `ChatContext` не обновляется при смене роли администратора
- **Статус:** требуется переподписка на `IChatHubConnection.UserRoleUpdated`

### 6. Миграции SQLite
- **Код:** `Desktop/Data/LocalDatabase.cs`, `MigrateToVersion2`
- **Проблема:** при переполнении WAL-файла миграция падает с `SQLITE_BUSY`
- **Статус:** требуется проверка `PRAGMA wal_checkpoint(TRUNCATE)` перед миграцией

### 7. Запись голосового сообщения
- **Код:** `Desktop/Services/Features/Media/AudioRecorderService.cs`
- **Проблема:** при прерывании (CancelAsync) временный WAV-файл не удаляется
- **Обход:** расширенный try-finally с очисткой
- **Статус:** требуется утилита очистки по таймеру