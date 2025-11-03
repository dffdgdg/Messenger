using MessengerAPI.Model;
using MessengerShared.DTO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MessengerAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DepartmentController(MessengerDbContext context) : ControllerBase
    {
        private readonly MessengerDbContext _context = context;

        [HttpGet]
        public async Task<ActionResult<IEnumerable<DepartmentDTO>>> GetDepartments()
        {
            var deps = await _context.Departments
                .Select(d => new DepartmentDTO { Id = d.Id, Name = d.Name, ParentDepartmentId = d.ParentDepartmentId })
                .ToListAsync();
            return Ok(deps);
        }

        [HttpPost]
        public async Task<ActionResult<DepartmentDTO>> CreateDepartment(DepartmentDTO dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest("Name is required");

            if (dto.ParentDepartmentId <= 0)
            {
                dto.ParentDepartmentId = null;
            }

            if (dto.ParentDepartmentId.HasValue)
            {
                var parentExists = await _context.Departments.AnyAsync(d => d.Id == dto.ParentDepartmentId.Value);
                if (!parentExists)
                    return BadRequest("Parent department does not exist");
            }

            var entity = new Department { Name = dto.Name, ParentDepartmentId = dto.ParentDepartmentId };
            _context.Departments.Add(entity);
            await _context.SaveChangesAsync();
            dto.Id = entity.Id;
            return CreatedAtAction(nameof(GetDepartments), new { id = dto.Id }, dto);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateDepartment(int id, DepartmentDTO dto)
        {
            var entity = await _context.Departments.FindAsync(id);
            if (entity == null) return NotFound();
            if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest("Name is required");

            if (dto.ParentDepartmentId <= 0)
            {
                dto.ParentDepartmentId = null;
            }

            if (dto.ParentDepartmentId == id)
                return BadRequest("Department cannot be its own parent");

            if (dto.ParentDepartmentId.HasValue)
            {
                var parentExists = await _context.Departments.AnyAsync(d => d.Id == dto.ParentDepartmentId.Value);
                if (!parentExists)
                    return BadRequest("Parent department does not exist");

                var childDepartments = await GetAllChildDepartmentsAsync(id);
                if (childDepartments.Contains(dto.ParentDepartmentId.Value))
                    return BadRequest("Cannot set a child department as parent (circular reference)");
            }

            entity.Name = dto.Name;
            entity.ParentDepartmentId = dto.ParentDepartmentId;

            try
            {
                await _context.SaveChangesAsync();
                return NoContent();
            }
            catch (DbUpdateException)
            {
                return BadRequest("Invalid parent department");
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteDepartment(int id)
        {
            var entity = await _context.Departments.FindAsync(id);
            if (entity == null) return NotFound();

            var hasChildren = await _context.Departments.AnyAsync(d => d.ParentDepartmentId == id);
            if (hasChildren) return BadRequest("Department has child departments");

            var hasUsers = await _context.Users.AnyAsync(u => u.Department == id);
            if (hasUsers) return BadRequest("Department has users");

            _context.Departments.Remove(entity);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        private async Task<HashSet<int>> GetAllChildDepartmentsAsync(int departmentId)
        {
            var children = new HashSet<int>();
            var departments = await _context.Departments.ToListAsync();
            
            void AddChildren(int parentId)
            {
                var directChildren = departments
                    .Where(d => d.ParentDepartmentId == parentId)
                    .Select(d => d.Id);

                foreach (var childId in directChildren)
                {
                    if (children.Add(childId)) 
                    {
                        AddChildren(childId);
                    }
                }
            }

            AddChildren(departmentId);
            return children;
        }
    }
}
