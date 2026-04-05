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

---

## BaseService (Backend)

Все бизнес-сервисы наследуют `BaseService<T>`:

```csharp
// Сохранение с обработкой EF-исключений:
// DbUpdateConcurrencyException → Result.Conflict
// Unique violation (23505)     → Result.Conflict
// DbUpdateException            → Result.Internal
protected Task<Result> SaveChangesAsync(ct)

// Поиск по ID → автоматический NotFound если null:
protected Task<Result<TEntity>> FindEntityAsync<TEntity>(int id, ct)

// Пагинация:
protected static IQueryable<T> Paginate(query, page, pageSize)
protected static (int Page, int PageSize) NormalizePagination(page, pageSize, maxSize = 100)
```

---

## Маппинг (Backend)

**Ручной маппинг** через статические extension-методы. AutoMapper не используется.

```csharp
// Модель → DTO:
public static UserDto ToDto(this User user, IUrlBuilder? urlBuilder, bool? isOnline)

// DTO → обновление модели:
public static void UpdateProfile(this User user, UserDto dto)
```

Классы: `UserMappings`, `ChatMappings`, `MessageMappings`, `FileMappings`, `PollMappings`.

> URL аватаров и файлов: в БД хранится **относительный путь**, конвертируется в абсолютный через `IUrlBuilder` при маппинге.

---

## BaseViewModel (Desktop)

Все ViewModel наследуют `BaseViewModel : ObservableObject, IDisposable`:

```csharp
bool IsBusy              // индикатор загрузки
string? ErrorMessage     // ошибка для UI
string? SuccessMessage   // успех для UI

// Хуки (виртуальные):
OnIsBusyUpdated(bool)
OnErrorMessageUpdated(string?)
OnSuccessMessageUpdated(string?)

// Безопасное выполнение — IsBusy + try/catch + OperationCanceled игнорируется:
SafeExecuteAsync(Func<Task>, successMessage?, finallyAction?)
SafeExecuteAsync(Func<CancellationToken, Task>, ...)

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
| Метод | Содержимое |
|-------|-----------|
| `AddMessengerDatabase()` | DbContext + Npgsql enum mappings |
| `AddInfrastructureServices()` | Cache, AccessControl, FileService, TokenService, HubNotifier, OnlineUserService |
| `AddBusinessServices()` | Auth, Users, Chats, Messaging, Departments |
| `AddMessengerJson()` | STJ: IgnoreCycles, WriteIndented в Dev |
| `AddMessengerAuth()` | JWT |
| `AddMessengerSwagger()` | Swagger (только Dev) |

**Lifetimes**:
- `Singleton`: `OnlineUserService`, `AppDateTime`, `TimeProvider`
- `Scoped`: все бизнес- и инфраструктурные сервисы
- `HostedService`: `TranscriptionBackgroundService`

### Desktop (`ServiceCollectionExtensions.cs`)
- `AddMessengerCoreServices(apiBaseUrl)` — все сервисы
- `AddMessengerViewModels()` — все ViewModel-ы

**Lifetimes**:
- `Singleton`: HttpClient, AuthManager, SessionStore, SecureStorage, NavigationService, DialogService, HubConnection, LocalDatabase, кеш-репозитории, AudioPlayer
- `Transient`: все ViewModel-ы

**HttpClient**:
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
| Backend | `System.Text.Json` | `IgnoreCycles`, `WriteIndented` в Dev |
| Desktop | `System.Text.Json` (ApiClientService) + `Newtonsoft.Json` (отдельные сценарии) | — |
| Shared enums | `JsonStringEnumConverter` | Сериализуются как строки |
| PG enums | `MapEnum` / `HasPostgresEnum` + `FixedEnumNameTranslator` | Только на стороне API, не в Shared |

---

## Обработка ошибок

### Backend
1. Бизнес-ошибки → `Result` → `BaseController.ExecuteAsync` → HTTP-код
2. Необработанные исключения → `ExceptionHandlingMiddleware` → 500 + ApiResponse
3. EF-ошибки → `BaseService.SaveChangesAsync` → Conflict / Internal

### Desktop
1. ViewModel → `SafeExecuteAsync` → `ErrorMessage`
2. `OperationCanceledException` → игнорируется
3. HTTP 401 → `ApiClientService` → refresh token → повтор

---

## Прочие соглашения

**Partial Classes (EF-модели)**:
- Основной файл — колонки + навигации
- `*.Partial.cs` — enum-свойства (`Type`, `Role`, `Theme`) и `[NotMapped]` вычисляемые свойства

**CancellationToken**:
- Backend: пробрасывается из контроллера параметром `ct`
- Desktop: `GetCancellationToken()` — новый вызов отменяет предыдущий

**URL файлов и аватаров**:
- БД: относительный путь
- DTO: абсолютный URL через `IUrlBuilder` (backend) или `GetAbsoluteUrl()` (desktop)