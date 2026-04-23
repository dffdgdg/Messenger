# API Reference.md

## Базовые правила
- Base URL: `{host}/api/{controller}`
- Auth: заголовок `Authorization: Bearer {token}` (везде где Auth: ✅)
- Все тела запросов/ответов: `application/json`
- Все ответы обёрнуты: `ApiResponse<T>`
- Файловые эндпоинты: `multipart/form-data`

## Rate Limits (при 429 — отступить и повторить)
- Глобально: 100 req/10s (по UserId или IP)
- `/api/auth/login` → 5/мин
- `/api/files/upload` → 10/мин
- Поиск (`/search`) → 15/мин
- `POST /api/messages` → 30/мин

## Коды ответов
200 OK | 201 Created | 400 Bad Request | 401 Unauthorized
403 Forbidden | 404 Not Found | 429 Rate Limited | 500 Server Error

---

## AUTH `/api/auth`

### POST `/login` — получить токены
Запрос: `{ username, password }`
Ответ: `AuthResponseDto` → `{ id, username, displayName, token, refreshToken, role }`

### POST `/refresh` — обновить токены
Запрос: `{ accessToken, refreshToken }`
Ответ: `TokenResponseDto` → `{ token, refreshToken, userId, role }`

### POST `/revoke` [Auth] — logout (отзыв всей сессии)
Тело: нет. Ответ: нет.

---

## USERS `/api/users`

> [SELF] — доступен только для своего UserId

| Метод | Путь | Auth | Описание |
|-------|------|------|----------|
| GET | `/` | ✅ | Все пользователи → `List<UserDto>` |
| GET | `/{id}` | ✅ | Пользователь по ID → `UserDto` |
| PUT | `/{id}` | ✅ SELF | Обновить профиль, тело: `UserDto` |
| POST | `/{id}/avatar` | ✅ SELF | Загрузить аватар (`multipart`) → `AvatarResponseDto` |
| PUT | `/{id}/username` | ✅ SELF | Тело: `ChangeUsernameDto` → `{ newUsername }` |
| PUT | `/{id}/password` | ✅ SELF | Тело: `ChangePasswordDto` → `{ currentPassword, newPassword }` |
| GET | `/online` | ✅ | Онлайн-пользователи → `OnlineUsersResponseDto` |
| GET | `/{id}/status` | ✅ | Статус → `OnlineStatusDto` |
| POST | `/status/batch` | ✅ | Тело: `[userId1, userId2]` → `List<OnlineStatusDto>` |

**UserDto**: `{ id, username, displayName, name, surname, midname, department, departmentId, avatar, isOnline, isBanned, lastOnline, theme, notificationsEnabled, soundsEnabled }`

---

## CHATS `/api/chats`

> [SELF] = только для чатов текущего пользователя

**ChatType**: `Chat` (группа) | `Contact` (личный) | `Department` (отдел) | `DepartmentHeads` (руководители)
**ChatRole**: `Member` | `Admin` | `Owner`

### Получение
| Метод | Путь | Auth | Описание |
|-------|------|------|----------|
| GET | `/user/{userId}` | ✅ SELF | Все чаты → `List<ChatDto>` |
| GET | `/user/{userId}/dialogs` | ✅ SELF | Только Contact → `List<ChatDto>` |
| GET | `/user/{userId}/groups` | ✅ SELF | Только Chat/Department → `List<ChatDto>` |
| GET | `/user/{userId}/contact/{contactUserId}` | ✅ SELF | Диалог с конкретным юзером → `ChatDto` |
| GET | `/{chatId}` | ✅ | Чат по ID → `ChatDto` |

### Управление чатом
| Метод | Путь | Тело / Ответ | Описание |
|-------|------|--------------|----------|
| POST | `/` | `ChatDto` → `ChatDto` | Создать |
| PUT | `/{id}` | `UpdateChatDto` → `ChatDto` | Переименовать |
| DELETE | `/{id}` | — | Удалить |
| POST | `/{id}/avatar` | `multipart` → `string (url)` | Загрузить аватар |
| DELETE | `/{id}/avatar` | — | Удалить аватар |

### Участники
| Метод | Путь | Ответ | Описание |
|-------|------|-------|----------|
| GET | `/{chatId}/members` | `List<UserDto>` | Базовый список |
| GET | `/{chatId}/members/detailed` | `List<ChatMemberDto>` | С ролями и датой |
| POST | `/{chatId}/members` | `UpdateChatMemberDto` → `ChatMemberDto` | Добавить |
| DELETE | `/{chatId}/members/{userId}` | — | Удалить |
| PUT | `/{chatId}/members/{userId}/role` | query: `?role=Admin` → `ChatMemberDto` | Изменить роль |

