using System.Collections.Generic;

namespace Prowl.Graphite.Rendering;

/// <summary>Every buffer the frame's tables live in, declared by the root passes that write them and read by the cameras.</summary>
internal sealed class FrameBuffers
{
    private static readonly List<RenderResourceID> s_importIds = new();

    public PersistentBuffer[] Persistent = [];
    public uint[] Sizes = [];
    public DeviceBuffer[] Imported = [];

    public void Describe(RenderContextBuilder builder, BufferAccess access)
    {
        foreach (PersistentBuffer buffer in Persistent)
            builder.DeclareOutputBuffer(buffer.Id, buffer.Description, usage: access, persistent: true);
        DeclareImports(builder, access);
    }

    public void Write(RenderContextBuilder builder, BufferAccess access)
    {
        foreach (PersistentBuffer buffer in Persistent)
            builder.DeclareWriteBuffer(buffer.Id, access);
        DeclareImports(builder, access);
    }

    public void Read(RenderContextBuilder builder)
    {
        foreach (PersistentBuffer buffer in Persistent)
            builder.DeclareInputBuffer(buffer.Id);
        DeclareImports(builder, BufferAccess.AllReads);
    }

    private void DeclareImports(RenderContextBuilder builder, BufferAccess access)
    {
        for (int i = 0; i < Imported.Length; i++)
            builder.DeclareImportedBuffer(ImportId(i), Imported[i], access);
    }

    private static RenderResourceID ImportId(int index)
    {
        lock (s_importIds)
        {
            while (s_importIds.Count <= index)
                s_importIds.Add(RenderResourceID.Intern("Graphite.Import" + s_importIds.Count));
            return s_importIds[index];
        }
    }
}
