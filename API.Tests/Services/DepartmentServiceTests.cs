using API.Configuration;
using API.Data;
using API.Hubs;
using API.Services.Abstractions;
using API.Services.Department;
using API.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Shared.Dto.Department;
using Shared.Dto.User;
using Shared.Enum;
using Xunit;

namespace API.Tests.Services;

public class DepartmentServiceTests : IDisposable
{
    private readonly MessengerDbContext _context;
    private readonly Mock<IHubNotifier> _hubMock = new();
    private readonly Mock<IOnlineUserService> _onlineMock = new();
    private readonly Mock<IHubContext<MessengerHub>> _hubContextMock = new();
    private readonly Mock<ICacheService> _cacheMock = new();
    private readonly DepartmentService _service;

    private static readonly MessengerSettings Settings = new()
    {
        AdminDepartmentId = 1
    };

    public DepartmentServiceTests()
    {
        _context = DbContextFactory.Create();

        _service = new DepartmentService(
            _context,
            Options.Create(Settings),
            new AppDateTime(TimeProvider.System),
            _hubMock.Object,
            _onlineMock.Object,
            _hubContextMock.Object,
            _cacheMock.Object,
            NullLogger<DepartmentService>.Instance);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task GetDepartments_Empty_ReturnsSuccess()
    {
        var result = await _service.GetDepartmentsAsync();
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDepartment_NotFound_ReturnsNotFound()
    {
        var result = await _service.GetDepartmentAsync(999);
        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.NotFound);
    }

    [Fact]
    public async Task GetDepartment_Found_ReturnsDto()
    {
        var dept = new Department { Name = "IT", HeadId = null };
        _context.Departments.Add(dept);
        await _context.SaveChangesAsync();

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
        var result = await _service.CreateDepartmentAsync(new DepartmentDto { Name = "Test", ParentDepartmentId = 999 });
        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.NotFound);
    }

    [Fact]
    public async Task CreateDepartment_HeadNotFound_ReturnsNotFound()
    {
        var result = await _service.CreateDepartmentAsync(new DepartmentDto { Name = "Test", Head = 999 });
        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.NotFound);
    }

    [Fact]
    public async Task CreateDepartment_Success_ReturnsDto()
    {
        var result = await _service.CreateDepartmentAsync(new DepartmentDto { Name = "Engineering" });

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("Engineering");
        result.Value.Id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task UpdateDepartment_NotFound_ReturnsNotFound()
    {
        var result = await _service.UpdateDepartmentAsync(999, new DepartmentDto { Name = "New" });
        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.NotFound);
    }

    [Fact]
    public async Task UpdateDepartment_EmptyName_ReturnsFailure()
    {
        var dept = new Department { Name = "Old" };
        _context.Departments.Add(dept);
        await _context.SaveChangesAsync();

        var result = await _service.UpdateDepartmentAsync(dept.Id, new DepartmentDto { Name = "" });
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Название");
    }

    [Fact]
    public async Task UpdateDepartment_SelfParent_ReturnsFailure()
    {
        var dept = new Department { Name = "Old" };
        _context.Departments.Add(dept);
        await _context.SaveChangesAsync();

        var result = await _service.UpdateDepartmentAsync(dept.Id, new DepartmentDto { Name = "New", ParentDepartmentId = dept.Id });
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("родителем самому себе");
    }

    [Fact]
    public async Task DeleteDepartment_NotFound_ReturnsNotFound()
    {
        var result = await _service.DeleteDepartmentAsync(999);
        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.NotFound);
    }

    [Fact]
    public async Task DeleteDepartment_WithChildren_ReturnsFailure()
    {
        var parent = new Department { Name = "Parent" };
        _context.Departments.Add(parent);
        await _context.SaveChangesAsync();

        var child = new Department { Name = "Child", ParentDepartmentId = parent.Id };
        _context.Departments.Add(child);
        await _context.SaveChangesAsync();

        var result = await _service.DeleteDepartmentAsync(parent.Id);
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("дочерними");
    }

    [Fact]
    public async Task DeleteDepartment_WithUsers_ReturnsFailure()
    {
        var dept = new Department { Name = "HR" };
        _context.Departments.Add(dept);
        await _context.SaveChangesAsync();

        var user = await DbContextFactory.SeedUserAsync(_context, "employee", "pass", departmentId: dept.Id);

        var result = await _service.DeleteDepartmentAsync(dept.Id);
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("сотрудниками");
    }

    [Fact]
    public async Task DeleteDepartment_Empty_Success()
    {
        var dept = new Department { Name = "Empty" };
        _context.Departments.Add(dept);
        await _context.SaveChangesAsync();

        var result = await _service.DeleteDepartmentAsync(dept.Id);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task GetDepartmentMembers_DepartmentNotFound_ReturnsNotFound()
    {
        var result = await _service.GetDepartmentMembersAsync(999);
        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.NotFound);
    }

    [Fact]
    public async Task GetDepartmentMembers_Empty_ReturnsEmptyList()
    {
        var dept = new Department { Name = "Empty" };
        _context.Departments.Add(dept);
        await _context.SaveChangesAsync();

        var result = await _service.GetDepartmentMembersAsync(dept.Id);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task AddUserToDepartment_NoPermission_ReturnsForbidden()
    {
        var result = await _service.AddUserToDepartmentAsync(1, 10, 1);
        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.Forbidden);
    }

    [Fact]
    public async Task AddUserToDepartment_AlreadyInDepartment_ReturnsConflict()
    {
        var dept = new Department { Name = "IT" };
        _context.Departments.Add(dept);
        await _context.SaveChangesAsync();

        var user = new User
        {
            Username = "test",
            DepartmentId = dept.Id,
            Password = UserPassword.Create("pass"),
            UserSetting = new UserSetting { NotificationsEnabled = true }
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var result = await _service.AddUserToDepartmentAsync(dept.Id, user.Id, user.Id);
        result.IsFailure.Should().BeTrue();
        result.ErrorType.Should().Be(ResultErrorType.Conflict);
    }

    [Fact]
    public async Task CanManageDepartment_Admin_ReturnsTrue()
    {
        var adminDept = new Department { Name = "Admin", Id = Settings.AdminDepartmentId };
        _context.Departments.Add(adminDept);

        var user = new User { Username = "admin", DepartmentId = Settings.AdminDepartmentId, Password = UserPassword.Create("pass") };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var dept = new Department { Name = "IT" };
        _context.Departments.Add(dept);
        await _context.SaveChangesAsync();

        var result = await _service.CanManageDepartmentAsync(user.Id, dept.Id);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }
}