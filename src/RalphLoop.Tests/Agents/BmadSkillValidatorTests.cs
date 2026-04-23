using RalphLoop.Agents;
using RalphLoop.Config;
using Spectre.Console;
using Xunit;

namespace RalphLoop.Tests.Agents;

public sealed class BmadSkillValidatorTests : IDisposable
{
    private readonly string _tempDir;

    public BmadSkillValidatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        Directory.Delete(_tempDir, recursive: true);
        GC.SuppressFinalize(this);
    }

    // ── All present ───────────────────────────────────────────────────────────

    [Fact]
    public void Check_ReturnsEmpty_WhenAllSkillsPresent()
    {
        var skillDir = MakeSkillDir("all");
        foreach (var (skillId, _) in SessionFactory.RequiredSkills)
            Directory.CreateDirectory(Path.Combine(skillDir, skillId));

        var missing = BmadSkillValidator.Check([skillDir]);

        Assert.Empty(missing);
    }

    // ── Partial installs ──────────────────────────────────────────────────────

    [Fact]
    public void Check_ReturnsMissingSkill_WhenOneSkillAbsent()
    {
        var skillDir = MakeSkillDir("partial");
        foreach (var (skillId, _) in SessionFactory.RequiredSkills)
            Directory.CreateDirectory(Path.Combine(skillDir, skillId));

        var (absentId, absentName) = SessionFactory.RequiredSkills[0];
        Directory.Delete(Path.Combine(skillDir, absentId), recursive: false);

        var missing = BmadSkillValidator.Check([skillDir]);

        Assert.Single(missing);
        Assert.Equal(absentId, missing[0].SkillId);
        Assert.Equal(absentName, missing[0].DisplayName);
    }

    // ── Fully missing ─────────────────────────────────────────────────────────

    [Fact]
    public void Check_ReturnsAllSkills_WhenSkillDirIsEmpty()
    {
        var emptyDir = MakeSkillDir("empty");

        var missing = BmadSkillValidator.Check([emptyDir]);

        Assert.Equal(SessionFactory.RequiredSkills.Count, missing.Count);
        foreach (var (skillId, displayName) in SessionFactory.RequiredSkills)
        {
            Assert.Contains(missing, m => m.SkillId == skillId && m.DisplayName == displayName);
        }
    }

    [Fact]
    public void Check_ReturnsAllSkills_WhenSkillDirsListIsEmpty()
    {
        var missing = BmadSkillValidator.Check([]);

        Assert.Equal(SessionFactory.RequiredSkills.Count, missing.Count);
        foreach (var (skillId, displayName) in SessionFactory.RequiredSkills)
        {
            Assert.Contains(missing, m => m.SkillId == skillId && m.DisplayName == displayName);
        }
    }

    [Fact]
    public void Check_ReturnsAllSkills_WhenSkillDirsDoNotExist()
    {
        var nonExistent = Path.Combine(_tempDir, "does-not-exist");

        var missing = BmadSkillValidator.Check([nonExistent]);

        Assert.Equal(SessionFactory.RequiredSkills.Count, missing.Count);
        foreach (var (skillId, displayName) in SessionFactory.RequiredSkills)
        {
            Assert.Contains(missing, m => m.SkillId == skillId && m.DisplayName == displayName);
        }
    }

    // ── Multi-directory fallback ──────────────────────────────────────────────

    [Fact]
    public void Check_Passes_WhenSkillsInSharedButNotProjectDir()
    {
        var sharedDir = MakeSkillDir("shared");
        var projectDir = MakeSkillDir("project"); // exists but empty

        foreach (var (skillId, _) in SessionFactory.RequiredSkills)
            Directory.CreateDirectory(Path.Combine(sharedDir, skillId));

        var missing = BmadSkillValidator.Check([sharedDir, projectDir]);

        Assert.Empty(missing);
    }

    [Fact]
    public void Check_Passes_WhenSkillsInProjectButNotSharedDir()
    {
        var sharedDir = MakeSkillDir("shared"); // exists but empty
        var projectDir = MakeSkillDir("project");

        foreach (var (skillId, _) in SessionFactory.RequiredSkills)
            Directory.CreateDirectory(Path.Combine(projectDir, skillId));

        var missing = BmadSkillValidator.Check([sharedDir, projectDir]);

        Assert.Empty(missing);
    }

    // ── BMAD 6.x alias resolution ─────────────────────────────────────────────

    [Fact]
    public void Check_ReturnsEmpty_WhenAllSkillsInstalledUnderAliasNames()
    {
        var skillDir = MakeSkillDir("bmad6");
        foreach (var (skillId, _) in SessionFactory.RequiredSkills)
        {
            var dirName = BmadSkillContentLoader.SkillAliases.TryGetValue(skillId, out var alias)
                ? alias
                : skillId;
            Directory.CreateDirectory(Path.Combine(skillDir, dirName));
        }

        var missing = BmadSkillValidator.Check([skillDir]);

        Assert.Empty(missing);
    }

    [Fact]
    public void Check_ReturnsEmpty_WhenSkillsAreMixOfCanonicalAndAliasNames()
    {
        var skillDir = MakeSkillDir("mixed");
        var required = SessionFactory.RequiredSkills;

        // Install first half under canonical names, rest under alias names.
        for (var i = 0; i < required.Count; i++)
        {
            var (skillId, _) = required[i];
            var hasAlias = BmadSkillContentLoader.SkillAliases.TryGetValue(skillId, out var alias);
            var dirName = (i >= required.Count / 2 && hasAlias) ? alias! : skillId;
            Directory.CreateDirectory(Path.Combine(skillDir, dirName));
        }

        var missing = BmadSkillValidator.Check([skillDir]);

        Assert.Empty(missing);
    }

    // ── PrintError ────────────────────────────────────────────────────────────

    [Fact]
    public void PrintError_ContainsMissingSkillIdsAndDisplayNames()
    {
        var missing = new List<(string SkillId, string DisplayName)>
        {
            ("bmad-dev", "Developer (Amelia)"),
            ("bmad-pm", "Product Manager (John)"),
        };
        var config = new RalphLoopConfig
        {
            SkillDirectories = new SkillDirectoriesConfig
            {
                Shared = Path.Combine(_tempDir, "s"),
                Project = Path.Combine(_tempDir, "p"),
                CopilotSkills = Path.Combine(_tempDir, "c"),
            },
        };

        var output = CaptureAnsiConsoleOutput(() => BmadSkillValidator.PrintError(missing, config));

        Assert.Contains("bmad-dev", output);
        Assert.Contains("Developer (Amelia)", output);
        Assert.Contains("bmad-pm", output);
        Assert.Contains("Product Manager (John)", output);
    }

    [Fact]
    public void PrintError_ShowsFoundStatus_WhenDirectoryExists()
    {
        var existingDir = MakeSkillDir("existing");
        var config = new RalphLoopConfig
        {
            SkillDirectories = new SkillDirectoriesConfig
            {
                Shared = existingDir,
                Project = Path.Combine(_tempDir, "no-project"),
                CopilotSkills = Path.Combine(_tempDir, "no-copilot"),
            },
        };

        var output = CaptureAnsiConsoleOutput(() => BmadSkillValidator.PrintError([], config));

        Assert.Contains("found", output);
        Assert.Contains(existingDir, output);
    }

    [Fact]
    public void PrintError_ShowsNotFoundStatus_WhenDirectoryMissing()
    {
        var missingDir = Path.Combine(_tempDir, "does-not-exist");
        var config = new RalphLoopConfig
        {
            SkillDirectories = new SkillDirectoriesConfig
            {
                Shared = missingDir,
                Project = Path.Combine(_tempDir, "also-missing"),
                CopilotSkills = Path.Combine(_tempDir, "missing-copilot"),
            },
        };

        var output = CaptureAnsiConsoleOutput(() => BmadSkillValidator.PrintError([], config));

        Assert.Contains("not found", output);
        Assert.Contains(missingDir, output);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string MakeSkillDir(string name)
    {
        var path = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Runs <paramref name="action"/> with a temporary non-ANSI console that writes
    /// to a StringWriter, then restores the original static console. This avoids
    /// mutating shared state and prevents parallel-test interference.
    /// </summary>
    private static string CaptureAnsiConsoleOutput(Action action)
    {
        var writer = new StringWriter();
        var testConsole = AnsiConsole.Create(
            new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
                Out = new AnsiConsoleOutput(writer),
            }
        );

        var previous = AnsiConsole.Console;
        AnsiConsole.Console = testConsole;
        try
        {
            action();
        }
        finally
        {
            AnsiConsole.Console = previous;
        }

        return writer.ToString();
    }
}
