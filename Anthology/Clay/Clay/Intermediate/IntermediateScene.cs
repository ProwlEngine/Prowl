// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Text.Json;

namespace Prowl.Clay.Intermediate;

/// <summary>
/// Mutable, writable intermediate form of a scene used between the format reader and the
/// post-process pipeline. Flat lists of nodes, meshes, materials, textures, skins, and
/// animations let post-process steps mutate the scene in place before bake.
/// </summary>
public sealed class IntermediateScene
{
    public IntermediateNode Root { get; set; } = new();
    public List<IntermediateNode> Nodes { get; } = [];
    public List<IntermediateMesh> Meshes { get; } = [];
    public List<IntermediateMaterial> Materials { get; } = [];
    public List<IntermediateTexture> Textures { get; } = [];
    public List<IntermediateCamera> Cameras { get; } = [];
    public List<IntermediateLight> Lights { get; } = [];
    public List<IntermediateSkin> Skins { get; } = [];
    public List<IntermediateAnimation> Animations { get; } = [];

    public CoordinateSystem SourceCoordinateSystem { get; set; } = CoordinateSystem.RightHandedYUp;
    public float SourceUnitToMeters { get; set; } = 1f;

    public Dictionary<string, JsonElement> RawExtensions { get; } = [];
    public Dictionary<string, object?> Extras { get; } = [];

    public string? FormatVersion { get; set; }
    public string? Generator { get; set; }
    public string? Copyright { get; set; }
}

/// <summary>Coordinate convention of an intermediate scene.</summary>
public enum CoordinateSystem
{
    /// <summary>Right-handed, Y-up, models facing +Z (glTF native).</summary>
    RightHandedYUp,
    /// <summary>Right-handed, Z-up, -Y forward (Blender, some FBX).</summary>
    RightHandedZUp,
    /// <summary>Left-handed, Y-up, +Z forward (DirectX convention).</summary>
    LeftHandedYUp,
    /// <summary>Left-handed, Z-up, +Y forward.</summary>
    LeftHandedZUp,
}
