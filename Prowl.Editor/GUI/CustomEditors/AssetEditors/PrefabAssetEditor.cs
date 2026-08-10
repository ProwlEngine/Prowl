using Prowl.Editor.GUI;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Inspector;

[CustomAssetEditor(typeof(PrefabAsset))]
public class PrefabAssetEditor : AssetImporterEditor
{

    public override void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset)
    {
        var prefab = asset as PrefabAsset;
        if (prefab == null) return;

        Origami.Header(paper, $"{id}_hdr", $"{EditorIcons.Cubes}  {Loc.Get("prefab.title", new { name = prefab.Name })}").Underline().Show();

        if (prefab.GameObjectData != null)
            Origami.Label(paper, $"{id}_info", Loc.Get("prefab.contains_hierarchy")).Show();
        else
            Origami.Label(paper, $"{id}_empty", Loc.Get("prefab.empty")).Show();

        // A generated prefab is rebuilt from its source file on every import, so opening it for
        // editing would offer to save changes that the next import would throw away.
        if (prefab.IsReadOnly)
            Origami.Label(paper, $"{id}_readonly", Loc.Get("prefab.read_only")).Show();

        paper.Box($"{id}_sp").Height(8);

        if (!prefab.IsReadOnly)
            Origami.Button(paper, $"{id}_edit", $"{EditorIcons.PenToSquare}  {Loc.Get("prefab.open")}",
                () => { PrefabEditingMode.Enter(entry.Guid); }).Width(140).Show();

        if (prefab.GameObjectData != null)
        {
            Origami.Header(paper, $"{id}_h_preview", Loc.Get("prefab.preview")).Underline().Show();
            PreviewWidget.For(entry.Guid, showGrid: true).Get(prefab, p => p.SetupForPrefab(prefab)).DrawPreview(paper, $"{id}_preview", 256, 256);
        }
    }
}
