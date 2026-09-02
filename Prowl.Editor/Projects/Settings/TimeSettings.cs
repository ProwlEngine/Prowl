using System;

using Prowl.Editor.GUI;
using Prowl.Editor.Inspector;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
namespace Prowl.Editor.Projects.Settings;

/// <summary> Project settings for time configuration, including fixed timestep, max fixed iterations, and default time scale. </summary>
[ProjectSettings("Time", EditorIcons.Clock, order: 22)]
public class TimeSettings : ProjectSettingsBase
{
    /// <summary> The fixed timestep in seconds. Default is 1/60 (approximately 16.67 ms). Minimum value is 0.0001. </summary>
    public float FixedTimestep = 1f / 60f;
    /// <summary> The maximum number of fixed update iterations per frame. Range 1 to 15. </summary>
    public int MaxFixedIterations = 3;
    /// <summary> The default time scale applied on startup. Range 0 to 10. A value of 1 represents normal speed. </summary>
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
