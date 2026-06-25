using API.Application.Features.Chat;
using API.Application.Features.Department.Commands;
using API.Application.Features.Department.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Contracts.Department;

namespace API.Web.Controllers;

[ApiController]
[Route("api/departments")]
public sealed class DepartmentsController(IDepartmentHandlers handlers, ILogger<DepartmentsController> logger)
    : BaseController<DepartmentsController>(logger)
{
    [HttpGet]
    public async Task<IActionResult> GetDepartments(CancellationToken ct)
        => Map(await handlers.GetDepartments.HandleAsync(new GetDepartmentsQuery(), ct));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetDepartment(int id, CancellationToken ct)
        => Map(await handlers.GetDepartment.HandleAsync(new GetDepartmentQuery(id), ct));

    [HttpPost]
    [Authorize(Policy = "IsAdmin")]
    public async Task<IActionResult> CreateDepartment([FromBody] DepartmentDto dto, CancellationToken ct)
        => Map(await handlers.CreateDepartment.HandleAsync(new CreateDepartmentCommand(dto), ct));

    [HttpPut("{id}")]
    [Authorize(Policy = "IsAdmin")]
    public async Task<IActionResult> UpdateDepartment(int id, [FromBody] DepartmentDto dto, CancellationToken ct)
        => Map(await handlers.UpdateDepartment.HandleAsync(new UpdateDepartmentCommand(id, dto), ct));

    [HttpDelete("{id}")]
    [Authorize(Policy = "IsAdmin")]
    public async Task<IActionResult> DeleteDepartment(int id, CancellationToken ct)
        => Map(await handlers.DeleteDepartment.HandleAsync(new DeleteDepartmentCommand(id), ct));

    [HttpGet("{id}/members")]
    public async Task<IActionResult> GetDepartmentMembers(int id, CancellationToken ct)
        => Map(await handlers.GetMembers.HandleAsync(new GetDepartmentMembersQuery(id), ct));

    [HttpPost("{id}/members")]
    public async Task<IActionResult> AddUserToDepartment(int id, [FromBody] UpdateDepartmentMemberDto dto, CancellationToken ct)
        => Map(await handlers.AddUserToDepartment.HandleAsync(new AddUserToDepartmentCommand(id, dto.UserId, GetCurrentUserId()), ct));

    [HttpDelete("{id}/members/{userId}")]
    public async Task<IActionResult> RemoveUserFromDepartment(int id, int userId, CancellationToken ct)
        => Map(await handlers.RemoveUser.HandleAsync(new RemoveUserFromDepartmentCommand(id, userId, GetCurrentUserId()), ct));

    [HttpGet("{id}/can-manage")]
    public async Task<IActionResult> CanManageDepartment(int id, CancellationToken ct)
        => Map(await handlers.CanManage.HandleAsync(new CanManageDepartmentQuery(GetCurrentUserId(), id), ct));
}