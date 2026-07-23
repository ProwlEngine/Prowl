using Prowl.Editor.Core;
using Prowl.Editor.Profiling;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime;

namespace Prowl.Editor.GUI.RenderProfiler;

public class RenderProfilerPanel : DockPanel
{
    [MenuItem("Window/Debug/Render Profiler", priority: 102)]
    static void Open() => EditorApplication.Instance?.OpenPanel(typeof(RenderProfilerPanel));

    public override string Title => "Render Profiler";
    public override string Icon => EditorIcons.ChartLine;

    private readonly EditorProfiler _profiler;

    public RenderProfilerPanel()
    {
        _profiler = new EditorProfiler();
        _profiler.Attach(Graphics.Device);
    }

    public override void OnClosed()
    {
        _profiler.Detach();
    }

    public override void OnGUI(Paper paper, float width, float height)
    {
    }

    public void OpenSnapshot(Snapshot snapshot)
    {
        SnapshotViewerPanel.Open(snapshot);
    }
}
