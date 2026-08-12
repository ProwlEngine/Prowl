// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime.Resources;
using Prowl.Vector.Spatial;

namespace Prowl.Editor.GUI.Widgets;

/// <summary>
/// Fluent builder for a texture preview card - a bordered card carrying the texture's name and either
/// an actual pixel preview (when a captured <see cref="Texture2D"/> is supplied via <see cref="Data"/>)
/// or an empty placeholder area (outside a snapshot, where no pixel bytes exist).
///
/// Usage:
///   TextureViewer.Create(paper, id, textureName).Show();
///   TextureViewer.Create(paper, id, textureName).Data(texture, width, height, format).Show();
/// </summary>
public sealed class TextureViewerBuilder
{
    private readonly Paper _paper;
    private readonly string _id;
    private readonly string _textureName;

    private float _previewHeight = 140f;
    private Texture2D? _texture;
    private uint _width;
    private uint _height;
    private string? _formatLabel;

    internal TextureViewerBuilder(Paper paper, string id, string textureName)
    {
        _paper = paper;
        _id = id;
        _textureName = textureName;
    }

    /// <summary>Override the preview area's height (default 140).</summary>
    public TextureViewerBuilder PreviewHeight(float height) { _previewHeight = height; return this; }

    /// <summary>Supplies a GPU-uploaded texture (e.g. from a snapshot's captured pixel bytes) to draw
    /// in the preview area instead of the empty placeholder.</summary>
    public TextureViewerBuilder Data(Texture2D texture, uint width, uint height, string formatLabel)
    {
        _texture = texture;
        _width = width;
        _height = height;
        _formatLabel = formatLabel;
        return this;
    }

    public void Show()
    {
        using (_paper.Column(_id)
            .Height(UnitValue.Auto)
            .BorderColor(EditorTheme.BorderStrong)
            .BorderWidth(1f)
            .Rounded(EditorTheme.Roundness)
            .Padding(8f)
            .ColBetween(6f)
            .Enter())
        {
            using (_paper.Row($"{_id}_hdr").Height(20f).ColBetween(8f).Enter())
            {
                Origami.Label(_paper, $"{_id}_title", _textureName)
                    .Subheading()
                    .LeadingIcon(EditorIcons.Image_I, 14f)
                    .AlignLeft()
                    .Show();

                if (_formatLabel != null)
                {
                    _paper.Box($"{_id}_hdr_spacer");

                    Origami.Label(_paper, $"{_id}_dims", $"{_width}x{_height} {_formatLabel}")
                        .Muted()
                        .SM()
                        .AlignRight()
                        .Show();
                }
            }

            if (_texture != null)
            {
                _paper.Box($"{_id}_preview")
                    .Height(_previewHeight)
                    .Rounded(EditorTheme.Roundness)
                    .Clip()
                    .BackgroundColor(EditorTheme.Neutral300)
                    .BorderColor(EditorTheme.BorderSoft)
                    .BorderWidth(1f)
                    .OnPostLayout((handle, rect) => _paper.Draw(ref handle, (canvas, r) =>
                    {
                        // Checkerboard so texture alpha reads clearly.
                        const float cell = 10f;
                        var ca = Prowl.Vector.Color32.FromArgb(255, 44, 40, 54);
                        var cb = Prowl.Vector.Color32.FromArgb(255, 34, 30, 44);
                        int cols = (int)MathF.Ceiling((float)r.Size.X / cell);
                        int crows = (int)MathF.Ceiling((float)r.Size.Y / cell);
                        for (int cy = 0; cy < crows; cy++)
                            for (int cx = 0; cx < cols; cx++)
                            {
                                float px = (float)r.Min.X + cx * cell, py = (float)r.Min.Y + cy * cell;
                                float cw = MathF.Min(cell, (float)r.Max.X - px), ch = MathF.Min(cell, (float)r.Max.Y - py);
                                canvas.RectFilled(px, py, cw, ch, ((cx + cy) & 1) == 0 ? ca : cb);
                            }

                        float maxW = (float)r.Size.X, maxH = (float)r.Size.Y;
                        float aspect = _width / MathF.Max(1f, _height);
                        float drawW = maxW, drawH = drawW / aspect;
                        if (drawH > maxH) { drawH = maxH; drawW = drawH * aspect; }
                        float drawX = (float)r.Min.X + ((float)r.Size.X - drawW) / 2f;
                        float drawY = (float)r.Min.Y + ((float)r.Size.Y - drawH) / 2f;

                        // Flip V (textures are stored Y-up), same idiom the asset inspector uses.
                        canvas.SetBrushTexture(_texture);
                        canvas.SetBrushTextureTransform(
                            Transform2D.CreateTranslation(drawX, drawY + drawH) *
                            Transform2D.CreateScale(drawW, -drawH));
                        canvas.RectFilled(drawX, drawY, drawW, drawH, Prowl.Vector.Color32.FromArgb(255, 255, 255, 255));
                        canvas.ClearBrushTexture();
                    }));
            }
            else
            {
                _paper.Box($"{_id}_preview")
                    .Height(_previewHeight)
                    .Rounded(EditorTheme.Roundness)
                    .BackgroundColor(EditorTheme.Neutral300)
                    .BorderColor(EditorTheme.BorderSoft)
                    .BorderWidth(1f)
                    .IsNotInteractable();
            }
        }
    }
}

public static class TextureViewer
{
    public static TextureViewerBuilder Create(Paper paper, string id, string textureName) => new(paper, id, textureName);
}
