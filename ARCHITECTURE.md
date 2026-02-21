# Документация проекта: Корпоративный мессенджер

## Общая информация

**Технологический стек:**
- **.NET 8** — основной фреймворк
- **Avalonia** — кроссплатформенный UI фреймворк (десктоп-клиент)
- **PostgreSQL** — база данных
- **Entity Framework Core** — ORM
- **CommunityToolkit.Mvvm** — MVVM-инструментарий для клиента
- **SignalR** — real-time коммуникация
- **BCrypt** — хеширование паролей
- **ImageSharp** — обработка изображений

**Структура решения:**
| Проект | Назначение |
|--------|------------|
| `MessengerAPI` | Серверная часть (REST API + SignalR Hub) |
| `MessengerDesktop` | Десктоп-клиент (Avalonia) |
| `MessengerShared` | Общие модели (DTO, Enum, Response) |

---

## База данных — Entity Models

### Основные сущности

#### User
```
Id, Username, Name, Surname, Midname, PasswordHash
CreatedAt, LastOnline, DepartmentId, Avatar, IsBanned
DisplayName (NotMapped, вычисляемое)

Навигация: Department, UserSetting, ChatMembers[], Chats[], Messages[], PollVotes[], Departments[]
```

#### UserSetting
```
UserId (PK), NotificationsEnabled, Theme (enum)
```

#### Chat
```
Id, Name?, Type (ChatType enum), CreatedAt, CreatedById, LastMessageTime, Avatar

Навигация: CreatedBy, Department?, ChatMembers[], Messages[]
```

#### ChatMember
```
ChatId + UserId (composite PK)
JoinedAt, Role (ChatRole enum), NotificationsEnabled
LastReadMessageId?, LastReadAt?

Навигация: Chat, User, LastReadMessage?
```

#### Message
```
Id, ChatId, SenderId, Content?, CreatedAt, EditedAt?, IsDeleted

Навигация: Chat, Sender, MessageFiles[], Polls[], ChatMembers[] (LastRead)
```

#### MessageFile
```
Id, MessageId, FileName, ContentType, Path?
```

#### Department
```
Id, Name, ParentDepartmentId?, ChatId?, HeadId?

Навигация: Chat?, Head?, ParentDepartment?, InverseParentDepartment[], Users[]
```

#### Poll
```
Id, MessageId, Question, IsAnonymous?, AllowsMultipleAnswers?, ClosesAt?

Навигация: Message, PollOptions[], PollVotes[]
```

#### PollOption
```
Id, PollId, OptionText, Position

Навигация: Poll, PollVotes[]
```

#### PollVote
```
Id, PollId, OptionId, UserId, VotedAt

Навигация: Poll, Option, User
Уникальный индекс: (PollId, UserId, OptionId)
```

#### SystemSetting
```
Key (PK), Value
```

### PostgreSQL Enum Types
- `chat_type`: Chat, Department, Contact
- `chat_role`: Member, Admin, Owner
- `theme`: light, dark, system

### Ключевые индексы
- `idx_messages_chatid_createdat` — для пагинации сообщений
- `idx_chat_members_last_read_message_id` — для прочтений
- `idx_chat_members_user_id` — для поиска членств
- `idx_departments_head_id` — для поиска руководителей
- `idx_polls_message_id` — для связи опросов
- `idx_poll_votes_user_id` — для голосов пользователя
- `users_username_key` — уникальность логина

---

## MessengerShared — Общие модели

### Перечисления (Enum)

| Enum | Значения | Описание |
|------|----------|----------|
| `UserRole` | `User`, `Head`, `Admin` | Роли пользователей в системе |
| `ChatRole` | `Member`, `Admin`, `Owner` | Роли участников в чате |
| `ChatType` | `Chat`, `Department`, `Contact` | Типы чатов (групповой, отдела, личный) |
| `Theme` | `light`, `dark`, `system` | Темы оформления |

---

### DTO — Аутентификация

**`LoginRequest`** — запрос на вход:
```
Username, Password
```

**`AuthResponseDTO`** — ответ аутентификации:
```
Id, Username, DisplayName?, Token, Role (UserRole)
```

---

### DTO — Пользователи

**`UserDTO`** — полная информация о пользователе:
```
Id, Username, DisplayName, Name, Midname, Surname
Department, DepartmentId, Avatar
IsOnline, IsBanned, LastOnline
Theme?, NotificationsEnabled?
```

**`CreateUserDTO`** — создание пользователя:
```
Username, Password, Surname, Name, Midname?, DepartmentId?
```

**`ChangePasswordDTO`**: `CurrentPassword, NewPassword`

**`ChangeUsernameDTO`**: `NewUsername`

**`AvatarResponseDTO`**: `AvatarUrl`

** (record): `UserId, IsOnline, LastOnline?`

**`OnlineUsersResponseDTO`**: `OnlineUserIds[], TotalOnline`

---

### DTO — Чаты

**`ChatDTO`** — информация о чате (ObservableObject для UI):
```
Id, Name?, Type (ChatType), CreatedById
LastMessageDate?, Avatar?, LastMessagePreview?, LastMessageSenderName?
UnreadCount (реактивное свойство)
```

