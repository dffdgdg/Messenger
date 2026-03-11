# Контекстный документ проекта: Корпоративный мессенджер

## 1. Общее описание и стек

Локальный корпоративный мессенджер — три .NET-проекта:
- **MessengerAPI** — ASP.NET Core Web API + SignalR, EF Core, PostgreSQL, BCrypt, JWT, SixLabors.ImageSharp, NAudio, whisper.cpp
- **MessengerDesktop** — Avalonia UI, CommunityToolkit.Mvvm, DI, SQLite (sqlite-net-pcl, FTS5), NAudio, AsyncImageLoader
- **MessengerShared** — общие DTO, Enums, ApiResponse\<T\>

---

## 2. Структура проекта и naming

```
MessengerAPI/
  Controllers/        — REST контроллеры
  Hubs/               — ChatHub
  Services/           — бизнес-логика
  Models/             — EF Entity
  Mapping/            — DTO ↔ Entity (статические методы MapToDto/ToEntity)
  Middleware/          — ExceptionHandling, MissingFileCleanup
  Configuration/      — extension methods для DI
  Common/             — BaseService<T>

MessengerDesktop/
  ViewModels/
    Admin/            — UsersTab, DepartmentsTab, AdminViewModel
    Auth/             — LoginViewModel
    Chat/             — ChatViewModel (partial), MessageVM, PollVM, Managers/
    Chats/            — ChatsViewModel, ChatListItemViewModel
    Department/       — DepartmentManagementViewModel
    Dialog/           — все диалоговые VM
    Factories/        — ChatViewModelFactory, ChatsViewModelFactory
    Shell/            — MainMenuViewModel, MainWindowViewModel
  Views/              — зеркально ViewModels
  Services/           — клиентские сервисы
  Infrastructure/     — DI, constants, endpoints, helpers
  Data/               — SQLite entities, repositories, LocalCacheService
  Converters/         — ConverterBase, все конвертеры
  Assets/             — иконки, шрифты, стили

MessengerShared/
  DTOs/               — все DTO
  Enums/              — UserRole, ChatType, ChatRole, Theme, SystemEventType
  ApiResponse.cs      — обёртка ApiResponse<T>
```

### Naming conventions
- Сервисы: `I{Name}Service` + `{Name}Service`
- VM: `{Feature}ViewModel`, диалоги: `{Feature}DialogViewModel`
- Views: `{Feature}View`, диалоги: `{Feature}Dialog`
- Команды: `{Action}Command` (RelayCommand / AsyncRelayCommand)
- Hub-события на клиенте: `On{EventName}`, сервер→клиент: `{EventName}`
- Кеш-сущности: `Cached{Entity}`
- Маппинг: `MapTo{Target}(source)` или `To{Target}(source)` — статические методы

---

## 3. Entity / схема БД (PostgreSQL)

```
User: Id, Username, DisplayName, PasswordHash, AvatarUrl?, IsBanned, DepartmentId?(FK→Department), LastOnline, CreatedAt
  Nav → Department, UserSetting(1:1), ChatMembers, Messages

Message: Id, ChatId(FK→Chat), SenderId(FK→User), Content?, CreatedAt, EditedAt?, IsDeleted,
  ReplyToMessageId?(FK→Message), ForwardedFromMessageId?(FK→Message),
  IsSystemMessage, SystemEventType?(enum), TargetUserId?(FK→User),
  IsVoiceMessage
  Nav → Chat, Sender, TargetUser, ReplyToMessage, ForwardedFromMessage, VoiceMessage(1:1), MessageFiles, Polls

Chat: Id, Name, Type(ChatType enum→int), AvatarUrl?, CreatedById(FK→User), CreatedAt, LastMessageAt
  Nav → CreatedBy, Members(ChatMember), Messages

ChatMember: ChatId+UserId (composite PK), Role(ChatRole enum→int), JoinedAt, NotificationsEnabled(default true), LastReadMessageId?(FK→Message)
  Nav → Chat, User, LastReadMessage

VoiceMessage: Id, MessageId(FK→Message, unique), FileUrl, FileName, ContentType, FileSize, Duration(double seconds),
  TranscriptionStatus(string: pending/processing/done/failed), TranscriptionText?

Poll: Id, MessageId(FK→Message), Question, IsAnonymous, AllowMultipleAnswers
  Nav → Message, Options

PollOption: Id, PollId(FK→Poll), Text
  Nav → Poll, Votes

PollVote: Id, PollOptionId(FK→PollOption), UserId(FK→User)
  Nav → PollOption, User

MessageFile: Id, MessageId(FK→Message), FileUrl, FileName, ContentType, FileSize(long)
  Nav → Message

RefreshToken: Id, UserId(FK→User), TokenHash, JwtId, FamilyId(Guid), ExpiresAt, CreatedAt, UsedAt?
  Nav → User

Department: Id, Name, ParentId?(self-FK→Department), HeadUserId?(FK→User)
  Nav → Parent, Children, Head, Users

UserSetting: Id, UserId(FK→User, unique), Theme(enum→int), NotificationsEnabled, CanBeFoundInSearch
  Nav → User
```

