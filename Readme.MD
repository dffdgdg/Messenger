# Корпоративный мессенджер

<div align="center">

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Avalonia](https://img.shields.io/badge/Avalonia-11.3-8B44AC?logo=avalonia)](https://avaloniaui.net/)
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
*   **Backend (MessengerAPI):** ASP.NET Core 8 REST API + SignalR Hub. База данных — PostgreSQL 16. Серверная часть контейнеризирована (Docker).
*   **Frontend (MessengerDesktop):** Кроссплатформенный десктоп-клиент на **Avalonia UI**. Использует архитектуру **MVVM** и стратегию **Offline-first** с локальной базой SQLite и FTS5.
*   **Shared (MessengerShared):** Общие DTO и контракты данных.

### Ключевые особенности
*   **Безопасность:** JWT ротация токенов (FamilyId), BCrypt паролей, Rate Limiting, безопасность уровней доступа.
*   **Real-time:** Мгновенные уведомления, статусы набора текста, чтения, онлайн/офлайн через SignalR.
*   **Кэширование:** Быстрый оффлайн доступ к истории чатов через SQLite + FTS5 поиск.
*   **Файлы:** Отправка файлов до 100 МБ, автоконвертация аватаров (WebP).
*   **Голосовые:** Запись микрофона.

---

## Возможности

### Коммуникация
- Личные диалоги и групповые чаты (до N участников)
- Поддержка опросов (Polls) с анонимным и множественным голосованием
- Ответы на сообщения (Quote) и пересылка (Forward)
- Редактирование и удаление сообщений (soft-delete)
- Голосовые сообщения с транскрибацией в текст (AI)
- Индикаторы прочтения (Read Receipts) и статуса "печатает..."
- Настройка уведомлений (мут чата, звук)

### Организация
- Иерархические отделы (структура дерева без циклов)
- Ролевая модель: Пользователь / Руководитель отдела / Администратор
- Управление составом отделов и назначением руководителей
- Глобальный поиск по всем чатам и сообщениям с подсветкой совпадений

### Клиент
- Работает на Windows, macOS и Linux
- Темы оформления (светлая/тёмная/системная)
- Безопасное хранение учётных данных через OS-native хранилища
- Асинхронная загрузка изображений (Avatars, Attachments)

### Администрирование
- Панель управления пользователями (бан/разбан, смены пароля)
- Создание и управление структурой организаций
- Контейнеризация сервера (Healthchecks, Restart Policies)

---

## Технологический стек

| Компонент | Технологии | Версия |
|-----------|------------|--------|
| **Runtime** | .NET | 8.0 |
| **Сервер** | ASP.NET Core Web API + SignalR | 8.0 |
| **Клиент** | Avalonia UI (MVVM) + CommunityToolkit.Mvvm | 11.3.10 |
| **ORM (Server)** | Entity Framework Core | 9.0.8 |
| **ORM (Client)** | sqlite-net-pcl | 1.9.172 |
| **База данных** | PostgreSQL + Npgsql | 16 / 9.0+ |
| **Audio** | NAudio | 2.2.1 |
| **Security** | JwtBearer, BCrypt.Net-Next | 8.0.22 / 4.0.3 |
| **Images** | SixLabors.ImageSharp.Web | 3.2.0 |

---

## Структура проекта

```text
📦 Messenger
│
├── 📂 MessengerShared/         # Общие компоненты (Class Library)
│   ├── 📂 Dto/                 # Объекты передачи данных
│   │   ├── Auth/               # LoginRequest, TokenResponseDto
│   │   ├── Chat/               # ChatDto, ChatMemberDto
│   │   ├── Message/            # MessageDto
│   │   ├── User/               # UserDto, CreateUserDto
│   │   └── ...                 # Polls, Notifications, Departments
│   ├── 📂 Enum/                # Типы данных (ChatType, Role, Theme)
│   └── 📄 Response/            # ApiResponse<T> wrapper
│
├── 📂 MessengerAPI/            # Серверная часть (ASP.NET Core)
│   ├── 📂 Controllers/         # REST API endpoints
│   │   ├── BaseController      # ExecuteAsync (Result -> HTTP)
│   │   ├── AuthController      # Login, Refresh, Revoke
│   │   ├── ChatsController     # CRUD чатов, члены
│   │   ├── MessagesController  # Сообщения, поиск, файлы
│   │   ├── PollsController     # Опросы
│   │   └── ...                 # Users, Departments, Admin
│   ├── 📂 Hubs/                # SignalR Hub (/chatHub)
│   ├── 📂 Models/              # EF Core Entities + DbContext
│   ├── 📂 Services/            # Бизнес-логика
│   │   ├── Infrastructure/     # CacheService, AccessControlService, HubNotifier
│   │   ├── Messaging/          # MessageService, FileService
│   │   └── ...                 # AuthService, ChatService
│   ├── 📂 Middleware/          # ExceptionHandling, RateLimiting
│   ├── 📂 Configuration/       # DI, Swagger, AppSettings
│   └── 📄 Dockerfile           # Multi-stage build (non-root user)
│
├── 📂 MessengerDesktop/        # Клиент (Avalonia UI)
│   ├── 📂 ViewModels/          # MVVM логика
│   │   ├── Shell/              # MainWindowViewModel, MainMenuViewModel
│   │   ├── Chat/               # ChatViewModel (Handler-based), ChatContext
│   │   ├── Factories/          # Dependency Injection factories
│   │   └── Dialog/             # Modal dialogs
│   ├── 📂 Services/            # Сервисы клиента
│   │   ├── Api/                # ApiClientService (auto-refresh)
│   │   ├── Auth/               # AuthManager, SecureStorage
│   │   ├── Realtime/           # GlobalHubConnection
│   │   └── Audio/              # NAudioRecording, Playback
│   ├── 📂 Data/                # Локальный кеш
│   │   ├── LocalDatabase       # SQLite Setup + FTS5
│   │   └── Repositories/       # CacheRepositories
│   └── 📂 Views/               # XAML разметка (AXAML)
│
├── 📄 docker-compose.server.yml # Сервер + Postgres + ZAP Scanner
├── 📄 README.md
└── 📄 LICENSE
```

---

## Установка

### Системные требования
- OS: Windows 10+ / macOS 12+ / Linux (Ubuntu 20.04+)
- RAM: 4 GB Minimum / 8 GB Recommended
- Disk: 500 MB

### Развёртывание сервера (Docker)

Рекомендуемый способ запуска сервера с базой данных одной командой:

```bash
# 1. Клонировать репозиторий
git clone https://github.com/username/messenger.git
cd messenger

# 2. Запустить сервер и БД
docker compose -f docker-compose.server.yml up -d

# 3. Проверить логи (если база не стартует сразу)
docker compose logs -f postgres
docker compose logs -f messenger-api
```

По умолчанию сервер доступен на порту **8080**.
Для разработки (dev): порт меняется динамически или задается переменной окружения.

> **Примечание:** Первый запуск может занять время на миграцию базы данных и создание таблиц.

### Развёртывание клиента (Release)
1. Скачайте архив из Releases (Windows/Linux/macOS).
2. Распакуйте в удобную папку.
3. Установите **[.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)**.
4. Запустите `MessengerDesktop.exe` (или бинарник).

> Входящие данные шифруются средствами ОС при сохранении (Secure Storage).

---

## Архитектурные особенности

### Backend Architecture
*   **Result Pattern:** Сервисы возвращают объекты `Result<T>` (успех/ошибка) вместо исключений. Контроллеры автоматически маппят их на HTTP статусы.
*   **Access Control:** Единый слой `AccessControlService` проверяет права перед выполнением бизнес-логики (Owner/Admin/Member).
*   **Caching:** `IMemoryCache` для списка чатов (TTL 5 мин) и состава участников (TTL 10 мин) с явной инвалидацией при изменениях.
*   **Rate Limiting:** Sliding Window Policy настроен индивидуально для разных действий (Login, Upload, Search, Messaging).
*   **SignalR:** Single Hub (`ChatHub`) с обработкой событий через `HubNotifier`. Клиенты подписываются на группы чатов и отдельных юзеров.

### Frontend Architecture (MVVM)
*   **Handler-Based Decomposition:** `ChatViewModel` разделён на отдельные классы-хаудеры (Edit, Reply, Forward, Typing...) для снижения связанности.
*   **Stale-While-Revalidate:** При загрузке списка чатов клиент мгновенно показывает закэшированные данные (`LocalDatabase`), параллельно подтягивая свежие с сервера.
*   **Offline-First:** Все важные данные хранятся в SQLite. Поиск работает локально через FTS5.
*   **Global Hub Connection:** Один глобальный WebSocket-соединение на всё приложение. События фильтруются внутри `ChatHubSubscriber` для каждого открытого чата.

---

## Скриншоты

<details>
<summary><b>Темы оформления и Навигация</b></summary>

| Светлая тема | Тёмная тема |
|:-------------:|:-----------:|
| *(Screenshot)* | *(Screenshot)* |
</details>

<details>
<summary><b>Рабочее пространство (Чаты)</b></summary>

| Список диалогов | Переписка с опросом |
|:---------------:|:-------------------:|
| *(Screenshot)* | *(Screenshot)* |
</details>

<details>
<summary><b>Администрирование</b></summary>

| Управление пользователями | Управление отделами |
|:-------------------------:|:-------------------:|
| *(Screenshot)* | *(Screenshot)* |
</details>

---

## API Документация

После запуска сервера Swagger доступен по адресу:
`http://localhost:8080/swagger`

### Основные эндпоинты

#### Аутентификация
| Endpoint | Метод | Описание |
|----------|-------|----------|
| `/api/auth/login` | POST | Вход, получение токенов |
| `/api/auth/refresh` | POST | Обновление access-токена |
| `/api/auth/revoke` | POST | Выход (реvoke всех refresh-токенов) |

#### Чаты и Сообщения
| Endpoint | Метод | Описание |
|----------|-------|----------|
| `/api/chats/{id}/members` | GET | Состав участников |
| `/api/messages/chat/{id}` | GET | История сообщений (paginated) |
| `/api/messages/{id}` | PUT | Редактирование текста |
| `/api/messages/{id}` | DELETE | Удаление (soft-delete) |

#### Пользователи
| Endpoint | Метод | Описание |
|----------|-------|----------|
| `/api/users/{id}` | GET | Профиль пользователя |
| `/api/users/{id}/avatar` | POST | Загрузка фото профиля |
| `/api/admin/users` | GET | Список пользователей (Admin) |

#### SignalR Hub
Подключение к URL: `{ApiUrl}/chatHub` (через JS SDK или SignalR Client)
Методы: `ReceiveMessage`, `SendTyping`, `GetUnreadCounts` и др.

---

## Терминология

| Термин | Значение |
|--------|----------|
| **Result Pattern** | Паттерн обработки ошибок без исключений (`Result<T>.Success()` / `.Failure()`) |
| **Soft Delete** | Удаление помечается флагом `IsDeleted=true`, но запись остаётся в БД |
| **FTS5** | Полнотекстовый поисковый индекс в SQLite для быстрого поиска |
| **Family Id** | Группа связанных Refresh-токенов для обнаружения атак повторного использования (Replay Attack) |

---

## Контакты

Разработано [@a35472](https://t.me/a35472)