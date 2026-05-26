$ErrorActionPreference = "Stop"
$Root = Split-Path $PSScriptRoot -Parent
Set-Location $Root

Write-Host "=== ВнутрьСеть Server ===" -ForegroundColor Cyan

if (-not (Test-Path ".env")) {
    Write-Error ".env файл не найден."
    exit 1
}

# Ищем реальный IP: 192.168.x.x (кроме 192.168.56.x VirtualBox)
$EXTERNAL_IP = (Get-NetIPAddress -AddressFamily IPv4 `
    | Where-Object {
        $_.IPAddress -match '^192\.168\.' -and
        $_.IPAddress -notmatch '^192\.168\.56\.' -and
        $_.InterfaceAlias -notmatch 'VirtualBox|Hyper-V|WSL|vEthernet|Bluetooth|Teredo'
    } | Select-Object -First 1).IPAddress

# Если нет — ищем 10.x.x.x (корпоративная сеть)
if (-not $EXTERNAL_IP) {
    $EXTERNAL_IP = (Get-NetIPAddress -AddressFamily IPv4 `
        | Where-Object {
            $_.IPAddress -match '^10\.' -and
            $_.InterfaceAlias -match 'Wi-Fi|Ethernet|Беспроводная|Подключение' -and
            $_.InterfaceAlias -notmatch 'VirtualBox|Hyper-V|WSL|vEthernet|Bluetooth|Teredo'
        } | Select-Object -First 1).IPAddress
}

# Если ничего — localhost
if (-not $EXTERNAL_IP) {
    Write-Host "Не удалось определить внешний IP, использую 127.0.0.1" -ForegroundColor Yellow
    $EXTERNAL_IP = "127.0.0.1"
}

Write-Host "IP сервера: $EXTERNAL_IP" -ForegroundColor Green

# Обновляем .env
$envContent = Get-Content ".env"
if ($envContent -match '^EXTERNAL_IP=') {
    $envContent = $envContent -replace '^EXTERNAL_IP=.*', "EXTERNAL_IP=$EXTERNAL_IP"
} else {
    $envContent += "`nEXTERNAL_IP=$EXTERNAL_IP"
}
$envContent | Set-Content ".env"

# Запускаем
docker compose up -d --build

Write-Host ""
Write-Host "=== Готово ===" -ForegroundColor Green
Write-Host "API: http://${EXTERNAL_IP}:5274" -ForegroundColor Cyan
Write-Host "Логи: docker compose logs -f api" -ForegroundColor Gray