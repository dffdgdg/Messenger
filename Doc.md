# ДОКУМЕНТАЦИЯ ПРОЕКТА ВнутрьСеть

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
| `ChatId` | `int?` | FK → Chat (1:1, auto-created) |
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

Свойства `FileName` и `ContentType` удалены.

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
| `AuthResponseDto` | `Id`, `Username`, `DisplayName`, `Token`, `RefreshToken`, `Role` | Ответ логина/регистрации |
| `LoginRequest` | `Username`, `Password` (record) | Вход |
| `RefreshTokenRequest` | `AccessToken`, `RefreshToken` (record) | Обновление токенов |
| `TokenResponseDto` | `Token`, `RefreshToken`, `UserId`, `Role` | Ответ обновления |

---

## 2.2 Call

| DTO | Поля | Назначение |
|---|---|---|
| `CallChatMessageDto` | `CallId`, `SenderId`, `SenderName`, `SenderAvatar`, `Text`, `SentAt` | Сообщение в чате звонка |
| `CallInviteDto` | `CallId`, `ChatId`, `ChatName`, `InitiatorId/Name/Avatar`, `ActiveParticipantsCount`, `IsGroupCall` | Входящий звонок |
| `CallParticipantDto` | `UserId`, `DisplayName`, `AvatarUrl`, `IsMuted`, `IsSpeaking` | Участник |
| `CallStateDto` | `CallId`, `ChatId`, `Status`, `InitiatorId`, `StartedAt` (**теперь `DateTimeOffset`**), `IsGroupCall`, `Participants` | Полное состояние |
| `WebRtcSignalDto` | `CallId`, `FromUserId`, `TargetUserId` (-1=broadcast), `Type` (offer/answer/candidate/hangup), `Payload` (JSON) | SDP/ICE сигнал |

---

## 2.3 Chat

| DTO | Ключевые поля | Назначение |
|---|---|---|
| `ChatDto` | `Id`, `Name`, `Type`, `LastMessage*` (7 полей), `UnreadCount`, `Contact*` (4 поля), `HideSenderPrefix` [JsonIgnore] | Представление чата |
| `ChatMemberDto` | `ChatId`, `UserId`, `Role`, `JoinedAt`, `NotificationsEnabled`, `Username`, `DisplayName`, `Avatar` | Участник |
| `ChatNotificationSettingsDto` | `ChatId`, `NotificationsEnabled` | Настройки уведомлений |
| `UpdateChatDto` | `Id`, `Name?`, `ChatType?`, `ShowHistoryForNewMembers?` | Редактирование |
| `UpdateChatMemberDto` | `UserId` | Изменение участника |

---

## 2.4 Message

| DTO | Назначение |
|---|---|
| `CreateMessageRequest` | `ChatId` [Required], `Content` [MaxLength 4000], `ReplyToMessageId?`, `ForwardedFromMessageId?`, `IsVoiceMessage`, `Voice*` (3 поля: DurationSeconds, Waveform, FileSize, FileUrl), `Files?` |
| `MessageDto` | 39 полей: полное представление. `IsSystemMessage` – логический признак. `SenderId` теперь nullable. `VoiceFileName`, `VoiceContentType` удалены. `IsPinned` вычисляется по `PinnedAt != null`. |
| `MessageFileDto` | `Id`, `MessageId`, `FileName`, `ContentType`, `Url`, `PreviewType` (file/image/video), `FileSize` |
| `MessageForwardInfoDto` | `OriginalMessageId`, `OriginalChatId`, **`OriginalSenderId?`** (теперь заполняется), `OriginalSenderName?`, `OriginalCreatedAt` |
| `MessageReplyPreviewDto` | `Id`, `ChatId`, `SenderId?`, `SenderName?`, `Content?`, `CreatedAt`, `IsDeleted`, **`IsVoiceMessage`**, **`HasPoll`**, **`FilesCount`** |
| `PagedMessagesDto` | `Messages`, `TotalCount`, `HasMoreMessages`, `HasNewerMessages`, `CurrentPage` |
| `UpdateMessageDto` | `Id`, `Content?` |

---

## 2.5 Прочие DTO

| Группа | DTO и поля |
|---|---|
| **Department** | `DepartmentDto` (Id, Name, ParentDepartmentId, Head, HeadName, UserCount), `UpdateDepartmentMemberDto` (UserId) |
| **Notification** | `NotificationDto` (Type: message/mention/poll, ChatId, ChatName, Avatar, MessageId, Sender* (`SenderId` nullable), Preview до 100 символов, CreatedAt) |
| **Online** | `UserStatusDto` (UserId, IsOnline, LastOnline, StatusType, StatusExpiresAt), `OnlineUsersResponseDto` (OnlineUserIds, TotalOnline), `SetStatusRequest` (StatusType, Duration) |
| **Poll** | `CreatePollDto` (ChatId, Question, IsAnonymous, AllowsMultipleAnswers, ClosesAt, Options), `PollDto` (+ SelectedOptionIds, CanVote), `PollOptionDto` (+ VotesCount, Votes), `PollVoteDto` (PollId, UserId, OptionId?, OptionIds?) |
| **ReadReceipt** | `MarkAsReadDto` (ChatId, MessageId), `ReadReceiptResponseDto`, `UnreadCountDto`, `AllUnreadCountsDto`, `ChatReadInfoDto` (+ FirstUnreadMessageId) |
| **Search** | `GlobalSearchMessageDto` (+ HighlightedContent, HasFiles/Voice/Poll, `SenderId` nullable), `GlobalSearchResponseDto`, `SearchMessagesResponseDto`, `SearchMessagesQueryDto` / `GlobalSearchQueryDto` (+FilterChatId) |
| **User** | `AvatarResponseDto`, `ChangePasswordDto`, `ChangeUsernameDto`, `CreateUserDto` (ФИО + DepartmentId), `ResetPasswordAdminDto`, `UserDto` (17 полей) |

---

# 3. КОНТРОЛЛЕРЫ (API.Controllers)

## BaseController\<T\>

**Путь:** `API/Controllers/BaseController.cs`

| Метод | Назначение |
|---|---|
| `GetCurrentUserId()` | Из JWT claim `sub` |
| `IsCurrentUser(int)` | Сравнение с текущим |
| `Map(Result<T>)` | Успех → 200+ApiResponse, ошибка → вызов `MapFailureToObjectResult<T>` |
| `MapFailureToObjectResult<T>(Result result)` | **Новый метод.** Создаёт `ApiResponse<T>` и возвращает HTTP-статус по `ResultErrorType` |
| `Forbidden(...)` | 403 |
| `ExecuteAsync(...)` | Обёртка с try-catch |

**Маппинг:** Unauthorized→401, Forbidden→403, NotFound→404, Conflict→409, Internal→500, default→400

---

## Контроллеры

| Контроллер | Эндпоинты | Авторизация | Rate Limit |
|---|---|---|---|
| `AuthController` | POST login, refresh, revoke | login/refresh — AllowAnonymous | `login` |
| `UsersController` | GET/PUT users, avatar, username, password, online-статусы | IsCurrentUser для изменений | — |
| `ChatsController` | CRUD, участники, роли, аватар | IsMember/Admin/Owner | — |
| `MessagesController` | CRUD, pin/unpin, пагинация (before/after/around), поиск | IsMember | `messaging`, `search` |
| `FilesController` | POST upload | IsMember | `upload` |
| `DepartmentsController` | CRUD, участники | Admin или Head | — |
| `PollsController` | Create/vote/close/get | IsMember | — |
| `ReadReceiptsController` | Mark read, unread-счётчики | Authorized | — |
| `StatusController` | Set/get | Authorized | — |
| `NotificationsController` | Настройки по чату | Authorized | — |
| `AdminController` | CRUD пользователей, бан, сброс пароля | Admin | — |

---

# 4. ХАБЫ (SignalR)

## ChatHub

**Путь:** `API/Hubs/ChatHub.cs`  
**Группы:** `user_{id}` (личные), `chat_{id}` (чат)

**Lifecycle:**
- `OnConnectedAsync` → группы + получение собственного статуса; успех → отправка `UserStatusChanged` себе и остальным, иначе → рассылка `UserOnline`
- `OnDisconnectedAsync` → обновление `LastOnline` + рассылка `UserOffline`

**Клиентские методы (вызывает клиент):**
`JoinChat(int)`, `LeaveChat(int)`, `MarkAsRead(int, int?)`, `MarkMessageAsRead(int, int)`, `SendTyping(int)`, `GetOnlineUsersInChat(int)`, `SetStatus(int, string?)`, `GetUnreadCounts()`, `GetReadInfo(int)`

**Серверные события (рассылает сервер):**
`HubMethods.Chat.UserStatusChanged`, `HubMethods.Chat.UserOnline`, `HubMethods.Chat.UserOffline`, `HubMethods.Chat.UserTyping`, `HubMethods.Chat.MessageRead`, `HubMethods.Chat.UnreadCountUpdated`, `HubMethods.Chat.ChatUpdated`, `HubMethods.Chat.ReceiveMessage`, `HubMethods.Chat.MessageUpdated`, `HubMethods.Chat.MessageDeleted`, `HubMethods.Chat.PollUpdated`, `HubMethods.Chat.ReceiveNotification`

**Изменения:** Теперь используется частичный класс с source-generated логгированием (`LogUserConnected`, `LogUserDisconnected`). Все строковые литералы заменены на константы из `Shared.Hubs.HubMethods`.

---

## CallHub

**Путь:** `API/Hubs/CallHub.cs`  
**Параметры:** MaxParticipants=12, RingingTimeout=60с

**Клиентские методы:**
`HubMethods.CallInvoke.InitiateCall(int chatId)`, `HubMethods.CallInvoke.JoinCall(string)`, `HubMethods.CallInvoke.LeaveCall(string)`, `HubMethods.CallInvoke.DeclineCall(string)`, `HubMethods.CallInvoke.CancelCall(string)`, `HubMethods.CallInvoke.SendSignal(WebRtcSignalDto)`, `HubMethods.CallInvoke.ToggleMute(string, bool)`, `HubMethods.CallInvoke.ToggleSpeaking(string, bool)`, `HubMethods.CallInvoke.SendCallMessage(string, string)`, `HubMethods.CallInvoke.GetCallState(string)`

