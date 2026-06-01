# Корпоративный мессенджер

<div align="center">

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Avalonia](https://img.shields.io/badge/Avalonia-11.3.12-8B44AC?logo=avalonia)](https://avaloniaui.net/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-4169E1?logo=postgresql&logoColor=white)](https://www.postgresql.org/)
[![SignalR](https://img.shields.io/badge/SignalR--Real--time-blue?logo=dotnet)](https://learn.microsoft.com/aspnet/core/signalr/)
[![Docker](https://img.shields.io/badge/Docker-Compose-2496ED?logo=docker&logoColor=white)](https://docs.docker.com/compose/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)]()

<img src="Assets/Screenshots/Dialog.jpg" width="700" alt="Главный экран приложения">

**Готовое решение для внутренней коммуникации в компании.** Обмен сообщениями, файлами и голосовыми заметками, управление отделами и ролями в реальном времени.

</div>

---

## О проекте

Система построена по архитектуре **клиент-сервер**:
- **Backend (API):** ASP.NET Core 10 REST API + SignalR Hub. База данных — PostgreSQL 16. Серверная часть контейнеризирована (Docker).
- **Frontend (Desktop):** Кроссплатформенный десктоп-клиент на **Avalonia UI**. Использует архитектуру **MVVM** и стратегию **Offline-first** с локальным кэшем на SQLite.
- **Shared:** Общие DTO, перечисления и контракты данных.

### Ключевые особенности
- **Безопасность:** JWT с ротацией токенов (FamilyId), BCrypt паролей, Rate Limiting, разграничение уровней доступа.
- **Real-time:** Мгновенные уведомления, статусы набора текста, прочтения, онлайн/офлайн через SignalR.
- **Кэширование:** Быстрый офлайн-доступ к истории чатов через SQLite. Поиск — через серверный API.
- **Голосовые звонки:** P2P аудио через прямой UDP, кодек Opus, сигнализация через SignalR.
- **Файлы:** Отправка файлов, автоконвертация аватаров в WebP.
- **Голосовые сообщения:** Запись и воспроизведение с визуализацией формы волны.

---

## Возможности

### Коммуникация
- Личные диалоги и групповые чаты
- Голосовые звонки (P2P UDP, Opus, шумоподавление)
- Опросы с анонимным и множественным голосованием
- Ответы на сообщения (Quote) и пересылка (Forward)
- Редактирование и удаление сообщений (soft-delete)
- Голосовые сообщения с формой волны
- Индикаторы прочтения (Read Receipts) и статус «печатает…»
- Закрепление сообщений
- Настройка уведомлений (мут чата)

### Организация
- Иерархические отделы (дерево без циклов, BFS-проверка)
- Ролевая модель: Пользователь / Руководитель отдела / Администратор
- Управление составом отделов и назначением руководителей
- Глобальный поиск по всем чатам и сообщениям с фильтрами и подсветкой совпадений

### Клиент
- Работает на Windows и Linux
- Темы оформления (светлая / тёмная / системная)
- Безопасное хранение учётных данных (DPAPI / KeyChain / AES)
- Асинхронная загрузка изображений с LRU-кэшем (80 элементов / 30 MB)
- Автообнаружение сервера в локальной сети (UDP)

### Администрирование
- Панель управления пользователями (бан/разбан, сброс пароля, создание)
- Управление структурой организации
- Контейнеризация сервера (Healthchecks, Restart Policies)

---

## Технологический стек

| Компонент | Технологии | Версия |
|---|---|---|
| **Runtime** | .NET | 10.0 |
| **Сервер** | ASP.NET Core Web API + SignalR | 10.0 |
| **Клиент** | Avalonia UI + CommunityToolkit.Mvvm | 11.3.12 |
| **ORM (Server)** | Entity Framework Core + Npgsql | 10.0 |
| **ORM (Client)** | sqlite-net-pcl | 1.9.172 |
| **База данных** | PostgreSQL | 16 |
| **Аудио** | PortAudio + Opus (Concentus) | — |
| **Security** | JwtBearer, BCrypt.Net-Next | 10.0 / 4.1.0 |
| **Images** | SixLabors.ImageSharp | 3.2.0 |

---

## Структура проекта

```text
📦 Messenger
│
├── 📂 Shared/                  # Общие компоненты (Class Library)
│   ├── 📂 Dto/                 # DTO: Auth, Chat, Message, User, Poll,
│   │                           #       Notification, Department, Search,
│   │                           #       ReadReceipt, Call, Online
│   ├── 📂 Enum/                # ChatType, ChatRole, Theme, UserRole,
│   │                           #   SystemEventType, CallStatus, UserStatusType
│   ├── 📂 Helpers/             # SystemEventMeta
│   ├── 📂 Hubs/                # HubMethods (константы имён SignalR-методов)
│   └── 📂 Response/            # ApiResponse<T>, ApiResponseHelper
│
├── 📂 API/                     # Серверная часть (ASP.NET Core)
│   ├── 📂 Controllers/         # REST API эндпоинты
│   │   ├── BaseController      # Map(Result<T>) → HTTP-статус
│   │   ├── AuthController      # Login, Refresh, Revoke
│   │   ├── ChatsController     # CRUD чатов, участники, роли
│   │   ├── MessagesController  # Сообщения, поиск, пагинация, pin
│   │   ├── PollsController     # Опросы и голосование
│   │   └── ...                 # Users, Departments, Admin, Files,
│   │                           #   Notifications, ReadReceipts, Status
│   ├── 📂 Hubs/                # MessengerHub (/chatHub)
│   ├── 📂 Data/                # EF Core сущности + MessengerDbContext
│   │   └── 📂 SeedData/        # DataSeeder (users.json)
│   ├── 📂 Services/
│   │   ├── 📂 Core/Auth/       # AuthService, TokenService
│   │   ├── 📂 Features/        # Chat, Messaging, Department,
│   │   │                       #   ReadReceipt, User, Call
│   │   └── 📂 Infrastructure/  # CacheService, AccessControlService,
│   │                           #   HubNotifier, OnlineUserService,
│   │                           #   UdpDiscoveryService, Bundles
│   ├── 📂 Repositories/        # Abstractions + Implementations
│   ├── 📂 Mapping/             # Ручной маппинг сущностей → DTO
│   ├── 📂 Middleware/          # ExceptionHandling, MissingFileCleanup
│   ├── 📂 Configuration/       # DI, Swagger, JWT, RateLimit, StaticFiles
│   └── 📄 Dockerfile
│
├── 📂 Desktop/                 # Клиент (Avalonia UI)
│   ├── 📂 ViewModels/
│   │   ├── 📂 Shell/           # MainWindowViewModel, MainMenuViewModel
│   │   ├── 📂 Chat/            # ChatViewModel + обработчики:
│   │   │   ├── 📂 Features/    #   Edit, Reply, Forward, Typing,
│   │   │   │                   #   Voice, Search, InfoPanel, Notifications
│   │   │   ├── 📂 Managers/    #   ChatMessageManager, AttachmentManager
│   │   │   ├── 📂 Polls/       #   PollViewModel, PollOptionViewModel
│   │   │   └── 📂 Messages/    #   MessageViewModel, MessageFileViewModel
│   │   ├── 📂 ChatList/        # ChatsViewModel, GlobalSearchManager
│   │   ├── 📂 Call/            # CallViewModel, IncomingCallViewModel
│   │   ├── 📂 Dialog/          # Модальные диалоги
│   │   └── 📂 Factories/       # ChatViewModelFactory, ChatsViewModelFactory
│   ├── 📂 Services/
│   │   ├── 📂 Core/            # ApiClientService, GlobalHubConnection,
│   │   │                       #   AuthManager, SessionStore, CookieStorage
│   │   ├── 📂 Features/        # CallService, CallAudioService,
│   │   │                       #   CallHubConnection, NoiseReducer,
│   │   │                       #   AudioPlayerService, AudioRecorderService,
│   │   │                       #   FileDownloadService
│   │   └── 📂 Platform/        # NavigationService, DialogService,
│   │                           #   ThemeService, SettingsService,
│   │                           #   ServerDiscoveryService, NotificationService
│   ├── 📂 Data/                # Локальный кэш (SQLite, схема v3)
│   │   ├── LocalDatabase       # WAL, индексы, миграции
│   │   └── 📂 Repositories/    # LocalCacheService,
│   │                           #   ChatCacheRepository,
│   │                           #   MessageCacheRepository,
│   │                           #   DownloadedFileRepository
│   ├── 📂 Converters/          # 60+ value converters для Avalonia
│   ├── 📂 Infrastructure/      # ApiEndpoints, AppConstants,
│   │                           #   AuthenticatedImageLoader, MemoryDiagnostics
│   └── 📂 Views/               # AXAML + code-behind
│       ├── 📂 Controls/        # CircularProgress, WaveformView,
│       │                       #   RichMessageTextBlock, FilterAutocomplete
│       └── 📂 Chat/            # ChatView, MessageControl, MessageParts
│
├── 📂 API.Tests/               # Интеграционные тесты контроллеров
├── 📄 docker-compose.server.yml
├── 📄 README.md
└── 📄 LICENSE
```

---

## Установка

### Системные требования
- OS: Windows 10+ / Linux (Ubuntu 20.04+)
- RAM: 4 GB минимум / 8 GB рекомендуется
- Disk: 500 MB

### Развёртывание сервера (Docker)

```bash
# 1. Клонировать репозиторий
git clone https://github.com/username/messenger.git
cd messenger

# 2. Запустить сервер и БД
docker compose -f docker-compose.server.yml up -d

# 3. Проверить логи
docker compose logs -f postgres
docker compose logs -f messenger-api
```

По умолчанию сервер доступен на порту **5274**.

> **Примечание:** При первом запуске автоматически применяются миграции БД и запускается DataSeeder.

### Развёртывание клиента (Release)
1. Скачайте архив из Releases (Windows / Linux).
2. Распакуйте в удобную папку.
3. Установите **[.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)**.
4. Запустите `Desktop.exe` (Windows) или бинарник (Linux).

При первом запуске клиент автоматически обнаруживает сервер в локальной сети через UDP (порт 5275). Если автообнаружение не работает — введите адрес сервера вручную через диалог в окне входа.

> Учётные данные шифруются средствами ОС (DPAPI на Windows, KeyChain на macOS, AES на Linux).

---

## Архитектурные особенности

### Backend
- **Result Pattern:** Сервисы возвращают `Result<T>` вместо исключений. `BaseController.Map()` автоматически транслирует результат в HTTP-статус и `ApiResponse<T>`.
- **Access Control:** `AccessControlService` проверяет права (Owner/Admin/Member) с двойным кэшем (MemoryCache + per-request).
- **Caching:** `IMemoryCache` для списков чатов (TTL 5 мин) и состава участников (TTL 10 мин) с явной инвалидацией.
- **Rate Limiting:** Sliding Window Policy для Login (5/мин), Upload (10/мин), Search (15/мин), Messaging (30/мин).
- **SignalR:** Единый `MessengerHub` (`/chatHub`) с рассылкой через `HubNotifier`. Группы: `chat_{id}` и `user_{id}`.

### Frontend (MVVM)
- **Handler Composition:** `ChatViewModel` декомпозирован на независимые Handler-классы (Edit, Reply, Forward, Typing, Voice, Search, InfoPanel, Notifications) через базовый `ChatFeatureHandler`.
- **Stale-While-Revalidate:** При открытии чата мгновенно показываются данные из SQLite, параллельно обновляются с сервера.
- **Offline-First:** История чатов хранится в SQLite (последние 200 сообщений на чат). Поиск всегда выполняется через серверный API.
- **Единое SignalR-соединение:** `GlobalHubConnection` — одно WebSocket-соединение на всё приложение. Фильтрация событий по чату через `ChatHubSubscriber`.

### Голосовые звонки
- **P2P UDP:** Аудио передаётся напрямую между клиентами, сервер аудио не обрабатывает.
- **Сигнализация:** Обмен UDP-эндпоинтами через SignalR (`WebRtcSignalDto`, тип `udp-endpoint`). Выбор лучшего эндпоинта по совпадению подсети.
- **Аудиостек:** PortAudio (48kHz/моно/20ms) + Opus 32kbps + адаптивный VAD + спектральное шумоподавление (Wiener Filter).

---

## Скриншоты

<details>
<summary><b>Темы оформления</b></summary>

| Светлая тема | Тёмная тема |
|:---:|:---:|
| *(Screenshot)* | *(Screenshot)* |
</details>

<details>
<summary><b>Рабочее пространство</b></summary>

| Список диалогов | Переписка с опросом |
|:---:|:---:|
| *(Screenshot)* | *(Screenshot)* |
</details>

<details>
<summary><b>Администрирование</b></summary>

| Управление пользователями | Управление отделами |
|:---:|:---:|
| *(Screenshot)* | *(Screenshot)* |
</details>

---

## API Документация

После запуска сервера Swagger доступен по адресу:
`http://localhost:5274/swagger`

### Основные эндпоинты

#### Аутентификация
| Endpoint | Метод | Описание |
|---|---|---|
| `/api/auth/login` | POST | Вход, получение JWT + refresh-токен в cookie |
| `/api/auth/refresh` | POST | Обновление access-токена по cookie |
| `/api/auth/revoke` | POST | Выход, отзыв refresh-токена |

#### Чаты и сообщения
| Endpoint | Метод | Описание |
|---|---|---|
| `/api/chats/user/{userId}` | GET | Список чатов пользователя |
| `/api/chats` | POST | Создать чат |
| `/api/chats/{id}/members` | GET | Участники чата |
| `/api/messages` | POST | Отправить сообщение |
| `/api/messages/{id}` | PUT | Редактировать сообщение |
| `/api/messages/{id}` | DELETE | Удалить (soft-delete) |
| `/api/messages/chat/{chatId}/latest` | GET | Последние сообщения |
| `/api/messages/chat/{chatId}/around/{messageId}` | GET | Сообщения вокруг якорного |
| `/api/messages/user/{userId}/search` | POST | Глобальный поиск |

#### Пользователи и администрирование
| Endpoint | Метод | Описание |
|---|---|---|
| `/api/users/{id}` | GET | Профиль пользователя |
| `/api/users/{id}/avatar` | POST | Загрузка аватара |
| `/api/users/{id}/status` | PUT | Установить статус |
| `/api/admin/users` | GET | Все пользователи (Admin) |
| `/api/admin/users/{id}/toggle-ban` | POST | Бан/разбан (Admin) |

#### SignalR
Подключение: `{server}/chatHub?access_token={jwt}`

Ключевые события сервера → клиент: `ReceiveMessage`, `MessageUpdated`, `MessageDeleted`, `UserOnline`, `UserOffline`, `UserTyping`, `MessageRead`, `UnreadCountUpdated`, `ChatUpdated`, `ChatRemoved`, `PollUpdated`, `IncomingCall`, `CallStateUpdated`, `CallEnded`

---

## Глоссарий

| Термин | Значение |
|---|---|
| **Result Pattern** | Обработка ошибок без исключений: `Result<T>.Success()` / `Result.NotFound()` / `Result.Forbidden()` и т.д. |
| **Soft Delete** | Удаление помечается флагом `is_deleted=true`, запись остаётся в БД |
| **FamilyId** | Группа связанных Refresh-токенов одной сессии для обнаружения повторного использования |
| **Handler Composition** | Декомпозиция `ChatViewModel` на независимые `ChatFeatureHandler`-классы |
| **Stale-While-Revalidate** | Мгновенный показ кэша из SQLite + фоновое обновление с сервера |
| **P2P UDP** | Прямая передача аудио между клиентами без участия сервера |
| **VAD** | Voice Activity Detection — определение наличия речи для подавления тишины перед отправкой |