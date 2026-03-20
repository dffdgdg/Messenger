# Data Model

## ORM & Database
- **ORM**: Entity Framework Core 9.0.8
- **СУБД**: PostgreSQL (Npgsql.EntityFrameworkCore.PostgreSQL)
- **DbContext**: `MessengerDbContext`
- **Стратегия именования**: snake_case для таблиц и колонок
- **Timestamps**: `timestamp without time zone` (без часового пояса)
- **ID generation**: PostgreSQL sequences (`nextval`)

## PostgreSQL Enum Types
В БД зарегистрированы кастомные типы:
- `chat_role` → `ChatRole` (Member, Admin, Owner)
- `chat_type` → `ChatType` (Chat, Department, Contact, DepartmentHeads)
- `theme` → `Theme` (light, dark, system)
- `system_event_type` → `SystemEventType` (ChatCreated, MemberAdded, MemberRemoved, MemberLeft, RoleChanged)
- `transcription_status` → `TranscriptionStatus` (Pending, Processing, Done, Failed)

---

## Сущности

### User (`users`)
Пользователь системы.

| Колонка | Тип | Ограничения | Описание |
|---------|-----|-------------|----------|
| id | int | PK, auto-seq | Идентификатор |
| username | varchar(32) | UNIQUE, NOT NULL | Логин |
| name | varchar(50) | nullable | Имя |
| surname | varchar(50) | nullable | Фамилия |
| midname | varchar(50) | nullable | Отчество |
| password_hash | text | NOT NULL | BCrypt-хеш пароля |
| created_at | timestamp | default now() | Дата регистрации |
| last_online | timestamp | nullable | Последний раз онлайн |
| department_id | int | FK → departments, SET NULL | Отдел |
| avatar | text | nullable | Путь к аватару |
| is_banned | bool | default false | Заблокирован |

**Вычисляемое свойство** (NotMapped):
- `DisplayName` → склейка `Surname + Name + Midname`

**Примечание**: Роль пользователя (UserRole) **не хранится в таблице users** в текущей модели EF. Роль определяется через enum `UserRole` (User, Head, Admin), но колонка в модели отсутствует — вероятно, хранится через другой механизм или TODO.

**Связи**:
- `ChatMembers` — участие в чатах (1:N)
- `Chats` — созданные чаты (1:N, через CreatedById)
- `Department` — принадлежность к отделу (N:1)
- `Departments` — руководство отделами (1:N, через HeadId)
- `Messages` — отправленные сообщения (1:N)
- `PollVotes` — голоса в опросах (1:N)
- `UserSetting` — настройки (1:1)
- `RefreshTokens` — refresh-токены (1:N)

---

### Chat (`chats`)
Чат (личный, групповой, департаментский).

| Колонка | Тип | Ограничения | Описание |
|---------|-----|-------------|----------|
| id | int | PK, auto-seq | Идентификатор |
| name | varchar(100) | nullable | Название чата |
| type | chat_type | NOT NULL | Тип: Chat, Department, Contact, DepartmentHeads |
| created_at | timestamp | default now() | Дата создания |
| created_by_id | int | FK → users, CASCADE | Создатель |
| last_message_time | timestamp | nullable | Время последнего сообщения |
| avatar | text | nullable | Путь к аватару чата |

**Связи**:
- `ChatMembers` — участники (1:N)
- `CreatedBy` — создатель (N:1 → User)
- `Department` — привязанный отдел (1:1, опционально)
- `Messages` — сообщения (1:N)

---

### ChatMember (`chat_members`)
Участник чата. Составной первичный ключ.

| Колонка | Тип | Ограничения | Описание |
|---------|-----|-------------|----------|
| chat_id | int | PK, FK → chats | Чат |
| user_id | int | PK, FK → users | Пользователь |
| role | chat_role | NOT NULL | Роль: Member, Admin, Owner |
| joined_at | timestamp | default now() | Дата вступления |
| notifications_enabled | bool | default true | Уведомления включены |
| last_read_message_id | int | FK → messages, SET NULL | Последнее прочитанное сообщение |
| last_read_at | timestamp | nullable | Время последнего прочтения |

