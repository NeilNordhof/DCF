# Persistent Login (30-Day Remember Me) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let production logins survive up to 30 days without re-authenticating through Auth0Lock, even though Auth0 itself hard-caps this app's implicit-flow access tokens at 24 hours.

**Architecture:** The API issues and owns a second, long-lived opaque credential (`RememberMeTokenEntity`, DB-backed, hashed at rest) alongside the existing Auth0 access token. The frontend uses the Auth0 token while it's fresh and falls back to the remember-me token once it isn't. A second ASP.NET Core authentication scheme validates the remember-me token; `/api/auth/me` (already called on every page load) extends it, making the window roll forward on any return visit rather than expiring on a fixed schedule.

**Tech Stack:** ASP.NET Core auth schemes (`AddPolicyScheme`), EF Core migration, xUnit + EF Core InMemory, React context, Vitest.

## Global Constraints

- Window is rolling: any return visit extends the remember-me token back to 30 days out; 30 days with no return visit (or explicit logout) ends the session.
- Each device/browser gets an independent token; logging in on a new device does not affect others; logout revokes only the calling device's token.
- Dev-mode login (`DevAuthContext`/`DevAuthHandler`) is unaffected and needs no changes — it already persists indefinitely.
- Explicitly out of scope (do not build): rotating the token's secret value on each use, reuse-detection/mass-revocation, a background cleanup job for expired rows, and a "view active sessions" / "log out all devices" UI.

---

### Task 1: `RememberMeTokenEntity` data model + migration

**Files:**
- Create: `DCF.Data/Entities/RememberMeTokenEntity.cs`
- Modify: `DCF.Data/Entities/UserEntity.cs`
- Modify: `DCF.Data/DcfDbContext.cs`
- Create (generated): `DCF.Data/Migrations/<timestamp>_AddRememberMeTokens.cs` and its `.Designer.cs`; modifies `DCF.Data/Migrations/DcfDbContextModelSnapshot.cs`

**Interfaces:**
- Produces: `RememberMeTokenEntity` with `Id: Guid`, `UserId: Guid`, `User: UserEntity`, `TokenHash: string`, `ExpiresAt: DateTimeOffset`, `CreatedAt: DateTimeOffset`; `DcfDbContext.RememberMeTokens: DbSet<RememberMeTokenEntity>`

- [ ] **Step 1: Create the entity**

```csharp
// DCF.Data/Entities/RememberMeTokenEntity.cs
namespace DCF.Data.Entities;

public class RememberMeTokenEntity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public UserEntity User { get; set; } = null!;
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
```

- [ ] **Step 2: Add the inverse navigation collection to `UserEntity`**

In `DCF.Data/Entities/UserEntity.cs`, add one line after the existing `DraftPicks` property:

```csharp
    public List<LeagueMemberEntity> LeagueMemberships { get; set; } = [];
    public List<LeagueEntity> CommissionedLeagues { get; set; } = [];
    public List<DraftPickEntity> DraftPicks { get; set; } = [];
    public List<RememberMeTokenEntity> RememberMeTokens { get; set; } = [];
```

- [ ] **Step 3: Register the `DbSet` and indexes in `DcfDbContext`**

In `DCF.Data/DcfDbContext.cs`, add the `DbSet` after the existing `ShowScheduleEntries` line:

```csharp
    public DbSet<ShowScheduleEntryEntity> ShowScheduleEntries => Set<ShowScheduleEntryEntity>();
    public DbSet<RememberMeTokenEntity> RememberMeTokens => Set<RememberMeTokenEntity>();
```

Add these two lines inside `OnModelCreating`, after the existing `mb.Entity<ShowScheduleEntryEntity>().HasIndex(e => e.ShowId);` line:

```csharp
        mb.Entity<ShowScheduleEntryEntity>().HasIndex(e => e.ShowId);

        mb.Entity<RememberMeTokenEntity>().HasIndex(e => e.TokenHash).IsUnique();
        mb.Entity<RememberMeTokenEntity>().HasIndex(e => e.UserId);
```

The FK to `UserEntity` needs no explicit Fluent API — EF Core infers it by convention from the `UserId`/`User` pair, the same way `ShowScheduleEntryEntity.ShowId`/`Show` and `CorpsId`/`Corps` are inferred without explicit configuration.

