using DCF.Api.Models;
using DCF.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DCF.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Authorize]
public class AuthController(IUserService userService, IRememberMeTokenService rememberMeTokenService) : ControllerBase
{
    private const string RememberMeHeader = "X-Remember-Token";

    [HttpGet("me")]
    public async Task<IActionResult> GetUser()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub")
                  ?? throw new InvalidOperationException("No sub claim");

        var profile = await userService.GetAsync(sub);

        if (profile is null)
        {
            return NotFound();
        }

        var rememberToken = Request.Headers[RememberMeHeader].FirstOrDefault();

        if (!string.IsNullOrEmpty(rememberToken))
        {
            await rememberMeTokenService.ExtendIfOwnedByAsync(rememberToken, profile.Id);
        }

        return Ok(new { profile.Id, profile.Email, profile.DisplayName, profile.IsAdmin, profile.EmailNotificationsEnabled });
    }

    [HttpPost("me")]
    public async Task<IActionResult> UpsertUser([FromBody] UpsertUserRequest? request)
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub")
                  ?? throw new InvalidOperationException("No sub claim");
        var email = request?.Email ?? string.Empty;
        var name = request?.DisplayName ?? string.Empty;

        var profile = await userService.UpsertAsync(sub, email, name, request?.DisplayName);

        return Ok(new { profile.Id, profile.Email, profile.DisplayName, profile.IsAdmin, profile.EmailNotificationsEnabled });
    }

    [HttpPost("remember-me")]
    public async Task<IActionResult> IssueRememberMeToken()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub")
                  ?? throw new InvalidOperationException("No sub claim");

        var profile = await userService.GetAsync(sub);

        if (profile is null)
        {
            return NotFound();
        }

        var token = await rememberMeTokenService.IssueAsync(profile.Id);

        return Ok(new { token });
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest? request)
    {
        if (!string.IsNullOrEmpty(request?.RememberToken))
        {
            await rememberMeTokenService.RevokeAsync(request.RememberToken);
        }

        return NoContent();
    }
}
