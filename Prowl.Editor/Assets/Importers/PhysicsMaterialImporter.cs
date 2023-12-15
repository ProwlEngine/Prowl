using Prowl.Runtime;
using Prowl.Runtime.Utils;

namespace Prowl.Editor.Assets
{
    [Importer("FileIcon.png", typeof(PhysicsMaterial), ".physicsmat")]
    public class PhysicsMaterialImporter : ScriptedImporter
    {
        public override void Import(SerializedAsset ctx, FileInfo assetPath)
        {
            CompoundTag tag = StringTagConverter.ReadFromFile(assetPath);
            PhysicsMaterial mat = TagSerializer.Deserialize<PhysicsMaterial>(tag);

            ctx.SetMainObject(mat);

            ImGuiNotify.InsertNotification("Physics Material Imported.", new(0.75f, 0.35f, 0.20f, 1.00f), assetPath.FullName);
        }
    }

}
