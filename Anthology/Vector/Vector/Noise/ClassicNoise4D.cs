// Copyright (c) Stefan Gustavson 2011-04-19. All rights reserved.
// This code is released under the conditions of the MIT license.
// See LICENSE file for details.
// https://github.com/stegu/webgl-noise

namespace Prowl.Vector
{
    public static partial class Noise
    {
        // Classic Perlin noise 4D
        public static float CNoise(Float4 P)
        {
            Float4 Pi0 = Maths.Floor(P); // Integer part for indexing
            Float4 Pi1 = Pi0 + 1.0f; // Integer part + 1
            Pi0 = Mod289(Pi0);
            Pi1 = Mod289(Pi1);
            Float4 Pf0 = Maths.Frac(P); // Fractional part for interpolation
            Float4 Pf1 = Pf0 - 1.0f; // Fractional part - 1.0
            Float4 ix = new Float4(Pi0.X, Pi1.X, Pi0.X, Pi1.X);
            Float4 iy = new Float4(Pi0.Y, Pi0.Y, Pi1.Y, Pi1.Y);
            Float4 iz0 = new Float4(Pi0.Z);
            Float4 iz1 = new Float4(Pi1.Z);
            Float4 iw0 = new Float4(Pi0.W);
            Float4 iw1 = new Float4(Pi1.W);

            Float4 ixy = Permute(Permute(ix) + iy);
            Float4 ixy0 = Permute(ixy + iz0);
            Float4 ixy1 = Permute(ixy + iz1);
            Float4 ixy00 = Permute(ixy0 + iw0);
            Float4 ixy01 = Permute(ixy0 + iw1);
            Float4 ixy10 = Permute(ixy1 + iw0);
            Float4 ixy11 = Permute(ixy1 + iw1);

            Float4 gx00 = ixy00 * (1.0f / 7.0f);
            Float4 gy00 = Maths.Floor(gx00) * (1.0f / 7.0f);
            Float4 gz00 = Maths.Floor(gy00) * (1.0f / 6.0f);
            gx00 = Maths.Frac(gx00) - 0.5f;
            gy00 = Maths.Frac(gy00) - 0.5f;
            gz00 = Maths.Frac(gz00) - 0.5f;
            Float4 gw00 = new Float4(0.75f) - Maths.Abs(gx00) - Maths.Abs(gy00) - Maths.Abs(gz00);
            Float4 sw00 = Maths.Step(gw00, new Float4(0.0f));
            gx00 -= sw00 * (Maths.Step(new Float4(0.0f), gx00) - 0.5f);
            gy00 -= sw00 * (Maths.Step(new Float4(0.0f), gy00) - 0.5f);

            Float4 gx01 = ixy01 * (1.0f / 7.0f);
            Float4 gy01 = Maths.Floor(gx01) * (1.0f / 7.0f);
            Float4 gz01 = Maths.Floor(gy01) * (1.0f / 6.0f);
            gx01 = Maths.Frac(gx01) - 0.5f;
            gy01 = Maths.Frac(gy01) - 0.5f;
            gz01 = Maths.Frac(gz01) - 0.5f;
            Float4 gw01 = new Float4(0.75f) - Maths.Abs(gx01) - Maths.Abs(gy01) - Maths.Abs(gz01);
            Float4 sw01 = Maths.Step(gw01, new Float4(0.0f));
            gx01 -= sw01 * (Maths.Step(new Float4(0.0f), gx01) - 0.5f);
            gy01 -= sw01 * (Maths.Step(new Float4(0.0f), gy01) - 0.5f);

            Float4 gx10 = ixy10 * (1.0f / 7.0f);
            Float4 gy10 = Maths.Floor(gx10) * (1.0f / 7.0f);
            Float4 gz10 = Maths.Floor(gy10) * (1.0f / 6.0f);
            gx10 = Maths.Frac(gx10) - 0.5f;
            gy10 = Maths.Frac(gy10) - 0.5f;
            gz10 = Maths.Frac(gz10) - 0.5f;
            Float4 gw10 = new Float4(0.75f) - Maths.Abs(gx10) - Maths.Abs(gy10) - Maths.Abs(gz10);
            Float4 sw10 = Maths.Step(gw10, new Float4(0.0f));
            gx10 -= sw10 * (Maths.Step(new Float4(0.0f), gx10) - 0.5f);
            gy10 -= sw10 * (Maths.Step(new Float4(0.0f), gy10) - 0.5f);

            Float4 gx11 = ixy11 * (1.0f / 7.0f);
            Float4 gy11 = Maths.Floor(gx11) * (1.0f / 7.0f);
            Float4 gz11 = Maths.Floor(gy11) * (1.0f / 6.0f);
            gx11 = Maths.Frac(gx11) - 0.5f;
            gy11 = Maths.Frac(gy11) - 0.5f;
            gz11 = Maths.Frac(gz11) - 0.5f;
            Float4 gw11 = new Float4(0.75f) - Maths.Abs(gx11) - Maths.Abs(gy11) - Maths.Abs(gz11);
            Float4 sw11 = Maths.Step(gw11, new Float4(0.0f));
            gx11 -= sw11 * (Maths.Step(new Float4(0.0f), gx11) - 0.5f);
            gy11 -= sw11 * (Maths.Step(new Float4(0.0f), gy11) - 0.5f);

            Float4 g0000 = new Float4(gx00.X, gy00.X, gz00.X, gw00.X);
            Float4 g1000 = new Float4(gx00.Y, gy00.Y, gz00.Y, gw00.Y);
            Float4 g0100 = new Float4(gx00.Z, gy00.Z, gz00.Z, gw00.Z);
            Float4 g1100 = new Float4(gx00.W, gy00.W, gz00.W, gw00.W);
            Float4 g0010 = new Float4(gx10.X, gy10.X, gz10.X, gw10.X);
            Float4 g1010 = new Float4(gx10.Y, gy10.Y, gz10.Y, gw10.Y);
            Float4 g0110 = new Float4(gx10.Z, gy10.Z, gz10.Z, gw10.Z);
            Float4 g1110 = new Float4(gx10.W, gy10.W, gz10.W, gw10.W);
            Float4 g0001 = new Float4(gx01.X, gy01.X, gz01.X, gw01.X);
            Float4 g1001 = new Float4(gx01.Y, gy01.Y, gz01.Y, gw01.Y);
            Float4 g0101 = new Float4(gx01.Z, gy01.Z, gz01.Z, gw01.Z);
            Float4 g1101 = new Float4(gx01.W, gy01.W, gz01.W, gw01.W);
            Float4 g0011 = new Float4(gx11.X, gy11.X, gz11.X, gw11.X);
            Float4 g1011 = new Float4(gx11.Y, gy11.Y, gz11.Y, gw11.Y);
            Float4 g0111 = new Float4(gx11.Z, gy11.Z, gz11.Z, gw11.Z);
            Float4 g1111 = new Float4(gx11.W, gy11.W, gz11.W, gw11.W);

            Float4 norm00 = TaylorInvSqrt(new Float4(
                Maths.Dot(g0000, g0000),
                Maths.Dot(g0100, g0100),
                Maths.Dot(g1000, g1000),
                Maths.Dot(g1100, g1100)
            ));
            Float4 norm01 = TaylorInvSqrt(new Float4(
                Maths.Dot(g0001, g0001),
                Maths.Dot(g0101, g0101),
                Maths.Dot(g1001, g1001),
                Maths.Dot(g1101, g1101)
            ));
            Float4 norm10 = TaylorInvSqrt(new Float4(
                Maths.Dot(g0010, g0010),
                Maths.Dot(g0110, g0110),
                Maths.Dot(g1010, g1010),
                Maths.Dot(g1110, g1110)
            ));
            Float4 norm11 = TaylorInvSqrt(new Float4(
                Maths.Dot(g0011, g0011),
                Maths.Dot(g0111, g0111),
                Maths.Dot(g1011, g1011),
                Maths.Dot(g1111, g1111)
            ));

            float n0000 = norm00.X * Maths.Dot(g0000, Pf0);
            float n0100 = norm00.Y * Maths.Dot(g0100, new Float4(Pf0.X, Pf1.Y, Pf0.Z, Pf0.W));
            float n1000 = norm00.Z * Maths.Dot(g1000, new Float4(Pf1.X, Pf0.Y, Pf0.Z, Pf0.W));
            float n1100 = norm00.W * Maths.Dot(g1100, new Float4(Pf1.X, Pf1.Y, Pf0.Z, Pf0.W));
            float n0010 = norm10.X * Maths.Dot(g0010, new Float4(Pf0.X, Pf0.Y, Pf1.Z, Pf0.W));
            float n0110 = norm10.Y * Maths.Dot(g0110, new Float4(Pf0.X, Pf1.Y, Pf1.Z, Pf0.W));
            float n1010 = norm10.Z * Maths.Dot(g1010, new Float4(Pf1.X, Pf0.Y, Pf1.Z, Pf0.W));
            float n1110 = norm10.W * Maths.Dot(g1110, new Float4(Pf1.X, Pf1.Y, Pf1.Z, Pf0.W));
            float n0001 = norm01.X * Maths.Dot(g0001, new Float4(Pf0.X, Pf0.Y, Pf0.Z, Pf1.W));
            float n0101 = norm01.Y * Maths.Dot(g0101, new Float4(Pf0.X, Pf1.Y, Pf0.Z, Pf1.W));
            float n1001 = norm01.Z * Maths.Dot(g1001, new Float4(Pf1.X, Pf0.Y, Pf0.Z, Pf1.W));
            float n1101 = norm01.W * Maths.Dot(g1101, new Float4(Pf1.X, Pf1.Y, Pf0.Z, Pf1.W));
            float n0011 = norm11.X * Maths.Dot(g0011, new Float4(Pf0.X, Pf0.Y, Pf1.Z, Pf1.W));
            float n0111 = norm11.Y * Maths.Dot(g0111, new Float4(Pf0.X, Pf1.Y, Pf1.Z, Pf1.W));
            float n1011 = norm11.Z * Maths.Dot(g1011, new Float4(Pf1.X, Pf0.Y, Pf1.Z, Pf1.W));
            float n1111 = norm11.W * Maths.Dot(g1111, Pf1);

            Float4 fade_xyzw = Fade(Pf0);
            Float4 n_0w = Maths.Lerp(new Float4(n0000, n1000, n0100, n1100), new Float4(n0001, n1001, n0101, n1101), fade_xyzw.W);
            Float4 n_1w = Maths.Lerp(new Float4(n0010, n1010, n0110, n1110), new Float4(n0011, n1011, n0111, n1111), fade_xyzw.W);
            Float4 n_zw = Maths.Lerp(n_0w, n_1w, fade_xyzw.Z);
            Float2 n_yzw = Maths.Lerp(n_zw.XY, n_zw.ZW, fade_xyzw.Y);
            float n_xyzw = Maths.Lerp(n_yzw.X, n_yzw.Y, fade_xyzw.X);
            return 2.2f * n_xyzw;
        }

