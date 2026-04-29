namespace MessengerAPI.Controllers;

public sealed class DepartmentsController(IDepartmentService department, ILogger<DepartmentsController> logger) : BaseController<DepartmentsController>(logger)
{
    [HttpGet]
    public async Task<IActionResult> GetDepartments(CancellationToken ct)
        => Map(await department.GetDepartmentsAsync(ct));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetDepartment(int id, CancellationToken ct)
        => Map(await department.GetDepartmentAsync(id, ct));

    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> CreateDepartment([FromBody] DepartmentDto dto, CancellationToken ct)
        => Map(await department.CreateDepartmentAsync(dto, ct));

    [HttpPut("{id}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> UpdateDepartment(int id, [FromBody] DepartmentDto dto, CancellationToken ct)
        => Map(await department.UpdateDepartmentAsync(id, dto, ct));

    [HttpDelete("{id}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> DeleteDepartment(int id, CancellationToken ct)
        => Map(await department.DeleteDepartmentAsync(id, ct));

    [HttpGet("{id}/members")]
    public async Task<IActionResult> GetDepartmentMembers(int id, CancellationToken ct)
        => Map(await department.GetDepartmentMembersAsync(id, ct));

    [HttpPost("{id}/members")]
    public async Task<IActionResult> AddUserToDepartment(int id, [FromBody] UpdateDepartmentMemberDto dto, CancellationToken ct)
        => Map(await department.AddUserToDepartmentAsync(id, dto.UserId, GetCurrentUserId(), ct));

    [HttpDelete("{id}/members/{userId}")]
    public async Task<IActionResult> RemoveUserFromDepartment(int id, int userId, CancellationToken ct)
        => Map(await department.RemoveUserFromDepartmentAsync(id, userId, GetCurrentUserId(), ct));

    [HttpGet("{id}/can-manage")]
    public async Task<IActionResult> CanManageDepartment(int id, CancellationToken ct)
        => Map(await department.CanManageDepartmentAsync(GetCurrentUserId(), id, ct));
}