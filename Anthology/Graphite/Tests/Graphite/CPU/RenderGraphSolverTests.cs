#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Graphite.RenderGraph;

using Xunit;

namespace Prowl.Graphite.RenderGraph.Tests;

public class RenderGraphSolverTests
{
    private static RenderGraph<TestView, int> Build(params IPass<TestView, int>[] passes)
        => RenderGraph<TestView, int>.Build(passes, new NoOpTestPresentPass());

    private static RenderGraph<TestView, int> Build(IPresentPass<TestView, int> presentPass, params IPass<TestView, int>[] passes)
        => RenderGraph<TestView, int>.Build(passes, presentPass);

    private static List<string> OrderNames(RenderGraph<TestView, int> graph)
        => graph.OrderedPasses.Select(n => n.Pass.Name).ToList();

    [Fact]
    public void Build_OrdersReaderAfterWriter_RegardlessOfInsertionOrder()
    {
        var writer = new TestPass("Writer",
            outputs: new[] { ("topo_shared", Desc.Color()) });
        var reader = new TestPass("Reader",
            inputs: new[] { ("topo_shared", Desc.Color()) },
            outputs: new[] { ("topo_readerOut", Desc.Color()) });

        RenderGraph<TestView, int> graph = Build(reader, writer);
        List<string> order = OrderNames(graph);

        Assert.True(order.IndexOf("Writer") < order.IndexOf("Reader"));
    }

    [Fact]
    public void Build_ChainOfDependencies_OrdersTransitively()
    {
        var a = new TestPass("A", outputs: new[] { ("chain_a", Desc.Color()) });
        var b = new TestPass("B",
            inputs: new[] { ("chain_a", Desc.Color()) },
            outputs: new[] { ("chain_b", Desc.Color()) });
        var c = new TestPass("C",
            inputs: new[] { ("chain_b", Desc.Color()) },
            outputs: new[] { ("chain_c", Desc.Color()) });

        RenderGraph<TestView, int> graph = Build(c, b, a);
        List<string> order = OrderNames(graph);

        Assert.True(order.IndexOf("A") < order.IndexOf("B"));
        Assert.True(order.IndexOf("B") < order.IndexOf("C"));
    }

    [Fact]
    public void Build_IndependentPasses_KeepInsertionOrder()
    {
        var a = new TestPass("A", outputs: new[] { ("indep_a", Desc.Color()) });
        var b = new TestPass("B", outputs: new[] { ("indep_b", Desc.Color()) });

        RenderGraph<TestView, int> graph = Build(a, b);

        Assert.Equal(new[] { "A", "B" }, OrderNames(graph).ToArray());
    }

    [Fact]
    public void Build_CyclicDependency_Throws()
    {
        var a = new TestPass("A",
            inputs: new[] { ("cycle_x", Desc.Color()) },
            outputs: new[] { ("cycle_y", Desc.Color()) });
        var b = new TestPass("B",
            inputs: new[] { ("cycle_y", Desc.Color()) },
            outputs: new[] { ("cycle_x", Desc.Color()) });

        Assert.Throws<InvalidOperationException>(() => Build(a, b));
    }

    [Fact]
    public void Build_FirstDeclarationWins_ForResourceMerge()
    {
        GraphTextureDesc first = GraphTextureDesc.ViewSized(true, 0.5f);
        GraphTextureDesc second = GraphTextureDesc.ViewSized(false, 0.25f);

        var writer = new TestPass("Writer",
            outputs: new[] { ("merge_shared", first) });
        var reader = new TestPass("Reader",
            inputs: new[] { ("merge_shared", second) },
            outputs: new[] { ("merge_readerOut", Desc.Color()) });

        RenderGraph<TestView, int> graph = Build(writer, reader);

        RenderResourceID id = RenderResourceID.Intern("merge_shared");
        GraphTextureDesc merged = graph.Resources[id];

        Assert.Equal(first.Scale, merged.Scale);
        Assert.True(merged.EnableDepth);
    }

    [Fact]
    public void Build_Presentation_IsLastMainOutputInExecutionOrder()
    {
        var first = new TestPass("First",
            outputs: new[] { ("present_first", Desc.Color()) },
            mainOutput: "present_first");
        var second = new TestPass("Second",
            inputs: new[] { ("present_first", Desc.Color()) },
            outputs: new[] { ("present_second", Desc.Color()) },
            mainOutput: "present_second");

        RenderGraph<TestView, int> graph = Build(second, first);

        Assert.Equal(RenderResourceID.Intern("present_second"), graph.PresentationSource);
    }

    [Fact]
    public void Build_NoMainOutputSet_PresentationIsInvalid()
    {
        var a = new TestPass("A", outputs: new[] { ("nopresent_a", Desc.Color()) });

        RenderGraph<TestView, int> graph = Build(a);

        Assert.False(graph.PresentationSource.IsValid);
    }

