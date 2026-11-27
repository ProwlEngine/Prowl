// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Clay.PostProcess;

/// <summary>
/// Decomposes a 4x4 transform into translation/rotation/scale. Prowl.Vector composes a TRS
/// (<see cref="Float4x4.CreateTRS"/>) but has no decompose, so this is the one piece that stays in Clay.
/// </summary>
internal static class SceneBakerHelpers
{
    public static void DecomposeMatrix(Float4x4 m, out Float3 translation, out Quaternion rotation, out Float3 scale)
    {
        translation = m.c3.XYZ;
        Float3 c0 = m.c0.XYZ, c1 = m.c1.XYZ, c2 = m.c2.XYZ;
        float sx = Float3.Length(c0), sy = Float3.Length(c1), sz = Float3.Length(c2);
        float det = c0.X * (c1.Y * c2.Z - c1.Z * c2.Y)
                  - c0.Y * (c1.X * c2.Z - c1.Z * c2.X)
                  + c0.Z * (c1.X * c2.Y - c1.Y * c2.X);
        if (det < 0f) sx = -sx;
        scale = new Float3(sx, sy, sz);
        Float3 r0 = Divide(c0, sx), r1 = Divide(c1, sy), r2 = Divide(c2, sz);
        rotation = Quaternion.FromMatrix(new Float3x3(r0, r1, r2));
    }

    private static Float3 Divide(Float3 v, float s) => s == 0f ? Float3.Zero : new Float3(v.X / s, v.Y / s, v.Z / s);
}
