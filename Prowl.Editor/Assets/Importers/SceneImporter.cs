using Prowl.Runtime;
using Prowl.Runtime.Utils;

namespace Prowl.Editor.Assets
{
    [Importer("HierarchyIcon.png", typeof(Scene), ".scene")]
    public class SceneImporter : ScriptedImporter
    {
        public override void Import(SerializedAsset ctx, FileInfo assetPath)
        {
            var tag = StringTagConverter.ReadFromFile(assetPath);
            Scene? scene = Serializer.Deserialize<Scene>(tag) ?? throw new Exception("Failed to Deserialize Scene.");
            ctx.SetMainObject(scene);
        }
    }

}
