namespace Prowl.Graphite;

/// <summary>How often a vertex buffer advances.</summary>
public enum VertexStepRate
{
    /// <summary>Advance once per vertex.</summary>
    PerVertex,

    /// <summary>Advance once per instance.</summary>
    PerInstance,
}
