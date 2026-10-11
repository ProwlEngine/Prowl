using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using Prowl.OrigamiUI;
using Prowl.OrigamiUI.Charts;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Scribe;

using Color = System.Drawing.Color;
using TextAlignment = Prowl.PaperUI.TextAlignment;

namespace Prowl.Graphite.Debugger.Viewer;

internal sealed partial class ProfilerView
{
    private static OrigamiMetrics Metrics => Style.Theme.Metrics;
    private static float Gap => Metrics.SpacingLarge;
    private static float HeaderHeight => Metrics.HeaderHeight;
    private static float PanelHeaderHeight => Metrics.HeaderHeight + Metrics.Padding;
    private const float FramesHeight = 200f;
    private const float HierarchyWidth = 330f;
    private const double BudgetMs = 16.7;
    private const double ScaleMs = 25;

    private readonly IRenderProfilerSource _source;
    private readonly bool _snapshotMode;
    private readonly string _id;
    private readonly Action _snapshotTaken;
    private DeepRecording? _shown;
    private ulong? _pinned;
    private Recording? _awaiting;
    private string _selId = "f";
    private string _message = "";
    private FrameModel? _liveModel;
    private FrameModel? _snapshotModel;
    private float _barsHoverX = -1f;
    private float _topHeight = 400f;

    private Paper _paper = null!;

    public ProfilerView(IRenderProfilerSource source, bool snapshotMode, string id, Action snapshotTaken)
    {
        _source = source;
        _snapshotMode = snapshotMode;
        _id = id;
        _snapshotTaken = snapshotTaken;
    }

    private DeepRecording? OpenSnapshot => _snapshotMode ? _source.Snapshot : null;

    private bool Heavy => OpenSnapshot != null;

    public void Draw(Paper paper, float width, float height)
    {
        _paper = paper;

        IReadOnlyList<Recording> frames = _source.Frames;
        if (frames.Count == 0)
            return;

        if (OpenSnapshot != _shown)
        {
            _shown = OpenSnapshot;
            ResetSelection();
        }

        if (_awaiting != null && !_snapshotMode)
        {
            int landed = frames.ToList().IndexOf(_awaiting);
            if (landed >= 0)
            {
                _pinned = _awaiting.ExecutionId;
                _awaiting = null;
            }
        }

        int liveIndex = _snapshotMode ? -1 : CurrentLive(frames);
        Recording? live = liveIndex >= 0 ? frames[liveIndex] : null;
        paper.PushID(_id);
        using (paper.Column("rp_root").Width(width).Height(height).Padding(Gap).Gap(Gap).Enter())
        {
            FrameModel? model = null;
            using (paper.Column("rp_top").Width(UnitValue.Stretch()).Height(UnitValue.Auto).Gap(Gap)
                .OnPostLayout((_, rect) => _topHeight = (float)rect.Size.Y).Enter())
            {
                if (_snapshotMode)
                    DrawSnapshotToolbar();
                else
                    DrawLiveToolbar(frames);

                if (_snapshotMode && OpenSnapshot == null)
                {
                    Dim("empty", "No snapshot open. Press Snapshot in the Render Profiler window, or load a saved one.");
                }
                else if (!_snapshotMode && live == null)
                {
                    DrawFramesRow(frames, null, null);
                    Divider("sep_top");
                    Dim("idle", "Pause Recording or Select a Frame");
                }
                else
                {
                    model = Heavy ? SnapshotModel(OpenSnapshot!) : LiveModel(frames, liveIndex);
                    if (!model.ById.ContainsKey(_selId))
                        _selId = "f";

                    if (!Heavy)
                        DrawFramesRow(frames, live, model);
                    Divider("sep_top");

                    DrawFlame(model, FlameHeight(model));
                    Divider("sep_flame");
                }
            }

            if (model != null)
            {
                float bottomWidth = width - Gap * 2;
                float bottomHeight = MathF.Max(200f, height - Gap * 3 - _topHeight);
                using (paper.Row("rp_bottom").Width(bottomWidth).Height(bottomHeight).Gap(Gap).Enter())
                {
                    DrawHierarchy(model, bottomHeight);
                    DrawInspector(model, frames, live!, bottomWidth - Gap - HierarchyWidth, bottomHeight);
                }
            }
        }
        paper.PopID();
    }

