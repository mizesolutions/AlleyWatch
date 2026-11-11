using AlleyWatch.Core.Options;
using AlleyWatch.Core.Tracking;
using AlleyWatch.Worker.Frigate;
using AlleyWatch.Worker.Notification;
using AlleyWatch.Worker.Reporting;
using Microsoft.Extensions.Options;

namespace AlleyWatch.Worker.Orchestration;

/// <summary>
/// Wires the whole pipeline: FrigateMqttListener zone events -> per-object ParkingStateTracker ->
/// frame sampling -> on Departed: ClaudeReportClient -> ReportStore -> GmailDraftClient. Each downstream
/// stage's failure is caught and logged, not allowed to take down the listener for the next vehicle.
///
/// Frame sourcing note: Frigate's MQTT snapshot topic (frigate/&lt;camera&gt;/&lt;label&gt;/snapshot) is
/// keyed by label, not by specific tracked-object id — with more than one vehicle of the same type in
/// frame at once there is no reliable way to attribute a snapshot to the correct object from that topic
/// alone. To keep report frames correctly attributed to the specific vehicle being reported on, this
/// orchestrator instead pulls each object's own snapshot directly by id via FrigateApiClient
/// (Frigate's tracked-object id and its corresponding event id are the same identifier), throttled to
/// avoid hammering the API on every MQTT event message.
/// </summary>
public sealed class ParkingEventOrchestrator : BackgroundService
{
    private static readonly TimeSpan MinSnapshotFetchInterval = TimeSpan.FromSeconds(30);
    private static readonly string[] VehicleLabels = ["car", "motorcycle", "bus", "truck"];

    private readonly FrigateMqttListener _mqttListener;
    private readonly FrigateApiClient _frigateApiClient;
    private readonly IOptionsMonitor<ParkingOptions> _parkingOptions;
    private readonly IOptionsMonitor<FrigateOptions> _frigateOptions;
    private readonly FrameSampler _frameSampler;
    private readonly ClaudeReportClient _claudeReportClient;
    private readonly ReportStore _reportStore;
    private readonly GmailDraftClient _gmailDraftClient;
    private readonly ILogger<ParkingEventOrchestrator> _logger;

    private readonly Dictionary<string, ParkingStateTracker> _trackersByObjectId = new();
    private readonly Dictionary<string, DateTimeOffset> _lastSnapshotFetchAttempt = new();

    public ParkingEventOrchestrator(
        FrigateMqttListener mqttListener,
        FrigateApiClient frigateApiClient,
        IOptionsMonitor<ParkingOptions> parkingOptions,
        IOptionsMonitor<FrigateOptions> frigateOptions,
        FrameSampler frameSampler,
        ClaudeReportClient claudeReportClient,
        ReportStore reportStore,
        GmailDraftClient gmailDraftClient,
        ILogger<ParkingEventOrchestrator> logger)
    {
        _mqttListener = mqttListener;
        _frigateApiClient = frigateApiClient;
        _parkingOptions = parkingOptions;
        _frigateOptions = frigateOptions;
        _frameSampler = frameSampler;
        _claudeReportClient = claudeReportClient;
        _reportStore = reportStore;
        _gmailDraftClient = gmailDraftClient;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _mqttListener.EventReceived += OnFrigateEvent;

        try
        {
            // Purely event-driven from here — FrigateMqttListener's own ExecuteAsync is what actually
            // blocks the host; this just needs to stay registered until shutdown.
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
        finally
        {
            _mqttListener.EventReceived -= OnFrigateEvent;
        }
    }

    private void OnFrigateEvent(FrigateEventMessage message)
    {
        var state = message.After ?? message.Before;
        if (state is null
            || !string.Equals(state.Camera, _frigateOptions.CurrentValue.CameraName, StringComparison.OrdinalIgnoreCase)
            || !VehicleLabels.Contains(state.Label, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        var tracker = GetOrCreateTracker(state.Id);

        ParkingEvent? transition;
        if (message.Type == "end")
        {
            transition = tracker.HandleTrackLost(state.EndTimeUtc ?? DateTimeOffset.UtcNow);
        }
        else
        {
            var inZone = state.CurrentZones.Contains(_frigateOptions.CurrentValue.ZoneName, StringComparer.OrdinalIgnoreCase);
            transition = tracker.Observe(inZone, DateTimeOffset.UtcNow);

            if (inZone)
            {
                MaybeFetchSnapshot(state.Id);
            }
        }

        if (transition is null)
        {
            return;
        }

        if (transition.Kind == ParkingEventKind.Parked)
        {
            _logger.LogInformation("Vehicle {ObjectId} has been parked in the alley since {Start:u}.", state.Id, transition.ParkStartUtc);
            return;
        }

        // Departed
        _trackersByObjectId.Remove(state.Id);
        _lastSnapshotFetchAttempt.Remove(state.Id);
        _ = HandleDepartedAsync(transition);
    }

    private void MaybeFetchSnapshot(string objectId)
    {
        var now = DateTimeOffset.UtcNow;
        if (_lastSnapshotFetchAttempt.TryGetValue(objectId, out var last) && now - last < MinSnapshotFetchInterval)
        {
            return;
        }
        _lastSnapshotFetchAttempt[objectId] = now;

        _ = FetchAndSampleAsync(objectId, now);
    }

    private async Task FetchAndSampleAsync(string objectId, DateTimeOffset timestampUtc)
    {
        var jpeg = await _frigateApiClient.TryGetEventSnapshotAsync(objectId, CancellationToken.None);
        if (jpeg is not null)
        {
            _frameSampler.AddFrame(objectId, jpeg, timestampUtc);
        }
    }

    private ParkingStateTracker GetOrCreateTracker(string objectId)
    {
        if (!_trackersByObjectId.TryGetValue(objectId, out var tracker))
        {
            tracker = new ParkingStateTracker(objectId, _parkingOptions);
            _trackersByObjectId[objectId] = tracker;
        }
        return tracker;
    }

    private async Task HandleDepartedAsync(ParkingEvent evt)
    {
        try
        {
            var frames = _frameSampler.TakeFrames(evt.ObjectId);

            var report = await _claudeReportClient.GenerateReportAsync(
                frames, evt.ParkStartUtc, evt.ParkEndUtc!.Value, CancellationToken.None);
            if (report is null)
            {
                return; // already logged by ClaudeReportClient (refusal, empty frames, or parse failure)
            }

            await _reportStore.InsertAsync(evt.ParkStartUtc, evt.ParkEndUtc.Value, report, CancellationToken.None);
            await _gmailDraftClient.CreateDraftAsync(evt.ParkStartUtc, evt.ParkEndUtc.Value, report, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // A failure in any downstream stage (Claude, SQLite, Gmail) must not take down the pipeline
            // for the next vehicle — log and move on.
            _logger.LogError(ex,
                "Failed to complete report pipeline for parking event {ObjectId} ({Start:u}-{End:u}).",
                evt.ObjectId, evt.ParkStartUtc, evt.ParkEndUtc);
        }
    }
}
