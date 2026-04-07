# Data Model

## ORM & Database
- **ORM**: Entity Framework Core 10
- **СУБД**: PostgreSQL (Npgsql.EntityFrameworkCore.PostgreSQL)
- **DbContext**: `MessengerDbContext`
- **Именование**: snake_case для таблиц и колонок
- **Timestamps**: `timestamp without time zone`
- **ID**: PostgreSQL sequences (`nextval`)

## PostgreSQL Enum Types
| PG тип | C# enum | Значения |
|--------|---------|---------|
| `chat_role` | `ChatRole` | Member, Admin, Owner |
| `chat_type` | `ChatType` | Chat, Department, Contact, DepartmentHeads |
| `theme` | `Theme` | light, dark, system |
| `system_event_type` | `SystemEventType` | ChatCreated, MemberAdded, MemberRemoved, MemberLeft, RoleChanged |

---
## EF Migrations (workflow)
1. Установить/обновить инструмент:
   - `dotnet tool update --global dotnet-ef`
2. Создать миграцию из корня репозитория:
   - `dotnet ef migrations add InitialSchema --project MessengerAPI --startup-project MessengerAPI`
3. Применить миграцию локально:
   - `dotnet ef database update --project MessengerAPI --startup-project MessengerAPI`
4. В Docker миграции применятся автоматически при старте API (`Database.MigrateAsync()`).

> Порядок важен: сначала `migrations add`, потом `database update`.
> Если сначала выполнить `database update`, EF покажет `No migrations were applied`, что нормально для текущего состояния БД.

> Для PowerShell не используйте запись вида `<MigrationName>` — символы `<` и `>` там интерпретируются как операторы.
> Рекомендуемое имя миграции: `InitialSchema`, `AddUserSettings`, `AddPollIndexes` и т.д.

### Скрипты (быстрый запуск)
- PowerShell:
  - `./scripts/db-migration-add.ps1 -Name InitialSchema`
  - `./scripts/db-migration-add.ps1 -Name AddPollIndexes -Apply` (создать и сразу применить)
  - `./scripts/db-update.ps1`

---


## Сущности

### User (`users`)

| Колонка | Тип | Ограничения | Описание |
|---------|-----|-------------|----------|
| id | int | PK, auto-seq | — |
| username | varchar(32) | UNIQUE, NOT NULL | Логин |
| name | varchar(50) | nullable | Имя |
| surname | varchar(50) | nullable | Фамилия |
| midname | varchar(50) | nullable | Отчество |
| password_hash | text | NOT NULL | BCrypt |
| created_at | timestamp | default now() | — |
| last_online | timestamp | nullable | — |
| department_id | int | FK → departments, SET NULL | — |
| avatar | text | nullable | Относительный путь |
| is_banned | bool | default false | — |

**NotMapped**: `DisplayName` → `Surname + Name + Midname`

> ⚠️ `UserRole` (User/Head/Admin) **не хранится** в таблице. Роль вычисляется динамически на основе принадлежности к отделу и статуса руководителя. Хранение в БД — под вопросом (TODO).

**Связи**: `ChatMembers` (1:N), `Chats` (1:N, CreatedById), `Department` (N:1), `Departments` (1:N, HeadId), `Messages` (1:N), `PollVotes` (1:N), `UserSetting` (1:1), `RefreshTokens` (1:N)

---

### Chat (`chats`)

| Колонка | Тип | Ограничения | Описание |
|---------|-----|-------------|----------|
| id | int | PK, auto-seq | — |
| name | varchar(100) | nullable | — |
| type | chat_type | NOT NULL | — |
| created_at | timestamp | default now() | — |
| created_by_id | int | FK → users, CASCADE | — |
| last_message_time | timestamp | nullable | — |
| avatar | text | nullable | Относительный путь |

**Связи**: `ChatMembers` (1:N), `CreatedBy` (N:1 → User), `Department` (1:1, опционально), `Messages` (1:N)

---

### ChatMember (`chat_members`)
Составной PK: `(chat_id, user_id)`

| Колонка | Тип | Ограничения | Описание |
|---------|-----|-------------|----------|
| chat_id | int | PK, FK → chats | — |
| user_id | int | PK, FK → users | — |
| role | chat_role | NOT NULL | — |
| joined_at | timestamp | default now() | — |
| notifications_enabled | bool | default true | Mute/unmute |
| last_read_message_id | int | FK → messages, SET NULL | Read state |
| last_read_at | timestamp | nullable | — |

**Индексы**: `UQ_Chat_User` (chat_id, user_id), `idx_chat_members_user_id`, `idx_chat_members_last_read_message_id`

---

### Message (`messages`)

| Колонка | Тип | Ограничения | Описание |
|---------|-----|-------------|----------|
| id | int | PK, auto-seq | — |
| chat_id | int | FK → chats | — |
| sender_id | int | FK → users | — |
| content | text | nullable | — |
| created_at | timestamp | default now() | — |
| edited_at | timestamp | nullable | — |
| is_deleted | bool | default false | Soft delete |
| reply_to_message_id | int | FK → messages, SET NULL | Self-ref |
| forwarded_from_message_id | int | FK → messages, SET NULL | Self-ref |
| is_system_message | bool | default false | — |
| system_event_type | system_event_type | nullable | — |
| target_user_id | int | FK → users, SET NULL | Для системных |

**NotMapped**: `IsVoiceMessage` → `VoiceMessage != null`

**Индексы**: `idx_messages_chatid_createdat` (пагинация), `idx_messages_reply_to_message_id`, `idx_messages_forwarded_from_message_id`, `idx_messages_target_user_id`

