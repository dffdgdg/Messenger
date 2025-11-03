#!/bin/bash

# Очистка предыдущих сборок
rm -rf ./MessengerAPI/bin/Release/net8.0/linux-x64/
rm -rf ./MessengerDesktop/bin/Release/net8.0/linux-x64/

# Сборка API
echo "Building MessengerAPI..."
dotnet publish MessengerAPI/MessengerAPI.csproj -c Release -r linux-x64 --self-contained true

# Сборка Desktop приложения
echo "Building MessengerDesktop..."
dotnet publish MessengerDesktop/MessengerDesktop.csproj -c Release -r linux-x64 --self-contained true

# Создание директории для дистрибутива
mkdir -p ./dist/linux-x64

# Копирование файлов
cp -r ./MessengerAPI/bin/Release/net8.0/linux-x64/publish/* ./dist/linux-x64/api/
cp -r ./MessengerDesktop/bin/Release/net8.0/linux-x64/publish/* ./dist/linux-x64/desktop/

# Установка прав на выполнение
chmod +x ./dist/linux-x64/api/MessengerAPI
chmod +x ./dist/linux-x64/desktop/MessengerDesktop

echo "Build completed! Files are in ./dist/linux-x64/"