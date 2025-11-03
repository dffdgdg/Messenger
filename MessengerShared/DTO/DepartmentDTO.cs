namespace MessengerShared.DTO
{
    public class DepartmentDTO
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int? ParentDepartmentId { get; set; }
    }
}