**Связи**: `Chat` (N:1), `Sender` (N:1), `TargetUser` (N:1), `ReplyToMessage` (N:1, self-ref), `ForwardedFromMessage` (N:1, self-ref), `VoiceMessage` (1:0..1), `MessageFiles` (1:N), `Polls` (1:N)

---

### MessageFile (`message_files`)

| Колонка | Тип | Ограничения | Описание |
|---------|-----|-------------|----------|
| id | int | PK, auto-seq | — |
| message_id | int | FK → messages | — |
| file_name | varchar(255) | NOT NULL | — |
| content_type | varchar(100) | NOT NULL | MIME-тип |
| path | text | nullable | Относительный путь |

---

### VoiceMessage (`voice_messages`)
PK = FK → messages (1:1, CASCADE)

| Колонка | Тип | Ограничения | Описание |
|---------|-----|-------------|----------|
| message_id | int | PK, FK → messages, CASCADE | — |
| duration_seconds | double | NOT NULL | — |
| file_path | text | NOT NULL | — |
| file_name | varchar(255) | NOT NULL | — |
| content_type | varchar(100) | default "audio/wav" | — |
| file_size | bigint | NOT NULL | Байты |

---

### Poll (`polls`)

| Колонка | Тип | Ограничения | Описание |
|---------|-----|-------------|----------|
| id | int | PK, auto-seq | — |
| message_id | int | FK → messages | — |
| is_anonymous | bool | default true | — |
| allows_multiple_answers | bool | default false | — |
| closes_at | timestamp | nullable | — |

**Индексы**: `idx_polls_message_id`

**Связи**: `Message` (N:1), `PollOptions` (1:N), `PollVotes` (1:N)

---

### PollOption (`poll_options`)

| Колонка | Тип | Ограничения | Описание |
|---------|-----|-------------|----------|
| id | int | PK, auto-seq | — |
| poll_id | int | FK → polls | — |
| option_text | varchar(50) | NOT NULL | — |
| position | int | NOT NULL | Порядок |

---

### PollVote (`poll_votes`)

| Колонка | Тип | Ограничения | Описание |
|---------|-----|-------------|----------|
| id | int | PK, auto-seq | — |
| poll_id | int | FK → polls | — |
| option_id | int | FK → poll_options | — |
| user_id | int | FK → users | — |
| voted_at | timestamp | default now() | — |

**Индексы**: `UQ_Poll_User_Option_Vote` (poll_id, user_id, option_id), `idx_poll_votes_user_id`

---

### Department (`departments`)
Иерархическая структура (self-referencing).

| Колонка | Тип | Ограничения | Описание |
|---------|-----|-------------|----------|
| id | int | PK, auto-seq | — |
| name | varchar(100) | NOT NULL | — |
| parent_department_id | int | FK → departments, SET NULL | Self-ref |
| chat_id | int | FK → chats, UNIQUE, SET NULL | Чат отдела |
| head_id | int | FK → users, SET NULL | Руководитель |

**Индексы**: `idx_departments_head_id`

**Связи**: `Chat` (1:1), `Head` (N:1 → User), `ParentDepartment` (N:1, self-ref), `InverseParentDepartment` (1:N), `Users` (1:N)

---

### RefreshToken (`refresh_tokens`)
Ротация JWT с защитой от replay-атак через семейства токенов.

| Колонка | Тип | Ограничения | Описание |
|---------|-----|-------------|----------|
| id | int | PK, auto-seq | — |
| user_id | int | FK → users, CASCADE | — |
| token_hash | varchar(128) | NOT NULL | SHA-256 хеш |
| jwt_id | varchar(64) | NOT NULL | Jti access-токена |
| created_at | timestamp | NOT NULL | — |
| expires_at | timestamp | NOT NULL | — |
| used_at | timestamp | nullable | null = не использован |
| revoked_at | timestamp | nullable | null = не отозван |
| replaced_by_token_id | int | FK → refresh_tokens, SET NULL | Следующий в цепочке |
| family_id | varchar(64) | NOT NULL | ID семейства |

**NotMapped**: `IsActive` → `UsedAt == null && RevokedAt == null && ExpiresAt > UtcNow`

**Индексы**: `idx_refresh_tokens_token_hash`, `idx_refresh_tokens_user_id`, `idx_refresh_tokens_family_id`, `idx_refresh_tokens_expires_at`

> ⚠️ Сам токен **не хранится** — только SHA-256 хеш. При повторном использовании токена вся семья (`family_id`) отзывается целиком.

---

### UserSetting (`user_settings`)
1:1 с User (PK = FK).

| Колонка | Тип | Ограничения | Описание |
|---------|-----|-------------|----------|
| user_id | int | PK, FK → users | — |
| theme | theme | nullable | — |
| notifications_enabled | bool | default true | — |

---

### SystemSetting (`system_settings`)

| Колонка | Тип | Ограничения | Описание |
|---------|-----|-------------|----------|
| key | varchar(50) | PK | — |
| value | text | NOT NULL | — |

---

## Особенности модели

### Partial Classes
Модели `User`, `Chat`, `ChatMember`, `UserSetting` разделены на partial:
- Основной файл — колонки и навигации
- `*.Partial.cs` — enum-свойства (`Type`, `Role`, `Theme`) и `[NotMapped]` свойства

### Soft Delete
`Message.is_deleted = true` — записи не удаляются физически.

### Каскады
| Отношение | Поведение |
|-----------|-----------|
| User → RefreshToken | CASCADE |
| User → Chat (CreatedBy) | CASCADE |
| Message → VoiceMessage | CASCADE |
| Остальные FK | SET NULL |

### Self-referencing
- `Message` → `Message` (reply, forward)
- `Department` → `Department` (иерархия)
- `RefreshToken` → `RefreshToken` (цепочка ротации)