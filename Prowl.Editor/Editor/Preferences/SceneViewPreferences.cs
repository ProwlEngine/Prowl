using Prowl.Editor.Utilities;
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
        [Space, Text("Rendering:")]
        public float NearClip = 0.02f;
        public float FarClip = 10000f;
        [Space]
        public float RenderResolution = 1f;
    }
}
