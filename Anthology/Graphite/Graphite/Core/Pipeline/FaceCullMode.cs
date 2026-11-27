namespace Prowl.Graphite;

/// <summary>Which face gets culled.</summary>
public enum FaceCullMode : byte
{
    /// <summary>Back face.</summary>
    Back,
    /// <summary>Front face.</summary>
    Front,
    /// <summary>No culling.</summary>
    None,
}
