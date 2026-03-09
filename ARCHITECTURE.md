## Проект

Локальный корпоративный мессенджер: MessengerAPI (ASP.NET Core, REST+SignalR), MessengerDesktop (Avalonia MVVM), MessengerShared (.NET Class Library — DTO, enums, ApiResponse).

Docker: multi-stage build, non-root, port 8080. Compose: API + PostgreSQL 16 + ZAProxy scan. Volumes: postgres_data, uploads, avatars.

---

## MessengerAPI

### Точка входа (Program.cs)

Npgsql legacy timestamps. DI: database, infrastructure, business services, JSON, auth, Swagger, rate limiter, SignalR, CORS.

Rate Limiting: глобальный SlidingWindow 100req/10s на IP; именованные: login 5/min/IP, upload 10/min/user, search 15/min/user, messaging 30/min/user. OnRejected → 429 + Retry-After.

Pipeline: ExceptionHandling → Swagger(dev) → HTTPS → security headers → static files (без auth) → MissingFileCleanup → CORS → RateLimiter → Auth. Routes: health, robots, sitemap (anon), controllers, ChatHub.

### Common

**AppDateTime** — singleton, DI TimeProvider, UtcNow с Kind=Unspecified для Npgsql.

**Result Pattern** — `Result`/`Result<T>` с `ResultErrorType` enum (Validation→400, Unauthorized→401, Forbidden→403, NotFound→404, Conflict→409, Internal→500). Фабрики: `NotFound()`, `Forbidden()`, `Unauthorized()`, `Conflict()`, `Internal()`. `FromFailure(Result)` — propagation ошибок между типами. `ResultExtensions` для Hub-контекста: `UnwrapOrDefault`, `UnwrapOrFallback`, `TryUnwrap` с CallerMemberName логированием. `Result<T>.Match(onSuccess, onFailure)`.

**UrlHelpers** — `BuildFullUrl` extension на string: null/empty→passthrough, already absolute→passthrough, else→`urlBuilder.BuildUrl(path)`.

**ValidationHelper** — regex username `^[a-z0-9_]{3,30}$`, password ≥6. Возвращают Result.

### Configuration

**Auth** — JWT Bearer из TokenService.CreateValidationParameters. SignalR token из query. Fallback: все endpoints требуют auth.

**DI** — Database: PostgreSQL+Npgsql, enum mapping. Infrastructure: MemoryCache, TimeProvider, AppDateTime, OnlineUserService (singleton), scoped: CacheService, AccessControlService, FileService, TokenService, HubNotifier, HttpUrlBuilder. Business: scoped сервисы (Auth, User, Admin, Chat, ChatMember, SystemMessage, Notification, Message, Poll, ReadReceipt, Department), singletons: TranscriptionQueue/Service, hosted: TranscriptionBackgroundService.

**JwtSettings** — Secret, AccessTokenLifetimeMinutes=15, RefreshTokenLifetimeDays=30, Issuer/Audience.

**MessengerSettings** — AdminDepartmentId=1, MaxFileSize=20MB, BcryptWorkFactor=12, MaxImageDimension=256, DefaultPageSize=50, MaxPageSize=100.

**StaticFiles** — wwwroot/uploads + avatars via PhysicalFileProvider. Custom MIME types (ipynb, md, yaml, py, cs, ts, tsx, jsx, webp). Uploads: ServeUnknownFileTypes=true, DefaultContentType=octet-stream. Avatars: no-cache headers. Middleware стоит до UseAuthentication — файлы доступны без авторизации.

### Controllers

**BaseController** — GetCurrentUserId из claims. `ExecuteAsync(Result/Result<T>)`: Success→Ok, Failure→MapFailure по ResultErrorType. Без catch — исключения в ExceptionHandlingMiddleware.

**AuthController** — `POST login` [anon, rate:login], `POST refresh` [anon] (expired access + refresh → new pair), `POST revoke` [auth] (отзыв всех refresh tokens).

**AdminController** — [Admin]. GET/POST users, toggle-ban.

**ChatsController** — CRUD чатов, members (add/remove/role), avatar upload. Доступ: часть через сервис, часть через CheckUserChat*Async → FromFailure.

**DepartmentsController** — CRUD (admin-only), members, can-manage. CancellationToken.

