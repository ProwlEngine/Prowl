using Prowl.Runtime;
using Prowl.Runtime.Utils;

namespace Prowl.Editor.Assets
{
    [Importer("ModelIcon.png", typeof(Mesh), ".mesh")]
    public class MeshImporter : ScriptedImporter
    {
        public override void Import(SerializedAsset ctx, FileInfo assetPath)
        {
            // Load the Texture into a TextureData Object and serialize to Asset Folder
            Mesh? mesh;
            try
            {
                string json = File.ReadAllText(assetPath.FullName);
                var tag = StringTagConverter.Read(json);
                mesh = Serializer.Deserialize<Mesh>(tag);
            }
            catch
            {
                Debug.LogError("Failed to deserialize mesh.");
                return;
            }

            ctx.SetMainObject(mesh);

            ImGuiNotify.InsertNotification("Mesh Imported.", new(0.75f, 0.35f, 0.20f, 1.00f), assetPath.FullName);
        }
    }

}
