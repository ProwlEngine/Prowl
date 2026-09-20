using Prowl.Vector;

namespace Prowl.Motion;

/// <summary>The data types a graph value node / control parameter can carry.</summary>
public enum AnimationValueType : byte
{
    Bool,
    Int,
    Float,
    Vector,
    Target,
    Id
}

/// <summary>
/// A tagged value flowing along the graph's value pins (control parameters, comparisons, math, etc.).
/// A plain struct so graph definitions stay serialization-friendly for the editor.
/// </summary>
public struct ParameterValue
{
    public AnimationValueType Type;
    public bool Bool;
    public int Int;
    public float Float;
    public Float3 Vector;
    public Target Target;
    public StringID Id;

    public static ParameterValue FromBool(bool v) => new() { Type = AnimationValueType.Bool, Bool = v };
    public static ParameterValue FromInt(int v) => new() { Type = AnimationValueType.Int, Int = v };
    public static ParameterValue FromFloat(float v) => new() { Type = AnimationValueType.Float, Float = v };
    public static ParameterValue FromVector(Float3 v) => new() { Type = AnimationValueType.Vector, Vector = v };
    public static ParameterValue FromTarget(Target v) => new() { Type = AnimationValueType.Target, Target = v };
    public static ParameterValue FromId(StringID v) => new() { Type = AnimationValueType.Id, Id = v };

    public readonly StringID AsId() => Type == AnimationValueType.Id ? Id : StringID.Invalid;

    /// <summary>Reads the value as a float, coercing bool/int where reasonable.</summary>
    public readonly float AsFloat() => Type switch
    {
        AnimationValueType.Float => Float,
        AnimationValueType.Int => Int,
        AnimationValueType.Bool => Bool ? 1f : 0f,
        _ => 0f
    };

    public readonly bool AsBool() => Type switch
    {
        AnimationValueType.Bool => Bool,
        AnimationValueType.Int => Int != 0,
        AnimationValueType.Float => Float != 0f,
        _ => false
    };

    public readonly int AsInt() => Type switch
    {
        AnimationValueType.Int => Int,
        AnimationValueType.Float => (int)Float,
        AnimationValueType.Bool => Bool ? 1 : 0,
        _ => 0
    };
}