**`UpdateChatDTO`**: `Id, Name?, ChatType?`

---

### DTO — Сообщения

**`MessageDTO`** — сообщение:
```
Id, ChatId, SenderId, SenderName?, SenderAvatarUrl?
Content?, CreatedAt, EditedAt?, IsEdited, IsDeleted
Poll?, Files[], PreviousMessage?
IsOwn, IsPrevSameSender, ShowSenderName (вычисляемое, интервал 5 мин)
```

**`MessageFileDTO`** — файл-вложение:
```
Id, MessageId, FileName, ContentType, Url?, PreviewType, FileSize
```

**`UpdateMessageDTO`**: `Id, Content?`

**`PagedMessagesDTO`** — пагинация сообщений:
```
Messages[], TotalCount, HasMoreMessages, HasNewerMessages, CurrentPage
```

---

### DTO — Опросы

**`PollDTO`**:
```
Id, MessageId, ChatId, CreatedById
Question, IsAnonymous?, AllowsMultipleAnswers?
CreatedAt, ClosesAt?
Options[], SelectedOptionIds?, CanVote
```

**`PollOptionDTO`**:
```
Id, PollId, OptionText (alias: Text), Position, Votes[], VotesCount
```

**`PollVoteDTO`**: `PollId, UserId, OptionId?, OptionIds?`

---

### DTO — Отделы

**`DepartmentDTO`**:
```
Id, Name, ParentDepartmentId?, Head?, HeadName?, UserCount
```

**`UpdateDepartmentMemberDTO`**: `UserId`

---

### DTO — Прочтение сообщений

**`MarkAsReadDTO`**: `ChatId, MessageId?`

**`ReadReceiptResponseDTO`**: `ChatId, LastReadMessageId?, LastReadAt?, UnreadCount`

**`UnreadCountDTO`** (record): `ChatId, UnreadCount`

**`AllUnreadCountsDTO`**: `Chats[], TotalUnread`

**`ChatReadInfoDTO`**: `ChatId, LastReadMessageId?, LastReadAt?, UnreadCount, FirstUnreadMessageId?`

---

### DTO — Поиск

**`GlobalSearchResponseDTO`**:
```
Chats[], Messages[] (GlobalSearchMessageDTO)
TotalChatsCount, TotalMessagesCount, CurrentPage, HasMoreMessages
```

**`GlobalSearchMessageDTO`**:
```
Id, ChatId, ChatName?, ChatAvatar?, ChatType
SenderId, SenderName?, Content?, CreatedAt
HighlightedContent?, HasFiles
```

**`SearchMessagesResponseDTO`**: `Messages[], TotalCount, CurrentPage, HasMoreMessages`

---

### DTO — Уведомления

**`NotificationDTO`**:
```
Type ("message" | "poll"), ChatId, ChatName?, ChatAvatar?
MessageId?, SenderId, SenderName?, SenderAvatar?
Preview?, CreatedAt
```

**`ChatNotificationSettingsDTO`**: `ChatId, NotificationsEnabled`

---

### Response — Обёртки API

**`ApiResponse<T>`** и **`ApiResponse`**:
```
Success, Data? (для generic), Message?, Error?, Details?, Timestamp
```

**`ApiResponseHelper`** — статические методы:
- `Success<T>(data, message?)` / `Success(message?)`
- `Error<T>(error, details?)` / `Error(error, details?)`

---

## MessengerAPI — Серверная часть

### Конфигурация

**`MessengerSettings`** (секция `Messenger`):
```csharp
AdminDepartmentId = 1          // ID отдела администраторов
MaxFileSizeBytes = 20MB        // Макс. размер файла
BcryptWorkFactor = 12          // Сложность хеширования
MaxImageDimension = 1600       // Макс. размер изображения
ImageQuality = 85              // Качество WebP
DefaultPageSize = 50           // Пагинация по умолчанию
MaxPageSize = 100              // Макс. размер страницы
```

**`JwtSettings`** (секция `Jwt`):
```csharp
Secret                         // Секретный ключ (мин. 32 символа)
LifetimeHours = 24             // Время жизни токена
Issuer = "MessengerAPI"
Audience = "MessengerClient"
```

---

### Program.cs — Конфигурация приложения

**Регистрация сервисов:**
```csharp
// Singleton
IOnlineUserService

// Scoped
ITokenService, IAccessControlService, IFileService
IAuthService, IUserService, IAdminService, IChatService
IDepartmentService, IMessageService, IPollService
INotificationService, IReadReceiptService
```

**Middleware pipeline:**
1. ExceptionHandling
2. Swagger (Development)
3. HTTPS Redirection
4. Static Files (uploads, avatars)
5. CORS
6. Authentication
7. Authorization
8. Controllers
9. SignalR Hub

**Static Files:**
- `/uploads` — загруженные файлы
- `/avatars` — аватары пользователей
- Отключено кеширование для аватаров

---

### Common — Вспомогательные классы

**`Result<T>`** — Result-паттерн для бизнес-логики:
```csharp
IsSuccess, IsFailure, Value?, Error?
// Методы:
Result<T>.Success(value)
Result<T>.Failure(error)
Match(onSuccess, onFailure)
// Деконструктор:
var (success, data, error) = result;
```

