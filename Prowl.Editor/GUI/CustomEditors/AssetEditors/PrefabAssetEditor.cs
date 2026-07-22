using Prowl.Editor.GUI;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Inspector;

[CustomAssetEditor(typeof(PrefabAsset))]
public class PrefabAssetEditor : AssetImporterEditor
{
    private readonly PreviewWidget _preview = new(showGrid: true);

    public override void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset)
    {
        var prefab = asset as PrefabAsset;
        if (prefab == null) return;

        Origami.Header(paper, $"{id}_hdr", $"{EditorIcons.Cubes}  Prefab: {prefab.Name}").Underline().Show();

        if (prefab.GameObjectData != null)
            Origami.Label(paper, $"{id}_info", "Contains a serialized GameObject hierarchy.").Show();
        else
            Origami.Label(paper, $"{id}_empty", "Empty prefab (no data).").Show();

        paper.Box($"{id}_sp").Height(8);

        Origami.Button(paper, $"{id}_edit", $"{EditorIcons.PenToSquare}  Open Prefab", () => { PrefabEditingMode.Enter(entry.Guid); }).Width(140).Show();

        if (prefab.GameObjectData != null)
        {
            Origami.Header(paper, $"{id}_h_preview", "Preview").Underline().Show();
            _preview.Get(prefab, p => p.SetupForPrefab(prefab)).DrawPreview(paper, $"{id}_preview", 256, 256);
        }
    }
}
