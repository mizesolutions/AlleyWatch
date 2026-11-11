using AlleyWatch.Core.Options;
using Microsoft.Extensions.Options;

namespace AlleyWatch.Core.Tracking;

/// <summary>
/// Dwell-time state machine for a single Frigate tracked-object id. Fed by zone-membership
/// booleans derived from `frigate/events` MQTT payloads (not centroid/bounding-box math — Frigate
/// already supplies clean per-object zone-entry/exit signals).
///
/// One instance per Frigate object id; the caller (FrigateMqttListener) owns the id-&gt;tracker map
/// and is responsible for removing entries once a Departed event has been emitted.
///
/// No ambient clock is needed here — every timestamp the state machine reasons about comes from the
/// Frigate event itself (<paramref name="timestampUtc"/> on <see cref="Observe"/>), which is what makes
/// this trivially unit-testable: tests just feed synthetic timestamps, no fake clock required.
/// </summary>
public sealed class ParkingStateTracker
{
    private readonly string _objectId;
    private readonly IOptionsMonitor<ParkingOptions> _options;

    private ParkingState _state = ParkingState.None;
    private DateTimeOffset? _zoneEntryTimeUtc;
    private DateTimeOffset? _lastSeenInZoneUtc;

    public ParkingStateTracker(string objectId, IOptionsMonitor<ParkingOptions> options)
    {
        _objectId = objectId;
        _options = options;
    }

    public ParkingState CurrentState => _state;

    /// <summary>
    /// Called once per Frigate event for this object. <paramref name="inAlleyZone"/> reflects whether
    /// the alley zone is present in that event's `current_zones`. Returns a <see cref="ParkingEvent"/>
    /// if this observation caused a state transition, otherwise null.
    /// </summary>
    public ParkingEvent? Observe(bool inAlleyZone, DateTimeOffset timestampUtc)
    {
        if (inAlleyZone)
        {
            _zoneEntryTimeUtc ??= timestampUtc;
            _lastSeenInZoneUtc = timestampUtc;

            if (_state != ParkingState.Parked)
            {
                _state = ParkingState.InZoneTransient;

                var threshold = TimeSpan.FromMinutes(_options.CurrentValue.ParkedThresholdMinutes);
                if (timestampUtc - _zoneEntryTimeUtc.Value >= threshold)
                {
                    _state = ParkingState.Parked;
                    return new ParkingEvent(ParkingEventKind.Parked, _objectId, _zoneEntryTimeUtc.Value, null);
                }
            }

            return null;
        }

        // Left the zone (or never entered it this call).
        return HandleZoneExitOrTrackLost(timestampUtc);
    }

    /// <summary>
    /// Called when Frigate reports the object's track has ended (left the camera view entirely,
    /// occlusion, etc.) rather than a normal zone-exit event. Synthesizes a Departed event if the
    /// object was Parked, so a lost track never leaves a "still parked" state hanging forever.
    /// </summary>
    public ParkingEvent? HandleTrackLost(DateTimeOffset lastSeenUtc) => HandleZoneExitOrTrackLost(lastSeenUtc);

    private ParkingEvent? HandleZoneExitOrTrackLost(DateTimeOffset timestampUtc)
    {
        ParkingEvent? result = null;

        if (_state == ParkingState.Parked)
        {
            var parkEnd = _lastSeenInZoneUtc ?? timestampUtc;
            result = new ParkingEvent(ParkingEventKind.Departed, _objectId, _zoneEntryTimeUtc!.Value, parkEnd);
        }

        Reset();
        return result;
    }

    private void Reset()
    {
        _state = ParkingState.None;
        _zoneEntryTimeUtc = null;
        _lastSeenInZoneUtc = null;
    }
}
