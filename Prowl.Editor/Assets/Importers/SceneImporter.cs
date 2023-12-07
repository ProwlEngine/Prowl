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
            Scene? scene = TagSerializer.Deserialize<Scene>(tag) ?? throw new Exception("Failed to Deserialize Scene.");
            ctx.SetMainObject(scene);

            ImGuiNotify.InsertNotification("Scene Imported.", new(0.75f, 0.35f, 0.20f, 1.00f), assetPath.FullName);
        }
    }

}
