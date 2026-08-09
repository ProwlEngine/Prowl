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
    private const float ToolbarHeight = 32f;
    private const float DividerHeight = 1f;

    [MenuItem("Window/Debug/Render Profiler", priority: 102)]
    static void Open() => EditorApplication.Instance?.OpenPanel(typeof(RenderProfilerPanel));

    public override string Title => "Render Profiler";
    public override string Icon => EditorIcons.ChartLine;

    private readonly EditorProfiler _profiler;
    private long? _selectedFrameIndex;

    public ProfiledFrame? SelectedFrame => _profiler.FrameAgo((int)(_selectedFrameIndex ?? 0));


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

    public void SelectFrame(long index)
    {
        _selectedFrameIndex = index;
        _profiler.Pause();
        ClearSubFrameSelection();
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

            DrawFramePicker(paper);

            paper.Box("rdp_picker_div")
                .Height((UnitValue)1)
                .IsNotInteractable()
                .BackgroundColor(EditorTheme.BorderStrong);

            DrawFlameGraph(paper);

            paper.Box("rdp_flame_div")
                .Height((UnitValue)1)
                .IsNotInteractable()
                .BackgroundColor(EditorTheme.BorderStrong);

            using (paper.Row("rdp_contents").Width(UnitValue.Stretch()).Height(UnitValue.Stretch()).Enter())
            {
                float contentsHeight = height - ToolbarHeight - FramePickerHeight - FlameGraphHeight - DividerHeight * 3f;
                DrawHierarchy(paper, HierarchyPanelWidth, contentsHeight);

                paper.Box("rdp_contents_vdiv")
                    .Width(1)
                    .IsNotInteractable()
                    .BackgroundColor(EditorTheme.BorderStrong);

                paper.Box("rdp_detail_panel")
                    .Width(UnitValue.Stretch())
                    .IsNotInteractable()
                    .BackgroundColor(Color.Red);
            }
        }
    }


    private void DrawToolbar(Paper paper)
    {
        using (paper.Row("rdp_toolbar")
            .Height(ToolbarHeight)
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
        {
            _profiler.Resume();
            _selectedFrameIndex = null;
        }
        else
        {
            _profiler.Pause();
            _selectedFrameIndex = 0;
        }

        ClearSubFrameSelection();
    }


    public void OpenSnapshot(Snapshot snapshot)
    {
        Debug.Log("Snapshot captured. Opening");
        SnapshotViewerPanel.Open(snapshot);
    }
}
