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
