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
