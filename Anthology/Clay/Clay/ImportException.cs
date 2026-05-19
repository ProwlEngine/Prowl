namespace Prowl.Clay;

/// <summary>
/// Thrown when import cannot produce a valid <see cref="Model"/>. The only exception type raised
/// by this library; consumers can catch this once and treat all import failures uniformly.
/// </summary>
public sealed class ImportException : Exception
{
    /// <summary>Source path or stream-hint identifying the file being imported.</summary>
    public string? SourcePath { get; }

    /// <summary>Format token (gltf/glb/obj/fbx/vrm) when known, otherwise <c>null</c>.</summary>
    public string? Format { get; }

    /// <summary>Initializes a new exception.</summary>
    public ImportException(string message, string? sourcePath = null, string? format = null, Exception? inner = null)
        : base(message, inner)
    {
        SourcePath = sourcePath;
        Format = format;
    }
}
