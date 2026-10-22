using System;

namespace Prowl.Graphite.Rendering.Tests;

internal static class TestGraph
{
    public static void Run(GraphicsDevice device, Action<FrameGraph> build)
    {
        using FrameGraph graph = new(device, "Test");
        build(graph);
        graph.Execute();
    }

    public static void Write(this FrameGraph graph, string name, DeviceBuffer buffer, BufferAccess access, Action<PassContext> render)
        => graph.AddPass(name, builder => builder.Write(graph.Import(buffer), access), render);

    public static void Uploads(this FrameGraph graph, string name, Uploader uploader, params DeviceBuffer[] destinations)
        => graph.AddPass(name, builder =>
        {
            foreach (DeviceBuffer destination in destinations)
                builder.Write(graph.Import(destination), BufferAccess.ShaderWrite);
        }, context => uploader.Record(context.Command, context.Task));

    public static void Copies(this FrameGraph graph, string name, DeviceBuffer source, DeviceBuffer destination)
        => graph.AddPass(name, builder =>
        {
            builder.Read(graph.Import(source), BufferAccess.TransferRead);
            builder.Write(graph.Import(destination), BufferAccess.TransferWrite);
        }, context => context.Command.CopyBuffer(source, 0, destination, 0, source.SizeInBytes));

    public static void Dispatches(this FrameGraph graph, string name, ComputeProgram program, PropertySet properties, uint push, DeviceBuffer[] inputs, DeviceBuffer output)
        => graph.AddPass(name, builder =>
        {
            foreach (DeviceBuffer input in inputs)
                builder.Read(graph.Import(input), BufferAccess.ShaderRead);
            builder.Write(graph.Import(output), BufferAccess.ShaderWrite);
        }, context =>
        {
            context.Command.SetComputeShader(program);
            context.Command.SetProperties(properties);
            context.Command.SetPushConstants(push);
            context.Command.Dispatch(1, 1, 1);
        });
}
