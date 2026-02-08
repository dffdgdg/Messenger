using MessengerAPI.Model;
using MessengerShared.DTO;
using MessengerShared.DTO.Chat.Poll;
using MessengerShared.DTO.Message;
using MessengerShared.DTO.User;
using MessengerShared.Enum;

namespace MessengerAPI.Helpers
{
    public static class ModelExtensions
    {
        #region User Extensions

        public static UserDTO ToDto(this User user, HttpRequest? request = null, bool? isOnline = null) => new()
        {
            Id = user.Id,
            Username = user.Username,
            DisplayName = user.FormatDisplayName(),
            Name = user.Name,
            Surname = user.Surname,
            Midname = user.Midname,
            Department = user.Department?.Name,
            DepartmentId = user.Department?.Id,
            Avatar = BuildFullUrl(user.Avatar, request),
            Theme = user.UserSetting?.Theme,
            NotificationsEnabled = user.UserSetting?.NotificationsEnabled,
            IsOnline = (bool)isOnline!,
            LastOnline = user.LastOnline
        };

        public static string FormatDisplayName(this User user)
        {
            var parts = new[] { user.Surname, user.Name, user.Midname }
                .Where(p => !string.IsNullOrWhiteSpace(p));

            var formatted = string.Join(" ", parts);
            return string.IsNullOrWhiteSpace(formatted) ? user.Username : formatted;
        }

        public static void UpdateSettings(this User user, UserDTO dto)
        {
            user.UserSetting ??= new UserSetting { UserId = user.Id };

            if (dto.Theme.HasValue)
                user.UserSetting.Theme = dto.Theme.Value;

            if (dto.NotificationsEnabled.HasValue)
                user.UserSetting.NotificationsEnabled = dto.NotificationsEnabled.Value;
        }

        public static void UpdateProfile(this User user, UserDTO dto)
        {
            if (!string.IsNullOrWhiteSpace(dto.Surname))
                user.Surname = dto.Surname.Trim();

            if (!string.IsNullOrWhiteSpace(dto.Name))
                user.Name = dto.Name.Trim();

            if (!string.IsNullOrWhiteSpace(dto.Midname))
                user.Midname = dto.Midname.Trim();

            if (dto.DepartmentId.HasValue)
                user.DepartmentId = dto.DepartmentId;

            user.UpdateSettings(dto);
        }

        #endregion

        #region Chat Extensions

        public static ChatDTO ToDto(this Chat chat, HttpRequest? request = null) => new()
        {
            Id = chat.Id,
            Name = chat.Name,
            Type = chat.Type,
            CreatedById = chat.CreatedById ?? 0,
            LastMessageDate = chat.LastMessageTime,
            Avatar = BuildFullUrl(chat.Avatar, request)
        };

        public static ChatDTO ToDto(this Chat chat, User? dialogPartner, HttpRequest? request = null)
        {
            var dto = chat.ToDto(request);

            if (chat.Type == ChatType.Contact && dialogPartner != null)
            {
                dto.Name = dialogPartner.FormatDisplayName();
                dto.Avatar = BuildFullUrl(dialogPartner.Avatar, request);
            }

            return dto;
        }

        #endregion

        #region Message Extensions


        public static MessageDTO ToDto(this Message message, int? currentUserId = null, HttpRequest? request = null)
        {
            var isDeleted = message.IsDeleted ?? false;

            var dto = new MessageDTO
            {
                Id = message.Id,
                ChatId = message.ChatId,
                SenderId = message.SenderId,
                Content = isDeleted ? "[Сообщение удалено]" : message.Content,
                CreatedAt = message.CreatedAt,
                EditedAt = message.EditedAt,
                IsEdited = message.EditedAt.HasValue && !isDeleted,
                IsDeleted = isDeleted,
                SenderName = message.Sender?.FormatDisplayName(),
                SenderAvatarUrl = BuildFullUrl(message.Sender?.Avatar, request),
                IsOwn = currentUserId.HasValue && message.SenderId == currentUserId
            };

            if (!isDeleted)
            {
                dto.Files = message.MessageFiles?.Select(f => f.ToDto(request)).ToList() ?? [];
                dto.Poll = message.Polls?.FirstOrDefault()?.ToDto(currentUserId);
            }
            else
            {
                dto.Files = [];
            }

            return dto;
        }

        #endregion

        #region MessageFile Extensions

        public static MessageFileDTO ToDto(this MessageFile file, HttpRequest? request = null) => new()
        {
            Id = file.Id,
            MessageId = file.MessageId,
            FileName = file.FileName,
            ContentType = file.ContentType,
            Url = BuildFullUrl(file.Path, request),
            PreviewType = DeterminePreviewType(file.ContentType)
        };

        #endregion

        #region Poll Extensions

        public static PollDTO ToDto(this Poll poll, int? currentUserId = null)
        {
            var selectedOptionIds = currentUserId.HasValue
                ? poll.PollOptions?
                    .SelectMany(o => o.PollVotes ?? [])
                    .Where(v => v.UserId == currentUserId)
                    .Select(v => v.OptionId)
                    .ToList() ?? []
                : [];

            return new PollDTO
            {
                Id = poll.Id,
                MessageId = poll.MessageId,
                IsAnonymous = poll.IsAnonymous ?? false,
                AllowsMultipleAnswers = poll.AllowsMultipleAnswers ?? false,
                ClosesAt = poll.ClosesAt,
                Options = poll.PollOptions?
                    .OrderBy(o => o.Position)
                    .Select(o => o.ToDto(poll.IsAnonymous ?? false))
                    .ToList() ?? [],
                SelectedOptionIds = selectedOptionIds,
                CanVote = selectedOptionIds.Count == 0
            };
        }

        public static PollOptionDTO ToDto(this PollOption option, bool isAnonymous = false) => new()
        {
            Id = option.Id,
            PollId = option.PollId,
            Text = option.OptionText,
            Position = option.Position,
            VotesCount = option.PollVotes?.Count ?? 0,
            Votes = isAnonymous
                ? []
                : option.PollVotes?.Select(v => new PollVoteDTO
                {
                    PollId = v.PollId,
                    UserId = v.UserId,
                    OptionId = v.OptionId
                }).ToList() ?? []
        };

        #endregion

        #region Helpers

        public static string? BuildFullUrl(this string? path, HttpRequest? request)
        {
            if (string.IsNullOrEmpty(path) || request == null)
                return path;

            if (path.StartsWith("http://") || path.StartsWith("https://"))
                return path;

            return $"{request.Scheme}://{request.Host}{path}";
        }

        private static string DeterminePreviewType(string? contentType)
        {
            if (string.IsNullOrEmpty(contentType))
                return "file";

            var type = contentType.ToLowerInvariant();

            return type switch
            {
                _ when type.StartsWith("image/") => "image",
                _ when type.StartsWith("video/") => "video",
                _ when type.StartsWith("audio/") => "audio",
                _ => "file"
            };
        }

        #endregion
    }
}