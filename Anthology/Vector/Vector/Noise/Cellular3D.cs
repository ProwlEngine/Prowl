// Copyright (c) Stefan Gustavson 2011-04-19. All rights reserved.
// This code is released under the conditions of the MIT license.
// See LICENSE file for details.
// https://github.com/stegu/webgl-noise

namespace Prowl.Vector
{
    public static partial class Noise
    {
        // Cellular noise ("Worley noise") in 3D
        // Returns F1 and F2 in a Float2
        public static Float2 Cellular3D(Float3 P)
        {
            const float K = 0.142857142857f; // 1/7
            const float Ko = 0.428571428571f; // 1/2-K/2
            const float K2 = 0.020408163265306f; // 1/(7*7)
            const float Kz = 0.166666666667f; // 1/6
            const float Kzo = 0.416666666667f; // 1/2-1/6*2
            const float jitter = 1.0f; // Less gives more regular pattern

            Float3 Pi = Mod289(Maths.Floor(P));
            Float3 Pf = Maths.Frac(P) - 0.5f;

            Float3 Pfx = Pf.X + new Float3(1.0f, 0.0f, -1.0f);
            Float3 Pfy = Pf.Y + new Float3(1.0f, 0.0f, -1.0f);
            Float3 Pfz = Pf.Z + new Float3(1.0f, 0.0f, -1.0f);

            Float3 p = Permute(Pi.X + new Float3(-1.0f, 0.0f, 1.0f));
            Float3 p1 = Permute(p + Pi.Y - 1.0f);
            Float3 p2 = Permute(p + Pi.Y);
            Float3 p3 = Permute(p + Pi.Y + 1.0f);

            Float3 p11 = Permute(p1 + Pi.Z - 1.0f);
            Float3 p12 = Permute(p1 + Pi.Z);
            Float3 p13 = Permute(p1 + Pi.Z + 1.0f);

            Float3 p21 = Permute(p2 + Pi.Z - 1.0f);
            Float3 p22 = Permute(p2 + Pi.Z);
            Float3 p23 = Permute(p2 + Pi.Z + 1.0f);

            Float3 p31 = Permute(p3 + Pi.Z - 1.0f);
            Float3 p32 = Permute(p3 + Pi.Z);
            Float3 p33 = Permute(p3 + Pi.Z + 1.0f);

            Float3 ox11 = Maths.Frac(p11 * K) - Ko;
            Float3 oy11 = Mod7(Maths.Floor(p11 * K)) * K - Ko;
            Float3 oz11 = Maths.Floor(p11 * K2) * Kz - Kzo; // p11 < 289 guaranteed

            Float3 ox12 = Maths.Frac(p12 * K) - Ko;
            Float3 oy12 = Mod7(Maths.Floor(p12 * K)) * K - Ko;
            Float3 oz12 = Maths.Floor(p12 * K2) * Kz - Kzo;

            Float3 ox13 = Maths.Frac(p13 * K) - Ko;
            Float3 oy13 = Mod7(Maths.Floor(p13 * K)) * K - Ko;
            Float3 oz13 = Maths.Floor(p13 * K2) * Kz - Kzo;

            Float3 ox21 = Maths.Frac(p21 * K) - Ko;
            Float3 oy21 = Mod7(Maths.Floor(p21 * K)) * K - Ko;
            Float3 oz21 = Maths.Floor(p21 * K2) * Kz - Kzo;

            Float3 ox22 = Maths.Frac(p22 * K) - Ko;
            Float3 oy22 = Mod7(Maths.Floor(p22 * K)) * K - Ko;
            Float3 oz22 = Maths.Floor(p22 * K2) * Kz - Kzo;

            Float3 ox23 = Maths.Frac(p23 * K) - Ko;
            Float3 oy23 = Mod7(Maths.Floor(p23 * K)) * K - Ko;
            Float3 oz23 = Maths.Floor(p23 * K2) * Kz - Kzo;

            Float3 ox31 = Maths.Frac(p31 * K) - Ko;
            Float3 oy31 = Mod7(Maths.Floor(p31 * K)) * K - Ko;
            Float3 oz31 = Maths.Floor(p31 * K2) * Kz - Kzo;

            Float3 ox32 = Maths.Frac(p32 * K) - Ko;
            Float3 oy32 = Mod7(Maths.Floor(p32 * K)) * K - Ko;
            Float3 oz32 = Maths.Floor(p32 * K2) * Kz - Kzo;

            Float3 ox33 = Maths.Frac(p33 * K) - Ko;
            Float3 oy33 = Mod7(Maths.Floor(p33 * K)) * K - Ko;
            Float3 oz33 = Maths.Floor(p33 * K2) * Kz - Kzo;

            Float3 dx11 = Pfx + jitter * ox11;
            Float3 dy11 = Pfy.X + jitter * oy11;
            Float3 dz11 = Pfz.X + jitter * oz11;

            Float3 dx12 = Pfx + jitter * ox12;
            Float3 dy12 = Pfy.X + jitter * oy12;
            Float3 dz12 = Pfz.Y + jitter * oz12;

            Float3 dx13 = Pfx + jitter * ox13;
            Float3 dy13 = Pfy.X + jitter * oy13;
            Float3 dz13 = Pfz.Z + jitter * oz13;

            Float3 dx21 = Pfx + jitter * ox21;
            Float3 dy21 = Pfy.Y + jitter * oy21;
            Float3 dz21 = Pfz.X + jitter * oz21;

            Float3 dx22 = Pfx + jitter * ox22;
            Float3 dy22 = Pfy.Y + jitter * oy22;
            Float3 dz22 = Pfz.Y + jitter * oz22;

            Float3 dx23 = Pfx + jitter * ox23;
            Float3 dy23 = Pfy.Y + jitter * oy23;
            Float3 dz23 = Pfz.Z + jitter * oz23;

            Float3 dx31 = Pfx + jitter * ox31;
            Float3 dy31 = Pfy.Z + jitter * oy31;
            Float3 dz31 = Pfz.X + jitter * oz31;

            Float3 dx32 = Pfx + jitter * ox32;
            Float3 dy32 = Pfy.Z + jitter * oy32;
            Float3 dz32 = Pfz.Y + jitter * oz32;

            Float3 dx33 = Pfx + jitter * ox33;
            Float3 dy33 = Pfy.Z + jitter * oy33;
            Float3 dz33 = Pfz.Z + jitter * oz33;

            Float3 d11 = dx11 * dx11 + dy11 * dy11 + dz11 * dz11;
            Float3 d12 = dx12 * dx12 + dy12 * dy12 + dz12 * dz12;
            Float3 d13 = dx13 * dx13 + dy13 * dy13 + dz13 * dz13;
            Float3 d21 = dx21 * dx21 + dy21 * dy21 + dz21 * dz21;
            Float3 d22 = dx22 * dx22 + dy22 * dy22 + dz22 * dz22;
            Float3 d23 = dx23 * dx23 + dy23 * dy23 + dz23 * dz23;
            Float3 d31 = dx31 * dx31 + dy31 * dy31 + dz31 * dz31;
            Float3 d32 = dx32 * dx32 + dy32 * dy32 + dz32 * dz32;
            Float3 d33 = dx33 * dx33 + dy33 * dy33 + dz33 * dz33;

            // Sort out the two smallest distances (F1, F2)
            // Do it right and sort out both F1 and F2
            Float3 d1a = Maths.Min(d11, d12);
            d12 = Maths.Max(d11, d12);
            d11 = Maths.Min(d1a, d13); // Smallest now not in d12 or d13
            d13 = Maths.Max(d1a, d13);
            d12 = Maths.Min(d12, d13); // 2nd smallest now not in d13

            Float3 d2a = Maths.Min(d21, d22);
            d22 = Maths.Max(d21, d22);
            d21 = Maths.Min(d2a, d23); // Smallest now not in d22 or d23
            d23 = Maths.Max(d2a, d23);
            d22 = Maths.Min(d22, d23); // 2nd smallest now not in d23

            Float3 d3a = Maths.Min(d31, d32);
            d32 = Maths.Max(d31, d32);
            d31 = Maths.Min(d3a, d33); // Smallest now not in d32 or d33
            d33 = Maths.Max(d3a, d33);
            d32 = Maths.Min(d32, d33); // 2nd smallest now not in d33

            Float3 da = Maths.Min(d11, d21);
            d21 = Maths.Max(d11, d21);
            d11 = Maths.Min(da, d31); // Smallest now in d11
            d31 = Maths.Max(da, d31); // 2nd smallest now not in d31

            d11.XY = (d11.X < d11.Y) ? d11.XY : d11.YX;
            d11.XZ = (d11.X < d11.Z) ? d11.XZ : d11.ZX; // d11.x now smallest

            d12 = Maths.Min(d12, d21); // 2nd smallest now not in d21
            d12 = Maths.Min(d12, d22); // nor in d22
            d12 = Maths.Min(d12, d31); // nor in d31
            d12 = Maths.Min(d12, d32); // nor in d32
            d11.YZ = Maths.Min(d11.YZ, d12.XY); // nor in d12.yz
            d11.Y = Maths.Min(d11.Y, d12.Z); // Only two more to go
            d11.Y = Maths.Min(d11.Y, d11.Z); // Done! (Phew!)

            return Maths.Sqrt(d11.XY); // F1, F2
        }
    }
}
