using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Echo;
using Prowl.Editor.Widgets;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.MeshFeatures;
using Prowl.Runtime.MeshFeatures.Generation;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Editor.Inspector;

/// <summary>
/// Inspector for a <see cref="Mesh"/> — works for both standalone .mesh files and
/// mesh sub-assets inside a model. Shows mesh info, a preview with Shaded / SDF view
/// modes, and a Generate-SDF toolbar that edits the parent importer's settings and
/// triggers a reimport.
/// </summary>
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
        DrawFeaturePanel(paper, id, parentEntry, subEntry, mesh, state);

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

    private static void DrawFeaturePanel(Paper paper, string id, AssetEntry parentEntry, SubAssetEntry? subEntry, Mesh mesh, State state)
    {
        EditorGUI.Header(paper, $"{id}_h_feat", "Mesh Features");

        var sdf = FindSDF(parentEntry, subEntry, mesh);
        if (sdf != null)
        {
            EditorGUI.Label(paper, $"{id}_sdf_info",
                $"SDF: {sdf.Resolution.X}³  padding={sdf.Padding:F3}  maxDist={sdf.MaxDistance:F3}");

            using (paper.Row($"{id}_sdf_btn_row").Height(28).RowBetween(6).ChildLeft(4).ChildRight(4).Enter())
            {
                EditorGUI.ToggleButton(paper, $"{id}_view_shaded", "Shaded", state.Mode == ViewMode.Shaded, fitWidth: true)
                    .OnValueChanged(_ => { state.Mode = ViewMode.Shaded; state.LastPreviewSubject = null; });

                EditorGUI.ToggleButton(paper, $"{id}_view_sdf", "SDF Raymarch", state.Mode == ViewMode.SDFRaymarch, fitWidth: true)
                    .OnValueChanged(_ => { state.Mode = ViewMode.SDFRaymarch; state.LastPreviewSubject = null; });

                paper.Box($"{id}_sdf_btn_spacer");

                EditorGUI.Button(paper, $"{id}_sdf_regen", "Regenerate", width: 110)
                    .OnValueChanged(_ => ToggleSDF(parentEntry, true));

                EditorGUI.Button(paper, $"{id}_sdf_remove", "Remove", width: 90)
                    .OnValueChanged(_ => ToggleSDF(parentEntry, false));
            }
        }
        else
        {
            EditorGUI.Label(paper, $"{id}_sdf_info", "SDF: not generated");
            EditorGUI.Button(paper, $"{id}_sdf_gen", $"{EditorIcons.WandMagicSparkles}  Generate SDF", width: 160)
                .OnValueChanged(_ => ToggleSDF(parentEntry, true));
        }
    }

    private static void DrawPreview(Paper paper, string id, AssetEntry parentEntry, SubAssetEntry? subEntry, Mesh mesh, State state)
    {
        EditorGUI.Header(paper, $"{id}_h_preview", "Preview");

        state.Preview ??= new PreviewRenderer(256, 256) { ShowGrid = state.Mode == ViewMode.Shaded };

        var sdf = FindSDF(parentEntry, subEntry, mesh);
        if (state.Mode == ViewMode.SDFRaymarch && sdf == null)
            state.Mode = ViewMode.Shaded;

        state.Preview.ShowGrid = state.Mode == ViewMode.Shaded;

        EngineObject? currentSubject = state.Mode == ViewMode.SDFRaymarch ? (EngineObject?)sdf : mesh;
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

    /// <summary>Toggle the SDF-enabled flag in the parent's .meta and reimport.</summary>
    private static void ToggleSDF(AssetEntry parentEntry, bool enabled)
    {
        if (Project.Current == null) return;
        string absPath = Path.Combine(Project.Current.AssetsPath, parentEntry.Path);
        string metaPath = MetaFile.GetMetaPath(absPath);

        MetaFileData meta;
        try { meta = File.Exists(metaPath) ? MetaFile.Read(metaPath) : MetaFile.CreateNew(parentEntry.ImporterType); }
        catch { meta = MetaFile.CreateNew(parentEntry.ImporterType); }

        var settings = meta.Settings ?? EchoObject.NewCompound();
        EchoObject sdf;
        if (settings.TryGet(SDFFeatureSpec.KeyRoot, out var existing) && existing != null)
        {
            sdf = existing;
        }
        else
        {
            sdf = EchoObject.NewCompound();
            sdf[SDFFeatureSpec.Key_Resolution] = new EchoObject(64);
            sdf[SDFFeatureSpec.Key_Padding] = new EchoObject(0.1f);
            sdf[SDFFeatureSpec.Key_MaxDistance] = new EchoObject(0.25f);
            settings[SDFFeatureSpec.KeyRoot] = sdf;
        }
        sdf[SDFFeatureSpec.Key_Enabled] = new EchoObject(enabled);
        meta.Settings = settings;

        MetaFile.Write(metaPath, meta);
        EditorAssetDatabase.Instance?.Reimport(parentEntry.Guid);

        // Clear cached preview state so the new SDF is picked up.
        if (_subAssetStates.TryGetValue(parentEntry.Guid, out var st))
            st.LastPreviewSubject = null;
    }
}
