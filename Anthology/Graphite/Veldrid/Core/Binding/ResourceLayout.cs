using System;

namespace Prowl.Veldrid;

/// <summary>
/// A device resource which describes the layout and kind of <see cref="BindableResource"/> objects available
/// to a shader set.
/// See <see cref="ResourceLayoutDescription"/>.
/// </summary>
public abstract class ResourceLayout : DeviceResource, IDisposable
{
    internal readonly ResourceLayoutDescription Description;

    /// <summary>
    /// Number of elements in this layout that were declared with
    /// <see cref="ResourceLayoutElementOptions.DynamicBinding"/>. Each of these consumes one
    /// entry in the <c>dynamicOffsets</c> array passed to
    /// <see cref="CommandBuffer.SetGraphicsResourceSet(uint, ResourceSet, uint[])"/>.
    /// </summary>
    internal readonly uint DynamicBufferCount;

    internal ResourceLayout(ref ResourceLayoutDescription description)
    {
        Description = description;
        foreach (ResourceLayoutElementDescription element in description.Elements)
        {
            if ((element.Options & ResourceLayoutElementOptions.DynamicBinding) != 0)
            {
                DynamicBufferCount += 1;
            }
        }
    }

    public abstract string Name { get; set; }
    public abstract bool IsDisposed { get; }
    public abstract void Dispose();
}
