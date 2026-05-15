# Документация проекта ВнутрьСеть

> **Стек:** C# / .NET 10, ASP.NET Core, Entity Framework Core, PostgreSQL, SignalR, Avalonia UI, SQLite  
> **Архитектура:** Слоёная (Entities → Services → Controllers / Hubs), Event-Driven через SignalR  
> **Принцип:** Railway-Oriented Programming (все ошибки через `Result`, а не исключения)  
> **Паттерн ошибок:** `BaseController.Map(Result<T>)` → HTTP-статус автоматически по `ResultErrorType`  
> **Авторизация:** JWT Bearer + WebSocket query token `?access_token=` для SignalR  

---

# 1. СЛОЙ СУЩНОСТЕЙ (API.Data)

## 1.1 Пользователи и авторизация

### User

**Путь:** `API/Data/User.cs` + `API/Data/Partial.cs`

| Свойство | Тип | Назначение |
|---|---|---|
| `Id` | `int` | PK |
| `Username` | `string` | Уникальный логин |
| `Surname`, `Name`, `Midname` | `string?` | ФИО (русская модель) |
| `Password` | `UserPassword` | Owned-сущность (только хэш BCrypt) |
| `CreatedAt` | `DateTime?` | Дата регистрации |
| `LastOnline` | `DateTime?` | Последняя активность |
| `DepartmentId` | `int?` | FK → Department |
| `Avatar` | `string?` | Относительный путь |
| `IsBanned` | `bool` | Заблокирован |
| `StatusType` | `UserStatusType` | Online/Away/Busy/DND |
| `StatusExpiresAt` | `DateTime?` | Истечение статуса |
| `DisplayName` | `string?` | Вычисляемое: "Фамилия Имя Отчество" или Username |

**Методы:** `GetDisplayName()` – возвращает `DisplayName`, иначе `Username`, иначе заглушку.

**Навигация:** `ChatMembers`, `Chats`, `Department`, `Departments` (где Head), `SentMessages` (ICollection<UserMessage>), `PollVotes`, `UserSetting` (1:1), `RefreshTokens`

---

### UserPassword

**Путь:** `API/Data/UserPassword.cs`  
**Методы:** `SetPassword(string)`, `Verify(string) → bool`  
**Поле:** `Hash: string` (BCrypt, стоимость из `MessengerSettings.BcryptWorkFactor=12`)

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
| `TokenHash` | `string` | SHA-256 хэш |
| `JwtId` | `string` | Связанный JWT `jti` |
| `CreatedAt`, `ExpiresAt` | `DateTime` | Период действия |
| `UsedAt` | `DateTime?` | null = не использован |
| `RevokedAt` | `DateTime?` | null = активен |
| `ReplacedByTokenId` | `int?` | Ссылка на следующий |
| `FamilyId` | `string` | Группа токенов одной сессии |
| `IsActive` | `bool` | Вычисляемое: !UsedAt && !RevokedAt && ExpiresAt > now |

**Механика:** reuse (UsedAt != null) → отзыв всей семьи через `FamilyId`. Цепочка: `ReplacedByTokenId`.

---

### TokenPair

**Путь:** `API/Data/TokenPair.cs` (не persist)

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

**Навигация:** `ChatMembers`, `Messages` (ICollection<Message>), `CreatedBy`, `Department` (1:1)

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
| `LastReadMessageId` | `int?` | Для unread-счётчика (ссылка на Message) |
| `LastReadAt` | `DateTime?` | |

---

### Department

**Путь:** `API/Data/Department.cs`

| Свойство | Тип | Назначение |
|---|---|---|
| `Id` | `int` | PK |
| `Name` | `string` | |
| `ParentDepartmentId` | `int?` | Self-referencing FK |
| `ChatId` | `int?` | FK → Chat (1:1, auto-created при создании отдела) |
| `HeadId` | `int?` | FK → User (уникальный индекс) |

---

## 1.3 Сообщения и вложения

### Message (абстрактный базовый класс)

**Путь:** `API/Data/Message.cs`  
**Иерархия:** TPH с дискриминатором `message_type` (false = UserMessage, true = SystemMessage)

| Свойство | Тип | Назначение |
|---|---|---|
| `Id` | `int` | PK |
| `ChatId` | `int` | FK → Chat |
| `CreatedAt` | `DateTime` | |
| `IsDeleted` | `bool?` | Soft-delete |
| `PinnedAt` | `DateTime?` | Закреплено, если не null (логический `IsPinned` отсутствует) |
| `PinnedByUserId` | `int?` | |
| `Chat` | `Chat` | Навигация |
| `PinnedByUser` | `User?` | Навигация |
| `ChatMembers` | `ICollection<ChatMember>` | Обратная навигация для LastReadMessage |

### UserMessage : Message

| Свойство | Тип | Назначение |
|---|---|---|
| `SenderId` | `int?` | FK → User (может быть null для системных действий, но в UserMessage обычно заполнен) |
| `Content` | `string?` | Текст (до 4000 символов) |
| `EditedAt` | `DateTime?` | |
| `ReplyToMessageId` | `int?` | Самореференс |
| `ForwardedFromMessageId` | `int?` | Самореференс |
| `VoiceMessage` | `VoiceMessage?` | 1:1 |
| `Poll` | `Poll?` | 1:1 |
| `IsVoiceMessage` | `bool` | Вычисляемое: VoiceMessage != null |
| `MessageFiles` | `ICollection<MessageFile>` | Файлы |
| `InverseReplyToMessage`, `InverseForwardedFromMessage` | `ICollection<UserMessage>` | Обратные навигации |

### SystemMessage : Message

| Свойство | Тип | Назначение |
|---|---|---|
| `InitiatorId` | `int?` | FK → User (кто инициировал событие) |
| `TargetUserId` | `int?` | FK → User (кого касается) |
| `SystemEventType` | `SystemEventType` | Тип системного события |
| `Content` | `string?` | Дополнительный текст |

**Примечание:** Для SystemMessage столбец `sender_id` хранит `InitiatorId`, для совместимости с FK ограничениями.

---

### VoiceMessage

**Путь:** `API/Data/VoiceMessage.cs`  
PK = FK → UserMessage (столбец `message_id`)

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

## 1.4 Звонки (In-Memory, не persist)

### CallSession

| Свойство | Тип |
|---|---|
| `CallId` | `string` (GUID) |
| `ChatId` | `int` |
| `InitiatorId` | `int` |
| `StartedAt` | `DateTimeOffset` |
| `Status` | `CallStatus` |
| `IsGroupCall` | `bool` |
| `PendingParticipants` | `ConcurrentDictionary<int, CallParticipant>` |
| `ActiveParticipants` | `ConcurrentDictionary<int, CallParticipant>` |
| `TimeoutCts` | `CancellationTokenSource` |

---

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

**DbSets:** `Messages` (базовый), `UserMessages`, `SystemMessages`, `Chats`, `RefreshTokens`, `ChatMembers`, `Departments`, `VoiceMessages`, `MessageFiles`, `Polls`, `PollOptions`, `PollVotes`, `SystemSettings`, `Users`, `UserSettings`

