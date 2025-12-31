using MessengerAPI.Services;
using MessengerShared.DTO.Message;
using MessengerShared.Response;
using Microsoft.AspNetCore.Mvc;

namespace MessengerAPI.Controllers
{
    public class FilesController(IFileService fileService, ILogger<FilesController> logger) : BaseController<FilesController>(logger)
    {
        [HttpPost("upload")]
        public async Task<ActionResult<ApiResponse<MessageFileDTO>>> Upload([FromQuery] int chatId, IFormFile file) => await ExecuteAsync(async () =>
        {
            if (file == null || file.Length == 0) throw new ArgumentException("Файл не предосавлен");

            return await fileService.SaveMessageFileAsync(file, chatId, Request);
        }, "Файлы загружены успешно");
    }
}