**FilesController** — POST upload [100MB, rate:upload], проверка доступа → fileService.SaveMessageFileAsync.

**MessagesController** — CRUD, пагинация (chat/{chatId}, around/before/after/{messageId}), search (chat + global) [rate:search], transcription get/retry.

**NotificationsController** — chat settings get/mute, all settings.

**PollsController** — get, create (→MessageDto), vote.

**ReadReceiptsController** — mark-read, unread-count (chat + all).

**UsersController** — CRUD, avatar, username/password change, online status.

### SignalR (ChatHub)

[Authorize]. OnConnected: register online, join user_{id} + chat groups. OnDisconnected: offline → ExecuteUpdateAsync LastOnline, broadcast.

JoinChat/LeaveChat: CheckIsMemberAsync → HubException. GetReadInfo: UnwrapOrDefault. MarkAsRead/MarkMessageAsRead: TryUnwrap + early return, notify caller + others. GetUnreadCounts: UnwrapOrFallback. SendTyping → UserTyping. GetOnlineUsersInChat: CheckIsMemberAsync.

### Mapping

**MessageMappings** — `Message.ToDto()`: IsDeleted→placeholder content, IsOwn подавляется для системных, voice из навигации VoiceMessage (duration, transcription, FilePath→BuildFullUrl для VoiceFileUrl, fileName, contentType, fileSize), system-поля (eventType, targetUser), ReplyTo→ToReplyPreviewDto, Forward→ToForwardInfoDto, Files→ToDto, Poll→ToDto. `ToReplyPreviewDto`, `ToForwardInfoDto`.

**FileMappings** — `MessageFile.ToDto()`: Path→BuildFullUrl, DeterminePreviewType (image/video/audio/file), GetFileSize (filesystem lookup). `DeterminePreviewType` — public static.

**Прочие** — Chat→ChatDto (с dialogPartner), Poll→PollDto (анонимность скрывает голоса), User→UserDto (FormatDisplayName).

### Middleware

**ExceptionHandling** — catch-all → 500 ApiResponse, dev: stack trace. Все бизнес-ошибки через Result.

**MissingFileCleanup** — GET/HEAD к /uploads, /avatars при 404: поиск в БД, удаление ссылок.

### Services

Все бизнес-сервисы возвращают Result (если не указано иное). Наследуют BaseService: `SaveChangesAsync` → Result (DbUpdateConcurrencyException→Conflict, unique violation→Conflict, прочие→Internal). `FindEntityAsync` → Result<T> (NotFound).

#### Auth

**AuthService** — MaxActiveSessions=5. Login: timing-safe (dummy BCrypt), проверка бана→Forbidden, роль (Admin/Head/User), token pair, refresh hash в БД с FamilyId, EnforceSessionLimit, cleanup expired. Refresh: claims из expired token (GetPrincipalFromExpiredToken→Result), replay detection (UsedAt/RevokedAt→отзыв семьи FamilyId), ротация (старый→UsedAt, новый→same FamilyId). Revoke: bulk ExecuteUpdateAsync всех active tokens.

**TokenService** — HMAC-SHA256, secret≥32. TokenPair record. Claims: NameIdentifier/Jti/Iat/Role. GenerateTokenPair: access (configurable lifetime) + random refresh (64 bytes Base64). GetPrincipalFromExpiredToken→Result (ValidateLifetime=false, boundary catch). HashToken: SHA-256.

#### Chat

**ChatService** — Access delegation: CheckUserChat*Async → accessControl.Check*Async. GetUserChats: GroupJoin с последним сообщением + unread + Contact partner. CreateChat: транзакция, после коммита → systemMessages.CreateAsync. UpdateChat: CheckIsAdmin→FromFailure, owner checks. DeleteChat: CheckIsOwner. UploadAvatar: CheckIsAdmin. Запрет Contact.

**ChatMemberService** — AddMember: CheckIsAdmin, dedup→Conflict, systemMessages.CreateAsync (member_added). RemoveMember: условная проверка прав, защита Owner→Forbidden, systemMessages (member_left/member_removed). UpdateRole: CheckIsOwner, защита Owner, systemMessages (role_changed). LeaveAsync → RemoveMember. Cache invalidation.