**Индексы**:
- `UQ_Chat_User` — уникальная пара (chat_id, user_id)
- `idx_chat_members_last_read_message_id`
- `idx_chat_members_user_id`

---

### Message (`messages`)
Сообщение в чате.

| Колонка | Тип | Ограничения | Описание |
|---------|-----|-------------|----------|
| id | int | PK, auto-seq | Идентификатор |
| chat_id | int | FK → chats | Чат |
| sender_id | int | FK → users | Отправитель |
| content | text | nullable | Текст сообщения |
| created_at | timestamp | default now() | Дата создания |
| edited_at | timestamp | nullable | Дата редактирования |
| is_deleted | bool | default false | Удалено (soft delete) |
| reply_to_message_id | int | FK → messages, SET NULL | Ответ на сообщение |
| forwarded_from_message_id | int | FK → messages, SET NULL | Пересылка из сообщения |
| is_system_message | bool | default false | Системное сообщение |
| system_event_type | system_event_type | nullable | Тип системного события |
| target_user_id | int | FK → users, SET NULL | Целевой пользователь (для системных) |

**Вычисляемое свойство** (NotMapped):
- `IsVoiceMessage` → `VoiceMessage != null`

**Индексы**:
- `idx_messages_chatid_createdat` — основной для пагинации
- `idx_messages_reply_to_message_id`
- `idx_messages_forwarded_from_message_id`
- `idx_messages_target_user_id`

**Связи**:
- `Chat` — чат (N:1)
- `Sender` — отправитель (N:1 → User)
- `TargetUser` — целевой пользователь системного события (N:1 → User)
- `ReplyToMessage` — оригинальное сообщение для ответа (N:1 → Message, self-ref)
- `ForwardedFromMessage` — оригинальное сообщение для пересылки (N:1 → Message, self-ref)
- `VoiceMessage` — голосовое сообщение (1:0..1)
- `MessageFiles` — прикреплённые файлы (1:N)
- `Polls` — опросы (1:N)
- `ChatMembers` — прочтения (через LastReadMessageId)

---

### MessageFile (`message_files`)
Файл, прикреплённый к сообщению.

| Колонка | Тип | Ограничения | Описание |
|---------|-----|-------------|----------|
| id | int | PK, auto-seq | Идентификатор |
| message_id | int | FK → messages | Сообщение |
| file_name | varchar(255) | NOT NULL | Имя файла |
| content_type | varchar(100) | NOT NULL | MIME-тип |
| path | text | nullable | Путь к файлу на сервере |

---

### VoiceMessage (`voice_messages`)
Голосовое сообщение. Связано 1:1 с Message (PK = FK).

| Колонка | Тип | Ограничения | Описание |
|---------|-----|-------------|----------|
| message_id | int | PK, FK → messages, CASCADE | Сообщение |
| duration_seconds | double | NOT NULL | Длительность в секундах |
| transcription_status | transcription_status | default Pending | Статус транскрипции |
| transcription_text | text | nullable | Результат транскрипции |
| file_path | text | NOT NULL | Путь к аудиофайлу |
| file_name | varchar(255) | NOT NULL | Имя файла |
| content_type | varchar(100) | default "audio/wav" | MIME-тип |
| file_size | bigint | NOT NULL | Размер файла в байтах |

---

### Poll (`polls`)
Опрос, прикреплённый к сообщению.

| Колонка | Тип | Ограничения | Описание |
|---------|-----|-------------|----------|
| id | int | PK, auto-seq | Идентификатор |
| message_id | int | FK → messages | Сообщение |
| is_anonymous | bool | default true | Анонимное голосование |
| allows_multiple_answers | bool | default false | Множественный выбор |
| closes_at | timestamp | nullable | Дата закрытия опроса |

