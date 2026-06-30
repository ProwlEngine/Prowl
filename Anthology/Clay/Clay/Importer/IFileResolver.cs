namespace Prowl.Clay.Importer;

/// <summary>
/// Resolves and opens sidecar files referenced by a model (glTF <c>.bin</c> buffers,
/// external textures, MTL files for OBJ, etc.).
/// </summary>
public interface IFileResolver
{
    /// <summary>
    /// Returns a resolved absolute path for a relative URI <paramref name="relativePath"/>
    /// found inside <paramref name="modelPath"/>, or <c>null</c> if the file cannot be found.
    /// </summary>
    string? Resolve(string modelPath, string relativePath);

    /// <summary>Opens the file at the given absolute path for reading.</summary>
    Stream OpenRead(string absolutePath);

    /// <summary>Returns the raw bytes of the file at the given absolute path.</summary>
    byte[] ReadAllBytes(string absolutePath);

    /// <summary>True when the file at the given absolute path exists.</summary>
    bool Exists(string absolutePath);
}
