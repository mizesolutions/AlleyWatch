namespace AlleyWatch.Core.Tracking;

public enum ParkingEventKind
{
    /// <summary>The object has been continuously in the alley zone for &gt;= the configured threshold.</summary>
    Parked,

    /// <summary>A previously-parked object has left the alley zone, or its track ended.</summary>
    Departed,
}

/// <summary>
/// Emitted by <see cref="ParkingStateTracker"/> on a state transition.
/// For a <see cref="ParkingEventKind.Parked"/> event, <see cref="ParkEndUtc"/> is null.
/// </summary>
public sealed record ParkingEvent(
    ParkingEventKind Kind,
    string ObjectId,
    DateTimeOffset ParkStartUtc,
    DateTimeOffset? ParkEndUtc);
