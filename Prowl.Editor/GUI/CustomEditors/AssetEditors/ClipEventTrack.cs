// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using Prowl.Echo;
using Prowl.Editor.GUI;
using Prowl.Editor.Importers;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Vector;

using Canvas = Prowl.Quill.Canvas;
using Color = System.Drawing.Color;
using Color32 = Prowl.Vector.Color32;

namespace Prowl.Editor.Inspector;

/// <summary>
/// The events of one clip on a strip the length of the clip: moments as markers, stretches as bars.
/// Events are stored in the model's import settings, timed in seconds of the take.
/// </summary>
internal sealed class ClipEventTrack
{
    private const float LaneHeight = 18f;
    private const float Pad = 4f;
    private const float EdgeGrab = 5f;
    private const float PointGrab = 6f;

    private enum Part { None, Body, End, Playhead }

    private readonly record struct Placed(int Index, int Lane, float X0, float X1, bool Stretch);

    private int _selected = -1;
    private Part _part;
    private int _dragging = -1;
    private float _dragTime;
    private float _dragLength;
    private float _width = 1f;

    /// <summary>Forgets the selection, for when another clip is picked.</summary>
    public void Reset()
    {
        _selected = -1;
        _dragging = -1;
        _part = Part.None;
    }

    /// <param name="start">Where the imported clip starts in the take, in seconds.</param>
    /// <param name="duration">How long the imported clip is, in seconds.</param>
    /// <param name="playhead">The preview's playhead, normalized over the clip.</param>
    /// <param name="scrub">Moves the playhead, normalized over the clip.</param>
    public void Draw(Paper paper, string id, Scribe.FontFile font, EchoObject settings, string clipName,
        float start, float duration, float playhead, Action<float> scrub)
    {
        var m = Origami.Current.Metrics;
        List<ClipEvent> events = ModelImportOverrides.ReadEvents(ModelImportOverrides.ReadClipBlock(settings, clipName));
        if (_selected >= events.Count) _selected = -1;

        duration = MathF.Max(duration, 1e-3f);
        List<Placed> placed = Layout(events, start, duration, _width);
        int lanes = 1;
        foreach (Placed p in placed) lanes = Math.Max(lanes, p.Lane + 1);
        float height = Pad * 2f + Math.Max(2, lanes) * LaneHeight;

        void Write() => ModelImportOverrides.WriteEvents(settings, clipName, events);
        float TimeAt(float x) => start + Math.Clamp(x / _width, 0f, 1f) * duration;

        var track = paper.Box($"{id}_track").Height(height)
            .Margin(m.PaddingLarge, m.PaddingLarge, m.Spacing, m.Spacing)
            .Rounded(m.SmallRounding).Clip()
            .BackgroundColor(EditorTheme.Neutral300)
            .BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
            .OnPostLayout((_, rect) => _width = MathF.Max(1f, (float)rect.Size.X))
            .OnPress(e =>
            {
                Float2 at = e.RelativePosition;
                (int index, Part part) = HitTest(placed, at);
                if (index < 0 || index >= events.Count)
                {
                    _part = Part.Playhead;
                    scrub((TimeAt(at.X) - start) / duration);
                    return;
                }

                _selected = index;
                _dragging = index;
                _part = part;
                _dragTime = events[index].Time;
                _dragLength = events[index].Length;
            })
            .OnDragging(e =>
            {
                if (_part == Part.Playhead)
                {
                    scrub((TimeAt(e.RelativePosition.X) - start) / duration);
                    return;
                }
                if (_dragging < 0 || _dragging >= events.Count) return;

                float moved = e.TotalDelta.X / _width * duration;
                ClipEvent dragged = events[_dragging];
                if (_part == Part.End) dragged.Length = MathF.Max(0f, _dragLength + moved);
                else dragged.Time = Math.Clamp(_dragTime + moved, start, start + duration);
                Write();
            })
            .OnDragEnd(_ =>
            {
                _dragging = -1;
                _part = Part.None;
            })
            .Tooltip("Click to move the playhead or pick an event. Drag an event to move it, or the end of a stretch to resize it.");

        // Painting happens after this frame's clicks have run, and a click can add or delete an event, so
        // the strip paints the events as they were laid out rather than the live list.
        ClipEvent[] shown = events.ConvertAll(e => e.Clone()).ToArray();
        using (track.Enter())
            paper.Draw((canvas, rect) => Paint(canvas, rect, font, shown, placed, playhead));

        DrawToolbar(paper, id, font, events, start, duration, playhead, Write);
        DrawList(paper, id, font, events, start, duration);
        DrawSelected(paper, id, events, Write);
    }

