using Hexa.NET.ImGui;
using Prowl.Editor.Assets;
using Prowl.Editor.PropertyDrawers;
using Prowl.Runtime;
using System.Reflection;

namespace Prowl.Editor.EditorWindows.CustomEditors
{
    [CustomEditor(typeof(ScriptableObjectImporter))]
    public class ScriptableObjectEditor : ScriptedEditor
    {
        public override void OnInspectorGUI()
        {
            var importer = (ScriptableObjectImporter)(target as MetaFile).importer;
            ImGui.PushID(importer.GetHashCode());

            try
            {
                bool changed = false;

                ScriptableObject scriptObject = Serializer.Deserialize<ScriptableObject>(StringTagConverter.ReadFromFile((target as MetaFile).AssetPath));

                FieldInfo[] fields = scriptObject.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                // Private fields need to have the SerializeField attribute
                fields = fields.Where(field => field.IsPublic || Attribute.IsDefined(field, typeof(SerializeFieldAttribute))).ToArray();

                foreach (var field in fields)
                {
                    // Draw the field using PropertyDrawer.Draw
                    changed |= PropertyDrawer.Draw(scriptObject, field);
                }

                // Draw any Buttons
                changed |= EditorGui.HandleAttributeButtons(scriptObject);

                if (changed)
                {
                    scriptObject.OnValidate();
                    StringTagConverter.WriteToFile(Serializer.Serialize(scriptObject), (target as MetaFile).AssetPath);
                    AssetDatabase.Reimport((target as MetaFile).AssetPath);
                }
            }
            catch
            {
                ImGui.LabelText("Failed to Deserialize ScriptableObject", "The ScriptableObject file is invalid.");
            }
            ImGui.PopID();
        }

    }
}
