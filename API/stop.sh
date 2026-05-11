ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

echo "Останавливаем Messenger..."
docker compose down
echo "Остановлено."