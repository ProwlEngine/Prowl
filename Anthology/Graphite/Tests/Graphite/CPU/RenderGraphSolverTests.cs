#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Graphite.RenderGraph;

using Xunit;

namespace Prowl.Graphite.RenderGraph.Tests;

public class RenderGraphSolverTests
{
    private static RenderGraph<TestView> Build(params IPass<TestView>[] passes)
        => RenderGraph<TestView>.Build(passes);

    private static List<string> OrderNames(RenderGraph<TestView> graph)
        => graph.OrderedPasses.Select(n => n.Pass.Name).ToList();

    [Fact]
    public void Build_TwoWritersWithDifferentDescriptions_Throws()
    {
        var a = new TestPass("A", outputs: new[] { ("conflict_shared", Desc.Color()) });
        var b = new TestPass("B", outputs: new[] { ("conflict_shared", GraphTextureDesc.ViewSized(PixelFormat.R8_G8_B8_A8_UNorm, depth: true)) });

        Assert.Throws<InvalidOperationException>(() => Build(a, b));
    }

    [Fact]
    public void Build_ChainOfDependencies_OrdersTransitively()
    {
        var a = new TestPass("A", outputs: new[] { ("chain_a", Desc.Color()) });
        var b = new TestPass("B",
            inputs: new[] { "chain_a" },
            outputs: new[] { ("chain_b", Desc.Color()) });
        var c = new TestPass("C",
            inputs: new[] { "chain_b" },
            outputs: new[] { ("chain_c", Desc.Color()) });

        RenderGraph<TestView> graph = Build(c, b, a);
        List<string> order = OrderNames(graph);

        Assert.True(order.IndexOf("A") < order.IndexOf("B"));
        Assert.True(order.IndexOf("B") < order.IndexOf("C"));
    }

    [Fact]
    public void Build_IndependentPasses_KeepInsertionOrder()
    {
        var a = new TestPass("A", outputs: new[] { ("indep_a", Desc.Color()) });
        var b = new TestPass("B", outputs: new[] { ("indep_b", Desc.Color()) });

        RenderGraph<TestView> graph = Build(a, b);

        Assert.Equal(new[] { "A", "B" }, OrderNames(graph).ToArray());
    }

    [Fact]
    public void Build_CyclicDependency_Throws()
    {
        var a = new TestPass("A",
            inputs: new[] { "cycle_x" },
            outputs: new[] { ("cycle_y", Desc.Color()) });
        var b = new TestPass("B",
            inputs: new[] { "cycle_y" },
            outputs: new[] { ("cycle_x", Desc.Color()) });

        Assert.Throws<InvalidOperationException>(() => Build(a, b));
    }

    [Fact]
    public void Build_InputWithNoProducer_Throws()
    {
        var reader = new TestPass("Orphan",
            inputs: new[] { "never_produced" },
            outputs: new[] { ("orphan_out", Desc.Color()) });

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => Build(reader));
        Assert.Contains("Orphan", ex.Message);
        Assert.Contains("never_produced", ex.Message);
    }

    [Fact]
    public void Build_BufferProducerBeforeBufferReader()
    {
        var producer = new TestBufferPass("Compute",
            outputs: new[] { ("light_grid", Desc.Storage()) });
        var reader = new TestBufferPass("Shade",
            inputs: new[] { "light_grid" });

        RenderGraph<TestView> graph = Build(reader, producer);
        List<string> order = OrderNames(graph);

        Assert.True(order.IndexOf("Compute") < order.IndexOf("Shade"));
        Assert.IsType<GraphBufferResource>(graph.Resources[RenderResourceID.Intern("light_grid")]);
    }

    [Fact]
    public void Build_DiamondDependency_OrdersProducerBeforeBothBranchesAndJoinAfterBoth()
    {
        var producer = new TestPass("Producer", outputs: new[] { ("diamond_x", Desc.Color()) });
        var left = new TestPass("Left",
            inputs: new[] { "diamond_x" },
            outputs: new[] { ("diamond_y1", Desc.Color()) });
        var right = new TestPass("Right",
            inputs: new[] { "diamond_x" },
            outputs: new[] { ("diamond_y2", Desc.Color()) });
        var join = new TestPass("Join",
            inputs: new[] { "diamond_y1", "diamond_y2" },
            outputs: new[] { ("diamond_out", Desc.Color()) });

        RenderGraph<TestView> graph = Build(join, right, left, producer);
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
            inputs: new[] { "fanin_shared" },
            outputs: new[] { ("fanin_out", Desc.Color()) });

        RenderGraph<TestView> graph = Build(reader, writer2, writer1);
        List<string> order = OrderNames(graph);

        Assert.True(order.IndexOf("Writer1") < order.IndexOf("Reader"));
        Assert.True(order.IndexOf("Writer2") < order.IndexOf("Reader"));
    }

    [Fact]
    public void Build_PassReadsAndWritesSameResource_SkipsSelfEdgeAndOrdersNeighborsCorrectly()
    {
        var upstream = new TestPass("Upstream", outputs: new[] { ("rmw_res", Desc.Color()) });
        var rmw = new TestPass("RMW",
            inputs: new[] { "rmw_res" },
            outputs: new[] { ("rmw_res", Desc.Color()) });
        var downstream = new TestPass("Downstream",
            inputs: new[] { "rmw_res" },
            outputs: new[] { ("rmw_downstream", Desc.Color()) });

        RenderGraph<TestView> graph = Build(downstream, rmw, upstream);
        List<string> order = OrderNames(graph);

        Assert.True(order.IndexOf("Upstream") < order.IndexOf("RMW"));
        Assert.True(order.IndexOf("RMW") < order.IndexOf("Downstream"));
    }

    [Fact]
    public void Build_PassWhoseOutputIsNeverRead_IsStillExecuted()
    {
        var unreferenced = new TestPass("Unreferenced", outputs: new[] { ("cull_unused", Desc.Color()) });
        var main = new TestPass("Main", outputs: new[] { ("cull_main", Desc.Color()) });

        RenderGraph<TestView> graph = Build(unreferenced, main);

        Assert.Contains(graph.OrderedPasses, n => n.Pass.Name == "Unreferenced");
    }

    [Fact]
    public void TextureResource_TransientDefaultsToClear_HistoryDefaultsToLoad()
    {
        var transient = new GraphTextureResource(RenderResourceID.Intern("ops_transient"), Desc.Color(), 0);
        var history = new GraphTextureResource(RenderResourceID.Intern("ops_history"), Desc.Color(), 1);

        Assert.Equal(LoadAction.Clear, transient.Ops.Color.Load);
        Assert.Equal(LoadAction.Clear, transient.Ops.Depth.Load);
        Assert.Equal(LoadAction.Load, history.Ops.Color.Load);
        Assert.Equal(LoadAction.Load, history.Ops.Depth.Load);
    }

    [Fact]
    public void Build_PassDeclaresBackbuffer_WritesViewTargetIsTrue()
    {
        RenderGraph<TestView> graph = Build(new TestBackbufferPass());

        Assert.True(graph.WritesViewTarget);
    }
}
