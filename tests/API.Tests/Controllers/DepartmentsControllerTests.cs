using API.Application.Services.Abstractions;
using API.Domain.Common;
using API.Tests.Helpers;
using API.Web.Controllers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Dto.Department;
using Shared.Dto.User;
using Shared.Response;
using Xunit;

namespace API.Tests.Controllers;

public class DepartmentsControllerTests
{
    private readonly Mock<IDepartmentService> _deptMock = new();
    private readonly DepartmentsController _controller;

    public DepartmentsControllerTests()
    {
        _controller = new DepartmentsController(_deptMock.Object, NullLogger<DepartmentsController>.Instance);
        AuthHelper.SetUser(_controller, userId: 1);
    }

    [Fact]
    public async Task GetDepartments_ReturnsSuccess()
    {
        _deptMock.Setup(s => s.GetDepartmentsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<DepartmentDto>>.Success([]));

        var result = await _controller.GetDepartments(CancellationToken.None);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task GetDepartment_ReturnsSuccess()
    {
        _deptMock.Setup(s => s.GetDepartmentAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DepartmentDto>.Success(new DepartmentDto()));

        var result = await _controller.GetDepartment(5, CancellationToken.None);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task GetDepartment_NotFound_Returns404()
    {
        _deptMock.Setup(s => s.GetDepartmentAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DepartmentDto>.NotFound("Отдел не найден"));

        var result = await _controller.GetDepartment(999, CancellationToken.None);

        result.ShouldHaveStatus(404);
    }

    [Fact]
    public async Task GetDepartmentMembers_ReturnsSuccess()
    {
        _deptMock.Setup(s => s.GetDepartmentMembersAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<UserDto>>.Success([]));

        var result = await _controller.GetDepartmentMembers(5, CancellationToken.None);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task AddUserToDepartment_Success_Returns200()
    {
        var dto = new UpdateDepartmentMemberDto { UserId = 200 };

        _deptMock.Setup(s => s.AddUserToDepartmentAsync(5, 200, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await _controller.AddUserToDepartment(5, dto, CancellationToken.None);

        result.ShouldHaveStatus(200);
    }

    [Fact]
    public async Task AddUserToDepartment_Forbidden_Returns403()
    {
        var dto = new UpdateDepartmentMemberDto { UserId = 200 };

        _deptMock.Setup(s => s.AddUserToDepartmentAsync(5, 200, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Forbidden("Нет прав"));

        var result = await _controller.AddUserToDepartment(5, dto, CancellationToken.None);

        result.ShouldHaveStatus(403);
    }

    [Fact]
    public async Task RemoveUserFromDepartment_Success_Returns200()
    {
        _deptMock.Setup(s => s.RemoveUserFromDepartmentAsync(5, 200, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await _controller.RemoveUserFromDepartment(5, 200, CancellationToken.None);

        result.ShouldHaveStatus(200);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CanManageDepartment_ReturnsCorrectValue(bool canManage)
    {
        _deptMock.Setup(s => s.CanManageDepartmentAsync(1, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(canManage));

        var result = await _controller.CanManageDepartment(5, CancellationToken.None);

        var response = result.ShouldHaveStatus(200)
            .ShouldHaveBody<ApiResponse<bool>>();
        response.Data.Should().Be(canManage);
    }
}