---

## 4. Shared-библиотека

### ApiResponse\<T\> { Success, Data, Message, Error, Details, Timestamp }
`ApiResponseHelper`: `Success<T>(data, message?)`, `Error<T>(error, details?)`

### Enums
- `UserRole`: User, Head, Admin
- `ChatType`: Chat, Department, Contact, DepartmentHeads
- `ChatRole`: Member, Admin, Owner
- `Theme`: light, dark, system
- `SystemEventType`: ChatCreated, MemberAdded, MemberRemoved, MemberLeft, RoleChanged

### DTO (ключевые моменты)
Полные DTO в коде. Важное:
- `MessageDto.ShowSenderName` — вычисляемое: другой отправитель или >5 мин разницы
- `ChatReadInfoDto` — включает FirstUnreadMessageId
- `PollVoteDto` — поддерживает и OptionId, и OptionIds (множественный выбор)
- Категории DTO: Auth, User, Chat, Message, Poll, ReadReceipt, Online, Notification, Search, Department

---

## 5. SignalR Hub контракт (/chatHub)

### Client → Server
```
JoinChat(int chatId)
LeaveChat(int chatId)
SendTypingIndicator(int chatId)
MarkMessageAsRead(int chatId, int messageId)
```

### Server → Client
```
ReceiveMessageDto(MessageDto)
MessageUpdated(MessageDto)
MessageDeleted(int chatId, int messageId)
ReceivePollUpdate(PollDto)
UserStatusChanged(int userId, bool isOnline)
UserProfileUpdated(UserDto)
MemberJoined(int chatId, ChatMemberDto)
MemberLeft(int chatId, int userId)
MemberRoleChanged(int chatId, int userId, ChatRole newRole)
ChatUpdated(ChatDto)
ChatDeleted(int chatId)
TypingIndicator(int chatId, int userId, string userName)
UnreadCountChanged(int chatId, int count)
TotalUnreadChanged(int totalCount)
TranscriptionStatusChanged(int messageId, string status)
TranscriptionCompleted(int messageId, string text)
NotificationReceived(NotificationDto)
```

---

## 6. Серверные сервисы

### BaseService\<T\>
`SaveChangesAsync` — DbUpdateConcurrencyException → Conflict, unique violation (23505) → Conflict. `FindEntityAsync`, `Paginate`, `NormalizePagination`.

### TokenService
- `GenerateTokenPair(userId, role)` → TokenPair { AccessToken, RefreshToken, JwtId } // HmacSha256, claims: NameIdentifier+Jti+Iat+Role
- `ValidateToken(token)` → ClaimsPrincipal // полная валидация с lifetime
- `GetPrincipalFromExpiredToken(token)` → ClaimsPrincipal // ValidateLifetime=false
- `HashToken(token)` → string // static, SHA256→Base64

### AuthService
- `LoginAsync(dto)` → LoginResponseDto // timing-safe (dummy hash), BCrypt, IsBanned check, DetermineUserRoleAsync, TokenPair, SaveRefreshToken(FamilyId=Guid)
- `RefreshAsync(dto)` → TokenPair // GetPrincipalFromExpiredToken → replay detection (UsedAt!=null → отзыв семьи) → ротация
- `RevokeAsync(userId)` → void // ExecuteUpdate всех активных
- EnforceSessionLimit: MaxActiveSessions=5, группировка по FamilyId
- CleanupExpired: удаление >60 дней

### AccessControlService
- Request-scoped кеш `Dictionary<(UserId,ChatId), ChatMember?>` + IMemoryCache
- `IsMemberAsync(userId, chatId)` → bool
- `IsOwnerAsync / IsAdminAsync / GetRoleAsync` → соответствующие типы
- `CheckIs*` → Result с Forbidden

