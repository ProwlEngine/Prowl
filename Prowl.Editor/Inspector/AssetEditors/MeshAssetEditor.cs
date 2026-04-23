using System;
using System.Collections.Generic;

using Prowl.Editor.Widgets;
using Prowl.PaperUI;
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
    public enum ViewMode { Shaded, SDFRaymarch }

    private sealed class State
    {
        public PreviewRenderer? Preview;
        public EngineObject? LastPreviewSubject;
        public ViewMode Mode;
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

        EditorGUI.Header(paper, $"{id}_h_info", subEntry != null ? $"Mesh: {subEntry.Name}" : "Mesh");

        if (mesh == null)
        {
            EditorGUI.Label(paper, $"{id}_noasset", "Mesh asset failed to load.");
            return;
        }

        DrawInfoPanel(paper, id, mesh);

        EditorGUI.Separator(paper, $"{id}_sep_feat");
        DrawFeaturePanel(paper, id, parentEntry, subEntry, mesh);

        EditorGUI.Separator(paper, $"{id}_sep_preview");
        DrawPreview(paper, id, parentEntry, subEntry, mesh, state);
    }

    private static void DrawInfoPanel(Paper paper, string id, Mesh mesh)
    {
        int verts = mesh.Vertices?.Length ?? 0;
        int tris = (mesh.Indices?.Length ?? 0) / 3;
        var size = mesh.bounds.Max - mesh.bounds.Min;

        EditorGUI.Label(paper, $"{id}_verts", $"Vertices: {verts:N0}");
        EditorGUI.Label(paper, $"{id}_tris", $"Triangles: {tris:N0}");
        EditorGUI.Label(paper, $"{id}_sub", $"Sub-Meshes: {mesh.SubMeshCount}");
        EditorGUI.Label(paper, $"{id}_bounds", $"Bounds: {size.X:F3}, {size.Y:F3}, {size.Z:F3}");
        EditorGUI.Label(paper, $"{id}_fmt", $"Index Format: {mesh.IndexFormat}");

        var attrs = "";
        if (mesh.HasNormals) attrs += "Normals ";
        if (mesh.HasTangents) attrs += "Tangents ";
        if (mesh.HasUV) attrs += "UV ";
        if (mesh.HasUV2) attrs += "UV2 ";
        if (mesh.HasColors || mesh.HasColors32) attrs += "Colors ";
        if (mesh.HasBoneIndices) attrs += "Bones ";
        if (attrs.Length == 0) attrs = "(positions only)";
        EditorGUI.Label(paper, $"{id}_attrs", $"Attributes: {attrs.TrimEnd()}");
    }

    private static void DrawFeaturePanel(Paper paper, string id, AssetEntry parentEntry, SubAssetEntry? subEntry, Mesh mesh)
    {
        EditorGUI.Header(paper, $"{id}_h_feat", "Mesh Features");

        var sdf = FindSDF(parentEntry, subEntry, mesh);
        if (sdf != null)
            EditorGUI.Label(paper, $"{id}_sdf_info",
                $"SDF: {sdf.Resolution.X}³  padding={sdf.Padding:F3}  maxDist={sdf.MaxDistance:F3}");
        else
            EditorGUI.Label(paper, $"{id}_sdf_info", "SDF: not generated  (toggle on the parent asset to enable)");
    }

    private static void DrawPreview(Paper paper, string id, AssetEntry parentEntry, SubAssetEntry? subEntry, Mesh mesh, State state)
    {
        var sdf = FindSDF(parentEntry, subEntry, mesh);

        using (paper.Row($"{id}_preview_header").Height(28).RowBetween(6).ChildLeft(4).ChildRight(4).Enter())
        {
            EditorGUI.Header(paper, $"{id}_h_preview", "Preview");

            EditorGUI.ToggleButton(paper, $"{id}_view_shaded", "Shaded", state.Mode == ViewMode.Shaded, fitWidth: true)
                .OnValueChanged(_ => { state.Mode = ViewMode.Shaded; state.LastPreviewSubject = null; });

            // Only offer the SDF view when an SDF actually exists.
            if (sdf != null)
                EditorGUI.ToggleButton(paper, $"{id}_view_sdf", "SDF", state.Mode == ViewMode.SDFRaymarch, fitWidth: true)
                    .OnValueChanged(_ => { state.Mode = ViewMode.SDFRaymarch; state.LastPreviewSubject = null; });
        }

        if (state.Mode == ViewMode.SDFRaymarch && sdf == null)
            state.Mode = ViewMode.Shaded;

        state.Preview ??= new PreviewRenderer(256, 256);
        state.Preview.ShowGrid = state.Mode == ViewMode.Shaded;

        EngineObject? currentSubject = state.Mode == ViewMode.SDFRaymarch ? sdf : (EngineObject)mesh;
        if (state.LastPreviewSubject != currentSubject)
        {
            state.LastPreviewSubject = currentSubject;
            if (state.Mode == ViewMode.SDFRaymarch && sdf != null)
                state.Preview.SetupForMeshSDF(sdf);
            else
                state.Preview.SetupForMesh(mesh);
        }

        state.Preview.DrawPreview(paper, $"{id}_preview_rt", 256, 256);
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
