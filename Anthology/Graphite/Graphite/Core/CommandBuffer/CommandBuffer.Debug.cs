namespace Prowl.Graphite;

public abstract partial class CommandBuffer
{
    /// <summary>Pushes a debug group for grouping commands in debug tools. Nestable. Every push needs a pop.</summary>
    /// <param name="name">Group name shown in debug tools.</param>
    /// <param name="meta">Object that tools capturing the commands receive with the group. Build it only when <see cref="WantsDebugMeta"/>.</param>
    public void PushDebugGroup(string name, object? meta = null)
    {
        PassSink?.PushMarker(name, meta);
        PushDebugGroupCore(name);
    }

    /// <summary>True when a tool capturing the commands is listening, so group payloads are worth building.</summary>
    public bool WantsDebugMeta => PassSink != null;

    private protected abstract void PushDebugGroupCore(string name);

    /// <summary>Pops current debug group. Only after a matching push.</summary>
    public void PopDebugGroup()
    {
        PassSink?.PopMarker();
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