**SystemMessageService** — Централизованный, fire-and-forget с try-catch+LogWarning. CreateAsync: пропуск Contact, создание Message с IsSystemMessage+SystemEventType+TargetUserId, обновление LastMessageTime, reload с Include, broadcast через hubNotifier. Используется ChatService и ChatMemberService.

**NotificationService** — CRUD NotificationsEnabled. SendNotificationAsync: void fire-and-forget.

#### Department

**DepartmentService** — CRUD с защитой от циклов (BFS→Result), запрет удаления при children/users. Состав: CheckCanManageAsync (Admin/Head), Add (перемещение Forbidden для не-Admin), Remove (запрет Head).

#### Infrastructure

**AccessControlService** — Per-request Dict + L2 IMemoryCache. Bool-методы (IsMember/IsOwner/IsAdmin) для условных проверок. Result-методы (CheckIs*→Forbidden) для guard-проверок + FromFailure.

**CacheService** — UserChats: 5min+2min sliding. Membership: 10min+3min sliding (кеширует null).

**HttpUrlBuilder** — IUrlBuilder impl. `BuildUrl`: null→null, already http(s)→passthrough, else→`{scheme}://{host}/{path.TrimStart('/')}` из HttpContext.Request.

**HubNotifier** — SendToChat/User → SignalR groups. Fire-and-forget try-catch.

**OnlineUserService** — Singleton, ConcurrentDictionary<int, ConcurrentDictionary<string, byte>>. Timer cleanup каждые 5min, атомарный TryRemove(KeyValuePair).

#### Messaging

**MessageService** — Create: валидация Reply/Forward, voice→VoiceMessage entity (StripBaseUrl для FilePath, pending transcription) + TranscriptionQueue, файлы→SaveMessageFiles (StripBaseUrl для Path), broadcast+notify+unread. StripBaseUrl: strip urlBuilder.BuildUrl("/") prefix, гарантирует ведущий `/`. Get: пагинация Reverse, Around/Before/After по ID. Update: owner check, запрет system/poll/voice/forward/deleted. Delete: soft-delete, voice→физическое удаление аудио + Remove VoiceMessage. Search/GlobalSearch: ILike с EscapeLikePattern, фильтр !IsSystemMessage, highlight ±40 chars.

**FileService** — SaveImage: MIME валидация, ресайз ImageSharp, WebP. SaveMessageFile: проверка membership, uploads/chats/{chatId}/{guid}{ext}, возвращает MessageFileDto с urlBuilder.BuildUrl. DeleteFile: void safe.

**PollService** — Get, Create (транзакция Message+Poll+Options, broadcast), Vote (delete old + create new, broadcast PollUpdate).

**TranscriptionService** — Whisper CLI, ggml-small-q5_1.bin, SemaphoreSlim(1,1). NAudio PCM 16kHz/16bit/mono. Timeout 5min. Работает с VoiceMessage entity. Channel<int> Bounded(100) + BackgroundService.

#### ReadReceipt

**ReadReceiptService** — MarkAsRead: target messageId, update LastReadMessageId (monotonic). GetChatReadInfo: GroupBy → Count + FirstUnreadId. GetAllUnreadCounts: subquery. GetUnreadCountsForChats: batch Dictionary (без Result).

#### User

**UserService** — CRUD с online status. UploadAvatar: FindEntity→SaveImage→SaveChanges (полная Result chain через FromFailure). ChangeUsername: regex + unique→Conflict. ChangePassword: BCrypt verify→Unauthorized.

**AdminService** — GetUsers, CreateUser (validation, unique→Conflict, department→NotFound, BCrypt), ToggleBan.

### Models

**Core**: Chat (Id, Name, Type, CreatedAt, LastMessageTime, Avatar), ChatMember (PK:Chat+User, JoinedAt, NotificationsEnabled, LastReadMessageId, Role), User (Id, Username unique, ФИО, PasswordHash, LastOnline, DepartmentId, Avatar, IsBanned, DisplayName computed), UserSetting (1:1, Theme?, Notifications).

