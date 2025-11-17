using MessengerAPI.Services;
using MessengerShared.DTO;
using MessengerShared.Response;
using Microsoft.AspNetCore.Mvc;

namespace MessengerAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FilesController(IFileService fileService, ILogger<FilesController> logger) : ControllerBase
{
    [HttpPost("upload")]
    public async Task<ActionResult<ApiResponse<MessageFileDTO>>> Upload([FromQuery] int chatId, IFormFile file)
    {
        try
        {
            if (file == null || file.Length == 0)
                return BadRequest(new ApiResponse { Success = false, Error = "No file" });

            var dto = await fileService.SaveMessageFileAsync(file, chatId, Request);
            return Ok(new ApiResponse<MessageFileDTO> { Success = true, Data = dto });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "File upload failed");
            return StatusCode(500, new ApiResponse { Success = false, Error = ex.Message });
        }
    }
}
