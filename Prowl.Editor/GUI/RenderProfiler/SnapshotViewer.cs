using Prowl.Editor.Core;
using Prowl.Editor.GUI.RenderProfiler.Data;
using Prowl.Editor.GUI.RenderProfiler.Inspectors;
using Prowl.Editor.GUI.RenderProfiler.Widgets;
using Prowl.Editor.Profiling;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

namespace Prowl.Editor.GUI.RenderProfiler;

public class SnapshotViewerPanel : DockPanel
{
    [MenuItem("Window/Debug/Snapshot Viewer", priority: 102)]
    static void Open() => EditorApplication.Instance?.OpenPanel(typeof(SnapshotViewerPanel));

    public override string Title => "Snapshot Viewer";
    public override string Icon => EditorIcons.MagnifyingGlassChart;

    private const float HeaderHeight = 32f;
    private const float FlameGraphHeight = 100f;
    private const float HierarchyPanelWidth = 340f;
    private const float DividerHeight = 1f;

    private readonly ProfilerSelection _selection = new();
    private readonly ProfilerHierarchyView _hierarchy = new("snap_hier_tree");
    private readonly ProfilerFlameGraphView _flame = new("snap_flame");
    private readonly ProfilerViewInspector _viewInspector = new();
    private readonly ProfilerPassInspector _passInspector = new();

    private Snapshot? _snapshot;
    private ISnapshotResourceResolver? _resolver;

    public Snapshot? Snapshot
    {
        get => _snapshot;
        set
        {
            _snapshot = value;
            _resolver = value != null ? new SnapshotResourceResolver(value) : null;
            _selection.ClearSubFrameSelection();
        }
    }

    private IProfilerHistory History => new SingleFrameHistory(_snapshot!.Frame);

    public SnapshotViewerPanel()
    {
        _hierarchy.IsSelected = IsHierarchyNodeSelected;
        _hierarchy.Selected += OnHierarchyNodeSelected;
        _flame.NodeClicked += t => _hierarchy.Ping(t.View, t.Pass, t.CommandBuffer);
        _viewInspector.PassSelected += (view, pass) =>
        {
            _selection.SelectPass(view, pass);
            _hierarchy.Ping(view, pass, null);
        };
    }

    public SnapshotViewerPanel(Snapshot snapshot) : this()
    {
        Snapshot = snapshot;
    }

    public override void OnGUI(Paper paper, float width, float height)
    {
        if (_snapshot == null)
        {
            Origami.Label(paper, "snap_empty", "No snapshot loaded")
                .Muted()
                .Show();
            return;
        }

        using (paper.Column("snap_root").Enter())
        {
            DrawHeader(paper);

            paper.Box("snap_header_div")
                .Height(DividerHeight)
                .IsNotInteractable()
                .BackgroundColor(EditorTheme.BorderStrong);

            _flame.Draw(paper, _snapshot.Frame, FlameGraphHeight);

            paper.Box("snap_flame_div")
                .Height(DividerHeight)
                .IsNotInteractable()
                .BackgroundColor(EditorTheme.BorderStrong);

            using (paper.Row("snap_contents").Width(UnitValue.Stretch()).Height(UnitValue.Stretch()).Enter())
            {
                float contentsHeight = height - HeaderHeight - FlameGraphHeight - DividerHeight * 2f;
                _hierarchy.Draw(paper, _snapshot.Frame, HierarchyPanelWidth, contentsHeight);

                paper.Box("snap_contents_vdiv")
                    .Width(1)
                    .IsNotInteractable()
                    .BackgroundColor(EditorTheme.BorderStrong);

                float detailPanelWidth = width - HierarchyPanelWidth - DividerHeight;
                DrawSelectionInspector(paper, detailPanelWidth, contentsHeight);
            }
        }
    }


