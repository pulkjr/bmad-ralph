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
    AnsiConsole.MarkupLine(
        "  [grey]--debug,   -d[/]               Enable debug mode: SDK log level=debug, all events logged"
    );
    AnsiConsole.MarkupLine(
        "  [grey]--smoke-test[/]                Run a minimal SDK write test and exit (no full loop)"
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

var debugMode = args.Any(a => a is "--debug" or "-d");
var smokeTestMode = args.Any(a => a is "--smoke-test");

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

// ── Apply debug mode ──────────────────────────────────────────────────────
if (debugMode)
{
    config.DebugLog = true;
    AnsiConsole.MarkupLine(
        "[yellow]DEBUG MODE: SDK log level set to debug. All events will be logged.[/]"
    );
}

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
// Skip skill validation in smoke-test mode — the smoke test uses a minimal
// session config that doesn't load any BMAD skills.
if (!smokeTestMode)
{
    var resolvedSkillDirs = SkillDirectoryResolver.Resolve(config);
    var missingSkills = BmadSkillValidator.Check(resolvedSkillDirs);
    if (missingSkills.Count > 0)
    {
        BmadSkillValidator.PrintError(missingSkills, config);
        return 1;
    }
}

// ── Wire up dependencies ───────────────────────────────────────────────────
var services = new ServiceCollection();

services.AddLogging(b =>
    b.AddConsole().SetMinimumLevel(debugMode ? LogLevel.Debug : LogLevel.Warning)
);

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

// Copilot SDK — --allow-all grants yolo rights to all agents (tools, paths, URLs).
// SDK 0.3.0 + CLI 1.0.37: session-level approve-all is set via SetApproveAllAsync()
// after session creation (see AgentRunner), replacing the old callback-based protocol.
// In debug mode, log level is elevated to capture all SDK internals including
// permission requests, tool dispatch, and subprocess communication.
services.AddSingleton(_ => new CopilotClient(
    new CopilotClientOptions
    {
        Cwd = config.ProjectPath,
        LogLevel = debugMode ? CopilotLogLevel.Debug : CopilotLogLevel.Default,
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
    if (!smokeTestMode)
    {
        await ModelResolver.ResolveAsync(
            copilotClient,
            config.Models,
            ui,
            cts.Token,
            askModelChoice: (question, choices, recommended) =>
                ui.AskModelChoice(question, choices, recommended)
        );
        ui.ShowModelSummary(config.Models);
    }
}
catch (InvalidOperationException ex)
{
    ui.ShowError(ex.Message);
    return 1;
}

// ── Check entire.io ────────────────────────────────────────────────────────

if (!smokeTestMode && config.Git.UseEntire && !await git.IsEntireEnabledAsync())
{
    ui.ShowWarning("entire.io is not enabled for this repository.");
    if (ui.Confirm("Enable entire.io now? (Recommended for session capture)"))
    {
        await git.EnableEntireAsync();
        ui.ShowSuccess("entire.io enabled.");
    }
}

// ── Smoke test mode — minimal SDK write test ──────────────────────────────
if (smokeTestMode)
{
    AnsiConsole.MarkupLine("[yellow]SMOKE TEST: Sending a file-write prompt to the SDK...[/]");
    AnsiConsole.MarkupLine($"[grey]Working directory: {config.ProjectPath}[/]");

    var runLogger = sp.GetRequiredService<RunLogger>();
    var smokeOutputFile = Path.Combine(config.ProjectPath, "smoke-test-result.txt");
    var sessionCfg = new GitHub.Copilot.SDK.SessionConfig
    {
        Model = "claude-sonnet-4.6",
        WorkingDirectory = config.ProjectPath,
        EnableConfigDiscovery = false,
        OnPermissionRequest = GitHub.Copilot.SDK.PermissionHandler.ApproveAll,
    };

    var responseBuilder = new System.Text.StringBuilder();
    var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var events = new System.Collections.Concurrent.ConcurrentBag<string>();

    await using var session = await copilotClient.CreateSessionAsync(sessionCfg);
    await session.Rpc.Permissions.SetApproveAllAsync(true, CancellationToken.None);
    using var _ = session.On(evt =>
    {
        var label = evt.GetType().Name;
        events.Add(label);

        switch (evt)
        {
            case GitHub.Copilot.SDK.AssistantMessageEvent msg:
                responseBuilder.Append(msg.Data.Content);
                break;

            case GitHub.Copilot.SDK.PermissionRequestedEvent perm:
                var permJson = System.Text.Json.JsonSerializer.Serialize(
                    perm.Data,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = false }
                );
                AnsiConsole.MarkupLine(
                    $"[yellow]PERMISSION REQUESTED: {Markup.Escape(permJson)}[/]"
                );
                runLogger.LogPermissionEvent("smoke-test", "permission.requested", permJson);
                break;

            case GitHub.Copilot.SDK.ToolExecutionStartEvent toolStart:
                AnsiConsole.MarkupLine(
                    $"[blue]TOOL START: {toolStart.Data?.ToolName ?? "(unknown)"}[/]"
                );
                runLogger.LogToolEvent(
                    "smoke-test",
                    toolStart.Data?.ToolName ?? "(unknown)",
                    "start",
                    ""
                );
                break;

            case GitHub.Copilot.SDK.ToolExecutionCompleteEvent toolComplete:
                var toolErr = toolComplete.Data?.Error?.Message;
                var status = toolErr is null ? "ok" : $"error: {toolErr}";
                AnsiConsole.MarkupLine(
                    $"[blue]TOOL COMPLETE: {toolComplete.Data?.ToolCallId ?? "(unknown)"} → {Markup.Escape(status)}[/]"
                );
                runLogger.LogToolEvent(
                    "smoke-test",
                    toolComplete.Data?.ToolCallId ?? "(unknown)",
                    "complete",
                    status
                );
                break;

            case GitHub.Copilot.SDK.ExternalToolRequestedEvent ext:
                AnsiConsole.MarkupLine(
                    $"[magenta]EXTERNAL TOOL REQUESTED: {ext.Data?.ToolName ?? "(unknown)"}[/]"
                );
                runLogger.LogToolEvent(
                    "smoke-test",
                    ext.Data?.ToolName ?? "(unknown)",
                    "external-requested",
                    ""
                );
                break;

            case GitHub.Copilot.SDK.SessionWarningEvent warn:
                AnsiConsole.MarkupLine(
                    $"[yellow]SESSION WARNING: {Markup.Escape(warn.Data?.Message ?? "")}[/]"
                );
                break;

            case GitHub.Copilot.SDK.SessionErrorEvent err:
                done.TrySetException(
                    new InvalidOperationException($"Session error: {err.Data.Message}")
                );
                break;

            case GitHub.Copilot.SDK.SessionIdleEvent:
                done.TrySetResult();
                break;
        }
    });

    var prompt =
        $"Write a file called 'smoke-test-result.txt' inside the directory '{config.ProjectPath}' "
        + "containing exactly the text: sdk-write-ok\n\n"
        + "After writing the file, confirm what you did in one sentence.";

    await session.SendAsync(new GitHub.Copilot.SDK.MessageOptions { Prompt = prompt });

    using var smokeCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
    try
    {
        await done.Task.WaitAsync(smokeCts.Token);
    }
    catch (OperationCanceledException)
    {
        AnsiConsole.MarkupLine("[red]SMOKE TEST TIMED OUT[/]");
        AnsiConsole.MarkupLine($"Events seen: {string.Join(", ", events.Distinct())}");
        return 1;
    }
    catch (InvalidOperationException ex)
    {
        AnsiConsole.MarkupLine($"[red]SMOKE TEST FAILED: {Markup.Escape(ex.Message)}[/]");
        AnsiConsole.MarkupLine($"Events seen: {string.Join(", ", events.Distinct())}");
        return 1;
    }

    AnsiConsole.MarkupLine($"[grey]Events seen: {string.Join(", ", events.Distinct())}[/]");
    AnsiConsole.MarkupLine(
        $"[grey]Agent response: {Markup.Escape(responseBuilder.ToString().Trim())}[/]"
    );

    if (File.Exists(smokeOutputFile))
    {
        var content = (await File.ReadAllTextAsync(smokeOutputFile)).Trim();
        if (content == "sdk-write-ok")
        {
            AnsiConsole.MarkupLine(
                "[green]✓ SMOKE TEST PASSED: SDK successfully wrote the file.[/]"
            );
            return 0;
        }
        else
        {
            AnsiConsole.MarkupLine(
                $"[red]✗ SMOKE TEST FAILED: File exists but content is '{Markup.Escape(content)}' (expected 'sdk-write-ok')[/]"
            );
            return 1;
        }
    }
    else
    {
        AnsiConsole.MarkupLine(
            $"[red]✗ SMOKE TEST FAILED: SDK did not create '{smokeOutputFile}'[/]"
        );
        AnsiConsole.MarkupLine(
            "[yellow]This indicates the SDK cannot write files in the project directory.[/]"
        );
        AnsiConsole.MarkupLine(
            $"[grey]Check {config.ProjectPath}/logs/ for the JSONL debug log.[/]"
        );
        return 1;
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
