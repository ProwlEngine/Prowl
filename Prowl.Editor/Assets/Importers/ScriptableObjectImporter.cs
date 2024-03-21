using Prowl.Runtime;
using Prowl.Runtime.Utils;

namespace Prowl.Editor.Assets
{
    [Importer("FileIcon.png", typeof(ScriptableObject), ".scriptobj")]
    public class ScriptableObjectImporter : ScriptedImporter
    {
        public override void Import(SerializedAsset ctx, FileInfo assetPath)
        {
            // Load the Texture into a TextureData Object and serialize to Asset Folder
            //var scriptable = JsonUtility.Deserialize<ScriptableObject>(File.ReadAllText(assetPath.FullName));
            var scriptable = Serializer.Deserialize<ScriptableObject>(StringTagConverter.ReadFromFile(assetPath));
            ctx.SetMainObject(scriptable);
        }
    }

}
