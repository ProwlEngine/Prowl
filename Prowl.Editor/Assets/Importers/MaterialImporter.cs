// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Preferences;
using Prowl.Editor.ScriptedEditors;
using Prowl.Runtime;
using Prowl.Runtime.Utils;

namespace Prowl.Editor.Assets;

[Importer("FileIcon.png", typeof(Material), ".mat")]
public class MaterialImporter : ScriptedImporter
{
    public override void Import(SerializedAsset ctx, FileInfo assetPath)
    {
        // Load the Texture into a TextureData Object and serialize to Asset Folder
        Material? mat;
        try
        {
            string json = File.ReadAllText(assetPath.FullName);
            var tag = StringTagConverter.Read(json);
            mat = Serializer.Deserialize<Material>(tag);
        }
        catch
        {
            // something went wrong, lets just create a new material and save it
            mat = new Material(Application.AssetProvider.LoadAsset<Shader>("Defaults/DefaultUnlit.shader"));
            string json = StringTagConverter.Write(Serializer.Serialize(mat));
            File.WriteAllText(assetPath.FullName, json);
        }

        ctx.SetMainObject(mat);
    }
}

[CustomEditor(typeof(MaterialImporter))]
public class MaterialImporterEditor : ScriptedEditor
{
    public override void OnInspectorGUI()
    {
        var importer = (MaterialImporter)(target as MetaFile).Importer;

        try
        {
            var tag = StringTagConverter.ReadFromFile((target as MetaFile).AssetPath);
            Material mat = Serializer.Deserialize<Material>(tag);

            MaterialEditor editor = new MaterialEditor(mat, () =>
            {
                StringTagConverter.WriteToFile(Serializer.Serialize(mat), (target as MetaFile).AssetPath);
                AssetDatabase.Reimport((target as MetaFile).AssetPath);
            });
            editor.OnInspectorGUI();
        }
        catch (Exception e)
        {
            gui.Node("DummyForText").ExpandWidth().Height(EditorStylePrefs.Instance.ItemSize * 10);
            gui.Draw2D.DrawText("Failed to Deserialize Material: " + e.Message + "\n" + e.StackTrace, gui.CurrentNode.LayoutData.Rect);
        }
    }
}
