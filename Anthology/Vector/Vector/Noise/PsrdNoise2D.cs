// Copyright (c) Stefan Gustavson 2011-04-19. All rights reserved.
// This code is released under the conditions of the MIT license.
// See LICENSE file for details.
// https://github.com/stegu/webgl-noise

namespace Prowl.Vector;

public static partial class Noise
{
    // Helper function for rotating gradients
    static Float2 RGrad2(Float2 p, float rot)
    {
        // For more isotropic gradients, sin/cos can be used instead.
        float u = Permute(Permute(p.X) + p.Y) * 0.0243902439f + rot; // Rotate by shift
        u = Maths.Frac(u) * 6.28318530718f; // 2*pi
        return new Float2(Maths.Cos(u), Maths.Sin(u));
    }

    // 2-D tiling simplex noise with rotating gradients and analytical derivative.
    // The first component of the 3-element return vector is the noise value,
    // and the second and third components are the x and y partial derivatives.
    public static Float3 Psrd_PSRDNoise(Float2 pos, Float2 per, float rot)
    {
        // Hack: offset y slightly to hide some rare artifacts
        pos.Y += 0.01f;
        // Skew to hexagonal grid
        Float2 uv = new Float2(pos.X + pos.Y * 0.5f, pos.Y);

        Float2 i0 = Maths.Floor(uv);
        Float2 f0 = Maths.Frac(uv);
        // Traversal order
        Float2 i1 = (f0.X > f0.Y) ? new Float2(1.0f, 0.0f) : new Float2(0.0f, 1.0f);

        // Unskewed grid points in (x,y) space
        Float2 p0 = new Float2(i0.X - i0.Y * 0.5f, i0.Y);
        Float2 p1 = new Float2(p0.X + i1.X - i1.Y * 0.5f, p0.Y + i1.Y);
        Float2 p2 = new Float2(p0.X + 0.5f, p0.Y + 1.0f);

        // Integer grid point indices in (u,v) space
        i1 = i0 + i1;
        Float2 i2 = i0 + new Float2(1.0f, 1.0f);

        // Vectors in unskewed (x,y) coordinates from
        // each of the simplex corners to the evaluation point
        Float2 d0 = pos - p0;
        Float2 d1 = pos - p1;
        Float2 d2 = pos - p2;

        // Wrap i0, i1 and i2 to the desired period before gradient hashing:
        // wrap points in (x,y), map to (u,v)
        Float3 xw = Mod(new Float3(p0.X, p1.X, p2.X), new Float3(per.X));
        Float3 yw = Mod(new Float3(p0.Y, p1.Y, p2.Y), new Float3(per.Y));
        Float3 iuw = xw + 0.5f * yw;
        Float3 ivw = yw;

        // Create gradients from indices
        Float2 g0 = RGrad2(new Float2(iuw.X, ivw.X), rot);
        Float2 g1 = RGrad2(new Float2(iuw.Y, ivw.Y), rot);
        Float2 g2 = RGrad2(new Float2(iuw.Z, ivw.Z), rot);

        // Gradients dot vectors to corresponding corners
        // (The derivatives of this are simply the gradients)
        Float3 w = new Float3(Maths.Dot(g0, d0), Maths.Dot(g1, d1), Maths.Dot(g2, d2));

        // Radial weights from corners
        // 0.8 is the square of 2/sqrt(5), the distance from
        // a grid point to the nearest simplex boundary
        Float3 t = 0.8f - new Float3(Maths.Dot(d0, d0), Maths.Dot(d1, d1), Maths.Dot(d2, d2));

        // Partial derivatives for analytical gradient computation
        Float3 dtdx = -2.0f * new Float3(d0.X, d1.X, d2.X);
        Float3 dtdy = -2.0f * new Float3(d0.Y, d1.Y, d2.Y);

        // Set influence of each surflet to zero outside radius sqrt(0.8)
        if (t.X < 0.0f)
        {
            dtdx.X = 0.0f;
            dtdy.X = 0.0f;
            t.X = 0.0f;
        }
        if (t.Y < 0.0f)
        {
            dtdx.Y = 0.0f;
            dtdy.Y = 0.0f;
            t.Y = 0.0f;
        }
        if (t.Z < 0.0f)
        {
            dtdx.Z = 0.0f;
            dtdy.Z = 0.0f;
            t.Z = 0.0f;
        }

        // Fourth power of t (and third power for derivative)
        Float3 t2 = t * t;
        Float3 t4 = t2 * t2;
        Float3 t3 = t2 * t;

        // Final noise value is:
        // sum of ((radial weights) times (gradient dot vector from corner))
        float n = Maths.Dot(t4, w);

        // Final analytical derivative (gradient of a sum of scalar products)
        Float2 dt0 = new Float2(dtdx.X, dtdy.X) * 4.0f * t3.X;
        Float2 dn0 = t4.X * g0 + dt0 * w.X;
        Float2 dt1 = new Float2(dtdx.Y, dtdy.Y) * 4.0f * t3.Y;
        Float2 dn1 = t4.Y * g1 + dt1 * w.Y;
        Float2 dt2 = new Float2(dtdx.Z, dtdy.Z) * 4.0f * t3.Z;
        Float2 dn2 = t4.Z * g2 + dt2 * w.Z;

        return 11.0f * new Float3(n, dn0 + dn1 + dn2);
    }

