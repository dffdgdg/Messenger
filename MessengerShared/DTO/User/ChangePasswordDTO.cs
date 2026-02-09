namespace MessengerShared.DTO.User;

public class ChangePasswordDTO
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}