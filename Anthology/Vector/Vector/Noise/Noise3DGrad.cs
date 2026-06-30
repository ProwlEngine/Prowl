// Copyright (c) Stefan Gustavson 2011-04-19. All rights reserved.
// This code is released under the conditions of the MIT license.
// See LICENSE file for details.
// https://github.com/stegu/webgl-noise

namespace Prowl.Vector
{
    public static partial class Noise
    {
        // Simplex noise 3D with gradient output
        // Returns the noise value and outputs the gradient through a ref parameter
        public static float SNoise(Float3 v, out Float3 gradient)
        {
            Float2 C = new Float2(1.0f / 6.0f, 1.0f / 3.0f);
            Float4 D = new Float4(0.0f, 0.5f, 1.0f, 2.0f);

            // First corner
            Float3 i = Maths.Floor(v + Float3.Dot(v, C.YYY));
            Float3 x0 = v - i + Float3.Dot(i, C.XXX);

            // Other corners
            Float3 g = Maths.Step(x0.YZX, x0.XYZ);
            Float3 l = new Float3(1.0f) - g;
            Float3 i1 = Maths.Min(g.XYZ, l.ZXY);
            Float3 i2 = Maths.Max(g.XYZ, l.ZXY);

            //   x0 = x0 - 0.0 + 0.0 * C.xxx;
            //   x1 = x0 - i1  + 1.0 * C.xxx;
            //   x2 = x0 - i2  + 2.0 * C.xxx;
            //   x3 = x0 - 1.0 + 3.0 * C.xxx;
            Float3 x1 = x0 - i1 + C.XXX;
            Float3 x2 = x0 - i2 + C.YYY; // 2.0*C.x = 1/3 = C.y
            Float3 x3 = x0 - D.YYY;      // -1.0+3.0*C.x = -0.5 = -D.y

            // Permutations
            i = Mod289(i);
            Float4 p = Permute(Permute(Permute(
                            i.Z + new Float4(0.0f, i1.Z, i2.Z, 1.0f))
                        + i.Y + new Float4(0.0f, i1.Y, i2.Y, 1.0f))
                        + i.X + new Float4(0.0f, i1.X, i2.X, 1.0f));

            // Gradients: 7x7 points over a square, mapped onto an octahedron.
            // The ring size 17*17 = 289 is close to a multiple of 49 (49*6 = 294)
            float n_ = 0.142857142857f; // 1.0/7.0
            Float3 ns = n_ * D.WYZ - D.XZX;

            Float4 j = p - 49.0f * Maths.Floor(p * ns.Z * ns.Z);  //  mod(p,7*7)

            Float4 x_ = Maths.Floor(j * ns.Z);
            Float4 y_ = Maths.Floor(j - 7.0f * x_);    // mod(j,N)

            Float4 x = x_ * ns.X + ns.YYYY;
            Float4 y = y_ * ns.X + ns.YYYY;
            Float4 h = 1.0f - Maths.Abs(x) - Maths.Abs(y);

            Float4 b0 = new Float4(x.X, x.Y, y.X, y.Y);
            Float4 b1 = new Float4(x.Z, x.W, y.Z, y.W);

            Float4 s0 = Maths.Floor(b0) * 2.0f + 1.0f;
            Float4 s1 = Maths.Floor(b1) * 2.0f + 1.0f;
            Float4 sh = -Maths.Step(h, new Float4(0.0f));

            Float4 a0 = b0.XZYW + s0.XZYW * sh.XXYY;
            Float4 a1 = b1.XZYW + s1.XZYW * sh.ZZWW;

            Float3 p0 = new Float3(a0.X, a0.Y, h.X);
            Float3 p1 = new Float3(a0.Z, a0.W, h.Y);
            Float3 p2 = new Float3(a1.X, a1.Y, h.Z);
            Float3 p3 = new Float3(a1.Z, a1.W, h.W);

            // Normalise gradients
            Float4 norm = TaylorInvSqrt(new Float4(Float3.Dot(p0, p0),
                                                   Float3.Dot(p1, p1),
                                                   Float3.Dot(p2, p2),
                                                   Float3.Dot(p3, p3)));
            p0 *= norm.X;
            p1 *= norm.Y;
            p2 *= norm.Z;
            p3 *= norm.W;

            // Mix final noise value
            Float4 m = Maths.Max(0.5f - new Float4(Float3.Dot(x0, x0),
                                                   Float3.Dot(x1, x1),
                                                   Float3.Dot(x2, x2),
                                                   Float3.Dot(x3, x3)),
                                 new Float4(0.0f));
            Float4 m2 = m * m;
            Float4 m4 = m2 * m2;
            Float4 pdotx = new Float4(Float3.Dot(p0, x0),
                                      Float3.Dot(p1, x1),
                                      Float3.Dot(p2, x2),
                                      Float3.Dot(p3, x3));

            // Determine noise gradient
            Float4 temp = m2 * m * pdotx;
            gradient = -8.0f * (temp.X * x0 + temp.Y * x1 + temp.Z * x2 + temp.W * x3);
            gradient += m4.X * p0 + m4.Y * p1 + m4.Z * p2 + m4.W * p3;
            gradient *= 105.0f;

            return 105.0f * Float4.Dot(m4, pdotx);
        }
    }
}
