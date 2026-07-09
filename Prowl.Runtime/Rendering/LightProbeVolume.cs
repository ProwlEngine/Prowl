// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Runtime light-probe sampler. Built from a baked set of probe positions + SH and a precomputed
/// tetrahedralization (tetra vertex indices + per-face neighbour links, produced by the editor's
/// bake). <see cref="SampleSH"/> locates the tetrahedron containing a world position, then
/// barycentric-blends the four corner probes' SH; points outside the hull fall back to a nearby blend.
/// </summary>
public sealed class LightProbeVolume
{
    private readonly Float3[] _positions;
    private readonly SphericalHarmonicsL2[] _sh;
    private readonly int[] _tetra;       // 4 vertex indices per tetrahedron
    private readonly int[] _neighbours;  // 4 neighbour-tet indices per tetra (across the face opposite vertex i); -1 = hull
    private readonly int _tetCount;
    private int _lastTet;                // walk start cache (single-threaded sampling)

    public bool HasProbes => _sh.Length > 0;

    public LightProbeVolume(Float3[] positions, SphericalHarmonicsL2[] sh, int[] tetra, int[] neighbours)
    {
        _positions = positions ?? [];
        _sh = sh ?? [];
        _tetra = tetra ?? [];
        _neighbours = neighbours ?? [];
        _tetCount = _tetra.Length / 4;
    }

    /// <summary>
    /// Interpolated SH at <paramref name="worldPos"/>. Falls back to the nearest probe when there
    /// are too few probes to tetrahedralize, or when the point is outside the probe hull.
    /// </summary>
    public SphericalHarmonicsL2 SampleSH(Float3 worldPos)
    {
        if (_sh.Length == 0) return default;
        if (_tetCount == 0) return BlendNearest(worldPos);

        const float inside = -1e-4f; // small slack so a point on a shared face is "inside" both tets

        // Frame coherence: a renderer is usually still inside the tet it sampled last frame.
        if (_lastTet >= 0 && _lastTet < _tetCount &&
            ComputeBarycentric(_lastTet, worldPos, out float lb0, out float lb1, out float lb2, out float lb3) &&
            lb0 >= inside && lb1 >= inside && lb2 >= inside && lb3 >= inside)
            return BlendTet(_lastTet, lb0, lb1, lb2, lb3);

        // Find the tetrahedron containing the point by direct scan. A neighbour walk would be faster,
        // but the Delaunay output of a near-regular probe grid contains zero-volume (coplanar) tets:
        // the input degeneracies the tetrahedralizer's jitter breaks collapse back to flat tets on the
        // real positions. Those corrupt a walk (it lands on one and stops), so we scan and let
        // ComputeBarycentric reject the degenerate ones. O(tets) per sample, fine for probe-count sets.
        for (int t = 0; t < _tetCount; t++)
        {
            if (ComputeBarycentric(t, worldPos, out float b0, out float b1, out float b2, out float b3) &&
                b0 >= inside && b1 >= inside && b2 >= inside && b3 >= inside)
            {
                _lastTet = t;
                return BlendTet(t, b0, b1, b2, b3);
            }
        }

        // Outside the probe hull: smooth inverse-distance blend of the nearest probes. (Clamping to a
        // single boundary tet instead is continuous at the hull but flips between faces as an object
        // moves just outside it - worse for objects sitting next to, but outside, the probe volume.)
        return BlendNearest(worldPos);
    }

    private SphericalHarmonicsL2 BlendTet(int tet, float b0, float b1, float b2, float b3)
    {
        int o = tet * 4;
        System.Span<SphericalHarmonicsL2> probes = stackalloc SphericalHarmonicsL2[4]
        {
            _sh[_tetra[o]], _sh[_tetra[o + 1]], _sh[_tetra[o + 2]], _sh[_tetra[o + 3]]
        };
        System.Span<float> w = stackalloc float[4] { b0, b1, b2, b3 };
        return SphericalHarmonicsL2.Blend(probes, w);
    }

