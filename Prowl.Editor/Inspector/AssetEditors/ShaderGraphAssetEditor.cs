using Prowl.Editor.GraphTools.ShaderGraphs.Editors;
using Prowl.Editor.Widgets;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Runtime.GraphTools.ShaderGraphs;

namespace Prowl.Editor.Inspector;

/// <summary>
/// Inspector for <see cref="ShaderGraph"/> assets quick stats + a button to open the
/// shader-graph editor window. The actual editing experience lives in
/// <see cref="ShaderGraphEditorWindow"/>.
/// </summary>
[CustomAssetEditor(typeof(ShaderGraph))]
public class ShaderGraphAssetEditor : AssetImporterEditor
{
    public override void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset)
    {
        Origami.Header(paper, $"{id}_hdr", $"{EditorIcons.DiagramProject}  Shader Graph").Show();

        if (asset is not ShaderGraph graph)
        {
            Origami.Label(paper, $"{id}_noasset", "Asset failed to load.").Show();
            return;
        }

        Origami.Label(paper, $"{id}_path", $"Path: {entry.Path}").Show();
        Origami.Label(paper, $"{id}_nodes", $"Nodes: {graph.Nodes.Count}").Show();
        Origami.Label(paper, $"{id}_edges", $"Edges: {graph.Edges.Count}").Show();
        Origami.Label(paper, $"{id}_vars", $"Variables: {graph.Blackboard.Count}").Show();

        paper.Box($"{id}_sp").Height(8);

        Origami.Button(paper, $"{id}_open", $"{EditorIcons.PenToSquare}  Open Editor", () => { ShaderGraphEditorWindow.OpenFor(graph); }).Width(160).Show();
    }
}
