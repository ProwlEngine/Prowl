using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Editor.Build;
using Prowl.Editor.Widgets;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;

using Color = System.Drawing.Color;

namespace Prowl.Editor;

public enum BuildTarget { Windows, Linux, MacOS }
public enum BuildConfiguration { Debug, Release }
public enum AssetExportMode { AllAssets, DependenciesOnly }

public class SceneBuildEntry
{
    public string Path { get; set; } = "";
    public Guid SceneGuid { get; set; }
    public bool Enabled { get; set; } = true;
}

[ProjectSettings("Build", EditorIcons.Hammer, order: 50, exportToBuild: false)]
public class BuildSettings : ProjectSettingsBase
{
    public List<SceneBuildEntry> Scenes { get; set; } = new();
    public BuildTarget Platform { get; set; } = BuildTarget.Windows;
    public BuildConfiguration Config { get; set; } = BuildConfiguration.Release;
    public string OutputDirectory { get; set; } = "Builds";
    public AssetPackagingMode PackagingMode { get; set; } = AssetPackagingMode.ProwlPak;
    public AssetExportMode AssetMode { get; set; } = AssetExportMode.DependenciesOnly;
    public int MaxPakSizeMB { get; set; } = 2048;
    public bool SelfContained { get; set; } = false;
    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 720;

    public string RuntimeIdentifier
    {
        get => Platform switch
        {
            BuildTarget.Linux => "linux-x64",
            BuildTarget.MacOS => "osx-x64",
            _ => "win-x64",
        };
    }

    public override void OnGUI(Paper paper, float width)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        // Scene List
        EditorGUI.Header(paper, "bld_scenes_h", $"{EditorIcons.Film}  Scenes");
        EditorGUI.Separator(paper, "bld_scenes_sep");

        for (int i = 0; i < Scenes.Count; i++)
        {
            int idx = i;
            var scene = Scenes[i];

            using (paper.Row($"bld_scene_{i}").Height(EditorTheme.RowHeight).RowBetween(4).ChildLeft(4).Enter())
            {
                // Index
                paper.Box($"bld_si_{i}")
                    .Width(20).Height(EditorTheme.RowHeight)
                    .Text(i.ToString(), font).TextColor(EditorTheme.Ink400)
                    .FontSize(EditorTheme.FontSize - 2).Alignment(TextAlignment.MiddleCenter);

                // Enable toggle
                EditorGUI.Toggle(paper, $"bld_se_{i}", "", scene.Enabled)
                    .OnValueChanged(v => { scene.Enabled = v; ProjectSettingsRegistry.SaveAll(); });

                // Scene name
                string displayName = !string.IsNullOrEmpty(scene.Path)
                    ? System.IO.Path.GetFileNameWithoutExtension(scene.Path)
                    : scene.SceneGuid.ToString()[..8];

                paper.Box($"bld_sn_{i}")
                    .Height(EditorTheme.RowHeight).ChildLeft(4)
                    .Text(displayName, font).TextColor(EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSize - 1).Alignment(TextAlignment.MiddleLeft);

                // Move up/down
                if (i > 0)
                {
                    paper.Box($"bld_su_{i}")
                        .Width(18).Height(EditorTheme.RowHeight).Rounded(3)
                        .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                        .Text(EditorIcons.ArrowUp, font).TextColor(EditorTheme.Ink400)
                        .FontSize(9f).Alignment(TextAlignment.MiddleCenter)
                        .OnClick(idx, (ci, _) =>
                        {
                            (Scenes[ci - 1], Scenes[ci]) = (Scenes[ci], Scenes[ci - 1]);
                            ProjectSettingsRegistry.SaveAll();
                        });
                }

                // Remove
                paper.Box($"bld_sr_{i}")
                    .Width(18).Height(EditorTheme.RowHeight).Rounded(3)
                    .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                    .Text(EditorIcons.Xmark, font).TextColor(EditorTheme.Ink400)
                    .FontSize(9f).Alignment(TextAlignment.MiddleCenter)
                    .OnClick(idx, (ci, _) =>
                    {
                        Scenes.RemoveAt(ci);
                        ProjectSettingsRegistry.SaveAll();
                    });
            }
        }

        // Add scene buttons
        using (paper.Row("bld_add_row").Height(EditorTheme.RowHeight).RowBetween(6).ChildLeft(4).Enter())
        {
            EditorGUI.Button(paper, "bld_add_open", $"{EditorIcons.Plus} Add Open Scene", width: 140)
                .OnValueChanged(_ =>
                {
                    if (EditorSceneManager.CurrentScenePath != null)
                    {
                        var db = EditorAssetDatabase.Instance;
                        var entry = db?.GetEntry(EditorSceneManager.CurrentScenePath);
                        if (entry != null && !Scenes.Any(s => s.SceneGuid == entry.Guid))
                        {
                            Scenes.Add(new SceneBuildEntry { Path = entry.Path, SceneGuid = entry.Guid });
                            ProjectSettingsRegistry.SaveAll();
                        }
                    }
                });
        }

        paper.Box("bld_sp1").Height(12);

