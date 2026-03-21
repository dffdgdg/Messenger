# API Reference

## Общая информация
- **Base URL**: `{host}/api/{controller}`
- **Аутентификация**: JWT Bearer Token (заголовок `Authorization: Bearer {token}`)
- **Формат ответов**: `ApiResponse<T>` (см. DTO_CONTRACTS.md)
- **Rate Limiting**: Sliding Window (см. TECH_STACK.md)

## Маршрутизация
Все контроллеры наследуют `BaseController<T>` с атрибутом:
```
[ApiController]
[Route("api/[controller]")]
```

---

## AuthController (`/api/auth`)

| Метод | Endpoint | Auth | Rate Limit | Запрос | Ответ | Описание |
|-------|----------|------|------------|--------|-------|----------|
| POST | `/api/auth/login` | ❌ | `login` (5/мин) | `LoginRequest` | `AuthResponseDto` | Авторизация |
| POST | `/api/auth/refresh` | ❌ | — | `RefreshTokenRequest` | `TokenResponseDto` | Обновление токенов |
| POST | `/api/auth/revoke` | ✅ | — | — | — | Отзыв всех refresh-токенов (logout) |

---

## UsersController (`/api/users`)

| Метод | Endpoint | Auth | Rate Limit | Запрос | Ответ | Описание |
|-------|----------|------|------------|--------|-------|----------|
| GET | `/api/users` | ✅ | — | — | `List<UserDto>` | Все пользователи |
| GET | `/api/users/{id}` | ✅ | — | — | `UserDto` | Пользователь по ID |
| PUT | `/api/users/{id}` | ✅¹ | — | `UserDto` | — | Обновить профиль |
| POST | `/api/users/{id}/avatar` | ✅¹ | — | `IFormFile` | `AvatarResponseDto` | Загрузить аватар |
| PUT | `/api/users/{id}/username` | ✅¹ | — | `ChangeUsernameDto` | — | Изменить username |
| PUT | `/api/users/{id}/password` | ✅¹ | — | `ChangePasswordDto` | — | Изменить пароль |
| GET | `/api/users/online` | ✅ | — | — | `OnlineUsersResponseDto` | Список онлайн-пользователей |
| GET | `/api/users/{id}/status` | ✅ | — | — | `OnlineStatusDto` | Статус одного пользователя |
| POST | `/api/users/status/batch` | ✅ | — | `List<int>` | `List<OnlineStatusDto>` | Статусы нескольких пользователей |

> ¹ Только для своего профиля (`IsCurrentUser`)

---

## ChatsController (`/api/chats`)

### Получение чатов

| Метод | Endpoint | Auth | Запрос | Ответ | Описание |
|-------|----------|------|--------|-------|----------|
| GET | `/api/chats/user/{userId}` | ✅¹ | — | `List<ChatDto>` | Все чаты пользователя |
| GET | `/api/chats/user/{userId}/dialogs` | ✅¹ | — | `List<ChatDto>` | Только диалоги (Contact) |
| GET | `/api/chats/user/{userId}/groups` | ✅¹ | — | `List<ChatDto>` | Только групповые |
| GET | `/api/chats/user/{userId}/contact/{contactUserId}` | ✅¹ | — | `ChatDto` | Чат с конкретным пользователем |
| GET | `/api/chats/{chatId}` | ✅ | — | `ChatDto` | Чат по ID |

### Управление чатами

| Метод | Endpoint | Auth | Запрос | Ответ | Описание |
|-------|----------|------|--------|-------|----------|
| POST | `/api/chats` | ✅ | `ChatDto` | `ChatDto` | Создать чат |
| PUT | `/api/chats/{id}` | ✅ | `UpdateChatDto` | `ChatDto` | Обновить чат |
| DELETE | `/api/chats/{id}` | ✅ | — | — | Удалить чат |
| POST | `/api/chats/{id}/avatar` | ✅ | `IFormFile` | `string` (url) | Загрузить аватар чата |
| DELETE | `/api/chats/{id}/avatar` | ✅ | — | — | Удалить аватар чата |

