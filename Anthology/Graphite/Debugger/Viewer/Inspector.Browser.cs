using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using Color = System.Drawing.Color;

namespace Prowl.Graphite.Debugger.Viewer;

internal sealed record PopoutRequest(FrameModel Model, ProfileNode Node, string Context, List<ViewerItem> Items, TargetStep? Step, string Title);

internal sealed partial class ProfilerView
{
    private const float PreviewWidth = 340f;
    private const float PreviewMinTable = 380f;

    private int _popouts;

    internal Action<PopoutRequest>? PopOut { get; set; }

    private static IOrigamiIcon TypeIcon(string key) => key.StartsWith("tex:") ? OrigamiGlyphs.Image_I : key.StartsWith("buf:") ? OrigamiGlyphs.Database_I : OrigamiGlyphs.Cube_I;

    private static Color TypeColor(string key) => key.StartsWith("tex:") ? Style.Read : key.StartsWith("buf:") ? Style.Write : Style.Good;

    private static void Mark(Row row, int column, string key)
    {
        row.Icons ??= new IOrigamiIcon?[row.Cells.Length];
        row.Colors ??= new Color?[row.Cells.Length];
        row.Icons[column] = TypeIcon(key);
        row.Colors[column] = TypeColor(key);
    }

    private string SelectedKey(string context, List<ViewerItem> items)
        => _viewerSelection.TryGetValue(context, out string? chosen) && items.Any(i => i.Key == chosen) ? chosen : items[0].Key;

    internal void DrawPopout(Paper paper, float width, float height, PopoutRequest request)
    {
        _paper = paper;
        paper.PushID(request.Context);
        float inner = width - Metrics.PaddingLarge * 2f - 12f;
        _contentWidth = inner;
        _sectionIndex = 0;
        Origami.ScrollView(paper, "po_scroll", width, height).Padding(Metrics.PaddingLarge).Body(
            () => ResourceViewer(request.Model, request.Node, request.Context, request.Items, request.Step, inner));
        paper.PopID();
    }

    private void ResourceTable(FrameModel model, ProfileNode node, string context, string title, string note, string[] head, float[] flex,
        IReadOnlyList<Row> rows, List<ViewerItem> items, TargetStep? step = null)
    {
        float full = SectionWidth(0);
        if (items.Count == 0 || full < PreviewWidth + PreviewMinTable)
        {
            TableSection(title, 0, note, head, flex, rows);
            return;
        }

        float gap = Metrics.SpacingLarge;
        float outer = _contentWidth;
        using (_paper.Row("sec", _sectionIndex++).Width(full).Height(UnitValue.Auto).Gap(gap).Enter())
        {
            _contentWidth = full - PreviewWidth - gap;
            TableSection(title, 0, note, head, flex, rows);
            _contentWidth = outer;
            ResourcePreview(model, node, context, items, step);
        }
    }

    private void ResourcePreview(FrameModel model, ProfileNode node, string context, List<ViewerItem> items, TargetStep? step)
    {
        string key = SelectedKey(context, items);
        ViewerItem item = items.First(i => i.Key == key);
        float inner = PreviewWidth - Metrics.PaddingLarge * 2f;

        using (_paper.Column("sec", _sectionIndex++).Width(PreviewWidth).Height(UnitValue.Auto).Gap(Metrics.SpacingSmall).Enter())
        {
            using (_paper.Row("ph").Width(UnitValue.Stretch()).Height(Metrics.HeaderHeight).Gap(Metrics.Spacing).AlignItems(LayoutAlignment.Center).Enter())
            {
                Origami.Header(_paper, "t", "Preview").Text().Show();
                Mono("name", item.Label, Style.InkDim).TextTruncate();
                Spacer("sp");
                Origami.IconButton(_paper, "open", OrigamiGlyphs.UpRightFromSquare_I, () => OpenPopout(model, node, items, step, key, item.Label))
                    .Small().NotFocusable().Subtle().Tooltip("Open in its own window").Show();
            }

            using (Card("card").Width(PreviewWidth).Height(UnitValue.Auto).Padding(Metrics.PaddingLarge).Enter())
                ResourceViewer(model, node, context, items, step, inner, true);
        }
    }

    private void OpenPopout(FrameModel model, ProfileNode node, List<ViewerItem> items, TargetStep? step, string key, string label)
    {
        string context = "popout:" + ++_popouts;
        _viewerSelection[context] = key;
        PopOut?.Invoke(new PopoutRequest(model, node, context, items, step, label));
    }
}