---

### Controllers — API-контроллеры

#### BaseController<T>
Базовый контроллер с общей функциональностью:

**Идентификация пользователя:**
- `GetCurrentUserId()` — ID из JWT claims
- `GetCurrentUsername()` — Username из claims
- `IsCurrentUser(userId)` — проверка владельца

**Обёртки ответов:**
- `Success<T>()`, `Created<T>()`, `BadRequest<T>()`, `NotFound<T>()`, `Unauthorized<T>()`, `Forbidden<T>()`, `InternalError<T>()`

**ExecuteAsync-паттерн:**
```csharp
await ExecuteAsync(async () => {
    // бизнес-логика
    return data;
}, "Успешное сообщение");
```
Автоматическая обработка исключений:
- `ArgumentException` → 400
- `UnauthorizedAccessException` → 401
- `KeyNotFoundException` → 404
- `InvalidOperationException` → 400
- `Exception` → 500

---

#### AuthController
| Метод | Endpoint | Описание |
|-------|----------|----------|
| POST | `/api/auth/login` | Аутентификация (AllowAnonymous) |

---

#### AdminController
**Требует роль: `Admin`**

| Метод | Endpoint | Описание |
|-------|----------|----------|
| GET | `/api/admin/users` | Список всех пользователей |
| POST | `/api/admin/users` | Создание пользователя |
| POST | `/api/admin/users/{id}/toggle-ban` | Блокировка/разблокировка |

---

#### UserController
| Метод | Endpoint | Описание |
|-------|----------|----------|
| GET | `/api/user` | Все пользователи |
| GET | `/api/user/{id}` | Пользователь по ID |
| GET | `/api/user/online` | Список онлайн |
| GET | `/api/user/{id}/status` | Статус пользователя |
| POST | `/api/user/status/batch` | Статусы списка пользователей |
| PUT | `/api/user/{id}` | Обновление профиля |
| POST | `/api/user/{id}/avatar` | Загрузка аватара |
| PUT | `/api/user/{id}/username` | Смена логина |
| PUT | `/api/user/{id}/password` | Смена пароля |

---

#### ChatsController
| Метод | Endpoint | Описание |
|-------|----------|----------|
| GET | `/api/chats/user/{userId}` | Все чаты пользователя |
| GET | `/api/chats/user/{userId}/dialogs` | Личные диалоги |
| GET | `/api/chats/user/{userId}/groups` | Групповые чаты |
| GET | `/api/chats/user/{userId}/contact/{contactUserId}` | Диалог с контактом |
| GET | `/api/chats/{chatId}` | Информация о чате |
| GET | `/api/chats/{chatId}/members` | Участники чата |
| POST | `/api/chats` | Создание чата |
| PUT | `/api/chats/{id}` | Обновление чата |
| DELETE | `/api/chats/{id}` | Удаление чата |
| POST | `/api/chats/{id}/avatar` | Аватар чата |

---

#### MessagesController
| Метод | Endpoint | Описание |
|-------|----------|----------|
| POST | `/api/messages` | Отправка сообщения |
| PUT | `/api/messages/{id}` | Редактирование |
| DELETE | `/api/messages/{id}` | Удаление |
| GET | `/api/messages/chat/{chatId}` | Сообщения чата (пагинация) |
| GET | `/api/messages/chat/{chatId}/around/{messageId}` | Сообщения вокруг ID |
| GET | `/api/messages/chat/{chatId}/before/{messageId}` | Старые сообщения |
| GET | `/api/messages/chat/{chatId}/after/{messageId}` | Новые сообщения |
| GET | `/api/messages/user/{userId}/search` | Глобальный поиск |

---

#### DepartmentController
| Метод | Endpoint | Авторизация | Описание |
|-------|----------|-------------|----------|
| GET | `/api/department` | — | Список отделов |
| GET | `/api/department/{id}` | — | Отдел по ID |
| POST | `/api/department` | Admin | Создание |
| PUT | `/api/department/{id}` | Admin | Обновление |
| DELETE | `/api/department/{id}` | Admin | Удаление |
| GET | `/api/department/{id}/members` | — | Сотрудники отдела |
| POST | `/api/department/{id}/members` | Head/Admin | Добавить сотрудника |
| DELETE | `/api/department/{id}/members/{userId}` | Head/Admin | Удалить сотрудника |
| GET | `/api/department/{id}/can-manage` | — | Проверка прав |

---

#### PollController
| Метод | Endpoint | Описание |
|-------|----------|----------|
| GET | `/api/poll/{pollId}` | Получить опрос |
| POST | `/api/poll` | Создать опрос |
| POST | `/api/poll/vote` | Проголосовать |

---

#### ReadReceiptsController
| Метод | Endpoint | Описание |
|-------|----------|----------|
| POST | `/api/readreceipts/mark-read` | Отметить прочитанным |
| GET | `/api/readreceipts/chat/{chatId}/unread-count` | Непрочитанные в чате |
| GET | `/api/readreceipts/unread-counts` | Все непрочитанные |

---

