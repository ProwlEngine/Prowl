namespace Prowl.Graphite;

public abstract partial class CommandBuffer
{
    /// <summary>Pushes a debug group for grouping commands in debug tools. Nestable. Every push needs a pop.</summary>
    /// <param name="name">Group name shown in debug tools.</param>
    public void PushDebugGroup(string name)
    {
        PushDebugGroupCore(name);
    }

    private protected abstract void PushDebugGroupCore(string name);

    /// <summary>Pops current debug group. Only after a matching push.</summary>
    public void PopDebugGroup()
    {
        PopDebugGroupCore();
    }

    private protected abstract void PopDebugGroupCore();

    /// <summary>Inserts a debug marker for spotting points of interest in debug tools.</summary>
    /// <param name="name">Marker name shown in debug tools.</param>
    public void InsertDebugMarker(string name)
    {
        InsertDebugMarkerCore(name);
    }

    private protected abstract void InsertDebugMarkerCore(string name);
}