    // 2-D tiling simplex noise with fixed gradients and analytical derivative.
    // This function is implemented as a wrapper to "psrdnoise",
    // at the minimal cost of three extra additions.
    public static Float3 Psrd_PSDNoise(Float2 pos, Float2 per)
    {
        return Psrd_PSRDNoise(pos, per, 0.0f);
    }

    // 2-D tiling simplex noise with rotating gradients,
    // but without the analytical derivative.
    public static float Psrd_PSRNoise(Float2 pos, Float2 per, float rot)
    {
        // Offset y slightly to hide some rare artifacts
        pos.Y += 0.001f;
        // Skew to hexagonal grid
        Float2 uv = new Float2(pos.X + pos.Y * 0.5f, pos.Y);

        Float2 i0 = Maths.Floor(uv);
        Float2 f0 = Maths.Frac(uv);
        // Traversal order
        Float2 i1 = (f0.X > f0.Y) ? new Float2(1.0f, 0.0f) : new Float2(0.0f, 1.0f);

        // Unskewed grid points in (x,y) space
        Float2 p0 = new Float2(i0.X - i0.Y * 0.5f, i0.Y);
        Float2 p1 = new Float2(p0.X + i1.X - i1.Y * 0.5f, p0.Y + i1.Y);
        Float2 p2 = new Float2(p0.X + 0.5f, p0.Y + 1.0f);

        // Integer grid point indices in (u,v) space
        i1 = i0 + i1;
        Float2 i2 = i0 + new Float2(1.0f, 1.0f);

        // Vectors in unskewed (x,y) coordinates from
        // each of the simplex corners to the evaluation point
        Float2 d0 = pos - p0;
        Float2 d1 = pos - p1;
        Float2 d2 = pos - p2;

        // Wrap i0, i1 and i2 to the desired period before gradient hashing:
        // wrap points in (x,y), map to (u,v)
        Float3 xw = Mod(new Float3(p0.X, p1.X, p2.X), new Float3(per.X));
        Float3 yw = Mod(new Float3(p0.Y, p1.Y, p2.Y), new Float3(per.Y));
        Float3 iuw = xw + 0.5f * yw;
        Float3 ivw = yw;

        // Create gradients from indices
        Float2 g0 = RGrad2(new Float2(iuw.X, ivw.X), rot);
        Float2 g1 = RGrad2(new Float2(iuw.Y, ivw.Y), rot);
        Float2 g2 = RGrad2(new Float2(iuw.Z, ivw.Z), rot);

        // Gradients dot vectors to corresponding corners
        // (The derivatives of this are simply the gradients)
        Float3 w = new Float3(Maths.Dot(g0, d0), Maths.Dot(g1, d1), Maths.Dot(g2, d2));

        // Radial weights from corners
        // 0.8 is the square of 2/sqrt(5), the distance from
        // a grid point to the nearest simplex boundary
        Float3 t = 0.8f - new Float3(Maths.Dot(d0, d0), Maths.Dot(d1, d1), Maths.Dot(d2, d2));

        // Set influence of each surflet to zero outside radius sqrt(0.8)
        t = Maths.Max(t, new Float3(0.0f));

        // Fourth power of t
        Float3 t2 = t * t;
        Float3 t4 = t2 * t2;

        // Final noise value is:
        // sum of ((radial weights) times (gradient dot vector from corner))
        float n = Maths.Dot(t4, w);

        // Rescale to cover the range [-1,1] reasonably well
        return 11.0f * n;
    }

