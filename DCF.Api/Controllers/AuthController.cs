using DCF.Data;
using DCF.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace DCF.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Authorize]
public class AuthController(DcfDbContext db) : ControllerBase
{
    [HttpPost("me")]
    public async Task<IActionResult> UpsertUser()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? User.FindFirstValue("sub")
                  ?? throw new InvalidOperationException("No sub claim");
        var email = User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
        var name = User.FindFirstValue("name") ?? email;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Auth0Sub == sub);
        if (user is null)
        {
            user = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = sub, Email = email, DisplayName = name };
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        return Ok(new { user.Id, user.Email, user.DisplayName, user.IsAdmin });
    }
}
