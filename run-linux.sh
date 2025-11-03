#!/bin/bash

# Запуск API
echo "Starting MessengerAPI..."
cd ./dist/linux-x64/api
./MessengerAPI &
API_PID=$!

# Ждем запуска API
sleep 5

# Запуск desktop приложения
echo "Starting MessengerDesktop..."
cd ../desktop
./MessengerDesktop &
DESKTOP_PID=$!

# Ожидание нажатия Ctrl+C
echo "Press Ctrl+C to stop both applications"
trap "kill $API_PID $DESKTOP_PID; exit" INT
wait