using System;

using Prowl.Editor.Core;
using Prowl.Editor.Profiling;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Vector;

namespace Prowl.Editor.GUI.RenderProfiler;

public partial class RenderProfilerPanel : DockPanel
{
    // Toolbar row (36) + its divider (1) below, subtracted from the OnGUI height before it
    // reaches DrawStatsViewer/DrawNativeStatsViewer, since those receive the panel's raw size.
    private const float ToolbarChromeHeight = 37f;

    [MenuItem("Window/Debug/Render Profiler", priority: 102)]
    static void Open() => EditorApplication.Instance?.OpenPanel(typeof(RenderProfilerPanel));

    public override string Title => "Render Profiler";
    public override string Icon => EditorIcons.ChartLine;

    private readonly EditorProfiler _profiler;
    private int _selectedTab;
    private int _selectedFrame;
    private int _selectedView;
    private ProfiledFrame? _selectedProfiledFrame => _profiler.FrameAgo(_selectedFrame);


    public RenderProfilerPanel()
    {
        _profiler = EditorProfiler.Instance ?? throw new InvalidOperationException("EditorProfiler.AttachShared must run before any RenderProfilerPanel is created.");
        _profiler.CaptureHandler = _profiler.SnapshotCapturer.HandleCapture;
        _profiler.CaptureFinalizeHandler = _profiler.SnapshotCapturer.Finalize;
        _profiler.SnapshotCaptured += OpenSnapshot;

        _profiler.Resume();
    }

    public override void OnClosed()
    {
        _profiler.SnapshotCaptured -= OpenSnapshot;
        _profiler.Pause();
    }

    public override void OnGUI(Paper paper, float width, float height)
    {
        using (paper.Column("rdp_root").Enter())
        {
            DrawToolbar(paper);

            paper.Box("rdp_toolbar_div")
                .Height((UnitValue)1)
                .IsNotInteractable()
                .BackgroundColor(EditorTheme.BorderStrong);

            using (paper.Box("rdp_body").ChildRight().Enter())
            {
                switch (_selectedTab)
                {
                    case 0:
                        // _profiler.DrawTiming(paper);
                        DrawTimingViewer(paper);
                        break;
                    case 1:
                        // _profiler.DrawMemory(paper);
                        DrawStatsViewer(paper, width, height);
                        break;
                    case 2:
                        // _profiler.DrawRenderables(paper);
                        DrawRenderablesViewer(paper);
                        break;
                    case 3:
                        // _profiler.DrawNativeStats(paper);
                        DrawNativeStatsViewer(paper, width, height);
                        break;
                    case 4:
                        // _profiler.DrawRenderGraph(paper);
                        DrawRenderGraphViewer(paper);
                        break;
                }
            }
        }
    }


    private void DrawToolbar(Paper paper)
    {
        using (paper.Row("rpe_toolbar")
            .Height(36)
            .ColBetween(6)
            .Enter())
        {
            Origami.Tabs(paper, "rpe_views", _selectedTab, i => _selectedTab = i)
                .Tab("Timing")
                .Tab("Stats")
                .Tab("Renderables")
                .Tab("Native Stats")
                .Tab("Render Graph")
                .Height(36)
                .Show();

            paper.Box("rpe_toolbar_div")
                .Width((UnitValue)1)
                .IsNotInteractable()
                .BackgroundColor(EditorTheme.BorderStrong);

            using (paper.Row("rpe_toolbar_spacer")
                .Padding(6)
                .RowBetween(9)
                .ChildLeft()
                .ChildRight()
                .Clip()
                .Enter())
            {
                if (_selectedProfiledFrame != null)
                {
                    Origami.Label(paper, "fps", $"FPS: {_selectedProfiledFrame.Fps:F2}")
                        .Width(80)
                        .Show();

                    Origami.Label(paper, "ms", $"ms: {_selectedProfiledFrame.FrameMilliseconds:F2} ms")
                        .Width(80)
                        .Show();

                    Origami.Label(paper, "frame_num", $"Frame: {_selectedProfiledFrame.FrameIndex}")
                        .Width(100)
                        .Show();

                    Origami.Label(paper, "rendered_views", $"Views: {_selectedProfiledFrame.Views.Count}")
                        .Width(100)
                        .Show();
                }
            }

            paper.Box("rpe_toolbar_div")
                .Width((UnitValue)1)
                .IsNotInteractable()
                .BackgroundColor(EditorTheme.BorderStrong);

            using (paper.Row("rpe_toolbar_right")
                .ChildTop()
                .ChildBottom()
                .ChildLeft()
                .Padding(6)
                .RowBetween(9)
                .Width(250)
                .Enter())
            {
                Origami.NumericField(paper, "frameselect", _selectedFrame, (x) => _selectedFrame = Maths.Clamp(x, 0, System.Math.Max(0, _profiler.History.Count - 1)))
                    .DraggableLabel("Frame", EditorTheme.Amber500)
                    .Width(100)
                    .Show();

                Origami.IconButton(paper, "record", EditorIcons.CircleDot_I, TogglePaused)
                    .Variant(_profiler.IsPaused ? OrigamiVariant.Default : OrigamiVariant.Danger)
                    .Soft()
                    .Rounding(90)
                    .Width(20)
                    .Height(20)
                    .Show();

                Origami.Button(paper, "snapshot", "Snapshot", _profiler.RequestCaptureNextFrame)
                    .Disabled(_profiler.IsCaptureArmed)
                    .Warning()
                    .Soft()
                    .Width(80)
                    .Height(20)
                    .Show();
            }

            // left-aligned button for creating a snapsho
        }
    }


    public void TogglePaused()
    {
        if (_profiler.IsPaused)
            _profiler.Resume();
        else
            _profiler.Pause();
    }


    public void OpenSnapshot(Snapshot snapshot)
    {
        Debug.Log("Snapshot captured. Opening");
        SnapshotViewerPanel.Open(snapshot);
    }
}
