// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Text.Json;

using Prowl.Clay.Importer;

namespace Prowl.Clay;

/// <summary>
/// File-level metadata captured from the source: format identification, generator strings,
/// copyright, and any unmodeled root extensions (e.g. VRM JSON).
/// </summary>
public sealed class ModelMetadata
{
    /// <summary>The model importer instance.</summary>
    public required IModelFormat Format { get; init; }

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
