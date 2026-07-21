using System;

namespace Prowl.Graphite;


/// <summary>Sync primitive: GPU signals it when submitted work finishes.</summary>
public abstract class Fence : DeviceResource, IDisposable
{
    /// <summary>True once the submitted CommandBuffer finishes executing.</summary>
    public abstract bool Signaled { get; }

    /// <summary>Resets to unsignaled.</summary>
    public abstract void Reset();

    /// <summary>Debug name, for graphics debuggers.</summary>
    public abstract string Name { get; set; }

    /// <summary>True if disposed.</summary>
    public abstract bool IsDisposed { get; }

    /// <summary>Frees unmanaged device resources.</summary>
    public abstract void Dispose();
}
