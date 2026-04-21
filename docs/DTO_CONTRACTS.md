# DTO Contracts.md

Все DTO находятся в проекте **MessengerShared** (`MessengerShared.Dto.*`).
Все API-ответы обёрнуты в `ApiResponse<T>`.

---

## ApiResponse\<T\>

```json
{
  "success": true,
  "data": { },
  "message": "Операция выполнена",
  "error": null,
  "details": null,
  "timestamp": "2025-01-01T00:00:00Z"
}
```

---

## Auth

### LoginRequest
```json
{ "username": "string", "password": "string" }
```

### AuthResponseDto
```json
{
  "id": 1,
  "username": "string",
  "displayName": "string",
  "token": "jwt_access_token",
  "refreshToken": "refresh_token_string",
  "role": "User|Head|Admin"
}
```

### TokenResponseDto
```json
{
  "token": "new_jwt_access_token",
  "refreshToken": "new_refresh_token",
  "userId": 1,
  "role": "User|Head|Admin"
}
```

### RefreshTokenRequest
```json
{ "accessToken": "expired_jwt", "refreshToken": "current_refresh_token" }
```

---

## User

### UserDto
```json
{
  "id": 1,
  "username": "ivan.petrov",
  "displayName": "Петров Иван Сергеевич",
  "name": "Иван",
  "surname": "Петров",
  "midname": "Сергеевич",
  "department": "IT отдел",
  "departmentId": 3,
  "avatar": "https://host/uploads/avatars/1.jpg",
  "isOnline": true,
  "isBanned": false,
  "lastOnline": "2025-01-01T12:00:00",
  "theme": "dark|light|system",
  "notificationsEnabled": true,
  "soundsEnabled": true
}
```

> ⚠️ `soundsEnabled` присутствует в DTO, но **не персистируется в БД** (отсутствует в модели `UserSetting`).
> Используется только на клиентской стороне. Серверная персистентность — TODO.

> ⚠️ `notificationsEnabled` в модели `UserSetting` — `bool` (non-nullable),
> в `UserDto` — `bool?` (nullable). При маппинге учитывать возможный null.

### CreateUserDto
```json
{
  "username": "string",
  "password": "string",
  "surname": "string",
  "name": "string",
  "midname": "string|null",
  "departmentId": "int|null"
}
```

### ResetPasswordAdminDto
```json
{ "newPassword": "string" }
```
> Используется в `POST /api/admin/users/{id}/reset-password` (только роль Admin).

### AvatarResponseDto
```json
{ "avatarUrl": "string" }
```

### ChangeUsernameDto
```json
{ "newUsername": "string" }
```

### ChangePasswordDto
```json
{ "currentPassword": "string", "newPassword": "string" }
```

---

## Chat

### ChatDto
```json
{
  "id": 1,
  "name": "Общий чат",
  "type": "Chat|Department|Contact|DepartmentHeads",
  "createdById": 1,
  "lastMessageDate": "2025-01-01T12:00:00",
  "avatar": "https://host/uploads/chat_avatars/1.jpg",
  "lastMessagePreview": "Привет всем!",
  "lastMessageSenderName": "Иван Петров",
  "unreadCount": 5
}
```

### ChatMemberDto
```json
{
  "chatId": 1,
  "userId": 2,
  "role": "Member|Admin|Owner",
  "joinedAt": "2025-01-01T10:00:00",
  "notificationsEnabled": true,
  "username": "ivan.petrov",
  "displayName": "Петров Иван",
  "avatar": "https://host/uploads/avatars/2.jpg"
}
```

### UpdateChatDto
```json
{ "name": "string" }
```

### UpdateChatMemberDto
```json
{ "userId": 2 }
```

### ChatNotificationSettingsDto
```json
{ "chatId": 1, "isMuted": false }
```

---

## Message

### CreateMessageRequest
```json
{
  "chatId": 1,
  "content": "Привет!",
  "replyToMessageId": null,
  "forwardedFromMessageId": null,
  "isVoiceMessage": false,
  "voiceFileUrl": null,
  "voiceFileName": null,
  "voiceContentType": null,
  "voiceFileSize": null,
  "voiceDurationSeconds": null,
  "files": []
}
```

