namespace Prowl.Clay.Importer;

/// <summary>
/// Bit flags selecting which post-process steps to run. Steps execute in a fixed canonical order
/// regardless of how the flags are combined.
/// </summary>
[Flags]
public enum PostProcessFlags : uint
{
    /// <summary>No steps.</summary>
    None = 0,

    /// <summary>Convert polygons and strip/fan topologies into a pure triangle list.</summary>
    Triangulate = 1u << 0,

    /// <summary>Identify and collapse identical vertex attribute tuples into shared vertices.</summary>
    JoinIdenticalVertices = 1u << 1,

    /// <summary>Generate flat per-face normals when the source did not supply them.</summary>
    GenerateNormals = 1u << 2,

    /// <summary>Generate angle-weighted smooth normals.</summary>
    GenerateSmoothNormals = 1u << 3,

    /// <summary>Compute tangents and bitangent signs using MikkTSpace.</summary>
    CalcTangentSpace = 1u << 4,

    /// <summary>Limit bone influences per vertex (default 4) and renormalize weights.</summary>
    LimitBoneWeights = 1u << 5,

    /// <summary>Remove zero-area triangles, coincident-index faces, etc.</summary>
    RemoveDegenerates = 1u << 6,

    /// <summary>Detect NaN/Inf values, redundant animation keys, and similar issues.</summary>
    FindInvalidData = 1u << 7,

    /// <summary>Flip the V axis on every UV channel (top-left vs bottom-left origin).</summary>
    FlipUVs = 1u << 8,

    /// <summary>Reverse triangle index winding order.</summary>
    FlipWindingOrder = 1u << 9,

    /// <summary>Convert the imported scene from its native coordinate system to the target
    /// (left-handed, Y-up, +Z forward).</summary>
    ConvertCoordinateSystem = 1u << 10,

    /// <summary>Apply <see cref="ModelImporterSettings.GlobalScale"/> to translations and positions.</summary>
    GlobalScale = 1u << 11,

    /// <summary>Compute per-mesh and per-submesh AABBs.</summary>
    GenerateBounds = 1u << 12,

    /// <summary>Read externally referenced textures into <see cref="Texture.EncodedBytes"/>.</summary>
    EmbedTextures = 1u << 13,

    /// <summary>Build <see cref="Skin"/> objects with bone-node links and bind poses.</summary>
    PopulateSkeletons = 1u << 14,

    /// <summary>Merge sibling meshes that share material and aren't skinned.</summary>
    OptimizeMeshes = 1u << 15,

    /// <summary>Collapse purely identity nodes that have no animation/skin/named role.</summary>
    OptimizeGraph = 1u << 16,

    /// <summary>Reorder triangle indices for better vertex-cache locality.</summary>
    ImproveCacheLocality = 1u << 17,

    /// <summary>Split meshes with too many bones into sub-meshes.</summary>
    SplitByBoneCount = 1u << 18,

    /// <summary>Split meshes that exceed the configured vertex limit.</summary>
    SplitLargeMeshes = 1u << 19,

    /// <summary>Strip dummy bone weights that have no animation effect.</summary>
    Debone = 1u << 20,

    /// <summary>Split meshes that mix triangle/line/point primitives.</summary>
    SortByPrimitiveType = 1u << 21,

    /// <summary>Run the cross-reference validator.</summary>
    ValidateDataStructure = 1u << 22,
}

/// <summary>Canonical bundles of <see cref="PostProcessFlags"/> for common scenarios.</summary>
public static class PostProcessPresets
{
    /// <summary>No post-processing - the raw importer output.</summary>
    public const PostProcessFlags Raw = PostProcessFlags.None;

    /// <summary>Fast game-engine preset, prioritizing import speed.</summary>
    public const PostProcessFlags GameFast =
        PostProcessFlags.Triangulate |
        PostProcessFlags.JoinIdenticalVertices |
        PostProcessFlags.ConvertCoordinateSystem |
        PostProcessFlags.GenerateBounds |
        PostProcessFlags.SortByPrimitiveType;

    /// <summary>Default preset for typical game loading: tangents, smooth normals, bone limit, bounds.</summary>
    public const PostProcessFlags GameQuality =
        PostProcessFlags.Triangulate |
        PostProcessFlags.JoinIdenticalVertices |
        PostProcessFlags.CalcTangentSpace |
        PostProcessFlags.GenerateSmoothNormals |
        PostProcessFlags.LimitBoneWeights |
        PostProcessFlags.RemoveDegenerates |
        PostProcessFlags.FindInvalidData |
        PostProcessFlags.ConvertCoordinateSystem |
        PostProcessFlags.GlobalScale |
        PostProcessFlags.PopulateSkeletons |
        PostProcessFlags.GenerateBounds |
        PostProcessFlags.SortByPrimitiveType;

    /// <summary>Editor preset, enabling validators, optimizers, and cache locality.</summary>
    public const PostProcessFlags EditorMaxQuality =
        GameQuality |
        PostProcessFlags.OptimizeMeshes |
        PostProcessFlags.OptimizeGraph |
        PostProcessFlags.ImproveCacheLocality |
        PostProcessFlags.ValidateDataStructure;
}
