using DCF.Api.Services;
using DCF.Data.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DCF.Tests.Services;

public class PresenceServiceTests
{
    private sealed class SpyScopeFactory : IServiceScopeFactory
    {
        public readonly SpyDraftService DraftService;

        public SpyScopeFactory(bool throwOnPublish = false)
        {
            DraftService = new SpyDraftService(throwOnPublish);
        }

        public IServiceScope CreateScope()
        {
            return new Scope(DraftService);
        }

        private sealed class Scope(IDraftService svc) : IServiceScope
        {
            public IServiceProvider ServiceProvider { get; } = new Provider(svc);

            public void Dispose() { }
        }

        private sealed class Provider(IDraftService svc) : IServiceProvider
        {
            public object? GetService(Type t)
            {
                return t == typeof(IDraftService) ? svc : null;
            }
        }
    }

    private sealed class SpyDraftService : IDraftService
    {
        private readonly bool _throwOnPublish;
        public List<Guid> PublishedStateFor { get; } = [];

        public SpyDraftService(bool throwOnPublish = false)
        {
            _throwOnPublish = throwOnPublish;
        }

        public Task PublishStateAsync(Guid leagueId)
        {
            if (_throwOnPublish)
            {
                throw new InvalidOperationException("publish failed");
            }

            PublishedStateFor.Add(leagueId);

            return Task.CompletedTask;
        }

        public Task OpenDraftAsync(Guid leagueId) => throw new NotImplementedException();
        public Task OpenDraftAsync(Guid leagueId, string userSub) => throw new NotImplementedException();
        public Task StartDraftAsync(Guid leagueId) => throw new NotImplementedException();
        public Task StartDraftAsync(Guid leagueId, string userSub) => throw new NotImplementedException();
        public Task<(Guid Id, int PickNumber)> SubmitPickAsync(Guid leagueId, string userSub, Guid corpsId, ComputedCaption caption) => throw new NotImplementedException();
        public Task SkipCurrentPickAsync(Guid leagueId, string userSub) => throw new NotImplementedException();
    }

    private static PresenceService Create(SpyScopeFactory? factory = null)
    {
        return new PresenceService(factory ?? new SpyScopeFactory(), NullLogger<PresenceService>.Instance);
    }

    [Fact]
    public async Task HandlePresenceAsync_Online_AddsToSet()
    {
        var svc = Create();
        var leagueId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await svc.HandlePresenceAsync(leagueId, userId, online: true);

        Assert.Contains(userId, svc.GetOnline(leagueId));
    }

    [Fact]
    public async Task HandlePresenceAsync_Offline_RemovesFromSet()
    {
        var svc = Create();
        var leagueId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await svc.HandlePresenceAsync(leagueId, userId, online: true);
        await svc.HandlePresenceAsync(leagueId, userId, online: false);

        Assert.DoesNotContain(userId, svc.GetOnline(leagueId));
    }

    [Fact]
    public async Task HandlePresenceAsync_Offline_UnknownUser_DoesNotThrow()
    {
        var svc = Create();

        var ex = await Record.ExceptionAsync(
            () => svc.HandlePresenceAsync(Guid.NewGuid(), Guid.NewGuid(), online: false));

        Assert.Null(ex);
    }

    [Fact]
    public void GetOnline_UnknownLeague_ReturnsEmpty()
    {
        var svc = Create();

        Assert.Empty(svc.GetOnline(Guid.NewGuid()));
    }

    [Fact]
    public async Task HandlePresenceAsync_TriggersDraftStatePublish()
    {
        var factory = new SpyScopeFactory();
        var svc = Create(factory);
        var leagueId = Guid.NewGuid();

        await svc.HandlePresenceAsync(leagueId, Guid.NewGuid(), online: true);

        Assert.Single(factory.DraftService.PublishedStateFor);
        Assert.Equal(leagueId, factory.DraftService.PublishedStateFor[0]);
    }

    [Fact]
    public async Task HandlePresenceAsync_PublishFails_DoesNotThrowAndPresenceIsUpdated()
    {
        var throwingFactory = new SpyScopeFactory(throwOnPublish: true);
        var svc = Create(throwingFactory);
        var leagueId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var ex = await Record.ExceptionAsync(
            () => svc.HandlePresenceAsync(leagueId, userId, online: true));

        Assert.Null(ex);
        Assert.Contains(userId, svc.GetOnline(leagueId));
    }
}
