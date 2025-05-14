// Cellul// Copyright (c) Stefan Gustavson 2011-04-19. All rights reserved.
// This code is released under the conditions of the MIT license.
// See LICENSE file for details.
// https://github.com/stegu/webgl-noise


/* Unmerged change from project 'Vector(net7.0)'
Before:
namespace Prowl.Vector
{
    public static partial class Noise
    {
        // Cellular noise, returning F1 and F2 in a vec2.
        // Standard 3x3 search window for good F1 and F2 values
        public static Float2 Cellular2D(Float2 P)
        {
            const float K = 0.142857142857f; // 1/7
            const float Ko = 0.428571428571f; // 3/7
            const float jitter = 1.0f; // Less gives more regular pattern

            Float2 Pi = Mod289(Maths.Floor(P));
            Float2 Pf = Maths.Frac(P);
            Float3 oi = new Float3(-1.0f, 0.0f, 1.0f);
            Float3 of = new Float3(-0.5f, 0.5f, 1.5f);
            Float3 px = Permute(Pi.X + oi);
            Float3 p = Permute(px.X + Pi.Y + oi); // p11, p12, p13
            Float3 ox = Maths.Frac(p * K) - Ko;
            Float3 oy = Mod7(Maths.Floor(p * K)) * K - Ko;
            Float3 dx = Pf.X + 0.5f + jitter * ox;
            Float3 dy = Pf.Y - of + jitter * oy;
            Float3 d1 = dx * dx + dy * dy; // d11, d12 and d13, squared
            p = Permute(px.Y + Pi.Y + oi); // p21, p22, p23
            ox = Maths.Frac(p * K) - Ko;
            oy = Mod7(Maths.Floor(p * K)) * K - Ko;
            dx = Pf.X - 0.5f + jitter * ox;
            dy = Pf.Y - of + jitter * oy;
            Float3 d2 = dx * dx + dy * dy; // d21, d22 and d23, squared
            p = Permute(px.Z + Pi.Y + oi); // p31, p32, p33
            ox = Maths.Frac(p * K) - Ko;
            oy = Mod7(Maths.Floor(p * K)) * K - Ko;
            dx = Pf.X - 1.5f + jitter * ox;
            dy = Pf.Y - of + jitter * oy;
            Float3 d3 = dx * dx + dy * dy; // d31, d32 and d33, squared
                                         // Sort out the two smallest distances (F1, F2)
            Float3 d1a = Maths.Min(d1, d2);
            d2 = Maths.Max(d1, d2); // Swap to keep candidates for F2
            d2 = Maths.Min(d2, d3); // neither F1 nor F2 are now in d3
            d1 = Maths.Min(d1a, d2); // F1 is now in d1
            d2 = Maths.Max(d1a, d2); // Swap to keep candidates for F2
            d1.XY = (d1.X < d1.Y) ? d1.XY : d1.YX; // Swap if smaller
            d1.XZ = (d1.X < d1.Z) ? d1.XZ : d1.ZX; // F1 is in d1.x
            d1.YZ = Maths.Min(d1.YZ, d2.YZ); // F2 is now not in d2.yz
            d1.Y = Maths.Min(d1.Y, d1.Z); // nor in  d1.z
            d1.Y = Maths.Min(d1.Y, d2.X); // F2 is in d1.y, we're done.
            return Maths.Sqrt(d1.XY);
        }
After:
namespace Prowl.Vector;

public static partial class Noise
{
    // Cellular noise, returning F1 and F2 in a vec2.
    // Standard 3x3 search window for good F1 and F2 values
    public static Float2 Cellular2D(Float2 P)
    {
        const float K = 0.142857142857f; // 1/7
        const float Ko = 0.428571428571f; // 3/7
        const float jitter = 1.0f; // Less gives more regular pattern

        Float2 Pi = Mod289(Maths.Floor(P));
        Float2 Pf = Maths.Frac(P);
        Float3 oi = new Float3(-1.0f, 0.0f, 1.0f);
        Float3 of = new Float3(-0.5f, 0.5f, 1.5f);
        Float3 px = Permute(Pi.X + oi);
        Float3 p = Permute(px.X + Pi.Y + oi); // p11, p12, p13
        Float3 ox = Maths.Frac(p * K) - Ko;
        Float3 oy = Mod7(Maths.Floor(p * K)) * K - Ko;
        Float3 dx = Pf.X + 0.5f + jitter * ox;
        Float3 dy = Pf.Y - of + jitter * oy;
        Float3 d1 = dx * dx + dy * dy; // d11, d12 and d13, squared
        p = Permute(px.Y + Pi.Y + oi); // p21, p22, p23
        ox = Maths.Frac(p * K) - Ko;
        oy = Mod7(Maths.Floor(p * K)) * K - Ko;
        dx = Pf.X - 0.5f + jitter * ox;
        dy = Pf.Y - of + jitter * oy;
        Float3 d2 = dx * dx + dy * dy; // d21, d22 and d23, squared
        p = Permute(px.Z + Pi.Y + oi); // p31, p32, p33
        ox = Maths.Frac(p * K) - Ko;
        oy = Mod7(Maths.Floor(p * K)) * K - Ko;
        dx = Pf.X - 1.5f + jitter * ox;
        dy = Pf.Y - of + jitter * oy;
        Float3 d3 = dx * dx + dy * dy; // d31, d32 and d33, squared
                                     // Sort out the two smallest distances (F1, F2)
        Float3 d1a = Maths.Min(d1, d2);
        d2 = Maths.Max(d1, d2); // Swap to keep candidates for F2
        d2 = Maths.Min(d2, d3); // neither F1 nor F2 are now in d3
        d1 = Maths.Min(d1a, d2); // F1 is now in d1
        d2 = Maths.Max(d1a, d2); // Swap to keep candidates for F2
        d1.XY = (d1.X < d1.Y) ? d1.XY : d1.YX; // Swap if smaller
        d1.XZ = (d1.X < d1.Z) ? d1.XZ : d1.ZX; // F1 is in d1.x
        d1.YZ = Maths.Min(d1.YZ, d2.YZ); // F2 is now not in d2.yz
        d1.Y = Maths.Min(d1.Y, d1.Z); // nor in  d1.z
        d1.Y = Maths.Min(d1.Y, d2.X); // F2 is in d1.y, we're done.
        return Maths.Sqrt(d1.XY);
*/
namespace Prowl.Vector;

