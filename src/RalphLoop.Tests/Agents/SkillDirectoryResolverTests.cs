using RalphLoop.Agents;
using RalphLoop.Config;
using Xunit;

namespace RalphLoop.Tests.Agents;

public sealed class SkillDirectoryResolverTests : IDisposable
{
    private readonly string _tempDir;

    public SkillDirectoryResolverTests()
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
    public void Resolve_ReturnsEmpty_WhenNeitherDirExists()
    {
        var config = BuildConfig(
            sharedDir: Path.Combine(_tempDir, "nonexistent-shared"),
            projectDir: Path.Combine(_tempDir, "nonexistent-project")
        );

        var dirs = SkillDirectoryResolver.Resolve(config);

        Assert.Empty(dirs);
    }

    [Fact]
    public void Resolve_IncludesBoth_WhenBothExist()
    {
        var sharedDir = MakeDir("shared");
        var projectDir = MakeDir("project");
        var config = BuildConfig(sharedDir, projectDir);

        var dirs = SkillDirectoryResolver.Resolve(config);

        Assert.Equal(2, dirs.Count);
        Assert.Contains(sharedDir, dirs);
        Assert.Contains(projectDir, dirs);
    }

    [Fact]
    public void Resolve_IncludesOnlyShared_WhenProjectDirMissing()
    {
        var sharedDir = MakeDir("shared");
        var config = BuildConfig(
            sharedDir: sharedDir,
            projectDir: Path.Combine(_tempDir, "no-project")
        );

        var dirs = SkillDirectoryResolver.Resolve(config);

        Assert.Single(dirs);
        Assert.Contains(sharedDir, dirs);
    }

    [Fact]
    public void Resolve_IncludesOnlyProject_WhenSharedDirMissing()
    {
        var projectDir = MakeDir("project");
        var config = BuildConfig(
            sharedDir: Path.Combine(_tempDir, "no-shared"),
            projectDir: projectDir
        );

        var dirs = SkillDirectoryResolver.Resolve(config);

        Assert.Single(dirs);
        Assert.Contains(projectDir, dirs);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string MakeDir(string name)
    {
        var path = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static RalphLoopConfig BuildConfig(string sharedDir, string projectDir) =>
        new()
        {
            SkillDirectories = new SkillDirectoriesConfig
            {
                Shared = sharedDir,
                Project = projectDir,
            },
        };
}
