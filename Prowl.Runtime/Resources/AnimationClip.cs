// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Linq;

using Prowl.Echo;
using Prowl.Vector;

namespace Prowl.Runtime;

public enum AnimationWrapMode
{
    Once,
    Loop,
    PingPong,
    ClampForever,
}

/// <summary>
/// An animation clip containing per-bone animation curves.
/// The Animation component evaluates these curves and applies them to bone Transforms by name.
/// No Skeleton reference needed bones are resolved by name at runtime.
/// </summary>
public sealed class AnimationClip : EngineObject, ISerializable
{
    private float _startTime;
    private float _duration;
    private float _ticksPerSecond = 1f;
    private float _durationInTicks;
    private AnimationWrapMode _wrap;

    /// <summary>
    /// Time of the clip's first key. Usually zero, but a clip authored on a shared timeline can
    /// start later, and playing it from zero would sit on its first pose for the gap.
    /// </summary>
    public float StartTime { get { EnsureNotDisposed(); return _startTime; } set { EnsureNotDisposed(); _startTime = value; } }

    public float Duration { get { EnsureNotDisposed(); return _duration; } set { EnsureNotDisposed(); _duration = value; } }
    public float TicksPerSecond { get { EnsureNotDisposed(); return _ticksPerSecond; } set { EnsureNotDisposed(); _ticksPerSecond = value; } }
    public float DurationInTicks { get { EnsureNotDisposed(); return _durationInTicks; } set { EnsureNotDisposed(); _durationInTicks = value; } }
    public AnimationWrapMode Wrap { get { EnsureNotDisposed(); return _wrap; } set { EnsureNotDisposed(); _wrap = value; } }

    private List<AnimBone> _bones = [];
    public List<AnimBone> Bones { get { EnsureNotDisposed(); return _bones; } }

    /// <summary>Blend-shape weight tracks. Each targets a renderer (by path) and a named blend shape.</summary>
    private List<BlendShapeAnim> _blendShapes = [];
    public List<BlendShapeAnim> BlendShapes { get { EnsureNotDisposed(); return _blendShapes; } }

    private Dictionary<string, AnimBone> _boneMap = [];

    public void AddBone(AnimBone bone)
    {
        EnsureNotDisposed();
        Bones.Add(bone);
        _boneMap[bone.BoneName] = bone;
    }

    public void AddBlendShape(BlendShapeAnim blendShape) { EnsureNotDisposed(); BlendShapes.Add(blendShape); }

    public AnimBone? GetBone(string name)
    {
        EnsureNotDisposed();
        if (_boneMap.TryGetValue(name, out AnimBone? bone))
            return bone;
        return null;
    }

    /// <summary>
    /// Flips rotation keys so no two adjacent ones sit more than a half turn apart. Delegates to the
    /// curve, which owns the rule.
    /// </summary>
    public void EnsureQuaternionContinuity()
    {
        EnsureNotDisposed();
        foreach (AnimBone bone in Bones)
            bone.Rotation?.EnsureQuaternionContinuity();
    }

    public void Deserialize(EchoObject value, SerializationContext ctx)
    {
        Name = value.Get("Name")?.StringValue ?? "Animation";
        StartTime = value.Get("StartTime")?.FloatValue ?? 0;
        Duration = value.Get("Duration")?.FloatValue ?? 0;
        TicksPerSecond = value.Get("TicksPerSecond")?.FloatValue ?? 1;
        DurationInTicks = value.Get("DurationInTicks")?.FloatValue ?? 0;
        Wrap = (AnimationWrapMode)(value.Get("Wrap")?.IntValue ?? 0);

        EchoObject? boneList = value.Get("Bones");
        if (boneList != null)
        {
            foreach (EchoObject boneProp in boneList.List)
            {
                Bones.Add(new AnimBone
                {
                    BoneName = boneProp.Get("BoneName")?.StringValue ?? "",
                    Position = Serializer.Deserialize<AnimationCurve>(boneProp.Get("Position"), ctx),
                    Rotation = Serializer.Deserialize<AnimationCurve>(boneProp.Get("Rotation"), ctx),
                    Scale = Serializer.Deserialize<AnimationCurve>(boneProp.Get("Scale"), ctx),
                });
            }

            _boneMap = Bones.ToDictionary(b => b.BoneName);
        }

        EchoObject? bsList = value.Get("BlendShapes");
        if (bsList != null)
        {
            foreach (EchoObject bsProp in bsList.List)
            {
                BlendShapes.Add(new BlendShapeAnim
                {
                    Path = bsProp.Get("Path")?.StringValue ?? "",
                    ShapeName = bsProp.Get("ShapeName")?.StringValue ?? "",
                    Weight = Serializer.Deserialize<AnimationCurve>(bsProp.Get("Weight"), ctx),
                });
            }
        }
    }

