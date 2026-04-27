using System.Reflection;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RalphLoop.Agents;
using RalphLoop.Build;
using RalphLoop.Config;
using RalphLoop.Data;
using RalphLoop.Data.FileStore;
using RalphLoop.Data.Repositories;
using RalphLoop.Git;
using RalphLoop.Loop;
using RalphLoop.Loop.Phases;
using RalphLoop.UI;
using Spectre.Console;

// ── Entry point ───────────────────────────────────────────────────────────────

// Handle flags that should not require a project path.
if (args.Any(a => a is "--version" or "-v"))
{
    var infoVersion =
        System
            .Reflection.Assembly.GetExecutingAssembly()
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? "unknown";
    Console.WriteLine($"ralph-loop {infoVersion}");
    return 0;
}

if (args.Any(a => a is "--help" or "-h"))
{
    AnsiConsole.Write(new FigletText("Ralph Loop").Color(Color.Blue));
    AnsiConsole.MarkupLine(
        "[grey]BMAD Agentic Development Loop — powered by GitHub Copilot SDK[/]"
    );
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine(
        "[bold]Usage:[/] ralph-loop [[[grey]project-path[/]]] [[[grey]options[/]]]"
    );
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine(
        "  [grey]project-path[/]   Path to the project root (default: current directory)"
    );
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[bold]Options:[/]");
    AnsiConsole.MarkupLine("  [grey]--version, -v[/]                Print the version and exit");
    AnsiConsole.MarkupLine("  [grey]--help,    -h[/]                Print this help and exit");
    AnsiConsole.MarkupLine(
        "  [grey]--skip-readiness-gate[/]        Skip Phase 2.5: Implementation Readiness Gate"
    );
    AnsiConsole.MarkupLine(
        "  [grey]--skip-code-quality[/]          Skip Phase 4: Code Quality Gate (all reviewers)"
    );
    AnsiConsole.MarkupLine(
        "  [grey]--skip-performance-pedant[/]    Skip Phase 4 Oliver reviewer (performance)"
    );
    AnsiConsole.MarkupLine(
        "  [grey]--skip-legacy-librarian[/]      Skip Phase 4 Vera reviewer (legacy drift)"
    );
    AnsiConsole.MarkupLine(
        "  [grey]--skip-test-archaeologist[/]    Skip Phase 4 Rex reviewer (test coverage)"
    );
    AnsiConsole.MarkupLine(
        "  [grey]--skip-coverage-critic[/]       Skip Phase 4 Nora reviewer (coverage gaps)"
    );
    AnsiConsole.MarkupLine(
        "  [grey]--skip-security-review[/]       Skip Phase 5 Security Analyst review"
    );
    AnsiConsole.MarkupLine("  [grey]--skip-architect-review[/]      Skip Phase 5 Architect review");
    AnsiConsole.MarkupLine(
        "  [grey]--skip-pm-review[/]             Skip Phase 5 Product Manager review"
    );
    AnsiConsole.MarkupLine(
        "  [grey]--skip-ux-review[/]             Skip Phase 5 UX Designer review"
    );
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[bold]Phases (per epic):[/]");
    AnsiConsole.MarkupLine(
        "  1. Sprint Planning     \u2014 Scrum Master scaffolds the sprint plan"
    );
    AnsiConsole.MarkupLine(
        "  2. Sprint Review       \u2014 Party-mode confidence vote + readiness gate"
    );
    AnsiConsole.MarkupLine(
        "  3. Story Loop          \u2014 Developer \u2192 QA \u2192 test.sh \u2192 commit (per story)"
    );
    AnsiConsole.MarkupLine("  4. Code Quality Gate   \u2014 4 parallel specialist reviewers");
    AnsiConsole.MarkupLine(
        "  5. Epic Completion     \u2014 Security, Architect, PM, UX reviews + final vote"
    );
    AnsiConsole.MarkupLine(
        "  6. Retrospective       \u2014 Retrospective + fast-forward merge to main"
    );
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[grey]Phases 1, 3, and 6 cannot be skipped.[/]");
    AnsiConsole.MarkupLine("[grey]See ralph-loop.json for full configuration options.[/]");
    return 0;
}

AnsiConsole.Write(new FigletText("Ralph Loop").Color(Color.Blue));
AnsiConsole.MarkupLine("[grey]BMAD Agentic Development Loop — powered by GitHub Copilot SDK[/]");
AnsiConsole.WriteLine();