    private int CurrentLive(IReadOnlyList<Recording> frames)
    {
        if (_pinned is { } n && IndexOf(frames, n) is var pinned and >= 0)
            return pinned;
        _pinned = null;
        return -1;
    }

    private static int IndexOf(IReadOnlyList<Recording> frames, ulong executionId)
    {
        for (int i = 0; i < frames.Count; i++)
            if (frames[i].ExecutionId == executionId)
                return i;
        return -1;
    }

    private FrameModel LiveModel(IReadOnlyList<Recording> frames, int index)
    {
        if (_liveModel == null || _liveModel.Recording != frames[index])
            _liveModel = FrameModel.Build(frames[index], _source.Timing(index), null);
        return _liveModel;
    }

    private FrameModel SnapshotModel(DeepRecording snapshot)
    {
        if (_snapshotModel == null || _snapshotModel.Deep != snapshot)
            _snapshotModel = FrameModel.Build(snapshot.Recording, _source.SnapshotTiming!, snapshot);
        return _snapshotModel;
    }

    private void ResetSelection()
    {
        _selId = "f";
        _pixel = null;
        _graphSelection = null;
        _replays.Clear();
    }

    private void TakeSnapshot()
    {
        IReadOnlyList<Recording> frames = _source.Frames;
        int index = CurrentLive(frames);
        if (index < 0)
            return;
        _source.Capture(index, DeepMode.Full);
        _message = $"Snapshot / frame #{frames[index].ExecutionId}";
        _snapshotTaken();
    }

    private void SaveSnapshot()
    {
        Origami.OpenFileDialog(FileDialogMode.Save, path =>
        {
            if (string.IsNullOrEmpty(path))
                return;
            _source.SaveSnapshot(path);
            _message = $"Saved {System.IO.Path.GetFileName(path)}";
        });
    }

    internal void LoadSnapshot()
    {
        Origami.OpenFileDialog(FileDialogMode.Open, path =>
        {
            if (string.IsNullOrEmpty(path))
                return;
            _source.LoadSnapshot(path);
            _message = $"Loaded {System.IO.Path.GetFileName(path)}";
            _snapshotTaken();
        });
    }

    private ElementBuilder Card(string id)
        => _paper.Column(id)
            .BackgroundColor(Style.CardFill)
            .BorderColor(Style.CardBorder).BorderWidth(1)
            .Rounded(Metrics.ContainerRounding)
            .Padding(Metrics.Padding)
            .Gap(Metrics.Spacing);

    private IDisposable Heading(string id)
        => _paper.Row(id).Width(UnitValue.Stretch()).Height(PanelHeaderHeight).Gap(Metrics.SpacingLarge).AlignItems(LayoutAlignment.Center).Enter();

    private IDisposable CardHeading(string id)
        => _paper.Row(id).Width(UnitValue.Stretch()).Height(PanelHeaderHeight).Padding(Metrics.PaddingLarge, 0).Gap(Metrics.SpacingLarge).AlignItems(LayoutAlignment.Center).Enter();

    private void Title(string id, string text) => Origami.Label(_paper, id, text).Subheading().Show();

    private void Dim(string id, string text) => Origami.Label(_paper, id, text).XS().TextColor(Style.InkDim).Show();

    private ElementBuilder Mono(string id, string text, Color color)
        => _paper.Box(id)
            .Width(UnitValue.Auto).Height(UnitValue.Auto)
            .Text(text, Style.Mono).FontSize(Style.Small).TextColor(color)
            .Alignment(TextAlignment.MiddleLeft)
            .IsNotInteractable();

    private void Swatch(string id, Color color, int index = 0, float size = 10f, float rounding = 2f)
        => _paper.Box(id, index).Size(size).Rounded(rounding).BackgroundColor(color).IsNotInteractable();

