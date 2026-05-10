# Развёртывание Messenger

## Требования
- Docker Desktop (Windows) или Docker + Docker Compose (Linux)
- Порты 5274 (TCP) и 5275 (UDP) должны быть свободны

## Первый запуск

### 1. Создать .env файл
Скопировать `.env.example` в `.env` (в корне проекта, рядом с docker-compose.yml):
```
cp .env.example .env
```
Заполнить значения в `.env`:
```
POSTGRES_PASSWORD=придумайте_пароль
JWT_SECRET=любая_строка_от_32_символов
```
`EXTERNAL_IP` — не трогать, скрипт заполнит сам.

### 2. Запустить

**Windows** (PowerShell от имени администратора):
```powershell
.\scripts\start.ps1
```

**Linux**:
```bash
chmod +x scripts/start.sh
./scripts/start.sh
```

### 3. Проверить
Открыть в браузере: `http://<IP_сервера>:5274`
Должно показать: `Messenger API is running`

## Остановка

**Windows:**
```powershell
.\scripts\stop.ps1
```

**Linux:**
```bash
./scripts/stop.sh
```

## Настройка клиента
Клиент (Desktop) находит сервер автоматически через UDP.
Если не нашёл — прописать вручную в `appsettings.json`:
```json
{
  "ApiUrl": "http://192.168.1.100:5274/"
}
```

## Логи
```bash
docker compose logs -f api
docker compose logs -f db
```