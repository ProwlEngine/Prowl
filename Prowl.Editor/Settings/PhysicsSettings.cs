using Prowl.Editor.Widgets;
using Prowl.PaperUI;

namespace Prowl.Editor;

[ProjectSettings("Physics", EditorIcons.Atom, order: 20)]
public class PhysicsSettings : ProjectSettingsBase
{
    public float Gravity { get; set; } = -9.81f;
    public int MaxSubSteps { get; set; } = 4;
    public float FixedTimestep { get; set; } = 1f / 60f;

    public override void Apply()
    {
        // TODO: Apply gravity to physics system
    }

    public override void ResetToDefaults()
    {
        Gravity = -9.81f;
        MaxSubSteps = 4;
        FixedTimestep = 1f / 60f;
    }

    public override void OnGUI(Paper paper, float width)
    {
        EditorGUI.Header(paper, "phys_header", $"{EditorIcons.Atom}  Physics");
        EditorGUI.Separator(paper, "phys_sep");

        EditorGUI.FloatField(paper, "phys_gravity", Gravity, "Gravity Y")
            .OnValueChanged(v => { Gravity = v; ProjectSettingsRegistry.SaveAll(); });

        EditorGUI.IntField(paper, "phys_substeps", MaxSubSteps, "Max Sub-Steps")
            .OnValueChanged(v => { MaxSubSteps = v; ProjectSettingsRegistry.SaveAll(); });

        EditorGUI.FloatField(paper, "phys_timestep", FixedTimestep, "Fixed Timestep")
            .OnValueChanged(v => { FixedTimestep = v; ProjectSettingsRegistry.SaveAll(); });
    }
}
