namespace Prowl.Clay.Importer;

/// <summary>
/// Shared state passed through one import call: settings, file resolver, log sink, cancellation
/// token, and the source path or format hint.
/// </summary>
internal sealed class ImportContext
{
    /// <summary>Settings provided by the caller.</summary>
    public required ModelImporterSettings Settings { get; init; }

    /// <summary>Resolves sidecar files referenced by the model.</summary>
    public required IFileResolver Resolver { get; init; }

    /// <summary>Log to attach to the final <see cref="Model"/>.</summary>
    public required ImportLog Log { get; init; }

    /// <summary>Cancellation token honored at chunk boundaries.</summary>
    public CancellationToken CancellationToken { get; init; }

    /// <summary>Source path, or <c>null</c> for stream-based imports.</summary>
    public string? SourcePath { get; init; }

    /// <summary>Format token (gltf/glb/obj/fbx/vrm).</summary>
    public required string Format { get; init; }
}
