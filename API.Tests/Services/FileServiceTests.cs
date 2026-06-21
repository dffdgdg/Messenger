using API.Common.Patterns;
using API.Configuration;
using API.Data;
using API.Services.Abstractions;
using API.Services.Messaging;
using API.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Shared.Dto.Message;
using Xunit;

namespace API.Tests.Services;

public class FileServiceTests : IDisposable
{
    private readonly MessengerDbContext _context;
    private readonly Mock<IAccessControlService> _accessMock = new();
    private readonly Mock<IWebHostEnvironment> _envMock = new();
    private readonly Mock<IUrlBuilder> _urlMock = new();
    private readonly FileService _service;

    public FileServiceTests()
    {
        _context = DbContextFactory.Create();
        _envMock.Setup(e => e.WebRootPath).Returns(Path.GetTempPath());

        _service = new FileService(
            _context,
            _accessMock.Object,
            _envMock.Object,
            _urlMock.Object,
            Options.Create(new MessengerSettings { MaxFileSizeBytes = 1024, MaxImageDimension = 100, ImageQuality = 85 }),
            NullLogger<FileService>.Instance);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public void IsValidImage_NullFile_ReturnsFalse()
    {
        _service.IsValidImage(null!).Should().BeFalse();
    }

    [Fact]
    public void IsValidImage_EmptyFile_ReturnsFalse()
    {
        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.Length).Returns(0);
        _service.IsValidImage(fileMock.Object).Should().BeFalse();
    }

    [Fact]
    public void IsValidImage_TooLarge_ReturnsFalse()
    {
        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.Length).Returns(2048);
        _service.IsValidImage(fileMock.Object).Should().BeFalse();
    }

    [Fact]
    public void IsValidImage_WrongContentType_ReturnsFalse()
    {
        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.Length).Returns(100);
        fileMock.Setup(f => f.ContentType).Returns("text/plain");
        _service.IsValidImage(fileMock.Object).Should().BeFalse();
    }

    [Fact]
    public void IsValidImage_CorrectType_ReturnsTrue()
    {
        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.Length).Returns(100);
        fileMock.Setup(f => f.ContentType).Returns("image/png");
        _service.IsValidImage(fileMock.Object).Should().BeTrue();
    }

    [Fact]
    public async Task SaveMessageFile_NoAccess_ReturnsForbidden()
    {
        _accessMock.Setup(a => a.IsMemberAsync(1, 10)).ReturnsAsync(false);
        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.Length).Returns(100);

        var result = await _service.SaveMessageFileAsync(fileMock.Object, 10, 1);

        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.Forbidden);
    }

    [Fact]
    public async Task SaveMessageFile_NullFile_ReturnsFailure()
    {
        _accessMock.Setup(a => a.IsMemberAsync(1, 10)).ReturnsAsync(true);

        var result = await _service.SaveMessageFileAsync(null!, 10, 1);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("не предоставлен");
    }

    [Fact]
    public async Task SaveMessageFile_TooLarge_ReturnsFailure()
    {
        _accessMock.Setup(a => a.IsMemberAsync(1, 10)).ReturnsAsync(true);
        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.Length).Returns(2048);

        var result = await _service.SaveMessageFileAsync(fileMock.Object, 10, 1);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("слишком большой");
    }

    [Fact]
    public void DeleteFile_NullPath_DoesNothing()
    {
        _service.Invoking(s => s.DeleteFile(null))
            .Should().NotThrow();
    }

    [Fact]
    public void DeleteFile_EmptyPath_DoesNothing()
    {
        _service.Invoking(s => s.DeleteFile(""))
            .Should().NotThrow();
    }
}