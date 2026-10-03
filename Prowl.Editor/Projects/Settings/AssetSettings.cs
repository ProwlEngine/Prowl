using System;

using Prowl.Editor.GUI;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime;

namespace Prowl.Editor.Projects.Settings;

/// <summary>
/// How long assets nothing uses stay loaded, and how much memory a built game lets loaded assets take before
/// freeing unused ones early.
/// </summary>
[ProjectSettings("Assets", EditorIcons.Cubes, order: 30)]
public class AssetSettings : ProjectSettingsBase
{
    /// <summary>Seconds an asset nothing reaches stays loaded, so something used again soon never reloads.</summary>
    public float GracePeriodSeconds = 10f;

    /// <summary>Megabytes of loaded assets a built game allows before freeing unused ones early. Zero for no budget. The editor never has one.</summary>
    public int MemoryBudgetMB = 0;

    public override void Apply() => AssetDatabase.GracePeriod = TimeSpan.FromSeconds(GracePeriodSeconds);

    public override void ResetToDefaults()
    {
        GracePeriodSeconds = 10f;
        MemoryBudgetMB = 0;
    }

    public override void OnGUI(Paper paper, float width)
    {
        Origami.Header(paper, "assets_h_load", $"{EditorIcons.Cubes}  Loading").Underline().Show();

        EditorGUI.SettingsSliderField(paper, "assets_grace", "Unused Grace Period (s)", GracePeriodSeconds, 0f, 120f,
            v => { GracePeriodSeconds = v; Apply(); EditorRegistries.SaveSettings(); });

        EditorGUI.Row(paper, "assets_budget", "Player Memory Budget (MB)", () =>
            Origami.NumericField<int>(paper, "assets_budget_v", MemoryBudgetMB, v =>
            {
                MemoryBudgetMB = Math.Max(0, v);
                EditorRegistries.SaveSettings();
            }).Min(0).Show());
    }
}
