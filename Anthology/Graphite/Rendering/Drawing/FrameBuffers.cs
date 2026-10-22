using System.Collections.Generic;

namespace Prowl.Graphite.Rendering;

internal sealed class BufferSet
{
    public readonly List<DeviceBuffer> Items = new();

    public void Clear() => Items.Clear();

    public void Add(PersistentBuffer buffer) => Items.Add(buffer.Buffer!);

    public void Add(DeviceBuffer buffer) => Items.Add(buffer);

    public void Write(FrameGraph graph, PassBuilder pass, BufferAccess access)
    {
        foreach (DeviceBuffer buffer in Items)
            pass.Write(graph.Import(buffer), access);
    }

    public void Read(FrameGraph graph, PassBuilder pass, BufferAccess access)
    {
        foreach (DeviceBuffer buffer in Items)
            pass.Read(graph.Import(buffer), access);
    }
}

/// <summary>Every buffer the frame's tables live in: the shared ones, each world's and each camera's, imported into the graph every frame.</summary>
internal sealed class FrameBuffers
{
    public readonly BufferSet Shared = new();
    public readonly List<WorldFrame> Worlds = new();
    public readonly List<CameraFrame> Cameras = new();

    public void WriteAll(FrameGraph graph, PassBuilder pass, BufferAccess access)
    {
        Shared.Write(graph, pass, access);
        foreach (WorldFrame world in Worlds)
            world.Buffers.Write(graph, pass, access);
        foreach (CameraFrame camera in Cameras)
            camera.Buffers.Write(graph, pass, access);
    }

    public void ReadWorld(FrameGraph graph, PassBuilder pass, WorldFrame world, BufferAccess access = BufferAccess.AllReads)
    {
        Shared.Read(graph, pass, access);
        world.Buffers.Read(graph, pass, access);
    }

    public void ReadCamera(FrameGraph graph, PassBuilder pass, CameraFrame camera, BufferAccess access = BufferAccess.AllReads)
    {
        ReadWorld(graph, pass, camera.World, access);
        camera.Buffers.Read(graph, pass, access);
    }
}