**Конфигурации:**
- PostgreSQL Enum: `theme`, `chat_role`, `chat_type`, `system_event_type`, `user_status_type`
- TPH: дискриминатор `message_type` (false/true) для UserMessage/SystemMessage
- `User.Password` → Owned Entity (колонка `password_hash`)
- Все timestamp: `timestamp without time zone` (кроме `CallSession.StartedAt` – не persist, в памяти как `DateTimeOffset`)
- Каскады: Chat→Messages, User→RefreshTokens, UserMessage→Poll; SetNull для остальных FK
- `PinnedAt` индекс с фильтром `WHERE Pinned_At IS NOT NULL`
- `UserMessage.ReplyToMessage` и `ForwardedFromMessage` – самореференс с SetNull
- `SystemMessage` использует колонку `sender_id` для `InitiatorId`, переиспользуя ограничение `Messages_SenderId_fkey`

---

## 1.8 Проекции API

**Путь:** `API/Repositories/Projections/`

### UserWithSettingsProjection

Используется для эффективной выборки пользователей с их настройками без загрузки полных сущностей.

| Свойство | Тип | Назначение |
|---|---|---|
| `Id` | `int` | PK |
| `Username` | `string` | Уникальный логин |
| `Surname`, `Name`, `Midname` | `string?` | ФИО |
| `Avatar` | `string?` | Относительный путь к аватару |
| `DepartmentId` | `int?` | FK → Department |
| `DepartmentName` | `string?` | Имя отдела (из навигации) |
| `IsBanned` | `bool` | Заблокирован |
| `LastOnline` | `DateTime?` | Последняя активность |
| `CreatedAt` | `DateTime?` | Дата регистрации |
| `Theme` | `Theme?` | Тема из `UserSetting` |
| `NotificationsEnabled` | `bool` | Уведомления из `UserSetting` (дефолт `true`, если настройки нет) |
| `StatusType` | `UserStatusType` | Текущий статус |
| `StatusExpiresAt` | `DateTime?` | Истечение статуса |

---

# 2. СЛОЙ DTO (Shared.Dto)

## 2.1 Auth

| DTO | Поля | Назначение |
|---|---|---|
| `AuthResponseDto` | `Id`, `Username`, `DisplayName`, `Token`, `Role` | Ответ логина (без refresh-токена в теле) |
| `LoginRequest` | `Username`, `Password` (record) | Вход |
| `RefreshTokenRequest` | `AccessToken` (record) | Обновление токенов (refresh-токен передаётся через httpOnly cookie) |
| `TokenResponseDto` | `Token`, `UserId`, `Role` | Ответ обновления (без refresh-токена в теле) |

Refresh-токен передаётся исключительно в httpOnly cookie `refresh_token`.  
Серверные внутренние модели:
- `AuthLoginResult` (содержит `AuthResponseDto` и `RefreshToken` для установки cookie).
- `AuthRefreshResult` (содержит `TokenResponseDto` и `RefreshToken`).

---

## 2.2 Call

| DTO | Поля | Назначение |
|---|---|---|
| `CallChatMessageDto` | `CallId`, `SenderId`, `SenderName`, `SenderAvatar`, `Text`, `SentAt` | Сообщение в чате звонка |
| `CallInviteDto` | `CallId`, `ChatId`, `ChatName`, `InitiatorId/Name/Avatar`, `ActiveParticipantsCount`, `IsGroupCall` | Входящий звонок |
| `CallParticipantDto` | `UserId`, `DisplayName`, `AvatarUrl`, `IsMuted`, `IsSpeaking` | Участник |
| `CallStateDto` | `CallId`, `ChatId`, `Status`, `InitiatorId`, `StartedAt` (DateTimeOffset), `IsGroupCall`, `Participants` | Полное состояние |
| `WebRtcSignalDto` | `CallId`, `FromUserId`, `TargetUserId` (-1=broadcast), `Type` (offer/answer/candidate/hangup), `Payload` (JSON) | SDP/ICE сигнал |

Пространство имён: `Shared.Dto.Call`.

---

## 2.3 Chat

| DTO | Ключевые поля | Назначение |
|---|---|---|
| `ChatDto` | `Id`, `Name`, `Type`, `LastMessage*` (7 полей), `UnreadCount`, `Contact*` (4 поля), `HideSenderPrefix` [JsonIgnore], `CurrentUserRole` (ChatRole?, игнорируется при null), `ShowHistoryForNewMembers` | Представление чата |
| `ChatMemberDto` | `ChatId`, `UserId`, `Role`, `JoinedAt`, `NotificationsEnabled`, `Username`, `DisplayName`, `Avatar` | Участник |
| `ChatNotificationSettingsDto` | `ChatId`, `NotificationsEnabled` | Настройки уведомлений |
| `UpdateChatDto` | `Id`, `Name?`, `ChatType?`, `ShowHistoryForNewMembers?` | Редактирование |
| `UpdateChatMemberDto` | `UserId` | Изменение участника |
| `ChatUpdateEventDto` | `Id`, `Name`, `Type`, `CreatedById`, `Avatar`, `ShowHistoryForNewMembers` | Событие обновления метаданных чата (отправляется через SignalR) |

---

## 2.4 Message

| DTO | Назначение |
|---|---|
| `CreateMessageRequest` | `ChatId` [Required], `Content` [MaxLength 4000], `ReplyToMessageId?`, `ForwardedFromMessageId?`, `IsVoiceMessage`, `Voice*` (3 поля: DurationSeconds, Waveform, FileSize, FileUrl), `Files?` |
| `MessageDto` | Полное представление сообщения. `Files` – `List<MessageFileDto>?`, не сериализуется при null. `Poll` nullable. Многие поля имеют `JsonIgnoreCondition.WhenWritingNull`. `IsPinned` вычисляется по `PinnedAt != null`. |
| `MessageFileDto` | `Id`, `MessageId`, `FileName`, `ContentType`, `Url`, `PreviewType` (file/image/video), `FileSize` |
| `MessageForwardInfoDto` | `OriginalMessageId`, `OriginalChatId`, `OriginalSenderId?`, `OriginalSenderName?`, `OriginalCreatedAt` |
| `MessageReplyPreviewDto` | `Id`, `ChatId`, `SenderId?`, `SenderName?`, `Content?`, `CreatedAt`, `IsDeleted`, `IsVoiceMessage`, `HasPoll`, `FilesCount` |
| `PagedMessagesDto` | `Messages`, `HasMoreMessages`, `HasNewerMessages` |
| `UpdateMessageDto` | `Id`, `Content?` |
| `ChatCountsDto` | `MediaCount`, `FilesCount`, `PollsCount`, `PinnedCount` |

---

## 2.5 Прочие DTO

