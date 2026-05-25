namespace DCF.Api.Services;

public interface IMqttService
{
    Task PublishAsync(string topic, object payload, bool retain = false, CancellationToken ct = default);
}
