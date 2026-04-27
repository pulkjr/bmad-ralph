using GitHub.Copilot.SDK;
using RalphLoop.Agents;
using RalphLoop.Config;
using Xunit;

namespace RalphLoop.Tests.Agents;

public sealed class SessionFactorySessionConfigTests : IDisposable
{
    private readonly string _tempDir;

    public SessionFactorySessionConfigTests()
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
    public void AllAgentSessionConfigs_EnableConfigDiscovery_AndKeepToolsUnrestricted()
    {
        var config = BuildConfig();
        SeedRequiredSkills(config.SkillDirectories.Shared);

        var factory = new SessionFactory(config);
        var permission = PermissionHandler.ApproveAll;

        var sessionConfigs = new SessionConfig[]
        {
            factory.ForDeveloper(permission),
            factory.ForQa(permission),
            factory.ForArchitect(permission),
            factory.ForProductManager(permission),
            factory.ForSecurity(permission),
            factory.ForTechWriter(permission),
            factory.ForUxDesigner(permission),
            factory.ForScrumMaster(permission),
            factory.ForStoryRefiner(permission),
            factory.ForSqliteStoryRefiner(permission),
            factory.ForPartyMode([], permission),
        };

        foreach (var cfg in sessionConfigs)
        {
            Assert.True(cfg.EnableConfigDiscovery);
            Assert.Equal(_tempDir, cfg.WorkingDirectory);
            Assert.Null(cfg.AvailableTools);
            Assert.Null(cfg.ExcludedTools);
            Assert.NotNull(cfg.OnPermissionRequest);
        }
    }

    [Fact]
    public void ForSqliteStoryRefiner_SystemMessageContainsSqlite3Instruction()
    {
        // Regression test: ForSqliteStoryRefiner must NOT load the bmad-create-story skill.
        // That skill can instruct the model that shell is unavailable, which breaks sqlite3 execution.
        // The system message must explicitly state that shell and sqlite3 are available.
        var config = BuildConfig();
        SeedRequiredSkills(config.SkillDirectories.Shared);

        var factory = new SessionFactory(config);
        var cfg = factory.ForSqliteStoryRefiner(PermissionHandler.ApproveAll);

        // Should have a system message with explicit sqlite3 / shell guidance
        Assert.NotNull(cfg.SystemMessage);
        Assert.Contains("sqlite3", cfg.SystemMessage.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("shell", cfg.SystemMessage.Content, StringComparison.OrdinalIgnoreCase);

        // System message must NOT contain bmad-create-story skill content
        // (skill content starts with "Skill marker for bmad-create-story" in the test seed)
        Assert.DoesNotContain(
            "bmad-create-story",
            cfg.SystemMessage.Content,
            StringComparison.OrdinalIgnoreCase
        );
    }

    private RalphLoopConfig BuildConfig() =>
        new()
        {
            ProjectPath = _tempDir,
            SkillDirectories = new SkillDirectoriesConfig
            {
                Shared = Path.Combine(_tempDir, "skills-shared"),
                Project = Path.Combine(_tempDir, "skills-project"),
                CopilotSkills = Path.Combine(_tempDir, "skills-copilot"),
            },
        };

    private static void SeedRequiredSkills(string sharedSkillDir)
    {
        foreach (var (skillId, _) in SessionFactory.RequiredSkills)
        {
            var dir = Path.Combine(sharedSkillDir, skillId);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "SKILL.md"), $"Skill marker for {skillId}");
        }
    }
}
