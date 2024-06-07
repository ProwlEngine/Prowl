using Prowl.Editor.Assets;
using Prowl.Runtime;
using static Prowl.Editor.EditorGUI;

namespace Prowl.Editor.ScriptedEditors
{
    [CustomEditor(typeof(ScriptableObjectImporter))]
    public class ScriptableObjectEditor : ScriptedEditor
    {
        public override void OnInspectorGUI()
        {
            var importer = (ScriptableObjectImporter)(target as MetaFile).importer;

            try
            {
                bool changed = false;

                ScriptableObject scriptObject = Serializer.Deserialize<ScriptableObject>(StringTagConverter.ReadFromFile((target as MetaFile).AssetPath));

                object t = scriptObject;
                changed |= PropertyGrid("CompPropertyGrid", ref t, TargetFields.Serializable, PropertyGridConfig.NoHeader | PropertyGridConfig.NoBorder | PropertyGridConfig.NoBackground);

                // Draw any Buttons
                //changed |= EditorGui.HandleAttributeButtons(scriptObject);

                if (changed)
                {
                    scriptObject.OnValidate();
                    StringTagConverter.WriteToFile(Serializer.Serialize(scriptObject), (target as MetaFile).AssetPath);
                    AssetDatabase.Reimport((target as MetaFile).AssetPath);
                }
            }
            catch
            {
                g.Node("DummyForText").ExpandWidth().Height(GuiStyle.ItemHeight * 10);
                g.Draw2D.DrawText("Failed to Deserialize ScriptableObject, The ScriptableObject file is invalid.", g.CurrentNode.LayoutData.Rect);
            }
        }

    }
}
