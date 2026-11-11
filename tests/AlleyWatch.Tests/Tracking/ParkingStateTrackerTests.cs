using AlleyWatch.Core.Options;
using AlleyWatch.Core.Tracking;
using Xunit;

namespace AlleyWatch.Tests.Tracking;

public class ParkingStateTrackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static ParkingStateTracker CreateTracker(double thresholdMinutes = 10) =>
        new("obj-1", new TestParkingOptionsMonitor(new ParkingOptions { ParkedThresholdMinutes = thresholdMinutes }));

    [Fact]
    public void Observe_StaysUnderThreshold_EmitsNoEvent()
    {
        var tracker = CreateTracker(thresholdMinutes: 10);

        var e1 = tracker.Observe(inAlleyZone: true, T0);
        var e2 = tracker.Observe(inAlleyZone: true, T0.AddMinutes(5));

        Assert.Null(e1);
        Assert.Null(e2);
        Assert.Equal(ParkingState.InZoneTransient, tracker.CurrentState);
    }

    [Fact]
    public void Observe_CrossesThreshold_EmitsParkedEventWithZoneEntryAsStart()
    {
        var tracker = CreateTracker(thresholdMinutes: 10);

        var e1 = tracker.Observe(inAlleyZone: true, T0);
        var e2 = tracker.Observe(inAlleyZone: true, T0.AddMinutes(9));
        var e3 = tracker.Observe(inAlleyZone: true, T0.AddMinutes(10));

        Assert.Null(e1);
        Assert.Null(e2);
        Assert.NotNull(e3);
        Assert.Equal(ParkingEventKind.Parked, e3!.Kind);
        Assert.Equal(T0, e3.ParkStartUtc);
        Assert.Null(e3.ParkEndUtc);
        Assert.Equal(ParkingState.Parked, tracker.CurrentState);
    }

    [Fact]
    public void Observe_LeavesZoneBeforeThreshold_EmitsNoEventAndResets()
    {
        var tracker = CreateTracker(thresholdMinutes: 10);

        tracker.Observe(inAlleyZone: true, T0);
        var departed = tracker.Observe(inAlleyZone: false, T0.AddMinutes(3));

        Assert.Null(departed); // never reached Parked, so leaving the zone is not a reportable event
        Assert.Equal(ParkingState.None, tracker.CurrentState);

        // Re-entering starts a fresh dwell clock, not a resumed one.
        var reEntered = tracker.Observe(inAlleyZone: true, T0.AddMinutes(3));
        var stillUnderThreshold = tracker.Observe(inAlleyZone: true, T0.AddMinutes(9));
        Assert.Null(reEntered);
        Assert.Null(stillUnderThreshold);
    }

    [Fact]
    public void Observe_LeavesZoneAfterParked_EmitsDepartedEventWithLastSeenAsEnd()
    {
        var tracker = CreateTracker(thresholdMinutes: 10);

        tracker.Observe(inAlleyZone: true, T0);
        tracker.Observe(inAlleyZone: true, T0.AddMinutes(10)); // -> Parked
        tracker.Observe(inAlleyZone: true, T0.AddMinutes(15)); // last seen still in zone
        var departed = tracker.Observe(inAlleyZone: false, T0.AddMinutes(16));

        Assert.NotNull(departed);
        Assert.Equal(ParkingEventKind.Departed, departed!.Kind);
        Assert.Equal(T0, departed.ParkStartUtc);
        Assert.Equal(T0.AddMinutes(15), departed.ParkEndUtc); // end = last confirmed in-zone sighting, not the exit message's own time
        Assert.Equal(ParkingState.None, tracker.CurrentState);
    }

    [Fact]
    public void HandleTrackLost_WhileParked_SynthesizesDepartedEvent()
    {
        var tracker = CreateTracker(thresholdMinutes: 10);

        tracker.Observe(inAlleyZone: true, T0);
        tracker.Observe(inAlleyZone: true, T0.AddMinutes(10)); // -> Parked

        var departed = tracker.HandleTrackLost(T0.AddMinutes(12));

        Assert.NotNull(departed);
        Assert.Equal(ParkingEventKind.Departed, departed!.Kind);
        Assert.Equal(T0, departed.ParkStartUtc);
        Assert.Equal(T0.AddMinutes(10), departed.ParkEndUtc); // last *confirmed* in-zone sighting was at +10, not the loss time
    }

    [Fact]
    public void HandleTrackLost_WhileNotParked_EmitsNoEvent()
    {
        var tracker = CreateTracker(thresholdMinutes: 10);

        tracker.Observe(inAlleyZone: true, T0); // InZoneTransient only
        var result = tracker.HandleTrackLost(T0.AddMinutes(1));

        Assert.Null(result);
    }

    [Fact]
    public void Observe_RespectsShortenedThresholdFromConfig()
    {
        // Sanity check for the config-binding path used in the shortened-threshold live dry run
        // (verification plan step 5): a 1-minute threshold should fire well before the 10-minute default would.
        var tracker = CreateTracker(thresholdMinutes: 1);

        var entered = tracker.Observe(inAlleyZone: true, T0);
        var underOldDefault = tracker.Observe(inAlleyZone: true, T0.AddSeconds(30));
        var overShortThreshold = tracker.Observe(inAlleyZone: true, T0.AddMinutes(1));

        Assert.Null(entered);
        Assert.Null(underOldDefault);
        Assert.NotNull(overShortThreshold);
        Assert.Equal(ParkingEventKind.Parked, overShortThreshold!.Kind);
    }
}
