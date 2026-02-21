using MessengerAPI.Model;
using MessengerAPI.Services.Infrastructure;
using MessengerShared.DTO.User;

namespace MessengerAPI.Mapping;

public static class UserMappings
{
    public static UserDTO ToDto(
        this User user,
        IUrlBuilder? urlBuilder = null,
        bool? isOnline = null) => new()
        {
            Id = user.Id,
            Username = user.Username,
            DisplayName = user.FormatDisplayName(),
            Name = user.Name,
            Surname = user.Surname,
            Midname = user.Midname,
            Department = user.Department?.Name,
            DepartmentId = user.Department?.Id,
            Avatar = user.Avatar.BuildFullUrl(urlBuilder),
            Theme = user.UserSetting?.Theme,
            NotificationsEnabled = user.UserSetting?.NotificationsEnabled,
            IsOnline = isOnline ?? false,
            LastOnline = user.LastOnline,
            IsBanned = user.IsBanned
        };

    public static string FormatDisplayName(this User user)
    {
        var parts = new[] { user.Surname, user.Name, user.Midname }
            .Where(p => !string.IsNullOrWhiteSpace(p));

        var formatted = string.Join(" ", parts);
        return string.IsNullOrWhiteSpace(formatted) ? user.Username : formatted;
    }

    public static void UpdateProfile(this User user, UserDTO dto)
    {
        if (!string.IsNullOrWhiteSpace(dto.Surname))
            user.Surname = dto.Surname.Trim();

        if (!string.IsNullOrWhiteSpace(dto.Name))
            user.Name = dto.Name.Trim();

        user.Midname = dto.Midname?.Trim();

        user.UpdateSettings(dto);
    }

    public static void UpdateSettings(this User user, UserDTO dto)
    {
        user.UserSetting ??= new UserSetting { UserId = user.Id };

        if (dto.Theme.HasValue)
            user.UserSetting.Theme = dto.Theme.Value;

        if (dto.NotificationsEnabled.HasValue)
            user.UserSetting.NotificationsEnabled = dto.NotificationsEnabled.Value;
    }
}