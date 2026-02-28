# Architecture.MD

## Обзор проекта

Корпоративный мессенджер, состоящий из трёх проектов в одном решении (solution):

| Проект | Технология | Назначение |
|---|---|---|
| **MessengerAPI** | ASP.NET Core Web API | Серверная часть (REST API + SignalR) |
| **MessengerDesktop** | Avalonia UI (MVVM) | Десктопный клиент (кроссплатформенный) |
| **MessengerShared** | .NET Class Library | Общие DTO, перечисления, обёртки ответов |

---

## MessengerAPI — Серверная часть

### Точка входа

**`Program.cs`** — Включает `Npgsql.EnableLegacyTimestampBehavior`. Привязка конфигурации `MessengerSettings`, `JwtSettings`. Регистрация: `AddMessengerDatabase`, `AddInfrastructureServices`, `AddBusinessServices`, `AddMessengerJson`, `AddMessengerAuth`, `AddMessengerSwagger`, SignalR, CORS (AllowAnyHeader/Method, AllowCredentials). Pipeline: `UseExceptionHandling`, Swagger (dev), HTTPS redirect, `UseMessengerStaticFiles`, CORS, Auth. Маршруты: `GET /` (health), `GET /robots.txt`, `GET /sitemap.xml` (AllowAnonymous), `MapControllers`, `MapHub<ChatHub>("/chatHub")`.

### Common — Общие утилиты

**`Common/AppDateTime.cs`** — Статический класс — единый источник времени. `UtcNow` → `DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)` для совместимости Npgsql.

**`Common/Result.cs`** — Result-паттерн. `Result` — `Success()`/`Failure(error)`. `Result<T>` — `Success(value)`/`Failure(error)`, деконструктор `(success, data, error)`, метод `Match<TResult>(onSuccess, onFailure)`.

**`Common/ValidationHelper.cs`** — `ValidateUsername` — source-generated regex `^[a-z0-9_]{3,30}$`. `ValidatePassword` — минимум 6 символов. Возвращают `Result`.

### Configuration — Конфигурация и DI

**`Configuration/AuthConfiguration.cs`** — `AddMessengerAuth`: JWT Bearer с параметрами из `TokenService.CreateValidationParameters`. SignalR-токен из query `access_token` для `/chatHub`. Fallback-политика: все эндпоинты требуют аутентификации.

**`Configuration/DependencyInjection.cs`** — `AddMessengerDatabase` — PostgreSQL/Npgsql, маппинг enum `Theme`/`ChatRole`/`ChatType`, dev: SensitiveDataLogging. `AddInfrastructureServices` — MemoryCache, HttpContextAccessor, OnlineUserService(singleton), scoped: CacheService, AccessControlService, FileService, TokenService, HubNotifier, HttpUrlBuilder. `AddBusinessServices` — scoped: AuthService, UserService, AdminService, ChatService, ChatMemberService, NotificationService, MessageService, PollService, ReadReceiptService, DepartmentService; singletons: TranscriptionQueue, TranscriptionService; hosted: TranscriptionBackgroundService. `AddMessengerJson` — ReferenceHandler.IgnoreCycles, WriteIndented в dev.

**`Configuration/JwtSettings.cs`** — POCO: `Secret`, `LifetimeHours`=24, `Issuer`="MessengerAPI", `Audience`="MessengerClient". Section: `"Jwt"`.

**`Configuration/MessengerSettings.cs`** — POCO: `AdminDepartmentId`=1, `MaxFileSizeBytes`=20MB, `BcryptWorkFactor`=12, `MaxImageDimension`=1600, `ImageQuality`=85, `DefaultPageSize`=50, `MaxPageSize`=100. Section: `"Messenger"`.

**`Configuration/StaticFilesConfiguration.cs`** — `UseMessengerStaticFiles`: создаёт `wwwroot/uploads` и `wwwroot/avatars`. Кастомный ContentTypeProvider (ipynb, md, yaml, py, cs, ts, tsx, jsx, webp). Аватары: `Cache-Control: no-cache, no-store`.

**`Configuration/SwaggerConfiguration.cs`** — Swagger v1 с JWT Bearer SecurityDefinition (ApiKey в Header).

### Controllers — API-контроллеры

**`Controllers/BaseController.cs`** — Абстрактный `[ApiController, Route("api/[controller]")]`. `GetCurrentUserId()` из ClaimTypes.NameIdentifier. Два `ExecuteAsync` (для `Result<T>` и `Result`) с обработкой: Unauthorized→401, KeyNotFound→404, Argument/InvalidOp→400, прочие→500. `Forbidden()` → 403.

**`Controllers/AuthController.cs`** — `[AllowAnonymous, EnableRateLimiting("login")]`. `POST login` → `AuthService.LoginAsync`.

**`Controllers/AdminController.cs`** — `[Authorize(Roles="Admin")]`. `GET users`, `POST users`, `POST users/{id}/toggle-ban`.

**`Controllers/ChatsController.cs`** — `GET user/{userId}/dialogs|groups|chats`, `GET user/{userId}/contact/{contactUserId}`, `GET {chatId}`, `GET {chatId}/members`, `POST {chatId}/members`, `DELETE {chatId}/members/{userId}`, `POST` (создание), `PUT {id}`, `DELETE {id}`, `POST {id}/avatar`. Проверка IsCurrentUser для списков.

**`Controllers/DepartmentController.cs`** — `GET` (все), `GET {id}`, `POST/PUT/DELETE` (Admin-only), `GET {id}/members`, `POST/DELETE {id}/members`, `GET {id}/can-manage`. CancellationToken на всех.

**`Controllers/FilesController.cs`** — `POST upload` с `[RequestSizeLimit(100MB)]`, query `chatId`. Проверка доступа к чату.

**`Controllers/MessagesController.cs`** — `POST`, `PUT {id}`, `DELETE {id}`, `GET chat/{chatId}` (пагинация), `GET chat/{chatId}/around|before|after/{messageId}`, `GET chat/{chatId}/search`, `GET user/{userId}/search` (глобальный), `GET {id}/transcription`, `POST {id}/transcription/retry`.

**`Controllers/NotificationController.cs`** — `GET chat/{chatId}/settings`, `POST chat/mute`, `GET settings`.

**`Controllers/PollController.cs`** — `GET {pollId}`, `POST` (создание → MessageDto), `POST vote`.

**`Controllers/ReadReceiptsController.cs`** — `POST mark-read`, `GET chat/{chatId}/unread-count`, `GET unread-counts`.

