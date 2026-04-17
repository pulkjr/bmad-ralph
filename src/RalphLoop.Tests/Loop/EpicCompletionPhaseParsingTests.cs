using RalphLoop.Loop.Phases;
using Xunit;

namespace RalphLoop.Tests.Loop;

/// <summary>
/// Tests for <see cref="EpicCompletionPhase"/> static verdict/consensus parsing helpers.
///
/// Covers the regression where swarm reviewers emit VERDICT: RESOLVED (not VERDICT: PASS),
/// causing <c>IsAllPassed</c> to return false and triggering a spurious force-proceed prompt
/// even when all issues are genuinely resolved.
/// </summary>
public class EpicCompletionPhaseParsingTests
{
    // ── IsAllPassed ───────────────────────────────────────────────────────────

    [Fact]
    public void IsAllPassed_VerdictPass_ReturnsTrue()
    {
        var response = """
            All findings reviewed. No issues remaining.
            VERDICT: PASS
            """;

        Assert.True(EpicCompletionPhase.IsAllPassed(response));
    }

    [Fact]
    public void IsAllPassed_VerdictPassWithDetail_ReturnsTrue()
    {
        var response = "VERDICT: PASS — all security checks green";

        Assert.True(EpicCompletionPhase.IsAllPassed(response));
    }

    [Fact]
    public void IsAllPassed_VerdictResolved_ReturnsTrue()
    {
        // Swarm reviewers emit VERDICT: RESOLVED, not VERDICT: PASS.
        // This was the regression: RESOLVED was not accepted as a passing state.
        var response = """
            F35 addressed. F36 addressed.
            VERDICT: RESOLVED — Architect
            """;

        Assert.True(EpicCompletionPhase.IsAllPassed(response));
    }

    [Fact]
    public void IsAllPassed_VerdictResolvedWithDash_ReturnsTrue()
    {
        var response = "VERDICT: RESOLVED — Security";

        Assert.True(EpicCompletionPhase.IsAllPassed(response));
    }

    [Fact]
    public void IsAllPassed_VerdictFail_ReturnsFalse()
    {
        var response = """
            Finding 1: PASS
            Finding 2: FAIL — hardcoded secret detected
            VERDICT: FAIL — one high-severity issue
            """;

        Assert.False(EpicCompletionPhase.IsAllPassed(response));
    }

    [Fact]
    public void IsAllPassed_VerdictUnresolved_ReturnsFalse()
    {
        var response = "VERDICT: UNRESOLVED — missing migration script";

        Assert.False(EpicCompletionPhase.IsAllPassed(response));
    }

    [Fact]
    public void IsAllPassed_NoVerdictLineNoFailWord_ReturnsTrue()
    {
        var response = "All findings reviewed. Everything looks good. No issues found.";

        Assert.True(EpicCompletionPhase.IsAllPassed(response));
    }

    [Fact]
    public void IsAllPassed_NoVerdictLineContainsFailWord_ReturnsFalse()
    {
        var response = "The authentication flow will FAIL under heavy load.";

        Assert.False(EpicCompletionPhase.IsAllPassed(response));
    }

    [Fact]
    public void IsAllPassed_NoVerdictLineWordFailInLongerWord_ReturnsTrue()
    {
        // \bFAIL\b is a whole-word match: "failures" does NOT match because
        // word-boundary after "l" in "fail" would require a non-word character next.
        var response = "No failures detected in test output. No violations found.";

        Assert.True(EpicCompletionPhase.IsAllPassed(response));
    }

    [Fact]
    public void IsAllPassed_VerdictResolvedCaseInsensitive_ReturnsTrue()
    {
        var response = "VERDICT: resolved — pm";

        Assert.True(EpicCompletionPhase.IsAllPassed(response));
    }

    // ── AllApproved ──────────────────────────────────────────────────────────

    [Fact]
    public void AllApproved_ConsensusUnanimous_ReturnsTrue()
    {
        var response = """
            Agent 1: APPROVED — implementation complete
            Agent 2: APPROVED — security clean
            CONSENSUS: UNANIMOUS — all agents approved
            """;

        Assert.True(EpicCompletionPhase.AllApproved(response));
    }

    [Fact]
    public void AllApproved_ConsensusNotReached_ReturnsFalse()
    {
        var response = """
            Agent 1: APPROVED — looks good
            Agent 2: CONCERNS: missing rollback plan
            CONSENSUS: NOT REACHED — Agent 2 has unresolved concerns
            """;

        Assert.False(EpicCompletionPhase.AllApproved(response));
    }

    [Fact]
    public void AllApproved_NoConsensusLineButUnanimousWord_ReturnsTrue()
    {
        var response = "The team reached unanimous agreement on all items.";

        Assert.True(EpicCompletionPhase.AllApproved(response));
    }

    [Fact]
    public void AllApproved_NoConsensusLineNoUnanimousWord_ReturnsFalse()
    {
        var response = "Agent 1 approved. Agent 2 has concerns about the schema.";

        Assert.False(EpicCompletionPhase.AllApproved(response));
    }
}
