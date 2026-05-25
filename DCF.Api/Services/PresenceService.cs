using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace DCF.Api.Services;

public class PresenceService(IServiceScopeFactory scopeFactory, ILogger<PresenceService> logger) : IPresenceService
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, bool>> _presence = new();

    public async Task HandlePresenceAsync(Guid leagueId, Guid userId, bool online)
    {
        var league = _presence.GetOrAdd(leagueId, _ => new ConcurrentDictionary<Guid, bool>());

        if (online)
        {
            league[userId] = true;
        }
        else
        {
            league.TryRemove(userId, out _);
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var draftService = scope.ServiceProvider.GetRequiredService<IDraftService>();

            await draftService.PublishStateAsync(leagueId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to publish draft state after presence change for league {LeagueId}", leagueId);
        }
    }

    public IReadOnlyCollection<Guid> GetOnline(Guid leagueId)
    {
        if (_presence.TryGetValue(leagueId, out var set))
        {
            return set.Keys.ToList();
        }

        return Array.Empty<Guid>();
    }
}
