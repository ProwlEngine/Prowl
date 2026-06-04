using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime;

using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.Editor.Theming;

namespace Prowl.Editor.Inspector;

// ================================================================
//  Skinned Mesh Renderer Component Editor
//  Draws the default fields, then a "Blend Shapes" section with a
//  0-100 weight slider per blend shape on the shared mesh.
// ================================================================

[CustomEditor(typeof(SkinnedMeshRenderer))]
public class SkinnedMeshRendererEditor : CustomEditor
{
    public override void OnGUI(Paper paper, string id, object target)
    {
        var renderer = (SkinnedMeshRenderer)target;
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        // Snapshot the component state before any widget (default fields or sliders) mutates it.
        Undo.Snapshot(renderer);

        // Default inspector (mesh, materials, bone paths, etc.).
        DrawDefaultInspector(paper, id, renderer);

        var mesh = renderer.SharedMesh.Res;
        int count = mesh?.BlendShapeCount ?? 0;
        if (mesh == null || count == 0)
            return;

        Origami.Separator(paper, $"{id}_bs_sep").Show();
        Origami.Header(paper, $"{id}_bs_header", $"Blend Shapes ({count})").Underline().Show();

        for (int i = 0; i < count; i++)
        {
            int index = i; // capture for the closure
            string name = mesh.GetBlendShapeName(index);
            if (string.IsNullOrEmpty(name)) name = $"BlendShape {index}";
            float weight = renderer.GetBlendShapeWeight(index);

            InspectorRow.Draw(paper, $"{id}_bs_{index}", name, () =>
                Origami.Slider(paper, $"{id}_bs_{index}_v", weight,
                    v => renderer.SetBlendShapeWeight(index, v),
                    0f, 100f).Format("F1").Show());
        }

        Origami.Button(paper, $"{id}_bs_reset", "Reset Weights", () =>
        {
            for (int i = 0; i < count; i++)
                renderer.SetBlendShapeWeight(i, 0f);
        }).Show();
    }
}
