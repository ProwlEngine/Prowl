// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

namespace Prowl.Editor.GUI.Widgets;

/// <summary>
/// Fluent builder for a simple buffer preview card - just a bordered card carrying the buffer's name,
/// no data. Left for someone else to design the actual byte/field inspection.
///
/// Usage:
///   BufferViewer.Create(paper, id, bufferName).Show();
/// </summary>
public sealed class BufferViewerBuilder
{
    private readonly Paper _paper;
    private readonly string _id;
    private readonly string _bufferName;

    internal BufferViewerBuilder(Paper paper, string id, string bufferName)
    {
        _paper = paper;
        _id = id;
        _bufferName = bufferName;
    }

    public void Show()
    {
        using (_paper.Column(_id)
            .Height(UnitValue.Auto)
            .BorderColor(EditorTheme.BorderStrong)
            .BorderWidth(1f)
            .Rounded(EditorTheme.Roundness)
            .Padding(8f)
            .Enter())
        {
            Origami.Label(_paper, $"{_id}_title", _bufferName)
                .Subheading()
                .LeadingIcon(EditorIcons.Database_I, 14f)
                .AlignLeft()
                .Height(20f)
                .Show();
        }
    }
}

public static class BufferViewer
{
    public static BufferViewerBuilder Create(Paper paper, string id, string bufferName) => new(paper, id, bufferName);
}
