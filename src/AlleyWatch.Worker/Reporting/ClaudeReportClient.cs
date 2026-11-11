using System.Text.Json;
using AlleyWatch.Core.Options;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Options;

namespace AlleyWatch.Worker.Reporting;

/// <summary>
/// Calls Claude on a completed parking event: sampled frames in, a structured VehicleReportResult out.
/// Uses the official Anthropic C# SDK (not hand-rolled HTTP) with output_config.format for guaranteed-
/// parseable JSON, and checks stop_reason == "refusal" before reading content — a real value the safety
/// classifiers can return, which must not be treated as a pipeline crash.
/// </summary>
public sealed class ClaudeReportClient
{
    private readonly AnthropicClient _client;
    private readonly IOptionsMonitor<ClaudeOptions> _options;
    private readonly ILogger<ClaudeReportClient> _logger;

    public ClaudeReportClient(AnthropicClient client, IOptionsMonitor<ClaudeOptions> options, ILogger<ClaudeReportClient> logger)
    {
        _client = client;
        _options = options;
        _logger = logger;
    }

    public async Task<VehicleReportResult?> GenerateReportAsync(
        IReadOnlyList<byte[]> jpegFrames,
        DateTimeOffset parkStartUtc,
        DateTimeOffset parkEndUtc,
        CancellationToken cancellationToken)
    {
        if (jpegFrames.Count == 0)
        {
            _logger.LogWarning("No sampled frames for parking event {Start:u}-{End:u}; skipping Claude report.", parkStartUtc, parkEndUtc);
            return null;
        }

        var options = _options.CurrentValue;

        var content = new List<ContentBlockParam>();
        foreach (var jpeg in jpegFrames)
        {
            content.Add(new ImageBlockParam
            {
                Source = new Base64ImageSource
                {
                    Data = Convert.ToBase64String(jpeg),
                    MediaType = "image/jpeg",
                },
            });
        }
        content.Add(new TextBlockParam { Text = BuildPrompt(parkStartUtc, parkEndUtc) });

        var response = await _client.Messages.Create(new MessageCreateParams
        {
            Model = options.Model,
            MaxTokens = options.MaxTokens,
            Messages = [new MessageParam { Role = Role.User, Content = content }],
            OutputConfig = new OutputConfig
            {
                Format = new JsonOutputFormat { Schema = ReportJsonSchema },
            },
        }, cancellationToken);

        if (response.StopReason == "refusal")
        {
            _logger.LogWarning(
                "Claude declined the report request for event {Start:u}-{End:u} (category: {Category}). Skipping this report.",
                parkStartUtc, parkEndUtc, response.StopDetails?.Category);
            return null;
        }

        var text = response.Content.Select(b => b.Value).OfType<TextBlock>().FirstOrDefault()?.Text;
        if (text is null)
        {
            _logger.LogWarning("Claude response for event {Start:u}-{End:u} had no text block to parse.", parkStartUtc, parkEndUtc);
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<VehicleReportResult>(text);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse Claude's JSON for event {Start:u}-{End:u}: {Text}", parkStartUtc, parkEndUtc, text);
            return null;
        }
    }

    private static string BuildPrompt(DateTimeOffset parkStartUtc, DateTimeOffset parkEndUtc) =>
        $"""
         These images are frames from a home security camera covering a private alley, sampled across a
         period when a vehicle was parked there (from {parkStartUtc:u} to {parkEndUtc:u}).

         Report on the vehicle and any people:
         - vehicleColor: the vehicle's color.
         - vehicleMakeModel: best-effort make/model if identifiable, otherwise a plain description (e.g. "dark sedan").
         - licensePlateText: the license plate text if legible in any frame, otherwise the string "not legible".
         - peopleActivityDescription: a brief description of anyone seen getting in or out of the vehicle, or "none observed" if nobody is visible.

         Be factual and brief - this is a private security log, not a narrative.
         """;

    private static readonly Dictionary<string, JsonElement> ReportJsonSchema = new()
    {
        ["type"] = JsonSerializer.SerializeToElement("object"),
        ["properties"] = JsonSerializer.SerializeToElement(new
        {
            vehicleColor = new { type = "string" },
            vehicleMakeModel = new { type = "string" },
            licensePlateText = new { type = "string" },
            peopleActivityDescription = new { type = "string" },
        }),
        ["required"] = JsonSerializer.SerializeToElement(new[] { "vehicleColor", "vehicleMakeModel", "licensePlateText", "peopleActivityDescription" }),
        ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
    };
}
