using Messenger.Tests.Helpers;

namespace Messenger.Tests.Controllers;

public class AdminControllerTests
{
    private readonly Mock<IAdminService> _adminServiceMock;
    private readonly Mock<ILogger<AdminController>> _loggerMock;
    private readonly AdminController _controller;

    public AdminControllerTests()
    {
        _adminServiceMock = new Mock<IAdminService>();
        _loggerMock = TestHelpers.CreateLogger<AdminController>();
        _controller = new AdminController(_adminServiceMock.Object, _loggerMock.Object);
        TestHelpers.SetupControllerContext(_controller, userId: 1, role: "Admin");
    }

    #region GetUsers Tests

    [Fact]
    public async Task GetUsers_ReturnsOkWithUsers_WhenUsersExist()
    {
        // Arrange
        var expectedUsers = new List<UserDTO>
        {
            new() { Id = 1, Username = "user1", DisplayName = "User One" },
            new() { Id = 2, Username = "user2", DisplayName = "User Two" }
        };
        _adminServiceMock
            .Setup(s => s.GetUsersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedUsers);

        // Act
        var result = await _controller.GetUsers(CancellationToken.None);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ApiResponse<List<UserDTO>>>().Subject;

        response.Success.Should().BeTrue();
        response.Data.Should().HaveCount(2);
        response.Message.Should().Be("Пользователи получены успешно");
    }

    [Fact]
    public async Task GetUsers_ReturnsEmptyList_WhenNoUsersExist()
    {
        // Arrange
        _adminServiceMock.Setup(s => s.GetUsersAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        // Act
        var result = await _controller.GetUsers(CancellationToken.None);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ApiResponse<List<UserDTO>>>().Subject;

        response.Success.Should().BeTrue();
        response.Data.Should().BeEmpty();
    }

    #endregion

    #region CreateUser Tests

    [Fact]
    public async Task CreateUser_ReturnsOk_WhenValidData()
    {
        // Arrange
        var createDto = new CreateUserDTO
        {
            Username = "newuser",
            Password = "password123",
            Name = "New",
            Surname = "User"
        };
        var expectedUser = new UserDTO
        {
            Id = 10,
            Username = "newuser",
            DisplayName = "User New"
        };

        _adminServiceMock.Setup(s => s.CreateUserAsync(createDto, It.IsAny<CancellationToken>())).ReturnsAsync(expectedUser);

        // Act
        var result = await _controller.CreateUser(createDto, CancellationToken.None);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ApiResponse<UserDTO>>().Subject;

        response.Success.Should().BeTrue();
        response.Data!.Id.Should().Be(10);
        response.Message.Should().Be("Пользователь создан успешно");
    }

    [Fact]
    public async Task CreateUser_ReturnsBadRequest_WhenUsernameExists()
    {
        // Arrange
        var createDto = new CreateUserDTO
        {
            Username = "existinguser",
            Password = "password123",
            Name = "Test",
            Surname = "User"
        };

        _adminServiceMock
            .Setup(s => s.CreateUserAsync(createDto, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Пользователь с таким логином уже существует"));

        // Act
        var result = await _controller.CreateUser(createDto, CancellationToken.None);

        // Assert
        var badRequestResult = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var response = badRequestResult.Value.Should().BeOfType<ApiResponse<UserDTO>>().Subject;

        response.Success.Should().BeFalse();
        response.Error.Should().Contain("логином уже существует");
    }

    [Theory]
    [InlineData("", "password123", "Логин не может быть пустым")]
    [InlineData("user", "123", "минимум 6 символов")]
    [InlineData("user@!", "password123", "3-30 символов")]
    public async Task CreateUser_ReturnsBadRequest_WhenValidationFails(string username, string password, string expectedError)
    {
        // Arrange
        var createDto = new CreateUserDTO
        {
            Username = username,
            Password = password,
            Name = "Test",
            Surname = "User"
        };

        _adminServiceMock.Setup(s => s.CreateUserAsync(createDto, It.IsAny<CancellationToken>())).ThrowsAsync(new ArgumentException(expectedError));

        // Act
        var result = await _controller.CreateUser(createDto, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    #endregion

    #region ToggleBan Tests

    [Fact]
    public async Task ToggleBan_ReturnsOk_WhenUserExists()
    {
        // Arrange
        _adminServiceMock.Setup(s => s.ToggleBanAsync(1, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        // Act
        var result = await _controller.ToggleBan(1, CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ApiResponse<object>>().Subject;

        response.Success.Should().BeTrue();
        response.Message.Should().Be("Статус блокировки изменён");
    }

    [Fact]
    public async Task ToggleBan_ReturnsNotFound_WhenUserDoesNotExist()
    {
        _adminServiceMock.Setup(s => s.ToggleBanAsync(999, It.IsAny<CancellationToken>())).ThrowsAsync(new KeyNotFoundException("Пользователь с ID 999 не найден"));

        var result = await _controller.ToggleBan(999, CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    #endregion
}