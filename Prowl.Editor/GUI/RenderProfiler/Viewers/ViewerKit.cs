using System;
using System.Collections.Generic;

using Prowl.Editor.GUI.RenderProfiler.Data;
using Prowl.Editor.Profiling;
using Prowl.Editor.Theming;
using Prowl.Graphite;
using Prowl.OrigamiUI;
using Prowl.OrigamiUI.Charts;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using Color = System.Drawing.Color;

namespace Prowl.Editor.GUI.RenderProfiler.Viewers;

// Normalized card/chart drawer shared by every sub-viewer (FrameViewer, ViewViewer, PassViewer, ...)
// so each one doesn't reinvent section cards, line charts, value formatting, or history projection.
public static class ViewerKit
{
    public const float ChartHeight = 170f;
    public const float ChartTitleHeight = 18f;
    public const float ChartRowGap = 4f;
    public const float ChartRowHeight = ChartHeight + ChartTitleHeight + ChartRowGap;
    public const float SectionGap = 6f;
    public const float SectionCardWidth = 320f;
    public const float VramColumnWidth = 160f;
    public const float DenseLegendFontSize = 12f;
    public const float SelectionViewerHeaderHeight = 24f;

    private const double BytesPerKb = 1024d;
    private const double BytesPerMb = 1024d * 1024d;
    private const double BytesPerGb = 1024d * 1024d * 1024d;

    // Bold, high-contrast colours cycled across a chart's series when a chart has more series than a
    // deliberate per-series colour assignment covers (Live/Alloc/Free bins, buffer roles, ...).
    public static readonly Color[] SeriesPalette =
    {
        EditorTheme.Blue500, EditorTheme.Green500, EditorTheme.Amber500, EditorTheme.Red500, EditorTheme.Purple500,
        EditorTheme.Blue300, EditorTheme.Green300, EditorTheme.Amber300, EditorTheme.Red300, EditorTheme.Purple300,
        EditorTheme.Blue700, EditorTheme.Green700, EditorTheme.Amber700, EditorTheme.Red700, EditorTheme.Purple700,
    };

    private static readonly Dictionary<string, int> CounterIndex = BuildCounterIndex();

    private static Dictionary<string, int> BuildCounterIndex()
    {
        IReadOnlyList<CounterDef> registry = CountersCollector.Registry;
        var map = new Dictionary<string, int>(registry.Count);
        for (int i = 0; i < registry.Count; i++)
            map[registry[i].Name] = i;
        return map;
    }


    // ── Card drawer ────────────────────────────────────────────────

    public static void SectionHeading(Paper paper, string id, string title)
    {
        Origami.Label(paper, id, title)
            .Subheading()
            .AlignLeft()
            .Height(20f)
            .Show();
    }


    public static void SectionCard(Paper paper, string id, string title, Action drawContent)
    {
        using (paper.Column(id + "_card")
            .Height(UnitValue.Auto)
            .BorderColor(EditorTheme.BorderStrong)
            .BorderWidth(1f)
            .Rounded(EditorTheme.Roundness)
            .Padding(8f)
            .ColBetween(6f)
            .Enter())
        {
            SectionHeading(paper, id + "_hdr", title);
            drawContent();
        }
    }


    public static void DrawLineChart(Paper paper, string id, string title, string yLabel, Func<double, string> valueFormatter,
        UnitValue width, float height, bool legend,
        params (string Label, Color Color, IReadOnlyList<double> Values)[] series)
    {
        CartesianChart<int> chart = Chart.CreateCartesian<int>(paper, id)
            .Title(title)
            .Width(width)
            .Height(height)
            .Legend(legend)
            .LegendFontSize(DenseLegendFontSize)
            .XLabel("Frame")
            .YLabel(yLabel)
            .ValueFormatter(valueFormatter)
            .XTickFormatter(FormatCountCompact)
            .Padding(6f)
            .IncludeZero()
            .Sampleable()
            .Zoomable()
            .Pannable();

        LineModule<int> line = chart.AddLineChart();
        foreach ((string label, Color color, IReadOnlyList<double> values) in series)
            line.Series(label, color, values).StrokeWidth(1.5f);

        chart.Show();
    }


    // ── Value formatting ──────────────────────────────────────────

    public static string FormatBytesAuto(double bytes)
    {
        double abs = Math.Abs(bytes);
        if (abs >= BytesPerGb)
            return (bytes / BytesPerGb).ToString("0.#") + " GB";
        if (abs >= BytesPerMb)
            return (bytes / BytesPerMb).ToString("0.#") + " MB";
        if (abs >= BytesPerKb)
            return (bytes / BytesPerKb).ToString("0.#") + " KB";
        return bytes.ToString("0") + " B";
    }

    public static string FormatCountCompact(double value)
    {
        double abs = Math.Abs(value);
        if (abs >= 1_000_000d)
            return (value / 1_000_000d).ToString("0.#") + "M";
        if (abs >= 1_000d)
            return (value / 1_000d).ToString("0.#") + "K";
        return value.ToString("0.#");
    }

    public static string FormatCountCompact(int value) => FormatCountCompact((double)value);


    // ── History series helpers ────────────────────────────────────

    public static List<double> FrameSeries(IProfilerHistory history, Func<ProfiledFrame, double> selector)
    {
        IReadOnlyList<ProfiledFrame> frames = history.Frames;
        var values = new List<double>(frames.Count);
        foreach (ProfiledFrame frame in frames)
            values.Add(selector(frame));
        return values;
    }

    // Per-frame history for one named view. Matches by ProfiledView.Name rather than by reference -
    // each retained ProfiledFrame owns its own ProfiledView instances, so the same logical view
    // (e.g. "Game") is a different object in every history entry. Frames where the view wasn't
    // touched contribute 0.
    public static List<double> ViewSeries(IProfilerHistory history, string viewName, Func<ProfiledView, double> selector)
    {
        IReadOnlyList<ProfiledFrame> frames = history.Frames;
        var values = new List<double>(frames.Count);
        foreach (ProfiledFrame frame in frames)
        {
            double value = 0d;
            foreach (ProfiledView view in frame.Views)
            {
                if (view.Name == viewName)
                {
                    value = selector(view);
                    break;
                }
            }
            values.Add(value);
        }
        return values;
    }

    // Per-frame history for one named pass within one named view - see ViewSeries for why matching
    // is by name/index rather than reference. Matched by ProfiledPass.Index (stable across frames),
    // not by position in ProfiledView.Passes. Frames where the view/pass wasn't touched contribute 0.
    public static List<double> PassSeries(IProfilerHistory history, string viewName, int passIndex, Func<ProfiledPass, double> selector)
    {
        IReadOnlyList<ProfiledFrame> frames = history.Frames;
        var values = new List<double>(frames.Count);
        foreach (ProfiledFrame frame in frames)
        {
            double value = 0d;
            foreach (ProfiledView view in frame.Views)
            {
                if (view.Name != viewName)
                    continue;
                foreach (ProfiledPass pass in view.Passes)
                {
                    if (pass.Index == passIndex)
                    {
                        value = selector(pass);
                        break;
                    }
                }
                break;
            }
            values.Add(value);
        }
        return values;
    }

    public static IReadOnlyList<double> CounterSeries(IProfilerHistory history, string name) => history.Counter(name);

    public static double CounterValue(ProfiledFrame? frame, string name)
    {
        if (frame == null || !CounterIndex.TryGetValue(name, out int index))
            return 0d;
        return frame.Counters[index].Value;
    }
}
