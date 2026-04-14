using RalphLoop.Config;
using Xunit;

namespace RalphLoop.Tests.Config;

public class PlanningArtifactsDiscoveryTests : IDisposable
{
    private readonly string _dir;

    public PlanningArtifactsDiscoveryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    // ── Directory missing ─────────────────────────────────────────────────────

    [Fact]
    public void Discover_MissingDirectory_ReturnsNotViable()
    {
        var result = PlanningArtifacts.Discover(Path.Combine(_dir, "does-not-exist"));

        Assert.False(result.IsViable);
        Assert.Null(result.EpicsMd);
        Assert.Null(result.PrdSource);
        Assert.Empty(result.AllMarkdownFiles);
    }

    // ── epics.md takes highest priority ───────────────────────────────────────

    [Fact]
    public void Discover_EpicsMdPresent_UsedAsPrimarySource()
    {
        File.WriteAllText(Path.Combine(_dir, "epics.md"), "# Epics");

        var result = PlanningArtifacts.Discover(_dir);

        Assert.True(result.IsViable);
        Assert.NotNull(result.EpicsMd);
        Assert.Equal(Path.Combine(_dir, "epics.md"), result.EpicsMd);
    }

    [Fact]
    public void Discover_EpicsMdAndPrdMdPresent_BothReported()
    {
        File.WriteAllText(Path.Combine(_dir, "epics.md"), "# Epics");
        File.WriteAllText(Path.Combine(_dir, "prd.md"), "# PRD");

        var result = PlanningArtifacts.Discover(_dir);

        Assert.NotNull(result.EpicsMd);
        Assert.NotNull(result.PrdSource);
        Assert.Equal("prd.md", result.PrdSourceLabel);
    }

    // ── PRD priority order ────────────────────────────────────────────────────

    [Fact]
    public void Discover_PrdMdPresent_UsesPrdMd()
    {
        File.WriteAllText(Path.Combine(_dir, "prd.md"), "# PRD");

        var result = PlanningArtifacts.Discover(_dir);

        Assert.True(result.IsViable);
        Assert.Equal(Path.Combine(_dir, "prd.md"), result.PrdSource);
        Assert.Equal("prd.md", result.PrdSourceLabel);
    }

