// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
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
    public float Duration;
    public float TicksPerSecond = 1f;
    public float DurationInTicks;

    public AnimationWrapMode Wrap;

    public List<AnimBone> Bones { get; private set; } = [];

    private Dictionary<string, AnimBone> _boneMap = [];

    public void AddBone(AnimBone bone)
    {
        Bones.Add(bone);
        _boneMap[bone.BoneName] = bone;
    }

    public AnimBone? GetBone(string name)
    {
        if (_boneMap.TryGetValue(name, out AnimBone? bone))
            return bone;
        return null;
    }

    public void EnsureQuaternionContinuity()
    {
        foreach (AnimBone bone in Bones)
        {
            if (bone.RotX == null || bone.RotX.Keys.Count < 2) continue;

            Quaternion prev = new(
                bone.RotX.Keys[0].Value,
                bone.RotY.Keys[0].Value,
                bone.RotZ.Keys[0].Value,
                bone.RotW.Keys[0].Value
            );

            for (int i = 1; i < bone.RotX.Keys.Count; i++)
            {
                Quaternion cur = new(
                    bone.RotX.Keys[i].Value,
                    bone.RotY.Keys[i].Value,
                    bone.RotZ.Keys[i].Value,
                    bone.RotW.Keys[i].Value
                );

                Quaternion midQ = (prev + cur) * 0.5f;
                Quaternion midQFlipped = (prev + (-cur)) * 0.5f;

                float angle = Quaternion.Angle(prev, midQ);
                float angleFlipped = Quaternion.Angle(prev, midQFlipped);
                Quaternion continuous = angleFlipped < angle ? (-cur) : cur;

                bone.RotX.Keys[i].Value = continuous.X;
                bone.RotY.Keys[i].Value = continuous.Y;
                bone.RotZ.Keys[i].Value = continuous.Z;
                bone.RotW.Keys[i].Value = continuous.W;

                prev = continuous;
            }
        }
    }

    public void Deserialize(EchoObject value, SerializationContext ctx)
    {
        Name = value.Get("Name")?.StringValue ?? "Animation";
        Duration = value.Get("Duration")?.FloatValue ?? 0;
        TicksPerSecond = value.Get("TicksPerSecond")?.FloatValue ?? 1;
        DurationInTicks = value.Get("DurationInTicks")?.FloatValue ?? 0;
        Wrap = (AnimationWrapMode)(value.Get("Wrap")?.IntValue ?? 0);

        EchoObject? boneList = value.Get("Bones");
        if (boneList != null)
        {
            foreach (EchoObject boneProp in boneList.List)
            {
                var bone = new AnimBone();
                bone.BoneName = boneProp.Get("BoneName")?.StringValue ?? "";

                bone.PosX = Serializer.Deserialize<AnimationCurve>(boneProp.Get("PosX"), ctx);
                bone.PosY = Serializer.Deserialize<AnimationCurve>(boneProp.Get("PosY"), ctx);
                bone.PosZ = Serializer.Deserialize<AnimationCurve>(boneProp.Get("PosZ"), ctx);

                bone.RotX = Serializer.Deserialize<AnimationCurve>(boneProp.Get("RotX"), ctx);
                bone.RotY = Serializer.Deserialize<AnimationCurve>(boneProp.Get("RotY"), ctx);
                bone.RotZ = Serializer.Deserialize<AnimationCurve>(boneProp.Get("RotZ"), ctx);
                bone.RotW = Serializer.Deserialize<AnimationCurve>(boneProp.Get("RotW"), ctx);

                bone.ScaleX = Serializer.Deserialize<AnimationCurve>(boneProp.Get("ScaleX"), ctx);
                bone.ScaleY = Serializer.Deserialize<AnimationCurve>(boneProp.Get("ScaleY"), ctx);
                bone.ScaleZ = Serializer.Deserialize<AnimationCurve>(boneProp.Get("ScaleZ"), ctx);

                Bones.Add(bone);
            }

            _boneMap = Bones.ToDictionary(b => b.BoneName);
        }
    }

    public void Serialize(ref EchoObject value, SerializationContext ctx)
    {
        value.Add("Name", new EchoObject(Name));
        value.Add("Duration", new EchoObject(Duration));
        value.Add("TicksPerSecond", new EchoObject(TicksPerSecond));
        value.Add("DurationInTicks", new EchoObject(DurationInTicks));
        value.Add("Wrap", new EchoObject((int)Wrap));

        var boneList = EchoObject.NewList();
        foreach (AnimBone bone in Bones)
        {
            var boneProp = EchoObject.NewCompound();
            boneProp.Add("BoneName", new EchoObject(bone.BoneName));

            boneProp.Add("PosX", Serializer.Serialize(bone.PosX, ctx));
            boneProp.Add("PosY", Serializer.Serialize(bone.PosY, ctx));
            boneProp.Add("PosZ", Serializer.Serialize(bone.PosZ, ctx));

            boneProp.Add("RotX", Serializer.Serialize(bone.RotX, ctx));
            boneProp.Add("RotY", Serializer.Serialize(bone.RotY, ctx));
            boneProp.Add("RotZ", Serializer.Serialize(bone.RotZ, ctx));
            boneProp.Add("RotW", Serializer.Serialize(bone.RotW, ctx));

            boneProp.Add("ScaleX", Serializer.Serialize(bone.ScaleX, ctx));
            boneProp.Add("ScaleY", Serializer.Serialize(bone.ScaleY, ctx));
            boneProp.Add("ScaleZ", Serializer.Serialize(bone.ScaleZ, ctx));

            boneList.ListAdd(boneProp);
        }
        value.Add("Bones", boneList);
    }


    /// <summary>Per-bone animation data with separate curves for each transform component.</summary>
    public class AnimBone
    {
        public string BoneName;

        public AnimationCurve PosX, PosY, PosZ;
        public AnimationCurve RotX, RotY, RotZ, RotW;
        public AnimationCurve ScaleX, ScaleY, ScaleZ;

        public Float3 EvaluatePositionAt(float time)
        {
            if (PosX == null || PosY == null || PosZ == null) return Float3.Zero;
            return new(PosX.Evaluate(time), PosY.Evaluate(time), PosZ.Evaluate(time));
        }

        public Quaternion EvaluateRotationAt(float time)
        {
            if (RotX == null || RotX.Keys.Count == 0)
                return Quaternion.Identity;

            if (RotX.Keys.Count == 1)
            {
                return NormalizeQuaternion(new Quaternion(
                    RotX.Keys[0].Value,
                    RotY.Keys[0].Value,
                    RotZ.Keys[0].Value,
                    RotW.Keys[0].Value
                ));
            }

            // Evaluate each component at the given time
            float x = RotX.Evaluate(time);
            float y = RotY.Evaluate(time);
            float z = RotZ.Evaluate(time);
            float w = RotW.Evaluate(time);

            // Normalize (curve interpolation may denormalize the quaternion)
            return NormalizeQuaternion(new Quaternion(x, y, z, w));
        }

        public Float3 EvaluateScaleAt(float time)
        {
            if (ScaleX == null || ScaleY == null || ScaleZ == null) return Float3.One;
            return new(ScaleX.Evaluate(time), ScaleY.Evaluate(time), ScaleZ.Evaluate(time));
        }

        private static Quaternion NormalizeQuaternion(Quaternion q)
        {
            float length = Maths.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W);
            if (length < 1e-6f) return Quaternion.Identity;
            float invLength = 1.0f / length;
            return new Quaternion(
                q.X * invLength,
                q.Y * invLength,
                q.Z * invLength,
                q.W * invLength
            );
        }
    }
}
