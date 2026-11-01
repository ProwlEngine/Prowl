using System;
using System.Collections.Generic;

using Prowl.Editor.GUI;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime;

namespace Prowl.Editor.Projects.Settings;

/// <summary>
/// The project's rendering quality levels and the one in use, applied to the editor and written to QualitySettings.yaml
/// for the built player, where PlayerSettingsLoader.ApplyQuality restores them.
/// </summary>
[ProjectSettings("Quality", EditorIcons.GaugeHigh, order: 23)]
public class QualitySettings : ProjectSettingsBase
{
    public List<QualityLevel> Levels = Runtime.QualitySettings.CreateDefaults();
    public int Active = 2;

    private static readonly string[] s_tiers = ["Full", "Lite"];

    public override void Apply() => Runtime.QualitySettings.Set(Levels, Active);

    public override void ResetToDefaults()
    {
        Levels = Runtime.QualitySettings.CreateDefaults();
        Active = 2;
    }

    // Saves and applies after an edit
    private void Changed()
    {
        Active = Math.Clamp(Active, 0, Math.Max(0, Levels.Count - 1));
        Apply();
        EditorRegistries.SaveSettings();
    }

    public override void OnGUI(Paper paper, float width)
    {
        Origami.Header(paper, "quality_hdr", $"{EditorIcons.GaugeHigh}  Quality").Underline().Show();

        string[] names = new string[Levels.Count];
        for (int i = 0; i < Levels.Count; i++) names[i] = Levels[i].Name;
        EditorGUI.Row(paper, "quality_active", "Active Level", () =>
            Origami.Dropdown(paper, "quality_active_v", Math.Clamp(Active, 0, Math.Max(0, Levels.Count - 1)), i => { Active = i; Changed(); }, names).Show());

        paper.Box("quality_sp").Height(8);

        for (int i = 0; i < Levels.Count; i++)
            DrawLevel(paper, i);

        using (paper.Row("quality_buttons").Height(EditorTheme.RowHeight).Gap(6).Enter())
        {
            Origami.Button(paper, "quality_add", "Add Level", () =>
            {
                QualityLevel copy = (Levels.Count > 0 ? Levels[^1] : new QualityLevel()).Clone();
                copy.Name = $"Level {Levels.Count + 1}";
                Levels.Add(copy);
                Changed();
            }).Show();
            Origami.Button(paper, "quality_defaults", "Reset to Defaults", () => { ResetToDefaults(); Changed(); }).Show();
        }
    }

    private void DrawLevel(Paper paper, int index)
    {
        QualityLevel level = Levels[index];
        string id = $"quality_l{index}";
        string title = index == Active ? $"{level.Name}  (active)" : level.Name;

        Origami.Foldout(paper, id, title).Body(() =>
        {
            EditorGUI.SettingsTextField(paper, $"{id}_name", "Name", level.Name, v => { level.Name = v; Apply(); });
            EditorGUI.Row(paper, $"{id}_tier", "PBR Tier", () =>
                Origami.Dropdown(paper, $"{id}_tier_v", (int)level.PbrTier, v => { level.PbrTier = (PbrTier)v; Changed(); }, s_tiers).Show());
            EditorGUI.SettingsSliderField(paper, $"{id}_scale", "Render Scale", level.RenderScale, 0.25f, 2f, v => { level.RenderScale = v; Apply(); });

            EditorGUI.Row(paper, $"{id}_sdist", "Shadow Distance", () =>
                Origami.NumericField<float>(paper, $"{id}_sdist_v", level.ShadowDistance, v => { level.ShadowDistance = MathF.Max(0.01f, v); Changed(); }).Show());
            EditorGUI.SettingsEnumDropdown(paper, $"{id}_casc", "Shadow Cascades", level.ShadowCascades, v => { level.ShadowCascades = v; Apply(); });
            EditorGUI.SettingsEnumDropdown(paper, $"{id}_sres", "Max Shadow Resolution", level.MaxShadowResolution, v => { level.MaxShadowResolution = v; Apply(); });

            EditorGUI.SettingsIntSlider(paper, $"{id}_probe", "Max Probe Resolution", level.MaxProbeResolution, 16, 2048, v => { level.MaxProbeResolution = v; Apply(); });
            EditorGUI.SettingsCheckbox(paper, $"{id}_ssr", "Screen Space Reflections", level.ScreenSpaceReflections, v => { level.ScreenSpaceReflections = v; Apply(); });
            EditorGUI.SettingsCheckbox(paper, $"{id}_ao", "Ambient Occlusion", level.AmbientOcclusion, v => { level.AmbientOcclusion = v; Apply(); });
            EditorGUI.SettingsIntSlider(paper, $"{id}_bloom", "Bloom Iterations", level.BloomIterations, 1, 8, v => { level.BloomIterations = v; Apply(); });

            if (Levels.Count > 1)
            {
                Origami.Button(paper, $"{id}_remove", "Remove Level", () =>
                {
                    Levels.RemoveAt(index);
                    if (Active >= index && Active > 0) Active--;
                    Changed();
                }).Show();
            }
        });
    }
}
