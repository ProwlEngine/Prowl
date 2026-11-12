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
    private int _peakFrames;
    private const double BudgetMs = 16.7;
    private const double ScaleMs = 25;
    private static readonly TimeSpan FollowInterval = TimeSpan.FromSeconds(0.5);

    private readonly IRenderProfilerSource _source;
    private readonly bool _snapshotMode;
    private readonly string _id;
    private readonly Action _snapshotTaken;
    private DeepRecording? _shown;
    private ulong? _pinned;
    private bool _follow;
    private ulong? _followed;
    private long _followedAt;
    private LiveDashboard? _liveStats;
    private long _liveStatsAt;
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

        int liveIndex = _snapshotMode ? -1 : ShownIndex(frames);
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
                Divider("rp_toolbar_div");
                    Dim("empty", "No snapshot open. Press Snapshot in the Render Profiler window, or load a saved one.");
                }
                else if (!_snapshotMode && live == null)
                {
                    DrawFramesRow(frames, null, null);
                    Divider("sep_top");
                    DrawLiveDashboard(frames);
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
            else if (!_snapshotMode)
            {
                // Nothing is open: point at the ways to open a frame, in the middle of the space its details would take
                float idleHeight = MathF.Max(40f, height - Gap * 3 - _topHeight);
                paper.Box("rp_idle").Width(UnitValue.Stretch()).Height(idleHeight).IsNotInteractable()
                    .Text("Select a frame, stop recording or turn on Follow latest to view more details", Style.Theme.Font ?? Style.Mono)
                    .FontSize(Style.Body).TextColor(Style.InkDim).Alignment(TextAlignment.MiddleCenter);
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

    // The pinned frame, or while nothing is pinned and Follow latest is on, a recent frame that is swapped for the
    // newest once per FollowInterval so the numbers stay readable. -1 when there is nothing to show
    private int ShownIndex(IReadOnlyList<Recording> frames)
    {
        int pinned = CurrentLive(frames);
        if (pinned >= 0 || !_follow)
            return pinned;

        if (_followed is { } id && Stopwatch.GetElapsedTime(_followedAt) < FollowInterval && IndexOf(frames, id) is var held and >= 0)
            return held;

        _followed = frames[^1].ExecutionId;
        _followedAt = Stopwatch.GetTimestamp();
        return frames.Count - 1;
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
        _frameGraphSelection = null;
        ClearReplays();
    }

    private void TakeSnapshot()
    {
        IReadOnlyList<Recording> frames = _source.Frames;
        int index = ShownIndex(frames);
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
            if (!path.EndsWith(SnapshotExtension, StringComparison.OrdinalIgnoreCase))
                path += SnapshotExtension;
            _source.SaveSnapshot(path);
            _message = $"Saved {System.IO.Path.GetFileName(path)}";
        }, filters: SnapshotFilters);
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
        }, filters: SnapshotFilters);
    }

    private const string SnapshotExtension = ".gsn";
    private static readonly string[] SnapshotFilters = [SnapshotExtension];

    private ElementBuilder Card(string id, int index = 0)
        => _paper.Column(id, index)
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

    private void Divider(string id) => _paper.Box(id).Height(1).BackgroundColor(Style.CardBorder).IsNotInteractable();

    private const float ToolbarHeight = 34f;

    private IDisposable Toolbar(float left = 10)
        => _paper.Row("rp_toolbar").Height(ToolbarHeight - 1).Padding(left, 8, 0, 0).Gap(6).AlignItems(LayoutAlignment.Center).Enter();

    private void DrawLiveToolbar(IReadOnlyList<Recording> frames)
    {
        using (Toolbar((ToolbarHeight - 1 - 26f) / 2f))
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
            Origami.Switch(_paper, "follow", _follow, v => _follow = v).Small().LabelRight("Follow latest").Show();
            Origami.Button(_paper, "snapshot", "Snapshot", TakeSnapshot).Small().NotFocusable().Soft().LeadingIcon(OrigamiGlyphs.Camera_I).Disabled(_pinned == null && !_follow).Show();
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
                HeaderStat("hs_instances", "Groups", model.Recording.Groups.Length.ToString(), Style.Ink);
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
        bool pinned = _pinned != null;
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
                    Mono("follow", pinned ? $"Selected #{live!.ExecutionId}" : _follow ? "Following latest" : "No frame selected", pinned ? Style.Selection : Style.InkDim);
                }

                using (_paper.Column("card").Height(cardHeight).Enter())
                    DrawFrameBars(frames, pinned ? live : null);
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
                    StatRow("r1", "Groups", live.Groups.Length.ToString(), "");
                    StatRow("r2", "FPS", Style.F(1000.0 / timing.FrameMilliseconds, "0.0"), "");
                    StatRow("r3", "CPU record", Style.F(timing.CpuMilliseconds, "0.00"), $" / {Style.F(avgCpu, "0.00")} avg");
                    StatRow("r4", "GPU draw", Style.F(timing.GpuMilliseconds, "0.00"), $" / {Style.F(avgGpu, "0.00")} avg");
                    _paper.Box("sp").Height(UnitValue.Stretch());
                    Dim("foot", $"Times in ms, averaged over {frames.Count} frames");
                }
            }
        }
    }

    private readonly record struct LiveStat(string Label, string Value);

    private sealed record LiveHero(string Label, string Value, Color Accent, Color ValueColor, float[] Spark, LiveStat[] Stats);

    private sealed record LiveDashboard(LiveHero[] Heroes, int StatRows);

    private const float HeroHeight = 84f;
    private const float StatRowHeight = 20f;
    private const int SparkPoints = 32;

    private static Color CpuColor => Style.Theme.Blue.C500;
    private static Color GpuColor => Style.Write;

    // The summary shown while no frame is open: numbers the source already measured, rebuilt once per FollowInterval.
    // It is one element painted with plain rects and text, so it adds no layout work and no path tessellation
    private void DrawLiveDashboard(IReadOnlyList<Recording> frames)
    {
        if (_liveStats == null || Stopwatch.GetElapsedTime(_liveStatsAt) >= FollowInterval)
        {
            _liveStats = BuildLiveDashboard(frames);
            _liveStatsAt = Stopwatch.GetTimestamp();
        }

        LiveDashboard live = _liveStats;
        float gap = Gap;
        using (_paper.Box("live").Width(UnitValue.Stretch()).Height(ColumnHeight(live)).IsNotInteractable().Enter())
            _paper.Draw((canvas, r) => PaintLiveDashboard(canvas, live, (float)r.Min.X, (float)r.Min.Y, (float)r.Size.X, gap));
    }

    private static float ColumnHeight(LiveDashboard live) => HeroHeight + live.StatRows * StatRowHeight + 10f;

    private static void PaintLiveDashboard(Prowl.Quill.Canvas canvas, LiveDashboard live, float x0, float y0, float w, float gap)
    {
        FontFile mono = Style.Mono, text = Style.Theme.Font ?? Style.Mono;
        Prowl.Vector.Float2 right = new(1, 0);

        // One column per main number, as wide as the panel allows, with the numbers that explain it listed underneath
        float columnHeight = ColumnHeight(live);
        float cw = (w - gap * (live.Heroes.Length - 1)) / live.Heroes.Length;
        for (int i = 0; i < live.Heroes.Length; i++)
        {
            LiveHero hero = live.Heroes[i];
            float x = x0 + i * (cw + gap), y = y0;
            canvas.RectFilled(x, y, cw, columnHeight, Style.CardFill);
            canvas.DrawText(hero.Label, x + 12, y + 12, hero.Accent, Style.Small, text);
            canvas.DrawText(hero.Value, x + 12, y + 34, hero.ValueColor, 26f, mono);

            float[] spark = hero.Spark;
            if (spark.Length > 0)
            {
                float sx = x + cw * 0.5f, sw = cw * 0.5f - 12f, bottom = y + HeroHeight - 12f, sh = HeroHeight - 46f;
                float slot = sw / spark.Length, bw = MathF.Max(1f, slot - 1f);
                Color bar = Style.Alpha(hero.Accent, 110);
                for (int b = 0; b < spark.Length; b++)
                {
                    float bh = MathF.Max(2f, sh * (0.15f + 0.85f * spark[b]));
                    canvas.RectFilled(sx + b * slot, bottom - bh, bw, bh, b == spark.Length - 1 ? hero.Accent : bar);
                }
            }

            for (int row = 0; row < hero.Stats.Length; row++)
            {
                LiveStat stat = hero.Stats[row];
                float sy = y + HeroHeight + row * StatRowHeight;
                canvas.DrawText(stat.Label, x + 12, sy + 3, Style.InkDim, Style.Small, text);
                canvas.DrawText(stat.Value, x + cw - 12, sy + 3, Style.Ink, Style.Small, mono, 0f, right);
            }
        }
    }


    private LiveDashboard BuildLiveDashboard(IReadOnlyList<Recording> frames)
    {
        int count = frames.Count;
        double[] frame = new double[count], fps = new double[count], cpu = new double[count], gpu = new double[count];
        double avgFrame = 0, avgCpu = 0, avgGpu = 0, min = double.MaxValue, max = 0;
        int over = 0;
        for (int i = 0; i < count; i++)
        {
            FrameTiming t = _source.Timing(i);
            frame[i] = t.FrameMilliseconds;
            fps[i] = t.FrameMilliseconds > 0 ? 1000.0 / t.FrameMilliseconds : 0;
            cpu[i] = t.CpuMilliseconds;
            gpu[i] = t.GpuMilliseconds;
            avgFrame += frame[i];
            avgCpu += cpu[i];
            avgGpu += gpu[i];
            min = Math.Min(min, frame[i]);
            max = Math.Max(max, frame[i]);
            if (frame[i] > BudgetMs)
                over++;
        }
        avgFrame /= count;
        avgCpu /= count;
        avgGpu /= count;

        Recording newest = frames[^1];
        FrameTiming timing = _source.Timing(count - 1);
        ulong draws = 0, dispatches = 0, binds = 0, shaders = 0;
        foreach (RecordedGroup group in newest.Groups)
            foreach (RecordedPass pass in group.Passes)
            {
                PassStats stats = pass.Stats;
                draws += stats.Draws + stats.IndirectDraws;
                dispatches += stats.Dispatches;
                binds += stats.PipelineBinds;
                shaders += stats.ShaderSwitches;
            }

        bool slow = timing.FrameMilliseconds > BudgetMs;
        double nowFps = fps[count - 1], avgFps = avgFrame > 0 ? 1000.0 / avgFrame : 0;
        LiveHero[] heroes =
        [
            Hero("FPS", Style.F(nowFps, "0.0"), Style.Good, slow, fps,
            [
                new("Average", Style.F(avgFps, "0.0")),
                new("Lowest", Style.F(max > 0 ? 1000.0 / max : 0, "0.0")),
                new("Over budget", $"{over} / {count}"),
            ]),
            Hero("Frame time / ms", Style.F(timing.FrameMilliseconds, "0.00"), Style.Primary, slow, frame,
            [
                new("Average", Style.F(avgFrame, "0.00")),
                new("Best", Style.F(min, "0.00")),
                new("Worst", Style.F(max, "0.00")),
            ]),
            Hero("CPU record / ms", Style.F(timing.CpuMilliseconds, "0.00"), CpuColor, false, cpu,
            [
                new("Average", Style.F(avgCpu, "0.00")),
                new("Draws", Style.Int(draws)),
                new("Dispatches", Style.Int(dispatches)),
            ]),
            Hero("GPU draw / ms", Style.F(timing.GpuMilliseconds, "0.00"), GpuColor, false, gpu,
            [
                new("Average", Style.F(avgGpu, "0.00")),
                new("Pipeline binds", Style.Int(binds)),
                new("Shader switches", Style.Int(shaders)),
            ]),
        ];

        return new LiveDashboard(heroes, heroes.Max(h => h.Stats.Length));
    }

    private static LiveHero Hero(string label, string value, Color accent, bool slow, double[] series, LiveStat[] stats)
    {
        return new LiveHero(label, value, accent, slow ? Style.OverBudget : Style.Ink, SparkPointsOf(series), stats);
    }

    // The series averaged into at most SparkPoints buckets and scaled into 0..1 across its own range
    private static float[] SparkPointsOf(double[] series)
    {
        int points = Math.Min(SparkPoints, series.Length);
        if (points < 2)
            return [];

        double[] buckets = new double[points];
        double lo = double.MaxValue, hi = double.MinValue;
        for (int b = 0; b < points; b++)
        {
            int from = b * series.Length / points, to = Math.Max(from + 1, (b + 1) * series.Length / points);
            double sum = 0;
            for (int i = from; i < to; i++)
                sum += series[i];
            buckets[b] = sum / (to - from);
            lo = Math.Min(lo, buckets[b]);
            hi = Math.Max(hi, buckets[b]);
        }

        float[] spark = new float[points];
        double range = hi - lo;
        for (int b = 0; b < points; b++)
            spark[b] = range > 1e-9 ? (float)((buckets[b] - lo) / range) : 0.5f;
        return spark;
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
        double cumulative = _source.TotalFrames > 0 ? _source.TotalMilliseconds / _source.TotalFrames : 0;
        double[] ms = frames.Select((_, i) => _source.Timing(i).FrameMilliseconds).ToArray();

        _peakFrames = Math.Max(_peakFrames, frames.Count);
        int capacity = Math.Max(1, _source.FrameCapacity > 0 ? _source.FrameCapacity : _peakFrames);

        int Hit(float x, float width)
        {
            float slot = width / capacity;
            int i = frames.Count - 1 - (int)((width - x) / slot);
            return x >= 0 && x < width && i >= 0 && i < frames.Count ? i : -1;
        }

        ElementBuilder box = _paper.Box("bars").Width(UnitValue.Stretch()).Height(UnitValue.Stretch()).Clip().Cursor(PaperCursor.Pointer);
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
                float Y(double v) => y0 + h - (float)Math.Min(1.0, v / ScaleMs) * h;
                canvas.RectFilled(x0, y0, w, h, Style.ChartFill);

                Color budget = Style.Alpha(Style.Good, 60);
                canvas.RectFilled(x0, Y(BudgetMs), w, 1, budget);
                canvas.RectFilled(x0, Y(BudgetMs / 2), w, 1, Style.Alpha(Style.Ink, 30));
                canvas.DrawText("16.7 ms", x0 + 6, Y(BudgetMs) - 12, budget, Style.Tiny, Style.Mono);
                canvas.DrawText("8.3 ms", x0 + 6, Y(BudgetMs / 2) - 12, Style.Alpha(Style.Ink, 30), Style.Tiny, Style.Mono);
                canvas.DrawText("25 ms", x0 + w - 6, y0 + 4, Style.InkDim, Style.Small, Style.Mono, 0f, new Prowl.Vector.Float2(1, 0));

                float slot = w / capacity;
                float barW = slot > 3f ? slot - 1f : MathF.Max(1f, slot);
                int hover = _barsHoverX >= 0 ? Hit(_barsHoverX, w) : -1;
                int visible = Math.Min(frames.Count, capacity);
                for (int ago = 0; ago < visible; ago++)
                {
                    int i = frames.Count - 1 - ago;
                    float bx = x0 + w - (ago + 1) * slot;
                    Color c = i == liveIndex ? Style.Selection : ms[i] > BudgetMs ? Style.OverBudget : Style.FrameBar;
                    float top = Y(ms[i]);
                    canvas.RectFilled(bx, top, barW, y0 + h - top, c);
                    if (i == liveIndex)
                        canvas.RectFilled(bx, y0, barW, h, Style.Alpha(Style.Primary, 70));
                    else if (i == hover)
                        canvas.RectFilled(bx, y0, barW, h, Color.FromArgb(28, 255, 255, 255));
                }

                float avgY = Y(cumulative);
                for (float x = x0; x < x0 + w; x += 7f)
                    canvas.RectFilled(x, avgY - 1, MathF.Min(4f, x0 + w - x), 2, Style.Ink);

                string label = $"avg {Style.Ms2(cumulative)} / {_source.TotalFrames} frames";
                Prowl.Vector.Float2 size = canvas.MeasureText(label, Style.Tiny, Style.Mono);
                float lx = x0 + w - (float)size.X - 16, ly = avgY - (float)size.Y - 8;
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
            string levels = Heavy ? "Frame > Group > Render Pass > Program > Renderer (marker) > Draw" : "Frame > Group > Render Pass";
            Dim("p", levels + " / timing ends at pass");
            Spacer("sp");
            LegendItem("l0", Style.KindFill(NodeKind.Group), "Group");
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

    private static bool DefaultOpen(NodeKind kind) => kind is NodeKind.Frame or NodeKind.Group or NodeKind.Program;

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
            IconColor = node.Idle ? Style.InkDim : Style.KindDot(node.Kind),
            Badge = node.Idle ? "idle" : node.IsTimed ? Style.F(node.Milliseconds, "0.000") : NodeStat(node),
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