        // Build Configuration
        EditorGUI.Header(paper, "bld_config_h", $"{EditorIcons.Gear}  Configuration");
        EditorGUI.Separator(paper, "bld_config_sep");

        EditorGUI.EnumDropdown(paper, "bld_platform", "Platform", Platform)
            .OnValueChanged(v => { Platform = v; ProjectSettingsRegistry.SaveAll(); });

        EditorGUI.EnumDropdown(paper, "bld_config", "Configuration", Config)
            .OnValueChanged(v => { Config = v; ProjectSettingsRegistry.SaveAll(); });

        EditorGUI.TextField(paper, "bld_output", "Output Directory", OutputDirectory)
            .OnValueChanged(v => { OutputDirectory = v; ProjectSettingsRegistry.SaveAll(); });

        EditorGUI.EnumDropdown(paper, "bld_packaging", "Asset Packaging", PackagingMode)
            .OnValueChanged(v => { PackagingMode = v; ProjectSettingsRegistry.SaveAll(); });

        EditorGUI.EnumDropdown(paper, "bld_assetmode", "Asset Export", AssetMode)
            .OnValueChanged(v => { AssetMode = v; ProjectSettingsRegistry.SaveAll(); });

        if (PackagingMode == AssetPackagingMode.ProwlPak)
        {
            EditorGUI.IntSlider(paper, "bld_maxpak", "Max Pak Size (MB)", MaxPakSizeMB, 256, 4096)
                .OnValueChanged(v => { MaxPakSizeMB = v; ProjectSettingsRegistry.SaveAll(); });
        }

        EditorGUI.Toggle(paper, "bld_selfcontained", "Self-Contained", SelfContained)
            .OnValueChanged(v => { SelfContained = v; ProjectSettingsRegistry.SaveAll(); });

        paper.Box("bld_sp2").Height(8);
        EditorGUI.Header(paper, "bld_window_h", "Window");
        EditorGUI.Separator(paper, "bld_window_sep");

        EditorGUI.IntField(paper, "bld_width", WindowWidth, "Width")
            .OnValueChanged(v => { WindowWidth = Math.Max(320, v); ProjectSettingsRegistry.SaveAll(); });

        EditorGUI.IntField(paper, "bld_height", WindowHeight, "Height")
            .OnValueChanged(v => { WindowHeight = Math.Max(240, v); ProjectSettingsRegistry.SaveAll(); });

        paper.Box("bld_sp3").Height(12);

        // Build buttons
        using (paper.Row("bld_buttons").Height(32).RowBetween(8).ChildLeft(4).Enter())
        {
            EditorGUI.Button(paper, "bld_build", $"{EditorIcons.Hammer}  Build", width: 120)
                .OnValueChanged(_ => StartBuild(false));

            EditorGUI.Button(paper, "bld_buildrun", $"{EditorIcons.Play}  Build & Run", width: 140)
                .OnValueChanged(_ => StartBuild(true));
        }
    }

    private static void StartBuild(bool andRun)
    {
        BuildSettings? settings;
        try { settings = ProjectSettingsRegistry.Get<BuildSettings>(); }
        catch { Runtime.Debug.LogError("BuildSettings not found."); return; }

        // Ask for output folder
        Widgets.FileDialog.Open(Widgets.FileDialogMode.SelectFolder, outputPath =>
        {
            if (string.IsNullOrEmpty(outputPath)) return;

            settings.OutputDirectory = outputPath;
            ProjectSettingsRegistry.SaveAll();

            var pipeline = new DesktopBuildPipeline();

            Runtime.Debug.Log($"[Build] Starting build to {outputPath}...");
            var result = pipeline.Build(settings, (stage, progress) =>
            {
                Runtime.Debug.Log($"[Build] {stage} ({progress * 100:F0}%)");
            });

            HandleBuildResult(result, settings, andRun);
        }, Project.Current?.RootPath);
    }

    private static void HandleBuildResult(BuildResult result, BuildSettings settings, bool andRun)
    {
        if (result.Success)
        {
            Runtime.Debug.Log($"[Build] SUCCESS: {result.AssetCount} assets → {result.OutputPath} ({result.Duration.TotalSeconds:F1}s)");

            if (andRun)
            {
                string exe = Path.Combine(result.OutputPath,
                    Project.Current!.Name + (settings.Platform == BuildTarget.Windows ? ".exe" : ""));
                if (File.Exists(exe))
                {
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true }); }
                    catch (Exception ex) { Runtime.Debug.LogError($"[Build] Failed to launch: {ex.Message}"); }
                }
            }
        }
        else
        {
            Runtime.Debug.LogError($"[Build] FAILED: {result.Errors}");
        }
    }

    public override void ResetToDefaults()
    {
        Scenes.Clear();
        Platform = BuildTarget.Windows;
        Config = BuildConfiguration.Release;
        OutputDirectory = "Builds";
        PackagingMode = AssetPackagingMode.ProwlPak;
        AssetMode = AssetExportMode.DependenciesOnly;
        MaxPakSizeMB = 2048;
        SelfContained = true;
        WindowWidth = 1280;
        WindowHeight = 720;
    }
}
