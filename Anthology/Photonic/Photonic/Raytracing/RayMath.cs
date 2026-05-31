using Prowl.Vector;

namespace Prowl.Photonic.Raytracing;

/// <summary>Small shared math helpers for the ray-tracing / rasterization paths.</summary>
internal static class RayMath
{
    /// <summary>Transform a vector with implicit w (1 for points, 0 for directions). Column-major matrix.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static Float3 Transform(Float4x4 m, Float3 v, float w)
    {
        float x = m.c0.X * v.X + m.c1.X * v.Y + m.c2.X * v.Z + m.c3.X * w;
        float y = m.c0.Y * v.X + m.c1.Y * v.Y + m.c2.Y * v.Z + m.c3.Y * w;
        float z = m.c0.Z * v.X + m.c1.Z * v.Y + m.c2.Z * v.Z + m.c3.Z * w;
        return new Float3(x, y, z);
    }
}
