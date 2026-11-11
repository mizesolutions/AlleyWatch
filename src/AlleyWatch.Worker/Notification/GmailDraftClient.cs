using System.Text;
using AlleyWatch.Core.Options;
using AlleyWatch.Worker.Reporting;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using Microsoft.Extensions.Options;

namespace AlleyWatch.Worker.Notification;

/// <summary>
/// Creates a Gmail draft summarizing a parking report — never sends. Uses the same
/// GoogleWebAuthorizationBroker + GmailTokenStore path as --auth-gmail; on the normal (unattended) run
/// path this loads the already-consented, DPAPI-protected token rather than prompting.
/// </summary>
public sealed class GmailDraftClient
{
    private readonly IConfiguration _configuration;
    private readonly IOptionsMonitor<GmailOptions> _options;
    private readonly ILogger<GmailDraftClient> _logger;

    public GmailDraftClient(IConfiguration configuration, IOptionsMonitor<GmailOptions> options, ILogger<GmailDraftClient> logger)
    {
        _configuration = configuration;
        _options = options;
        _logger = logger;
    }

    public async Task CreateDraftAsync(
        DateTimeOffset parkStartUtc,
        DateTimeOffset parkEndUtc,
        VehicleReportResult report,
        CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var clientId = _configuration["Gmail:ClientId"]
            ?? throw new InvalidOperationException("Gmail:ClientId (env var Gmail__ClientId) is not set.");
        var clientSecret = _configuration["Gmail:ClientSecret"]
            ?? throw new InvalidOperationException("Gmail:ClientSecret (env var Gmail__ClientSecret) is not set.");

        var dataStore = new GmailTokenStore(options.TokenStorePath);
        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            new ClientSecrets { ClientId = clientId, ClientSecret = clientSecret },
            [GmailService.Scope.GmailCompose],
            "alleywatch-user",
            cancellationToken,
            dataStore);

        using var service = new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "AlleyWatch",
        });

        var duration = parkEndUtc - parkStartUtc;
        var subject = $"AlleyWatch: vehicle parked {parkStartUtc:t} - {parkEndUtc:t} ({duration:hh\\:mm} in the alley)";
        var body = BuildBody(parkStartUtc, parkEndUtc, duration, report);
        var to = string.Join(", ", options.RecipientAddresses);

        var draft = new Draft { Message = new Message { Raw = Base64UrlEncode(BuildMimeMessage(to, subject, body)) } };

        await service.Users.Drafts.Create(draft, "me").ExecuteAsync(cancellationToken);
        _logger.LogInformation("Created Gmail draft for parking event {Start:u}-{End:u}.", parkStartUtc, parkEndUtc);
    }

    private static string BuildBody(DateTimeOffset parkStartUtc, DateTimeOffset parkEndUtc, TimeSpan duration, VehicleReportResult report) =>
        $"""
         Parked: {parkStartUtc:u}
         Departed: {parkEndUtc:u}
         Duration: {duration:hh\:mm}

         Vehicle color: {report.VehicleColor}
         Vehicle make/model: {report.VehicleMakeModel ?? "unknown"}
         License plate: {report.LicensePlateText ?? "not legible"}

         People: {report.PeopleActivityDescription}
         """;

    private static string BuildMimeMessage(string to, string subject, string body)
    {
        var message = new StringBuilder();
        message.AppendLine($"To: {to}");
        message.AppendLine($"Subject: {subject}");
        message.AppendLine("Content-Type: text/plain; charset=utf-8");
        message.AppendLine();
        message.Append(body);
        return message.ToString();
    }

    private static string Base64UrlEncode(string input) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(input)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
