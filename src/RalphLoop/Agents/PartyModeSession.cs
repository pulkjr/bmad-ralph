using GitHub.Copilot.SDK;
using RalphLoop.Config;
using RalphLoop.UI;

namespace RalphLoop.Agents;

/// <summary>
/// Orchestrates a BMAD party-mode session with multiple agent personas.
/// Returns the consensus outcome from the session.
/// </summary>
public class PartyModeSession(CopilotClient client, SessionFactory factory, ConsoleUI ui)
{
    /// <summary>
    /// Run a party-mode session with the given personas and prompt.
    /// Pauses on any OnUserInputRequest and resumes after the user responds.
    /// Returns the final assistant response.
    /// </summary>
    public async Task<AgentResult> RunAsync(
        IReadOnlyList<CustomAgentConfig> personas,
        string prompt,
        string sessionLabel = "Party Mode",
        CancellationToken ct = default)
    {
        ui.ShowSection($"🎉 {sessionLabel}");

        var config = factory.ForPartyMode(
            personas,
            AgentRunner.ApproveAll(),
            async (req, _) =>
            {
                var answer = await ui.WaitForUserInputAsync(req.Question ?? "Team needs your input:");
                return new UserInputResponse { Answer = answer, WasFreeform = true };
            });

        ui.ShowPartyRoster(personas, config.Model);

        var runner = new AgentRunner(client, ui);
        return await runner.RunAsync(config, prompt, sessionLabel, ct);
    }
}
