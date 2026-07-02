using System.Security.Cryptography;
using System.Text;
using DCF.Data;
using DCF.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DCF.Api.Services;

public class RememberMeTokenService(DcfDbContext db) : IRememberMeTokenService
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    public async Task<string> IssueAsync(Guid userId)
    {
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        db.RememberMeTokens.Add(new RememberMeTokenEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = Hash(rawToken),
            ExpiresAt = DateTimeOffset.UtcNow.Add(Lifetime),
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync();

        return rawToken;
    }

    public async Task<string?> ValidateAsync(string rawToken)
    {
        if (string.IsNullOrEmpty(rawToken))
        {
            return null;
        }

        var hash = Hash(rawToken);

        var entry = await db.RememberMeTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash);

        if (entry is null || entry.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return null;
        }

        return entry.User.Auth0Sub;
    }

    public async Task ExtendIfOwnedByAsync(string rawToken, Guid userId)
    {
        if (string.IsNullOrEmpty(rawToken))
        {
            return;
        }

        var hash = Hash(rawToken);

        var entry = await db.RememberMeTokens.FirstOrDefaultAsync(t => t.TokenHash == hash);

        if (entry is null || entry.UserId != userId || entry.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return;
        }

        entry.ExpiresAt = DateTimeOffset.UtcNow.Add(Lifetime);

        await db.SaveChangesAsync();
    }

    public async Task RevokeAsync(string rawToken)
    {
        if (string.IsNullOrEmpty(rawToken))
        {
            return;
        }

        var hash = Hash(rawToken);

        var entry = await db.RememberMeTokens.FirstOrDefaultAsync(t => t.TokenHash == hash);

        if (entry is not null)
        {
            db.RememberMeTokens.Remove(entry);

            await db.SaveChangesAsync();
        }
    }

    private static string Hash(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));

        return Convert.ToBase64String(bytes);
    }
}
