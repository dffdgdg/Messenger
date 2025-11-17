# Архитектура проекта Messenger

## 🏗️ Общее описание
Messenger - это полнофункциональная система обмена сообщениями, состоящая из:
- **Backend API** на ASP.NET Core (MessengerAPI)
- **Desktop клиент** на Avalonia UI (MessengerDesktop) 
- **Общей библиотеки** DTO и моделей (MessengerShared)

Проект поддерживает личные и групповые чаты, опросы, управление пользователями и отделами, а также систему авторизации.

## 🛠 Технологический стек

### Backend (MessengerAPI)
- **Фреймворк**: ASP.NET Core 8.0
- **База данных**: PostgreSQL с Entity Framework Core
- **Аутентификация**: JWT Bearer Tokens
- **Реальное время**: SignalR для чатов
- **Обработка файлов**: ImageSharp для обработки аватаров
- **Логирование**: Встроенное логирование .NET + DEBUG вывод

### Frontend (MessengerDesktop)
- **UI Framework**: Avalonia UI (кроссплатформенный)
- **Архитектура**: MVVM с CommunityToolkit.Mvvm
- **HTTP клиент**: HttpClient с кастомной обработкой ошибок
- **Локальное хранилище**: SecureStorage для токенов
- **Навигация**: Кастомная система навигации

### Общее
- **Язык**: C# 11.0
- **База данных**: PostgreSQL с Enums (theme, chat_role)
- **Сериализация**: System.Text.Json

## 📁 Структура проекта

```
Messenger/
├── MessengerAPI/                    # Backend ASP.NET Core
│   ├── Controllers/                # API контроллеры
│   │   ├── AuthController.cs       # Аутентификация
│   │   ├── ChatsController.cs      # Управление чатами
│   │   ├── MessagesController.cs    # Сообщения
│   │   ├── UserController.cs       # Пользователи
│   │   ├── AdminController.cs      # Администрирование
│   │   ├── PollController.cs       # Опросы
│   │   ├── DepartmentController.cs # Отделы
│   │   └── BaseController.cs       # Базовый контроллер
│   ├── Services/                   # Бизнес-логика
│   │   ├── AuthService.cs          # Логика аутентификации
│   │   ├── ChatService.cs          # Логика чатов
│   │   ├── MessageService.cs       # Логика сообщений ⭐ ИСПРАВЛЕНА КРИТИЧЕСКАЯ ОШИБКА
│   │   ├── UserService.cs          # Логика пользователей
│   │   ├── PollService.cs          # Логика опросов
│   │   ├── DepartmentService.cs    # Логика отделов
│   │   ├── AccessControlService.cs # Контроль доступа
│   │   ├── FileService.cs          # Работа с файлами
│   │   ├── AdminService.cs         # Административные функции
│   │   └── BaseService.cs          # Базовый сервис
│   ├── Model/                      # Модели Entity Framework
│   │   ├── MessengerDbContext.cs   # Контекст БД
│   │   ├── User.cs                 # Пользователь
│   │   ├── Chat.cs                 # Чат
│   │   ├── Message.cs              # Сообщение
│   │   ├── Poll.cs                 # Опрос
│   │   ├── Department.cs           # Отдел
│   │   └── ChatMember.cs           # Участник чата
│   ├── Helpers/                    # Вспомогательные классы
│   │   ├── TokenService.cs         # JWT токены
│   │   └── ModelExtensions.cs      # Расширения моделей
│   ├── Hubs/                       # SignalR хабы
│   │   └── ChatHub.cs              # Хаб для чатов
│   └── Program.cs                  # Конфигурация приложения
├── MessengerDesktop/               # Desktop клиент
│   ├── ViewModels/                 # MVVM ViewModels
│   │   ├── MainWindowViewModel.cs  # Главное окно
│   │   ├── MainMenuViewModel.cs    # Главное меню
│   │   ├── ChatViewModel.cs        # Чат ⭐ ОБНОВЛЕНА ЛОГИКА ОТОБРАЖЕНИЯ
│   │   ├── LoginViewModel.cs       # Авторизация
│   │   ├── AdminViewModel.cs       # Админка
│   │   ├── ProfileViewModel.cs     # Профиль
│   │   ├── SettingsViewModel.cs    # Настройки
│   │   ├── BaseViewModel.cs        # Базовая VM
│   │   └── Dialog/                 # Диалоги
│   │       ├── DialogBaseViewModel.cs
│   │       ├── UserProfileDialogViewModel.cs
│   │       ├── PollDialogViewModel.cs
│   │       └── DepartmentDialogViewModel.cs
│   ├── Views/                      # Avalonia Views
│   │   ├── MainWindow.axaml        # Главное окно
│   │   ├── MainMenuView.axaml      # Меню
│   │   ├── ChatView.axaml          # Чат
│   │   └── Dialog/                 # Диалоги
│   ├── Services/                   # Сервисы клиента
│   │   ├── ApiClientService.cs     # HTTP клиент
│   │   ├── AuthService.cs          # Управление аутентификацией
│   │   ├── NavigationService.cs    # Навигация
│   │   ├── DialogService.cs        # Управление диалогами
│   │   ├── SecureStorage.cs        # Безопасное хранилище
│   │   └── NotificationService.cs  # Уведомления
│   ├── Converters/                 # Конвертеры для XAML
│   │   ├── Boolean/                # Boolean конвертеры
│   │   ├── DateTime/               # Конвертеры дат
│   │   ├── Message/                # Конвертеры сообщений
│   │   └── ConverterLocator.cs     # Локатор конвертеров
│   └── App.axaml.cs                # Конфигурация приложения
└── MessengerShared/                # Общие модели
    ├── DTO/                        # Data Transfer Objects
    │   ├── UserDTO.cs              # Пользователь
    │   ├── ChatDTO.cs              # Чат
    │   ├── MessageDTO.cs           # Сообщение
    │   ├── PollDTO.cs              # Опрос
    │   ├── DepartmentDTO.cs        # Отдел
    │   └── AuthDTO.cs              # Аутентификация
    ├── Enum/                       # Перечисления
    │   ├── Theme.cs                # Темы оформления
    │   └── ChatRole.cs             # Роли в чате
    └── Response/                   # Ответы API
        └── ApiResponse.cs          # Стандартный ответ
```

