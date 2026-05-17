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
    /// Write panel-specific state for layout persistence. Return false if nothing to save.
    /// </summary>
    public virtual bool SerializeState(JsonObject state) => false;

    /// <summary>
    /// Restore panel state from a previously serialized blob.
    /// </summary>
    public virtual void RestoreState(JsonObject state) { }
}
