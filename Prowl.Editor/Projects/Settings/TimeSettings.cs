using System;

using Prowl.Editor.Inspector;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Editor.Theming;

using Prowl.Editor.GUI;
namespace Prowl.Editor.Projects.Settings;

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

        EditorGUI.Row(paper, "time_fixed", "Fixed Timestep", () =>
            Origami.NumericField<float>(paper, "time_fixed_v", FixedTimestep, v =>
            {
                FixedTimestep = MathF.Max(0.0001f, v);
                Apply();
                EditorRegistries.SaveSettings();
            }).Min(0.0001f).Show());

        Origami.Label(paper, "time_fixed_info",
            $"  {(int)(1f / FixedTimestep + 0.5f)} Hz ({FixedTimestep * 1000f:F2} ms)").Show();

        EditorGUI.SettingsIntSlider(paper, "time_maxiter", "Max Fixed Iterations", MaxFixedIterations, 1, 15,
            v => { MaxFixedIterations = v; Apply(); });

        EditorGUI.SettingsSliderField(paper, "time_scale", "Default Time Scale", DefaultTimeScale, 0f, 10f,
            v => { DefaultTimeScale = v; Apply(); });
    }
}