**Messaging**: Message (ChatId, SenderId, Content, CreatedAt, EditedAt, IsDeleted, ReplyTo, ForwardedFrom, IsSystemMessage, SystemEventType, TargetUserId; [NotMapped] IsVoiceMessage computed from VoiceMessage!=null), VoiceMessage (PK:MessageId 1:1, Duration, TranscriptionStatus/Text, FilePath, FileName, ContentType, FileSize; cascade delete), MessageFile (FileName, ContentType, Path).

**Auth**: RefreshToken (TokenHash SHA-256, JwtId, FamilyId, CreatedAt, ExpiresAt, UsedAt, RevokedAt, ReplacedByTokenId; computed IsActive).

**Other**: Department (Name, Parent tree, ChatId, HeadId), Poll/PollOption/PollVote (unique Poll+User+Option), SystemSetting (KV store).

**DbContext**: PostgreSQL enums, snake_case, sequences. Indices: messages(chatId,createdAt), chatMembers(lastRead, userId), refreshTokens(tokenHash, userId, familyId). Cascades: Chat→CreatedBy, User→RefreshTokens, Message→VoiceMessage; SetNull: остальные.

---

## MessengerDesktop

### Infrastructure

**App** — Dark default. ApiUrl (debug/release). DI: singleton services (DB, repos, cache, platform, hub, notification, AudioPlayerService), HttpClient 30s, auth chain, navigation/dialog, VMs (main singleton, rest transient). AuthenticatedImageLoader. Dispose: sequential cleanup (notification→platform→mainVM→apiClient→authManager→session→dialog→nav→localDb→audioPlayer→ServiceProvider).

**ViewLocator** — "ViewModel"→"View" convention.

**ApiEndpoints** — Статические URL-билдеры по контроллерам.

**AppConstants** — MaxFileSize=20MB, Debounce=300ms, PageSize=50, LoadMore=30, Search=20, Highlight=3000ms, MarkAsRead debounce=300ms/cooldown=1s, Typing send=1200ms/indicator=3500ms.

### Services

**ApiClientService** — HttpClient + auth headers. Auto-refresh: SendWithRefreshAsync (Func<> для retry), перехват 401 (кроме Login/Refresh/Revoke), TryRefreshTokenAsync, lambda retry с новым токеном. Upload: stream rewind при retry. GetStream: >10MB→temp file, ≤10MB→memory.

**AuthService (client)** — Login/Refresh/Revoke HTTP calls. IsAccessTokenValid: локальная JWT exp проверка с 30s буфером.

**AuthManager** — LoadStoredSession: SecureStorage → local JWT check → SetSession или Refresh. Login: SemaphoreSlim, cache clear on user change, SaveAuth. Logout: RevokeAsync (best-effort), cache clear; RememberMe→сохранить токены, !RememberMe→очистить. TryRefreshTokenAsync: SemaphoreSlim + shared Task<bool> (все 401 ожидают один refresh); неудача→ForceLogout. SecureStorage keys: token, refresh_token, user_id, role, remember_me, saved_username.

**SecureStorage** — AES-256 + PBKDF2 100K SHA256, machine-bound salt, random IV.

**SessionStore** — ObservableObject. RoleHierarchy (User<Head<Admin). SetSession, UpdateTokens, ClearSession, SessionChanged event.

**GlobalHubConnection** — Единственный SignalR hub (app-wide). Unread tracking: Dictionary<int,int> + totalUnread with lock. Debounce: lastSentReadMessageId/Time, lastSentTypingTime — reset при SetCurrentChat. Hub subscriptions: List<IDisposable>. Events для UI: UnreadCountChanged, TotalUnreadChanged, MessageReceived/Updated/DeletedGlobally, UserTyping, MessageRead, MemberJoined/Left, Reconnected, NotificationReceived, UserStatusChanged, UserProfileUpdated. Handlers: cache incoming messages, increment unread, forward events. Methods: Connect/Disconnect, SetCurrentChat, MarkChatAsRead, MarkMessageAsRead (debounce + monotonic), SendTyping (debounce), GetReadInfo, GetUnreadCounts. Reconnecting: 401→TryRefresh. Reconnected: reload unread + reconcile.

