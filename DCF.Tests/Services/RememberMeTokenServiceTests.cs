using DCF.Api.Services;
using DCF.Data;
using DCF.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DCF.Tests.Services;

public class RememberMeTokenServiceTests
{
    private static DcfDbContext CreateDb(string name)
    {
        var opts = new DbContextOptionsBuilder<DcfDbContext>()
            .UseInMemoryDatabase(name)
            .Options;

        return new DcfDbContext(opts);
    }

    private static async Task<UserEntity> AddUserAsync(DcfDbContext db, string auth0Sub)
    {
        var user = new UserEntity
        {
            Id = Guid.NewGuid(),
            Auth0Sub = auth0Sub,
            Email = $"{auth0Sub}@example.com",
            DisplayName = auth0Sub,
        };
        db.Users.Add(user);

        await db.SaveChangesAsync();

        return user;
    }

    [Fact]
    public async Task IssueAsync_ThenValidateAsync_ReturnsOwningUsersAuth0Sub()
    {
        using var db = CreateDb("issue_validate_roundtrip");
        var user = await AddUserAsync(db, "auth0|alice");
        var svc = new RememberMeTokenService(db);

        var token = await svc.IssueAsync(user.Id);
        var result = await svc.ValidateAsync(token);

        Assert.Equal("auth0|alice", result);
    }

    [Fact]
    public async Task ValidateAsync_UnknownToken_ReturnsNull()
    {
        using var db = CreateDb("validate_unknown");
        var svc = new RememberMeTokenService(db);

        var result = await svc.ValidateAsync("not-a-real-token");

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateAsync_ExpiredToken_ReturnsNull()
    {
        using var db = CreateDb("validate_expired");
        var user = await AddUserAsync(db, "auth0|bob");
        var svc = new RememberMeTokenService(db);
        var token = await svc.IssueAsync(user.Id);

        var entry = await db.RememberMeTokens.FirstAsync();
        entry.ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1);
        await db.SaveChangesAsync();

        var result = await svc.ValidateAsync(token);

        Assert.Null(result);
    }

    [Fact]
    public async Task RevokeAsync_ThenValidateAsync_ReturnsNull()
    {
        using var db = CreateDb("revoke_then_validate");
        var user = await AddUserAsync(db, "auth0|carol");
        var svc = new RememberMeTokenService(db);
        var token = await svc.IssueAsync(user.Id);

        await svc.RevokeAsync(token);
        var result = await svc.ValidateAsync(token);

        Assert.Null(result);
    }

    [Fact]
    public async Task RevokeAsync_OnlyDeletesMatchingDevice_OtherTokensForSameUserSurvive()
    {
        using var db = CreateDb("revoke_scoped_to_device");
        var user = await AddUserAsync(db, "auth0|dave");
        var svc = new RememberMeTokenService(db);
        var laptopToken = await svc.IssueAsync(user.Id);
        var phoneToken = await svc.IssueAsync(user.Id);

        await svc.RevokeAsync(laptopToken);

        Assert.Null(await svc.ValidateAsync(laptopToken));
        Assert.Equal("auth0|dave", await svc.ValidateAsync(phoneToken));
    }

    [Fact]
    public async Task ExtendIfOwnedByAsync_ValidTokenOwnedByCaller_PushesExpiryForward()
    {
        using var db = CreateDb("extend_owned");
        var user = await AddUserAsync(db, "auth0|erin");
        var svc = new RememberMeTokenService(db);
        var token = await svc.IssueAsync(user.Id);

        var entry = await db.RememberMeTokens.FirstAsync();
        entry.ExpiresAt = DateTimeOffset.UtcNow.AddDays(1);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await svc.ExtendIfOwnedByAsync(token, user.Id);

        var updated = await db.RememberMeTokens.FirstAsync();
        Assert.True(updated.ExpiresAt > DateTimeOffset.UtcNow.AddDays(29));
    }

    [Fact]
    public async Task ExtendIfOwnedByAsync_TokenOwnedByDifferentUser_DoesNotExtend()
    {
        using var db = CreateDb("extend_wrong_owner");
        var user = await AddUserAsync(db, "auth0|frank");
        var otherUserId = Guid.NewGuid();
        var svc = new RememberMeTokenService(db);
        var token = await svc.IssueAsync(user.Id);

        var entry = await db.RememberMeTokens.FirstAsync();
        entry.ExpiresAt = DateTimeOffset.UtcNow.AddDays(1);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await svc.ExtendIfOwnedByAsync(token, otherUserId);

        var updated = await db.RememberMeTokens.FirstAsync();
        Assert.True(updated.ExpiresAt < DateTimeOffset.UtcNow.AddDays(2));
    }
}