**ChatDto**: `{ id, name, type, createdById, lastMessageDate, avatar, lastMessagePreview, lastMessageSenderName, unreadCount }`
**ChatMemberDto**: `{ chatId, userId, role, joinedAt, notificationsEnabled, username, displayName, avatar }`

---

## MESSAGES `/api/messages`

### Отправка и редактирование

| Метод | Путь | Rate | Описание |
|-------|------|------|----------|
| POST | `/` | `messaging` | `CreateMessageRequest` → `MessageDto` |
| PUT | `/{id}` | — | `UpdateMessageDto` → `MessageDto` (только своё) |
| DELETE | `/{id}` | — | Soft delete (только автор, Admin или Owner чата) |
| POST | `/{id}/pin` | — | Закрепить → `MessageDto` |
| DELETE | `/{id}/pin` | — | Открепить → `MessageDto` |
| GET | `/chat/{chatId}/pinned` | — | Закреплённые → `List<MessageDto>` |

> ⚠️ `PUT /{id}`: если `id` в URL не совпадает с `UpdateMessageDto.Id` — вернёт 400.

**CreateMessageRequest**:
```json
{
  "chatId": 1,
  "content": "текст",
  "replyToMessageId": null,
  "forwardedFromMessageId": null,
  "isVoiceMessage": false,
  "voiceFileUrl": null,
  "voiceFileName": null,
  "voiceContentType": null,
  "voiceFileSize": null,
  "voiceDurationSeconds": null,
  "files": []
}
```

### Загрузка (пагинация)

| Метод | Путь | Query | Ответ |
|-------|------|-------|-------|
| GET | `/chat/{chatId}` | `?page=1&pageSize=15` | Страница (новые→старые) |
| GET | `/chat/{chatId}/around/{messageId}` | `?count=50` | Вокруг сообщения |
| GET | `/chat/{chatId}/before/{messageId}` | `?count=30` | Старше (scroll вверх) |
| GET | `/chat/{chatId}/after/{messageId}` | `?count=30` | Новее (догрузка) |

Все → `PagedMessagesDto`: `{ messages, totalCount, hasMoreMessages, hasNewerMessages, currentPage }`

### Поиск в чате (Rate: search)

```
GET /chat/{chatId}/search
```

| Query-параметр | Тип | Default | Описание |
|----------------|-----|---------|----------|
| `query` | string | `""` | Текст поиска |
| `page` | int | 1 | Номер страницы |
| `pageSize` | int | 20 | Размер страницы |
| `senderId` | int? | null | Фильтр по отправителю |
| `hasFiles` | bool? | null | Только с файлами |
| `hasVoice` | bool? | null | Только с голосовыми |
| `hasPoll` | bool? | null | Только с опросами |
| `onlyText` | bool? | null | Только текстовые |
| `dateFrom` | DateTime? | null | Начало диапазона |
| `dateTo` | DateTime? | null | Конец диапазона |
| `oldestFirst` | bool | false | Порядок (старые→новые) |

Ответ: `SearchMessagesResponseDto`

> Query-модель backend: `SearchMessagesQueryDto` (все параметры из таблицы выше).

### Глобальный поиск (Rate: search)

```
GET /user/{userId}/search
```
> [SELF] — только для своего userId. Поиск по всем доступным чатам пользователя.

| Query-параметр | Тип | Default | Описание |
|----------------|-----|---------|----------|
| `query` | string | `""` | Текст поиска |
| `page` | int | 1 | Номер страницы |
| `pageSize` | int | 20 | Размер страницы |
| `senderId` | int? | null | Фильтр по отправителю |
| `filterChatId` | int? | null | Ограничить конкретным чатом |
| `hasFiles` | bool? | null | Только с файлами |
| `hasVoice` | bool? | null | Только с голосовыми |
| `hasPoll` | bool? | null | Только с опросами |
| `onlyText` | bool? | null | Только текстовые |
| `dateFrom` | DateTime? | null | Начало диапазона |
| `dateTo` | DateTime? | null | Конец диапазона |
| `oldestFirst` | bool | false | Порядок (старые→новые) |

Ответ: `GlobalSearchResponseDto`

> Query-модель backend: `GlobalSearchQueryDto` (наследует `SearchMessagesQueryDto` и добавляет `filterChatId`).

