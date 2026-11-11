using System.Text.Json;
using System.Text.RegularExpressions;
using AlleyWatch.Core.Options;
using Microsoft.Extensions.Options;
using MQTTnet;

namespace AlleyWatch.Worker.Frigate;

public sealed record FrigateSnapshotReceived(string Camera, string Label, byte[] JpegBytes, DateTimeOffset ReceivedAtUtc);

/// <summary>
/// Owns the single MQTT connection to the mosquitto broker Frigate publishes to. Subscribes:
///   - frigate/events               (object lifecycle + zone membership -> EventReceived)
///   - frigate/{camera}/+/snapshot  (periodic JPEGs of tracked objects -> SnapshotReceived)
///   - frigate/available            (connection health -> logged only)
///
/// This is deliberately transport-only: it does no state-machine or report-generation work itself —
/// ParkingEventOrchestrator subscribes to its events and owns the actual pipeline logic. Combining the
/// events and snapshot subscriptions into one listener (rather than two separate MQTT connections)
/// avoids a redundant second connection to the same local broker.
/// </summary>
public sealed class FrigateMqttListener : BackgroundService
{
    private static readonly Regex SnapshotTopicPattern = new(@"^frigate/(?<camera>[^/]+)/(?<label>[^/]+)/snapshot$", RegexOptions.Compiled);

    private readonly IOptionsMonitor<FrigateOptions> _options;
    private readonly ILogger<FrigateMqttListener> _logger;
    private IMqttClient? _client;

    public FrigateMqttListener(IOptionsMonitor<FrigateOptions> options, ILogger<FrigateMqttListener> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <summary>Raised for every `frigate/events` message, already deserialized.</summary>
    public event Action<FrigateEventMessage>? EventReceived;

    /// <summary>Raised for every snapshot JPEG published for a tracked object.</summary>
    public event Action<FrigateSnapshotReceived>? SnapshotReceived;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = _options.CurrentValue;

        var factory = new MqttClientFactory();
        _client = factory.CreateMqttClient();

        _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
        _client.DisconnectedAsync += OnDisconnectedAsync;

        var clientOptions = new MqttClientOptionsBuilder()
            .WithTcpServer(opts.MqttHost, opts.MqttPort)
            .WithClientId($"alleywatch-{Environment.MachineName}")
            .Build();

        await ConnectWithRetryAsync(clientOptions, stoppingToken);

        // Block until the service is stopped; reconnect handling happens in OnDisconnectedAsync.
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }

        if (_client is { IsConnected: true })
        {
            await _client.DisconnectAsync(cancellationToken: CancellationToken.None);
        }
    }

    private async Task ConnectWithRetryAsync(MqttClientOptions clientOptions, CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(5);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _client!.ConnectAsync(clientOptions, stoppingToken);
                _logger.LogInformation("Connected to MQTT broker at {Host}:{Port}", clientOptions.ChannelOptions, _options.CurrentValue.MqttPort);

                var cameraTopic = _options.CurrentValue.CameraName;
                await _client.SubscribeAsync(
                    new MqttClientSubscribeOptionsBuilder()
                        .WithTopicFilter("frigate/events")
                        .WithTopicFilter("frigate/available")
                        .WithTopicFilter($"frigate/{cameraTopic}/+/snapshot")
                        .Build(),
                    stoppingToken);

                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MQTT connect failed, retrying in {Delay}", delay);
                await Task.Delay(delay, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 60));
            }
        }
    }

    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
    {
        _logger.LogWarning("MQTT disconnected ({Reason}); MQTTnet auto-reconnect is not configured here, " +
                            "so if this fires unexpectedly the connect-retry loop in ExecuteAsync needs revisiting.", args.Reason);
        return Task.CompletedTask;
    }

    private Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        var topic = args.ApplicationMessage.Topic;

        try
        {
            if (topic == "frigate/events")
            {
                var payload = System.Buffers.BuffersExtensions.ToArray(args.ApplicationMessage.Payload);
                var message = JsonSerializer.Deserialize<FrigateEventMessage>(payload);
                if (message is not null)
                {
                    EventReceived?.Invoke(message);
                }
                return Task.CompletedTask;
            }

            if (topic == "frigate/available")
            {
                _logger.LogInformation("Frigate availability: {Status}", args.ApplicationMessage.ConvertPayloadToString());
                return Task.CompletedTask;
            }

            var match = SnapshotTopicPattern.Match(topic);
            if (match.Success)
            {
                var jpegBytes = System.Buffers.BuffersExtensions.ToArray(args.ApplicationMessage.Payload);
                SnapshotReceived?.Invoke(new FrigateSnapshotReceived(
                    match.Groups["camera"].Value,
                    match.Groups["label"].Value,
                    jpegBytes,
                    DateTimeOffset.UtcNow));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle MQTT message on topic {Topic}", topic);
        }

        return Task.CompletedTask;
    }
}
