// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime.Resources;
using Prowl.Vector;
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
    /// <summary>Pan/zoom state for a texture preview, keyed by widget id so it survives across frames
    /// and re-fits whenever the previewed texture (or its dimensions) changes.</summary>
    private sealed class ViewState
    {
        public const float MinZoom = 0.05f;
        public const float MaxZoom = 40f;
        public const float ZoomStep = 1.10f;

        public Float2 Pan;
        private float _zoom = 1f;

        public float Zoom
        {
            get => _zoom;
            set => _zoom = Math.Clamp(value, MinZoom, MaxZoom);
        }

        private Texture2D? _fittedTexture;
        private uint _fittedWidth;
        private uint _fittedHeight;

        public bool NeedsFit(Texture2D texture, uint width, uint height)
            => !ReferenceEquals(_fittedTexture, texture) || _fittedWidth != width || _fittedHeight != height;

        public void Fit(Texture2D texture, uint width, uint height, Float2 boxSize)
        {
            _fittedTexture = texture;
            _fittedWidth = width;
            _fittedHeight = height;

            float contentW = MathF.Max(1f, width);
            float contentH = MathF.Max(1f, height);
            Zoom = MathF.Min(boxSize.X / contentW, boxSize.Y / contentH);
            Pan = (boxSize - new Float2(contentW, contentH) * Zoom) * 0.5f;
        }

        public void ZoomBy(float factor, Float2 anchorInBox)
        {
            Float2 contentAnchor = (anchorInBox - Pan) / _zoom;
            Zoom = _zoom * factor;
            Pan = anchorInBox - contentAnchor * Zoom;
        }

        public void PanBy(Float2 delta) => Pan += delta;
    }

    private static readonly Dictionary<string, ViewState> s_viewStates = new();

    private readonly Paper _paper;
    private readonly string _id;
    private readonly string _textureName;

    private float _previewHeight = 320f;
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

    /// <summary>Override the preview area's height (default 320).</summary>
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
            .Gap(6f)
            .Enter())
        {
            using (_paper.Row($"{_id}_hdr").Height(20f).Gap(8f).Enter())
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
                Texture2D texture = _texture;
                uint width = _width, height = _height;

                if (!s_viewStates.TryGetValue(_id, out ViewState? state))
                    s_viewStates[_id] = state = new ViewState();

                _paper.Box($"{_id}_preview")
                    .Width(UnitValue.Stretch())
                    .Height(_previewHeight)
                    .Rounded(EditorTheme.Roundness)
                    .Clip()
                    .BackgroundColor(EditorTheme.Neutral300)
                    .BorderColor(EditorTheme.BorderSoft)
                    .BorderWidth(1f)
                    .Cursor(PaperCursor.Grab)
                    .OnScroll(e =>
                    {
                        float factor = e.Delta > 0 ? ViewState.ZoomStep : 1f / ViewState.ZoomStep;
                        state.ZoomBy(factor, e.RelativePosition);
                    })
                    .OnDragging(e => state.PanBy(e.Delta))
                    .OnPostLayout((handle, rect) => _paper.Draw(ref handle, (canvas, r) =>
                    {
                        Float2 boxSize = new((float)r.Size.X, (float)r.Size.Y);
                        if (state.NeedsFit(texture, width, height))
                            state.Fit(texture, width, height, boxSize);

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

                        float drawW = width * state.Zoom, drawH = height * state.Zoom;
                        float drawX = (float)r.Min.X + state.Pan.X;
                        float drawY = (float)r.Min.Y + state.Pan.Y;

                        // Flip V (textures are stored Y-up), same idiom the asset inspector uses.
                        canvas.SetBrushTexture(texture);
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
