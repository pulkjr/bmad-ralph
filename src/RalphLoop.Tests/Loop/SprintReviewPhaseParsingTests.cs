using RalphLoop.Loop.Phases;
using Xunit;

namespace RalphLoop.Tests.Loop;

/// <summary>
/// Tests for <see cref="SprintReviewPhase"/> confidence vote parsing.
///
/// Covers the confirmed regression where the Copilot SDK wraps agent speech in
/// markdown blockquotes (each VOTE: line starts with &gt; ), causing the original
/// regex to find zero matches and the &lt;minor-issues&gt; block to be empty.
/// </summary>
public class SprintReviewPhaseParsingTests
{
    // ── Plain VOTE: lines (existing format, must not regress) ─────────────────

    [Fact]
    public void ParseConfidenceVoteResult_PlainVoteLines_ParsesAllVotes()
    {
        var response = """
            VOTE: YES — Looks good
            VOTE: NO (MINOR) — Missing error handling | FIX: Add try-catch
            VOTE: NO (MAJOR) — Scope unclear

            CONFIDENCE: FAILED (MAJOR) — one major issue
            """;

        var result = SprintReviewPhase.ParseConfidenceVoteResult(response);

        Assert.Equal(1, result.YesCount);
        Assert.Equal(1, result.NoMinorCount);
        Assert.Single(result.MajorIssues);
        Assert.Equal(SprintReviewPhase.VoteOutcome.FailedMajor, result.Outcome);
    }

    // ── Blockquote-prefixed VOTE: lines (confirmed regression case) ───────────

    [Fact]
    public void ParseConfidenceVoteResult_BlockquoteVoteLines_ParsesAllVotes()
    {
        // This is the format the Copilot SDK actually produces — each agent
        // speech bubble is wrapped in a markdown blockquote.
        var response = """
            > VOTE: YES — All issues are minor; no scope changes needed.

            > VOTE: NO (MINOR) — Stories 1.1 and 1.6 have TBD package versions | FIX: Pin versions before sprint

            > VOTE: NO (MINOR) — Missing .gitattributes CRLF enforcement | FIX: Add *.cs text eol=crlf

            > VOTE: NO (MINOR) — AT10 is unenforceable | FIX: Rewrite to check CancellationToken parameter

            ```
            CONFIDENCE: FAILED (MINOR) — 3 minor issues, all resolvable before sprint start
            ```
            """;

        var result = SprintReviewPhase.ParseConfidenceVoteResult(response);

        Assert.Equal(1, result.YesCount);
        Assert.Equal(3, result.NoMinorCount);
        Assert.Empty(result.MajorIssues);
        Assert.Equal(SprintReviewPhase.VoteOutcome.FailedMinorOnly, result.Outcome);
    }

    [Fact]
    public void ParseConfidenceVoteResult_BlockquoteVoteLines_ExtractsDetailCorrectly()
    {
        const string response =
            "> VOTE: NO (MINOR) — Migration path conflict: AC says src/Migrations/, arch doc says schema/ | FIX: Adopt schema/ root path\n"
            + "```\nCONFIDENCE: FAILED (MINOR) — 1 minor issue\n```";

        var result = SprintReviewPhase.ParseConfidenceVoteResult(response);

        Assert.Single(result.Votes);
        Assert.False(result.Votes[0].IsYes);
        Assert.False(result.Votes[0].IsMajor);
        Assert.Contains("Migration path conflict", result.Votes[0].Detail);
    }

    [Fact]
    public void ParseConfidenceVoteResult_BlockquoteYesVote_IsYes()
    {
        const string response =
            "> VOTE: YES — Requirements are clear and complete.\n"
            + "```\nCONFIDENCE: PASSED (1 yes / 0 no)\n```";

        var result = SprintReviewPhase.ParseConfidenceVoteResult(response);

        Assert.Equal(1, result.YesCount);
        Assert.Equal(0, result.NoMinorCount);
        Assert.Equal(SprintReviewPhase.VoteOutcome.Passed, result.Outcome);
    }

    [Fact]
    public void ParseConfidenceVoteResult_BlockquoteMajorVote_ParsedAsMajor()
    {
        const string response =
            "> VOTE: NO (MAJOR) — Architecture decision requires product owner sign-off\n"
            + "```\nCONFIDENCE: FAILED (MAJOR) — 1 major issue\n```";

        var result = SprintReviewPhase.ParseConfidenceVoteResult(response);

        Assert.Single(result.MajorIssues);
        Assert.Equal(SprintReviewPhase.VoteOutcome.FailedMajor, result.Outcome);
    }

