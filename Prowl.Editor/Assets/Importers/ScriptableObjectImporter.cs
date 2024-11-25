// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Editor.Preferences;
using Prowl.Runtime;
using Prowl.Runtime.Utils;

namespace Prowl.Editor.Assets;

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

[CustomEditor(typeof(ScriptableObjectImporter))]
public class ScriptableObjectImporterEditor : ScriptedEditor
{
    private ScriptableObject? _editingObject;
    private ScriptedEditor? _objectEditor;

    public override void OnEnable()
    {
        EchoObject tag = StringTagConverter.ReadFromFile((target as MetaFile).AssetPath);
        _editingObject = Serializer.Deserialize<ScriptableObject>(tag);
        _objectEditor = null; // Replace this to load a Scripta
    }


    public override void OnInspectorGUI(EditorGUI.FieldChanges changes)
    {
        try
        {
            bool changed = false;

            object t = _editingObject;
            changed |= EditorGUI.PropertyGrid("CompPropertyGrid", ref t,
                EditorGUI.TargetFields.Serializable | EditorGUI.TargetFields.Properties,
                EditorGUI.PropertyGridConfig.NoHeader | EditorGUI.PropertyGridConfig.NoBorder | EditorGUI.PropertyGridConfig.NoBackground,
                changes);

            // Draw any Buttons
            //changed |= EditorGui.HandleAttributeButtons(scriptObject);

            if (changed)
            {
                _editingObject.OnValidate();
                StringTagConverter.WriteToFile(Serializer.Serialize(_editingObject), (target as MetaFile).AssetPath);
                AssetDatabase.Reimport((target as MetaFile).AssetPath);
            }
        }
        catch (Exception e)
        {
            double ItemSize = EditorStylePrefs.Instance.ItemSize;
            gui.Node("DummyForText").ExpandWidth().Height(ItemSize * 10);
            gui.Draw2D.DrawText("Failed to Deserialize ScriptableObject, The ScriptableObject file is invalid. Error: " + e.ToString(), gui.CurrentNode.LayoutData.Rect);
        }
    }

}