**Серверные события:**
`HubMethods.Call.IncomingCall`, `HubMethods.Call.CallStateUpdated`, `HubMethods.Call.CallEnded`, `HubMethods.Call.CallMessageReceived`, `HubMethods.Call.CallParticipantJoined`, `HubMethods.Call.CallParticipantLeft`, `HubMethods.Call.ParticipantMuteChanged`, `HubMethods.Call.ParticipantSpeakingChanged`, `HubMethods.Call.ActiveCallStarted`, `HubMethods.Call.ActiveCallUpdated`, `HubMethods.Call.ActiveCallEnded`, `HubMethods.Call.CallError`, `HubMethods.Call.ReceiveSignal`

**Критические изменения:**
- `_userCache` теперь `ConcurrentDictionary<int, Task<(string? Name, string? Avatar)>>` (потокобезопасность).
- Метод `GetUserInfoAsync` теперь не асинхронный, а возвращает `Task` из `ConcurrentDictionary.GetOrAdd`.
- Логика `CancelCall` для групповых звонков сразу вызывает `LeaveCall`.
- `ToStateDtoAsync` загружает информацию о пользователях параллельно через `Task.WhenAll`. **Добавлена обработка ошибок**: если задача получения инфы о пользователе завершилась с ошибкой, пишется предупреждение в лог.
- **`JoinCall`** теперь обёрнут в `try-catch` с детальным логированием и отсылкой `CallError` при исключении. Добавлены дополнительные проверки: логгирование отсутствия сессии, прав доступа, неудачного присоединения.
- **`GetCallState`** также обёрнут в `try-catch`, при ошибке возвращает `null` и логирует.
- Все строковые литералы заменены на константы `HubMethods.Call` и `HubMethods.CallInvoke`.

---

# 5. MIDDLEWARE

| Middleware | Назначение |
|---|---|
| `ExceptionHandlingMiddleware` | Все исключения → 500 + ApiResponse. Dev: стектрейс, Prod: "Произошла внутренняя ошибка" |
| `MissingFileCleanupMiddleware` | 404 на `/uploads` или `/avatars` → очистка ссылок в БД (только GET/HEAD, после next) |

---

# 6. МАППИНГ (Ручной, extension-методы)

| Класс маппинга | Ключевые методы |
|---|---|
| `ChatMappings` | `.ToDto(IUrlBuilder?)`, `.ToDto(User? contact, IUrlBuilder?)` |
| `FileMappings` | `.ToDto()`, `DeterminePreviewType(contentType)` → file/image/video/audio |
| `MessageMappings` | `.ToDto(currentUserId, urlBuilder)` — теперь использует **рекурсивный обход цепочки пересылки** для получения содержимого, голосового, файлов, опроса. Добавлены методы `ResolveInForwardChain<T>` и `ResolveContentInForwardChain`. В `MessageForwardInfoDto` теперь заполняется `OriginalSenderId`. |
| `PollMappings` | `Poll.ToDto(currentUserId?)` (заполняет SelectedOptionIds, CanVote), `PollOption.ToDto(isAnonymous)` (скрывает Votes для анонимных) |
| `UserMappings` | `.ToDto(urlBuilder, isOnline?)` – теперь добавляет `StatusType` и `StatusExpiresAt`. `GetDisplayName()` – instance method на User. |

**Примечание:** Статический метод `FormatDisplayNameStatic` удалён, вместо него используется `user.GetDisplayName()`.

---

# 7. ИНФРАСТРУКТУРА API

## Утилиты (Common)

| Класс | Назначение |
|---|---|
| `AppDateTime` | Обёртка `TimeProvider`. ⚠️ Возвращает `DateTimeKind.Unspecified` вместо Utc |
| `Result<T>` / `Result` | ROP: `IsSuccess`, `IsFailure`, `Error`, `ErrorType`. Фабрики: `Success()`, `Failure()`, `NotFound()`, `Forbidden()`, `Conflict()`, `Internal()` |
| `ResultExtensions` | `UnwrapOrDefault`, `UnwrapOrFallback`, `TryUnwrap` |
| `ValidationHelper` | `ValidateUsername` (regex `^[a-z0-9_]{3,30}$`), `ValidatePassword` (≥6 символов) |
| `StatusExtensions` | `Parse(string?)` → TimeSpan: "15m", "30m", "1h", "2h", "4h", "8h", "24h" |
| `UrlHelpers` | `BuildFullUrl(string?, IUrlBuilder?)` |
| `HubMethods` | **Новый класс.** Статические константы для имён хаб-методов. Вложенные классы: `Chat`, `Call`, `ChatInvoke`, `CallInvoke`. |
| **`SystemEventMeta`** | **Новый класс в `Shared.Helpers`**. Форматирует системные сообщения и предоставляет префиксы/суффиксы для UI. Используется вместо switch в `SystemMessageFormatter` и на клиенте. |

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
| `AccessControlService` | Проверка прав с двойным кэшем (MemoryCache + per-request). Добавлен `IsSystemAdmin()` – если HttpContext пользователь в роли "Admin", все проверки ролей обходятся (bypass). |
| `OnlineUserService` | Singleton. `ConcurrentDictionary<userId, ConcurrentDictionary<connectionId, byte>>`. Очистка каждые 5 мин |
| `UserStatusService` | Статусы теперь обновляются через `ExecuteUpdateAsync` (без загрузки сущности). `GetStatusAsync` использует проекцию. `CleanupExpiredStatusesAsync` также через `ExecuteUpdateAsync`. |
| `StatusCleanupHostedService` | Фоновый: очистка истёкших статусов каждую минуту |
| `CacheService` | MemoryCache: чаты (TTL 5м, sliding 2м), членство (TTL 10м, sliding 3м) |
| `HubNotifier` | `SendToChatAsync`, `SendToUserAsync`. Глотает исключения |
| `HttpUrlBuilder` | Абсолютный URL через `IHttpContextAccessor` |
| `UdpDiscoveryService` | **Обновлён.** UDP порт 5275. Запрос: `MESSENGER_DISCOVER`, ответ: `MESSENGER_HERE:PORT` или `MESSENGER_HERE:PORT:IP`. IP определяется через `Discovery:ExternalIp` или автоматически по подсети запроса. |
| `EnumNameTranslator` | CLR → PostgreSQL snake_case для enum. Добавлен транслятор `UserStatusTypeNameTranslator`. |

---

## Репозитории

| Интерфейс | Реализация | Назначение |
|---|---|---|
| `IUserRepository` | `UserRepository` | **Обновлён.** Инкапсулирует запросы к `Users`: `FindByUsernameAsync`, `FindByIdAsync`, `FindByIdWithPasswordAsync`, `Add`. **Добавлены:** `GetAllWithSettingsAsync`, `GetWithSettingsAsync`, `UsernameExistsByOtherUserAsync`. **Удалён:** `GetAllAsync`. |
| `IRefreshTokenRepository` | `RefreshTokenRepository` | Управление токенами: отзыв семейства, отзыв всех для пользователя, удаление истёкших, активные семьи. **Удалён:** `GetActiveByUserIdAsync`. |
| `IChatRepository` | `ChatRepository` | **Существенно расширен и очищен.** Удалены методы: `GetByIdsAsync`, `GetMemberAsync`, `GetChatTypeAsync`. **`GetLastMessagesAsync`** теперь разрешает превью содержимого, голосового, файлов и опроса по цепочке пересылки (если исходное сообщение не имеет контента, проверяется `ForwardedFromMessage`). |
| `IMessageRepository` | `MessageRepository` | **Кардинально переработан.** **Удалены методы:** `FindForBroadcastAsync`, `GetPagedAsync`, `GetWithIncludesAsync`, `GetAroundAsync`. **Упрощены сигнатуры:** `GetBeforeAsync`, `GetAfterAsync` и др. с параметром `cutoff`. **Добавлены:** `PinAsync`, `UnpinAsync`. `GetUserMessagesForMixedAsync` теперь использует строгое `<` вместо `<=` для правильной пагинации. **Добавлен метод** `FindUserMessageWithIncludesNoTrackingAsync` для загрузки сообщения без отслеживания изменений. |
| **`IReadReceiptRepository`** | **`ReadReceiptRepository`** | **Новый.** Все операции с отметками о прочтении. **Добавлен:** `GetUnreadCountsForUsersAsync`. Метод `UpdateReadPointerAsync` больше не вызывает `SaveChangesAsync` (контекст управляется сервисом). |
| **`IPollRepository`** | **`PollRepository`** | **Новый.** Управление опросами и голосами. Методы упорядочены. |

Репозитории используются в `AuthService`, `AdminService`, `UserService`, `ChatService`, `MessageService`, `PollService`, `ReadReceiptService`.

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

**RefreshTokenAsync:**
- `UsedAt != null` ИЛИ `RevokedAt != null` → reuse detected → отзыв всей семьи
- Ротация: UsedAt = now, новый токен с тем же FamilyId
- **Изменения:** Теперь использует `IRefreshTokenRepository` и `IUserRepository` для абстракции доступа к данным. Обновлён поиск refresh-токена через `FindByHashAsync`.

**TokenService:**
- HMAC-SHA256, `ClockSkew=Zero`, секрет ≥32 символов (проверяется при старте)
- Refresh: 64 случайных байта в Base64

---

## Бизнес-сервисы

