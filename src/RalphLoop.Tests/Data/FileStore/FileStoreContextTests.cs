using RalphLoop.Data.FileStore;
using Xunit;

namespace RalphLoop.Tests.Data.FileStore;

/// <summary>
/// Unit tests for <see cref="FileStoreContext"/>.
/// </summary>
public sealed class FileStoreContextTests : IDisposable
{
    private readonly string _dir;

    public FileStoreContextTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string WriteMinimalYaml()
    {
        var path = Path.Combine(_dir, "sprint-status.yaml");
        File.WriteAllText(
            path,
            """
            development_status:
              1-1-my-story: Pending
            """
        );
        return path;
    }

    // ── IsInitialized ─────────────────────────────────────────────────────────

    [Fact]
    public void IsInitialized_BeforeInitialize_ReturnsFalse()
    {
        var ctx = new FileStoreContext();
        Assert.False(ctx.IsInitialized);
    }

    [Fact]
    public void IsInitialized_AfterInitialize_ReturnsTrue()
    {
        var ctx = new FileStoreContext();
        ctx.Initialize("/some/path.yaml");
        Assert.True(ctx.IsInitialized);
    }

    // ── GetAsync throws when not initialized ──────────────────────────────────

    [Fact]
    public async Task GetAsync_NotInitialized_ThrowsInvalidOperationException()
    {
        var ctx = new FileStoreContext();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.GetAsync());
        Assert.Contains("not initialised", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── GetAsync loads and caches ─────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_LoadsFromDiskOnFirstCall()
    {
        var yaml = WriteMinimalYaml();
        var ctx = new FileStoreContext();
        ctx.Initialize(yaml);

        var ssf = await ctx.GetAsync();

        Assert.NotNull(ssf);
        Assert.Single(ssf.GetStoriesOnly());
    }

    [Fact]
    public async Task GetAsync_ReturnsSameInstanceOnSecondCall()
    {
        var yaml = WriteMinimalYaml();
        var ctx = new FileStoreContext();
        ctx.Initialize(yaml);

        var first = await ctx.GetAsync();
        var second = await ctx.GetAsync();

        Assert.Same(first, second);
    }

    // ── Invalidate ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Invalidate_CausesReloadOnNextGet()
    {
        var yaml = WriteMinimalYaml();
        var ctx = new FileStoreContext();
        ctx.Initialize(yaml);

        var first = await ctx.GetAsync();

        // Modify the file on disk
        await File.WriteAllTextAsync(
            yaml,
            """
            development_status:
              1-1-my-story: Done
              1-2-new-story: Pending
            """
        );

        ctx.Invalidate();
        var second = await ctx.GetAsync();

        Assert.NotSame(first, second);
        Assert.Equal(2, second.GetStoriesOnly().Count);
    }
}