    // ── Mixed plain + blockquote lines ────────────────────────────────────────

    [Fact]
    public void ParseConfidenceVoteResult_MixedPlainAndBlockquoteVotes_ParsesBoth()
    {
        var response = """
            VOTE: YES — LGTM

            > VOTE: NO (MINOR) — Missing acceptance criteria | FIX: Add specific criteria

            ```
            CONFIDENCE: FAILED (MINOR) — 1 minor issue
            ```
            """;

        var result = SprintReviewPhase.ParseConfidenceVoteResult(response);

        Assert.Equal(1, result.YesCount);
        Assert.Equal(1, result.NoMinorCount);
        Assert.Equal(SprintReviewPhase.VoteOutcome.FailedMinorOnly, result.Outcome);
    }

    // ── CONFIDENCE: line without individual votes ─────────────────────────────

    [Fact]
    public void ParseConfidenceVoteResult_ConfidenceLineOnly_OutcomeFromLine()
    {
        // When the AI includes a CONFIDENCE: summary but no individual VOTE: lines,
        // the outcome should still be determined from the CONFIDENCE: line.
        var response = """
            After discussion, the team agreed on the following:
            - Minor issues identified with stories 1.3 and 1.5

            ```
            CONFIDENCE: FAILED (MINOR) — 2 minor issues, resolvable before sprint
            ```
            """;

        var result = SprintReviewPhase.ParseConfidenceVoteResult(response);

        Assert.Empty(result.Votes);
        Assert.Equal(SprintReviewPhase.VoteOutcome.FailedMinorOnly, result.Outcome);
    }

    [Fact]
    public void ParseConfidenceVoteResult_PassedConfidenceLine_OutcomeIsPassed()
    {
        var response = """
            All agents agreed the stories are well-defined.

            ```
            CONFIDENCE: PASSED (6 yes / 0 no)
            ```
            """;

        var result = SprintReviewPhase.ParseConfidenceVoteResult(response);

        Assert.Equal(SprintReviewPhase.VoteOutcome.Passed, result.Outcome);
    }

    // ── Tiebreaker in blockquote ──────────────────────────────────────────────

    [Fact]
    public void ParseConfidenceVoteResult_BlockquoteTiebreaker_ResolvesTied()
    {
        var response = """
            > VOTE: YES — LGTM
            > VOTE: NO (MINOR) — Missing edge case | FIX: Add null check

            > TIEBREAKER: YES — The fix is straightforward; proceed.

            ```
            CONFIDENCE: TIED — architect tiebreaker applied — PASSED
            ```
            """;

        var result = SprintReviewPhase.ParseConfidenceVoteResult(response);

        Assert.True(result.ArchitectTiebreakerUsed);
        Assert.True(result.ArchitectTiebreakerYes);
        Assert.Equal(SprintReviewPhase.VoteOutcome.Passed, result.Outcome);
    }

    // ── em-dash vs hyphen variants ────────────────────────────────────────────

    [Fact]
    public void ParseConfidenceVoteResult_HyphenSeparator_ParsesVote()
    {
        const string response =
            "> VOTE: NO (MINOR) - Story lacks acceptance criteria - FIX: Add AC\n"
            + "```\nCONFIDENCE: FAILED (MINOR) — 1 issue\n```";

        var result = SprintReviewPhase.ParseConfidenceVoteResult(response);

        Assert.Equal(1, result.NoMinorCount);
    }

    [Fact]
    public void ParseConfidenceVoteResult_EnDashSeparator_ParsesVote()
    {
        const string response =
            "> VOTE: YES – Requirements are clear\n"
            + "```\nCONFIDENCE: PASSED (1 yes / 0 no)\n```";

        var result = SprintReviewPhase.ParseConfidenceVoteResult(response);

        Assert.Equal(1, result.YesCount);
    }

    // ── Markdown table vote format (new format observed in production) ────────

