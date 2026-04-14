using GitHub.Copilot.SDK;
using RalphLoop.UI;

namespace RalphLoop.Config;

/// <summary>
/// Validates configured model IDs against the models the user actually has access to
/// and substitutes any unavailable model with the best available 1x (non-opus) alternative.
///
/// Also enforces that the QA agent and Developer agent never use the same model, since
/// they serve opposing roles (implementation vs. verification) and diversity improves outcomes.
/// </summary>
public static class ModelResolver
{
    // Policy state strings that mean the model cannot be used.
    private static readonly HashSet<string> DeniedStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "denied",
        "disabled",
        "blocked",
        "denied-by-content-exclusion-policy",
    };

    /// <summary>
    /// Resolves all models in <paramref name="models"/> in-place.
    /// Logs a warning via <paramref name="ui"/> for every substitution made.
    /// </summary>
    public static async Task ResolveAsync(
        CopilotClient client,
        ModelsConfig models,
        ConsoleUI ui,
        CancellationToken ct = default)
    {
        var available = await client.ListModelsAsync(ct);

        // Build the set of usable model IDs.
        var usable = available
            .Where(m => m.Policy is null || !DeniedStates.Contains(m.Policy.State))
            .ToList();

        if (usable.Count == 0)
            throw new InvalidOperationException(
                "No models are available on this Copilot subscription. " +
                "Check your GitHub Copilot access and try again.");

        var usableIds = usable.Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Resolve each role's model, collecting the fields and their role labels.
        models.Default = Resolve(models.Default, "Default (Scrum Master)", usable, usableIds, ui);
        models.Developer = Resolve(models.Developer, "Developer", usable, usableIds, ui);
        models.Architect = Resolve(models.Architect, "Architect", usable, usableIds, ui);
        models.ProductManager = Resolve(models.ProductManager, "Product Manager", usable, usableIds, ui);
        models.Qa = Resolve(models.Qa, "QA", usable, usableIds, ui);
        models.Security = Resolve(models.Security, "Security", usable, usableIds, ui);
        models.TechWriter = Resolve(models.TechWriter, "Tech Writer", usable, usableIds, ui);
        models.UxDesigner = Resolve(models.UxDesigner, "UX Designer", usable, usableIds, ui);
        models.PartyMode = Resolve(models.PartyMode, "Party Mode", usable, usableIds, ui);

        // Enforce QA ≠ Developer — diverse models produce more reliable acceptance reviews.
        if (models.Qa.Equals(models.Developer, StringComparison.OrdinalIgnoreCase))
        {
            var alternate = BestFallback(usable, preferSameProvider: null, exclude: models.Developer);
            if (alternate is not null)
            {
                ui.ShowWarning(
                    $"QA and Developer resolved to the same model ('{models.Qa}'). " +
                    $"Assigning '{alternate}' to QA for independent verification.");
                models.Qa = alternate;
            }
        }
    }

    private static string Resolve(
        string configured,
        string roleLabel,
        List<ModelInfo> usable,
        HashSet<string> usableIds,
        ConsoleUI ui)
    {
        if (usableIds.Contains(configured))
            return configured;

        // Try to pick a fallback that:
        //   1. Is 1x (Billing.Multiplier <= 1.5 or Billing is null — standard pricing)
        //   2. Prefers the same provider prefix (gpt-* or claude-*)
        var fallback = BestFallback(usable, preferSameProvider: ProviderPrefix(configured), exclude: null);
        fallback ??= usable[0].Id; // last resort: anything usable

        ui.ShowWarning($"Model '{configured}' is not available — using '{fallback}' for [{roleLabel}].");
        return fallback;
    }

    private static string? BestFallback(
        List<ModelInfo> usable,
        string? preferSameProvider,
        string? exclude)
    {
        var candidates = usable
            .Where(m => exclude is null || !m.Id.Equals(exclude, StringComparison.OrdinalIgnoreCase))
            .Where(m => m.Billing is null || m.Billing.Multiplier <= 1.5)
            .ToList();

        if (candidates.Count == 0)
        {
            // No 1x model available; fall back to anything that isn't excluded.
            candidates = usable
                .Where(m => exclude is null || !m.Id.Equals(exclude, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (candidates.Count == 0)
            return null;

        // Prefer same provider (gpt- → gpt-, claude- → claude-).
        if (preferSameProvider is not null)
        {
            var sameProvider = candidates.FirstOrDefault(
                m => m.Id.StartsWith(preferSameProvider, StringComparison.OrdinalIgnoreCase));
            if (sameProvider is not null)
                return sameProvider.Id;
        }

        return candidates[0].Id;
    }

    private static string? ProviderPrefix(string modelId)
    {
        if (modelId.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase)) return "gpt-";
        if (modelId.StartsWith("claude-", StringComparison.OrdinalIgnoreCase)) return "claude-";
        if (modelId.StartsWith("o", StringComparison.OrdinalIgnoreCase)) return "o";
        return null;
    }
}