| Сервис | Строк | Ключевое поведение |
|---|---|---|
| `CallSessionService` | 141 | Singleton. `ConcurrentDictionary`. Не масштабируется. Длительность звонка вычисляется как `DateTimeOffset.UtcNow - session.StartedAt`. |
| `ChatService` | ~440 | Переведён на `IChatRepository` и `IUserRepository`. Загрузка последних сообщений через `GetLastMessagesAsync`, диалогов — через `GetDialogPartnersAsync`. Участники загружаются проекцией `GetMembersWithUsersAsync`. Удаление чата использует `GetVoiceFilePathsAsync`. Внутренний класс `RawLastMessage` удалён. **Изменения:** Отправка `ChatUpdated` теперь использует `HubMethods.Chat.ChatUpdated`. **При загрузке аватара** создаётся системное сообщение `ChatAvatarUpdated` и рассылается уведомление `ChatUpdated` всем участникам. |
| `ChatMemberService` | 100 | Инвалидация кэша после операций |
| `DepartmentService` | 218 | BFS для проверки циклов в иерархии. Использует проекцию для списка пользователей. |
| `FileService` | 112 | Изображения → WebP (JPEG/PNG/GIF/WebP/BMP). Путь: `wwwroot/uploads/chats/{chatId}/{guid}{ext}` |
| `MessageService` | ~590 | **Значительно изменён.** Все вызовы хаба теперь используют `HubMethods.Chat.*`. `BroadcastToMembersAsync` теперь отправляет одно сообщение в чат (`SendToChatAsync`) вместо индивидуальной рассылки. `NotifyAndUpdateUnreadAsync` использует пакетное получение unread-счётчиков через `GetUnreadCountsForUsersInChatAsync`. **`CreateMessageAsync`** теперь разрешает корневое пересланное сообщение через `ResolveRootForwardedMessageIdAsync` и автоматически подставляет его контент, если текущее сообщение отправлено без текста. **`PinMessageAsync`** теперь проверяет, не закреплено ли уже сообщение; после закрепления использует `FindUserMessageWithIncludesNoTrackingAsync`, отправляет `MessageUpdated` и создаёт системное сообщение `MessagePinned`. **`UnpinMessageAsync`** аналогично отправляет `MessageUpdated` и создаёт `MessageUnpinned`. |
| `NotificationService` | 98 | Для Contact: ChatName = имя отправителя. Preview ≤100 символов. Отправка через `HubMethods.Chat.ReceiveNotification`. |
| `PollService` | ~150 | **Изменения:** Внедрён `TimeBundle` для консистентности `DateTime`. Проверка `ClosesAt` теперь `HasValue && ClosesAt < now`. Операции закрытия используют `_appDateTime.UtcNow`. Работа с транзакцией улучшена: блок `try-catch`. Отправка событий через `HubMethods.Chat.ReceiveMessage` и `HubMethods.Chat.PollUpdated`. |
| `ReadReceiptService` | ~90 | Полный переход на `IReadReceiptRepository`. `MarkAsReadAsync` и `MarkMessageAsReadAsync` теперь управляют сохранением контекста (`SaveChangesAsync`) явно, вместо делегирования этого репозиторию. **Добавлен метод** `GetUnreadCountsForUsersInChatAsync`. Баг с двойным вызовом хаба исправлен. |
| `AdminService` | 149 | Использует репозитории `IUserRepository`, `IRefreshTokenRepository` для операций с пользователями и токенами. `GetUsersAsync` использует `GetAllWithSettingsAsync` и маппит `UserWithSettingsProjection` в `UserDto`. |
| `UserService` | 180 | **Существенно изменён.** `GetAllUsersAsync` и `GetUserAsync` используют `GetAllWithSettingsAsync`/`GetWithSettingsAsync` и новый метод `MapProjectionToDto`. `ChangeUsernameAsync` использует `UsernameExistsByOtherUserAsync`. Весь маппинг проекций централизован. Логирование обновлено. |
| `SystemMessageService` | 49 | Создаёт экземпляры `SystemMessage`, использует поле `InitiatorId`. Отправка через `HubMethods.Chat.ReceiveMessage`. |
| `SystemMessageFormatter` | 23 | Делегирует форматирование классу `SystemEventMeta` из `Shared.Helpers`. Fallback: "Пользователь"/"пользователя". Поддерживает все типы событий, включая `ChatAvatarUpdated`. |

---

# 9. АБСТРАКЦИИ API

| Интерфейс | Ключевые методы |
|---|---|
| `IAuthService` | `LoginAsync`, `RefreshTokenAsync`, `RevokeRefreshTokenAsync` |
| `ITokenService` | `GenerateTokenPair`, `ValidateToken`, `GetPrincipalFromExpiredToken`, `HashToken` |
| `ICallSessionService` | `CreateCallAsync`, `JoinCall`, `LeaveCall`, `EndCallAsync`, `ToStateDto` |
| `IChatService` | `GetUserChatsAsync`, `GetContactChatAsync`, `CreateChatAsync`, `UpdateChatAsync`, `DeleteChatAsync` |
| `IChatMemberService` | `AddMemberAsync`, `RemoveMemberAsync`, `UpdateRoleAsync`, `GetMembersAsync` |
| `IMessageService` | `CreateMessageAsync`, `GetChatMessagesAsync`, `GetMessagesAroundAsync`, `SearchMessagesAsync`, `GlobalSearchAsync`, `PinMessageAsync` |
| `IPollService` | `CreatePollAsync`, `VoteAsync`, `ClosePollAsync`, `GetPollAsync` |
| `IReadReceiptService` | `MarkAsReadAsync`, `GetUnreadCountAsync`, `GetAllUnreadCountsAsync`, `GetChatReadInfoAsync` |
| `IDepartmentService` | `GetDepartmentsAsync`, `CreateDepartmentAsync`, `UpdateDepartmentAsync`, `DeleteDepartmentAsync` |
| `IFileService` | `SaveImageAsync`, `SaveMessageFileAsync`, `DeleteFile`, `IsValidImage` |
| `IUserService` | `GetAllUsersAsync`, `GetUserAsync`, `UpdateUserAsync`, `UploadAvatarAsync`, `ChangeUsernameAsync`, `ChangePasswordAsync` |
| `IAdminService` | `GetUsersAsync`, `CreateUserAsync`, `UpdateUserAsync`, `ToggleBanAsync`, `ResetPasswordAsync` |
| `INotificationService` | `SendNotificationAsync`, `SendMentionNotificationAsync`, `SetChatMuteAsync` |
| `ISystemMessageService` | `CreateAsync`, `CreateCallStartedMessageAsync`, `CreateCallEndedMessageAsync` |
| `IUserStatusService` | `SetStatusAsync`, `GetStatusAsync`, `CleanupExpiredStatusesAsync` |
| `IOnlineUserService` | `UserConnected`, `UserDisconnected`, `IsOnline`, `GetOnlineUserIds`, `FilterOnline` |
| `IHubNotifier` | `SendToChatAsync(chatId, method, args)`, `SendToUserAsync(userId, method, args)` |
| `ICacheService` | `GetUserChatIdsAsync`, `GetMembershipAsync`, `InvalidateUserChats`, `InvalidateMembership`, `InvalidateChat` |
| `IAccessControlService` | `IsMemberAsync`, `IsAdminAsync`, `IsOwnerAsync`, `EnsureMemberOfAsync`, `GetChatMemberIdsAsync` |
| `IUrlBuilder` | `BuildUrl(string?)` |
| **`IUserRepository`** | `FindByUsernameAsync`, `FindByIdAsync`, `FindByIdWithPasswordAsync`, `UsernameExistsAsync`, `Add` |
| **`IRefreshTokenRepository`** | `RevokeByFamilyIdAsync`, `RevokeAllForUserAsync`, `DeleteExpiredAsync`, `GetActiveFamiliesAsync` |
| **`IChatRepository`** | `FindByIdAsync`, `FindByIdWithMembersAsync`, `IsMemberAsync`, `GetByIdsLightAsync`, `GetLastMessagesAsync`, `GetDialogPartnersAsync`, `UpdateLastMessageTimeAsync`, `GetMembersWithUsersAsync`, `GetVoiceFilePathsAsync`, `GetChatTypeAsync`, `GetShowHistoryForNewMembersAsync`, `GetContactChatsWithMembersAsync`, `SearchGroupChatsAsync`, `GetMembersForNotificationAsync`, `GetHistoryRestrictionsAsync`, `Add`, `AddMember`, `RemoveMember` |
| **`IMessageRepository`** | `FindUserMessageByIdAsync`, `FindUserMessageWithIncludesAsync`, `FindUserMessageForDeleteAsync`, `FindForBroadcastAsync`, `GetWithIncludesAsync`, `GetBeforeAsync`, `GetAfterAsync`, `GetUserMessagesForMixedAsync`, `GetSystemMessagesAsync`, `GetPinnedAsync`, `CountAsync`, `HasOlderAsync`, `HasNewerAsync`, `ExistsInChatAsync`, `ExistsAsync`, `SearchInChatAsync`, `SearchGlobalAsync`, `GetForwardedToChatIdsAsync`, `SoftDeleteAsync`, `PinAsync`, `UnpinAsync`, `Add`, `RemoveVoiceMessage`, **`FindUserMessageWithIncludesNoTrackingAsync`** |
| **`IReadReceiptRepository`** | `FindMemberAsync`, `FindMemberReadonlyAsync`, `UpdateReadPointerAsync`, `CountUnreadAsync`, `GetUnreadInfoAsync`, `GetAllUnreadCountsAsync`, `GetUnreadCountsAsync`, `MessageExistsAsync`, `GetLastMessageIdAsync` |
| **`IPollRepository`** | `FindByIdWithDetailsAsync`, `Add(Poll)`, `AddOption(PollOption)`, `AddVote(PollVote)`, `GetUserVotesAsync`, `RemoveVotes(IEnumerable<PollVote>)`, `CloseAsync` |

---

# 10. ПЕРЕЧИСЛЕНИЯ (Shared.Enum)

| Enum | Значения | Примечание |
|---|---|---|
| `CallEndReason` | Ended, Cancelled, Timeout, Declined | |
| `CallStatus` | Ringing, Active, Ended | |
| `ChatRole` | Member, Admin, Owner | |
| `ChatType` | Chat, Department, Contact, DepartmentHeads | DepartmentHeads → `"department_heads"` (EnumMember) |
| `SystemEventType` | ChatCreated, MemberAdded, MemberRemoved, MemberLeft, RoleChanged, CallStarted, CallEnded, MessagePinned, MessageUnpinned, **ChatAvatarUpdated** | `[JsonStringEnumConverter]` |
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

**Boolean:** `BoolToString` (разделитель `|`), `BoolToGeometry`, `BoolToDouble` (Rotation/Opacity), `BoolToColor`, `BoolToHAlignment`, `BoolToBrush`, `BoolToThickness`, `BooleanAnd` (Multi), `BooleanOr` (Multi), `EnumEquals`, `EnumNotEquals`, `UserRoleToVisibility`

**DateTime:** `DateTimeFormatConverter` (форматы: Time/Date/ShortDate/DateTime/Chat/Relative), `LastMessageDateConverter`, `LastSeenTextConverter` (Multi)

