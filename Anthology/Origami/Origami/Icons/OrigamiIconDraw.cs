// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.PaperUI;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI;

/// <summary>Helpers for painting an <see cref="IOrigamiIcon"/> into a Paper element.</summary>
public static class OrigamiIconDraw
{
    /// <summary>Paint <paramref name="icon"/> centered in this box after layout (no-op if null).
    /// When <paramref name="size"/> is positive the glyph is drawn at that pixel size, centered; otherwise it fills the box.</summary>
    public static ElementBuilder Icon(this ElementBuilder box, Paper paper, IOrigamiIcon? icon, Color color, float stroke = 1.5f, float size = 0f)
    {
        if (icon != null)
            box.OnPostLayout((h, r) => paper.Draw(ref h, (canvas, rr) =>
            {
                if (size > 0f)
                {
                    float cx = (float)(rr.Min.X + rr.Size.X / 2), cy = (float)(rr.Min.Y + rr.Size.Y / 2);
                    rr = new Rect(new Float2(cx - size / 2, cy - size / 2), new Float2(cx + size / 2, cy + size / 2));
                }
                icon.Draw(canvas, rr, color, stroke);
            }));
        return box;
    }
}
