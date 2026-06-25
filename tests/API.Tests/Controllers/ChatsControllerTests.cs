using API.Application.Common;
using API.Application.Features.Chat;
using API.Application.Features.Chat.Commands;
using API.Application.Features.Chat.Queries;
using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Tests.Helpers;
using API.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Contracts.Chat;
using Shared.Contracts.User;
using Shared.Enum;
using Xunit;

namespace API.Tests.Controllers;

public class ChatsControllerTests : ControllerTestBase
{
    private readonly Mock<IChatHandlers> _handlers = new();
    private readonly Mock<IChatMemberService> _memberMock = new();

    private readonly Mock<IQueryHandler<GetUserChatsQuery, Result<List<ChatDto>>>> _getUserChats = new();
    private readonly Mock<IQueryHandler<GetUserDialogsQuery, Result<List<ChatDto>>>> _getUserDialogs = new();
    private readonly Mock<IQueryHandler<GetUserGroupsQuery, Result<List<ChatDto>>>> _getUserGroups = new();
    private readonly Mock<IQueryHandler<GetChatForUserQuery, Result<ChatDto>>> _getChatForUser = new();
    private readonly Mock<IQueryHandler<GetContactChatQuery, Result<ChatDto>>> _getContactChat = new();
    private readonly Mock<IQueryHandler<GetChatMembersQuery, Result<List<UserDto>>>> _getChatMembers = new();
    private readonly Mock<ICommandHandler<CreateChatCommand, ChatDto>> _createChat = new();
    private readonly Mock<ICommandHandler<UpdateChatCommand, ChatDto>> _updateChat = new();
    private readonly Mock<ICommandHandler<DeleteChatCommand>> _deleteChat = new();
    private readonly Mock<ICommandHandler<UploadChatAvatarCommand, string>> _uploadAvatar = new();
    private readonly Mock<ICommandHandler<RemoveChatAvatarCommand>> _removeAvatar = new();

    private readonly ChatsController _controller;

    public ChatsControllerTests()
    {
        _handlers.Setup(h => h.GetUserChats).Returns(_getUserChats.Object);
        _handlers.Setup(h => h.GetUserDialogs).Returns(_getUserDialogs.Object);
        _handlers.Setup(h => h.GetUserGroups).Returns(_getUserGroups.Object);
        _handlers.Setup(h => h.GetChatForUser).Returns(_getChatForUser.Object);
        _handlers.Setup(h => h.GetContactChat).Returns(_getContactChat.Object);
        _handlers.Setup(h => h.GetChatMembers).Returns(_getChatMembers.Object);
        _handlers.Setup(h => h.CreateChat).Returns(_createChat.Object);
        _handlers.Setup(h => h.UpdateChat).Returns(_updateChat.Object);
        _handlers.Setup(h => h.DeleteChat).Returns(_deleteChat.Object);
        _handlers.Setup(h => h.UploadAvatar).Returns(_uploadAvatar.Object);
        _handlers.Setup(h => h.RemoveAvatar).Returns(_removeAvatar.Object);

        _controller = new ChatsController(_handlers.Object, _memberMock.Object, NullLogger<ChatsController>.Instance);
        SetUser(_controller, userId: 582);
    }

    [Theory]
    [InlineData(nameof(ChatsController.GetUserDialogs))]
    [InlineData(nameof(ChatsController.GetUserChats))]
    [InlineData(nameof(ChatsController.GetUserGroups))]
    public async Task UserSpecificEndpoint_WrongUserId_Returns403(string methodName)
    {
        IActionResult result = methodName switch
        {
            nameof(ChatsController.GetUserDialogs) => await _controller.GetUserDialogs(userId: 731),
            nameof(ChatsController.GetUserChats) => await _controller.GetUserChats(userId: 731),
            nameof(ChatsController.GetUserGroups) => await _controller.GetUserGroups(userId: 731),
            _ => throw new ArgumentException("Unknown method")
        };

        result.ShouldHaveStatus(403);
    }

    [Fact]
    public async Task GetContactChat_WrongUserId_Returns403()
    {
        var result = await _controller.GetContactChat(userId: 731, contactUserId: 582);
        result.ShouldHaveStatus(403);
    }

