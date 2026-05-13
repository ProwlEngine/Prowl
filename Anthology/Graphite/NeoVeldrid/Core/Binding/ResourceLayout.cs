using System;

namespace NeoVeldrid;

/// <summary>
/// A device resource which describes the layout and kind of <see cref="BindableResource"/> objects available
/// to a shader set.
/// See <see cref="ResourceLayoutDescription"/>.
/// </summary>
public abstract class ResourceLayout : DeviceResource, IDisposable
{
    internal readonly ResourceLayoutDescription Description;

    internal ResourceLayout(ref ResourceLayoutDescription description)
    {
        Description = description;
    }

    public abstract string Name { get; set; }
    public abstract bool IsDisposed { get; }
    public abstract void Dispose();
}
