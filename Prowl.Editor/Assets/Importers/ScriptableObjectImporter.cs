using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Runtime.Utils;

namespace Prowl.Editor.Assets
{
    [Importer("FileIcon.png", typeof(ScriptableObject), ".scriptobj")]
    public class ScriptableObjectImporter : ScriptedImporter
    {
        public override void Import(SerializedAsset ctx, FileInfo assetPath)
        {
            try
            {
                // Load the Texture into a TextureData Object and serialize to Asset Folder
                var scriptable = JsonUtility.Deserialize<ScriptableObject>(File.ReadAllText(assetPath.FullName));
                ctx.SetMainObject(scriptable);

                ImGuiNotify.InsertNotification("ScriptableObject Imported.", new(0.75f, 0.35f, 0.20f, 1.00f), assetPath.FullName);
            }
            catch (Exception e)
            {
                ImGuiNotify.InsertNotification("Failed to Import ScriptableObject.", new(0.8f, 0.1f, 0.1f, 1), "Reason: " + e.Message);
            }
        }
    }

}
