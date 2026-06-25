using API.Application.Common;
using API.Application.Features.Chat;
using API.Application.Features.Department.Commands;
using API.Application.Features.Department.Queries;
using API.Domain.Common;
using API.Tests.Helpers;
using API.Web.Controllers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Contracts.Department;
using Shared.Contracts.User;
using Shared.Infrastructure;
using Xunit;

namespace API.Tests.Controllers;

public class DepartmentsControllerTests : ControllerTestBase
{
    private readonly Mock<IDepartmentHandlers> _handlers = new();

    private readonly Mock<ICommandHandler<AddUserToDepartmentCommand>> _addUser = new();
    private readonly Mock<ICommandHandler<CreateDepartmentCommand, DepartmentDto>> _createDept = new();
    private readonly Mock<ICommandHandler<DeleteDepartmentCommand>> _deleteDept = new();
    private readonly Mock<ICommandHandler<RemoveUserFromDepartmentCommand>> _removeUser = new();
    private readonly Mock<ICommandHandler<UpdateDepartmentCommand>> _updateDept = new();
    private readonly Mock<IQueryHandler<CanManageDepartmentQuery, Result<bool>>> _canManage = new();
    private readonly Mock<IQueryHandler<GetDepartmentMembersQuery, Result<List<UserDto>>>> _getMembers = new();
    private readonly Mock<IQueryHandler<GetDepartmentQuery, Result<DepartmentDto>>> _getDepartment = new();
    private readonly Mock<IQueryHandler<GetDepartmentsQuery, Result<List<DepartmentDto>>>> _getDepartments = new();

    private readonly DepartmentsController _controller;

    public DepartmentsControllerTests()
    {
        _handlers.Setup(h => h.AddUserToDepartment).Returns(_addUser.Object);
        _handlers.Setup(h => h.CreateDepartment).Returns(_createDept.Object);
        _handlers.Setup(h => h.DeleteDepartment).Returns(_deleteDept.Object);
        _handlers.Setup(h => h.RemoveUser).Returns(_removeUser.Object);
        _handlers.Setup(h => h.UpdateDepartment).Returns(_updateDept.Object);
        _handlers.Setup(h => h.CanManage).Returns(_canManage.Object);
        _handlers.Setup(h => h.GetMembers).Returns(_getMembers.Object);
        _handlers.Setup(h => h.GetDepartment).Returns(_getDepartment.Object);
        _handlers.Setup(h => h.GetDepartments).Returns(_getDepartments.Object);

        _controller = new DepartmentsController(_handlers.Object, NullLogger<DepartmentsController>.Instance);
        SetUser(_controller, userId: 1);
    }

    [Fact]
    public async Task GetDepartments_ReturnsSuccess()
    {
        _getDepartments
            .Setup(h => h.HandleAsync(It.IsAny<GetDepartmentsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<DepartmentDto>>.Success([]));

        var result = await _controller.GetDepartments(CancellationToken.None);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task GetDepartment_ReturnsSuccess()
    {
        _getDepartment
            .Setup(h => h.HandleAsync(It.IsAny<GetDepartmentQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DepartmentDto>.Success(new DepartmentDto()));

        var result = await _controller.GetDepartment(5, CancellationToken.None);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task GetDepartment_NotFound_Returns404()
    {
        _getDepartment
            .Setup(h => h.HandleAsync(It.IsAny<GetDepartmentQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DepartmentDto>.NotFound("Отдел не найден"));

        var result = await _controller.GetDepartment(999, CancellationToken.None);

        result.ShouldHaveStatus(404);
    }

    [Fact]
    public async Task GetDepartmentMembers_ReturnsSuccess()
    {
        _getMembers
            .Setup(h => h.HandleAsync(It.IsAny<GetDepartmentMembersQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<UserDto>>.Success([]));

        var result = await _controller.GetDepartmentMembers(5, CancellationToken.None);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task AddUserToDepartment_Success_Returns200()
    {
        _addUser
            .Setup(h => h.HandleAsync(It.IsAny<AddUserToDepartmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await _controller.AddUserToDepartment(
            5, new UpdateDepartmentMemberDto { UserId = 200 }, CancellationToken.None);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task AddUserToDepartment_Forbidden_Returns403()
    {
        _addUser
            .Setup(h => h.HandleAsync(It.IsAny<AddUserToDepartmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Forbidden("Нет прав"));

        var result = await _controller.AddUserToDepartment(
            5, new UpdateDepartmentMemberDto { UserId = 200 }, CancellationToken.None);

        result.ShouldHaveStatus(403);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CanManageDepartment_ReturnsCorrectValue(bool canManageValue)
    {
        _canManage
            .Setup(h => h.HandleAsync(It.IsAny<CanManageDepartmentQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(canManageValue));

        var result = await _controller.CanManageDepartment(5, CancellationToken.None);

        var response = result.ShouldHaveStatus(200).ShouldHaveBody<ApiResponse<bool>>();
        response.Data.Should().Be(canManageValue);
    }
}