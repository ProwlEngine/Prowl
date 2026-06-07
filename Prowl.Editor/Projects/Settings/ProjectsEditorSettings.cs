using System.Collections.Generic;

using Prowl.Editor.Theming;
using Prowl.PaperUI;

namespace Prowl.Editor.Projects.Settings;

/// <summary>
/// Stores color and curve palettes per-project.
/// </summary>
[ProjectSettings("Editor", EditorIcons.Palette, order: 5, exportToBuild: false)]
public class ProjectsEditorSettings : ProjectSettingsBase
{
    public List<string> ColorPalette = DefaultColorPalette();

    /// <summary>Last resolution preset selected in the Game View toolbar.</summary>
    public int SelectedResolutionIndex = 0;

    public override void ResetToDefaults()
    {
        ColorPalette = DefaultColorPalette();
    }

    public override void OnGUI(Paper paper, float width)
    {
    }

    // -- Default game/UI color palette --

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
