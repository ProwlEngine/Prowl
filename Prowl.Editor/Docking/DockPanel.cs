using System.Text.Json.Nodes;

using Prowl.PaperUI;

namespace Prowl.Editor.Docking;

public abstract class DockPanel
{
    public abstract string Title { get; }
    public virtual string Icon => "";
    public bool IsOpen { get; set; } = true;

    public abstract void OnGUI(Paper paper, float width, float height);

    /// <summary>
    /// Write any panel-specific state that should persist across editor restarts into
    /// <paramref name="state"/>. Return false if there's nothing to save so the serializer
    /// can skip storing an empty blob. Called once during layout save.
    ///
    /// Avoid storing transient UI state (hover, scroll) unless it's genuinely useful to
    /// restore — the goal is "picking up where I left off", not frame-perfect replay.
    /// </summary>
    public virtual bool SerializeState(JsonObject state) => false;

    /// <summary>
    /// Restore panel state previously written by <see cref="SerializeState"/>. Called
    /// after the panel is instantiated and before its first draw. The scene has already
    /// been loaded by this point, so it's safe to resolve GameObjects by Identifier.
    /// </summary>
    public virtual void RestoreState(JsonObject state) { }
}