### CacheService (IMemoryCache)
- UserChats: TTL 5мин, sliding 2мин. Membership: TTL 10мин, sliding 3мин
- `Invalidate*` — по конкретным ключам

### OnlineUserService (Singleton)
- `ConcurrentDictionary<int, ConcurrentDictionary<string, byte>>`, таймер очистки каждые 5 мин

### HubNotifier
- `SendToChatAsync(chatId, method, data)` → Group("chat_{id}")
- `SendToUserAsync(userId, method, data)` → Group("user_{id}")

### ChatService
- `GetUserChatsAsync(userId)` → List\<ChatDto\> // sorted: unread first → by date, GroupJoin LastMessage, DialogPartners для Contact
- `CreateChatAsync(dto, creatorId)` → ChatDto // Contact: парсит contactUserId из Name, дедупликация, транзакция. Group: системное ChatCreated
- `UpdateChatAsync(chatId, dto, userId)` → ChatDto // Admin+, нельзя Contact, смена типа Owner only
- `DeleteChatAsync(chatId, userId)` → void // Owner only, cascade ExecuteDelete
- `UploadAvatarAsync(chatId, stream, fileName, userId)` → string url // Admin+, не Contact, WebP

### ChatMemberService
- `AddMemberAsync(chatId, userId, addedByUserId)` → void // Admin+, системное MemberAdded
- `RemoveMemberAsync(chatId, userId, removedByUserId)` → void // Admin+ или self, нельзя Owner, системное MemberLeft/MemberRemoved
- `UpdateRoleAsync(chatId, userId, newRole, changedByUserId)` → void // Owner only, нельзя менять/назначать Owner, системное RoleChanged
- `LeaveAsync(chatId, userId)` → void // делегирует RemoveMember(chatId, userId, userId)

### NotificationService (серверный)
- `SendNotificationAsync(message, chat)` → void // BuildNotificationDto → HubNotifier. Contact: ChatName=SenderName. Type: "poll"/"message"
- CRUD для ChatMember.NotificationsEnabled

### SystemMessageService
- `CreateAsync(chatId, eventType, actorId, targetUserId?)` → void // пропускает Contact, IsSystemMessage=true, Hub ReceiveMessageDto, fire-and-forget safe

### FileService
- `SaveImageAsync(stream, fileName, subFolder)` → string path // ImageSharp → resize → WebP (Quality из настроек), uploads/{subFolder}/{Guid}.webp
- `SaveMessageFileAsync(chatId, userId, file)` → MessageFile // проверка членства + size limit, uploads/chats/{chatId}/{Guid}{ext}
- AllowedImageTypes: jpeg, png, gif, webp, bmp

### MessageService
- `CreateAsync(dto, senderId)` → MessageDto // проверка reply (тот же чат, не удалено) + forward (существует), VoiceMessage → SaveFiles → UpdateLastMessageTime → Hub ReceiveMessageDto → NotifyAndUpdateUnread → TranscriptionQueue
- `GetChatMessagesAsync(chatId, userId, page, pageSize)` → List\<MessageDto\> // OrderByDesc CreatedAt → reverse
- `GetMessagesAroundAsync / BeforeAsync / AfterAsync(chatId, messageId, userId, count)` → объект с hasMore/hasNewer
- `UpdateAsync(messageId, dto, userId)` → MessageDto // только свои, не системные/удалённые/опрос/голосовые/пересланные, Hub MessageUpdated
- `DeleteAsync(messageId, userId)` → void // soft delete, VoiceMessage физически удаляется, Hub MessageDeleted
- `SearchMessagesAsync(chatId, query)` → List\<MessageDto\> // ILIKE + EscapeLikePattern
- `GlobalSearchAsync(userId, query)` → SearchResult // SearchChats (Contact по partner name, Group по Name) + SearchMessages с HighlightedContent (±40 символов)
- MessagesWithIncludes: Sender, TargetUser, VoiceMessage, MessageFiles, Polls→Options→Votes, Reply→Sender, Forward→Sender

### PollService
- `CreateAsync(dto, creatorId)` → MessageDto // транзакция Message→Poll→Options, Hub ReceiveMessageDto
- `VoteAsync(dto, userId)` → PollDto // replace-стратегия (удаление старых + добавление новых), Hub ReceivePollUpdate
- `GetAsync(pollId, userId)` → PollDto // include Options→Votes, маппинг с currentUserId