### Участники чата

| Метод | Endpoint | Auth | Запрос | Ответ | Описание |
|-------|----------|------|--------|-------|----------|
| GET | `/api/chats/{chatId}/members` | ✅ | — | `List<UserDto>` | Участники (краткий) |
| GET | `/api/chats/{chatId}/members/detailed` | ✅ | — | `List<ChatMemberDto>` | Участники (детальный, с ролями) |
| POST | `/api/chats/{chatId}/members` | ✅ | `UpdateChatMemberDto` | `ChatMemberDto` | Добавить участника |
| DELETE | `/api/chats/{chatId}/members/{userId}` | ✅ | — | — | Удалить участника |
| PUT | `/api/chats/{chatId}/members/{userId}/role` | ✅ | `?role={ChatRole}` | `ChatMemberDto` | Изменить роль участника |

> ¹ Только для своих чатов (`IsCurrentUser`)

---

## MessagesController (`/api/messages`)

### CRUD

| Метод | Endpoint | Auth | Rate Limit | Запрос | Ответ | Описание |
|-------|----------|------|------------|--------|-------|----------|
| POST | `/api/messages` | ✅ | `messaging` (30/мин) | `CreateMessageRequest` | `MessageDto` | Отправить сообщение |
| PUT | `/api/messages/{id}` | ✅ | — | `UpdateMessageDto` | `MessageDto` | Редактировать |
| DELETE | `/api/messages/{id}` | ✅ | — | — | — | Удалить (soft delete) |

### Получение сообщений

| Метод | Endpoint | Auth | Параметры | Ответ | Описание |
|-------|----------|------|-----------|-------|----------|
| GET | `/api/messages/chat/{chatId}` | ✅ | `?page=1&pageSize=15` | `PagedMessagesDto` | Постраничная загрузка |
| GET | `/api/messages/chat/{chatId}/around/{messageId}` | ✅ | `?count=50` | `PagedMessagesDto` | Сообщения вокруг конкретного |
| GET | `/api/messages/chat/{chatId}/before/{messageId}` | ✅ | `?count=30` | `PagedMessagesDto` | Сообщения старше |
| GET | `/api/messages/chat/{chatId}/after/{messageId}` | ✅ | `?count=30` | `PagedMessagesDto` | Сообщения новее |

### Поиск

| Метод | Endpoint | Auth | Rate Limit | Параметры | Ответ | Описание |
|-------|----------|------|------------|-----------|-------|----------|
| GET | `/api/messages/chat/{chatId}/search` | ✅ | `search` (15/мин) | `?query=&page=1&pageSize=20` | `SearchMessagesResponseDto` | Поиск в чате |
| GET | `/api/messages/user/{userId}/search` | ✅¹ | `search` (15/мин) | `?query=&page=1&pageSize=20` | `GlobalSearchResponseDto` | Глобальный поиск |

### Транскрипция голосовых

| Метод | Endpoint | Auth | Запрос | Ответ | Описание |
|-------|----------|------|--------|-------|----------|
| GET | `/api/messages/{id}/transcription` | ✅ | — | `VoiceTranscriptionDto` | Получить транскрипцию |
| POST | `/api/messages/{id}/transcription/retry` | ✅ | — | — | Перезапустить транскрипцию |

> ¹ Только свой userId

---

## FilesController (`/api/files`)

| Метод | Endpoint | Auth | Rate Limit | Запрос | Ответ | Описание |
|-------|----------|------|------------|--------|-------|----------|
| POST | `/api/files/upload` | ✅ | — | `?chatId={id}` + `IFormFile` | `MessageFileDto` | Загрузить файл |

**Ограничения**: макс. размер файла — **100 MB** (`RequestSizeLimit`)

---

## PollsController (`/api/polls`)

| Метод | Endpoint | Auth | Запрос | Ответ | Описание |
|-------|----------|------|--------|-------|----------|
| GET | `/api/polls/{pollId}` | ✅ | — | `PollDto` | Получить опрос |
| POST | `/api/polls` | ✅ | `CreatePollDto` | `MessageDto` | Создать опрос (создаёт сообщение) |
| POST | `/api/polls/vote` | ✅ | `PollVoteDto` | `PollDto` | Проголосовать |