- [ ] **Step 4: Generate the migration**

Run from the repo root:

```bash
dotnet ef migrations add AddRememberMeTokens --project DCF.Data/DCF.Data.csproj --startup-project DCF.Api/DCF.Api.csproj
```

Expected: a new file `DCF.Data/Migrations/<timestamp>_AddRememberMeTokens.cs` that creates a `RememberMeTokens` table with columns matching the entity, a FK to `Users`, a unique index on `TokenHash`, and a non-unique index on `UserId`.

- [ ] **Step 5: Build to confirm the migration compiles and applies cleanly**

```bash
dotnet build DCF.slnx
```

Expected: build succeeds with no errors.

- [ ] **Step 6: Commit**

```bash
git add DCF.Data/Entities/RememberMeTokenEntity.cs DCF.Data/Entities/UserEntity.cs DCF.Data/DcfDbContext.cs DCF.Data/Migrations/
git commit -m "feat: add RememberMeTokenEntity data model and migration"
```

---

### Task 2: `RememberMeTokenService` (TDD)

**Files:**
- Create: `DCF.Api/Services/IRememberMeTokenService.cs`
- Create: `DCF.Api/Services/RememberMeTokenService.cs`
- Test: `DCF.Tests/Services/RememberMeTokenServiceTests.cs`

