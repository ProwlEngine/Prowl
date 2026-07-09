// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.OrigamiUI;
using Prowl.Quill;
using Prowl.Vector;
using Prowl.Vector.Spatial;

using Color = System.Drawing.Color;

namespace Prowl.Editor.GUI;

/// <summary>
/// An <see cref="IOrigamiIcon"/> that paints an SVG path through Quill's SVG parser/renderer. The path
/// is parsed once, lazily, on first draw, then aspect-fit and centered within the target rect. This is
/// the SVG counterpart to <see cref="EditorGlyphIcon"/> (which paints a font glyph).
/// </summary>
public sealed class EditorSvgIcon : IOrigamiIcon
{
    private readonly string _pathData;
    private readonly float _viewWidth;
    private readonly float _viewHeight;
    private SvgElement? _element;

    /// <param name="pathData">The SVG path "d" data.</param>
    /// <param name="viewWidth">viewBox width the path is authored in.</param>
    /// <param name="viewHeight">viewBox height the path is authored in.</param>
    public EditorSvgIcon(string pathData, float viewWidth, float viewHeight)
    {
        _pathData = pathData ?? string.Empty;
        _viewWidth = viewWidth > 0f ? viewWidth : 1f;
        _viewHeight = viewHeight > 0f ? viewHeight : 1f;
    }

    private SvgElement GetElement()
    {
        if (_element == null)
        {
            var path = new SvgPathElement { tag = SvgElement.TagType.path };
            path.Attributes["d"] = _pathData;
            path.Attributes["fill"] = "#ffffff"; // placeholder; the draw colour overrides this per call
            path.Attributes["fill-rule"] = "evenodd";
            path.Parse(SvgPaintContext.Root);
            _element = path;
        }
        return _element;
    }

    public void Draw(Canvas canvas, Rect rect, Color color, float strokeWidth = 1.5f)
    {
        if (string.IsNullOrEmpty(_pathData)) return;

        // Tint the whole shape to the requested colour (icons are drawn in the host's colour).
        var element = GetElement();
        element.fillType = SvgElement.ColorType.specific;
        element.fill = Color32.FromArgb(color.A, color.R, color.G, color.B);

        float scale = (float)Math.Min(rect.Size.X / _viewWidth, rect.Size.Y / _viewHeight);
        float ox = (float)(rect.Min.X + (rect.Size.X - _viewWidth * scale) / 2.0);
        float oy = (float)(rect.Min.Y + (rect.Size.Y - _viewHeight * scale) / 2.0);

        canvas.SaveState();
        canvas.SetSolidity(WindingMode.OddEven);
        canvas.TransformBy(Transform2D.CreateTranslation(ox, oy));
        canvas.TransformBy(Transform2D.CreateScale(scale, scale));
        SVGRenderer.DrawToCanvas(canvas, Float2.Zero, element);
        canvas.RestoreState();
    }
}
