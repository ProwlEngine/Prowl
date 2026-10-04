namespace Prowl.Graphite;


/// <summary>Sync primitive: GPU signals it when submitted work finishes.</summary>
internal abstract class Fence : GraphicsResource
{
    /// <summary>True once the submitted CommandBuffer finishes executing.</summary>
    public abstract bool Signaled { get; }
}
