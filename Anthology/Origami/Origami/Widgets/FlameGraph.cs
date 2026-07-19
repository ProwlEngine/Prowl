// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Vector;

using Color = System.Drawing.Color;
using TextAlignment = Prowl.PaperUI.TextAlignment;

namespace Prowl.OrigamiUI;

/// <summary>
/// A single node in a flame graph tree. Fully data-agnostic: the widget knows only
/// <see cref="Label"/>, a time <see cref="Start"/> and <see cref="Duration"/> on a shared axis, and
/// nested <see cref="Children"/>. <see cref="Start"/>/<see cref="Duration"/> are absolute values on
/// the same axis for every node (a child's span lies inside its parent's span). The axis unit is up
/// to the caller (milliseconds, bytes, samples, ...); a value formatter turns it into display text.
/// </summary>
public sealed class FlameNode
{
    /// <summary>Text drawn on the bar and shown first in the tooltip.</summary>
    public string Label = "";
    /// <summary>Start position on the shared axis (absolute).</summary>
    public double Start;
    /// <summary>Length on the shared axis. Maps to the bar's width.</summary>
    public double Duration;
    /// <summary>Nested nodes, each drawn one row below this one within this node's span.</summary>
    public List<FlameNode> Children = new();
    /// <summary>Explicit bar fill. Overrides the widget's color function and default palette.</summary>
    public Color? Color;
    /// <summary>Extra text appended to the tooltip (below the label and value). May contain newlines.</summary>
    public string? Tooltip;
    /// <summary>Opaque host payload; never read by the widget.</summary>
    public object? UserData;

    public FlameNode() { }

    public FlameNode(string label, double start, double duration)
    {
        Label = label;
        Start = start;
        Duration = duration;
    }
}

/// <summary>
/// Fluent builder for a generic flame graph: horizontally stacked bars where depth grows downward
/// and a bar's x/width map from <see cref="FlameNode.Start"/>/<see cref="FlameNode.Duration"/> across
/// the widget. Bars carry clipped labels, a hover highlight and a hover tooltip. Scroll to zoom the
/// axis (cursor anchored), left-drag to pan, click a bar to zoom into it, double-click to reset.
///
/// Construct via <see cref="Origami.FlameGraph"/>; chain modifiers; call <see cref="Show"/> to render.
/// </summary>
public sealed class FlameGraphBuilder
{
    private readonly Paper _paper;
    private readonly string _id;
    private readonly OrigamiTheme _theme;

    private UnitValue _width = UnitValue.Stretch();
    private float _height = 260f;
    private float? _rowHeight;
    private IReadOnlyList<FlameNode> _roots = Array.Empty<FlameNode>();
    private OrigamiVariant _variant = OrigamiVariant.Primary;
    private Func<double, string>? _valueFormatter;
    private Func<FlameNode, int, Color>? _colorFunc;
    private bool _zoomable = true;
    private bool _pannable = true;
    private Color? _backgroundColor;

    internal FlameGraphBuilder(Paper paper, string id, OrigamiTheme theme)
    {
        _paper = paper ?? throw new ArgumentNullException(nameof(paper));
        _id = id ?? throw new ArgumentNullException(nameof(id));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
    }

    /// <summary>Fix the overall widget size to an explicit pixel width and height.</summary>
    public FlameGraphBuilder Size(float width, float height) { _width = MathF.Max(32f, width); _height = MathF.Max(32f, height); return this; }
    /// <summary>Fix the widget width in pixels. Default inherits the parent's width (<see cref="UnitValue.Stretch"/>).</summary>
    public FlameGraphBuilder Width(float width) { _width = MathF.Max(32f, width); return this; }
    /// <summary>Set the widget width as a layout unit (e.g. <see cref="UnitValue.Stretch"/> to inherit the parent's width, or <see cref="UnitValue.Percentage"/>). Default is Stretch.</summary>
    public FlameGraphBuilder Width(UnitValue width) { _width = width; return this; }
    public FlameGraphBuilder Height(float height) { _height = MathF.Max(32f, height); return this; }

    /// <summary>Per-row (per-depth) height in pixels. Defaults to the theme row height.</summary>
    public FlameGraphBuilder RowHeight(float px) { _rowHeight = MathF.Max(8f, px); return this; }

    /// <summary>Set a single root node.</summary>
    public FlameGraphBuilder Root(FlameNode root) { _roots = new[] { root ?? throw new ArgumentNullException(nameof(root)) }; return this; }

