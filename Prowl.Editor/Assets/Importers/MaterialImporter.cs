// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Editor.ScriptedEditors;
using Prowl.Runtime;
using Prowl.Runtime.Utils;

namespace Prowl.Editor.Assets;

[Importer("FileIcon.png", typeof(Material), ".mat")]
public class MaterialImporter : ScriptedImporter
{
    public override void Import(SerializedAsset ctx, FileInfo assetPath)
    {
        Material? mat;
        try
        {
            string json = File.ReadAllText(assetPath.FullName);
            var tag = EchoObject.ReadFromString(json);
            mat = Serializer.Deserialize<Material>(tag);
        }
        catch
        {
            // something went wrong, lets just create a new material and save it
            mat = Material.CreateDefaultMaterial();
            string json = Serializer.Serialize(mat).WriteToString();
            File.WriteAllText(assetPath.FullName, json);
        }

        ctx.SetMainObject(mat);
    }
}

[CustomEditor(typeof(MaterialImporter))]
public class MaterialImporterEditor : ScriptedEditor
{
    private Material? _editingMaterial;
    private ScriptedEditor? _editor;


    public override void OnEnable()
    {
        EchoObject tag = EchoObject.ReadFromString((target as MetaFile).AssetPath);
        _editingMaterial = Serializer.Deserialize<Material>(tag);

        _editor = CreateEditor(_editingMaterial);

        if (_editor is MaterialEditor matEditor)
            matEditor.onChange = OnChange;
    }


    private void OnChange()
    {
        Serializer.Serialize(_editingMaterial).WriteToString((target as MetaFile).AssetPath);
        AssetDatabase.Reimport((target as MetaFile).AssetPath);
    }


    public override void OnInspectorGUI(EditorGUI.FieldChanges changes)
    {
        _editor?.OnInspectorGUI(changes);
    }
}
