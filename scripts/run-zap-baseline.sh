#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

mkdir -p security-reports

if ! command -v docker >/dev/null 2>&1; then
  echo "Docker не найден. Установите Docker Desktop или Docker Engine и повторите попытку."
  exit 1
fi

echo "[1/2] Поднимаю API + PostgreSQL..."
docker compose -f docker-compose.server.yml up -d postgres messenger-api

echo "[2/2] Запускаю OWASP ZAP Baseline scan..."
docker compose -f docker-compose.server.yml --profile security run --rm zap-baseline

echo "Готово. Отчеты лежат в папке: security-reports/"