### MessageDto
```json
{
  "id": 100,
  "chatId": 1,
  "senderId": 2,
  "senderName": "Петров Иван",
  "senderAvatarUrl": "https://host/uploads/avatars/2.jpg",
  "content": "Привет!",
  "createdAt": "2025-01-01T12:00:00",
  "isOwn": false,
  "isPrevSameSender": false,
  "editedAt": null,
  "isEdited": false,
  "isDeleted": false,
  "isPinned": false,
  "pinnedAt": null,
  "pinnedByUserId": null,

  "replyToMessageId": null,
  "replyToMessage": null,
  "forwardedFromMessageId": null,
  "forwardedFrom": null,

  "isSystemMessage": false,
  "systemEventType": null,
  "targetUserId": null,
  "targetUserName": null,

  "isVoiceMessage": false,
  "voiceDurationSeconds": null,
  "voiceFileUrl": null,
  "voiceFileName": null,
  "voiceContentType": null,
  "voiceFileSize": null,

  "poll": null,
  "files": []
}
```

**Вычисляемое свойство `isPrevSameSender`:**
- `true` если предыдущее сообщение от того же отправителя И разница < 5 минут
- Используется для группировки сообщений в UI (скрытие аватара/имени)

### MessageReplyPreviewDto
```json
{
  "id": 99,
  "senderName": "Иванов",
  "content": "Текст оригинала...",
  "senderAvatarUrl": "..."
}
```

### MessageForwardInfoDto
```json
{
  "originalMessageId": 50,
  "originalChatId": 2,
  "originalChatName": "IT отдел",
  "originalSenderName": "Сидоров"
}
```

### UpdateMessageDto
```json
{ "id": 100, "content": "Отредактированный текст" }
```

### MessageFileDto
```json
{
  "id": 1,
  "messageId": 100,
  "fileName": "document.pdf",
  "contentType": "application/pdf",
  "url": "https://host/uploads/files/document.pdf",
  "previewType": "file|image",
  "fileSize": 1048576
}
```

### PagedMessagesDto
```json
{
  "messages": [],
  "totalCount": 150,
  "hasMoreMessages": true,
  "hasNewerMessages": false,
  "currentPage": 1
}
```

---

## Poll

### CreatePollDto
```json
{
  "chatId": 1,
  "question": "Когда встреча?",
  "isAnonymous": true,
  "allowsMultipleAnswers": false,
  "closesAt": "2025-02-01T00:00:00",
  "options": [
    { "text": "Понедельник", "position": 0 },
    { "text": "Вторник", "position": 1 }
  ]
}
```

### PollDto
```json
{
  "id": 1,
  "messageId": 100,
  "isAnonymous": true,
  "allowsMultipleAnswers": false,
  "closesAt": null,
  "options": [
    { "id": 1, "text": "Понедельник", "position": 0, "votesCount": 3 }
  ],
  "selectedOptionIds": [1],
  "canVote": true
}
```

### PollVoteDto
```json
{
  "pollId": 1,
  "optionIds": [1, 2],
  "userId": 0
}
```
> `userId` перезаписывается сервером из JWT — клиент может передавать `0`.

---

## ReadReceipt

### MarkAsReadDto
```json
{ "chatId": 1, "messageId": 100 }
```
> `messageId = null` → отметить весь чат прочитанным.

### ReadReceiptResponseDto
```json
{
  "chatId": 1,
  "lastReadMessageId": 100,
  "lastReadAt": "2025-01-01T12:00:00",
  "unreadCount": 0
}
```

### AllUnreadCountsDto
```json
{
  "chats": [
    { "chatId": 1, "unreadCount": 5 },
    { "chatId": 3, "unreadCount": 2 }
  ],
  "totalUnread": 7
}
```

### ChatReadInfoDto
```json
{
  "chatId": 1,
  "lastReadMessageId": 95,
  "lastReadAt": "2025-01-01T11:00:00",
  "unreadCount": 5,
  "firstUnreadMessageId": 96
}
```

---

## Online

### OnlineStatusDto
```json
{ "userId": 1, "isOnline": true, "lastOnline": "2025-01-01T12:00:00" }
```

