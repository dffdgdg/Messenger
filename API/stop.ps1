$Root = Split-Path $PSScriptRoot -Parent
Set-Location $Root

Write-Host "Останавливаем Messenger..." -ForegroundColor Yellow
docker compose down
Write-Host "Остановлено." -ForegroundColor Green