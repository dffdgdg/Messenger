using API.Application.Configuration;
using API.Application.Services.Abstractions;
using API.Application.Services.Features.Department;
using API.Domain.Common;
using API.Domain.Entities;
using API.Domain.Repositories;
using API.Infrastructure.Database;
using API.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Shared.Dto.Department;
using Xunit;

namespace API.Tests.Services;

public class DepartmentServiceTests : IDisposable
{
    private readonly MessengerDbContext _context;
    private readonly UnitOfWork _unitOfWork;
    private readonly Mock<IDepartmentRepository> _deptRepoMock = new();
    private readonly Mock<IChatRepository> _chatRepoMock = new();
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IHubNotifier> _hubMock = new();
    private readonly Mock<ICacheService> _cacheMock = new();
    private readonly DepartmentService _service;

    private static readonly MessengerSettings Settings = new()
    {
        AdminDepartmentId = 1
    };

    public DepartmentServiceTests()
    {
        _context = DbContextFactory.Create();
        _unitOfWork = new UnitOfWork(_context, NullLogger<UnitOfWork>.Instance);

        _service = new DepartmentService(
            _unitOfWork,
            Options.Create(Settings),
            new AppDateTime(TimeProvider.System),
            _hubMock.Object,
            _cacheMock.Object,
            _deptRepoMock.Object,
            _chatRepoMock.Object,
            _userRepoMock.Object,
            NullLogger<DepartmentService>.Instance);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task GetDepartments_Empty_ReturnsSuccess()
    {
        _deptRepoMock.Setup(r => r.GetAllWithHeadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _deptRepoMock.Setup(r => r.GetUserCountsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _service.GetDepartmentsAsync();
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDepartment_NotFound_ReturnsNotFound()
    {
        _deptRepoMock.Setup(r => r.FindByIdWithHeadAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Department?)null);

        var result = await _service.GetDepartmentAsync(999);
        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.NotFound);
    }

    [Fact]
    public async Task GetDepartment_Found_ReturnsDto()
    {
        var dept = new Department { Name = "IT", HeadId = null };
        _deptRepoMock.Setup(r => r.FindByIdWithHeadAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dept);
        _deptRepoMock.Setup(r => r.CountUsersAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var result = await _service.GetDepartmentAsync(dept.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("IT");
    }

    [Fact]
    public async Task CreateDepartment_EmptyName_ReturnsFailure()
    {
        var result = await _service.CreateDepartmentAsync(new DepartmentDto { Name = "" });
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Название");
    }

    [Fact]
    public async Task CreateDepartment_ParentNotFound_ReturnsNotFound()
    {
        _deptRepoMock.Setup(r => r.ExistsAsync(999, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await _service.CreateDepartmentAsync(new DepartmentDto { Name = "Test", ParentDepartmentId = 999 });
        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.NotFound);
    }

    [Fact]
    public async Task CreateDepartment_HeadNotFound_ReturnsNotFound()
    {
        _userRepoMock.Setup(r => r.ExistsAsync(999, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await _service.CreateDepartmentAsync(new DepartmentDto { Name = "Test", Head = 999 });
        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.NotFound);
    }

    [Fact]
    public async Task UpdateDepartment_NotFound_ReturnsNotFound()
    {
        _deptRepoMock.Setup(r => r.FindByIdAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Department?)null);

        var result = await _service.UpdateDepartmentAsync(999, new DepartmentDto { Name = "New" });
        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.NotFound);
    }

    [Fact]
    public async Task UpdateDepartment_SelfParent_ReturnsFailure()
    {
        var dept = new Department { Name = "Old", Id = 1 };
        _deptRepoMock.Setup(r => r.FindByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dept);

        var result = await _service.UpdateDepartmentAsync(1, new DepartmentDto { Name = "New", ParentDepartmentId = 1 });
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("родителем самому себе");
    }

    [Fact]
    public async Task DeleteDepartment_NotFound_ReturnsNotFound()
    {
        _deptRepoMock.Setup(r => r.FindByIdAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Department?)null);

        var result = await _service.DeleteDepartmentAsync(999);
        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.NotFound);
    }

    [Fact]
    public async Task DeleteDepartment_WithChildren_ReturnsFailure()
    {
        var dept = new Department { Name = "Parent", Id = 1 };
        _deptRepoMock.Setup(r => r.FindByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(dept);
        _deptRepoMock.Setup(r => r.HasChildDepartmentsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await _service.DeleteDepartmentAsync(1);
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("дочерними");
    }

    [Fact]
    public async Task GetDepartmentMembers_DepartmentNotFound_ReturnsNotFound()
    {
        _deptRepoMock.Setup(r => r.ExistsAsync(999, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await _service.GetDepartmentMembersAsync(999);
        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.NotFound);
    }

    [Fact]
    public async Task AddUserToDepartment_NoPermission_ReturnsForbidden()
    {
        _deptRepoMock.Setup(r => r.ResolveUserRoleAsync(1, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserRole.User);

        var result = await _service.AddUserToDepartmentAsync(1, 10, 1);
        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.Forbidden);
    }
}