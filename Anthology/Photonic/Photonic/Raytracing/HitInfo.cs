using Prowl.Vector;

namespace Prowl.Photonic.Raytracing;

/// <summary>Result of a closest-hit query against the scene BVH.</summary>
internal struct HitInfo
{
    /// <summary>True when something was hit.</summary>
    public bool Hit;

    /// <summary>Hit distance along the incoming ray.</summary>
    public float Distance;

    /// <summary>Barycentric (u, v) inside the triangle (w = 1 - u - v on the first vertex).</summary>
    public float U, V;

    /// <summary>Index into the TLAS instance list.</summary>
    public int InstanceIndex;

    /// <summary>Triangle entry index within the hit mesh's BLAS.</summary>
    public int TriangleIndex;

    /// <summary>World-space hit position.</summary>
    public Float3 Position;

    /// <summary>Interpolated world-space normal (renormalised).</summary>
    public Float3 Normal;
}
