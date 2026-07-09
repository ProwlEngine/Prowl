// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.OrigamiUI;
using Prowl.Quill;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.Editor.GUI;

/// <summary>Which Font Awesome weight an <see cref="EditorGlyphIcon"/> renders in.</summary>
public enum GlyphWeight
{
    /// <summary>Default font + fallback chain (regular resolves before solid for shared codepoints).</summary>
    Auto,
    /// <summary>Force the filled (fa-solid) face.</summary>
    Solid,
    /// <summary>Force the outline (fa-regular) face.</summary>
    Outline,
}

/// <summary>
/// An <see cref="IOrigamiIcon"/> that paints a Font glyph.
/// </summary>
public sealed class EditorGlyphIcon : IOrigamiIcon
{
    private readonly string _glyph;
    private readonly float _scale;
    private readonly GlyphWeight _weight;

    public EditorGlyphIcon(string glyph, float scale = 0.82f, GlyphWeight weight = GlyphWeight.Auto)
    {
        _glyph = glyph ?? string.Empty;
        _scale = MathF.Max(0.05f, scale);
        _weight = weight;
    }

    public void Draw(Canvas canvas, Rect rect, Color color, float strokeWidth = 1.5f)
    {
        var font = _weight switch
        {
            GlyphWeight.Solid => Theming.EditorTheme.FontIconSolid,
            GlyphWeight.Outline => Theming.EditorTheme.FontIconOutline,
            _ => null,
        } ?? Theming.EditorTheme.DefaultFont;
        if (font == null || string.IsNullOrEmpty(_glyph)) return;

        float size = MathF.Min((float)rect.Size.X, (float)rect.Size.Y) * _scale;
        var m = canvas.MeasureText(_glyph, size, font);
        float tx = (float)(rect.Min.X + (rect.Size.X - m.X) / 2.0);
        float ty = (float)(rect.Min.Y + (rect.Size.Y - m.Y) / 2.0);
        canvas.DrawText(_glyph, tx, ty, Color32.FromArgb(color.A, color.R, color.G, color.B), size, font);
    }
}
