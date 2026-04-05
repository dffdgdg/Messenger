# Glossary

## Роли пользователей (`UserRole`)
| Термин | Описание |
- `User` — обычный пользователь.
- `Head` — руководитель подразделения.
- `Admin` — администратор системы.

## Роли в чатах (`ChatRole`)
- `Member` — участник.
- `Admin` — администратор чата.
- `Owner` — владелец чата.

## Типы чатов (`ChatType`)
- `Chat` — обычный групповой чат.
- `Department` — чат отдела.
- `Contact` — 1:1 диалог.
- `DepartmentHeads` — чат руководителей.

## Realtime и инфраструктура
- `ChatHub` — SignalR hub (`/chatHub`).
- `HubNotifier` — backend-сервис доставки hub-событий из бизнес-слоя.
- `GlobalHubConnection` — desktop singleton SignalR-клиент.
- `OnlineUserService` — backend трекинг online-состояния и connection-id.
- `AccessControlService` — проверки доступа (membership/roles/permissions).

## Read-state
- **Read receipt** — состояние прочтения в `ChatMember` (`LastReadMessageId`, `LastReadAt`).
- **Unread count** — расчёт непрочитанных сообщений на стороне сервисов/API/Hub.
