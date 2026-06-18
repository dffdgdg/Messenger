using API.Repositories.Implementations;
using API.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace API.Tests.Repositories;

public class UserRepositoryTests : IntegrationTestBase
{
    private readonly UserRepository _repo;

    public UserRepositoryTests() => _repo = new UserRepository(Context);

    [Fact]
    public async Task FindByUsername_ReturnsUser_WhenExists()
    {
        await DbContextFactory.SeedUserAsync(Context, "john_doe", "pass123");
        var user = await _repo.FindByUsernameAsync("john_doe");
        user.Should().NotBeNull();
        user!.Username.Should().Be("john_doe");
    }

    [Fact]
    public async Task FindByUsername_ReturnsNull_WhenNotExists()
    {
        var user = await _repo.FindByUsernameAsync("nonexistent");
        user.Should().BeNull();
    }

    [Fact]
    public async Task FindByUsername_TrimsUsername()
    {
        await DbContextFactory.SeedUserAsync(Context, "trim_test", "pass123");
        var user = await _repo.FindByUsernameAsync("  trim_test  ");
        user.Should().NotBeNull();
        user!.Username.Should().Be("trim_test");
    }

    [Fact]
    public async Task FindByIdWithPassword_ReturnsUserWithPassword()
    {
        var seeded = await DbContextFactory.SeedUserAsync(Context, "testuser", "securePass");
        var user = await _repo.FindByIdWithPasswordAsync(seeded.Id);
        user.Should().NotBeNull();
        user!.Password.Should().NotBeNull();
        user.Password.Verify("securePass").Should().BeTrue();
    }

    [Fact]
    public async Task FindByIdWithPassword_ReturnsNull_WhenNotExists()
    {
        var user = await _repo.FindByIdWithPasswordAsync(999);
        user.Should().BeNull();
    }

    [Fact]
    public async Task UsernameExists_True()
    {
        await DbContextFactory.SeedUserAsync(Context, "existing", "pass");
        var exists = await _repo.UsernameExistsAsync("existing");
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task UsernameExists_False()
    {
        var exists = await _repo.UsernameExistsAsync("missing");
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task UsernameExistsByOtherUser_ExcludesOwnId()
    {
        var user = await DbContextFactory.SeedUserAsync(Context, "duplicate", "pass");
        var exists = await _repo.UsernameExistsByOtherUserAsync("duplicate", user.Id);
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task UsernameExistsByOtherUser_FindsOtherUser()
    {
        var user1 = await DbContextFactory.SeedUserAsync(Context, "same_username", "pass");
        var user2 = await DbContextFactory.SeedUserAsync(Context, "another", "pass");
        var u2 = await Context.Users.FindAsync(user2.Id);
        u2!.Username = "same_username";
        await Context.SaveChangesAsync();

        var exists = await _repo.UsernameExistsByOtherUserAsync("same_username", user1.Id);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task GetAllWithSettings_ReturnsAllUsers()
    {
        await DbContextFactory.SeedUserAsync(Context, "alice", "pass");
        await DbContextFactory.SeedUserAsync(Context, "bob", "pass");
        var users = await _repo.GetAllWithSettingsAsync();
        users.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetWithSettings_ReturnsUser()
    {
        var seeded = await DbContextFactory.SeedUserAsync(Context, "charlie", "pass");
        var user = await _repo.GetWithSettingsAsync(seeded.Id);
        user.Should().NotBeNull();
        user!.Username.Should().Be("charlie");
    }

    [Fact]
    public async Task GetWithSettings_ReturnsNull()
    {
        var user = await _repo.GetWithSettingsAsync(999);
        user.Should().BeNull();
    }

    [Fact]
    public async Task GetByIds_ReturnsMatchingUsers()
    {
        var u1 = await DbContextFactory.SeedUserAsync(Context, "user1", "pass");
        var u2 = await DbContextFactory.SeedUserAsync(Context, "user2", "pass");
        await DbContextFactory.SeedUserAsync(Context, "user3", "pass");

        var users = await _repo.GetByIdsAsync([u1.Id, u2.Id]);
        users.Should().HaveCount(2);
        users.Select(u => u.Id).Should().Contain([u1.Id, u2.Id]);
    }

    [Fact]
    public async Task FindById_BaseMethod_Works()
    {
        var seeded = await DbContextFactory.SeedUserAsync(Context, "dave", "pass");
        var user = await _repo.FindByIdAsync(seeded.Id);
        user.Should().NotBeNull();
        user!.Username.Should().Be("dave");
    }

    [Fact]
    public async Task Exists_BaseMethod_Works()
    {
        var seeded = await DbContextFactory.SeedUserAsync(Context, "eve", "pass");
        var exists = await _repo.ExistsAsync(seeded.Id);
        exists.Should().BeTrue();

        var notExists = await _repo.ExistsAsync(999);
        notExists.Should().BeFalse();
    }

    [Fact]
    public async Task Add_BaseMethod_Works()
    {
        var user = new User { Username = "frank", Password = new UserPassword() };
        user.Password.SetPassword("pass");
        _repo.Add(user);
        await Context.SaveChangesAsync();

        var saved = await Context.Users.FirstOrDefaultAsync(u => u.Username == "frank");
        saved.Should().NotBeNull();
    }
}