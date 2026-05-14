using DCF.Data;
using DCF.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DCF.Api.Services;

public record UserProfile(Guid Id, string Email, string DisplayName, bool IsAdmin);

public class UserService(DcfDbContext db) : IUserService
{
    public async Task<UserProfile> UpsertAsync(string sub, string email, string name)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Auth0Sub == sub);

        if (user is null)
        {
            user = new UserEntity { Id = Guid.NewGuid(), Auth0Sub = sub, Email = email, DisplayName = name };
            db.Users.Add(user);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (!await db.Users.AnyAsync(u => u.Auth0Sub == sub))
                {
                    throw;
                }

                db.ChangeTracker.Clear();

                user = await db.Users.FirstAsync(u => u.Auth0Sub == sub);
            }
        }
        else
        {
            user.Email = email;
            user.DisplayName = name;

            await db.SaveChangesAsync();
        }

        return new UserProfile(user.Id, user.Email, user.DisplayName, user.IsAdmin);
    }
}
