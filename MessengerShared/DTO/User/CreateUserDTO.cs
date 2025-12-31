namespace MessengerShared.DTO.User
{
    public class CreateUserDTO
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Surname { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Midname { get; set; }
        public int? DepartmentId { get; set; }
    }
}
