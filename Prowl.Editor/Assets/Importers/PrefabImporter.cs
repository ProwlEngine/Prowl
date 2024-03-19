using Prowl.Runtime;
using Prowl.Runtime.Utils;

namespace Prowl.Editor.Assets
{
    [Importer("GameObjectIcon.png", typeof(Prefab), ".prefab")]
    public class PrefabImporter : ScriptedImporter
    {
        public override void Import(SerializedAsset ctx, FileInfo assetPath)
        {
            var tag = StringTagConverter.ReadFromFile(assetPath);
            Prefab? prefab = Serializer.Deserialize<Prefab>(tag) ?? throw new Exception("Failed to Deserialize Prefab.");
            ctx.SetMainObject(prefab);

            ImGuiNotify.InsertNotification("Prefab Imported.", new(0.75f, 0.35f, 0.20f, 1.00f), assetPath.FullName);
        }
    }

}