| Группа | DTO и поля |
|---|---|
| **Department** | `DepartmentDto` (Id, Name, ParentDepartmentId, Head, HeadName, UserCount), `UpdateDepartmentMemberDto` (UserId) |
| **Notification** | `NotificationDto` (Type: message/mention/poll, ChatId, ChatName, Avatar, MessageId, Sender*, Preview до 100 символов, CreatedAt) |
| **Online** | `UserStatusDto` (UserId, IsOnline, LastOnline, StatusType, StatusExpiresAt), `OnlineUsersResponseDto` (OnlineUserIds, TotalOnline), `SetStatusRequest` (StatusType, Duration) |
| **Poll** | `CreatePollDto` (ChatId, Question, IsAnonymous, AllowsMultipleAnswers, Options), `PollDto` (+ SelectedOptionIds, CanVote), `PollOptionDto` (+ VotesCount, Votes), `PollVoteDto` |
| **ReadReceipt** | `MarkAsReadDto` (ChatId, MessageId), `ReadReceiptResponseDto`, `UnreadCountDto`, `AllUnreadCountsDto`, `ChatReadInfoDto` (+ FirstUnreadMessageId) |
| **Search** | `GlobalSearchMessageDto` (+ HighlightedContent, HasFiles/Voice/Poll, `SenderId` nullable), `GlobalSearchResponseDto`, `SearchMessagesResponseDto`, `SearchMessagesQueryDto` / `GlobalSearchQueryDto` |
| **User** | `AvatarResponseDto`, `ChangePasswordDto`, `ChangeUsernameDto`, `CreateUserDto` (ФИО + DepartmentId), `ResetPasswordAdminDto`, `UserDto` (16 полей) |

---

# 3. КОНТРОЛЛЕРЫ (API.Controllers)

## BaseController\<T\>

**Путь:** `API/Controllers/BaseController.cs`

| Метод | Назначение |
|---|---|
| `GetCurrentUserId()` | Из JWT claim `sub` |
| `IsCurrentUser(int)` | Сравнение с текущим |
| `Map(Result<T>)` | Успех → 200+ApiResponse, ошибка → вызов `MapFailureToObjectResult<T>` |
| `MapFailureToObjectResult<T>(Result result)` | Создаёт `ApiResponse<T>` и возвращает HTTP-статус по `ResultErrorType` |
| `Forbidden(...)` | 403 |
| `ExecuteAsync(...)` | Обёртка с try-catch, логирует ошибку перед возвратом |

**Маппинг:** Unauthorized→401, Forbidden→403, NotFound→404, Conflict→409, Internal→500, default→400

---

## Контроллеры

| Контроллер | Эндпоинты | Авторизация | Rate Limit |
|---|---|---|---|
| `AuthController` | POST login, refresh, revoke | login/refresh — AllowAnonymous | `login` |
| `UsersController` | GET/PUT users, avatar, username, password, online-статусы, DELETE avatar | IsCurrentUser для изменений | — |
| `ChatsController` | CRUD, участники, роли, аватар | IsMember/Admin/Owner | — |
| `MessagesController` | CRUD, pin/unpin, GET /chat/{chatId}/latest, GET /chat/{chatId}/counts, before/after/around, поиск | IsMember | `messaging`, `search` |
| `FilesController` | POST upload | IsMember | `upload` |
| `DepartmentsController` | CRUD, участники | Admin или Head | — |
| `PollsController` | Create/vote/close/get | IsMember | — |
| `ReadReceiptsController` | Mark read, unread-счётчики | Authorized | — |
| `StatusController` | Set/get | Authorized | — |
| `NotificationsController` | Настройки по чату | Authorized | — |
| `AdminController` | CRUD пользователей, бан, сброс пароля | Admin | — |

**AuthController**  
- Принимает `IOptions<JwtSettings>` для получения времени жизни refresh-токена.  
- `POST /login` возвращает `AuthResponseDto` (без refresh-токена) и устанавливает httpOnly cookie `refresh_token` (Secure, SameSite=Strict, Path=/api/auth).  
- `POST /refresh` читает refresh-токен из cookie `refresh_token`, при успехе обновляет cookie и возвращает `TokenResponseDto` (без refresh-токена).  
- `POST /revoke` удаляет cookie `refresh_token`.

---

# 4. ХАБЫ (SignalR)

## MessengerHub

**Путь:** `API/Hubs/MessengerHub.cs`  
**Эндпоинт:** `/chatHub` (единый хаб для чата и звонков)  
**Группы:** `user_{id}` (личные), `chat_{id}` (чат)

### Подключение
- `OnConnectedAsync` — пользователь добавляется в группы своих чатов, публикуется `UserStatusChanged` или `UserOnline`.
- `OnDisconnectedAsync` — обновляется `LastOnline`, рассылается `UserOffline`, выполняется выход из всех активных звонков.

### Клиентские методы чата
`JoinChat`, `LeaveChat`, `MarkAsRead`, `MarkMessageAsRead`, `GetUnreadCounts`, `GetReadInfo`, `SendTyping`, `GetOnlineUsersInChat`, `SetStatus`

### Клиентские методы звонков
`InitiateCall`, `JoinCall`, `LeaveCall`, `DeclineCall`, `CancelCall`, `SendSignal`, `ToggleMute`, `ToggleSpeaking`, `SendCallMessage`, `GetCallState`

### Серверные события
- **Чат:** `UserStatusChanged`, `UserOnline`, `UserOffline`, `UserTyping`, `MessageRead`, `UnreadCountUpdated`, `ChatUpdated`, `ChatRemoved`, `ReceiveMessage`, `MessageUpdated`, `MessageDeleted`, `PollUpdated`, `ReceiveNotification`
- **Звонки:** `IncomingCall`, `CallStateUpdated`, `CallEnded`, `CallMessageReceived`, `CallParticipantJoined`, `CallParticipantLeft`, `ParticipantMuteChanged`, `ParticipantSpeakingChanged`, `ActiveCallStarted`, `ActiveCallUpdated`, `ActiveCallEnded`, `CallError`, `ReceiveSignal`

### Особенности реализации звонков
- Кэш информации о пользователях `ConcurrentDictionary<int, Task<(string? Name, string? Avatar)>>`.
- Параллельная загрузка данных участников в `ToStateDtoAsync`.
- `JoinCall` обёрнут в try-catch с отправкой `CallError`.
- `CancelCall` для групповых звонков делегирует в `LeaveCall`.
- Имена методов вынесены в константы `HubMethods`.

---

# 5. MIDDLEWARE

| Middleware | Назначение |
|---|---|
| `ExceptionHandlingMiddleware` | Все исключения → 500 + ApiResponse. Dev: стектрейс, Prod: "Произошла внутренняя ошибка" |
| `MissingFileCleanupMiddleware` | 404 на `/uploads` или `/avatars` → очистка ссылок в БД (только GET/HEAD, после next) |
| `CookiePolicy` | `MinimumSameSitePolicy=Strict`, `HttpOnly=Always`, `Secure` зависит от окружения |

---

# 6. МАППИНГ (Ручной, extension-методы)

