using Prowl.PaperUI;

namespace Prowl.Editor.Docking;

public abstract class DockPanel
{
    public abstract string Title { get; }
    public bool IsOpen { get; set; } = true;

    public abstract void OnGUI(Paper paper, float width, float height);
}
