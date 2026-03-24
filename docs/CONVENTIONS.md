# Code Conventions

## Общие настройки
- **Целевой фреймворк**: .NET 8.0
- **Nullable reference types**: включены (`<Nullable>enable</Nullable>`)
- **Implicit usings**: включены
- **Язык кода**: английский (идентификаторы, типы)
- **Язык сообщений об ошибках**: русский (пользовательские ошибки в Result и ApiResponse)
- **Язык комментариев/summary**: русский

---

## Именование

### Backend (MessengerAPI)

| Элемент | Шаблон | Пример |
|---------|--------|--------|
| Контроллер | `{Entity}Controller` | `ChatsController`, `MessagesController` |
| Сервис (интерфейс) | `I{Entity}Service` | `IChatService`, `IMessageService` |
| Сервис (реализация) | `{Entity}Service` | `ChatService`, `MessageService` |
| Маппинг | `{Entity}Mappings` | `UserMappings`, `ChatMappings` |
| Модель (EF) | `{Entity}` | `User`, `Chat`, `Message` |
| DTO | `{Entity}Dto` | `UserDto`, `ChatDto` |
| Конфигурация | `{Feature}Configuration` | `AuthConfiguration`, `SwaggerConfiguration` |

### Desktop (MessengerDesktop)

| Элемент | Шаблон | Пример |
|---------|--------|--------|
| ViewModel | `{Name}ViewModel` | `ChatViewModel`, `LoginViewModel` |
| View | `{Name}View.axaml` | `ChatView.axaml`, `LoginView.axaml` |
| Dialog VM | `{Name}DialogViewModel` | `PollDialogViewModel` |
| Dialog View | `{Name}Dialog.axaml` | `PollDialog.axaml` |
| Reusable user item VM | `UserListItemViewModel` | `UserListItemViewModel` |
| Handler | `Chat{Feature}Handler` | `ChatReplyHandler`, `ChatSearchHandler` |
| Manager | `Chat{Feature}Manager` | `ChatMessageManager`, `ChatAttachmentManager` |
| Converter | `{Purpose}Converter` | `BoolToBrushConverter`, `LastSeenTextConverter` |
| Cached entity | `Cached{Entity}` | `CachedChat`, `CachedMessage` |

### Shared (MessengerShared)

| Элемент | Шаблон | Пример |
|---------|--------|--------|
| DTO | `{Entity}Dto` / `{Action}{Entity}Dto` / `{Action}{Entity}Request` | `MessageDto`, `CreatePollDto`, `UpdateChatDto`, `CreateMessageRequest` |
| Enum | `{Name}` (без суффикса) | `ChatRole`, `ChatType`, `Theme` |
| Response wrapper | `ApiResponse<T>` | — |

---

## Архитектурные паттерны

### Result Pattern (Backend)
Сервисы **не бросают исключения** для бизнес-ошибок. Вместо этого возвращают `Result` или `Result<T>`.

```csharp
// Сервис возвращает Result
public async Task<Result<UserDto>> GetUserAsync(int id) { ... }

// Типы ошибок:
ResultErrorType.Validation    // 400 Bad Request
ResultErrorType.Unauthorized  // 401
ResultErrorType.Forbidden     // 403
ResultErrorType.NotFound      // 404
ResultErrorType.Conflict      // 409
ResultErrorType.Internal      // 500

// Фабричные методы:
Result.Success()
Result<T>.Success(value)
Result.NotFound("Не найден")
Result.Forbidden("Нет доступа")
Result<T>.FromFailure(otherResult)  // Пробросить ошибку

// Деконструкция:
var (success, data, error) = await service.GetUserAsync(id);

// Match:
result.Match(
    onSuccess: user => ...,
    onFailure: error => ...
);

// Extension-методы для Hub/fire-and-forget:
result.UnwrapOrDefault(logger)       // ref types → null при ошибке
result.UnwrapOrFallback(fallback, logger)  // value types
result.TryUnwrap(out var value, logger)    // try-pattern
```

### API Response Wrapper (Shared)
Все HTTP-ответы оборачиваются в `ApiResponse<T>`:

```json
{
  "success": true,
  "data": { ... },
  "message": "Операция выполнена",
  "error": null,
  "details": null,
  "timestamp": "2025-01-01T00:00:00Z"
}
```

```csharp
// Успех:
ApiResponse<T>.Ok(data, message?)

// Ошибка:
ApiResponse<T>.Fail(error, details?)
```

### BaseController → Result → ApiResponse маппинг
`BaseController.ExecuteAsync()` автоматически:
1. Вызывает сервисный метод, возвращающий `Result<T>`
2. При успехе → `200 OK` + `ApiResponse<T>.Ok(data)`
3. При ошибке → соответствующий HTTP-код + `ApiResponse<T>.Fail(error)`

```csharp
// В контроллере:
[HttpGet("{id}")]
public Task<ActionResult<ApiResponse<UserDto>>> Get(int id)
    => ExecuteAsync(() => _userService.GetUserAsync(id));
```

Маппинг `ResultErrorType → HTTP Status`:
| ResultErrorType | HTTP Status |
|-----------------|-------------|
| Validation | 400 Bad Request |
| Unauthorized | 401 Unauthorized |
| Forbidden | 403 Forbidden |
| NotFound | 404 Not Found |
| Conflict | 409 Conflict |
| Internal | 500 Internal Server Error |

### Извлечение текущего пользователя
`BaseController.GetCurrentUserId()` — из JWT claim `ClaimTypes.NameIdentifier`.
Бросает `UnauthorizedAccessException` если claim отсутствует.

---

## Backend: BaseService

Все бизнес-сервисы наследуют `BaseService<T>`, который предоставляет:

```csharp
public abstract class BaseService<T>(MessengerDbContext context, ILogger<T> logger)
{
    // Безопасное сохранение с обработкой:
    // - DbUpdateConcurrencyException → Result.Conflict
    // - Unique violation (23505) → Result.Conflict
    // - DbUpdateException → Result.Internal
    protected Task<Result> SaveChangesAsync(ct)

    // Поиск по ID с автоматическим NotFound
    protected Task<Result<TEntity>> FindEntityAsync<TEntity>(int id, ct)

    // Пагинация
    protected static IQueryable<T> Paginate(query, page, pageSize)
    protected static (int Page, int PageSize) NormalizePagination(page, pageSize, maxSize = 100)
}
```

---

## Backend: Маппинг

Используется **ручной маппинг** через статические extension-методы (НЕ AutoMapper):

```csharp
// Extension-метод на модели:
public static UserDto ToDto(this User user, IUrlBuilder? urlBuilder, bool? isOnline)

// Extension-метод для обновления модели из DTO:
public static void UpdateProfile(this User user, UserDto dto)
```

Классы маппинга:
- `UserMappings` — User ↔ UserDto
- `ChatMappings` — Chat ↔ ChatDto
- `MessageMappings` — Message ↔ MessageDto
- `FileMappings` — MessageFile ↔ FileDto
- `PollMappings` — Poll ↔ PollDto

URL аватаров и файлов формируются через `IUrlBuilder` в маппингах.

---

## Desktop: MVVM

### ViewLocator
Автоматический маппинг ViewModel → View по имени:
```
MessengerDesktop.ViewModels.Chat.ChatViewModel
→ MessengerDesktop.Views.Chat.ChatView
```
Правило: замена `"ViewModel"` → `"View"` в полном имени типа.

### BaseViewModel
Все ViewModel наследуют `BaseViewModel` (CommunityToolkit.Mvvm `ObservableObject`):

```csharp
public abstract class BaseViewModel : ObservableObject, IDisposable
{
    bool IsBusy              // Индикатор загрузки
    string? ErrorMessage     // Сообщение об ошибке
    string? SuccessMessage   // Сообщение об успехе

    // Виртуальные хуки для подклассов:
    OnIsBusyUpdated(bool)
    OnErrorMessageUpdated(string?)
    OnSuccessMessageUpdated(string?)

    // Безопасное выполнение с try/catch:
    SafeExecuteAsync(Func<Task>, successMessage?, finallyAction?)
    SafeExecuteAsync(Func<CancellationToken, Task>, ...)

    // Управление отменой — при каждом вызове предыдущий CTS отменяется:
    CancellationToken GetCancellationToken()

    // URL-хелпер:
    static string? GetAbsoluteUrl(string? url)

    // IDisposable — отменяет CTS
}
```