#### NotificationController
| Метод | Endpoint | Описание |
|-------|----------|----------|
| GET | `/api/notification/chat/{chatId}/settings` | Настройки уведомлений чата |
| POST | `/api/notification/chat/mute` | Вкл/выкл уведомления |
| GET | `/api/notification/settings` | Все настройки |

---

#### FilesController
| Метод | Endpoint | Описание |
|-------|----------|----------|
| POST | `/api/files/upload?chatId={id}` | Загрузка файла |

---

### SignalR Hub — ChatHub

**Авторизация:** Требуется JWT

#### События подключения:
- `OnConnectedAsync` — регистрация онлайн, подписка на группы чатов, группу пользователя `user_{userId}`
- `OnDisconnectedAsync` — обновление LastOnline, уведомление об офлайне

#### Методы клиента → сервер:
| Метод | Параметры | Описание |
|-------|-----------|----------|
| `JoinChat` | chatId | Подписка на чат (с проверкой членства) |
| `LeaveChat` | chatId | Отписка от чата |
| `MarkAsRead` | chatId, messageId? | Отметить прочитанным |
| `MarkMessageAsRead` | chatId, messageId | Прочитать конкретное сообщение |
| `GetReadInfo` | chatId | Получить инфо о прочтении → `ChatReadInfoDTO?` |
| `GetUnreadCounts` | — | Все непрочитанные → `AllUnreadCountsDTO` |
| `SendTyping` | chatId | Индикатор набора |
| `GetOnlineUsersInChat` | chatId | Онлайн участники чата → `List<int>` |

#### События сервер → клиент:
| Событие | Данные | Описание |
|---------|--------|----------|
| `UserOnline` | userId | Пользователь онлайн |
| `UserOffline` | userId | Пользователь офлайн |
| `UserTyping` | chatId, userId | Набирает сообщение |
| `UnreadCountUpdated` | chatId, count | Обновление счётчика |
| `MessageRead` | chatId, userId, messageId, timestamp | Сообщение прочитано |
| `ReceiveMessageDTO` | MessageDTO | Новое сообщение |
| `MessageUpdated` | MessageDTO | Сообщение отредактировано |
| `MessageDeleted` | {MessageId, ChatId} | Сообщение удалено |
| `ReceiveNotification` | NotificationDTO | Push-уведомление |
| `ReceivePollUpdate` | PollDTO | Обновление опроса |

---

### Services — Сервисы (API)

#### AccessControlService (Scoped)
Кеширование проверок членства в рамках HTTP-запроса:
```csharp
IsMemberAsync(userId, chatId)
IsOwnerAsync(userId, chatId)
IsAdminAsync(userId, chatId)
GetRoleAsync(userId, chatId)
EnsureIsMemberAsync/EnsureIsOwnerAsync/EnsureIsAdminAsync
```

---

#### AuthService
- `LoginAsync(username, password)` → `Result<AuthResponseDTO>`
- Определение роли: AdminDepartmentId → Admin, Head отдела → Head, иначе User
- Защита от timing-атак (dummy hash при отсутствии пользователя)

---

#### AdminService
- `GetUsersAsync()` — все пользователи
- `CreateUserAsync(dto)` — с валидацией username (regex: `^[a-z0-9_]{3,30}$`)
- `ToggleBanAsync(userId)` — блокировка

---

#### ChatService
**Получение:**
- `GetUserChatsAsync` — с непрочитанными, превью, сортировкой
- `GetChatAsync`, `GetUserDialogsAsync`, `GetUserGroupsAsync`, `GetContactChatAsync`
- `GetChatMembersAsync` — с онлайн-статусами

**CRUD:**
- `CreateChatAsync` — для Contact: проверка существующего диалога
- `UpdateChatAsync` — смена типа только для Owner
- `DeleteChatAsync` — каскадное удаление
- `UploadChatAvatarAsync`

**Особенности диалогов (Contact):**
- Name = null, данные берутся от собеседника
- Автоматическое определение собеседника

---

#### MessageService
**CRUD:**
- `CreateMessageAsync` — с файлами, опросами, SignalR-уведомлениями, обновлением счётчиков
- `UpdateMessageAsync` — нельзя редактировать сообщения с опросами
- `DeleteMessageAsync` — soft delete (IsDeleted = true, Content = null)

**Получение:**
- `GetChatMessagesAsync` — пагинация, сортировка по дате DESC
- `GetMessagesAroundAsync` — для скролла к непрочитанным
- `GetMessagesBeforeAsync` — подгрузка старых
- `GetMessagesAfterAsync` — подгрузка новых

**Поиск:**
- `SearchMessagesAsync` — в чате с экранированием LIKE-паттерна
- `GlobalSearchAsync` — по всем чатам пользователя с подсветкой контекста

**Особенности:**
- `EscapeLikePattern` — экранирование `\`, `%`, `_`
- `CreateHighlightedContent` — контекст ±40 символов вокруг совпадения

---

#### NotificationService (API)
- `NotifyNewMessageAsync` — уведомления участникам чата
- `GetChatNotificationSettingsAsync`, `SetChatMuteAsync`, `GetAllChatSettingsAsync`

**Логика уведомлений:**
- Исключается отправитель
- Проверяется `ChatMember.NotificationsEnabled`
- Проверяется `UserSetting.NotificationsEnabled`
- Отправляется через группу `user_{userId}`

---

#### OnlineUserService (Singleton)
```csharp
ConcurrentDictionary<userId, ConcurrentDictionary<connectionId, byte>>

