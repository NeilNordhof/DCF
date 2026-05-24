namespace DCF.Api.Services;

public interface IMqttPublisherService
{
    Task PublishAsync(string topic, object payload, bool retain = false, CancellationToken ct = default);
}
