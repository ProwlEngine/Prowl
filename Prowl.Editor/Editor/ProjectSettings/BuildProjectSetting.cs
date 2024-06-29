using Prowl.Runtime;
using Prowl.Runtime.Utils;

namespace Prowl.Editor.ProjectSettings
{
    [FilePath("BuildSettings.projsetting", FilePathAttribute.Location.EditorSetting)]
    public class BuildProjectSetting : ScriptableSingleton<BuildProjectSetting>
    {
        public AssetRef<Scene>[] Scenes = [];

        public override void OnValidate()
        {
            Scenes ??= []; // Ensure scenes are never null
        }

    }
}
