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
        var userId = Guid.NewGuid();
        db.Users.Add(new UserEntity
        {
            Id = userId,
            Auth0Sub = "auth0|123",
            Email = "test@example.com",
            DisplayName = "TestUser",
            IsAdmin = false
        });
        await db.SaveChangesAsync();

        var svc = new UserService(db);
        var result = await svc.GetAsync("auth0|123");

        Assert.NotNull(result);
        Assert.Equal(userId, result.Id);
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

    [Fact]
    public async Task UpsertAsync_NewUser_UsesProvidedDisplayName()
    {
        using var db = CreateDb("upsert_new_displayname");

        var svc = new UserService(db);
        var result = await svc.UpsertAsync("auth0|new", "new@example.com", "Auth0 Name", "ChosenName");

        Assert.Equal("ChosenName", result.DisplayName);
    }

    [Fact]
    public async Task UpsertAsync_NewUser_FallsBackToJwtName_WhenDisplayNameNull()
    {
        using var db = CreateDb("upsert_new_fallback");

        var svc = new UserService(db);
        var result = await svc.UpsertAsync("auth0|new2", "new2@example.com", "Auth0 Name", null);

        Assert.Equal("Auth0 Name", result.DisplayName);
    }

    [Fact]
    public async Task UpsertAsync_ExistingUser_DoesNotOverwriteDisplayName()
    {
        using var db = CreateDb("upsert_existing_no_overwrite");
        db.Users.Add(new UserEntity
        {
            Id = Guid.NewGuid(),
            Auth0Sub = "auth0|existing",
            Email = "old@example.com",
            DisplayName = "OriginalName"
        });
        await db.SaveChangesAsync();

        var svc = new UserService(db);
        var result = await svc.UpsertAsync("auth0|existing", "updated@example.com", "New JWT Name", "AttemptedOverwrite");

        Assert.Equal("OriginalName", result.DisplayName);
        Assert.Equal("updated@example.com", result.Email);
    }

    [Fact]
    public async Task UpsertAsync_NewUser_FallsBackToJwtName_WhenDisplayNameEmpty()
    {
        using var db = CreateDb("upsert_new_empty");

        var svc = new UserService(db);
        var result = await svc.UpsertAsync("auth0|new3", "new3@example.com", "Auth0 Name", "");

        Assert.Equal("Auth0 Name", result.DisplayName);
    }

    [Fact]
    public async Task UpsertAsync_DifferentSubSameEmail_RelinksToExistingAccount()
    {
        using var db = CreateDb("upsert_relink");
        var existingId = Guid.NewGuid();
        db.Users.Add(new UserEntity
        {
            Id = existingId,
            Auth0Sub = "google-oauth2|111",
            Email = "shared@example.com",
            DisplayName = "OriginalUser"
        });

        await db.SaveChangesAsync();

        var svc = new UserService(db);
        var result = await svc.UpsertAsync("auth0|222", "shared@example.com", "Shared User", "Shared User");

        Assert.Equal(existingId, result.Id);
        Assert.Equal("OriginalUser", result.DisplayName);

        var dbUser = await db.Users.FindAsync(existingId);
        Assert.Equal("auth0|222", dbUser!.Auth0Sub);
    }

    [Fact]
    public async Task UpsertAsync_DifferentSubSameEmail_DoesNotCreateDuplicateRow()
    {
        using var db = CreateDb("upsert_no_duplicate");
        db.Users.Add(new UserEntity
        {
            Id = Guid.NewGuid(),
            Auth0Sub = "google-oauth2|333",
            Email = "dupe@example.com",
            DisplayName = "ExistingUser"
        });

        await db.SaveChangesAsync();

        var svc = new UserService(db);

        await svc.UpsertAsync("auth0|444", "dupe@example.com", "ExistingUser", "ExistingUser");

        var count = await db.Users.CountAsync(u => u.Email == "dupe@example.com");
        Assert.Equal(1, count);
    }
}