UserConnected(userId, connectionId)
UserDisconnected(userId, connectionId)
IsUserOnline(userId) / IsOnline(userId)
GetOnlineUserIds() → HashSet<int>
FilterOnlineUserIds(userIds) / FilterOnline(userIds)
OnlineCount
```
Поддержка множественных подключений одного пользователя.

---

#### PollService
- `GetPollAsync(pollId, userId)` — с информацией о голосе пользователя
- `CreatePollAsync(dto)` — создаёт Message + Poll + PollOptions
- `VoteAsync(voteDto)` — удаляет старые голоса, добавляет новые

---

#### ReadReceiptService
- `MarkAsReadAsync(userId, request)` — до указанного или последнего сообщения
- `MarkMessageAsReadAsync(userId, chatId, messageId)` — конкретное сообщение
- `GetUnreadCountAsync(userId, chatId)` — не считает свои сообщения
- `GetAllUnreadCountsAsync(userId)`
- `GetUnreadCountsForChatsAsync(userId, chatIds)` — batch
- `GetChatReadInfoAsync(userId, chatId)` — с FirstUnreadMessageId

---

#### UserService
- `GetAllUsersAsync`, `GetUserAsync` — с онлайн-статусами
- `UpdateUserAsync` — профиль и настройки
- `UploadAvatarAsync`
- `GetOnlineStatusAsync`, `GetOnlineStatusesAsync` — batch
- `ChangeUsernameAsync` — с валидацией уникальности
- `ChangePasswordAsync` — с проверкой текущего пароля

---

#### DepartmentService
**CRUD:**
- Валидация циклических зависимостей для ParentDepartmentId
- Проверка наличия дочерних отделов/сотрудников при удалении

**Участники:**
- `AddUserToDepartmentAsync` — перемещение между отделами только для Admin
- `RemoveUserFromDepartmentAsync` — нельзя удалить руководителя

**Права:**
- `CanManageDepartmentAsync` — Admin или Head отдела

---

#### FileService
- `SaveImageAsync` — конвертация в WebP, ресайз до MaxImageDimension
- `SaveMessageFileAsync` — сохранение с проверкой размера
- `DeleteFile` — безопасное удаление
- Поддерживаемые форматы изображений: jpeg, png, gif, webp, bmp

---

#### TokenService (JWT)
- `GenerateToken(userId, role?)` — создание токена
- `ValidateToken(token, out userId)` — проверка
- `GetValidationParameters()` — параметры для middleware

---

### Middleware

**ExceptionHandlingMiddleware:**
- Глобальный перехват исключений
- Маппинг типов исключений на HTTP-коды
- Details только в Development для 500-ошибок

---

### Helpers

**ModelExtensions:**
- `User.ToDto()`, `User.FormatDisplayName()`, `User.UpdateProfile()`, `User.UpdateSettings()`
- `Chat.ToDto()`, `Chat.ToDto(dialogPartner)` — с подстановкой данных собеседника
- `Message.ToDto()` — с обработкой удалённых сообщений
- `MessageFile.ToDto()`, `Poll.ToDto()`, `PollOption.ToDto()`
- `BuildFullUrl(path, request)` — построение полного URL
- `DeterminePreviewType(contentType)` → image/video/audio/file

---

## MessengerDesktop — Клиентская часть

### App.xaml.cs — Точка входа

**Инициализация:**
1. `Initialize()` — загрузка XAML, конфигурация DI
2. `OnFrameworkInitializationCompleted()` — создание MainWindow, инициализация сервисов

**API URL:**
- Debug: `https://localhost:7190/`
- Release: `http://localhost:5274/`

**Dispose:**
- Освобождение всех IDisposable сервисов
- Cleanup платформенных сервисов

---

### Dependency Injection

**ServiceCollectionExtensions** — регистрация сервисов:
```csharp
AddMessengerCoreServices(apiBaseUrl)  // Core + Auth + API + Navigation
AddMessengerViewModels()               // ViewModels + Factories
```

**Singleton сервисы:**
- `HttpClient`, `IAuthService`, `ISessionStore`, `ISecureStorageService`, `IAuthManager`
- `IApiClientService`, `INavigationService`, `IDialogService`, `INotificationService`
- `IGlobalHubConnection`, `ISettingsService`, `IPlatformService`
- `IChatViewModelFactory`, `IChatsViewModelFactory`

**Transient ViewModels:**
- `LoginViewModel`, `MainMenuViewModel`, `AdminViewModel`, `ProfileViewModel`
- `DepartmentManagementViewModel`, `SettingsViewModel`

---

### Services/Api

#### ApiClientService
HTTP-клиент с автоматической авторизацией:
```csharp
GetAsync<T>(url)
PostAsync<TRequest, TResponse>(url, data)
PostAsync<T>(url, data) / PostAsync(url, data)
PutAsync<T>(url, data) / PutAsync(url, data)
DeleteAsync(url)
UploadFileAsync<T>(url, stream, fileName, contentType)
GetStreamAsync(url) — с поддержкой больших файлов (>10MB → temp file)
```

