using System;
using System.IO;

using Prowl.Motion;
using Prowl.Warp;
using Prowl.Vector;

namespace Prowl.Runtime.Networking;

/// <summary>A graph parameter value. The default is unset: a trigger or target, which isn't replicated as a value.</summary>
internal readonly struct AnimatorParameterValue(ParameterValue value) : IEquatable<AnimatorParameterValue>
{
    private const byte Unset = 0xFF;

    internal ParameterValue Value { get; } = value;

    internal bool IsSet { get; } = true;

    public bool Equals(AnimatorParameterValue other) => IsSet == other.IsSet && (!IsSet || Value.Type == other.Value.Type && Value.Type switch
    {
        AnimationValueType.Float => Value.Float.Equals(other.Value.Float),
        AnimationValueType.Bool => Value.Bool == other.Value.Bool,
        AnimationValueType.Int => Value.Int == other.Value.Int,
        AnimationValueType.Vector => Value.Vector == other.Value.Vector,
        AnimationValueType.Id => Value.Id == other.Value.Id,
        _ => false
    });

    public override bool Equals(object? obj) => obj is AnimatorParameterValue other && Equals(other);

    public override int GetHashCode() => !IsSet ? 0 : Value.Type switch
    {
        AnimationValueType.Float => HashCode.Combine(Value.Type, Value.Float),
        AnimationValueType.Bool => HashCode.Combine(Value.Type, Value.Bool),
        AnimationValueType.Int => HashCode.Combine(Value.Type, Value.Int),
        AnimationValueType.Vector => HashCode.Combine(Value.Type, Value.Vector),
        AnimationValueType.Id => HashCode.Combine(Value.Type, Value.Id.ID),
        _ => (int)Value.Type
    };

    public override string ToString() => !IsSet ? "—" : Value.Type switch
    {
        AnimationValueType.Float => Value.Float.ToString("0.###"),
        AnimationValueType.Bool => Value.Bool.ToString(),
        AnimationValueType.Int => Value.Int.ToString(),
        AnimationValueType.Vector => Value.Vector.ToString(),
        AnimationValueType.Id => $"#{Value.Id.ID}",
        _ => Value.Type.ToString()
    };

    internal static void Write(NetWriter writer, AnimatorParameterValue parameter)
    {
        if (!parameter.IsSet)
        {
            writer.WriteByte(Unset);
            return;
        }

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
        byte type = reader.ReadByte();
        if (type == Unset)
        {
            return default;
        }

        ParameterValue value = (AnimationValueType)type switch
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
        if (!IsSet) return;
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

/// <summary>
///     An animator's graph parameters by their index in the graph. One plain sync value, so it rides in snapshots and
///     the newest wins. Immutable: a change is a new value, which is how the sync member sees it changed.
/// </summary>
internal readonly struct AnimatorParameters(AnimatorParameterValue[] values) : IEquatable<AnimatorParameters>
{
    private readonly AnimatorParameterValue[]? _values = values;

    internal int Count => _values?.Length ?? 0;

    internal AnimatorParameterValue this[int index] => index < Count ? _values![index] : default;

    /// <summary>A copy to change, at least <paramref name="length" /> long.</summary>
    internal AnimatorParameterValue[] ToArray(int length)
    {
        var copy = new AnimatorParameterValue[Math.Max(length, Count)];
        _values?.CopyTo(copy, 0);
        return copy;
    }

    public bool Equals(AnimatorParameters other) =>
        ((ReadOnlySpan<AnimatorParameterValue>)_values).SequenceEqual(other._values);

    public override bool Equals(object? obj) => obj is AnimatorParameters other && Equals(other);

    public override int GetHashCode()
    {
        HashCode hash = default;
        foreach (AnimatorParameterValue value in _values ?? []) hash.Add(value);
        return hash.ToHashCode();
    }

    public override string ToString() => $"[{string.Join(", ", _values ?? [])}]";

    internal static void Write(NetWriter writer, AnimatorParameters parameters)
    {
        writer.WriteVarUInt32((uint)parameters.Count);
        foreach (AnimatorParameterValue value in parameters._values ?? []) AnimatorParameterValue.Write(writer, value);
    }

    internal static AnimatorParameters Read(ref NetReader reader)
    {
        uint count = reader.ReadVarUInt32();
        if (count > (uint)reader.Remaining)
            throw new InvalidDataException("Animator parameter count exceeds the remaining data.");
        var values = new AnimatorParameterValue[count];
        for (int i = 0; i < values.Length; i++) values[i] = AnimatorParameterValue.Read(ref reader);
        return new(values);
    }
}

/// <summary>
///     How many times each trigger has fired, by its index in the graph. A proxy fires the difference from what it last
///     saw, so the newest value alone carries every event. Immutable, like <see cref="AnimatorParameters" />.
/// </summary>
internal readonly struct AnimatorTriggers(uint[] revisions) : IEquatable<AnimatorTriggers>
{
    private readonly uint[]? _revisions = revisions;

    internal int Count => _revisions?.Length ?? 0;

    internal uint this[int index] => index < Count ? _revisions![index] : 0;

    internal AnimatorTriggers Fired(int index)
    {
        var copy = new uint[Math.Max(index + 1, Count)];
        _revisions?.CopyTo(copy, 0);
        copy[index] = unchecked(copy[index] + 1);
        return new(copy);
    }

    public bool Equals(AnimatorTriggers other) => ((ReadOnlySpan<uint>)_revisions).SequenceEqual(other._revisions);

    public override bool Equals(object? obj) => obj is AnimatorTriggers other && Equals(other);

    public override int GetHashCode()
    {
        HashCode hash = default;
        foreach (uint revision in _revisions ?? []) hash.Add(revision);
        return hash.ToHashCode();
    }

    public override string ToString() => $"[{string.Join(", ", _revisions ?? [])}]";

    internal static void Write(NetWriter writer, AnimatorTriggers triggers)
    {
        writer.WriteVarUInt32((uint)triggers.Count);
        foreach (uint revision in triggers._revisions ?? []) writer.WriteVarUInt32(revision);
    }

    internal static AnimatorTriggers Read(ref NetReader reader)
    {
        uint count = reader.ReadVarUInt32();
        if (count > (uint)reader.Remaining)
            throw new InvalidDataException("Animator trigger count exceeds the remaining data.");
        var revisions = new uint[count];
        for (int i = 0; i < revisions.Length; i++) revisions[i] = reader.ReadVarUInt32();
        return new(revisions);
    }
}
