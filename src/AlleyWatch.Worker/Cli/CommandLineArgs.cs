namespace AlleyWatch.Worker.Cli;

/// <summary>
/// The service supports a handful of one-off CLI modes alongside its normal run (auth bootstrap, dry
/// runs for manual verification). Deliberately a plain switch over `args` rather than a CLI-parsing
/// package — there are exactly four modes and none take further options.
/// </summary>
public enum RunMode
{
    /// <summary>Normal operation: run the Frigate listener + orchestrator pipeline (as a Windows Service or console app).</summary>
    Run,

    /// <summary>--auth-gmail: one-time interactive OAuth consent flow. Run manually, once, as the account the service will log on as.</summary>
    AuthGmail,

    /// <summary>--dry-run-claude &lt;folder&gt;: send the JPEGs in a folder through ClaudeReportClient and print the parsed result.</summary>
    DryRunClaude,

    /// <summary>--test-gmail-draft: create one placeholder Gmail draft to confirm auth + recipient config.</summary>
    TestGmailDraft,
}

public sealed record ParsedArgs(RunMode Mode, string? DryRunClaudeFolder = null);

public static class CommandLineArgs
{
    public static ParsedArgs Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return new ParsedArgs(RunMode.Run);
        }

        switch (args[0])
        {
            case "--auth-gmail":
                return new ParsedArgs(RunMode.AuthGmail);

            case "--test-gmail-draft":
                return new ParsedArgs(RunMode.TestGmailDraft);

            case "--dry-run-claude":
                if (args.Length < 2)
                {
                    throw new ArgumentException("--dry-run-claude requires a folder path argument.");
                }
                return new ParsedArgs(RunMode.DryRunClaude, args[1]);

            default:
                throw new ArgumentException(
                    $"Unrecognized argument '{args[0]}'. Supported: --auth-gmail, --test-gmail-draft, --dry-run-claude <folder>, or no arguments for normal run.");
        }
    }
}
