// Copyright (c) Stefan Gustavson 2011-04-19. All rights reserved.
// This code is released under the conditions of the MIT license.
// See LICENSE file for details.
// https://github.com/stegu/webgl-noise

namespace Prowl.Vector;

public static partial class Noise
{
    // (sqrt(5) - 1)/4 = F4, used once below
    const float F4 = 0.309016994374947451f;

    // Gradient helper function for 4D noise
    static Float4 Grad4(float j, Float4 ip)
    {
        Float4 ones = new Float4(1.0f, 1.0f, 1.0f, -1.0f);
        Float4 p = Float4.Zero;
        Float4 s = Float4.Zero;

        p.XYZ = Maths.Floor(Maths.Frac(new Float3(j) * ip.XYZ) * 7.0f) * ip.Z - 1.0f;
        p.W = 1.5f - Maths.Dot(Maths.Abs(p.XYZ), ones.XYZ);
        s = new Float4(
            p.X < 0.0f ? 1.0f : 0.0f,
            p.Y < 0.0f ? 1.0f : 0.0f,
            p.Z < 0.0f ? 1.0f : 0.0f,
            p.W < 0.0f ? 1.0f : 0.0f
        );
        p.XYZ = p.XYZ + (s.XYZ * 2.0f - 1.0f) * s.WWW;

        return p;
    }

    // Simplex noise 4D
    public static float SNoise(Float4 v)
    {
        Float4 C = new Float4(0.138196601125011f,  // (5 - sqrt(5))/20  G4
                              0.276393202250021f,  // 2 * G4
                              0.414589803375032f,  // 3 * G4
                             -0.447213595499958f); // -1 + 4 * G4

        // First corner
        Float4 i = Maths.Floor(v + Maths.Dot(v, new Float4(F4)));
        Float4 x0 = v - i + Maths.Dot(i, C.XXXX);

        // Other corners
        // Rank sorting originally contributed by Bill Licea-Kane, AMD (formerly ATI)
        Float4 i0 = Float4.Zero;
        Float3 isX = Maths.Step(x0.YZW, x0.XXX);
        Float3 isYZ = Maths.Step(x0.ZWW, x0.YYZ);

        i0.X = isX.X + isX.Y + isX.Z;
        i0.YZW = 1.0f - isX;
        i0.Y += isYZ.X + isYZ.Y;
        i0.ZW += 1.0f - isYZ.XY;
        i0.Z += isYZ.Z;
        i0.W += 1.0f - isYZ.Z;

        // i0 now contains the unique values 0,1,2,3 in each channel
        Float4 i3 = Maths.Clamp(i0, 0.0f, 1.0f);
        Float4 i2 = Maths.Clamp(i0 - 1.0f, 0.0f, 1.0f);
        Float4 i1 = Maths.Clamp(i0 - 2.0f, 0.0f, 1.0f);

        //  x0 = x0 - 0.0 + 0.0 * C.xxxx
        //  x1 = x0 - i1  + 1.0 * C.xxxx
        //  x2 = x0 - i2  + 2.0 * C.xxxx
        //  x3 = x0 - i3  + 3.0 * C.xxxx
        //  x4 = x0 - 1.0 + 4.0 * C.xxxx
        Float4 x1 = x0 - i1 + C.XXXX;
        Float4 x2 = x0 - i2 + C.YYYY;
        Float4 x3 = x0 - i3 + C.ZZZZ;
        Float4 x4 = x0 + C.WWWW;

        // Permutations
        i = Mod289(i);
        float j0 = Permute(Permute(Permute(Permute(i.W) + i.Z) + i.Y) + i.X);
        Float4 j1 = Permute(Permute(Permute(Permute(
                    i.W + new Float4(i1.W, i2.W, i3.W, 1.0f))
                  + i.Z + new Float4(i1.Z, i2.Z, i3.Z, 1.0f))
                  + i.Y + new Float4(i1.Y, i2.Y, i3.Y, 1.0f))
                  + i.X + new Float4(i1.X, i2.X, i3.X, 1.0f));

        // Gradients: 7x7x6 points over a cube, mapped onto a 4-cross polytope
        // 7*7*6 = 294, which is close to the ring size 17*17 = 289.
        Float4 ip = new Float4(1.0f / 294.0f, 1.0f / 49.0f, 1.0f / 7.0f, 0.0f);

        Float4 p0 = Grad4(j0, ip);
        Float4 p1 = Grad4(j1.X, ip);
        Float4 p2 = Grad4(j1.Y, ip);
        Float4 p3 = Grad4(j1.Z, ip);
        Float4 p4 = Grad4(j1.W, ip);

        // Normalise gradients
        Float4 norm = TaylorInvSqrt(new Float4(Maths.Dot(p0, p0),
                                               Maths.Dot(p1, p1),
                                               Maths.Dot(p2, p2),
                                               Maths.Dot(p3, p3)));
        p0 *= norm.X;
        p1 *= norm.Y;
        p2 *= norm.Z;
        p3 *= norm.W;
        p4 *= TaylorInvSqrt(Maths.Dot(p4, p4));

        // Mix contributions from the five corners
        Float3 m0 = Maths.Max(0.57f - new Float3(Maths.Dot(x0, x0),
                                                 Maths.Dot(x1, x1),
                                                 Maths.Dot(x2, x2)),
                              new Float3(0.0f));
        Float2 m1 = Maths.Max(0.57f - new Float2(Maths.Dot(x3, x3),
                                                 Maths.Dot(x4, x4)),
                              new Float2(0.0f));
        m0 = m0 * m0;
        m1 = m1 * m1;

        return 60.1f * (Maths.Dot(m0 * m0, new Float3(Maths.Dot(p0, x0),
                                                       Maths.Dot(p1, x1),
                                                       Maths.Dot(p2, x2)))
                       + Maths.Dot(m1 * m1, new Float2(Maths.Dot(p3, x3),
                                                       Maths.Dot(p4, x4))));
    }
}
