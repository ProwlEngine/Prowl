// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Icons;
using Prowl.Runtime.SceneManagement;

using static Prowl.Editor.EditorGUI;

namespace Prowl.Editor;

public class SceneEditorWindow : EditorWindow
{
    protected override double Width { get; } = 300;
    protected override double Height { get; } = 512;

    public SceneEditorWindow() : base() { Title = FontAwesome6.Gear + " Scene Settings"; }

    protected override void Draw()
    {
        if (!Project.HasProject) return;

        // Draw Settings
        object setting = SceneManager.Scene;

        if (PropertyGrid("Scene Settings", ref setting, TargetFields.Serializable | TargetFields.Properties, PropertyGridConfig.NoBorder | PropertyGridConfig.NoBackground))
        {
            SceneManager.Scene.OnValidate();
        }
    }
}