---

## ReadReceiptsController (`/api/readreceipts`)

| Метод | Endpoint | Auth | Запрос | Ответ | Описание |
|-------|----------|------|--------|-------|----------|
| POST | `/api/readreceipts/mark-read` | ✅ | `MarkAsReadDto` | `ReadReceiptResponseDto` | Отметить прочитанным |
| GET | `/api/readreceipts/chat/{chatId}/unread-count` | ✅ | — | `int` | Непрочитанных в чате |
| GET | `/api/readreceipts/unread-counts` | ✅ | — | `AllUnreadCountsDto` | Все счётчики непрочитанных |

---

## NotificationsController (`/api/notifications`)

| Метод | Endpoint | Auth | Запрос | Ответ | Описание |
|-------|----------|------|--------|-------|----------|
| GET | `/api/notifications/chat/{chatId}/settings` | ✅ | — | `ChatNotificationSettingsDto` | Настройки уведомлений чата |
| POST | `/api/notifications/chat/mute` | ✅ | `ChatNotificationSettingsDto` | `ChatNotificationSettingsDto` | Вкл/выкл уведомления чата |
| GET | `/api/notifications/settings` | ✅ | — | `List<ChatNotificationSettingsDto>` | Все настройки уведомлений |

---

## DepartmentsController (`/api/departments`)

### Чтение

| Метод | Endpoint | Auth | Ответ | Описание |
|-------|----------|------|-------|----------|
| GET | `/api/departments` | ✅ | `List<DepartmentDto>` | Все отделы |
| GET | `/api/departments/{id}` | ✅ | `DepartmentDto` | Отдел по ID |
| GET | `/api/departments/{id}/members` | ✅ | `List<UserDto>` | Сотрудники отдела |
| GET | `/api/departments/{id}/can-manage` | ✅ | `bool` | Может ли текущий пользователь управлять |

### Управление (Admin only)

| Метод | Endpoint | Auth | Запрос | Ответ | Описание |
|-------|----------|------|--------|-------|----------|
| POST | `/api/departments` | ✅ Admin | `DepartmentDto` | `DepartmentDto` | Создать отдел |
| PUT | `/api/departments/{id}` | ✅ Admin | `DepartmentDto` | — | Обновить отдел |
| DELETE | `/api/departments/{id}` | ✅ Admin | — | — | Удалить отдел |

### Управление составом

| Метод | Endpoint | Auth | Запрос | Ответ | Описание |
|-------|----------|------|--------|-------|----------|
| POST | `/api/departments/{id}/members` | ✅ | `UpdateDepartmentMemberDto` | — | Добавить в отдел |
| DELETE | `/api/departments/{id}/members/{userId}` | ✅ | — | — | Удалить из отдела |

---

## AdminController (`/api/admin`)

> Все эндпоинты требуют роли `Admin`

| Метод | Endpoint | Auth | Запрос | Ответ | Описание |
|-------|----------|------|--------|-------|----------|
| GET | `/api/admin/users` | ✅ Admin | — | `List<UserDto>` | Все пользователи |
| POST | `/api/admin/users` | ✅ Admin | `CreateUserDto` | `UserDto` | Создать пользователя |
| PUT | `/api/admin/users/{id}` | ✅ Admin | `UserDto` | `UserDto` | Обновить пользователя |
| POST | `/api/admin/users/{id}/toggle-ban` | ✅ Admin | — | — | Заблокировать/разблокировать |

---

## Статические эндпоинты (без контроллера)

| Метод | Endpoint | Auth | Описание |
|-------|----------|------|----------|
| GET | `/` | ❌ | Health check: "Messenger API is running" |
| GET | `/robots.txt` | ❌ | Robots exclusion |
| GET | `/sitemap.xml` | ❌ | Пустой sitemap |

Все с `Cache-Control: public, max-age=300`.