// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Quill;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI;

/// <summary>
/// A drawable icon. Widgets hand it a rect + colour and it paints itself onto the canvas, so the
/// same widget chrome works with vector (SVG) icons, glyph-font icons (Font Awesome, etc.) or any
/// custom drawing. Implement this to supply your own icon set; Origami ships <see cref="SvgIcon"/>,
/// <see cref="FontIcon"/> and <see cref="IconAction"/>, and a default set on <see cref="OrigamiIcons"/>.
/// </summary>
public interface IOrigamiIcon
{
    /// <summary>Paint the icon inside <paramref name="rect"/>. <paramref name="color"/> is the colour the
    /// host wants (e.g. the current text/hover colour); an icon may honour it or use its own. </summary>
    void Draw(Canvas canvas, Rect rect, Color color, float strokeWidth = 1.5f);
}

/// <summary>An icon drawn from a stroked/filled SVG path in a 16x16 viewBox (Origami's default set).</summary>
public sealed class SvgIcon : IOrigamiIcon
{
    private readonly string _path;
    private readonly Color? _color;   // null = use the colour passed to Draw
    private readonly bool _fill;
    private readonly float _scale;     // fraction of the rect's short side the 16-unit glyph fills

    /// <param name="path">SVG path data in a 16x16 viewBox (M/L/H/V/C/A/Z).</param>
    /// <param name="color">Fixed colour; when null the icon uses whatever colour the widget passes.</param>
    /// <param name="fill">Fill the path instead of stroking it.</param>
    /// <param name="scale">Fraction of the target rect's shorter side the glyph occupies (default 1).</param>
    public SvgIcon(string path, Color? color = null, bool fill = false, float scale = 1f)
    {
        _path = path ?? string.Empty;
        _color = color;
        _fill = fill;
        _scale = MathF.Max(0.05f, scale);
    }

    /// <summary>Return a copy of this icon locked to <paramref name="color"/>.</summary>
    public SvgIcon Tinted(Color color) => new(_path, color, _fill, _scale);

    public void Draw(Canvas canvas, Rect rect, Color color, float strokeWidth = 1.5f)
    {
        if (string.IsNullOrEmpty(_path)) return;
        float size = MathF.Min((float)rect.Size.X, (float)rect.Size.Y) * _scale;
        float ox = (float)(rect.Min.X + (rect.Size.X - size) / 2.0);
        float oy = (float)(rect.Min.Y + (rect.Size.Y - size) / 2.0);
        SvgPath.Stroke(canvas, _path, ox, oy, size, _color ?? color, strokeWidth, _fill);
    }
}

/// <summary>An icon drawn as a glyph from a font face — for Font Awesome / icon-font hosts.</summary>
public sealed class FontIcon : IOrigamiIcon
{
    private readonly Prowl.Scribe.FontFile _font;
    private readonly string _glyph;
    private readonly Color? _color;
    private readonly float _scale;

    public FontIcon(Prowl.Scribe.FontFile font, string glyph, Color? color = null, float scale = 1f)
    {
        _font = font;
        _glyph = glyph ?? string.Empty;
        _color = color;
        _scale = MathF.Max(0.05f, scale);
    }

    public void Draw(Canvas canvas, Rect rect, Color color, float strokeWidth = 1.5f)
    {
        if (_font == null || string.IsNullOrEmpty(_glyph)) return;
        float size = MathF.Min((float)rect.Size.X, (float)rect.Size.Y) * _scale;
        var c = _color ?? color;
        var m = canvas.MeasureText(_glyph, size, _font);
        float tx = (float)(rect.Min.X + (rect.Size.X - m.X) / 2.0);
        float ty = (float)(rect.Min.Y + (rect.Size.Y - m.Y) / 2.0);
        canvas.DrawText(_glyph, tx, ty, Color32.FromArgb(c.A, c.R, c.G, c.B), size, _font);
    }
}

/// <summary>An icon backed by a caller-supplied draw callback — the escape hatch for custom rendering.</summary>
public sealed class IconAction : IOrigamiIcon
{
    private readonly Action<Canvas, Rect, Color, float> _draw;
    public IconAction(Action<Canvas, Rect, Color, float> draw) => _draw = draw ?? throw new ArgumentNullException(nameof(draw));
    public void Draw(Canvas canvas, Rect rect, Color color, float strokeWidth = 1.5f) => _draw(canvas, rect, color, strokeWidth);
}