        // Classic Perlin noise 4D, periodic version
        public static float PNoise(Float4 P, Float4 rep)
        {
            Float4 Pi0 = Mod(Maths.Floor(P), rep); // Integer part modulo rep
            Float4 Pi1 = Mod(Pi0 + 1.0f, rep); // Integer part + 1 mod rep
            Pi0 = Mod289(Pi0);
            Pi1 = Mod289(Pi1);
            Float4 Pf0 = Maths.Frac(P); // Fractional part for interpolation
            Float4 Pf1 = Pf0 - 1.0f; // Fractional part - 1.0
            Float4 ix = new Float4(Pi0.X, Pi1.X, Pi0.X, Pi1.X);
            Float4 iy = new Float4(Pi0.Y, Pi0.Y, Pi1.Y, Pi1.Y);
            Float4 iz0 = new Float4(Pi0.Z);
            Float4 iz1 = new Float4(Pi1.Z);
            Float4 iw0 = new Float4(Pi0.W);
            Float4 iw1 = new Float4(Pi1.W);

            Float4 ixy = Permute(Permute(ix) + iy);
            Float4 ixy0 = Permute(ixy + iz0);
            Float4 ixy1 = Permute(ixy + iz1);
            Float4 ixy00 = Permute(ixy0 + iw0);
            Float4 ixy01 = Permute(ixy0 + iw1);
            Float4 ixy10 = Permute(ixy1 + iw0);
            Float4 ixy11 = Permute(ixy1 + iw1);

            Float4 gx00 = ixy00 * (1.0f / 7.0f);
            Float4 gy00 = Maths.Floor(gx00) * (1.0f / 7.0f);
            Float4 gz00 = Maths.Floor(gy00) * (1.0f / 6.0f);
            gx00 = Maths.Frac(gx00) - 0.5f;
            gy00 = Maths.Frac(gy00) - 0.5f;
            gz00 = Maths.Frac(gz00) - 0.5f;
            Float4 gw00 = new Float4(0.75f) - Maths.Abs(gx00) - Maths.Abs(gy00) - Maths.Abs(gz00);
            Float4 sw00 = Maths.Step(gw00, new Float4(0.0f));
            gx00 -= sw00 * (Maths.Step(new Float4(0.0f), gx00) - 0.5f);
            gy00 -= sw00 * (Maths.Step(new Float4(0.0f), gy00) - 0.5f);

            Float4 gx01 = ixy01 * (1.0f / 7.0f);
            Float4 gy01 = Maths.Floor(gx01) * (1.0f / 7.0f);
            Float4 gz01 = Maths.Floor(gy01) * (1.0f / 6.0f);
            gx01 = Maths.Frac(gx01) - 0.5f;
            gy01 = Maths.Frac(gy01) - 0.5f;
            gz01 = Maths.Frac(gz01) - 0.5f;
            Float4 gw01 = new Float4(0.75f) - Maths.Abs(gx01) - Maths.Abs(gy01) - Maths.Abs(gz01);
            Float4 sw01 = Maths.Step(gw01, new Float4(0.0f));
            gx01 -= sw01 * (Maths.Step(new Float4(0.0f), gx01) - 0.5f);
            gy01 -= sw01 * (Maths.Step(new Float4(0.0f), gy01) - 0.5f);

            Float4 gx10 = ixy10 * (1.0f / 7.0f);
            Float4 gy10 = Maths.Floor(gx10) * (1.0f / 7.0f);
            Float4 gz10 = Maths.Floor(gy10) * (1.0f / 6.0f);
            gx10 = Maths.Frac(gx10) - 0.5f;
            gy10 = Maths.Frac(gy10) - 0.5f;
            gz10 = Maths.Frac(gz10) - 0.5f;
            Float4 gw10 = new Float4(0.75f) - Maths.Abs(gx10) - Maths.Abs(gy10) - Maths.Abs(gz10);
            Float4 sw10 = Maths.Step(gw10, new Float4(0.0f));
            gx10 -= sw10 * (Maths.Step(new Float4(0.0f), gx10) - 0.5f);
            gy10 -= sw10 * (Maths.Step(new Float4(0.0f), gy10) - 0.5f);

            Float4 gx11 = ixy11 * (1.0f / 7.0f);
            Float4 gy11 = Maths.Floor(gx11) * (1.0f / 7.0f);
            Float4 gz11 = Maths.Floor(gy11) * (1.0f / 6.0f);
            gx11 = Maths.Frac(gx11) - 0.5f;
            gy11 = Maths.Frac(gy11) - 0.5f;
            gz11 = Maths.Frac(gz11) - 0.5f;
            Float4 gw11 = new Float4(0.75f) - Maths.Abs(gx11) - Maths.Abs(gy11) - Maths.Abs(gz11);
            Float4 sw11 = Maths.Step(gw11, new Float4(0.0f));
            gx11 -= sw11 * (Maths.Step(new Float4(0.0f), gx11) - 0.5f);
            gy11 -= sw11 * (Maths.Step(new Float4(0.0f), gy11) - 0.5f);

            Float4 g0000 = new Float4(gx00.X, gy00.X, gz00.X, gw00.X);
            Float4 g1000 = new Float4(gx00.Y, gy00.Y, gz00.Y, gw00.Y);
            Float4 g0100 = new Float4(gx00.Z, gy00.Z, gz00.Z, gw00.Z);
            Float4 g1100 = new Float4(gx00.W, gy00.W, gz00.W, gw00.W);
            Float4 g0010 = new Float4(gx10.X, gy10.X, gz10.X, gw10.X);
            Float4 g1010 = new Float4(gx10.Y, gy10.Y, gz10.Y, gw10.Y);
            Float4 g0110 = new Float4(gx10.Z, gy10.Z, gz10.Z, gw10.Z);
            Float4 g1110 = new Float4(gx10.W, gy10.W, gz10.W, gw10.W);
            Float4 g0001 = new Float4(gx01.X, gy01.X, gz01.X, gw01.X);
            Float4 g1001 = new Float4(gx01.Y, gy01.Y, gz01.Y, gw01.Y);
            Float4 g0101 = new Float4(gx01.Z, gy01.Z, gz01.Z, gw01.Z);
            Float4 g1101 = new Float4(gx01.W, gy01.W, gz01.W, gw01.W);
            Float4 g0011 = new Float4(gx11.X, gy11.X, gz11.X, gw11.X);
            Float4 g1011 = new Float4(gx11.Y, gy11.Y, gz11.Y, gw11.Y);
            Float4 g0111 = new Float4(gx11.Z, gy11.Z, gz11.Z, gw11.Z);
            Float4 g1111 = new Float4(gx11.W, gy11.W, gz11.W, gw11.W);

            Float4 norm00 = TaylorInvSqrt(new Float4(
                Maths.Dot(g0000, g0000),
                Maths.Dot(g0100, g0100),
                Maths.Dot(g1000, g1000),
                Maths.Dot(g1100, g1100)
            ));
            Float4 norm01 = TaylorInvSqrt(new Float4(
                Maths.Dot(g0001, g0001),
                Maths.Dot(g0101, g0101),
                Maths.Dot(g1001, g1001),
                Maths.Dot(g1101, g1101)
            ));
            Float4 norm10 = TaylorInvSqrt(new Float4(
                Maths.Dot(g0010, g0010),
                Maths.Dot(g0110, g0110),
                Maths.Dot(g1010, g1010),
                Maths.Dot(g1110, g1110)
            ));
            Float4 norm11 = TaylorInvSqrt(new Float4(
                Maths.Dot(g0011, g0011),
                Maths.Dot(g0111, g0111),
                Maths.Dot(g1011, g1011),
                Maths.Dot(g1111, g1111)
            ));

            float n0000 = norm00.X * Maths.Dot(g0000, Pf0);
            float n0100 = norm00.Y * Maths.Dot(g0100, new Float4(Pf0.X, Pf1.Y, Pf0.Z, Pf0.W));
            float n1000 = norm00.Z * Maths.Dot(g1000, new Float4(Pf1.X, Pf0.Y, Pf0.Z, Pf0.W));
            float n1100 = norm00.W * Maths.Dot(g1100, new Float4(Pf1.X, Pf1.Y, Pf0.Z, Pf0.W));
            float n0010 = norm10.X * Maths.Dot(g0010, new Float4(Pf0.X, Pf0.Y, Pf1.Z, Pf0.W));
            float n0110 = norm10.Y * Maths.Dot(g0110, new Float4(Pf0.X, Pf1.Y, Pf1.Z, Pf0.W));
            float n1010 = norm10.Z * Maths.Dot(g1010, new Float4(Pf1.X, Pf0.Y, Pf1.Z, Pf0.W));
            float n1110 = norm10.W * Maths.Dot(g1110, new Float4(Pf1.X, Pf1.Y, Pf1.Z, Pf0.W));
            float n0001 = norm01.X * Maths.Dot(g0001, new Float4(Pf0.X, Pf0.Y, Pf0.Z, Pf1.W));
            float n0101 = norm01.Y * Maths.Dot(g0101, new Float4(Pf0.X, Pf1.Y, Pf0.Z, Pf1.W));
            float n1001 = norm01.Z * Maths.Dot(g1001, new Float4(Pf1.X, Pf0.Y, Pf0.Z, Pf1.W));
            float n1101 = norm01.W * Maths.Dot(g1101, new Float4(Pf1.X, Pf1.Y, Pf0.Z, Pf1.W));
            float n0011 = norm11.X * Maths.Dot(g0011, new Float4(Pf0.X, Pf0.Y, Pf1.Z, Pf1.W));
            float n0111 = norm11.Y * Maths.Dot(g0111, new Float4(Pf0.X, Pf1.Y, Pf1.Z, Pf1.W));
            float n1011 = norm11.Z * Maths.Dot(g1011, new Float4(Pf1.X, Pf0.Y, Pf1.Z, Pf1.W));
            float n1111 = norm11.W * Maths.Dot(g1111, Pf1);

            Float4 fade_xyzw = Fade(Pf0);
            Float4 n_0w = Maths.Lerp(new Float4(n0000, n1000, n0100, n1100), new Float4(n0001, n1001, n0101, n1101), fade_xyzw.W);
            Float4 n_1w = Maths.Lerp(new Float4(n0010, n1010, n0110, n1110), new Float4(n0011, n1011, n0111, n1111), fade_xyzw.W);
            Float4 n_zw = Maths.Lerp(n_0w, n_1w, fade_xyzw.Z);
            Float2 n_yzw = Maths.Lerp(n_zw.XY, n_zw.ZW, fade_xyzw.Y);
            float n_xyzw = Maths.Lerp(n_yzw.X, n_yzw.Y, fade_xyzw.X);
            return 2.2f * n_xyzw;
        }
    }
}
