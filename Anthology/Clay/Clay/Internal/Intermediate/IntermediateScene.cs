using System.Text.Json;

namespace Prowl.Clay.Internal.Intermediate;

/// <summary>
/// Mutable, writable intermediate form of a scene used between the format reader and the
/// post-process pipeline. Flat lists of nodes, meshes, materials, textures, skins, and
/// animations let post-process steps mutate the scene in place before bake.
/// </summary>
internal sealed class IntermediateScene
{
    public IntermediateNode Root { get; set; } = new();
    public List<IntermediateNode> Nodes { get; } = new();
    public List<IntermediateMesh> Meshes { get; } = new();
    public List<IntermediateMaterial> Materials { get; } = new();
    public List<IntermediateTexture> Textures { get; } = new();
    public List<IntermediateSkin> Skins { get; } = new();
    public List<IntermediateAnimation> Animations { get; } = new();

    public CoordinateSystem SourceCoordinateSystem { get; set; } = CoordinateSystem.RightHandedYUp;
    public float SourceUnitToMeters { get; set; } = 1f;

    public Dictionary<string, JsonElement> RawExtensions { get; } = new();
    public Dictionary<string, object?> Extras { get; } = new();

    public string Format { get; set; } = string.Empty;
    public string? FormatVersion { get; set; }
    public string? Generator { get; set; }
    public string? Copyright { get; set; }
}

/// <summary>Coordinate convention of an intermediate scene.</summary>
internal enum CoordinateSystem
{
    /// <summary>Right-handed, Y-up, -Z forward (glTF native).</summary>
    RightHandedYUp,
    /// <summary>Right-handed, Z-up, -Y forward (Blender, some FBX).</summary>
    RightHandedZUp,
    /// <summary>Left-handed, Y-up, +Z forward (DirectX convention).</summary>
    LeftHandedYUp,
    /// <summary>Left-handed, Z-up, +Y forward.</summary>
    LeftHandedZUp,
}
