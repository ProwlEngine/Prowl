// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Graphite;
using Prowl.Graphite.ShaderDef;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

sealed file class FakeRenderable : IRenderable
{
    public int Layer;
    public Float3 Position;

    public Material GetMaterial() => null!;
    public int GetLayer() => Layer;
    public Float3 GetPosition() => Position;
    public void GetRenderingData(ViewerData viewer, out PropertySet properties, out Mesh mesh, out Float4x4 model, out InstanceData[]? instanceData)
        => throw new NotImplementedException();
    public void GetCullingData(out bool isRenderable, out AABB bounds) => throw new NotImplementedException();
}

sealed file class FakeSink : IRenderProfilerSink
{
    public readonly List<RenderableRecord> Renderables = new();
    public readonly List<ShaderBindRecord> ShaderBinds = new();

    public void BeginFrame() { }
    public void EndFrame() { }
    public void BeginView(string name) { }
    public void EndView() { }
    public void Renderable(in RenderableRecord r) => Renderables.Add(r);
    public void ShaderBind(in ShaderBindRecord r) => ShaderBinds.Add(r);
}

/// <summary>
/// Covers the Prowl.Runtime side of the render-profiler marker hook (ProfilerTasks/TaskA/A4):
/// <see cref="RenderProfilerHooks.Sink"/> is a true zero-alloc no-op when null, and the guarded
/// emission call sites (OpaquePass.EmitRenderable, RenderCommandExtensions.EmitShaderBind) build the
/// correct records when a sink is attached.
/// </summary>
public class RenderProfilerHooksTests
{
    [Fact]
    public void SinkDefaultsToNull()
    {
        Assert.Null(RenderProfilerHooks.Sink);
    }

    [Fact]
    public void EmitRenderable_SinkNull_AllocatesNothing()
    {
        RenderProfilerHooks.Sink = null;
        var renderable = new FakeRenderable { Layer = 2, Position = new Float3(1, 2, 3) };

        OpaquePass.EmitRenderable(renderable, "Mat", "Mesh", culled: false, drawCallCount: 1);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
            OpaquePass.EmitRenderable(renderable, "Mat", "Mesh", culled: false, drawCallCount: 1);
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(before, after);
    }

    [Fact]
    public void EmitRenderable_SinkAttached_ForwardsRecord()
    {
        var sink = new FakeSink();
        RenderProfilerHooks.Sink = sink;
        try
        {
            var renderable = new FakeRenderable { Layer = 5, Position = new Float3(1, 2, 3) };
            OpaquePass.EmitRenderable(renderable, "MyMaterial", "MyMesh", culled: true, drawCallCount: 0);

            Assert.Single(sink.Renderables);
            RenderableRecord r = sink.Renderables[0];
            Assert.Equal("MyMaterial", r.MaterialName);
            Assert.Equal("MyMesh", r.MeshName);
            Assert.Equal(5, r.Layer);
            Assert.Equal(new Float3(1, 2, 3), r.Position);
            Assert.True(r.Culled);
            Assert.Equal(0, r.DrawCallCount);
        }
        finally
        {
            RenderProfilerHooks.Sink = null;
        }
    }

    private static ShaderPass MakePass(string name, Dictionary<string, string>? tags = null)
        => new() { Name = name, Tags = tags, State = new PassState(), InlineSlang = "" };

    [Fact]
    public void EmitShaderBind_SinkNull_AllocatesNothing()
    {
        RenderProfilerHooks.Sink = null;
        ShaderPass pass = MakePass("Forward");

        RenderCommandExtensions.EmitShaderBind(pass, "Mat");

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
            RenderCommandExtensions.EmitShaderBind(pass, "Mat");
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(before, after);
    }
}