### OnlineUsersResponseDto
```json
{ "onlineUserIds": [1, 2, 5], "totalOnline": 3 }
```

---

## Search

### GlobalSearchMessageDto
```json
{
  "id": 100,
  "chatId": 1,
  "chatName": "IT отдел",
  "chatAvatar": "https://...",
  "chatType": "Chat|Department|Contact",
  "senderId": 2,
  "senderName": "Петров Иван",
  "content": "Текст сообщения",
  "createdAt": "2025-01-01T12:00:00",
  "highlightedContent": "Текст <mark>поиск</mark>",
  "hasFiles": false
}
```

---

## Department

### DepartmentDto
```json
{
  "id": 1,
  "name": "IT отдел",
  "parentDepartmentId": null,
  "head": 5,
  "headName": "Иванов Пётр",
  "userCount": 12
}
```

### UpdateDepartmentMemberDto
```json
{ "userId": 3 }
```

---

## Notification

### NotificationDto
```json
{
  "chatId": 1,
  "chatName": "IT отдел",
  "senderId": 2,
  "senderName": "Петров Иван",
  "preview": "Привет!",
  "type": "message|poll|mention"
}
```

> `type = "mention"` — текущий пользователь упомянут через `@username` в сообщении.

---

## Enums (строковые значения)

| Enum | Значения |
|------|----------|
| `ChatType` | `Chat`, `Department`, `Contact`, `DepartmentHeads` |
| `ChatRole` | `Member`, `Admin`, `Owner` |
| `UserRole` | `User`, `Head`, `Admin` |
| `Theme` | `light`, `dark`, `system` |
| `SystemEventType` | `chat_created`, `member_added`, `member_removed`, `member_left`, `role_changed` |
| `CallStatus` | `Ringing`, `Active`, `Ended` |
| `CallEndReason` | `Ended`, `Cancelled`, `Timeout`, `Declined` |


## Call

### CallInviteDto
```json
{
  "callId": "guid",
  "chatId": 1,
  "chatName": "IT отдел",
  "initiatorId": 2,
  "initiatorName": "Петров Иван",
  "initiatorAvatar": "https://...",
  "activeParticipantsCount": 1,
  "isGroupCall": false
}
```

### CallStateDto
```json
{
  "callId": "guid",
  "chatId": 1,
  "status": "Ringing|Active|Ended",
  "initiatorId": 2,
  "startedAt": "2025-01-01T12:00:00Z",
  "isGroupCall": false,
  "participants": [
    {
      "userId": 2,
      "displayName": "Петров Иван",
      "avatarUrl": "https://...",
      "isMuted": false,
      "isSpeaking": false
    }
  ]
}
```

### CallParticipantDto
```json
{
  "userId": 2,
  "displayName": "Петров Иван",
  "avatarUrl": "https://...",
  "isMuted": false,
  "isSpeaking": false
}
```
> `isSpeaking` присутствует в DTO, но VAD не реализован — всегда `false`.

### WebRtcSignalDto
```json
{
  "callId": "guid",
  "fromUserId": 2,
  "targetUserId": 3,
  "type": "offer|answer|ice-candidate|udp-endpoint",
  "payload": "SDP строка, JSON ICE candidate или ip:port"
}
```
> `fromUserId` заполняется **сервером** из JWT — клиент передаёт `0`.
---

## Search

### SearchMessagesQueryDto
```json
{
  "query": "строка поиска",
  "page": 1,
  "pageSize": 20,
  "senderId": null,
  "hasFiles": null,
  "hasVoice": null,
  "hasPoll": null,
  "onlyText": null,
  "dateFrom": null,
  "dateTo": null,
  "oldestFirst": false
}
```
> Используется как query-модель для `GET /api/messages/chat/{chatId}/search`.

### GlobalSearchQueryDto
```json
{
  "query": "строка поиска",
  "page": 1,
  "pageSize": 20,
  "senderId": null,
  "filterChatId": null,
  "hasFiles": null,
  "hasVoice": null,
  "hasPoll": null,
  "onlyText": null,
  "dateFrom": null,
  "dateTo": null,
  "oldestFirst": false
}
```
> Используется как query-модель для `GET /api/messages/user/{userId}/search`.
