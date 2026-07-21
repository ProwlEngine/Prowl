using System;

namespace Prowl.Graphite;

/// <summary>
/// Bindable device resource controlling how a texture is sampled in a shader.
/// </summary>
public abstract class Sampler : DeviceResource, BindableResource, IDisposable
{
    /// <summary>Debug name, shows up in graphics debuggers.</summary>
    public abstract string Name { get; set; }

    /// <summary>True if disposed.</summary>
    public abstract bool IsDisposed { get; }

    /// <summary>Frees unmanaged device resources.</summary>
    public abstract void Dispose();
}
