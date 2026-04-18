# Code Conventions

## Общие настройки
- **Фреймворк**: .NET 10.0, Nullable enabled, Implicit usings enabled
- **Язык идентификаторов**: английский
- **Язык ошибок / комментариев**: русский

---

## Именование

### Backend (MessengerAPI)
| Элемент | Шаблон | Пример |
|---------|--------|--------|
| Контроллер | `{Entity}Controller` | `ChatsController` |
| Сервис (интерфейс) | `I{Entity}Service` | `IChatService` |
| Сервис (реализация) | `{Entity}Service` | `ChatService` |
| Маппинг | `{Entity}Mappings` | `ChatMappings` |
| Модель (EF) | `{Entity}` | `Chat`, `Message` |
| Конфигурация | `{Feature}Configuration` | `AuthConfiguration` |

### Desktop (MessengerDesktop)
| Элемент | Шаблон | Пример |
|---------|--------|--------|
| ViewModel | `{Name}ViewModel` | `ChatViewModel` |
| View | `{Name}View.axaml` | `ChatView.axaml` |
| Dialog VM | `{Name}DialogViewModel` | `PollDialogViewModel` |
| Dialog View | `{Name}Dialog.axaml` | `PollDialog.axaml` |
| Handler | `Chat{Feature}Handler` | `ChatReplyHandler` |
| Manager | `Chat{Feature}Manager` | `ChatMessageManager` |
| Converter | `{Purpose}Converter` | `BoolToBrushConverter` |
| Cached entity | `Cached{Entity}` | `CachedMessage` |

### Shared (MessengerShared)
| Элемент | Шаблон | Пример |
|---------|--------|--------|
| DTO | `{Entity}Dto` / `{Action}{Entity}Dto` / `{Action}{Entity}Request` | `CreateMessageRequest` |
| Enum | `{Name}` (без суффикса) | `ChatRole`, `ChatType` |
| Response wrapper | `ApiResponse<T>` | — |

---

## Result Pattern (Backend)

Сервисы **не бросают исключения** для бизнес-ошибок — возвращают `Result` / `Result<T>`.

```csharp
// Типы ошибок → HTTP-код:
ResultErrorType.Validation    // 400
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
Result.Validation("Ошибка валидации")
Result.Conflict("Уже существует")
Result.Unauthorized("Нет прав")
Result<T>.FromFailure(otherResult)   // пробросить ошибку дальше

// Деконструкция:
var (success, data, error) = await service.GetUserAsync(id);

// Match:
result.Match(onSuccess: user => ..., onFailure: err => ...);

// Для Hub / fire-and-forget:
result.UnwrapOrDefault(logger)              // ref type → null при ошибке
result.UnwrapOrFallback(fallback, logger)   // value type → fallback
result.TryUnwrap(out var value, logger)     // try-pattern
```

### BaseController.ExecuteAsync
Автоматически конвертирует `Result<T>` → HTTP-код + `ApiResponse<T>`:
```csharp
[HttpGet("{id}")]
public Task<ActionResult<ApiResponse<UserDto>>> Get(int id)
    => ExecuteAsync(() => _userService.GetUserAsync(id));
```

`GetCurrentUserId()` — извлекает userId из JWT claim `ClaimTypes.NameIdentifier`.
Бросает `UnauthorizedAccessException` если claim отсутствует.

`IsCurrentUser(int userId)` — проверяет совпадение с текущим userId из JWT.
Используется для [SELF]-эндпоинтов вместо атрибута политики.

---

## BaseService (Backend)

Все бизнес-сервисы наследуют `BaseService<T>`:

```csharp
// Сохранение с обработкой EF-исключений:
// DbUpdateConcurrencyException → Result.Conflict
// Unique violation (23505)     → Result.Conflict
// DbUpdateException            → Result.Internal
protected Task<Result> SaveChangesAsync(CancellationToken ct)

// Поиск по ID → автоматический NotFound если null:
protected Task<Result<TEntity>> FindEntityAsync<TEntity>(int id, CancellationToken ct)

// Пагинация:
protected static IQueryable<T> Paginate(IQueryable<T> query, int page, int pageSize)
protected static (int Page, int PageSize) NormalizePagination(int page, int pageSize, int maxSize = 100)
```

