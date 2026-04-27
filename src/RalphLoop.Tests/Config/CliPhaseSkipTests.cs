using RalphLoop.Config;
using Xunit;

namespace RalphLoop.Tests.Config;

/// <summary>
/// Tests for the CLI flag-application logic (CliPhaseFlags.Apply).
/// The static helper maps --skip-* flags onto RalphLoopConfig.Phases properties.
/// </summary>
public sealed class CliPhaseSkipTests
{
    // ── --party-mode ──────────────────────────────────────────────────────────

    [Fact]
    public void Apply_PartyMode_SetsPartyModeTrue()
    {
        var config = new RalphLoopConfig();
        CliPhaseFlags.Apply(["--party-mode"], config);

        Assert.True(config.Phases.SprintReview.PartyMode);
    }

    // ── --skip-readiness-gate ──────────────────────────────────────────────────

    [Fact]
    public void Apply_SkipReadinessGate_SetsImplementationReadinessFalse()
    {
        var config = new RalphLoopConfig();
        CliPhaseFlags.Apply(["--skip-readiness-gate"], config);

        Assert.False(config.Phases.SprintReview.ImplementationReadiness);
    }

    // ── --skip-code-quality ───────────────────────────────────────────────────

    [Fact]
    public void Apply_SkipCodeQuality_SetsEnabledFalse()
    {
        var config = new RalphLoopConfig();
        CliPhaseFlags.Apply(["--skip-code-quality"], config);

        Assert.False(config.Phases.CodeQualityGate.Enabled);
    }

    // ── Individual Phase 4 reviewers ──────────────────────────────────────────

    [Fact]
    public void Apply_SkipPerformancePedant_SetsPerformancePedantFalse()
    {
        var config = new RalphLoopConfig();
        CliPhaseFlags.Apply(["--skip-performance-pedant"], config);

        Assert.False(config.Phases.CodeQualityGate.PerformancePedant);
    }

    [Fact]
    public void Apply_SkipLegacyLibrarian_SetsLegacyLibrarianFalse()
    {
        var config = new RalphLoopConfig();
        CliPhaseFlags.Apply(["--skip-legacy-librarian"], config);

        Assert.False(config.Phases.CodeQualityGate.LegacyLibrarian);
    }

    [Fact]
    public void Apply_SkipTestArchaeologist_SetsTestArchaeologistFalse()
    {
        var config = new RalphLoopConfig();
        CliPhaseFlags.Apply(["--skip-test-archaeologist"], config);

        Assert.False(config.Phases.CodeQualityGate.TestArchaeologist);
    }

    [Fact]
    public void Apply_SkipCoverageCritic_SetsCoverageCriticFalse()
    {
        var config = new RalphLoopConfig();
        CliPhaseFlags.Apply(["--skip-coverage-critic"], config);

        Assert.False(config.Phases.CodeQualityGate.CoverageCritic);
    }

    // ── Individual Phase 5 reviewers ──────────────────────────────────────────

    [Fact]
    public void Apply_SkipSecurityReview_SetsSecurityFalse()
    {
        var config = new RalphLoopConfig();
        CliPhaseFlags.Apply(["--skip-security-review"], config);

        Assert.False(config.Phases.EpicCompletion.Security);
    }

    [Fact]
    public void Apply_SkipArchitectReview_SetsArchitectFalse()
    {
        var config = new RalphLoopConfig();
        CliPhaseFlags.Apply(["--skip-architect-review"], config);

        Assert.False(config.Phases.EpicCompletion.Architect);
    }

    [Fact]
    public void Apply_SkipPmReview_SetsProductManagerFalse()
    {
        var config = new RalphLoopConfig();
        CliPhaseFlags.Apply(["--skip-pm-review"], config);

        Assert.False(config.Phases.EpicCompletion.ProductManager);
    }

    [Fact]
    public void Apply_SkipUxReview_SetsUxDesignerFalse()
    {
        var config = new RalphLoopConfig();
        CliPhaseFlags.Apply(["--skip-ux-review"], config);

        Assert.False(config.Phases.EpicCompletion.UxDesigner);
    }

    // ── Multiple flags ────────────────────────────────────────────────────────

    [Fact]
    public void Apply_MultipleFlags_AllApplied()
    {
        var config = new RalphLoopConfig();
        CliPhaseFlags.Apply(
            ["--skip-code-quality", "--skip-security-review", "--skip-ux-review"],
            config
        );

        Assert.False(config.Phases.CodeQualityGate.Enabled);
        Assert.False(config.Phases.EpicCompletion.Security);
        Assert.False(config.Phases.EpicCompletion.UxDesigner);
        // Unmentioned flags stay true
        Assert.True(config.Phases.EpicCompletion.Architect);
        Assert.True(config.Phases.SprintReview.ImplementationReadiness);
    }

    // ── Unknown flags ignored ─────────────────────────────────────────────────

    [Fact]
    public void Apply_UnknownFlags_AreIgnored_NoException()
    {
        var config = new RalphLoopConfig();
        var ex = Record.Exception(() => CliPhaseFlags.Apply(["--some-unknown-flag"], config));
        Assert.Null(ex);
    }

    // ── Empty flags ────────────────────────────────────────────────────────────

    [Fact]
    public void Apply_EmptyArgs_LeavesDefaultsUnchanged()
    {
        var config = new RalphLoopConfig();
        CliPhaseFlags.Apply([], config);

        Assert.True(config.Phases.SprintReview.ImplementationReadiness);
        Assert.True(config.Phases.CodeQualityGate.Enabled);
        Assert.True(config.Phases.EpicCompletion.Security);
    }
}
