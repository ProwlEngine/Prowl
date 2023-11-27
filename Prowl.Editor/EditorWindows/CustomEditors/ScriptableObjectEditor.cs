using Prowl.Runtime;
using Prowl.Runtime.Utils;
using Prowl.Editor.Assets;
using Prowl.Editor.PropertyDrawers;
using HexaEngine.ImGuiNET;
using System.Reflection;
using Prowl.Runtime.Resources;
using Prowl.Runtime.Assets;
using System;

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
                ScriptableObject scriptObject = JsonUtility.Deserialize<ScriptableObject>(File.ReadAllText((target as MetaFile).AssetPath.FullName));

                FieldInfo[] fields = scriptObject.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                // Private fields need to have the SerializeField attribute
                fields = fields.Where(field => field.IsPublic || Attribute.IsDefined(field, typeof(SerializeFieldAttribute))).ToArray();

                foreach (var field in fields)
                {
                    // Dont render if the field has the Hide attribute
                    if (!Attribute.IsDefined(field, typeof(HideInInspectorAttribute)))
                    {
                        var attributes = field.GetCustomAttributes(true);
                        var imGuiAttributes = attributes.Where(attr => attr is IImGUIAttri).Cast<IImGUIAttri>();

                        foreach (var imGuiAttribute in imGuiAttributes)
                            imGuiAttribute.Draw();

                        // enums are a special case
                        if (field.FieldType.IsEnum)
                        {
                            var currentEnumValue = (Enum)field.GetValue(scriptObject);

                            if (ImGui.BeginCombo(field.FieldType.Name, currentEnumValue.ToString()))
                            {
                                foreach (var enumValue in Enum.GetValues(field.FieldType))
                                {
                                    bool isSelected = currentEnumValue.Equals(enumValue);

                                    bool enumChanged = ImGui.Selectable(enumValue.ToString(), isSelected);
                                    changed |= enumChanged;
                                    if (enumChanged) field.SetValue(scriptObject, enumValue);

                                    if (isSelected) ImGui.SetItemDefaultFocus();
                                }

                                ImGui.EndCombo();
                            }
                        }
                        else
                        {
                            // Draw the field using PropertyDrawer.Draw
                            changed |= PropertyDrawer.Draw(scriptObject, field);
                        }

                        foreach (var imGuiAttribute in imGuiAttributes)
                            imGuiAttribute.End();
                    }
                }

                // Draw any Buttons
                ImGUIButtonAttribute.DrawButtons(scriptObject);

                if (changed)
                {
                    scriptObject.OnValidate();
                    File.WriteAllText((target as MetaFile).AssetPath.FullName, JsonUtility.Serialize(scriptObject));
                    AssetDatabase.Reimport(AssetDatabase.FileToRelative((target as MetaFile).AssetPath));
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
