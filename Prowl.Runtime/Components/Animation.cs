// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Plays AnimationClips by driving bone Transforms in the hierarchy.
/// Simple legacy-style animation — one clip at a time.
/// Bones are found by name in the child Transform hierarchy.
/// </summary>
[AddComponentMenu("Animation/Animation")]
public class AnimationComponent : MonoBehaviour
{
    /// <summary>Default animation clip to play on start.</summary>
    public AssetRef<AnimationClip> DefaultClip;

    /// <summary>All available animation clips for this model.</summary>
    public List<AssetRef<AnimationClip>> Clips = new();

    /// <summary>Auto-play the default clip on enable.</summary>
    public bool PlayAutomatically = true;

    /// <summary>Playback speed multiplier.</summary>
    public float Speed = 1f;

    /// <summary>Whether the current animation is playing.</summary>
    [NonSerialized] public bool IsPlaying;

    /// <summary>Current playback time in seconds.</summary>
    [NonSerialized] public float Time;

    /// <summary>The currently playing clip.</summary>
    [NonSerialized] public AnimationClip? CurrentClip;

    // Bone name → Transform lookup (cached on first use)
    [System.NonSerialized] private Dictionary<string, Transform>? _boneCache;

    public override void OnEnable()
    {
        _boneCache = null; // Force rebuild on enable

        if (PlayAutomatically)
        {
            var clip = DefaultClip.Res;
            if (clip == null && Clips.Count > 0)
                clip = Clips[0].Res;
            if (clip != null)
                Play(clip);
        }
    }

    public override void Update()
    {
        if (!IsPlaying || CurrentClip == null) return;

        Time += Prowl.Runtime.Time.DeltaTime * Speed;
        float duration = CurrentClip.Duration;

        if (duration <= 0f) return;

        // Handle wrap mode
        switch (CurrentClip.Wrap)
        {
            case AnimationWrapMode.Once:
                if (Time >= duration)
                {
                    Time = duration;
                    IsPlaying = false;
                }
                break;
            case AnimationWrapMode.Loop:
                Time %= duration;
                break;
            case AnimationWrapMode.PingPong:
                float cycle = Time / duration;
                int wholeCycles = (int)cycle;
                float frac = cycle - wholeCycles;
                Time = (wholeCycles % 2 == 0) ? frac * duration : (1f - frac) * duration;
                break;
            case AnimationWrapMode.ClampForever:
                if (Time >= duration)
                    Time = duration;
                break;
        }

        ApplyPose(CurrentClip, Time);
    }

    /// <summary>Play a specific animation clip from the beginning.</summary>
    public void Play(AnimationClip clip)
    {
        CurrentClip = clip;
        Time = 0f;
        IsPlaying = true;
    }

    /// <summary>Play a clip by name (searches the Clips list).</summary>
    public void Play(string clipName)
    {
        foreach (var clipRef in Clips)
        {
            var clip = clipRef.Res;
            if (clip != null && clip.Name == clipName)
            {
                Play(clip);
                return;
            }
        }
        Debug.LogWarning($"[Animation] Clip '{clipName}' not found.");
    }

    /// <summary>Stop playback and reset to the beginning.</summary>
    public void Stop()
    {
        IsPlaying = false;
        Time = 0f;
    }

    /// <summary>Pause playback at the current time.</summary>
    public void Pause() => IsPlaying = false;

    /// <summary>Resume playback from the current time.</summary>
    public void Resume() => IsPlaying = true;

    private void ApplyPose(AnimationClip clip, float time)
    {
        EnsureBoneCache();
        if (_boneCache == null || _boneCache.Count == 0) return;

        foreach (var animBone in clip.Bones)
        {
            if (!_boneCache.TryGetValue(animBone.BoneName, out Transform? bone)) continue;
            if (bone == null) continue;

            Float3 pos = animBone.EvaluatePositionAt(time);
            Quaternion rot = animBone.EvaluateRotationAt(time);
            Float3 scale = animBone.EvaluateScaleAt(time);

            bone.LocalPosition = pos;
            bone.LocalRotation = rot;
            bone.LocalScale = scale;
        }
    }

    private void EnsureBoneCache()
    {
        if (_boneCache != null) return;
        _boneCache = new Dictionary<string, Transform>();
        CacheBonesRecursive(Transform);
    }

    private void CacheBonesRecursive(Transform t)
    {
        string name = t.GameObject.Name;
        // Don't overwrite — first occurrence wins (handles duplicate names by preferring higher in hierarchy)
        _boneCache.TryAdd(name, t);

        foreach (var child in t.GameObject.Children)
            CacheBonesRecursive(child.Transform);
    }
}
