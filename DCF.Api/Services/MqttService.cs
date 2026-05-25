using Microsoft.Extensions.Hosting;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using System.Text;
using System.Text.Json;

namespace DCF.Api.Services;

public class MqttService : IMqttService, IHostedService
{
    private readonly IMqttClient _client;
    private readonly string _host;
    private readonly int _port;
    private readonly ILogger<MqttService> _logger;
    private readonly IPresenceService _presenceService;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public MqttService(IConfiguration config, ILogger<MqttService> logger, IPresenceService presenceService)
    {
        _host = config["Mqtt:Host"] ?? "localhost";
        _port = config.GetValue<int>("Mqtt:Port", 1883);
        _logger = logger;
        _presenceService = presenceService;
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
            _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;

            await _client.ConnectAsync(options, ct);

            await _client.SubscribeAsync(
                new MqttClientSubscribeOptionsBuilder()
                    .WithTopicFilter(f => f
                        .WithTopic("dcf/leagues/+/draft/presence")
                        .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
                    .Build(),
                ct);

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

    public async Task PublishAsync(string topic, object payload, bool retain = false, CancellationToken ct = default)
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
                .WithRetainFlag(retain)
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

    private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        try
        {
            var parts = e.ApplicationMessage.Topic.Split('/');

            // Topic format: dcf/leagues/{leagueId}/draft/presence

            if (parts.Length != 5 || !Guid.TryParse(parts[2], out var leagueId))
            {
                return;
            }

            var json = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);

            var payload = JsonSerializer.Deserialize<PresencePayload>(
                json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (payload is null)
            {
                return;
            }

            bool online = payload.Status.Equals("online", StringComparison.OrdinalIgnoreCase);

            await _presenceService.HandlePresenceAsync(leagueId, payload.UserId, online);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process presence message on topic {Topic}", e.ApplicationMessage.Topic);
        }
    }

    private record PresencePayload(Guid UserId, string Status);
}
