using Prowl.Editor.Assets;
using Prowl.Editor.Preferences;
using Prowl.Runtime;
using static Prowl.Editor.EditorGUI;

namespace Prowl.Editor.ScriptedEditors
{
    [CustomEditor(typeof(ScriptableObjectImporter))]
    public class ScriptableObjectEditor : ScriptedEditor
    {
        ScriptableObject? scriptObject = null;

        public override void OnInspectorGUI()
        {
            var importer = (ScriptableObjectImporter)(target as MetaFile).importer;

            try
            {
                bool changed = false;

                scriptObject ??= Serializer.Deserialize<ScriptableObject>(StringTagConverter.ReadFromFile((target as MetaFile).AssetPath));

                object t = scriptObject;
                changed |= PropertyGrid("CompPropertyGrid", ref t, TargetFields.Serializable | EditorGUI.TargetFields.Properties, PropertyGridConfig.NoHeader | PropertyGridConfig.NoBorder | PropertyGridConfig.NoBackground);

                // Draw any Buttons
                //changed |= EditorGui.HandleAttributeButtons(scriptObject);

                if (changed)
                {
                    scriptObject.OnValidate();
                    StringTagConverter.WriteToFile(Serializer.Serialize(scriptObject), (target as MetaFile).AssetPath);
                    AssetDatabase.Reimport((target as MetaFile).AssetPath);
                }
            }
            catch (System.Exception e)
            {
                double ItemSize = EditorStylePrefs.Instance.ItemSize;
                gui.Node("DummyForText").ExpandWidth().Height(ItemSize * 10);
                gui.Draw2D.DrawText("Failed to Deserialize ScriptableObject, The ScriptableObject file is invalid. Error: " + e.ToString(), gui.CurrentNode.LayoutData.Rect);
            }
        }

    }
}
