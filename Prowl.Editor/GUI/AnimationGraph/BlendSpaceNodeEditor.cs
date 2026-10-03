// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.Events;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.AnimationNodes;
using Prowl.Vector;

using Canvas = Prowl.Quill.Canvas;
using Color = System.Drawing.Color;
using Color32 = Prowl.Vector.Color32;

namespace Prowl.Editor.Inspector;

/// <summary>A blend space is a layout, so it is laid out on a chart rather than listed, with each sample's exact value under it.</summary>
[AnimationNodeEditor(typeof(Blend1DNode))]
[AnimationNodeEditor(typeof(Blend2DNode))]
internal sealed class BlendSpaceNodeEditor : AnimationNodeEditor
{
    private readonly Dictionary<string, BlendChart> _charts = new();

    private bool TwoD => Node.Id == AnimationNodeIds.Blend2D;

    public override bool HasEntries => true;

    // A line reports no value of its own while it runs, so it shows the value driving it.
    public override string LiveText(AnimationGraphEditing editing, GraphNodeRecord record, string reported)
    {
        if (reported.Length > 0 || TwoD || record.Inputs.Count == 0 || editing.RecordOf(record.Inputs[0].Node) is not { } driver)
            return reported;
        return editing.LiveValue(driver);
    }

    public override void DrawEntry(AnimationGraphEditing editing, Paper paper, string id, GraphNodeRecord record, int pin)
    {
        if (pin >= record.Inputs.Count) return;

        GraphInputRecord input = record.Inputs[pin];
        if (TwoD)
            Origami.Float2Field(paper, id, input.Position, v => editing.Edit("Move Blend Sample", () => input.Position = v, false)).Show();
        else
            Origami.NumericField<float>(paper, id, input.Value, v => editing.Edit("Set Blend Threshold", () => input.Value = v, false))
                .Width(UnitValue.Stretch()).Height(Origami.Current.Metrics.RowHeight).Show();
    }

    public override void BuildCard(AnimationNodeCard card, GraphNodeRecord record)
    {
        if (TwoD) card.Width = 264f;
        float height = TwoD ? 180f : 64f;
        card.AddSettings();
        card.Add(height, (paper, id) => DrawChart(card.Editing, paper, id, record, height, live: true));
        card.AddEntries();
        card.AddToggles();
    }

    public override void DrawInspector(AnimationNodeInspector inspector, GraphNodeRecord record)
    {
        inspector.Description();
        inspector.Settings();
        inspector.Header("ag_h_blend", TwoD ? "Blend Space" : "Blend Line");

        if (record.Inputs.Count <= Node.VariadicStart)
        {
            inspector.Note("ag_blendEmpty", "Wire poses into the node's open socket, then drag them into place here.");
            return;
        }

        DrawChart(inspector.Editing, inspector.Paper, "ag_blend", record, null, live: false);
        inspector.Entries(header: null);
    }

    private void DrawChart(AnimationGraphEditing editing, Paper paper, string id, GraphNodeRecord record, float? height, bool live)
    {
        string key = record.Id + (live ? "/card" : "/inspector");
        if (!_charts.TryGetValue(key, out BlendChart? chart))
        {
            chart = new BlendChart();
            _charts[key] = chart;
        }

        var samples = new List<BlendChart.Sample>();
        for (int pin = Node.VariadicStart; pin < record.Inputs.Count; pin++)
            samples.Add(new BlendChart.Sample(pin, record.Inputs[pin], editing.InputLabel(record, pin)));

        void Set(string description, Action change) => editing.Edit(description, change, false);

        if (!live)
        {
            chart.Draw(paper, id, samples, Set, TwoD);
            return;
        }

        // While the game runs, where the blend is being driven to right now.
        Float2? marker = null;
        float? x = DriverValue(editing, record, 0);
        if (x.HasValue) marker = new Float2(x.Value, TwoD ? DriverValue(editing, record, 1) ?? 0f : 0f);

        chart.Draw(paper, id, samples, Set, TwoD, height, margin: 0f, marker: marker);
    }