### ReadReceiptService
- `MarkAsReadAsync(chatId, userId, messageId?)` → void // forward-only (новый > текущего), Hub UnreadCountChanged+TotalUnreadChanged
- `GetChatReadInfoAsync(chatId, userId)` → ChatReadInfoDto // unread count + FirstUnreadMessageId
- `GetAllUnreadCountsAsync(userId)` → Dictionary\<int,int\> // subquery Count
- `GetUnreadCountsForChatsAsync(userId, chatIds)` → Dictionary\<int,int\> // batch
- `MarkAllAsReadAsync(chatId, userId)` → void // делегирует MarkAsReadAsync без messageId

### TranscriptionService (Singleton) + TranscriptionQueue + BackgroundService
- whisper.cpp локальный бинарник, модель ggml-small-q5_1.bin
- SemaphoreSlim(1,1), Channel\<int\>(100, Wait)
- Pipeline: SetStatus("processing") → ConvertToPcm16Mono16K (NAudio) → temp WAV → Process whisper → парсинг stdout → SetStatus("done"/"failed")
- Timeout 5мин. Hub: TranscriptionStatusChanged, TranscriptionCompleted
- `RetryTranscriptionAsync(messageId)` → void

### AdminService
- `GetUsersAsync()` → List\<UserDto\> // include Department+UserSetting
- `CreateUserAsync(dto)` → UserDto // валидация, уникальность, BCrypt, UserSetting
- `ToggleBanAsync(userId)` → void // toggle IsBanned

### UserService
- `GetAllUsersAsync()` → List\<UserDto\> // include Department+UserSetting, online status
- `GetUserAsync(id)` → UserDto
- `UpdateUserAsync(id, dto)` → UserDto // проверка id==dto.Id
- `UploadAvatarAsync(id, stream, fileName)` → string url
- `ChangeUsernameAsync(id, dto)` → void // regex ^[a-z0-9_]{3,30}$, toLower, уникальность
- `ChangePasswordAsync(id, dto)` → void // BCrypt.Verify текущего, min 6
- `GetOnlineUsersAsync / GetStatusAsync / GetStatusesAsync` → через OnlineUserService

### Серверный pipeline
ExceptionHandling → Swagger(dev) → HTTPS → SecurityHeaders(COEP/COOP/CORP/X-Content-Type-Options) → StaticFiles → MissingFileCleanup → CORS → RateLimiter → Auth → Controllers + SignalR

Rate Limiting (SlidingWindow): Global 100/10s, login 5/1min, upload 10/1min, search 15/1min, messaging 30/1min. По UserId или IP. 429 + RetryAfter.

---

## 7. Клиент — Infrastructure

### ApiEndpoints (статический класс)
Все URL-шаблоны: Auth (Login/Refresh/Revoke), Users, Chats, Messages (ForChat/Around/Before/After/Search/ChatSearch), Files, Polls, Departments, Notifications, ReadReceipts, Admin. Параметризованные методы типа `Messages.Around(chatId, messageId, userId, count)`.

### AppConstants
MaxFileSizeBytes: 20MB. Debounce: Default 300ms, MarkAsRead 300ms/1s cooldown, TypingSend 1200ms, TypingDuration 3500ms. Pages: Default 50, LoadMore 30, Search 20. HighlightDuration 3000ms.

### DI регистрация (ServiceCollectionExtensions)
**AddMessengerCoreServices(apiBaseUrl):** Singleton: LocalDatabase, MessageCacheRepository, ChatCacheRepository, LocalCacheService, CacheMaintenanceService, PlatformService, SettingsService, GlobalHubConnection, ChatNotificationApiService, ChatInfoPanelStateStore, AudioPlayerService. HttpClient (Singleton, 30s timeout). AuthService, SessionStore, SecureStorageService, AuthManager — Singleton. ApiClientService — Singleton (manual). NavigationService, DialogService, NotificationService — Singleton. FileDownloadService, NAudioRecorderService — Singleton.

**AddMessengerViewModels():** Singleton: ChatViewModelFactory, ChatsViewModelFactory, MainWindowViewModel. Transient: UsersTab/DepartmentsTab/Login/MainMenu/Admin/Profile/DepartmentManagement/Settings/StyleGuide ViewModels.

### Вспомогательное
- **AuthenticatedImageLoader**: наследует RamCachedWebImageLoader, добавляет Bearer token для URL с apiBaseUrl
- **AvatarHelper**: GetSafeUri (absolute или default-avatar.webp), GetUriWithCacheBuster (?v=ticks)
- **MimeTypeHelper**: расширение → MIME type

---

## 8. Клиент — Data (SQLite кеш)