**MessageDto**: `{ id, chatId, senderId, senderName, senderAvatarUrl, content, createdAt, isOwn, isPrevSameSender, editedAt, isEdited, isDeleted, isPinned, pinnedAt, pinnedByUserId, replyToMessageId, replyToMessage, forwardedFromMessageId, forwardedFrom, isSystemMessage, systemEventType, targetUserId, targetUserName, isVoiceMessage, voiceDurationSeconds, voiceFileUrl, voiceFileName, voiceContentType, voiceFileSize, poll, files }`

---

## FILES `/api/files`

### POST `/upload` [Auth, Rate: upload]
- Content-Type: `multipart/form-data`
- Query: `?chatId={id}`
- Макс. размер: **100 MB**
- Ответ: `MessageFileDto` → `{ id, messageId, fileName, contentType, url, previewType, fileSize }`

> ⚠️ Типичный flow: загрузить файл → получить `id` → передать в `files[]` при создании сообщения.

---

## POLLS `/api/polls`

| Метод | Путь | Описание |
|-------|------|----------|
| POST | `/` | Создать опрос, тело: `CreatePollDto` → `MessageDto` |
| GET | `/{pollId}` | Опрос с результатами → `PollDto` |
| POST | `/vote` | Проголосовать, тело: `PollVoteDto` |

**CreatePollDto**: `{ chatId, question, isAnonymous, allowsMultipleAnswers, closesAt?, options: [{ text, position }] }`
**PollVoteDto**: `{ pollId, optionIds: int[], userId }` (userId перезаписывается сервером)
**PollDto**: `{ id, messageId, isAnonymous, allowsMultipleAnswers, closesAt, options, selectedOptionIds, canVote }`

---

## READ RECEIPTS `/api/readreceipts`

| Метод | Путь | Тело / Ответ | Описание |
|-------|------|--------------|----------|
| POST | `/mark-read` | `MarkAsReadDto` → `ReadReceiptResponseDto` | Отметить прочитанным |
| GET | `/chat/{chatId}/unread-count` | → `int` | Непрочитанных в чате |
| GET | `/unread-counts` | → `AllUnreadCountsDto` | Все счётчики |

**MarkAsReadDto**: `{ chatId, messageId }` (`messageId = null` → весь чат)

---

## NOTIFICATIONS `/api/notifications`

| Метод | Путь | Тело / Ответ | Описание |
|-------|------|--------------|----------|
| GET | `/chat/{chatId}/settings` | → `ChatNotificationSettingsDto` | Настройки чата |
| POST | `/chat/mute` | `{ chatId, isMuted }` → `ChatNotificationSettingsDto` | Mute/unmute |
| GET | `/settings` | → все настройки текущего юзера | — |

---

## DEPARTMENTS `/api/departments`

| Метод | Путь | Auth | Описание |
|-------|------|------|----------|
| GET | `/` | ✅ | Все отделы → `List<DepartmentDto>` |
| GET | `/{id}` | ✅ | Отдел → `DepartmentDto` |
| GET | `/{id}/members` | ✅ | Сотрудники → `List<UserDto>` |
| GET | `/{id}/can-manage` | ✅ | Есть ли права → `bool` |
| POST | `/` | Admin | Создать, тело: `DepartmentDto` |
| PUT | `/{id}` | Admin | Обновить, тело: `DepartmentDto` |
| DELETE | `/{id}` | Admin | Удалить |
| POST | `/{id}/members` | ✅ | Добавить: `UpdateDepartmentMemberDto` → `{ userId }` |
| DELETE | `/{id}/members/{userId}` | ✅ | Удалить участника |

**DepartmentDto**: `{ id, name, parentDepartmentId, head, headName, userCount }`

---

## ADMIN `/api/admin`

> ⚠️ Все эндпоинты только для роли `Admin`

| Метод | Путь | Тело / Ответ | Описание |
|-------|------|--------------|----------|
| GET | `/users` | → `List<UserDto>` | Все пользователи (включая забаненных) |
| POST | `/users` | `CreateUserDto` → `UserDto` | Создать пользователя |
| PUT | `/users/{id}` | `UserDto` → `UserDto` | Обновить любого |
| POST | `/users/{id}/toggle-ban` | — | Бан/разбан |
| POST | `/users/{id}/reset-password` | `ResetPasswordAdminDto` → `{ newPassword }` | Сброс пароля |

**CreateUserDto**: `{ username, password, surname, name, midname?, departmentId? }`
```