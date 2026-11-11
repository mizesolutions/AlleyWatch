using AlleyWatch.Core.Options;
using AlleyWatch.Worker.Cli;
using AlleyWatch.Worker.Frigate;
using AlleyWatch.Worker.Notification;
using AlleyWatch.Worker.Orchestration;
using AlleyWatch.Worker.Reporting;
using Anthropic;
using Microsoft.Extensions.Options;

var parsedArgs = CommandLineArgs.Parse(args);

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<FrigateOptions>(builder.Configuration.GetSection(FrigateOptions.SectionName));
builder.Services.Configure<ParkingOptions>(builder.Configuration.GetSection(ParkingOptions.SectionName));
builder.Services.Configure<ClaudeOptions>(builder.Configuration.GetSection(ClaudeOptions.SectionName));
builder.Services.Configure<GmailOptions>(builder.Configuration.GetSection(GmailOptions.SectionName));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));

builder.Services.AddSingleton(_ =>
{
    var apiKey = builder.Configuration["Claude:ApiKey"]
        ?? throw new InvalidOperationException("Claude:ApiKey (env var Claude__ApiKey) is not set.");
    return new AnthropicClient { ApiKey = apiKey };
});

builder.Services.AddHttpClient<FrigateApiClient>();

builder.Services.AddSingleton<FrigateMqttListener>();
builder.Services.AddSingleton<FrameSampler>();
builder.Services.AddSingleton<ClaudeReportClient>();
builder.Services.AddSingleton<ReportStore>();
builder.Services.AddSingleton<GmailDraftClient>();

builder.Services.AddHostedService(sp => sp.GetRequiredService<FrigateMqttListener>());
builder.Services.AddHostedService<ParkingEventOrchestrator>();

builder.Services.AddWindowsService(options => options.ServiceName = "AlleyWatch");

var host = builder.Build();

switch (parsedArgs.Mode)
{
    case RunMode.AuthGmail:
        await GmailAuthCommand.RunAsync(
            host.Services.GetRequiredService<IConfiguration>(),
            host.Services.GetRequiredService<IOptionsMonitor<GmailOptions>>(),
            CancellationToken.None);
        return;

    case RunMode.TestGmailDraft:
        await RunTestGmailDraftAsync(host.Services);
        return;

    case RunMode.DryRunClaude:
        await RunDryRunClaudeAsync(host.Services, parsedArgs.DryRunClaudeFolder!);
        return;

    case RunMode.Run:
    default:
        await host.RunAsync();
        return;
}

static async Task RunTestGmailDraftAsync(IServiceProvider services)
{
    var gmailDraftClient = services.GetRequiredService<GmailDraftClient>();
    var placeholderReport = new VehicleReportResult(
        VehicleColor: "silver",
        VehicleMakeModel: "test vehicle (placeholder)",
        LicensePlateText: "TEST-1234",
        PeopleActivityDescription: "none observed (this is a placeholder draft from --test-gmail-draft)");

    var start = DateTimeOffset.UtcNow.AddMinutes(-15);
    var end = DateTimeOffset.UtcNow;

    await gmailDraftClient.CreateDraftAsync(start, end, placeholderReport, CancellationToken.None);
    Console.WriteLine("Placeholder Gmail draft created. Check Gmail Drafts to confirm it looks right and that nothing was sent.");
}

static async Task RunDryRunClaudeAsync(IServiceProvider services, string folder)
{
    if (!Directory.Exists(folder))
    {
        Console.Error.WriteLine($"Folder not found: {folder}");
        Environment.ExitCode = 1;
        return;
    }

    var jpegPaths = Directory.GetFiles(folder, "*.jpg").Concat(Directory.GetFiles(folder, "*.jpeg")).ToList();
    if (jpegPaths.Count == 0)
    {
        Console.Error.WriteLine($"No .jpg/.jpeg files found in {folder}");
        Environment.ExitCode = 1;
        return;
    }

    var frames = jpegPaths.Select(File.ReadAllBytes).ToList();
    var claudeReportClient = services.GetRequiredService<ClaudeReportClient>();

    var report = await claudeReportClient.GenerateReportAsync(
        frames, DateTimeOffset.UtcNow.AddMinutes(-15), DateTimeOffset.UtcNow, CancellationToken.None);

    if (report is null)
    {
        Console.WriteLine("Claude did not return a report (see the logged warning above - refusal, empty frames, or parse failure).");
        return;
    }

    Console.WriteLine(
        $"""
         Vehicle color: {report.VehicleColor}
         Vehicle make/model: {report.VehicleMakeModel}
         License plate: {report.LicensePlateText}
         People: {report.PeopleActivityDescription}
         """);
}