public static partial class Noise
{
    // Cellular noise, returning F1 and F2 in a vec2.
    // Standard 3x3 search window for good F1 and F2 values
    public static Float2 Cellular2D(Float2 P)
    {
        const float K = 0.142857142857f; // 1/7
        const float Ko = 0.428571428571f; // 3/7
        const float jitter = 1.0f; // Less gives more regular pattern

        Float2 Pi = Mod289(Maths.Floor(P));
        Float2 Pf = Maths.Frac(P);
        Float3 oi = new Float3(-1.0f, 0.0f, 1.0f);
        Float3 of = new Float3(-0.5f, 0.5f, 1.5f);
        Float3 px = Permute(Pi.X + oi);
        Float3 p = Permute(px.X + Pi.Y + oi); // p11, p12, p13
        Float3 ox = Maths.Frac(p * K) - Ko;
        Float3 oy = Mod7(Maths.Floor(p * K)) * K - Ko;
        Float3 dx = Pf.X + 0.5f + jitter * ox;
        Float3 dy = Pf.Y - of + jitter * oy;
        Float3 d1 = dx * dx + dy * dy; // d11, d12 and d13, squared
        p = Permute(px.Y + Pi.Y + oi); // p21, p22, p23
        ox = Maths.Frac(p * K) - Ko;
        oy = Mod7(Maths.Floor(p * K)) * K - Ko;
        dx = Pf.X - 0.5f + jitter * ox;
        dy = Pf.Y - of + jitter * oy;
        Float3 d2 = dx * dx + dy * dy; // d21, d22 and d23, squared
        p = Permute(px.Z + Pi.Y + oi); // p31, p32, p33
        ox = Maths.Frac(p * K) - Ko;
        oy = Mod7(Maths.Floor(p * K)) * K - Ko;
        dx = Pf.X - 1.5f + jitter * ox;
        dy = Pf.Y - of + jitter * oy;
        Float3 d3 = dx * dx + dy * dy; // d31, d32 and d33, squared
                                       // Sort out the two smallest distances (F1, F2)
        Float3 d1a = Maths.Min(d1, d2);
        d2 = Maths.Max(d1, d2); // Swap to keep candidates for F2
        d2 = Maths.Min(d2, d3); // neither F1 nor F2 are now in d3
        d1 = Maths.Min(d1a, d2); // F1 is now in d1
        d2 = Maths.Max(d1a, d2); // Swap to keep candidates for F2
        d1.XY = (d1.X < d1.Y) ? d1.XY : d1.YX; // Swap if smaller
        d1.XZ = (d1.X < d1.Z) ? d1.XZ : d1.ZX; // F1 is in d1.x
        d1.YZ = Maths.Min(d1.YZ, d2.YZ); // F2 is now not in d2.yz
        d1.Y = Maths.Min(d1.Y, d1.Z); // nor in  d1.z
        d1.Y = Maths.Min(d1.Y, d2.X); // F2 is in d1.y, we're done.
        return Maths.Sqrt(d1.XY);
    }
}