    public void Serialize(ref EchoObject value, SerializationContext ctx)
    {
        value.Add("Name", new EchoObject(Name));
        value.Add("StartTime", new EchoObject(StartTime));
        value.Add("Duration", new EchoObject(Duration));
        value.Add("TicksPerSecond", new EchoObject(TicksPerSecond));
        value.Add("DurationInTicks", new EchoObject(DurationInTicks));
        value.Add("Wrap", new EchoObject((int)Wrap));

        var boneList = EchoObject.NewList();
        foreach (AnimBone bone in Bones)
        {
            var boneProp = EchoObject.NewCompound();
            boneProp.Add("BoneName", new EchoObject(bone.BoneName));
            boneProp.Add("Position", Serializer.Serialize(bone.Position, ctx));
            boneProp.Add("Rotation", Serializer.Serialize(bone.Rotation, ctx));
            boneProp.Add("Scale", Serializer.Serialize(bone.Scale, ctx));
            boneList.ListAdd(boneProp);
        }
        value.Add("Bones", boneList);

        var bsList = EchoObject.NewList();
        foreach (BlendShapeAnim bs in BlendShapes)
        {
            var bsProp = EchoObject.NewCompound();
            bsProp.Add("Path", new EchoObject(bs.Path));
            bsProp.Add("ShapeName", new EchoObject(bs.ShapeName));
            bsProp.Add("Weight", Serializer.Serialize(bs.Weight, ctx));
            bsList.ListAdd(bsProp);
        }
        value.Add("BlendShapes", bsList);
    }

    /// <summary>
    /// A blend-shape weight track. <see cref="Path"/> is the renderer GameObject's path relative to the
    /// animation root; <see cref="ShapeName"/> selects the blend shape on that renderer's mesh.
    /// </summary>
    public class BlendShapeAnim
    {
        public string Path = string.Empty;
        public string ShapeName = string.Empty;
        public AnimationCurve? Weight;

        public float EvaluateAt(float time) => Weight is { Count: > 0 } ? Weight.Evaluate(time) : 0f;
    }

    /// <summary>
    /// Per-bone animation, one curve per transform channel.
    /// </summary>
    /// <remarks>
    /// One multi-component curve per channel rather than one scalar curve per axis. That is what lets
    /// a rotation slerp as a rotation instead of drifting off the unit sphere between keys, and it is
    /// what makes a stepped or cubic channel keep its shape: interpolation and tangents live on the
    /// curve, so splitting a channel across three or four of them would have to pick one.
    /// </remarks>
    public class AnimBone
    {
        public string BoneName = string.Empty;

        /// <summary>Three-component local position curve.</summary>
        public AnimationCurve? Position;

        /// <summary>Four-component local rotation curve, XYZW.</summary>
        public AnimationCurve? Rotation;

        /// <summary>Three-component local scale curve.</summary>
        public AnimationCurve? Scale;

        public Float3 EvaluatePositionAt(float time) =>
            Position is { Count: > 0 } ? Position.EvaluateFloat3(time) : Float3.Zero;

        /// <summary>
        /// Evaluates the rotation. Linear segments slerp and cubic segments are renormalised, both
        /// handled by the curve, so nothing here has to re-normalise after the fact.
        /// </summary>
        public Quaternion EvaluateRotationAt(float time) =>
            Rotation is { Count: > 0 } ? Rotation.EvaluateQuaternion(time) : Quaternion.Identity;

        public Float3 EvaluateScaleAt(float time) =>
            Scale is { Count: > 0 } ? Scale.EvaluateFloat3(time) : Float3.One;
    }
}
