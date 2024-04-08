using Prowl.Runtime;
using Prowl.Runtime.Utils;

namespace Prowl.Editor.Editor.ProjectSettings
{
    [FilePath("BuildSettings.projsetting", FilePathAttribute.Location.EditorSetting)]
    public class BuildProjectSetting : ScriptableSingleton<BuildProjectSetting>
    {
        public AssetRef<Scene> InitialScene;

    }
}
