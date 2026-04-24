using System.Text.Json;
using RalphLoop.Config;
using Xunit;

namespace RalphLoop.Tests.Config;

public sealed class PhasesConfigTests
{
    // ── Defaults ──────────────────────────────────────────────────────────────

    [Fact]
    public void PhasesConfig_AllDefaults_AreTrue()
    {
        var cfg = new PhasesConfig();

        Assert.True(cfg.SprintReview.ImplementationReadiness);
        Assert.True(cfg.CodeQualityGate.Enabled);
        Assert.True(cfg.CodeQualityGate.PerformancePedant);
        Assert.True(cfg.CodeQualityGate.LegacyLibrarian);
        Assert.True(cfg.CodeQualityGate.TestArchaeologist);
        Assert.True(cfg.CodeQualityGate.CoverageCritic);
        Assert.True(cfg.EpicCompletion.Security);
        Assert.True(cfg.EpicCompletion.Architect);
        Assert.True(cfg.EpicCompletion.ProductManager);
        Assert.True(cfg.EpicCompletion.UxDesigner);
    }

    [Fact]
    public void RalphLoopConfig_Phases_DefaultsToAllTrue()
    {
        var config = new RalphLoopConfig();

        Assert.NotNull(config.Phases);
        Assert.True(config.Phases.CodeQualityGate.Enabled);
        Assert.True(config.Phases.SprintReview.ImplementationReadiness);
    }

    // ── JSON round-trip ───────────────────────────────────────────────────────

    [Fact]
    public void PhasesConfig_RoundTrips_ThroughJsonSerializer()
    {
        var original = new PhasesConfig
        {
            SprintReview = new PhaseSprintReviewConfig { ImplementationReadiness = false },
            CodeQualityGate = new PhaseCodeQualityConfig
            {
                Enabled = false,
                PerformancePedant = false,
                LegacyLibrarian = true,
                TestArchaeologist = false,
                CoverageCritic = true,
            },
            EpicCompletion = new PhaseEpicCompletionConfig
            {
                Security = false,
                Architect = true,
                ProductManager = false,
                UxDesigner = true,
            },
        };

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        var json = JsonSerializer.Serialize(original, options);
        var deserialized = JsonSerializer.Deserialize<PhasesConfig>(json, options)!;

        Assert.False(deserialized.SprintReview.ImplementationReadiness);
        Assert.False(deserialized.CodeQualityGate.Enabled);
        Assert.False(deserialized.CodeQualityGate.PerformancePedant);
        Assert.True(deserialized.CodeQualityGate.LegacyLibrarian);
        Assert.False(deserialized.CodeQualityGate.TestArchaeologist);
        Assert.True(deserialized.CodeQualityGate.CoverageCritic);
        Assert.False(deserialized.EpicCompletion.Security);
        Assert.True(deserialized.EpicCompletion.Architect);
        Assert.False(deserialized.EpicCompletion.ProductManager);
        Assert.True(deserialized.EpicCompletion.UxDesigner);
    }

    // ── Reviewer flags independent of Enabled ─────────────────────────────────

    [Fact]
    public void CodeQualityConfig_AllReviewersFalse_DoesNotForceEnabledOff()
    {
        var cfg = new PhaseCodeQualityConfig
        {
            Enabled = true,
            PerformancePedant = false,
            LegacyLibrarian = false,
            TestArchaeologist = false,
            CoverageCritic = false,
        };

        // Enabled is still true regardless of individual reviewer flags
        Assert.True(cfg.Enabled);
    }

    [Fact]
    public void CodeQualityConfig_EnabledTrue_DoesNotForceReviewersOn()
    {
        var cfg = new PhaseCodeQualityConfig
        {
            Enabled = true,
            PerformancePedant = false,
            LegacyLibrarian = false,
            TestArchaeologist = false,
            CoverageCritic = false,
        };

        Assert.False(cfg.PerformancePedant);
        Assert.False(cfg.LegacyLibrarian);
        Assert.False(cfg.TestArchaeologist);
        Assert.False(cfg.CoverageCritic);
    }

    // ── ConfigLoader round-trip ───────────────────────────────────────────────

    [Fact]
    public void ConfigLoader_Deserializes_NestedPhasesJson()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            const string json = """
                {
                  "phases": {
                    "sprintReview": { "implementationReadiness": false },
                    "codeQualityGate": { "enabled": false, "performancePedant": false },
                    "epicCompletion": { "security": false, "productManager": false }
                  }
                }
                """;
            File.WriteAllText(Path.Combine(tempDir, "ralph-loop.json"), json);

            var config = ConfigLoader.Load(tempDir);

            Assert.False(config.Phases.SprintReview.ImplementationReadiness);
            Assert.False(config.Phases.CodeQualityGate.Enabled);
            Assert.False(config.Phases.CodeQualityGate.PerformancePedant);
            Assert.True(config.Phases.CodeQualityGate.LegacyLibrarian); // not set → default true
            Assert.False(config.Phases.EpicCompletion.Security);
            Assert.False(config.Phases.EpicCompletion.ProductManager);
            Assert.True(config.Phases.EpicCompletion.Architect); // not set → default true
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── RunLogger.LogPhaseSkipped ─────────────────────────────────────────────

    [Fact]
    public void RunLogger_LogPhaseSkipped_WritesToLog_WhenEnabled()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            var config = new RalphLoopConfig { ProjectPath = tempDir, DebugLog = true };
            var logger = new RalphLoop.UI.RunLogger(config);

            logger.LogPhaseSkipped("code-quality-gate", "disabled in config");

            var logsDir = Path.Combine(tempDir, "logs");
            var logFiles = Directory.GetFiles(logsDir, "*.jsonl");
            Assert.Single(logFiles);

            var contents = File.ReadAllText(logFiles[0]);
            Assert.Contains("phase_skipped", contents);
            Assert.Contains("code-quality-gate", contents);
            Assert.Contains("disabled in config", contents);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void RunLogger_LogPhaseSkipped_IsNoOp_WhenDisabled()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            var config = new RalphLoopConfig { ProjectPath = tempDir, DebugLog = false };
            var logger = new RalphLoop.UI.RunLogger(config);

            // Should not throw and should not create a log file
            logger.LogPhaseSkipped("code-quality-gate", "disabled in config");

            var logsDir = Path.Combine(tempDir, "logs");
            Assert.False(Directory.Exists(logsDir));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
