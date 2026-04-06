using System;

using Prowl.Editor.Widgets;
using Prowl.PaperUI;
using Prowl.Runtime;

namespace Prowl.Editor;

[ProjectSettings("Time", EditorIcons.Clock, order: 22)]
public class TimeSettings : ProjectSettingsBase
{
    public float FixedTimestep { get; set; } = 1f / 60f;
    public int MaxFixedIterations { get; set; } = 3;
    public float DefaultTimeScale { get; set; } = 1f;

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
        EditorGUI.Header(paper, "time_hdr", $"{EditorIcons.Clock}  Time");
        EditorGUI.Separator(paper, "time_sep");

        EditorGUI.FloatField(paper, "time_fixed", FixedTimestep, "Fixed Timestep")
            .OnValueChanged(v =>
            {
                FixedTimestep = MathF.Max(0.0001f, v);
                Apply();
                ProjectSettingsRegistry.SaveAll();
            });

        EditorGUI.Label(paper, "time_fixed_info",
            $"  {(int)(1f / FixedTimestep + 0.5f)} Hz ({FixedTimestep * 1000f:F2} ms)");

        EditorGUI.IntSlider(paper, "time_maxiter", "Max Fixed Iterations", MaxFixedIterations, 1, 15)
            .OnValueChanged(v =>
            {
                MaxFixedIterations = v;
                Apply();
                ProjectSettingsRegistry.SaveAll();
            });

        EditorGUI.Slider(paper, "time_scale", "Default Time Scale", DefaultTimeScale, 0f, 10f)
            .OnValueChanged(v =>
            {
                DefaultTimeScale = v;
                Apply();
                ProjectSettingsRegistry.SaveAll();
            });
    }
}