**Особенности:**
- Подписка на `ISessionStore.SessionChanged` для обновления Authorization header
- Автоматическая десериализация `ApiResponse<T>`
- Fallback на прямую десериализацию данных

---

### Services/Auth

#### AuthManager
Координатор аутентификации:
```csharp
IsInitialized, Session
InitializeAsync() — загрузка сохранённой сессии
LoginAsync(username, password) — вход + сохранение
LogoutAsync() — выход + очистка
WaitForInitializationAsync(timeout?)
```

**Хранение:**
- Токен, UserId, UserRole в SecureStorage
- Валидация токена при запуске

---

#### AuthService
```csharp
LoginAsync(username, password) → ApiResponse<AuthResponseDTO>
ValidateTokenAsync(token) → ApiResponse
LogoutAsync(token) → ApiResponse
```

---

#### SecureStorageService
Шифрованное локальное хранилище:
```csharp
SaveAsync<T>(key, value)
GetAsync<T>(key)
RemoveAsync(key)
ContainsKeyAsync(key)
```

**Реализация:**
- AES-256 шифрование
- Ключ из PBKDF2 (machine data + salt, 100K итераций)
- Salt хранится в `.salt` (hidden file)
- Файлы в `%AppData%/MessengerDesktop/SecureStorage/`

---

#### SessionStore (ObservableObject)
```csharp
UserId?, Token?, UserRole
IsAuthenticated, IsAdmin, IsHead, IsUser

SetSession(token, userId, role)
ClearSession()

HasRole(requiredRole) — иерархическая проверка
HasAnyRole(roles) — любая из ролей
IsInRole(role) — точное совпадение

event SessionChanged
```

**Иерархия ролей:** User(0) < Head(1) < Admin(2)

---

### Services — Real-time

#### GlobalHubConnection
Глобальное SignalR-подключение для уведомлений:
```csharp
// События
NotificationReceived(NotificationDTO)
UserStatusChanged(userId, isOnline)
UnreadCountChanged(chatId, count)
TotalUnreadChanged(total)

// Методы
ConnectAsync(), DisconnectAsync()
SetCurrentChat(chatId?) — для подавления уведомлений текущего чата
GetUnreadCountsAsync() → AllUnreadCountsDTO?
MarkChatAsReadAsync(chatId)
GetUnreadCount(chatId), GetTotalUnread()
```

**Обработка событий SignalR:**
- `ReceiveNotification` — показ уведомления (если не текущий чат)
- `UserOnline` / `UserOffline`
- `UnreadCountUpdated`
- `ReceiveMessageDTO` — инкремент счётчика
- `UserTyping`

**Особенности:**
- Локальный кеш счётчиков `Dictionary<chatId, count>`
- Мгновенный отклик UI при `MarkChatAsReadAsync`
- Автоматическое переподключение (`WithAutomaticReconnect`)

---

#### ChatHubConnection
SignalR-подключение для конкретного чата:
- Debounce 300мс для `MarkMessageAsRead`
- Обработка сообщений, опросов, typing

---

### Services/Navigation

#### NavigationService
```csharp
CurrentViewModel, CurrentViewModelChanged event
NavigateTo<T>() where T : BaseViewModel
NavigateToLogin()
NavigateToMainMenu()
GoBack() — история навигации
CanGoBack
```

---

### Services/Platform

#### PlatformService
```csharp
MainWindow, Clipboard
Initialize(window), Cleanup()
CopyToClipboardAsync(text)
GetFromClipboardAsync()
ClearClipboardAsync()
IsClipboardAvailable()
```

---

### Services/Storage

#### SettingsService
```csharp
// Серверные настройки
NotificationsEnabled, CanBeFoundInSearch
ResetUserSettings()

// Локальное key-value хранилище
Get<T>(key), Set<T>(key, value), Remove(key)
```
Файл: `%AppData%/MessengerDesktop/settings.json`

---

### Services/UI

#### DialogService
Стек диалоговых окон с анимациями:
```csharp
DialogStack, CurrentDialog, HasOpenDialogs, IsDialogVisible

ShowAsync<TViewModel>(dialogViewModel)
CloseAsync()
CloseAllAsync()
NotifyAnimationComplete()

event OnDialogStackChanged
event OnDialogAnimationRequested(isOpening)
```

**Особенности:**
- Channel для очереди закрытия
- Таймаут анимации 1 сек
- Поддержка вложенных диалогов

---

#### NotificationService (Avalonia)
```csharp
Initialize(window)
ShowWindow(title, message, type, durationMs)
ShowBothAsync(title, message, type, copyToClipboard)
ShowErrorAsync/ShowSuccessAsync/ShowWarningAsync/ShowInfoAsync
ShowCopyableErrorAsync(message) — копирует в буфер с timestamp
```

---

### Services — Специализированные

#### FileDownloadService
```csharp
DownloadFileAsync(url, fileName, progress?, ct) → filePath?
GetDownloadsFolder() — кроссплатформенный
OpenFileAsync(filePath)
OpenFolderAsync(folderPath) — с выделением файла
```

