using AlleyWatch.Core.Options;
using Microsoft.Extensions.Options;

namespace AlleyWatch.Worker.Frigate;

/// <summary>
/// HTTP client against Frigate's REST API — a fallback/supplement to the MQTT snapshot topic (e.g.
/// fetching a specific event's best snapshot by id after the fact).
///
/// NOTE: exact endpoint paths are the long-standing Frigate convention (/api/events, .../snapshot.jpg,
/// .../thumbnail.jpg, .../clip.mp4) but were not confirmed against a live docs fetch for this build —
/// verify against the installed Frigate version's own API docs page (its web UI ships one) before
/// relying on this in production. In practice FrigateSnapshotSubscriber's MQTT-delivered images are the
/// primary path for report frames; this client exists for the cases that need a specific past event by id.
/// </summary>
public sealed class FrigateApiClient
{
    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<FrigateOptions> _options;

    public FrigateApiClient(HttpClient httpClient, IOptionsMonitor<FrigateOptions> options)
    {
        _httpClient = httpClient;
        _options = options;
        _httpClient.BaseAddress = new Uri(_options.CurrentValue.ApiBaseUrl);
    }

    /// <summary>Fetches the best snapshot JPEG for a specific Frigate event id. Returns null on any
    /// non-success response rather than throwing — this is a best-effort supplement to MQTT snapshots,
    /// not something that should take down report generation if it fails.</summary>
    public async Task<byte[]?> TryGetEventSnapshotAsync(string eventId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"/api/events/{eventId}/snapshot.jpg", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }
            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }
}
