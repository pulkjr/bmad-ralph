using RalphLoop.Git;
using Xunit;

namespace RalphLoop.Tests.Git;

/// <summary>
/// Pure unit tests for <see cref="GitManager.FormatCommitMessage"/>.
/// No I/O — safe to run on every commit (pre-commit hook, fast subset).
/// </summary>
public class GitManagerTests
{
    // ── Scope / subject line ─────────────────────────────────────────────────

    [Fact]
    public void FormatCommitMessage_SpacesInEpicName_ConvertedToKebabCase()
    {
        var msg = GitManager.FormatCommitMessage("Add login", "User Auth", 1);

        Assert.Contains("feat(user-auth):", msg);
    }

    [Fact]
    public void FormatCommitMessage_MixedCaseEpicName_LowercasedInScope()
    {
        var msg = GitManager.FormatCommitMessage("Setup DB", "DATA LAYER", 1);

        Assert.Contains("feat(data-layer):", msg);
    }

    [Fact]
    public void FormatCommitMessage_StoryName_AppearsInSubjectLine()
    {
        var msg = GitManager.FormatCommitMessage("Implement JWT refresh", "Auth", 1);

        var firstLine = msg.Split('\n')[0];
        Assert.Contains("Implement JWT refresh", firstLine);
    }

    [Fact]
    public void FormatCommitMessage_SubjectLine_StartsWithFeatType()
    {
        var msg = GitManager.FormatCommitMessage("Any story", "Any Epic", 1);

        Assert.StartsWith("feat(", msg.TrimStart());
    }

    [Fact]
    public void FormatCommitMessage_SubjectLine_UsesEmDashBeforeStoryName()
    {
        var msg = GitManager.FormatCommitMessage("My Story", "Epic", 1);

        var firstLine = msg.Split('\n')[0];
        Assert.Contains("— My Story", firstLine);
    }

    // ── Body content ─────────────────────────────────────────────────────────

    [Fact]
    public void FormatCommitMessage_EpicName_AppearsVerbatimInBody()
    {
        var msg = GitManager.FormatCommitMessage("Story A", "My Important Epic", 2);

        Assert.Contains("My Important Epic", msg);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(10)]
    [InlineData(99)]
    public void FormatCommitMessage_RoundNumber_AppearsInBody(int round)
    {
        var msg = GitManager.FormatCommitMessage("Story", "Epic", round);

        Assert.Contains($"round {round}", msg);
    }

    [Fact]
    public void FormatCommitMessage_ContainsIntentMarker()
    {
        var msg = GitManager.FormatCommitMessage("S", "E", 1);

        Assert.Contains("[Intent]:", msg);
    }

    [Fact]
    public void FormatCommitMessage_ContainsApproachMarker()
    {
        var msg = GitManager.FormatCommitMessage("S", "E", 1);

        Assert.Contains("[Approach]:", msg);
    }

    [Fact]
    public void FormatCommitMessage_ContainsOutcomeMarker()
    {
        var msg = GitManager.FormatCommitMessage("S", "E", 1);

        Assert.Contains("[Outcome]:", msg);
    }

    [Fact]
    public void FormatCommitMessage_ContainsCopilotCoAuthoredByTrailer()
    {
        var msg = GitManager.FormatCommitMessage("S", "E", 1);

        Assert.Contains(
            "Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>",
            msg
        );
    }

    // ── Edge cases ───────────────────────────────────────────────────────────

    [Fact]
    public void FormatCommitMessage_EmptyEpicName_ProducesEmptyScopeParens()
    {
        var msg = GitManager.FormatCommitMessage("My Story", "", 1);

        Assert.Contains("feat():", msg);
    }

    [Fact]
    public void FormatCommitMessage_UnicodeStoryName_PreservedInFull()
    {
        const string unicode = "日本語のストーリー: 認証を実装する";
        var msg = GitManager.FormatCommitMessage(unicode, "Auth", 1);

        Assert.Contains(unicode, msg);
    }

    [Fact]
    public void FormatCommitMessage_LongStoryName_NotTruncated()
    {
        var longName = new string('A', 200);
        var msg = GitManager.FormatCommitMessage(longName, "Epic", 1);

        Assert.Contains(longName, msg);
    }

    [Fact]
    public void FormatCommitMessage_MultipleSpacesInEpicName_EachConvertedToDash()
    {
        // "A B C" → "a-b-c" (spaces → dashes, lowercased)
        var msg = GitManager.FormatCommitMessage("Story", "A B C", 1);

        Assert.Contains("feat(a-b-c):", msg);
    }

    [Fact]
    public void FormatCommitMessage_EpicNameWithOnlySpaces_DoesNotThrow()
    {
        // Spaces become dashes — just verify no exception and a non-empty result
        var msg = GitManager.FormatCommitMessage("Story", "   ", 1);

        Assert.NotNull(msg);
        Assert.NotEmpty(msg);
    }

    [Fact]
    public void FormatCommitMessage_SubjectLine_HasNoTrailingWhitespace()
    {
        var msg = GitManager.FormatCommitMessage("Story", "Epic", 1);

        var firstLine = msg.Split('\n')[0];
        Assert.Equal(firstLine.TrimEnd(), firstLine);
    }

    [Fact]
    public void FormatCommitMessage_ReturnedString_ContainsAtLeastFiveLines()
    {
        // subject + blank + [Intent] + [Approach] + [Outcome] + blank + Co-authored-by
        var msg = GitManager.FormatCommitMessage("S", "E", 1);

        var lines = msg.Split('\n');
        Assert.True(lines.Length >= 5, $"Expected ≥5 lines, got {lines.Length}");
    }
}
