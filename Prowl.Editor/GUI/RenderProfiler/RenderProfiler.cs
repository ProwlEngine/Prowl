using System;

using Prowl.Editor.Core;
using Prowl.Editor.GUI.RenderProfiler.Data;
using Prowl.Editor.GUI.RenderProfiler.Inspectors;
using Prowl.Editor.GUI.RenderProfiler.Widgets;
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
    private const float FlameGraphHeight = 100f;
    private const float HierarchyPanelWidth = 340f;

    [MenuItem("Window/Debug/Render Profiler", priority: 102)]
    static void Open() => EditorApplication.Instance?.OpenPanel(typeof(RenderProfilerPanel));

    public override string Title => "Render Profiler";
    public override string Icon => EditorIcons.ChartLine;

    private readonly EditorProfiler _profiler;
    private long? _selectedFrameIndex;

    private readonly ProfilerHierarchyView _hierarchy = new("rdp_hier_tree");
    private readonly ProfilerFlameGraphView _flame = new("rdp_flame");
    private readonly ProfilerViewInspector _viewInspector = new();
    private readonly ProfilerPassInspector _passInspector = new();

    public ProfiledFrame? SelectedFrame => _profiler.FrameAgo((int)(_selectedFrameIndex ?? 0));

    private IProfilerHistory History => new LiveHistory(EditorProfiler.Instance!, SelectedFrame?.FrameIndex);


    public RenderProfilerPanel()
    {
        _profiler = EditorProfiler.Instance ?? throw new InvalidOperationException("EditorProfiler.AttachShared must run before any RenderProfilerPanel is created.");
        _profiler.CaptureHandler = _profiler.SnapshotCapturer.HandleCapture;
        _profiler.CaptureFinalizeHandler = _profiler.SnapshotCapturer.Finalize;
        _profiler.SnapshotCaptured += OpenSnapshot;

        _hierarchy.IsSelected = IsHierarchyNodeSelected;
        _hierarchy.Selected += OnHierarchyNodeSelected;
        _flame.NodeClicked += t => _hierarchy.Ping(t.View, t.Pass, t.CommandBuffer);
        _viewInspector.PassSelected += (view, pass) =>
        {
            SelectPass(view, pass);
            _hierarchy.Ping(view, pass, null);
        };

        _profiler.Resume();
    }

    public override void OnClosed()
    {
        _profiler.SnapshotCaptured -= OpenSnapshot;
        _profiler.Pause();
        _passInspector.Dispose();
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

            _flame.Draw(paper, SelectedFrame, FlameGraphHeight);

            paper.Box("rdp_flame_div")
                .Height((UnitValue)1)
                .IsNotInteractable()
                .BackgroundColor(EditorTheme.BorderStrong);

            using (paper.Row("rdp_contents").Width(UnitValue.Stretch()).Height(UnitValue.Stretch()).Enter())
            {
                float contentsHeight = height - ToolbarHeight - FramePickerHeight - FlameGraphHeight - DividerHeight * 3f;
                _hierarchy.Draw(paper, SelectedFrame, HierarchyPanelWidth, contentsHeight);

                paper.Box("rdp_contents_vdiv")
                    .Width(1)
                    .IsNotInteractable()
                    .BackgroundColor(EditorTheme.BorderStrong);

                float detailPanelWidth = width - HierarchyPanelWidth - DividerHeight;
                DrawSelectionInspector(paper, detailPanelWidth, contentsHeight);
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


    private void OnHierarchyNodeSelected(HierarchyNode d)
    {
        switch (d.Kind)
        {
            case HierarchyNodeKind.Frame:
                ClearSubFrameSelection();
                break;
            case HierarchyNodeKind.View:
                SelectView(d.View!);
                break;
            case HierarchyNodeKind.Pass:
                SelectPass(d.View!, d.Pass!);
                break;
            case HierarchyNodeKind.CommandBuffer:
                SelectCommandBuffer(d.View, d.Pass, d.CommandBuffer!);
                break;
            case HierarchyNodeKind.Pipeline:
                SelectPipeline(d.View, d.Pass, d.CommandBuffer, d.Pipeline!);
                break;
            case HierarchyNodeKind.Object:
                SelectObject(d.View, d.Pass, d.CommandBuffer, d.Pipeline!, d.Object!);
                break;
            case HierarchyNodeKind.DrawCall:
                SelectDrawCall(d.View, d.Pass, d.CommandBuffer, d.Pipeline!, d.Object, d.DrawIndex);
                break;
        }
    }


    private bool IsHierarchyNodeSelected(HierarchyNode d) => d.Kind switch
    {
        HierarchyNodeKind.Frame => SelectionType == ProfilerSelectionType.Frame,
        HierarchyNodeKind.View => SelectionType == ProfilerSelectionType.View && ReferenceEquals(SelectedView, d.View),
        HierarchyNodeKind.Pass => SelectionType == ProfilerSelectionType.Pass && ReferenceEquals(SelectedPass, d.Pass),
        HierarchyNodeKind.CommandBuffer => SelectionType == ProfilerSelectionType.CommandBuffer && ReferenceEquals(SelectedCommandBuffer, d.CommandBuffer),
        HierarchyNodeKind.Pipeline => SelectionType == ProfilerSelectionType.Pipeline && ReferenceEquals(SelectedPipeline, d.Pipeline),
        HierarchyNodeKind.Object => SelectionType == ProfilerSelectionType.Object && ReferenceEquals(SelectedObject, d.Object),
        HierarchyNodeKind.DrawCall => SelectionType == ProfilerSelectionType.DrawCall && SelectedDrawCallIndex == d.DrawIndex && ReferenceEquals(SelectedPipeline, d.Pipeline),
        _ => false,
    };
}