    [Fact]
    public void Build_MainOutputNotDeclaredAsOutput_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Build(new MisdeclaredMainOutputPass()));
    }

    [Fact]
    public void Build_MergesResourceTable_AcrossPasses()
    {
        var a = new TestPass("A", outputs: new[] { ("table_a", Desc.Color()) });
        var b = new TestPass("B",
            inputs: new[] { ("table_a", Desc.Color()) },
            outputs: new[] { ("table_b", Desc.Color()) });

        RenderGraph<TestView, int> graph = Build(a, b);

        Assert.True(graph.Resources.ContainsKey(RenderResourceID.Intern("table_a")));
        Assert.True(graph.Resources.ContainsKey(RenderResourceID.Intern("table_b")));
        Assert.Equal(2, graph.Resources.Count);
    }

    [Fact]
    public void Build_DiamondDependency_OrdersProducerBeforeBothBranchesAndJoinAfterBoth()
    {
        var producer = new TestPass("Producer", outputs: new[] { ("diamond_x", Desc.Color()) });
        var left = new TestPass("Left",
            inputs: new[] { ("diamond_x", Desc.Color()) },
            outputs: new[] { ("diamond_y1", Desc.Color()) });
        var right = new TestPass("Right",
            inputs: new[] { ("diamond_x", Desc.Color()) },
            outputs: new[] { ("diamond_y2", Desc.Color()) });
        var join = new TestPass("Join",
            inputs: new[] { ("diamond_y1", Desc.Color()), ("diamond_y2", Desc.Color()) },
            outputs: new[] { ("diamond_out", Desc.Color()) });

        RenderGraph<TestView, int> graph = Build(join, right, left, producer);
        List<string> order = OrderNames(graph);

        Assert.True(order.IndexOf("Producer") < order.IndexOf("Left"));
        Assert.True(order.IndexOf("Producer") < order.IndexOf("Right"));
        Assert.True(order.IndexOf("Left") < order.IndexOf("Join"));
        Assert.True(order.IndexOf("Right") < order.IndexOf("Join"));
    }

    [Fact]
    public void Build_MultipleWritersOfOneResource_ReaderRunsAfterEveryWriter()
    {
        var writer1 = new TestPass("Writer1", outputs: new[] { ("fanin_shared", Desc.Color()) });
        var writer2 = new TestPass("Writer2", outputs: new[] { ("fanin_shared", Desc.Color()) });
        var reader = new TestPass("Reader",
            inputs: new[] { ("fanin_shared", Desc.Color()) },
            outputs: new[] { ("fanin_out", Desc.Color()) });

        RenderGraph<TestView, int> graph = Build(reader, writer2, writer1);
        List<string> order = OrderNames(graph);

        Assert.True(order.IndexOf("Writer1") < order.IndexOf("Reader"));
        Assert.True(order.IndexOf("Writer2") < order.IndexOf("Reader"));
    }

    [Fact]
    public void Build_PassReadsAndWritesSameResource_SkipsSelfEdgeAndOrdersNeighborsCorrectly()
    {
        var upstream = new TestPass("Upstream", outputs: new[] { ("rmw_res", Desc.Color()) });
        var rmw = new TestPass("RMW",
            inputs: new[] { ("rmw_res", Desc.Color()) },
            outputs: new[] { ("rmw_res", Desc.Color()) });
        var downstream = new TestPass("Downstream",
            inputs: new[] { ("rmw_res", Desc.Color()) },
            outputs: new[] { ("rmw_downstream", Desc.Color()) });

        RenderGraph<TestView, int> graph = Build(downstream, rmw, upstream);
        List<string> order = OrderNames(graph);

        Assert.True(order.IndexOf("Upstream") < order.IndexOf("RMW"));
        Assert.True(order.IndexOf("RMW") < order.IndexOf("Downstream"));
    }

    [Fact]
    public void Build_PassWhoseOutputIsNeverReadAndNotPresentationSource_IsStillExecuted()
    {
        var unreferenced = new TestPass("Unreferenced", outputs: new[] { ("cull_unused", Desc.Color()) });
        var main = new TestPass("Main", outputs: new[] { ("cull_main", Desc.Color()) }, mainOutput: "cull_main");

        RenderGraph<TestView, int> graph = Build(unreferenced, main);

        Assert.Contains(graph.OrderedPasses, n => n.Pass.Name == "Unreferenced");
    }

    [Fact]
    public void Build_PresentPassRequestsSwapchain_SetsPresentRequestsSwapchainTrue()
    {
        var present = new TestPresentPass(requestSwapchain: true);

        RenderGraph<TestView, int> graph = Build(present);

        Assert.True(graph.PresentRequestsSwapchain);
    }

    [Fact]
    public void Build_PresentPassDoesNotRequestSwapchain_PresentRequestsSwapchainIsFalse()
    {
        var present = new TestPresentPass(requestSwapchain: false);

        RenderGraph<TestView, int> graph = Build(present);

        Assert.False(graph.PresentRequestsSwapchain);
    }

    [Fact]
    public void Build_PresentPassDeclaresInputForResourceWrittenByAPass_ResourceKeepsWritersDesc()
    {
        var writerDesc = GraphTextureDesc.ViewSized(true, 0.5f);
        var presentDesc = GraphTextureDesc.ViewSized(false, 0.25f);

        var writer = new TestPass("Writer", outputs: new[] { ("present_in_shared", writerDesc) });
        var present = new TestPresentPass(inputs: new[] { ("present_in_shared", presentDesc) });

        RenderGraph<TestView, int> graph = Build(present, writer);

        Assert.Contains(RenderResourceID.Intern("present_in_shared"), graph.PresentInputs);
        GraphTextureDesc merged = graph.Resources[RenderResourceID.Intern("present_in_shared")];
        Assert.Equal(writerDesc.Scale, merged.Scale);
        Assert.True(merged.EnableDepth);
    }

    [Fact]
    public void Build_PresentPassDeclaresInputForResourceNoPassWrites_IsAddedToResourceTable()
    {
        var present = new TestPresentPass(inputs: new[] { ("present_only_resource", Desc.Color()) });

        RenderGraph<TestView, int> graph = Build(present);

        Assert.True(graph.Resources.ContainsKey(RenderResourceID.Intern("present_only_resource")));
    }
}
