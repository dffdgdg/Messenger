using MessengerAPI.Services;
using MessengerShared.DTO;
using MessengerShared.Response;
using Microsoft.AspNetCore.Mvc;

namespace MessengerAPI.Controllers
{
    public class DepartmentController(IDepartmentService departmentService, ILogger<DepartmentController> logger) : BaseController<DepartmentController>(logger)
    {
        [HttpGet]
        public async Task<ActionResult<ApiResponse<List<DepartmentDTO>>>> GetDepartments()
        {
            return await ExecuteAsync(async () =>
            {
                var departments = await departmentService.GetDepartmentsAsync();
                return departments;
            }, "Отделы успешно загружены");
        }

        [HttpPost]
        public async Task<ActionResult<ApiResponse<DepartmentDTO>>> CreateDepartment([FromBody] DepartmentDTO dto)
        {
            return await ExecuteAsync(async () =>
            {
                ValidateModel();
                var department = await departmentService.CreateDepartmentAsync(dto);
                return department;
            }, "Отдел успешно создан");
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateDepartment(int id, [FromBody] DepartmentDTO dto)
        {
            return await ExecuteAsync(async () =>
            {
                ValidateModel();
                await departmentService.UpdateDepartmentAsync(id, dto);
            }, "Отдел успешно обновлён");
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteDepartment(int id)
        {
            return await ExecuteAsync(async () =>
            {
                await departmentService.DeleteDepartmentAsync(id);
            }, "Отдел успешно удалён");
        }
    }
}