// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime;
using Prowl.Runtime.Audio;
using Prowl.Runtime.Audio.Native;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.GUI;

/// <summary>
/// Auditions a clip from the inspector, outside play mode.
/// </summary>
/// <remarks>
/// Driven straight off the native source rather than through an AudioSource, because component
/// lifecycle callbacks are gated to play mode, so an AudioSource placed in the editor would never
/// create anything to play with. One voice, so starting a preview replaces the one before it.
/// </remarks>
public static class AudioPreview
{
    private static IntPtr s_source;
    private static IntPtr s_group;
    private static int s_deviceGeneration = -1;
    private static Guid s_playing;

    /// <summary>The clip currently being auditioned, or Guid.Empty.</summary>
    public static Guid PlayingClip => IsPlaying ? s_playing : Guid.Empty;

    public static bool IsPlaying
        => s_source != IntPtr.Zero
           && s_deviceGeneration == AudioContext.DeviceGeneration
           && MiniAudioExNative.ma_ex_audio_source_get_is_playing(s_source) > 0;

    /// <summary>Starts auditioning a clip, replacing whatever was playing.</summary>
    public static void Play(AudioClip clip)
    {
        if (clip.IsNotValid() || !AudioContext.IsInitialized) return;

        if (!EnsureVoice()) return;

        MiniAudioExNative.ma_ex_audio_source_stop(s_source);
        MiniAudioExNative.ma_ex_audio_source_set_loop(s_source, 0);

        if (clip.Handle != IntPtr.Zero)
            MiniAudioExNative.ma_ex_audio_source_play_from_memory(s_source, clip.Handle, clip.DataSize);
        else if (!string.IsNullOrEmpty(clip.FilePath))
            MiniAudioExNative.ma_ex_audio_source_play_from_file(s_source, clip.FilePath, 0);
        else
            return;

        s_playing = clip.AssetID;
    }

    public static void Stop()
    {
        if (s_source == IntPtr.Zero || s_deviceGeneration != AudioContext.DeviceGeneration) return;

        MiniAudioExNative.ma_ex_audio_source_stop(s_source);
        s_playing = Guid.Empty;
    }

    /// <summary>Creates the preview voice, or rebuilds it if the device was reopened under it.</summary>
    private static bool EnsureVoice()
    {
        if (s_source != IntPtr.Zero && s_deviceGeneration == AudioContext.DeviceGeneration)
            return true;

        // Anything from a previous device is gone with it, so drop the handles rather than uninit
        // them against a context that no longer exists.
        s_source = IntPtr.Zero;
        s_group = IntPtr.Zero;
        s_deviceGeneration = AudioContext.DeviceGeneration;

        s_group = MiniAudioExNative.ma_ex_sound_group_init(AudioContext.NativeContext);

        if (s_group == IntPtr.Zero) return false;

        // A preview is not a thing in the world, so it plays flat rather than positioned.
        MiniAudioNative.ma_sound_group_set_spatialization_enabled(new ma_sound_group_ptr(s_group), 0);

        s_source = MiniAudioExNative.ma_ex_audio_source_init(AudioContext.NativeContext);

        if (s_source == IntPtr.Zero) return false;

        MiniAudioExNative.ma_ex_audio_source_set_group(s_source, s_group);
        return true;
    }
}
