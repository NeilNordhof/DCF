using System.Security.Claims;

namespace DCF.Api.Services
{
    public class Auth0SentryUserFactory(IHttpContextAccessor httpContextAccessor) : ISentryUserFactory
    {
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
}
