namespace AlleyWatch.Core.Options;

/// <summary>
/// Bound from the "Parking" section of appsettings.json. Read via IOptionsMonitor so
/// ParkedThresholdMinutes can change at runtime with a config file edit — no rebuild, no restart.
/// </summary>
public sealed class ParkingOptions
{
    public const string SectionName = "Parking";

    /// <summary>How long a vehicle must sit continuously in the alley zone to count as "parked".</summary>
    public double ParkedThresholdMinutes { get; set; } = 10;
}
