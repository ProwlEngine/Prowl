// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Assets;
using Prowl.Editor.Preferences;
using Prowl.Runtime;

using static Prowl.Editor.EditorGUI;

namespace Prowl.Editor.ScriptedEditors;

[CustomEditor(typeof(ScriptableObjectImporter))]
public class ScriptableObjectEditor : ScriptedEditor
{
    ScriptableObject? _scriptObject;

    public override void OnInspectorGUI()
    {
        var metaFile = target as MetaFile ?? throw new Exception();
        // var importer = (ScriptableObjectImporter)metaFile.importer;

        try
        {
            bool changed = false;

            _scriptObject ??= Serializer.Deserialize<ScriptableObject>(StringTagConverter.ReadFromFile(metaFile.AssetPath));

            object t = _scriptObject ?? throw new Exception();
            changed |= PropertyGrid("CompPropertyGrid", ref t, TargetFields.Serializable | TargetFields.Properties, PropertyGridConfig.NoHeader | PropertyGridConfig.NoBorder | PropertyGridConfig.NoBackground);

            // Draw any Buttons
            //changed |= EditorGui.HandleAttributeButtons(scriptObject);

            if (changed)
            {
                _scriptObject.OnValidate();
                StringTagConverter.WriteToFile(Serializer.Serialize(_scriptObject), metaFile.AssetPath);
                AssetDatabase.Reimport(metaFile.AssetPath);
            }
        }
        catch (Exception e)
        {
            double itemSize = EditorStylePrefs.Instance.ItemSize;
            gui.Node("DummyForText").ExpandWidth().Height(itemSize * 10);
            gui.Draw2D.DrawText("Failed to Deserialize ScriptableObject, The ScriptableObject file is invalid. Error: " + e.ToString(), gui.CurrentNode.LayoutData.Rect);
        }
    }

}