    /// <summary>
    /// Barycentric coords of <paramref name="p"/> in tetra <paramref name="tet"/> (b0 = corner 0, etc.).
    /// Returns false only when the tetrahedron is degenerate (near-zero volume) - the grid
    /// tetrahedralization can emit zero-volume slivers on the real positions - so the caller skips it.
    /// The coordinates are output even when <paramref name="p"/> is outside the tet (some negative), so
    /// the caller can both test containment and find the nearest boundary tet for extrapolation.
    /// </summary>
    private bool ComputeBarycentric(int tet, Float3 p, out float b0, out float b1, out float b2, out float b3)
    {
        int o = tet * 4;
        Float3 a = _positions[_tetra[o]];
        Float3 v1 = _positions[_tetra[o + 1]] - a;
        Float3 v2 = _positions[_tetra[o + 2]] - a;
        Float3 v3 = _positions[_tetra[o + 3]] - a;
        Float3 vp = p - a;

        b0 = b1 = b2 = b3 = 0f;
        // Solve [v1 v2 v3] * (b1,b2,b3)^T = vp via Cramer's rule.
        float d = Det(v1, v2, v3);
        if (System.Math.Abs(d) < 1e-12f) return false; // degenerate / coplanar tet
        float inv = 1f / d;
        b1 = Det(vp, v2, v3) * inv;
        b2 = Det(v1, vp, v3) * inv;
        b3 = Det(v1, v2, vp) * inv;
        b0 = 1f - b1 - b2 - b3;
        return true;
    }

    private static float Det(Float3 a, Float3 b, Float3 c)
        => (float)(a.X * (b.Y * c.Z - b.Z * c.Y)
                 - a.Y * (b.X * c.Z - b.Z * c.X)
                 + a.Z * (b.X * c.Y - b.Y * c.X));

    // Smooth fallback for points the tetrahedral walk can't serve: too few probes to tetrahedralize,
    // a layout the tetrahedralizer degenerated on (e.g. a near-regular grid), or a point outside the
    // hull. Inverse-distance-weighted blend of the nearest few probes, so the result still
    // interpolates between neighbours instead of snapping to a single probe.
    private const int FallbackBlendCount = 4;

    private SphericalHarmonicsL2 BlendNearest(Float3 p)
    {
        int count = _positions.Length;
        if (count == 0) return default;

        int k = System.Math.Min(FallbackBlendCount, count);
        System.Span<int> idx = stackalloc int[FallbackBlendCount];
        System.Span<float> dist2 = stackalloc float[FallbackBlendCount];
        for (int j = 0; j < k; j++) { idx[j] = -1; dist2[j] = float.MaxValue; }

        // Keep the k smallest squared distances via insertion into the small sorted buffer.
        for (int i = 0; i < count; i++)
        {
            Float3 d = _positions[i] - p;
            float dsq = (float)(d.X * d.X + d.Y * d.Y + d.Z * d.Z);
            if (dsq >= dist2[k - 1]) continue;
            int j = k - 1;
            while (j > 0 && dist2[j - 1] > dsq) { dist2[j] = dist2[j - 1]; idx[j] = idx[j - 1]; j--; }
            dist2[j] = dsq; idx[j] = i;
        }

        System.Span<SphericalHarmonicsL2> probes = stackalloc SphericalHarmonicsL2[FallbackBlendCount];
        System.Span<float> w = stackalloc float[FallbackBlendCount];
        float wsum = 0f;
        for (int j = 0; j < k; j++)
        {
            probes[j] = _sh[idx[j]];
            w[j] = 1f / (dist2[j] + 1e-6f); // inverse-distance-squared; nearest dominates, neighbours add the gradient
            wsum += w[j];
        }
        float inv = wsum > 0f ? 1f / wsum : 0f;
        for (int j = 0; j < k; j++) w[j] *= inv; // normalize: Blend is a plain weighted sum

        return SphericalHarmonicsL2.Blend(probes.Slice(0, k), w.Slice(0, k));
    }
}
