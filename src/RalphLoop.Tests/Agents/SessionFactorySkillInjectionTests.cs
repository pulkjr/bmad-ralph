using GitHub.Copilot.SDK;
using RalphLoop.Agents;
using RalphLoop.Config;
using Xunit;

namespace RalphLoop.Tests.Agents;

public sealed class SessionFactorySkillInjectionTests : IDisposable
{
    private readonly string _tempDir;

    public SessionFactorySkillInjectionTests()
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
    public void ForDeveloper_InjectsSkillContentIntoSystemMessage()
    {
        var shared = Path.Combine(_tempDir, "skills");
        var skillDir = Path.Combine(shared, "bmad-agent-dev");
        Directory.CreateDirectory(skillDir);
        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), "Developer identity marker.");

        var factory = new SessionFactory(BuildConfig(shared));
        var cfg = factory.ForDeveloper(PermissionHandler.ApproveAll);

        Assert.NotNull(cfg.SystemMessage);
        Assert.Contains("BMAD SKILL CONTEXT (bmad-agent-dev)", cfg.SystemMessage!.Content);
        Assert.Contains("Developer identity marker.", cfg.SystemMessage.Content);
    }

    [Fact]
    public void BuildPartyPersonas_IncludesSkillContentForMappedPersona()
    {
        var shared = Path.Combine(_tempDir, "skills");
        var pmSkillDir = Path.Combine(shared, "bmad-agent-pm");
        Directory.CreateDirectory(pmSkillDir);
        File.WriteAllText(Path.Combine(pmSkillDir, "SKILL.md"), "PM identity marker.");

        var factory = new SessionFactory(BuildConfig(shared));
        var personas = factory.BuildPartyPersonas(includeUxDesigner: false);
        var pm = Assert.Single(personas.Where(p => p.Name == "product-manager"));

        Assert.Contains("PM identity marker.", pm.Prompt);
    }

    [Fact]
    public void BuildPartyPersonas_DeveloperUsesQuickDevSkillContent()
    {
        var shared = Path.Combine(_tempDir, "skills");
        var quickDevSkillDir = Path.Combine(shared, "bmad-quick-dev");
        Directory.CreateDirectory(quickDevSkillDir);
        File.WriteAllText(Path.Combine(quickDevSkillDir, "SKILL.md"), "Quick dev marker.");

        var factory = new SessionFactory(BuildConfig(shared));
        var personas = factory.BuildPartyPersonas(includeUxDesigner: false);
        var developer = Assert.Single(personas.Where(p => p.Name == "developer"));

        Assert.Contains("Quick dev marker.", developer.Prompt);
        Assert.Contains("BMAD SKILL CONTEXT (bmad-quick-dev)", developer.Prompt);
    }

    private RalphLoopConfig BuildConfig(string sharedSkillDir) =>
        new()
        {
            ProjectPath = _tempDir,
            SkillDirectories = new SkillDirectoriesConfig
            {
                Shared = sharedSkillDir,
                Project = Path.Combine(_tempDir, "none"),
                CopilotSkills = Path.Combine(_tempDir, "none2"),
            },
        };
}