## 🔄 Основные модули и их взаимодействие

### Модуль аутентификации
- **Контроллер**: `AuthController`
- **Сервис**: `AuthService`, `TokenService`
- **Взаимодействие**: 
  - Принимает логин через `AuthController`
  - Генерирует JWT токены через `TokenService`
  - Сохраняет сессию в клиенте через `AuthService`

### Модуль чатов
- **Контроллер**: `ChatsController`, `MessagesController`
- **Сервис**: `ChatService`, `MessageService`, `AccessControlService`
- **Взаимодействие**:
  - `ChatService` управляет созданием чатов и участниками
  - `MessageService` обрабатывает сообщения и опросы ⭐ ИСПРАВЛЕНА ЗАГРУЗКА ОТПРАВИТЕЛЕЙ
  - `AccessControlService` проверяет права доступа
  - `ChatHub` обеспечивает реальное время через SignalR

### Модуль пользователей
- **Контроллер**: `UserController`, `AdminController`
- **Сервис**: `UserService`, `AdminService`
- **Взаимодействие**:
  - Управление профилями и настройками
  - Административные функции через `AdminService`
  - Иерархия отделов через `DepartmentService`

### Модуль опросов
- **Контроллер**: `PollController`
- **Сервис**: `PollService`
- **Взаимодействие**:
  - Интегрирован с системой сообщений
  - Поддерживает анонимные и множественные голоса
  - Обновляет результаты в реальном времени

## 💾 Модель данных

### Основные сущности:
- **User** - пользователи с настройками (UserSetting)
- **Chat** - чаты (личные и групповые)
- **Message** - сообщения с прикрепленными файлами и опросами ⭐ ИСПРАВЛЕНА СВЯЗЬ С ОТПРАВИТЕЛЕМ
- **ChatMember** - участники чатов с ролями
- **Department** - иерархия отделов
- **Poll** - опросы с вариантами ответов и голосами

### Ключевые отношения:
- Пользователь может быть в нескольких чатах (ChatMember)
- Сообщение принадлежит чату и отправителю ⭐ ТЕПЕРЬ КОРРЕКТНО ЗАГРУЖАЕТСЯ
- Опрос привязан к сообщению
- Отделы образуют иерархию через ParentDepartmentId

## 🔒 Безопасность и доступ

### Аутентификация:
- JWT токены с валидацией через `TokenService`
- Автоматическое обновление заголовков в `ApiClientService`
- Безопасное хранение в `SecureStorageService`

### Авторизация:
- `AccessControlService` проверяет права доступа к чатам
- Ролевая модель: owner, admin, member
- Владелец чата имеет полные права

## 🔄 Взаимодействие компонентов

