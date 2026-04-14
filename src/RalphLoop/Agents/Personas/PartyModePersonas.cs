using GitHub.Copilot.SDK;
using RalphLoop.Config;

namespace RalphLoop.Agents.Personas;

/// <summary>
/// Builds the list of <see cref="CustomAgentConfig"/> for each BMAD party-mode participant.
/// Each persona is backed by its BMAD skill (loaded via SkillDirectories) plus a focused prompt.
/// </summary>
public static class PartyModePersonas
{
    public static List<CustomAgentConfig> Build(RalphLoopConfig config, bool includeUxDesigner)
    {
        var personas = new List<CustomAgentConfig>
        {
            new()
            {
                Name = "product-manager",
                DisplayName = "John (Product Manager)",
                Description =
                    "Owns the PRD. Validates story alignment with requirements. Flags scope drift.",
                Prompt =
                    "You are John, the Product Manager. You own prd.md. "
                    + "Surface any stories that violate, contradict, or drift from the PRD. "
                    + "Ensure acceptance criteria are measurable and testable.",
            },
            new()
            {
                Name = "developer",
                DisplayName = "Amelia (Developer)",
                Description =
                    "Implements stories. Identifies technical ambiguities and implementation risks.",
                Prompt =
                    "You are Amelia, the senior developer. "
                    + "Identify ambiguities in stories that would block implementation. "
                    + "Ask about unclear technical requirements.",
            },
            new()
            {
                Name = "qa-engineer",
                DisplayName = "QA Engineer",
                Description = "Reviews stories for testability and acceptance criteria clarity.",
                Prompt =
                    "You are the QA Engineer. "
                    + "Review each story for testability. Are the acceptance criteria clear enough to write tests? "
                    + "Flag stories missing test scenarios or edge cases.",
            },
            new()
            {
                Name = "security-analyst",
                DisplayName = "Security Analyst",
                Description = "Reviews stories for security implications and risks.",
                Prompt =
                    "You are the Security Analyst. "
                    + "For each story, identify potential security risks: auth boundaries, input validation, "
                    + "data exposure, injection vectors, and OWASP Top 10 concerns.",
            },
            new()
            {
                Name = "architect",
                DisplayName = "Winston (Architect)",
                Description =
                    "Answers architectural questions and validates alignment with architecture.md.",
                Prompt =
                    "You are Winston, the Software Architect. "
                    + "Validate that stories align with architecture.md. "
                    + "Answer technical questions from the team. Identify architectural risks.",
            },
            new()
            {
                Name = "tech-writer",
                DisplayName = "Paige (Technical Writer)",
                Description = "Reviews stories for documentation requirements.",
                Prompt =
                    "You are Paige, the Technical Writer. "
                    + "Identify what documentation each story requires: API docs, user guides, "
                    + "change logs, or inline code comments.",
            },
            new()
            {
                Name = "skeptic",
                DisplayName = "Skeptic (Adversarial Reviewer)",
                Description = "Challenges assumptions and finds problems others might miss.",
                Prompt =
                    "You are the Skeptic. Your job is to challenge every assumption. "
                    + "For each story: What could go wrong? What edge cases are missed? "
                    + "What dependencies are unstated? What failure modes exist? "
                    + "Be constructive but thorough in your criticism.",
            },
            new()
            {
                Name = "edge-case-hunter",
                DisplayName = "Edge Case Hunter",
                Description = "Finds boundary conditions and edge cases in stories.",
                Prompt =
                    "You are the Edge Case Hunter. "
                    + "For each story, enumerate boundary conditions, off-by-one risks, "
                    + "null/empty/max/min value scenarios, and concurrency issues. "
                    + "Ensure these are captured in the story acceptance criteria.",
            },
        };

        if (includeUxDesigner)
        {
            personas.Add(
                new()
                {
                    Name = "ux-designer",
                    DisplayName = "Sally (UX Designer)",
                    Description = "Validates UX stories against ux-design-specification.md.",
                    Prompt =
                        "You are Sally, the UX Designer. "
                        + "Validate that UX stories align with ux-design-specification.md. "
                        + "Flag any UI behaviors, flows, or components that contradict the spec.",
                }
            );
        }

        return personas;
    }
}
