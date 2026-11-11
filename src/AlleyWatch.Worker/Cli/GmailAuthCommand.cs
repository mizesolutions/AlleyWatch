using AlleyWatch.Core.Options;
using AlleyWatch.Worker.Notification;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Microsoft.Extensions.Options;

namespace AlleyWatch.Worker.Cli;

/// <summary>
/// --auth-gmail: one-time interactive OAuth consent flow. Run manually, once, under the same Windows
/// user account the service will later log on as (see GmailTokenStore for why that matters) — after
/// this, the normal run path and --test-gmail-draft load the persisted token without prompting.
/// </summary>
public static class GmailAuthCommand
{
    public static async Task RunAsync(IConfiguration configuration, IOptionsMonitor<GmailOptions> gmailOptions, CancellationToken cancellationToken)
    {
        var clientId = configuration["Gmail:ClientId"]
            ?? throw new InvalidOperationException("Gmail:ClientId (env var Gmail__ClientId) is not set.");
        var clientSecret = configuration["Gmail:ClientSecret"]
            ?? throw new InvalidOperationException("Gmail:ClientSecret (env var Gmail__ClientSecret) is not set.");

        var dataStore = new GmailTokenStore(gmailOptions.CurrentValue.TokenStorePath);

        Console.WriteLine("Opening a browser for Gmail OAuth consent (scope: gmail.compose)...");
        await GoogleWebAuthorizationBroker.AuthorizeAsync(
            new ClientSecrets { ClientId = clientId, ClientSecret = clientSecret },
            [GmailService.Scope.GmailCompose],
            "alleywatch-user",
            cancellationToken,
            dataStore);

        Console.WriteLine($"Done. Refresh token stored (DPAPI-protected, CurrentUser scope) under: {gmailOptions.CurrentValue.TokenStorePath}");
        Console.WriteLine("Make sure the Windows Service is configured to log on as this same Windows user account.");
    }
}
