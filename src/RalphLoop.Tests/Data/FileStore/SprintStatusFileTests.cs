using RalphLoop.Data.FileStore;
using Xunit;

namespace RalphLoop.Tests.Data.FileStore;

/// <summary>
/// Unit tests for <see cref="SprintStatusFile"/> using temporary yaml files.
/// </summary>
public sealed class SprintStatusFileTests : IDisposable
{
    private readonly string _dir;

    public SprintStatusFileTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string WriteYaml(string content)
    {
        var path = Path.Combine(_dir, "sprint-status.yaml");
        File.WriteAllText(path, content);
        return path;
    }

    // ── LoadAsync ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_MissingFile_ThrowsFileNotFoundException()
    {
        var path = Path.Combine(_dir, "does-not-exist.yaml");

        await Assert.ThrowsAsync<FileNotFoundException>(() => SprintStatusFile.LoadAsync(path));
    }

    [Fact]
    public async Task LoadAsync_NoStoryLocation_DefaultsToYamlDirectory()
    {
        var path = WriteYaml(
            """
            development_status:
              1-1-my-story: Pending
            """
        );

        var ssf = await SprintStatusFile.LoadAsync(path);

        Assert.Equal(Path.GetDirectoryName(Path.GetFullPath(path)), ssf.StoryLocationAbsolute);
    }

    [Fact]
    public async Task LoadAsync_RelativeStoryLocation_ResolvesAgainstYamlDir()
    {
        var path = WriteYaml(
            """
            story_location: stories
            development_status:
              1-1-task: Pending
            """
        );

        var ssf = await SprintStatusFile.LoadAsync(path);

        var expected = Path.GetFullPath(Path.Combine(_dir, "stories"));
        Assert.Equal(expected, ssf.StoryLocationAbsolute);
    }

    [Fact]
    public async Task LoadAsync_ParsesStoriesAndEpics()
    {
        var path = WriteYaml(
            """
            development_status:
              1-1-auth-login: Pending
              1-2-user-profile: InProgress
              epic-auth: Pending
              sprint-1-retrospective: Done
            """
        );

        var ssf = await SprintStatusFile.LoadAsync(path);

        Assert.Equal(3, ssf.Entries.Count); // epic + 2 stories (retro skipped)
        Assert.Equal(2, ssf.GetStoriesOnly().Count);
        Assert.Single(ssf.GetEpicsOnly());
    }

    [Fact]
    public async Task LoadAsync_EmptyDevelopmentStatus_ReturnsNoEntries()
    {
        var path = WriteYaml(
            """
            story_location: .
            development_status:
            """
        );

        var ssf = await SprintStatusFile.LoadAsync(path);

        Assert.Empty(ssf.Entries);
    }

    // ── ClassifyKey ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("1-1-my-story", EntryType.Story)]
    [InlineData("2-10-another", EntryType.Story)]
    [InlineData("epic-auth", EntryType.Epic)]
    [InlineData("epic-storage", EntryType.Epic)]
    [InlineData("sprint-1-retrospective", EntryType.Retrospective)]
    [InlineData("misc-thing", EntryType.Unknown)]
    public void ClassifyKey_ReturnsExpectedType(string key, EntryType expected)
    {
        Assert.Equal(expected, SprintStatusFile.ClassifyKey(key));
    }

    // ── GetStoriesOnly / GetEpicsOnly ─────────────────────────────────────────

    [Fact]
    public async Task GetStoriesOnly_FiltersToStoriesOnly()
    {
        var path = WriteYaml(
            """
            development_status:
              1-1-login: Pending
              epic-auth: Done
              2-1-signup: InProgress
            """
        );

        var ssf = await SprintStatusFile.LoadAsync(path);
        var stories = ssf.GetStoriesOnly();

        Assert.Equal(2, stories.Count);
        Assert.All(stories, s => Assert.Equal(EntryType.Story, s.EntryType));
    }

    [Fact]
    public async Task GetEpicsOnly_FiltersToEpicsOnly()
    {
        var path = WriteYaml(
            """
            development_status:
              1-1-login: Pending
              epic-auth: Done
            """
        );

        var ssf = await SprintStatusFile.LoadAsync(path);
        var epics = ssf.GetEpicsOnly();

        Assert.Single(epics);
        Assert.Equal("epic-auth", epics[0].Key);
        Assert.Equal("Done", epics[0].Status);
    }

    // ── UpdateStatusAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateStatusAsync_ExistingKey_UpdatesInPlace()
    {
        var path = WriteYaml(
            """
            development_status:
              1-1-auth-login: Pending
              1-2-profile: InProgress
            """
        );

        var ssf = await SprintStatusFile.LoadAsync(path);
        await ssf.UpdateStatusAsync("1-1-auth-login", "Done");

        var updated = await File.ReadAllTextAsync(path);
        Assert.Contains("1-1-auth-login: Done", updated);
        Assert.DoesNotContain("1-1-auth-login: Pending", updated);
        Assert.Contains("1-2-profile: InProgress", updated); // other entries preserved
    }

    [Fact]
    public async Task UpdateStatusAsync_PreservesCommentsAndBlankLines()
    {
        var path = WriteYaml(
            """
            # Sprint status file
            development_status:
              # Auth stories
              1-1-login: Pending

              1-2-logout: Pending
            """
        );

        var ssf = await SprintStatusFile.LoadAsync(path);
        await ssf.UpdateStatusAsync("1-1-login", "Done");

        var updated = await File.ReadAllTextAsync(path);
        Assert.Contains("# Sprint status file", updated);
        Assert.Contains("# Auth stories", updated);
        Assert.Contains("1-1-login: Done", updated);
    }

    // ── ResolveStoryFilePath ──────────────────────────────────────────────────

    [Fact]
    public async Task ResolveStoryFilePath_ExactMatch_ReturnsExactPath()
    {
        var storyDir = Path.Combine(_dir, "stories");
        Directory.CreateDirectory(storyDir);
        var storyFile = Path.Combine(storyDir, "1-1-login.md");
        await File.WriteAllTextAsync(storyFile, "# Login Story");

        var path = WriteYaml(
            $"""
            story_location: stories
            development_status:
              1-1-login: Pending
            """
        );

        var ssf = await SprintStatusFile.LoadAsync(path);
        var resolved = ssf.ResolveStoryFilePath("1-1-login");

        Assert.Equal(Path.GetFullPath(storyFile), resolved);
    }

    [Fact]
    public async Task ResolveStoryFilePath_NoFileExists_ReturnsCanonicalPath()
    {
        var path = WriteYaml(
            """
            development_status:
              1-1-missing: Pending
            """
        );

        var ssf = await SprintStatusFile.LoadAsync(path);
        var resolved = ssf.ResolveStoryFilePath("1-1-missing");

        Assert.Equal(Path.Combine(ssf.StoryLocationAbsolute, "1-1-missing.md"), resolved);
    }

    [Fact]
    public async Task ResolveStoryFilePath_MultipleGlobMatches_Throws()
    {
        var storyDir = Path.Combine(_dir, "stories");
        Directory.CreateDirectory(storyDir);
        await File.WriteAllTextAsync(Path.Combine(storyDir, "prefix-1-1-login.md"), "");
        await File.WriteAllTextAsync(Path.Combine(storyDir, "other-1-1-login.md"), "");

        var path = WriteYaml(
            $"""
            story_location: stories
            development_status:
              1-1-login: Pending
            """
        );

        var ssf = await SprintStatusFile.LoadAsync(path);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ssf.ResolveStoryFilePath("1-1-login")
        );
        Assert.Contains("Ambiguous", ex.Message);
    }
}