    [Fact]
    public void Discover_PrdDistillateDir_UsedWhenNoPrdMd()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "prd-distillate"));

        var result = PlanningArtifacts.Discover(_dir);

        Assert.True(result.IsViable);
        Assert.Equal(Path.Combine(_dir, "prd-distillate"), result.PrdSource);
        Assert.Equal("prd-distillate/", result.PrdSourceLabel);
    }

    [Fact]
    public void Discover_PrdMdTakesPriorityOverDistillate()
    {
        File.WriteAllText(Path.Combine(_dir, "prd.md"), "# PRD");
        Directory.CreateDirectory(Path.Combine(_dir, "prd-distillate"));

        var result = PlanningArtifacts.Discover(_dir);

        Assert.Equal("prd.md", result.PrdSourceLabel);
    }

    [Fact]
    public void Discover_ValidationReportUsedWhenNoPrdMdOrDistillate()
    {
        File.WriteAllText(Path.Combine(_dir, "validation-report-prd-2026-04-10.md"), "# Validation");

        var result = PlanningArtifacts.Discover(_dir);

        Assert.True(result.IsViable);
        Assert.Equal("validation-report-prd-2026-04-10.md", result.PrdSourceLabel);
    }

    [Fact]
    public void Discover_MultipleValidationReports_MostRecentSelected()
    {
        File.WriteAllText(Path.Combine(_dir, "validation-report-prd-2026-04-10.md"), "old");
        File.WriteAllText(Path.Combine(_dir, "validation-report-prd-2026-04-14.md"), "newer");
        File.WriteAllText(Path.Combine(_dir, "validation-report-prd-2026-04-10-final.md"), "mid");

        var result = PlanningArtifacts.Discover(_dir);

        Assert.Equal("validation-report-prd-2026-04-14.md", result.PrdSourceLabel);
    }

    // ── Architecture priority order ───────────────────────────────────────────

    [Fact]
    public void Discover_ArchMdPresent_UsesArchMd()
    {
        File.WriteAllText(Path.Combine(_dir, "epics.md"), "# Epics");
        File.WriteAllText(Path.Combine(_dir, "architecture.md"), "# Arch");

        var result = PlanningArtifacts.Discover(_dir);

        Assert.Equal(Path.Combine(_dir, "architecture.md"), result.ArchSource);
        Assert.Equal("architecture.md", result.ArchSourceLabel);
    }

    [Fact]
    public void Discover_ArchDistillateDir_UsedWhenNoArchMd()
    {
        File.WriteAllText(Path.Combine(_dir, "epics.md"), "# Epics");
        Directory.CreateDirectory(Path.Combine(_dir, "architecture-distillate"));

        var result = PlanningArtifacts.Discover(_dir);

        Assert.Equal(Path.Combine(_dir, "architecture-distillate"), result.ArchSource);
        Assert.Equal("architecture-distillate/", result.ArchSourceLabel);
    }

    [Fact]
    public void Discover_ImplementationReadinessReport_UsedWhenNoArchMdOrDistillate()
    {
        File.WriteAllText(Path.Combine(_dir, "epics.md"), "# Epics");
        File.WriteAllText(Path.Combine(_dir, "implementation-readiness-report-2026-04-14-v2.md"), "report");

        var result = PlanningArtifacts.Discover(_dir);

        Assert.Equal("implementation-readiness-report-2026-04-14-v2.md", result.ArchSourceLabel);
    }

    [Fact]
    public void Discover_MultipleReadinessReports_MostRecentSelected()
    {
        File.WriteAllText(Path.Combine(_dir, "epics.md"), "# Epics");
        File.WriteAllText(Path.Combine(_dir, "implementation-readiness-report-2026-04-10.md"), "old");
        File.WriteAllText(Path.Combine(_dir, "implementation-readiness-report-2026-04-14.md"), "mid");
        File.WriteAllText(Path.Combine(_dir, "implementation-readiness-report-2026-04-14-v2.md"), "newest");

        var result = PlanningArtifacts.Discover(_dir);

        Assert.Equal("implementation-readiness-report-2026-04-14-v2.md", result.ArchSourceLabel);
    }

    // ── UX spec ───────────────────────────────────────────────────────────────

    [Fact]
    public void Discover_UxSpecMdPresent_Reported()
    {
        File.WriteAllText(Path.Combine(_dir, "epics.md"), "# Epics");
        File.WriteAllText(Path.Combine(_dir, "ux-design-specification.md"), "# UX");

        var result = PlanningArtifacts.Discover(_dir);

        Assert.NotNull(result.UxSource);
    }

    [Fact]
    public void Discover_UxDistillateDir_ReportedWhenNoUxMd()
    {
        File.WriteAllText(Path.Combine(_dir, "epics.md"), "# Epics");
        Directory.CreateDirectory(Path.Combine(_dir, "ux-design-specification-distillate"));

        var result = PlanningArtifacts.Discover(_dir);

        Assert.NotNull(result.UxSource);
    }

    // ── AllMarkdownFiles ──────────────────────────────────────────────────────

    [Fact]
    public void Discover_ListsAllMarkdownFilesInDirectory()
    {
        File.WriteAllText(Path.Combine(_dir, "epics.md"), "# Epics");
        File.WriteAllText(Path.Combine(_dir, "validation-report-prd-2026-04-10.md"), "v");
        File.WriteAllText(Path.Combine(_dir, "notes.txt"), "ignored");

        var result = PlanningArtifacts.Discover(_dir);

        Assert.Equal(2, result.AllMarkdownFiles.Count);
        Assert.All(result.AllMarkdownFiles, f => Assert.EndsWith(".md", f));
    }

    // ── IsViable ──────────────────────────────────────────────────────────────

    [Fact]
    public void Discover_NoUsableArtifacts_NotViable()
    {
        // Only non-qualifying files
        File.WriteAllText(Path.Combine(_dir, "implementation-readiness-report-2026-04-10.md"), "r");
        Directory.CreateDirectory(Path.Combine(_dir, "architecture-distillate"));

        var result = PlanningArtifacts.Discover(_dir);

        Assert.False(result.IsViable);
        Assert.NotNull(result.ArchSource); // arch found but no epics/prd source
    }
}
