// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

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
    ScriptableObject? scriptObject;

    public override void OnInspectorGUI()
    {
        var importer = (ScriptableObjectImporter)(target as MetaFile).importer;

        try
        {
            bool changed = false;

            scriptObject ??= Serializer.Deserialize<ScriptableObject>(StringTagConverter.ReadFromFile((target as MetaFile).AssetPath));

            object t = scriptObject;
            changed |= EditorGUI.PropertyGrid("CompPropertyGrid", ref t,
                EditorGUI.TargetFields.Serializable | EditorGUI.TargetFields.Properties,
                EditorGUI.PropertyGridConfig.NoHeader | EditorGUI.PropertyGridConfig.NoBorder | EditorGUI.PropertyGridConfig.NoBackground);

            // Draw any Buttons
            //changed |= EditorGui.HandleAttributeButtons(scriptObject);

            if (changed)
            {
                scriptObject.OnValidate();
                StringTagConverter.WriteToFile(Serializer.Serialize(scriptObject), (target as MetaFile).AssetPath);
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
