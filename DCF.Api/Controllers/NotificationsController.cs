using DCF.Api.Models;
using DCF.Api.Services;
using DCF.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DCF.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[AllowAnonymous]
public class NotificationsController(
    DcfDbContext db,
    EmailTokenService emailTokenService) : ControllerBase
{
    [HttpPost("unsubscribe")]
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
}
