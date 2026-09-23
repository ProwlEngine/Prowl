namespace Prowl.Motion;

/// <summary>
/// A number a node is built with: a fixed value, or a value node read every update. Settings that take
/// one can be tuned in the graph and driven from the game without the node knowing the difference.
/// </summary>
public readonly struct FloatInput
{
    // Held one past the node index so the default of this struct is an undriven zero rather than a
    // value driven by node 0.
    private readonly int _node;

    private FloatInput(float constant, int nodeIndex)
    {
        Constant = constant;
        _node = nodeIndex + 1;
    }

    /// <summary>The value used when no node drives this input.</summary>
    public float Constant { get; }

    /// <summary>The value node driving this input, or -1 for the fixed value.</summary>
    public int NodeIndex => _node - 1;

    public bool IsDriven => _node > 0;

    public static FloatInput Of(float value) => new(value, -1);

    /// <summary>Driven by a value node, falling back to <paramref name="fallback"/> when it is not one.</summary>
    public static FloatInput From(int nodeIndex, float fallback = 0f) => new(fallback, nodeIndex);

    public static implicit operator FloatInput(float value) => Of(value);
}

/// <summary>A <see cref="FloatInput"/> after binding, which is how a node reads it each update.</summary>
public struct BoundFloat
{
    private ValueNodeInstance? _node;
    private float _constant;

    public static BoundFloat Bind(GraphBindContext context, in FloatInput input)
        => new() { _constant = input.Constant, _node = context.OptionalValueNode(input.NodeIndex, ValueInputKind.Number) };

    public readonly float Get(GraphContext context) => _node is null ? _constant : _node.GetValue(context).AsFloat();
}
