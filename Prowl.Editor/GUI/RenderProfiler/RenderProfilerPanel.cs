using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Editor.Core;
using Prowl.Editor.GUI.Panels;
using Prowl.Editor.Projects;
using Prowl.Editor.Theming;
using Prowl.Graphite;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Scribe;

using Color = System.Drawing.Color;
using TextAlignment = Prowl.PaperUI.TextAlignment;
using RuntimeProfiler = Prowl.Runtime.Rendering.RenderProfiler;

namespace Prowl.Editor.GUI.RenderProfiler;

/// <summary>
/// Window 2: the live render profiler. A dockable, tabbed panel that targets one camera (default: the
/// focused scene-view camera) and maintains a ring buffer of that camera's
/// <see cref="Camera.LastRenderReport"/> pulled each editor frame. A Pause toggle freezes collection and
/// a scrub slider inspects any buffered frame. Four live tabs (Scene Objects, Flame Graph, Counters,
/// Render Graph) render the live or scrubbed frame. Everything guards a null/empty report safely.
/// </summary>
public sealed class RenderProfilerPanel : DockPanel
{
    [MenuItem("Window/Debug/Render Profiler", priority: 101)]
    static void Open() => EditorApplication.Instance?.OpenPanel(typeof(RenderProfilerPanel));

    public override string Title => "Render Profiler";
    public override string Icon => EditorIcons.GaugeHigh;

    private enum ProfilerTab { SceneObjects, FlameGraph, Counters, Graphite, RenderGraph }
    private enum ProfilerMode { Live, Snapshot }

    private ProfilerTab _tab = ProfilerTab.Counters;
    private ProfilerMode _mode = ProfilerMode.Live;
    private readonly FrameHistoryBuffer _history = new(240);
    private readonly List<RenderFrameReport> _historyList = new();
    private readonly GraphiteSnapshotHistory _graphiteHistory = new(240);
    private readonly List<ProfileSnapshot> _graphiteHistoryList = new();
    private readonly RenderGraphView _graphView = new();
    private readonly SnapshotViewer _snapshotViewer = new();

    private RenderSnapshot? _snapshot;
    private volatile bool _pendingSwapToSnapshot;

    private bool _paused;
    private int _scrub;
    private int _selectedCameraId; // 0 = scene view camera

    private const float ModeBarHeight = 34f;
    private const float ToolbarHeight = 36f;
    private const float TabHeight = 32f;

    private readonly List<(string label, Camera? camera, int id)> _cameraOptions = new();
    private readonly List<string> _cameraLabels = new();

    public RenderProfilerPanel()
    {
        _snapshot = RuntimeProfiler.LastSnapshot;
        RuntimeProfiler.SnapshotCaptured += OnSnapshotCaptured;
    }

    public override void OnClosed()
    {
        RuntimeProfiler.SnapshotCaptured -= OnSnapshotCaptured;
    }

    private void OnSnapshotCaptured(RenderSnapshot snapshot)
    {
        _snapshot = snapshot;
        _pendingSwapToSnapshot = true;
    }

    public override void OnGUI(Paper paper, float width, float height)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        if (_pendingSwapToSnapshot)
        {
            _pendingSwapToSnapshot = false;
            _mode = ProfilerMode.Snapshot;
        }

        BuildCameraOptions();
        var camera = ResolveSelectedCamera();

        if (_mode == ProfilerMode.Live && !_paused)
        {
            RuntimeProfiler.RequestReport();

            GraphicsDevice? device = Graphics.Device;
            if (device != null)
                _graphiteHistory.Push(device.GetProfile());
        }

        if (!_paused && camera?.LastRenderReport != null)
            _history.Push(camera.LastRenderReport);

        int count = _history.Count;
        if (count > 0 && _scrub > count - 1) _scrub = count - 1;
        if (_scrub < 0) _scrub = 0;

        RenderFrameReport? shown = _paused ? _history.At(_scrub) : _history.Latest;
        _history.CopyTo(_historyList);
        _graphiteHistory.CopyTo(_graphiteHistoryList);

