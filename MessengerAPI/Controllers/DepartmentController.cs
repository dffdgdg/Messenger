using MessengerAPI.Services;
using MessengerShared.DTO;
using MessengerShared.Response;
using Microsoft.AspNetCore.Mvc;

namespace MessengerAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DepartmentController(IDepartmentService departmentService, ILogger<DepartmentController> logger) : BaseController<DepartmentController>(logger)
    {
        [HttpGet]
        public async Task<ActionResult<ApiResponse<List<DepartmentDTO>>>> GetDepartments()
        {
            return await ExecuteAsync(async () =>
            {
                var departments = await departmentService.GetDepartmentsAsync();
                return departments;
            }, "Отдел успешно загружен");
        }

        [HttpPost]
        public async Task<ActionResult<DepartmentDTO>> CreateDepartment([FromBody] DepartmentDTO dto)
        {
            try
            {
                var department = await departmentService.CreateDepartmentAsync(dto);
                return CreatedAtAction(nameof(GetDepartments), new { id = department.Id }, department);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ОШИБКА создания отдела");
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateDepartment(int id, [FromBody] DepartmentDTO dto)
        {
            try
            {
                await departmentService.UpdateDepartmentAsync(id, dto);
                return NoContent();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ОШИБКА обновления отдела {DepartmentId}", id);
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteDepartment(int id)
        {
            try
            {
                await departmentService.DeleteDepartmentAsync(id);
                return NoContent();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ОШИБКА удаления отдела {DepartmentId}", id);
                return BadRequest(new { error = ex.Message });
            }
        }
    }
}