**Audio**: NAudioRecorderService (WaveInEvent 16kHz/16bit/mono, lock, Stopwatch, IgnoreDisposeStream wrapper). AudioPlayerService (singleton): NAudio WaveOutEvent, play/pause/resume/stop/seek, CurrentMessageId tracking, Timer position updates 50ms, events (PlaybackStarted/Paused/Resumed/Stopped, PositionChanged). TranscriptionPoller: ConcurrentDictionary, exponential backoff [1,2,4,8,16]s max 60, safe dispose (StopPolling only cancels, finally does cleanup).

**NavigationService** — Stack<Type> history, NavigateTo/GoBack.

**DialogService** — Stack dialogs, Channel<CloseRequest> + background processing. Animation: TaskCompletionSource + 1s timeout, nested-aware (animate only first/last dialog transition). Dispose: 2s timeout wait for lock.

**Other services**: PlatformService (clipboard, MainWindow), SettingsService (JSON file), NotificationService (WindowNotificationManager), FileDownloadService (80KB buffer, progress, OS-specific folders), ThemeService (Dark/Light toggle), CacheMaintenanceService (size+timing+vacuum), ChatInfoPanelStateStore.

### ViewModels

**BaseViewModel** — ObservableObject + IDisposable. IsBusy, Error/SuccessMessage, SafeExecuteAsync, CancellationToken.

**MainWindowViewModel** — CurrentViewModel, dialog system (ShowDialogAsync), logout, theme toggle, refresh.

**MainMenuViewModel** — Lazy tab VMs. Navigation: Stack back/forward. Search with CTS. SwitchToTabAndOpenChat/Message. Dialog actions: CreateGroup (create→add members→set roles→avatar), EditGroup (diff members add/remove, roles promote/demote). InitializeGlobalHub.

**LoginViewModel** — IsInitializing. InitializeAsync: wait auth→restore session or load saved username. Login [CanExecute guard].

**ChatsViewModel** — IRefreshable. Stale-while-revalidate: cached chats → server refresh. On new message: update preview/date, move chat to top. OpenOrCreateDialog. GlobalSearchManager (dual mode: global chats+messages, chat-local).

**ChatViewModel** — Partial class (8 files). Managers: ChatMessageManager, ChatAttachmentManager, ChatMemberLoader. All realtime via GlobalHubConnection.

- **Lifecycle**: InitializeChatAsync (LoadChat→Members→SubscribeEvents→ReadInfo→Messages→Notifications→Polls→Voice→InfoPanel). Event subscription with _chatEventsSubscribed flag, cleanup on error (prevent leak of transient VM via delegates on singleton hub). Dispose: cleanup subscriptions, cancel CTS, dispose voice/attachments.
- **Messages**: All Dispatcher.UIThread.Post handlers check _disposed. OnMessageReceived→add+transcription polling. OnMessageVisible→MarkMessageAsRead (debounce). Send: forward content inheritance, upload→POST. LoadOlder/Newer with _disposed guard.
- **Commands**: attachments, emoji, info panel toggle, leave chat, create poll, edit chat (diff sync), profile, notifications toggle.
- **Edit/Reply/Forward**: edit (unchanged→cancel, empty→delete), reply (cancel edit+forward), forward with preview (deleted/voice/poll/files/text).
- **Search**: ScrollToMessage (find→scroll+highlight, else LoadAround), HighlightMessage (auto-reset).
- **Typing**: Dictionary<int,DateTime>, cleanup loop (auto-starts, auto-stops when empty, no CTS recreation).
- **InfoPanel**: Subscribe 4 sources (UserStatus, UserProfile, Members.CollectionChanged, MemberJoined/Left). Online/profile updates, member dedup.
- **Voice**: Record (0.5s–300s), auto-stop with cancellable timer. SendVoice: upload→create MessageDto with voice fields. TranscriptionPoller callback checks _disposed.

**ChatMessageManager** — Cache-first + revalidation. LoadInitial: unread→LoadAround, else cache→render + background revalidate. LoadOlder/Newer: cache-first + server + dedup. GapFillAfterReconnect: iterative, MaxGapFillBatches=5, exceed→ResetToLatestAsync (clear cache + reload). DateSeparators. Grouping: 2min threshold, CanGroup excludes system messages.

**ChatAttachmentManager** — File picker, size/MIME check, thumbnail with resize (maxDimension=200, dispose full-size), upload all.

