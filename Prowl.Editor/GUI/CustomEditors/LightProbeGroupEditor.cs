// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Vector;

namespace Prowl.Editor.Inspector;

/// <summary>
/// Inspector for <see cref="LightProbeGroup"/>: shows the probe count and gives quick tools to
/// populate the group (generate a grid, add a probe, clear). Probes themselves are visualised in
/// the scene view by the component's gizmos.
/// </summary>
[CustomEditor(typeof(LightProbeGroup))]
public class LightProbeGroupEditor : CustomEditor
{
    // Shared across the (type-cached) editor instance; transient grid-gen parameters.
    private Float3 _min = new(-5f, 0f, -5f);
    private Float3 _max = new(5f, 5f, 5f);
    private int _countX = 4, _countY = 3, _countZ = 4;

    public override void OnGUI(Paper paper, string id, object target)
    {
        var grp = (LightProbeGroup)target;
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        paper.Box($"{id}_count").Height(EditorTheme.RowHeight)
            .Text($"{grp.ProbePositions.Count} probe(s)", font)
            .TextColor(EditorTheme.Ink300).FontSize(EditorTheme.FontSize)
            .Alignment(TextAlignment.MiddleLeft);

        Origami.Header(paper, $"{id}_h_grid", "Generate Grid").Underline().Show();

        PropertyGridUtils.DrawField(paper, $"{id}_min", "Min (local)", typeof(Float3), _min,
            v => _min = (Float3)v!, 0);
        PropertyGridUtils.DrawField(paper, $"{id}_max", "Max (local)", typeof(Float3), _max,
            v => _max = (Float3)v!, 0);

        EditorGUI.Row(paper, $"{id}_cx", "Count X", () =>
            Origami.IntSlider(paper, $"{id}_cx_v", _countX, v => _countX = v, 1, 32).Show());
        EditorGUI.Row(paper, $"{id}_cy", "Count Y", () =>
            Origami.IntSlider(paper, $"{id}_cy_v", _countY, v => _countY = v, 1, 32).Show());
        EditorGUI.Row(paper, $"{id}_cz", "Count Z", () =>
            Origami.IntSlider(paper, $"{id}_cz_v", _countZ, v => _countZ = v, 1, 32).Show());

        Origami.Button(paper, $"{id}_gen", $"{EditorIcons.Sun}  Generate Grid", () =>
        {
            Undo.Snapshot(grp);
            grp.GenerateGrid(_min, _max, _countX, _countY, _countZ);
            EditorSceneManager.MarkDirty();
        }).Show();

        paper.Box($"{id}_sp").Height(4);

        Origami.Button(paper, $"{id}_add", "Add Probe", () =>
        {
            Undo.Snapshot(grp);
            grp.ProbePositions.Add(Float3.Zero);
            EditorSceneManager.MarkDirty();
        }).Show();

        if (grp.ProbePositions.Count > 0)
            Origami.Button(paper, $"{id}_clear", $"{EditorIcons.Trash}  Clear Probes", () =>
            {
                Undo.Snapshot(grp);
                grp.ProbePositions.Clear();
                EditorSceneManager.MarkDirty();
            }).Show();

        paper.Box($"{id}_sp2").Height(6);

        // Raw list for fine editing of individual positions.
        DrawDefaultInspector(paper, $"{id}_def", target);
    }
}