    private void LegendItem(string id, Color color, string label, int index = 0)
    {
        using (_paper.Row(id, index).Width(UnitValue.Auto).Height(UnitValue.Auto).Gap(Metrics.Spacing).AlignItems(LayoutAlignment.Center).Enter())
        {
            Swatch("sw", color);
            Dim("tx", label);
        }
    }

    private void Spacer(string id) => _paper.Box(id).Width(UnitValue.Stretch()).Height(1);

    private static readonly IOrigamiIcon RecordDot = new IconAction((canvas, rect, color, _) =>
    {
        float cx = (float)(rect.Min.X + rect.Size.X / 2), cy = (float)(rect.Min.Y + rect.Size.Y / 2);
        canvas.BeginPath();
        canvas.Circle(cx, cy, (float)Math.Min(rect.Size.X, rect.Size.Y) * 0.32f);
        canvas.SetFillColor(color);
        canvas.Fill();
    });

    private void Divider(string id) => Origami.Separator(_paper, id).Height(1).Margin(0, 0).Show();

    private IDisposable Toolbar() => _paper.Row("rp_toolbar").Height(HeaderHeight).Gap(Metrics.SpacingLarge).AlignItems(LayoutAlignment.Center).Enter();

    private void DrawLiveToolbar(IReadOnlyList<Recording> frames)
    {
        using (Toolbar())
        {
            bool recording = _source.IsRecording;
            ButtonBuilder record = Origami.Button(_paper, "record", recording ? "Stop recording" : "Record", ToggleRecording)
                .Small().NotFocusable().Soft().LeadingIcon(RecordDot);
            (recording ? record.Danger() : record).Show();

            string status = $"{(recording ? "Recording" : "Paused")} / live / frames #{frames[0].ExecutionId}-#{frames[^1].ExecutionId}";
            Mono("status", status, Style.InkDim);
            Spacer("sp");

            if (_message.Length > 0)
                Mono("message", _message, Style.InkDim);
            Origami.Button(_paper, "snapshot", "Snapshot", TakeSnapshot).Small().NotFocusable().Soft().LeadingIcon(OrigamiGlyphs.Camera_I).Disabled(_pinned == null).Show();
        }
    }

    private void ToggleRecording()
    {
        if (_source.IsRecording)
        {
            _source.IsRecording = false;
            IReadOnlyList<Recording> frames = _source.Frames;
            if (frames.Count > 0)
                _pinned = frames[^1].ExecutionId;
            _awaiting = _source.Latest;
        }
        else
        {
            _pinned = null;
            _awaiting = null;
            _source.IsRecording = true;
        }
    }

    private void DrawSnapshotToolbar()
    {
        using (Toolbar())
        {
            DeepRecording? open = OpenSnapshot;
            if (open != null)
            {
                FrameModel model = SnapshotModel(open);
                HeaderStat("hs_frame", "Frame", $"#{open.ExecutionId}", Style.Ink);
                HeaderStat("hs_mode", "Mode", $"DeepMode.{open.Mode}", Style.Ink);
                HeaderStat("hs_views", "Views", model.Recording.Views.Length.ToString(), Style.Ink);
                HeaderStat("hs_cpu", "CPU", Style.Ms2(model.Timing.CpuMilliseconds), Style.Ink);
                HeaderStat("hs_gpu", "GPU", Style.Ms2(model.Timing.GpuMilliseconds), Style.Ink);
            }
            Spacer("sp");

            if (_message.Length > 0)
                Mono("message", _message, Style.InkDim);
            ButtonBuilder save = Origami.IconButton(_paper, "save", OrigamiGlyphs.Download_I, SaveSnapshot).Small().NotFocusable().Subtle().Tooltip("Save snapshot");
            save.Disabled(open == null).Show();
            Origami.IconButton(_paper, "load", OrigamiGlyphs.FolderOpen_I, LoadSnapshot).Small().NotFocusable().Subtle().Tooltip("Load snapshot").Show();
        }
    }