    private void DrawHeader(Paper paper)
    {
        using (paper.Row("snap_header").Height(HeaderHeight).ColBetween(6).Padding(6).Enter())
        {
            Origami.Label(paper, "snap_header_name", _snapshot!.Name ?? $"Frame {_snapshot.FrameIndex}")
                .Heading()
                .AlignLeft()
                .Show();

            paper.Box("snap_header_spacer");

            Origami.Label(paper, "snap_header_gpu", $"{_snapshot.Frame.GpuMilliseconds:F2} ms")
                .Muted()
                .AlignRight()
                .Show();
        }
    }


    private void DrawSelectionInspector(Paper paper, float width, float height)
    {
        using (paper.Box("snap_detail_panel").Enter())
        {
            Origami.ScrollView(paper, "snap_detail_scroll", width, height)
                .Padding(8)
                .ColSpacing(6)
                .Body(() =>
                {
                    switch (_selection.SelectionType)
                    {
                        case ProfilerSelectionType.Frame:
                            ProfilerFrameInspector.Draw(paper, _snapshot!.Frame, History);
                            break;
                        case ProfilerSelectionType.View:
                            _viewInspector.Draw(paper, _selection.SelectedView, History, width);
                            break;
                        case ProfilerSelectionType.Pass:
                            _passInspector.Draw(paper, _selection.SelectedView, _selection.SelectedPass, History, _resolver);
                            break;
                    }
                });
        }
    }


    private void OnHierarchyNodeSelected(HierarchyNode d)
    {
        switch (d.Kind)
        {
            case HierarchyNodeKind.Frame:
                _selection.ClearSubFrameSelection();
                break;
            case HierarchyNodeKind.View:
                _selection.SelectView(d.View!);
                break;
            case HierarchyNodeKind.Pass:
                _selection.SelectPass(d.View!, d.Pass!);
                break;
            case HierarchyNodeKind.CommandBuffer:
                _selection.SelectCommandBuffer(d.View, d.Pass, d.CommandBuffer!);
                break;
            case HierarchyNodeKind.Pipeline:
                _selection.SelectPipeline(d.View, d.Pass, d.CommandBuffer, d.Pipeline!);
                break;
            case HierarchyNodeKind.Object:
                _selection.SelectObject(d.View, d.Pass, d.CommandBuffer, d.Pipeline!, d.Object!);
                break;
            case HierarchyNodeKind.DrawCall:
                _selection.SelectDrawCall(d.View, d.Pass, d.CommandBuffer, d.Pipeline!, d.Object, d.DrawIndex);
                break;
        }
    }


    private bool IsHierarchyNodeSelected(HierarchyNode d) => d.Kind switch
    {
        HierarchyNodeKind.Frame => _selection.SelectionType == ProfilerSelectionType.Frame,
        HierarchyNodeKind.View => _selection.SelectionType == ProfilerSelectionType.View && ReferenceEquals(_selection.SelectedView, d.View),
        HierarchyNodeKind.Pass => _selection.SelectionType == ProfilerSelectionType.Pass && ReferenceEquals(_selection.SelectedPass, d.Pass),
        HierarchyNodeKind.CommandBuffer => _selection.SelectionType == ProfilerSelectionType.CommandBuffer && ReferenceEquals(_selection.SelectedCommandBuffer, d.CommandBuffer),
        HierarchyNodeKind.Pipeline => _selection.SelectionType == ProfilerSelectionType.Pipeline && ReferenceEquals(_selection.SelectedPipeline, d.Pipeline),
        HierarchyNodeKind.Object => _selection.SelectionType == ProfilerSelectionType.Object && ReferenceEquals(_selection.SelectedObject, d.Object),
        HierarchyNodeKind.DrawCall => _selection.SelectionType == ProfilerSelectionType.DrawCall && _selection.SelectedDrawCallIndex == d.DrawIndex && ReferenceEquals(_selection.SelectedPipeline, d.Pipeline),
        _ => false,
    };


    public static void Open(Snapshot snapshot)
    {
        var panel = new SnapshotViewerPanel(snapshot);
        EditorApplication.Instance?.OpenPanelInstance(panel);
    }
}
