using DCF.Api.Models;
using DCF.Api.Services;
using DCF.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace DCF.Api.Controllers;

[ApiController]
[Route("api/notifications")]
public class NotificationsController(
    DcfDbContext db,
    EmailTokenService emailTokenService) : ControllerBase
{
    [HttpPost("unsubscribe")]
    [AllowAnonymous]
    public async Task<IActionResult> Unsubscribe([FromBody] UnsubscribeRequest request)
    {
        var userId = emailTokenService.ValidateToken(request.Token);

        if (userId is null)
        {

            return BadRequest("Invalid token.");
        }

        var user = await db.Users.FindAsync(userId.Value);

        if (user is null)
        {

            return BadRequest("User not found.");
        }

        user.EmailNotificationsEnabled = false;

        await db.SaveChangesAsync();

        return NoContent();
    }

    [HttpPatch("preferences")]
    [Authorize]
    public async Task<IActionResult> UpdatePreferences([FromBody] UpdateNotificationPreferencesRequest request)
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub")
                  ?? throw new InvalidOperationException("No sub claim");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Auth0Sub == sub);

        if (user is null)
        {

            return NotFound();
        }

        user.EmailNotificationsEnabled = request.EmailNotificationsEnabled;

        await db.SaveChangesAsync();

        return NoContent();
    }
}
