namespace DCF.Api.Services;

public interface IMqttPublisherService
{
    Task PublishAsync(string topic, object payload, CancellationToken ct = default);
}