    private void HeaderStat(string id, string key, string value, Color color)
    {
        using (_paper.Row(id).Width(UnitValue.Auto).Height(UnitValue.Auto).Gap(Metrics.Spacing).AlignItems(LayoutAlignment.Center).Enter())
        {
            Dim("k", key);
            Mono("v", value, color);
        }
    }

    private void DrawFramesRow(IReadOnlyList<Recording> frames, Recording? live, FrameModel? model)
    {
        float cardHeight = FramesHeight - PanelHeaderHeight;
        using (_paper.Row("rp_frames_row").Height(FramesHeight).Gap(Gap).Enter())
        {
            using (_paper.Column("frames").Width(UnitValue.Stretch()).Height(FramesHeight).Enter())
            {
                using (Heading("hdr"))
                {
                    Title("t", "Frames");
                    Dim("s", $"Last {frames.Count} / click a bar to inspect it");
                    Spacer("sp");
                    LegendItem("lg_avg", Style.Ink, "Cumulative avg");
                    LegendItem("lg_over", Style.OverBudget, $"Over {BudgetMs} ms");
                    bool pinned = _pinned != null;
                    Mono("follow", pinned ? $"Selected #{live!.ExecutionId}" : "No frame selected", pinned ? Style.Selection : Style.InkDim);
                }

                using (Card("card").Height(cardHeight).Enter())
                    DrawFrameBars(frames, live);
            }

            if (live != null && model != null)
            using (_paper.Column("stats").Width(260).Height(FramesHeight).Enter())
            {
                using (Heading("hdr"))
                    Title("t", "Frame stats");

                using (Card("card").Height(cardHeight).Enter())
                {
                    double avgCpu = 0, avgGpu = 0;
                    for (int i = 0; i < frames.Count; i++)
                    {
                        FrameTiming t = _source.Timing(i);
                        avgCpu += t.CpuMilliseconds;
                        avgGpu += t.GpuMilliseconds;
                    }
                    avgCpu /= frames.Count;
                    avgGpu /= frames.Count;

                    FrameTiming timing = model.Timing;
                    StatRow("r0", "Frame", $"#{live.ExecutionId}", "");
                    StatRow("r1", "Views", live.Views.Length.ToString(), "");
                    StatRow("r2", "FPS", Style.F(1000.0 / timing.FrameMilliseconds, "0.0"), "");
                    StatRow("r3", "CPU record", Style.F(timing.CpuMilliseconds, "0.00"), $" / {Style.F(avgCpu, "0.00")} avg");
                    StatRow("r4", "GPU draw", Style.F(timing.GpuMilliseconds, "0.00"), $" / {Style.F(avgGpu, "0.00")} avg");
                    _paper.Box("sp").Height(UnitValue.Stretch());
                    Dim("foot", $"Times in ms, averaged over {frames.Count} frames");
                }
            }
        }
    }

    private void StatRow(string id, string key, string value, string suffix)
    {
        using (_paper.Row(id).Height(Metrics.CompactHeight).Gap(Metrics.Spacing).AlignItems(LayoutAlignment.Center).Enter())
        {
            Dim("k", key);
            Spacer("sp");
            Mono("v", value, Style.Ink);
            if (suffix.Length > 0)
                Mono("s", suffix, Style.InkDim);
        }
    }

