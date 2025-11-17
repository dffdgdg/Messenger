using MessengerShared.Enum;

namespace MessengerShared.DTO
{
    public class UserDTO
    {
        public int Id { get; set; }
        public string? Username { get; set; }
        public string? DisplayName { get; set; }
        public string? Department { get; set; }
        public int? DepartmentId { get; set; }
        public string? Avatar { get; set; }

        public Theme? Theme { get; set; }
        public bool? NotificationsEnabled { get; set; }
        public bool? CanBeFoundInSearch { get; set; }
    }
}
