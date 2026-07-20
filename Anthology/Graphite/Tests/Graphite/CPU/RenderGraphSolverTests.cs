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
        => RenderGraph<TestView, int>.Build(passes);

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
}
