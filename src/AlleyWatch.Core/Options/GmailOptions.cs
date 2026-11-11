namespace AlleyWatch.Core.Options;

/// <summary>
/// Bound from the "Gmail" section of appsettings.json. OAuth ClientId/ClientSecret are read from
/// Gmail__ClientId / Gmail__ClientSecret environment variables, not stored here.
/// </summary>
public sealed class GmailOptions
{
    public const string SectionName = "Gmail";

    public List<string> RecipientAddresses { get; set; } = [];

    /// <summary>Directory holding DPAPI-protected OAuth token files (one per Google.Apis.Auth data-store key).</summary>
    public string TokenStorePath { get; set; } = "data\\gmail-token";
}
