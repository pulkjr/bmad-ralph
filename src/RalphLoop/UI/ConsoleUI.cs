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
            new SelectionPrompt<string>()
                .Title(question)
                .AddChoices(choices));
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
        AnsiConsole.MarkupLine($"[bold blue][[{Markup.Escape(agentName)}]][/] {Markup.Escape(message)}");
    }

    public void ShowSection(string title)
    {
        AnsiConsole.Write(new Rule($"[bold yellow]{Markup.Escape(title)}[/]").LeftJustified());
    }

    public void ShowPhase(string phase, string description)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Panel(
            new Markup($"[bold]{Markup.Escape(description)}[/]"))
        {
            Header = new PanelHeader($" {Markup.Escape(phase)} ", Justify.Center),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Blue),
            Padding = new Padding(1, 0),
        });
        AnsiConsole.WriteLine();
    }

    public void ShowStoryStatus(string storyName, string status, int round, int failCount, long tokens)
    {
        var statusColor = status switch
        {
            "complete" => "green",
            "qa_passed" or "build_passed" => "cyan",
            "failed" => "red",
            "ready_for_review" => "yellow",
            _ => "white"
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
            tokens.ToString("N0"));

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
        var color = passed ? "green" : "red";
        var header = passed ? "✓ Build/Test Passed" : "✗ Build/Test Failed";
        AnsiConsole.Write(new Panel(new Text(output))
        {
            Header = new PanelHeader($" {header} ", Justify.Center),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(passed ? Color.Green : Color.Red),
        });
    }

    public async Task WithSpinnerAsync(string message, Func<Task> action)
    {
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("blue"))
            .StartAsync(message, async _ => await action());
    }
}
