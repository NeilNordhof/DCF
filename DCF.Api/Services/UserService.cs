using DCF.Data;
using DCF.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DCF.Api.Services;

public record UserProfile(Guid Id, string Email, string DisplayName, bool IsAdmin, bool EmailNotificationsEnabled);

public class UserService(DcfDbContext db) : IUserService
{
    public async Task<UserProfile> UpsertAsync(string sub, string email, string name, string? displayName = null)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Auth0Sub == sub);

        if (user is null)
        {
            if (!string.IsNullOrWhiteSpace(email))
            {
                user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
            }

            if (user is not null)
            {
                user.Auth0Sub = sub;

                await db.SaveChangesAsync();
            }
            else
            {
                user = new UserEntity
                {
                    Id = Guid.NewGuid(),
                    Auth0Sub = sub,
                    Email = email,
                    DisplayName = !string.IsNullOrWhiteSpace(displayName) ? displayName : name
                };
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
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(email))
            {
                user.Email = email;
            }

            await db.SaveChangesAsync();
        }

        return new UserProfile(user.Id, user.Email, user.DisplayName, user.IsAdmin, user.EmailNotificationsEnabled);
    }

    public async Task<UserProfile?> GetAsync(string sub)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Auth0Sub == sub);

        if (user is null)
        {
            return null;
        }

        return new UserProfile(user.Id, user.Email, user.DisplayName, user.IsAdmin, user.EmailNotificationsEnabled);
    }
}