| Класс маппинга | Ключевые методы |
|---|---|
| `ChatMappings` | `.ToDto(IUrlBuilder?)`, `.ToDto(User? contact, IUrlBuilder?)` |
| `FileMappings` | `.ToDto()`, `DeterminePreviewType(contentType)` → file/image/video/audio |
| `MessageMappings` | `.ToDto(currentUserId, urlBuilder)` — рекурсивный обход цепочки пересылки, заполняет `OriginalSenderId` |
| `PollMappings` | `Poll.ToDto(currentUserId?)` (SelectedOptionIds, CanVote), `PollOption.ToDto(isAnonymous)` |
| `UserMappings` | `.ToDto(urlBuilder, isOnline?)` (включает `StatusType` и `StatusExpiresAt`), `GetDisplayName()` |

---

# 7. ИНФРАСТРУКТУРА API

## Утилиты (Common)

| Класс | Назначение |
|---|---|
| `AppDateTime` | Обёртка `TimeProvider`. Возвращает `DateTimeKind.Unspecified` |
| `Result<T>` / `Result` | ROP: `IsSuccess`, `IsFailure`, `Error`, `ErrorType`. Фабрики: `Success()`, `Failure()`, `NotFound()`, `Forbidden()`, `Conflict()`, `Internal()` |
| `ResultExtensions` | `UnwrapOrDefault`, `UnwrapOrFallback`, `TryUnwrap` |
| `ValidationHelper` | `ValidateUsername` (regex `^[a-z0-9_]{3,30}$`), `ValidatePassword` (≥6 символов) |
| `StatusExtensions` | `Parse(string?)` → TimeSpan: "15m", "30m", "1h", "2h", "4h", "8h", "24h" |
| `UrlHelpers` | `BuildFullUrl(string?, IUrlBuilder?)` |
| `HubMethods` | Статические константы для имён хаб-методов. Вложенные классы: `Chat`, `Call`, `ChatInvoke`, `CallInvoke`. |
| `SystemEventMeta` | Форматирует системные сообщения и предоставляет префиксы/суффиксы для UI. |

---

## Конфигурация

| Класс | Ключевые значения |
|---|---|
| `JwtSettings` | `AccessTokenLifetimeMinutes=15`, `RefreshTokenLifetimeDays=30`, `Issuer="API"`, `Audience="MessengerClient"` |
| `MessengerSettings` | `AdminDepartmentId=1`, `MaxFileSizeBytes=20MB`, `BcryptWorkFactor=12`, `MaxImageDimension=100px`, `ImageQuality=85`, `DefaultPageSize=50`, `MaxPageSize=100` |
| `AuthConfiguration` | JWT Bearer + query token `access_token` для `/chatHub` |
| `StaticFilesConfiguration` | `/uploads`, `/avatars`. Аватары: `Cache-Control: no-cache, no-store` |

---

## Инфраструктурные сервисы

| Класс | Назначение |
|---|---|
| `AccessControlService` | Проверка прав с двойным кэшем (MemoryCache + per-request). `IsSystemAdmin()` – bypass для роли Admin. |
| `OnlineUserService` | Singleton. `ConcurrentDictionary<userId, ConcurrentDictionary<connectionId, byte>>`. Очистка каждые 5 мин. |
| `UserStatusService` | Статусы обновляются через `ExecuteUpdateAsync`. Использует `IHubContext<MessengerHub>`. |
| `StatusCleanupHostedService` | Фоновый: очистка истёкших статусов каждую минуту |
| `CacheService` | MemoryCache: чаты (TTL 5м, sliding 2м), членство (TTL 10м, sliding 3м) |
| `HubNotifier` | `SendToChatAsync`, `SendToUserAsync` через `IHubContext<MessengerHub>`. Глотает исключения. |
| `HttpUrlBuilder` | Абсолютный URL через `IHttpContextAccessor` |
| `UdpDiscoveryService` | UDP порт 5275. Запрос: `MESSENGER_DISCOVER`, ответ: `MESSENGER_HERE:PORT` или `MESSENGER_HERE:PORT:IP`. |
| `EnumNameTranslator` | CLR → PostgreSQL snake_case для enum. |

---

## Репозитории

| Интерфейс | Реализация | Назначение |
|---|---|---|
| `IUserRepository` | `UserRepository` | `FindByUsernameAsync`, `FindByIdAsync`, `FindByIdWithPasswordAsync`, `Add`, `GetAllWithSettingsAsync`, `GetWithSettingsAsync`, `UsernameExistsByOtherUserAsync` |
| `IRefreshTokenRepository` | `RefreshTokenRepository` | Управление токенами: отзыв семейства, отзыв всех для пользователя, удаление истёкших, активные семьи. |
| `IChatRepository` | `ChatRepository` | Расширен: `GetLastMessagesAsync` разрешает цепочку пересылки. |
| `IMessageRepository` | `MessageRepository` | `GetLatestAsync` теперь сначала выбирает ID, затем раздельно загружает UserMessages и SystemMessages, сортирует по карте порядка. `GetChatCountsAsync` вычисляет `totalFiles`. |
| `IReadReceiptRepository` | `ReadReceiptRepository` | Все операции с отметками о прочтении. |
| `IPollRepository` | `PollRepository` | Управление опросами и голосами. |

---

# 8. СЛОЙ СЕРВИСОВ API

## BaseService\<T\>

`_context: MessengerDbContext`, `_logger: ILogger<T>`

`SaveChangesAsync` → ConcurrencyException: Conflict, UniqueViolation (23505): Conflict, прочие DbUpdateException: Internal

Методы: `FindEntityAsync<T>(id)`, `Paginate(query, page, pageSize)`, `NormalizePagination(page, pageSize, max)`

---

## AuthService

**Путь:** `API/Services/Core/Auth/AuthService.cs`

**LoginAsync:**
1. Dummy-hash BCrypt для несуществующих пользователей (timing-safe)
2. Проверка бана
3. Роль: `DepartmentId == AdminDepartmentId` → Admin; является HeadId → Head; иначе User
4. `MaxActiveSessions=5` — при превышении отзыв старых семей
5. Удаление токенов истёкших >60 дней
6. Возвращает `Result<AuthLoginResult>` (содержит `AuthResponseDto` без refresh-токена и строку `RefreshToken` для установки cookie).

**RefreshTokenAsync:**
- `UsedAt != null` ИЛИ `RevokedAt != null` → reuse detected → отзыв всей семьи
- Ротация: UsedAt = now, новый токен с тем же FamilyId
- Возвращает `Result<AuthRefreshResult>` (содержит `TokenResponseDto` без refresh-токена и строку `RefreshToken`).

**TokenService:**
- HMAC-SHA256, `ClockSkew=Zero`, секрет ≥32 символов (проверяется при старте)
- Refresh: 64 случайных байта в Base64

**Интерфейс `IAuthService`:**
```csharp
Task<Result<AuthLoginResult>> LoginAsync(string username, string password, CancellationToken ct = default);
Task<Result<AuthRefreshResult>> RefreshTokenAsync(string accessToken, string refreshToken, CancellationToken ct = default);
Task<Result> RevokeRefreshTokenAsync(int userId, CancellationToken ct = default);
```

