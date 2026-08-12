// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Text;

using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

namespace Prowl.Editor.GUI.Widgets;

/// <summary>
/// Fluent builder for a buffer preview card - a bordered card carrying the buffer's name, size/kind
/// stats, and either decoded named fields (when <see cref="SnapshotBufferMeta.Layout"/> is populated)
/// or a capped raw hex dump of the captured bytes.
///
/// Usage:
///   BufferViewer.Create(paper, id, bufferName).Show();
///   BufferViewer.Create(paper, id, bufferName).Data(bytes, meta).Show();
/// </summary>
public sealed class BufferViewerBuilder
{
    // Hex dumping every byte of a multi-megabyte buffer would blow up layout/draw cost for no benefit
    // - a capped preview is enough to eyeball the content.
    private const int MaxHexBytes = 256;
    private const int BytesPerRow = 16;

    private readonly Paper _paper;
    private readonly string _id;
    private readonly string _bufferName;

    private byte[]? _bytes;
    private Prowl.Editor.Profiling.SnapshotBufferMeta? _meta;

    internal BufferViewerBuilder(Paper paper, string id, string bufferName)
    {
        _paper = paper;
        _id = id;
        _bufferName = bufferName;
    }

    /// <summary>Supplies captured buffer bytes and metadata (e.g. from a snapshot resource version) to
    /// render stats/fields/hex preview instead of just the name.</summary>
    public BufferViewerBuilder Data(byte[] bytes, Prowl.Editor.Profiling.SnapshotBufferMeta? meta)
    {
        _bytes = bytes;
        _meta = meta;
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
            Origami.Label(_paper, $"{_id}_title", _bufferName)
                .Subheading()
                .LeadingIcon(EditorIcons.Database_I, 14f)
                .AlignLeft()
                .Height(20f)
                .Show();

            if (_bytes == null)
                return;

            Prowl.Editor.Profiling.SnapshotBufferMeta? meta = _meta;
            Prowl.Editor.GUI.RenderProfiler.Inspectors.InspectorKit.StatGroup(_paper, $"{_id}_stats", "Buffer",
                ("Size", Prowl.Editor.GUI.RenderProfiler.Inspectors.InspectorKit.FormatBytesAuto(_bytes.Length)),
                ("Kind", meta?.Kind.ToString() ?? "Unknown"),
                ("Stride", meta is { Stride: > 0 } m ? $"{m.Stride} B" : "-"));

            if (meta is { Layout.Count: > 0 })
                DrawFields(meta.Value.Layout);
            else
                DrawHexPreview();
        }
    }

    private void DrawFields(System.Collections.Generic.IReadOnlyList<Prowl.Editor.Profiling.BufferField> layout)
    {
        using (_paper.Column($"{_id}_fields").Height(UnitValue.Auto).ColBetween(2f).Enter())
        {
            for (int i = 0; i < layout.Count; i++)
            {
                Prowl.Editor.Profiling.BufferField field = layout[i];
                using (_paper.Row($"{_id}_field_{i}").Height(18f).ColBetween(8f).Enter())
                {
                    Origami.Label(_paper, $"{_id}_field_{i}_name", field.Name)
                        .SM()
                        .AlignLeft()
                        .Show();

                    _paper.Box($"{_id}_field_{i}_spacer");

                    Origami.Label(_paper, $"{_id}_field_{i}_type", $"{field.Type} @{field.Offset} ({field.SizeBytes}B)")
                        .Muted()
                        .SM()
                        .AlignRight()
                        .Show();
                }
            }
        }
    }

    // Labels don't wrap embedded newlines, so the dump is laid out as one Label per row instead of a
    // single multi-line string.
    private void DrawHexPreview()
    {
        byte[] bytes = _bytes!;
        int count = Math.Min(bytes.Length, MaxHexBytes);
        if (count == 0)
            return;

        using (_paper.Column($"{_id}_hex").Height(UnitValue.Auto).ColBetween(1f).Enter())
        {
            var sb = new StringBuilder();
            int rowIndex = 0;
            for (int row = 0; row < count; row += BytesPerRow, rowIndex++)
            {
                sb.Clear();
                int rowEnd = Math.Min(row + BytesPerRow, count);
                for (int i = row; i < rowEnd; i++)
                    sb.Append(bytes[i].ToString("X2")).Append(' ');

                Origami.Label(_paper, $"{_id}_hex_{rowIndex}", sb.ToString())
                    .Muted()
                    .SM()
                    .AlignLeft()
                    .Show();
            }

            if (bytes.Length > MaxHexBytes)
            {
                Origami.Label(_paper, $"{_id}_hex_more", $"... {bytes.Length - MaxHexBytes} more bytes")
                    .Muted()
                    .SM()
                    .AlignLeft()
                    .Show();
            }
        }
    }
}

public static class BufferViewer
{
    public static BufferViewerBuilder Create(Paper paper, string id, string bufferName) => new(paper, id, bufferName);
}