**Interfaces:**
- Consumes: `RememberMeTokenEntity`, `DcfDbContext.RememberMeTokens`, `DcfDbContext.Users` (Task 1)
- Produces: `IRememberMeTokenService` with `IssueAsync(Guid userId): Task<string>`, `ValidateAsync(string rawToken): Task<string?>` (returns the owning user's `Auth0Sub`, not their `Guid` — every other controller reads `ClaimTypes.NameIdentifier` as an Auth0 sub string, so this keeps that contract intact regardless of which auth scheme authenticated the request), `ExtendIfOwnedByAsync(string rawToken, Guid userId): Task`, `RevokeAsync(string rawToken): Task`

- [ ] **Step 1: Write the failing test suite**

```csharp
// DCF.Tests/Services/RememberMeTokenServiceTests.cs
using DCF.Api.Services;
using DCF.Data;
using DCF.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DCF.Tests.Services;

public class RememberMeTokenServiceTests
{
    private static DcfDbContext CreateDb(string name)
    {
        var opts = new DbContextOptionsBuilder<DcfDbContext>()
            .UseInMemoryDatabase(name)
            .Options;

        return new DcfDbContext(opts);
    }

    private static async Task<UserEntity> AddUserAsync(DcfDbContext db, string auth0Sub)
    {
        var user = new UserEntity
        {
            Id = Guid.NewGuid(),
            Auth0Sub = auth0Sub,
            Email = $"{auth0Sub}@example.com",
            DisplayName = auth0Sub,
        };
        db.Users.Add(user);

        await db.SaveChangesAsync();

        return user;
    }

    [Fact]
    public async Task IssueAsync_ThenValidateAsync_ReturnsOwningUsersAuth0Sub()
    {
        using var db = CreateDb("issue_validate_roundtrip");
        var user = await AddUserAsync(db, "auth0|alice");
        var svc = new RememberMeTokenService(db);

        var token = await svc.IssueAsync(user.Id);
        var result = await svc.ValidateAsync(token);

        Assert.Equal("auth0|alice", result);
    }

    [Fact]
    public async Task ValidateAsync_UnknownToken_ReturnsNull()
    {
        using var db = CreateDb("validate_unknown");
        var svc = new RememberMeTokenService(db);

        var result = await svc.ValidateAsync("not-a-real-token");

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateAsync_ExpiredToken_ReturnsNull()
    {
        using var db = CreateDb("validate_expired");
        var user = await AddUserAsync(db, "auth0|bob");
        var svc = new RememberMeTokenService(db);
        var token = await svc.IssueAsync(user.Id);

        var entry = await db.RememberMeTokens.FirstAsync();
        entry.ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1);
        await db.SaveChangesAsync();

        var result = await svc.ValidateAsync(token);

        Assert.Null(result);
    }

    [Fact]
    public async Task RevokeAsync_ThenValidateAsync_ReturnsNull()
    {
        using var db = CreateDb("revoke_then_validate");
        var user = await AddUserAsync(db, "auth0|carol");
        var svc = new RememberMeTokenService(db);
        var token = await svc.IssueAsync(user.Id);

        await svc.RevokeAsync(token);
        var result = await svc.ValidateAsync(token);

        Assert.Null(result);
    }

    [Fact]
    public async Task RevokeAsync_OnlyDeletesMatchingDevice_OtherTokensForSameUserSurvive()
    {
        using var db = CreateDb("revoke_scoped_to_device");
        var user = await AddUserAsync(db, "auth0|dave");
        var svc = new RememberMeTokenService(db);
        var laptopToken = await svc.IssueAsync(user.Id);
        var phoneToken = await svc.IssueAsync(user.Id);

        await svc.RevokeAsync(laptopToken);

        Assert.Null(await svc.ValidateAsync(laptopToken));
        Assert.Equal("auth0|dave", await svc.ValidateAsync(phoneToken));
    }

    [Fact]
    public async Task ExtendIfOwnedByAsync_ValidTokenOwnedByCaller_PushesExpiryForward()
    {
        using var db = CreateDb("extend_owned");
        var user = await AddUserAsync(db, "auth0|erin");
        var svc = new RememberMeTokenService(db);
        var token = await svc.IssueAsync(user.Id);

        var entry = await db.RememberMeTokens.FirstAsync();
        entry.ExpiresAt = DateTimeOffset.UtcNow.AddDays(1);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await svc.ExtendIfOwnedByAsync(token, user.Id);

        var updated = await db.RememberMeTokens.FirstAsync();
        Assert.True(updated.ExpiresAt > DateTimeOffset.UtcNow.AddDays(29));
    }

    [Fact]
    public async Task ExtendIfOwnedByAsync_TokenOwnedByDifferentUser_DoesNotExtend()
    {
        using var db = CreateDb("extend_wrong_owner");
        var user = await AddUserAsync(db, "auth0|frank");
        var otherUserId = Guid.NewGuid();
        var svc = new RememberMeTokenService(db);
        var token = await svc.IssueAsync(user.Id);

        var entry = await db.RememberMeTokens.FirstAsync();
        entry.ExpiresAt = DateTimeOffset.UtcNow.AddDays(1);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await svc.ExtendIfOwnedByAsync(token, otherUserId);

        var updated = await db.RememberMeTokens.FirstAsync();
        Assert.True(updated.ExpiresAt < DateTimeOffset.UtcNow.AddDays(2));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~RememberMeTokenServiceTests"
```

Expected: compile error (`RememberMeTokenService` and `IRememberMeTokenService` don't exist yet).

- [ ] **Step 3: Implement the interface**

```csharp
// DCF.Api/Services/IRememberMeTokenService.cs
namespace DCF.Api.Services;

public interface IRememberMeTokenService
{
    Task<string> IssueAsync(Guid userId);
    Task<string?> ValidateAsync(string rawToken);
    Task ExtendIfOwnedByAsync(string rawToken, Guid userId);
    Task RevokeAsync(string rawToken);
}
```

- [ ] **Step 4: Implement the service**

```csharp
// DCF.Api/Services/RememberMeTokenService.cs
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
```

Note on `TokenHash`: unlike `EmailTokenService`'s HMAC (which binds a token to a guessable `userId` and therefore needs a secret key), this token's unforgeability comes from the raw value's own 256 bits of randomness. A plain hash is sufficient — no secret is needed, and none is introduced.

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test DCF.Tests/DCF.Tests.csproj --filter "FullyQualifiedName~RememberMeTokenServiceTests"
```

Expected: all 7 tests pass.

- [ ] **Step 6: Commit**

```bash
git add DCF.Api/Services/IRememberMeTokenService.cs DCF.Api/Services/RememberMeTokenService.cs DCF.Tests/Services/RememberMeTokenServiceTests.cs
git commit -m "feat: add RememberMeTokenService with issue/validate/extend/revoke"
```

---

### Task 3: Wire into the API — auth scheme + endpoints

**Files:**
- Create: `DCF.Api/RememberMeAuthHandler.cs`
- Modify: `DCF.Api/Program.cs`
- Modify: `DCF.Api/Models/AuthRequests.cs`
- Modify: `DCF.Api/Controllers/AuthController.cs`

**Interfaces:**
- Consumes: `IRememberMeTokenService` (Task 2)
- Produces: `POST /api/auth/remember-me` (returns `{ token: string }`), `POST /api/auth/logout` (anonymous, body `{ rememberToken: string | null }`), `GET /api/auth/me` now also reads an `X-Remember-Token` request header

This task has no dedicated automated tests of its own — `RememberMeAuthHandler` and the controller actions are thin adapters with no branching logic beyond what `RememberMeTokenServiceTests` (Task 2) already covers. Full HTTP-level integration tests (`WebApplicationFactory`) are already an established out-of-scope decision for this codebase (see `docs/superpowers/specs/2026-06-24-testing-and-ci-design.md`), so this task is verified by build success plus the manual check in Step 5.

- [ ] **Step 1: Create the auth handler**

```csharp
// DCF.Api/RememberMeAuthHandler.cs
using System.Security.Claims;
using System.Text.Encodings.Web;
using DCF.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace DCF.Api;

public class RememberMeAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IRememberMeTokenService rememberMeTokenService)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authHeader = Request.Headers.Authorization.FirstOrDefault();

        if (authHeader is null || !authHeader.StartsWith("Bearer "))
        {
            return AuthenticateResult.NoResult();
        }

        var token = authHeader["Bearer ".Length..].Trim();

        if (string.IsNullOrEmpty(token))
        {
            return AuthenticateResult.NoResult();
        }

        var sub = await rememberMeTokenService.ValidateAsync(token);

        if (sub is null)
        {
            return AuthenticateResult.Fail("Invalid or expired remember-me token");
        }

        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, sub) };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}
