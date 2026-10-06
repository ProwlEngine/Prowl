// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Vector;

using Color = System.Drawing.Color;

using Prowl.OrigamiUI;

namespace Prowl.OrigamiUI.Charts;

/// <summary>Flame graph. Each node is a bar whose width is its share of the total and whose row is its
/// depth. Zoom and pan act on the value axis only.</summary>
public sealed class FlameGraphChart<T> : ChartCore<FlameGraphChart<T>, T>
{
    private sealed class Node
    {
        public string Label = "";
        public double Value;
        public double Start;
        public Color Color;
        public int Depth;
        public int Index;
        public bool Hidden;
        public readonly List<Node> Children = new();
    }

    private readonly List<Node> _roots = new();
    private readonly List<Node> _flat = new();
    private readonly List<List<Node>> _rows = new();
    private double _total;

    private Func<T, string>? _nameSelector;
    private Func<T, double>? _valueSelector;
    private Func<T, IEnumerable<T>>? _childrenSelector;
    private Func<T, int, Color>? _colorFunction;
    private bool _labels = true;
    private bool _tooltip = true;
    private bool _zoomable, _pannable;
    private float _rowHeight = 18f;

    private const string PlotSizeKey = "flame_size";
    private const int MaxDepth = 32;
    private const float MinRowHeight = 6f;
    private const float MinLabelWidth = 60f;
    private const float MinLabelHeight = 12f;
    private const float CornerRadius = 2f;

    internal FlameGraphChart(Paper paper, string id, OrigamiTheme theme, IReadOnlyList<T>? data)
        : base(paper, id, theme, data) { }

    public FlameGraphChart<T> Name(Func<T, string> selector) { _nameSelector = selector; return this; }

    /// <summary>A node's own weight. Non-positive values make it weigh the sum of its children.</summary>
    public FlameGraphChart<T> Value(Func<T, double> selector) { _valueSelector = selector; return this; }

    public FlameGraphChart<T> Children(Func<T, IEnumerable<T>> selector) { _childrenSelector = selector; return this; }

    /// <summary>Node colour from the item and its depth.</summary>
    public FlameGraphChart<T> ColorFunction(Func<T, int, Color> selector) { _colorFunction = selector; return this; }

    public FlameGraphChart<T> Labels(bool show = true) { _labels = show; return this; }
    public FlameGraphChart<T> Tooltip(bool show = true) { _tooltip = show; return this; }
    public FlameGraphChart<T> Zoomable(bool enable = true) { _zoomable = enable; return this; }
    public FlameGraphChart<T> Pannable(bool enable = true) { _pannable = enable; return this; }

    /// <summary>Height of one depth row in pixels, gap included. Default 18.</summary>
    public FlameGraphChart<T> RowHeight(float height) { _rowHeight = MathF.Max(MinRowHeight, height); return this; }

    protected override IReadOnlyList<LegendEntry> BuildLegendEntries()
    {
        _roots.Clear();
        _flat.Clear();
        _rows.Clear();
        _total = 0d;

        if (_data != null)
            foreach (T item in _data)
            {
                Node? node = BuildNode(item, 0);
                if (node != null) _roots.Add(node);
            }

        Index(_roots);

        List<Node> legend = _roots.Count == 1 ? _roots[0].Children : _roots;
        bool anyHidden = false;
        foreach (Node node in legend)
            if (IsLegendHidden(node.Index)) { Hide(node); anyHidden = true; }

        foreach (Node root in _roots)
        {
            if (anyHidden) Reaggregate(root);
            Layout(root, _total);
            _total += root.Value;
        }

        var entries = new List<LegendEntry>(legend.Count);
        foreach (Node node in legend)
            entries.Add(new LegendEntry(NodeName(node), node.Color, node.Index,
                LegendShowValueEnabled ? FormatValue(node.Value) : null, node.Hidden));
        return entries;
    }

    private Node? BuildNode(T item, int depth)
    {
        if (item is null || depth > MaxDepth) return null;

        var node = new Node { Label = _nameSelector?.Invoke(item) ?? "", Depth = depth };

        double childSum = 0d;
        IEnumerable<T>? kids = _childrenSelector?.Invoke(item);
        if (kids != null)
            foreach (T kid in kids)
            {
                Node? child = BuildNode(kid, depth + 1);
                if (child == null) continue;
                node.Children.Add(child);
                childSum += child.Value;
            }

        double own = _valueSelector != null ? _valueSelector(item) : 0d;
        node.Value = IsFinite(own) && own > 0d ? Math.Max(own, childSum) : childSum;
        if (node.Value <= 0d) return null;

        if (_colorFunction != null) node.Color = _colorFunction(item, depth);
        return node;
    }

    private void Index(List<Node> siblings)
    {
        for (int i = 0; i < siblings.Count; i++)
        {
            Node node = siblings[i];
            node.Index = _flat.Count;
            _flat.Add(node);
            if (_colorFunction == null) node.Color = RampColor(i, node.Depth);

            while (_rows.Count <= node.Depth) _rows.Add(new List<Node>());
            _rows[node.Depth].Add(node);

            Index(node.Children);
        }
    }

