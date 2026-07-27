using System.Security.Claims;

namespace DCF.Api.Services;

public class Auth0SentryUserFactory(IHttpContextAccessor httpContextAccessor) : ISentryUserFactory
{
    // Confirmed empirically: replacing the default ISentryUserFactory does not disable
    // SendDefaultPii's separate IP-address capture, so IpAddress is intentionally left unset here.
    public SentryUser? Create()
    {
        var user = httpContextAccessor.HttpContext?.User;
        var sub = user?.FindFirstValue(ClaimTypes.NameIdentifier) ?? user?.FindFirstValue("sub");

        if (sub is null)
        {
            return null;
        }

        return new SentryUser { Id = sub };
    }
}
