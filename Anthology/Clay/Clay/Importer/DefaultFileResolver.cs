namespace Prowl.Clay.Importer;

/// <summary>
/// Default <see cref="IFileResolver"/>: resolves relative paths against the directory of the
/// source model, optionally searching additional fallback directories.
/// </summary>
public sealed class DefaultFileResolver : IFileResolver
{
    private readonly string[] _searchDirectories;

    /// <summary>Initializes a resolver with no extra search directories.</summary>
    public DefaultFileResolver() : this(Array.Empty<string>())
    {
    }

    /// <summary>Initializes a resolver with additional fallback directories.</summary>
    public DefaultFileResolver(IEnumerable<string> additionalSearchDirectories)
    {
        _searchDirectories = additionalSearchDirectories.ToArray();
    }

    /// <inheritdoc />
    public string? Resolve(string modelPath, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(modelPath);
        ArgumentNullException.ThrowIfNull(relativePath);

        if (Path.IsPathRooted(relativePath) && File.Exists(relativePath))
            return Path.GetFullPath(relativePath);

        string? modelDir = Path.GetDirectoryName(modelPath);
        if (!string.IsNullOrEmpty(modelDir))
        {
            string candidate = Path.GetFullPath(Path.Combine(modelDir, relativePath));
            if (File.Exists(candidate))
                return candidate;
        }

        for (int i = 0; i < _searchDirectories.Length; i++)
        {
            string candidate = Path.GetFullPath(Path.Combine(_searchDirectories[i], relativePath));
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    /// <inheritdoc />
    public Stream OpenRead(string absolutePath) => File.OpenRead(absolutePath);

    /// <inheritdoc />
    public byte[] ReadAllBytes(string absolutePath) => File.ReadAllBytes(absolutePath);

    /// <inheritdoc />
    public bool Exists(string absolutePath) => File.Exists(absolutePath);
}