        using (paper.Column("rp_root").Size(width, height).Enter())
        {
            DrawModeBar(paper, font, width);

            if (_mode == ProfilerMode.Snapshot)
            {
                float snapH = height - ModeBarHeight;
                using (paper.Box("rp_snap_host").Width(width).Height(snapH).Clip().Enter())
                    _snapshotViewer.Draw(paper, _snapshot, font, width, snapH);
                return;
            }

            DrawToolbar(paper, font, width, shown);
            DrawTabs(paper, width);

            float contentH = height - ModeBarHeight - ToolbarHeight - TabHeight;
            using (paper.Box("rp_content").Width(width).Height(contentH).Clip().Enter())
            {
                switch (_tab)
                {
                    case ProfilerTab.SceneObjects:
                        SceneObjectsView.Draw(paper, shown, font, width, contentH);
                        break;
                    case ProfilerTab.FlameGraph:
                        FlameGraphView.Draw(paper, shown, font, width, contentH);
                        break;
                    case ProfilerTab.Counters:
                        CountersView.Draw(paper, _historyList, font, width, contentH);
                        break;
                    case ProfilerTab.Graphite:
                        GraphiteCountersView.Draw(paper, _graphiteHistoryList, font, width, contentH);
                        break;
                    case ProfilerTab.RenderGraph:
                        _graphView.Draw(paper, shown, font, width, contentH);
                        break;
                }
            }
        }
    }

    private void DrawModeBar(Paper paper, FontFile font, float width)
    {
        using (paper.Row("rp_modebar").Width(width).Height(ModeBarHeight).Padding(8, 8, 5, 5).RowBetween(6).Enter())
        {
            using (paper.Box("rp_mode_wrap").Width(180).Height(24).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).Enter())
                Origami.ButtonGroup(paper, "rp_mode", (int)_mode, i => _mode = (ProfilerMode)i)
                    .Segmented().Height(24)
                    .Item("Live")
                    .Item("Snapshot")
                    .Show();

            paper.Box("rp_mode_spacer");

            using (paper.Box("rp_cap_wrap").Width(88).Height(24).Margin(0, 4, UnitValue.StretchOne, UnitValue.StretchOne).Enter())
                Origami.Button(paper, "rp_cap", RuntimeProfiler.IsCaptureArmed ? "Arming..." : "Capture", RequestCapture)
                    .Primary().Width(88).Show();

            using (paper.Box("rp_save_wrap").Width(64).Height(24).Margin(0, 4, UnitValue.StretchOne, UnitValue.StretchOne).Enter())
                Origami.Button(paper, "rp_save", "Save", SaveSnapshot).Disabled(_snapshot == null).Width(64).Show();

            using (paper.Box("rp_load_wrap").Width(64).Height(24).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).Enter())
                Origami.Button(paper, "rp_load", "Load", LoadSnapshot).Width(64).Show();
        }
    }

    private static void RequestCapture() => RuntimeProfiler.RequestCapture();

    private void SaveSnapshot()
    {
        var snapshot = _snapshot;
        if (snapshot == null) return;

        string? start = Project.Current?.AssetsPath;
        EditorApplication.OpenFileDialog(FileDialogMode.Save, path =>
        {
            if (string.IsNullOrEmpty(path) || snapshot == null) return;
            if (!path.EndsWith(".rendersnapshot")) path += ".rendersnapshot";
            try { RuntimeProfiler.Save(snapshot, path); }
            catch (Exception e) { Prowl.Runtime.Debug.LogError($"Failed to save snapshot: {e.Message}"); }
        }, start, new[] { "*.rendersnapshot" }, new[] { "Render Snapshot" });
    }

    private void LoadSnapshot()
    {
        string? start = Project.Current?.AssetsPath;
        EditorApplication.OpenFileDialog(FileDialogMode.Open, path =>
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                _snapshot = RuntimeProfiler.Load(path);
                _mode = ProfilerMode.Snapshot;
            }
            catch (Exception e) { Prowl.Runtime.Debug.LogError($"Failed to load snapshot: {e.Message}"); }
        }, start, new[] { "*.rendersnapshot" }, new[] { "Render Snapshot" });
    }

    private void DrawToolbar(Paper paper, FontFile font, float width, RenderFrameReport? shown)
    {
        using (paper.Row("rp_tb").Width(width).Height(ToolbarHeight).Padding(8, 8, 6, 6).RowBetween(6).Enter())
        {
            int selIndex = 0;
            for (int i = 0; i < _cameraOptions.Count; i++)
                if (_cameraOptions[i].id == _selectedCameraId) { selIndex = i; break; }

            using (paper.Box("rp_tb_cam_wrap").Width(180).Height(24).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).Enter())
                Origami.Dropdown(paper, "rp_tb_cam", selIndex, i =>
                {
                    if (i >= 0 && i < _cameraOptions.Count)
                    {
                        _selectedCameraId = _cameraOptions[i].id;
                        _history.Clear();
                        _graphiteHistory.Clear();
                        _scrub = 0;
                    }
                }, _cameraLabels).Width(UnitValue.Pixels(180)).Height(24).Show();

            paper.Box("rp_tb_spacer");

            string frameLabel = shown != null
                ? $"Frame {shown.FrameIndex}  {shown.CpuFrameMs:0.00} ms"
                : "No frame";
            paper.Box("rp_tb_frame").Width(UnitValue.Auto).Height(24).Margin(0, 6, UnitValue.StretchOne, UnitValue.StretchOne)
                .Text(frameLabel, font).FontSize(EditorTheme.FontSizeSmall).TextColor(EditorTheme.InkDim)
                .Alignment(TextAlignment.MiddleRight).IsNotInteractable();

            if (_paused && _history.Count > 1)
                using (paper.Box("rp_tb_scrub_wrap").Width(160).Height(24).Margin(0, 6, UnitValue.StretchOne, UnitValue.StretchOne).Enter())
                    Origami.IntSlider(paper, "rp_tb_scrub", _scrub, v => _scrub = v, 0, _history.Count - 1)
                        .Width(UnitValue.Pixels(160)).Height(24).Show();

            using (paper.Box("rp_tb_pause_wrap").Width(90).Height(24).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).Enter())
                Origami.Button(paper, "rp_tb_pause", _paused ? "Resume" : "Pause", () =>
                {
                    _paused = !_paused;
                    if (_paused) _scrub = _history.Count > 0 ? _history.Count - 1 : 0;
                }).Width(90).Show();
        }
    }

    private void DrawTabs(Paper paper, float width)
    {
        using (paper.Box("rp_tabs_wrap").Width(width).Height(TabHeight).Padding(8, 8, 0, 0).Enter())
            Origami.Tabs(paper, "rp_tabs", (int)_tab, i => _tab = (ProfilerTab)i)
                .Height(TabHeight)
                .Tab("Scene Objects")
                .Tab("Flame Graph")
                .Tab("Counters")
                .Tab("Graphite")
                .Tab("Render Graph")
                .Show();
    }

    private void BuildCameraOptions()
    {
        _cameraOptions.Clear();
        _cameraLabels.Clear();

        var sceneCam = SceneViewPanel.ActiveCamera?.Camera;
        _cameraOptions.Add(("Scene View", sceneCam, 0));
        _cameraLabels.Add("Scene View");

        var scene = Scene.Current;
        if (scene != null)
        {
            foreach (var go in scene.AllObjects)
            {
                var cam = go.GetComponent<Camera>();
                if (cam == null) continue;
                _cameraOptions.Add((go.Name, cam, cam.InstanceID));
                _cameraLabels.Add(go.Name);
            }
        }
    }

    private Camera? ResolveSelectedCamera()
    {
        foreach (var opt in _cameraOptions)
            if (opt.id == _selectedCameraId)
                return opt.camera;
        _selectedCameraId = 0;
        return SceneViewPanel.ActiveCamera?.Camera;
    }
}