**Domain:** `ChatRoleToDisplay`, `ContentFilterToLabel`, `InitialsConverter`, `LevelToMargin` (20px×level), `LevelToVisibility`, `SearchScopeToTitle/Watermark/Hint/MessagesHeader`, `ThemeToDisplay`

**Generic:** `ComparisonConverter` (Equal/NotEqual/GreaterThanZero/Zero), `IndexToText`, `HasContentConverter`, `HasTextOrAttachmentsMultiConverter` (Multi), `MultiplyConverter`, `PercentToWidthConverter` (Multi, min 8px), `PluralizeConverter` (правила: n|один|два|пять), `ResourceKeyToGeometryConverter`

---

# 13. DESKTOP — ЛОКАЛЬНАЯ БД (Desktop.Data, SQLite)

## LocalDatabase

**Путь:** `Desktop/Data/LocalDatabase.cs`
- WAL-режим, `synchronous=NORMAL`, `cache_size=-4000`, `mmap_size=33554432`
- Миграции через `PRAGMA user_version` (текущая: **2**, была 1)
- FTS5 для полнотекстового поиска с триггерами
- Потокобезопасность: `SemaphoreSlim`

## Cached-модели

| Модель | Таблица | Особенности |
|---|---|---|
| `CachedMessage` | `messages` | 30+ колонок, `poll_json`/`files_json`, даты в Ticks. Поле `sender_id` теперь `int?`. Добавлены поля `reply_is_voice`, `reply_has_poll`, `reply_files_count`. |
| `CachedChat` | `chats` | 18 колонок, `contact_*`, Ticks |
| `CachedUser` | `users` | id, username, display_name, avatar, cached_at |
| `CachedReadPointer` | `read_pointers` | chat_id (PK), last_read, first_unread, unread_count |
| `ChatSyncState` | `chat_sync_state` | OldestLoadedId, NewestLoadedId, has_more_older/newer |
| **`CachedDownloadedFile`** | **`downloaded_files`** | file_id (PK), message_id, local_path, file_name, file_size, downloaded_at, content_type. Хранит состояние скачанных файлов для `FileDownloadStateService`. |

## Репозитории и сервисы

| Класс | Назначение |
|---|---|
| `MessageCacheRepository` | CRUD, FTS5→LIKE fallback, `TrimOldMessages(keepPerChat=200)` |
| `ChatCacheRepository` | Upsert, `UpdateLastMessageAsync` |
| `LocalCacheService` | API: пакеты, SyncState, ReadPointer, поиск |
| `CacheMapper` | `MessageDto↔CachedMessage`, `ChatDto↔CachedChat`. Source Generated JSON (`CacheJsonContext`). Добавлены поля для превью ответа: `ReplyIsVoice`, `ReplyHasPoll`, `ReplyFilesCount`. Удалены устаревшие поля `VoiceFileName`, `VoiceContentType`. |
| **`DownloadedFileRepository`** | **Новый.** Управление записями о скачанных файлах в локальной БД. |

---

# 14. DESKTOP — ИНФРАСТРУКТУРА

## Конфигурация

| Класс | Назначение |
|---|---|
| `ApiEndpoints` | Статический билдер URL всех эндпоинтов |
| `AppConstants` | `MaxFileSizeBytes=20MB`, `DefaultPageSize=50`, `LoadMorePageSize=30`, `SearchPageSize=20`, `TypingIndicatorDurationMs=3500` |
| `ServiceCollectionExtensions` | `AddMessengerCoreServices(apiBaseUrl)`, `AddMessengerViewModels()`. **Добавлены регистрации** `IDownloadedFileRepository`, `IFileDownloadStateService`. |

## Хелперы

| Класс | Назначение |
|---|---|
| `AvatarHelper` | `GetSafeUri`, `GetUriWithCacheBuster` (хэш → query param), **добавлен метод `WithFreshCacheBuster(avatarUrl)`** — добавляет `?v=timestamp` для принудительного обновления. |
| `MimeTypeHelper` | `GetMimeType(extension)` — словарь |
| `ChatPreviewFormatter` | `BuildPreview`, `BuildPreviewWithMeta`, `BuildReplyPreview` (для `MessageReplyPreviewDto` — учитывает голосовые, опросы, файлы, использует склонение), **метод `Pluralize` сделан публичным**. **Форматирование системных сообщений теперь делегируется `SystemEventMeta.Format`**. |
| `HttpResponseHelper` | `TryExtractErrorMessage` — десериализация `ApiResponse.Error` |
| `PasswordHelper` | `CalculateStrength(0–4)`, `ToStrengthLabel` |
| `RangeObservableCollection<T>` | `AddRange`, `InsertRange`, `RemoveRange` — одно Reset-событие |

## Медиа

| Класс | Назначение |
|---|---|
| `AuthenticatedImageLoader` | LRU RAM (80 items/30MB), LOH-защита >85KB, дедупликация, дисковый кэш, Bearer-токен. **Добавлены методы:** `InvalidateUrl(string)` — удаляет конкретный URL из RAM и дискового кэша; `InvalidateByRelativePath(string)` — удаляет все записи, содержащие заданный относительный путь; `IsCached(string)` — проверяет наличие в RAM. |
| `RemoteImage` | Attached Property для Avalonia Image. **Свойство `CurrentUrlProperty` стало публичным.** При изменении источника теперь проверяется, находится ли URL в кэше через `IsCached`; если нет — принудительно перезагружается. Добавлена очистка старого Bitmap при сбросе источника. |
| `ImageCacheService` | ⚠️ Дублирует `AuthenticatedImageLoader` (deprecated) |
| `MemoryDiagnostics` | Счётчики ChatVM/MessageVM/Bitmap/RemoteImage, LOH, дамп GC. **Диагностика памяти полностью активирована** (ранее была закомментирована). Включены методы `Dump`, `DumpDetailed`, ForceFullGc, LOH-эксперимент. |

---

# 15. DESKTOP — СЕРВИСЫ

## Auth

| Сервис | Назначение |
|---|---|
| `AuthService` | Login, Refresh, Revoke, Ping, `IsAccessTokenValid` |
| `SessionStore` | In-Memory: Token, RefreshToken, UserId, UserRole, события |
| `SecureStorageService` | DPAPI/KeyChain/AES |
| `AuthManager` | `InitializeAsync`, `LoginAsync`, `TryRefreshTokenAsync`, `LogoutAsync` |

## API & Realtime

| Сервис | Строк | Назначение |
|---|---|---|
| `ApiClientService` | 376 | HTTP + авто-рефреш 401 |
| `GlobalHubConnection` | ~460 | SignalR `/chatHub`, 15+ событий. **Добавлен retry при 503 ServiceUnavailable.** Все строковые литералы заменены на `HubMethods`. `UserOnline` только логируется. Исправлена двойная отправка `MarkAsRead` (удалён лишний вызов `MarkAsRead` при обновлении указателя). Добавлены логи для `MessageUpdated`. |
| `CallHubConnection` | ~260 | SignalR `/callHub`, 12 событий. **Добавлен retry при 503.** Все строковые литералы заменены на `HubMethods`. **Метод `JoinCallAsync` теперь проверяет состояние подключения** и ловит ошибки, логируя их. Улучшена обработка состояния подключения при `Connecting`. **Отписка от событий в `DisconnectAsync` убрана** (комментарии удалены). |

## Call

| Сервис | Строк | Назначение |
|---|---|---|
| `CallHubConnection` | 260 | SignalR `/callHub`, 12 событий |
| `CallService` | 368 | Оркестратор. **Теперь вызывает событие `CallStarted` после успешного `InitiateCallAsync`.** UDP-аудио: `[userId:4][seq:4][opus:N]`. Endpoint discovery через SignalR |
| `CallAudioService` | 271 | PortAudio. 48kHz/моно/20ms. Opus 32kbps. VAD адаптивный (noiseFloor*2.5) |
| `NoiseReducer` | 322 | Спектральное шумоподавление: FFT→Wiener Filter→Gate. Decision-Directed SNR α=0.96 |
| `ActiveCallStore` | 34 | ObservableObject: `ActiveCall`, `IsCallUiOpen`, `IsInCall` |

**CallAudioService параметры:**
- SampleRate: 48000, Channels: 1, FrameDuration: 20ms, FrameSamples: 960
- Opus: 32kbps, Complexity=5, VOIP mode
- VAD: noiseFloor α=0.005, hold 1200ms, debounce 150ms

## Media

| Сервис | Строк | Назначение |
|---|---|---|
| `AudioPlayerService` | 247 | WAV через PortAudio. Play/Pause/Resume/Stop/Seek(0.0–1.0). Таймер позиции 50ms |
| `AudioRecorderService` | 252 | 16kHz/моно/16-bit PCM. WAV+Waveform(100 баров). `AudioRecordingResult` без FileName/ContentType |
| `FileDownloadService` | 186 | Скачивание+прогресс (шаг 1%), OS-открытие, кроссплатформенный Downloads |
| **`FileDownloadStateService`** | **Новый** | Управляет состоянием скачанных файлов: регистрация после загрузки, получение статуса (`GetStateAsync`), сброс. Взаимодействует с `IDownloadedFileRepository`. |

**AudioRecordingResult:** `AudioStream`, `FileName` (voice_YYYYmmdd_HHmmss.wav), `ContentType` (audio/wav), `Duration`, `Waveform` (Base64, 100 баров)

## Platform

