using Prowl.Editor.GUI;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime;

namespace Prowl.Editor.Projects.Settings;

/// <summary> Project settings for headset support: whether play mode and built players start XR on their own, and how. </summary>
[ProjectSettings("XR", EditorIcons.Headset, order: 30)]
public class XRSettings : ProjectSettingsBase
{
    /// <summary> Start XR when entering play mode, so the game shows in the headset without calling <see cref="XR.Start"/> itself. </summary>
    public bool StartInPlayMode = false;

    /// <summary> Start XR when a built player launches. </summary>
    public bool StartInPlayer = false;

    /// <summary> Where tracked poses are measured from when XR is started for the game. </summary>
    public XRTrackingOrigin TrackingOrigin = XRTrackingOrigin.Floor;

    /// <summary> Scale on the headset's recommended eye resolution. Range 0.25 to 2. </summary>
    public float RenderScale = 1f;

    public override void Apply() => XR.RenderScale = RenderScale;

    public override void ResetToDefaults()
    {
        StartInPlayMode = false;
        StartInPlayer = false;
        TrackingOrigin = XRTrackingOrigin.Floor;
        RenderScale = 1f;
    }

    public override void OnGUI(Paper paper, float width)
    {
        Origami.Header(paper, "xr_hdr", $"{EditorIcons.Headset}  XR").Underline().Show();

        EditorGUI.SettingsToggle(paper, "xr_play", "Start In Play Mode", StartInPlayMode,
            v => { StartInPlayMode = v; EditorRegistries.SaveSettings(); });
        EditorGUI.SettingsToggle(paper, "xr_player", "Start In Player", StartInPlayer,
            v => { StartInPlayer = v; EditorRegistries.SaveSettings(); });
        EditorGUI.SettingsEnumDropdown(paper, "xr_origin", "Tracking Origin", TrackingOrigin, v => TrackingOrigin = v);
        EditorGUI.SettingsSliderField(paper, "xr_scale", "Render Scale", RenderScale, 0.25f, 2f,
            v => { RenderScale = v; Apply(); });

        Origami.Label(paper, "xr_info",
            "  Needs an OpenXR runtime with OpenGL support, such as SteamVR or the Meta app. Render scale applies the next time XR starts.").Show();
    }
}
