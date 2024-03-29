using Prowl.Editor.Utilities;
using Prowl.Runtime;

namespace Prowl.Editor.Editor.Preferences
{
    [FilePath("AssetPipeline.pref", FilePathAttribute.Location.PreferencesFolder)]
    public class AssetPipelinePreferences : ScriptableSingleton<AssetPipelinePreferences>
    {
        [Text("Asset Browser:")]
        public bool HideExtensions = true;
        public float ThumbnailSize = 0.0f;
        [Text("Pipeline:")]
        public bool AutoImport = true;
    }
}