    private void DrawFrameBars(IReadOnlyList<Recording> frames, Recording? live)
    {
        const float axis = 44f;
        double cumulative = _source.TotalFrames > 0 ? _source.TotalMilliseconds / _source.TotalFrames : 0;
        double[] ms = frames.Select((_, i) => _source.Timing(i).FrameMilliseconds).ToArray();

        int Hit(float x, float width)
        {
            float slot = (width - axis) / frames.Count;
            int i = (int)MathF.Floor((x - axis) / slot);
            return i >= 0 && i < frames.Count ? i : -1;
        }

        ElementBuilder box = _paper.Box("bars").Width(UnitValue.Stretch()).Height(UnitValue.Stretch()).Cursor(PaperCursor.Pointer);
        box.OnHover(e =>
        {
            _barsHoverX = (float)e.RelativePosition.X;
            int i = Hit(_barsHoverX, (float)e.ElementRect.Size.X);
            if (i >= 0)
                Origami.ShowTooltip(e.Source.Data.ID, $"Frame #{frames[i].ExecutionId} / {Style.Ms2(ms[i])}");
        });
        box.OnLeave(_ => _barsHoverX = -1f);
        box.OnClick(e =>
        {
            int i = Hit((float)e.RelativePosition.X, (float)e.ElementRect.Size.X);
            if (i < 0)
                return;
            ulong n = frames[i].ExecutionId;
            _awaiting = null;
            _pinned = _pinned == n ? null : n;
            if (_pinned != null)
                _source.IsRecording = false;
        });

        using (box.Enter())
        {
            int liveIndex = -1;
            for (int i = 0; i < frames.Count; i++)
                if (frames[i] == live)
                    liveIndex = i;

            _paper.Draw((canvas, rect) =>
            {
                float x0 = (float)rect.Min.X, y0 = (float)rect.Min.Y, w = (float)rect.Size.X, h = (float)rect.Size.Y;
                float px = x0 + axis, pw = w - axis;
                float Y(double v) => y0 + h - (float)Math.Min(1.0, v / ScaleMs) * h;

                canvas.RectFilled(px, Y(BudgetMs), pw, 1, Style.Border);
                canvas.RectFilled(px, Y(BudgetMs / 2), pw, 1, Style.Alpha(Style.Border, 120));
                canvas.RectFilled(px, y0 + h - 1, pw, 1, Style.InkFaint);
                canvas.DrawText("25ms", x0, y0, Style.InkDim, Style.Tiny, Style.Mono);
                canvas.DrawText("16.7ms", x0, Y(BudgetMs), Style.InkDim, Style.Tiny, Style.Mono, 0f, new Prowl.Vector.Float2(0, 0.5f));
                canvas.DrawText("8.3ms", x0, Y(BudgetMs / 2), Style.InkDim, Style.Tiny, Style.Mono, 0f, new Prowl.Vector.Float2(0, 0.5f));

                float slot = pw / frames.Count;
                float barW = MathF.Max(1f, slot - 1f);
                int hover = _barsHoverX >= 0 ? Hit(_barsHoverX, w) : -1;
                for (int i = 0; i < frames.Count; i++)
                {
                    Color c = i == liveIndex ? Style.Selection : ms[i] > BudgetMs ? Style.OverBudget : Style.FrameBar;
                    if (i == hover)
                        c = Color.FromArgb(255, Math.Min(255, c.R + 50), Math.Min(255, c.G + 50), Math.Min(255, c.B + 50));
                    float top = Y(ms[i]);
                    canvas.RectFilled(px + i * slot, top, barW, y0 + h - 1 - top, c);
                }

                float avgY = Y(cumulative);
                for (float x = px; x < px + pw; x += 7f)
                    canvas.RectFilled(x, avgY - 1, MathF.Min(4f, px + pw - x), 2, Style.Ink);

                string label = $"avg {Style.Ms2(cumulative)} / {_source.TotalFrames} frames";
                Prowl.Vector.Float2 size = canvas.MeasureText(label, Style.Tiny, Style.Mono);
                float lx = px + pw - (float)size.X - 16, ly = avgY - (float)size.Y - 8;
                canvas.RoundedRectFilled(lx - 6, ly - 2, (float)size.X + 12, (float)size.Y + 4, 3, 3, 3, 3, Style.Theme.Popover);
                canvas.DrawText(label, lx, ly, Style.Ink, Style.Tiny, Style.Mono);
            });
        }
    }

    private const float FlameRowHeight = 22f;

    private static int FlameDepth(ProfileNode node, int depth)
    {
        if (node.Kind is NodeKind.Pass or NodeKind.CommandBuffer)
            return depth;
        int max = depth;
        foreach (ProfileNode child in node.Children)
            max = Math.Max(max, FlameDepth(child, depth + 1));
        return max;
    }

    private static float FlameHeight(FrameModel model) => (FlameDepth(model.Root, 0) + 1) * FlameRowHeight + Metrics.SpacingSmall * 2 + 2;