    /// <summary>Set several root nodes laid out along the shared axis at depth 0.</summary>
    public FlameGraphBuilder Roots(IReadOnlyList<FlameNode> roots) { _roots = roots ?? throw new ArgumentNullException(nameof(roots)); return this; }

    /// <summary>Format an axis value (duration) for tooltips. Default is a plain numeric string.</summary>
    public FlameGraphBuilder ValueFormatter(Func<double, string> formatter) { _valueFormatter = formatter; return this; }

    /// <summary>Choose a bar fill from the node and its depth. A node's own <see cref="FlameNode.Color"/> still wins.</summary>
    public FlameGraphBuilder ColorFunction(Func<FlameNode, int, Color> colorFunc) { _colorFunc = colorFunc; return this; }

    /// <summary>Background colour of the flame graph panel. Default is none (transparent), matching bare widgets.</summary>
    public FlameGraphBuilder BackgroundColor(Color color) { _backgroundColor = color; return this; }

    public FlameGraphBuilder Variant(OrigamiVariant v) { _variant = v; return this; }
    public FlameGraphBuilder Primary() => Variant(OrigamiVariant.Primary);
    public FlameGraphBuilder Success() => Variant(OrigamiVariant.Success);
    public FlameGraphBuilder Warning() => Variant(OrigamiVariant.Warning);
    public FlameGraphBuilder Danger() => Variant(OrigamiVariant.Danger);
    public FlameGraphBuilder Info() => Variant(OrigamiVariant.Info);

    /// <summary>Enable/disable scroll-to-zoom and click-to-zoom. Default on.</summary>
    public FlameGraphBuilder Zoomable(bool zoomable = true) { _zoomable = zoomable; return this; }

    /// <summary>Enable/disable left-drag panning of the axis. Default on.</summary>
    public FlameGraphBuilder Pannable(bool pannable = true) { _pannable = pannable; return this; }

    private sealed class FlameState
    {
        public double ViewStart, ViewSpan;
        public bool ViewInit;
        public float ScreenX, ScreenY;
        public float Width = 480f, Height = 260f;
    }

    private struct Bar
    {
        public FlameNode Node;
        public int Depth;
        public double Start;
        public double End;
    }

    private const float TopPad = 0f;
    private const float BarGap = 1f;
    private const float LabelPad = 5f;
    private const float MinLabelWidth = 22f;