**MessageViewModel** — IDisposable, ~30 computed properties. System messages suppress: text, sender, delivery status, edit/delete, files, IsOwn. Voice player: subscribes to singleton AudioPlayerService events (Started/Paused/Resumed/Stopped/PositionChanged), filters by messageId. States: IsVoicePlaying/Paused/Loading, VoicePositionPercent/Text, VoiceError. Computed: ShowPlayButton/ShowPauseButton/ShowResumeButton. Commands: PlayVoice (load via GetStreamAsync→cache MemoryStream→Play copy), PauseVoice, StopVoice, SeekVoice(percent), DownloadVoice (via FileDownloadService). Audio cache: _cachedAudioStream (MemoryStream, disposed on Dispose/MarkAsDeleted). UpdateTranscription, MarkAsDeleted (stop player, reset voice state, dispose cache), MarkAsRead. Static grouping: Alone/First/Middle/Last.

**MessageFileViewModel** — IDisposable. Download state machine (5 states) with CTS. Progress on UIThread with disposed guard. Open file/folder.

**PollViewModel/PollOptionViewModel** — Parent-child. Single-selection enforcement. Vote/CancelVote via API.

**ProfileViewModel** — IRefreshable. Edit states for profile/username/password. Upload avatar.

**SettingsViewModel** — Theme, notifications. 800ms debounce auto-save.

**AdminViewModel** — Users+Departments tabs. Parallel init + cross-reference. Search propagation. Commands route to tabs.

**Dialog VMs**: ChatEditDialog (create/edit group, ManageParticipants/Admins→UserPicker, avatar), UserPickerDialog (dual-mode: multi-select with callback, single-select with TaskCompletionSource), ConfirmDialog (TCS<bool>), DepartmentDialog (cycle check delegated to server), DepartmentHeadDialog, PollDialog, UserEditDialog, UserProfileDialog.

### Views (summary)

MainWindow: acrylic blur, 3-column grid, dialog overlay with open/close animation. MainMenu, Login (Enter→submit). ChatsView: CompactMode 72px with hysteresis, GridSplitter, InfoPanel hide <820px. ChatView: scroll preservation, visibility tracking debounced. MessageControl: triple mode — system (centered, italic, no bubble) / voice (play/pause button + slider + position/duration + error + transcription panel) / normal (avatar, bubble, context menu, files, polls). Voice player UI: VoicePlayBtn (36px circle, accent bg), VoiceSlider (0-100%), time display (position/duration), CircularProgress for loading, context menu «Скачать аудио». AdminView: 2-column sidebar+content, DepartmentCardView (recursive with Canvas hierarchy lines), UserCardView. Dialogs: UserPickerDialog (multi→checkbox, single→click-to-select).

### Controls

AvatarControl: StyledProperties (Size, Source, DisplayName, IsOnline, etc), bitmap priority, initials fallback, async image loader panel wrapper, size classes (Small 32→XXLarge 100). RichMessageTextBlock: URL regex, LinkBrush, hit-testing via TextLayout. CircularProgress: arc rendering, indeterminate animation. ThemeSelectorControl: 3 options with mini-previews, bidirectional enum↔bool sync.

### Data (Local SQLite Cache)

**LocalDatabase** — sqlite-net-pcl. WAL, synchronous=NORMAL, cache_size=-4000, mmap_size=32MB. Schema migration via user_version, downgrade→drop. FTS5 with triggers.

**Entities**: CachedChat, CachedMessage (flattened: voice, system, sender, reply, forward, poll/files JSON), CachedReadPointer, CachedUser, ChatSyncState (OldestLoadedId/NewestLoadedId/HasMore).

**Repositories**: ChatCache (upsert, batch transactions), MessageCache (Latest/Before/After/Around, FTS5+LIKE fallback, MarkDeleted). LocalCacheService facade: SyncState-aware, ClearChatMessages.

**CacheMapper** — Static. DTO↔Cache (flatten/unflatten, JSON for poll/files, voice/system field mapping, ChatType int↔enum).

### Converters

ConverterLocator singleton ~35 converters. Categories: Bool (BoolToValue<T>, And/Or multi), Comparison, DateTime (7 formats, relative time, LastSeen multi), Domain (Initials, PollToVM), Enum, Level (margin/visibility), Message (alignment/margin), Generic (pluralize Russian, PercentToWidth).

