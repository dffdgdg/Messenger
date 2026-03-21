# Technology Stack

## Платформа
- **.NET 8.0** — целевой фреймворк для всех проектов
- **Nullable reference types** — включены везде
- **Implicit usings** — включены

---

## Backend (MessengerAPI)

### Фреймворк
- **ASP.NET Core 8.0** (Minimal hosting model, `WebApplication.CreateBuilder`)

### База данных
- **PostgreSQL** (через Npgsql.EntityFrameworkCore.PostgreSQL 9.0.4)
- **Entity Framework Core 9.0.8** — ORM
- **EF Core Tools / Design** — миграции

### Аутентификация и безопасность
- **JWT Bearer** — Microsoft.AspNetCore.Authentication.JwtBearer 8.0.22
- **System.IdentityModel.Tokens.Jwt 8.15.0**
- **Microsoft.IdentityModel.Tokens 8.15.0**
- **BCrypt.Net-Next 4.0.3** — хеширование паролей

### Real-time
- **SignalR** (Microsoft.AspNetCore.SignalR 1.2.0)
- Hub endpoint: `/chatHub`

### Rate Limiting
- Встроенный `System.Threading.RateLimiting` (ASP.NET Core)
- Sliding Window стратегия
- Политики: `login` (5/мин), `upload` (10/мин), `search` (15/мин), `messaging` (30/мин)
- Глобальный лимит: 100 запросов / 10 сек

### Кеширование
- **Microsoft.Extensions.Caching.Memory 9.0.10** (IMemoryCache)

### Медиа и файлы
- **SixLabors.ImageSharp.Web 3.2.0** — обработка изображений
- **AsyncImageLoader.Avalonia 3.5.0**
- Статические файлы через middleware

### Распознавание речи
- **Vosk 0.3.38** — офлайн STT (Speech-to-Text)
- **NAudio 2.2.1** — работа с аудио

### Документация API
- **Swashbuckle.AspNetCore 6.6.2** (Swagger)
- Доступен только в Development

### Конфигурация
- **Microsoft.Extensions.Configuration 9.0.10**
- **User Secrets** для локальной разработки
- Поддержка JSON-конфигурации

### Деплой
- **Dockerfile** включён в проект
- Поддержка отключения HTTPS-редиректа (`DisableHttpsRedirection`)
- Security-заголовки: COEP, COOP, CORP, X-Content-Type-Options

---

## Desktop Client (MessengerDesktop)

### UI Framework
- **Avalonia UI 11.3.10**
- **Avalonia.Themes.Fluent** — Fluent Design тема
- **Avalonia.Fonts.Inter** — шрифт Inter
- **Avalonia.Desktop** — десктоп-платформа
- **Avalonia.X11** — поддержка Linux
- **Avalonia.Diagnostics** — только для Debug
- **Compiled Bindings** включены по умолчанию

### MVVM
- **CommunityToolkit.Mvvm 8.4.0** — ObservableObject, RelayCommand и т.д.

### Целевые платформы
- `win-x64`, `linux-x64`
- Self-contained: `IncludeNativeLibrariesForSelfExtract = true`

### HTTP и Real-time
- **HttpClient** (встроенный) — через ApiClientService
- **Microsoft.AspNetCore.SignalR.Client 10.0.1**

### Локальная база данных
- **sqlite-net-pcl 1.9.172** — SQLite ORM
- **SQLitePCLRaw.bundle_e_sqlite3 2.1.11** — нативный SQLite

### Аудио
- **NAudio 2.2.1** — запись и воспроизведение

### Сериализация
- **Newtonsoft.Json 13.0.4**
- **System.IdentityModel.Tokens.Jwt 8.15.0** — парсинг JWT на клиенте

### Графика
- **SkiaSharp 3.119.1**
- **AsyncImageLoader.Avalonia 3.5.0** — асинхронная загрузка изображений

### DI
- **Microsoft.Extensions.DependencyInjection** (встроенный)
- **Scrutor 7.0.0** — автоматическая регистрация сервисов (scan assemblies)

### Уведомления
- **Custom in-app notification host** — собственные overlay-уведомления поверх `MainWindow` для Windows/Linux

### Дополнительно
- **Avalonia.Markup.Declarative 11.1.3**

---

## Shared Library (MessengerShared)

### Тип
- .NET 8.0 Class Library

### Зависимости
- Прямых runtime-зависимостей на `Npgsql` нет; библиотека остаётся переносимой и не знает о Postgres-специфике

### Содержимое
- DTO-контракты (Auth, Chat, Message, User, Poll, и др.)
- DTO и enum-ы без прямых зависимостей на `Npgsql`; Postgres-маппинг для `ChatRole`, `ChatType`, `TranscriptionStatus` вынесен в `MessengerAPI`
- Enum-ы с JSON-сериализацией (SystemEventType, Theme)
- ApiResponse / ApiResponseHelper — обёртка ответов

---

## Сводная таблица ключевых зависимостей

| Компонент | Технология | Версия |
|-----------|-----------|--------|
| Runtime | .NET | 8.0 |
| Web Framework | ASP.NET Core | 8.0 |
| UI Framework | Avalonia UI | 11.3.10 |
| MVVM Toolkit | CommunityToolkit.Mvvm | 8.4.0 |
| ORM (сервер) | Entity Framework Core | 9.0.8 |
| ORM (клиент) | sqlite-net-pcl | 1.9.172 |
| БД (сервер) | PostgreSQL + Npgsql | 9.0.4 |
| БД (клиент) | SQLite | e_sqlite3 |
| Real-time | SignalR | Server 1.2.0 / Client 10.0.1 |
| Auth | JWT Bearer | 8.0.22 |
| Passwords | BCrypt.Net-Next | 4.0.3 |
| STT | Vosk | 0.3.38 |
| Audio | NAudio | 2.2.1 |
| Images | SixLabors.ImageSharp.Web | 3.2.0 |
| API Docs | Swashbuckle (Swagger) | 6.6.2 |
| DI Scanning | Scrutor | 7.0.0 |