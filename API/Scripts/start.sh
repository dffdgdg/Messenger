#!/bin/bash
set -e

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

echo "=== Messenger Server ==="

# Проверяем .env
if [ ! -f ".env" ]; then
    echo "Ошибка: .env файл не найден."
    echo "Скопируйте .env.example в .env и заполните значения."
    exit 1
fi

# Определяем IP
EXTERNAL_IP=$(ip route get 1 | awk '{print $7; exit}')

if [ -z "$EXTERNAL_IP" ]; then
    echo "Ошибка: не удалось определить IP адрес"
    exit 1
fi

echo "IP сервера: $EXTERNAL_IP"

# Обновляем EXTERNAL_IP в .env
sed -i "s/^EXTERNAL_IP=.*/EXTERNAL_IP=$EXTERNAL_IP/" .env

# Запускаем
echo "Запускаем контейнеры..."
docker compose up -d --build

echo ""
echo "=== Готово ==="
echo "API: http://$EXTERNAL_IP:5274"
echo "Логи: docker compose logs -f api"