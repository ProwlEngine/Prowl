// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.MeshFeatures;

/// <summary>
/// A Signed Distance Field for a mesh, stored as a single-channel float 3D texture.
/// Distances are measured in mesh-local units. Sign convention: negative inside, positive outside.
/// </summary>
/// <remarks>
/// Created by <see cref="Prowl.Runtime.MeshFeatures.Generation.SDFGenerator"/> during asset
/// import. Treat as read-only once imported — to change it, edit the parent asset's importer
/// settings and reimport.
///
/// Volume sampling (GPU shader):
///   uvw = (localPos - Bounds.Min) / (Bounds.Max - Bounds.Min);
///   distance = texture(volume, uvw).r;
/// </remarks>
public sealed class MeshSDF : EngineObject, IMeshFeature
{
    /// <summary>Float3D texture holding signed distances, layout matches <see cref="Resolution"/>.</summary>
    public Texture3D? Volume;

    /// <summary>World-agnostic bounds the volume covers, in mesh-local coordinates.</summary>
    public AABB Bounds;

    /// <summary>Voxel grid resolution (X/Y/Z counts).</summary>
    public Int3 Resolution;

    /// <summary>Extra margin around the source mesh bounds, in mesh-local units.</summary>
    public float Padding;

    /// <summary>
    /// Distance values are clamped to this during generation. Useful for narrow-band SDFs
    /// and for normalizing for visualization.
    /// </summary>
    public float MaxDistance;

    public MeshSDF() { }

    public void Serialize(ref EchoObject compoundTag, SerializationContext ctx)
    {
        compoundTag.Add("Bounds.Min.X", new(Bounds.Min.X));
        compoundTag.Add("Bounds.Min.Y", new(Bounds.Min.Y));
        compoundTag.Add("Bounds.Min.Z", new(Bounds.Min.Z));
        compoundTag.Add("Bounds.Max.X", new(Bounds.Max.X));
        compoundTag.Add("Bounds.Max.Y", new(Bounds.Max.Y));
        compoundTag.Add("Bounds.Max.Z", new(Bounds.Max.Z));
        compoundTag.Add("Res.X", new(Resolution.X));
        compoundTag.Add("Res.Y", new(Resolution.Y));
        compoundTag.Add("Res.Z", new(Resolution.Z));
        compoundTag.Add("Padding", new(Padding));
        compoundTag.Add("MaxDistance", new(MaxDistance));
        if (Volume != null)
            compoundTag.Add("Volume", Serializer.Serialize(typeof(Texture3D), Volume, ctx));
    }

    public void Deserialize(EchoObject value, SerializationContext ctx)
    {
        Bounds = new AABB(
            new Float3(value["Bounds.Min.X"].FloatValue, value["Bounds.Min.Y"].FloatValue, value["Bounds.Min.Z"].FloatValue),
            new Float3(value["Bounds.Max.X"].FloatValue, value["Bounds.Max.Y"].FloatValue, value["Bounds.Max.Z"].FloatValue));
        Resolution = new Int3(value["Res.X"].IntValue, value["Res.Y"].IntValue, value["Res.Z"].IntValue);
        Padding = value["Padding"].FloatValue;
        MaxDistance = value["MaxDistance"].FloatValue;
        if (value.TryGet("Volume", out var volTag))
            Volume = Serializer.Deserialize<Texture3D>(volTag, ctx);
    }
}
