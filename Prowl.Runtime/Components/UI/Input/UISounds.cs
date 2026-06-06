// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Audio;
using Prowl.Runtime.Audio.Native;
using Prowl.Runtime.Resources;

namespace Prowl.Runtime.UI;

public enum UISound
{
    /// <summary>Pointer first entered an interactable widget.</summary>
    Hover,
    /// <summary>Button / Selectable was pressed (the down half).</summary>
    Press,
    /// <summary>Successful click — press + release on the same element.</summary>
    Click,
    /// <summary>A drag began.</summary>
    DragStart,
    /// <summary>A drag was released.</summary>
    DragEnd,
    /// <summary>Keyboard / gamepad navigation moved focus.</summary>
    Navigate,
    /// <summary>Focused element confirmed via Submit (Enter / Gamepad-A).</summary>
    Submit,
    /// <summary>Focused element canceled via Cancel (Escape / Gamepad-B).</summary>
    Cancel,
    /// <summary>A non-interactable element was clicked — gives the player negative feedback.</summary>
    Denied,
}


public static class UISounds
{
    /// <summary>Played the moment the pointer enters an interactable widget.</summary>
    public static AudioClip? HoverClip;
    /// <summary>Played on pointer-down on an interactable widget.</summary>
    public static AudioClip? PressClip;
    /// <summary>Played on a successful click (down + up on the same widget).</summary>
    public static AudioClip? ClickClip;
    /// <summary>Played when a drag operation begins.</summary>
    public static AudioClip? DragStartClip;
    /// <summary>Played when a drag operation ends (any release).</summary>
    public static AudioClip? DragEndClip;
    /// <summary>Played when keyboard / gamepad navigation moves the focused element.</summary>
    public static AudioClip? NavigateClip;
    /// <summary>Played when the focused element fires its submit handler.</summary>
    public static AudioClip? SubmitClip;
    /// <summary>Played when the focused element fires its cancel handler.</summary>
    public static AudioClip? CancelClip;
    /// <summary>Played when a non-interactable widget is clicked.</summary>
    public static AudioClip? DeniedClip;

    public static float Volume = 1f;

    public static bool Enabled = true;

    public static bool SuppressDefault = false;

    public static event Action<UISound, AudioClip>? OnPlay;

    public static void Play(UISound sound) => Play(sound, null);

    public static void Play(UISound sound, AudioClip? overrideClip)
    {
        if (!Enabled) return;
        AudioClip? clip = overrideClip ?? Resolve(sound);
        if (clip == null) return;

        try { OnPlay?.Invoke(sound, clip); }
        catch (Exception ex) { Debug.LogError($"[UISounds] OnPlay handler threw: {ex.Message}"); }

        if (SuppressDefault) return;
        PlayDirect(clip, Volume);
    }

    private static AudioClip? Resolve(UISound sound) => sound switch
    {
        UISound.Hover => HoverClip,
        UISound.Press => PressClip,
        UISound.Click => ClickClip,
        UISound.DragStart => DragStartClip,
        UISound.DragEnd => DragEndClip,
        UISound.Navigate => NavigateClip,
        UISound.Submit => SubmitClip,
        UISound.Cancel => CancelClip,
        UISound.Denied => DeniedClip,
        _ => null,
    };

    // -----------------------------------------------------------------------------

    private const int VoiceCount = 8;
    private static readonly IntPtr[] s_voices = new IntPtr[VoiceCount];
    private static IntPtr s_group = IntPtr.Zero;
    private static int s_nextVoice;
    private static readonly object s_lock = new();

    private static void PlayDirect(AudioClip clip, float volume)
    {
        IntPtr ctx = AudioContext.NativeContext;
        if (ctx == IntPtr.Zero) return; // audio not initialized yet

        lock (s_lock)
        {
            if (s_group == IntPtr.Zero)
            {
                s_group = MiniAudioExNative.ma_ex_sound_group_init(ctx);
                if (s_group == IntPtr.Zero) return;
            }

            int slot = s_nextVoice;
            s_nextVoice = (s_nextVoice + 1) % VoiceCount;

            if (s_voices[slot] == IntPtr.Zero)
            {
                IntPtr handle = MiniAudioExNative.ma_ex_audio_source_init(ctx);
                if (handle == IntPtr.Zero) return;
                MiniAudioExNative.ma_ex_audio_source_set_group(handle, s_group);
                MiniAudioExNative.ma_ex_audio_source_set_spatialization(handle, 0);
                s_voices[slot] = handle;
            }

            IntPtr src = s_voices[slot];

            // Stop whatever was playing in this voice — keeps responsiveness tight without
            // stealing the *other* voices, so rapid distinct sounds layer naturally.
            MiniAudioExNative.ma_ex_audio_source_stop(src);
            MiniAudioExNative.ma_ex_audio_source_set_pcm_position(src, 0);
            MiniAudioExNative.ma_ex_audio_source_set_volume(src, volume);

            if (clip.Handle != IntPtr.Zero)
                MiniAudioExNative.ma_ex_audio_source_play_from_memory(src, clip.Handle, clip.DataSize);
            else if (!string.IsNullOrEmpty(clip.FilePath))
                MiniAudioExNative.ma_ex_audio_source_play_from_file(src, clip.FilePath, clip.StreamFromDisk ? (uint)1 : 0);
        }
    }
}
