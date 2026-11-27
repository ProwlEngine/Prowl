using System;
using System.Collections.Generic;

using Prowl.Editor.GUI;
using Prowl.Editor.Theming;
using Prowl.Graphite.Rendering;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Vector;

namespace Prowl.Editor.Projects.Settings;

/// <summary> Project settings for how every camera draws unless it overrides them. </summary>
[ProjectSettings("Graphics", EditorIcons.Display, order: 24)]
public class GraphicsSettings : ProjectSettingsBase
{
    /// <summary> What cameras sort transparent renderers back to front by. </summary>
    public TransparencySortMode TransparencySortMode = TransparencySortMode.Default;

    /// <summary> The axis the custom axis mode sorts along, renderers further along it drawn first. </summary>
    public Float3 TransparencySortAxis = new(0, 1, 0);

    public override void Apply() => Runtime.Graphics.TransparencySort = new TransparencySort(TransparencySortMode, TransparencySortAxis);

    public override void ResetToDefaults()
    {
        TransparencySortMode = TransparencySortMode.Default;
        TransparencySortAxis = new Float3(0, 1, 0);
    }

    public override void OnGUI(Paper paper, float width)
    {
        Origami.Header(paper, "gfx_sort_hdr", $"{EditorIcons.LayerGroup}  Transparency Sort").Underline().Show();

        List<TransparencySortMode> modes = [.. Enum.GetValues<TransparencySortMode>()];
        EditorGUI.Row(paper, "gfx_sort_mode", "Mode", () =>
            Origami.Dropdown(paper, "gfx_sort_mode_dd", TransparencySortMode, v =>
            {
                TransparencySortMode = v;
                Apply();
                EditorRegistries.SaveSettings();
            }, modes).Show());

        if (TransparencySortMode != TransparencySortMode.CustomAxis) return;

        EditorGUI.Row(paper, "gfx_sort_axis", "Axis", () =>
        {
            using (paper.Row("gfx_sort_axis_row").Gap(4).Enter())
            {
                Axis("x", TransparencySortAxis.X, v => TransparencySortAxis.X = v);
                Axis("y", TransparencySortAxis.Y, v => TransparencySortAxis.Y = v);
                Axis("z", TransparencySortAxis.Z, v => TransparencySortAxis.Z = v);
            }
        });

        void Axis(string id, float value, Action<float> set)
            => Origami.NumericField<float>(paper, $"gfx_sort_axis_{id}", value, v => { set(v); Apply(); })
                .OnCommit(EditorRegistries.SaveSettings).Show();
    }
}
