// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Quill;
using Prowl.Vector;

namespace Prowl.OrigamiUI;

/// <summary>
/// Canvas primitives shared by every chart family that paints radial geometry.
/// </summary>
internal static class ChartGeometry
{
    /// <summary>Fills the sector between <paramref name="a0"/> and <paramref name="a1"/>. An
    /// <paramref name="innerR"/> of zero or less gives a solid pie wedge; anything larger cuts the
    /// wedge's inner end out for a donut or sunburst ring segment.</summary>
    internal static void PaintWedge(Canvas canvas, float cx, float cy, float innerR, float outerR,
        float a0, float a1, Color32 fill)
    {
        if (outerR <= 0f || MathF.Abs(a1 - a0) < 1e-5f) return;

        if (innerR <= 0f)
        {
            canvas.Pie(cx, cy, outerR, a0, a1);
            canvas.SetFillColor(fill);
            canvas.Fill();
            return;
        }

        int segments = ArcSegments(outerR, a1 - a0);

        canvas.BeginPath();
        for (int i = 0; i <= segments; i++)
        {
            float a = a0 + (a1 - a0) * i / segments;
            float x = cx + MathF.Cos(a) * outerR, y = cy + MathF.Sin(a) * outerR;
            if (i == 0) canvas.MoveTo(x, y); else canvas.LineTo(x, y);
        }
        for (int i = segments; i >= 0; i--)
        {
            float a = a0 + (a1 - a0) * i / segments;
            canvas.LineTo(cx + MathF.Cos(a) * innerR, cy + MathF.Sin(a) * innerR);
        }
        canvas.ClosePath();

        canvas.SetFillColor(fill);
        canvas.FillComplexAA();
    }

    /// <summary>How many straight segments an arc of this radius and sweep is flattened into. Scales
    /// with arc length so a large ring stays smooth without a small one paying for subdivision it
    /// cannot show.</summary>
    internal static int ArcSegments(float radius, float sweep)
        => Math.Clamp((int)MathF.Ceiling(MathF.Abs(sweep) * MathF.Max(radius, 1f) / 4f), 3, 240);
}
