using Prowl.Editor.Core;
using Prowl.Editor.Profiling;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;

namespace Prowl.Editor.GUI.RenderProfiler;

public class SnapshotViewerPanel : DockPanel
{
    public override string Title => "Snapshot Viewer";
    public override string Icon => EditorIcons.MagnifyingGlassChart;

    public Snapshot? Snapshot { get; set; }

    public SnapshotViewerPanel() { }

    public SnapshotViewerPanel(Snapshot snapshot)
    {
        Snapshot = snapshot;
    }

    public override void OnGUI(Paper paper, float width, float height)
    {
    }

    public static void Open(Snapshot snapshot)
    {
        var panel = new SnapshotViewerPanel(snapshot);
        EditorApplication.Instance?.OpenPanelInstance(panel);
    }
}
