// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Linq;

using Prowl.Echo;
namespace Prowl.Runtime;

public enum AnimationWrapMode
{
    Once,
    Loop,
    PingPong,
    ClampForever,
}

public sealed class AnimationClip : EngineObject, ISerializable
{
    public double Duration;
    public double TicksPerSecond;
    public double DurationInTicks;

    public AnimationWrapMode Wrap;

    public List<AnimBone> Bones { get; private set; } = [];

    private Dictionary<string, AnimBone> _boneMap = new Dictionary<string, AnimBone>();

    public void AddBone(AnimBone bone)
    {
        Bones.Add(bone);
        _boneMap[bone.BoneName] = bone;
    }

    public AnimBone? GetBone(string name)
    {
        if (_boneMap.TryGetValue(name, out var bone))
            return bone;
        return null;
    }


    public void EnsureQuaternionContinuity()
    {
        foreach (AnimBone bone in Bones)
        {
            // Store the previous quaternion value
            Quaternion prev = new Quaternion(
                bone.RotX.Keys[0].Value,
                bone.RotY.Keys[0].Value,
                bone.RotZ.Keys[0].Value,
                bone.RotW.Keys[0].Value
            );

            // Iterate through each keyframe starting from the second keyframe
            for (int i = 1; i < bone.RotX.Keys.Count; i++)
            {
                // Get the current quaternion value
                Quaternion cur = new Quaternion(
                    bone.RotX.Keys[i].Value,
                    bone.RotY.Keys[i].Value,
                    bone.RotZ.Keys[i].Value,
                    bone.RotW.Keys[i].Value
                );

                // Ensure quaternion continuity between the previous and current quaternions
                Quaternion midQ = (prev + cur) * 0.5f;
                Quaternion midQFlipped = (prev + (-cur)) * 0.5f;

                double angle = Quaternion.Angle(prev, midQ);
                double angleFlipped = Quaternion.Angle(prev, midQFlipped);
                Quaternion continuous = angleFlipped < angle ? (-cur) : cur;

                // Update the keyframe values with the continuous quaternion
                bone.RotX.Keys[i].Value = continuous.x;
                bone.RotY.Keys[i].Value = continuous.y;
                bone.RotZ.Keys[i].Value = continuous.z;
                bone.RotW.Keys[i].Value = continuous.w;

                // Store the current quaternion as the previous quaternion for the next iteration
                prev = continuous;
            }
        }
    }

    public void Deserialize(EchoObject value, SerializationContext ctx)
    {
        Name = value.Get("Name").StringValue;
        Duration = value.Get("Duration").DoubleValue;
        TicksPerSecond = value.Get("TicksPerSecond").DoubleValue;
        DurationInTicks = value.Get("DurationInTicks").DoubleValue;
        Wrap = (AnimationWrapMode)value.Get("Wrap").IntValue;

        var boneList = value.Get("Bones");
        foreach (var boneProp in boneList.List)
        {
            var bone = new AnimBone();
            bone.BoneName = boneProp.Get("BoneName").StringValue;

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

        // Created map
        _boneMap = Bones.ToDictionary(b => b.BoneName);
    }

    public void Serialize(ref EchoObject value, SerializationContext ctx)
    {
        value.Add("Name", new EchoObject(Name));
        value.Add("Duration", new EchoObject(Duration));
        value.Add("TicksPerSecond", new EchoObject(TicksPerSecond));
        value.Add("DurationInTicks", new EchoObject(DurationInTicks));
        value.Add("Wrap", new EchoObject((int)Wrap));

        var boneList = EchoObject.NewList();
        foreach (var bone in Bones)
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


    public class AnimBone
    {
        public string BoneName;

        public AnimationCurve PosX, PosY, PosZ;
        public AnimationCurve RotX, RotY, RotZ, RotW;
        public AnimationCurve ScaleX, ScaleY, ScaleZ;

        public Vector3 EvaluatePositionAt(double time)
            => new Vector3(PosX.Evaluate(time), PosY.Evaluate(time), PosZ.Evaluate(time));

        public Quaternion EvaluateRotationAt(double time)
            => new Quaternion(RotX.Evaluate(time), RotY.Evaluate(time), RotZ.Evaluate(time), RotW.Evaluate(time));

        public Vector3 EvaluateScaleAt(double time)
            => new Vector3(ScaleX.Evaluate(time), ScaleY.Evaluate(time), ScaleZ.Evaluate(time));
    }


}