```

- [ ] **Step 2: Wire the second auth scheme and register the service in `Program.cs`**

Replace the `else` branch of the environment check (the production auth setup) with:

```csharp
else
{
    const string Auth0Scheme = "Auth0Jwt";
    const string RememberMeScheme = "RememberMe";

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddPolicyScheme(JwtBearerDefaults.AuthenticationScheme, "Auth0 or RememberMe", opt =>
        {
            opt.ForwardDefaultSelector = context =>
            {
                var authHeader = context.Request.Headers.Authorization.FirstOrDefault() ?? string.Empty;
                var token = authHeader.StartsWith("Bearer ") ? authHeader["Bearer ".Length..].Trim() : string.Empty;

                return token.Count(c => c == '.') == 2 ? Auth0Scheme : RememberMeScheme;
            };
        })
        .AddJwtBearer(Auth0Scheme, opt =>
        {
            opt.Authority = $"https://{builder.Configuration["Auth0:Domain"]}/";
            opt.Audience = builder.Configuration["Auth0:Audience"];
        })
        .AddScheme<AuthenticationSchemeOptions, RememberMeAuthHandler>(RememberMeScheme, null);
}
```

A JWT always has two `.` separators (header.payload.signature); the opaque remember-me token (a bare base64url blob) never does. That's what `ForwardDefaultSelector` uses to route each request to the right handler. The dev-mode (`if`) branch is unchanged.

Add the service registration in the shared (not dev/prod-conditional) section, alongside the other scoped services:

```csharp
    builder.Services.AddScoped<IUserService, UserService>();
    builder.Services.AddScoped<IAdminService, AdminService>();
    builder.Services.AddScoped<ILeagueService, LeagueService>();
    builder.Services.AddScoped<IStandingsService, StandingsService>();
    builder.Services.AddScoped<IDraftService, DraftService>();
    builder.Services.AddScoped<IRememberMeTokenService, RememberMeTokenService>();
```

This must be registered unconditionally (not inside the dev/prod branch) because `AuthController` will depend on it regardless of environment — DI resolves all controller dependencies at startup, and a missing registration would break `AuthController` even in dev mode where the scheme itself isn't exercised.

- [ ] **Step 3: Add the `LogoutRequest` model**

In `DCF.Api/Models/AuthRequests.cs`:

```csharp
namespace DCF.Api.Models;