| Сервис | Назначение |
|---|---|
| `PlatformService` | Clipboard |
| `SettingsService` | JSON в AppData |
| `ThemeService` | `Application.RequestedThemeVariant` |
| `NavigationService` | Стек истории, проверка авторизации |
| `ServerDiscoveryService` | **Обновлён.** Парсит ответы в формате `MESSENGER_HERE:PORT:IP` |
| `NotificationService` | Стек ≤3, анимация прогресс-бара |
| `DialogService` | Стек диалогов, `Channel<CloseRequest>`, анимация (таймаут 1с) |
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
| `ICallHubConnection` | 10 методов + 12 событий WebRTC |
| `ICallService` | `StartCallAsync`, `JoinCallAsync`, `LeaveCallAsync`, `ToggleMuteAsync`, события |
| `IDialogService` | `ShowAsync<T>`, `CloseAsync`, `CloseAllAsync` |
| `IFileDownloadService` | `DownloadFileAsync(progress?)`, `OpenFileAsync`, `OpenFolderAsync` |
| **`IFileDownloadStateService`** | `GetStateAsync(MessageFileDto)`, `RegisterDownloadAsync`, `ResetAsync` |
| `IGlobalHubConnection` | `ConnectAsync`, `DisconnectAsync`, 15+ событий |
| `INavigationService` | `NavigateToLogin`, `NavigateToMainMenu`, `NavigateTo<T>`, `GoBack` |
| `ISecureStorageService` | `SaveAsync<T>`, `GetAsync<T>`, `RemoveAsync` |
| `ISessionStore` | Token, RefreshToken, UserId, UserRole, `HasRole(UserRole)`, события |
| `ISettingsService` | `Get<T>(key)`, `Set<T>(key, value)` |
| `IThemeService` | `Toggle`, `LoadFromSettings`, `SaveTheme` |
| Остальные | `INotificationService`, `IPlatformService`, `IServerDiscoveryService`, `ICacheMaintenanceService`, `IChatNotificationApiService`, `IChatInfoPanelStateStore`, `IAuthService` |

---

# 17. DESKTOP — ФАБРИКИ

**Путь:** `Desktop/ViewModels/ChatList/Factories/`

| Класс | Назначение |
|---|---|
| `CacheServices` | `ILocalCacheService` + `ICacheMaintenanceService` |
| `CallServices` | `ICallService` + `ActiveCallStore` |
| `ChatCoreServices` | `IChatService` + `IMessageService` + `IPollService` |
| `MediaServices` | `IAudioPlayerService` + `IAudioRecorderService` + `IFileDownloadService` + **`IFileDownloadStateService`** |
| `ChatViewModelDependencies` | Полный набор для `ChatViewModel`, включает **`IFileDownloadStateService`** |
| `ChatViewModelFactory` | Фабрика `ChatViewModel(chatId)` |
| `ChatsViewModelFactory` | Фабрика `ChatsViewModel` |

---

# 18. DESKTOP — VIEW MODELS

## Chat Core

### ChatViewModel

**Путь:** `Desktop/ViewModels/Chat/Core/ChatViewModel.cs`  
**Строк:** 1067 (самый большой)  
**Имплементирует:** `IAsyncDisposable`

**Handler'ы:**

| Handler | Строк | Назначение |
|---|---|---|
| `MessageManager` | 599 | Сообщения, пагинация, кэш |
| `Attachments` | 169 | Вложения, upload |
| `MemberLoader` | 38 | Участники |
| `EditDelete` | 123 | Редактирование, удаление, pin, copy |
| `Reply` | 55 | Ответы, scroll to original |
| `Forward` | 78 | Пересылка через ChatPicker |
| `Typing` | 101 | Индикатор печати |
| `Voice` | 171 | Голосовые сообщения |
| `InfoPanel` | 409 | Панель информации |
| `Search` | 62 | Поиск по чату |
| `Notification` | 54 | Mute/unmute |

**Добавлена команда `ShowPollResults`** в `ChatCommands`, которая открывает `PollResultsDialogViewModel`.

**@Mentions:** regex поиск `@token` перед кареткой → фильтр участников → выпадающий список (≤7). Клавиши: ↓↑ навигация, Enter выбор, Escape закрытие.

**Инициализация (порядок):**
1. Загрузка ChatDto
2. Проверка активного звонка (ожидание подключения к CallHub до 3 сек)
3. ReadInfo
4. Настройки уведомлений
5. Участники
6. Закреплённые сообщения
7. Сообщения (кэш → API)
8. Контакт-пользователь (для Contact)
9. Обновление инфопанели

**Новые возможности:**
- **PinnedBannerPreviewText**: формирует превью "Имя: содержимое" для баннера закреплённого сообщения, обновляется при изменении ContentPreview/SenderName.
- **Аватар чата**: при `ChatUpdated` инвалидирует кэш изображений при смене аватара и не сбрасывает всю историю сообщений.
- **Звонки**: `_chatActiveCallId` отслеживает ID текущего активного звонка для фильтрации событий. `JoinActiveCallAsync` открывает UI звонка, если пользователь уже в активном звонке этого чата.
- Публичный метод `RequestScrollToBottom`.

**Dispose:** `Interlocked`-защита от двойного, `DisposeAsync` + `DisposeCommonResources`

---

### ChatMessageManager

**Строк:** 599

| Метод | Назначение |
|---|---|
| `LoadInitialMessagesAsync` | Кэш → API, возвращает индекс для скролла |
| `LoadOlderMessagesAsync` | Пагинация вверх |
| `LoadNewerMessagesAsync` | Пагинация вниз |
| `LoadMessagesAroundAsync(messageId)` | Вокруг сообщения → индекс |
| `AddReceivedMessage(MessageDto)` | Из SignalR или своё. `SenderId` обрабатывается как nullable. |
| `HandleMessageUpdated/Deleted/PollUpdated` | Обновления из SignalR. **`HandleMessageUpdated`** всегда считает изменение состояния закрепления (pinChanged) при отсутствии существующего сообщения. |
| `GapFillAfterReconnectAsync` | Заполнение пробелов после реконнекта |
| `ResetToLatestAsync` | Полная перезагрузка |

**Стратегия:** кэш → мгновенный показ → фоновое обновление из API. Синхронизация `ChatSyncState`.  
**Изменение:** При создании `MessageViewModel` теперь передаёт `_userId` и `IFileDownloadStateService`.

---

### MessageViewModel

**Строк:** 730

| Свойство | Тип | Назначение |
|---|---|---|
| `Id`, `ChatId`, `SenderId` (nullable), `SenderName`, `Content`, `CreatedAt` | Базовые | |
| `IsOwn`, `ShowSenderName`, `IsDeleted`, `IsEdited`, `IsPinned` (по PinnedAt != null), `IsSystemMessage` | Состояние | |
| `ReplyToMessage`, `ForwardedFrom` | Вложенные | |
| `Poll` | `PollViewModel?` | |
| `Files` | `List<MessageFileViewModel>` | |
| `GroupPosition` | `MessageGroupPosition` | Top/Middle/Bottom/Single |
| `IsHighlighted` | `bool` | Подсветка 3 секунды |
| `ShowPollResultsButton` | `bool` | Управляется свойством `ShowResultsButton` опроса |
| **`OriginalIsVoiceMessage`** | `bool` | Запоминает исходный тип (голосовое) |
| **`OriginalHasPoll`** | `bool` | Запоминает наличие опроса |
| **`ContentPreview`** | `string` | Превью для отображения самого сообщения (в баббле) и для панели ответа; генерируется методом `BuildSelfPreview()` |
| **`ShowVoiceMessage`** | `bool` | Теперь управляется `OriginalIsVoiceMessage`, не сбрасывается при удалении |
| **`ForwardedFromSenderId`** | `int?` | ID отправителя пересланного сообщения |
| **`CanOpenForwardSenderProfile`** | `bool` | Можно открыть профиль переславшего (если есть ID) |
| `SystemActionPrefixText` / `SystemActionSuffixText` | `string` | Теперь заполняются через `SystemEventMeta.GetPrefix/GetSuffix` |

**Конструктор:** теперь принимает `IFileDownloadStateService? stateService`. При наличии файлов после создания `FileViewModels` вызывает асинхронную инициализацию состояний загрузки.

---

### ChatHubSubscriber

**Строк:** 81. Фильтрация по `chatId`. Диспетчеризация через `Dispatcher.UIThread.Post`. Подписки: `MessageReceivedGlobally`, `MessageUpdatedGlobally`, `MessageDeletedGlobally`, `PollUpdatedGlobally`, `MessageRead`, `UnreadCountChanged`, `Reconnected`

---

## Главные ViewModel

### MainMenuViewModel

**Строк:** 700

**Вкладки:**

| Индекс | Вкладка | ViewModel |
|---|---|---|
| 0 | Настройки | `SettingsViewModel` |
| 1–2 | Чаты | `ChatsViewModel` (groups) |
| 3 | Профиль | `ProfileViewModel` |
| 4 | Админ | `AdminViewModel` |
| 5 | Контакты | `ChatsViewModel` (contacts) |
| 6 | Отделы | `DepartmentManagementViewModel` |

**Функции:** поиск (GlobalSearchManager), звонки (IncomingCall→IncomingCallViewModel→CallViewModel), статусы (UserStatusChanged), открытие чатов, уведомления (переход к сообщению), профили, опросы, редактирование групп. При инициализации загружается собственный статус пользователя.

**Изменения:**
- **Управление жизненным циклом CallHub:** При инициализации и реконнектах корректно отписывается от событий `IncomingCall`, `CallStateUpdated` перед отключением и подписывается заново после.
- **Обработка `OnIncomingCall`:** Попытка переподключения к CallHub, если не в сети; защита от повторного входа в звонок.
- **Обработка `OnCallAcceptedAsync`:** Добавлена подписка на `CallError` для перехвата ошибок присоединения, задержка 200 мс перед получением `CallState`, fallback-состояние из `CallInviteDto` при неудаче. UI-операции вызываются без ожидания.
- **`ShowCallViewAsync` → `ShowCallViewSync`:** Стал синхронным методом.
- **Добавлены методы `NavigateToForwardedChatAsync`** (открывает чат после пересылки), **`OpenCallUi`**, **`ShowCallView`**.

**Цвета статусов:** Online=#43A047, Away=#FFA000, Busy=#E53935, DND=#9C27B0, Offline=#9E9E9E

---

## Feature Handlers

### ChatEditDeleteHandler

`StartEdit`, `SaveEdit` (пустой → Delete), `CancelEdit`, `DeleteMessage`, `TogglePin` (установка/снятие через `PinnedAt`), `CopyMessageText`. **При toggle pin теперь вызывает `Ctx.RaisePinStateChanged(result.Data)` для немедленного обновления баннера.**

### ChatReplyHandler

`StartReply`, `CancelReply`, `ScrollToReplyOriginal` (если не загружено → `LoadMessagesAroundAsync` → highlight 3с)

### ChatForwardHandler

