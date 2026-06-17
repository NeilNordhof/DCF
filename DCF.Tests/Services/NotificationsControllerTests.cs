using DCF.Api.Controllers;
using DCF.Api.Models;
using DCF.Api.Services;
using DCF.Data;
using DCF.Data.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace DCF.Tests.Services;

public class NotificationsControllerTests
{
    private static DcfDbContext CreateDb(string name)
    {
        var opts = new DbContextOptionsBuilder<DcfDbContext>()
            .UseInMemoryDatabase(name)
            .Options;

        return new DcfDbContext(opts);
    }

    private static EmailTokenService CreateTokenService()
    {
        var opts = Options.Create(new EmailOptions { UnsubscribeSecret = "test-secret-32-chars-minimum-len!" });

        return new EmailTokenService(opts);
    }

    [Fact]
    public async Task Unsubscribe_ValidToken_DisablesNotificationsAndReturnsNoContent()
    {
        using var db = CreateDb("unsub_valid");
        var userId = Guid.NewGuid();

        db.Users.Add(new UserEntity
        {
            Id = userId,
            Auth0Sub = "auth0|test",
            Email = "user@example.com",
            DisplayName = "Test User",
            EmailNotificationsEnabled = true
        });

        await db.SaveChangesAsync();

        var tokenService = CreateTokenService();
        var token = tokenService.GenerateToken(userId);
        var controller = new NotificationsController(db, tokenService);

        var result = await controller.Unsubscribe(new UnsubscribeRequest(token));

        Assert.IsType<NoContentResult>(result);

        var user = await db.Users.FindAsync(userId);

        Assert.False(user!.EmailNotificationsEnabled);
    }

    [Fact]
    public async Task Unsubscribe_InvalidToken_ReturnsBadRequest()
    {
        using var db = CreateDb("unsub_invalid");
        var tokenService = CreateTokenService();
        var controller = new NotificationsController(db, tokenService);

        var result = await controller.Unsubscribe(new UnsubscribeRequest("not-valid"));

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Unsubscribe_ValidTokenAlreadyUnsubscribed_ReturnsNoContent()
    {
        using var db = CreateDb("unsub_idempotent");
        var userId = Guid.NewGuid();

        db.Users.Add(new UserEntity
        {
            Id = userId,
            Auth0Sub = "auth0|test2",
            Email = "user2@example.com",
            DisplayName = "Test User 2",
            EmailNotificationsEnabled = false
        });

        await db.SaveChangesAsync();

        var tokenService = CreateTokenService();
        var token = tokenService.GenerateToken(userId);
        var controller = new NotificationsController(db, tokenService);

        var result = await controller.Unsubscribe(new UnsubscribeRequest(token));

        Assert.IsType<NoContentResult>(result);
    }

    private static NotificationsController CreateControllerWithSub(DcfDbContext db, EmailTokenService tokenService, string sub)
    {
        var claims = new List<System.Security.Claims.Claim>
        {
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, sub)
        };

        var identity = new System.Security.Claims.ClaimsIdentity(claims);
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);

        var controller = new NotificationsController(db, tokenService);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };

        return controller;
    }

    [Fact]
    public async Task UpdatePreferences_ValidUser_UpdatesFieldAndReturnsNoContent()
    {
        using var db = CreateDb("pref_update_valid");
        const string sub = "auth0|pref-user";

        db.Users.Add(new UserEntity
        {
            Id = Guid.NewGuid(),
            Auth0Sub = sub,
            Email = "pref@example.com",
            DisplayName = "Pref User",
            EmailNotificationsEnabled = false
        });

        await db.SaveChangesAsync();

        var tokenService = CreateTokenService();
        var controller = CreateControllerWithSub(db, tokenService, sub);
        var request = new UpdateNotificationPreferencesRequest(true);

        var result = await controller.UpdatePreferences(request);

        Assert.IsType<NoContentResult>(result);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Auth0Sub == sub);

        Assert.True(user!.EmailNotificationsEnabled);
    }

    [Fact]
    public async Task UpdatePreferences_UserNotFound_ReturnsNotFound()
    {
        using var db = CreateDb("pref_update_notfound");
        var tokenService = CreateTokenService();
        var controller = CreateControllerWithSub(db, tokenService, "auth0|ghost");
        var request = new UpdateNotificationPreferencesRequest(true);

        var result = await controller.UpdatePreferences(request);

        Assert.IsType<NotFoundResult>(result);
    }
}
