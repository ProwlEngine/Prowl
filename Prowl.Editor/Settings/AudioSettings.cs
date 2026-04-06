using Prowl.Editor.Widgets;
using Prowl.PaperUI;
using Prowl.Runtime.Audio;

namespace Prowl.Editor;

[ProjectSettings("Audio", EditorIcons.VolumeHigh, order: 25)]
public class AudioSettings : ProjectSettingsBase
{
    public float GlobalVolume { get; set; } = 1.0f;

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
        EditorGUI.Header(paper, "audio_hdr", $"{EditorIcons.VolumeHigh}  Audio");
        EditorGUI.Separator(paper, "audio_sep");

        EditorGUI.Slider(paper, "audio_vol", "Global Volume", GlobalVolume, 0f, 1f)
            .OnValueChanged(v => { GlobalVolume = v; Apply(); ProjectSettingsRegistry.SaveAll(); });
    }
}