---

## MessengerShared

**DTOs**: Auth (Login/Refresh request, AuthResponse, TokenResponse), Chat (ChatDto, ChatMemberDto, NotificationSettings, Update), Department (DepartmentDto), Message (MessageDto with reply/forward/poll/files/voice/system fields, PagedMessages, Search results), Notification, Online (OnlineStatusDto), Poll (Create/Vote/PollDto), ReadReceipt (MarkAsRead, UnreadCount, AllUnread, ChatReadInfo), User (UserDto, Create, ChangePassword/Username, Avatar).

**Enums**: ChatRole (Member/Admin/Owner), ChatType (Chat/Department/Contact/DepartmentHeads), Theme (light/dark/system), UserRoles (User/Head/Admin), SystemEventTypes (chat_created, member_added/removed/left, role_changed).

**Response**: ApiResponse<T> (Success, Data, Message, Error, Details, Timestamp).

---

## Архитектурные паттерны

**Структура**: MVVM + ViewLocator convention. Partial VM decomposition (ChatViewModel 8 files). Manager delegation. Factory pattern для VM DI. Repository+Facade (SQLite). Recursive UI controls (DepartmentCard). Command propagation via StyledProperty. Nested dialogs via delegates.

**Realtime**: Single GlobalHubConnection (app-wide), server-side ChatHub с группами chat_{id}/user_{id}. ChatViewModel фильтрует события по chatId через обёртки. Channel<T>+BackgroundService для transcription queue и dialog close queue.

**Data flow**: Result pattern — единый для всех серверных сервисов. BaseController.ExecuteAsync маппит ResultErrorType→HTTP. ExceptionHandlingMiddleware — чистый safety net. BaseService.SaveChangesAsync — единственная точка конвертации EF exceptions→Result. AccessControlService: bool-методы для условий, Result-методы для guards. Hub: ResultExtensions (TryUnwrap/UnwrapOrDefault/UnwrapOrFallback). Fire-and-forget (HubNotifier, FileService.Delete, NotificationService.Send): try-catch без Result.

**Caching**: Stale-while-revalidate (ChatsVM). Cache-first messages (ChatMessageManager). L1(per-request Dict)+L2(MemoryCache) access control. FTS5+LIKE fallback. WAL+PRAGMA SQLite. Schema migration user_version. Voice audio: per-MessageViewModel MemoryStream cache (download once, play many).

**Security**: Timing-safe auth. JWT 15min + refresh rotation (FamilyId, replay detection→revoke family, SHA-256 storage). Session limit 5. Concurrent refresh: SemaphoreSlim + shared Task. Auto-refresh: 401 intercept + lambda retry. Local JWT exp check (30s buffer). AES-256+PBKDF2 SecureStorage. Role hierarchy. RememberMe controls token persistence. Rate limiting (global + named). Non-root container + ZAP scan. Static files served without auth (before UseAuthentication).

**UI/UX**: Adaptive layout (CompactMode, responsive InfoPanel). Scroll preservation. Visibility tracking (debounced read receipts). Dialog animation (TCS+timeout, nested-aware). Message grouping (2min, Alone/First/Middle/Last). Typing cleanup loop (auto-start/stop). System messages: centered inline blocks, excluded from grouping/search. Voice messages: inline player with play/pause/seek/progress, download via context menu.

**Async**: Fire-and-forget init with TCS. Exponential backoff polling. Debounce (settings 800ms, typing, mark-read). TCS dialog results. Download state machine with CTS. Navigation history stack. Bounded gap fill (5 batches→reset). Stream-safe retry (rewind+new content).

**Domain**: Voice pipeline (record WAV 16kHz/16bit/mono → validate 0.5-300s → upload → create message with voice metadata → transcribe via Whisper CLI → poll status with exponential backoff). Inline voice playback (singleton AudioPlayerService, per-VM event filtering, cached audio streams, exclusive playback — new play stops previous). System messages via centralized SystemMessageService (ChatService+ChatMemberService→create→broadcast). Diff-based group management. Dual-scope search. Optimistic unread tracking. BFS cycle prevention (departments). Forward content inheritance. Universal UserPicker (multi/single select).