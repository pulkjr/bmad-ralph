namespace RalphLoop.Config;

/// <summary>
/// Applies --skip-* CLI flags to a <see cref="RalphLoopConfig"/> instance.
/// CLI flags take precedence over JSON config values; they are applied after loading.
/// </summary>
public static class CliPhaseFlags
{
    /// <summary>
    /// Scans <paramref name="args"/> for recognised --skip-* flags and sets the
    /// corresponding <c>Phases.*</c> property to <see langword="false"/>.
    /// Unknown flags are silently ignored (they may be handled elsewhere).
    /// </summary>
    public static void Apply(IEnumerable<string> args, RalphLoopConfig config)
    {
        foreach (var arg in args)
        {
            switch (arg)
            {
                case "--skip-readiness-gate":
                    config.Phases.SprintReview.ImplementationReadiness = false;
                    break;
                case "--skip-code-quality":
                    config.Phases.CodeQualityGate.Enabled = false;
                    break;
                case "--skip-performance-pedant":
                    config.Phases.CodeQualityGate.PerformancePedant = false;
                    break;
                case "--skip-legacy-librarian":
                    config.Phases.CodeQualityGate.LegacyLibrarian = false;
                    break;
                case "--skip-test-archaeologist":
                    config.Phases.CodeQualityGate.TestArchaeologist = false;
                    break;
                case "--skip-coverage-critic":
                    config.Phases.CodeQualityGate.CoverageCritic = false;
                    break;
                case "--skip-security-review":
                    config.Phases.EpicCompletion.Security = false;
                    break;
                case "--skip-architect-review":
                    config.Phases.EpicCompletion.Architect = false;
                    break;
                case "--skip-pm-review":
                    config.Phases.EpicCompletion.ProductManager = false;
                    break;
                case "--skip-ux-review":
                    config.Phases.EpicCompletion.UxDesigner = false;
                    break;
            }
        }
    }
}