### Типичный поток отправки сообщения:
1. Пользователь вводит сообщение в `ChatViewModel`
2. `ChatViewModel` вызывает `ApiClientService.PostAsync()`
3. `MessagesController` принимает запрос и валидирует через `BaseController`
4. `MessageService` создает сообщение и сохраняет в БД ⭐ КОРРЕКТНО ЗАГРУЖАЕТ ОТПРАВИТЕЛЯ
5. `MessageService` уведомляет `ChatHub` о новом сообщении
6. `ChatHub` рассылает сообщение всем участникам чата
7. Клиенты получают сообщение через SignalR и обновляют UI

### Поток аутентификации:
1. `LoginViewModel` отправляет credentials через `AuthService`
2. `AuthController` валидирует и генерирует токен
3. Токен сохраняется в `SecureStorage` и добавляется в заголовки
4. Все последующие запросы автоматически включают токен

### ⭐ ИСПРАВЛЕННЫЙ ПОТОК ЗАГРУЗКИ СООБЩЕНИЙ:
1. `ChatViewModel` запрашивает сообщения через `ApiClientService.GetAsync()`
2. `MessageService.GetChatMessagesAsync()` выполняет запрос с ПРАВИЛЬНЫМ порядком:
   ```csharp
   .Include(m => m.Sender).AsNoTracking() // Include ПЕРЕД AsNoTracking
   ```
3. Данные отправителя корректно загружаются из БД
4. `MapToDto` генерирует полные URL для аватаров
5. Frontend получает полные данные и отображает реальные имена вместо "User #ID"

## 🚀 Запуск и разработка

### Требования:
- .NET 8.0 SDK
- PostgreSQL 12+
- Avalonia UI workload для разработки клиента

### Конфигурация:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=MessengerDB;Username=postgres;Password=123"
  },
  "Jwt": {
    "Secret": "vRQHb2XkyCqD7hZP9xjMwN5tF3gAS4Ue",
    "LifetimeHours": 24
  }
}
```

### Миграции БД:
```bash
Scaffold-DbContext "Host=localhost;Port=5432;Database=MessengerDB;Username=postgres;Password=123" Npgsql.EntityFrameworkCore.PostgreSQL -OutputDir Models -Context MessengerDbContext -f
```

## 📝 Ключевые архитектурные решения (ADR)

### 1. Единая кодовая база для API и клиента
**Решение**: Использовать общую библиотеку MessengerShared для DTO и enum
**Причина**: Синхронизация типов между клиентом и сервером, избежание дублирования

### 2. MVVM с реактивными командами
**Решение**: CommunityToolkit.Mvvm с ObservableProperty и RelayCommand
**Причина**: Чистая архитектура, тестируемость, привязка данных в Avalonia

### 3. Централизованная обработка ошибок
**Решение**: BaseController и BaseViewModel с SafeExecuteAsync
**Причина**: Единообразная обработка исключений, улучшенный UX

### 4. SignalR для реального времени
**Решение**: ChatHub для мгновенных сообщений и обновлений опросов
**Причина**: Производительность, меньше HTTP запросов, лучший пользовательский опыт

### 5. Иерархическая система доступа
**Решение**: AccessControlService с ролями в чатах
**Причина**: Гибкое управление правами, безопасность данных

### 6. Generic API клиент
**Решение**: ApiClientService с универсальными методами
**Причина**: Переиспользование кода, централизованная обработка токенов

### ⭐ 7. ИСПРАВЛЕНИЕ EF CORE INCLUDE/ASNOTRACKING
**Проблема**: Отправители сообщений не загружались, показывались как "User #ID"
**Решение**: Правильный порядок `.Include().AsNoTracking()` в MessageService
**Причина**: Include должен выполняться ДО AsNoTracking для загрузки связанных данных
**Результат**: 100% корректное отображение имен отправителей

## 🎨 Особенности UI/UX

### Темизация:
- Поддержка light/dark/system тем через перечисление Theme
- Автоматическое сохранение предпочтений
- Синхронизация между устройствами через настройки пользователя

### Адаптивный интерфейс:
- GridSplitter для регулировки ширины списка чатов
- Конвертеры для условного отображения элементов
- Анимации для диалогов и переходов

### Уведомления:
- Встроенная система уведомлений через NotificationService
- Копирование в буфер обмена для ошибок
- Toast-уведомления для успешных операций

### Улучшенная обработка метаданных сообщений
**Frontend**: Приоритет API данных над локальными данными участников
**Backend**: Генерация полных URL для аватаров через HttpRequest
**Логирование**: 23+ DEBUG сообщений для отладки потока данных