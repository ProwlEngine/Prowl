namespace Prowl.Graphite.Rendering;

/// <summary>Where a component makes its tables and arrays: one device and store, with a name prefix that keeps their ids unique.</summary>
internal readonly record struct TableHost(GraphicsDevice Device, RenderStore Store, string Prefix)
{
    public GpuTable Table(string name, uint strideWords, uint initialCapacity = 256)
        => new(Device, Store, $"{Prefix}.{name}", strideWords, initialCapacity);

    public GpuTable Of<T>(string name, uint initialCapacity = 256) where T : unmanaged
        => GpuTable.Of<T>(Device, Store, $"{Prefix}.{name}", initialCapacity);

    public GpuArray Array(string name, BufferUsage usage = BufferUsage.StructuredBufferReadOnly)
        => new(Device, Store, RenderResourceID.Intern($"{Prefix}.{name}"), usage);

    public TableHost Scope(string name) => this with { Prefix = $"{Prefix}.{name}" };
}
