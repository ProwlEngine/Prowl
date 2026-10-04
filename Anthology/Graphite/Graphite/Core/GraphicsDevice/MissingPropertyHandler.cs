namespace Prowl.Graphite;

/// <summary>
/// Fires when a reflected resource slot has no entry in the merged property table; a default gets substituted.
/// </summary>
/// <param name="program">Graphics or compute program being bound.</param>
/// <param name="name">Name of the missing resource.</param>
/// <param name="expectedKind">Resource kind the shader expects.</param>
/// <param name="set">Descriptor-set / register-space index.</param>
/// <param name="bindingIndex">Binding / register index in the set.</param>
public delegate void MissingPropertyHandler(
    ShaderProgram program,
    PropertyID name,
    ResourceKind expectedKind,
    uint set,
    int bindingIndex);
