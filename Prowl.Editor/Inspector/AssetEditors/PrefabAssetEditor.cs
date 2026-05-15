using System.IO;

using Prowl.Editor.Prefabs;
using Prowl.Editor.Widgets;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Inspector;

[CustomAssetEditor(typeof(PrefabAsset))]
public class PrefabAssetEditor : AssetImporterEditor
{
    private PreviewRenderer? _preview;
    private PrefabAsset? _lastPreviewAsset;

    public override void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;
        var prefab = asset as PrefabAsset;
        if (prefab == null) return;

        Origami.Header(paper, $"{id}_hdr", $"{EditorIcons.Cubes}  Prefab: {prefab.Name}").Underline().Show();

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
        Origami.Button(paper, $"{id}_edit", $"{EditorIcons.PenToSquare}  Open Prefab", () => { PrefabEditingMode.Enter(entry.Guid); }).Width(140).Show();

        // 3D Preview
        if (prefab.GameObjectData != null)
        {
                        Origami.Header(paper, $"{id}_h_preview", "Preview").Underline().Show();

            _preview ??= new PreviewRenderer(256, 256) { ShowGrid = true };

            if (_lastPreviewAsset != prefab)
            {
                _lastPreviewAsset = prefab;
                _preview.SetupForPrefab(prefab);
            }

            _preview.DrawPreview(paper, $"{id}_preview", 256, 256);
        }
    }
}
