using GitHub.Copilot.SDK;
using RalphLoop.Config;
using RalphLoop.UI;
using Xunit;

namespace RalphLoop.Tests.Config;

/// <summary>
/// Tests for <see cref="ModelResolver"/>.
///
/// Uses <see cref="CopilotClientOptions.OnListModels"/> to inject a fake model list
/// so tests run without a real Copilot CLI process.
/// </summary>
public class ModelResolverTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Creates a CopilotClient whose ListModelsAsync returns the given list.</summary>
    private static CopilotClient ClientWith(params ModelInfo[] models)
    {
        var list = models.ToList();
        return new CopilotClient(
            new CopilotClientOptions
            {
                AutoStart = false,
                OnListModels = _ => Task.FromResult(list),
            }
        );
    }

    private static ModelInfo Model(
        string id,
        string? policyState = null,
        double? multiplier = null
    ) =>
        new()
        {
            Id = id,
            Name = id,
            Policy = policyState is null ? null : new() { State = policyState },
            Billing = multiplier is null ? null : new() { Multiplier = multiplier.Value },
        };

    private static ModelsConfig AllSetTo(string modelId) =>
        new()
        {
            Default = modelId,
            Developer = modelId,
            Architect = modelId,
            ProductManager = modelId,
            Qa = modelId,
            Security = modelId,
            TechWriter = modelId,
            UxDesigner = modelId,
            PartyMode = modelId,
        };

    private static ConsoleUI SilentUi() => new();

    // ── Policy allowlist (the denylist-vs-allowlist bug) ──────────────────────

    [Fact]
    public async Task Model_WithNullPolicy_IsKept()
    {
        // A model with no Policy object at all should be treated as unrestricted.
        var client = ClientWith(Model("gpt-5.1", policyState: null));
        var models = AllSetTo("gpt-5.1");

        await ModelResolver.ResolveAsync(client, models, SilentUi());

        Assert.Equal("gpt-5.1", models.Developer);
    }

    [Fact]
    public async Task Model_WithEnabledPolicy_IsKept()
    {
        // Explicitly "enabled" is the only named "good" state in the SDK docs.
        var client = ClientWith(Model("gpt-5.1", policyState: "enabled"));
        var models = AllSetTo("gpt-5.1");

        await ModelResolver.ResolveAsync(client, models, SilentUi());

        Assert.Equal("gpt-5.1", models.Developer);
    }

    [Fact]
    public async Task Model_WithDisabledPolicy_IsSubstituted()
    {
        // "disabled" must be treated as unavailable.
        var client = ClientWith(
            Model("gpt-5", policyState: "disabled"),
            Model("gpt-5.1", policyState: "enabled")
        );
        var models = AllSetTo("gpt-5");

        await ModelResolver.ResolveAsync(client, models, SilentUi());

        Assert.Equal("gpt-5.1", models.Developer);
    }

    [Fact]
    public async Task Model_WithEmptyPolicyState_IsSubstituted()
    {
        // Empty string is the C# default for string properties — it is NOT "enabled".
        // This was the root cause of the original crash: our old denylist did not block "".
        var client = ClientWith(
            Model("gpt-5", policyState: ""),
            Model("gpt-5.1", policyState: null)
        );
        var models = AllSetTo("gpt-5");

        await ModelResolver.ResolveAsync(client, models, SilentUi());

        Assert.Equal("gpt-5.1", models.Developer);
    }

    [Fact]
    public async Task Model_WithUnknownPolicyState_IsSubstituted()
    {
        // Any future/unknown state (e.g. "pending-approval", "requires-terms")
        // must NOT slip through. Safe-by-default: only "enabled" or null is allowed.
        var client = ClientWith(
            Model("gpt-5", policyState: "pending-approval"),
            Model("gpt-5.1", policyState: "enabled")
        );
        var models = AllSetTo("gpt-5");

        await ModelResolver.ResolveAsync(client, models, SilentUi());

        Assert.Equal("gpt-5.1", models.Developer);
    }

    [Fact]
    public async Task Model_NotReturnedByListModels_IsSubstituted()
    {
        // If the model doesn't appear in ListModelsAsync at all it can't be used.
        var client = ClientWith(Model("gpt-5.1", policyState: "enabled"));
        var models = AllSetTo("gpt-5"); // "gpt-5" is not in the list

        await ModelResolver.ResolveAsync(client, models, SilentUi());

        Assert.Equal("gpt-5.1", models.Developer);
    }

    // ── Fallback selection ────────────────────────────────────────────────────

    [Fact]
    public async Task Fallback_Prefers1xModel_OverOpusModel()
    {
        // When both a 1x and a 3x model are available, the fallback must pick 1x.
        var client = ClientWith(
            Model("configured-model", policyState: "disabled"),
            Model("opus-model", policyState: "enabled", multiplier: 3.0),
            Model("standard-model", policyState: "enabled", multiplier: 1.0)
        );
        var models = AllSetTo("configured-model");

        await ModelResolver.ResolveAsync(client, models, SilentUi());

        Assert.Equal("standard-model", models.Developer);
    }

    [Fact]
    public async Task Fallback_PrefersSameProviderPrefix_ForGptModel()
    {
        // A gpt-* model should fall back to another gpt-* model first.
        var client = ClientWith(
            Model("gpt-5", policyState: "disabled"),
            Model("claude-sonnet-4.6", policyState: "enabled", multiplier: 1.0),
            Model("gpt-5.1", policyState: "enabled", multiplier: 1.0)
        );
        var models = new ModelsConfig { Developer = "gpt-5" };

        await ModelResolver.ResolveAsync(client, models, SilentUi());

        Assert.Equal("gpt-5.1", models.Developer);
    }

    [Fact]
    public async Task Fallback_PrefersSameProviderPrefix_ForClaudeModel()
    {
        // A claude-* model should fall back to another claude-* model first.
        var client = ClientWith(
            Model("claude-sonnet-4.6", policyState: "disabled"),
            Model("gpt-5.1", policyState: "enabled", multiplier: 1.0),
            Model("claude-sonnet-4.5", policyState: "enabled", multiplier: 1.0)
        );
        var models = new ModelsConfig { Qa = "claude-sonnet-4.6" };

        await ModelResolver.ResolveAsync(client, models, SilentUi());

        Assert.Equal("claude-sonnet-4.5", models.Qa);
    }

    [Fact]
    public async Task Fallback_UsesAnyAvailable1x_WhenNoSameProviderExists()
    {
        // When no same-provider fallback is available, any 1x model is acceptable.
        var client = ClientWith(
            Model("gpt-5", policyState: "disabled"),
            Model("claude-sonnet-4.6", policyState: "enabled", multiplier: 1.0)
        );
        var models = new ModelsConfig { Security = "gpt-5" };

        await ModelResolver.ResolveAsync(client, models, SilentUi());

        Assert.Equal("claude-sonnet-4.6", models.Security);
    }

    [Fact]
    public async Task Fallback_UsesOpusModel_WhenNo1xModelExists()
    {
        // Last resort: if only opus (3x) models are available, use them rather than failing.
        var client = ClientWith(
            Model("unavailable-model", policyState: "disabled"),
            Model("claude-opus", policyState: "enabled", multiplier: 3.0)
        );
        var models = new ModelsConfig { Architect = "unavailable-model" };

        await ModelResolver.ResolveAsync(client, models, SilentUi());

        Assert.Equal("claude-opus", models.Architect);
    }

    // ── QA ≠ Developer enforcement ────────────────────────────────────────────

    [Fact]
    public async Task QaAndDeveloper_RemainUnchanged_WhenAlreadyDifferent()
    {
        var client = ClientWith(
            Model("gpt-5.3-codex", policyState: "enabled"),
            Model("claude-sonnet-4.6", policyState: "enabled")
        );
        var models = new ModelsConfig { Developer = "gpt-5.3-codex", Qa = "claude-sonnet-4.6" };

        await ModelResolver.ResolveAsync(client, models, SilentUi());

        Assert.Equal("gpt-5.3-codex", models.Developer);
        Assert.Equal("claude-sonnet-4.6", models.Qa);
    }

    [Fact]
    public async Task QaAndDeveloper_AreAssignedDifferentModels_WhenBothResolveToSame()
    {
        // If only one model survives resolution, QA should still differ from Developer.
        // We provide two models; one is available for QA diversification.
        var client = ClientWith(
            Model("model-a", policyState: "enabled"),
            Model("model-b", policyState: "enabled")
        );
        var models = new ModelsConfig
        {
            Developer = "model-a",
            Qa = "model-a", // same as Developer
        };

        await ModelResolver.ResolveAsync(client, models, SilentUi());

        Assert.Equal("model-a", models.Developer);
        Assert.NotEqual(models.Developer, models.Qa);
    }

    [Fact]
    public async Task QaAndDeveloper_BothHaveValidModels_WhenDiversificationIsImpossible()
    {
        // If only one model is available at all, we keep it for both rather than failing.
        // The warning is emitted but the run must not be blocked.
        var client = ClientWith(Model("only-model", policyState: "enabled"));
        var models = new ModelsConfig { Developer = "only-model", Qa = "only-model" };

        // Should not throw — best-effort diversification
        await ModelResolver.ResolveAsync(client, models, SilentUi());

        Assert.Equal("only-model", models.Developer);
        Assert.Equal("only-model", models.Qa);
    }

    // ── Error case ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Throws_InvalidOperationException_WhenNoModelsAreUsable()
    {
        // All models denied → clear error, not a cryptic session.create failure.
        var client = ClientWith(
            Model("model-a", policyState: "disabled"),
            Model("model-b", policyState: "denied")
        );
        var models = AllSetTo("model-a");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ModelResolver.ResolveAsync(client, models, SilentUi())
        );

        Assert.Contains("No models are available", ex.Message);
    }

    // ── SDK error handling ────────────────────────────────────────────────────

    [Fact]
    public async Task ListModels_NotAuthenticated_ThrowsFriendlyError()
    {
        // When the user is not signed in, the SDK throws an IOException whose message
        // contains "Not authenticated. Please authenticate first."
        // ModelResolver must convert this into a user-friendly InvalidOperationException
        // that tells the user to run 'gh auth login'.
        var client = new CopilotClient(
            new CopilotClientOptions
            {
                AutoStart = false,
                OnListModels = _ =>
                    throw new System.IO.IOException(
                        "Communication error with Copilot CLI: Request models.list failed "
                            + "with message: Not authenticated. Please authenticate first."
                    ),
            }
        );
        var models = AllSetTo("gpt-5");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ModelResolver.ResolveAsync(client, models, SilentUi())
        );

        Assert.Contains("gh auth login", ex.Message);
    }

    [Fact]
    public async Task ListModels_CliProcessDied_ThrowsFriendlyError()
    {
        // When the CLI process exits unexpectedly, the SDK wraps the failure in an
        // IOException. ModelResolver must surface this as an actionable message.
        var client = new CopilotClient(
            new CopilotClientOptions
            {
                AutoStart = false,
                OnListModels = _ =>
                    throw new System.IO.IOException(
                        "CLI process exited unexpectedly.\nstderr: something went wrong"
                    ),
            }
        );
        var models = AllSetTo("gpt-5");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ModelResolver.ResolveAsync(client, models, SilentUi())
        );

        Assert.Contains("exited unexpectedly", ex.Message);
    }

    [Fact]
    public void CopilotErrorHandler_CliNotFound_ProducesFriendlyError()
    {
        // When the CLI binary cannot be located, StartAsync throws an
        // InvalidOperationException with "Copilot CLI not found".
        // CopilotErrorHandler must convert this to an actionable message.
        var inner = new InvalidOperationException(
            "Copilot CLI not found at '/home/dev/.nuget/...'. "
                + "Ensure the SDK NuGet package was restored correctly or provide an explicit CliPath."
        );

        var ex = Assert.Throws<InvalidOperationException>(() => CopilotErrorHandler.Rethrow(inner));

        Assert.Contains("gh extension install", ex.Message);
    }

    [Fact]
    public void CopilotErrorHandler_UnknownIoException_IncludesOriginalMessage()
    {
        // Unrecognised IOExceptions should still produce an InvalidOperationException
        // (not a raw IOException) so Program.cs's existing catch block handles them.
        var inner = new System.IO.IOException("some unknown transport error");

        var ex = Assert.Throws<InvalidOperationException>(() => CopilotErrorHandler.Rethrow(inner));

        Assert.Contains("some unknown transport error", ex.Message);
    }

    // ── All-available happy path ───────────────────────────────────────────────

    [Fact]
    public async Task AllRoles_KeepConfiguredModels_WhenEverythingIsAvailable()
    {
        // When all configured models are available and enabled, nothing is substituted.
        var client = ClientWith(
            Model("gpt-5.3-codex", policyState: "enabled"),
            Model("claude-sonnet-4.6", policyState: "enabled"),
            Model("claude-sonnet-4.5", policyState: "enabled"),
            Model("gpt-5", policyState: "enabled"),
            Model("gpt-5.1", policyState: "enabled")
        );

        var models = new ModelsConfig
        {
            Default = "gpt-5",
            Developer = "gpt-5.3-codex",
            Architect = "claude-sonnet-4.6",
            ProductManager = "claude-sonnet-4.6",
            Qa = "claude-sonnet-4.6",
            Security = "gpt-5",
            TechWriter = "claude-sonnet-4.5",
            UxDesigner = "claude-sonnet-4.5",
            PartyMode = "claude-sonnet-4.6",
        };

        await ModelResolver.ResolveAsync(client, models, SilentUi());

        // Developer and Qa are already different — no diversification needed.
        Assert.Equal("gpt-5.3-codex", models.Developer);
        Assert.Equal("claude-sonnet-4.6", models.Qa);
        Assert.Equal("gpt-5", models.Security);
        Assert.Equal("claude-sonnet-4.5", models.TechWriter);
    }
}