---

## Бизнес-сервисы

| Сервис | Строк | Ключевое поведение |
|---|---|---|
| `CallSessionService` | 141 | Singleton. `ConcurrentDictionary`. Не масштабируется. |
| `ChatService` | ~440 | Использует `IChatRepository` и `IUserRepository`. `BuildChatDto` принимает словарь ролей и устанавливает `CurrentUserRole`. События в хаб отправляются через `IHubContext<MessengerHub>`. |
| `ChatMemberService` | 100 | При удалении участника отправляет `ChatRemoved` персонально. |
| `DepartmentService` | 218+ | Автоматически управляет связанными чатами при создании/удалении/переименовании отдела. BFS для проверки циклов. |
| `FileService` | 112 | Изображения → WebP. Путь: `wwwroot/uploads/chats/{chatId}/{guid}{ext}` |
| `MessageService` | ~590 | Вызовы хаба используют `HubMethods.Chat.*`. `CreateMessageAsync` разрешает корневое пересланное сообщение. `PinMessageAsync` проверяет, не закреплено ли уже. |
| `NotificationService` | 98 | Для Contact: ChatName = имя отправителя. Preview ≤100 символов. |
| `PollService` | ~150 | Внедрён `TimeBundle`. `ClosesAt` не сохраняется при создании опроса. |
| `ReadReceiptService` | ~90 | Полный переход на `IReadReceiptRepository`. |
| `AdminService` | 149 | Использует репозитории, маппит `UserWithSettingsProjection` в `UserDto`. |
| `UserService` | 180 | `GetAllUsersAsync`/`GetUserAsync` используют проекции. `ChangeUsernameAsync` проверяет уникальность. `RemoveAvatarAsync`. |
| `SystemMessageService` | 49 | Создаёт `SystemMessage` с `InitiatorId`. Отправка через `HubMethods.Chat.ReceiveMessage`. |
| `SystemMessageFormatter` | 23 | Делегирует форматирование `SystemEventMeta`. |

---

# 9. АБСТРАКЦИИ API

| Интерфейс | Ключевые методы |
|---|---|
| `IAuthService` | `LoginAsync` → `Result<AuthLoginResult>`, `RefreshTokenAsync` → `Result<AuthRefreshResult>`, `RevokeRefreshTokenAsync` |
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
| `IUserRepository` | `FindByUsernameAsync`, `FindByIdAsync`, `FindByIdWithPasswordAsync`, `UsernameExistsAsync`, `Add` |
| `IRefreshTokenRepository` | `RevokeByFamilyIdAsync`, `RevokeAllForUserAsync`, `DeleteExpiredAsync`, `GetActiveFamiliesAsync` |
| `IChatRepository` | `FindByIdAsync`, `FindByIdWithMembersAsync`, `IsMemberAsync`, `GetByIdsLightAsync`, `GetLastMessagesAsync`, `GetDialogPartnersAsync`, `UpdateLastMessageTimeAsync`, `GetMembersWithUsersAsync`, `GetVoiceFilePathsAsync`, `GetChatTypeAsync`, `GetShowHistoryForNewMembersAsync`, `GetContactChatsWithMembersAsync`, `SearchGroupChatsAsync`, `GetMembersForNotificationAsync`, `GetHistoryRestrictionsAsync`, `Add`, `AddMember`, `RemoveMember` |
| `IMessageRepository` | `FindUserMessageByIdAsync`, `FindUserMessageWithIncludesAsync`, `FindUserMessageForDeleteAsync`, `FindForBroadcastAsync`, `GetWithIncludesAsync`, `GetBeforeAsync`, `GetAfterAsync`, `GetUserMessagesForMixedAsync`, `GetSystemMessagesAsync`, `GetPinnedAsync`, `CountAsync`, `HasOlderAsync`, `HasNewerAsync`, `ExistsInChatAsync`, `ExistsAsync`, `SearchInChatAsync`, `SearchGlobalAsync`, `GetForwardedToChatIdsAsync`, `SoftDeleteAsync`, `PinAsync`, `UnpinAsync`, `Add`, `RemoveVoiceMessage`, `FindUserMessageWithIncludesNoTrackingAsync`, `GetLatestAsync`, `GetChatCountsAsync` |
| `IReadReceiptRepository` | `FindMemberAsync`, `FindMemberReadonlyAsync`, `UpdateReadPointerAsync`, `CountUnreadAsync`, `GetUnreadInfoAsync`, `GetAllUnreadCountsAsync`, `GetUnreadCountsAsync`, `MessageExistsAsync`, `GetLastMessageIdAsync` |
| `IPollRepository` | `FindByIdWithDetailsAsync`, `Add(Poll)`, `AddOption(PollOption)`, `AddVote(PollVote)`, `GetUserVotesAsync`, `RemoveVotes(IEnumerable<PollVote>)`, `CloseAsync` |

---

# 10. ПЕРЕЧИСЛЕНИЯ (Shared.Enum)

| Enum | Значения | Примечание |
|---|---|---|
| `CallEndReason` | Ended, Cancelled, Timeout, Declined | |
| `CallStatus` | Ringing, Active, Ended | |
| `ChatRole` | Member, Admin, Owner | |
| `ChatType` | Chat, Department, Contact, DepartmentHeads | `EnumMember`: `"chat"`, `"department"`, `"contact"`, `"department_heads"` |
| `SystemEventType` | ChatCreated, MemberAdded, MemberRemoved, MemberLeft, RoleChanged, CallStarted, CallEnded, MessagePinned, MessageUnpinned, ChatAvatarUpdated | `[JsonStringEnumConverter]` |
| `Theme` | light, dark, system | `[JsonStringEnumConverter]` |
| `UserRole` | User, Head, Admin | |
| `UserStatusType` | Online=0, Away=1, Busy=2, DoNotDisturb=3 | |

---

# 11. ОТВЕТЫ API (Shared.Response)

## ApiResponse\<T\>

| Поле | Тип | Назначение |
|---|---|---|
| `Success` | `bool` | |
| `Data` | `T?` | |
| `Message` | `string?` | При успехе |
| `Error` | `string?` | Код ошибки |
| `Details` | `string?` | Детали |
| `Timestamp` | `DateTime` | UTC |

**Фабрики:** `Ok(data, message?)`, `Fail(error, details?)`  
**ApiResponseHelper:** `Success<T>(data, msg?)`, `Error<T>(error, details?)`, `Error(error, details?)`

---

# 12. DESKTOP — КОНВЕРТЕРЫ (Desktop.Converters)

## Инфраструктура

| Класс | Назначение |
|---|---|
| `ConverterLocator` | Singleton, регистрирует 60+ конвертеров |
| `Converter` (MarkupExtension) | `{conv:Converter Name=...}` |
| `MultiConverter` (MarkupExtension) | `{conv:MultiConverter Name=...}` |
| `ConverterBase<TIn, TOut>` | `AllowNull`, `DefaultValue`, защита от исключений |

