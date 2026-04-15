using RalphLoop.Agents;
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
    }

    [Fact]
    public void Check_ReturnsAllSkills_WhenSkillDirsDoNotExist()
    {
        var nonExistent = Path.Combine(_tempDir, "does-not-exist");

        var missing = BmadSkillValidator.Check([nonExistent]);

        Assert.Equal(SessionFactory.RequiredSkills.Count, missing.Count);
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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string MakeSkillDir(string name)
    {
        var path = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(path);
        return path;
    }
}
