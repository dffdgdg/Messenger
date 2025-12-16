using MessengerAPI.Services;
using MessengerShared.DTO;
using MessengerShared.Response;
using Microsoft.AspNetCore.Mvc;

namespace MessengerAPI.Controllers
{
    public class FilesController(IFileService fileService, ILogger<FilesController> logger) : BaseController<FilesController>(logger)
    {
        [HttpPost("upload")]
        public async Task<ActionResult<ApiResponse<MessageFileDTO>>> Upload(
            [FromQuery] int chatId,
            IFormFile file)
        {
            return await ExecuteAsync(async () =>
            {
                if (file == null || file.Length == 0)
                    throw new ArgumentException("No file provided");

                var dto = await fileService.SaveMessageFileAsync(file, chatId, Request);
                return dto;
            }, "Файлы загружены успешно");
        }
    }
}