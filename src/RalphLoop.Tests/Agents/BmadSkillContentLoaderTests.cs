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
        var skillDir = Path.Combine(shared, "bmad-agent-dev");
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

        var content = loader.LoadForPrompt("bmad-agent-dev");

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
}
