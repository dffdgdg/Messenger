using API.Common.Patterns;
using API.Controllers;
using API.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Dto.Chat;
using Shared.Dto.User;
using Shared.Response;
using Xunit;

namespace API.Tests.Controllers;

public class ChatsControllerTests
{
    private readonly Mock<IChatService> _chatMock = new();
    private readonly Mock<IChatMemberService> _memberMock = new();
    private readonly ChatsController _controller;

    public ChatsControllerTests()
    {
        _controller = new ChatsController(_chatMock.Object, _memberMock.Object, NullLogger<ChatsController>.Instance);
        AuthHelper.SetUser(_controller, userId: 582);
    }

    #region Authorization Tests

    [Theory]
    [InlineData(nameof(ChatsController.GetUserDialogs))]
    [InlineData(nameof(ChatsController.GetUserChats))]
    [InlineData(nameof(ChatsController.GetUserGroups))]
    public async Task UserSpecificEndpoint_WrongUserId_Returns403WithoutCallingService(string methodName)
    {
        IActionResult result = methodName switch
        {
            nameof(ChatsController.GetUserDialogs) => await _controller.GetUserDialogs(userId: 731),
            nameof(ChatsController.GetUserChats) => await _controller.GetUserChats(userId: 731),
            nameof(ChatsController.GetUserGroups) => await _controller.GetUserGroups(userId: 731),
            _ => throw new ArgumentException("Unknown method")
        };

        result.ShouldHaveStatus(403);

        _chatMock.Verify(s => s.GetUserDialogsAsync(It.IsAny<int>()), Times.Never);
        _chatMock.Verify(s => s.GetUserChatsAsync(It.IsAny<int>()), Times.Never);
        _chatMock.Verify(s => s.GetUserGroupsAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task GetContactChat_WrongUserId_Returns403()
    {
        var result = await _controller.GetContactChat(userId: 731, contactUserId: 582);

        result.ShouldHaveStatus(403);
        _chatMock.Verify(s => s.GetContactChatAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    #endregion

    #region Get User Chats

    [Fact]
    public async Task GetUserDialogs_OwnUserId_Returns200()
    {
        var expected = new List<ChatDto> { new() { Id = 1, Name = "Test" } };

        _chatMock.Setup(s => s.GetUserDialogsAsync(582))
            .Returns(Result<List<ChatDto>>.Success(expected).AsTask());

        var result = await _controller.GetUserDialogs(userId: 582);

        var response = result.ShouldHaveStatus(200)
            .ShouldHaveBody<ApiResponse<List<ChatDto>>>();
        response.Data.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetUserChats_OwnUserId_Returns200()
    {
        _chatMock.Setup(s => s.GetUserChatsAsync(582))
            .Returns(Result<List<ChatDto>>.Success([]).AsTask());

        var result = await _controller.GetUserChats(userId: 582);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task GetUserGroups_OwnUserId_Returns200()
    {
        _chatMock.Setup(s => s.GetUserGroupsAsync(582))
            .Returns(Result<List<ChatDto>>.Success([]).AsTask());

        var result = await _controller.GetUserGroups(userId: 582);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task GetContactChat_OwnUserId_Returns200()
    {
        _chatMock.Setup(s => s.GetContactChatAsync(582, 100))
            .Returns(Result<ChatDto>.Success(new ChatDto()).AsTask());

        var result = await _controller.GetContactChat(userId: 582, contactUserId: 100);

        result.ShouldHaveStatus(200);
    }

    #endregion

    #region Get Chat

    [Fact]
    public async Task GetChat_Success_Returns200()
    {
        _chatMock.Setup(s => s.GetChatForUserAsync(42, 582))
            .Returns(Result<ChatDto>.Success(new ChatDto { Id = 42 }).AsTask());

        var result = await _controller.GetChat(42);

        result.ShouldHaveStatus(200);
        _chatMock.Verify(s => s.GetChatForUserAsync(42, 582), Times.Once);
    }

    [Fact]
    public async Task GetChat_NotFound_Returns404()
    {
        _chatMock.Setup(s => s.GetChatForUserAsync(999, 582))
            .Returns(Result<ChatDto>.NotFound("Чат не найден").AsTask());

        var result = await _controller.GetChat(999);

        result.ShouldHaveStatus(404);
    }

    #endregion

    #region Get Members

    [Fact]
    public async Task GetMembers_PassesCurrentUserId()
    {
        _chatMock.Setup(s => s.GetChatMembersAsync(5, 582))
            .Returns(Result<List<UserDto>>.Success([]).AsTask());

        var result = await _controller.GetMembers(5);

        result.ShouldHaveStatus(200);
        _chatMock.Verify(s => s.GetChatMembersAsync(5, 582), Times.Once);
    }

    [Fact]
    public async Task GetChatMembersDetailed_ReturnsSuccess()
    {
        _memberMock.Setup(s => s.GetMembersAsync(5, 582))
            .Returns(Result<List<ChatMemberDto>>.Success([]).AsTask());

        var result = await _controller.GetChatMembersDetailed(5);

        result.ShouldHaveStatus(200);
    }

    #endregion

    #region Manage Members

    [Fact]
    public async Task AddChatMember_Success_Returns200()
    {
        var dto = new UpdateChatMemberDto { UserId = 200 };

        _memberMock.Setup(s => s.AddMemberAsync(5, 200, 582, ChatRole.Member))
            .Returns(Result<ChatMemberDto>.Success(new ChatMemberDto()).AsTask());

        var result = await _controller.AddChatMember(5, dto);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task AddChatMember_Forbidden_Returns403()
    {
        var dto = new UpdateChatMemberDto { UserId = 200 };

        _memberMock.Setup(s => s.AddMemberAsync(5, 200, 582, ChatRole.Member))
            .Returns(Result<ChatMemberDto>.Forbidden("Нет прав").AsTask());

        var result = await _controller.AddChatMember(5, dto);

        result.ShouldHaveStatus(403);
    }

    [Fact]
    public async Task RemoveChatMember_Success_Returns200()
    {
        _memberMock.Setup(s => s.RemoveMemberAsync(5, 300, 582))
            .Returns(Result.Success().AsTask());

        var result = await _controller.RemoveChatMember(5, 300);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task RemoveChatMember_Forbidden_Returns403()
    {
        _memberMock.Setup(s => s.RemoveMemberAsync(5, 300, 582))
            .Returns(Result.Forbidden("Нет прав").AsTask());

        var result = await _controller.RemoveChatMember(5, 300);

        result.ShouldHaveStatus(403);
    }

    [Fact]
    public async Task UpdateChatMemberRole_Success_Returns200()
    {
        _memberMock.Setup(s => s.UpdateRoleAsync(5, 300, ChatRole.Admin, 582))
            .Returns(Result<ChatMemberDto>.Success(new ChatMemberDto()).AsTask());

        var result = await _controller.UpdateChatMemberRole(5, 300, ChatRole.Admin);

        result.ShouldHaveStatus(200);
    }

    #endregion

    #region Create/Update/Delete Chat

    [Fact]
    public async Task CreateChat_SetsCreatedByIdAndReturns200()
    {
        AuthHelper.SetUser(_controller, 915);
        var dto = new ChatDto();

        _chatMock.Setup(x => x.CreateChatAsync(It.IsAny<ChatDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ChatDto>.Success(new ChatDto { Id = 10 }));

        var result = await _controller.CreateChat(dto);

        dto.CreatedById.Should().Be(915);
        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task UpdateChat_Success_Returns200()
    {
        var dto = new UpdateChatDto { Name = "New Name" };

        _chatMock.Setup(s => s.UpdateChatAsync(5, 582, dto))
            .Returns(Result<ChatDto>.Success(new ChatDto()).AsTask());

        var result = await _controller.UpdateChat(5, dto);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task DeleteChat_Success_Returns200()
    {
        _chatMock.Setup(s => s.DeleteChatAsync(5, 582))
            .Returns(Result.Success().AsTask());

        var result = await _controller.DeleteChat(5);

        result.ShouldHaveStatus(200);
    }

    #endregion

    #region Avatar

    [Fact]
    public async Task UploadAvatar_Success_Returns200()
    {
        var fileMock = new Mock<IFormFile>();

        _chatMock.Setup(s => s.UploadChatAvatarAsync(5, 582, fileMock.Object))
            .Returns(Result<string>.Success("/avatars/chat_5.webp").AsTask());

        var result = await _controller.UploadAvatar(5, fileMock.Object);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task RemoveAvatar_Success_Returns200()
    {
        _chatMock.Setup(s => s.RemoveChatAvatarAsync(5, 582))
            .Returns(Result.Success().AsTask());

        var result = await _controller.RemoveAvatar(5);

        result.ShouldHaveStatus(200);
    }

    #endregion
}