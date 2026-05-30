namespace Prowl.Veldrid;

/// <summary>
/// Callback invoked by a backend when, at draw or dispatch time, a reflected resource slot has no matching
/// entry in the merged property table and a kind-specific default is substituted.
/// </summary>
/// <param name="shader">The <see cref="GraphicsProgram"/> being bound, or null if this is a compute dispatch.</param>
/// <param name="compute">The <see cref="ComputeProgram"/> being bound, or null if this is a graphics draw.</param>
/// <param name="name">The reflected name of the missing resource element.</param>
/// <param name="expectedKind">The <see cref="ResourceKind"/> the shader declares for this element.</param>
/// <param name="set">The descriptor-set or register-space index this element belongs to.</param>
/// <param name="bindingIndex">The binding / register index within the set.</param>
public delegate void MissingPropertyHandler(
    GraphicsProgram? shader,
    ComputeProgram? compute,
    PropertyID name,
    ResourceKind expectedKind,
    uint set,
    int bindingIndex);