**Особенности:**
- Прогресс скачивания
- Уникальные имена файлов (counter)
- Кроссплатформенное открытие папок (explorer/open/xdg-open)

---

#### ChatInfoPanelStateStore
```csharp
IsOpen — сохраняется в SettingsService
```

---

#### ChatNotificationApiService
```csharp
GetChatSettingsAsync(chatId) → ChatNotificationSettingsDTO?
SetChatMuteAsync(chatId, isMuted) → bool
GetAllSettingsAsync() → List<ChatNotificationSettingsDTO>
```

---

### Helpers

#### AvatarHelper
```csharp
GetSafeUrl(avatarUrl?) — fallback на default-avatar.webp
GetUrlWithCacheBuster(avatarUrl?) — добавляет ?v=timestamp
```

#### MimeTypeHelper
```csharp
GetMimeType(filePath) → contentType
// Поддержка: jpg, png, gif, webp, mp4, webm, mp3, wav, pdf, doc, xls, txt
```

---

### ViewModels — Базовые

#### BaseViewModel
```csharp
IsBusy, ErrorMessage, SuccessMessage
GetCancellationToken() — отменяет предыдущий
SafeExecuteAsync(operation, successMessage?, finallyAction?)
SafeExecuteAsync(operation with CancellationToken, ...)
GetAbsoluteUrl(url?) — добавляет базовый URL
ClearMessagesCommand
Dispose() — отмена CancellationTokenSource
```

---

#### DialogBaseViewModel : BaseViewModel
```csharp
Title, CanCloseOnBackgroundClick, IsInitialized
CloseRequested event (Action)
RequestClose()
InitializeAsync(initAction) — обёртка для безопасной инициализации
CancelCommand, CloseOnBackgroundClickCommand
```

---

### ViewModels — Factories

#### IChatViewModelFactory
```csharp
Create(chatId, parent) → ChatViewModel
```

#### IChatsViewModelFactory
```csharp
Create(parent, isGroupMode) → ChatsViewModel
```

---

### ViewModels — Основные

#### MainMenuViewModel
**Свойства:**
```csharp
CurrentMenuViewModel, UserId, SearchText
AllContacts, UserChats
IsSearching, SelectedMenuIndex
HasSearchText, ShowNoResults
```

**Навигация (SetItemCommand):**
- 0: SettingsViewModel
- 1, 2: ChatsViewModel (groups)
- 3: ProfileViewModel
- 4: AdminViewModel
- 5: ChatsViewModel (contacts)
- 6: DepartmentManagementViewModel

**Методы:**
```csharp
SwitchToTabAndOpenChatAsync(chat) — универсальный переход
SwitchToTabAndOpenMessageAsync(message) — из глобального поиска
OpenOrCreateChatAsync(user)
ShowUserProfileAsync(userId)
ShowPollDialogAsync(chatId, onPollCreated?)
ShowCreateGroupDialogAsync(onGroupCreated?)
ShowEditGroupDialogAsync(chat, onGroupUpdated?)
SetActiveMenu(index)
```

---

#### LoginViewModel
```csharp
Username, Password, RememberMe
LoginCommand — с сохранением credentials
ClearCredentialsCommand
```

**Автозагрузка:**
- Проверка `IsAuthenticated` → `NavigateToMainMenu`
- Загрузка сохранённого username

---

#### SettingsViewModel
```csharp
SelectedTheme, NotificationsEnabled, CanBeFoundInSearch
AvailableThemes, CurrentThemeDisplay

ToggleThemeCommand
SaveNowCommand
ResetToDefaultsCommand
```

**Автосохранение:**
- Таймер 1 сек после изменения
- Синхронизация с сервером и `ISettingsService`
- Применение темы через `App.Current.ThemeVariant`

---

#### AdminViewModel
**Вкладки:** `UsersTabViewModel`, `DepartmentsTabViewModel`
**Группировка:** `DepartmentGroup`, `HierarchicalDepartmentViewModel`

---

#### ChatsViewModel
```csharp
IsGroupMode, Chats, SelectedChat, CurrentChatViewModel
SearchManager (GlobalSearchManager), TotalUnreadCount
```

---

#### ChatViewModel
**Компоненты:**
- `ChatMessageManager` — загрузка, пагинация, прочтение
- `ChatAttachmentManager` — файлы
- `ChatSearchManager` — поиск в чате
- `ChatHubConnection` — real-time
- `ChatMemberLoader` — участники

**События:**
- `ScrollToMessageRequested(MessageViewModel)`
- `ScrollToIndexRequested(int)`

---

### ViewModels — Chat Components

#### MessageViewModel
```csharp
Id, ChatId, SenderId, Content, CreatedAt, IsOwn, IsEdited, IsDeleted
SenderName, SenderAvatar, ShowSenderName
IsHighlighted, IsUnread, IsRead
Poll?, FileViewModels[]
```

#### MessageFileViewModel
```csharp
State: NotStarted|Downloading|Completed|Failed|Cancelled
DownloadCommand, OpenFileCommand, OpenInFolderCommand
```

