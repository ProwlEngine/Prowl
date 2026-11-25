// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Editor.GUI;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.Importers;
using Prowl.Editor.Projects;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Rosetta;
using Prowl.Runtime;

namespace Prowl.Editor.Inspector;

/// <summary>The import settings of a <c>.pmesh</c> model, and the way into the model world.</summary>
[CustomAssetEditor(typeof(PMeshImporter))]
public class PMeshAssetEditor : ImportSettingsEditor
{
    public override void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset)
    {
        id = $"{id}_{entry.Guid:N}";
        EchoObject settings = Settings(entry);

        Origami.Button(paper, $"{id}_open", $"{EditorIcons.PenToSquare}  {Loc.Get("model.open_model")}", () => ModelEditingMode.Open(entry.Guid))
            .Primary().Height(30).Show();

        EditorGUI.SectionHeader(paper, $"{id}_h_import", Loc.Get("model.import"), first: false);

        var collider = (PMeshCollider)(settings.TryGet(PMeshImporter.Keys.Collider, out EchoObject? c) ? c!.IntValue : 0);
        EditorGUI.Row(paper, $"{id}_collider", Loc.Get("model.collider"), () =>
            Origami.EnumDropdown(paper, $"{id}_collider_v", collider,
                v => settings[PMeshImporter.Keys.Collider] = new EchoObject((int)v)).Show());

        bool uvs = settings.TryGet(PMeshImporter.Keys.LightmapUVs, out EchoObject? l) && l!.BoolValue;
        EditorGUI.SettingsToggle(paper, $"{id}_uv2", Loc.Get("model.lightmap_uvs"), uvs,
            v => settings[PMeshImporter.Keys.LightmapUVs] = new EchoObject(v), separator: false);

        DrawApplyRevertBar(paper, id, entry, asset);
    }
}
