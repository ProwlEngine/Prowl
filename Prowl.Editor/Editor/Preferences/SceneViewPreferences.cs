// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.Utils;

namespace Prowl.Editor.Preferences;

[FilePath("SceneView.pref", FilePathAttribute.Location.EditorPreference)]
public class SceneViewPreferences : ScriptableSingleton<SceneViewPreferences>
{
    [Text("Controls:")]
    public readonly float LookSensitivity = 1f;
    public readonly float PanSensitivity = 1f;
    public readonly float ZoomSensitivity = 1f;

    public readonly bool InvertLook = false;

    public readonly double SnapDistance = 0.5f;
    public readonly double SnapAngle = 10f;

    [Space, Text("Rendering:")]
    public SceneViewWindow.GridType GridType = SceneViewWindow.GridType.XZ;
    public readonly bool ShowFPS = true;

    [Space]
    public readonly float NearClip = 0.02f;
    public readonly float FarClip = 10000f;
    public float RenderResolution = 1f;

    [Text("Grid:")]
    public readonly float LineWidth = 0.02f;
    public readonly float PrimaryGridSize = 1f;
    public readonly float SecondaryGridSize = 5f;
    public Color GridColor = new Color(1.0f, 1.0f, 1.0f, 0.5f);
}