    private static void Hide(Node node)
    {
        node.Hidden = true;
        foreach (Node child in node.Children) Hide(child);
    }

    private static double Reaggregate(Node node)
    {
        if (node.Hidden)
        {
            node.Value = 0d;
            foreach (Node child in node.Children) Reaggregate(child);
            return 0d;
        }

        if (node.Children.Count == 0) return node.Value;

        double sum = 0d;
        foreach (Node child in node.Children) sum += Reaggregate(child);
        return node.Value = sum;
    }

    private static void Layout(Node node, double start)
    {
        node.Start = start;
        foreach (Node child in node.Children)
        {
            Layout(child, start);
            start += child.Value;
        }
    }

    private static string NodeName(Node node) => node.Label.Length > 0 ? node.Label : "Node " + node.Index;

    protected override void DrawPlot()
    {
        if (_total <= 0d)
        {
            DrawEmpty();
            return;
        }

        ElementBuilder plotBox = _paper.Box(_id + "_chart_plot").Clip();

        using (plotBox.Enter())
        {
            ElementHandle plotEl = _paper.CurrentParent;
            WireView(plotBox, plotEl, _zoomable, _pannable, false);

            ViewRect view = View;
            Node? hover = null;

            if (_tooltip)
            {
                TrackPointer(plotBox, plotEl);

                Float2 size = _paper.GetElementStorage(plotEl, PlotSizeKey, new Float2(0f, 0f));
                if (size.X > 0f && TryGetPointer(plotEl, out Float2 pointer))
                {
                    hover = HitTest(pointer, size.X, view);
                    if (hover != null)
                    {
                        var rows = new List<(Color Color, string Text)>
                        {
                            (hover.Color, FormatValue(hover.Value)),
                            (hover.Color, (hover.Value / _total * 100d).ToString("0.#") + "%"),
                        };
                        Popup(pointer.X, Math.Clamp(pointer.Y + 8f, 0f, size.Y), 0f, size.X, NodeName(hover), rows);
                    }
                }
            }

            _paper.Draw((canvas, rect) => Paint(canvas, rect, plotEl, view, hover));
        }
    }

    private Node? HitTest(Float2 pointer, float width, ViewRect view)
    {
        int depth = (int)MathF.Floor(pointer.Y / _rowHeight);
        if (depth < 0 || depth >= _rows.Count || pointer.Y - depth * _rowHeight > _rowHeight - PaddingValue) return null;

        double value = (view.X + pointer.X / width * view.W) * _total;
        List<Node> row = _rows[depth];

        int lo = 0, hi = row.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            Node node = row[mid];
            if (value < node.Start) hi = mid - 1;
            else if (value >= node.Start + node.Value) lo = mid + 1;
            else return node.Hidden ? null : node;
        }
        return null;
    }

    private void Paint(Canvas canvas, Rect rect, ElementHandle plotEl, ViewRect view, Node? hover)
    {
        float ox = (float)rect.Min.X, oy = (float)rect.Min.Y;
        float w = (float)rect.Size.X, h = (float)rect.Size.Y;
        if (w < 4f || h < 4f) return;

        _paper.SetElementStorage(plotEl, PlotSizeKey, new Float2(w, h));

        float barHeight = MathF.Max(1f, _rowHeight - PaddingValue);
        bool labels = _labels && barHeight >= MinLabelHeight;

        foreach (Node node in _flat)
        {
            if (node.Hidden) continue;

            float y = oy + node.Depth * _rowHeight;
            if (y >= oy + h) continue;

            float left = ox + (float)((node.Start / _total - view.X) / view.W) * w;
            float right = ox + (float)(((node.Start + node.Value) / _total - view.X) / view.W) * w;
            left = MathF.Max(left, ox);
            right = MathF.Min(right, ox + w);
            float width = right - left;
            if (width < 1f) continue;

            canvas.BeginPath();
            canvas.RoundedRect(left, y, width, barHeight, MathF.Min(CornerRadius, width * 0.5f));
            canvas.SetFillColor(ToC32(node.Color));
            canvas.Fill();

            if (node == hover)
            {
                canvas.SetStrokeColor(ToC32(_theme.Ink.C700));
                canvas.SetStrokeWidth(1.5f);
                canvas.Stroke();
            }

            if (labels && width >= MinLabelWidth)
            {
                float luminance = (0.2126f * node.Color.R + 0.7152f * node.Color.G + 0.0722f * node.Color.B) / 255f;
                DrawText(canvas, FitText(canvas, node.Label, width - 6f), left + 3f, y + barHeight * 0.5f, 0f, 0.5f,
                    luminance > 0.55f ? _theme.Ink.C100 : _theme.Ink.C700);
            }
        }
    }
}