### LocalDatabase
- SQLite через sqlite-net-pcl, путь: `%LocalAppData%/MessengerDesktop/messenger_cache.db`
- PRAGMAs: WAL, NORMAL sync, cache_size=-4000, temp_store=MEMORY, mmap_size=32MB
- Schema versioning: PRAGMA user_version (текущая 3), downgrade → drop all
- FTS5: virtual table `messages_fts` (content), triggers INSERT/UPDATE/DELETE

### Кеш-сущности
- **CachedMessage**: Id(PK), ChatId(Idx), SenderId(Idx), Content, CreatedAtTicks, EditedAtTicks, IsDeleted, ReplyToMessageId, ForwardedFromMessageId, IsOwn, IsVoiceMessage, VoiceDurationSeconds, TranscriptionStatus/Text, VoiceFileUrl/Name/ContentType/Size, SenderName/AvatarUrl, Reply*, Forward*, PollJson, FilesJson, IsSystemMessage, SystemEventTypeInt, TargetUserId/Name, CachedAtTicks
- **CachedChat**: Id(PK), Name, Type(int), Avatar, CreatedById, LastMessageDateTicks, LastMessagePreview, LastMessageSenderName, CachedAtTicks
- **CachedUser**: Id(PK), Username, DisplayName, Avatar, CachedAtTicks
- **CachedReadPointer**: ChatId(PK), LastReadMessageId, FirstUnreadMessageId, UnreadCount, LastReadAtTicks
- **ChatSyncState**: ChatId(PK), OldestLoadedId, NewestLoadedId, HasMoreOlder(default true), HasMoreNewer, LastSyncAtTicks

### CacheMapper (static)
MessageDto ↔ CachedMessage (полный маппинг, Poll/Files → JSON). ChatDto ↔ CachedChat. UserDto ↔ CachedUser.

### Repositories
- **MessageCacheRepository**: UpsertBatch, MarkDeleted, GetLatest/Before/After/Around, Search (FTS5 MATCH + LIKE fallback), DeleteForChat
- **ChatCacheRepository**: GetByType (SQL IN), UpdateLastMessage
- **LocalCacheService** (фасад): Messages, Chats, SyncState, ReadPointers, Users, SearchMessagesLocal
  - `CachedMessagesResult { Messages, HasMoreOlder, HasMoreNewer, IsComplete }`

---

## 9. Клиентские сервисы

- **ApiClientService**: auto-refresh при 401, large file temp. Методы: GetAsync\<T\>, PostAsync\<T\>, PutAsync\<T\>, DeleteAsync\<T\>, UploadFileAsync, GetStreamAsync
- **AudioPlayerService**: Singleton, NAudio. Play/Pause/Stop/Seek. События: PositionChanged, PlaybackStopped. Фильтрация по messageId
- **NAudioRecorderService**: Start/Stop/Cancel → MemoryStream (WAV). IsRecording, Elapsed
- **TranscriptionPollerService**: поллинг статуса транскрипции с интервалом, auto-stop при done/failed
- **AuthService**: Login/Refresh/Revoke → API calls
- **AuthManager**: LoginAsync, LogoutAsync, TryAutoLoginAsync, HasRole. Хранит SessionStore
- **SecureStorageService**: шифрованное хранение (DPAPI/Keychain/libsecret)
- **SessionStore**: AccessToken, RefreshToken, UserId, Username, Role. InMemory
- **CacheMaintenanceService**: ClearAllAsync, GetSizeAsync, Vacuum
- **DialogService**: ShowDialogAsync\<T\>(vm) → Task. Stack-based
- **NavigationService**: NavigateTo\<T\>(vm), Back, Forward — Stack-based
- **GlobalHubConnection**: SignalR connection lifecycle, auto-reconnect, event subscriptions
- **SettingsService**: Load/Save user settings, local preferences
- **ThemeService**: Apply theme (RequestedThemeVariant), LoadFromSettings
- **NotificationService** (клиентский): OS-level notifications (toast)
- **PlatformService**: OS detection, platform-specific paths
- **ChatInfoPanelStateStore**: IsOpen, SelectedChat — observable state
- **ChatNotificationApiService**: GET/PUT mute settings per chat
- **FileDownloadService**: DownloadFileAsync (progress, unique filename), GetDownloadsFolder (кроссплатформенно), OpenFileAsync (UseShellExecute), OpenFolderAsync (explorer/open/xdg-open)

---

## 10. ViewModels

