using MessengerAPI.Configuration;
using MessengerAPI.Helpers;
using Microsoft.Extensions.Options;

namespace Messenger.Tests.Helpers;

public static class MockServiceFactory
{
    public static IOptions<MessengerSettings> CreateMessengerSettings(int adminDepartmentId = 2, int bcryptWorkFactor = 10) => Options.Create(new MessengerSettings
    {
        AdminDepartmentId = adminDepartmentId,
        BcryptWorkFactor = bcryptWorkFactor,
        MaxFileSizeBytes = 10 * 1024 * 1024,
        MaxImageDimension = 1920,
        ImageQuality = 80,
        DefaultPageSize = 50,
        MaxPageSize = 100
    });

    public static Mock<IAccessControlService> CreateAccessControlMock()
    {
        var mock = new Mock<IAccessControlService>();

        mock.Setup(a => a.IsMemberAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(true);
        mock.Setup(a => a.IsAdminAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(true);
        mock.Setup(a => a.IsOwnerAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(true);
        mock.Setup(a => a.EnsureIsMemberAsync(It.IsAny<int>(), It.IsAny<int>())).Returns(Task.CompletedTask);
        mock.Setup(a => a.EnsureIsAdminAsync(It.IsAny<int>(), It.IsAny<int>())).Returns(Task.CompletedTask);
        mock.Setup(a => a.EnsureIsOwnerAsync(It.IsAny<int>(), It.IsAny<int>())).Returns(Task.CompletedTask);

        return mock;
    }

    public static Mock<IOnlineUserService> CreateOnlineUserServiceMock()
    {
        var mock = new Mock<IOnlineUserService>();
        mock.Setup(s => s.FilterOnline(It.IsAny<IEnumerable<int>>())).Returns([]);
        mock.Setup(s => s.IsOnline(It.IsAny<int>())).Returns(false);
        return mock;
    }

    public static Mock<IReadReceiptService> CreateReadReceiptServiceMock()
    {
        var mock = new Mock<IReadReceiptService>();
        mock.Setup(s => s.GetUnreadCountsForChatsAsync(It.IsAny<int>(), It.IsAny<List<int>>())).ReturnsAsync([]);
        mock.Setup(s => s.GetUnreadCountAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(0);
        return mock;
    }

    public static Mock<ITokenService> CreateTokenServiceMock(string token = "test-jwt-token")
    {
        var mock = new Mock<ITokenService>();
        mock.Setup(t => t.GenerateToken(It.IsAny<int>(), It.IsAny<UserRole>())).Returns(token);
        return mock;
    }
}