    private static float? DriverValue(AnimationGraphEditing editing, GraphNodeRecord record, int pin)
    {
        if (pin >= record.Inputs.Count || editing.RecordOf(record.Inputs[pin].Node) is not { } driver) return null;
        return editing.LiveNumber(driver);
    }

    /// <summary>
    /// The blend space of a 1D or 2D blend node as a small canvas: the wheel zooms, dragging empty space
    /// pans, dragging a sample moves it, and a double click frames them all.
    /// </summary>
    private sealed class BlendChart
    {
        private const float Inset = 10f;
        private const float DotRadius = 5f;
        private const float GrabRadius = 11f;
        private const float LabelSize = 9.5f;

        /// <summary>One draggable point: the input it belongs to and what it is called.</summary>
        internal readonly struct Sample
        {
            public Sample(int pin, GraphInputRecord input, string label)
            {
                Pin = pin; Input = input; Label = label;
            }

            public int Pin { get; }
            public GraphInputRecord Input { get; }
            public string Label { get; }
            public bool Wired => Input.Node.Length > 0;
        }

        private enum Drag { None, Sample, Pan }

        private Drag _drag;
        private int _dragging = -1;
        private int _hovered = -1;
        private int _framedCount = -1;
        private Float2 _min, _max;

        /// <summary>Where the running game is driving the blend right now, or null outside play.</summary>
        private Float2? _marker;

        /// <summary>A line of poses laid out by one number each, or a field of them laid out by two.</summary>
        public void Draw(Paper paper, string id, IReadOnlyList<Sample> samples, Action<string, Action> edit, bool twoD,
            float? height = null, float? margin = null, Float2? marker = null)
        {
            _marker = marker;
            // A sample coming or going is the one change worth reframing for, so a new one is never off view.
            if (samples.Count != _framedCount) Frame(samples, twoD);

            float side = margin ?? Origami.Current.Metrics.PaddingLarge;
            var box = paper.Box(id).Width(UnitValue.Stretch()).Height(height ?? (twoD ? 210f : 64f))
                .Margin(side, side, margin.HasValue ? 0 : 4, margin.HasValue ? 0 : 6)
                .Rounded(Origami.Current.Metrics.SmallRounding)
                .BackgroundColor(Tinted(EditorTheme.Neutral300, EditorTheme.Green400, 0.16f))
                .Clip();

            Hook(box, samples, edit, twoD);

            using (box.Enter())
            {
                paper.Draw((canvas, r) => Paint(canvas, r, samples, twoD));
                DrawRecenter(paper, id, samples, twoD);
            }
        }

        /// <summary>A corner button that frames every sample again.</summary>
        private void DrawRecenter(Paper paper, string id, IReadOnlyList<Sample> samples, bool twoD)
        {
            const float size = 20f;
            var button = paper.Box(id + "_frame")
                .PositionType(PositionType.SelfDirected).AnchorRight(2).AnchorTop(2).Width(size).Height(size)
                .Cursor(PaperCursor.Pointer)
                .StopDragPropagation()
                .Tooltip("Frame all samples")
                .OnClick(0, (_, _) => Frame(samples, twoD));

            // With no background to light up, the icon itself brightens under the pointer.
            using (button.Enter())
            {
                var colour = paper.IsParentHovered ? EditorTheme.Ink700 : EditorTheme.Ink400;
                paper.Draw((canvas, rect) => EditorIcons.Crosshairs_I.Draw(canvas, rect, colour));
            }
        }

