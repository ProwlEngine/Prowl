// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Quill;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI;

public enum GlyphWeight
{
    Auto,
    Solid,
    Outline,
}

public sealed class OrigamiGlyphIcon : IOrigamiIcon
{
    private readonly string _glyph;
    private readonly float _scale;
    private readonly GlyphWeight _weight;

    public OrigamiGlyphIcon(string glyph, float scale = 0.82f, GlyphWeight weight = GlyphWeight.Auto)
    {
        _glyph = glyph ?? string.Empty;
        _scale = MathF.Max(0.05f, scale);
        _weight = weight;
    }

    public void Draw(Canvas canvas, Rect rect, Color color, float strokeWidth = 1.5f)
    {
        if (string.IsNullOrEmpty(_glyph)) return;

        var font = _weight switch
        {
            GlyphWeight.Solid => OrigamiFonts.IconSolid,
            GlyphWeight.Outline => OrigamiFonts.IconOutline,
            _ => OrigamiFonts.IconOutline is { } o && o.FindGlyphIndex(_glyph[0]) != 0 ? o : OrigamiFonts.IconSolid,
        };
        if (font == null) return;

        float size = MathF.Min((float)rect.Size.X, (float)rect.Size.Y) * _scale * Origami.IconScale;
        var m = canvas.MeasureText(_glyph, size, font);
        float tx = (float)(rect.Min.X + (rect.Size.X - m.X) / 2.0);
        float ty = (float)(rect.Min.Y + (rect.Size.Y - m.Y) / 2.0);
        canvas.DrawText(_glyph, tx, ty, Color32.FromArgb(color.A, color.R, color.G, color.B), size, font);
    }
}
