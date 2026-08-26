using Prowl.Editor.Inspector;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Runtime.Audio;
using Prowl.Editor.Theming;

using Prowl.Editor.GUI;
namespace Prowl.Editor.Projects.Settings;

[ProjectSettings("Audio", EditorIcons.VolumeHigh, order: 25)]
public class AudioSettings : ProjectSettingsBase
{
    public float GlobalVolume = 1.0f;

    /// <summary>Rate the device is opened at. Clips at other rates are resampled to it.</summary>
    public int SampleRate = 44100;

    /// <summary>Channel count the device is opened with. 2 is stereo.</summary>
    public int Channels = 2;

    /// <summary>
    /// Device buffer size in frames. Smaller means less latency and more risk of dropouts, and the
    /// device treats it as a hint rather than a promise.
    /// </summary>
    public int BufferSize = 2048;

    private static readonly int[] s_sampleRates = [22050, 44100, 48000, 96000];
    private static readonly int[] s_bufferSizes = [256, 512, 1024, 2048, 4096];

    public override void Apply()
    {
        AudioContext.MasterVolume = GlobalVolume;

        // Reopens the device only when something about it actually changed.
        AudioContext.Restart((uint)SampleRate, (uint)Channels, (uint)BufferSize);
    }

    public override void ResetToDefaults()
    {
        GlobalVolume = 1.0f;
        SampleRate = 44100;
        Channels = 2;
        BufferSize = 2048;
    }

    public override void OnGUI(Paper paper, float width)
    {
        Origami.Header(paper, "audio_hdr", $"{EditorIcons.VolumeHigh}  Audio").Underline().Show();

        EditorGUI.SettingsSliderField(paper, "audio_vol", "Global Volume", GlobalVolume, 0f, 1f,
            v => { GlobalVolume = v; Apply(); });

        Origami.Header(paper, "audio_dev_hdr", "Device").Underline().Show();

        DrawChoice(paper, "audio_rate", "Sample Rate", s_sampleRates, SampleRate, "Hz",
            v => { SampleRate = v; Apply(); });

        DrawChoice(paper, "audio_ch", "Channels", [1, 2], Channels, "",
            v => { Channels = v; Apply(); });

        DrawChoice(paper, "audio_buf", "Buffer Size", s_bufferSizes, BufferSize, "frames",
            v => { BufferSize = v; Apply(); });

        Origami.Label(paper, "audio_latency",
            $"About {BufferSize * 1000.0f / System.Math.Max(1, SampleRate):F1} ms of output latency.").Show();

        using (paper.Row("audio_out_row").Height(26).RowBetween(4).Enter())
        {
            Origami.Header(paper, "audio_out_hdr", "Outputs").Underline().Show();
            Origami.Button(paper, "audio_out_refresh", "Refresh", () => s_devices = null).Show();
        }

        DeviceInfo[] devices = Devices();

        if (devices.Length == 0)
        {
            Origami.Label(paper, "audio_nodev", "No playback devices were reported.").Show();
            return;
        }

        // Listed rather than selectable: the engine opens the system default, and picking a specific
        // one is a player-facing setting rather than a project-wide one.
        foreach (DeviceInfo device in devices)
        {
            string marker = device.IsDefault ? "  (default)" : "";
            Origami.Label(paper, $"audio_dev_{device.Index}", $"{device.Name}{marker}").Show();
        }
    }

    private static DeviceInfo[] s_devices;
    private static int s_devicesForGeneration = -1;

    /// <summary>
    /// The playback devices, enumerated once rather than per frame. Enumerating asks the backend to
    /// walk the system's devices and allocates a managed object per device and per format it reports,
    /// which is not something an immediate mode panel should do while it is on screen.
    /// </summary>
    private static DeviceInfo[] Devices()
    {
        // Reopening the device is the one thing the editor does that can change what is available,
        // and the Refresh button covers anything plugged in while the page is open.
        if (s_devices != null && s_devicesForGeneration == AudioContext.DeviceGeneration)
            return s_devices;

        s_devices = AudioContext.GetDevices() ?? [];
        s_devicesForGeneration = AudioContext.DeviceGeneration;
        return s_devices;
    }

    /// <summary>A row of buttons for a small fixed set of values, with the current one highlighted.</summary>
    private static void DrawChoice(Paper paper, string id, string label, int[] options, int current, string suffix, System.Action<int> onChange)
    {
        using (paper.Row($"{id}_row").Height(26).RowBetween(4).Enter())
        {
            Origami.Label(paper, $"{id}_lbl", label).Show();

            foreach (int option in options)
            {
                string text = option == current ? $"[{option}{suffix}]" : $"{option}{suffix}";
                int captured = option;
                Origami.Button(paper, $"{id}_{option}", text, () => onChange(captured)).Show();
            }
        }
    }
}