        private void Hook(ElementBuilder box, IReadOnlyList<Sample> samples, Action<string, Action> edit, bool twoD)
        {
            box.TabIndex(0).Cursor(PaperCursor.Grab).CursorDragging(PaperCursor.Grabbing);

            box.OnDragStart(e =>
            {
                _dragging = Nearest(e.ElementRect, samples, Point(e), twoD);
                _drag = _dragging >= 0 ? Drag.Sample : Drag.Pan;
            });

            box.OnDragging(e =>
            {
                if (_drag == Drag.Pan)
                {
                    Float2 perPixel = UnitsPerPixel(e.ElementRect, twoD);
                    var shift = new Float2(-e.Delta.X * perPixel.X, twoD ? e.Delta.Y * perPixel.Y : 0f);
                    _min += shift;
                    _max += shift;
                    return;
                }

                if (_drag != Drag.Sample || _dragging < 0 || _dragging >= samples.Count) return;

                GraphInputRecord input = samples[_dragging].Input;
                Float2 value = ToValue(e.ElementRect, Point(e), twoD);

                if (twoD) edit("Move Blend Sample", () => input.Position = value);
                else edit("Set Blend Threshold", () => input.Value = value.X);
            });

            box.OnDragEnd(_ => { _drag = Drag.None; _dragging = -1; });

            // The wheel zooms this chart and stops there, rather than zooming the whole graph as well.
            box.OnScroll(e =>
            {
                e.StopPropagation();
                Float2 about = ToValue(e.ElementRect, Point(e), twoD, round: false);
                float factor = MathF.Exp(-e.Delta * 0.14f);
                _min = about + (_min - about) * factor;
                _max = about + (_max - about) * factor;
                if (!twoD) { _min = new Float2(_min.X, -1f); _max = new Float2(_max.X, 1f); }
            });

            box.OnDoubleClick(_ => Frame(samples, twoD));
            box.OnHover(0, (_, e) => _hovered = Nearest(e.ElementRect, samples, Point(e), twoD));
            box.OnLeave(_ => _hovered = -1);
        }

        // In the chart's own layout space, which is what its rectangle is in even when a card zooms it.
        private static Float2 Point(ElementEvent e) => new((float)e.LocalPosition.X, (float)e.LocalPosition.Y);

        private int Nearest(Rect rect, IReadOnlyList<Sample> samples, Float2 point, bool twoD)
        {
            int best = -1;
            float bestDistance = GrabRadius * GrabRadius;

            for (int i = 0; i < samples.Count; i++)
            {
                Float2 at = ToScreen(rect, At(samples[i], twoD), twoD);
                float dx = at.X - point.X, dy = at.Y - point.Y;
                float distance = dx * dx + dy * dy;
                if (distance > bestDistance) continue;

                bestDistance = distance;
                best = i;
            }
            return best;
        }

        /// <summary>Fits the view around the samples, with room for the outermost ones to be grabbed.</summary>
        private void Frame(IReadOnlyList<Sample> samples, bool twoD)
        {
            _framedCount = samples.Count;
            var min = new Float2(float.MaxValue, float.MaxValue);
            var max = new Float2(float.MinValue, float.MinValue);

            foreach (Sample sample in samples)
            {
                Float2 at = At(sample, twoD);
                min = new Float2(MathF.Min(min.X, at.X), MathF.Min(min.Y, at.Y));
                max = new Float2(MathF.Max(max.X, at.X), MathF.Max(max.Y, at.Y));
            }

            if (samples.Count == 0) { min = new Float2(0f, 0f); max = new Float2(1f, 1f); }

            _min = new Float2(Widen(min.X, max.X, true), twoD ? Widen(min.Y, max.Y, true) : -1f);
            _max = new Float2(Widen(min.X, max.X, false), twoD ? Widen(min.Y, max.Y, false) : 1f);
        }

        private static float Widen(float min, float max, bool wantMin)
        {
            float span = max - min;
            if (span < 1e-4f)
            {
                float centre = (min + max) * 0.5f;
                return wantMin ? centre - 1f : centre + 1f;
            }
            return wantMin ? min - span * 0.12f : max + span * 0.12f;
        }

