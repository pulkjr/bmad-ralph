using System.Text.Json;
using RalphLoop.Config;
using Xunit;

namespace RalphLoop.Tests.Config;

public sealed class ConfigLoaderTests : IDisposable
{
    private readonly string _tempDir;

    public ConfigLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        Directory.Delete(_tempDir, recursive: true);
        GC.SuppressFinalize(this);
    }

    // ── Missing ralph-loop.json ───────────────────────────────────────────────

    [Fact]
    public void Load_ReturnsDefaults_WhenConfigFileMissing()
    {
        var config = ConfigLoader.Load(_tempDir);

        Assert.Equal(Path.GetFullPath(_tempDir), config.ProjectPath);
        Assert.Equal(3, config.MaxQaFailsBeforeSwarm);
        Assert.Equal(10, config.MaxStoryRounds);
        Assert.True(config.Git.AutoCommit);
        Assert.True(config.Git.UseEntire);
    }

    // ── JSON parsing ──────────────────────────────────────────────────────────

    [Fact]
    public void Load_ParsesJsonValues_WhenConfigFilePresent()
    {
        const string json = """
            {
              "maxQaFailsBeforeSwarm": 5,
              "maxStoryRounds": 20,
              "appCommand": "dotnet run",
              "git": { "autoCommit": false, "mergeStrategy": "squash", "useEntire": false }
            }
            """;
        WriteConfig(json);

        var config = ConfigLoader.Load(_tempDir);

        Assert.Equal(5, config.MaxQaFailsBeforeSwarm);
        Assert.Equal(20, config.MaxStoryRounds);
        Assert.Equal("dotnet run", config.AppCommand);
        Assert.False(config.Git.AutoCommit);
        Assert.Equal("squash", config.Git.MergeStrategy);
        Assert.False(config.Git.UseEntire);
    }

    [Fact]
    public void Load_ParsesModels_WhenConfigFilePresent()
    {
        const string json = """
            {
              "models": {
                "default": "gpt-4",
                "developer": "gpt-5-codex",
                "qa": "claude-3"
              }
            }
            """;
        WriteConfig(json);

        var config = ConfigLoader.Load(_tempDir);

        Assert.Equal("gpt-4", config.Models.Default);
        Assert.Equal("gpt-5-codex", config.Models.Developer);
        Assert.Equal("claude-3", config.Models.Qa);
    }

    [Fact]
    public void Load_ToleratesTrailingCommasAndComments()
    {
        const string json = """
            {
              // a comment
              "maxQaFailsBeforeSwarm": 7, /* trailing */
            }
            """;
        WriteConfig(json);

        var config = ConfigLoader.Load(_tempDir);
        Assert.Equal(7, config.MaxQaFailsBeforeSwarm);
    }

    [Fact]
    public void Load_Throws_InvalidOperationException_WhenJsonIsMalformed()
    {
        WriteConfig("{ this is not valid JSON !!! }");

        var ex = Assert.Throws<InvalidOperationException>(() => ConfigLoader.Load(_tempDir));
        Assert.Contains("ralph-loop.json", ex.Message);
        Assert.IsType<System.Text.Json.JsonException>(ex.InnerException);
    }

    // ── Path resolution ───────────────────────────────────────────────────────

    [Fact]
    public void Load_SetsLedgerDbPath_ToProjectDir()
    {
        var config = ConfigLoader.Load(_tempDir);

        Assert.Equal(Path.Combine(Path.GetFullPath(_tempDir), "ledger.db"), config.LedgerDbPath);
    }

    [Fact]
    public void Load_SetsPlanningArtifactsPath_ToDefaultFallback_WhenNoBmadConfig()
    {
        var config = ConfigLoader.Load(_tempDir);

        var expected = Path.GetFullPath(Path.Combine(_tempDir, "_bmad-output"));
        Assert.Equal(expected, config.PlanningArtifactsPath);
    }

    // ── BMAD config.yaml ──────────────────────────────────────────────────────

    [Fact]
    public void Load_ResolvesPlanningArtifacts_FromBmadYaml()
    {
        var bmadDir = Path.Combine(_tempDir, "_bmad", "bmm");
        Directory.CreateDirectory(bmadDir);
        File.WriteAllText(
            Path.Combine(bmadDir, "config.yaml"),
            $"planning_artifacts: {_tempDir}/custom-output\n"
        );

        var config = ConfigLoader.Load(_tempDir);

        Assert.Equal(
            Path.GetFullPath(Path.Combine(_tempDir, "custom-output")),
            config.PlanningArtifactsPath
        );
    }

    [Fact]
    public void Load_ResolvesPlanningArtifacts_WithProjectRootPlaceholder()
    {
        var bmadDir = Path.Combine(_tempDir, "_bmad", "bmm");
        Directory.CreateDirectory(bmadDir);
        File.WriteAllText(
            Path.Combine(bmadDir, "config.yaml"),
            "planning_artifacts: \"{project-root}/artifacts\"\n"
        );

        var config = ConfigLoader.Load(_tempDir);

        Assert.Equal(
            Path.GetFullPath(Path.Combine(_tempDir, "artifacts")),
            config.PlanningArtifactsPath
        );
    }

    [Fact]
    public void Load_SetsPlanningArtifactsPath_ToDefaultFallback_WhenBmadYamlExistsButKeyAbsent()
    {
        // YAML file exists but has no planning_artifacts key → must fall back to _bmad-output/
        var bmadDir = Path.Combine(_tempDir, "_bmad", "bmm");
        Directory.CreateDirectory(bmadDir);
        File.WriteAllText(
            Path.Combine(bmadDir, "config.yaml"),
            "project_name: my-project\ncommunication_language: en\n"
        );

        var config = ConfigLoader.Load(_tempDir);

        var expected = Path.GetFullPath(Path.Combine(_tempDir, "_bmad-output"));
        Assert.Equal(expected, config.PlanningArtifactsPath);
    }

    [Fact]
    public void Load_ResolvesCopilotSkillsPath_ToProjectGitHubSkills()
    {
        var config = ConfigLoader.Load(_tempDir);

        var expected = Path.GetFullPath(Path.Combine(_tempDir, ".github", "skills"));
        Assert.Equal(expected, config.SkillDirectories.CopilotSkills);
    }

    // ── SaveDefault ───────────────────────────────────────────────────────────

    [Fact]
    public void SaveDefault_WritesValidJson()
    {
        ConfigLoader.SaveDefault(_tempDir);

        var configPath = Path.Combine(_tempDir, "ralph-loop.json");
        Assert.True(File.Exists(configPath));
        var content = File.ReadAllText(configPath);
        var ex = Record.Exception(() => JsonDocument.Parse(content));
        Assert.Null(ex);
    }

    [Fact]
    public void SaveDefault_ThenLoad_RoundTrips()
    {
        ConfigLoader.SaveDefault(_tempDir);
        var config = ConfigLoader.Load(_tempDir);

        Assert.Equal(3, config.MaxQaFailsBeforeSwarm);
        Assert.Equal(Path.GetFullPath(_tempDir), config.ProjectPath);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void WriteConfig(string json) =>
        File.WriteAllText(Path.Combine(_tempDir, "ralph-loop.json"), json);
}
