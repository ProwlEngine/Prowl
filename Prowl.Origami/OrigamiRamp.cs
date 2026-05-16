// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Drawing;

namespace Prowl.OrigamiUI;

/// <summary>
/// 7-stop colour ramp, dark (<see cref="C100"/>) to light (<see cref="C700"/>).
/// Used for surface fills (header backgrounds, body fills, hover states, borders).
/// Mirrors the convention in <c>EditorTheme.Purple100</c>..<c>Purple700</c> etc.
/// </summary>
public sealed class OrigamiRamp
{
    public Color C100;
    public Color C200;
    public Color C300;
    public Color C400;
    public Color C500;
    public Color C600;
    public Color C700;

    public OrigamiRamp Clone() => new()
    {
        C100 = C100, C200 = C200, C300 = C300,
        C400 = C400, C500 = C500, C600 = C600, C700 = C700,
    };

    public static OrigamiRamp Lerp(OrigamiRamp a, OrigamiRamp b, float t) => new()
    {
        C100 = LerpColor(a.C100, b.C100, t),
        C200 = LerpColor(a.C200, b.C200, t),
        C300 = LerpColor(a.C300, b.C300, t),
        C400 = LerpColor(a.C400, b.C400, t),
        C500 = LerpColor(a.C500, b.C500, t),
        C600 = LerpColor(a.C600, b.C600, t),
        C700 = LerpColor(a.C700, b.C700, t),
    };

    public static Color LerpColor(Color a, Color b, float t)
    {
        if (t <= 0f) return a;
        if (t >= 1f) return b;
        int A = (int)(a.A + (b.A - a.A) * t);
        int R = (int)(a.R + (b.R - a.R) * t);
        int G = (int)(a.G + (b.G - a.G) * t);
        int B = (int)(a.B + (b.B - a.B) * t);
        return Color.FromArgb(A, R, G, B);
    }
}