        private static Float2 At(Sample sample, bool twoD) => twoD ? sample.Input.Position : new Float2(sample.Input.Value, 0f);

        private Float2 UnitsPerPixel(Rect rect, bool twoD) => new(
            (_max.X - _min.X) / MathF.Max(1f, InnerWidth(rect)),
            twoD ? (_max.Y - _min.Y) / MathF.Max(1f, InnerHeight(rect)) : 0f);

        private Float2 ToScreen(Rect rect, Float2 value, bool twoD)
        {
            float x = (float)rect.Min.X + Inset + Normalize(value.X, _min.X, _max.X) * InnerWidth(rect);
            if (!twoD) return new Float2(x, TrackY(rect));

            float y = (float)rect.Min.Y + Inset + (1f - Normalize(value.Y, _min.Y, _max.Y)) * InnerHeight(rect);
            return new Float2(x, y);
        }

        private Float2 ToValue(Rect rect, Float2 point, bool twoD, bool round = true)
        {
            float x = _min.X + (point.X - (float)rect.Min.X - Inset) / MathF.Max(1f, InnerWidth(rect)) * (_max.X - _min.X);
            float y = twoD
                ? _min.Y + (1f - (point.Y - (float)rect.Min.Y - Inset) / MathF.Max(1f, InnerHeight(rect))) * (_max.Y - _min.Y)
                : 0f;
            return round ? new Float2(Round(x), Round(y)) : new Float2(x, y);
        }

        private static float Round(float value) => MathF.Round(value, 3);

        private static float Normalize(float value, float min, float max) => (value - min) / MathF.Max(1e-5f, max - min);

        private static float InnerWidth(Rect rect) => (float)rect.Size.X - Inset * 2f;

        private static float InnerHeight(Rect rect) => (float)rect.Size.Y - Inset * 2f;

        // A 1D chart puts its line a little above the middle, leaving room for the grid labels under it.
        private static float TrackY(Rect rect) => (float)(rect.Min.Y + rect.Size.Y * 0.42f);

        private void Paint(Canvas canvas, Rect rect, IReadOnlyList<Sample> samples, bool twoD)
        {
            var font = EditorTheme.DefaultFont;
            if (font == null) return;

            PaintGrid(canvas, font, rect, twoD);
            PaintSamples(canvas, font, rect, samples, twoD);
            PaintMarker(canvas, rect, twoD);
        }

        /// <summary>Labelled grid lines at round steps, with the zero lines drawn stronger.</summary>
        private void PaintGrid(Canvas canvas, Scribe.FontFile font, Rect rect, bool twoD)
        {
            float left = (float)rect.Min.X, top = (float)rect.Min.Y;
            float right = (float)rect.Max.X, bottom = (float)rect.Max.Y;

            canvas.SaveState();
            canvas.SetStrokeWidth(1f);

            float stepX = NiceStep((_max.X - _min.X) / 4f);
            for (float v = MathF.Ceiling(_min.X / stepX) * stepX; v <= _max.X; v += stepX)
            {
                float x = ToScreen(rect, new Float2(v, 0f), twoD).X;
                bool zero = MathF.Abs(v) < stepX * 0.01f;
                canvas.SetStrokeColor(EditorTheme.ToColor32(EditorTheme.Ink300, zero ? 0.55f : 0.16f));
                Line(canvas, x, twoD ? top : TrackY(rect) - 6f, x, twoD ? bottom : TrackY(rect) + 6f);
                Label(canvas, font, Text(v), x + 3f, bottom - LabelSize - 3f, 0f, 0.8f);
            }

            if (twoD)
            {
                float stepY = NiceStep((_max.Y - _min.Y) / 4f);
                for (float v = MathF.Ceiling(_min.Y / stepY) * stepY; v <= _max.Y; v += stepY)
                {
                    float y = ToScreen(rect, new Float2(0f, v), true).Y;
                    bool zero = MathF.Abs(v) < stepY * 0.01f;
                    canvas.SetStrokeColor(EditorTheme.ToColor32(EditorTheme.Ink300, zero ? 0.55f : 0.16f));
                    Line(canvas, left, y, right, y);
                    Label(canvas, font, Text(v), left + 3f, y - LabelSize - 2f, 0f, 0.8f);
                }
            }
            else
            {
                canvas.SetStrokeColor(EditorTheme.ToColor32(EditorTheme.Ink300, 0.4f));
                Line(canvas, left + Inset, TrackY(rect), right - Inset, TrackY(rect));
            }

            canvas.RestoreState();
        }

