// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Text.Json.Nodes;

using Prowl.PaperUI;

namespace Prowl.OrigamiUI;

public abstract class DockPanel
{
    public abstract string Title { get; }
    public virtual string Icon => "";
    public bool IsOpen { get; set; } = true;

    public abstract void OnGUI(Paper paper, float width, float height);

    /// <summary>
    /// Width (px) to reserve on the right side of this panel's tab bar for header controls
    /// (e.g. a refresh or options button). 0 = no header controls. Only the active tab's panel
    /// draws its header content.
    /// </summary>
    public virtual float HeaderWidth => 0f;

    /// <summary>
    /// Draw controls into the reserved header area on the right of the leaf's tab bar. The area is
    /// <paramref name="width"/> (= <see cref="HeaderWidth"/>) by <paramref name="height"/> (tab-bar
    /// height). Called for the active tab only, inside a right-aligned Row.
    /// </summary>
    public virtual void OnHeaderContent(Paper paper, float width, float height) { }

    /// <summary>
    /// Write panel-specific state for layout persistence. Return false if nothing to save.
    /// </summary>
    public virtual bool SerializeState(JsonObject state) => false;

    /// <summary>
    /// Restore panel state from a previously serialized blob.
    /// </summary>
    public virtual void RestoreState(JsonObject state) { }

    /// <summary>
    /// Called once when the user closes this panel's tab, permanently removing it from the dock
    /// tree. NOT called when the tab is merely dragged to a different dock/floating window - the
    /// panel stays open in that case. Override to release resources (GPU handles, event
    /// subscriptions, etc.) the panel would otherwise hold onto for the rest of the process.
    /// </summary>
    public virtual void OnClosed() { }
}
