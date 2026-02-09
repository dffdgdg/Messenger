using MessengerAPI.Services.Department;
using MessengerShared.DTO.Department;
using MessengerShared.DTO.User;
using MessengerShared.Enum;
using MessengerShared.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MessengerAPI.Controllers;

public class DepartmentController(IDepartmentService departmentService,ILogger<DepartmentController> logger) : BaseController<DepartmentController>(logger)
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<DepartmentDTO>>>> GetDepartments(CancellationToken ct)
        => await ExecuteResultAsync(() => departmentService.GetDepartmentsAsync(ct),"Список отделов получен");

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<DepartmentDTO>>> GetDepartment(int id, CancellationToken ct)
        => await ExecuteResultAsync(() => departmentService.GetDepartmentAsync(id, ct));

    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<ApiResponse<DepartmentDTO>>> CreateDepartment([FromBody] DepartmentDTO dto, CancellationToken ct)
        => await ExecuteResultAsync(() => departmentService.CreateDepartmentAsync(dto, ct),"Отдел успешно создан");

    [HttpPut("{id}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> UpdateDepartment(int id, [FromBody] DepartmentDTO dto, CancellationToken ct)
        => await ExecuteResultAsync(() => departmentService.UpdateDepartmentAsync(id, dto, ct),"Отдел успешно обновлён");

    [HttpDelete("{id}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> DeleteDepartment(int id, CancellationToken ct)
        => await ExecuteResultAsync(() => departmentService.DeleteDepartmentAsync(id, ct),"Отдел успешно удалён");

    [HttpGet("{id}/members")]
    public async Task<ActionResult<ApiResponse<List<UserDTO>>>> GetDepartmentMembers(int id, CancellationToken ct)
        => await ExecuteResultAsync(() => departmentService.GetDepartmentMembersAsync(id, ct),"Список сотрудников получен");

    [HttpPost("{id}/members")]
    public async Task<IActionResult> AddUserToDepartment(int id, [FromBody] UpdateDepartmentMemberDTO dto, CancellationToken ct)
    {
        var currentUserId = GetCurrentUserId();
        return await ExecuteResultAsync(() => departmentService.AddUserToDepartmentAsync(id, dto.UserId, currentUserId, ct),"Пользователь добавлен в отдел");
    }

    [HttpDelete("{id}/members/{userId}")]
    public async Task<IActionResult> RemoveUserFromDepartment(int id, int userId, CancellationToken ct)
    {
        var currentUserId = GetCurrentUserId();
        return await ExecuteResultAsync(() => departmentService.RemoveUserFromDepartmentAsync(id, userId, currentUserId, ct),"Пользователь удалён из отдела");
    }

    [HttpGet("{id}/can-manage")]
    public async Task<ActionResult<ApiResponse<bool>>> CanManageDepartment(int id, CancellationToken ct)
    {
        var currentUserId = GetCurrentUserId();
        return await ExecuteResultAsync(() => departmentService.CanManageDepartmentAsync(currentUserId, id, ct));
    }
}