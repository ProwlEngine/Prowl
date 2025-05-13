// Copyright (c) Stefan Gustavson 2011-04-19. All rights reserved.
// This code is released under the conditions of the MIT license.
// See LICENSE file for details.
// https://github.com/stegu/webgl-noise

namespace Prowl.Vector
{
    public static partial class Noise
    {
        // Classic Perlin noise 3D
        public static float CNoise(Float3 P)
        {
            Float3 Pi0 = Maths.Floor(P); // Integer part for indexing
            Float3 Pi1 = Pi0 + new Float3(1.0f); // Integer part + 1
            Pi0 = Mod289(Pi0);
            Pi1 = Mod289(Pi1);
            Float3 Pf0 = Maths.Frac(P); // Fractional part for interpolation
            Float3 Pf1 = Pf0 - new Float3(1.0f); // Fractional part - 1.0
            Float4 ix = new Float4(Pi0.X, Pi1.X, Pi0.X, Pi1.X);
            Float4 iy = new Float4(Pi0.Y, Pi0.Y, Pi1.Y, Pi1.Y);
            Float4 iz0 = new Float4(Pi0.Z);
            Float4 iz1 = new Float4(Pi1.Z);

            Float4 ixy = Permute(Permute(ix) + iy);
            Float4 ixy0 = Permute(ixy + iz0);
            Float4 ixy1 = Permute(ixy + iz1);

            Float4 gx0 = ixy0 * (1.0f / 7.0f);
            Float4 gy0 = Maths.Frac(Maths.Floor(gx0) * (1.0f / 7.0f)) - 0.5f;
            gx0 = Maths.Frac(gx0);
            Float4 gz0 = new Float4(0.5f) - Maths.Abs(gx0) - Maths.Abs(gy0);
            Float4 sz0 = Maths.Step(gz0, new Float4(0.0f));
            gx0 -= sz0 * (Maths.Step(new Float4(0.0f), gx0) - 0.5f);
            gy0 -= sz0 * (Maths.Step(new Float4(0.0f), gy0) - 0.5f);

            Float4 gx1 = ixy1 * (1.0f / 7.0f);
            Float4 gy1 = Maths.Frac(Maths.Floor(gx1) * (1.0f / 7.0f)) - 0.5f;
            gx1 = Maths.Frac(gx1);
            Float4 gz1 = new Float4(0.5f) - Maths.Abs(gx1) - Maths.Abs(gy1);
            Float4 sz1 = Maths.Step(gz1, new Float4(0.0f));
            gx1 -= sz1 * (Maths.Step(new Float4(0.0f), gx1) - 0.5f);
            gy1 -= sz1 * (Maths.Step(new Float4(0.0f), gy1) - 0.5f);

            Float3 g000 = new Float3(gx0.X, gy0.X, gz0.X);
            Float3 g100 = new Float3(gx0.Y, gy0.Y, gz0.Y);
            Float3 g010 = new Float3(gx0.Z, gy0.Z, gz0.Z);
            Float3 g110 = new Float3(gx0.W, gy0.W, gz0.W);
            Float3 g001 = new Float3(gx1.X, gy1.X, gz1.X);
            Float3 g101 = new Float3(gx1.Y, gy1.Y, gz1.Y);
            Float3 g011 = new Float3(gx1.Z, gy1.Z, gz1.Z);
            Float3 g111 = new Float3(gx1.W, gy1.W, gz1.W);

            Float4 norm0 = TaylorInvSqrt(new Float4(
                Maths.Dot(g000, g000),
                Maths.Dot(g010, g010),
                Maths.Dot(g100, g100),
                Maths.Dot(g110, g110)
            ));
            Float4 norm1 = TaylorInvSqrt(new Float4(
                Maths.Dot(g001, g001),
                Maths.Dot(g011, g011),
                Maths.Dot(g101, g101),
                Maths.Dot(g111, g111)
            ));

            float n000 = norm0.X * Maths.Dot(g000, Pf0);
            float n010 = norm0.Y * Maths.Dot(g010, new Float3(Pf0.X, Pf1.Y, Pf0.Z));
            float n100 = norm0.Z * Maths.Dot(g100, new Float3(Pf1.X, Pf0.Y, Pf0.Z));
            float n110 = norm0.W * Maths.Dot(g110, new Float3(Pf1.X, Pf1.Y, Pf0.Z));
            float n001 = norm1.X * Maths.Dot(g001, new Float3(Pf0.X, Pf0.Y, Pf1.Z));
            float n011 = norm1.Y * Maths.Dot(g011, new Float3(Pf0.X, Pf1.Y, Pf1.Z));
            float n101 = norm1.Z * Maths.Dot(g101, new Float3(Pf1.X, Pf0.Y, Pf1.Z));
            float n111 = norm1.W * Maths.Dot(g111, Pf1);

            Float3 fade_xyz = Fade(Pf0);
            Float4 n_z = Maths.Lerp(new Float4(n000, n100, n010, n110), new Float4(n001, n101, n011, n111), fade_xyz.Z);
            Float2 n_yz = Maths.Lerp(n_z.XY, n_z.ZW, fade_xyz.Y);
            float n_xyz = Maths.Lerp(n_yz.X, n_yz.Y, fade_xyz.X);
            return 2.2f * n_xyz;
        }

