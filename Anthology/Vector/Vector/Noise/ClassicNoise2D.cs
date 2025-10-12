// Copyright (c) Stefan Gustavson 2011-04-19. All rights reserved.
// This code is released under the conditions of the MIT license.
// See LICENSE file for details.
// https://github.com/stegu/webgl-noise

namespace Prowl.Vector
{
    public static partial class Noise
    {
        // Classic Perlin noise 2D
        public static float CNoise(Float2 P)
        {
            Float4 Pi = Maths.Floor(P.XYXY) + new Float4(0.0f, 0.0f, 1.0f, 1.0f);
            Float4 Pf = Maths.Frac(P.XYXY) - new Float4(0.0f, 0.0f, 1.0f, 1.0f);
            Pi = Mod289(Pi); // To avoid truncation effects in permutation
            Float4 ix = Pi.XZXZ;
            Float4 iy = Pi.YYWW;
            Float4 fx = Pf.XZXZ;
            Float4 fy = Pf.YYWW;

            Float4 i = Permute(Permute(ix) + iy);

            Float4 gx = Maths.Frac(i * (1.0f / 41.0f)) * 2.0f - 1.0f;
            Float4 gy = Maths.Abs(gx) - 0.5f;
            Float4 tx = Maths.Floor(gx + 0.5f);
            gx = gx - tx;

            Float2 g00 = new Float2(gx.X, gy.X);
            Float2 g10 = new Float2(gx.Y, gy.Y);
            Float2 g01 = new Float2(gx.Z, gy.Z);
            Float2 g11 = new Float2(gx.W, gy.W);

            Float4 norm = TaylorInvSqrt(new Float4(
                Float2.Dot(g00, g00),
                Float2.Dot(g01, g01),
                Float2.Dot(g10, g10),
                Float2.Dot(g11, g11)
            ));

            float n00 = norm.X * Float2.Dot(g00, new Float2(fx.X, fy.X));
            float n01 = norm.Y * Float2.Dot(g01, new Float2(fx.Z, fy.Z));
            float n10 = norm.Z * Float2.Dot(g10, new Float2(fx.Y, fy.Y));
            float n11 = norm.W * Float2.Dot(g11, new Float2(fx.W, fy.W));

            Float2 fade_xy = Fade(Pf.XY);
            Float2 n_x = Maths.Lerp(new Float2(n00, n01), new Float2(n10, n11), fade_xy.X);
            float n_xy = Maths.Lerp(n_x.X, n_x.Y, fade_xy.Y);
            return 2.3f * n_xy;
        }

        // Classic Perlin noise 2D, periodic variant
        public static float PNoise(Float2 P, Float2 rep)
        {
            Float4 Pi = Maths.Floor(P.XYXY) + new Float4(0.0f, 0.0f, 1.0f, 1.0f);
            Float4 Pf = Maths.Frac(P.XYXY) - new Float4(0.0f, 0.0f, 1.0f, 1.0f);
            Pi = Mod(Pi, rep.XYXY); // To create noise with explicit period
            Pi = Mod289(Pi);        // To avoid truncation effects in permutation
            Float4 ix = Pi.XZXZ;
            Float4 iy = Pi.YYWW;
            Float4 fx = Pf.XZXZ;
            Float4 fy = Pf.YYWW;

            Float4 i = Permute(Permute(ix) + iy);

            Float4 gx = Maths.Frac(i * (1.0f / 41.0f)) * 2.0f - 1.0f;
            Float4 gy = Maths.Abs(gx) - 0.5f;
            Float4 tx = Maths.Floor(gx + 0.5f);
            gx = gx - tx;

            Float2 g00 = new Float2(gx.X, gy.X);
            Float2 g10 = new Float2(gx.Y, gy.Y);
            Float2 g01 = new Float2(gx.Z, gy.Z);
            Float2 g11 = new Float2(gx.W, gy.W);

            Float4 norm = TaylorInvSqrt(new Float4(
                Float2.Dot(g00, g00),
                Float2.Dot(g01, g01),
                Float2.Dot(g10, g10),
                Float2.Dot(g11, g11)
            ));

            float n00 = norm.X * Float2.Dot(g00, new Float2(fx.X, fy.X));
            float n01 = norm.Y * Float2.Dot(g01, new Float2(fx.Z, fy.Z));
            float n10 = norm.Z * Float2.Dot(g10, new Float2(fx.Y, fy.Y));
            float n11 = norm.W * Float2.Dot(g11, new Float2(fx.W, fy.W));

            Float2 fade_xy = Fade(Pf.XY);
            Float2 n_x = Maths.Lerp(new Float2(n00, n01), new Float2(n10, n11), fade_xy.X);
            float n_xy = Maths.Lerp(n_x.X, n_x.Y, fade_xy.Y);
            return 2.3f * n_xy;
        }
    }
}