Загрузка чатов → `ChatPickerDialogViewModel` → `POST /messages` с `ForwardedFromMessageId`. **После успешной пересылки осуществляет навигацию к целевому чату через `IChatNavigator.NavigateToForwardedChatAsync` (если навигатор доступен), иначе показывает уведомление.**

**Превью пересылаемого:** удалено/"голосовое"/"опрос"/"📎 N файл(ов)"/текст≤100 символов

### ChatTypingHandler

Отправка при вводе → `Hub.SendTypingAsync`. Очистка каждые 500ms для записей старше 3500ms. Форматирование: 1 пользователь → `"{имя} печатает..."`, несколько → `"Несколько человек печатают..."`

### ChatInfoPanelHandler

**Строк:** 409. Секции: Медиа (пагинация 30/страница), Документы, Опросы (поиск), Участники (поиск). **Участники теперь сортируются: сначала онлайн (IsOnline), затем по алфавиту.**

Для Contact: загрузка полного профиля, LastSeen формат: <1мин/"X мин. назад"/"X ч. назад"/"вчера"/"X дн. назад"/DD.MM.YYYY

Подписки: `UserStatusChanged`, `UserProfileUpdated`, `MemberJoined`, `MemberLeft`. **При обновлении аватара контакта или чата теперь инвалидирует кэш `AuthenticatedImageLoader` по относительному пути, чтобы гарантировать перезагрузку изображения.**

### ChatAttachmentManager

`PickAndAddFilesAsync`, `AddFileAsync` (проверка 20MB, MIME), `UploadAllAsync → List<MessageFileDto>`. Для изображений: thumbnail до 200px.

### ChatVoiceHandler

Запись: `StartRecording` → авто-стоп 300с. Отправка: `StopAndSend` → проверка >0.5с → upload → POST. Минимум: 0.5с, максимум: 300с. Больше не отправляет `FileName/ContentType`.

### ChatNotificationHandler

`LoadSettingsAsync`, `Toggle` (с `IsLoadingMuteState` защитой). Показывает уведомление при переключении.

### ChatSearchHandler

`ScrollToMessageAsync(messageId)`: найти в загруженных → если нет → `LoadMessagesAroundAsync` → highlight. `AppConstants.HighlightDurationMs=3000`

---

# 19. ТИПИЧНЫЕ ПОТОКИ ДАННЫХ

## Отправка сообщения

```
Пользователь → ChatViewModel.SendMessage()
  → ChatAttachmentManager.UploadAllAsync() [если есть файлы]
  → POST /api/messages (CreateMessageRequest)
  → MessageService.CreateMessageAsync()
    → Проверка доступа (AccessControlService)
    → Извлечение @mentions
    → Сохранение UserMessage + MessageFiles
    → ChatMember.LastMessageTime update
    → HubNotifier.SendToChatAsync("ReceiveMessageDto")
    → NotificationService (для каждого участника)
  → ChatHub → все клиенты чата
  → ChatHubSubscriber.MessageReceivedGlobally
  → ChatMessageManager.AddReceivedMessage()
  → MessageViewModel создаётся
  → ObservableCollection обновляется
  → UI рендерит новое сообщение
```

## Звонок (WebRTC)

```
Инициатор → CallService.StartCallAsync(chatId)
  → CallHub.InitiateCall(chatId)
  → CallSessionService.CreateCallAsync()
  → Рассылка IncomingCall всем участникам чата
  → Участник видит IncomingCallViewModel
  → Принимает → CallService.JoinCallAsync()
  → CallHub.JoinCall()
  → CallSession.Status = Active
  → CallStateUpdated → все участники
  → UDP endpoint обмен через SignalR (udp-endpoint сигнал)
  → Прямой UDP аудио между участниками
```

## Обновление токенов

```
ApiClientService получает 401
  → AuthManager.TryRefreshTokenAsync()
  → POST /api/auth/refresh (RefreshTokenRequest)
  → AuthService.RefreshTokenAsync()
    → GetPrincipalFromExpiredToken(accessToken)
    → Поиск RefreshToken по хэшу
    → Проверка: UsedAt/RevokedAt/Expired/Banned
    → Ротация: UsedAt=now, новый токен с FamilyId
  → SessionStore.Token/RefreshToken обновляются
  → Повтор оригинального запроса с новым токеном
```

---

# 20. DESKTOP — VIEWS (Слой представлений)

## 20.1 App.axaml.cs — Точка входа

**Путь:** `Desktop/App.axaml.cs` (255 строк)

**Назначение:** Корневой класс приложения Avalonia. Управляет DI-контейнером, обнаружением сервера, инициализацией БД и жизненным циклом.

### Последовательность запуска

```
Initialize()
  1. BuildConfiguration()       — appsettings.json + env-переменные MESSENGER_*
  2. ResolveApiUrl()            — определение URL сервера
  3. AvaloniaXamlLoader.Load()  — загрузка XAML-ресурсов
  4. ConfigureServices()        — сборка DI-контейнера

OnFrameworkInitializationCompleted()
  5. Создание MainWindow
  6. ConfigureImageLoader()     — AuthenticatedImageLoader
  7. InitializeLocalDatabaseAsync() — SQLite + обслуживание
  8. ThemeService.LoadFromSettings()
  9. MainWindowViewModel → DataContext
```

### Стратегия определения URL сервера (`ResolveApiUrl`)

```
1. TryLoadSavedServerUrl()     — читает settings.json в AppData
2. HealthCheck(savedUrl)       — GET /api/health (таймаут 2с)
   → Жив → использовать
   → Мёртв → шаг 3
3. DiscoverServerBlocking()    — UDP-обнаружение (3000ms таймаут)
   → Найден → SaveServerUrlToSettings() → использовать
   → Не найден → шаг 4
4. configuration["ApiUrl"]     — из appsettings.json
   → Fallback: "http://localhost:5274/"
```

**⚠️ В Debug-режиме:** `Thread.Sleep(5000)` в начале `ResolveApiUrl` — пауза, чтобы API успел подняться при одновременном старте проектов.

### Конфигурация (`BuildConfiguration`)

| Источник | Условие |
|---|---|
| `appsettings.json` | Всегда (optional) |
| `appsettings.{env}.json` | Если `MESSENGER_ENV` задан |
| Environment variables | Префикс `MESSENGER_` |

### DI-контейнер (`ConfigureServices`)

```csharp
services.AddLogging(Debug + Console)
services.AddMessengerCoreServices(ApiUrl)   // все сервисы кроме Theme
services.AddMessengerViewModels()           // все ViewModel
services.AddSingleton<IThemeService>()
// ValidateScopes=true, ValidateOnBuild=true
```

### Порядок Dispose при выходе

1. `INotificationService.Dispose()`
2. `IPlatformService.Cleanup()`
3. `MainWindowViewModel.Dispose()`
4. `IApiClientService.Dispose()`
5. `IAuthManager.Dispose()`
6. `ISessionStore.Dispose()`
7. `IDialogService.Dispose()`
8. `INavigationService.Dispose()`
9. `AuthenticatedImageLoader.Dispose()`
10. `LocalDatabase.Dispose()`
11. `ServiceProvider.Dispose()`

### Хранение URL сервера

**Файл:** `%APPDATA%/Desktop/settings.json`
```json
{ "server_url": "http://192.168.1.100:5274/" }
```

---

## 20.2 MainWindow.axaml.cs — Главное окно

**Путь:** `Desktop/Views/Shell/MainWindow.axaml.cs` (205 строк)

**Назначение:** Главное окно приложения. Управляет анимацией диалогов, адаптивной шириной, поиском и системными событиями.

### Константы

| Константа | Значение | Назначение |
|---|---|---|
| `AnimationDurationMs` | 250 | Длительность анимации диалога |
| `FrameDelayMs` | 16 | Задержка перед стартом анимации (~1 кадр) |
| `MaximizedPadding` | 7 | Отступ при разворачивании на весь экран |
| `CompactModeThreshold` | 800 | Ширина переключения в компактный режим |

### Адаптивный режим

`IsCompactMode` — DirectProperty, изменяется при `BoundsProperty.Changed`. При ширине ≤800px → компактный режим (узкая боковая панель чатов).

### Анимация диалогов

Управляется через `IDialogService.OnDialogAnimationRequested`:

```
Открытие:
  Task.Delay(16ms) → Add class "Open" на Overlay + AnimWrapper → Task.Delay(250ms)

Закрытие:
  Remove "Open" → Add "Closing" → Task.Delay(250ms) → Remove "Closing"
```

**Защита от гонок:** `CancellationTokenSource` + `Lock _animationLock`. Предыдущая анимация отменяется при новом запросе. Таймаут ожидания `NotifyAnimationComplete()` — 1 секунда.

### Обработка событий

| Обработчик | Событие | Действие |
|---|---|---|
| `OnGlobalSearchFocused` | `SearchBox.SearchFocused` | `menu.SearchManager.EnterSearchMode()` |
| `OnWindowPointerPressed` | Tunnel PointerPressed | Закрытие поиска при клике вне SearchBox/Popup |
| `OnTitleBarPointerPressed` | PointerPressed на TitleBar | `BeginMoveDrag()` |
| `OnDialogBackgroundPressed` | PointerPressed на overlay | `CurrentDialog.CloseOnBackgroundClick()` |
| `WindowStateProperty.Changed` | Maximize/Restore | `UpdateWindowPadding()` |

### Закрытие окна (`OnClosed`)

1. Отписка от `DialogService.OnDialogAnimationRequested`
2. Отписка от `SearchBox.SearchFocused`
3. Отмена анимации (`_animationCts`)
4. `IPlatformService.Cleanup()`
5. `INotificationService.Dispose()`

---

## 20.3 ChatView.axaml.cs — Основной вид чата

**Путь:** `Desktop/Views/Chat/ChatView.axaml.cs` (полностью переработан)

**Назначение:** Code-behind для главного экрана чата. Управляет скроллом с якорной привязкой, пагинацией, видимостью сообщений и сохранением позиции.  
**Большая переработка:** таймеры теперь создаются динамически и останавливаются при очистке, удалён старый `_scheduleCts`, добавлены `_findCts`/`_restoreCts`. Сохранение позиции скролла переведено на **якорную модель** (`ChatScrollState` теперь содержит `AnchorMessageId` и `AnchorOffset`), восстановление идёт через `ScrollIntoView` + корректировку смещения.

