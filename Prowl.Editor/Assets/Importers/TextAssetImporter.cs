using Prowl.Runtime;
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
        }
    }

}
