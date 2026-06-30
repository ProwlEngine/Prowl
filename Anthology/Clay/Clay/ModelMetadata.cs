using System.Text.Json;

namespace Prowl.Clay;

/// <summary>
/// File-level metadata captured from the source: format identification, generator strings,
/// copyright, and any unmodeled root extensions (e.g. VRM JSON).
/// </summary>
public sealed class ModelMetadata
{
    /// <summary>Short format token, e.g. <c>"gltf"</c>, <c>"glb"</c>, <c>"obj"</c>, <c>"fbx"</c>, <c>"vrm"</c>.</summary>
    public required string Format { get; init; }

    /// <summary>Format version string when available, e.g. <c>"2.0"</c> for glTF, <c>"7400"</c> for FBX.</summary>
    public string? FormatVersion { get; init; }

    /// <summary>Tool that produced the file (glTF <c>asset.generator</c>, FBX <c>FBXHeaderExtension.Creator</c>, etc.).</summary>
    public string? Generator { get; init; }

    /// <summary>Copyright string declared by the source, if any.</summary>
    public string? Copyright { get; init; }

    /// <summary>
    /// Unmodeled root-level extension data. For glTF this is the contents of <c>extensions</c> on the
    /// top-level asset (e.g. <c>VRMC_vrm</c>, <c>VRMC_springBone</c>).
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement> RawExtensions { get; init; } =
        new Dictionary<string, JsonElement>();

    /// <summary>Other free-form metadata (FBX user properties at scene level, glTF <c>asset.extras</c>, ...).</summary>
    public IReadOnlyDictionary<string, object?> Extras { get; init; } =
        new Dictionary<string, object?>();
}
