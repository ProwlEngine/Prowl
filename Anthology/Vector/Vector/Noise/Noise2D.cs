// Copyright (c) Stefan Gustavson 2011-04-19. All rights reserved.
// This code is released under the conditions of the MIT license.
// See LICENSE file for details.
// https://github.com/stegu/webgl-noise

namespace Prowl.Vector;

public static partial class Noise
{
    // Simplex noise 2D
    public static float SNoise(Float2 v)
    {
        // Constants
        Float4 C = new Float4(0.211324865405187f,  // (3.0-sqrt(3.0))/6.0
                              0.366025403784439f,  // 0.5*(sqrt(3.0)-1.0)
                             -0.577350269189626f,  // -1.0 + 2.0 * C.x
                              0.024390243902439f); // 1.0 / 41.0

        // First corner
        Float2 i = Maths.Floor(v + Maths.Dot(v, C.YY));
        Float2 x0 = v - i + Maths.Dot(i, C.XX);

        // Other corners
        Float2 i1;
        i1 = (x0.X > x0.Y) ? new Float2(1.0f, 0.0f) : new Float2(0.0f, 1.0f);

        // x0 = x0 - 0.0 + 0.0 * C.xx ;
        // x1 = x0 - i1 + 1.0 * C.xx ;
        // x2 = x0 - 1.0 + 2.0 * C.xx ;
        Float4 x12 = x0.XYXY + C.XXZZ;
        x12.XY -= i1;

        // Permutations
        i = Mod289(i); // Avoid truncation effects in permutation
        Float3 p = Permute(Permute(i.Y + new Float3(0.0f, i1.Y, 1.0f))
                        + i.X + new Float3(0.0f, i1.X, 1.0f));

        Float3 m = Maths.Max(0.5f - new Float3(Maths.Dot(x0, x0),
                                               Maths.Dot(x12.XY, x12.XY),
                                               Maths.Dot(x12.ZW, x12.ZW)),
                             new Float3(0.0f));
        m = m * m;
        m = m * m;

        // Gradients: 41 points uniformly over a line, mapped onto a diamond.
        // The ring size 17*17 = 289 is close to a multiple of 41 (41*7 = 287)
        Float3 x = 2.0f * Maths.Frac(p * C.WWW) - 1.0f;
        Float3 h = Maths.Abs(x) - 0.5f;
        Float3 ox = Maths.Floor(x + 0.5f);
        Float3 a0 = x - ox;

        // Normalise gradients implicitly by scaling m
        // Approximation of: m *= inversesqrt( a0*a0 + h*h );
        m *= 1.79284291400159f - 0.85373472095314f * (a0 * a0 + h * h);

        // Compute final noise value at P
        Float3 g = new Float3();
        g.X = a0.X * x0.X + h.X * x0.Y;
        g.YZ = a0.YZ * x12.XZ + h.YZ * x12.YW;
        return 130.0f * Maths.Dot(m, g);
    }
}
