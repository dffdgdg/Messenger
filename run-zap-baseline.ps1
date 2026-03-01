$ErrorActionPreference = 'Stop'

Set-Location -Path (Join-Path $PSScriptRoot '..')

if (-not (Test-Path 'security-reports')) {
    New-Item -Path 'security-reports' -ItemType Directory | Out-Null
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Write-Host 'Docker не найден в PATH. Установите Docker Desktop и перезапустите PowerShell.' -ForegroundColor Red
    exit 1
}

Write-Host '[1/2] Поднимаю API + PostgreSQL...' -ForegroundColor Cyan
docker compose -f docker-compose.server.yml up -d postgres messenger-api

Write-Host '[2/2] Запускаю OWASP ZAP Baseline scan...' -ForegroundColor Cyan
docker compose -f docker-compose.server.yml --profile security run --rm zap-baseline

Write-Host 'Готово. Отчеты лежат в папке: security-reports/' -ForegroundColor Green