## Группы конвертеров

**Boolean:** `BoolToString`, `BoolToGeometry`, `BoolToDouble`, `BoolToColor`, `BoolToHAlignment`, `BoolToBrush`, `BoolToThickness`, `BooleanAnd`, `BooleanOr`, `EnumEquals`, `EnumNotEquals`, `UserRoleToVisibility`

**DateTime:** `DateTimeFormatConverter` (Time/Date/ShortDate/DateTime/Chat/Relative), `LastMessageDateConverter`, `LastSeenTextConverter` (Multi)

**Domain:** `ChatRoleToDisplay`, `ContentFilterToLabel`, `InitialsConverter`, `LevelToMargin` (20px×level), `LevelToVisibility`, `SearchScopeToTitle/Watermark/Hint/MessagesHeader`, `ThemeToDisplay`

**Generic:** `ComparisonConverter`, `IndexToText`, `HasContentConverter`, `HasTextOrAttachmentsMultiConverter`, `MultiplyConverter`, `PercentToWidthConverter` (Multi, min 8px), `PluralizeConverter`, `ResourceKeyToGeometryConverter`

---

# 13. DESKTOP — ЛОКАЛЬНАЯ БД (Desktop.Data, SQLite)

## LocalDatabase

**Путь:** `Desktop/Data/LocalDatabase.cs`
- WAL-режим, `synchronous=NORMAL`, `cache_size=-4000`, `mmap_size=33554432`
- Миграции через `PRAGMA user_version` (текущая: 2)
- FTS5 для полнотекстового поиска с триггерами
- Потокобезопасность: `SemaphoreSlim`
- Индексы: `idx_msg_chat_id_asc`, `idx_chats_last_msg`, `idx_chats_type_date`, `idx_messages_chat_id`

## Cached-модели

| Модель | Таблица | Особенности |
|---|---|---|
| `CachedMessage` | `messages` | 30+ колонок, `poll_json`/`files_json`, даты в Ticks. `sender_id` `int?`. Поля `reply_is_voice`, `reply_has_poll`, `reply_files_count`. |
| `CachedChat` | `chats` | 18 колонок, `contact_*`, Ticks |
| `CachedUser` | `users` | id, username, display_name, avatar, cached_at |
| `CachedReadPointer` | `read_pointers` | chat_id (PK), last_read, first_unread, unread_count |
| `ChatSyncState` | `chat_sync_state` | OldestLoadedId, NewestLoadedId, has_more_older/newer |
| `CachedDownloadedFile` | `downloaded_files` | file_id (PK), message_id, local_path, file_name, file_size, downloaded_at, content_type |

## Репозитории и сервисы

| Класс | Назначение |
|---|---|
| `MessageCacheRepository` | CRUD, FTS5→LIKE fallback. `TrimOldMessages` теперь обрабатывает каждый чат отдельно. `MarkDeletedAsync` очищает поля reply и forward. |
| `ChatCacheRepository` | Upsert, `UpdateLastMessageAsync` |
| `LocalCacheService` | `GetMessagesBeforeAsync` корректно определяет достижение начала истории через сравнение с `OldestLoadedId`. `PatchChatMetaAsync` для точечного обновления метаданных. |
| `CacheMapper` | `MessageDto↔CachedMessage`, `ChatDto↔CachedChat`. Source Generated JSON (`CacheJsonContext`). |
| `DownloadedFileRepository` | Управление записями о скачанных файлах. |

---

# 14. DESKTOP — ИНФРАСТРУКТУРА

## Конфигурация

| Класс | Назначение |
|---|---|
| `ApiEndpoints` | Статический билдер URL всех эндпоинтов. Включает `Messages.Latest`, `Messages.Counts`. |
| `AppConstants` | `MaxFileSizeBytes=20MB`, `DefaultPageSize=50`, `LoadMorePageSize=30`, `SearchPageSize=20`, `TypingIndicatorDurationMs=3500` |
| `ServiceCollectionExtensions` | Регистрации `CookieContainer`, `ICookieStorageService`, `HttpClient` с `UseCookies = true` и `CookieContainer`. |

## Хелперы

| Класс | Назначение |
|---|---|
| `AvatarHelper` | `GetSafeUri`, `GetUriWithCacheBuster`, `WithFreshCacheBuster` |
| `MimeTypeHelper` | `GetMimeType(extension)` |
| `ChatPreviewFormatter` | `BuildPreview`, `BuildReplyPreview`, `Pluralize` (публичный), делегирует системные сообщения `SystemEventMeta`. |
| `HttpResponseHelper` | `TryExtractErrorMessage` |
| `PasswordHelper` | `CalculateStrength(0–4)`, `ToStrengthLabel` |
| `RangeObservableCollection<T>` | `AddRange`, `InsertRange`, `RemoveRange` |

## Медиа

| Класс | Назначение |
|---|---|
| `AuthenticatedImageLoader` | LRU RAM (80 items/30MB), LOH-защита, дедупликация, дисковый кэш. Методы `InvalidateUrl`, `InvalidateByRelativePath`, `IsCached`. Поддержка `CancellationToken`. |
| `RemoteImage` | Attached Property для Avalonia Image. `CurrentUrlProperty` публичное. Оптимизация повторной загрузки того же URL. |
| `ImageCacheService` | (deprecated) |
| `MemoryDiagnostics` | Счётчики ChatVM/MessageVM/Bitmap/RemoteImage, LOH, дамп GC. Активирован. `OnMessageVmDisposed`. |

---

# 15. DESKTOP — СЕРВИСЫ

## Auth

| Сервис | Назначение |
|---|---|
| `AuthService` (клиент) | `LoginAsync`, `RefreshTokenAsync` (без явного refresh-токена), `RevokeAsync`, `Ping`, `IsAccessTokenValid` |
| `SessionStore` | In-Memory: Token, UserId, UserRole, события. Свойство `RefreshToken` отсутствует. |
| `SecureStorageService` | DPAPI/KeyChain/AES |
| `CookieStorageService` | Сохраняет/восстанавливает cookies из `CookieContainer` через `ISecureStorageService`. Методы `PersistAsync`, `RestoreAsync`, `ClearAsync`. |
| `AuthManager` | `InitializeAsync`, `LoginAsync`, `TryRefreshTokenAsync`, `LogoutAsync`. Управляет cookie через `ICookieStorageService`. При запуске восстанавливает cookie до попытки обновления токена. При логауте очищает cookie и secure storage. |

## API & Realtime

| Сервис | Назначение |
|---|---|
| `ApiClientService` | HTTP + авто-рефреш 401. Cookie прикрепляются автоматически. |
| `GlobalHubConnection` | SignalR `/chatHub`, 15+ событий. Retry при 503. События `ChatRemoved`, `ChatUpdated` (через `ChatUpdateEventDto`). |
| `CallHubConnection` | SignalR `/chatHub`, 12 событий. Retry при 503. |

## Call

