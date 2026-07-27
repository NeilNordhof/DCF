using Microsoft.AspNetCore.Mvc.Filters;

namespace DCF.Api.Services;

public class SentryLeagueTaggingFilter : IActionFilter
{
    public const string LeagueIdTagKey = "league_id";

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }

    public void OnActionExecuting(ActionExecutingContext context)
    {
        var leagueId = ResolveLeagueId(context.RouteData.Values, context.HttpContext.Request.Path);

        if (leagueId is not null)
        {
            SentrySdk.ConfigureScope(scope => scope.SetTag(LeagueIdTagKey, leagueId.Value.ToString()));
        }
    }

    public static Guid? ResolveLeagueId(RouteValueDictionary routeValues, PathString path)
    {
        object? raw = null;

        if (routeValues.TryGetValue("leagueId", out var leagueIdValue))
        {
            raw = leagueIdValue;
        }
        else if (path.StartsWithSegments("/api/leagues") && routeValues.TryGetValue("id", out var idValue))
        {
            raw = idValue;
        }

        if (raw is string s && Guid.TryParse(s, out var id))
        {
            return id;
        }

        return null;
    }
}
