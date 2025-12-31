using MessengerAPI.Services;
using MessengerShared.DTO;
using MessengerShared.DTO.Department;
using MessengerShared.Enum;
using MessengerShared.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MessengerAPI.Controllers
{
    public class DepartmentController(IDepartmentService departmentService, ILogger<DepartmentController> logger)
        : BaseController<DepartmentController>(logger)
    {
        [HttpGet]
        public async Task<ActionResult<ApiResponse<List<DepartmentDTO>>>> GetDepartments() => await ExecuteAsync(() => departmentService.GetDepartmentsAsync(), "Список отделов получен");

        [HttpGet("{id}")]
        public async Task<ActionResult<ApiResponse<DepartmentDTO>>> GetDepartment(int id) => await ExecuteAsync(async () =>
        {
            var department = await departmentService.GetDepartmentAsync(id);

            return department ?? throw new KeyNotFoundException($"Отдел с ID {id} не найден");
        });

        [HttpPost]
        [Authorize(Roles = nameof(UserRole.Admin))]
        public async Task<ActionResult<ApiResponse<DepartmentDTO>>> CreateDepartment([FromBody] DepartmentDTO dto) => await ExecuteAsync(async () =>
        {
            ValidateModel();

            return await departmentService.CreateDepartmentAsync(dto);
        }, "Отдел успешно создан");

        /// <summary>
        /// Обновление отдела
        /// </summary>
        [HttpPut("{id}")]
        [Authorize(Roles = nameof(UserRole.Admin))]
        public async Task<IActionResult> UpdateDepartment(int id, [FromBody] DepartmentDTO dto) => await ExecuteAsync(async () =>
        {
            ValidateModel();
            await departmentService.UpdateDepartmentAsync(id, dto);
        }, "Отдел успешно обновлён");

        [HttpDelete("{id}")]
        [Authorize(Roles = nameof(UserRole.Admin))]
        public async Task<IActionResult> DeleteDepartment(int id) => await ExecuteAsync(() => departmentService.DeleteDepartmentAsync(id), "Отдел успешно удалён");

        [HttpGet("{id}/members")]
        public async Task<ActionResult<ApiResponse<List<UserDTO>>>> GetDepartmentMembers(int id)
            => await ExecuteAsync(() => departmentService.GetDepartmentMembersAsync(id),"Список сотрудников получен");

        /// <summary>
        /// Добавить пользователя в отдел
        /// </summary>
        [HttpPost("{id}/members")]
        public async Task<IActionResult> AddUserToDepartment(int id, [FromBody] UpdateDepartmentMemberDTO dto)
        {
            var currentUserId = GetCurrentUserId();

            return await ExecuteAsync(async () =>
            {
                ValidateModel();
                await departmentService.AddUserToDepartmentAsync(id, dto.UserId, currentUserId);
            }, "Пользователь добавлен в отдел");
        }

        /// <summary>
        /// Удалить пользователя из отдела
        /// </summary>
        [HttpDelete("{id}/members/{userId}")]
        public async Task<IActionResult> RemoveUserFromDepartment(int id, int userId)
        {
            var currentUserId = GetCurrentUserId();

            return await ExecuteAsync(() => departmentService.RemoveUserFromDepartmentAsync(id, userId, currentUserId),"Пользователь удалён из отдела");
        }


        [HttpGet("{id}/can-manage")]
        public async Task<ActionResult<ApiResponse<bool>>> CanManageDepartment(int id)
        {
            var currentUserId = GetCurrentUserId();

            return await ExecuteAsync(() => departmentService.CanManageDepartmentAsync(currentUserId, id));
        }
    }
}