using Microsoft.Extensions.Hosting;

namespace DCF.Api.Services;

public class MqttPublisherService : IHostedService, IMqttPublisherService
{
    public Task PublishAsync(string topic, string payload) => Task.CompletedTask;
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
