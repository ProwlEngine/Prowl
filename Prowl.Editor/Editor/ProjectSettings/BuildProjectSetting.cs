using Prowl.Editor.Utilities;
using Prowl.Runtime;

namespace Prowl.Editor.Editor.ProjectSettings
{
    [EditorFilePath("BuildSettings.projsetting", EditorFilePathAttribute.Location.ProjectSettingsFolder)]
    public class BuildProjectSetting : ScriptableSingleton<BuildProjectSetting>
    {
        public AssetRef<Scene> InitialScene;

    }
}
