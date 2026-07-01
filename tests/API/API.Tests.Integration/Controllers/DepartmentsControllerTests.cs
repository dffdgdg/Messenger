using API.Tests.Integration.Infrastructure;
using FluentAssertions;
using Shared.Contracts.Department;
using Shared.Contracts.User;
using Shared.Enum;
using Shared.Infrastructure;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace API.Tests.Integration.Controllers;

public class DepartmentsControllerTests : ControllerTestBase
{
    public DepartmentsControllerTests(WebAppFactory factory) : base(factory) { }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private async Task AuthenticateAsAdminAsync(string username = "admin", string password = "AdminP@ss123!")
    {
        await TestDataSeeder.SeedUserAsync(DbContext, username, password, departmentId: 1);
        await AuthenticateAsync(username, password);
    }

    [Fact]
    public async Task GetDepartments_WhenAuthorized_Returns200()
    {
        await SeedAndAuthenticateAsync("Viewer", "Pass123!");

        var response = await Client.GetAsync("/api/departments");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<List<DepartmentDto>>>(JsonOptions);
        body!.Success.Should().BeTrue();
    }

    [Fact]
    public async Task GetDepartments_WhenUnauthorized_Returns401()
    {
        var response = await Client.GetAsync("/api/departments");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetDepartment_WhenExists_Returns200()
    {
        await SeedAndAuthenticateAsync("Viewer", "Pass123!");
        await AuthenticateAsAdminAsync();
        var createDto = new DepartmentDto { Name = "IT Department" };
        var createResponse = await Client.PostAsJsonAsync("/api/departments", createDto);
        var created = await createResponse.Content.ReadFromJsonAsync<ApiResponse<DepartmentDto>>(JsonOptions);

        var response = await Client.GetAsync($"/api/departments/{created!.Data!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<DepartmentDto>>(JsonOptions);
        body!.Data!.Name.Should().Be("IT Department");
    }

    [Fact]
    public async Task GetDepartment_WhenNotFound_Returns404()
    {
        await SeedAndAuthenticateAsync("Viewer", "Pass123!");

        var response = await Client.GetAsync("/api/departments/99999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetDepartment_WhenUnauthorized_Returns401()
    {
        var response = await Client.GetAsync("/api/departments/1");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateDepartment_WhenAdmin_Returns200()
    {
        await AuthenticateAsAdminAsync();

        var dto = new DepartmentDto { Name = "New Department" };

        var response = await Client.PostAsJsonAsync("/api/departments", dto);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<DepartmentDto>>(JsonOptions);
        body!.Data!.Name.Should().Be("New Department");
    }

    [Fact]
    public async Task CreateDepartment_WhenNotAdmin_Returns403()
    {
        await SeedAndAuthenticateAsync("pleb", "Pass123!");

        var dto = new DepartmentDto { Name = "New Department" };

        var response = await Client.PostAsJsonAsync("/api/departments", dto);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateDepartment_WhenUnauthorized_Returns401()
    {
        var dto = new DepartmentDto { Name = "New Department" };

        var response = await Client.PostAsJsonAsync("/api/departments", dto);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateDepartment_WhenAdmin_Returns200()
    {
        await AuthenticateAsAdminAsync();
        var createDto = new DepartmentDto { Name = "Old Name" };
        var createResponse = await Client.PostAsJsonAsync("/api/departments", createDto);
        var created = await createResponse.Content.ReadFromJsonAsync<ApiResponse<DepartmentDto>>(JsonOptions);

        var updateDto = new DepartmentDto { Id = created!.Data!.Id, Name = "Updated Name" };

        var response = await Client.PutAsJsonAsync($"/api/departments/{created.Data.Id}", updateDto);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<DepartmentDto>>(JsonOptions);
        body!.Data!.Name.Should().Be("Updated Name");
    }

    [Fact]
    public async Task UpdateDepartment_WhenNotFound_Returns404()
    {
        await AuthenticateAsAdminAsync();

        var dto = new DepartmentDto { Id = 99999, Name = "Ghost" };

        var response = await Client.PutAsJsonAsync("/api/departments/99999", dto);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateDepartment_WhenNotAdmin_Returns403()
    {
        await SeedAndAuthenticateAsync("pleb", "Pass123!");

        var dto = new DepartmentDto { Id = 1, Name = "Hacked" };

        var response = await Client.PutAsJsonAsync("/api/departments/1", dto);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdateDepartment_WhenUnauthorized_Returns401()
    {
        var dto = new DepartmentDto { Id = 1, Name = "X" };

        var response = await Client.PutAsJsonAsync("/api/departments/1", dto);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteDepartment_WhenAdmin_Returns200()
    {
        await AuthenticateAsAdminAsync();
        var createDto = new DepartmentDto { Name = "To Delete" };
        var createResponse = await Client.PostAsJsonAsync("/api/departments", createDto);
        var created = await createResponse.Content.ReadFromJsonAsync<ApiResponse<DepartmentDto>>(JsonOptions);

        var response = await Client.DeleteAsync($"/api/departments/{created!.Data!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteDepartment_WhenNotFound_Returns404()
    {
        await AuthenticateAsAdminAsync();

        var response = await Client.DeleteAsync("/api/departments/99999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteDepartment_WhenNotAdmin_Returns403()
    {
        await SeedAndAuthenticateAsync("pleb", "Pass123!");

        var response = await Client.DeleteAsync("/api/departments/1");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteDepartment_WhenUnauthorized_Returns401()
    {
        var response = await Client.DeleteAsync("/api/departments/1");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetDepartmentMembers_WhenAuthorized_Returns200()
    {
        await SeedAndAuthenticateAsync("Viewer", "Pass123!");
        await AuthenticateAsAdminAsync("admin2", "AdminP@ss123!");
        var createDto = new DepartmentDto { Name = "Department" };
        var createResponse = await Client.PostAsJsonAsync("/api/departments", createDto);
        var created = await createResponse.Content.ReadFromJsonAsync<ApiResponse<DepartmentDto>>(JsonOptions);

        var response = await Client.GetAsync($"/api/departments/{created!.Data!.Id}/members");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<List<UserDto>>>(JsonOptions);
        body!.Success.Should().BeTrue();
    }

    [Fact]
    public async Task GetDepartmentMembers_WhenUnauthorized_Returns401()
    {
        var response = await Client.GetAsync("/api/departments/1/members");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AddUserToDepartment_WhenAuthorized_Returns200()
    {
        var manager = await SeedAndAuthenticateAsync("manager", "Pass123!");
        var newUser = await TestDataSeeder.SeedUserAsync(DbContext, "newuser", "Pass123!");

        await AuthenticateAsAdminAsync("admin3", "AdminP@ss123!");
        var createDto = new DepartmentDto { Name = "Department", Head = manager.Id };
        var createResponse = await Client.PostAsJsonAsync("/api/departments", createDto);
        var created = await createResponse.Content.ReadFromJsonAsync<ApiResponse<DepartmentDto>>(JsonOptions);

        await AuthenticateAsync("manager", "Pass123!");

        var dto = new UpdateDepartmentMemberDto { UserId = newUser.Id };

        var response = await Client.PostAsJsonAsync($"/api/departments/{created!.Data!.Id}/members", dto);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AddUserToDepartment_WhenForbidden_Returns403()
    {
        await SeedAndAuthenticateAsync("pleb", "Pass123!");
        var newUser = await TestDataSeeder.SeedUserAsync(DbContext, "newuser", "Pass123!");

        var dto = new UpdateDepartmentMemberDto { UserId = newUser.Id };

        var response = await Client.PostAsJsonAsync("/api/departments/1/members", dto);

        response.StatusCode.Should().BeOneOf([HttpStatusCode.Forbidden, HttpStatusCode.NotFound]);
    }

    [Fact]
    public async Task AddUserToDepartment_WhenUnauthorized_Returns401()
    {
        var dto = new UpdateDepartmentMemberDto { UserId = 1 };

        var response = await Client.PostAsJsonAsync("/api/departments/1/members", dto);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RemoveUserFromDepartment_WhenAuthorized_Returns200()
    {
        var manager = await SeedAndAuthenticateAsync("manager", "Pass123!");
        var member = await TestDataSeeder.SeedUserAsync(DbContext, "member", "Pass123!");

        await AuthenticateAsAdminAsync("admin4", "AdminP@ss123!");
        var createDto = new DepartmentDto { Name = "Department", Head = manager.Id };
        var createResponse = await Client.PostAsJsonAsync("/api/departments", createDto);
        var created = await createResponse.Content.ReadFromJsonAsync<ApiResponse<DepartmentDto>>(JsonOptions);

        var addDto = new UpdateDepartmentMemberDto { UserId = member.Id };
        await Client.PostAsJsonAsync($"/api/departments/{created!.Data!.Id}/members", addDto);

        await AuthenticateAsync("manager", "Pass123!");

        var response = await Client.DeleteAsync($"/api/departments/{created.Data.Id}/members/{member.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RemoveUserFromDepartment_WhenUnauthorized_Returns401()
    {
        var response = await Client.DeleteAsync("/api/departments/1/members/1");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CanManageDepartment_WhenAuthorized_Returns200()
    {
        var user = await SeedAndAuthenticateAsync("manager", "Pass123!");

        var response = await Client.GetAsync($"/api/departments/1/can-manage");

        response.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.NotFound]);
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var body = await response.Content.ReadFromJsonAsync<ApiResponse<bool>>(JsonOptions);
            body!.Success.Should().BeTrue();
        }
    }

    [Fact]
    public async Task CanManageDepartment_WhenUnauthorized_Returns401()
    {
        var response = await Client.GetAsync("/api/departments/1/can-manage");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}