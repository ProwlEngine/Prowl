using System.Collections.Generic;
using System.Text.Json.Serialization;

using Prowl.Editor.Widgets;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime;

namespace Prowl.Editor;

/// <summary>
/// Stores color and curve palettes per-project.
/// </summary>
[ProjectSettings("Editor", EditorIcons.Palette, order: 5, exportToBuild: false)]
public class ProjectsEditorSettings : ProjectSettingsBase
{
    public List<string> ColorPalette = DefaultColorPalette();
    public List<CurvePreset> CurvePalette = DefaultCurvePalette();

    public class CurvePreset
    {
        public string Name = "";
        public List<KeyFrameData> Keys = new();
    }

    public class KeyFrameData
    {
        public float Position;
        public float Value;
        public float TangentIn;
        public float TangentOut;

        public KeyFrame ToKeyFrame() => new(Position, Value, TangentIn, TangentOut);

        public static KeyFrameData FromKeyFrame(KeyFrame k) => new()
        {
            Position = k.Position, Value = k.Value,
            TangentIn = k.TangentIn, TangentOut = k.TangentOut
        };
    }

    public override void ResetToDefaults()
    {
        ColorPalette = DefaultColorPalette();
        CurvePalette = DefaultCurvePalette();
    }

    public override void OnGUI(Paper paper, float width)
    {
        // Color Palette
        Origami.Header(paper, "pal_color_hdr", $"{EditorIcons.Palette}  Color Palette ({ColorPalette.Count})").Underline().Show();

        Widgets.PaletteUI.DrawColorPalette(paper, "pal_c", ColorPalette, width - 16, swatchSize: 22f,
            onAdd: () => "#FFFFFF");

        paper.Box("pal_sp1").Height(12);

        // Curve Palette
        Origami.Header(paper, "pal_curve_hdr", $"{EditorIcons.ChartLine}  Curve Presets ({CurvePalette.Count})").Underline().Show();

        Widgets.PaletteUI.DrawCurvePresets(paper, "pal_cv", CurvePalette, width - 16, maxRows: 10);
    }

    // ── Default game/UI color palette ──

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

    // ── Default curve presets ──

    public static List<CurvePreset> DefaultCurvePalette() =>
    [
        new() { Name = "Linear", Keys = [new() { Position = 0, Value = 0, TangentOut = 1 }, new() { Position = 1, Value = 1, TangentIn = 1 }] },
        new() { Name = "Ease In-Out", Keys = [new() { Position = 0, Value = 0 }, new() { Position = 1, Value = 1 }] },
        new() { Name = "Ease In", Keys = [new() { Position = 0, Value = 0 }, new() { Position = 1, Value = 1, TangentIn = 2 }] },
        new() { Name = "Ease Out", Keys = [new() { Position = 0, Value = 0, TangentOut = 2 }, new() { Position = 1, Value = 1 }] },
        new() { Name = "Flat", Keys = [new() { Position = 0, Value = 1 }, new() { Position = 1, Value = 1 }] },
        new() { Name = "Bounce", Keys = [
            new() { Position = 0, Value = 0, TangentOut = 4 },
            new() { Position = 0.35f, Value = 1, TangentIn = 0, TangentOut = 0 },
            new() { Position = 0.65f, Value = 0.3f, TangentIn = 0, TangentOut = 0 },
            new() { Position = 0.85f, Value = 0.8f, TangentIn = 0, TangentOut = 0 },
            new() { Position = 1, Value = 1 }
        ]},
        new() { Name = "Step", Keys = [
            new() { Position = 0, Value = 0, TangentOut = 0 },
            new() { Position = 0.5f, Value = 0, TangentIn = 0, TangentOut = 100 },
            new() { Position = 0.5001f, Value = 1, TangentIn = 100, TangentOut = 0 },
            new() { Position = 1, Value = 1, TangentIn = 0 }
        ]},
    ];
}
