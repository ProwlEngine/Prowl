using System;
using System.Collections.Generic;

using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

namespace Prowl.Editor.Projects.Settings;

/// <summary>A Game view size: pixels, or an aspect ratio when <see cref="Aspect"/> is set.</summary>
public class GameViewResolution
{
    public string Name = "";
    public int Width = 1920;
    public int Height = 1080;
    public bool Aspect;
}

/// <summary> Stores the color palette per-project. </summary>
[ProjectSettings("Editor", EditorIcons.Palette, order: 5, exportToBuild: false)]
public class ProjectsEditorSettings : ProjectSettingsBase
{
    /// <summary> The list of hex color strings that make up the editor color palette. </summary>
    public List<string> ColorPalette = DefaultColorPalette();

    /// <summary>An image shown behind the editor while this project is open, relative to the project folder. Empty uses the theme's background.</summary>
    public string BackgroundImage = "";

    /// <summary>The project's own Game view sizes, listed after the built in ones.</summary>
    public List<GameViewResolution> CustomResolutions = [];

    /// <summary>Last resolution preset selected in the Game View toolbar.</summary>
    public int SelectedResolutionIndex = 0;

    /// <summary> Resets ColorPalette and SelectedResolutionIndex to their default values. </summary>
    public override void ResetToDefaults()
    {
        ColorPalette = DefaultColorPalette();
        SelectedResolutionIndex = 0;
        BackgroundImage = "";
        CustomResolutions = [];
    }

    /// <summary> Draws the colour palette as a grid of editable swatches. </summary>
    public override void OnGUI(Paper paper, float width)
    {
        Origami.Header(paper, "pe_bg_header", $"{EditorIcons.Image}  Background").Underline().Show();
        EditorGUI.Row(paper, "pe_bg", "Background Image", () =>
        {
            using (paper.Row("pe_bg_btns").Height(UnitValue.Auto).Gap(4).Enter())
            {
                string label = string.IsNullOrEmpty(BackgroundImage) ? "None (use the theme's)" : System.IO.Path.GetFileName(BackgroundImage);
                Origami.Button(paper, "pe_bg_pick", $"{EditorIcons.FolderOpen}  {label}", PickBackgroundImage).Show();
                if (!string.IsNullOrEmpty(BackgroundImage))
                    Origami.IconButton(paper, "pe_bg_clear", EditorIcons.Xmark_I, () => { BackgroundImage = ""; EditorRegistries.SaveSettings(); })
                        .Small().Ghost().Show();
            }
        });

        paper.Box("pe_bg_sp").Height(12);
        Origami.Header(paper, "pe_palette_header", $"{EditorIcons.Palette}  Color Palette").Underline().Show();

        const float FieldW = 110, DeleteW = 20, Gap = 4, RowH = 24;
        int columns = Math.Max(1, (int)((width - 16) / (FieldW + DeleteW + Gap * 3)));

        for (int start = 0; start < ColorPalette.Count; start += columns)
        {
            using (paper.Row("pe_palette_row", start).Height(RowH).Gap(Gap * 3).PaddingLeft(8).Enter())
            {
                for (int i = start; i < Math.Min(start + columns, ColorPalette.Count); i++)
                {
                    int idx = i;
                    using (paper.Row("pe_palette_cell", i).Width(FieldW + DeleteW + Gap).Height(RowH).Gap(Gap).Enter())
                    {
                        Origami.ColorField(paper, $"pe_palette_color_{i}", EditorGUI.HexToVColor(ColorPalette[i]), v =>
                            {
                                ColorPalette[idx] = EditorGUI.VColorToHex(v);
                                EditorRegistries.SaveSettings();
                            }).Width(FieldW).Show();

                        Origami.IconButton(paper, $"pe_palette_del_{i}", EditorIcons.Xmark_I, () =>
                            {
                                ColorPalette.RemoveAt(idx);
                                EditorRegistries.SaveSettings();
                            }).Small().Width(DeleteW).Height(22).Ghost().Show();
                    }
                }
            }
        }

        paper.Box("pe_palette_sp").Height(4);

        using (paper.Row("pe_palette_actions").Height(RowH + 4).Gap(Gap).PaddingLeft(8).Enter())
        {
            Origami.Button(paper, "pe_palette_add", $"{EditorIcons.Plus}  Add Color", () =>
            {
                ColorPalette.Add("#FFFFFF");
                EditorRegistries.SaveSettings();
            }).Show();

            Origami.Button(paper, "pe_palette_reset", $"{EditorIcons.ArrowsRotate}  Reset Palette", () =>
            {
                ColorPalette = DefaultColorPalette();
                EditorRegistries.SaveSettings();
            }).Show();
        }
    }

    private void PickBackgroundImage()
    {
        if (Project.Current is not { } project) return;
        EditorApplication.OpenFileDialog(FileDialogMode.Open, path =>
        {
            if (path == null) return;
            // Kept relative when it is inside the project, so it works on every machine that opens it.
            string relative = System.IO.Path.GetRelativePath(project.RootPath, path);
            BackgroundImage = relative.StartsWith("..") || System.IO.Path.IsPathRooted(relative) ? path : relative;
            EditorRegistries.SaveSettings();
        }, project.RootPath, ["*.png;*.jpg;*.jpeg;*.bmp;*.tga;*.webp"], ["Images"]);
    }

    // -- Default game/UI color palette --

    /// <summary> Returns the default list of hex color strings used as the editor color palette. </summary>
    public static List<string> DefaultColorPalette() =>
    [
        // Grayscale
        "#FFFFFF", "#E0E0E0", "#B0B0B0", "#808080", "#505050", "#282828", "#000000",
        // Reds
        "#FF0000", "#CC3333", "#993333", "#FF6666", "#FF9999",
        // Oranges
        "#FF8800", "#CC6600", "#FFB366",
        // Yellows
        "#FFCC00", "#FFE066", "#CCAA00",
        // Greens
        "#00CC44", "#33AA33", "#66FF66", "#009933", "#88CC88",
        // Cyans
        "#00CCCC", "#66CCCC", "#009999",
        // Blues
        "#3366FF", "#0044CC", "#6699FF", "#003399", "#99BBFF",
        // Purples
        "#9933FF", "#6600CC", "#CC66FF", "#7733AA",
        // Pinks
        "#FF33CC", "#CC0099", "#FF99DD",
        // Browns/Skin
        "#8B4513", "#A0522D", "#D2B48C", "#FFDAB9",
    ];
}