    // 2-D tiling simplex noise with fixed gradients,
    // without the analytical derivative.
    // This function is implemented as a wrapper to "psrnoise",
    // at the minimal cost of three extra additions.
    public static float Psrd_PSNoise(Float2 pos, Float2 per)
    {
        return Psrd_PSRNoise(pos, per, 0.0f);
    }

    // 2-D non-tiling simplex noise with rotating gradients and analytical derivative.
    // The first component of the 3-element return vector is the noise value,
    // and the second and third components are the x and y partial derivatives.
    public static Float3 Psrd_SRDNoise(Float2 pos, float rot)
    {
        // Offset y slightly to hide some rare artifacts
        pos.Y += 0.001f;
        // Skew to hexagonal grid
        Float2 uv = new Float2(pos.X + pos.Y * 0.5f, pos.Y);

        Float2 i0 = Maths.Floor(uv);
        Float2 f0 = Maths.Frac(uv);
        // Traversal order
        Float2 i1 = (f0.X > f0.Y) ? new Float2(1.0f, 0.0f) : new Float2(0.0f, 1.0f);

        // Unskewed grid points in (x,y) space
        Float2 p0 = new Float2(i0.X - i0.Y * 0.5f, i0.Y);
        Float2 p1 = new Float2(p0.X + i1.X - i1.Y * 0.5f, p0.Y + i1.Y);
        Float2 p2 = new Float2(p0.X + 0.5f, p0.Y + 1.0f);

        // Integer grid point indices in (u,v) space
        i1 = i0 + i1;
        Float2 i2 = i0 + new Float2(1.0f, 1.0f);

        // Vectors in unskewed (x,y) coordinates from
        // each of the simplex corners to the evaluation point
        Float2 d0 = pos - p0;
        Float2 d1 = pos - p1;
        Float2 d2 = pos - p2;

        Float3 x = new Float3(p0.X, p1.X, p2.X);
        Float3 y = new Float3(p0.Y, p1.Y, p2.Y);
        Float3 iuw = x + 0.5f * y;
        Float3 ivw = y;

        // Avoid precision issues in permutation
        iuw = Mod289(iuw);
        ivw = Mod289(ivw);

        // Create gradients from indices
        Float2 g0 = RGrad2(new Float2(iuw.X, ivw.X), rot);
        Float2 g1 = RGrad2(new Float2(iuw.Y, ivw.Y), rot);
        Float2 g2 = RGrad2(new Float2(iuw.Z, ivw.Z), rot);

        // Gradients dot vectors to corresponding corners
        // (The derivatives of this are simply the gradients)
        Float3 w = new Float3(Maths.Dot(g0, d0), Maths.Dot(g1, d1), Maths.Dot(g2, d2));

        // Radial weights from corners
        // 0.8 is the square of 2/sqrt(5), the distance from
        // a grid point to the nearest simplex boundary
        Float3 t = 0.8f - new Float3(Maths.Dot(d0, d0), Maths.Dot(d1, d1), Maths.Dot(d2, d2));

        // Partial derivatives for analytical gradient computation
        Float3 dtdx = -2.0f * new Float3(d0.X, d1.X, d2.X);
        Float3 dtdy = -2.0f * new Float3(d0.Y, d1.Y, d2.Y);

        // Set influence of each surflet to zero outside radius sqrt(0.8)
        if (t.X < 0.0f)
        {
            dtdx.X = 0.0f;
            dtdy.X = 0.0f;
            t.X = 0.0f;
        }
        if (t.Y < 0.0f)
        {
            dtdx.Y = 0.0f;
            dtdy.Y = 0.0f;
            t.Y = 0.0f;
        }
        if (t.Z < 0.0f)
        {
            dtdx.Z = 0.0f;
            dtdy.Z = 0.0f;
            t.Z = 0.0f;
        }

        // Fourth power of t (and third power for derivative)
        Float3 t2 = t * t;
        Float3 t4 = t2 * t2;
        Float3 t3 = t2 * t;

        // Final noise value is:
        // sum of ((radial weights) times (gradient dot vector from corner))
        float n = Maths.Dot(t4, w);

        // Final analytical derivative (gradient of a sum of scalar products)
        Float2 dt0 = new Float2(dtdx.X, dtdy.X) * 4.0f * t3.X;
        Float2 dn0 = t4.X * g0 + dt0 * w.X;
        Float2 dt1 = new Float2(dtdx.Y, dtdy.Y) * 4.0f * t3.Y;
        Float2 dn1 = t4.Y * g1 + dt1 * w.Y;
        Float2 dt2 = new Float2(dtdx.Z, dtdy.Z) * 4.0f * t3.Z;
        Float2 dn2 = t4.Z * g2 + dt2 * w.Z;

        return 11.0f * new Float3(n, dn0 + dn1 + dn2);
    }

