using AlleyWatch.Core.Options;
using Microsoft.Extensions.Options;

namespace AlleyWatch.Worker.Reporting;

/// <summary>
/// Keeps a small, time-spread set of frames per actively-tracked object rather than archiving every
/// snapshot Frigate publishes — this is what gets sent to Claude when the object departs.
/// </summary>
public sealed class FrameSampler
{
    private static readonly TimeSpan MinSampleInterval = TimeSpan.FromMinutes(2);

    private readonly IOptionsMonitor<ClaudeOptions> _options;
    private readonly Dictionary<string, List<(DateTimeOffset TimestampUtc, byte[] Jpeg)>> _framesByObjectId = new();
    private readonly Dictionary<string, DateTimeOffset> _lastSampledAtByObjectId = new();

    public FrameSampler(IOptionsMonitor<ClaudeOptions> options)
    {
        _options = options;
    }

    public void AddFrame(string objectId, byte[] jpegBytes, DateTimeOffset timestampUtc)
    {
        if (!_framesByObjectId.TryGetValue(objectId, out var frames))
        {
            frames = [];
            _framesByObjectId[objectId] = frames;
        }

        // Throttle mid-samples so a fast snapshot cadence doesn't blow the cap immediately — but always
        // keep the *most recent* frame fresh by overwriting the last slot when we're inside the interval.
        if (frames.Count > 0
            && _lastSampledAtByObjectId.TryGetValue(objectId, out var lastSampledAt)
            && timestampUtc - lastSampledAt < MinSampleInterval)
        {
            frames[^1] = (timestampUtc, jpegBytes);
            return;
        }

        frames.Add((timestampUtc, jpegBytes));
        _lastSampledAtByObjectId[objectId] = timestampUtc;

        var max = _options.CurrentValue.MaxSampledFrames;
        if (frames.Count > max)
        {
            // Drop the oldest *mid* sample — always keep index 0 (first-in-zone) and the last (most recent).
            frames.RemoveAt(1);
        }
    }

    /// <summary>Returns and clears the buffered frames for an object (called once its Departed event fires).</summary>
    public IReadOnlyList<byte[]> TakeFrames(string objectId)
    {
        if (_framesByObjectId.Remove(objectId, out var frames))
        {
            _lastSampledAtByObjectId.Remove(objectId);
            return frames.Select(f => f.Jpeg).ToList();
        }

        return [];
    }

    /// <summary>Discards any buffered frames for an object without returning them (e.g. it never crossed
    /// the parked threshold, so no report is warranted).</summary>
    public void Discard(string objectId)
    {
        _framesByObjectId.Remove(objectId);
        _lastSampledAtByObjectId.Remove(objectId);
    }
}
