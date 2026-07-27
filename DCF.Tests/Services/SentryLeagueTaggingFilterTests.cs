using DCF.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace DCF.Tests.Services;

public class SentryLeagueTaggingFilterTests
{
    [Fact]
    public void LeagueIdTagKey_IsLeagueId()
    {
        Assert.Equal("league_id", SentryLeagueTaggingFilter.LeagueIdTagKey);
    }

    [Fact]
    public void ResolveLeagueId_LeagueIdRouteValue_ReturnsIt()
    {
        var leagueId = Guid.NewGuid();
        var routeValues = new RouteValueDictionary { ["leagueId"] = leagueId.ToString() };

        var result = SentryLeagueTaggingFilter.ResolveLeagueId(routeValues, $"/api/leagues/{leagueId}/draft/pick");

        Assert.Equal(leagueId, result);
    }

    [Fact]
    public void ResolveLeagueId_IdRouteValueUnderApiLeagues_ReturnsIt()
    {
        var leagueId = Guid.NewGuid();
        var routeValues = new RouteValueDictionary { ["id"] = leagueId.ToString() };

        var result = SentryLeagueTaggingFilter.ResolveLeagueId(routeValues, $"/api/leagues/{leagueId}");

        Assert.Equal(leagueId, result);
    }

    [Fact]
    public void ResolveLeagueId_IdRouteValueOutsideApiLeagues_ReturnsNull()
    {
        var routeValues = new RouteValueDictionary { ["id"] = Guid.NewGuid().ToString() };

        var result = SentryLeagueTaggingFilter.ResolveLeagueId(routeValues, $"/api/admin/shows/{Guid.NewGuid()}");

        Assert.Null(result);
    }

    [Fact]
    public void ResolveLeagueId_NoMatchingRouteValue_ReturnsNull()
    {
        var routeValues = new RouteValueDictionary();

        var result = SentryLeagueTaggingFilter.ResolveLeagueId(routeValues, "/api/leagues/public");

        Assert.Null(result);
    }

    [Fact]
    public void ResolveLeagueId_MalformedGuid_ReturnsNull()
    {
        var routeValues = new RouteValueDictionary { ["leagueId"] = "not-a-guid" };

        var result = SentryLeagueTaggingFilter.ResolveLeagueId(routeValues, "/api/leagues/not-a-guid/draft/pick");

        Assert.Null(result);
    }
}