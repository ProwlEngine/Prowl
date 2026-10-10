using System;
using System.Linq;

using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Vector;

namespace Prowl.Graphite.Debugger.Viewer;

/// <summary>The live render profiler and the snapshot viewer as dockable Origami panels.</summary>
/// <remarks>Dock Live yourself. Snapshots opens as a floating window when a snapshot is taken, or is focused if already docked.</remarks>
public sealed class RenderProfilerWindows
{
    private readonly DockSpace _dock;
    private readonly ProfilerView _snapshotView;

    public RenderProfilerWindows(IRenderProfilerSource source, DockSpace dock)
    {
        _dock = dock;
        Live = new LiveProfilerPanel(new ProfilerView(source, false, "rp_live", ShowSnapshots));
        _snapshotView = new ProfilerView(source, true, "rp_snapshots", ShowSnapshots);
        Snapshots = new SnapshotViewerPanel(_snapshotView);
    }

    public DockPanel Live { get; }

    public DockPanel Snapshots { get; }

    /// <summary>Brings the live profiler to front, docking or floating it when it is closed.</summary>
    public void ShowLive() => Show(Live);

    /// <summary>Brings the snapshot viewer to front, opening it as a floating window when it is closed.</summary>
    public void ShowSnapshots() => Show(Snapshots);

    /// <summary>Asks for a saved snapshot file, opens it, and shows the snapshot viewer.</summary>
    public void LoadSnapshot() => _snapshotView.LoadSnapshot();

    private void Show(DockPanel panel)
    {
        FloatingWindow? window = _dock.FloatingWindows.FirstOrDefault(w => Activate(w.Node, panel));
        if (window != null)
        {
            _dock.FloatingWindows.Remove(window);
            _dock.FloatingWindows.Add(window);
            return;
        }

        if (Activate(_dock.Root, panel))
            return;

        if (_dock.Root.IsLeaf && _dock.Root.Tabs!.Count == 0)
        {
            _dock.Root.InsertTab(panel);
            _dock.Root.ActiveTabIndex = _dock.Root.Tabs.Count - 1;
            return;
        }

        Float2 position = new(80, 80), size = new(1280, 820);
        Type other = panel == Live ? typeof(SnapshotViewerPanel) : typeof(LiveProfilerPanel);
        if (_dock.TryGetPanelRect(other, out Rect anchor))
        {
            position = new Float2((float)anchor.Min.X + 60, (float)anchor.Min.Y + 40);
            size = new Float2((float)anchor.Size.X * 0.9f, (float)anchor.Size.Y * 0.9f);
        }
        _dock.FloatingWindows.Add(new FloatingWindow(DockNode.Leaf(panel), position, size));
    }

    private static bool Activate(DockNode? node, DockPanel panel)
    {
        if (node == null)
            return false;
        if (node.IsLeaf)
        {
            int index = node.Tabs!.IndexOf(panel);
            if (index < 0)
                return false;
            node.ActiveTabIndex = index;
            return true;
        }
        return Activate(node.ChildA, panel) || Activate(node.ChildB, panel);
    }
}

/// <summary>Dockable live render profiler.</summary>
public sealed class LiveProfilerPanel : DockPanel
{
    private readonly ProfilerView _view;

    internal LiveProfilerPanel(ProfilerView view)
    {
        _view = view;
    }

    public override string Title => "Render Profiler";

    public override string Icon => Glyph.ChartLine;

    public override void OnGUI(Paper paper, float width, float height) => _view.Draw(paper, width, height);
}

/// <summary>Dockable snapshot viewer with save and load.</summary>
public sealed class SnapshotViewerPanel : DockPanel
{
    private readonly ProfilerView _view;

    internal SnapshotViewerPanel(ProfilerView view)
    {
        _view = view;
    }

    public override string Title => "Snapshot";

    public override string Icon => Glyph.Camera;

    public override void OnGUI(Paper paper, float width, float height) => _view.Draw(paper, width, height);
}