**Индексы**: `idx_polls_message_id`

**Связи**:
- `Message` — сообщение (N:1)
- `PollOptions` — варианты ответов (1:N)
- `PollVotes` — голоса (1:N)

---

### PollOption (`poll_options`)
Вариант ответа в опросе.

| Колонка | Тип | Ограничения | Описание |
|---------|-----|-------------|----------|
| id | int | PK, auto-seq | Идентификатор |
| poll_id | int | FK → polls | Опрос |
| option_text | varchar(50) | NOT NULL | Текст варианта |
| position | int | NOT NULL | Порядковый номер |

**Связи**:
- `Poll` — опрос (N:1)
- `PollVotes` — голоса за этот вариант (1:N)

---

### PollVote (`poll_votes`)
Голос пользователя в опросе.

| Колонка | Тип | Ограничения | Описание |
|---------|-----|-------------|----------|
| id | int | PK, auto-seq | Идентификатор |
| poll_id | int | FK → polls | Опрос |
| option_id | int | FK → poll_options | Выбранный вариант |
| user_id | int | FK → users | Голосующий |
| voted_at | timestamp | default now() | Время голосования |

**Индексы**:
- `UQ_Poll_User_Option_Vote` — уникальная тройка (poll_id, user_id, option_id)
- `idx_poll_votes_user_id`

---

### Department (`departments`)
Подразделение организации. Иерархическая структура (self-referencing).

| Колонка | Тип | Ограничения | Описание |
|---------|-----|-------------|----------|
| id | int | PK, auto-seq | Идентификатор |
| name | varchar(100) | NOT NULL | Название |
| parent_department_id | int | FK → departments, SET NULL | Родительский отдел |
| chat_id | int | FK → chats, UNIQUE, SET NULL | Привязанный чат |
| head_id | int | FK → users, SET NULL | Руководитель |

**Индексы**: `idx_departments_head_id`

**Связи**:
- `Chat` — привязанный чат отдела (1:1)
- `Head` — руководитель (N:1 → User)
- `ParentDepartment` — родительский отдел (N:1 → Department, self-ref)
- `InverseParentDepartment` — дочерние отделы (1:N)
- `Users` — сотрудники отдела (1:N)

---

### RefreshToken (`refresh_tokens`)
Refresh-токен для ротации JWT. Реализует семейство ротации (rotation family) для обнаружения replay-атак.

| Колонка | Тип | Ограничения | Описание |
|---------|-----|-------------|----------|
| id | int | PK, auto-seq | Идентификатор |
| user_id | int | FK → users, CASCADE | Пользователь |
| token_hash | varchar(128) | NOT NULL | SHA-256 хеш токена |
| jwt_id | varchar(64) | NOT NULL | Jti access-токена |
| created_at | timestamp | NOT NULL | Дата создания |
| expires_at | timestamp | NOT NULL | Дата истечения |
| used_at | timestamp | nullable | Null = активен |
| revoked_at | timestamp | nullable | Null = не отозван |
| replaced_by_token_id | int | FK → refresh_tokens, SET NULL | Следующий токен в цепочке |
| family_id | varchar(64) | NOT NULL | ID семейства ротации |

**Вычисляемое свойство**:
- `IsActive` → `UsedAt == null && RevokedAt == null && ExpiresAt > UtcNow`

**Индексы**:
- `idx_refresh_tokens_token_hash`
- `idx_refresh_tokens_user_id`
- `idx_refresh_tokens_family_id`
- `idx_refresh_tokens_expires_at`

**Безопасность**: Сам токен **не хранится** — только SHA-256 хеш. При обнаружении повторного использования вся семья (`family_id`) отзывается.

---

### UserSetting (`user_settings`)
Настройки пользователя. Связь 1:1 с User (PK = FK).

