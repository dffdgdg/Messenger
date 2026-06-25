using API.Application.Common;
using API.Application.Configuration;
using API.Application.Features.File;
using API.Application.Features.File.Commands;
using API.Domain.Common;
using API.Tests.Helpers;
using API.Web.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Shared.Contracts.Message;
using Shared.Infrastructure;
using Xunit;

namespace API.Tests.Controllers;

public class FilesControllerTests : ControllerTestBase
{
    private readonly Mock<IFileHandlers> _handlers = new();
    private readonly Mock<ICommandHandler<UploadFileCommand, MessageFileDto>> _uploadFile = new();
    private readonly MessengerSettings _settings = new() { MaxFileSizeBytes = 1024 };
    private readonly FilesController _controller;

    public FilesControllerTests()
    {
        _handlers.Setup(h => h.UploadFile).Returns(_uploadFile.Object);

        var options = Mock.Of<IOptions<MessengerSettings>>(o => o.Value == _settings);
        _controller = new FilesController(_handlers.Object, options, NullLogger<FilesController>.Instance);
        SetUser(_controller, userId: 582);
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

        _uploadFile
            .Setup(h => h.HandleAsync(It.IsAny<UploadFileCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MessageFileDto>.Success(new MessageFileDto()));

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

        _uploadFile
            .Setup(h => h.HandleAsync(It.IsAny<UploadFileCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultFactory.CreateByStatusCode<MessageFileDto>(expectedStatus, errorMessage));

        var result = await _controller.Upload(chatId: 10, fileMock.Object);

        result.ShouldHaveStatus(expectedStatus);
    }
}