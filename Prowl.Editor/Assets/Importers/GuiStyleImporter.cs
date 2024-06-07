using Prowl.Runtime;
using Prowl.Runtime.Utils;

namespace Prowl.Editor.Assets
{
    [Importer("FileIcon.png", typeof(GuiStyle), ".guistyle")]
    public class GuiStyleImporter : ScriptedImporter
    {
        public override void Import(SerializedAsset ctx, FileInfo assetPath)
        {
            GuiStyle? style;
            try
            {
                string json = File.ReadAllText(assetPath.FullName);
                var tag = StringTagConverter.Read(json);
                style = Serializer.Deserialize<GuiStyle>(tag);
            }
            catch
            {
                style = new GuiStyle();
                string json = StringTagConverter.Write(Serializer.Serialize(style));
                File.WriteAllText(assetPath.FullName, json);
            }

            ctx.SetMainObject(style);
        }
    }

}
