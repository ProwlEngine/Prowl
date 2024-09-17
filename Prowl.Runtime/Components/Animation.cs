// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Linq;

using Prowl.Icons;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.PersonRunning}  Animation/{FontAwesome6.PersonRunning}  Animation")]
public class Animation : MonoBehaviour
{

    public readonly List<AssetRef<AnimationClip>> Clips = [];
    public AssetRef<AnimationClip> DefaultClip;
    public readonly bool PlayAutomatically = true;
    public readonly double Speed = 1.0;

    private readonly List<AnimationState> _states = new List<AnimationState>();
    private readonly Dictionary<string, AnimationState> _stateDictionary = new Dictionary<string, AnimationState>();

    private readonly List<Transform> transforms = [];

    public override void OnEnable()
    {
        // Assign DefaultClip to the first clip if it's not set
        if (!DefaultClip.IsAvailable && Clips.Count > 0)
            DefaultClip = Clips[0];

        foreach (var clip in Clips)
            if (clip.IsAvailable)
                AddClip(clip.Res!);
        if (DefaultClip.IsAvailable)
        {
            AddClip(DefaultClip.Res!);
            if (PlayAutomatically)
                Play(DefaultClip.Res!.Name);
        }
    }

    public override void Update()
    {
        foreach (var state in _states)
        {
            if (state.Enabled)
            {
                state.Time += state.Speed * Speed * Time.deltaTimeF;

                if (state.Time >= state.Length)
                {
                    if (state.Wrap == AnimationWrapMode.Loop)
                        state.Time = 0.0f;
                    else if (state.Wrap == AnimationWrapMode.PingPong)
                        state.Speed = -state.Speed;
                    else if (state.Wrap == AnimationWrapMode.ClampForever)
                    {
                        state.Time = state.Length;
                    }
                    else
                    {
                        state.Time = 0;
                        state.Enabled = false;
                    }
                }
            }

            // Weight always update even if the state is disabled
            state.Weight = MathD.MoveTowards(state.Weight, state.TargetWeight, state.MoveWeightSpeed * Time.deltaTimeF);
        }

        if (_states.Where(s => s.Enabled).Sum(s => s.Weight) <= 0)
        {
            // Either all disabled or all weights are zero
            return;
        }

        // Normalize weights for Blend states
        var totalBlendWeight = _states.Where(s => s.Enabled && s.Blend == AnimationState.BlendMode.Blend).Sum(s => s.Weight);
        var blendNormalizer = totalBlendWeight > 0 ? 1.0 / totalBlendWeight : 0;

        // Update all transforms
        foreach (var transform in transforms)
        {
            var position = Vector3.zero;
            var rotation = Quaternion.identity;
            var scale = Vector3.one;

            if (blendNormalizer > 0)
            {
                // Process Blend states
                foreach (var state in _states.Where(s => s.Enabled && s.Blend == AnimationState.BlendMode.Blend))
                {
                    var normalizedWeight = state.Weight * blendNormalizer;

                    var pos = state.EvaluatePosition(transform, state.Time);
                    if (pos.HasValue)
                        position += pos.Value * (float)normalizedWeight;

                    var rot = state.EvaluateRotation(transform, state.Time);
                    if (rot.HasValue)
                        rotation = Quaternion.Slerp(rotation, rot.Value, (float)normalizedWeight);

                    var scl = state.EvaluateScale(transform, state.Time);
                    if (scl.HasValue)
                        scale = Vector3.Lerp(scale, scl.Value, (float)normalizedWeight);
                }
            }

            // Process Additive states
            foreach (var state in _states.Where(s => s.Enabled && s.Blend == AnimationState.BlendMode.Additive))
            {
                var pos = state.EvaluatePosition(transform, state.Time);
                if (pos.HasValue)
                    position += pos.Value * (float)state.Weight;

                var rot = state.EvaluateRotation(transform, state.Time);
                if (rot.HasValue)
                    rotation *= Quaternion.Slerp(Quaternion.identity, rot.Value, (float)state.Weight);

                var scl = state.EvaluateScale(transform, state.Time);
                if (scl.HasValue)
                    scale = Vector3.Lerp(scale, scale * scl.Value, (float)state.Weight);
            }

            transform.localPosition = position;
            transform.localRotation = rotation;
            transform.localScale = scale;
        }


    }

    public void Blend(string clipName, double targetWeight, double fadeLength = 0.3f)
    {
        if (_stateDictionary.TryGetValue(clipName, out var state))
        {
            state.TargetWeight = targetWeight;
            state.MoveWeightSpeed = 1.0f / fadeLength;
        }
    }

