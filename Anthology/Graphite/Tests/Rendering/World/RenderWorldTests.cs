using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;
using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Rendering.Tests;

[Collection("GPU Tests")]
public sealed class RenderWorldTests : IDisposable
{
    private readonly GraphicsDevice _device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
    private readonly RenderStore _store = new();
    private readonly GlslShaderCompiler _compiler = TestCompiler.Shared;
    private readonly Uploader _uploader;
    private readonly GeometryPool _geometry;
    private readonly MaterialSystem _materials;
    private readonly RenderWorld _world;
    private readonly Shader _shader;
    private readonly GpuMesh _cube;

    public RenderWorldTests()
    {
        InternalShaders.Register();
        _uploader = new Uploader(_device, InternalShaders.Compute(_device, _compiler, "Internal/Scatter.glsl"));
        _geometry = new GeometryPool(_device, _store, 1 << 16, 1 << 16);
        _materials = new MaterialSystem(_device, _store, new NoResources());
        _world = new RenderWorld(_device, _store);
        _shader = new Shader(ShaderParser.Parse("""Shader "Test/Plain" { Pass { GLSLPROGRAM ENDGLSL } }"""), _compiler);
        _cube = Mesh(new Float3(-1, -1, -1), new Float3(1, 1, 1));
    }

    public void Dispose()
    {
        _world.Dispose();
        _uploader.Dispose();
        _store.Dispose();
        _device.Dispose();
    }

    private GpuMesh Mesh(Float3 min, Float3 max)
    {
        Float3[] positions = [min, new(max.X, min.Y, min.Z), max];
        GpuMesh mesh = _geometry.Create(new MeshData
        {
            VertexCount = 3,
            Streams = [new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(positions.AsSpan()).ToArray() }],
            Indices = new uint[] { 0, 1, 2 },
        });
        _geometry.Apply(_uploader, 0, long.MaxValue, new List<GpuMesh>());
        return mesh;
    }

    private Material NewMaterial() => _materials.Create(_shader);

    private RenderWorld.RenderSide Frame()
    {
        _materials.Extract(new List<Material>());
        _world.Extract();
        return _world.Render;
    }

    private static Double4x4 Translate(double x, double y, double z) => Double4x4.CreateTranslation(new Double3(x, y, z));

    [Fact]
    public void PreviousTransforms_FollowOneFrameBehind_AndCatchUpWhenTheObjectStops()
    {
        RenderableHandle handle = _world.Add(new RenderableDesc { Mesh = _cube, Transform = Translate(1, 0, 0), Flags = RenderableFlags.MotionVectors }, [NewMaterial()]);
        RenderWorld.RenderSide render = Frame();
        uint index = (uint)handle.Index;
        float Previous() => render.PreviousTransforms.Get<ObjectTransform>(render.Attributes.Get<ObjectAttributes>(index).PreviousTransform).Row0.W;
        Assert.Equal(1f, Previous());

        _world.SetTransform(handle, Translate(2, 0, 0));
        Frame();
        Assert.Equal(1f, Previous());
        Assert.Equal(2f, render.Transforms.Get<ObjectTransform>(index).Row0.W);

        Frame();
        Assert.Equal(2f, Previous());
    }

    [Fact]
    public void RemovedRenderables_HideTheirRecords_AndTheirIndexIsReusedWithANewGeneration()
    {
        Material material = NewMaterial();
        RenderableHandle first = _world.Add(new RenderableDesc { Mesh = _cube }, [material]);
        Frame();

        _world.Remove(first);
        RenderWorld.RenderSide render = Frame();
        Assert.Equal([first.Index], render.DrawChanged);
        Assert.Equal((uint)RenderableFlags.Hidden, render.Attributes.Get<ObjectAttributes>((uint)first.Index).Flags);
        Assert.False(render.Alive[first.Index]);
        Assert.False(_world.IsAlive(first));
        Assert.Throws<ArgumentException>(() => _world.SetTransform(first, Float4x4.Identity));

        RenderableHandle second = _world.Add(new RenderableDesc { Mesh = _cube, ObjectId = 5 }, [material]);
        Assert.Equal(first.Index, second.Index);
        Assert.NotEqual(first.Generation, second.Generation);
        Assert.Equal(5u, Frame().Attributes.Get<ObjectAttributes>((uint)second.Index).ObjectId);
    }

    private static MeshData SubMeshes(int count)
    {
        Float3[] positions = [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0)];
        return new MeshData
        {
            VertexCount = 3,
            Streams = [new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(positions.AsSpan()).ToArray() }],
            Indices = Enumerable.Repeat(new uint[] { 0, 1, 2 }, count).SelectMany(i => i).ToArray(),
            SubMeshes = Enumerable.Range(0, count).Select(s => new SubMesh { IndexStart = (uint)s * 3, IndexCount = 3, Topology = PrimitiveTopology.TriangleList }).ToArray(),
        };
    }

    [Fact]
    public void MeshDataChanges_RecomputeBounds_AndChangeDraws()
    {
        GpuMesh mesh = Mesh(new Float3(0, 0, 0), new Float3(1, 1, 1));
        RenderableHandle handle = _world.Add(new RenderableDesc { Mesh = mesh }, [NewMaterial()]);
        Frame();

        Float3[] positions = [new(0, 0, 0), new(4, 0, 0), new(4, 4, 4)];
        mesh.SetData(new MeshData
        {
            VertexCount = 3,
            Streams = [new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(positions.AsSpan()).ToArray() }],
            Indices = new uint[] { 0, 1, 2 },
        });
        RenderWorld.RenderSide render = Frame();
        List<GpuMesh> changed = new();
        _geometry.Apply(_uploader, 1, long.MaxValue, changed);
        render.Update(changed, []);

        Assert.Equal([handle.Index], render.DrawChanged);
        Assert.Equal(new Double3(2, 2, 2), render.WorldCenter[handle.Index]);
        Assert.Equal(new Float3(2, 2, 2), render.WorldExtents[handle.Index]);
    }

    [Fact]
    public void FarFromTheOrigin_CameraRelativePositions_KeepFullPrecision()
    {
        Double3 camera = new(10_000_000, 2_000, -3_000_000);
        Double3 position = camera + new Double3(0.25, -0.125, 0.0625);
        RenderableHandle handle = _world.Add(new RenderableDesc { Mesh = _cube, Transform = Double4x4.CreateTranslation(position) }, [NewMaterial()]);
        ObjectTransform transform = Frame().Transforms.Get<ObjectTransform>((uint)handle.Index);

        (Float3 cameraHigh, Float3 cameraLow) = Precise.Split(camera);
        Float3 high = new(transform.Row0.W, transform.Row1.W, transform.Row2.W);
        Float3 low = new(transform.TranslationLow.X, transform.TranslationLow.Y, transform.TranslationLow.Z);
        Float3 relative = (high - cameraHigh) + (low - cameraLow);
        Assert.Equal(new Float3(0.25f, -0.125f, 0.0625f), relative);

        Float3 naive = new Float3((float)position.X, (float)position.Y, (float)position.Z) - new Float3((float)camera.X, (float)camera.Y, (float)camera.Z);
        Assert.NotEqual(relative, naive);
    }
}
