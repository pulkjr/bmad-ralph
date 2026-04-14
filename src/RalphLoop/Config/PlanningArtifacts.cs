using System.Text.RegularExpressions;

namespace RalphLoop.Config;

/// <summary>
/// Describes the planning artifacts discovered in PlanningArtifactsPath.
/// Discovery priority order (highest → lowest):
///   Epics source:        epics.md  >  prd.md  >  prd-distillate/  >  validation-report-prd-*.md
///   Architecture source: architecture.md  >  architecture-distillate/  >  implementation-readiness-report-*.md
/// </summary>
public record PlanningArtifacts(
    /// <summary>Path to epics.md if found; null otherwise.</summary>
    string? EpicsMd,
    /// <summary>Best available PRD-like source (prd.md, distillate dir, or validation report).</summary>
    string? PrdSource,
    /// <summary>Human-readable label for PrdSource (e.g. "prd.md", "prd-distillate/").</summary>
    string? PrdSourceLabel,
    /// <summary>Best available architecture source.</summary>
    string? ArchSource,
    /// <summary>Human-readable label for ArchSource.</summary>
    string? ArchSourceLabel,
    /// <summary>UX design specification file or directory if present.</summary>
    string? UxSource,
    /// <summary>All markdown files found directly in the artifacts directory.</summary>
    IReadOnlyList<string> AllMarkdownFiles)
{
    /// <summary>True when the minimum required artifacts to create a sprint backlog were found.</summary>
    public bool IsViable => EpicsMd is not null || PrdSource is not null;

    /// <summary>
    /// Scans <paramref name="artifactsPath"/> and returns a populated <see cref="PlanningArtifacts"/>.
    /// Never throws — returns an instance with nulls if the directory is absent.
    /// </summary>
    public static PlanningArtifacts Discover(string artifactsPath)
    {
        if (!Directory.Exists(artifactsPath))
            return new PlanningArtifacts(null, null, null, null, null, null, []);

        // ── Epics ──────────────────────────────────────────────────────────────
        var epicsMd = Probe(artifactsPath, "epics.md");

        // ── PRD source (in priority order) ────────────────────────────────────
        string? prdSource = null;
        string? prdLabel = null;

        if (Probe(artifactsPath, "prd.md") is { } prd)
        {
            prdSource = prd;
            prdLabel = "prd.md";
        }
        else if (ProbeDir(artifactsPath, "prd-distillate") is { } prdDir)
        {
            prdSource = prdDir;
            prdLabel = "prd-distillate/";
        }
        else
        {
            // Most-recent validation-report-prd-*.md
            var validationReports = Directory.GetFiles(artifactsPath, "validation-report-prd-*.md")
                .OrderByDescending(VersionSortKey)
                .ToArray();
            if (validationReports.Length > 0)
            {
                prdSource = validationReports[0];
                prdLabel = Path.GetFileName(validationReports[0]);
            }
        }

        // ── Architecture source (in priority order) ───────────────────────────
        string? archSource = null;
        string? archLabel = null;

        if (Probe(artifactsPath, "architecture.md") is { } arch)
        {
            archSource = arch;
            archLabel = "architecture.md";
        }
        else if (ProbeDir(artifactsPath, "architecture-distillate") is { } archDir)
        {
            archSource = archDir;
            archLabel = "architecture-distillate/";
        }
        else
        {
            // Most-recent implementation-readiness-report-*.md
            var readinessReports = Directory.GetFiles(artifactsPath, "implementation-readiness-report-*.md")
                .OrderByDescending(VersionSortKey)
                .ToArray();
            if (readinessReports.Length > 0)
            {
                archSource = readinessReports[0];
                archLabel = Path.GetFileName(readinessReports[0]);
            }
        }

        // ── UX spec ───────────────────────────────────────────────────────────
        string? uxSource = Probe(artifactsPath, "ux-design-specification.md")
            ?? ProbeDir(artifactsPath, "ux-design-specification-distillate");

        // ── All markdown files (for agent reference context) ──────────────────
        var allMd = Directory.GetFiles(artifactsPath, "*.md")
            .OrderBy(f => f)
            .ToList()
            .AsReadOnly();

        return new PlanningArtifacts(epicsMd, prdSource, prdLabel, archSource, archLabel, uxSource, allMd);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static string? Probe(string dir, string fileName)
    {
        var path = Path.Combine(dir, fileName);
        return File.Exists(path) ? path : null;
    }

    private static string? ProbeDir(string dir, string subDir)
    {
        var path = Path.Combine(dir, subDir);
        return Directory.Exists(path) ? path : null;
    }

    /// <summary>
    /// Produces a sort key that treats <c>-v&lt;N&gt;</c> version suffixes as higher
    /// than the unversioned base name.  E.g.:
    ///   report-2026-04-14-v2.md  →  report-2026-04-14.v0002.md
    ///   report-2026-04-14.md     →  report-2026-04-14.v0000.md
    /// Descending sort on this key gives the highest-versioned (newest) file first.
    /// </summary>
    private static string VersionSortKey(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        var dir  = Path.GetDirectoryName(filePath) ?? "";
        var ext  = Path.GetExtension(filePath);

        // Match optional -v<N> suffix at the end of the stem
        var match = Regex.Match(name, @"^(.*?)(?:-v(\d+))?$");
        var stem    = match.Groups[1].Value;
        var version = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0;

        // Zero-pad version so lexicographic order == numeric order
        return Path.Combine(dir, $"{stem}.v{version:D4}{ext}");
    }
}
