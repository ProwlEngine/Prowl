// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Runtime light-probe sampler. Built from a baked set of probe positions + SH and a precomputed
/// tetrahedralization (tetra vertex indices + per-face neighbour links, produced by the editor's
/// bake). <see cref="SampleSH"/> locates the tetrahedron containing a world position by walking the
/// neighbour graph, then barycentric-blends the four corner probes' SH — Unity's light-probe model.
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
        if (_tetCount == 0) return _sh[NearestProbe(worldPos)];

        int tet = (_lastTet >= 0 && _lastTet < _tetCount) ? _lastTet : 0;
        // Bounded walk: hop across the most-negative barycentric face toward the containing tet.
        for (int step = 0; step < _tetCount + 8; step++)
        {
            Barycentric(tet, worldPos, out float b0, out float b1, out float b2, out float b3);

            int face = -1;
            float worst = -1e-5f;
            if (b0 < worst) { worst = b0; face = 0; }
            if (b1 < worst) { worst = b1; face = 1; }
            if (b2 < worst) { worst = b2; face = 2; }
            if (b3 < worst) { worst = b3; face = 3; }

            if (face == -1)
            {
                _lastTet = tet;
                return BlendTet(tet, b0, b1, b2, b3);
            }

            int nb = _neighbours[tet * 4 + face];
            if (nb < 0)
            {
                // Outside the hull through this face: clamp the barycentrics to this boundary tet.
                _lastTet = tet;
                if (b0 < 0) b0 = 0; if (b1 < 0) b1 = 0; if (b2 < 0) b2 = 0; if (b3 < 0) b3 = 0;
                float s = b0 + b1 + b2 + b3;
                if (s <= 1e-12f) return _sh[NearestProbe(worldPos)];
                float inv = 1f / s;
                return BlendTet(tet, b0 * inv, b1 * inv, b2 * inv, b3 * inv);
            }
            tet = nb;
        }
        return _sh[NearestProbe(worldPos)];
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

    /// <summary>Barycentric coords of <paramref name="p"/> in tetra <paramref name="tet"/> (b0 = corner 0, etc.).</summary>
    private void Barycentric(int tet, Float3 p, out float b0, out float b1, out float b2, out float b3)
    {
        int o = tet * 4;
        Float3 a = _positions[_tetra[o]];
        Float3 v1 = _positions[_tetra[o + 1]] - a;
        Float3 v2 = _positions[_tetra[o + 2]] - a;
        Float3 v3 = _positions[_tetra[o + 3]] - a;
        Float3 vp = p - a;

        // Solve [v1 v2 v3] * (b1,b2,b3)^T = vp  via Cramer's rule.
        float d = Det(v1, v2, v3);
        if (System.Math.Abs(d) < 1e-20f) { b0 = 1; b1 = b2 = b3 = 0; return; }
        float inv = 1f / d;
        b1 = Det(vp, v2, v3) * inv;
        b2 = Det(v1, vp, v3) * inv;
        b3 = Det(v1, v2, vp) * inv;
        b0 = 1f - b1 - b2 - b3;
    }

    private static float Det(Float3 a, Float3 b, Float3 c)
        => (float)(a.X * (b.Y * c.Z - b.Z * c.Y)
                 - a.Y * (b.X * c.Z - b.Z * c.X)
                 + a.Z * (b.X * c.Y - b.Y * c.X));

    private int NearestProbe(Float3 p)
    {
        int best = 0; float bestD = float.MaxValue;
        for (int i = 0; i < _positions.Length; i++)
        {
            Float3 d = _positions[i] - p;
            float dsq = (float)(d.X * d.X + d.Y * d.Y + d.Z * d.Z);
            if (dsq < bestD) { bestD = dsq; best = i; }
        }
        return best;
    }
}