### Константы

| Константа | Значение | Назначение |
|---|---|---|
| `MaxScrollToEndRetries` | 10 | Макс. попыток скролла вниз |
| `VisibilityCheckDelayMs` | 1000 | Debounce проверки видимых сообщений |
| `NearBottomThreshold` | 200 | Порог "у дна" (px от конца) |
| `NearTopThreshold` | 400 | Порог "у верха" для догрузки |
| `ScrollStateSaveDebounceMs` | 350 | Debounce сохранения позиции |
| `ScrollStateMaxAgeHours` | 72 | Максимальный возраст сохранённого состояния |

### Жизненный цикл компонента

```
OnLoaded → FindScrollViewer
DataContextChanged → CancelAllPendingOperations → DetachFromViewModel → ResetScrollState → StopTimers → CreateTimers → AttachToViewModel
OnUnloaded → SaveScrollState → Cleanup
OnDetachedFromVisualTree → Cleanup
```

### Подписки на ViewModel

| Событие | Обработчик |
|---|---|
| `PropertyChanged` | Переподписка на Messages при смене; при `IsInitialLoading=false` → `ScheduleFind(FindScrollViewer, 50)` + `ScheduleRestore(150)` |
| `Messages.CollectionChanged` | При добавлении в конец: `BeginScrollToBottom` (если внизу) или UnreadCount++ |
| `ScrollToMessageRequested` | `ScheduleScrollAction → ScrollToItem` |
| `ScrollToIndexRequested` | `ScheduleScrollAction → ScrollToItem(Messages[index])` |
| `ScrollToBottomRequested` | `BeginScrollToBottom` |

### Скролл и пагинация

- `PerformScrollToBottom` / `FinishScrollToBottom`: учитывают проверку жизни ViewModel, сброс флагов.
- `LoadOlderMessagesAsync` / `LoadNewerMessagesAsync`: атомарные флаги `Interlocked`.
- Сохранение позиции `SaveScrollState`: вычисляет якорь (первый видимый элемент) и сохраняет `(MessageId, OffsetFromTop, IsAtBottom)`. Восстановление `RestoreToAnchor` ищет сообщение, вызывает `ScrollIntoView`, затем корректирует смещение.
- Удалена старая логика сохранения `OffsetY`, теперь всё на якорях.

### Таймеры

- `_visibilityTimer` и `_saveScrollStateTimer` теперь пересоздаются при входе в DataContext и останавливаются при очистке; методы `OnVisibilityTimerTick` / `OnSaveScrollStateTimerTick` статические.

### Потокобезопасность

- Дополнительный `_restoreCts` для отмены восстановления.
- Все отложенные операции (`ScheduleFind`, `ScheduleRestore`, `ScheduleScrollAction`) — с отдельными токенами и `RunDelayedAsync`.

---

## 20.4 AvatarControl.axaml.cs — Контрол аватара

**Путь:** `Desktop/Views/Controls/Shared/AvatarControl.axaml.cs` (338 строк)

**Назначение:** Универсальный контрол отображения аватара пользователя или чата с индикатором онлайн-статуса, учитывающим тип статуса.

### Styled Properties (входные параметры)

| Свойство | Тип | Default | Назначение |
|---|---|---|---|
| `Source` | `string?` | null | URL или путь к изображению |
| `DisplayName` | `string?` | null | Имя для инициалов |
| `Size` | `double` | 40 | Размер контрола |
| `FontSize` | `double` | 14 | Размер шрифта инициалов |
| `IconSize` | `double` | 18 | Размер fallback-иконки |
| `IsOnline` | `bool` | false | Онлайн? |
| `ShowOnlineIndicator` | `bool` | true | Показывать индикатор? |
| `StatusType` | `UserStatusType` | Online | Тип статуса (влияет на цвет индикатора) |
| `FallbackIcon` | `Geometry?` | null | Иконка вместо инициалов |
| `PlaceholderBackground` | `IBrush?` | AccentLight | Фон плейсхолдера |
| `IsCircular` | `bool` | true | Круглый или скруглённый |
| `ImageBitmap` | `IImage?` | null | Готовый bitmap (приоритет над Source) |

### Direct Properties (вычисляемые, readonly)

| Свойство | Назначение |
|---|---|
| `HasImage` | `Source` валиден и нет `ImageBitmap` |
| `HasBitmapImage` | `ImageBitmap != null` |
| `ShowInitials` | Нет изображения, есть имя, нет FallbackIcon |
| `ShowIcon` | Нет изображения (нет имени или есть FallbackIcon) |
| `Initials` | Вычисляется из `DisplayName` |
| `ShowOnlineStatus` | `IsOnline && ShowOnlineIndicator` |
| `OnlineIndicatorBackground` | Цвет по `StatusType` |
| `OnlineIndicatorSize` | Зависит от `Size` |
| `StatusTooltip` | "В сети" / "Отошёл" / "Занят" / "Не беспокоить" |

### Логика отображения (приоритет)

```
1. ImageBitmap != null → показать bitmap напрямую
2. Source валиден → RemoteImage (AuthenticatedImageLoader)
3. DisplayName не пусто, нет FallbackIcon → ShowInitials (инициалы)
4. Иначе → ShowIcon (PersonIcon или FallbackIcon)
```

**Новое:** если `Source` тот же, но кэш `AuthenticatedImageLoader` не содержит изображение, контрол принудительно перезагружает его через `RemoteImage`.

### Вычисление инициалов (`ExtractInitials`)

```
"Иван Петров" → "ИП"
"Иван" → "ИВ" (первые 2 буквы)
"И" → "И"
null/"" → "?"
```

### Цвета статусов

| StatusType | Цвет |
|---|---|
| Online | `#43A047` |
| Away | `#FFA000` |
| Busy | `#E53935` |
| DoNotDisturb | `#9C27B0` |

### Размер индикатора онлайн

| Size | Индикатор |
|---|---|
| ≤32 | 8px |
| ≤48 | 10px |
| ≤64 | 12px |
| ≤80 | 14px |
| >80 | 16px |

### Валидация Source (`IsValidImageSource`)

- `avares://` → всегда валидно
- `http://` / `https://` → всегда валидно
- Расширение в словаре ImageExtensions (jpg/jpeg/png/gif/webp/bmp/ico/avif) → валидно
- Нет расширения → пробуем загрузить (валидно)
- Другое расширение → не валидно

### Dispose

При `OnDetachedFromVisualTree`: освобождает `Bitmap`, вызывает `MemoryDiagnostics.OnBitmapDisposed()`

---

## 20.5 RichMessageTextBlock.cs — Текстовый блок с разметкой

**Путь:** `Desktop/Views/Controls/Shared/RichMessageTextBlock.cs` (181 строка)
**Наследует:** `TextBlock`

**Назначение:** Кастомный TextBlock с поддержкой кликабельных URL и @упоминаний.

### Свойства

| Свойство | Тип | Назначение |
|---|---|---|
| `RawText` | `string?` | Исходный текст (при изменении → `RebuildInlines`) |
| `MentionClickCommand` | `ICommand?` | Команда при клике на @упоминание |

### Regex-паттерны

| Паттерн | Выражение | Назначение |
|---|---|---|
| URL | `(https?://[^\s<>"')\]]+)` | Ссылки http/https |
| Mention | `(?<![A-Za-z0-9_])@[A-Za-z0-9_]{3,30}` | @упоминания (отрицательный lookbehind) |

Оба с `Compiled` и таймаутом 1 секунда (защита от ReDoS).

### Алгоритм построения Inlines (`RebuildInlines`)

```
1. Если text == _lastBuiltText → return (кэш)
2. CollectMatches(text) → сортировка по позиции
3. Если нет совпадений → Text = text (простой режим)
4. Иначе:
   Text = null, строим Inlines:
   - Промежуток до совпадения → Run(plainText)
   - URL → Run(url) { Foreground=#4A9EEA, Underline }
   - Mention → Run(@mention) { Foreground=#8F7DFF, SemiBold }
5. Трекинг позиций в _linkRanges / _mentionRanges
```

### Интерактивность

**Клик (`OnPointerPressed`):**
1. `GetCharIndex(e)` через `TextLayout.HitTestPoint`
2. `FindUrl(charIndex)` → `Process.Start(url, UseShellExecute=true)`
3. `FindMention(charIndex)` → `MentionClickCommand.Execute(@mention)`

**Курсор (`OnPointerMoved`):** `StandardCursorType.Hand` над ссылками/упоминаниями.

### Цвета

| Тип | Цвет |
|---|---|
| URL | `#4A9EEA` (синий) |
| Mention | `#8F7DFF` (фиолетовый) |

### Память

- Конструктор: `MemoryDiagnostics.OnRichTextCreated()`
- `OnDetachedFromVisualTree`: `Inlines.Clear()`, `Text = null`, очистка кэшей, `OnRichTextDestroyed()`

---

## 20.6 WaveformView.cs — Визуализация аудиоволны

**Путь:** `Desktop/Views/Controls/Shared/WaveformView.cs` (197 строк)
**Наследует:** `Control`

**Назначение:** Кастомный контрол для отображения и перемотки голосовых сообщений. Добавлен адаптивный ресэмплинг пиков для корректного отображения на любой ширине.

### Свойства

| Свойство | Тип | Default | Назначение |
|---|---|---|---|
| `Progress` | `double` | 0 | Прогресс воспроизведения (0–100) |
| `Waveform` | `string?` | null | Base64-массив байт (100 баров) |
| `SeekCommand` | `ICommand?` | null | Команда перемотки (параметр: процент) |
| `PlayedBrush` | `IBrush` | DodgerBlue | Цвет воспроизведённой части |
| `UnplayedBrush` | `IBrush` | LightGray | Цвет невоспроизведённой части |
| `Background` | `IBrush?` | null | Фон |

### Алгоритм рендеринга (`Render`)

