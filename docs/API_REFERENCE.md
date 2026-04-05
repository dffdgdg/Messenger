# API Reference (Agent-optimized)

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
Запрос: `{ Username, Password }`
Ответ: `{ User: UserDto, AccessToken, RefreshToken }`

### POST `/refresh` — обновить токены
Запрос: `{ RefreshToken }`
Ответ: `{ AccessToken, RefreshToken }`

### POST `/revoke` [Auth] — logout (отзыв всей сессии)
Тело: нет. Ответ: нет.

---

## USERS `/api/users`

> Эндпоинты сометкой [SELF] доступны только для своего UserId

| Метод | Путь | Auth | Описание |
|-------|------|------|----------|
| GET | `/` | ✅ | Все пользователи → `List<UserDto>` |
| GET | `/{id}` | ✅ | Пользователь по ID → `UserDto` |
| PUT | `/{id}` | ✅ SELF | Обновить профиль, тело: `UserDto` |
| POST | `/{id}/avatar` | ✅ SELF | Загрузить аватар (`multipart`) → `AvatarResponseDto` |
| PUT | `/{id}/username` | ✅ SELF | Тело: `{ NewUsername }` |
| PUT | `/{id}/password` | ✅ SELF | Тело: `{ OldPassword, NewPassword }` |
| GET | `/online` | ✅ | Онлайн-пользователи → `OnlineUsersResponseDto` |
| GET | `/{id}/status` | ✅ | Статус → `{ UserId, IsOnline, LastSeen }` |
| POST | `/status/batch` | ✅ | Тело: `[userId1, userId2]` → `List<OnlineStatusDto>` |

**UserDto**: `{ Id, Username, DisplayName, AvatarUrl, Role, DepartmentId }`

---

## CHATS `/api/chats`

> [SELF] = только для чатов текущего пользователя

**ChatType**: `Contact` (личный) | `Group` | `Department`
**ChatRole**: `Member` | `Admin` | `Owner`

### Получение
| GET | `/user/{userId}` | [SELF] | Все чаты |
| GET | `/user/{userId}/dialogs` | [SELF] | Только личные (Contact) |
| GET | `/user/{userId}/groups` | [SELF] | Только группы |
| GET | `/user/{userId}/contact/{contactUserId}` | [SELF] | Диалог с конкретным юзером |
| GET | `/{chatId}` | ✅ | Чат по ID |

### Управление чатом
| POST | `/` | тело: `ChatDto` → `ChatDto` | Создать |
| PUT | `/{id}` | тело: `UpdateChatDto` → `ChatDto` | Переименовать |
| DELETE | `/{id}` | — | Удалить |
| POST | `/{id}/avatar` | `multipart` → `string (url)` | Загрузить аватар |
| DELETE | `/{id}/avatar` | — | Удалить аватар |

### Участники
| GET | `/{chatId}/members` | → `List<UserDto>` | Базовый список |
| GET | `/{chatId}/members/detailed` | → `List<ChatMemberDto>` | С ролями и датой |
| POST | `/{chatId}/members` | тело: `UpdateChatMemberDto` → `ChatMemberDto` | Добавить |
| DELETE | `/{chatId}/members/{userId}` | — | Удалить |
| PUT | `/{chatId}/members/{userId}/role` | query: `?role=Admin` → `ChatMemberDto` | Изменить роль |

**ChatDto**: `{ Id, Name, ChatType, AvatarUrl, LastMessage, CreatedAt }`
**ChatMemberDto**: `{ UserId, User, Role, JoinedAt }`

---

## MESSAGES `/api/messages`

### Отправка и редактирование
| POST | `/` | Rate: messaging | `CreateMessageRequest` → `MessageDto` |
| PUT | `/{id}` | | `UpdateMessageDto` → `MessageDto` (только своё) |
| DELETE | `/{id}` | | Soft delete (только своё) |