| Сервис | Назначение |
|---|---|
| `CallService` | Оркестратор. `CallStarted` при первом `CallStateUpdated`. Поддержка нескольких сетевых интерфейсов. |
| `CallAudioService` | PortAudio. 48kHz/моно/20ms. Opus 32kbps. VAD адаптивный (noiseFloor*2.5) |
| `NoiseReducer` | Спектральное шумоподавление: FFT→Wiener Filter→Gate. Decision-Directed SNR α=0.96 |
| `ActiveCallStore` | ObservableObject: `ActiveCall`, `IsCallUiOpen`, `IsInCall` |

## Media

| Сервис | Назначение |
|---|---|
| `AudioPlayerService` | WAV через PortAudio. Play/Pause/Resume/Stop/Seek. |
| `AudioRecorderService` | 16kHz/моно/16-bit PCM. WAV+Waveform(100 баров). |
| `FileDownloadService` | Скачивание+прогресс, OS-открытие. |
| `FileDownloadStateService` | Состояние скачанных файлов, взаимодействует с `IDownloadedFileRepository`. |

## Platform

| Сервис | Назначение |
|---|---|
| `PlatformService` | Clipboard |
| `SettingsService` | JSON в AppData |
| `ThemeService` | `Application.RequestedThemeVariant` |
| `NavigationService` | Стек истории, проверка авторизации |
| `ServerDiscoveryService` | UDP 5275, парсит `MESSENGER_HERE:PORT:IP` |
| `NotificationService` | Стек ≤3, анимация прогресс-бара |
| `DialogService` | Стек диалогов, `Channel<CloseRequest>`, анимация |
| `CacheMaintenanceService` | Trim, VACUUM, очистка |
| `ChatNotificationApiService` | GET/POST настройки уведомлений |
| `ChatInfoPanelStateStore` | `IsOpen` ↔ `ISettingsService["ChatInfoPanelIsOpen"]` |

---

# 16. DESKTOP — АБСТРАКЦИИ

| Интерфейс | Ключевые члены |
|---|---|
| `IApiClientService` | `GetAsync<T>`, `PostAsync`, `PutAsync`, `DeleteAsync`, `UploadFileAsync`, `GetStreamAsync` |
| `IAudioPlayerService` | `Play`, `Pause`, `Resume`, `Stop`, `Seek`, события Position/Started/Stopped |
| `IAudioRecorderService` | `StartAsync`, `StopAsync → AudioRecordingResult?`, `CancelAsync` |
| `IAuthManager` | `LoginAsync`, `LogoutAsync`, `TryRefreshTokenAsync`, `WaitForInitializationAsync` |
| `IAuthService` (клиент) | `LoginAsync(username, password)`, `RefreshTokenAsync(accessToken, refreshToken? = null)` |
| `ICallHubConnection` | 10 методов + 12 событий WebRTC |
| `ICallService` | `StartCallAsync`, `JoinCallAsync`, `LeaveCallAsync`, `ToggleMuteAsync`, события |
| `IDialogService` | `ShowAsync<T>`, `CloseAsync`, `CloseAllAsync` |
| `IFileDownloadService` | `DownloadFileAsync(progress?)`, `OpenFileAsync`, `OpenFolderAsync` |
| `IFileDownloadStateService` | `GetStateAsync(MessageFileDto)`, `RegisterDownloadAsync`, `ResetAsync` |
| `IGlobalHubConnection` | `ConnectAsync`, `DisconnectAsync`, 15+ событий (включая `ChatRemoved`, `ChatUpdated` как `Action<ChatUpdateEventDto>`) |
| `INavigationService` | `NavigateToLogin`, `NavigateToMainMenu`, `NavigateTo<T>`, `GoBack` |
| `ISecureStorageService` | `SaveAsync<T>`, `GetAsync<T>`, `RemoveAsync` |
| `ISessionStore` | Token, UserId, UserRole, `HasRole(UserRole)`, события. Методы: `SetSession(string token, int userId, UserRole role)`, `UpdateTokens(string token)`. |
| `ISettingsService` | `Get<T>(key)`, `Set<T>(key, value)` |
| `IThemeService` | `Toggle`, `LoadFromSettings`, `SaveTheme` |
| `ICookieStorageService` | `PersistAsync()`, `RestoreAsync()`, `ClearAsync()` |
| Остальные | `INotificationService`, `IPlatformService`, `IServerDiscoveryService`, `ICacheMaintenanceService`, `IChatNotificationApiService`, `IChatInfoPanelStateStore` |

---

# 17. DESKTOP — ФАБРИКИ

**Путь:** `Desktop/ViewModels/ChatList/Factories/`

| Класс | Назначение |
|---|---|
| `CacheServices` | `ILocalCacheService` + `ICacheMaintenanceService` |
| `CallServices` | `ICallService` + `ActiveCallStore` |
| `ChatCoreServices` | `IChatService` + `IMessageService` + `IPollService` |
| `MediaServices` | `IAudioPlayerService` + `IAudioRecorderService` + `IFileDownloadService` + `IFileDownloadStateService` |
| `ChatViewModelDependencies` | Полный набор для `ChatViewModel`, включает `IFileDownloadStateService` |
| `ChatViewModelFactory` | Фабрика `ChatViewModel(chatId)` |
| `ChatsViewModelFactory` | Фабрика `ChatsViewModel` |

---

# 18. DESKTOP — VIEW MODELS

## Chat Core

### ChatViewModel

**Путь:** `Desktop/ViewModels/Chat/Core/ChatViewModel.cs`  
**Строк:** ~1700  

**Права и роли:**
- `CanEditGroupChat` – можно редактировать группу (системный администратор, создатель или роль Admin/Owner).
- `CanLeaveChat` – можно ли покинуть чат (не отделовский чат и не являешься создателем группового).
- `_isSystemAdmin` – флаг, определяемый по роли `Admin` в сессии.
- `CurrentUserRole` загружается из `ChatDto`.

**Счётчики и информационная панель:**
- Счётчики `PhotosCount`, `FilesCount`, `PollsCount` обновляются через вызов API `ChatCountsDto` (метод `RefreshCountersAndSectionAsync`).
- При открытии секции (`OpenInfoSection`) сразу асинхронно загружаются соответствующие данные (фото, файлы, опросы) с сервера.
- При получении нового сообщения через хаб (`RequestRefreshCounters`) вызывается полное обновление счётчиков и активной секции.
- Метод `RefreshInfoPanelLists` теперь только перестраивает превью участников.

**Управление коллекциями:** обработчики `CollectionChanged` для `Messages` и `LocalAttachments` обновляют инфопанель при удалении сообщений.

**Инициализация:** параллельно с сообщениями и участниками загружаются счётчики. После инициализации вызывается `RefreshChatPermissions`.

**Обработка `OnChatUpdated`:** принимает `ChatUpdateEventDto`, обновляет поля чата, инвалидирует кэш аватара.

**Handler'ы:**
- `MessageManager`, `Attachments`, `MemberLoader`, `EditDelete`, `Reply`, `Forward`, `Typing`, `Voice`, `InfoPanel`, `Search`, `Notification`.

