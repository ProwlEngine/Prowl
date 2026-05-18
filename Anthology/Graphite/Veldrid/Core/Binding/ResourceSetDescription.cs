using System;

namespace Prowl.Veldrid;

/// <summary>
/// Describes a <see cref="ResourceSet"/>, for creation using a <see cref="ResourceFactory"/>.
/// </summary>
public struct ResourceSetDescription : IEquatable<ResourceSetDescription>
{
    /// <summary>
    /// The <see cref="ResourceLayout"/> describing the number and kind of resources used.
    /// </summary>
    public ResourceLayout Layout;
    /// <summary>
    /// An array of <see cref="BindableResource"/> objects.
    /// The number and type of resources must match those specified in the <see cref="ResourceLayout"/>.
    /// </summary>
    public BindableResource[] BoundResources;

    public ResourceSetDescription(ResourceLayout layout, params BindableResource[] boundResources)
    {
        Layout = layout;
        BoundResources = boundResources;
    }

    public bool Equals(ResourceSetDescription other)
    {
        return Layout.Equals(other.Layout) && Util.ArrayEquals(BoundResources, other.BoundResources);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Layout.GetHashCode(), BoundResources.ArrayHash());
    }
}