    // 2-D non-tiling simplex noise with fixed gradients and analytical derivative.
    // This function is implemented as a wrapper to "srdnoise",
    // at the minimal cost of three extra additions.
    public static Float3 Psrd_SDNoise(Float2 pos)
    {
        return Psrd_SRDNoise(pos, 0.0f);
    }

    // 2-D non-tiling simplex noise with rotating gradients,
    // without the analytical derivative.
    public static float SRNoise(Float2 pos, float rot)
    {
        // Offset y slightly to hide some rare artifacts
        pos.Y += 0.001f;
        // Skew to hexagonal grid
        Float2 uv = new Float2(pos.X + pos.Y * 0.5f, pos.Y);

        Float2 i0 = Maths.Floor(uv);
        Float2 f0 = Maths.Frac(uv);
        // Traversal order
        Float2 i1 = (f0.X > f0.Y) ? new Float2(1.0f, 0.0f) : new Float2(0.0f, 1.0f);

        // Unskewed grid points in (x,y) space
        Float2 p0 = new Float2(i0.X - i0.Y * 0.5f, i0.Y);
        Float2 p1 = new Float2(p0.X + i1.X - i1.Y * 0.5f, p0.Y + i1.Y);
        Float2 p2 = new Float2(p0.X + 0.5f, p0.Y + 1.0f);

        // Integer grid point indices in (u,v) space
        i1 = i0 + i1;
        Float2 i2 = i0 + new Float2(1.0f, 1.0f);

        // Vectors in unskewed (x,y) coordinates from
        // each of the simplex corners to the evaluation point
        Float2 d0 = pos - p0;
        Float2 d1 = pos - p1;
        Float2 d2 = pos - p2;

        // Wrap i0, i1 and i2 to the desired period before gradient hashing:
        // wrap points in (x,y), map to (u,v)
        Float3 x = new Float3(p0.X, p1.X, p2.X);
        Float3 y = new Float3(p0.Y, p1.Y, p2.Y);
        Float3 iuw = x + 0.5f * y;
        Float3 ivw = y;

        // Avoid precision issues in permutation
        iuw = Mod289(iuw);
        ivw = Mod289(ivw);

        // Create gradients from indices
        Float2 g0 = RGrad2(new Float2(iuw.X, ivw.X), rot);
        Float2 g1 = RGrad2(new Float2(iuw.Y, ivw.Y), rot);
        Float2 g2 = RGrad2(new Float2(iuw.Z, ivw.Z), rot);

        // Gradients dot vectors to corresponding corners
        // (The derivatives of this are simply the gradients)
        Float3 w = new Float3(Maths.Dot(g0, d0), Maths.Dot(g1, d1), Maths.Dot(g2, d2));

        // Radial weights from corners
        // 0.8 is the square of 2/sqrt(5), the distance from
        // a grid point to the nearest simplex boundary
        Float3 t = 0.8f - new Float3(Maths.Dot(d0, d0), Maths.Dot(d1, d1), Maths.Dot(d2, d2));

        // Set influence of each surflet to zero outside radius sqrt(0.8)
        t = Maths.Max(t, new Float3(0.0f));

        // Fourth power of t
        Float3 t2 = t * t;
        Float3 t4 = t2 * t2;

        // Final noise value is:
        // sum of ((radial weights) times (gradient dot vector from corner))
        float n = Maths.Dot(t4, w);

        // Rescale to cover the range [-1,1] reasonably well
        return 11.0f * n;
    }

    // 2-D non-tiling simplex noise with fixed gradients,
    // without the analytical derivative.
    // This function is implemented as a wrapper to "srnoise",
    // at the minimal cost of three extra additions.
    // Note: if this kind of noise is all you want, there are faster
    // implementations of non-tiling simplex noise out there.
    // This one is included mainly for completeness and compatibility
    // with the other functions.
    public static float Psrd_SNoise(Float2 pos)
    {
        return SRNoise(pos, 0.0f);
    }
}
