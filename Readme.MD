<div align="center">

# Корпоративный мессенджер

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Avalonia](https://img.shields.io/badge/Avalonia-11.3-8B44AC?logo=avalonia)](https://avaloniaui.net/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-4169E1?logo=postgresql&logoColor=white)](https://www.postgresql.org/)
[![SignalR](https://img.shields.io/badge/SignalR-Real--time-blue?logo=dotnet)](https://learn.microsoft.com/aspnet/core/signalr/)
[![Docker](https://img.shields.io/badge/Docker-Compose-2496ED?logo=docker&logoColor=white)](https://docs.docker.com/compose/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Build](https://img.shields.io/badge/build-passing-brightgreen)]()

<img src="Assets/Screenshots/Dialog.jpg" width="700" alt="Главный экран приложения">

</div>

---

## О проекте

Корпоративный мессенджер — современное решение для организации внутренней коммуникации в компаниях. Система обеспечивает обмен сообщениями и файлами в реальном времени, голосовые сообщения с автоматической расшифровкой, опросы, управление отделами и ролевую модель доступа.

Построен на клиент-серверной архитектуре с использованием **.NET 8**, **Avalonia UI** (кроссплатформенный десктопный клиент) и **SignalR** для двусторонней связи в реальном времени. Клиент реализует стратегию **cache-first** с локальной базой SQLite и полнотекстовым поиском. Серверная часть контейнеризирована с помощью **Docker**.

---

## Возможности

### Коммуникация
- Обмен сообщениями в реальном времени (SignalR)
- Личные диалоги и групповые чаты
- Ответы на сообщения и пересылка с наследованием контента
- Редактирование и удаление (soft-delete) сообщений
- Голосовые сообщения с автоматической транскрибацией (Whisper)
- Отправка файлов до 20 МБ (изображения конвертируются в WebP)
- Создание опросов с анонимным и множественным голосованием
- Индикаторы набора текста и статусы прочтения
- Уведомления о новых сообщениях с настройкой мьютинга по чатам

### Организация
- Иерархическая структура отделов с защитой от циклов (BFS)
- Роли: пользователь, руководитель отдела, администратор
- Управление составом отделов (руководитель / администратор)
- Глобальный поиск по чатам и сообщениям с подсветкой совпадений
- Поиск внутри отдельного чата (курсорная пагинация)
- Профили пользователей с аватарами
- Отслеживание онлайн-статуса в реальном времени

### Клиент
- Кроссплатформенный десктопный клиент (Windows, macOS, Linux)
- Темы оформления: светлая, тёмная, системная
- Офлайн-доступ к истории сообщений (SQLite кеш, FTS5 полнотекстовый поиск)
- Мгновенное отображение кеша с фоновой синхронизацией (stale-while-revalidate)
- Безопасное хранение учётных данных (AES-256 + PBKDF2, привязка к машине)
- Адаптивный интерфейс с поддержкой компактного режима

### Администрирование
- Панель управления пользователями (создание, блокировка)
- Управление структурой отделов
- JWT-аутентификация с ролевой авторизацией
- Контейнеризация серверной части (Docker Compose)
- Сканирование безопасности (OWASP ZAP)

---

## Технологический стек

| Компонент | Технологии |
|-----------|-----------|
| **Сервер** | ASP.NET Core 8, Entity Framework Core, SignalR, PostgreSQL 16 |
| **Клиент** | Avalonia UI 11.3, CommunityToolkit.Mvvm, sqlite-net-pcl, SignalR Client |
| **Медиа** | ImageSharp (ресайз/WebP), NAudio (запись аудио), Whisper.cpp (транскрибация) |
| **Безопасность** | JWT Bearer, BCrypt, AES-256 + PBKDF2, OWASP ZAP |
| **Инфраструктура** | Docker, Docker Compose, multi-stage build |
| **Общие** | .NET 8 Class Library (DTO, Enum, ApiResponse) |

---

## Установка

### Системные требования

| Параметр | Минимальное значение | Рекомендуемое |
|----------|---------------------|---------------|
| **ОС** | Windows 10 / macOS 10.15 / Linux | Windows 11 / macOS 12+ |
| **ОЗУ** | 4 ГБ | 8 ГБ |
| **Диск** | 200 МБ | 500 МБ |
| **Runtime** | .NET 8.0 | .NET 8.0 |

### Установка клиента (из релиза)

1. Скачайте дистрибутив для вашей ОС
2. Распакуйте архив в удобную директорию
3. Установите [.NET 8.0 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (если не установлен)
4. Запустите приложение:
   - **Windows:** `MessengerDesktop.exe`
   - **Linux/macOS:** `./MessengerDesktop`
5. Авторизуйтесь с учётной записью, созданной администратором

> **Примечание:** Регистрация пользователей осуществляется только администратором через панель управления.

---

### Развёртывание сервера

#### Вариант 1: Docker Compose (рекомендуется)

Самый быстрый способ — одна команда поднимает API-сервер и PostgreSQL:

```bash
# Запуск сервера и базы данных
docker compose -f docker-compose.server.yml up -d

# Проверка статуса
docker compose -f docker-compose.server.yml ps

# Просмотр логов
docker compose -f docker-compose.server.yml logs -f messenger-api

# Остановка
docker compose -f docker-compose.server.yml down
```

Сервер будет доступен на `http://localhost:8080`. База данных создаётся автоматически.

**Что включено:**
- **messenger-api** — API-сервер (порт 8080), multi-stage build, non-root user
- **postgres** — PostgreSQL 16 Alpine с healthcheck
- **Volumes** — персистентное хранение: БД (`postgres_data`), загруженные файлы (`messenger_uploads`), аватары (`messenger_avatars`)

##### Сканирование безопасности (OWASP ZAP)

```bash
# Запуск baseline-сканирования
docker compose -f docker-compose.server.yml --profile security up zap-baseline

# Отчёты сохраняются в ./security-reports/
```

##### Настройка переменных окружения

По умолчанию Docker Compose использует тестовые значения. Для продакшена переопределите через `.env` файл или переменные окружения:

```bash
# .env
POSTGRES_PASSWORD=strong_production_password
ASPNETCORE_ENVIRONMENT=Production
```

Или напрямую:

```bash
docker compose -f docker-compose.server.yml up -d \
  -e POSTGRES_PASSWORD=strong_production_password
```

#### Вариант 2: Ручное развёртывание

##### 1. Подготовка базы данных

```sql
CREATE DATABASE messenger_db;
CREATE USER messenger_user WITH ENCRYPTED PASSWORD 'your_password';
GRANT ALL PRIVILEGES ON DATABASE messenger_db TO messenger_user;
```

##### 2. Конфигурация сервера

Создайте файл `appsettings.Production.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=messenger_db;Username=messenger_user;Password=your_password"
  },
  "Jwt": {
    "Secret": "your-secret-key-minimum-32-characters-long!",
    "LifetimeHours": 24,
    "Issuer": "MessengerAPI",
    "Audience": "MessengerClient"
  },
  "Messenger": {
    "AdminDepartmentId": 1,
    "MaxFileSizeBytes": 20971520,
    "BcryptWorkFactor": 12,
    "MaxImageDimension": 1600,
    "ImageQuality": 85,
    "DefaultPageSize": 50,
    "MaxPageSize": 100
  }
}
```

##### 3. Транскрибация голосовых сообщений (опционально)

Для работы транскрибации необходим [Whisper.cpp](https://github.com/ggerganov/whisper.cpp) и модель `ggml-small-q5_1.bin`.

##### 4. Запуск

```bash
cd MessengerAPI
dotnet ef database update
dotnet run --configuration Release
```

Сервер автоматически создаст директории `wwwroot/uploads` и `wwwroot/avatars`.

---

## Структура проекта

```
📦 Messenger
├── 📂 MessengerShared/              # Общие компоненты (Class Library)
│   ├── 📂 Dto/                      # Объекты передачи данных
│   │   ├── 📂 Auth/                 # LoginRequest, AuthResponseDto
│   │   ├── 📂 Chat/                 # ChatDto, ChatMemberDto, UpdateChatDto
│   │   ├── 📂 Department/           # DepartmentDto, UpdateDepartmentMemberDto
│   │   ├── 📂 Message/              # MessageDto, MessageFileDto, PagedMessagesDto
│   │   ├── 📂 Notification/         # NotificationDto
│   │   ├── 📂 Online/               # OnlineStatusDto
│   │   ├── 📂 Poll/                 # PollDto, CreatePollDto, PollVoteDto
│   │   ├── 📂 ReadReceipt/          # MarkAsReadDto, UnreadCountDto
│   │   ├── 📂 Search/               # GlobalSearchResponseDto, SearchMessagesResponseDto
│   │   └── 📂 User/                 # UserDto, CreateUserDto, ChangePasswordDto
│   ├── 📂 Enum/                     # ChatRole, ChatType, Theme, UserRoles
│   └── 📂 Response/                 # ApiResponse<T>, ApiResponseHelper
│
├── 📂 MessengerAPI/                 # Серверная часть (ASP.NET Core)
│   ├── 📂 Common/                   # AppDateTime, Result<T>, ValidationHelper
│   ├── 📂 Configuration/            # DI, JWT, Swagger, StaticFiles, MessengerSettings
│   ├── 📂 Controllers/              # REST API эндпоинты
│   │   ├── BaseController           # ExecuteAsync, GetCurrentUserId
│   │   ├── AuthController           # Авторизация (AllowAnonymous)
│   │   ├── AdminController          # Управление пользователями (Admin)
│   │   ├── ChatsController          # CRUD чатов, участники, аватары
│   │   ├── MessagesController       # CRUD сообщений, поиск, пагинация
│   │   ├── FilesController          # Загрузка файлов (до 100 МБ)
│   │   ├── PollController           # Опросы и голосование
│   │   ├── ReadReceiptsController   # Статусы прочтения
│   │   ├── NotificationController   # Настройки уведомлений
│   │   ├── UserController           # Профили, онлайн-статусы
│   │   └── DepartmentController     # Управление отделами
│   ├── 📂 Hubs/                     # SignalR (ChatHub)
│   ├── 📂 Mapping/                  # Маппинг Entity → DTO
│   ├── 📂 Middleware/               # Глобальная обработка исключений
│   ├── 📂 Model/                    # EF Core сущности + DbContext
│   ├── 📄 Dockerfile                # Multi-stage build, non-root user
│   └── 📂 Services/                 # Бизнес-логика
│       ├── 📂 Auth/                 # AuthService, TokenService
│       ├── 📂 Base/                 # BaseService (SaveChanges, Paginate)
│       ├── 📂 Chat/                 # ChatService, ChatMemberService, NotificationService
│       ├── 📂 Department/           # DepartmentService (BFS, иерархия)
│       ├── 📂 Infrastructure/       # AccessControl, Cache, HubNotifier, OnlineUserService
│       ├── 📂 Messaging/            # MessageService, FileService, PollService, Transcription
│       ├── 📂 ReadReceipt/          # ReadReceiptService
│       └── 📂 User/                 # UserService, AdminService
│
├── 📂 MessengerDesktop/             # Клиент (Avalonia UI, MVVM)
│   ├── 📂 Controls/                 # Кастомные контролы
│   │   ├── AvatarControl            # Аватар с инициалами и онлайн-индикатором
│   │   ├── RichMessageTextBlock     # Кликабельные ссылки в сообщениях
│   │   ├── CircularProgress         # Круговой прогресс-бар
│   │   ├── ThemeSelectorControl     # Переключатель тем с превью
│   │   └── *Skeleton                # Skeleton-заглушки (чаты, сообщения, инфо-панель)
│   ├── 📂 Converters/               # XAML конвертеры (~35 штук)
│   ├── 📂 Data/                     # Локальный кеш (SQLite)
│   │   ├── LocalDatabase            # WAL, FTS5, schema migration
│   │   ├── 📂 Entities/             # CachedChat, CachedMessage, CachedUser, ChatSyncState
│   │   ├── 📂 Mappers/              # CacheMapper (DTO ↔ Entity)
│   │   └── 📂 Repositories/         # ChatCache, MessageCache, LocalCacheService (фасад)
│   ├── 📂 Infrastructure/           # DI, ApiEndpoints, AppConstants, Helpers
│   ├── 📂 Services/                 # Клиентские сервисы
│   │   ├── 📂 Api/                  # ApiClientService (HTTP + auth)
│   │   ├── 📂 Audio/                # NAudioRecorderService, TranscriptionPoller
│   │   ├── 📂 Auth/                 # AuthManager, SecureStorage (AES-256), SessionStore
│   │   ├── 📂 Cache/                # CacheMaintenanceService
│   │   ├── 📂 Navigation/           # NavigationService, DialogService
│   │   ├── 📂 Platform/             # PlatformService (clipboard, window)
│   │   ├── 📂 Realtime/             # GlobalHubConnection, ChatHubConnection
│   │   ├── 📂 Storage/              # SettingsService (JSON)
│   │   └── 📂 UI/                   # NotificationService, ThemeService
│   ├── 📂 ViewModels/               # MVVM логика
│   │   ├── 📂 Shell/                # MainWindowVM, MainMenuVM
│   │   ├── 📂 Auth/                 # LoginViewModel (auto-login, SecureStorage)
│   │   ├── 📂 Chats/                # ChatsVM, ChatListItemVM, GlobalSearchManager
│   │   ├── 📂 Chat/                 # ChatViewModel (8 partial-файлов)
│   │   │   ├── 📂 Managers/         # ChatMessageManager, ChatAttachmentManager, ChatMemberLoader
│   │   │   ├── 📂 Messages/         # MessageVM, MessageFileVM, MessageGroupPosition
│   │   │   └── 📂 Polls/            # PollVM, PollOptionVM
│   │   ├── 📂 Admin/                # AdminVM, UsersTabVM, DepartmentsTabVM
│   │   ├── 📂 Department/           # DepartmentManagementVM, SelectUserDialogVM
│   │   ├── 📂 Dialog/               # ChatEdit, Confirm, Poll, UserEdit, UserProfile, Department
│   │   └── 📂 Factories/            # ChatsViewModelFactory, ChatViewModelFactory
│   └── 📂 Views/                    # XAML разметка (зеркалирует ViewModels)
│
├── 📂 Messenger.Tests/              # Тесты
│   ├── 📂 Controllers/              # Тесты API
│   ├── 📂 Services/                 # Тесты бизнес-логики
│   └── 📂 Helpers/                  # Моки и утилиты
│
├── 📄 docker-compose.server.yml     # Оркестрация: API + PostgreSQL + ZAP
└── 📄 README.md
```

---

## Скриншоты

<details>
<summary><b>Темы оформления</b></summary>

| Светлая тема | Тёмная тема |
|:------------:|:-----------:|
| <img src="Assets/Screenshots/Settings.jpg" width="350"> | <img src="Assets/Screenshots/DarkTheme.jpg" width="350"> |

</details>

<details>
<summary><b>Авторизация и профиль</b></summary>

| Авторизация | Профиль | Настройки |
|:-----------:|:-------:|:---------:|
| <img src="Assets/Screenshots/Login.jpg" width="250"> | <img src="Assets/Screenshots/Profile.jpg" width="250"> | <img src="Assets/Screenshots/Settings.jpg" width="250"> |

</details>

<details>
<summary><b>Чаты и сообщения</b></summary>

| Список диалогов | Переписка | Поиск в чате |
|:---------------:|:---------:|:------------:|
| <img src="Assets/Screenshots/DialogList.png" width="250"> | <img src="Assets/Screenshots/Dialog.jpg" width="250"> | <img src="Assets/Screenshots/ChatSearch.jpg" width="250"> |

| Список чатов | Переписка | Создание чата |
|:------------:|:-------------:|:-------------:|
| <img src="Assets/Screenshots/Chat.jpg" width="250"> | <img src="Assets/Screenshots/ChatList.jpg" width="250"> | <img src="Assets/Screenshots/CreateChat.jpg" width="250"> |

</details>

<details>
<summary><b>Опросы</b></summary>

| Создание опроса |
|:---------------:|
| <img src="Assets/Screenshots/CreatePoll.jpg" width="400"> |

</details>

<details>
<summary><b>Администрирование</b></summary>

| Список сотрудников | Создание сотрудника |
|:------------------:|:-------------------:|
| <img src="Assets/Screenshots/AdminUser.jpg" width="300"> | <img src="Assets/Screenshots/CreateUser.jpg" width="300"> |

| Список отделов | Создание отдела |
|:--------------:|:---------------:|
| <img src="Assets/Screenshots/AdminDepartmentList.jpg" width="300"> | <img src="Assets/Screenshots/CreateDepartment.jpg" width="300"> |

</details>

<details>
<summary><b>Руководитель отдела</b></summary>

| Страница руководителя | Добавление в отдел |
|:---------------------:|:------------------:|
| <img src="Assets/Screenshots/DepartmentHead.jpg" width="300"> | <img src="Assets/Screenshots/AddDepartmentUser.jpg" width="300"> |

</details>

---

## API Документация

После запуска сервера в режиме разработки документация доступна:

- **Swagger UI:** `https://localhost:7190/swagger`
- **OpenAPI JSON:** `https://localhost:7190/swagger/v1/swagger.json`

> При запуске через Docker (порт 8080): `http://localhost:8080/swagger`

> Все эндпоинты (кроме `/api/auth/login`) требуют JWT-аутентификации (заголовок `Authorization: Bearer <token>`).

### Основные эндпоинты

#### Аутентификация

| Метод | Endpoint | Описание |
|-------|----------|----------|
| `POST` | `/api/auth/login` | Авторизация (AllowAnonymous) |

#### Пользователи

| Метод | Endpoint | Описание |
|-------|----------|----------|
| `GET` | `/api/user` | Список всех пользователей |
| `GET` | `/api/user/{id}` | Профиль пользователя |
| `PUT` | `/api/user/{id}` | Обновление профиля |
| `POST` | `/api/user/{id}/avatar` | Загрузка аватара |
| `PUT` | `/api/user/{id}/username` | Смена имени пользователя |
| `PUT` | `/api/user/{id}/password` | Смена пароля |
| `GET` | `/api/user/online` | Список онлайн-пользователей |
| `POST` | `/api/user/status/batch` | Статусы группы пользователей |

#### Чаты

| Метод | Endpoint | Описание |
|-------|----------|----------|
| `GET` | `/api/chats/user/{userId}/chats` | Все чаты пользователя |
| `GET` | `/api/chats/user/{userId}/dialogs` | Личные диалоги |
| `GET` | `/api/chats/user/{userId}/groups` | Групповые чаты |
| `GET` | `/api/chats/user/{userId}/contact/{contactUserId}` | Диалог с конкретным пользователем |
| `GET` | `/api/chats/{chatId}` | Информация о чате |
| `POST` | `/api/chats` | Создание чата |
| `PUT` | `/api/chats/{id}` | Обновление чата |
| `DELETE` | `/api/chats/{id}` | Удаление чата (Owner) |
| `POST` | `/api/chats/{id}/avatar` | Аватар чата |
| `GET` | `/api/chats/{chatId}/members` | Участники чата |
| `POST` | `/api/chats/{chatId}/members` | Добавление участника |
| `DELETE` | `/api/chats/{chatId}/members/{userId}` | Удаление участника |

#### Сообщения

| Метод | Endpoint | Описание |
|-------|----------|----------|
| `POST` | `/api/messages` | Отправка сообщения |
| `PUT` | `/api/messages/{id}` | Редактирование |
| `DELETE` | `/api/messages/{id}` | Удаление (soft-delete) |
| `GET` | `/api/messages/chat/{chatId}` | История (пагинация) |
| `GET` | `/api/messages/chat/{chatId}/around/{messageId}` | Сообщения вокруг |
| `GET` | `/api/messages/chat/{chatId}/before/{messageId}` | Сообщения до |
| `GET` | `/api/messages/chat/{chatId}/after/{messageId}` | Сообщения после |
| `GET` | `/api/messages/chat/{chatId}/search` | Поиск в чате |
| `GET` | `/api/messages/user/{userId}/search` | Глобальный поиск |
| `GET` | `/api/messages/{id}/transcription` | Транскрибация голосового |
| `POST` | `/api/messages/{id}/transcription/retry` | Повтор транскрибации |

#### Файлы, опросы, прочтения

| Метод | Endpoint | Описание |
|-------|----------|----------|
| `POST` | `/api/files/upload?chatId={id}` | Загрузка файла (до 100 МБ) |
| `POST` | `/api/poll` | Создание опроса |
| `POST` | `/api/poll/vote` | Голосование |
| `POST` | `/api/readreceipts/mark-read` | Отметить прочитанным |
| `GET` | `/api/readreceipts/unread-counts` | Счётчики непрочитанных |

#### Администрирование

| Метод | Endpoint | Описание |
|-------|----------|----------|
| `GET` | `/api/admin/users` | Список пользователей (Admin) |
| `POST` | `/api/admin/users` | Создание пользователя (Admin) |
| `POST` | `/api/admin/users/{id}/toggle-ban` | Блокировка/разблокировка (Admin) |

#### Отделы

| Метод | Endpoint | Описание |
|-------|----------|----------|
| `GET` | `/api/department` | Все отделы |
| `POST` | `/api/department` | Создание (Admin) |
| `PUT` | `/api/department` | Обновление (Admin) |
| `DELETE` | `/api/department` | Удаление (Admin) |
| `GET` | `/api/department/{id}/members` | Сотрудники отдела |
| `POST` | `/api/department/{id}/members` | Добавление сотрудника |
| `DELETE` | `/api/department/{id}/members` | Удаление сотрудника |

#### SignalR Hub

| Endpoint | Описание |
|----------|----------|
| `/chatHub` | Real-time: сообщения, typing, online/offline, read receipts |

---

## Архитектурные особенности

### Серверная часть
- **Result-паттерн** — сервисы возвращают `Result<T>`, контроллеры маппят в HTTP-коды через `ExecuteAsync`
- **Двухуровневый кеш доступа** — per-request Dictionary (L1) + IMemoryCache (L2) для проверки членства в чатах
- **Транскрибация** — Channel\<int\> очередь + BackgroundService + Whisper CLI с SemaphoreSlim(1)
- **Timing-safe аутентификация** — dummy BCrypt.Verify при несуществующем пользователе
- **Контейнеризация** — multi-stage Docker build, non-root user, healthcheck PostgreSQL

### Клиентская часть
- **Stale-while-revalidate** — мгновенный показ кеша, фоновая загрузка актуальных данных
- **Cache-first сообщения** — SQLite → рендер → revalidate, заполнение пробелов после реконнекта
- **Два SignalR-хаба** — GlobalHubConnection (app-wide: уведомления, unread, online) + ChatHubConnection (per-chat: сообщения, typing, read receipts)
- **Partial ViewModel** — ChatViewModel разбит на 8 файлов по ответственностям (Lifecycle, Messages, Commands, Edit, Reply, Forward, Search, Typing, InfoPanel, Voice)
- **Группировка сообщений** — порог 2 минуты → позиции Alone/First/Middle/Last для стилизации
- **SecureStorage** — AES-256 + PBKDF2, машинно-привязанная соль

### Инфраструктура
- **Docker Compose** — оркестрация API + PostgreSQL + опциональный ZAP-сканер
- **Персистентные volumes** — данные БД, загруженные файлы и аватары сохраняются между перезапусками
- **OWASP ZAP** — автоматизированное baseline-сканирование безопасности (профиль `security`)

---

## Тестирование

```bash
# Запуск всех тестов
cd Messenger.Tests
dotnet test

# Запуск с покрытием
dotnet test --collect:"XPlat Code Coverage"

# Запуск конкретной категории
dotnet test --filter "Category=Controllers"
```

### Сканирование безопасности

```bash
# Запуск OWASP ZAP baseline scan (требуется запущенный сервер)
docker compose -f docker-compose.server.yml --profile security up zap-baseline

# Отчёты: ./security-reports/zap-report.html, zap-report.json, zap-warnings.md
```

---

## Термины и сокращения

| Термин | Расшифровка | Описание |
|--------|-------------|----------|
| **API** | Application Programming Interface | Интерфейс программирования приложений |
| **JWT** | JSON Web Token | Токен для безопасной аутентификации |
| **DTO** | Data Transfer Object | Объект для передачи данных между слоями |
| **ORM** | Object-Relational Mapping | Технология связи объектов и БД |
| **SignalR** | — | Библиотека Microsoft для real-time коммуникации |
| **MVVM** | Model-View-ViewModel | Архитектурный паттерн для UI |
| **CRUD** | Create, Read, Update, Delete | Базовые операции с данными |
| **WebP** | — | Современный формат сжатия изображений |
| **FTS5** | Full-Text Search 5 | Модуль полнотекстового поиска SQLite |
| **WAL** | Write-Ahead Logging | Режим журналирования SQLite для конкурентности |
| **BFS** | Breadth-First Search | Поиск в ширину (проверка циклов в иерархии) |
| **ZAP** | Zed Attack Proxy | Инструмент OWASP для сканирования безопасности |

---

## Контакты

| Канал | Контакт |
|-------|---------|
| Telegram | [@a35472](https://t.me/a35472) |