    public void Show()
    {
        var font = _theme.Font;
        var ink = _theme.Ink;
        Color borderSoft = _theme.BorderSoft;
        float rowH = _rowHeight ?? MathF.Max(16f, _theme.Metrics.RowHeight);

        var bars = new List<Bar>();
        Flatten(_roots, 0, bars);

        double fullStart = double.MaxValue, fullEnd = double.MinValue;
        foreach (var r in _roots)
        {
            if (r == null) continue;
            fullStart = Math.Min(fullStart, r.Start);
            fullEnd = Math.Max(fullEnd, r.Start + Math.Max(0d, r.Duration));
        }
        if (fullEnd <= fullStart) { fullStart = 0d; fullEnd = 1d; }
        double fullSpan = fullEnd - fullStart;

        var container = _paper.Box(_id)
            .Width(_width).Height(_height)
            .Rounded(_theme.Metrics.ContainerRounding)
            .BorderColor(borderSoft).BorderWidth(1f)
            .Clip();
        if (_backgroundColor.HasValue) container.BackgroundColor(_backgroundColor.Value);
        var handle = container._handle;

        var st = _paper.GetElementStorage<FlameState>(handle, "state", null!);
        if (st == null) { st = new FlameState(); _paper.SetElementStorage(handle, "state", st); }
        if (!st.ViewInit)
        {
            st.ViewInit = true;
            st.ViewStart = fullStart;
            st.ViewSpan = fullSpan;
        }

        double minSpan = Math.Max(fullSpan * 1e-4, double.Epsilon);
        st.ViewSpan = Math.Clamp(st.ViewSpan, minSpan, fullSpan);
        st.ViewStart = Math.Clamp(st.ViewStart, fullStart, fullEnd - st.ViewSpan);

        bool zoomed = st.ViewSpan < fullSpan - fullSpan * 1e-6 || st.ViewStart > fullStart + fullSpan * 1e-6;

        float localX = (float)_paper.PointerPos.X - st.ScreenX;
        float localY = (float)_paper.PointerPos.Y - st.ScreenY;
        bool overWidget = localX >= 0 && localX <= st.Width && localY >= 0 && localY <= st.Height;

        int hoverIdx = -1;
        if (overWidget)
            for (int i = 0; i < bars.Count; i++)
                if (BarScreenRect(bars[i], st, rowH, out float bx, out float by, out float bw, out float bh)
                    && localX >= bx && localX <= bx + bw && localY >= by && localY <= by + bh)
                    hoverIdx = i;

        var snap = new Snapshot
        {
            Bars = bars,
            HoverIdx = hoverIdx,
            ViewStart = st.ViewStart,
            ViewSpan = st.ViewSpan,
            RowH = rowH,
            Font = font,
            PointerX = localX,
            PointerY = localY,
            OverWidget = overWidget,
            TextCol = ToC32(ink.C700),
            TextDim = ToC32(ink.C500),
            BarStroke = ToC32(Color.FromArgb(60, 0, 0, 0)),
            HoverStroke = ToC32(_theme.Get(_variant).C600),
            TooltipBg = ToC32(_theme.Popover),
            TooltipBorder = ToC32(borderSoft),
            AccentPalette = BuildPalette(),
            Fallback = _theme.Get(_variant).C500,
        };

        using (container.Enter())
        {
            var bg = _paper.Box($"{_id}_bg")
                .PositionType(PositionType.SelfDirected).Left(0).Top(0)
                .Width(UnitValue.Percentage(100)).Height(UnitValue.Percentage(100))
                .Cursor(_pannable ? PaperCursor.Default : PaperCursor.Default)
                .OnPostLayout((h, r) => { st.ScreenX = (float)r.Min.X; st.ScreenY = (float)r.Min.Y; st.Width = (float)r.Size.X; st.Height = (float)r.Size.Y; });

            if (_zoomable)
                bg.OnScroll(st, (s, e) =>
                {
                    float w = (float)e.ElementRect.Size.X;
                    float lx = (float)(e.PointerPosition.X - e.ElementRect.Min.X);
                    double frac = w > 0 ? Math.Clamp(lx / w, 0d, 1d) : 0d;
                    double anchor = s.ViewStart + frac * s.ViewSpan;
                    double factor = Math.Exp(-e.Delta * 0.16);
                    double newSpan = Math.Clamp(s.ViewSpan * factor, minSpan, fullSpan);
                    s.ViewStart = anchor - frac * newSpan;
                    s.ViewSpan = newSpan;
                    s.ViewStart = Math.Clamp(s.ViewStart, fullStart, fullEnd - s.ViewSpan);
                });

            if (_pannable)
            {
                bg.OnDragging(st, (s, e) =>
                {
                    float w = (float)e.ElementRect.Size.X;
                    double dt = w > 0 ? e.Delta.X / w * s.ViewSpan : 0d;
                    s.ViewStart = Math.Clamp(s.ViewStart - dt, fullStart, fullEnd - s.ViewSpan);
                });
            }

            if (_zoomable)
            {
                var barsForClick = bars;
                bg.OnClick(st, (s, e) =>
                {
                    float cx = (float)e.PointerPosition.X - s.ScreenX;
                    float cy = (float)e.PointerPosition.Y - s.ScreenY;
                    int hit = -1;
                    for (int i = 0; i < barsForClick.Count; i++)
                        if (BarScreenRect(barsForClick[i], s, rowH, out float bx, out float by, out float bw, out float bh)
                            && cx >= bx && cx <= bx + bw && cy >= by && cy <= by + bh)
                            hit = i;
                    if (hit >= 0)
                    {
                        var b = barsForClick[hit];
                        double span = Math.Max(minSpan, b.End - b.Start);
                        s.ViewStart = b.Start;
                        s.ViewSpan = span;
                    }
                });
                bg.OnDoubleClick(st, (s, e) => { s.ViewStart = fullStart; s.ViewSpan = fullSpan; });
            }

            using (bg.Enter())
                _paper.Draw((canvas, rect) => Paint(canvas, rect, in snap));

            if (zoomed && _zoomable && font != null)
            {
                float rbW = 54f, rbH = 20f;
                var reset = _paper.Box($"{_id}_reset")
                    .PositionType(PositionType.SelfDirected)
                    .Left(st.Width - rbW - 8f).Top(8f).Width(rbW).Height(rbH)
                    .Rounded(6f)
                    .BackgroundColor(_theme.Popover)
                    .BorderColor(borderSoft).BorderWidth(1f)
                    .Alignment(TextAlignment.MiddleCenter)
                    .Cursor(PaperCursor.Pointer)
                    .Text("Reset", font).FontSize(_theme.Metrics.FontSizeSmall).TextColor(ink.C600);
                reset.OnClick(st, (s, e) => { s.ViewStart = fullStart; s.ViewSpan = fullSpan; });
            }

            if (bars.Count == 0 && font != null)
            {
                _paper.Box($"{_id}_empty")
                    .PositionType(PositionType.SelfDirected).Left(0).Top(0)
                    .Width(UnitValue.Percentage(100)).Height(UnitValue.Percentage(100))
                    .Alignment(TextAlignment.MiddleCenter).IsNotInteractable()
                    .Text("No data", font).FontSize(_theme.Metrics.FontSize).TextColor(ink.C400);
            }
        }
    }