```
Если нет данных (_peaks == null) → нарисовать горизонтальную линию
Параметры бара: width=3px, gap=2px, totalBarSpace=5px
maxBars = (контейнер.Width + gap) / totalBarSpace
peaksToDraw = ResamplePeaks(_peaks, maxBars)  — линейная интерполяция
totalWidth = barCount * 5 - 2
startX = (containerWidth - totalWidth) / 2    — центрирование
progressX = startX + (Progress/100) * totalWidth

Для каждого бара:
  normalizedPeak = 0.2 + (peak/255 * 0.8)  — минимум 20% высоты
  barHeight = normalizedPeak * containerHeight
  brush = barCenter <= progressX ? PlayedBrush : UnplayedBrush
  DrawRectangle(rounded, barWidth/2 radius)
```

### Ресэмплинг (`ResamplePeaks`)

Линейная интерполяция: если данных больше maxBars — усредняет соседние. Если меньше — возвращает копию.

### Интерактивность (перемотка)

**Drag-поведение:**
- `OnPointerPressed` → захват указателя, `_isDragging = true`, `UpdateProgressFromPoint`
- `OnPointerMoved` → если `_isDragging` и смещение >2px → `UpdateProgressFromPoint`
- `OnPointerReleased` → `UpdateProgressFromPoint`, освобождение захвата
- `OnPointerCaptureLost` → `_isDragging = false`

**`UpdateProgressFromPoint`:**
```
pct = (position.X - startX) / totalWidth * 100
Clamp(pct, 0, 100)
SeekCommand.Execute(pct) или Progress = pct
```

---

## 20.7 ChatsView.axaml.cs — Список чатов

**Путь:** `Desktop/Views/Chat/ChatsView.axaml.cs` (207 строк)

**Назначение:** Адаптивный layout с изменяемой шириной панели чатов, компактным режимом и управлением панелью информации.

### Пороги адаптивного layout

| Константа | Значение | Назначение |
|---|---|---|
| `COMPACT_WIDTH` | 72 | Ширина в компактном режиме |
| `ENTER_COMPACT_THRESHOLD` | 120 | Порог входа в компактный (при drag) |
| `EXIT_COMPACT_THRESHOLD` | 160 | Порог выхода из компактного (при drag) |
| `NORMAL_DEFAULT_WIDTH` | 280 | Ширина при восстановлении |
| `MIN_WIDTH` / `MAX_WIDTH` | 72 / 400 | Ограничения ColumnDefinition |
| `FORCE_COMPACT_ENTER_WIDTH` | 1020 | Авто-компактный при ширине окна ≤1020 |
| `FORCE_COMPACT_EXIT_WIDTH` | 1100 | Выход из авто-компактного при ≥1100 |
| `HIDE_INFO_PANEL_ENTER_WIDTH` | 820 | Скрыть InfoPanel при ≤820 |
| `HIDE_INFO_PANEL_EXIT_WIDTH` | 900 | Показать InfoPanel при ≥900 |

### Адаптивность (`EvaluateResponsiveLayout`)

**Гистерезис** для предотвращения мерцания:
- Вход в состояние при одном пороге, выход при другом (±80px)

**Авто-компактный режим** (`_forceCompactMode`):
- При `_forceCompactMode && !IsCompactMode` → Column.Width = 72, IsCompactMode = true
- При `!_forceCompactMode && _compactModeWasForced` → Column.Width = 280, IsCompactMode = false

### Drag-to-resize (GridSplitter)

```
DragStarted → _isDragging = true
DragCompleted → проверка пороговых значений:
  width ≤ 120 + !IsCompactMode → compact(72)
  width ≥ 160 + IsCompactMode → normal
  IsCompactMode + width < 160 → зафиксировать compact(72)
```

### Видимость InfoPanel

```
Показывать если:
  - vm.CurrentChatViewModel != null  (чат открыт)
  - _chatInfoPanelStateStore.IsOpen   (пользователь не закрыл)
  - !_hideInfoPanelForWidth           (достаточно места)
```

### Публичные методы

| Метод | Действие |
|---|---|
| `ToggleCompactMode()` | Переключение (если не `_forceCompactMode`) |
| `ExpandFromCompact()` | Выход из компактного (если в нём) |

### Обработчики кнопок

| Кнопка | Действие |
|---|---|
| Expand | `ToggleCompactMode()` |
| Search | `ExpandFromCompact()` + фокус на `SearchBox` |

---

## 20.8 MessageControl.axaml — Шаблон сообщения

**Путь:** `Desktop/Views/Chat/MessageControl.axaml`

**Назначение:** Основной шаблон элемента списка сообщений.  
**Изменения:**
- В `Border.ContextFlyout` убран дублирующийся `DeletedTemplate`.
- `ReplyPreviewBlock` теперь использует `ContentPreview` вместо `Content` для отображения превью.
- Добавлено удалённое состояние для опросов, голосовых и текстовых сообщений.
- **Заголовок пересылки** вынесен в отдельный `ForwardHeaderBlock` с кнопкой для открытия профиля отправителя.

---

## 20.9 MessagePartSelector.cs — Селектор части сообщения

**Путь:** `Desktop/Views/Chat/MessagePartSelector.cs`  
**Назначение:** Выбирает один из трёх шаблонов на основе `OriginalIsVoiceMessage` и `OriginalHasPoll`.  
**Изменения:**
- Удалён `DeletedTemplate`.
- Добавлен `PollTemplate`.
- Логика: если `OriginalHasPoll` → PollTemplate, иначе если `OriginalIsVoiceMessage` → VoiceTemplate, иначе TextTemplate.  
  Таким образом, при удалении сообщения сохраняется его исходный тип для превью.

---

## 20.10 MessageParts — Обновлённые части

- **PollMessagePart.axaml**: теперь содержит `StackPanel` с двумя состояниями — для удалённого опроса (показывается `DisplayContent` и мета-блок) и для живого опроса (прежнее содержимое).
- **TextMessagePart.axaml**: аналогично, при `IsDeleted` отображается `DisplayContent` (сообщение удалено) с серым курсивом.
- **VoiceMessagePart.axaml**: при `IsDeleted` показывается заглушка; плеер скрывается.

Все части теперь используют свойство `ContentPreview` для отображения превью в `ChatView.axaml` (блок ответа).

---

## 20.11 ForwardHeaderBlock.axaml — Блок пересылки

**Путь:** `Desktop/Views/Chat/MessageParts/Shared/ForwardHeaderBlock.axaml`  
**Полностью переработан.** Теперь отображает текст «Переслано от» и имя отправителя. Если известен `ForwardedFromSenderId` и `CanOpenForwardSenderProfile == true`, имя отправителя становится кнопкой, открывающей профиль. В противном случае имя отображается простым текстом.

---

## 20.12 FilterAutocomplete.axaml.cs — Автодополнение фильтров

**Путь:** `Desktop/Views/Controls/FilterAutocomplete.axaml.cs` (118 строк)

**Назначение:** Кастомный контрол поиска с выпадающим списком. Используется в фильтрах глобального поиска (отправитель, чат).

### Свойства

| Свойство | Тип | Назначение |
|---|---|---|
| `SearchText` | `string` | Текст поиска (TwoWay) |
| `Placeholder` | `string` | Текст-подсказка |
| `Icon` | `Geometry?` | Иконка слева |
| `SelectedItem` | `SearchFilterItem?` | Выбранный элемент (TwoWay) |
| `IsDropdownOpen` | `bool` | Открыт ли дропдаун (TwoWay) |
| `Suggestions` | `IEnumerable<SearchFilterItem>` | Список подсказок |

### Команды

| Команда | Действие |
|---|---|
| `ClearCommand` | `SelectedItem = null`, `SearchText = ""`, `IsDropdownOpen = false` |
| `SelectItemCommand(item)` | `SelectedItem = item`, `SearchText = item.DisplayName`, `IsDropdownOpen = false` |

### Логика закрытия дропдауна

**Глобальный pointer-listener** (tunnel на TopLevel):
```
OnGlobalPointerPressed:
  if IsPointerOver → return (клик внутри контрола)
  if popup.Child.IsPointerOver → return (клик в дропдауне)
  IsDropdownOpen = false
```

**Потеря фокуса** (`OnInputLostFocus`): пост на Background-приоритет → проверка `IsPointerOver` и popup → `IsDropdownOpen = false`.

---

# 🔴 ИЗВЕСТНЫЕ ПРОБЛЕМЫ

## Критические

| # | Проблема | Где | Рекомендация |
|---|---|---|---|
| 1 | In-Memory звонки — не масштабируются | `CallSessionService` (Singleton) | Redis Backplane |
| 2 | `AppDateTime.UtcNow` → `DateTimeKind.Unspecified` | `API/Infrastructure/Common/AppDateTime.cs` | `DateTimeKind.Utc` или `DateTimeOffset` |
| 3 | RefreshToken в теле JSON | `AuthResponseDto`, `TokenResponseDto` | httpOnly cookie |
| 4 | `MissingFileCleanupMiddleware` — DB-запрос на каждый 404 | Middleware | Rate limiting по IP |
| 5 | `PollOptionDto.Votes` для анонимных опросов | `PollMappings.ToDto(isAnonymous)` | Фильтруется, но проверить |
| 6 | `MessageForwardInfoDto` без проверки доступа к чату | `MessageMappings` | Проверка IsMember для OriginalChatId |

## Архитектурные

| # | Проблема | Рекомендация |
|---|---|---|
| 7 | `MessageService` — ~590 строк | `MessageWriter`, `MessageReader`, `MessageSearchService` |
| 8 | `ChatDto` — 22 поля, смешивает чат + сообщение + контакт | `ChatListItemDto`, `ChatDetailDto` |
| 9 | `ChatHub` создаёт scope вручную | Проверить dispose через `using` |

## БАГИ

| # | Баг |
|---|---|
| 1 | Баги подключения к звонку |
| 2 | Таймер звонка уходит в отрицательное значение у пользователя присоединившегося |
| 3 | Баги в полях фильтров поиска |
| 4 | Требуется в LoginView добавить кнопку с диалогом ввода IP, на случай если UDP не работает предлогать его ввести, если введен IP, то UDP не ищет сервер, а подключается по IP |
| 5 | При быстром скроле ломаются варианты ответа у опроса |
| 6 | Безопасность звонков |
| 7 | Добавь сид данные для первичного запуска через докер |
| 8 | Утечка памяти в чатах, возможно аватарки |

## Не обязательно, но я бы закрыл

| # | Пожелание |
|---|---|
| 1 | При пересылке Last Message пишется не от имени чье сообщение мы переслали, а от нашего |