using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Editor.Build;
using Prowl.Editor.GUI;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.Inspector;
using Prowl.Editor.Theming;
using Prowl.Editor.Utils;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime;

namespace Prowl.Editor.Projects.Settings;

public enum BuildTarget { Windows, Linux, MacOS }
public enum BuildConfiguration { Debug, Release }
public enum AssetExportMode { AllAssets, DependenciesOnly }

public class SceneBuildEntry
{
    public string Path = "";
    public Guid SceneGuid;
    public bool Enabled = true;
}

/// <summary> Project settings for configuring builds, including scenes, build configuration, output directory, asset packaging, and platform profiles. </summary>
[ProjectSettings("Build", EditorIcons.Hammer, order: 50, exportToBuild: false)]
public sealed class BuildSettings : ProjectSettingsBase
{
    public override bool DrawInProjectSettingsPanel => false;

    public List<SceneBuildEntry> Scenes = [];

    public BuildConfiguration Config = BuildConfiguration.Release;
    public string OutputDirectory = "Builds";
    public AssetPackagingMode PackagingMode = AssetPackagingMode.ProwlPak;
    public AssetExportMode AssetMode = AssetExportMode.DependenciesOnly;
    public int MaxPakSizeMB = 2048;

    /// <summary>Full name of the <see cref="BuildPipeline"/> to build with. Empty means desktop.</summary>
    public string SelectedPipeline = "";

    /// <summary>
    /// Per-platform profiles.  Missing entries will be created with
    /// defaults on first access.
    /// </summary>
    public List<PlatformBuildProfile> PlatformProfiles = [];

    /// <summary>
    /// Returns (or lazily creates) the profile for the given target.
    /// </summary>
    public T GetProfile<T>(Type pipelineType) where T : PlatformBuildProfile
    {
        PlatformBuildProfile? profile = PlatformProfiles.FirstOrDefault(profile => pipelineType == profile.GetPipelineType());
        if (profile == null)
        {
            profile = System.Activator.CreateInstance<T>();
            PlatformProfiles.Add(profile);
        }
        return (T)profile;
    }

    /// <summary>
    /// Returns or creates the profile for the given target.
    /// If not present, returns null.
    /// </summary>
    public PlatformBuildProfile GetOrCreateProfile(Type pipelineType)
    {
        PlatformBuildProfile? profile = PlatformProfiles.FirstOrDefault(profile => pipelineType == profile.GetPipelineType());

        // If the profile is null, create the profile
        if (profile == null)
        {
            Type targetType = null;
            foreach (Type type in EditorUtils.GetAllTypes())
            {
                if (!type.IsSubclassOf(typeof(PlatformBuildProfile)) || type == typeof(PlatformBuildProfile)) continue;
                var check = Activator.CreateInstance(type) as PlatformBuildProfile;
                if (check?.GetPipelineType() == pipelineType)
                {
                    targetType = type;
                    break;
                }
            }

            if (targetType != null)
            {
                profile = (PlatformBuildProfile)System.Activator.CreateInstance(targetType);
                PlatformProfiles.Add(profile);
                EditorRegistries.SaveSettings();
            }
        }
        return profile;
    }

    /// <summary> Draws the build settings UI in the project settings panel. </summary>
    public override void OnGUI(Paper paper, float width)
    {
        Scribe.FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;

        // Scene List
        Origami.Header(paper, "bld_scenes_h", $"{EditorIcons.Shapes}  Scenes").Underline().Show();

        for (int i = 0; i < Scenes.Count; i++)
        {
            int idx = i;
            SceneBuildEntry scene = Scenes[i];

            using (paper.Row($"bld_scene_{i}").Height(EditorTheme.RowHeight).Gap(4).PaddingLeft(4).Enter())
            {
                // Index
                paper.Box($"bld_si_{i}")
                    .Width(20).Height(EditorTheme.RowHeight)
                    .Text(i.ToString(), font).TextColor(EditorTheme.Ink400)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter);

                // Enable toggle
                Origami.Checkbox(paper, $"bld_se_{i}", scene.Enabled,
                        v => { scene.Enabled = v; EditorRegistries.SaveSettings(); })
                    .NoLabel().Show();

                // Scene name
                string displayName = !string.IsNullOrEmpty(scene.Path)
                    ? System.IO.Path.GetFileNameWithoutExtension(scene.Path)
                    : scene.SceneGuid.ToString()[..8];

                paper.Box($"bld_sn_{i}")
                    .Height(EditorTheme.RowHeight).PaddingLeft(4)
                    .Text(displayName, font).TextColor(EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

                // Move up/down
                if (i > 0)
                {
                    Origami.IconButton(paper, $"bld_su_{i}", EditorIcons.ArrowUp_I, () =>
                        {
                            (Scenes[idx - 1], Scenes[idx]) = (Scenes[idx], Scenes[idx - 1]);
                            EditorRegistries.SaveSettings();
                        })
                        .Small().Width(EditorTheme.RowHeight).Height(EditorTheme.RowHeight).Ghost().Show();
                }

                // Remove
                Origami.IconButton(paper, $"bld_sr_{i}", EditorIcons.Xmark_I, () =>
                    {
                        Scenes.RemoveAt(idx);
                        EditorRegistries.SaveSettings();
                    })
                    .Small().Width(EditorTheme.RowHeight).Height(EditorTheme.RowHeight).Ghost().Show();
            }
        }

        // Add scene buttons
        using (paper.Row("bld_add_row").Height(EditorTheme.RowHeight).Gap(6).PaddingLeft(4).Enter())
        {
            Origami.Button(paper, "bld_add_open", $"{EditorIcons.Plus} Add Open Scene", () =>
                {
                    if (EditorSceneManager.CurrentScenePath != null)
                    {
                        EditorAssetBackend? db = EditorAssetBackend.Instance;
                        AssetEntry? entry = db?.GetEntry(EditorSceneManager.CurrentScenePath);
                        if (entry != null && !Scenes.Any(s => s.SceneGuid == entry.Guid))
                        {
                            Scenes.Add(new SceneBuildEntry { Path = entry.Path, SceneGuid = entry.Guid });
                            EditorRegistries.SaveSettings();
                        }
                    }
                }).Width(140).Show();
        }

        paper.Box("bld_sp1").Height(12);

        // Build Configuration
        Origami.Header(paper, "bld_config_h", $"{EditorIcons.Gear}  Configuration").Underline().Show();

        EditorGUI.SettingsEnumDropdown(paper, "bld_config", "Configuration", Config, v => Config = v);
        EditorGUI.SettingsTextField(paper, "bld_output", "Output Directory", OutputDirectory, v => OutputDirectory = v);
        EditorGUI.SettingsEnumDropdown(paper, "bld_packaging", "Asset Packaging", PackagingMode, v => PackagingMode = v);
        EditorGUI.SettingsEnumDropdown(paper, "bld_assetmode", "Asset Export", AssetMode, v => AssetMode = v);

        if (PackagingMode == AssetPackagingMode.ProwlPak)
            EditorGUI.SettingsIntSlider(paper, "bld_maxpak", "Max Pak Size (MB)", MaxPakSizeMB, 256, 4096, v => MaxPakSizeMB = v);

        paper.Box("bld_sp3").Height(12);

    }

    public override void ResetToDefaults()
    {
        Scenes.Clear();
        PlatformProfiles.ForEach(p => p.ToDefault());
    }
}
