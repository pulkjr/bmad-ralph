using GitHub.Copilot.SDK;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RalphLoop.Agents;
using RalphLoop.Build;
using RalphLoop.Config;
using RalphLoop.Data;
using RalphLoop.Data.Repositories;
using RalphLoop.Git;
using RalphLoop.Loop;
using RalphLoop.Loop.Phases;
using RalphLoop.UI;
using Spectre.Console;
using System.Reflection;

// ── Entry point ───────────────────────────────────────────────────────────────

// Handle flags that should not require a project path.
if (args.Length == 1 && args[0] is "--version" or "-v")
{
    var infoVersion = System.Reflection.Assembly.GetExecutingAssembly()
        .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion ?? "unknown";
    Console.WriteLine($"ralph-loop {infoVersion}");
    return 0;
}

if (args.Length == 1 && args[0] is "--help" or "-h")
{
    AnsiConsole.Write(new FigletText("Ralph Loop").Color(Color.Blue));
    AnsiConsole.MarkupLine("[grey]BMAD Agentic Development Loop — powered by GitHub Copilot SDK[/]");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[bold]Usage:[/] ralph-loop [[[grey]project-path[/]]]");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("  [grey]project-path[/]   Path to the project root (default: current directory)");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[bold]Options:[/]");
    AnsiConsole.MarkupLine("  [grey]--version, -v[/]  Print the version and exit");
    AnsiConsole.MarkupLine("  [grey]--help,    -h[/]  Print this help and exit");
    return 0;
}

AnsiConsole.Write(new FigletText("Ralph Loop").Color(Color.Blue));
AnsiConsole.MarkupLine("[grey]BMAD Agentic Development Loop — powered by GitHub Copilot SDK[/]");
AnsiConsole.WriteLine();

// ── Resolve project path (first arg or cwd) ────────────────────────────────
var projectPath = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();

if (!Directory.Exists(projectPath))
{
    AnsiConsole.MarkupLine($"[red]Project path not found: {projectPath}[/]");
    return 1;
}

// ── Load config ────────────────────────────────────────────────────────────
RalphLoopConfig config;
try
{
    config = ConfigLoader.Load(projectPath);
}
catch (Exception ex)
{
    AnsiConsole.MarkupLine($"[red]Failed to load ralph-loop.json: {ex.Message}[/]");
    return 1;
}

// ── Validate prerequisites ─────────────────────────────────────────────────
var prereqErrors = new List<string>();
foreach (var (cmd, arg) in new (string, string)[] { ("git", "--version"), ("bash", "--version") })
{
    try
    {
        var psi = new System.Diagnostics.ProcessStartInfo(cmd, arg)
        {
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true,
        };
        using var proc = System.Diagnostics.Process.Start(psi);
        proc?.WaitForExit(3000);
        if (proc?.ExitCode != 0)
            prereqErrors.Add($"'{cmd}' did not exit cleanly.");
    }
    catch
    {
        prereqErrors.Add($"'{cmd}' is not installed or not on PATH.");
    }
}

if (prereqErrors.Count > 0)
{
    foreach (var err in prereqErrors)
        AnsiConsole.MarkupLine($"[red]Prerequisite check failed: {Markup.Escape(err)}[/]");
    return 1;
}

// ── Wire up dependencies ───────────────────────────────────────────────────
var services = new ServiceCollection();

services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));

// Infrastructure
services.AddSingleton(config);
services.AddSingleton<ConsoleUI>();
services.AddSingleton(_ => new LedgerDb(config.LedgerDbPath));
services.AddSingleton<SprintRepository>();
services.AddSingleton<EpicRepository>();
services.AddSingleton<StoryRepository>();

// Git + build
services.AddSingleton(_ => new GitManager(config.ProjectPath));
services.AddSingleton(_ => new TestScriptRunner(config.ProjectPath));
services.AddSingleton(_ => new AgentTuiRunner(config.ProjectPath));

// Copilot SDK
services.AddSingleton(_ => new CopilotClient(new CopilotClientOptions
{
    Cwd = config.ProjectPath,
    LogLevel = CopilotLogLevel.Default,
}));

// Agents
services.AddSingleton<SessionFactory>();
services.AddSingleton<AgentRunner>();
services.AddSingleton<PartyModeSession>();

// Phases
services.AddSingleton<SprintPlanningPhase>();
services.AddSingleton<SprintReviewPhase>();
services.AddSingleton<StoryLoopPhase>();
services.AddSingleton<EpicCompletionPhase>();
services.AddSingleton<RetrospectivePhase>();

// Orchestrator
services.AddSingleton<RalphLoopOrchestrator>();

await using var sp = services.BuildServiceProvider();

// ── Async initialization (after DI build — avoids sync-over-async deadlocks) ──
var db = sp.GetRequiredService<LedgerDb>();
await db.OpenAsync();

var copilotClient = sp.GetRequiredService<CopilotClient>();
await copilotClient.StartAsync();

var git = sp.GetRequiredService<GitManager>();
var ui = sp.GetRequiredService<ConsoleUI>();

// ── Resolve models against the user's actual Copilot subscription ──────────
// Substitutes any unavailable model with the best available 1x alternative
// and ensures QA and Developer never share the same model.
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    ui.ShowWarning("Cancellation requested — finishing current step...");
    cts.Cancel();
};

try
{
    await ModelResolver.ResolveAsync(copilotClient, config.Models, ui, cts.Token);
    ui.ShowModelSummary(config.Models);
}
catch (InvalidOperationException ex)
{
    ui.ShowError(ex.Message);
    return 1;
}

// ── Check entire.io ────────────────────────────────────────────────────────

if (config.Git.UseEntire && !await git.IsEntireEnabledAsync())
{
    ui.ShowWarning("entire.io is not enabled for this repository.");
    if (ui.Confirm("Enable entire.io now? (Recommended for session capture)"))
    {
        await git.EnableEntireAsync();
        ui.ShowSuccess("entire.io enabled.");
    }
}

// ── Run ────────────────────────────────────────────────────────────────────
try
{
    var orchestrator = sp.GetRequiredService<RalphLoopOrchestrator>();
    await orchestrator.RunAsync(cts.Token);
    return 0;
}
catch (OperationCanceledException)
{
    ui.ShowWarning("Ralph Loop was cancelled.");
    return 130;
}
catch (Exception ex)
{
    ui.ShowError($"Fatal error: {ex.Message}");
    AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
    return 1;
}
finally
{
    var client = sp.GetRequiredService<CopilotClient>();
    await client.StopAsync();
}
