using DCF.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DCF.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Authorize]
public class AuthController(UserService userService) : ControllerBase
{
    [HttpPost("me")]
    public async Task<IActionResult> UpsertUser()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub")
                  ?? throw new InvalidOperationException("No sub claim");
        var email = User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
        var name = User.FindFirstValue("name") ?? email;

        var profile = await userService.UpsertAsync(sub, email, name);
        return Ok(new { profile.Id, profile.Email, profile.DisplayName, profile.IsAdmin });
    }
}
