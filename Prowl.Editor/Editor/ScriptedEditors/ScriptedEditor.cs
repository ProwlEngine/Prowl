// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.GUI;

namespace Prowl.Editor;

public class ScriptedEditor
{
    public Gui gui => Gui.ActiveGUI;

    public object target { get; internal set; }
    public virtual void OnEnable() { }
    public virtual void OnInspectorGUI() { }
    public virtual void OnDisable() { }
}
