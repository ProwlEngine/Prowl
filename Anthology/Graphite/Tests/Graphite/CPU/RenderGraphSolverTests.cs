#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;


using Xunit;

namespace Prowl.Graphite.Tests;

public class RenderGraphSolverTests
{
    private static readonly GraphKind World = GraphKind.Intern("Test.World");
    private static readonly GraphKind Camera = GraphKind.Intern("Test.Camera");
    private static readonly GraphScope[] Nested = [new GraphScope(World, GraphKind.Root), new GraphScope(Camera, World)];

    private static RenderGraph Build(params IPass[] passes)
        => RenderGraph.Build(passes, TestViews.Scopes);

    private static List<string> OrderNames(RenderGraph graph)
        => graph.Passes.Select(n => n.Pass.Name).ToList();

    [Fact]
    public void Build_TwoWritersWithDifferentDescriptions_Throws()
    {
        var a = new TestPass("A", outputs: new[] { ("conflict_shared", Desc.Color()) });
        var b = new TestPass("B", outputs: new[] { ("conflict_shared", GraphTextureDesc.InstanceSized(PixelFormat.R8_G8_B8_A8_UNorm, depth: true)) });

        Assert.Throws<RenderGraphException>(() => Build(a, b));
    }

    [Fact]
    public void Build_IndependentPasses_RunInInsertionOrder()
    {
        var a = new TestPass("A", outputs: new[] { ("order_a", Desc.Color()) });
        var b = new TestPass("B", outputs: new[] { ("order_b", Desc.Color()) });
        var c = new TestPass("C", outputs: new[] { ("order_c", Desc.Color()) });

        Assert.Equal(new[] { "A", "B", "C" }, OrderNames(Build(a, b, c)).ToArray());
    }

    [Fact]
    public void Build_ReaderGivenBeforeItsWriter_RunsAfterIt()
    {
        var reader = new TestPass("Reader", inputs: new[] { "late_x" });
        var writer = new TestPass("Writer", outputs: new[] { ("late_x", Desc.Color()) });

        Assert.Equal(new[] { "Writer", "Reader" }, OrderNames(Build(reader, writer)).ToArray());
    }

    [Fact]
    public void Build_WritersOfOneResource_KeepInsertionOrderAroundTheirReaders()
    {
        var first = new TestPass("First", outputs: new[] { ("version_x", Desc.Color()) });
        var between = new TestPass("Between", inputs: new[] { "version_x" });
        var second = new TestPass("Second", writes: new[] { "version_x" });
        var after = new TestPass("After", inputs: new[] { "version_x" });

        Assert.Equal(new[] { "First", "Between", "Second", "After" }, OrderNames(Build(first, between, second, after)).ToArray());
    }

    [Fact]
    public void Build_CyclicDependency_Throws()
    {
        var a = new TestPass("A", inputs: new[] { "cycle_b" }, outputs: new[] { ("cycle_a", Desc.Color()) });
        var b = new TestPass("B", inputs: new[] { "cycle_a" }, outputs: new[] { ("cycle_b", Desc.Color()) });

        RenderGraphException ex = Assert.Throws<RenderGraphException>(() => Build(a, b));
        Assert.Contains("'A'", ex.Message);
        Assert.Contains("'B'", ex.Message);
    }

    [Fact]
    public void Build_InputWithNoProducer_Throws()
    {
        var reader = new TestPass("Orphan",
            inputs: new[] { "never_produced" },
            outputs: new[] { ("orphan_out", Desc.Color()) });

        InvalidOperationException ex = Assert.Throws<RenderGraphException>(() => Build(reader));
        Assert.Contains("Orphan", ex.Message);
        Assert.Contains("never_produced", ex.Message);
    }

    [Fact]
    public void Build_AKindWritingAResourceAnotherKindOwns_Throws()
    {
        var root = new TestRootPass("Root", outputs: new[] { ("owned_x", Desc.Fixed()) });
        var camera = new TestPass("Camera", outputs: new[] { ("owned_x", Desc.Fixed()) });

        Assert.Throws<RenderGraphException>(() => RenderGraph.Build([root, camera], TestViews.Scopes));
    }

    [Fact]
    public void Build_AnOuterKindReadingAnInnerKindsResource_Throws()
    {
        var camera = new TestPass("Camera", outputs: new[] { ("inner_x", Desc.Color()) }, kind: Camera);
        var world = new TestPass("World", inputs: new[] { "inner_x" }, kind: World);

        RenderGraphException ex = Assert.Throws<RenderGraphException>(() => RenderGraph.Build([camera, world], Nested));
        Assert.Same(world, ex.Pass);
    }

    [Fact]
    public void Build_AnInnerKindReadingAnOuterKindsResource_OrdersTheWriterFirst()
    {
        var camera = new TestPass("Camera", inputs: new[] { "outer_x" }, kind: Camera);
        var world = new TestPass("World", outputs: new[] { ("outer_x", Desc.Color()) }, kind: World);

        RenderGraph graph = RenderGraph.Build([camera, world], Nested);

        Assert.Equal(new[] { "World", "Camera" }, OrderNames(graph).ToArray());
        Assert.Equal(new[] { GraphKind.Root, World, Camera }, graph.Kinds);
    }

    [Fact]
    public void Build_AKindNoScopeDeclares_Throws()
    {
        var pass = new TestPass("Stray", kind: GraphKind.Intern("Test.Stray"));

        Assert.Throws<RenderGraphException>(() => RenderGraph.Build([pass], TestViews.Scopes));
    }

    [Fact]
    public void Build_AKindNamingTwoParents_Throws()
    {
        GraphScope[] scopes = [new GraphScope(Camera, World), new GraphScope(World, GraphKind.Root), new GraphScope(Camera, GraphKind.Root)];

        Assert.Throws<ArgumentException>(() => RenderGraph.Build([], scopes));
    }

    [Fact]
    public void Build_PassWhoseOutputIsNeverRead_IsStillExecuted()
    {
        var unreferenced = new TestPass("Unreferenced", outputs: new[] { ("cull_unused", Desc.Color()) });
        var main = new TestPass("Main", outputs: new[] { ("cull_main", Desc.Color()) });

        RenderGraph graph = Build(unreferenced, main);

        Assert.Contains(graph.Passes, n => n.Pass.Name == "Unreferenced");
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
    public void Build_PassDeclaresBackbuffer_WritesInstanceTargetIsTrue()
    {
        RenderGraph graph = Build(new TestBackbufferPass());

        Assert.True(graph.WritesInstanceTarget);
    }
}