**CreateMessageRequest**:
```json
{
  "ChatId": 1,
  "Content": "текст",
  "ReplyToMessageId": null,
  "ForwardFromMessageId": null,
  "FileIds": []
}
```

### Загрузка (пагинация)
| GET | `/chat/{chatId}` | `?page=1&pageSize=15` | Страница (новые→старые) |
| GET | `/chat/{chatId}/around/{messageId}` | `?count=50` | Вокруг сообщения |
| GET | `/chat/{chatId}/before/{messageId}` | `?count=30` | Старше (scroll вверх) |
| GET | `/chat/{chatId}/after/{messageId}` | `?count=30` | Новее (догрузка) |

Все → `PagedMessagesDto`: `{ Messages, TotalCount, HasMore }`

### Поиск (Rate: search)
| GET | `/chat/{chatId}/search?query=текст&page=1&pageSize=20` | → `SearchMessagesResponseDto` |
| GET | `/user/{userId}/search?query=текст&page=1&pageSize=20` | [SELF] → `GlobalSearchResponseDto` |

**MessageDto**: `{ Id, ChatId, SenderId, Content, MessageType, ReplyToId, Files, Poll, IsEdited, CreatedAt }`

---

## FILES `/api/files`

### POST `/upload` [Auth, Rate: upload]
- Content-Type: `multipart/form-data`
- Query: `?chatId={id}`
- Макс. размер: **100 MB**
- Ответ: `MessageFileDto`: `{ Id, FileName, FileUrl, FileSize, MimeType }`

> ⚠️ Типичный flow: сначала загрузить файл → получить `Id` → передать в `FileIds[]` при создании сообщения

---

## POLLS `/api/polls`

| POST | `/` | Создать опрос (возвращает `MessageDto`) |
| GET | `/{pollId}` | Опрос с результатами → `PollDto` |
| POST | `/vote` | Проголосовать |

**CreatePollDto**: `{ ChatId, Question, Options: string[], IsAnonymous, IsMultipleChoice }`
**PollVoteDto**: `{ PollId, OptionIds: int[] }`
**PollDto**: `{ Id, Question, Options, TotalVotes, UserVotes }`

---

## READ RECEIPTS `/api/readreceipts`

| POST | `/mark-read` | `{ ChatId, LastReadMessageId }` → `ReadReceiptResponseDto` |
| GET | `/chat/{chatId}/unread-count` | → `int` |
| GET | `/unread-counts` | → `{ Counts: { [chatId]: number } }` |

---

## NOTIFICATIONS `/api/notifications`

| GET | `/chat/{chatId}/settings` | → `ChatNotificationSettingsDto` |
| POST | `/chat/mute` | `{ ChatId, IsMuted, MutedUntil? }` → обновлённые настройки |
| GET | `/settings` | → все настройки текущего юзера |

---

## DEPARTMENTS `/api/departments`

| GET | `/` | Все отделы → `List<DepartmentDto>` |
| GET | `/{id}` | Отдел → `DepartmentDto` |
| GET | `/{id}/members` | Сотрудники → `List<UserDto>` |
| GET | `/{id}/can-manage` | Есть ли права → `bool` |
| POST | `/` | [Admin] Создать отдел |
| PUT | `/{id}` | [Admin] Обновить |
| DELETE | `/{id}` | [Admin] Удалить |
| POST | `/{id}/members` | Добавить: `{ UserId, Role }` |
| DELETE | `/{id}/members/{userId}` | Удалить участника |

**DepartmentDto**: `{ Id, Name, Description, HeadUserId, ParentDepartmentId }`

---

## ADMIN `/api/admin`

> ⚠️ Все эндпоинты только для роли `Admin`

| GET | `/users` | Все пользователи (включая забаненных) |
| POST | `/users` | Создать: `{ Username, Password, DisplayName, Role, DepartmentId }` → `UserDto` |
| PUT | `/users/{id}` | Обновить любого → `UserDto` |
| POST | `/users/{id}/toggle-ban` | Бан/разбан |