---

## Маппинг (Backend)

**Ручной маппинг** через статические extension-методы. AutoMapper не используется.

```csharp
// Модель → DTO:
public static UserDto ToDto(this User user, IUrlBuilder? urlBuilder = null, bool? isOnline = null)

// DTO → обновление модели:
public static void UpdateProfile(this User user, UserDto dto)
```

Классы маппинга: `UserMappings`, `ChatMappings`, `MessageMappings`, `FileMappings`, `PollMappings`.

> URL аватаров и файлов: в БД хранится **относительный путь**,
> конвертируется в абсолютный через `IUrlBuilder` при маппинге.
> `AdminService.ToDto()` вызывается без `urlBuilder` — аватары не резолвятся в абсолютный URL.

---

## Partial Classes (EF-модели)

Модели разделены на два файла:

| Файл | Содержимое |
|------|-----------|
| `{Entity}.cs` | Колонки (скалярные свойства) + навигационные свойства |
| `Partial.cs` | Enum-свойства и `[NotMapped]` вычисляемые свойства |

**Текущие partial-расширения:**

```csharp
// UserSetting.Partial → Theme (хранится в БД через PG enum)
public partial class UserSetting
{
    public Theme? Theme { get; set; }
}

// ChatMember.Partial → Role
public partial class ChatMember
{
    public ChatRole Role { get; set; }
}

// Chat.Partial → Type
public partial class Chat
{
    public ChatType Type { get; set; }
}

// User.Partial → DisplayName (NotMapped, null если все части пустые)
public partial class User
{
    [NotMapped]
    public string? DisplayName => /* Surname + Name + Midname, null если все пусты */
}

// Message.Partial → IsVoiceMessage (NotMapped)
// public partial class Message
// {
//     [NotMapped]
//     public bool IsVoiceMessage => VoiceMessage != null;
// }
```

> Enum-свойства вынесены в Partial потому что EF Fluent API настраивает их
> через `HasConversion` / PG enum mapping — это разделяет конфигурацию от структуры.

---

## BaseViewModel (Desktop)

Все ViewModel наследуют `BaseViewModel : ObservableObject, IDisposable`:

```csharp
bool IsBusy              // индикатор загрузки
string? ErrorMessage     // ошибка для UI
string? SuccessMessage   // успех для UI

// Хуки (виртуальные):
OnIsBusyUpdated(bool value)
OnErrorMessageUpdated(string? value)
OnSuccessMessageUpdated(string? value)

// Безопасное выполнение — IsBusy + try/catch + OperationCanceled игнорируется:
SafeExecuteAsync(Func<Task> action, string? successMessage = null, Action? finallyAction = null)
SafeExecuteAsync(Func<CancellationToken, Task> action, string? successMessage = null, Action? finallyAction = null)

// Отмена — каждый вызов отменяет предыдущий CTS:
CancellationToken GetCancellationToken()

// URL-хелпер:
static string? GetAbsoluteUrl(string? relativeUrl)
```

Пример использования:
```csharp
await SafeExecuteAsync(async ct =>
{
    var result = await _api.GetAsync<UserDto>("/api/users/me", ct);
    // ...
}, successMessage: "Профиль загружен");
```

### ViewLocator
Правило: `"ViewModel"` → `"View"` в полном имени типа:
```
MessengerDesktop.ViewModels.Chat.ChatViewModel
→ MessengerDesktop.Views.Chat.ChatView
```

---

## DI Registration

### Backend (`DependencyInjection.cs`)