public record UpsertUserRequest(string? DisplayName, string? Email);
public record LogoutRequest(string? RememberToken);
```

- [ ] **Step 4: Update `AuthController`**

Replace the full contents of `DCF.Api/Controllers/AuthController.cs`:

```csharp
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
```

`Logout` is `[AllowAnonymous]` deliberately: revocation only requires possessing the raw token value (which only the owning device has), not a separately-valid credential, and it needs to work even when both the Auth0 token and the remember-me token are already dead.

- [ ] **Step 5: Build and manually verify**

```bash
dotnet build DCF.slnx
```

Expected: build succeeds. Then, with the API running locally against `docker compose up postgres mosquitto mailpit`, manually confirm with `curl` or the browser devtools network tab that `POST /api/auth/remember-me` (with a valid dev bearer token) returns a token, and that presenting that token as `Authorization: Bearer <token>` on `GET /api/auth/me` succeeds.

- [ ] **Step 6: Commit**

```bash
git add DCF.Api/RememberMeAuthHandler.cs DCF.Api/Program.cs DCF.Api/Models/AuthRequests.cs DCF.Api/Controllers/AuthController.cs
git commit -m "feat: wire RememberMeTokenService into auth pipeline and endpoints"
```

---

### Task 4: Extract the session-resolution decision into a pure, tested function (TDD)

**Files:**
- Create: `DCF.Web/src/context/authSession.ts`
- Test: `DCF.Web/src/context/authSession.test.ts`

**Interfaces:**
- Produces: `REMEMBER_TOKEN_STORAGE_KEY: string`, `StoredAuthState` interface, `ResolvedSession` interface, `resolveSession(state: StoredAuthState, now: number): ResolvedSession`

This mirrors the existing codebase pattern of extracting pure decision logic out of stateful/plumbing-heavy code so it can be tested directly (see `ApplyStatusTransitions` in `SeasonStatusService` and its tests) rather than trying to mount `AuthContext`'s full `Auth0LockPasswordless`-wrapping provider under test.

- [ ] **Step 1: Write the failing tests**

```typescript
// DCF.Web/src/context/authSession.test.ts
import { describe, it, expect } from 'vitest';
import { resolveSession } from './authSession';

const user = { name: 'Alice', email: 'alice@example.com' };
const now = 1_000_000;

describe('resolveSession', () => {
  it('uses the access token when it is still valid', () => {
    const result = resolveSession(
      { accessToken: 'access-1', tokenExpiry: now + 1000, rememberToken: 'remember-1', user },
      now
    );

    expect(result).toEqual({ isAuthenticated: true, user, bearerToken: 'access-1' });
  });

  it('falls back to the remember token when the access token has expired', () => {
    const result = resolveSession(
      { accessToken: 'access-1', tokenExpiry: now - 1000, rememberToken: 'remember-1', user },
      now
    );

    expect(result).toEqual({ isAuthenticated: true, user, bearerToken: 'remember-1' });
  });

  it('falls back to the remember token when there is no access token at all', () => {
    const result = resolveSession(
      { accessToken: null, tokenExpiry: null, rememberToken: 'remember-1', user },
      now
    );

    expect(result).toEqual({ isAuthenticated: true, user, bearerToken: 'remember-1' });
  });

  it('is not authenticated when neither token is valid', () => {
    const result = resolveSession(
      { accessToken: 'access-1', tokenExpiry: now - 1000, rememberToken: null, user },
      now
    );

    expect(result).toEqual({ isAuthenticated: false, user: null, bearerToken: null });
  });

  it('is not authenticated when there is no stored state at all', () => {
    const result = resolveSession(
      { accessToken: null, tokenExpiry: null, rememberToken: null, user: null },
      now
    );

    expect(result).toEqual({ isAuthenticated: false, user: null, bearerToken: null });
  });
});
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
cd DCF.Web && npm test -- authSession
```

Expected: FAIL — `./authSession` does not exist yet.

- [ ] **Step 3: Implement the module**

```typescript
// DCF.Web/src/context/authSession.ts
export const REMEMBER_TOKEN_STORAGE_KEY = 'dcf_remember_token';

