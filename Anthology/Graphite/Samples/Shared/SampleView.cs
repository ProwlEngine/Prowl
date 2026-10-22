namespace Prowl.Graphite.Samples;


public static class SampleView
{
    public static readonly GraphKind Kind = GraphKind.Intern("Sample.View");

    public static RenderGraph Build(params IPass[] passes) => RenderGraph.Build(passes, [new GraphScope(Kind, GraphKind.Root)], "Sample");

    public static GraphInstances Instances(RenderGraph graph, uint width, uint height, Swapchain swapchain)
    {
        GraphInstances instances = graph.CreateInstances();
        instances.Add(Kind, 0, extent: new GraphExtent(width, height), target: swapchain.Framebuffer);
        return instances;
    }
}