// ── Resolve project path (first non-flag arg or cwd) ─────────────────────
var skipFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "--skip-readiness-gate",
    "--skip-code-quality",
    "--skip-performance-pedant",
    "--skip-legacy-librarian",
    "--skip-test-archaeologist",
    "--skip-coverage-critic",
    "--skip-security-review",
    "--skip-architect-review",
    "--skip-pm-review",
    "--skip-ux-review",
};
var projectPath = args.FirstOrDefault(a => !a.StartsWith("--")) ?? Directory.GetCurrentDirectory();

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

// ── Apply CLI phase-skip flags (override JSON config) ─────────────────────
CliPhaseFlags.Apply(args, config);

// ── Validate prerequisites ─────────────────────────────────────────────────
var prereqErrors = new List<string>();
foreach (var (cmd, arg) in new (string, string)[] { ("git", "--version"), ("bash", "--version") })
{
    try
    {
        var psi = new System.Diagnostics.ProcessStartInfo(cmd, arg)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
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

// ── Validate BMAD skills ───────────────────────────────────────────────────
var resolvedSkillDirs = SkillDirectoryResolver.Resolve(config);
var missingSkills = BmadSkillValidator.Check(resolvedSkillDirs);
if (missingSkills.Count > 0)
{
    BmadSkillValidator.PrintError(missingSkills, config);
    return 1;
}

// ── Wire up dependencies ───────────────────────────────────────────────────
var services = new ServiceCollection();

services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));

// Infrastructure
services.AddSingleton(config);
services.AddSingleton<ConsoleUI>();
services.AddSingleton<RunLogger>();
services.AddSingleton(_ => new LedgerDb(config.LedgerDbPath));
services.AddSingleton<SprintRepository>();
services.AddSingleton<EpicRepository>();
services.AddSingleton<StoryRepository>();
services.AddSingleton<FileStoreContext>();

// Git + build
services.AddSingleton(_ => new GitManager(
    config.ProjectPath,
    config.Git.TimeoutSeconds,
    config.Git.SuppressInteractivePrompts
));
services.AddSingleton(_ => new TestScriptRunner(config.ProjectPath, config.TestTimeoutMinutes));
services.AddSingleton(_ => new AgentTuiRunner(config.ProjectPath));

// Copilot SDK — --allow-all grants yolo rights to all agents (tools, paths, URLs)
services.AddSingleton(_ => new CopilotClient(
    new CopilotClientOptions
    {
        Cwd = config.ProjectPath,
        LogLevel = CopilotLogLevel.Default,
        CliArgs = ["--allow-all"],
    }
));

// Agents
services.AddSingleton<SessionFactory>();
services.AddSingleton<AgentRunner>();
services.AddSingleton<PartyModeSession>();

// Phases
services.AddSingleton<SprintPlanningPhase>();
services.AddSingleton<SprintReviewPhase>();
services.AddSingleton<StoryLoopPhase>();
services.AddSingleton<CodeQualityGatePhase>();
services.AddSingleton<EpicCompletionPhase>();
services.AddSingleton<RetrospectivePhase>();

// Orchestrator
services.AddSingleton<RalphLoopOrchestrator>();

await using var sp = services.BuildServiceProvider();

// ── Async initialization (after DI build — avoids sync-over-async deadlocks) ──
var db = sp.GetRequiredService<LedgerDb>();
await db.OpenAsync();

var ui = sp.GetRequiredService<ConsoleUI>();
var copilotClient = sp.GetRequiredService<CopilotClient>();
try
{
    await copilotClient.StartAsync();
}
catch (Exception ex) when (ex is System.IO.IOException or InvalidOperationException)
{
    try
    {
        CopilotErrorHandler.Rethrow(ex);
    }
    catch (InvalidOperationException friendlyEx)
    {
        ui.ShowError(friendlyEx.Message);
        return 1;
    }
    throw;
}

var git = sp.GetRequiredService<GitManager>();

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
catch (InvalidOperationException ex)
{
    // Show the error — some phase errors (e.g. git branch failures) throw without prior UI output.
    ui.ShowError(ex.Message);
    sp.GetRequiredService<RunLogger>().LogError("phase-fail", ex.ToString());
    return 1;
}
catch (Exception ex)
{
    ui.ShowError($"Fatal error: {ex.Message}");
    sp.GetRequiredService<RunLogger>().LogError("fatal", ex.ToString());
    AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
    return 1;
}
finally
{
    var client = sp.GetRequiredService<CopilotClient>();
    await client.StopAsync();
}
