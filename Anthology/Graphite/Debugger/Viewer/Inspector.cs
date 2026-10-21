using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.OrigamiUI;
using Prowl.OrigamiUI.Charts;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using Color = System.Drawing.Color;
using TextAlignment = Prowl.PaperUI.TextAlignment;

namespace Prowl.Graphite.Debugger.Viewer;

internal sealed partial class ProfilerView
{
    private static float SectionGap => Style.Theme.Metrics.SpacingLarge;
    
    private static readonly Dictionary<NodeKind, string[]> LiveTabs = new()
    {
        [NodeKind.Frame] = new[] { "Overview", "Timing", "Memory" },
        [NodeKind.Instance] = new[] { "Overview" },
        [NodeKind.Pass] = new[] { "Overview" },
        [NodeKind.CommandBuffer] = new[] { "Overview" },
    };

    private static readonly Dictionary<NodeKind, string[]> SnapshotTabs = new()
    {
        [NodeKind.Frame] = new[] { "Overview", "Timing", "Memory", "Render Graph", "Capture" },
        [NodeKind.Instance] = new[] { "Overview", "Render Graph" },
        [NodeKind.Pass] = new[] { "Overview", "Resources", "Commands" },
        [NodeKind.CommandBuffer] = new[] { "Overview" },
        [NodeKind.Program] = new[] { "Program", "Bindings", "State" },
        [NodeKind.Marker] = new[] { "Overview", "Material", "Mesh" },
        [NodeKind.Draw] = new[] { "Overview", "Geometry", "Targets", "State" },
    };

    private readonly Dictionary<NodeKind, string> _tabs = new();
    private float _contentWidth;
    private int _sectionIndex;

    private void DrawInspector(FrameModel model, IReadOnlyList<Recording> frames, Recording live, float width, float height)
    {
        ProfileNode node = model.ById[_selId];
        string[] tabs = (Heavy ? SnapshotTabs : LiveTabs)[node.Kind];
        string tab = _tabs.TryGetValue(node.Kind, out string? chosen) && tabs.Contains(chosen) ? chosen : tabs[0];

        using (Card("rp_insp").Width(width).Height(height).Padding(0).Gap(Metrics.Spacing).Enter())
        {
            using (CardHeading("hdr"))
            {
                Origami.Label(_paper, "kind", Style.KindLabel(node.Kind)).FieldLabel().TextColor(Style.KindDot(node.Kind)).Show();
                _paper.Box("name").Width(UnitValue.Auto).MaxWidth(UnitValue.Percentage(50)).Height(UnitValue.Auto)
                    .Text(node.Name, Style.Strong).FontSize(Style.Title).TextColor(Style.Ink).TextTruncate().IsNotInteractable();
                string stat = (node.IsTimed ? Style.Ms(node.Milliseconds) : NodeStat(node)) + (node.Children.Count > 0 ? $" / {node.Children.Count} children" : "");
                Mono("ms", stat, Style.InkSoft);
                Spacer("sp");
                Title("title", Heavy ? "Snapshot Inspector" : "Inspector");
                Origami.Label(_paper, "badge", Heavy ? "SNAPSHOT" : "LIVE").XS().Pill().Padding(Metrics.PaddingLarge, 0).Height(Metrics.RowHeight).Show();
            }

            List<string> chain = new();
            for (ProfileNode? p = node; p != null; p = p.Parent)
                chain.Insert(0, p.Name);
            _paper.Box("path").Height(Metrics.CompactHeight).Margin(Metrics.PaddingLarge, Metrics.PaddingLarge, 0, 0).Text(string.Join("  >  ", chain), Style.Mono).FontSize(Style.Small)
                .TextColor(Style.InkDim).Alignment(TextAlignment.MiddleLeft).TextTruncate().IsNotInteractable();

            int selected = Array.IndexOf(tabs, tab);
            using (_paper.Row("tabsrow").Height(Metrics.TabBarHeight).Padding(Metrics.PaddingLarge, 0).Enter())
            {
                TabsBuilder tabStrip = Origami.Tabs(_paper, "tabs", selected, i =>
                {
                    _tabs[node.Kind] = tabs[i];
                    _pixel = null;
                }).Height(Metrics.TabBarHeight);
                foreach (string t in tabs)
                    tabStrip.Tab(t);
                tabStrip.Show();
            }

            float innerWidth = width - 2f;
            float scrollHeight = MathF.Max(80f, height - 2f - PanelHeaderHeight - Metrics.CompactHeight - Metrics.TabBarHeight - Metrics.Spacing * 3);
            _contentWidth = innerWidth - Metrics.PaddingLarge * 2f;
            _sectionIndex = 0;

            Origami.ScrollView(_paper, "rp_insp_scroll", innerWidth, scrollHeight).Padding(Metrics.PaddingLarge, Metrics.PaddingLarge, Metrics.Spacing, Metrics.PaddingLarge).Body(() =>
            {
                using (_paper.Row("secs").Width(UnitValue.Stretch()).Height(UnitValue.Auto).WrapContent().Gap(SectionGap).LineGap(SectionGap).AlignItems(LayoutAlignment.Stretch).Enter())
                    DrawInspectorBody(model, node, tab, frames, live);
            });
        }
    }

