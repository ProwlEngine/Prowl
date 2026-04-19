using Prowl.Editor.GraphTools;
using Prowl.Editor.Widgets;
using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Runtime.GraphTools.ShaderGraphs;

namespace Prowl.Editor.Inspector;

/// <summary>
/// Inspector for <see cref="ShaderGraph"/> assets — quick stats + a button to open the
/// node-graph editor window. The actual editing experience lives in
/// <see cref="GraphEditorWindow"/>.
/// </summary>
[CustomAssetEditor(typeof(ShaderGraph))]
public class ShaderGraphAssetEditor : AssetImporterEditor
{
    public override void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset)
    {
        EditorGUI.Header(paper, $"{id}_hdr", $"{EditorIcons.DiagramProject}  Shader Graph");

        if (asset is not ShaderGraph graph)
        {
            EditorGUI.Label(paper, $"{id}_noasset", "Asset failed to load.");
            return;
        }

        EditorGUI.Label(paper, $"{id}_path", $"Path: {entry.Path}");
        EditorGUI.Label(paper, $"{id}_nodes", $"Nodes: {graph.Nodes.Count}");
        EditorGUI.Label(paper, $"{id}_edges", $"Edges: {graph.Edges.Count}");
        EditorGUI.Label(paper, $"{id}_vars", $"Variables: {graph.Blackboard.Count}");

        paper.Box($"{id}_sp").Height(8);

        EditorGUI.Button(paper, $"{id}_open", $"{EditorIcons.PenToSquare}  Open Editor", width: 160)
            .OnValueChanged(_ => GraphEditorWindow.OpenFor(graph));
    }
}
