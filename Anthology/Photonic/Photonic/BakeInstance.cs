using Prowl.Vector;

namespace Prowl.Photonic;

/// <summary>
/// One placement of a <see cref="BakeMesh"/> into a <see cref="LightmapTarget"/>. Holds the
/// world transform, the UV transform that maps the mesh's bake UV layer into atlas space, and
/// shadow/visibility flags.
/// </summary>
public sealed class BakeInstance
{
    /// <summary>The mesh this instance represents.</summary>
    public BakeMesh Mesh { get; }

    /// <summary>Object-to-world transform applied to mesh vertices.</summary>
    public Float4x4 WorldTransform { get; set; }

    /// <summary>UV translation applied to the bake UV layer when placing this instance in the atlas.</summary>
    public Float2 UVOffset { get; set; }

    /// <summary>UV scale applied to the bake UV layer when placing this instance in the atlas.</summary>
    public Float2 UVScale { get; set; }

    /// <summary>Name of the UV layer used for atlas placement (defaults to <c>"UV1"</c>).</summary>
    public string BakeUVLayer { get; }

    /// <summary>True if this instance should cast shadows for direct lighting.</summary>
    public bool CastsShadows { get; set; } = true;

    /// <summary>True if this instance receives baked lighting (false means it occludes but isn't written into).</summary>
    public bool ReceivesLighting { get; set; } = true;

    /// <summary>The target this instance bakes into.</summary>
    public LightmapTarget Target { get; }

    internal BakeInstance(BakeMesh mesh, Float4x4 xform, Float2 offset, Float2 scale, string layer, LightmapTarget target)
    {
        Mesh = mesh;
        WorldTransform = xform;
        UVOffset = offset;
        UVScale = scale;
        BakeUVLayer = layer;
        Target = target;
    }
}
