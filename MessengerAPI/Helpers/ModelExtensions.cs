using MessengerAPI.Model;
using MessengerShared.DTO;
using MessengerShared.Enum;

namespace MessengerAPI.Helpers
{
    public static class ModelExtensions
    {
        public static UserDTO ToDto(this User user, HttpRequest? request = null)
        {
            var dto = new UserDTO
            {
                Id = user.Id,
                Username = user.Username,
                DisplayName = user.DisplayName,
                Department = user.DepartmentNavigation?.Name,
                DepartmentId = user.DepartmentNavigation?.Id,
                Avatar = GetFullAvatarUrl(user.Avatar, request),
                Theme = user.UserSetting?.Theme.ToSharedTheme(),
                NotificationsEnabled = user.UserSetting?.NotificationsEnabled,
                CanBeFoundInSearch = user.UserSetting?.CanBeFoundInSearch
            };

            return dto;
        }

        public static ChatDTO ToDto(this Chat chat, HttpRequest? request = null)
        {
            return new ChatDTO
            {
                Id = chat.Id,
                Name = chat.Name,
                IsGroup = chat.IsGroup,
                CreatedById = chat.CreatedById ?? 0,
                LastMessageDate = chat.LastMessageTime,
                Avatar = GetFullAvatarUrl(chat.Avatar, request)
            };
        }

        public static void UpdateSettings(this User user, UserDTO userDto)
        {
            if (user.UserSetting == null)
            {
                user.UserSetting = new UserSetting
                {
                    UserId = user.Id,
                    Theme = userDto.Theme.ToModelTheme(),
                    NotificationsEnabled = userDto.NotificationsEnabled ?? true,
                    CanBeFoundInSearch = userDto.CanBeFoundInSearch ?? true
                };
            }
            else
            {
                user.UserSetting.Theme = userDto.Theme.ToModelTheme() ?? user.UserSetting.Theme;
                user.UserSetting.NotificationsEnabled = userDto.NotificationsEnabled ?? user.UserSetting.NotificationsEnabled;
                user.UserSetting.CanBeFoundInSearch = userDto.CanBeFoundInSearch ?? user.UserSetting.CanBeFoundInSearch;
            }
        }

        private static string? GetFullAvatarUrl(string? avatarPath, HttpRequest? request)
        {
            if (string.IsNullOrEmpty(avatarPath) || request == null)
                return avatarPath;

            return $"{request.Scheme}://{request.Host}{avatarPath}";
        }

        public static Theme? ToSharedTheme(this Theme? theme)
        {
            if (!theme.HasValue) return null;

            return theme.Value switch
            {
                Theme.light => Theme.light,
                Theme.dark => Theme.dark,
                Theme.system => Theme.system,
                _ => Theme.system
            };
        }

        public static Theme? ToModelTheme(this Theme? theme)
        {
            if (!theme.HasValue) return null;

            return theme.Value switch
            {
                Theme.light => Theme.light,
                Theme.dark => Theme.dark,
                Theme.system => Theme.system,
                _ => Theme.system
            };
        }

        public static Theme? ToSharedTheme(this Theme theme)
        {
            return theme switch
            {
                Theme.light => Theme.light,
                Theme.dark => Theme.dark,
                Theme.system => Theme.system,
                _ => Theme.system
            };
        }

        public static Theme? ToModelTheme(this Theme theme)
        {
            return theme switch
            {
                Theme.light => Theme.light,
                Theme.dark => Theme.dark,
                Theme.system => Theme.system,
                _ => Theme.system
            };
        }
    }
}