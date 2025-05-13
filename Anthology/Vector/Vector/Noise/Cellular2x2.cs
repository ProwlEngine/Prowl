// Copyright (c) Stefan Gustavson 2011-04-19. All rights reserved.
// This code is released under the conditions of the MIT license.
// See LICENSE file for details.
// https://github.com/stegu/webgl-noise

namespace Prowl.Vector
{
    public static partial class Noise
    {
        // Cellular noise 2x2 search window
        // Returns F1 and F2 in a Float2
        // Faster than 3x3 but with some pattern artifacts
        public static Float2 Cellular2x2(Float2 P)
        {
            const float K = 0.142857142857f; // 1/7
            const float K2 = 0.0714285714285f; // K/2
            const float jitter = 0.8f; // jitter 1.0 makes F1 wrong more often

            Float2 Pi = Mod289(Maths.Floor(P));
            Float2 Pf = Maths.Frac(P);
            Float4 Pfx = Pf.X + new Float4(-0.5f, -1.5f, -0.5f, -1.5f);
            Float4 Pfy = Pf.Y + new Float4(-0.5f, -0.5f, -1.5f, -1.5f);
            Float4 p = Permute(Pi.X + new Float4(0.0f, 1.0f, 0.0f, 1.0f));
            p = Permute(p + Pi.Y + new Float4(0.0f, 0.0f, 1.0f, 1.0f));
            Float4 ox = Mod7(p) * K + K2;
            Float4 oy = Mod7(Maths.Floor(p * K)) * K + K2;
            Float4 dx = Pfx + jitter * ox;
            Float4 dy = Pfy + jitter * oy;
            Float4 d = dx * dx + dy * dy; // d11, d12, d21 and d22, squared

            // Sort out the two smallest distances
            // Do it right and find both F1 and F2
            d.XY = (d.X < d.Y) ? d.XY : d.YX; // Swap if smaller
            d.XZ = (d.X < d.Z) ? d.XZ : d.ZX;
            d.XW = (d.X < d.W) ? d.XW : d.WX;
            d.Y = Maths.Min(d.Y, d.Z);
            d.Y = Maths.Min(d.Y, d.W);
            return Maths.Sqrt(d.XY);
        }
    }
}