export interface StoredAuthState {
  accessToken: string | null;
  tokenExpiry: number | null;
  rememberToken: string | null;
  user: { name: string; email: string } | null;
}

export interface ResolvedSession {
  isAuthenticated: boolean;
  user: { name: string; email: string } | null;
  bearerToken: string | null;
}

export function resolveSession(state: StoredAuthState, now: number): ResolvedSession {
  const accessTokenValid = state.tokenExpiry !== null && now < state.tokenExpiry && !!state.accessToken;

  if (accessTokenValid) {
    return { isAuthenticated: true, user: state.user, bearerToken: state.accessToken };
  }

  if (state.rememberToken) {
    return { isAuthenticated: true, user: state.user, bearerToken: state.rememberToken };
  }

  return { isAuthenticated: false, user: null, bearerToken: null };
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
cd DCF.Web && npm test -- authSession
```

Expected: all 5 tests pass.

- [ ] **Step 5: Commit**

```bash
git add DCF.Web/src/context/authSession.ts DCF.Web/src/context/authSession.test.ts
git commit -m "feat: add resolveSession pure function for auth fallback logic"
```

---

### Task 5: Wire persistence into `AuthContext`, `client.ts`, and `App.tsx`

**Files:**
- Modify: `DCF.Web/src/api/client.ts`
- Modify: `DCF.Web/src/context/AuthContext.tsx`
- Modify: `DCF.Web/src/App.tsx`

**Interfaces:**
- Consumes: `resolveSession`, `REMEMBER_TOKEN_STORAGE_KEY` (Task 4); `POST /api/auth/remember-me`, `POST /api/auth/logout`, `GET /api/auth/me` with `X-Remember-Token` header (Task 3)
- Produces: `client.ts` exports `AuthExpiredError`, `api.issueRememberMeToken()`, `api.logout(rememberToken)`

No new dedicated tests in this task — the branching logic it wires together is already covered by `authSession.test.ts` (Task 4); this task is plumbing that connects tested logic to the DOM/network, consistent with this codebase's existing test-the-logic-not-the-plumbing convention.

- [ ] **Step 1: Add `AuthExpiredError`, the remember-token header, and the two new API calls to `client.ts`**

At the top of `DCF.Web/src/api/client.ts`, add the import and error class, and change the token-getter section:

```typescript
import type { ActiveSeason, Corps, CreateLeagueRequest, League, MemberScoreBreakdown, PublicLeague, Season, SeasonCorps, SeasonDetail, Show, ShowPrefillResponse, Standing, UpdateLeagueRequest, UserProfile } from '../types/api';
import { REMEMBER_TOKEN_STORAGE_KEY } from '../context/authSession';

const API_URL = import.meta.env.VITE_API_URL as string;

export class AuthExpiredError extends Error {}

let getToken: (() => Promise<string>) | null = null;

export function setTokenGetter(fn: () => Promise<string>) {
  getToken = fn;
}
```

Replace the existing `getUser` method inside the `api` object with:

```typescript
  getUser: async (): Promise<UserProfile | null> => {
    const token = getToken ? await getToken() : null;
    const rememberToken = localStorage.getItem(REMEMBER_TOKEN_STORAGE_KEY);
    const res = await fetch(`${API_URL}/api/auth/me`, {
      headers: {
        'Content-Type': 'application/json',
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        ...(rememberToken ? { 'X-Remember-Token': rememberToken } : {}),
      },
    });

    if (res.status === 404) return null;
    if (res.status === 401) throw new AuthExpiredError('Session expired');
    if (!res.ok) throw new Error(await res.text());

    return res.json() as Promise<UserProfile>;
  },
```

Add these two methods to the `api` object, alongside `unsubscribe`/`updateNotificationPreferences`:

```typescript
  issueRememberMeToken: (): Promise<{ token: string }> =>
    request<{ token: string }>('/api/auth/remember-me', { method: 'POST' }),
  logout: (rememberToken: string | null) =>
    request<void>('/api/auth/logout', { method: 'POST', body: JSON.stringify({ rememberToken }) }),
```

- [ ] **Step 2: Update `AuthContext.tsx`**

Replace the full contents of `DCF.Web/src/context/AuthContext.tsx`:

```tsx
import { Auth0LockPasswordless } from 'auth0-lock';
import { createContext, useCallback, useContext, useEffect, useRef, useState } from 'react';
import { api } from '../api/client';
import { REMEMBER_TOKEN_STORAGE_KEY, resolveSession } from './authSession';
import { DevAuthProvider, useDevAuth } from './DevAuthContext';

export interface AuthValue {
  isAuthenticated: boolean;
  isLoading: boolean;
  user: { name: string; email: string } | null;
  logout: () => void;
  getAccessTokenSilently: () => Promise<string>;
  loginWithRedirect: () => void;
  devLogin?: (sub: string) => void;
}

const AuthContext = createContext<AuthValue | null>(null);

const TOKEN_KEY = 'dcf_access_token';
const TOKEN_EXPIRY_KEY = 'dcf_token_expiry';
const USER_KEY = 'dcf_user';

function readStoredSession() {
  const expiryStr = localStorage.getItem(TOKEN_EXPIRY_KEY);
  const userStr = localStorage.getItem(USER_KEY);

  return resolveSession(
    {
      accessToken: localStorage.getItem(TOKEN_KEY),
      tokenExpiry: expiryStr ? parseInt(expiryStr, 10) : null,
      rememberToken: localStorage.getItem(REMEMBER_TOKEN_STORAGE_KEY),
      user: userStr ? (JSON.parse(userStr) as { name: string; email: string }) : null,
    },
    Date.now()
  );
}

function DevAuthBridge({ children }: { children: React.ReactNode }) {
  const { isAuthenticated, isLoading, user, logout, getAccessTokenSilently, login } = useDevAuth();

  const value: AuthValue = {
    isAuthenticated,
    isLoading,
    user,
    logout,
    getAccessTokenSilently,
    loginWithRedirect: () => {},
    devLogin: login,
  };

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

function ProductionLockProvider({ children }: { children: React.ReactNode }) {
  const [state, setState] = useState(() => {
    const session = readStoredSession();

    return {
      isAuthenticated: session.isAuthenticated,
      isLoading: false,
      user: session.user,
    };
  });

  const lockRef = useRef<InstanceType<typeof Auth0LockPasswordless> | null>(null);

  useEffect(() => {
    const lock = new Auth0LockPasswordless(
      import.meta.env.VITE_AUTH0_CLIENT_ID,
      import.meta.env.VITE_AUTH0_DOMAIN,
      {
        container: 'auth0-lock-container',
        passwordlessMethod: 'code',
        allowedConnections: ['email', 'google-oauth2'],
        closable: false,
        avatar: null,
        auth: {
          responseType: 'token id_token',
          audience: import.meta.env.VITE_AUTH0_AUDIENCE,
          redirect: false,
          params: { scope: 'openid profile email' },
        },
        socialButtonStyle: 'big',
        languageDictionary: { title: '' },
        theme: {
          logo: '',
          primaryColor: '#c084fc',
          hideMainScreenTitle: true,
        },
      },
    );

    lock.on('authenticated', (authResult) => {
      // Decode id_token directly — getUserInfo fails when a custom audience is set
      // because the access_token is scoped to our API, not Auth0's /userinfo endpoint
      const b64 = authResult.idToken.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
      const claims = JSON.parse(atob(b64)) as Record<string, string>;
      const expiry = Date.now() + authResult.expiresIn * 1000;
      const user = { name: claims['name'] ?? claims['email'] ?? '', email: claims['email'] ?? '' };

      localStorage.setItem(TOKEN_KEY, authResult.accessToken);
      localStorage.setItem(TOKEN_EXPIRY_KEY, String(expiry));
      localStorage.setItem(USER_KEY, JSON.stringify(user));

      setState({ isAuthenticated: true, isLoading: false, user });

      api.issueRememberMeToken()
        .then(({ token }) => localStorage.setItem(REMEMBER_TOKEN_STORAGE_KEY, token))
        .catch((err) => console.error('Failed to issue remember-me token:', err));
    });

    lockRef.current = lock;

    if (!readStoredSession().isAuthenticated && document.getElementById('auth0-lock-container')) {
      lock.show();
    }
  }, []);

  const showLock = useCallback(() => {
    lockRef.current?.show();
  }, []);

  const logout = useCallback(() => {
    const rememberToken = localStorage.getItem(REMEMBER_TOKEN_STORAGE_KEY);

    api.logout(rememberToken).catch((err) => console.error('Failed to revoke remember-me token:', err));

    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(TOKEN_EXPIRY_KEY);
    localStorage.removeItem(USER_KEY);
    localStorage.removeItem(REMEMBER_TOKEN_STORAGE_KEY);
    setState({ isAuthenticated: false, isLoading: false, user: null });
  }, []);

  const getAccessTokenSilently = useCallback((): Promise<string> => {
    const session = readStoredSession();

    if (session.bearerToken) {
      return Promise.resolve(session.bearerToken);
    }

    return Promise.reject(new Error('Session expired — please sign in again'));
  }, []);

  const value: AuthValue = {
    isAuthenticated: state.isAuthenticated,
    isLoading: state.isLoading,
    user: state.user,
    logout,
    getAccessTokenSilently,
    loginWithRedirect: showLock,
  };

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function AuthProvider({ children }: { children: React.ReactNode }) {
  if (import.meta.env.DEV) {
    return (
      <DevAuthProvider>
        <DevAuthBridge>{children}</DevAuthBridge>
      </DevAuthProvider>
    );
  }

  return <ProductionLockProvider>{children}</ProductionLockProvider>;
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAuth(): AuthValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be inside AuthProvider');
  return ctx;
}
```

- [ ] **Step 3: Make `App.tsx` log out on a confirmed-dead credential**

Replace the full contents of `DCF.Web/src/App.tsx`:

```tsx
import { useEffect } from 'react';
import { Outlet } from 'react-router-dom';
import { api, AuthExpiredError, setTokenGetter } from './api/client';
import { Nav } from './components/Nav';
import { useAuth } from './context/AuthContext';
import { useUser } from './context/UserContext';

export function AuthenticatedLayout({ children }: { children: React.ReactNode }) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', minHeight: '100svh' }}>
      <Nav />
      <div className="page-content" style={{ flex: 1, maxWidth: 1200, width: '100%', margin: '0 auto', padding: '24px 20px', boxSizing: 'border-box' }}>
        {children}
      </div>
    </div>
  );
}

export default function App() {
  const { getAccessTokenSilently, isAuthenticated, logout } = useAuth();
  const { setUser } = useUser();

  setTokenGetter(() => getAccessTokenSilently());

  useEffect(() => {
    if (!isAuthenticated) return;

    api.getUser().then((profile) => {
      if (profile) {
        setUser(profile);
      }
    }).catch((err) => {
      if (err instanceof AuthExpiredError) {
        logout();
        return;
      }

      console.error('Failed to load user profile:', err);
    });
  }, [isAuthenticated, setUser, logout]);

  return <Outlet />;
}
```

This is the one place the design's "invalid remember-me token → clear storage → show Lock" behavior actually gets enforced — `getAccessTokenSilently` only checks locally-stored state, it can't itself detect that a stored remember-me token has been revoked or expired server-side. `/api/auth/me` is what actually asks the server, and it already runs on every authenticated page load.

- [ ] **Step 4: Run the full frontend test suite**

```bash
cd DCF.Web && npm test
```

Expected: all tests pass, including the `authSession` suite from Task 4 and the existing `TimePicker` suite.

- [ ] **Step 5: Manual verification**

Run `npm run dev` and `dotnet run --project DCF.Api/DCF.Api.csproj` locally. Since local dev uses `DevAuthContext` (Auth0 is bypassed), full manual verification of the Lock-driven flow requires either a deployed environment with real Auth0 credentials, or temporarily pointing local dev at a real Auth0 tenant. At minimum, confirm `npm run build` (which runs `tsc -b`) succeeds with no type errors, since `AuthContext.tsx`, `App.tsx`, and `client.ts` all changed.

- [ ] **Step 6: Commit**

```bash
git add DCF.Web/src/api/client.ts DCF.Web/src/context/AuthContext.tsx DCF.Web/src/App.tsx
git commit -m "feat: fall back to remember-me token when the Auth0 access token expires"
```
