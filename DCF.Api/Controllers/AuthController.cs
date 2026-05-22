using DCF.Api.Models;
using DCF.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DCF.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Authorize]
public class AuthController(IUserService userService) : ControllerBase
{
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

        return Ok(new { profile.Id, profile.Email, profile.DisplayName, profile.IsAdmin });
    }

    [HttpPost("me")]
    public async Task<IActionResult> UpsertUser([FromBody] UpsertUserRequest? request)
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub")
                  ?? throw new InvalidOperationException("No sub claim");
        var email = User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
        var name = User.FindFirstValue("name") ?? email;

        var profile = await userService.UpsertAsync(sub, email, name, request?.DisplayName);

        return Ok(new { profile.Id, profile.Email, profile.DisplayName, profile.IsAdmin });
    }
}
