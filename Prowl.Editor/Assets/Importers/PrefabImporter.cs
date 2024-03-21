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
        }
    }

}
