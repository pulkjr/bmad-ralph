namespace RalphLoop.Data.FileStore;

/// <summary>
/// Singleton that lazily holds the loaded <see cref="SprintStatusFile"/> for the current run.
/// In sqlite storage mode, <see cref="SprintStatusYamlPath"/> is empty and
/// <see cref="GetAsync"/> throws <see cref="InvalidOperationException"/>.
/// In file storage mode, the file is loaded on first access and cached for the run lifetime.
/// </summary>
public sealed class FileStoreContext
{
    private SprintStatusFile? _cached;
    private string? _yamlPath;

    /// <summary>Configure the context with the path to sprint-status.yaml.</summary>
    public void Initialize(string yamlPath) => _yamlPath = yamlPath;

    /// <summary>True if the context has been initialised with a yaml path.</summary>
    public bool IsInitialized => _yamlPath is not null;

    /// <summary>
    /// Returns the loaded (and cached) <see cref="SprintStatusFile"/>.
    /// Loads from disk on first call.
    /// </summary>
    public async Task<SprintStatusFile> GetAsync()
    {
        if (_yamlPath is null)
            throw new InvalidOperationException(
                "FileStoreContext is not initialised. Ensure StorageMode is 'file' and "
                    + "Initialize() is called before use."
            );

        if (_cached is not null)
            return _cached;

        _cached = await SprintStatusFile.LoadAsync(_yamlPath);
        return _cached;
    }

    /// <summary>Reloads the sprint-status.yaml from disk (e.g., after a status update).</summary>
    public void Invalidate() => _cached = null;
}