    // ---- layout -----------------------------------------------------------------------------

    /// <summary>Where each event sits on a strip this wide, stacked into lanes so none overlap.</summary>
    private static List<Placed> Layout(List<ClipEvent> events, float start, float duration, float width)
    {
        var order = new List<int>(events.Count);
        for (int i = 0; i < events.Count; i++) order.Add(i);
        order.Sort((a, b) => events[a].Time.CompareTo(events[b].Time));

        var laneEnds = new List<float>();
        var placed = new List<Placed>(events.Count);
        foreach (int i in order)
        {
            ClipEvent e = events[i];
            bool stretch = e.Length > 0f;
            float x0 = (e.Time - start) / duration * width;
            float x1 = stretch ? (e.Time + e.Length - start) / duration * width : x0 + PointGrab + LabelOf(e).Length * 6f;

            int lane = laneEnds.FindIndex(end => end + 3f < x0 - (stretch ? 0f : PointGrab));
            if (lane < 0) { lane = laneEnds.Count; laneEnds.Add(x1); }
            else laneEnds[lane] = x1;

            placed.Add(new Placed(i, lane, x0, x1, stretch));
        }
        return placed;
    }

    private static (int Index, Part Part) HitTest(List<Placed> placed, Float2 at)
    {
        for (int i = placed.Count - 1; i >= 0; i--)
        {
            Placed p = placed[i];
            float top = Pad + p.Lane * LaneHeight;
            if (at.Y < top || at.Y > top + LaneHeight) continue;

            if (p.Stretch)
            {
                if (MathF.Abs(at.X - p.X1) <= EdgeGrab) return (p.Index, Part.End);
                if (at.X >= p.X0 && at.X <= p.X1) return (p.Index, Part.Body);
            }
            else if (at.X >= p.X0 - PointGrab && at.X <= p.X1) return (p.Index, Part.Body);
        }
        return (-1, Part.None);
    }

    // ---- painting ---------------------------------------------------------------------------

    private void Paint(Canvas canvas, Rect rect, Scribe.FontFile font,
        ClipEvent[] events, List<Placed> placed, float playhead)
    {
        float left = (float)rect.Min.X, top = (float)rect.Min.Y;
        float width = (float)rect.Size.X, height = (float)rect.Size.Y;

        canvas.SaveState();
        foreach (Placed p in placed)
        {
            ClipEvent e = events[p.Index];
            Color colour = ColourOf(e.Kind);
            bool selected = p.Index == _selected;
            float y = top + Pad + p.Lane * LaneHeight;

            if (p.Stretch)
            {
                float x0 = left + p.X0, w = MathF.Max(3f, p.X1 - p.X0);
                canvas.RoundedRectFilled(x0, y + 2f, w, LaneHeight - 4f, 3f, 3f, 3f, 3f, EditorTheme.ToColor32(colour, selected ? 0.75f : 0.45f));
                if (selected) canvas.RectFilled(x0 + w - 2f, y + 2f, 2f, LaneHeight - 4f, EditorTheme.ToColor32(Color.White, 0.9f));
                canvas.DrawText(LabelOf(e), x0 + 4f, y + 3f, EditorTheme.ToColor32(EditorTheme.Ink700, 1f), 10f, font);
            }
            else
            {
                float x = left + p.X0;
                canvas.RectFilled(x - 1f, y + 1f, 2f, LaneHeight - 2f, EditorTheme.ToColor32(colour, 1f));
                float r = selected ? 5f : 4f;
                canvas.CircleFilled(x, y + LaneHeight * 0.5f, r, EditorTheme.ToColor32(colour, 1f));
                if (selected) canvas.CircleFilled(x, y + LaneHeight * 0.5f, 2f, EditorTheme.ToColor32(Color.White, 1f));
                canvas.DrawText(LabelOf(e), x + PointGrab, y + 3f, EditorTheme.ToColor32(EditorTheme.Ink600, 1f), 10f, font);
            }
        }

        float px = left + Math.Clamp(playhead, 0f, 1f) * width;
        canvas.RectFilled(px - 1f, top, 2f, height, EditorTheme.ToColor32(EditorTheme.Accent, 0.9f));
        canvas.RestoreState();
    }


