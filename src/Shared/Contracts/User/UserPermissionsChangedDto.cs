using Shared.Enum;

namespace Shared.Contracts.User;

public class UserPermissionsChangedDto
{
    public int UserId { get; set; }
    public UserRole Role { get; set; }
    public string Reason { get; set; } = string.Empty;
}