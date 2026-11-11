using System.Text.Json.Serialization;

namespace AlleyWatch.Worker.Frigate;

/// <summary>
/// Deserialized `frigate/events` MQTT payload. Field names/shape per Frigate's documented MQTT
/// integration: each message carries "before"/"after" snapshots of one tracked object's state.
/// </summary>
public sealed class FrigateEventMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("before")]
    public FrigateObjectState? Before { get; set; }

    [JsonPropertyName("after")]
    public FrigateObjectState? After { get; set; }
}

public sealed class FrigateObjectState
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("camera")]
    public string Camera { get; set; } = "";

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("current_zones")]
    public List<string> CurrentZones { get; set; } = [];

    [JsonPropertyName("entered_zones")]
    public List<string> EnteredZones { get; set; } = [];

    [JsonPropertyName("start_time")]
    public double StartTime { get; set; }

    [JsonPropertyName("end_time")]
    public double? EndTime { get; set; }

    [JsonPropertyName("stationary")]
    public bool Stationary { get; set; }

    [JsonPropertyName("score")]
    public double Score { get; set; }

    /// <summary>Converts the Unix-epoch-seconds <see cref="StartTime"/> to a UTC timestamp.</summary>
    public DateTimeOffset StartTimeUtc => DateTimeOffset.FromUnixTimeMilliseconds((long)(StartTime * 1000));

    /// <summary>Best-effort "when was this state observed" timestamp for feeding ParkingStateTracker —
    /// end_time when the track has ended, otherwise start_time (Frigate's events payload does not carry
    /// a separate per-update timestamp; the message-arrival time, tracked by the listener, is the more
    /// precise signal for ongoing updates — see FrigateMqttListener).</summary>
    public DateTimeOffset? EndTimeUtc => EndTime is { } et ? DateTimeOffset.FromUnixTimeMilliseconds((long)(et * 1000)) : null;
}