**`Controllers/UserController.cs`** — `GET` (все), `GET {id}`, `PUT {id}`, `POST {id}/avatar`, `PUT {id}/username`, `PUT {id}/password`, `GET online`, `GET {id}/status`, `POST status/batch`.

### Hubs — SignalR

**`Hubs/ChatHub.cs`** — `[Authorize]`. **OnConnectedAsync**: регистрация в OnlineUserService, join `user_{id}` + все `chat_{id}`, broadcast `UserOnline`. **OnDisconnectedAsync**: при полном оффлайне — `LastOnline` в БД, broadcast `UserOffline`. **JoinChat/LeaveChat**: с проверкой членства. **MarkAsRead/MarkMessageAsRead**: через ReadReceiptService → `UnreadCountUpdated` caller + `MessageRead` остальным. **SendTyping** → `UserTyping`. **GetOnlineUsersInChat**. `IServiceScopeFactory`.

### Mapping — Маппинг сущностей в DTO

**`Mapping/ChatMappings.cs`** — `Chat.ToDto()` → ChatDto. Перегрузка с `dialogPartner` для Contact-чатов. IUrlBuilder для URL.

**`Mapping/FileMappings.cs`** — `MessageFile.ToDto()` → MessageFileDto. `DeterminePreviewType` → "image"/"video"/"audio"/"file".

**`Mapping/MessageMappings.cs`** — `Message.ToDto()` → MessageDto. IsDeleted → "[Сообщение удалено]". ReplyTo → `ToReplyPreviewDto()`, Forward → `ToForwardInfoDto()`. IsOwn, IsVoiceMessage, TranscriptionStatus, файлы, опрос.

**`Mapping/PollMappings.cs`** — `Poll.ToDto()` — SelectedOptionIds текущего пользователя, CanVote. Анонимные опросы скрывают голоса.

**`Mapping/UrlHelpers.cs`** — `BuildFullUrl(this string?, IUrlBuilder?)` — делегация в urlBuilder.

**`Mapping/UserMappings.cs`** — `User.ToDto()` → UserDto. `FormatDisplayName()` — ФИО/Username. `UpdateProfile()`, `UpdateSettings()`.

### Middleware

**`Middleware/ExceptionHandlingMiddleware.cs`** — Глобальный перехват: Argument→400, Unauthorized→401, KeyNotFound→404, InvalidOp→400, прочие→500. Dev: Details для 500. Extension `UseExceptionHandling()`.

### Services — Бизнес-логика

#### Auth

**`Services/Auth/AuthService.cs`** — `LoginAsync`: timing-safe (dummy BCrypt.Verify). Проверка пароля, бана. `DetermineUserRoleAsync`: Admin/Head/User. JWT через TokenService.

**`Services/Auth/TokenService.cs`** — HMAC-SHA256. Секрет ≥32 символа. Claims: NameIdentifier/Jti/Iat/Role. `ValidateToken`, статический `CreateValidationParameters`.

#### Base

**`Services/Base/BaseService.cs`** — Абстрактный с DbContext + ILogger. `SaveChangesAsync`, `GetRequiredEntityAsync<T>`, `EnsureNotNull`, `Paginate`, `NormalizePagination`.

#### Chat

**`Services/Chat/ChatService.cs`** — **GetUserChatsAsync**: GroupJoin с последним сообщением, unread, партнёр для Contact, сортировка. **CreateChatAsync**: транзакция, проверка дубликата. **UpdateChatAsync**: Admin, запрет Contact, смена типа Owner. **DeleteChatAsync**: Owner, `ExecuteDeleteAsync`. **UploadChatAvatarAsync**: webp.

**`Services/Chat/ChatMemberService.cs`** — Add (Admin + дубликат), Remove (защита Owner), UpdateRole (Owner only), Leave. Инвалидация кеша.

**`Services/Chat/NotificationService.cs`** — `SendNotificationAsync`: NotificationDto → HubNotifier. CRUD NotificationsEnabled.

#### Department

**`Services/Department/DepartmentService.cs`** — CRUD: GroupBy подсчёт, защита от циклов (BFS), запрет удаления при дочерних/сотрудниках. Состав: Add (перемещение — Admin), Remove (запрет Head). CanManage — Admin или Head.

#### Infrastructure

**`Services/Infrastructure/AccessControlService.cs`** — Per-request кеш `Dictionary<(UserId,ChatId), ChatMember?>` + L2 через ICacheService. IsMember/IsOwner/IsAdmin, Ensure-методы throw UnauthorizedAccessException.

**`Services/Infrastructure/CacheService.cs`** — IMemoryCache. UserChats: TTL 5min + 2min sliding. Membership: TTL 10min + 3min sliding, кеширует null. Invalidation: UserChats, Membership (+UserChats), Chat, ChatMembers (natural expiry).

**`Services/Infrastructure/HttpUrlBuilder.cs`** — IUrlBuilder. Абсолютный → as-is. Относительный → `{scheme}://{host}/{path}` из HttpContext.

**`Services/Infrastructure/HubNotifier.cs`** — `SendToChatAsync` → Group("chat_{id}"). `SendToUserAsync` → Group("user_{id}"). Try-catch + LogWarning.

**`Services/Infrastructure/OnlineUserService.cs`** — Singleton. `ConcurrentDictionary<int, ConcurrentDictionary<string, byte>>`. Connect/Disconnect, IsOnline, GetOnlineUserIds, FilterOnline, OnlineCount.

#### Messaging

**`Services/Messaging/MessageService.cs`** — **Create**: валидация Reply/Forward, файлы (reverse URL→path), LastMessageTime, broadcast, notify+unread, voice→TranscriptionQueue. **Get**: пагинация Reverse, Around/Before/After по ID. **Update**: проверка владельца, запрет poll/voice/forward/deleted. **Delete**: soft-delete. **Search**: ILike с escape. **GlobalSearch**: чаты (Contact партнёр + Group name, до 5) + сообщения с highlight (±40 chars).

**`Services/Messaging/FileService.cs`** — **SaveImageAsync**: MIME валидация, ресайз ImageSharp, WebP. **SaveMessageFileAsync**: размер, `uploads/chats/{chatId}/{guid}{ext}`. **DeleteFile**: safe. **IsValidImage**.

**`Services/Messaging/PollService.cs`** — **Get**: Include Options→Votes. **Create**: транзакция Message+Poll+Options, broadcast. **Vote**: удаление старых + создание новых, broadcast `ReceivePollUpdate`.

**`Services/Messaging/TranscriptionQueue.cs`** — `Channel<int>` Unbounded/SingleReader. **TranscriptionBackgroundService**: BackgroundService, scope→TranscribeAsync.

