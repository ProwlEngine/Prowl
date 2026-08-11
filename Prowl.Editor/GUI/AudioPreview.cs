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
    private static ulong s_clipHash;

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

        // Held for as long as the voice can read it, because auditioning a clip and then reimporting
        // it is an ordinary thing to do and disposing the clip frees the buffer the decoder is on.
        // Taken before the previous one is dropped, so re-auditioning the same clip cannot free it.
        ulong retained = AudioContext.RetainClipHandle(clip.Hash) ? clip.Hash : 0;
        ReleaseClip();
        s_clipHash = retained;

        if (retained != 0)
            MiniAudioExNative.ma_ex_audio_source_play_from_memory(s_source, clip.Handle, clip.DataSize);
        else if (!string.IsNullOrEmpty(clip.FilePath))
            MiniAudioExNative.ma_ex_audio_source_play_from_file(s_source, clip.FilePath, 0);
        else
            return;

        s_playing = clip.AssetID;
    }

    private static void ReleaseClip()
    {
        if (s_clipHash == 0) return;

        AudioContext.ReleaseClipHandle(s_clipHash);
        s_clipHash = 0;
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

        // Getting here with a handle already set means the last attempt built the group and then
        // failed on the source. That half belongs to the device that is still open, so it has to be
        // released rather than overwritten, or every retry leaks a sound group.
        ReleaseVoice(uninit: s_deviceGeneration == AudioContext.DeviceGeneration);

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

    /// <summary>
    /// Drops the voice. Only uninit what the current device owns: handles from a device that has
    /// since been closed died with it, and uninitializing those would be running over freed memory.
    /// </summary>
    private static void ReleaseVoice(bool uninit)
    {
        if (uninit)
        {
            if (s_source != IntPtr.Zero)
                MiniAudioExNative.ma_ex_audio_source_uninit(s_source);

            if (s_group != IntPtr.Zero)
                MiniAudioExNative.ma_ex_sound_group_uninit(s_group);
        }

        ReleaseClip();

        s_source = IntPtr.Zero;
        s_group = IntPtr.Zero;
        s_playing = Guid.Empty;
    }
}