| Метод расширения | Содержимое |
|-----------------|-----------|
| `AddMessengerDatabase()` | DbContext + Npgsql enum mappings (`Theme`, `ChatRole`, `ChatType`, `SystemEventType`) |
| `AddInfrastructureServices()` | MemoryCache, HttpContextAccessor, TimeProvider, AppDateTime, OnlineUserService, CacheService, AccessControlService, FileService, TokenService, HubNotifier, HttpUrlBuilder |
| `AddBusinessServices()` | AuthService, UserService, AdminService, ChatService, ChatMemberService, SystemMessageService, NotificationService, MessageService, PollService, ReadReceiptService, DepartmentService |
| `AddMessengerJson()` | STJ: IgnoreCycles, WriteIndented только в Dev |
| `AddMessengerAuth()` | JWT Bearer |
| `AddMessengerSwagger()` | Swagger (только Dev) |

**Lifetimes (Backend)**:

| Lifetime | Сервисы |
|----------|---------|
| `Singleton` | `TimeProvider`, `AppDateTime`, `OnlineUserService` |
| `Scoped` | Все бизнес- и инфраструктурные сервисы |

> `EnableSensitiveDataLogging()` и `EnableDetailedErrors()` для DbContext — **только в Development**.
> HostedService (`BackgroundService`) в проекте отсутствует.

### Desktop (`ServiceCollectionExtensions.cs`)

| Метод расширения | Содержимое |
|-----------------|-----------|
| `AddMessengerCoreServices(apiBaseUrl)` | Все сервисы (API, Auth, Cache, Navigation, Realtime, Storage, UI, Audio, Platform) |
| `AddMessengerViewModels()` | Все ViewModel-ы |

**Lifetimes (Desktop)**:

| Lifetime | Сервисы |
|----------|---------|
| `Singleton` | HttpClient, AuthManager, SessionStore, SecureStorage, NavigationService, DialogService, GlobalHubConnection, LocalDatabase, репозитории кэша, AudioPlayerService |
| `Transient` | Все ViewModel-ы |

**HttpClient** (настройки):
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

| Контекст | Библиотека | Особенности |
|---------|-----------|-------------|
| Backend | `System.Text.Json` | `IgnoreCycles`, `WriteIndented` только в Dev |
| Desktop (HTTP) | `System.Text.Json` | Через `ApiClientService` |
| Desktop (прочее) | `Newtonsoft.Json` | Отдельные сценарии |
| Shared enums | `JsonStringEnumConverter` | Сериализуются как строки |
| PG enums | `MapEnum` + `EnumNameTranslator` | Только backend, через Npgsql |

---

## Обработка ошибок

### Backend
1. Бизнес-ошибки → `Result` → `BaseController.ExecuteAsync` → HTTP-код
2. Необработанные исключения → `ExceptionHandlingMiddleware` → 500 + `ApiResponse`
3. EF-ошибки → `BaseService.SaveChangesAsync` → Conflict / Internal

### Desktop
1. ViewModel → `SafeExecuteAsync` → `ErrorMessage` в UI
2. `OperationCanceledException` → молча игнорируется
3. HTTP 401 → `ApiClientService` → refresh token → повтор запроса

---

## Валидация (Backend)

Централизована в `ValidationHelper`:

| Правило | Детали |
|---------|--------|
| Username | `^[a-z0-9_]{3,30}$`, нормализуется через `Trim().ToLowerInvariant()` |
| Пароль | Минимум 6 символов (расширенная логика в `ValidationHelper.ValidatePassword`) |
| Фамилия / Имя | Обязательны при создании пользователя (не пустые) |

Валидация в сервисах выполняется **последовательно** — первая ошибка прерывает обработку.

---

## CancellationToken

| Контекст | Подход |
|---------|--------|
| Backend | Пробрасывается из контроллера параметром `ct` во все async-методы |
| Desktop | `GetCancellationToken()` — новый вызов отменяет предыдущий CTS того же ViewModel |

---

## URL файлов и аватаров

| Место | Формат |
|-------|--------|
| БД | Относительный путь (`avatars/users/1.jpg`) |
| Backend DTO | Абсолютный URL через `IUrlBuilder.BuildUrl()` |
| Desktop | Абсолютный URL через `GetAbsoluteUrl()` / `AvatarHelper` |

> `AdminService.ToDto()` вызывается без `urlBuilder` — аватары не преобразуются в абсолютный URL
> в ответах `/api/admin`. Учитывать при рендеринге на клиенте.
