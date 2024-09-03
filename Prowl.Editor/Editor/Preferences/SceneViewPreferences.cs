// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.Utils;

namespace Prowl.Editor.Preferences
{
    [FilePath("SceneView.pref", FilePathAttribute.Location.EditorPreference)]
    public class SceneViewPreferences : ScriptableSingleton<SceneViewPreferences>
    {
        [Text("Controls:")]
        public float LookSensitivity = 1f;
        public float PanSensitivity = 1f;
        public float ZoomSensitivity = 1f;

        public bool InvertLook = false;

        public double SnapDistance = 0.5f;
        public double SnapAngle = 10f;

        [Space, Text("Rendering:")]
        public SceneViewWindow.GridType GridType = SceneViewWindow.GridType.XZ;
        public bool ShowFPS = true;

        [Space]
        public float NearClip = 0.02f;
        public float FarClip = 10000f;
        public float RenderResolution = 1f;

        [Text("Grid:")]
        public float LineWidth = 1f;
        public float PrimaryGridSize = 1f;
        public float SecondaryGridSize = 5f;
        public Color GridColor = Color.white;
    }
}
