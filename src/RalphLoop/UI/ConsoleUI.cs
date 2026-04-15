using GitHub.Copilot.SDK;
using RalphLoop.Config;
using Spectre.Console;

namespace RalphLoop.UI;

/// <summary>
/// Wrapper around Spectre.Console for interactive prompts and status display.
/// Used both directly and as the handler for OnUserInputRequest callbacks.
/// </summary>
public class ConsoleUI
{
    public bool Confirm(string question, bool defaultValue = true)
    {
        return AnsiConsole.Confirm(question, defaultValue);
    }

    public string Ask(string question)
    {
        return AnsiConsole.Ask<string>(question);
    }

    public string AskChoice(string question, IEnumerable<string> choices)
    {
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>().Title(question).AddChoices(choices)
        );
    }

    public void ShowInfo(string message) =>
        AnsiConsole.MarkupLine($"[cyan]{Markup.Escape(message)}[/]");

    public void ShowSuccess(string message) =>
        AnsiConsole.MarkupLine($"[green]✓ {Markup.Escape(message)}[/]");

    public void ShowWarning(string message) =>
        AnsiConsole.MarkupLine($"[yellow]⚠ {Markup.Escape(message)}[/]");

    public void ShowError(string message) =>
        AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(message)}[/]");

    public void ShowAgentOutput(string agentName, string message)
    {
        AnsiConsole.MarkupLine(
            $"[bold blue][[{Markup.Escape(agentName)}]][/] {Markup.Escape(message)}"
        );
    }

    public void ShowSection(string title)
    {
        AnsiConsole.Write(new Rule($"[bold yellow]{Markup.Escape(title)}[/]").LeftJustified());
    }

    public void ShowPhase(string phase, string description)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(
            new Panel(new Markup($"[bold]{Markup.Escape(description)}[/]"))
            {
                Header = new PanelHeader($" {Markup.Escape(phase)} ", Justify.Center),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Blue),
                Padding = new Padding(1, 0),
            }
        );
        AnsiConsole.WriteLine();
    }

    public void ShowStoryStatus(
        string storyName,
        string status,
        int round,
        int failCount,
        long tokens
    )
    {
        var statusColor = status switch
        {
            "complete" => "green",
            "qa_passed" or "build_passed" => "cyan",
            "failed" => "red",
            "ready_for_review" => "yellow",
            _ => "white",
        };

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Story")
            .AddColumn("Status")
            .AddColumn("Round")
            .AddColumn("Fails")
            .AddColumn("Tokens");

        table.AddRow(
            Markup.Escape(storyName),
            $"[{statusColor}]{Markup.Escape(status)}[/]",
            round.ToString(),
            failCount.ToString(),
            tokens.ToString("N0")
        );

        AnsiConsole.Write(table);
    }

    public Task<string> WaitForUserInputAsync(string prompt)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[bold magenta]⏸  Agent is asking for your input:[/]");
        AnsiConsole.MarkupLine($"[white]{Markup.Escape(prompt)}[/]");
        AnsiConsole.WriteLine();
        // AnsiConsole.Ask is synchronous; wrap in Task.Run to avoid blocking the thread pool
        return Task.Run(() => AnsiConsole.Ask<string>("[yellow]Your response[/]:"));
    }

    public void ShowBuildOutput(string output, bool passed)
    {
        var header = passed ? "✓ Build/Test Passed" : "✗ Build/Test Failed";
        AnsiConsole.Write(
            new Panel(new Text(output))
            {
                Header = new PanelHeader($" {header} ", Justify.Center),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(passed ? Color.Green : Color.Red),
            }
        );
    }

    public async Task WithSpinnerAsync(string message, Func<Task> action)
    {
        await AnsiConsole
            .Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("blue"))
            .StartAsync(message, async _ => await action());
    }

    public void ShowAgentIntro(string agentName, string? model)
    {
        AnsiConsole.WriteLine();
        var modelLine = string.IsNullOrWhiteSpace(model)
            ? ""
            : $"\n[grey]Model: {Markup.Escape(model)}[/]";
        AnsiConsole.Write(
            new Panel(new Markup($"[bold white]{Markup.Escape(agentName)}[/]{modelLine}"))
            {
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Blue),
                Padding = new Padding(1, 0),
            }
        );
    }

    public void ShowAgentTokenSummary(string agentLabel, long tokens)
    {
        AnsiConsole.MarkupLine(
            $"[grey][[{Markup.Escape(agentLabel)}]][/] [dim]✦ {tokens:N0} tokens used[/]"
        );
    }

    public void ShowPartyRoster(IReadOnlyList<CustomAgentConfig> personas, string? model)
    {
        AnsiConsole.WriteLine();
        var modelLabel = string.IsNullOrWhiteSpace(model)
            ? ""
            : $"  [grey]Model: {Markup.Escape(model)}[/]";
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderStyle(new Style(Color.Yellow))
            .Title($"[bold yellow]Party Mode — {personas.Count} Participant(s)[/]{modelLabel}")
            .AddColumn(new TableColumn("[bold]Persona[/]"))
            .AddColumn(new TableColumn("[bold]Role[/]"))
            .AddColumn(new TableColumn("[bold]Purpose[/]"));

        foreach (var p in personas)
        {
            table.AddRow(
                $"[bold cyan]{Markup.Escape(p.DisplayName ?? p.Name)}[/]",
                Markup.Escape(p.Name),
                Markup.Escape(p.Description ?? string.Empty)
            );
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    public void ShowModelSummary(ModelsConfig models)
    {
        AnsiConsole.WriteLine();
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderStyle(new Style(Color.Grey))
            .Title("[bold grey]Role → Model Configuration[/]")
            .AddColumn(new TableColumn("[bold]Role[/]"))
            .AddColumn(new TableColumn("[bold]Model[/]"));

        table.AddRow("Developer", Markup.Escape(models.Developer));
        table.AddRow("Architect", Markup.Escape(models.Architect));
        table.AddRow("Product Manager", Markup.Escape(models.ProductManager));
        table.AddRow("QA", Markup.Escape(models.Qa));
        table.AddRow("Security", Markup.Escape(models.Security));
        table.AddRow("Tech Writer", Markup.Escape(models.TechWriter));
        table.AddRow("UX Designer", Markup.Escape(models.UxDesigner));
        table.AddRow("Party Mode", Markup.Escape(models.PartyMode));
        table.AddRow("Scrum Master", Markup.Escape(models.Default));

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }
}
