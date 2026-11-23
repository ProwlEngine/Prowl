// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Rosetta;
using Prowl.Runtime.CSG;

namespace Prowl.Editor.Inspector;

/// <summary>Inspector for <see cref="CSGModel"/>: its settings, what it built, and the rebuilds it can be asked for.</summary>
[CustomEditor(typeof(CSGModel))]
public class CSGModelEditor : CustomEditor
{
    public override void OnGUI(Paper paper, string id, object target)
    {
        var model = (CSGModel)target;

        DrawDefaultInspector(paper, $"{id}_def", target);

        paper.Box($"{id}_sp").Height(6);
        Origami.Label(paper, $"{id}_stats", Loc.Get("csg.stats", new
        {
            brushes = model.BrushCount,
            vertices = model.VertexCount,
            triangles = model.TriangleCount,
        })).Show();

        paper.Box($"{id}_sp2").Height(4);
        Origami.Button(paper, $"{id}_rebuild", Loc.Get("csg.rebuild"), model.Rebuild).Tooltip(Loc.Get("csg.rebuild_tip")).Show();

        if (!model.GenerateLightmapUVs || !model.LightmapUVsStale) return;

        paper.Box($"{id}_sp3").Height(4);
        Origami.Button(paper, $"{id}_uvs", Loc.Get("csg.lightmap_rebuild_uvs"), () =>
        {
            Undo.RecordObject(model, "Rebuild Lightmap UVs");
            model.RebuildLightmapUVs();
        }).Tooltip(Loc.Get("csg.lightmap_rebuild_tip")).Show();
    }
}