#### PollViewModel
```csharp
Options (PollOptionViewModel[]), CanVote, TotalVotes
VoteCommand, CancelVoteCommand
```

#### GlobalSearchManager
```csharp
ChatResults, MessageResults, HasMoreMessages
ExecuteSearchAsync(), LoadMoreMessagesAsync(), ExitSearch()
```

---

### ViewModels — Диалоги

#### ChatEditDialogViewModel
Создание/редактирование группового чата:
```csharp
Name, AvailableUsers, FilteredUsers, SearchUserQuery
AvatarPreview, IsNewChat, SelectedUsersCount, CanSave

SaveAction: Func<ChatDTO, List<int>, Stream?, string?, Task<bool>>

InitializeCommand — загрузка пользователей и аватара
SelectAvatarCommand, ClearAvatarCommand
SelectAllCommand, DeselectAllCommand
SaveCommand
```

**SelectableUserItem:**
```csharp
User, IsSelected, Id, DisplayName, Username, AvatarUrl, HasAvatar
```

---

#### DepartmentDialogViewModel
Создание/редактирование отдела:
```csharp
Name, AvailableParents, SelectedParent
EditId?, IsNewDepartment, ParentDepartmentId?, CanSave

SaveAction: Func<DepartmentDialogViewModel, Task>
SaveCommand
```

**Валидация:**
- Исключение циклических зависимостей
- Исключение потомков из списка родителей

---

#### DepartmentHeadDialogViewModel
Расширенное редактирование отдела с руководителем:
```csharp
// + к DepartmentDialogViewModel:
SelectedHead?, HasHead, HeadDisplayText
UserCount, HasChildren, CanDelete, DeleteTooltip

DeleteAction: Func<DepartmentHeadDialogViewModel, Task>

SelectHeadCommand — открывает SelectUserDialogViewModel
ClearHeadCommand
DeleteCommand — с подтверждением
```

---

#### PollDialogViewModel
Создание опроса:
```csharp
Question, Options (ObservableCollection<OptionItem>)
AllowsMultipleAnswers, IsAnonymous
CanAddOption, CanRemoveOption, CanCreate

CreateAction: Action<PollDTO>

AddOptionCommand, RemoveOptionCommand(item)
CreateCommand
```

**Ограничения:**
- MinOptions = 2, MaxOptions = 10

---

#### UserEditDialogViewModel
Создание/редактирование пользователя:
```csharp
Username, Surname, Name, Midname
Password, ConfirmPassword (только для нового)
Departments, SelectedDepartment
IsNewUser, CanSave, DisplayNamePreview

CreateAction: Func<CreateUserDTO, Task>
UpdateAction: Func<UserDTO, Task>
SaveCommand
```

**Валидация:**
- Username: мин. 3 символа
- Password: мин. 6 символов, совпадение

---

#### UserProfileDialogViewModel
Просмотр профиля пользователя:
```csharp
User, AvatarBitmap, Department, AvatarUrl
CanSendMessage (скрываем для себя)

OpenChatWithUserAction: Func<UserDTO, Task>

InitializeCommand — загрузка аватара
SendMessageCommand
```

---

#### SelectUserDialogViewModel
```csharp
Users, SelectedUser
Result: Task<UserDTO?>
SelectCommand(user)
```

---

#### ConfirmDialogViewModel
```csharp
Message, ConfirmText, CancelText
Result: Task<bool>
ConfirmCommand, CancelCommand
```

---

## Ключевые особенности архитектуры

1. **Иерархия отделов** — `ParentDepartmentId` с защитой от циклов
2. **Три типа чатов** — групповые, отделов (автоматические), личные (Contact)
3. **Диалоги (Contact)** — Name хранится null, данные берутся от собеседника динамически
4. **Result-паттерн** — для бизнес-логики без исключений
5. **ExecuteAsync-паттерн** — унифицированная обработка ошибок в контроллерах
6. **SafeExecuteAsync** — аналог на клиенте с CancellationToken
7. **Кеширование доступа** — AccessControlService в рамках запроса
8. **Опросы** — анонимность, множественный выбор, 2-10 вариантов, дедлайн
9. **Реактивный UI** — `ObservableObject` в ChatDTO, SessionStore и ViewModels
10. **Пагинация** — двунаправленная (before/after/around messageId)
11. **Real-time** — GlobalHubConnection + ChatHubConnection (SignalR)
12. **Debounce** — поиск (300мс), MarkMessageAsRead (300мс)
13. **Оптимизация изображений** — автоконвертация в WebP
14. **Безопасное хранение** — AES-256 + PBKDF2 (100K итераций) для токенов
15. **Множественные подключения** — ConcurrentDictionary для connections одного пользователя
16. **Глобальный поиск** — по чатам и сообщениям с подсветкой контекста
17. **Factory-паттерн** — для создания ChatViewModel и ChatsViewModel
18. **Dialog Stack** — поддержка вложенных диалогов с анимациями
19. **Scroll to message** — навигация из поиска с подсветкой
20. **Unread tracking** — FirstUnreadMessageId, автоскролл к непрочитанным
21. **PostgreSQL Enums** — ChatType, ChatRole, Theme как нативные enum-типы в БД