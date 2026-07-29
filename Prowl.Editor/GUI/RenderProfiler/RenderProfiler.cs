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

            paper.Box("rdp_contents")
                .IsNotInteractable()
                .BackgroundColor(Color.Red);
        }
    }


    private void DrawToolbar(Paper paper)
    {
        using (paper.Row("rdp_toolbar")
            .Height(32)
            .ColBetween(6)
            .Padding(6)
            .Enter())
        {
            Origami.IconButton(paper, "record", EditorIcons.CircleDot_I, TogglePaused)
                .Variant(_profiler.IsPaused ? OrigamiVariant.Default : OrigamiVariant.Danger)
                .Soft()
                .Rounding(90)
                .Width(20)
                .Height(20)
                .Show();

            Origami.Label(paper, "record_label", _profiler.IsPaused ? "Paused" : "Recording")
                .AlignCenter()
                .AlignLeft()
                .Height(20)
                .Show();

            paper.Box("rdp_toolbar_spacer");

            Origami.Button(paper, "snapshot", "Snapshot", _profiler.RequestCaptureNextFrame)
                .Disabled(_profiler.IsCaptureArmed)
                .Warning()
                .Soft()
                .Width(80)
                .Height(20)
                .Show();
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