    public void CrossFade(string clipName, double fadeLength = 0.3f)
    {
        // Set all target weights to 0, and assign movespeed according to fadeLength
        foreach (var state in _states)
        {
            state.TargetWeight = state.Name.Equals(clipName, System.StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.0;
            state.MoveWeightSpeed = 1.0f / fadeLength;
        }
    }

    public void Play(string stateName)
    {
        if (_stateDictionary.TryGetValue(stateName, out var state))
        {
            state.Enabled = true;
            state.Time = 0.0f;
        }
    }

    public void Stop(string stateName)
    {
        if (_stateDictionary.TryGetValue(stateName, out var state))
        {
            state.Enabled = false;
            state.Time = 0.0f;
        }
    }

    public void StopAll()
    {
        foreach (var state in _states)
        {
            state.Enabled = false;
            state.Time = 0.0f;
        }
    }

    public void AddClip(AnimationClip clip)
    {
        if (_stateDictionary.ContainsKey(clip.Name))
            return;
        _states.Add(new AnimationState(clip.Name, clip));
        _stateDictionary[clip.Name] = _states[_states.Count - 1];

        // Find all bone names used by the clip
        foreach (var bone in clip.Bones)
        {
            var t = GameObject.Transform.DeepFind(bone.BoneName);
            if (t == null)
                continue;
            if (!transforms.Contains(t))
                transforms.Add(t);
        }
    }

    public void RemoveClip(string stateName)
    {
        if (_stateDictionary.TryGetValue(stateName, out var state))
        {
            _states.Remove(state);
            _stateDictionary.Remove(stateName);
        }
    }
}


public class AnimationState
{
    public readonly string Name;
    public readonly AnimationClip Clip;
    public bool Enabled;
    public double Length => Clip.Duration;
    public double NormalizedTime => Time / Length;
    public double Speed = 1.0;
    public double Time = 0;
    public double Weight = 1.0;
    public double MoveWeightSpeed = 1.0;
    public double TargetWeight = 1.0;

    public readonly AnimationWrapMode Wrap = AnimationWrapMode.Loop;

    public readonly HashSet<string> MixingTransforms = new HashSet<string>();

    public enum BlendMode
    {
        Blend,
        Additive,
    }

    public readonly BlendMode Blend = BlendMode.Blend;

    public AnimationState(string name, AnimationClip clip)
    {
        Name = name;
        Clip = clip;
    }

    public Vector3? EvaluatePosition(Transform target, double time)
    {
        // If MixingTransforms has elements, ensure target is in the list, its like a Whitelist for an animation clip
        if (MixingTransforms.Count > 0)
        {
            // Ensure Target exists inside MixingTransforms
            if (!MixingTransforms.Contains(target.gameObject.Name))
                return null;
        }

        var bone = Clip.GetBone(target.gameObject.Name);
        return bone?.EvaluatePositionAt(time);
    }

    public Quaternion? EvaluateRotation(Transform target, double time)
    {
        // If MixingTransforms has elements, ensure target is in the list, its like a Whitelist for an animation clip
        if (MixingTransforms.Count > 0)
        {
            // Ensure Target exists inside MixingTransforms
            if (!MixingTransforms.Contains(target.gameObject.Name))
                return null;
        }

        var bone = Clip.GetBone(target.gameObject.Name);
        return bone?.EvaluateRotationAt(time);
    }

    public Vector3? EvaluateScale(Transform target, double time)
    {
        // If MixingTransforms has elements, ensure target is in the list, its like a Whitelist for an animation clip
        if (MixingTransforms.Count > 0)
        {
            // Ensure Target exists inside MixingTransforms
            if (!MixingTransforms.Contains(target.gameObject.Name))
                return null;
        }

        var bone = Clip.GetBone(target.gameObject.Name);
        return bone?.EvaluateScaleAt(time);
    }

    public void AddMixingTransform(Transform transform, bool recursive)
    {
        MixingTransforms.Add(transform.gameObject.Name);
        if (recursive)
        {
            foreach (var child in transform.gameObject.children)
                AddMixingTransform(child.Transform, true);
        }
    }

    public void RemoveMixingTransform(Transform transform, bool recursive)
    {
        MixingTransforms.Remove(transform.gameObject.Name);
        if (recursive)
        {
            foreach (var child in transform.gameObject.children)
                RemoveMixingTransform(child.Transform, true);
        }
    }
}