### BaseViewModel (abstract, IDisposable)
- `[ObservableProperty]` IsBusy, ErrorMessage, SuccessMessage
- `GetCancellationToken()` — cancel previous → new CTS
- `SafeExecuteAsync(action)` — IsBusy guard, OperationCanceledException ignored, Exception → ErrorMessage
- `GetAbsoluteUrl(url)` — static, App.ApiUrl resolve
- Dispose pattern

### IRefreshable { IAsyncRelayCommand RefreshCommand }

### Factories
- **ChatsViewModelFactory**: `Create(parent, isGroupMode)` → ChatsViewModel
- **ChatViewModelFactory**: `Create(chatId, parent)` → ChatViewModel (10 зависимостей)

### Shell/MainWindowViewModel
- CurrentViewModel, CurrentDialog, HasOpenDialogs, IsDialogVisible
- Logout → CloseAll → AuthManager.LogoutAsync → NavigateToLogin
- ShowDialogAsync\<T\>, ToggleTheme, RefreshCurrentView (IRefreshable)

### Shell/MainMenuViewModel
- 8 menu items (0:Settings, 1-2:Groups/Contacts tabs, 3:Profile, 4:Admin, 5:Contacts, 6:StyleGuide, 7:Department)
- Stack-based back/forward history
- Cross-tab: SwitchToTabAndOpenChat/Message, OpenOrCreateChatAsync
- Dialog orchestration: ShowUserProfile, ShowPollDialog, ShowCreateGroupDialog, ShowEditGroupDialog
- CreateGroupChatAsync: POST → add members → set roles → upload avatar → open
- InitializeGlobalHubAsync, LoadContactsAndChatsAsync

### Auth/LoginViewModel
- InitializeAsync: WaitForInitialization (15s timeout) → NavigateToMainMenu or LoadSavedUsername
- LoginCommand: validate → AuthManager.LoginAsync → navigate. Password cleared

### ProfileViewModel (IRefreshable)
- Edit Profile/Username/Password. UploadAvatar (FilePickerOpenOptions). Logout command

### SettingsViewModel
- Auto-save Timer (800ms debounce). SelectedTheme, NotificationsEnabled, CanBeFoundInSearch
- ClearCacheAsync. Dispose: flush pending

### Chats/ChatsViewModel (IRefreshable)
- **Stale-while-revalidate LoadChats**: Phase 1: cache → instant UI. Phase 2: server → smart update + cache write
- Hub events: TotalUnreadChanged, UnreadCountChanged, MessageReceivedGlobally (preview + MoveChatToTop)
- Chat selection → create ChatViewModel via factory
- Search integration via GlobalSearchManager
- OpenChatByIdAsync, OpenOrCreateDialogWithUserAsync, FindDialogWithUser, CreateGroup

### Chats/ChatListItemViewModel
- ChatDto wrapper. Observable: Name, LastMessageDate, Avatar, Preview, UnreadCount. ToDto(), Apply(ChatDto)

### Chat/ChatViewModel (10 partial files, sealed, IAsyncDisposable)

**Core**: Dependencies (7 сервисов), _chatId, managers (Message/Attachment/Member), Parent (ChatsViewModel). 15+ observable properties, 25+ computed. PopularEmojis (32).

**Init**: LoadChat → LoadMembers → SubscribeChatEvents → GetReadInfo → LoadInitialMessages → LoadNotificationSettings → UpdatePollsCount → InitializeVoice → SubscribeInfoPanelEvents. Scroll to unread or bottom.

**Messages**: OnMessageReceived → AddReceivedMessage + transcription polling. OnMessageUpdated/Deleted. OnMessageVisible: mark unread=false + hub MarkMessageAsRead. MarkMessagesAsRead с 1s cooldown. LoadOlder/Newer. SendMessage: edit redirect, forward handling, upload attachments → POST.

**EditDelete**: StartEdit/SaveEdit/CancelEdit. Delete (soft). CopyMessageText (clipboard). **Mutual exclusive**: edit/reply/forward — starting one cancels others.

**Reply**: Start/Cancel. ScrollToReplyOriginal: find or LoadMessagesAround. HighlightAndScroll (2s reset).

**Forward**: Start/Cancel. ForwardPreviewText (deleted/voice/poll/files/text).

**Typing**: `Dictionary<int, DateTime>`, cleanup loop 500ms, 3.5s expiry, self-terminating. TypingText computed.

**Voice**: Start/Stop/Cancel/Send. AutoStop 300s. Min 0.5s. Transcription polling + retry.

