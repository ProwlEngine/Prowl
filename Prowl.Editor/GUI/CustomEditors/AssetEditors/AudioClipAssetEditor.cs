// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Echo;
using Prowl.Editor.GUI;
using Prowl.Editor.Importers;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
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