**`Services/Messaging/TranscriptionService.cs`** — Whisper CLI, модель ggml-small-q5_1.bin, SemaphoreSlim(1,1). PCM 16kHz/16bit/mono через NAudio. Timeout 5min. Broadcast TranscriptionCompleted/StatusChanged. IDisposable.

#### ReadReceipt

**`Services/ReadReceipt/ReadReceiptService.cs`** — **MarkAsRead**: target messageId (конкретный или последний), обновление LastReadMessageId (только если >). **GetChatReadInfoAsync**: GroupBy → Count + FirstUnreadId. **GetAllUnreadCounts**: subquery. **GetUnreadCountsForChats**: батч.

#### User

**`Services/User/UserService.cs`** — GetAll/Get с online. UpdateProfile. UploadAvatar. GetOnlineStatus(es). **ChangeUsername**: regex + уникальность. **ChangePassword**: BCrypt verify→hash.

**`Services/User/AdminService.cs`** — GetUsers. **CreateUser**: validation, lowercase, уникальность, BCrypt, UserSetting. **ToggleBan**: toggle.

### Model — Сущности БД

**`Model/Chat.cs`** — Id, Name, CreatedAt, CreatedById, LastMessageTime, Avatar. Partial: `Type` (ChatType).

**`Model/ChatMember.cs`** — PK: ChatId+UserId. JoinedAt, NotificationsEnabled, LastReadMessageId, LastReadAt. Partial: `Role` (ChatRole).

**`Model/Department.cs`** — Id, Name, ParentDepartmentId, ChatId, HeadId. 1:1 Chat, Head, Parent tree, Users.

**`Model/Message.cs`** — Id, ChatId, SenderId, Content, CreatedAt, EditedAt, IsDeleted, ReplyTo, ForwardedFrom, IsVoiceMessage, TranscriptionStatus. Навигации: Files, Polls, Reply/Forward inverse.

**`Model/MessageFile.cs`** — Id, FileName, ContentType, MessageId, Path.

**`Model/MessageStatus.cs`** — Id, MessageId, UserId, Status, UpdatedAt (legacy, не используется).

**`Model/Poll.cs`** — Id, MessageId, IsAnonymous, AllowsMultipleAnswers, ClosesAt.

**`Model/PollOption.cs`** — Id, PollId, OptionText (max 50), Position.

**`Model/PollVote.cs`** — Id, PollId, OptionId, UserId, VotedAt. Unique: (Poll,User,Option).

**`Model/SystemSetting.cs`** — Key-Value store. PK: Key (max 50).

**`Model/User.cs`** — Id, Username (unique, max 32), Name/Surname/Midname, PasswordHash, CreatedAt, LastOnline, DepartmentId, Avatar, IsBanned. `[NotMapped] DisplayName`.

**`Model/UserSetting.cs`** — PK: UserId (1:1). NotificationsEnabled. Partial: `Theme` (Theme?).

**`Model/MessengerDbContext.cs`** — PostgreSQL enums, snake_case таблицы, sequences. Индексы: messages(chatId,createdAt), reply, forward, chatMembers(lastRead, userId), departments(headId), pollVotes(userId). Unique: Chat_User, Poll_User_Option, username. Каскады: Chat→CreatedBy(Cascade), остальные SetNull.

---

## MessengerDesktop — Клиентская часть (Avalonia, MVVM)

### Точка входа и каркас

**`Program.cs`** — `[STAThread]`. `AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace()`.

**`App.axaml`** — RequestedThemeVariant="Dark". Resources: `ConverterLocator` (x:Key="Converters"), MergedDictionaries: `Icons.axaml`. ThemeDictionaries: Dark→`DarkTheme.axaml`, Light→`LightTheme.axaml`. Styles: `FluentTheme`, `Animations.axaml`, `MainStyle.axaml`.