        private static float NiceStep(float rough)
        {
            if (!(rough > 0f) || !float.IsFinite(rough)) return 1f;

            float power = MathF.Pow(10f, MathF.Floor(MathF.Log10(rough)));
            float scaled = rough / power;
            float nice = scaled < 1.5f ? 1f : scaled < 3.5f ? 2f : scaled < 7.5f ? 5f : 10f;
            return nice * power;
        }

        private void PaintSamples(Canvas canvas, Scribe.FontFile font, Rect rect, IReadOnlyList<Sample> samples, bool twoD)
        {
            for (int i = 0; i < samples.Count; i++)
            {
                Sample sample = samples[i];
                Float2 at = ToScreen(rect, At(sample, twoD), twoD);
                bool active = i == _dragging || i == _hovered;

                Color32 fill = !sample.Wired ? EditorTheme.ToColor32(EditorTheme.Ink300, 0.45f)
                    : active ? EditorTheme.ToColor32(EditorTheme.AccentBright, 1f)
                    : EditorTheme.ToColor32(EditorTheme.Amber400, 1f);

                canvas.CircleFilled(at.X, at.Y, active ? DotRadius + 1.5f : DotRadius, fill);

                // Every label at once piles up into noise, so a chart only names what is pointed at.
                if (active)
                    Label(canvas, font, sample.Label, at.X, at.Y - DotRadius - LabelSize - 3f, 0.5f, 1f);
            }
        }

        /// <summary>The live position, as a line across a 1D blend or a ringed dot in a 2D one.</summary>
        private void PaintMarker(Canvas canvas, Rect rect, bool twoD)
        {
            if (_marker is not { } value) return;

            Float2 at = ToScreen(rect, value, twoD);
            Color32 green = EditorTheme.ToColor32(EditorTheme.Green400, 1f);

            if (!twoD)
            {
                canvas.SaveState();
                canvas.SetStrokeWidth(2f);
                canvas.SetStrokeColor(green);
                Line(canvas, at.X, at.Y - 11f, at.X, at.Y + 11f);
                canvas.RestoreState();
                return;
            }

            canvas.CircleFilled(at.X, at.Y, DotRadius + 4f, EditorTheme.ToColor32(EditorTheme.Green400, 0.3f));
            canvas.CircleFilled(at.X, at.Y, DotRadius, green);
        }

        private static void Line(Canvas canvas, float x0, float y0, float x1, float y1)
        {
            canvas.BeginPath();
            canvas.MoveTo(x0, y0);
            canvas.LineTo(x1, y1);
            canvas.Stroke();
        }

        private static void Label(Canvas canvas, Scribe.FontFile font, string text, float x, float y, float align, float alpha)
        {
            if (text.Length == 0) return;

            float width = canvas.MeasureText(text, LabelSize, font).X;
            canvas.DrawText(text, x - width * align, y, EditorTheme.ToColor32(EditorTheme.Ink400, alpha), LabelSize, font);
        }

        private static string Text(float value) => MathF.Abs(value) < 1e-4f ? "0" : value.ToString("0.##");


        private static Color Tinted(Color from, Color toward, float amount) => Color.FromArgb(255,
            (int)(from.R + (toward.R - from.R) * amount),
            (int)(from.G + (toward.G - from.G) * amount),
            (int)(from.B + (toward.B - from.B) * amount));
    }
}
