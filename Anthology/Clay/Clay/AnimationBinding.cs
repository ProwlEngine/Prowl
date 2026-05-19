namespace Prowl.Clay;

/// <summary>
/// One curve binding inside an <see cref="AnimationClip"/>: which node, which property, what curve.
/// </summary>
public sealed class AnimationBinding
{
    /// <summary>Index into <see cref="Model.Nodes"/> of the node whose property this curve drives.</summary>
    public required int NodeIndex { get; init; }

    /// <summary>Which property is being animated.</summary>
    public required AnimatedProperty Property { get; init; }

    /// <summary>
    /// Sub-property index. Currently used only when <see cref="Property"/> is
    /// <see cref="AnimatedProperty.BlendShapeWeight"/>, where it selects which blend shape on the
    /// referenced mesh is animated.
    /// </summary>
    public int SubIndex { get; init; }

    /// <summary>The keyframed curve.</summary>
    public required AnimationCurve Curve { get; init; }
}