    private static void Flatten(IReadOnlyList<FlameNode> nodes, int depth, List<Bar> outBars)
    {
        if (nodes == null) return;
        foreach (var n in nodes)
        {
            if (n == null) continue;
            outBars.Add(new Bar { Node = n, Depth = depth, Start = n.Start, End = n.Start + Math.Max(0d, n.Duration) });
            if (n.Children != null && n.Children.Count > 0)
                Flatten(n.Children, depth + 1, outBars);
        }
    }

    private bool BarScreenRect(Bar b, FlameState st, float rowH, out float x, out float y, out float w, out float h)
    {
        double span = st.ViewSpan <= 0 ? 1d : st.ViewSpan;
        double x0 = (b.Start - st.ViewStart) / span * st.Width;
        double x1 = (b.End - st.ViewStart) / span * st.Width;
        x = (float)x0;
        w = (float)Math.Max(0d, x1 - x0);
        y = TopPad + b.Depth * rowH;
        h = MathF.Max(1f, rowH - BarGap);
        return w > 0.5f && x < st.Width && x + w > 0f && y < st.Height;
    }

    private Color ResolveColor(FlameNode node, int depth, Color[] palette, Color fallback)
    {
        if (node.Color.HasValue) return node.Color.Value;
        if (_colorFunc != null) return _colorFunc(node, depth);
        if (palette.Length == 0) return fallback;
        int idx = (int)((uint)StableHash(node.Label) % (uint)palette.Length);
        return palette[idx];
    }

    private Color[] BuildPalette() => new[]
    {
        _theme.Blue.C500, _theme.Green.C500, _theme.Amber.C500, _theme.Red.C500,
        _theme.Primary.C500, _theme.Blue.C400, _theme.Green.C600, _theme.Amber.C600,
    };

    private static int StableHash(string s)
    {
        int h = 17;
        if (s != null)
            foreach (char c in s) h = h * 31 + c;
        return h;
    }

    private string FormatValue(double v) => _valueFormatter != null ? _valueFormatter(v) : v.ToString("0.###");

    private struct Snapshot
    {
        public List<Bar> Bars;
        public int HoverIdx;
        public double ViewStart, ViewSpan;
        public float Width, Height, RowH;
        public FontFile? Font;
        public float PointerX, PointerY;
        public bool OverWidget;
        public Color32 TextCol, TextDim, BarStroke, HoverStroke, TooltipBg, TooltipBorder;
        public Color[] AccentPalette;
        public Color Fallback;
    }