    private void DrawInspectorBody(FrameModel model, ProfileNode node, string tab, IReadOnlyList<Recording> frames, Recording live)
    {
        switch (node.Kind)
        {
            case NodeKind.Frame: FrameTab(model, tab, frames); break;
            case NodeKind.Instance: InstanceTab(model, node, tab, frames); break;
            case NodeKind.Pass: PassTab(model, node, tab, frames); break;
            case NodeKind.CommandBuffer: CommandBufferTab(node); break;
            case NodeKind.Program: ProgramTab(model, node, tab); break;
            case NodeKind.Marker: MarkerTab(model, node, tab); break;
            case NodeKind.Draw: DrawTab(model, node, tab); break;
        }
    }

    private float SectionWidth(float basis)
    {
        if (basis <= 0 || basis >= _contentWidth)
            return _contentWidth;
        int perLine = Math.Max(1, (int)((_contentWidth + SectionGap) / (basis + SectionGap)));
        return (_contentWidth - SectionGap * (perLine - 1)) / perLine;
    }

    private void SectionHeading(string title, string note)
    {
        using (_paper.Row("h").Width(UnitValue.Stretch()).Height(UnitValue.Auto).Gap(Style.Theme.Metrics.SpacingLarge).AlignItems(LayoutAlignment.Center).Enter())
        {
            Origami.Header(_paper, "t", title).Text().Show();
            if (note.Length > 0)
                Dim("n", note);
        }
    }

    private void Section(string title, float basis, string note, Action<float> body, bool uniformPadding = false)
    {
        if (_inGroup)
        {
            NestedAccordion(title, () => Section(title, basis, note, body, uniformPadding));
            return;
        }
        OrigamiMetrics m = Style.Theme.Metrics;
        float width = SectionWidth(basis);
        if (_inAccordion)
        {
            using (_paper.Column("sec", _sectionIndex++).Width(width).Height(UnitValue.Auto).Gap(m.SpacingSmall).Enter())
            {
                if (note.Length > 0)
                    Dim("n", note);
                body(width);
            }
            return;
        }
        float top = uniformPadding ? m.PaddingLarge : m.Padding;
        using (_paper.Column("sec", _sectionIndex++).Width(width).Height(UnitValue.Auto).Gap(m.SpacingSmall).Enter())
        {
            SectionHeading(title, note);
            using (Card("card").Width(width).Height(UnitValue.Auto + UnitValue.Stretch()).Padding(m.PaddingLarge, m.PaddingLarge, top, m.PaddingLarge).Enter())
                body(width - m.PaddingLarge * 2f);
        }
    }

    private readonly record struct Kv(string Key, string Value, Color? Color = null);

    private void KvSection(string title, float basis, IEnumerable<Kv?> pairs, string note = "")
        => Section(title, basis, note, w => KvGrid(w, pairs.Where(p => p != null).Select(p => p!.Value).ToList()));

    private void KvGrid(float width, List<Kv> pairs, float minColumn = 210f)
    {
        float columnGap = Metrics.PaddingLarge * 1.5f;
        int columns = Math.Max(1, (int)((width + columnGap) / (minColumn + columnGap)));
        int rows = (pairs.Count + columns - 1) / columns;
        using (_paper.Column("kv").Width(UnitValue.Stretch()).Height(UnitValue.Auto).Enter())
            for (int row = 0; row < rows; row++)
                using (_paper.Row("line", row).Width(UnitValue.Stretch()).Height(Metrics.RowHeight).Gap(columnGap).Enter())
                    for (int column = 0; column < columns; column++)
                    {
                        int i = row * columns + column;
                        using (_paper.Row("cell", column).Width(UnitValue.Stretch()).Height(UnitValue.Stretch()).Gap(Metrics.SpacingLarge).AlignItems(LayoutAlignment.Center).Enter())
                        {
                            if (i >= pairs.Count)
                                continue;
                            Kv pair = pairs[i];
                            _paper.Draw((canvas, r) => canvas.RectFilled((float)r.Min.X, (float)r.Max.Y - 1, (float)r.Size.X, 1, Style.Border));
                            _paper.Box("k").Width(UnitValue.Auto).Height(UnitValue.Stretch())
                                .Text(pair.Key, Style.Font).FontSize(Style.Small).TextColor(Style.InkDim)
                                .Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
                            _paper.Box("v").Width(UnitValue.Stretch()).Height(UnitValue.Stretch())
                                .Text(pair.Value, Style.Mono).FontSize(Style.Small).TextColor(pair.Color ?? Style.Ink)
                                .Alignment(TextAlignment.MiddleRight).TextTruncate().IsNotInteractable();
                        }
                    }
    }