**InfoPanel**: Subscribe UserStatusChanged, UserProfileUpdated, MemberJoined/Left. Reload after edit.

**Search**: ScrollToMessageAsync (find or LoadAround). HighlightMessage auto-reset. GoToSearchResult.

**Commands**: RemoveAttachment, InsertEmoji, AttachFile, ToggleInfoPanel, LeaveChat, OpenCreatePoll, OpenEditChat, OpenProfile, ToggleChatNotifications.

### Chat/Managers/ChatMessageManager
- State: bounds (oldest/newest loaded ID), hasMore flags, loadedMessageIds HashSet, ReadInfo. MaxGapFillBatches=5
- **LoadInitialMessages**: FirstUnreadId → LoadAround. Else cache (+ background RevalidateNewest). Else server page 1
- **LoadOlder/Newer**: cache-first + server fallback, merge/dedup. Insert/Append. UpdateBounds/DateSeparators/Grouping/SyncState
- **GapFill**: batched loop (max 5). If limit → ResetToLatest (clear cache + reload)
- **AddReceivedMessage**: dedup, create VM, IsUnread. Background cache write
- **HandleDeleted/Updated**: VM update + background cache
- **Grouping**: RecalculateGrouping, CanGroup (2min threshold, same sender, not system/deleted, same date)
- **DateSeparators**: "Сегодня"/"Вчера"/"d MMMM"/"d MMMM yyyy" (ru-RU)

### Chat/Managers/ChatAttachmentManager (IDisposable)
- PickAndAddFilesAsync (AllowMultiple), AddFileAsync (size check, thumbnail 200px)
- UploadAllAsync, Remove/Clear/Dispose

### Chat/Managers/ChatMemberLoader
- LoadMembersAsync: GET Chats.Members. Contact fallback: parse Name → GET Users.ById

### Chat/MessageViewModel (sealed, IDisposable)
- 30+ observable, 25+ computed. Retains original MessageDto
- Audio: Subscribe player events (filtered by messageId). PlayVoice: cache stream → play copy. Pause/Stop/Seek/Download
- UpdatePoll: ApplyDto or create new. PersistPollStateToCacheAsync (best-effort)
- ApplyUpdate: Content, IsEdited, EditedAt
- MarkAsDeleted: stop audio, clear all, dispose, notify computed
- Grouping (static): CanGroup, RecalculateGrouping. GroupPosition: Alone/First/Middle/Last

### Chat/MessageFileViewModel (IDisposable)
- Download state: NotStarted/Downloading/Completed/Failed/Cancelled
- Download/Cancel/Open/OpenFolder/Retry. Progress reporting. CTS with lock
- FormatFileSize, FormatDisplayFileName (truncate max 18 chars). FileIconResourceKey (pdf/word/excel/archive/default)

### Chat/PollViewModel, PollOptionViewModel
- Options collection, AllowsMultipleAnswers, CanVote, IsAnonymous, TotalVotes, HasVoted
- Single-select mutual exclusion. Vote/CancelVote: POST Polls.Vote
- ApplyDto: update options (add/remove/update)

### Chat/VoiceRecordingViewModel (sealed, IDisposable)
- State: Idle/Recording/Sending/Error. Elapsed, ElapsedFormatted. DispatcherTimer 200ms

### Chat/GlobalSearchManager (sealed, IDisposable)
- Dual mode: global (Messages.Search) и chat-local (Messages.ChatSearch)
- Debounce 300ms, CTS. ChatResults + MessageResults. LoadMoreMessagesAsync (next page)

### Admin/AdminViewModel (IRefreshable)
- Composition: UsersTabViewModel + DepartmentsTabViewModel. InitializeAsync: WhenAll → cross-link

### Admin/UsersTabViewModel
- LoadAsync → GET Admin.Users → RebuildGroups (GroupBy DepartmentId → DepartmentGroup)
- Create/Edit: UserEditDialogViewModel + TCS. ToggleBan: ConfirmDialog → POST
- FilteredGroups: search by 4 fields

### Admin/DepartmentsTabViewModel
- LoadAsync → GET Departments.GetAll → BuildHierarchy (recursive → HierarchicalDepartmentViewModel)
- Create/Edit/Delete: DepartmentHeadDialogViewModel, ConfirmDialog. FilteredDepartments: recursive

### Admin/HierarchicalDepartmentViewModel
- DepartmentDto wrapper. Level, IsExpanded, Children, computed UserCountText (склонение)

