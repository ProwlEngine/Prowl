using System;

using Prowl.Editor.Inspector;
using Prowl.Editor.Widgets;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime;

namespace Prowl.Editor;

[ProjectSettings("Time", EditorIcons.Clock, order: 22)]
public class TimeSettings : ProjectSettingsBase
{
    public float FixedTimestep = 1f / 60f;
    public int MaxFixedIterations = 3;
    public float DefaultTimeScale = 1f;

    public override void Apply()
    {
        Runtime.Time.FixedDeltaTime = FixedTimestep;
        Runtime.Time.TimeScale = DefaultTimeScale;
        Runtime.Time.MaxFixedIterations = MaxFixedIterations;
    }

    public override void ResetToDefaults()
    {
        FixedTimestep = 1f / 60f;
        MaxFixedIterations = 3;
        DefaultTimeScale = 1f;
    }

    public override void OnGUI(Paper paper, float width)
    {
        Origami.Header(paper, "time_hdr", $"{EditorIcons.Clock}  Time").Underline().Show();

        InspectorRow.Draw(paper, "time_fixed", "Fixed Timestep", () =>
            Origami.NumericField<float>(paper, "time_fixed_v", FixedTimestep, v =>
            {
                FixedTimestep = MathF.Max(0.0001f, v);
                Apply();
                ProjectSettingsRegistry.SaveAll();
            }).Min(0.0001f).Show());

        EditorGUI.Label(paper, "time_fixed_info",
            $"  {(int)(1f / FixedTimestep + 0.5f)} Hz ({FixedTimestep * 1000f:F2} ms)");

        InspectorRow.Draw(paper, "time_maxiter", "Max Fixed Iterations", () =>
            Origami.IntSlider(paper, "time_maxiter_v", MaxFixedIterations, v =>
            {
                MaxFixedIterations = v;
                Apply();
                ProjectSettingsRegistry.SaveAll();
            }, 1, 15).Show());

        InspectorRow.Draw(paper, "time_scale", "Default Time Scale", () =>
            Origami.Slider(paper, "time_scale_v", DefaultTimeScale, v =>
            {
                DefaultTimeScale = v;
                Apply();
                ProjectSettingsRegistry.SaveAll();
            }, 0f, 10f).Format("F2").Show());
    }
}
