using System;
using System.Collections.Generic;

using Prowl.Editor.GUI;
using static Prowl.Editor.GUI.EditorGUI;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.MeshFeatures;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Inspector;

/// <summary>
/// Read-only inspector for a <see cref="Mesh"/>. Shows mesh info, the list of generated
/// features, and a preview with view-mode switching (Shaded vs SDF Raymarch when an SDF
/// feature exists).
/// </summary>
/// <remarks>
/// All mesh features (SDF, BVH, Prism, ...) are produced by the parent asset's importer
/// from a single set of importer settings. To enable/configure them, edit the parent
/// asset (e.g. the Model) never this view.
/// </remarks>
[CustomAssetEditor(typeof(Mesh))]
public class MeshAssetEditor : AssetImporterEditor
{
    private sealed class State
    {
        public PreviewRenderer? Preview;
        public EngineObject? LastPreviewSubject;
    }

    private readonly State _ownState = new();

    private static readonly Dictionary<Guid, State> _subAssetStates = new();

    public override void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset)
    {
        Draw(paper, id, entry, subEntry: null, asset as Mesh, _ownState);
    }

    /// <summary>
    /// Inspector entry point used from <c>InspectorPanel.DrawSubAssetInspector</c> when a
    /// Mesh sub-asset (e.g. inside a Model) is selected.
    /// </summary>
    public static void DrawForSubAsset(Paper paper, string id, AssetEntry parentEntry, SubAssetEntry subEntry, Mesh mesh)
    {
        if (!_subAssetStates.TryGetValue(subEntry.Guid, out var state))
            _subAssetStates[subEntry.Guid] = state = new State();
        Draw(paper, id, parentEntry, subEntry, mesh, state);
    }

    private static void Draw(Paper paper, string id, AssetEntry parentEntry, SubAssetEntry? subEntry, Mesh? mesh, State state)
    {
        id = $"{id}_{parentEntry.Guid:N}";
        if (subEntry != null) id = $"{id}_{subEntry.Guid:N}";

        var font = EditorTheme.DefaultFont;
        if (font == null) return;
        var m = Origami.Current.Metrics;

        if (mesh == null)
        {
            EditorGUI.SectionHeader(paper, $"{id}_h_info", "Mesh", first: true);
            paper.Box($"{id}_noasset").Height(m.RowHeight)
                .Margin(m.PaddingLarge, m.PaddingLarge, 0, 0).IsNotInteractable()
                .Text("Mesh asset failed to load.", font).TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
            return;
        }

        DrawPreview(paper, id, parentEntry, subEntry, mesh, state, m);
        DrawStatChips(paper, id, mesh, font, m);
        DrawDetails(paper, id, mesh, font, m);
        DrawFeaturePanel(paper, id, parentEntry, subEntry, mesh, font, m);
    }

    private static void DrawPreview(Paper paper, string id, AssetEntry parentEntry, SubAssetEntry? subEntry, Mesh mesh, State state, OrigamiMetrics m)
    {
        state.Preview ??= new PreviewRenderer(256, 256);
        state.Preview.ShowGrid = true;

        if (state.LastPreviewSubject != mesh)
        {
            state.LastPreviewSubject = mesh;
            state.Preview.SetupForMesh(mesh);
        }

        // Preview hero card wraps the 3D orbit preview in themed chrome.
        using (paper.Box($"{id}_previewCard").Height(200)
            .Margin(m.PaddingLarge, m.PaddingLarge, m.PaddingLarge, m.Spacing)
            .Rounded(8).Clip()
            .BackgroundColor(EditorTheme.Neutral300)
            .BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
            .ChildLeft().ChildRight().ChildTop().ChildBottom().Enter())
        {
            state.Preview.DrawPreview(paper, $"{id}_preview_rt", 184, 184);
        }
    }

    private static void DrawStatChips(Paper paper, string id, Mesh mesh, Prowl.Scribe.FontFile font, OrigamiMetrics m)
    {
        int verts = mesh.Vertices?.Length ?? 0;
        int tris = (mesh.Indices?.Length ?? 0) / 3;
        var size = mesh.bounds.Max - mesh.bounds.Min;

        using (paper.Row($"{id}_stats").Height(UnitValue.Auto)
            .Margin(m.PaddingLarge, m.PaddingLarge, 0, m.SpacingLarge).RowBetween(m.SpacingMedium).Enter())
        {
            EditorGUI.StatChip(paper, $"{id}_st_verts", $"{verts:N0} Verts", font);
            EditorGUI.StatChip(paper, $"{id}_st_tris", $"{tris:N0} Tris", font);
            EditorGUI.StatChip(paper, $"{id}_st_sub", $"{mesh.SubMeshCount} Sub-Meshes", font);
            EditorGUI.StatChip(paper, $"{id}_st_bounds", $"{size.X:F2} x {size.Y:F2} x {size.Z:F2}", font);
            paper.Box($"{id}_st_pad").Height(1).IsNotInteractable();
        }
    }

    private static void DrawDetails(Paper paper, string id, Mesh mesh, Prowl.Scribe.FontFile font, OrigamiMetrics m)
    {
        EditorGUI.SectionHeader(paper, $"{id}_h_details", "Details");

        ValueRow(paper, id, "_idx", "Index Format", mesh.IndexFormat.ToString(), font, m);

        var attrs = "";
        if (mesh.HasNormals) attrs += "Normals ";
        if (mesh.HasTangents) attrs += "Tangents ";
        if (mesh.HasUV) attrs += "UV ";
        if (mesh.HasUV2) attrs += "UV2 ";
        if (mesh.HasColors || mesh.HasColors32) attrs += "Colors ";
        if (mesh.HasBoneIndices) attrs += "Bones ";
        if (attrs.Length == 0) attrs = "(positions only)";
        ValueRow(paper, id, "_attrs", "Attributes", attrs.TrimEnd(), font, m);
    }

    private static void DrawFeaturePanel(Paper paper, string id, AssetEntry parentEntry, SubAssetEntry? subEntry, Mesh mesh, Prowl.Scribe.FontFile font, OrigamiMetrics m)
    {
        EditorGUI.SectionHeader(paper, $"{id}_h_feat", "Mesh Features");

        var sdf = FindSDF(parentEntry, subEntry, mesh);
        string sdfText = sdf != null
            ? $"{sdf.Resolution.X}^3  padding={sdf.Padding:F3}  maxDist={sdf.MaxDistance:F3}"
            : "Not generated (toggle on the parent asset)";
        ValueRow(paper, id, "_sdf", "SDF", sdfText, font, m);
    }

    private static void ValueRow(Paper paper, string id, string key, string label, string value, Prowl.Scribe.FontFile font, OrigamiMetrics m)
    {
        EditorGUI.Row(paper, $"{id}{key}", label, () =>
            paper.Box($"{id}{key}_v").Height(m.RowHeight).IsNotInteractable()
                .Text(value, font).TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).TextTruncate());
    }


    private static MeshSDF? FindSDF(AssetEntry parentEntry, SubAssetEntry? subEntry, Mesh mesh)
    {
        string meshName = subEntry?.Name ?? mesh.Name ?? "Mesh";
        var sdfGuid = AssetEntry.DeriveSubAssetGuid(parentEntry.Guid, $"{meshName}_sdf");
        return Runtime.AssetDatabase.Get(sdfGuid) as MeshSDF;
    }

    /// <summary>
    /// Called by parent-asset editors after they trigger a reimport, so cached previews
    /// drop their stale references and re-bind to the freshly generated sub-assets.
    /// </summary>
    internal static void InvalidateCachedPreviews()
    {
        foreach (var s in _subAssetStates.Values)
            s.LastPreviewSubject = null;
    }
}