---

### ChatMessageManager

**Строк:** 599

**Кэширование:**
- При старте загружается из локального кэша, если там не менее `DefaultPage/2` сообщений.
- После отображения кэша проверяется возраст синхронизации (порог 30 секунд). Если старше, запускается фоновая ревалидация с сервера; иначе пропускается.
- В `HandleMessageDeleted` очищаются ссылки на удалённое сообщение.

---

### MessageViewModel

**Строк:** 730

- Принимает `IFileDownloadStateService` для асинхронной инициализации состояния загрузки файлов.
- `ShowVoiceMessage` управляется свойством `OriginalIsVoiceMessage`.

---

### ChatHubSubscriber

При получении сообщения вызывает `ctx.RequestRefreshCounters?.Invoke()` для обновления счётчиков.

---

## Главные ViewModel

### MainMenuViewModel

**Строк:** 700

- Для определения вкладки группы/контакты используется `chat.Type is not ChatType.Contact`.
- Управление жизненным циклом CallHub: подписки/отписки при инициализации и переподключении.

---

### ChatsViewModel

**Путь:** `Desktop/ViewModels/ChatList/Core/ChatsViewModel.cs`

- `IsChatMatchingCurrentTab` возвращает `type is not ChatType.Contact` для групп.
- Метод `UpdateChatMeta` обновляет только метаданные.

---

## Представления (Views)

### ChatView.axaml
- `VirtualizingStackPanel CacheLength="2"`.
- Элементы списка сообщений без дополнительного `Margin`.

### ChatView.axaml.cs
- `VisibilityCheckDelayMs = 300`.
- При скролле регистрируется время последнего скролла. Таймер проверки видимости откладывается, если с момента скролла прошло <200 мс.
- При подгрузке старых сообщений сохраняется якорное сообщение и корректируется смещение для сохранения позиции просмотра.

---

# 19. ТИПИЧНЫЕ ПОТОКИ ДАННЫХ

## Отправка сообщения
```
Пользователь → ChatViewModel.SendMessage()
  → загрузка файлов
  → POST /api/messages (CreateMessageRequest)
  → MessageService.CreateMessageAsync()
    → проверка доступа, извлечение @mentions
    → сохранение в БД
    → HubNotifier.SendToChatAsync("ReceiveMessage")
  → MessengerHub → все клиенты чата
  → ChatHubSubscriber → ChatMessageManager.AddReceivedMessage()
  → обновление UI и счётчиков (RefreshCountersAndSectionAsync)
```

## Звонок (WebRTC)
```
Инициатор → CallService.StartCallAsync(chatId)
  → MessengerHub.InitiateCall(chatId)
  → CallSessionService.CreateCallAsync()
  → рассылка IncomingCall
Принятие → CallService.JoinCallAsync() → MessengerHub.JoinCall()
  → CallSession.Status = Active
  → прямой UDP аудио (обмен endpoint'ами через сигнальные сообщения)
```

## Обновление токенов
```
401 от API
  → AuthManager.TryRefreshTokenAsync()
  → POST /api/auth/refresh (RefreshTokenRequest с AccessToken; cookie refresh_token прикреплён)
  → AuthService.RefreshTokenAsync()
    → проверка refresh-токена из cookie
    → ротация, выдача нового refresh-токена
    → сервер устанавливает cookie, возвращает TokenResponseDto (без refresh-токена)
  → клиент: _cookieStorage.PersistAsync(), SessionStore.UpdateTokens(newAccessToken)
  → повтор запроса
```

## Обновление метаданных чата
```
ChatService → BuildUpdateEvent(chatEntity) → ChatUpdateEventDto
  → HubNotifier.SendToChatAsync("ChatUpdated")
  → GlobalHubConnection → ChatsViewModel.UpdateChatMeta / ChatViewModel.OnChatUpdated
```

## Удаление из чата
```
ChatMemberService.RemoveMemberAsync()
  → HubNotifier.SendToUserAsync(userId, "ChatRemoved", chatId)
  → Клиент удаляет чат из списка, сбрасывает SelectedChat
```

---

# 20. DESKTOP — VIEWS (Слой представлений)

### Основные стили и ресурсы
- `App.axaml`: подключает `Icons.axaml`, `Animations.axaml`, `MainStyle.axaml`, `MessageStyles.axaml`.
- `MainStyle.axaml`: стили для `ToggleButton.SwitchSmall:checked`.

### Главное окно
- Адаптивный режим при ширине ≤800px.
- Анимация открытия/закрытия диалогов с защитой от гонок.
- Закрытие поиска при клике вне поля.

### ChatView
- Виртуализирующий StackPanel с `CacheLength=2`.
- Якорное восстановление позиции скролла после подгрузки старых сообщений.
- Отложенная проверка видимости сообщений (300 мс) с учётом недавнего скролла.

### Информационная панель чата
- Кнопки редактирования/удаления/выхода управляются `CanEditGroupChat`/`CanLeaveChat`.
- Секции «Медиа» и «Опросы» скрываются при нулевых счётчиках.

### Профиль пользователя
- Кнопка «Отправить сообщение» видна только при `CanSendMessage`.

---

# 🔴 ИЗВЕСТНЫЕ ПРОБЛЕМЫ

## Критические

| # | Проблема | Где | Рекомендация |
|---|---|---|---|
| 1 | In-Memory звонки — не масштабируются | `CallSessionService` (Singleton) | Redis Backplane |
| 2 | `AppDateTime.UtcNow` → `DateTimeKind.Unspecified` | `API/Infrastructure/Common/AppDateTime.cs` | `DateTimeKind.Utc` или `DateTimeOffset` |
| 3 | `MissingFileCleanupMiddleware` — DB-запрос на каждый 404 | Middleware | Rate limiting по IP |
| 4 | `PollOptionDto.Votes` для анонимных опросов | `PollMappings.ToDto(isAnonymous)` | Фильтруется, но проверить |
| 5 | `MessageForwardInfoDto` без проверки доступа к чату | `MessageMappings` | Проверка IsMember для OriginalChatId |

## Архитектурные

| # | Проблема | Рекомендация |
|---|---|---|
| 6 | `MessageService` — ~590 строк | `MessageWriter`, `MessageReader`, `MessageSearchService` |
| 7 | `ChatDto` — множество полей, смешивает чат + сообщение + контакт | `ChatListItemDto`, `ChatDetailDto` |

## БАГИ

| # | Баг |
|---|---|
| 1 | Баги в полях фильтров поиска |
| 2 | Требуется в LoginView добавить кнопку с диалогом ввода IP для случаев, когда UDP-обнаружение не работает |
| 3 | При быстром скроле ломаются варианты ответа у опроса |
| 4 | Безопасность звонков |
| 5 | Отсутствуют сид-данные для первичного запуска через Docker |

## Пожелания

| # | Пожелание |
|---|---|
| 1 | При пересылке Last Message отображается от имени переславшего, а не оригинального автора |