    public static Color ColourOf(ClipEventKind kind) => kind switch
    {
        ClipEventKind.Named => EditorTheme.Blue400,
        ClipEventKind.Foot => EditorTheme.Green400,
        ClipEventKind.TransitionWindow => EditorTheme.Amber400,
        ClipEventKind.OrientationWarp => EditorTheme.Purple400,
        ClipEventKind.TargetWarp => EditorTheme.Red400,
        ClipEventKind.RootMotion => EditorTheme.Purple200,
        _ => EditorTheme.Ink400,
    };

    // ---- the rest of the section --------------------------------------------------------------

    private void DrawToolbar(Paper paper, string id, Scribe.FontFile font, List<ClipEvent> events,
        float start, float duration, float playhead, Action write)
    {
        var m = Origami.Current.Metrics;
        using (paper.Row($"{id}_evTools").Height(26).Margin(m.PaddingLarge, m.PaddingLarge, 0, m.Spacing)
            .Gap(m.SpacingMedium).AlignItems(LayoutAlignment.Center).Enter())
        {
            Origami.Button(paper, $"{id}_evAdd", "Add Event", () =>
                Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, b =>
                {
                    b.Header("At the playhead");
                    foreach (ClipEventKind kind in Enum.GetValues<ClipEventKind>())
                    {
                        ClipEventKind chosen = kind;
                        b.Item(Words(kind.ToString()), () =>
                        {
                            float at = start + Math.Clamp(playhead, 0f, 1f) * duration;
                            events.Add(new ClipEvent
                            {
                                Kind = chosen,
                                Time = at,
                                Length = ClipEvent.IsStretch(chosen) ? MathF.Min(0.2f, start + duration - at) : 0f,
                            });
                            _selected = events.Count - 1;
                            write();
                        });
                    }
                })).Subtle().Show();

            paper.Box($"{id}_evCount").Width(UnitValue.Stretch()).Height(26).IsNotInteractable()
                .Text(events.Count == 1 ? "1 event" : $"{events.Count} events", font)
                .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleRight);
        }
    }

    /// <summary>Every event as a row, including any the trim has put outside the clip, which the strip cannot show.</summary>
    private void DrawList(Paper paper, string id, Scribe.FontFile font, List<ClipEvent> events, float start, float duration)
    {
        var m = Origami.Current.Metrics;
        for (int i = 0; i < events.Count; i++)
        {
            ClipEvent e = events[i];
            int index = i;
            bool outside = e.Time + e.Length < start || e.Time > start + duration;
            string when = e.Length > 0f ? $"{e.Time:0.00} to {e.Time + e.Length:0.00}s" : $"{e.Time:0.00}s";

            using (paper.Row($"{id}_ev{i}").Height(22).Margin(m.PaddingLarge, m.PaddingLarge, 0, 0).Padding(6, 6, 0, 0)
                .Gap(m.SpacingMedium).AlignItems(LayoutAlignment.Center).Rounded(m.SmallRounding)
                .BackgroundColor(index == _selected ? EditorTheme.Selected : Color.Transparent)
                .Hovered.BackgroundColor(index == _selected ? EditorTheme.Selected : EditorTheme.Hover).End()
                .OnClick(_ => _selected = index).Enter())
            {
                paper.Box($"{id}_ev{i}_dot").Width(8).Height(8).Rounded(4).BackgroundColor(ColourOf(e.Kind)).IsNotInteractable();
                paper.Box($"{id}_ev{i}_label").Width(UnitValue.Stretch()).Height(22).IsNotInteractable()
                    .Text(LabelOf(e), font).TextColor(EditorTheme.Ink600).FontSize(EditorTheme.FontSizeSmall)
                    .Alignment(TextAlignment.MiddleLeft).TextTruncate();
                paper.Box($"{id}_ev{i}_when").Width(UnitValue.Auto).Height(22).IsNotInteractable()
                    .Text(outside ? when + " (trimmed away)" : when, font)
                    .TextColor(outside ? EditorTheme.Amber400 : EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall)
                    .Alignment(TextAlignment.MiddleRight);
            }
        }
    }

    private void DrawSelected(Paper paper, string id, List<ClipEvent> events, Action write)
    {
        if (_selected < 0 || _selected >= events.Count) return;
        ClipEvent e = events[_selected];
        string sid = $"{id}_evSel";

        EditorGUI.SectionHeader(paper, $"{sid}_h", "Selected Event");

        string[] kinds = Array.ConvertAll(Enum.GetNames<ClipEventKind>(), Words);
        EditorGUI.Row(paper, $"{sid}_kind", "Kind", () =>
            Origami.Dropdown(paper, $"{sid}_kind_v", (int)e.Kind, v =>
            {
                e.Kind = (ClipEventKind)v;
                e.Option = 0;
                if (ClipEvent.IsStretch(e.Kind) && e.Length <= 0f) e.Length = 0.2f;
                write();
            }, kinds).Show());

        EditorGUI.Row(paper, $"{sid}_time", "Time (s)", () =>
            Origami.NumericField<float>(paper, $"{sid}_time_v", e.Time, v => { e.Time = MathF.Max(0f, v); write(); }).Min(0f).Show());

        EditorGUI.Row(paper, $"{sid}_length", ClipEvent.IsStretch(e.Kind) ? "Length (s)" : "Length (s, 0 = moment)", () =>
            Origami.NumericField<float>(paper, $"{sid}_length_v", e.Length, v => { e.Length = MathF.Max(0f, v); write(); }).Min(0f).Show());

        if (e.Kind is ClipEventKind.Named or ClipEventKind.TransitionWindow)
            EditorGUI.Row(paper, $"{sid}_name", e.Kind == ClipEventKind.Named ? "Name" : "Filter Id", () =>
                Origami.TextField(paper, $"{sid}_name_v", e.Name, v => { e.Name = v; write(); }).Show());

        if (ClipEvent.OptionNames(e.Kind) is { } options)
        {
            string label = e.Kind switch
            {
                ClipEventKind.Foot => "Phase",
                ClipEventKind.TargetWarp => "Axes",
                ClipEventKind.SnapToFrame => "Mode",
                _ => "Rule",
            };
            EditorGUI.Row(paper, $"{sid}_option", label, () =>
                Origami.Dropdown(paper, $"{sid}_option_v", Math.Clamp(e.Option, 0, options.Length - 1),
                    v => { e.Option = v; write(); }, Array.ConvertAll(options, Words)).Show());
        }

        if (e.Kind == ClipEventKind.RootMotion)
            EditorGUI.Row(paper, $"{sid}_blend", "Blend (s)", () =>
                Origami.NumericField<float>(paper, $"{sid}_blend_v", e.BlendTime, v => { e.BlendTime = MathF.Max(0f, v); write(); }).Min(0f).Show());

        var m = Origami.Current.Metrics;
        using (paper.Row($"{sid}_actions").Height(26).Margin(m.PaddingLarge, m.PaddingLarge, m.Spacing, m.Spacing).Enter())
            Origami.Button(paper, $"{sid}_delete", "Delete Event", () =>
            {
                events.RemoveAt(_selected);
                _selected = -1;
                write();
            }).Danger().Subtle().Show();
    }

    private static string LabelOf(ClipEvent e)
    {
        string[]? options = ClipEvent.OptionNames(e.Kind);
        string option = options != null && e.Option >= 0 && e.Option < options.Length ? Words(options[e.Option]) : string.Empty;
        return e.Kind switch
        {
            ClipEventKind.Named => e.Name.Length > 0 ? e.Name : "Unnamed",
            ClipEventKind.Foot => option,
            ClipEventKind.TransitionWindow => e.Name.Length > 0 ? $"{option}: {e.Name}" : option,
            ClipEventKind.TargetWarp => $"Target Warp: {option}",
            ClipEventKind.SnapToFrame => $"Snap: {option}",
            _ => Words(e.Kind.ToString()),
        };
    }

    private static string Words(string name) => Regex.Replace(name, "(?<=[a-z])(?=[A-Z])", " ");
}
