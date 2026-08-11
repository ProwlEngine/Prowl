// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

namespace Prowl.Editor.GUI.Widgets;

/// <summary>
/// Fluent builder for a simple texture preview card - a bordered card carrying the texture's name and
/// an empty preview area. Deliberately dumb (no pixel data drawn yet); left for the render-profiler
/// texture inspector to fill in.
///
/// Usage:
///   TextureViewer.Create(paper, id, textureName).Show();
/// </summary>
public sealed class TextureViewerBuilder
{
    private readonly Paper _paper;
    private readonly string _id;
    private readonly string _textureName;

    private float _previewHeight = 140f;

    internal TextureViewerBuilder(Paper paper, string id, string textureName)
    {
        _paper = paper;
        _id = id;
        _textureName = textureName;
    }

    /// <summary>Override the empty preview area's height (default 140).</summary>
    public TextureViewerBuilder PreviewHeight(float height) { _previewHeight = height; return this; }

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
            Origami.Label(_paper, $"{_id}_title", _textureName)
                .Subheading()
                .LeadingIcon(EditorIcons.Image_I, 14f)
                .AlignLeft()
                .Height(20f)
                .Show();

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

public static class TextureViewer
{
    public static TextureViewerBuilder Create(Paper paper, string id, string textureName) => new(paper, id, textureName);
}
