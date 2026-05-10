using API.Controllers;
using API.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Dto.Message;
using Xunit;

namespace API.Tests.Controllers
{
    public class MessagesControllerTests
    {
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