    private static IEnumerable<ProfileNode> FlameChildren(ProfileNode node)
        => node.Kind is NodeKind.Pass or NodeKind.CommandBuffer ? Array.Empty<ProfileNode>() : node.Children;

    private void DrawFlame(FrameModel model, float height)
    {
        using (Heading("rp_flame_hdr"))
        {
            string title = Heavy ? $"Snapshot / frame #{model.Number}" : $"Frame #{model.Number}";
            Title("t", title);
            string levels = Heavy ? "Frame > View > Render Pass > Program > Renderer (marker) > Draw" : "Frame > View > Render Pass";
            Dim("p", levels + " / timing ends at pass");
            Spacer("sp");
            LegendItem("l0", Style.KindFill(NodeKind.View), "View");
            LegendItem("l1", Style.KindFill(NodeKind.Pass), "Render Pass");
            LegendItem("l2", Style.KindFill(NodeKind.CommandBuffer), "Command Buffer (outside passes)");
        }

        model.ById.TryGetValue(_selId, out ProfileNode? selected);
        Chart.FlameGraph(_paper, "rp_flame", new[] { model.Root })
            .Size(UnitValue.Stretch(), height)
            .Legend(false)
            .Name(n => n.Name)
            .Value(n => n.Milliseconds)
            .Children(FlameChildren)
            .ColorFunction((n, _) => Style.KindFill(n.Kind))
            .RowHeight(FlameRowHeight)
            .Padding(Metrics.SpacingSmall)
            .ValueFormatter(Style.Ms)
            .Zoomable()
            .Pannable()
            .Selected(selected)
            .OnNodeClick(n => _selId = n.Id)
            .Show();
    }

    private static bool DefaultOpen(NodeKind kind) => kind is NodeKind.Frame or NodeKind.View or NodeKind.Program;

    private void DrawHierarchy(FrameModel model, float height)
    {
        using (Card("rp_hier").Width(HierarchyWidth).Height(height).Padding(0).Gap(0).Enter())
        {
            using (CardHeading("hdr"))
            {
                Title("t", "Hierarchy");
                Dim("n", $"{model.ById.Count} nodes");
            }

            List<TreeNode> nodes = new();
            Flatten(model.Root, 0, nodes);

            Origami.Tree(_paper, "rp_tree", HierarchyWidth - 2, height - PanelHeaderHeight - 2)
                    .Nodes(nodes)
                    .IsSelected(n => n.Id == _selId)
                    .OnSelect(e => _selId = e.Node.Id)
                    .OnHover((n, _) => Origami.ShowTooltip(_paper.HoveredElementId, $"{n.Label}  /  {Style.KindLabel(((ProfileNode)n.UserData!).Kind)}  /  {n.Badge}"))
                    .Show();
        }
    }

    private void Flatten(ProfileNode node, int depth, List<TreeNode> into)
    {
        bool kids = node.Children.Count > 0;
        into.Add(new TreeNode
        {
            Id = node.Id,
            Label = node.Name,
            Depth = depth,
            HasChildren = kids,
            IsLeaf = !kids,
            DefaultExpanded = DefaultOpen(node.Kind),
            IconDraw = KindSquare,
            IconColor = Style.KindDot(node.Kind),
            Badge = node.IsTimed ? Style.F(node.Milliseconds, "0.000") : NodeStat(node),
            UserData = node,
        });
        foreach (ProfileNode child in node.Children)
            Flatten(child, depth + 1, into);
    }

    private static readonly IOrigamiIcon KindSquare = new IconAction((canvas, rect, color, _) =>
    {
        float cx = (float)(rect.Min.X + rect.Size.X / 2), cy = (float)(rect.Min.Y + rect.Size.Y / 2);
        canvas.RoundedRectFilled(cx - 4f, cy - 4f, 8f, 8f, 2f, 2f, 2f, 2f, color);
    });

    private static string NodeStat(ProfileNode node)
        => node.Kind == NodeKind.Draw ? $"EID {node.CommandIndex}" : $"{node.CountDraws()} draws";
}