| Колонка | Тип | Ограничения | Описание |
|---------|-----|-------------|----------|
| user_id | int | PK, FK → users | Пользователь |
| theme | theme | nullable | Тема: light, dark, system |
| notifications_enabled | bool | default true | Глобальное вкл/выкл уведомлений |

---

### SystemSetting (`system_settings`)
Системные настройки (key-value).

| Колонка | Тип | Ограничения | Описание |
|---------|-----|-------------|----------|
| key | varchar(50) | PK | Ключ настройки |
| value | text | NOT NULL | Значение |

---

## Диаграмма связей (ERD)

```
┌─────────────┐     1:N      ┌──────────────┐     N:1      ┌─────────────┐
│    User      │─────────────│  ChatMember   │─────────────│    Chat      │
│              │              │  (PK: chat_id │              │             │
│  id          │              │   + user_id)  │              │  id         │
│  username    │              │  role         │              │  name       │
│  name        │              │  last_read_   │              │  type       │
│  surname     │              │   message_id  │              │  avatar     │
│  midname     │              │  notifications│              │             │
│  avatar      │              │   _enabled    │              │             │
│  is_banned   │              └───────────────┘              └──────┬──────┘
│  department_ │                                                    │
│   id         │                                               1:1 (opt)
│              │                                                    │
│              │  1:1    ┌──────────────┐                   ┌───────┴──────┐
│              │────────│  UserSetting  │                   │  Department  │
│              │         │  theme       │                   │  name        │
│              │         │  notif_on    │                   │  parent_id   │
│              │         └──────────────┘                   │  head_id     │
│              │                                            │  chat_id     │
│              │  1:N    ┌──────────────┐                   └──────────────┘
│              │────────│ RefreshToken  │                     ↑ self-ref
│              │         │ token_hash   │                     │ (parent ↔ children)
│              │         │ family_id    │
│              │         │ expires_at   │
│              │         └──────────────┘
└──────┬───────┘
       │ 1:N
       │
┌──────┴───────┐    1:N     ┌──────────────┐    1:N    ┌──────────────┐
│   Message    │───────────│  MessageFile  │          │    Poll       │
│              │            │  file_name    │          │  is_anonymous │
│  id          │            │  content_type │          │  allows_multi │
│  chat_id     │            │  path         │          │  closes_at    │
│  sender_id   │            └───────────────┘          └──────┬───────┘
│  content     │                                              │ 1:N
│  is_deleted  │    1:0..1  ┌──────────────┐          ┌───────┴──────┐
│  reply_to_   │───────────│ VoiceMessage  │          │  PollOption  │
│   message_id │            │ duration_sec  │          │  option_text │
│  forwarded_  │            │ transcription │          │  position    │
│   from_msg_id│            │  _status      │          └──────┬───────┘
│  is_system   │            │ transcription │                 │ 1:N
│  system_event│            │  _text        │          ┌──────┴───────┐
│   _type      │            │ file_path     │          │  PollVote    │
│  target_user │            │ file_size     │          │  user_id     │
│   _id        │            └───────────────┘          │  voted_at    │
└──────────────┘                                       └──────────────┘
  ↑ self-ref:
  │ reply_to_message_id
  │ forwarded_from_message_id
```

## Особенности модели

### Partial Classes
Модели `User`, `Chat`, `ChatMember`, `UserSetting` используют **partial classes** для разделения:
- Основной файл — свойства из БД
- `Partial.cs` — enum-свойства (`Type`, `Role`, `Theme`) и вычисляемые свойства (`DisplayName`)

### Soft Delete
Сообщения используют soft delete (`is_deleted = true`), не удаляются из БД.

### Каскадные удаления
- `User → Chat` (CreatedBy): CASCADE
- `User → RefreshToken`: CASCADE
- `Message → VoiceMessage`: CASCADE
- Остальные FK: SET NULL или стандартное поведение

### Self-referencing
- `Message` → `Message` (reply, forward)
- `Department` → `Department` (parent-child иерархия)
- `RefreshToken` → `RefreshToken` (цепочка ротации)