**`App.axaml.cs`** — `IDisposable`. ApiUrl (debug: https://localhost:7190, release: http://localhost:5274). ConfigureServices → DI. OnFrameworkInitializationCompleted: MainWindow, PlatformService, NotificationService, AuthenticatedImageLoader, LocalDatabase+CacheMaintenanceService (фоновая), ThemeService, DataContext=MainWindowVM. Dispose: последовательная очистка всех сервисов.

**`ViewLocator.cs`** — `IDataTemplate`. Конвенция: "ViewModel"→"View". Match: `BaseViewModel`.

### Controls

**`Controls/Shared/AvatarControl.axaml.cs`** — StyledProperties: Size, FontSize, IconSize, Source, DisplayName, IsOnline, ShowOnlineIndicator, FallbackIcon, PlaceholderBg/Fg, IsCircular. Computed: ImageSource (валидация расширения — защита от non-image URL), HasImage, Initials (ФИ/Ии/?), ShowInitials/ShowIcon. Adaptive OnlineIndicator (8–16px). CornerRadius: circular=Size/2, else=8.

**`Controls/ChatInfoSkeleton.axaml`** — Skeleton-заглушка для панели информации о чате. ScrollViewer: аватар (100×100 круг), имя, статус, два поля, toggle, 3 участника (аватар 40×40 + имя + статус). Классы `Skeleton`, разделители `DynamicResource BorderDefault`.

**`Controls/ChatItemView.axaml`** — DataType=`ChatDto`. Grid(Auto,\*) высотой 52px. AvatarControl (Medium, GroupIcon fallback). ChatName, ChatPreview (Run: Sender + Content) с fallback "Нет сообщений".

**`Controls/ChatListSkeleton.axaml`** — Skeleton-заглушка для списка чатов. 6 элементов (Height=68): аватар 44×44 + имя + превью + время. Варьирующиеся ширины.

**`Controls/ChatMessagesSkeleton.axaml`** — Skeleton-заглушка для области сообщений. 6 блоков в формате чата — аватары 36×36, пузыри с BoxShadow, 1–3 строки разной ширины, промежуточные «продолжения».

**`Controls/RichMessageTextBlock.cs`** — Наследует `SelectableTextBlock`. StyledProperty `RawText`. URL Regex `(https?://[^\s<>"')\]]+)` (Compiled, 1s timeout). RebuildInlines: Run обычный + Run с LinkBrush=#4A9EEA + Underline. Hit-testing через `_linkRanges` + TextLayout.HitTestPoint (рефлексия + fallback GetCharIndexByPosition). Клик → `Process.Start(UseShellExecute)`, Hand cursor на ховере.

**`Controls/Shared/CircularProgress.cs`** — Custom `Control`. StyledProperties: Value, Maximum(100), Minimum, StrokeWidth(4), Size(32), Foreground(DodgerBlue), BackgroundTrack, IsIndeterminate. Анимация: DispatcherTimer.Run 16ms (~60fps), `_animationAngle += 6` mod 360. Render: StreamGeometry arc — background track (ellipse) + foreground arc. `DrawArc` с PenLineCap.Round, isLargeArc при >180°.

**`Controls/Shared/ThemeSelectorControl.axaml`** — 3-колоночный Grid. Resources: LightPreview/DarkPreview ControlTemplate — мини-превью мессенджера. RadioButton.ThemeOption с custom template, `:checked`→Accent. System: preview по IsSystemDarkNow + badge "AUTO".

**`Controls/Shared/ThemeSelectorControl.axaml.cs`** — StyledProperties: SelectedTheme (AppTheme enum), IsLightSelected, IsDarkSelected, IsSystemSelected. DirectProperty: IsSystemDarkNow. Двухсторонняя синхронизация enum↔bool. Подписка ActualThemeVariantChanged.

### Converters

**`Converters/ConverterExtension.cs`** — Два MarkupExtension: Converter и MultiConverter — резолв по строковому имени через ConverterLocator.

**`Converters/ConverterLocator.cs`** — Singleton-реестр ~35 конвертеров (case-insensitive). Категории: Bool, Comparison, DateTime, Domain, Enum, Level, Message, Generic. Multi: BooleanAnd/Or, LastSeen, HasTextOrAttachments, PercentToWidth.

**`Converters/Base/ConverterBase.cs`** — ConverterBase\<TIn,TOut\> — типизированный IValueConverter. AllowNull, DefaultValue, SupportsConvertBack. ConverterBase (нетипизированный) — упрощённый.

**`Converters/Boolean/`** — BoolToValueConverter\<T\> (абстрактный), BoolToStringConverter (Split по `|`), BoolToDoubleConverter, BoolToColorConverter, BoolToHAlignmentConverter, BooleanAndConverter (IMultiValueConverter AND), BooleanOrConverter (OR), BoolToBrushConverter (TryFindResource), BoolToThicknessConverter (Thickness.Parse), EnumEqualsConverter (OrdinalIgnoreCase).

**`Converters/Comparison/ComparisonConverter.cs`** — Enum ComparisonMode: Equal, NotEqual, GreaterThanZero, Zero.

**`Converters/DateTime/DateTimeFormatConverter.cs`** — Enum DateTimeFormat (7 режимов). FormatChatTime: сегодня→HH:mm, вчера→"Вчера", год→"d MMMM"/"d MMMM yyyy". FormatRelativeTime: "только что"/мин/ч/д/dd.MM.yy.

**`Converters/DateTime/LastMessageDateConverter.cs`** — Делегация в DateTimeFormatConverter(Chat).

**`Converters/DateTime/LastSeenTextConverter.cs`** — IMultiValueConverter. IsOnline→"в сети", else→"был(а)...".

**`Converters/Domain/`** — DisplayOrUsernameConverter, InitialsConverter (?/1/2 буквы), PollToPollViewModelConverter (Service Locator), ThemeToDisplayConverter.

**`Converters/Enum/UserRoleToVisibilityConverter.cs`** — Резолв IAuthManager, сравнение HasRole.

**`Converters/Generic/`** — IndexToTextConverter (Split по Separator), PercentToWidthConverter (IMultiValueConverter, Clamp, MinVisibleWidth=8), PluralizeConverter (русская плюрализация: mod10).

**`Converters/Hierarchy/LevelConverters.cs`** — LevelToMarginConverter (indent=20), LevelToVisibilityConverter (>0).

**`Converters/Message/MessageConverters.cs`** — MessageAlignmentConverter (Own→Right), MessageMarginConverter (60/10 margins), HasContentConverter, HasTextOrAttachmentsMultiConverter.

### Data — Локальный кеш (SQLite)

**`Data/LocalDatabase.cs`** — sqlite-net-pcl. PRAGMA: WAL, synchronous=NORMAL, cache_size=-8000, temp_store=MEMORY, mmap_size=256MB. Schema migration (user_version), downgrade→drop. FTS5: messages_fts + триггеры. ClearAll, Vacuum, GetDatabaseSizeBytes.

**`Data/Entities/`** — CachedChat (chats), CachedMessage (messages, indexed ChatId/SenderId, flatten all), CachedReadPointer (read_pointers), CachedUser (users), ChatSyncState (chat_sync_state, OldestLoadedId/NewestLoadedId/HasMore).

**`Data/Mappers/CacheMapper.cs`** — Статический. MessageDto↔CachedMessage (flatten/unflatten, JSON poll/files). ChatDto↔CachedChat (Type int↔enum). UserDto↔CachedUser.

**`Data/Repositories/`** — IChatCacheRepository/ChatCacheRepository (Upsert, GetByType, batch transactions). IMessageCacheRepository/MessageCacheRepository (GetLatest DESC→Reverse, Before/After/Around, FTS5 MATCH + fallback LIKE, MarkDeleted). ILocalCacheService/LocalCacheService (фасад: координация repos, SyncState-aware IsComplete, CachedMessagesResult).

### Infrastructure

**`Infrastructure/AuthenticatedImageLoader.cs`** — Наследует RamCachedWebImageLoader. JWT Bearer для apiBaseUrl, проверка расширения + Content-Type, fallback для внешних URL.

**`Infrastructure/ServiceCollectionExtensions.cs`** — AddMessengerCoreServices: LocalDatabase, repos, cache, platform, settings, hub, notification — singleton. HttpClient 30s. Auth chain. Navigation, Dialog — singleton. AddMessengerViewModels: MainWindowVM singleton, Factories singleton, VMs transient.

**`Infrastructure/Configuration/ApiEndPoints.cs`** — Статические классы по контроллерам с параметризованными URL-методами.

**`Infrastructure/Configuration/AppConstants.cs`** — MaxFileSize=20MB, Debounce=300ms, PageSize=50, LoadMore=30, Search=20, Highlight=3000ms, MarkAsRead debounce=300ms/cooldown=1s, Typing send=1200ms/indicator=3500ms.

**`Infrastructure/Helpers/AvatarHelper.cs`** — GetSafeUri (avares:// default), GetUriWithCacheBuster (?v=Ticks). MimeTypeHelper.GetMimeType.

### Services — Сервисный слой клиента

**`Services/Api/ApiClientService.cs`** — IApiClientService: Get/Post/Put/Delete + UploadFile + GetStream. HttpClient + ISessionStore, SessionChanged→UpdateAuthorizationHeader. ProcessResponseAsync\<T\>: fallback direct deserialize. GetStreamAsync: >10MB→temp file (DeleteOnClose), ≤10MB→MemoryStream.

**`Services/Audio/`** — AudioRecordingState (Idle/Recording/Sending/Error). IAudioRecorderService (Start/Stop/Cancel→AudioRecordingResult IDisposable). NAudioRecorderService: WaveInEvent MME, 16kHz/16bit/mono, IgnoreDisposeStream wrapper, Stopwatch, thread-safe lock. TranscriptionPoller: ConcurrentDictionary, exponential backoff [1,2,4,8,16]s, max 60, IDisposable.

**`Services/Auth/AuthService.cs`** — Login→PostAsJsonAsync, двойная десериализация. Validate→GET Bearer. Logout→POST.

**`Services/Auth/AuthManager.cs`** — InitializeInternalAsync (constructor). LoadStoredSession→SecureStorage→ValidateToken→SetSession. LoginAsync: SemaphoreSlim, cache clear on user change, SaveAuth. LogoutAsync: clear all. TaskCompletionSource для инициализации.

**`Services/Auth/SecureStorage.cs`** — AES-256 + PBKDF2 (100K, SHA256). Machine-bound salt (.salt файл). AppData/SecureStorage/{base64key}.secure. Random IV prepend. SemaphoreSlim, auto-remove corrupted.

**`Services/Auth/SessionStore.cs`** — ObservableObject. RoleHierarchy dict (User=0, Head=1, Admin=2). HasRole→hierarchy comparison (≥). SetSession validation, SessionChanged event.

**`Services/Cache/CacheMaintenanceService.cs`** — RunMaintenance (size+timing), ClearAllData (cache+vacuum), ClearChatCache (reset SyncState).

**`Services/Navigation/NavigationService.cs`** — Stack\<Type\> history. NavigateTo→Push. GoBack→Pop. NavigateToMainMenu→auth check.

**`Services/Navigation/DialogService.cs`** — List\<DialogBaseViewModel\> stack. Channel\<CloseRequest\> (Bounded=10, SingleReader) + background processing. ShowAsync: SemaphoreSlim, animation(open). RequestAnimationAsync: TaskCompletionSource + 1s timeout.

**`Services/Platform/PlatformService.cs`** — MainWindow (explicit/ApplicationLifetime fallback). Clipboard (TopLevel). Copy/Get/Clear с try-catch.

**`Services/Realtime/ChatHubConnection.cs`** — Per-chat SignalR. Handlers: ReceiveMessageDto, MessageUpdated/Deleted, MessageRead, UnreadCountUpdated, UserTyping, MemberJoined/Left. MarkMessageAsReadAsync: debounce. SendTypingAsync: debounce. Reconnected→rejoin.

**`Services/Realtime/GlobalHubConnection.cs`** — App-wide SignalR. Dictionary\<int,int\> unreadCounts + totalUnread с lock. Handlers: ReceiveNotification (filter+ShowWindow 5s), ReceiveMessageDto (CacheIncoming+IncrementUnread), MessageUpdated/Deleted, UserOnline/Offline, UserProfileUpdated, UnreadCountUpdated. LoadUnreadCountsAsync, MarkChatAsReadAsync, ReconcileAfterReconnectAsync.

**`Services/Storage/SettingsService.cs`** — JSON файл settings.json. Dictionary\<string, JsonElement\>. Generic Get\<T\>/Set\<T\>.

**`Services/UI/NotificationService.cs`** — WindowNotificationManager (TopRight, MaxItems=3). ShowWindow, ShowBothAsync, ShowCopyableErrorAsync.

**`Services/ChatInfoPanelStateStore.cs`** — Делегация в SettingsService по ключу "ChatInfoPanelIsOpen".

**`Services/ChatNotificationApiService.cs`** — GetChatSettings, SetChatMute, GetAllSettings через ApiEndpoints.Notification.

**`Services/IFileDownloadService.cs`** — DownloadFileAsync (80KB buffer, progress 1%), GetDownloadsFolder (OS-specific), OpenFileAsync (UseShellExecute), OpenFolderAsync (explorer/open/xdg-open).

**`Services/ThemeService.cs`** — Toggle Dark↔Light + SaveTheme. LoadFromSettings default dark.

### ViewModels

**`ViewModels/BaseViewModel.cs`** — ObservableObject + IDisposable. IsBusy, ErrorMessage, SuccessMessage с virtual hook-методами. GetCancellationToken(), SafeExecuteAsync (2 перегрузки). GetAbsoluteUrl. ClearMessagesCommand.

**`ViewModels/IRefreshable.cs`** — `IAsyncRelayCommand RefreshCommand`.

#### Shell

**`ViewModels/Shell/MainWindowViewModel.cs`** — Deps: INavigationService, IDialogService, IAuthManager, IThemeService. CurrentViewModel, CurrentDialog, HasOpenDialogs, IsDialogVisible. Logout→CloseAllDialogs→LogoutAsync→Login. ShowDialogAsync\<T\>. ToggleTheme, RefreshCurrentView (IRefreshable).

**`ViewModels/Shell/MainMenuViewModel.cs`** — Lazy VMs для каждой вкладки. Navigation: Stack\<int\> back/forward. NavigateToMenu(0–6). Search с CTS. SwitchToTabAndOpenChatAsync/MessageAsync. ShowUserProfileAsync, ShowPollDialogAsync, ShowCreateGroupDialogAsync, ShowEditGroupDialogAsync. InitializeGlobalHubAsync.

#### Auth

**`ViewModels/Auth/LoginViewModel.cs`** — IsInitializing(true). InitializeAsync: WaitForInitialization (15s timeout), auto-login if RememberMe. LoadSavedCredentials/SaveCredentials через SecureStorage. LoginAsync [RelayCommand, CanExecute=!IsBusy&&!IsInitializing]: validate→LoginAsync→SaveCredentials→Navigate. ClearCredentialsAsync.

#### Chats

**`ViewModels/Chats/ChatsViewModel.cs`** — IRefreshable. IsGroupMode, IsInitialLoading. **Stale-while-revalidate**: Phase1 ShowCachedChatsAsync (мгновенный показ + unread из GlobalHub), Phase2 LoadFreshChatsFromServerAsync (сервер + кеширование). Smart update: сохранение selectedId. Unread: подписка GlobalHub. OnSelectedChatChanged: SyncSearchScope, MarkAsRead, create ChatViewModel. OpenOrCreateDialogWithUserAsync. CreateGroup→Parent.ShowCreateGroupDialogAsync. OpenChatByIdAsync (find/GET/insert, optional scroll). Search integration: GlobalSearchManager, OpenSearchedChat/Result.

**`ViewModels/Chats/ChatListItemViewModel.cs`** — ObservableObject. Constructor from ChatDto. Read-only: Id, Type, CreatedById. Observable: Name, LastMessageDate, Avatar, Preview, SenderName, UnreadCount. ToDto(), Apply(ChatDto).

**`ViewModels/Chats/GlobalSearchManager.cs`** — ObservableObject + IDisposable. Debounce search. Dual mode: global (chats+messages via GlobalSearchResponseDto) и chat-local (per-chat via SearchMessagesResponseDto→adapt). Collections: ChatResults, MessageResults. LoadMoreMessagesAsync pagination. EnterSearchMode/ExitSearch/Clear.

#### Chat — Экран чата

**`ViewModels/Chat/ChatScreen/ChatViewModel.cs`** — Partial-класс (8 файлов). BaseViewModel + IAsyncDisposable. Managers: ChatMessageManager, ChatAttachmentManager, ChatMemberLoader, ChatHubConnection. Events: ScrollToMessage/Index/BottomRequested. ~20 ObservableProperties. ~15 Computed. Constructor: SetCurrentChat, init managers, placeholder Chat, fire-and-forget InitializeChatAsync. WaitForInitializationAsync (TaskCompletionSource). PopularEmojis (32).

**`ChatViewModel.Lifecycle.cs`** — InitializeChatAsync: LoadChat→LoadMembers→InitHub→GetReadInfo→LoadInitialMessages→LoadNotifications→UpdatePolls→InitVoice→SubscribeInfoPanel→scroll. DisposeAsync: unsubscribe all, cancel CTS, dispose hub/voice/attachments.

**`ChatViewModel.Messages.cs`** — OnMessageReceived→AddReceivedMessage→TranscriptionPolling→HasNewMessages/MarkAsRead. OnMessageUpdated/Deleted→delegate. OnMessageRead→mark IsRead. OnMessageVisibleAsync→MarkLocally+hub. SendMessage: forward content/files inheritance, upload→POST→clear. LoadOlder/Newer commands.

**`ChatViewModel.Commands.cs`** — RemoveAttachment, InsertEmoji, AttachFile, ToggleInfoPanel, LeaveChat, OpenCreatePoll, OpenEditChat (update ChatsList + ReloadMembers), OpenProfile, ToggleChatNotifications.

**`ChatViewModel.Edit.cs`** — StartEditMessage, SaveEditMessage (unchanged→cancel, empty→delete, else PUT), CancelEditMessage, DeleteMessage (soft), CopyMessageText (clipboard).

**`ChatViewModel.Reply.cs`** — StartReply (cancel edit+forward), CancelReply, ScrollToReplyOriginal (find/LoadAround→HighlightAndScroll, 2s reset).

**`ChatViewModel.Forward.cs`** — ForwardingMessage, ForwardPreviewText (switch: deleted/voice/poll/files/text/fallback). StartForward (cancel edit+reply), CancelForward.

**`ChatViewModel.Search.cs`** — ScrollToMessageAsync: find→scroll+highlight, else LoadAround→delay→scroll+highlight. HighlightMessage: clear all→set→auto-reset (HighlightDurationMs). GoToSearchResult→exit search→scroll.

**`ChatViewModel.Typing.cs`** — Dictionary\<int,DateTime\> _typingUsers. TypingText: 0→"", 1→"{name} печатает...", >1→"Несколько человек печатают...". OnNewMessage→SendTypingAsync. Cleanup loop: 500ms interval, expire по TypingIndicatorDurationMs.

**`ChatViewModel.InfoPanel.cs`** — Subscribe/Unsubscribe 4 event sources. OnUserStatusChanged: contact IsOnline+LastSeen, members replace-at-index. OnUserProfileUpdated: contact+Chat.Name/Avatar, members replace. OnMemberJoined/Left: dedup add/remove. ReloadMembersAfterEditAsync, RefreshInfoPanelDataAsync, InvalidateAllInfoPanelProperties (10 notifications).

**`ChatViewModel.Voice.cs`** — IAudioRecorderService + TranscriptionPoller. Min=0.5s, Max=300s. StartVoiceRecording→StartTimer→AutoStopAfterLimit. StopAndSendVoice→validate→SendVoiceMessageAsync (upload+POST IsVoiceMessage). CancelVoiceRecording. StartTranscriptionPollingIfNeeded, RetryTranscription. DisposeVoiceAsync.

#### Chat — Менеджеры

**`Managers/ChatMessageManager.cs`** — Cache-first с revalidation. LoadInitial: unread→LoadAround, else cache→RenderMessages + background RevalidateNewest, fallback server. LoadOlder/Newer: cache-first + server дозагрузка + dedup. GapFillAfterReconnect: recursive After-endpoint. AddReceivedMessage: dedup→CreateVM→bounds/dates/grouping→background cache. HandleDeleted/Updated→cache sync. DateSeparators (Сегодня/Вчера/d MMMM). Grouping→delegate to MessageViewModel statics.

**`Managers/ChatMemberLoader.cs`** — GET Chat.Members, fallback Contact→parse Name as userId→GET individual users.

**`Managers/ChatAttachmentManager.cs`** — IDisposable. PickAndAddFilesAsync (FilePickerOpenOptions AllowMultiple). AddFileAsync: size check, MIME, MemoryStream, TryCreateThumbnail. UploadAllAsync→List\<MessageFileDto\>. Safe dispose in finally.

**`Managers/LocalFileAttachment.cs`** — IDisposable. FileName, ContentType, FilePath, Data (MemoryStream), Thumbnail (Bitmap?). FileSizeFormatted (B/KB/MB).

#### Chat — Сообщения

**`Messages/MessageViewModel.cs`** — ObservableObject. ~25 computed properties. Constructor: map DTO, Reply/Forward, CreatePollViewModel (Service Locator), FileViewModels. UpdateTranscription, UpdatePoll, ApplyUpdate, MarkAsDeleted/Read. 14 partial property handlers. UpdateGroupPosition: (IsContinuation, HasNextFromSame)→Alone/First/Middle/Last. **Statics**: GroupingThreshold=2min, CanGroup (sender+deleted+date+threshold), RecalculateGrouping, UpdateGroupingAround.

**`Messages/MessageFileViewModel.cs`** — ObservableObject. DownloadState enum (5 states). FileIcon (emoji by MIME). DownloadAsync: CTS с lock, Progress→UIThread, downloadService. CancelDownload: lock+guard. OpenFileAsync: download-if-needed→open. OpenInFolderAsync. RetryDownload. FormatFileSize (B/KB/MB/GB).

**`Messages/MessageGroupPosition.cs`** — Enum: Alone, First, Middle, Last.

#### Chat — Опросы

**`Polls/PollViewModel.cs`** — BaseViewModel. Options, AllowsMultipleAnswers, CanVote, TotalVotes, HasVoted. Single-selection enforcement. ApplyDto: UpdateOptions (match/add/remove), ApplySelectedOptions. Vote/CancelVote [RelayCommand]: POST PollVoteDto→ApplyDto.

**`Polls/PollOptionViewModel.cs`** — ObservableObject. Parent reference (PollViewModel). IsSelected, VotesCount, VotesPercentage. UpdateVotes: recalculate percentage. ToggleSelection.

**`VoiceRecordingViewModel.cs`** — ObservableObject + IDisposable. State (AudioRecordingState). DispatcherTimer 200ms→format m:ss.

#### Profile & Settings

**`ViewModels/ProfileViewModel.cs`** — IRefreshable. User (UserDto), editing states (Profile/Username/Password), temp fields, validation. LoadUser, SaveProfile, SaveUsername, SavePassword, UploadAvatar, Logout.

**`ViewModels/SettingsViewModel.cs`** — SelectedTheme, NotificationsEnabled. Timer 800ms debounce auto-save. LoadSettings, SaveSettings, ApplyTheme, ClearCache.

#### Admin

**`ViewModels/Admin/AdminViewModel.cs`** — IRefreshable. UsersTab + DepartmentsTab. Parallel init + cross-reference. SearchQuery propagation. Error/Success message bubbling.

**`ViewModels/Admin/UsersTabViewModel.cs`** — GroupBy DepartmentId→DepartmentGroup. Create/Edit (UserEditDialogVM, TaskCompletionSource), ToggleBan (ConfirmDialog). ApplyFilter LINQ.

**`ViewModels/Admin/DepartmentsTabViewModel.cs`** — BuildHierarchy recursive. Create/Edit/Delete (DepartmentHeadDialogVM, ConfirmDialog). ApplyFilter recursive (child propagation). ExpandAll/CollapseAll.

**`ViewModels/Admin/DepartmentGroup.cs`** — departmentName, departmentId?, ObservableCollection\<UserDto\>. Computed: counts, summary.

**`ViewModels/Admin/HierarchicalDepartmentViewModel.cs`** — ObservableObject. Level, IsExpanded(true)→ExpanderRotation. Children. UserCountText (русское склонение).

#### Department

**`ViewModels/Department/DepartmentManagementViewModel.cs`** — LoadAsync: user department→CanManage→members (exclude self, sort online→name)→available users. AddMember/RemoveMember via action delegates. SearchQuery→FilteredMembers.

**`ViewModels/Department/DepartmentMemberViewModel.cs`** — UserId, DisplayName, AvatarUrl, IsOnline, LastSeen. FormatLastSeen.

**`ViewModels/Department/SelectUserDialogViewModel.cs`** — TaskCompletionSource\<UserDto?\>. FilteredUsers (Contains query). Confirm→TrySetResult.

#### Dialogs

**`ViewModels/Dialog/DialogViewModelBase.cs`** — DialogBaseViewModel : BaseViewModel. CloseRequested event. InitializeAsync, Cancel, CloseOnBackgroundClick.

**`ViewModels/Dialog/ChatEditDialogViewModel.cs`** — Create/Edit group. SelectableUserItem (IsSelected). Avatar picker (5MB limit). SaveAction delegate.

**`ViewModels/Dialog/ConfirmDialogViewModel.cs`** — TaskCompletionSource\<bool\>. Message, ConfirmText, CancelText.

**`ViewModels/Dialog/DepartmentDialogViewModel.cs`** — BFS GetDescendantIds для предотвращения циклов. SaveAction delegate.

**`ViewModels/Dialog/DepartmentHeadDialogViewModel.cs`** — Head management + Confirm dialogs. CanDelete guard (hasChildren, userCount).

**`ViewModels/Dialog/PollDialogViewModel.cs`** — MinOptions=2, MaxOptions=10. OptionItem (ObservableObject). CreatePollDto→CreateAction.

**`ViewModels/Dialog/UserEditDialogViewModel.cs`** — Create/Edit user. Validation chain. CreateAction/UpdateAction.

**`ViewModels/Dialog/UserProfileDialogViewModel.cs`** — Avatar loading (GetStreamAsync→Bitmap). SendMessage→OpenChatWithUserAction.

#### Factories

**`ViewModels/Factories/ChatsViewModelFactory.cs`** — IChatsViewModelFactory: Create(parent, isGroupMode) с DI-зависимостями.

**`ViewModels/Factories/ChatViewModelFactory.cs`** — IChatViewModelFactory: Create(chatId, parent) с 11 зависимостями.

### Views — UI-представления

**`Views/Shell/MainWindow.axaml`** — ExtendClientAreaToDecorationsHint, AcrylicBlur, MinWidth=700. Styles: DialogOverlay (opacity transition), DialogAnimWrapper (scale+translate), TitleBarHelp. Layout: 3-column Grid, TitleBar drag zones. ContentControl с 8 DataTemplates. Dialog system: overlay ZIndex=10000 + wrapper ZIndex=10001, 8 dialog DataTemplates.

**`Views/Shell/MainWindow.axaml.cs`** — Анимация диалогов (Open/Closing CSS-классы, 250ms). TitleBar drag. Maximized padding +7px.

**`Views/Shell/MainMenu.axaml(.cs)`** — Простой UserControl.

**`Views/Auth/LoginView.axaml(.cs)`** — Enter в PasswordBox → LoginCommand.

**`Views/Chat/ChatsView.axaml(.cs)`** — CompactMode (72px) с гистерезисом. GridSplitter. InfoPanel hide <820px.

**`Views/Chat/ChatView.axaml(.cs)`** — FindScrollViewer (retry), DoScrollToEnd (10 retries), LoadOlderWithPreserve (offset compensation). Visibility tracking: debounced 300ms → OnMessageVisibleAsync.

**`Views/Chat/ChatInfoPanel.axaml(.cs)`**, **`MessageControl.axaml(.cs)`** (7 StyledProperty ICommand), **`FileAttachmentControl`**, **`PollView`**, **`ChatEditDialogView`** (Loaded→Initialize) — UI-представления чата.

**`Views/ProfileView`**, **`SettingsView`**, **`AdminView`**, **`DepartmentManagementView`** — простые UserControl/AvaloniaXamlLoader.

**Dialogs**: DepartmentDialog, DepartmentHeadDialog, PollDialog (RemoveOption via Tag), SelectUserDialog (PointerPressed→SelectUser), UserEditDialog, UserProfileDialog (Loaded→Initialize), ConfirmDialog.

---

## MessengerShared — Общие модели

### DTO

**Auth** — `LoginRequest` record(Username, Password). `AuthResponseDto` (Id, Username, DisplayName, Token, Role).

**Chat** — `ChatDto` (Id, Name, Type, CreatedById, LastMessageDate, Avatar, Preview, SenderName, UnreadCount). `ChatMemberDto`, `ChatNotificationSettingsDto`, `UpdateChatDto`, `UpdateChatMemberDto`.

**Department** — `DepartmentDto` (Id, Name, ParentDepartmentId, Head, HeadName, UserCount). `UpdateDepartmentMemberDto`.

**Message** — `MessageDto` (полное сообщение с ReplyTo, Forward, Poll, Files, IsVoiceMessage, TranscriptionStatus; computed ShowSenderName). `MessageFileDto`, `MessageForwardInfoDto`, `MessageReplyPreviewDto`, `PagedMessagesDto`, `UpdateMessageDto`, `VoiceTranscriptionDto`.

**Notification** — `NotificationDto` (Type, ChatId/Name/Avatar, MessageId, Sender, Preview, CreatedAt).

**Online** — `OnlineStatusDto` record(UserId, IsOnline, LastOnline). `OnlineUsersResponseDto`.

**Poll** — `CreatePollDto`, `PollDto` (Options, SelectedOptionIds, CanVote), `PollOptionDto`, `PollVoteDto`.

**ReadReceipt** — `MarkAsReadDto`, `ReadReceiptResponseDto`, `UnreadCountDto`, `AllUnreadCountsDto`, `ChatReadInfoDto`.

**Search** — `GlobalSearchMessageDto` (message + chat info + HighlightedContent). `GlobalSearchResponseDto`, `SearchMessagesResponseDto`.

**User** — `UserDto` (Id, Username, DisplayName, ФИО, Department, Avatar, IsOnline, IsBanned, LastOnline, Theme?, Notifications?). `CreateUserDto`, `ChangePasswordDto`, `ChangeUsernameDto`, `AvatarResponseDto`.

### Enum

`ChatRole` (Member, Admin, Owner [PgName]). `ChatType` (Chat, Department, Contact, DepartmentHeads). `Theme` (light, dark, system [JsonStringEnumConverter]). `UserRoles` (User, Head, Admin).

### Response

`ApiResponse<T>` (Success, Data, Message, Error, Details, Timestamp). `ApiResponseHelper` (static Ok/Fail).

---

## Архитектурные паттерны

**Структурные**
- MVVM — Avalonia, конвенционный ViewLocator
- Partial ViewModel decomposition — ChatViewModel разделён на 8 файлов по ответственностям
- Manager delegation — ChatViewModel делегирует в ChatMessageManager, ChatAttachmentManager, ChatMemberLoader
- Factory — ViewModel с DI (ChatsViewModelFactory, ChatViewModelFactory)
- Repository + Facade — SQLite кеш с ILocalCacheService
- Service Locator (Converters) — ConverterLocator singleton + MarkupExtension

**Коммуникация**
- SignalR Hub — real-time (сообщения, typing, online, read receipts)
- Dual Hub Pattern — ChatHubConnection (per-chat) + GlobalHubConnection (app-wide)
- Channel\<T\> + BackgroundService — очередь транскрибации (server) + dialog close queue (client)

**Данные**
- Result Pattern — сервисный слой возвращает Result\<T\>
- Soft Delete — сообщения
- Cursor-based navigation — Around/Before/After по ID
- Stale-while-revalidate — ChatsViewModel: мгновенный показ кэша + фоновая загрузка
- Cache-first message loading — ChatMessageManager: cache→render→background revalidate
- Gap fill after reconnect — recursive After-endpoint загрузка пропущенных
- L1+L2 cache — per-request Dict + IMemoryCache (access control)
- FTS5 — полнотекстовый поиск SQLite + триггеры + fallback LIKE
- WAL + PRAGMA — production SQLite
- Schema migration — user_version + downgrade detection

**Безопасность**
- Timing-safe auth — dummy BCrypt.Verify
- AES-256 + PBKDF2 — SecureStorage, machine-bound key derivation
- Role Hierarchy — SessionStore с числовой иерархией (User < Head < Admin)
- Credential persistence — RememberMe + SecureStorage + auto-login

**UI/UX**
- Adaptive UI — CompactMode с гистерезисом, responsive InfoPanel
- Scroll preservation — offset compensation при подгрузке истории
- Visibility tracking — debounced для read receipts
- Animation coordination — TaskCompletionSource + timeout для dialog open/close
- Message grouping — 2-минутный порог, Alone/First/Middle/Last для bubble radius
- Typing indicator cleanup loop — background 500ms interval, expire threshold

**Асинхронные паттерны**
- Fire-and-forget initialization — конструктор → InitializeChatAsync с TaskCompletionSource
- Exponential backoff polling — TranscriptionPoller [1,2,4,8,16]s, max 60 attempts
- Auto-save debounce — SettingsViewModel Timer 800ms
- TaskCompletionSource dialog results — ConfirmDialog, SelectUserDialog возвращают Task\<T\>
- Download state machine — NotStarted→Downloading→Completed/Failed/Cancelled с CTS и lock

**Медиа**
- Voice recording pipeline — Start→Record→Stop→Validate (0.5s–300s)→Upload→Send
- Whisper CLI — speech-to-text (NAudio PCM 16kHz/16bit/mono)
- Large file streaming — >10MB→temp file (DeleteOnClose), ≤10MB→MemoryStream
- IgnoreDisposeStream — NAudioRecorderService wrapper для WaveFileWriter
- AuthenticatedImageLoader — JWT + extension filtering

**Domain-specific**
- BFS cycle prevention — Department hierarchy (server + client)
- Forward with content inheritance — копирование content/files оригинала
- Dual-scope search — global (chats+messages) и chat-local режимы
- Reactive InfoPanel — подписки GlobalHub + ChatHub → InvalidateAllInfoPanelProperties
- Poll parent-child architecture — PollOptionViewModel→PollViewModel для TotalVotes и single-selection
- Optimistic unread tracking — локальный increment/set + серверная синхронизация
- Cross-reference initialization — Admin parallel load + SetDepartments/SetUsers
- Hierarchical filtering — recursive clone with child propagation