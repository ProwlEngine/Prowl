using Prowl.Editor.Utilities;
using Prowl.Runtime;

namespace Prowl.Editor.Editor.ProjectSettings
{
    [FilePath("BuildSettings.projsetting", FilePathAttribute.Location.ProjectSettingsFolder)]
    public class BuildProjectSetting : ScriptableSingleton<BuildProjectSetting>
    {
        public AssetRef<Scene> InitialScene;

    }
}
