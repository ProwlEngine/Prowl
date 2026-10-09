using System;

using Prowl.Editor.GUI;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Runtime.MeshFeatures.Generation;

namespace Prowl.Editor.Projects.Settings;

/// <summary>
/// How long assets nothing uses stay loaded, how much memory a built game lets loaded assets take before
/// freeing unused ones early, and how every mesh's signed distance field is built.
/// </summary>
[ProjectSettings("Assets", EditorIcons.Cubes, order: 30)]
public class AssetSettings : ProjectSettingsBase
{
    /// <summary>Seconds an asset nothing reaches stays loaded, so something used again soon never reloads.</summary>
    public float GracePeriodSeconds = 10f;

    /// <summary>Megabytes of loaded assets a built game allows before freeing unused ones early. Zero for no budget. The editor never has one.</summary>
    public int MemoryBudgetMB = 0;

    /// <summary>Whether every imported mesh gets a signed distance field.</summary>
    public bool GenerateMeshSDFs = false;

    /// <summary>Edge length of an SDF voxel in mesh units.</summary>
    public float SDFVoxelSize = SDFGenerator.Options.Default.VoxelSize;

    /// <summary>Most SDF voxels along any axis of a mesh.</summary>
    public int SDFMaxResolution = SDFGenerator.Options.Default.MaxResolution;

    public override void Apply()
    {
        AssetDatabase.GracePeriod = TimeSpan.FromSeconds(GracePeriodSeconds);
        SDFFeatureSpec.Enabled = GenerateMeshSDFs;
        SDFFeatureSpec.Options = SDFOptions;
    }

    private SDFGenerator.Options SDFOptions => new() { VoxelSize = SDFVoxelSize, MaxResolution = SDFMaxResolution };

    private bool SDFChanged => GenerateMeshSDFs != SDFFeatureSpec.Enabled
        || SDFVoxelSize != SDFFeatureSpec.Options.VoxelSize || SDFMaxResolution != SDFFeatureSpec.Options.MaxResolution;

    public override void ResetToDefaults()
    {
        GracePeriodSeconds = 10f;
        MemoryBudgetMB = 0;
        GenerateMeshSDFs = false;
        SDFVoxelSize = SDFGenerator.Options.Default.VoxelSize;
        SDFMaxResolution = SDFGenerator.Options.Default.MaxResolution;
    }

    public override void OnGUI(Paper paper, float width)
    {
        Origami.Header(paper, "assets_h_load", $"{EditorIcons.Cubes}  Loading").Underline().Show();

        EditorGUI.SettingsSliderField(paper, "assets_grace", "Unused Grace Period (s)", GracePeriodSeconds, 0f, 120f,
            v => { GracePeriodSeconds = v; AssetDatabase.GracePeriod = TimeSpan.FromSeconds(v); EditorRegistries.SaveSettings(); });

        EditorGUI.Row(paper, "assets_budget", "Player Memory Budget (MB)", () =>
            Origami.NumericField<int>(paper, "assets_budget_v", MemoryBudgetMB, v =>
            {
                MemoryBudgetMB = Math.Max(0, v);
                EditorRegistries.SaveSettings();
            }).Min(0).Show());

        Origami.Header(paper, "assets_h_sdf", $"{EditorIcons.Cubes}  Mesh Signed Distance Fields").Underline().Show();

        EditorGUI.SettingsToggle(paper, "assets_sdf", "Generate SDFs", GenerateMeshSDFs, v => GenerateMeshSDFs = v);

        if (GenerateMeshSDFs)
        {
            EditorGUI.Row(paper, "assets_sdf_voxel", "Voxel Size", () =>
                Origami.NumericField<float>(paper, "assets_sdf_voxel_v", SDFVoxelSize, v => SDFVoxelSize = MathF.Max(0.001f, v))
                    .Min(0.001f).Show());

            EditorGUI.Row(paper, "assets_sdf_max", "Max Resolution", () =>
                Origami.NumericField<int>(paper, "assets_sdf_max_v", SDFMaxResolution,
                    v => SDFMaxResolution = Math.Clamp(v, SDFGenerator.MinResolution, 256))
                    .Min(SDFGenerator.MinResolution).Max(256).Show());
        }

        // Every mesh reimports when these change, so they only take effect on request
        if (SDFChanged)
            Origami.Button(paper, "assets_sdf_apply", "Apply and Reimport Meshes", () =>
            {
                Apply();
                EditorRegistries.SaveSettings();
                EditorAssetBackend.Instance?.Refresh();
            }).Show();
    }
}
