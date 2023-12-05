using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Runtime.Serializer;
using Prowl.Runtime.Utils;

namespace Prowl.Editor.Assets
{
    [Importer("FileIcon.png", typeof(TextAsset), ".txt", ".md")]
    public class TextAssetImporter : ScriptedImporter
    {
        public override void Import(SerializedAsset ctx, FileInfo assetPath)
        {
            TextAsset textAsset = new();
            textAsset.Text = File.ReadAllText(assetPath.FullName);

            ctx.SetMainObject(textAsset);

            ImGuiNotify.InsertNotification("TextAsset Imported.", new(0.75f, 0.35f, 0.20f, 1.00f), assetPath.FullName);
        }
    }

}
