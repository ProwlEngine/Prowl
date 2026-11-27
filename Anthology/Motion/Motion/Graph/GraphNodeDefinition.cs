namespace Prowl.Motion;

/// <summary>
/// Base for a graph node's authored definition (config + child indices). Definitions are plain data
/// shared by every instance of the graph and are what an editor authors / serializes.
/// </summary>
public abstract class GraphNodeDefinition
{
    /// <summary>Index of this node in its owning <see cref="AnimationGraph"/>.</summary>
    public int Index { get; internal set; } = -1;

    /// <summary>Optional author-facing name (for editors and debugging).</summary>
    public string? Name { get; set; }

    /// <summary>Creates the runtime instance for this node.</summary>
    public abstract GraphNodeInstance CreateInstance();
}

/// <summary>A node that produces a pose.</summary>
public abstract class PoseNodeDefinition : GraphNodeDefinition
{
}

/// <summary>A node that produces a typed value.</summary>
public abstract class ValueNodeDefinition : GraphNodeDefinition
{
    public abstract AnimationValueType ValueType { get; }
}

/// <summary>A node that produces a <see cref="BoneMask"/> (a per-bone weight set).</summary>
public abstract class BoneMaskNodeDefinition : GraphNodeDefinition
{
}
