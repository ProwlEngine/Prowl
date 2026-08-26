// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Clay.Importer;

/// <summary>
/// Per-import configuration. Construct directly with object-initializer syntax, or start from
/// <see cref="GameQuality"/>/<see cref="GameFast"/>/<see cref="EditorMaxQuality"/>/<see cref="Raw"/>.
/// </summary>
public sealed record ModelImporterSettings
{
    /// <summary>Bit mask of post-process steps to run.</summary>
    public PostProcessFlags PostProcess { get; init; } = PostProcessPresets.GameQuality;

    /// <summary>Maximum smoothing angle (in degrees) used by <c>GenerateSmoothNormals</c>.</summary>
    public float SmoothNormalsAngleDeg { get; init; } = 80f;

    /// <summary>
    /// When true, the <c>GenerateNormals</c> / <c>GenerateSmoothNormals</c> steps replace normals
    /// the source already supplied instead of leaving them alone. Off by default: a source that
    /// shipped normals knows its own smoothing groups, and rebuilding them from geometry discards
    /// that.
    /// </summary>
    public bool RecalculateNormals { get; init; }

    /// <summary>
    /// Maximum number of bone weights kept per vertex by <c>LimitBoneWeights</c>. Values above
    /// <see cref="BoneWeight.MaxInfluences"/> are honoured through the pipeline but not in the baked
    /// <see cref="Model"/>, whose vertex layout holds four; the bake keeps the strongest four,
    /// renormalises them, and says so in the log.
    /// </summary>
    public int BoneWeightLimit { get; init; } = 4;

    /// <summary>Uniform scale applied by <c>GlobalScale</c> (defaults to 1.0 = no scaling).</summary>
    public float GlobalScale { get; init; } = 1f;

    /// <summary>Maximum vertices per mesh enforced by <c>SplitLargeMeshes</c>.</summary>
    public int MaxVerticesPerMesh { get; init; } = 65534;

    /// <summary>
    /// When true (and the <c>ValidateDataStructure</c> step is enabled), validator warnings are
    /// promoted to <see cref="ImportException"/>.
    /// </summary>
    public bool StrictValidation { get; init; }

    /// <summary>
    /// Index of the scene to import from a file that defines several, or -1 for the one the file
    /// nominates as its default. Out-of-range values fall back to the default with a warning.
    /// </summary>
    public int SceneIndex { get; init; } = -1;

    /// <summary>Names of nodes the <c>OptimizeGraph</c> step must never collapse.</summary>
    public IReadOnlyList<string> OptimizeGraphPreserveNodeNames { get; init; } = Array.Empty<string>();

    /// <summary>Sink invoked for every <see cref="ImportLogEntry"/> as it is emitted.</summary>
    public Action<ImportLogEntry>? OnLog { get; init; }

    /// <summary>Raw, no-post-processing preset.</summary>
    public static ModelImporterSettings Raw { get; } = new() { PostProcess = PostProcessPresets.Raw };

    /// <summary>Fast game-engine preset (minimal post-processing).</summary>
    public static ModelImporterSettings GameFast { get; } = new() { PostProcess = PostProcessPresets.GameFast };

    /// <summary>Default game-engine preset (recommended for runtime imports).</summary>
    public static ModelImporterSettings GameQuality { get; } = new() { PostProcess = PostProcessPresets.GameQuality };

    /// <summary>Maximum-quality editor preset (slower, runs validators + optimizers).</summary>
    public static ModelImporterSettings EditorMaxQuality { get; } = new() { PostProcess = PostProcessPresets.EditorMaxQuality };
}