    private void Paint(Canvas canvas, Rect rect, in Snapshot sIn)
    {
        Snapshot s = sIn;
        s.Width = (float)rect.Size.X;
        s.Height = (float)rect.Size.Y;

        float ox = (float)rect.Min.X, oy = (float)rect.Min.Y;
        double span = s.ViewSpan <= 0 ? 1d : s.ViewSpan;
        float labelFont = MathF.Min(s.RowH - 6f, _theme.Metrics.FontSizeSmall);
        if (labelFont < 7f) labelFont = 7f;

        canvas.SaveState();
        canvas.IntersectScissor(ox, oy, s.Width, s.Height);

        for (int i = 0; i < s.Bars.Count; i++)
        {
            var b = s.Bars[i];
            double x0 = (b.Start - s.ViewStart) / span * s.Width;
            double x1 = (b.End - s.ViewStart) / span * s.Width;
            float x = ox + (float)x0;
            float w = (float)Math.Max(0d, x1 - x0);
            float y = oy + TopPad + b.Depth * s.RowH;
            float h = MathF.Max(1f, s.RowH - BarGap);
            if (w <= 0.5f || x + w < ox || x > ox + s.Width || y > oy + s.Height) continue;

            float drawX = MathF.Max(x, ox);
            float drawW = MathF.Min(x + w, ox + s.Width) - drawX;
            if (drawW <= 0.5f) continue;

            Color fill = ResolveColor(b.Node, b.Depth, s.AccentPalette, s.Fallback);
            bool hovered = i == s.HoverIdx;

            canvas.RoundedRect(drawX, y, drawW, h, 2.5f, 2.5f, 2.5f, 2.5f);
            canvas.SetFillColor(hovered ? Lighten(fill, 0.16f) : fill);
            canvas.Fill();

            canvas.SaveState();
            canvas.RoundedRect(drawX, y, drawW, h, 2.5f, 2.5f, 2.5f, 2.5f);
            canvas.SetStrokeColor(hovered ? s.HoverStroke : s.BarStroke);
            canvas.SetStrokeWidth(hovered ? 1.5f : 1f);
            canvas.Stroke();
            canvas.RestoreState();

            if (s.Font != null && drawW >= MinLabelWidth)
            {
                canvas.SaveState();
                canvas.IntersectScissor(drawX + LabelPad, y, drawW - LabelPad * 2f, h);
                var ts = canvas.MeasureText(b.Node.Label, labelFont, s.Font);
                float ty = y + (h - (float)ts.Y) * 0.5f;
                canvas.DrawText(b.Node.Label, drawX + LabelPad, ty, ToC32(BestTextColor(fill)), labelFont, s.Font);
                canvas.RestoreState();
            }
        }

        canvas.RestoreState();

        if (s.OverWidget && s.HoverIdx >= 0 && s.Font != null)
            PaintTooltip(canvas, ox, oy, in s);
    }

    private void PaintTooltip(Canvas canvas, float ox, float oy, in Snapshot s)
    {
        var node = s.Bars[s.HoverIdx].Node;
        float fs = _theme.Metrics.FontSizeSmall;
        var lines = new List<string> { node.Label, FormatValue(node.Duration) };
        if (!string.IsNullOrEmpty(node.Tooltip))
            foreach (var part in node.Tooltip!.Split('\n'))
                lines.Add(part);

        float pad = 8f, lineGap = 3f;
        float maxW = 0f, lineH = 0f;
        foreach (var ln in lines)
        {
            var m = canvas.MeasureText(ln.Length == 0 ? " " : ln, fs, s.Font!);
            maxW = MathF.Max(maxW, (float)m.X);
            lineH = MathF.Max(lineH, (float)m.Y);
        }

        float boxW = maxW + pad * 2f;
        float boxH = pad * 2f + lines.Count * lineH + (lines.Count - 1) * lineGap;
        float bx = s.PointerX + 14f;
        float by = s.PointerY + 16f;
        if (bx + boxW > s.Width) bx = MathF.Max(2f, s.PointerX - boxW - 12f);
        if (by + boxH > s.Height) by = MathF.Max(2f, s.Height - boxH - 2f);
        bx += ox; by += oy;

        canvas.SaveState();
        canvas.RoundedRect(bx, by, boxW, boxH, 6f, 6f, 6f, 6f);
        canvas.SetFillColor(s.TooltipBg);
        canvas.Fill();
        canvas.RoundedRect(bx, by, boxW, boxH, 6f, 6f, 6f, 6f);
        canvas.SetStrokeColor(s.TooltipBorder);
        canvas.SetStrokeWidth(1f);
        canvas.Stroke();

        float ty = by + pad;
        for (int i = 0; i < lines.Count; i++)
        {
            canvas.DrawText(lines[i], bx + pad, ty, i == 0 ? s.TextCol : s.TextDim, fs, s.Font!);
            ty += lineH + lineGap;
        }
        canvas.RestoreState();
    }

    private static Color BestTextColor(Color bg)
    {
        double lum = (0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B) / 255.0;
        return lum > 0.6 ? Color.FromArgb(255, 20, 20, 24) : Color.FromArgb(255, 244, 244, 248);
    }

    private static Color Lighten(Color c, float amount)
    {
        int r = (int)Math.Clamp(c.R + (255 - c.R) * amount, 0, 255);
        int g = (int)Math.Clamp(c.G + (255 - c.G) * amount, 0, 255);
        int b = (int)Math.Clamp(c.B + (255 - c.B) * amount, 0, 255);
        return Color.FromArgb(c.A, r, g, b);
    }

    private static Color32 ToC32(Color c) => new Color32(c.R, c.G, c.B, c.A);
}
