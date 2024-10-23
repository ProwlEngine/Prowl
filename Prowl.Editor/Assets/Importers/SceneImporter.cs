// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Preferences;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.Utils;

namespace Prowl.Editor.Assets;

[Importer("HierarchyIcon.png", typeof(Scene), ".scene")]
public class SceneImporter : ScriptedImporter
{
    public override void Import(SerializedAsset ctx, FileInfo assetPath)
    {
        var tag = StringTagConverter.ReadFromFile(assetPath);
        Scene? scene = Serializer.Deserialize<Scene>(tag) ?? throw new Exception("Failed to Deserialize Scene.");
        ctx.SetMainObject(scene);
    }
}

[CustomEditor(typeof(SceneImporter))]
public class SceneEditor : ScriptedEditor
{
    SerializedAsset serialized;

    public override void OnEnable()
    {
        serialized = AssetDatabase.LoadAsset((target as MetaFile).AssetPath);
    }

    public override void OnDisable()
    {
    }

    public override void OnInspectorGUI(EditorGUI.FieldChanges changes)
    {
        double ItemSize = EditorStylePrefs.Instance.ItemSize;

        var importer = (SceneImporter)(target as MetaFile).importer;

        try
        {
            gui.CurrentNode.Layout(LayoutType.Column);

            var scene = (Scene)serialized.Main;
            if (EditorGUI.DrawProperty(0, "Build BVH", ref scene.BuildBVH))
            {
                changes.Add(scene, nameof(Scene.BuildBVH));
                AssetDatabase.SaveAsset(scene);
            }

        }
        catch
        {
            gui.TextNode("error", "Failed to display AudioClip Data").ExpandWidth().Height(ItemSize);
        }
    }
}
