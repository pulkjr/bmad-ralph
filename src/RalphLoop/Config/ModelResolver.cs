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
    /// <summary>
    /// Resolves all models in <paramref name="models"/> in-place.
    /// Logs a warning via <paramref name="ui"/> for every substitution made.
    /// When <paramref name="askModelChoice"/> is provided, unavailable models trigger an
    /// interactive selection prompt instead of silent auto-substitution.
    /// </summary>
    public static async Task ResolveAsync(
        CopilotClient client,
        ModelsConfig models,
        ConsoleUI ui,
        CancellationToken ct = default,
        Func<string, List<string>, string, string>? askModelChoice = null
    )
    {
        IList<GitHub.Copilot.SDK.ModelInfo> available;
        try
        {
            available = await client.ListModelsAsync(ct);
        }
        catch (Exception ex) when (ex is System.IO.IOException or InvalidOperationException)
        {
            CopilotErrorHandler.Rethrow(ex);
            throw; // unreachable — satisfies the compiler
        }

        // Allowlist approach: a model is usable only when its policy is absent (no
        // restrictions) or explicitly "enabled". This is safer than blocklisting known
        // denial strings — any undocumented or future state is treated as unavailable.
        var usable = available.Where(IsUsable).ToList();

        if (usable.Count == 0)
            throw new InvalidOperationException(
                "No models are available on this Copilot subscription. "
                    + "Check your GitHub Copilot access and try again."
            );

        var usableIds = usable.Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Resolve each role's model.
        models.Default = Resolve(
            models.Default,
            "Default (Scrum Master)",
            usable,
            usableIds,
            ui,
            askModelChoice
        );
        models.Developer = Resolve(
            models.Developer,
            "Developer",
            usable,
            usableIds,
            ui,
            askModelChoice
        );
        models.Architect = Resolve(
            models.Architect,
            "Architect",
            usable,
            usableIds,
            ui,
            askModelChoice
        );
        models.ProductManager = Resolve(
            models.ProductManager,
            "Product Manager",
            usable,
            usableIds,
            ui,
            askModelChoice
        );
        models.Qa = Resolve(models.Qa, "QA", usable, usableIds, ui, askModelChoice);
        models.Security = Resolve(
            models.Security,
            "Security",
            usable,
            usableIds,
            ui,
            askModelChoice
        );
        models.TechWriter = Resolve(
            models.TechWriter,
            "Tech Writer",
            usable,
            usableIds,
            ui,
            askModelChoice
        );
        models.UxDesigner = Resolve(
            models.UxDesigner,
            "UX Designer",
            usable,
            usableIds,
            ui,
            askModelChoice
        );
        models.PartyMode = Resolve(
            models.PartyMode,
            "Party Mode",
            usable,
            usableIds,
            ui,
            askModelChoice
        );

        // Enforce QA ≠ Developer — diverse models produce more reliable acceptance reviews.
        if (models.Qa.Equals(models.Developer, StringComparison.OrdinalIgnoreCase))
        {
            var alternate = BestFallback(
                usable,
                preferSameProvider: null,
                exclude: models.Developer
            );
            if (alternate is not null)
            {
                ui.ShowWarning(
                    $"QA and Developer resolved to the same model ('{models.Qa}'). "
                        + $"Assigning '{alternate}' to QA for independent verification."
                );
                models.Qa = alternate;
            }
        }
    }

    /// <summary>
    /// A model is usable when it has no policy (unrestricted) or its policy state is
    /// explicitly "enabled". Any other state — including empty string, "disabled",
    /// "denied", or any future state — is treated as unavailable.
    /// </summary>
    private static bool IsUsable(ModelInfo m) =>
        m.Policy is null || m.Policy.State.Equals("enabled", StringComparison.OrdinalIgnoreCase);

    private static string Resolve(
        string configured,
        string roleLabel,
        List<ModelInfo> usable,
        HashSet<string> usableIds,
        ConsoleUI ui,
        Func<string, List<string>, string, string>? askModelChoice
    )
    {
        // Case-insensitive match — return the correctly-cased ID from the available list
        // to prevent the SDK from rejecting a model due to casing differences (e.g. "GPT-5.4" vs "gpt-5.4").
        var match = usable.FirstOrDefault(m =>
            m.Id.Equals(configured, StringComparison.OrdinalIgnoreCase)
        );
        if (match is not null)
            return match.Id;

        var fallback =
            BestFallback(usable, preferSameProvider: ProviderPrefix(configured), exclude: null)
            ?? usable[0].Id;

        ui.ShowWarning($"Model '{configured}' is not available for [{roleLabel}].");

        if (askModelChoice is not null)
        {
            var allIds = usable.Select(m => m.Id).ToList();
            return askModelChoice(
                $"Select a replacement model for [{roleLabel}]:",
                allIds,
                fallback
            );
        }

        ui.ShowWarning($"Using '{fallback}' for [{roleLabel}].");
        return fallback;
    }

    private static string? BestFallback(
        List<ModelInfo> usable,
        string? preferSameProvider,
        string? exclude
    )
    {
        var candidates = usable
            .Where(m =>
                exclude is null || !m.Id.Equals(exclude, StringComparison.OrdinalIgnoreCase)
            )
            .Where(m => m.Billing is null || m.Billing.Multiplier <= 1.5)
            .ToList();

        if (candidates.Count == 0)
        {
            // No 1x model available; fall back to anything that isn't excluded.
            candidates = usable
                .Where(m =>
                    exclude is null || !m.Id.Equals(exclude, StringComparison.OrdinalIgnoreCase)
                )
                .ToList();
        }

        if (candidates.Count == 0)
            return null;

        // Prefer same provider (gpt- → gpt-, claude- → claude-).
        if (preferSameProvider is not null)
        {
            var sameProvider = candidates.FirstOrDefault(m =>
                m.Id.StartsWith(preferSameProvider, StringComparison.OrdinalIgnoreCase)
            );
            if (sameProvider is not null)
                return sameProvider.Id;
        }

        return candidates[0].Id;
    }

    private static string? ProviderPrefix(string modelId)
    {
        if (modelId.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase))
            return "gpt-";
        if (modelId.StartsWith("claude-", StringComparison.OrdinalIgnoreCase))
            return "claude-";
        if (modelId.StartsWith("o", StringComparison.OrdinalIgnoreCase))
            return "o";
        return null;
    }
}
