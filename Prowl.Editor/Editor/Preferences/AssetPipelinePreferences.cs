using Prowl.Runtime;
using Prowl.Runtime.Utils;

namespace Prowl.Editor.Preferences
{
    [FilePath("AssetPipeline.pref", FilePathAttribute.Location.EditorPreference)]
    public class AssetPipelinePreferences : ScriptableSingleton<AssetPipelinePreferences>
    {
        [Text("Asset Browser:")]
        public bool HideExtensions = true;
        public float ThumbnailSize = 0.0f;
        [Text("Pipeline:")]
        public bool AutoImport = true;
    }
}
