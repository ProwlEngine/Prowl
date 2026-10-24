using System;
using System.IO;

using Prowl.Motion;
using Prowl.Warp;
using Prowl.Vector;

namespace Prowl.Runtime.Networking;

/// <summary>A graph parameter value, carried by the animator's replicated parameter dictionary.</summary>
internal readonly struct AnimatorParameterValue(ParameterValue value) : IEquatable<AnimatorParameterValue>
{
    internal ParameterValue Value { get; } = value;

    public bool Equals(AnimatorParameterValue other) => Value.Type == other.Value.Type && Value.Type switch
    {
        AnimationValueType.Float => Value.Float.Equals(other.Value.Float),
        AnimationValueType.Bool => Value.Bool == other.Value.Bool,
        AnimationValueType.Int => Value.Int == other.Value.Int,
        AnimationValueType.Vector => Value.Vector == other.Value.Vector,
        AnimationValueType.Id => Value.Id == other.Value.Id,
        _ => false
    };

    internal static void Write(NetWriter writer, AnimatorParameterValue parameter)
    {
        ParameterValue value = parameter.Value;
        writer.WriteByte((byte)value.Type);
        switch (value.Type)
        {
            case AnimationValueType.Float: writer.WriteSingle(value.Float); break;
            case AnimationValueType.Bool: writer.WriteBool(value.Bool); break;
            case AnimationValueType.Int: writer.WriteVarInt32(value.Int); break;
            case AnimationValueType.Vector: NetSerializer<Float3>.Write(writer, value.Vector); break;
            case AnimationValueType.Id: writer.WriteUInt32(value.Id.ID); break;
            default: throw new InvalidDataException("Unsupported animation parameter type.");
        }
    }

    internal static AnimatorParameterValue Read(ref NetReader reader)
    {
        var type = (AnimationValueType)reader.ReadByte();
        ParameterValue value = type switch
        {
            AnimationValueType.Float => ParameterValue.FromFloat(reader.ReadSingle()),
            AnimationValueType.Bool => ParameterValue.FromBool(reader.ReadBool()),
            AnimationValueType.Int => ParameterValue.FromInt(reader.ReadVarInt32()),
            AnimationValueType.Vector => ParameterValue.FromVector(NetSerializer<Float3>.Read(ref reader)),
            AnimationValueType.Id => ParameterValue.FromId(new StringID(reader.ReadUInt32())),
            _ => throw new InvalidDataException("Unsupported animation parameter type.")
        };
        return new(value);
    }

    internal void Apply(Animator animator, string name)
    {
        switch (Value.Type)
        {
            case AnimationValueType.Float: animator.SetFloat(name, Value.Float); break;
            case AnimationValueType.Bool: animator.SetBool(name, Value.Bool); break;
            case AnimationValueType.Int: animator.SetInt(name, Value.Int); break;
            case AnimationValueType.Vector: animator.SetVector(name, Value.Vector); break;
            case AnimationValueType.Id: animator.SetId(name, Value.Id); break;
        }
    }
}
