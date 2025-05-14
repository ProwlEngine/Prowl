// Copyright (c) Stefan Gustavson 2011-04-19. All rights reserved.
// This code is released under the conditions of the MIT license.
// See LICENSE file for details.
// https://github.com/stegu/webgl-noise

namespace Prowl.Vector;

public static partial class Noise
{
    // Cellular noise 2x2x2 search window
    // Returns F1 and F2 in a Float2
    // Faster than 3x3x3 but with some pattern artifacts
    public static Float2 Cellular2x2x2(Float3 P)
    {
        const float K = 0.142857142857f; // 1/7
        const float Ko = 0.428571428571f; // 1/2-K/2
        const float K2 = 0.020408163265306f; // 1/(7*7)
        const float Kz = 0.166666666667f; // 1/6
        const float Kzo = 0.416666666667f; // 1/2-1/6*2
        const float jitter = 0.8f; // smaller jitter gives less errors in F2

        Float3 Pi = Mod289(Maths.Floor(P));
        Float3 Pf = Maths.Frac(P);
        Float4 Pfx = Pf.X + new Float4(0.0f, -1.0f, 0.0f, -1.0f);
        Float4 Pfy = Pf.Y + new Float4(0.0f, 0.0f, -1.0f, -1.0f);
        Float4 p = Permute(Pi.X + new Float4(0.0f, 1.0f, 0.0f, 1.0f));
        p = Permute(p + Pi.Y + new Float4(0.0f, 0.0f, 1.0f, 1.0f));
        Float4 p1 = Permute(p + Pi.Z); // z+0
        Float4 p2 = Permute(p + Pi.Z + new Float4(1.0f)); // z+1
        Float4 ox1 = Maths.Frac(p1 * K) - Ko;
        Float4 oy1 = Mod7(Maths.Floor(p1 * K)) * K - Ko;
        Float4 oz1 = Maths.Floor(p1 * K2) * Kz - Kzo; // p1 < 289 guaranteed
        Float4 ox2 = Maths.Frac(p2 * K) - Ko;
        Float4 oy2 = Mod7(Maths.Floor(p2 * K)) * K - Ko;
        Float4 oz2 = Maths.Floor(p2 * K2) * Kz - Kzo;
        Float4 dx1 = Pfx + jitter * ox1;
        Float4 dy1 = Pfy + jitter * oy1;
        Float4 dz1 = Pf.Z + jitter * oz1;
        Float4 dx2 = Pfx + jitter * ox2;
        Float4 dy2 = Pfy + jitter * oy2;
        Float4 dz2 = Pf.Z - 1.0f + jitter * oz2;
        Float4 d1 = dx1 * dx1 + dy1 * dy1 + dz1 * dz1; // z+0
        Float4 d2 = dx2 * dx2 + dy2 * dy2 + dz2 * dz2; // z+1

        // Sort out the two smallest distances (F1, F2)
        // Do it right and sort out both F1 and F2
        Float4 d = Maths.Min(d1, d2); // F1 is now in d
        d2 = Maths.Max(d1, d2); // Make sure we keep all candidates for F2
        d.XY = (d.X < d.Y) ? d.XY : d.YX; // Swap smallest to d.x
        d.XZ = (d.X < d.Z) ? d.XZ : d.ZX;
        d.XW = (d.X < d.W) ? d.XW : d.WX; // F1 is now in d.x
        d.YZW = Maths.Min(d.YZW, d2.YZW); // F2 now not in d2.yzw
        d.Y = Maths.Min(d.Y, d.Z); // nor in d.z
        d.Y = Maths.Min(d.Y, d.W); // nor in d.w
        d.Y = Maths.Min(d.Y, d2.X); // F2 is now in d.y
        return Maths.Sqrt(d.XY); // F1 and F2
    }
}
