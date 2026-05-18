using API.Controllers;
using API.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Dto.Message;
using Shared.Dto.Search;
using Shared.DTO.Message;
using Shared.Response;
using Xunit;

namespace API.Tests.Controllers
{
    public class MessagesControllerTests
    {
        [Fact]
        public async Task DeleteMessage_CallsServiceWithCurrentUserId()
        {
            // TC10: Удаление сообщения — проверка что userId берётся из токена, а не из запроса
            var mock = new Mock<IMessageService>();
            var controller = new MessagesController(mock.Object, NullLogger<MessagesController>.Instance);
            AuthHelper.SetUser(controller, userId: 42);

            mock.Setup(x => x.DeleteMessageAsync(7, 42))
                .ReturnsAsync(Result.Success());

            var result = await controller.DeleteMessage(7);

            result.Should().BeOfType<OkObjectResult>();
            mock.Verify(x => x.DeleteMessageAsync(7, 42), Times.Once);
        }

        [Fact]
        public async Task DeleteMessage_ServiceReturnsForbidden_Returns403()
        {
            // TC11: Попытка удалить чужое сообщение — сервис вернул Forbidden
            var mock = new Mock<IMessageService>();
            var controller = new MessagesController(mock.Object, NullLogger<MessagesController>.Instance);
            AuthHelper.SetUser(controller, userId: 1);

            mock.Setup(x => x.DeleteMessageAsync(99, 1))
                .ReturnsAsync(Result.Forbidden("Нет прав для удаления этого сообщения"));

            var result = await controller.DeleteMessage(99);

            var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
            objectResult.StatusCode.Should().Be(403);
            var body = objectResult.Value.Should().BeOfType<ApiResponse<object>>().Subject;
            body.Success.Should().BeFalse();
        }

        [Fact]
        public async Task PinMessage_ServiceReturnsNotFound_Returns404()
        {
            // TC12: Закрепление несуществующего сообщения → 404
            var mock = new Mock<IMessageService>();
            var controller = new MessagesController(mock.Object, NullLogger<MessagesController>.Instance);
            AuthHelper.SetUser(controller, userId: 1);

            mock.Setup(x => x.PinMessageAsync(999, 1))
                .ReturnsAsync(Result<MessageDto>.NotFound("Сообщение не найдено"));

            var result = await controller.PinMessage(999);

            var objectResult = result.Should().BeOfType<NotFoundObjectResult>().Subject;
            objectResult.StatusCode.Should().Be(404);
            var body = objectResult.Value.Should().BeOfType<ApiResponse<MessageDto>>().Subject;
            body.Success.Should().BeFalse();
        }

        [Fact]
        public async Task GlobalSearch_AnotherUserId_Returns403WithoutCallingService()
        {
            // TC13: Глобальный поиск от чужого имени → 403, сервис не вызывается
            var mock = new Mock<IMessageService>();
            var controller = new MessagesController(mock.Object, NullLogger<MessagesController>.Instance);
            AuthHelper.SetUser(controller, userId: 1);

            var result = await controller.GlobalSearch(userId: 55, new GlobalSearchQueryDto());

            var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
            objectResult.StatusCode.Should().Be(403);
            mock.Verify(x => x.GlobalSearchAsync(It.IsAny<int>(), It.IsAny<GlobalSearchQueryDto>()), Times.Never);
        }

        
        [Fact]
        public async Task UpdateMessage_IdMismatch_Returns400WithoutCallingService()
        {
            var mock = new Mock<IMessageService>();
            var controller = new MessagesController(mock.Object, NullLogger<MessagesController>.Instance);
            AuthHelper.SetUser(controller, 1);

            var dto = new UpdateMessageDto
            {
                Id = 5
            };

            var result = await controller.UpdateMessage(10, dto);

            var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            badRequest.StatusCode.Should().Be(400);

            mock.Verify(x => x.UpdateMessageAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<UpdateMessageDto>()), Times.Never);
        }
    }
}