### Паттерн SafeExecuteAsync
```csharp
await SafeExecuteAsync(async ct =>
{
    var result = await _api.GetAsync<UserDto>("/api/users/me", ct);
    // ...
}, successMessage: "Профиль загружен");
// Автоматически: IsBusy = true/false, ErrorMessage при ошибке,
// OperationCanceledException игнорируется
```

---

## DI Registration

### Backend (`DependencyInjection.cs`)
Группировка по слоям:
- `AddMessengerDatabase()` — DbContext + Npgsql enum mappings (через API-side `MapEnum`/`HasPostgresEnum` и кастомные name translator-ы)
- `AddInfrastructureServices()` — Cache, AccessControl, FileService, TokenService, HubNotifier, OnlineUserService
- `AddBusinessServices()` — Auth, Users, Chats, Messaging, Departments
- `AddMessengerJson()` — JSON serialization (IgnoreCycles, WriteIndented в Dev)
- `AddMessengerAuth()` — JWT authentication
- `AddMessengerSwagger()` — Swagger

**Lifetime правила**:
- `Singleton` — OnlineUserService, TranscriptionQueue, TranscriptionService, AppDateTime, TimeProvider
- `Scoped` — все бизнес-сервисы (привязаны к HTTP-запросу)
- `HostedService` — TranscriptionBackgroundService

### Desktop (`ServiceCollectionExtensions.cs`)
Группировка:
- `AddMessengerCoreServices(apiBaseUrl)` — все сервисы
- `AddMessengerViewModels()` — все ViewModel-ы

**Lifetime правила**:
- `Singleton` — HttpClient, AuthManager, SessionStore, SecureStorage, NavigationService, DialogService, HubConnection, LocalDatabase, Cache repositories, AudioPlayer
- `Transient` — ViewModel-ы (новый экземпляр при каждой навигации)

**HttpClient настройка**:
```csharp
new HttpClient(new HttpClientHandler
{
    CheckCertificateRevocationList = false,
    UseProxy = false
})
{
    BaseAddress = new Uri(apiBaseUrl),
    Timeout = TimeSpan.FromSeconds(30)
};
```

---

## JSON-сериализация

### Backend
- `System.Text.Json` (встроенный)
- `ReferenceHandler.IgnoreCycles` — предотвращает циклические ссылки
- `WriteIndented = true` — только в Development

### Desktop
- `Newtonsoft.Json` для некоторых сценариев
- `System.Text.Json` через ApiClientService

### Shared DTO
- Enum-ы сериализуются как строки (`[JsonConverter(typeof(JsonStringEnumConverter))]`)
- PostgreSQL enum-ы маппятся на стороне API через `MapEnum` / `HasPostgresEnum` и `FixedEnumNameTranslator`; `MessengerShared` не содержит `Npgsql`-атрибутов
---

## Обработка ошибок

### Backend
1. **Бизнес-ошибки**: `Result` / `Result<T>` → `BaseController.ExecuteAsync()` → HTTP-код
2. **Необработанные исключения**: `ExceptionHandlingMiddleware` → 500 + ApiResponse
3. **EF ошибки**: `BaseService.SaveChangesAsync()` → ловит Concurrency, Unique, Update exceptions

### Desktop
1. **В ViewModel**: `SafeExecuteAsync()` → catch → `ErrorMessage`
2. **OperationCanceledException**: игнорируется (не ошибка)
3. **HTTP 401**: перехватывается в `ApiClientService` → refresh token

---

## Прочие соглашения

### Partial Classes
Модели EF разделены на partial:
- Основной файл — свойства-колонки и навигации
- `Partial.cs` — enum-свойства (Type, Role, Theme) и [NotMapped] вычисляемые свойства

### CancellationToken
- Backend: пробрасывается из контроллера через `ct` параметр
- Desktop: `GetCancellationToken()` — каждый новый вызов отменяет предыдущий

### URL файлов/аватаров
- В БД хранится **относительный путь**
- При маппинге в DTO конвертируется в абсолютный через `IUrlBuilder` (backend) или `GetAbsoluteUrl()` (desktop)