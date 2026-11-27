// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Photonic.Raytracing;
using Prowl.Photonic.Sampling;
using Prowl.Vector;

namespace Prowl.Photonic.Rasterization;

/// <summary>
/// Post-processes the rasterized texel samples against the scene before any lighting is computed:
/// picks the Phong-tessellated position where it is safe to use, and pushes samples that landed
/// inside solid geometry back out into the open.
/// </summary>
/// <remarks>
/// Both passes exist to fix artifacts that come from a texel being an <i>area</i> while lighting is
/// evaluated at a <i>point</i>. The smooth position removes faceted self-shadowing on low-poly
/// curved surfaces; the push-out removes the dark halos where a texel straddles a wall or a floor
/// junction and its centre ends up behind the surface it belongs to.
/// </remarks>
internal static class SamplePositionRefiner
{
    /// <summary>Tangential probe length as a multiple of the texel's world half-width: the texel diagonal.</summary>
    private const float ProbeDiagonal = 1.4142135f;

    /// <summary>
    /// Width of the contact line, in texels. Two is enough to carry a junction: the line only has to
    /// be wide enough that the points elected along it sit on the contact rather than beside it.
    /// </summary>
    private const float ContactWidthTexels = 2f;

    /// <summary>
    /// Probe directions for the proximity test, as (tilt from the normal, azimuth). Straight up plus
    /// two rings: the shallow ring finds walls and neighbouring objects, the steep one finds anything
    /// resting on or overhanging the surface.
    /// </summary>
    private static readonly (float Tilt, float Azimuth)[] ProximityProbes = BuildProximityProbes();

    private static (float, float)[] BuildProximityProbes()
    {
        var probes = new System.Collections.Generic.List<(float, float)> { (0f, 0f) };
        for (int i = 0; i < 4; i++) probes.Add((45f, i * 90f));
        for (int i = 0; i < 4; i++) probes.Add((72f, i * 90f + 45f));
        return probes.ToArray();
    }

    public static void Run(TargetWorkspace[] workspaces, Blas blas, BakeOptions options,
                           int triangleCount, System.Threading.Tasks.ParallelOptions parallelOpts)
    {
        if (!options.Diagnostics.DisableSmoothTerminator)
        {
            var useFlat = new bool[triangleCount];
            MarkOccludedTriangles(workspaces, blas, options, useFlat, parallelOpts);
            ApplySmoothPositions(workspaces, useFlat, parallelOpts);
        }

        if (!options.Diagnostics.DisableShadowLeakFix)
            PushOutOfSolids(workspaces, blas, options, parallelOpts);

        if (options.SparseStride > 1)
            MeasureProximity(workspaces, blas, options, parallelOpts);
    }

    /// <summary>
    /// Record how close the nearest other surface is, in each texel's own hemisphere. This is what
    /// tells sparse sampling where it is not allowed to be sparse: the ring of floor around an object
    /// resting on it, the base of a wall, the inside of a corner. Those are where contact shadows live
    /// and where interpolating across a junction would carry light through it.
    /// </summary>
    private static void MeasureProximity(TargetWorkspace[] workspaces, Blas blas, BakeOptions options,
                                         System.Threading.Tasks.ParallelOptions parallelOpts)
    {
        const float contactTexels = ContactWidthTexels;

        foreach (var ws in workspaces)
        {
            int W = ws.Width, H = ws.Height;
            System.Threading.Tasks.Parallel.For(0, H, parallelOpts, y =>
            {
                for (int x = 0; x < W; x++)
                {
                    int idx = y * W + x;
                    if (!ws.Covered[idx]) continue;
                    var s = ws.Samples[idx];

                    float radius = s.WorldRadius * 2f * contactTexels;
                    if (radius <= options.EffectiveRayBias) continue;

                    Hemisphere.BuildOrthonormalBasis(s.Normal, out var tangent, out var bitangent);
                    var origin = RayMath.OffsetOrigin(s.Position, s.FaceNormal, options.EffectiveRayBias);

                    float nearest = radius;
                    foreach (var probe in ProximityProbes)
                    {
                        float tilt = probe.Tilt * (System.MathF.PI / 180f);
                        float azimuth = probe.Azimuth * (System.MathF.PI / 180f);
                        float sin = System.MathF.Sin(tilt);
                        var direction = Float3.Normalize(
                            s.Normal * System.MathF.Cos(tilt)
                            + tangent * (sin * System.MathF.Cos(azimuth))
                            + bitangent * (sin * System.MathF.Sin(azimuth)));

                        if (!blas.ClosestHit(origin, direction, options.EffectiveRayBias, nearest, out float hitT, out _, out _, out int triIndex))
                            continue;

                        // Only surfaces turned towards this texel shade it or bounce onto it. Skipping
                        // the rest keeps a curved surface from flagging itself a contact everywhere its
                        // own geometry happens to be within reach at a grazing angle.
                        if (Float3.Dot(GeometricNormal(blas, triIndex), direction) > -0.2f) continue;
                        nearest = System.MathF.Min(nearest, hitT);
                    }

                    ws.Samples[idx].Proximity = nearest / radius;
                }
            });
        }
    }

