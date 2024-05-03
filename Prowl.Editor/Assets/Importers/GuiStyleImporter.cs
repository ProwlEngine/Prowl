using Hexa.NET.ImGui;
using Prowl.Editor.EditorWindows.CustomEditors;
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

    [CustomEditor(typeof(GuiStyleImporter))]
    public class GuiStyleImporterEditor : ScriptedEditor
    {
        public override void OnInspectorGUI()
        {
            var importer = (GuiStyleImporter)(target as MetaFile).importer;

            try
            {
                var tag = StringTagConverter.ReadFromFile((target as MetaFile).AssetPath);
                GuiStyle style = Serializer.Deserialize<GuiStyle>(tag);

                GuiStyleEditor editor = new GuiStyleEditor(style, () => {
                    StringTagConverter.WriteToFile(Serializer.Serialize(style), (target as MetaFile).AssetPath);
                    AssetDatabase.Reimport((target as MetaFile).AssetPath);
                });
                editor.OnInspectorGUI();
            }
            catch
            {
                ImGui.LabelText("Failed to Deserialize GuiStyle", "The GuiStyle file is invalid.");
            }
        }
    }

}
