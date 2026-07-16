// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Plays AnimationClips by driving bone Transforms in the hierarchy.
/// Simple legacy-style animation one clip at a time.
/// Bones are found by path from the hierarchy root (e.g. "Armature/Hips/Spine").
/// Paths match those stored in AnimationClip.AnimBone.BoneName.
/// </summary>
[AddComponentMenu("Animation/Animation")]
[ComponentIcon("\uf008")] // Film
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

    // Bone path → Transform lookup (cached on first use)
    [System.NonSerialized] private Dictionary<string, Transform>? _boneCache;

    // Renderer-path → SkinnedMeshRenderer lookup for blend-shape tracks (cached on first use)
    [System.NonSerialized] private Dictionary<string, SkinnedMeshRenderer?>? _blendShapeTargets;

    // The root the bone cache (and blend-shape paths) are resolved against. A blend-shape track
    // path of "" targets this root itself (e.g. a single-mesh model whose root is the mesh node).
    [System.NonSerialized] private Transform? _boneCacheRoot;

    // Set when PlayAutomatically wanted to start but the clip was still streaming in (async
    // loading); Update starts it once the clip arrives.
    [System.NonSerialized] private bool _pendingAutoPlay;

    public override void OnEnable()
    {
        _boneCache = null; // Force rebuild on enable
        _blendShapeTargets = null;

        if (PlayAutomatically)
        {
            var clip = ResolveAutoPlayClip();
            if (clip != null)
                Play(clip);
            else
                _pendingAutoPlay = true; // clip still streaming in defer to Update
        }
    }

    /// <summary>The clip PlayAutomatically should start: the default clip, else the first clip.</summary>
    private AnimationClip? ResolveAutoPlayClip()
    {
        var clip = DefaultClip.Res;
        // CollectionsMarshal.AsSpan: List<T>'s indexer would copy the value-type element, so
        // AssetRef.Res's internal caching would mutate a throwaway copy and never actually stick.
        if (clip == null && Clips.Count > 0)
            clip = CollectionsMarshal.AsSpan(Clips)[0].Res;
        return clip;
    }

    public override void Update()
    {
        // Deferred auto-play: the clip was still streaming in at OnEnable; start it now.
        if (_pendingAutoPlay)
        {
            var clip = ResolveAutoPlayClip();
            if (clip != null)
            {
                _pendingAutoPlay = false;
                Play(clip);
            }
        }

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
        // foreach over List<T> copies each element by value - .Res's caching would mutate the
        // throwaway copy and never stick, so use CollectionsMarshal to resolve against the real
        // backing elements (same reasoning as the DefaultClip.Res resolve above).
        var clips = CollectionsMarshal.AsSpan(Clips);
        for (int i = 0; i < clips.Length; i++)
        {
            var clip = clips[i].Res;
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

        ApplyBlendShapes(clip, time);
    }

    private void ApplyBlendShapes(AnimationClip clip, float time)
    {
        if (clip.BlendShapes.Count == 0) return;
        _blendShapeTargets ??= new Dictionary<string, SkinnedMeshRenderer?>();

        foreach (var track in clip.BlendShapes)
        {
            if (!_blendShapeTargets.TryGetValue(track.Path, out SkinnedMeshRenderer? smr))
            {
                // Resolve the renderer once. An empty path targets the cache root itself; otherwise
                // look it up via the bone-path cache (same relative-path convention).
                Transform? target = string.IsNullOrEmpty(track.Path)
                    ? _boneCacheRoot
                    : (_boneCache != null && _boneCache.TryGetValue(track.Path, out Transform? t) ? t : null);
                smr = target?.GameObject.GetComponent<SkinnedMeshRenderer>();
                _blendShapeTargets[track.Path] = smr;
            }
            smr?.SetBlendShapeWeight(track.ShapeName, track.EvaluateAt(time));
        }
    }

    private void EnsureBoneCache()
    {
        if (_boneCache != null) return;
        _boneCache = new Dictionary<string, Transform>();

        // Find the correct search root try ancestors until bone paths resolve.
        // This handles reparenting (model placed under another GO).
        Transform root = FindBoneCacheRoot();
        _boneCacheRoot = root;

        // Cache all descendants by their relative path from root (excluding root's name)
        foreach (var child in root.GameObject.Children)
            CacheBonesRecursive(child.Transform, "");
    }

    private Transform FindBoneCacheRoot()
    {
        // Walk up to absolute root
        Transform root = Transform;
        while (root.Parent != null) root = root.Parent;

        // If the clip has bones, verify the first bone path resolves from this root
        var clip = CurrentClip ?? DefaultClip.Res;
        if (clip != null && clip.Bones.Count > 0)
        {
            string testPath = clip.Bones[0].BoneName;
            // Try each ancestor from top down until the path resolves
            var ancestors = new List<Transform>();
            Transform current = Transform;
            while (current != null) { ancestors.Add(current); current = current.Parent; }

            for (int i = ancestors.Count - 1; i >= 0; i--)
            {
                // Build cache from this ancestor and check if test path exists
                foreach (var child in ancestors[i].GameObject.Children)
                {
                    if (child.Name == testPath.Split('/')[0])
                        return ancestors[i];
                }
            }
        }

        return root;
    }

    private void CacheBonesRecursive(Transform t, string parentPath)
    {
        string path = string.IsNullOrEmpty(parentPath)
            ? t.GameObject.Name
            : parentPath + "/" + t.GameObject.Name;

        // Don't overwrite first occurrence wins
        _boneCache.TryAdd(path, t);

        foreach (var child in t.GameObject.Children)
            CacheBonesRecursive(child.Transform, path);
    }
}
