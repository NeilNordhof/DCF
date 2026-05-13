namespace DCF.Api.Services;

public interface IMqttPublisherService
{
    Task PublishAsync(string topic, string payload);
}
