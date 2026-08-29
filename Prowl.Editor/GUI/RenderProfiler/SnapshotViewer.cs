using System;
using System.IO;

using Prowl.Echo;
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

    private const float ToolbarHeight = 32f;
    private const float FlameGraphHeight = 100f;
    private const float HierarchyPanelWidth = 340f;
    private const float DividerHeight = 1f;
    private static readonly string[] SnapshotFilters = { "*.prowlsnap" };
    private static readonly string[] SnapshotFilterLabels = { "Render Profiler Snapshot" };

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
        using (paper.Column("snap_root").Enter())
        {
            DrawToolbar(paper);

            EditorGUI.Divider(paper, "snap_toolbar_div");

            if (_snapshot == null)
            {
                EditorGUI.EmptyState(paper, "snap_empty", "No snapshot loaded", EditorTheme.DefaultFont!);
                return;
            }

            _flame.Draw(paper, _snapshot.Frame, FlameGraphHeight);

            EditorGUI.Divider(paper, "snap_flame_div");

            using (paper.Row("snap_contents").Width(UnitValue.Stretch()).Height(UnitValue.Stretch()).Enter())
            {
                float contentsHeight = Math.Max(0f, height - ToolbarHeight - FlameGraphHeight - DividerHeight * 2f);
                _hierarchy.Draw(paper, _snapshot.Frame, HierarchyPanelWidth, contentsHeight);

                EditorGUI.VerticalDivider(paper, "snap_contents_vdiv");

                float detailPanelWidth = Math.Max(0f, width - HierarchyPanelWidth - DividerHeight);
                DrawSelectionInspector(paper, detailPanelWidth, contentsHeight);
            }
        }
    }


    public override void OnClosed()
    {
        _passInspector.Dispose();
    }


    private void DrawToolbar(Paper paper)
    {
        using (paper.Row("snap_toolbar").Height(ToolbarHeight).ColBetween(6).Padding(6).Enter())
        {
            Origami.Label(paper, "snap_toolbar_name", _snapshot != null ? _snapshot.Name ?? $"Frame {_snapshot.FrameIndex}" : "No snapshot")
                .Heading()
                .AlignLeft()
                .Show();

            if (_snapshot != null)
            {
                Origami.Label(paper, "snap_toolbar_gpu", $"{_snapshot.Frame.GpuMilliseconds:F2} ms")
                    .Muted()
                    .AlignLeft()
                    .Show();
            }

            paper.Box("snap_toolbar_spacer");

            // Save no-ops when there's no snapshot loaded (see SaveSnapshot) rather than disabling the
            // segment - DisabledItem only takes a font-glyph icon, not the vector IOrigamiIcon used here.
            Origami.ButtonGroup(paper, "snap_toolbar_saveload", -1, OnSaveLoadClicked)
                .Item("Save", EditorIcons.FloppyDisk_I)
                .Item("Load", EditorIcons.FolderOpen_I)
                .Small()
                .Show();
        }
    }


    private void OnSaveLoadClicked(int index)
    {
        if (index == 0)
            SaveSnapshot();
        else
            LoadSnapshot();
    }


    private void SaveSnapshot()
    {
        if (_snapshot == null)
            return;

        Snapshot snapshot = _snapshot;
        EditorApplication.OpenFileDialog(FileDialogMode.Save, path =>
            {
                if (path == null)
                    return;

                if (!path.EndsWith(".prowlsnap", StringComparison.OrdinalIgnoreCase))
                    path += ".prowlsnap";

                EchoObject echo = SnapshotSerializer.ToEcho(snapshot);
                echo.WriteToBinary(new FileInfo(path));
            },
            filters: SnapshotFilters,
            filterLabels: SnapshotFilterLabels);
    }


    private void LoadSnapshot()
    {
        EditorApplication.OpenFileDialog(FileDialogMode.Open, path =>
            {
                if (path == null)
                    return;

                EchoObject echo = EchoObject.ReadFromBinary(new FileInfo(path));
                Snapshot = SnapshotSerializer.FromEcho(echo);
            },
            filters: SnapshotFilters,
            filterLabels: SnapshotFilterLabels);
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
                            _passInspector.Draw(paper, _selection.SelectedView, _selection.SelectedPass, History, _resolver, width);
                            break;
                        case ProfilerSelectionType.CommandBuffer:
                            ProfilerCommandBufferInspector.Draw(paper, _selection.SelectedView, _selection.SelectedPass, _selection.SelectedCommandBuffer);
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
