// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Editor.GUI;
using Prowl.Editor.Importers;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

using PropertyGridUtils = Prowl.Editor.GUI.PropertyGridUtils;

namespace Prowl.Editor.Inspector;

/// <summary>
/// Inspector for imported audio clips: what the file turned out to contain, and the import settings
/// that decide how it is stored.
/// </summary>
[CustomAssetEditor(typeof(AudioClip))]
public class AudioClipAssetEditor : ImportSettingsEditor
{
    public override void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset)
    {
        id = $"{id}_{entry.Guid:N}";

        Origami.Header(paper, $"{id}_hdr", $"{EditorIcons.VolumeHigh}  Audio Clip").Show();
        Origami.Label(paper, $"{id}_path", $"Path: {entry.Path}").Show();

        DrawClipInfo(paper, id, asset as AudioClip);
        DrawWaveform(paper, id, asset as AudioClip);
        DrawTransport(paper, id, asset as AudioClip);

        EchoObject settings = Settings(entry);

        paper.Box($"{id}_sp1").Height(6);
        Origami.Header(paper, $"{id}_settings_hdr", $"{EditorIcons.Gear}  Import Settings").Underline().Show();

        var loadType = (AudioLoadType)Int(settings, AudioImportKeys.LoadType);
        PropertyGridUtils.DrawField(paper, $"{id}_loadtype", "Load Type", typeof(AudioLoadType), loadType,
            v => settings[AudioImportKeys.LoadType] = new EchoObject((int)(AudioLoadType)v!));

        Origami.Checkbox(paper, $"{id}_mono", Bool(settings, AudioImportKeys.ForceMono),
                v => settings[AudioImportKeys.ForceMono] = new EchoObject(v))
            .LabelRight("Force Mono").Show();

        PropertyGridUtils.DrawField(paper, $"{id}_rate", "Sample Rate Override", typeof(int),
            Int(settings, AudioImportKeys.SampleRateOverride),
            v => settings[AudioImportKeys.SampleRateOverride] = new EchoObject(Math.Max(0, (int)v!)));

        Origami.Label(paper, $"{id}_ratehint", "0 keeps the file's own rate.").Show();

        // Either conversion forces a decode, and there is no encoder to put the result back into the
        // source format, so the stored asset becomes uncompressed either way.
        if (Bool(settings, AudioImportKeys.ForceMono) || Int(settings, AudioImportKeys.SampleRateOverride) > 0)
            Origami.Label(paper, $"{id}_convnote", "Converting stores the clip uncompressed.").Show();

        DrawApplyRevertBar(paper, id, entry, asset);
    }

    private static void DrawClipInfo(Paper paper, string id, AudioClip? clip)
    {
        if (clip.IsNotValid()) return;

        Origami.Separator(paper, $"{id}_sep_info").Show();

        int channels = clip!.Channels;

        if (channels == 0)
        {
            Origami.Label(paper, $"{id}_undecodable", "This file could not be decoded.").Show();
            return;
        }

        string layout = channels switch { 1 => "Mono", 2 => "Stereo", _ => $"{channels} channels" };

        Origami.Label(paper, $"{id}_format", $"{layout}  |  {clip.SampleRate} Hz  |  {FormatDuration(clip.LengthInSeconds)}").Show();
        Origami.Label(paper, $"{id}_size", $"Stored: {FormatBytes(clip.DataSize)}").Show();
    }

    /// <summary>
    /// Peak envelope of the clip, as one column per horizontal slot. Cached per asset because it has
    /// to decode the whole clip to build it, which is far too much to redo every frame.
    /// </summary>
    private static readonly Dictionary<Guid, float[]> s_waveforms = new();

    private const int WaveformColumns = 160;
    private const float WaveformHeight = 64.0f;

    private static void DrawWaveform(Paper paper, string id, AudioClip? clip)
    {
        if (clip.IsNotValid()) return;

        float[] peaks = Waveform(clip!);

        if (peaks.Length == 0) return;

        Origami.Header(paper, $"{id}_wave_hdr", "Waveform").Underline().Show();

        using (paper.Row($"{id}_wave").Height(WaveformHeight).Enter())
        {
            for (int i = 0; i < peaks.Length; i++)
            {
                // Drawn from the middle out, so the column reads as an envelope rather than a bar chart.
                float height = Math.Max(1.0f, peaks[i] * WaveformHeight);

                using (paper.Column($"{id}_wc{i}").Width(UnitValue.Stretch()).Enter())
                {
                    paper.Box($"{id}_wt{i}").Height(UnitValue.Stretch());
                    paper.Box($"{id}_wb{i}").Height(height).BackgroundColor(EditorTheme.Purple400);
                    paper.Box($"{id}_wf{i}").Height(UnitValue.Stretch());
                }
            }
        }
    }

    private static float[] Waveform(AudioClip clip)
    {
        Guid key = clip.AssetID;

        if (key != Guid.Empty && s_waveforms.TryGetValue(key, out float[]? cached))
            return cached;

        float[] samples = clip.GetSampleData();
        int channels = Math.Max(1, clip.Channels);
        int frames = samples.Length / channels;

        float[] peaks = new float[frames > 0 ? WaveformColumns : 0];

        for (int column = 0; column < peaks.Length; column++)
        {
            int start = (int)((long)column * frames / WaveformColumns);
            int end = (int)((long)(column + 1) * frames / WaveformColumns);
            float peak = 0.0f;

            for (int frame = start; frame < end && frame < frames; frame++)
            {
                for (int channel = 0; channel < channels; channel++)
                    peak = Math.Max(peak, Math.Abs(samples[frame * channels + channel]));
            }

            peaks[column] = Math.Min(1.0f, peak);
        }

        if (key != Guid.Empty)
            s_waveforms[key] = peaks;

        return peaks;
    }

    private static void DrawTransport(Paper paper, string id, AudioClip? clip)
    {
        if (clip.IsNotValid()) return;

        using (paper.Row($"{id}_transport").Height(26).RowBetween(4).Enter())
        {
            bool playing = AudioPreview.PlayingClip == clip!.AssetID && AudioPreview.IsPlaying;

            if (playing)
                Origami.Button(paper, $"{id}_stop", $"{EditorIcons.Stop}  Stop", AudioPreview.Stop).Show();
            else
                Origami.Button(paper, $"{id}_play", $"{EditorIcons.Play}  Play", () => AudioPreview.Play(clip)).Show();
        }
    }

    private static string FormatDuration(float seconds)
    {
        if (seconds < 1.0f) return $"{seconds * 1000.0f:F0} ms";

        int minutes = (int)(seconds / 60.0f);
        return minutes > 0 ? $"{minutes}m {seconds - minutes * 60:F1}s" : $"{seconds:F2} s";
    }

    private static string FormatBytes(ulong bytes)
    {
        if (bytes >= 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }

    private static int Int(EchoObject settings, string key)
        => settings.TryGet(key, out EchoObject? value) && value != null ? value.IntValue : 0;

    private static bool Bool(EchoObject settings, string key)
        => settings.TryGet(key, out EchoObject? value) && value != null && value.BoolValue;
}
