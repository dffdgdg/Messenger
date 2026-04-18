# Glossary

## Роли пользователей (`UserRole`)
- `User` — обычный пользователь.
- `Head` — руководитель подразделения (`departments.head_id = userId`).
  Приоритет ниже `Admin`.
- `Admin` — администратор системы (`department_id = 1`, технический отдел).
  Имеет наивысший приоритет.

> Роль **не хранится** в БД — вычисляется динамически при каждом запросе.
> Приоритет: `Admin` > `Head` > `User`.

---

## Роли в чатах (`ChatRole`)
- `Member` — участник. Может читать, писать, добавлять участников.
- `Admin` — администратор чата. Дополнительно может удалять участников.
- `Owner` — владелец чата. Полные права, включая удаление чата и смену ролей.
  Не может покинуть чат без передачи роли.

---

## Типы чатов (`ChatType`)
- `Chat` — обычный групповой чат, создаётся пользователем вручную.
- `Department` — чат отдела, создаётся автоматически вместе с отделом.
  Состав синхронизируется триггерами БД.
- `Contact` — личный диалог 1:1, создаётся автоматически при первом обращении.
- `DepartmentHeads` — системный чат для всех руководителей отделов (`Head`).

---

## Архитектура — Backend

- **`ChatHub`** — SignalR hub (`/chatHub`, `[Authorize]`).
  Группы: `chat_{chatId}` (все участники чата), `user_{userId}` (персональная).
- **`HubNotifier`** — backend-сервис доставки SignalR-событий из бизнес-слоя.
  Fire-and-forget, ошибки логируются.
- **`OnlineUserService`** — backend singleton, трекинг online-состояния
  через `ConcurrentDictionary`. Поддерживает несколько соединений на одного юзера.
- **`AccessControlService`** — проверки доступа (membership / roles / permissions).
  Кэшируется на время запроса (Scoped).
- **`CacheService`** — `IMemoryCache`, TTL 5/10 мин, явная инвалидация.
- **`HttpUrlBuilder`** — строит абсолютные URL из относительных путей через `HttpContext`.
- **`AppDateTime`** — Singleton-обёртка над `TimeProvider` для тестируемого времени.
- **`BaseController`** — базовый контроллер. Содержит `ExecuteAsync` (Result → HTTP),
  `GetCurrentUserId()` (из JWT claim), `IsCurrentUser(int)` (проверка SELF).
- **`BaseService<T>`** — базовый сервис. Содержит `SaveChangesAsync`,
  `FindEntityAsync`, `NormalizePagination`, `Paginate`.

---

## Архитектура — Desktop

- **`GlobalHubConnection`** — desktop singleton SignalR-клиент.
  Единая точка подписки на все real-time события.
- **`AuthManager`** — управляет жизненным циклом сессии:
  инициализация → refresh → logout. `_refreshLock` предотвращает параллельные рефреши.
- **`ApiClientService`** — HTTP-клиент с авто-retry при 401 (refresh token).
  Большие файлы (>10MB) сохраняются во временный файл.
- **`LocalCacheService`** — фасад над SQLite: upsert/get/search сообщений,
  чатов, пользователей.
- **`LocalDatabase`** — инициализация SQLite, схема v3, FTS5, PRAGMA.
- **`CacheMaintenanceService`** — фоновый сервис, выполняет `VACUUM` SQLite
  для освобождения дискового пространства.
- **`DialogService`** — стек модальных окон. SemaphoreSlim + Channel для
  упорядочивания показа/скрытия.
- **`NavigationService`** — переключение основных вкладок (`MainMenuViewModel`).
- **`AvatarHelper`** — нормализация URL аватаров + cachebuster (`?t={timestamp}`).
- **`AuthenticatedImageLoader`** — `AsyncImageLoader` с `Authorization` header
  для загрузки защищённых изображений.

---

## Паттерны и соглашения

- **`Result<T>` / Result Pattern** — возвращаемый тип бизнес-сервисов вместо исключений.
  Содержит: значение (при успехе) или тип ошибки + сообщение (при провале).
  `ResultErrorType` определяет HTTP-код ответа.
- **`ApiResponse<T>`** — единый конверт всех HTTP-ответов API.
  Поля: `success`, `data`, `message`, `error`, `details`, `timestamp`.
- **Soft delete** — логическое удаление записи через флаг `is_deleted = true`
  без физического удаления из БД. Применяется к сообщениям.
- **Partial Classes** — EF-модели разделены на два файла:
  основной (колонки + навигации) и `Partial.cs` (enum-свойства + `[NotMapped]`).
- **Cache-first** — стратегия загрузки на клиенте: сначала SQLite, затем
  фоновая ревалидация с сервером.
- **Gap-fill** — восстановление пропущенных сообщений после reconnect:
  батчами через `after/{newestId}` до актуального состояния.

---

## Read-state

- **Read receipt** — состояние прочтения в `ChatMember`:
  `last_read_message_id`, `last_read_at`.
- **Unread count** — количество непрочитанных сообщений.
  Считается на сервере, кэшируется в `GlobalHubConnection._unreadCounts`.
- **`FirstUnreadMessageId`** — ID первого непрочитанного сообщения.
  Используется для начальной позиции скролла при открытии чата.

---

## База данных

- **FTS5** — модуль полнотекстового поиска SQLite (Full-Text Search v5).
  Таблица `messages_fts` с триггерами INSERT/UPDATE/DELETE.
- **WAL** — Write-Ahead Logging, режим журналирования SQLite.
  Позволяет параллельное чтение во время записи.
- **`ChatSyncState`** — метаданные синхронизации чата в SQLite:
  `OldestLoadedId`, `NewestLoadedId`, `HasMoreOlder`, `HasMoreNewer`.
- **`family_id`** — идентификатор семейства RefreshToken.
  При обнаружении replay-атаки (повторное использование токена) —
  все токены семейства отзываются целиком.

---

## Токены и безопасность

- **JWT** — access-токен, короткоживущий. Содержит `userId`, `role`, `jti`.
- **RefreshToken** — долгоживущий токен ротации. В БД хранится только SHA-256 хеш.
- **`jti`** — уникальный идентификатор JWT (`jwt_id` в `RefreshToken`).
  Связывает access-токен с его RefreshToken.
- **Replay-атака** — повторное использование уже применённого RefreshToken.
  Защита: отзыв всего семейства (`family_id`).