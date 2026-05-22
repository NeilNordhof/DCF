using DCF.Api.Services;
using DCF.Data;
using DCF.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DCF.Tests.Services;

public class UserServiceTests
{
    private static DcfDbContext CreateDb(string name)
    {
        var opts = new DbContextOptionsBuilder<DcfDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new DcfDbContext(opts);
    }

    [Fact]
    public async Task GetAsync_ExistingUser_ReturnsProfile()
    {
        using var db = CreateDb("get_existing");
        db.Users.Add(new UserEntity
        {
            Id = Guid.NewGuid(),
            Auth0Sub = "auth0|123",
            Email = "test@example.com",
            DisplayName = "TestUser",
            IsAdmin = false
        });
        await db.SaveChangesAsync();

        var svc = new UserService(db);
        var result = await svc.GetAsync("auth0|123");

        Assert.NotNull(result);
        Assert.Equal("test@example.com", result.Email);
        Assert.Equal("TestUser", result.DisplayName);
        Assert.False(result.IsAdmin);
    }

    [Fact]
    public async Task GetAsync_NonExistentUser_ReturnsNull()
    {
        using var db = CreateDb("get_missing");

        var svc = new UserService(db);
        var result = await svc.GetAsync("auth0|does-not-exist");

        Assert.Null(result);
    }
}
