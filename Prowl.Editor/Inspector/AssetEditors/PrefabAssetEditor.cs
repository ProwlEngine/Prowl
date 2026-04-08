using System.IO;

using Prowl.Editor.Prefabs;
using Prowl.Editor.Widgets;
using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Inspector;

[CustomAssetEditor(typeof(PrefabAsset))]
public class PrefabAssetEditor : AssetImporterEditor
{
    public override void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;
        var prefab = asset as PrefabAsset;
        if (prefab == null) return;

        EditorGUI.Header(paper, $"{id}_hdr", $"{EditorIcons.Cubes}  Prefab: {prefab.Name}");
        EditorGUI.Separator(paper, $"{id}_sep");

        // Info
        if (prefab.GameObjectData != null)
        {
            EditorGUI.Label(paper, $"{id}_info", "Contains a serialized GameObject hierarchy.");
        }
        else
        {
            EditorGUI.Label(paper, $"{id}_empty", "Empty prefab (no data).");
        }

        paper.Box($"{id}_sp").Height(8);

        // Open in editing mode
        EditorGUI.Button(paper, $"{id}_edit", $"{EditorIcons.PenToSquare}  Open Prefab", width: 140)
            .OnValueChanged(_ => PrefabEditingMode.Enter(entry.Guid));
    }
}