        // Classic Perlin noise 3D, periodic variant
        public static float PNoise(Float3 P, Float3 rep)
        {
            Float3 Pi0 = Mod(Maths.Floor(P), rep); // Integer part, modulo period
            Float3 Pi1 = Mod(Pi0 + new Float3(1.0f), rep); // Integer part + 1, mod period
            Pi0 = Mod289(Pi0);
            Pi1 = Mod289(Pi1);
            Float3 Pf0 = Maths.Frac(P); // Fractional part for interpolation
            Float3 Pf1 = Pf0 - new Float3(1.0f); // Fractional part - 1.0
            Float4 ix = new Float4(Pi0.X, Pi1.X, Pi0.X, Pi1.X);
            Float4 iy = new Float4(Pi0.Y, Pi0.Y, Pi1.Y, Pi1.Y);
            Float4 iz0 = new Float4(Pi0.Z);
            Float4 iz1 = new Float4(Pi1.Z);

            Float4 ixy = Permute(Permute(ix) + iy);
            Float4 ixy0 = Permute(ixy + iz0);
            Float4 ixy1 = Permute(ixy + iz1);

            Float4 gx0 = ixy0 * (1.0f / 7.0f);
            Float4 gy0 = Maths.Frac(Maths.Floor(gx0) * (1.0f / 7.0f)) - 0.5f;
            gx0 = Maths.Frac(gx0);
            Float4 gz0 = new Float4(0.5f) - Maths.Abs(gx0) - Maths.Abs(gy0);
            Float4 sz0 = Maths.Step(gz0, new Float4(0.0f));
            gx0 -= sz0 * (Maths.Step(new Float4(0.0f), gx0) - 0.5f);
            gy0 -= sz0 * (Maths.Step(new Float4(0.0f), gy0) - 0.5f);

            Float4 gx1 = ixy1 * (1.0f / 7.0f);
            Float4 gy1 = Maths.Frac(Maths.Floor(gx1) * (1.0f / 7.0f)) - 0.5f;
            gx1 = Maths.Frac(gx1);
            Float4 gz1 = new Float4(0.5f) - Maths.Abs(gx1) - Maths.Abs(gy1);
            Float4 sz1 = Maths.Step(gz1, new Float4(0.0f));
            gx1 -= sz1 * (Maths.Step(new Float4(0.0f), gx1) - 0.5f);
            gy1 -= sz1 * (Maths.Step(new Float4(0.0f), gy1) - 0.5f);

            Float3 g000 = new Float3(gx0.X, gy0.X, gz0.X);
            Float3 g100 = new Float3(gx0.Y, gy0.Y, gz0.Y);
            Float3 g010 = new Float3(gx0.Z, gy0.Z, gz0.Z);
            Float3 g110 = new Float3(gx0.W, gy0.W, gz0.W);
            Float3 g001 = new Float3(gx1.X, gy1.X, gz1.X);
            Float3 g101 = new Float3(gx1.Y, gy1.Y, gz1.Y);
            Float3 g011 = new Float3(gx1.Z, gy1.Z, gz1.Z);
            Float3 g111 = new Float3(gx1.W, gy1.W, gz1.W);

            Float4 norm0 = TaylorInvSqrt(new Float4(
                Maths.Dot(g000, g000),
                Maths.Dot(g010, g010),
                Maths.Dot(g100, g100),
                Maths.Dot(g110, g110)
            ));
            Float4 norm1 = TaylorInvSqrt(new Float4(
                Maths.Dot(g001, g001),
                Maths.Dot(g011, g011),
                Maths.Dot(g101, g101),
                Maths.Dot(g111, g111)
            ));

            float n000 = norm0.X * Maths.Dot(g000, Pf0);
            float n010 = norm0.Y * Maths.Dot(g010, new Float3(Pf0.X, Pf1.Y, Pf0.Z));
            float n100 = norm0.Z * Maths.Dot(g100, new Float3(Pf1.X, Pf0.Y, Pf0.Z));
            float n110 = norm0.W * Maths.Dot(g110, new Float3(Pf1.X, Pf1.Y, Pf0.Z));
            float n001 = norm1.X * Maths.Dot(g001, new Float3(Pf0.X, Pf0.Y, Pf1.Z));
            float n011 = norm1.Y * Maths.Dot(g011, new Float3(Pf0.X, Pf1.Y, Pf1.Z));
            float n101 = norm1.Z * Maths.Dot(g101, new Float3(Pf1.X, Pf0.Y, Pf1.Z));
            float n111 = norm1.W * Maths.Dot(g111, Pf1);

            Float3 fade_xyz = Fade(Pf0);
            Float4 n_z = Maths.Lerp(new Float4(n000, n100, n010, n110), new Float4(n001, n101, n011, n111), fade_xyz.Z);
            Float2 n_yz = Maths.Lerp(n_z.XY, n_z.ZW, fade_xyz.Y);
            float n_xyz = Maths.Lerp(n_yz.X, n_yz.Y, fade_xyz.X);
            return 2.2f * n_xyz;
        }
    }
}
