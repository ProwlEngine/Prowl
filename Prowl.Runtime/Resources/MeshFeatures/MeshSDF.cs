// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.MeshFeatures;

/// <summary>
/// A Signed Distance Field for a mesh, stored as a single channel 16 bit 3D texture of cubic voxels.
/// Distances are measured in mesh-local units. Sign convention: negative inside, positive outside.
/// </summary>
/// <remarks>
/// Created by <see cref="Prowl.Runtime.MeshFeatures.Generation.SDFGenerator"/> during asset
/// import with the project's SDF settings. Treat as read-only, changing those settings reimports every mesh.
///
/// Volume sampling (GPU shader):
///   uvw = (localPos - Bounds.Min) / (Bounds.Max - Bounds.Min);
///   distance = texture(volume, uvw).r * MaxDistance;
/// </remarks>
public sealed class MeshSDF : Asset, IMeshFeature
{
    private Texture3D? _volume;
    private AABB _bounds;
    private Int3 _resolution;
    private float _voxelSize;
    private float _maxDistance;

    /// <summary>Signed distances divided by <see cref="MaxDistance"/>, as signed normalized shorts. Layout matches <see cref="Resolution"/>.</summary>
    public Texture3D? Volume { get { EnsureLoaded(); return _volume; } set { EnsureLoaded(); _volume = value; } }

    /// <summary>World-agnostic bounds the volume covers, in mesh-local coordinates.</summary>
    public AABB Bounds { get { EnsureLoaded(); return _bounds; } set { EnsureLoaded(); _bounds = value; } }

    /// <summary>Voxel grid resolution (X/Y/Z counts).</summary>
    public Int3 Resolution { get { EnsureLoaded(); return _resolution; } set { EnsureLoaded(); _resolution = value; } }

    /// <summary>Edge length of every voxel, in mesh-local units.</summary>
    public float VoxelSize { get { EnsureLoaded(); return _voxelSize; } set { EnsureLoaded(); _voxelSize = value; } }

    /// <summary>The distance a stored value of one stands for, the diagonal of <see cref="Bounds"/>.</summary>
    public float MaxDistance { get { EnsureLoaded(); return _maxDistance; } set { EnsureLoaded(); _maxDistance = value; } }

    public MeshSDF() { }

    public void Serialize(ref EchoObject compoundTag, SerializationContext ctx)
    {
        SerializeHeader(compoundTag);
        compoundTag.Add("Bounds.Min.X", new(Bounds.Min.X));
        compoundTag.Add("Bounds.Min.Y", new(Bounds.Min.Y));
        compoundTag.Add("Bounds.Min.Z", new(Bounds.Min.Z));
        compoundTag.Add("Bounds.Max.X", new(Bounds.Max.X));
        compoundTag.Add("Bounds.Max.Y", new(Bounds.Max.Y));
        compoundTag.Add("Bounds.Max.Z", new(Bounds.Max.Z));
        compoundTag.Add("Res.X", new(Resolution.X));
        compoundTag.Add("Res.Y", new(Resolution.Y));
        compoundTag.Add("Res.Z", new(Resolution.Z));
        compoundTag.Add("VoxelSize", new(VoxelSize));
        compoundTag.Add("MaxDistance", new(MaxDistance));
        if (Volume != null)
            compoundTag.Add("Volume", Serializer.Serialize(typeof(Texture3D), Volume, ctx));
    }

    public void Deserialize(EchoObject value, SerializationContext ctx)
    {
        DeserializeHeader(value);
        Bounds = new AABB(
            new Float3(value["Bounds.Min.X"].FloatValue, value["Bounds.Min.Y"].FloatValue, value["Bounds.Min.Z"].FloatValue),
            new Float3(value["Bounds.Max.X"].FloatValue, value["Bounds.Max.Y"].FloatValue, value["Bounds.Max.Z"].FloatValue));
        Resolution = new Int3(value["Res.X"].IntValue, value["Res.Y"].IntValue, value["Res.Z"].IntValue);
        VoxelSize = value["VoxelSize"].FloatValue;
        MaxDistance = value["MaxDistance"].FloatValue;
        if (value.TryGet("Volume", out var volTag))
            Volume = Serializer.Deserialize<Texture3D>(volTag, ctx);
    }
}
