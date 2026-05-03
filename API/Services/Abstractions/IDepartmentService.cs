namespace API.Services.Abstractions;

public interface IDepartmentService
{
    Task<Result<List<DepartmentDto>>> GetDepartmentsAsync(CancellationToken ct = default);
    Task<Result<DepartmentDto>> GetDepartmentAsync(int id, CancellationToken ct = default);
    Task<Result<DepartmentDto>> CreateDepartmentAsync(DepartmentDto dto, CancellationToken ct = default);
    Task<Result> UpdateDepartmentAsync(int id, DepartmentDto dto, CancellationToken ct = default);
    Task<Result> DeleteDepartmentAsync(int id, CancellationToken ct = default);
    Task<Result<List<UserDto>>> GetDepartmentMembersAsync(int departmentId, CancellationToken ct = default);
    Task<Result> AddUserToDepartmentAsync(int departmentId, int userId, int requesterId, CancellationToken ct = default);
    Task<Result> RemoveUserFromDepartmentAsync(int departmentId, int userId, int requesterId, CancellationToken ct = default);
    Task<Result<bool>> CanManageDepartmentAsync(int userId, int departmentId, CancellationToken ct = default);
}