    /// <summary>
    /// A smooth position that is separated from its flat position by geometry would light this texel
    /// as if it were floating inside the neighbouring surface, so the whole triangle falls back to
    /// flat. Deciding per triangle rather than per texel keeps a single face from being half smooth
    /// and half flat, which would show up as a hard discontinuity mid-surface.
    /// </summary>
    private static void MarkOccludedTriangles(TargetWorkspace[] workspaces, Blas blas, BakeOptions options,
                                              bool[] useFlat, System.Threading.Tasks.ParallelOptions parallelOpts)
    {
        foreach (var ws in workspaces)
        {
            int W = ws.Width, H = ws.Height;
            System.Threading.Tasks.Parallel.For(0, H, parallelOpts, y =>
            {
                for (int x = 0; x < W; x++)
                {
                    int idx = y * W + x;
                    if (!ws.Covered[idx]) continue;
                    var s = ws.Samples[idx];
                    if (s.TriangleId < 0 || s.TriangleId >= useFlat.Length || useFlat[s.TriangleId]) continue;

                    var delta = s.SmoothPosition - s.Position;
                    float distance = Float3.Length(delta);
                    if (distance <= options.EffectiveRayBias) continue;

                    var origin = RayMath.OffsetOrigin(s.Position, s.FaceNormal, options.EffectiveRayBias);
                    if (blas.AnyHit(origin, delta / distance, options.EffectiveRayBias, distance))
                        useFlat[s.TriangleId] = true;
                }
            });
        }
    }

    private static void ApplySmoothPositions(TargetWorkspace[] workspaces, bool[] useFlat,
                                             System.Threading.Tasks.ParallelOptions parallelOpts)
    {
        foreach (var ws in workspaces)
        {
            int W = ws.Width, H = ws.Height;
            System.Threading.Tasks.Parallel.For(0, H, parallelOpts, y =>
            {
                for (int x = 0; x < W; x++)
                {
                    int idx = y * W + x;
                    if (!ws.Covered[idx]) continue;
                    int id = ws.Samples[idx].TriangleId;
                    if (id >= 0 && id < useFlat.Length && useFlat[id]) continue;
                    ws.Samples[idx].Position = ws.Samples[idx].SmoothPosition;
                }
            });
        }
    }

    /// <summary>
    /// Fire four tangential probes one texel-diagonal long. Hitting a back face means this sample sits
    /// inside a solid, so it is moved to the far side of that face: the leaking shadow becomes the
    /// lighting of the surface the texel is actually part of.
    /// </summary>
    private static void PushOutOfSolids(TargetWorkspace[] workspaces, Blas blas, BakeOptions options,
                                        System.Threading.Tasks.ParallelOptions parallelOpts)
    {
        foreach (var ws in workspaces)
        {
            int W = ws.Width, H = ws.Height;
            System.Threading.Tasks.Parallel.For(0, H, parallelOpts, y =>
            {
                for (int x = 0; x < W; x++)
                {
                    int idx = y * W + x;
                    if (!ws.Covered[idx]) continue;
                    var s = ws.Samples[idx];
                    float reach = s.WorldRadius * ProbeDiagonal;
                    if (reach <= options.EffectiveRayBias) continue;

                    Hemisphere.BuildOrthonormalBasis(s.FaceNormal, out var tangent, out var bitangent);
                    var origin = RayMath.OffsetOrigin(s.Position, s.FaceNormal, options.EffectiveRayBias);

                    // Take the nearest wall, not the first one probed. On a thin double-sided wall the
                    // probes hit both of its faces, and stepping past the far one drops the sample on
                    // the wrong side of it, which is what carries a room's light out onto the floor
                    // outside. The nearest hit is the surface this texel actually belongs against.
                    float nearest = float.MaxValue;
                    Float3 pushed = default, pushNormal = default;
                    for (int d = 0; d < 4; d++)
                    {
                        var dir = d switch
                        {
                            0 => tangent,
                            1 => -tangent,
                            2 => bitangent,
                            _ => -bitangent,
                        };
                        if (!blas.ClosestHit(origin, dir, options.EffectiveRayBias, reach, out float hitT, out _, out _, out int triIndex))
                            continue;
                        if (hitT >= nearest) continue;

                        var hitNormal = GeometricNormal(blas, triIndex);
                        if (Float3.Dot(hitNormal, dir) <= 0f) continue; // front face: nothing is leaking here

                        nearest = hitT;
                        pushed = s.Position + dir * hitT;
                        pushNormal = hitNormal;
                    }

                    if (nearest < float.MaxValue)
                        ws.Samples[idx].Position = RayMath.OffsetOrigin(pushed, pushNormal, options.EffectiveRayBias);
                }
            });
        }
    }

    /// <summary>Geometric normal of a merged-BLAS triangle, oriented to agree with its vertex normals.</summary>
    private static Float3 GeometricNormal(Blas blas, int triIndex)
    {
        var tri = blas.Triangles[triIndex];
        var positions = blas.Mesh.Positions;
        var normals = blas.Mesh.Normals;
        var geometric = Float3.Cross(positions[tri.I1] - positions[tri.I0], positions[tri.I2] - positions[tri.I0]);
        var shading = normals[tri.I0] + normals[tri.I1] + normals[tri.I2];
        if (Float3.Dot(geometric, shading) < 0f) geometric = -geometric;
        return Float3.NormalizeSafe(geometric, Float3.UnitY);
    }
}
