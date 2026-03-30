using MessengerAPI.Services.Department;
using MessengerShared.Dto.Department;

namespace MessengerAPI.Controllers;

public sealed class DepartmentsController(IDepartmentService departmentService, ILogger<DepartmentsController> logger)
    : BaseController<DepartmentsController>(logger)
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<DepartmentDto>>>> GetDepartments(CancellationToken ct)
        => await ExecuteAsync(() => departmentService.GetDepartmentsAsync(ct),"Список отделов получен");

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<DepartmentDto>>> GetDepartment(int id, CancellationToken ct)
        => await ExecuteAsync(() => departmentService.GetDepartmentAsync(id, ct));

    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<ApiResponse<DepartmentDto>>> CreateDepartment([FromBody] DepartmentDto dto, CancellationToken ct)
        => await ExecuteAsync(() => departmentService.CreateDepartmentAsync(dto, ct),"Отдел успешно создан");

    [HttpPut("{id}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> UpdateDepartment(int id, [FromBody] DepartmentDto dto, CancellationToken ct)
        => await ExecuteAsync(() => departmentService.UpdateDepartmentAsync(id, dto, ct),"Отдел успешно обновлён");

    [HttpDelete("{id}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> DeleteDepartment(int id, CancellationToken ct)
        => await ExecuteAsync(() => departmentService.DeleteDepartmentAsync(id, ct),"Отдел успешно удалён");

    [HttpGet("{id}/members")]
    public async Task<ActionResult<ApiResponse<List<UserDto>>>> GetDepartmentMembers(int id, CancellationToken ct)
        => await ExecuteAsync(() => departmentService.GetDepartmentMembersAsync(id, ct),"Список сотрудников получен");

    [HttpPost("{id}/members")]
    public async Task<IActionResult> AddUserToDepartment(int id, [FromBody] UpdateDepartmentMemberDto dto, CancellationToken ct) => await ExecuteAsync(()
        => departmentService.AddUserToDepartmentAsync(id, dto.UserId, GetCurrentUserId(), ct), "Пользователь добавлен в отдел");

    [HttpDelete("{id}/members/{userId}")]
    public async Task<IActionResult> RemoveUserFromDepartment(int id, int userId, CancellationToken ct) => await ExecuteAsync(()
        => departmentService.RemoveUserFromDepartmentAsync(id, userId, GetCurrentUserId(), ct), "Пользователь удалён из отдела");

    [HttpGet("{id}/can-manage")]
    public async Task<ActionResult<ApiResponse<bool>>> CanManageDepartment(int id, CancellationToken ct) => await ExecuteAsync(()
        => departmentService.CanManageDepartmentAsync(GetCurrentUserId(), id, ct));
}