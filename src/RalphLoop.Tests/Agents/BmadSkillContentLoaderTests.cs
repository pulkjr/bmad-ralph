using RalphLoop.Agents;
using RalphLoop.Config;
using Xunit;

namespace RalphLoop.Tests.Agents;

public sealed class BmadSkillContentLoaderTests : IDisposable
{
    private readonly string _tempDir;

    public BmadSkillContentLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        Directory.Delete(_tempDir, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void LoadForPrompt_NormalizesRelativePaths()
    {
        var shared = Path.Combine(_tempDir, "skills");
        var skillDir = Path.Combine(shared, "bmad-dev");
        Directory.CreateDirectory(skillDir);
        File.WriteAllText(
            Path.Combine(skillDir, "SKILL.md"),
            """
            See [guide](./docs/guide.md), `../common/rules.md`, and "./local/file.txt".
            """
        );

        var loader = new BmadSkillContentLoader(
            new RalphLoopConfig
            {
                ProjectPath = _tempDir,
                SkillDirectories = new SkillDirectoriesConfig
                {
                    Shared = shared,
                    Project = Path.Combine(_tempDir, "none"),
                    CopilotSkills = Path.Combine(_tempDir, "none2"),
                },
            }
        );

        var content = loader.LoadForPrompt("bmad-dev");

        Assert.Contains(Path.GetFullPath(Path.Combine(skillDir, "docs", "guide.md")), content);
        Assert.Contains(
            Path.GetFullPath(Path.Combine(skillDir, "..", "common", "rules.md")),
            content
        );
        Assert.Contains(Path.GetFullPath(Path.Combine(skillDir, "local", "file.txt")), content);
    }

    [Fact]
    public void LoadForPrompt_Throws_WhenSkillMissing()
    {
        var loader = new BmadSkillContentLoader(
            new RalphLoopConfig
            {
                ProjectPath = _tempDir,
                SkillDirectories = new SkillDirectoriesConfig
                {
                    Shared = Path.Combine(_tempDir, "skills"),
                    Project = Path.Combine(_tempDir, "none"),
                    CopilotSkills = Path.Combine(_tempDir, "none2"),
                },
            }
        );

        var ex = Assert.Throws<InvalidOperationException>(() =>
            loader.LoadForPrompt("missing-skill")
        );
        Assert.Contains("missing-skill", ex.Message);
    }

    [Fact]
    public void LoadForPrompt_FindsSkill_WhenInstalledUnderAliasName()
    {
        var shared = Path.Combine(_tempDir, "skills");
        // Install under the BMAD 6.x alias name, not the canonical "bmad-dev".
        var aliasSkillDir = Path.Combine(shared, "bmad-agent-dev");
        Directory.CreateDirectory(aliasSkillDir);
        File.WriteAllText(Path.Combine(aliasSkillDir, "SKILL.md"), "# Developer skill");

        var loader = new BmadSkillContentLoader(
            new RalphLoopConfig
            {
                ProjectPath = _tempDir,
                SkillDirectories = new SkillDirectoriesConfig
                {
                    Shared = shared,
                    Project = Path.Combine(_tempDir, "none"),
                    CopilotSkills = Path.Combine(_tempDir, "none2"),
                },
            }
        );

        // Request using the canonical name — should resolve via alias.
        var content = loader.LoadForPrompt("bmad-dev");

        Assert.Contains("Developer skill", content);
    }

    [Fact]
    public void LoadForPrompt_PrefersCanonicalOverAlias_WhenBothExist()
    {
        var shared = Path.Combine(_tempDir, "skills");
        var canonicalSkillDir = Path.Combine(shared, "bmad-dev");
        var aliasSkillDir = Path.Combine(shared, "bmad-agent-dev");
        Directory.CreateDirectory(canonicalSkillDir);
        Directory.CreateDirectory(aliasSkillDir);
        File.WriteAllText(Path.Combine(canonicalSkillDir, "SKILL.md"), "# Canonical skill");
        File.WriteAllText(Path.Combine(aliasSkillDir, "SKILL.md"), "# Alias skill");

        var loader = new BmadSkillContentLoader(
            new RalphLoopConfig
            {
                ProjectPath = _tempDir,
                SkillDirectories = new SkillDirectoriesConfig
                {
                    Shared = shared,
                    Project = Path.Combine(_tempDir, "none"),
                    CopilotSkills = Path.Combine(_tempDir, "none2"),
                },
            }
        );

        var content = loader.LoadForPrompt("bmad-dev");

        Assert.Contains("Canonical skill", content);
        Assert.DoesNotContain("Alias skill", content);
    }
}
