namespace AlleyWatch.Core.Options;

/// <summary>
/// Bound from the "Claude" section of appsettings.json. ApiKey is deliberately not a property here —
/// it is read from the Claude__ApiKey environment variable and passed directly to the Anthropic client,
/// never persisted in this options object or logged.
/// </summary>
public sealed class ClaudeOptions
{
    public const string SectionName = "Claude";

    public string Model { get; set; } = "claude-sonnet-5";
    public int MaxTokens { get; set; } = 1024;

    /// <summary>Upper bound on how many sampled frames FrameSampler will send per report.</summary>
    public int MaxSampledFrames { get; set; } = 8;
}
