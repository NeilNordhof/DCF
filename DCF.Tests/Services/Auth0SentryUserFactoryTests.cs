using System.Security.Claims;
using DCF.Api.Services;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace DCF.Tests.Services;

public class Auth0SentryUserFactoryTests
{
    private sealed class FakeHttpContextAccessor(HttpContext? context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = context;
    }

    private static HttpContext ContextWithClaims(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "TestAuth");

        return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }

    [Fact]
    public void Create_NameIdentifierClaimPresent_ReturnsUserWithThatId()
    {
        var context = ContextWithClaims(new Claim(ClaimTypes.NameIdentifier, "auth0|123"));
        var factory = new Auth0SentryUserFactory(new FakeHttpContextAccessor(context));

        var user = factory.Create();

        Assert.Equal("auth0|123", user?.Id);
    }

    [Fact]
    public void Create_OnlyRawSubClaimPresent_FallsBackToSubClaim()
    {
        var context = ContextWithClaims(new Claim("sub", "auth0|456"));
        var factory = new Auth0SentryUserFactory(new FakeHttpContextAccessor(context));

        var user = factory.Create();

        Assert.Equal("auth0|456", user?.Id);
    }

    [Fact]
    public void Create_NoIdentifyingClaim_ReturnsNull()
    {
        var context = ContextWithClaims();
        var factory = new Auth0SentryUserFactory(new FakeHttpContextAccessor(context));

        var user = factory.Create();

        Assert.Null(user);
    }

    [Fact]
    public void Create_NoHttpContext_ReturnsNull()
    {
        var factory = new Auth0SentryUserFactory(new FakeHttpContextAccessor(null));

        var user = factory.Create();

        Assert.Null(user);
    }
}