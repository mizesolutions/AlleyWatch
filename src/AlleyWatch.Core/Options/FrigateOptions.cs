namespace AlleyWatch.Core.Options;

/// <summary>Bound from the "Frigate" section of appsettings.json.</summary>
public sealed class FrigateOptions
{
    public const string SectionName = "Frigate";

    public string MqttHost { get; set; } = "localhost";
    public int MqttPort { get; set; } = 1883;

    /// <summary>Frigate's web UI / REST API base URL.</summary>
    public string ApiBaseUrl { get; set; } = "http://localhost:5000";

    public string CameraName { get; set; } = "alley";
    public string ZoneName { get; set; } = "alley_zone";
}