    private sealed class Row
    {
        public required string[] Cells;
        public Color?[]? Colors;
        public IOrigamiIcon?[]? Icons;
        public Action? Pick;
        public bool Selected;
    }

    private static Row R(params string[] cells) => new() { Cells = cells };

    private void TableSection(string title, float basis, string note, string[] head, float[] flex, IReadOnlyList<Row> rows, float maxHeight = 360f)
    {
        if (_inGroup)
        {
            NestedAccordion(title, () => TableSection(title, basis, note, head, flex, rows, maxHeight));
            return;
        }
        float width = SectionWidth(basis);
        using (_paper.Column("sec", _sectionIndex++).Width(width).Height(UnitValue.Auto).Gap(Style.Theme.Metrics.SpacingSmall).Enter())
        {
            if (_inAccordion)
            {
                if (note.Length > 0)
                    Dim("n", note);
            }
            else
                SectionHeading(title, note);
            Table(width, head, flex, rows, maxHeight);
        }
    }

    private void Table(float width, string[] head, float[] flex, IReadOnlyList<Row> rows, float maxHeight = 360f)
    {
        int selected = -1;
        for (int i = 0; i < rows.Count; i++)
            if (rows[i].Selected)
                selected = i;

        TableBuilder table = Origami.Table(_paper, "tbl", selected, i =>
        {
            if (i >= 0 && i < rows.Count)
                rows[i].Pick?.Invoke();
        });

        for (int c = 0; c < head.Length; c++)
            table.Column(head[c], flex[c]);

        foreach (Row row in rows)
        {
            TableRow tr = table.Row();
            for (int c = 0; c < head.Length; c++)
            {
                string text = c < row.Cells.Length ? row.Cells[c] : "";
                Color color = row.Colors != null && c < row.Colors.Length && row.Colors[c] is { } custom ? custom : c == 0 ? Style.Ink : Style.InkSoft;
                if (row.Icons != null && c < row.Icons.Length && row.Icons[c] is { } icon)
                    tr.Cell(text, color, icon);
                else
                    tr.Cell(text, color);
            }
        }

        float natural = 27f + rows.Count * 25f;
        if (natural > maxHeight)
            table.Scroll(width, maxHeight);
        else
            table.Width(UnitValue.Stretch());

        using (_paper.Column("tblbg").Width(UnitValue.Stretch()).Height(UnitValue.Auto + UnitValue.Stretch())
            .BackgroundColor(Style.CardFill).Rounded(Metrics.ContainerRounding).Enter())
            table.Show();
    }

    private readonly record struct Chip(string Text, string Tooltip, Color Fill, Color Border, Color Ink);

    private void ChipsSection(string title, float basis, string note, IReadOnlyList<Chip> chips)
        => Section(title, basis, note, _ => Chips(chips), true);

    private void Chips(IReadOnlyList<Chip> chips)
    {
        using (_paper.Row("chips").Width(UnitValue.Stretch()).Height(UnitValue.Auto).WrapContent().Gap(Metrics.Spacing).LineGap(Metrics.Spacing).Enter())
            for (int i = 0; i < chips.Count; i++)
            {
                Chip chip = chips[i];
                _paper.Box("c", i).Width(UnitValue.Auto).Height(UnitValue.Auto).Padding(Metrics.Padding, Metrics.Padding, Metrics.SpacingSmall, Metrics.SpacingSmall)
                    .BackgroundColor(chip.Fill).BorderColor(chip.Border).BorderWidth(1).Rounded(10)
                    .Text(chip.Text, Style.Mono).FontSize(Style.Tiny).TextColor(chip.Ink)
                    .OnHover(e => { if (chip.Tooltip.Length > 0) Origami.ShowTooltip(e.Source.Data.ID, chip.Tooltip); });
            }
    }