    [Fact]
    public void ParseConfidenceVoteResult_MarkdownTableVotes_CorrectCounts()
    {
        // Exact output format that caused the production regression:
        // - Votes in a Markdown table (no VOTE: prefix)
        // - CONFIDENCE: line prefixed with "## 🏁"
        var response = """
            ## 📊 VOTE TALLY
            | Agent | Vote |
            |-------|------|
            | Winston (Architect) | NO (MINOR) |
            | John (PM) | NO (MINOR) |
            | Quinn (QA) | NO (MINOR) |
            | Amelia (Dev) | NO (MINOR) |
            | Skeptic | NO (MINOR) |
            | Edge Case Hunter | NO (MINOR) |

            **Yes votes: 0 · No (MINOR) votes: 6 · No (MAJOR) votes: 0**

            ## 🏁 CONFIDENCE: FAILED (MINOR)
            """;

        var result = SprintReviewPhase.ParseConfidenceVoteResult(response);

        Assert.Equal(0, result.YesCount);
        Assert.Equal(6, result.NoMinorCount);
        Assert.Empty(result.MajorIssues);
        Assert.Equal(SprintReviewPhase.VoteOutcome.FailedMinorOnly, result.Outcome);
    }

    [Fact]
    public void ParseConfidenceVoteResult_MarkdownTableMixedVotes_CorrectCounts()
    {
        var response = """
            | Agent | Vote |
            |-------|------|
            | Winston | YES |
            | John | NO (MINOR) |
            | Amelia | NO (MAJOR) |

            ## 🏁 CONFIDENCE: FAILED (MAJOR) — scope ambiguity
            """;

        var result = SprintReviewPhase.ParseConfidenceVoteResult(response);

        Assert.Equal(1, result.YesCount);
        Assert.Equal(1, result.NoMinorCount);
        Assert.Equal(1, result.MajorIssues.Count);
        Assert.Equal(SprintReviewPhase.VoteOutcome.FailedMajor, result.Outcome);
    }

    [Fact]
    public void ParseConfidenceVoteResult_MarkdownHeaderConfidenceLine_OutcomeDetected()
    {
        // Verify that various markdown header prefixes before CONFIDENCE: are handled.
        var response = """
            Team discussed the stories and agreed.

            ### ✅ CONFIDENCE: PASSED (4 yes / 0 no)
            """;

        var result = SprintReviewPhase.ParseConfidenceVoteResult(response);

        Assert.Equal(SprintReviewPhase.VoteOutcome.Passed, result.Outcome);
    }

    [Fact]
    public void ParseConfidenceVoteResult_ConfirmedRealWorldSample_ParsesAllSixNoMinorVotes()
    {
        // Exact blockquote format observed in the failing run that triggered this fix.
        var response = """
            **Bob 🏃 (Scrum Master):**
            > VOTE: NO (MINOR) — Stories 1.1 and 1.6 have TBD package versions | FIX: Pin versions

            **Amelia 💻 (Developer):**
            > VOTE: NO (MINOR) — Migration path conflict (Story 1.3) | FIX: Adopt root schema/ path

            **Winston 🏗️ (Architect):**
            > VOTE: NO (MINOR) — .gitattributes CRLF missing from Story 1.1 | FIX: Add .gitattributes

            **Murat 🧪 (Test Architect):**
            > VOTE: NO (MINOR) — Three untestable AC clauses | FIX: Rewrite to measurable criteria

            **John 📋 (PM):**
            > VOTE: YES — All issues are minor; no scope changes required.

            **Skeptic 🗡️:**
            > VOTE: NO (MINOR) — AT10 unenforceable by NetArchTest | FIX: Rewrite constraint

            **Edge Case Hunter 🔍:**
            > VOTE: NO (MINOR) — Missing .gitattributes and AT6 vacuous-pass caveat | FIX: Add note

            *Vote tally: 1 YES, 6 NO (MINOR). No MAJOR issues.*

            ```
            CONFIDENCE: FAILED (MINOR) — 6 issues across 5 stories, all resolvable before sprint start
            ```
            """;

        var result = SprintReviewPhase.ParseConfidenceVoteResult(response);

        Assert.Equal(1, result.YesCount);
        Assert.Equal(6, result.NoMinorCount);
        Assert.Empty(result.MajorIssues);
        Assert.Equal(SprintReviewPhase.VoteOutcome.FailedMinorOnly, result.Outcome);
        // All 7 votes should be parsed individually so <minor-issues> has content
        Assert.Equal(7, result.Votes.Count);
    }
}
