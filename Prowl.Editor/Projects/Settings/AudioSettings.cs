using Prowl.Editor.Inspector;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime.Audio;
using Prowl.Editor.Theming;

using Prowl.Editor.GUI;
namespace Prowl.Editor.Projects.Settings;

[ProjectSettings("Audio", EditorIcons.VolumeHigh, order: 25)]
public class AudioSettings : ProjectSettingsBase
{
    public float GlobalVolume = 1.0f;

    public override void Apply()
    {
        AudioContext.MasterVolume = GlobalVolume;
    }

    public override void ResetToDefaults()
    {
        GlobalVolume = 1.0f;
    }

    public override void OnGUI(Paper paper, float width)
    {
        Origami.Header(paper, "audio_hdr", $"{EditorIcons.VolumeHigh}  Audio").Underline().Show();

        EditorGUI.Row(paper, "audio_vol", "Global Volume", () =>
            Origami.Slider(paper, "audio_vol_v", GlobalVolume,
                v => { GlobalVolume = v; Apply(); EditorRegistries.SaveSettings(); },
                0f, 1f).Format("F2").Show());
    }
}