    [Fact]
    public async Task GetUserChats_OwnUserId_Returns200()
    {
        _getUserChats
            .Setup(h => h.HandleAsync(It.Is<GetUserChatsQuery>(q => q.UserId == 582), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<ChatDto>>.Success([]));

        var result = await _controller.GetUserChats(userId: 582);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task GetUserDialogs_OwnUserId_Returns200()
    {
        _getUserDialogs
            .Setup(h => h.HandleAsync(It.Is<GetUserDialogsQuery>(q => q.UserId == 582), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<ChatDto>>.Success([]));

        var result = await _controller.GetUserDialogs(userId: 582);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task GetUserGroups_OwnUserId_Returns200()
    {
        _getUserGroups
            .Setup(h => h.HandleAsync(It.Is<GetUserGroupsQuery>(q => q.UserId == 582), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<ChatDto>>.Success([]));

        var result = await _controller.GetUserGroups(userId: 582);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task GetContactChat_OwnUserId_Returns200()
    {
        _getContactChat
            .Setup(h => h.HandleAsync(
                It.Is<GetContactChatQuery>(q => q.UserId == 582 && q.ContactUserId == 100),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ChatDto>.Success(new ChatDto()));

        var result = await _controller.GetContactChat(userId: 582, contactUserId: 100);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task GetChat_Success_Returns200()
    {
        _getChatForUser
            .Setup(h => h.HandleAsync(
                It.Is<GetChatForUserQuery>(q => q.ChatId == 42 && q.UserId == 582),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ChatDto>.Success(new ChatDto { Id = 42 }));

        var result = await _controller.GetChat(42);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task GetChat_NotFound_Returns404()
    {
        _getChatForUser
            .Setup(h => h.HandleAsync(It.IsAny<GetChatForUserQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ChatDto>.NotFound("Чат не найден"));

        var result = await _controller.GetChat(999);

        result.ShouldHaveStatus(404);
    }

    [Fact]
    public async Task GetMembers_Success_Returns200()
    {
        _getChatMembers
            .Setup(h => h.HandleAsync(
                It.Is<GetChatMembersQuery>(q => q.ChatId == 5 && q.UserId == 582),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<UserDto>>.Success([]));

        var result = await _controller.GetMembers(5);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task GetMembersDetailed_ReturnsSuccess()
    {
        _memberMock
            .Setup(s => s.GetMembersAsync(5, 582))
            .ReturnsAsync(Result<List<ChatMemberDto>>.Success([]));

        var result = await _controller.GetMembersDetailed(5);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task AddMember_Success_Returns200()
    {
        _memberMock
            .Setup(s => s.AddMemberAsync(5, 200, 582))
            .ReturnsAsync(Result<ChatMemberDto>.Success(new ChatMemberDto()));

        var result = await _controller.AddMember(5, new UpdateChatMemberDto { UserId = 200 });

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task AddMember_Forbidden_Returns403()
    {
        _memberMock
            .Setup(s => s.AddMemberAsync(5, 200, 582))
            .ReturnsAsync(Result<ChatMemberDto>.Forbidden("Нет прав"));

        var result = await _controller.AddMember(5, new UpdateChatMemberDto { UserId = 200 });

        result.ShouldHaveStatus(403);
    }

    [Fact]
    public async Task RemoveMember_Success_Returns200()
    {
        _memberMock
            .Setup(s => s.RemoveMemberAsync(5, 300, 582))
            .ReturnsAsync(Result.Success());

        var result = await _controller.RemoveMember(5, 300);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task RemoveMember_Forbidden_Returns403()
    {
        _memberMock
            .Setup(s => s.RemoveMemberAsync(5, 300, 582))
            .ReturnsAsync(Result.Forbidden("Нет прав"));

        var result = await _controller.RemoveMember(5, 300);

        result.ShouldHaveStatus(403);
    }

    [Fact]
    public async Task UpdateMemberRole_Success_Returns200()
    {
        _memberMock
            .Setup(s => s.UpdateRoleAsync(5, 300, ChatRole.Admin, 582))
            .ReturnsAsync(Result<ChatMemberDto>.Success(new ChatMemberDto()));

        var result = await _controller.UpdateMemberRole(5, 300, ChatRole.Admin);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task CreateChat_Success_Returns200()
    {
        _createChat
            .Setup(h => h.HandleAsync(It.IsAny<CreateChatCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ChatDto>.Success(new ChatDto { Id = 10 }));

        var result = await _controller.CreateChat(new ChatDto());

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task UpdateChat_Success_Returns200()
    {
        _updateChat
            .Setup(h => h.HandleAsync(It.IsAny<UpdateChatCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ChatDto>.Success(new ChatDto()));

        var result = await _controller.UpdateChat(5, new UpdateChatDto());

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task DeleteChat_Success_Returns200()
    {
        _deleteChat
            .Setup(h => h.HandleAsync(It.IsAny<DeleteChatCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await _controller.DeleteChat(5);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task UploadAvatar_Success_Returns200()
    {
        _uploadAvatar
            .Setup(h => h.HandleAsync(It.IsAny<UploadChatAvatarCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string>.Success("/avatars/chat_5.webp"));

        var result = await _controller.UploadAvatar(5, new Mock<IFormFile>().Object);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task RemoveAvatar_Success_Returns200()
    {
        _removeAvatar
            .Setup(h => h.HandleAsync(It.IsAny<RemoveChatAvatarCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await _controller.RemoveAvatar(5);

        result.ShouldHaveStatus(200);
    }
}