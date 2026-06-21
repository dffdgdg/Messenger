using API.Common.Patterns;
using API.Configuration;
using API.Controllers;
using API.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Shared.Dto.Message;
using Shared.Response;
using Xunit;

namespace API.Tests.Controllers;

public class FilesControllerTests
{
    private readonly Mock<IFileService> _fileMock = new();
    private readonly FilesController _controller;
    private readonly MessengerSettings _settings = new() { MaxFileSizeBytes = 1024 };

    public FilesControllerTests()
    {
        var options = Mock.Of<IOptions<MessengerSettings>>(o => o.Value == _settings);
        _controller = new FilesController(_fileMock.Object, options, NullLogger<FilesController>.Instance);
        AuthHelper.SetUser(_controller, userId: 582);
    }

    [Fact]
    public async Task Upload_FileTooLarge_Returns400()
    {
        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.Length).Returns(2048);

        var result = await _controller.Upload(chatId: 10, fileMock.Object);

        result.Should().BeOfType<BadRequestObjectResult>()
            .Which.Value.Should().BeOfType<ApiResponse<MessageFileDto>>()
            .Which.Success.Should().BeFalse();
    }

    [Fact]
    public async Task Upload_Success_Returns200()
    {
        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.Length).Returns(512);

        _fileMock.Setup(s => s.SaveMessageFileAsync(fileMock.Object, 10, 582))
            .Returns(Result<MessageFileDto>.Success(new MessageFileDto()).AsTask());

        var result = await _controller.Upload(chatId: 10, fileMock.Object);

        result.ShouldHaveStatus(200);
    }

    [Theory]
    [InlineData("Нет доступа", 403)]
    [InlineData("Чат не найден", 404)]
    public async Task Upload_ServiceError_ReturnsCorrectStatus(string errorMessage, int expectedStatus)
    {
        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.Length).Returns(512);
        var result = ResultFactory.CreateByStatusCode<MessageFileDto>(expectedStatus, errorMessage);
        _fileMock.Setup(s => s.SaveMessageFileAsync(fileMock.Object, 10, 582))
            .Returns(result.AsTask());
        var actionResult = await _controller.Upload(chatId: 10, fileMock.Object);
        actionResult.ShouldHaveStatus(expectedStatus);
    }
}