namespace Prowl.Graphite;


/// <summary>Sync primitive: GPU signals it when submitted work finishes.</summary>
public abstract class Fence : GraphicsResource
{
    /// <summary>True once the submitted CommandBuffer finishes executing.</summary>
    public abstract bool Signaled { get; }

    /// <summary>Resets to unsignaled.</summary>
    public abstract void Reset();
}