    private readonly record struct Segment(string Label, double Value, Color Color);

    private void StackedBar(string id, string label, IReadOnlyList<Segment> segments, Func<double, string> format)
    {
        double total = Math.Max(1e-9, segments.Sum(s => s.Value));
        using (_paper.Column(id).Width(UnitValue.Stretch()).Height(UnitValue.Auto).Gap(Metrics.Spacing).Enter())
        {
            if (label.Length > 0)
                Dim("label", label);

            using (_paper.Row("bar").Height(18).Rounded(Metrics.SmallRounding).Clip().BackgroundColor(Style.Theme.Popover).BorderColor(Style.Border).BorderWidth(1).Enter())
                for (int i = 0; i < segments.Count; i++)
                {
                    Segment s = segments[i];
                    string tip = $"{s.Label} {format(s.Value)}";
                    _paper.Box("s", i).Width(UnitValue.Percentage((float)(s.Value / total * 100.0))).Height(UnitValue.Stretch())
                        .BackgroundColor(s.Color).OnHover(e => Origami.ShowTooltip(e.Source.Data.ID, tip));
                }

            using (_paper.Row("legend").Width(UnitValue.Stretch()).Height(UnitValue.Auto).WrapContent().Gap(Metrics.PaddingLarge).LineGap(Metrics.SpacingSmall).Enter())
                for (int i = 0; i < segments.Count; i++)
                    using (_paper.Row("l", i).Width(UnitValue.Auto).Height(Metrics.CompactHeight).Gap(Metrics.Spacing).AlignItems(LayoutAlignment.Center).Enter())
                    {
                        Swatch("sw", segments[i].Color);
                        Dim("k", segments[i].Label);
                        Mono("v", format(segments[i].Value), Style.Ink);
                    }
        }
    }

    private readonly record struct Sparkline(string Title, double[] Values, Color Color, Func<double, string> Format, double[]? Second = null, Color? SecondColor = null, string Legend = "");

    private void ChartsSection(string title, string note, int current, IReadOnlyList<Sparkline> charts, float minColumn = 260f, float height = 120f)
        => Section(title, 0, note, width =>
        {
            int columns = Math.Max(1, (int)((width + 8f) / (minColumn + 8f)));
            float chartWidth = (width - 8f * (columns - 1)) / columns;
            using (_paper.Row("charts").Width(UnitValue.Stretch()).Height(UnitValue.Auto).WrapContent().Gap(8).LineGap(8).Enter())
                for (int i = 0; i < charts.Count; i++)
                    using (_paper.Column("ch", i).Width(chartWidth).Height(UnitValue.Auto).Gap(2).Enter())
                        SparklineChart(charts[i], current, chartWidth, height);
        });

    private void SparklineChart(Sparkline chart, int current, float width, float height)
    {
        double[] v = chart.Values;
        double avg = v.Length > 0 ? v.Average() : 0;
        double[] average = Enumerable.Repeat(avg, v.Length).ToArray();
        string cur = current >= 0 && current < v.Length ? chart.Format(v[current]) : "-";
        CartesianChart<object> c = Chart.CreateCartesian<object>(_paper, "c")
            .Title($"{chart.Title}   {cur}")
            .Size(width, height)
            .Legend(false)
            .Bordered(false)
            .Axes(false)
            .Grid(false, false)
            .Sampleable()
            .SampleLineColor(chart.Color)
            .ValueFormatter(chart.Format)
            .Padding(4);

        LineModule<object> lines = c.AddLineChart();
        lines.Series("avg", Style.InkFaint, average).Dashed();
        if (chart.Second != null)
            lines.Series(chart.Legend.Length > 0 ? chart.Legend.Split('/').Last().Trim() : "second", chart.SecondColor ?? Style.Snapshot, chart.Second).StrokeWidth(1.25f);
        lines.Series(chart.Title, chart.Color, v).StrokeWidth(1.25f);
        if (current >= 0 && current < v.Length)
            lines.MarkerLine(current, Style.Selection);
        c.Show();

        string min = v.Length > 0 ? chart.Format(v.Min()) : "-", max = v.Length > 0 ? chart.Format(v.Max()) : "-";
        using (_paper.Row("foot").Width(UnitValue.Stretch()).Height(UnitValue.Auto).WrapContent().Gap(8).LineGap(2).Enter())
        {
            if (chart.Legend.Length > 0)
                Mono("l", chart.Legend, Style.InkDim);
            Mono("r", $"min {min} / avg {chart.Format(avg)} / max {max}", Style.InkDim);
        }
    }
}
