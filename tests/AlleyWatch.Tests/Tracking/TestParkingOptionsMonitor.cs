using AlleyWatch.Core.Options;
using Microsoft.Extensions.Options;

namespace AlleyWatch.Tests.Tracking;

/// <summary>Minimal IOptionsMonitor test double — mutable CurrentValue, no DI container needed.</summary>
internal sealed class TestParkingOptionsMonitor(ParkingOptions value) : IOptionsMonitor<ParkingOptions>
{
    public ParkingOptions CurrentValue { get; set; } = value;

    public ParkingOptions Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<ParkingOptions, string> listener) => null;
}
