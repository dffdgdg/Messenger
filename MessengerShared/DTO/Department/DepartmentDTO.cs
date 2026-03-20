namespace MessengerShared.Dto.Department;

public class DepartmentDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? ParentDepartmentId { get; set; }
    public int? Head { get; set; }
    public string? HeadName { get; set; }
    public int UserCount { get; set; }
}