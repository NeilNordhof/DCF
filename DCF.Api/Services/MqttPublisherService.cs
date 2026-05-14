using Microsoft.Extensions.Hosting;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using System.Text.Json;

namespace DCF.Api.Services;

public class MqttPublisherService : IMqttPublisherService, IHostedService
{
    private readonly IMqttClient _client;
    private readonly string _host;
    private readonly int _port;
    private readonly ILogger<MqttPublisherService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public MqttPublisherService(IConfiguration config, ILogger<MqttPublisherService> logger)
    {
        _host = config["Mqtt:Host"] ?? "localhost";
        _port = config.GetValue<int>("Mqtt:Port", 1883);
        _logger = logger;
        _client = new MqttFactory().CreateMqttClient();
    }

    public async Task StartAsync(CancellationToken ct)
    {
        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(_host, _port)
            .WithCleanStart()
            .Build();

        try
        {
            await _client.ConnectAsync(options, ct);

            _logger.LogInformation("MQTT connected to {Host}:{Port}", _host, _port);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MQTT connection failed — publishing will be silently skipped");
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (_client.IsConnected)
        {
            await _client.DisconnectAsync(cancellationToken: ct);
        }
    }

    public async Task PublishAsync(string topic, object payload, CancellationToken ct = default)
    {
        if (!_client.IsConnected)
        {
            return;
        }

        await _lock.WaitAsync(ct);

        try
        {
            if (!_client.IsConnected)
            {
                return;
            }

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }))
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();

            await _client.PublishAsync(message, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MQTT publish failed for topic {Topic}", topic);
        }
        finally
        {
            _lock.Release();
        }
    }
}
