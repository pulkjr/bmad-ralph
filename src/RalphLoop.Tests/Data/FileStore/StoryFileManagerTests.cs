using RalphLoop.Data.FileStore;
using Xunit;

namespace RalphLoop.Tests.Data.FileStore;

/// <summary>
/// Unit tests for <see cref="StoryFileManager"/> static methods.
/// Each test uses a temporary directory that is cleaned up on dispose.
/// </summary>
public sealed class StoryFileManagerTests : IDisposable
{
    private readonly string _dir;

    public StoryFileManagerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Write(string fileName, string content)
    {
        var path = Path.Combine(_dir, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    // ── ReadRalphStoryIdAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task ReadRalphStoryId_NoFrontMatter_ReturnsNull()
    {
        var path = Write("story.md", "# My Story\n\nContent here.\n");

        var id = await StoryFileManager.ReadRalphStoryIdAsync(path);

        Assert.Null(id);
    }

    [Fact]
    public async Task ReadRalphStoryId_FrontMatterWithId_ReturnsId()
    {
        var path = Write(
            "story.md",
            """
            ---
            ralph-story-id: 42
            status: InProgress
            ---
            # My Story
            """
        );

        var id = await StoryFileManager.ReadRalphStoryIdAsync(path);

        Assert.Equal(42L, id);
    }

    [Fact]
    public async Task ReadRalphStoryId_FrontMatterWithoutKey_ReturnsNull()
    {
        var path = Write(
            "story.md",
            """
            ---
            status: Pending
            ---
            # Story
            """
        );

        var id = await StoryFileManager.ReadRalphStoryIdAsync(path);

        Assert.Null(id);
    }

    [Fact]
    public async Task ReadRalphStoryId_FrontMatterWithNonNumericId_ReturnsNull()
    {
        var path = Write(
            "story.md",
            """
            ---
            ralph-story-id: not-a-number
            ---
            # Story
            """
        );

        var id = await StoryFileManager.ReadRalphStoryIdAsync(path);

        Assert.Null(id);
    }

    [Fact]
    public async Task ReadRalphStoryId_EmptyFile_ReturnsNull()
    {
        var path = Write("empty.md", "");

        var id = await StoryFileManager.ReadRalphStoryIdAsync(path);

        Assert.Null(id);
    }

    // ── WriteRalphStoryIdAsync ────────────────────────────────────────────────

    [Fact]
    public async Task WriteRalphStoryId_NoFrontMatter_PrependsFrontMatter()
    {
        var path = Write("story.md", "# My Story\n\nContent.\n");

        await StoryFileManager.WriteRalphStoryIdAsync(path, 7);
        var id = await StoryFileManager.ReadRalphStoryIdAsync(path);

        Assert.Equal(7L, id);
        var content = await File.ReadAllTextAsync(path);
        Assert.Contains("ralph-story-id: 7", content);
        Assert.Contains("# My Story", content);
    }

    [Fact]
    public async Task WriteRalphStoryId_ExistingFrontMatterWithoutKey_InsertsKey()
    {
        var path = Write(
            "story.md",
            """
            ---
            status: Pending
            ---
            # Story
            """
        );

        await StoryFileManager.WriteRalphStoryIdAsync(path, 99);
        var id = await StoryFileManager.ReadRalphStoryIdAsync(path);

        Assert.Equal(99L, id);
    }

    [Fact]
    public async Task WriteRalphStoryId_ExistingFrontMatterWithKey_UpdatesKey()
    {
        var path = Write(
            "story.md",
            """
            ---
            ralph-story-id: 5
            status: Pending
            ---
            # Story
            """
        );

        await StoryFileManager.WriteRalphStoryIdAsync(path, 99);
        var id = await StoryFileManager.ReadRalphStoryIdAsync(path);

        Assert.Equal(99L, id);
        var content = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain("ralph-story-id: 5", content);
    }

    [Fact]
    public async Task WriteRalphStoryId_RoundTrip_ReadBackMatchesWritten()
    {
        var path = Write("story.md", "No front-matter at all.");

        await StoryFileManager.WriteRalphStoryIdAsync(path, 1234);
        var id = await StoryFileManager.ReadRalphStoryIdAsync(path);

        Assert.Equal(1234L, id);
    }

    // ── ReadTitleAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadTitle_H1Present_ReturnsTitle()
    {
        var path = Write("story.md", "# My Awesome Story\n\nContent.\n");

        var title = await StoryFileManager.ReadTitleAsync(path);

        Assert.Equal("My Awesome Story", title);
    }

    [Fact]
    public async Task ReadTitle_NoH1_ReturnsEmpty()
    {
        var path = Write("story.md", "## Section\n\nContent.\n");

        var title = await StoryFileManager.ReadTitleAsync(path);

        Assert.Equal(string.Empty, title);
    }

    [Fact]
    public async Task ReadTitle_H1AfterFrontMatter_ReturnsTitle()
    {
        var path = Write(
            "story.md",
            """
            ---
            ralph-story-id: 1
            ---
            # Story in Front Matter File
            Content.
            """
        );

        var title = await StoryFileManager.ReadTitleAsync(path);

        Assert.Equal("Story in Front Matter File", title);
    }

    // ── ReadShortDescriptionAsync ─────────────────────────────────────────────

    [Fact]
    public async Task ReadShortDescription_StorySectionPresent_ReturnsContent()
    {
        var path = Write(
            "story.md",
            """
            # Title
            ## Story
            As a user, I want to log in so that I can access my dashboard.
            ## Acceptance Criteria
            - AC1
            """
        );

        var desc = await StoryFileManager.ReadShortDescriptionAsync(path);

        Assert.Contains("As a user", desc);
        Assert.DoesNotContain("AC1", desc);
    }

    [Fact]
    public async Task ReadShortDescription_NoStorySectionFallsBackToFirstParagraph()
    {
        var path = Write(
            "story.md",
            """
            # Title
            This is the fallback paragraph content.
            ## Some Section
            """
        );

        var desc = await StoryFileManager.ReadShortDescriptionAsync(path);

        Assert.Contains("fallback paragraph", desc);
    }

    [Fact]
    public async Task ReadShortDescription_TruncatesAtMaxLength()
    {
        var longText = new string('x', 300);
        var path = Write(
            "story.md",
            $"""
            # Title
            ## Story
            {longText}
            """
        );

        var desc = await StoryFileManager.ReadShortDescriptionAsync(path);

        Assert.True(desc.Length <= 200, $"Description length {desc.Length} exceeds 200");
    }

    [Fact]
    public async Task ReadShortDescription_EmptyFile_ReturnsEmpty()
    {
        var path = Write("empty.md", "");

        var desc = await StoryFileManager.ReadShortDescriptionAsync(path);

        Assert.Equal(string.Empty, desc);
    }

    // ── UpdateStatusLineAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task UpdateStatusLine_StatusPresent_UpdatesValue()
    {
        var path = Write(
            "story.md",
            """
            # Story
            Status: Pending
            """
        );

        await StoryFileManager.UpdateStatusLineAsync(path, "InProgress");

        var content = await File.ReadAllTextAsync(path);
        Assert.Contains("Status: InProgress", content);
        Assert.DoesNotContain("Status: Pending", content);
    }

    [Fact]
    public async Task UpdateStatusLine_NoStatusLine_LeavesFileUnchanged()
    {
        const string original = """
            # Story
            No status line here.
            """;
        var path = Write("story.md", original);

        await StoryFileManager.UpdateStatusLineAsync(path, "Done");

        var content = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain("Done", content);
    }

    [Fact]
    public async Task UpdateStatusLine_StatusOutsideFrontMatter_Updates()
    {
        var path = Write(
            "story.md",
            """
            ---
            ralph-story-id: 1
            ---
            # Story
            Status: Pending
            More content.
            """
        );

        await StoryFileManager.UpdateStatusLineAsync(path, "Done");

        var content = await File.ReadAllTextAsync(path);
        Assert.Contains("Status: Done", content);
    }

    // ── UpdateAcceptanceCriteriaAsync ─────────────────────────────────────────

    [Fact]
    public async Task UpdateAcceptanceCriteria_SectionPresent_ReplacesContent()
    {
        var path = Write(
            "story.md",
            """
            # Story
            ## Acceptance Criteria
            - Old AC1
            - Old AC2
            ## Dev Notes
            Some notes.
            """
        );

        await StoryFileManager.UpdateAcceptanceCriteriaAsync(path, "- New AC1\n- New AC2");

        var content = await File.ReadAllTextAsync(path);
        Assert.Contains("New AC1", content);
        Assert.Contains("New AC2", content);
        Assert.DoesNotContain("Old AC1", content);
        Assert.Contains("## Dev Notes", content); // next section preserved
    }

    [Fact]
    public async Task UpdateAcceptanceCriteria_SectionAtEndOfFile_ReplacesContent()
    {
        var path = Write(
            "story.md",
            """
            # Story
            ## Acceptance Criteria
            - Old only AC
            """
        );

        await StoryFileManager.UpdateAcceptanceCriteriaAsync(path, "- Brand new AC");

        var content = await File.ReadAllTextAsync(path);
        Assert.Contains("Brand new AC", content);
        Assert.DoesNotContain("Old only AC", content);
    }

    [Fact]
    public async Task UpdateAcceptanceCriteria_NoSection_LeavesFileUnchanged()
    {
        const string original = """
            # Story
            No AC section here.
            """;
        var path = Write("story.md", original);

        await StoryFileManager.UpdateAcceptanceCriteriaAsync(path, "- New AC");

        var content = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain("New AC", content);
    }

    [Fact]
    public async Task UpdateAcceptanceCriteria_CaseInsensitiveHeading_ReplacesContent()
    {
        var path = Write(
            "story.md",
            """
            # Story
            ## acceptance criteria
            - lowercase heading AC
            """
        );

        await StoryFileManager.UpdateAcceptanceCriteriaAsync(path, "- Updated AC");

        var content = await File.ReadAllTextAsync(path);
        Assert.Contains("Updated AC", content);
        Assert.DoesNotContain("lowercase heading AC", content);
    }
}
