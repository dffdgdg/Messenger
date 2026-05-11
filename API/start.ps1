$ErrorActionPreference = "Stop"

$Root = Split-Path $PSScriptRoot -Parent
Set-Location $Root

Write-Host "=== ВнутрьСеть Server ===" -ForegroundColor Cyan

# Проверяем .env
if (-not (Test-Path ".env")) {
    Write-Error ".env файл не найден. Скопируйте .env.example в .env и заполните."
    exit 1
}

# Определяем IP
$EXTERNAL_IP = (Get-NetIPAddress -AddressFamily IPv4 `
    | Where-Object {
        $_.IPAddress -notmatch '^127\.' -and
        $_.IPAddress -notmatch '^169\.254\.' -and
        $_.IPAddress -notmatch '^172\.'
    } `
    | Select-Object -First 1).IPAddress

if (-not $EXTERNAL_IP) {
    Write-Error "Не удалось определить IP адрес"
    exit 1
}

Write-Host "IP сервера: $EXTERNAL_IP" -ForegroundColor Green

# Обновляем EXTERNAL_IP в .env
$envContent = Get-Content ".env"
$envContent = $envContent -replace '^EXTERNAL_IP=.*', "EXTERNAL_IP=$EXTERNAL_IP"
$envContent | Set-Content ".env"

# Запускаем
Write-Host "Запускаем контейнеры..." -ForegroundColor Yellow

$previousErrorAction = $ErrorActionPreference
$ErrorActionPreference = "Continue"
docker compose up -d --build
$ErrorActionPreference = $previousErrorAction

if ($LASTEXITCODE -ne 0) {
    Write-Error "Ошибка запуска Docker"
    exit 1
}

Write-Host ""
Write-Host "=== Готово ===" -ForegroundColor Green
Write-Host "API: http://${EXTERNAL_IP}:5274" -ForegroundColor Cyan
Write-Host "Логи: docker compose logs -f api" -ForegroundColor Gray