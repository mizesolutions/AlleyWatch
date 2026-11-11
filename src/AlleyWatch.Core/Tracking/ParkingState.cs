namespace AlleyWatch.Core.Tracking;

/// <summary>
/// State of a single tracked Frigate object with respect to the alley zone.
/// </summary>
public enum ParkingState
{
    /// <summary>Not currently in the alley zone.</summary>
    None,

    /// <summary>In the alley zone, but not yet stationary long enough to count as parked.</summary>
    InZoneTransient,

    /// <summary>In the alley zone for at least the configured threshold.</summary>
    Parked,
}