### Department/DepartmentManagementViewModel
- Head-only management. LoadAsync → check dept → CanManage → LoadMembers + LoadAvailable
- AddMember, RemoveMember. FilteredMembers

### Dialog ViewModels (все наследуют DialogBaseViewModel)
- **DialogBaseViewModel**: CloseRequested (Action?), Title, CanCloseOnBackgroundClick, InitializeAsync, Cancel/CloseOnBackgroundClick
- **ConfirmDialogViewModel**: Message, Task\<bool\> Result via TCS
- **ChatEditDialogViewModel**: Create/edit group. SelectableUserItem. SaveAction callback: (ChatDto, memberIds, adminIds, avatarStream?, fileName?) → bool
- **PollDialogViewModel**: 2-10 options. CreateAction callback. Min 2 non-empty validation
- **UserEditDialogViewModel**: Admin create/edit. CreateAction/UpdateAction callbacks. Validation
- **UserPickerDialogViewModel**: Dual-mode: multi-select (Save) и single-select (TCS\<UserDto?\>). Search filter
- **UserProfileDialogViewModel**: Avatar bitmap. SendMessage → OpenChatWithUserAction callback
- **DepartmentDialogViewModel**: Simple create/edit. NoParentPlaceholder(Id=-1)
- **DepartmentHeadDialogViewModel**: Head selection. GetDescendantIds (BFS) prevents circular parents

---

## 11. Views (паттерны)

### ChatView (code-behind — scroll management)
Scroll management: LoadOlderWithPreserve (сохранение позиции при подгрузке старых), retry-based DoScrollToEnd, visibility tracking через TransformToVisual. CollectionChanged: auto-scroll если внизу, иначе UnreadCount++. Подгрузка: offset<100 → older, distBottom<100 → newer. Extent compensation при layout-изменениях.

### Ключевые контролы
- **AvatarControl**: ImageBitmap/Source/DisplayName/IsOnline/Size/IsCircular. Адаптивный OnlineIndicatorSize
- **RichMessageTextBlock**: URL regex → кликабельные Inlines (#4A9EEA + Underline)
- **MessageControl**: 8 StyledProperty\<ICommand?\> (Edit/Copy/Delete/OpenProfile/Reply/ScrollToReply/RetryTranscription/Forward)
- **SearchBox**: TwoWay binding + ClearCommand

### MainWindow
- Dialog animations: Open/Closing CSS classes, 250ms, CancellationToken
- Window padding: Maximized → Thickness(7). Title bar drag

### Converters
ConverterBase\<TIn,TOut\> (типизирован��ый) и ConverterBase (нетипизированный). ConverterLocator (singleton, Dictionary по именам). XAML: `{c:Converter Name=X}`. Категории: Boolean (8), Comparison (1), DateTime (3), Domain (2), Enum (1), Generic (4), Hierarchy (2), Message (2).

### App.axaml.cs
- ApiUrl: DEBUG → `https://localhost:7190/`, Release → `https://localhost:5274/`
- Initialize: DI (ValidateScopes/ValidateOnBuild), AuthenticatedImageLoader, LocalDatabase+Maintenance (фон), ThemeService

---

## 12. Бизнес-правила (cross-cutting)

1. **Роль динамическая**: AdminDepartmentId → Admin, Head отдела → Head, иначе → User
2. **Timing-safe auth**: dummy BCrypt hash при неверном логине
3. **Refresh token rotation** + FamilyId. Replay → отзыв всей семьи. Max 5 сессий, cleanup >60 дней
4. **Soft delete**: IsDeleted=true, Content=null. VoiceMessage удаляется физически
5. **Анонимные опросы**: голоса не включаются в DTO
6. **Изображения → WebP** через ImageSharp (Quality из настроек)
7. **MissingFileCleanup**: middleware автоочистки БД при 404 на файлы
8. **Cache strategy**: stale-while-revalidate, gap-fill max 5 batches → reset to latest
9. **Mutual exclusive modes**: edit/reply/forward — начало одного отменяет другие
10. **Message grouping**: 2min threshold, same sender, not system/deleted, same date
11. **Транскрипция**: whisper.cpp, SemaphoreSlim(1), PCM 16kHz/16bit/mono, timeout 5мин
12. **Poll vote**: replace-стратегия (удаление старых + добавление новых)
13. **Contact chat**: имя = имя собеседника, аватар = аватар собеседника, максимум 2 участника, дедупликация
14. **SystemMessages**: пропускаются для Contact чатов, fire-and-forget safe