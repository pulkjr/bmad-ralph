using RalphLoop.Config;
using Spectre.Console;

namespace RalphLoop.Agents;

/// <summary>
/// Checks that all required BMAD agent skills are installed before the loop starts.
/// A skill is considered installed when a subdirectory with its exact name exists
/// in at least one of the configured skill directories.
/// </summary>
public static class BmadSkillValidator
{
    /// <summary>
    /// Returns the required skills that are missing from all configured skill directories.
    /// An empty result means every skill was found and the loop may proceed.
    /// </summary>
    public static IReadOnlyList<(string SkillId, string DisplayName)> Check(
        IReadOnlyList<string> skillDirs
    )
    {
        var missing = new List<(string SkillId, string DisplayName)>();

        foreach (var (skillId, displayName) in SessionFactory.RequiredSkills)
        {
            var found = skillDirs.Any(dir => Directory.Exists(Path.Combine(dir, skillId)));
            if (!found)
                missing.Add((skillId, displayName));
        }

        return missing;
    }

    /// <summary>
    /// Writes a user-friendly error panel to the console listing missing skills and
    /// pointing to the BMAD Method installation instructions.
    /// </summary>
    public static void PrintError(
        IReadOnlyList<(string SkillId, string DisplayName)> missing,
        RalphLoopConfig config
    )
    {
        AnsiConsole.MarkupLine("[red bold]✗ BMAD skills not found.[/]");
        AnsiConsole.MarkupLine(
            "[red]The following required skills are missing from all skill directories:[/]"
        );
        AnsiConsole.WriteLine();

        foreach (var (skillId, displayName) in missing)
            AnsiConsole.MarkupLine(
                $"    [yellow]{Markup.Escape(skillId), -26}[/] {Markup.Escape(displayName)}"
            );

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Searched in:[/]");

        var searched = new[]
        {
            config.SkillDirectories.Shared,
            config.SkillDirectories.Project,
            config.SkillDirectories.CopilotSkills,
        };

        foreach (var dir in searched)
        {
            var exists = Directory.Exists(dir);
            var status = exists ? "[green]found[/]" : "[red]not found[/]";
            AnsiConsole.MarkupLine($"    • {Markup.Escape(dir)}  ({status})");
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Install BMAD Method, then re-run ralph-loop:[/]");
        AnsiConsole.MarkupLine("    [cyan]npx bmad-method install[/]");
        AnsiConsole.MarkupLine("    [link]https://github.com/bmad-code-org/BMAD-METHOD[/]");
    }
}
