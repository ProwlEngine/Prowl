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
public sealed class BucketTests : IDisposable
{
    private const string Lit = """
        Shader "Test/Lit"
        {
            Pass { Tags { "LightMode" = "Prepass" } GLSLPROGRAM Shared { #pragma variant _NORMALMAP } ENDGLSL }
            Pass { Tags { "LightMode" = "Forward" } GLSLPROGRAM Shared { #pragma variant _NORMALMAP } ENDGLSL }
            Pass { Tags { "LightMode" = "ShadowCaster" } GLSLPROGRAM ENDGLSL }
        }
        """;

    private const int Prepass = 0, Forward = 1, Shadows = 2;

    private readonly GraphicsDevice _device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
    private readonly RenderStore _store = new();
    private readonly GlslShaderCompiler _compiler = TestCompiler.Shared;
    private readonly Uploader _uploader;
    private readonly GeometryPool _geometry;
    private readonly MaterialSystem _materials;
    private readonly RenderWorld _world;
    private readonly DrawPrograms _programs = new();
    private readonly BucketSet _buckets;
    private readonly Shader _lit;
    private readonly Shader _unlit;
    private readonly GpuMesh _triangle;
    private readonly GpuMesh _other;
    private readonly DrawList _list = new();
    private readonly List<RenderableHandle> _added = new();

    // Sees x and y from -10 to 10 and z from 0 to 10.
    private static readonly CullFrustum s_frustum = new(Float4x4.CreateScale(0.1f), default);

    public BucketTests()
    {
        InternalShaders.Register();
        _uploader = new Uploader(_device, InternalShaders.Compute(_device, _compiler, "Internal/Scatter.glsl"));
        _geometry = new GeometryPool(_device, _store, 1 << 18, 1 << 18);
        _materials = new MaterialSystem(_device, _store, new NoResources());
        _world = new RenderWorld(_device, _store);
        _buckets = new BucketSet(_world.Render, _programs);
        _lit = new Shader(ShaderParser.Parse(Lit), _compiler);
        _unlit = new Shader(ShaderParser.Parse("""Shader "Test/Unlit" { Pass { Tags { "LightMode" = "Forward" } GLSLPROGRAM ENDGLSL } }"""), _compiler);
        _triangle = Mesh(3);
        _other = Mesh(3);
    }

    public void Dispose()
    {
        _world.Dispose();
        _uploader.Dispose();
        _store.Dispose();
        _device.Dispose();
    }

    private GpuMesh Mesh(int vertices, float spacing = 0.001f)
    {
        Float3[] positions = Enumerable.Range(0, vertices).Select(i => new Float3(i / 3 * spacing, i % 3 == 2 ? 0.001f : 0, 0)).ToArray();
        GpuMesh mesh = _geometry.Create(new MeshData
        {
            VertexCount = (uint)vertices,
            Streams = [new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(positions.AsSpan()).ToArray() }],
            Indices = Enumerable.Range(0, vertices).Select(i => (uint)i).ToArray(),
        });
        _geometry.Apply(_uploader, 0, long.MaxValue, new List<GpuMesh>());
        return mesh;
    }

    private Material Material(Shader? shader = null) => _materials.Create(shader ?? _lit);

    private RenderableHandle Add(GpuMesh mesh, Material material, Float4x4? transform = null, RenderableFlags flags = RenderableFlags.CastShadows, uint layer = 1)
    {
        RenderableHandle handle = _world.Add(new RenderableDesc { Mesh = mesh, Transform = (Double4x4)(transform ?? Float4x4.CreateTranslation(new Float3(0, 0, 5))), Flags = flags, LayerMask = layer }, [material]);
        _added.Add(handle);
        return handle;
    }

    // Removes every renderable Add made and runs a frame so the buckets let go of them
    private void Clear()
    {
        foreach (RenderableHandle handle in _added)
            _world.Remove(handle);
        _added.Clear();
        Frame(Transparent);
    }

    private const int Transparent = -1;

    private DrawList Frame(int pass, uint mask = ~0u, CullFrustum? frustum = null)
    {
        List<Material> changed = new();
        _materials.Extract(changed);
        _world.Extract();
        _buckets.Update(_world.Render.DrawChanged, _world.Render.BoundsChanged, changed);
        _buckets.Cull(frustum ?? s_frustum, mask);
        if (pass == Transparent)
            _buckets.BuildTransparent(frustum ?? s_frustum, _list);
        else
            _buckets.Build(pass, frustum ?? s_frustum, _list);
        return _list;
    }

    private Material Glass(int queue = 3000)
    {
        Material material = Material();
        material.RenderQueue = queue;
        return material;
    }

    private uint[] Instances(DrawList list) => list.Instances[..list.InstanceCount];

    private static Float4x4 At(float x, float y = 0, float z = 5) => Float4x4.CreateTranslation(new Float3(x, y, z));

    [Fact]
    public void ObjectsSharingAChunkAndProgram_DrawAsOneCommand_WhateverTheirMaterials()
    {
        RenderableHandle a = Add(_triangle, Material());
        RenderableHandle b = Add(_triangle, Material(), At(1));

        DrawList list = Frame(Forward);
        Assert.Equal(1, list.CommandCount);
        Assert.Equal(2u, list.Commands[0].InstanceCount);
        Assert.Equal(new uint[] { (uint)a.Index, (uint)b.Index }, list.Instances[..2]);
        Assert.Single(list.Calls);

        Add(_triangle, Material());
        Add(_other, Material(), At(1));

        DrawList listPage = Frame(Forward);
        Assert.Equal(2, listPage.CommandCount);
        Assert.Equal(new DrawCall(listPage.Calls[0].Program, 0, PrimitiveTopology.TriangleList, false, 0, 2), Assert.Single(listPage.Calls));
        Assert.NotEqual(listPage.Records[0].Chunk, listPage.Records[1].Chunk);
        Assert.Equal(1u, listPage.Commands[1].FirstInstance);
    }

    [Fact]
    public void DifferentPrograms_SplitCalls_AndKeywordChangesRebuildBuckets()
    {
        Material plain = Material();
        Material mapped = Material();
        Add(_triangle, plain);
        Add(_triangle, mapped, At(1));
        Assert.Single(Frame(Forward).Calls);

        mapped.Set(mapped.Shader.Axis("_NORMALMAP"), true);
        DrawList list = Frame(Forward);
        Assert.Equal(2, list.Calls.Count);
        Assert.Equal(2, list.CommandCount);
    }

    [Fact]
    public void SortOrderThenQueue_OrderTransparentItems_BeforeDepth()
    {
        Material late = Glass(3100);
        Material early = Glass(3000);
        RenderableHandle lateFar = Add(_triangle, late, At(0, 0, 9));
        RenderableHandle earlyNear = Add(_other, early, At(1, 0, 1));
        RenderableHandle orderedFar = Add(_other, early, At(2, 0, 9));
        _world.SetSort(orderedFar, new TransparentOrder(100000));

        Assert.Equal(new uint[] { (uint)earlyNear.Index, (uint)lateFar.Index, (uint)orderedFar.Index }, Instances(Frame(Transparent)));
        Clear();

        Material glass = Glass();
        RenderableHandle near = Add(_triangle, glass, At(0, 0, 2));
        RenderableHandle far = Add(_other, glass, At(0, 0, 8));
        RenderableHandle middle = Add(_triangle, glass, At(1, 0, 5));
        Assert.Equal(new uint[] { (uint)far.Index, (uint)middle.Index, (uint)near.Index }, Instances(Frame(Transparent)));
        Assert.Equal(3, _buckets.TransparentCount);
        Clear();

        RenderableHandle negative = Add(_triangle, glass, At(0, 0, 1));
        RenderableHandle behind = Add(_other, glass, At(1, 0, 9));
        _world.SetSort(negative, new TransparentOrder(-70000));
        Assert.Equal(new uint[] { (uint)negative.Index, (uint)behind.Index }, Instances(Frame(Transparent)));
        Clear();

        Material lateQueue = Material(_unlit);
        Material earlyQueue = Material(_unlit);
        RenderableHandle lateObject = Add(_triangle, lateQueue);
        RenderableHandle earlyObject = Add(_triangle, earlyQueue, At(1));
        earlyQueue.RenderQueue = 1000;
        DrawList list = Frame(Forward);
        Assert.Equal(2, list.CommandCount);
        Assert.Equal(new uint[] { (uint)earlyObject.Index, (uint)lateObject.Index }, Instances(list));
        earlyQueue.RenderQueue = 2400;
        Assert.Equal(new uint[] { (uint)lateObject.Index, (uint)earlyObject.Index }, Instances(Frame(Forward)));
        Clear();

        Material lowQueue = Material(_unlit);
        lowQueue.RenderQueue = 1000;
        Add(_triangle, Material(_unlit));
        Add(_other, lowQueue, At(1));
        Assert.Equal(_other.Placement!.FirstChunk, Frame(Forward).Records[0].Chunk);
    }

    [Fact]
    public void SortGroup_DrawsItsMembersTogether_AtItsAnchor()
    {
        Material glass = Glass();
        RenderableHandle groupBack = Add(_triangle, glass, At(0, 0, 9));
        RenderableHandle loose = Add(_other, glass, At(1, 0, 5));
        RenderableHandle groupFront = Add(_triangle, glass, At(2, 0, 2));
        _world.SetSort(groupBack, new TransparentOrder(0, 7, new Double3(0, 0, 8)));
        _world.SetSort(groupFront, new TransparentOrder(0, 7, new Double3(0, 0, 8)));

        Assert.Equal(new uint[] { (uint)groupBack.Index, (uint)groupFront.Index, (uint)loose.Index }, Instances(Frame(Transparent)));
        Clear();

        RenderableHandle innerFront = Add(_triangle, glass, At(0, 0, 2));
        RenderableHandle innerBack = Add(_other, glass, At(1, 0, 9));
        _world.SetSort(innerFront, new TransparentOrder(0, 3, new Double3(0, 0, 5), InnerOrder: -1));
        _world.SetSort(innerBack, new TransparentOrder(0, 3, new Double3(0, 0, 5), InnerOrder: 1));
        Assert.Equal(new uint[] { (uint)innerFront.Index, (uint)innerBack.Index }, Instances(Frame(Transparent)));
        Clear();

        RenderableHandle member = Add(_triangle, glass, At(0, 0, 1));
        RenderableHandle far = Add(_other, glass, At(1, 0, 9));
        _world.SetSort(member, new TransparentOrder(-1, 5, new Double3(0, 0, 1)));
        Assert.Equal(new uint[] { (uint)member.Index, (uint)far.Index }, Instances(Frame(Transparent)));
    }

    // Looks along +Z from the origin, x and y from -10 to 10, z from 0 to 10
    private static CullFrustum Orthographic()
        => new(ViewCamera.Orthographic(Double3.Zero, new Float3(0, 0, 1), new Float3(0, 1, 0), 20, 0, 10, aspect: 1).Layers, 1);

    [Fact]
    public void OrthographicTransparentItems_SortByViewDepth_NotDistanceFromTheCamera()
    {
        Material glass = Glass();
        RenderableHandle back = Add(_triangle, glass, At(0, 0, 8));
        RenderableHandle front = Add(_other, glass, At(8, 0, 3));

        Assert.Equal(new uint[] { (uint)back.Index, (uint)front.Index }, Instances(Frame(Transparent, frustum: Orthographic())));
        Clear();

        RenderableHandle background = Add(_triangle, glass, At(0, 0, 8) * Float4x4.CreateScale(20000));
        RenderableHandle small = Add(_other, glass, At(0, 0, 4));
        Assert.Equal(new uint[] { (uint)background.Index, (uint)small.Index }, Instances(Frame(Transparent, frustum: Orthographic())));
        Clear();

        List<uint> added = new();
        for (int i = 0; i < 40; i++)
            added.Add((uint)Add(i % 2 == 0 ? _triangle : _other, glass, At(9 - i * 0.45f, 0, 5)).Index);
        Assert.Equal(added.Order().ToArray(), Instances(Frame(Transparent, frustum: Orthographic())));
    }

    [Fact]
    public void FarFromTheOrigin_CullingIsRelativeToTheCamera()
    {
        Double3 camera = new(10_000_000, 0, -3_000_000);
        Material material = Material();
        RenderableHandle near = _world.Add(new RenderableDesc { Mesh = _triangle, Transform = Double4x4.CreateTranslation(camera + new Double3(9.5, 0, 5)), LayerMask = 1 }, [material]);
        _world.Add(new RenderableDesc { Mesh = _other, Transform = Double4x4.CreateTranslation(camera + new Double3(10.5, 0, 5)), LayerMask = 1 }, [material]);

        DrawList list = Frame(Forward, frustum: new CullFrustum(Float4x4.CreateScale(0.1f), camera));
        Assert.Equal(1, list.InstanceCount);
        Assert.Equal((uint)near.Index, list.Instances[0]);
    }

    [Fact]
    public void RemovedObjects_LeaveTheirBuckets_AndEmptyBucketsGo()
    {
        Material material = Material();
        RenderableHandle a = Add(_triangle, material);
        RenderableHandle b = Add(_triangle, material, At(1));
        Frame(Forward);
        int buckets = _buckets.BucketCount;

        _world.Remove(a);
        DrawList list = Frame(Forward);
        Assert.Equal((uint)b.Index, list.Instances[0]);
        Assert.Equal(1u, list.Commands[0].InstanceCount);

        _world.Remove(b);
        Frame(Forward);
        Assert.True(_buckets.BucketCount < buckets);
        Assert.Equal(0, _buckets.BucketCount);

        Material glass = Glass();
        RenderableHandle visible = Add(_triangle, glass, At(0, 0, 5));
        RenderableHandle outside = Add(_other, glass, At(50, 0, 5));
        Assert.Equal(new uint[] { (uint)visible.Index }, Instances(Frame(Transparent)));

        _world.Remove(visible);
        _world.Remove(outside);
        Assert.Equal(0, Frame(Transparent).CommandCount);
        Assert.Equal(0, _buckets.TransparentCount);
    }

    [Fact]
    public void StaticObjectsThatMove_AreCulledWhereTheyAre()
    {
        Material material = Material();
        RenderableHandle handle = Add(_triangle, material, flags: RenderableFlags.Static);
        Add(_triangle, material, At(1), RenderableFlags.Static);
        Assert.Equal(2, Frame(Forward).InstanceCount);

        _world.SetTransform(handle, At(50));
        Assert.Equal(1, Frame(Forward).InstanceCount);
        _world.SetTransform(handle, At(-2));
        Assert.Equal(2, Frame(Forward).InstanceCount);

        _world.Remove(handle);
        Assert.Equal(1, Frame(Forward).InstanceCount);
    }

    [Fact]
    public void ManyObjects_TheTreesCullJustAsTestingEachOneWould()
    {
        Material material = Material();
        RenderableHandle inside = Add(_triangle, material);
        Add(_triangle, material, At(50));
        Add(_triangle, material, layer: 2);
        Add(_triangle, material, flags: RenderableFlags.Hidden);
        Add(_other, material, At(0, 0, -5));
        DrawList culled = Frame(Forward, mask: 1);
        Assert.Equal(1, culled.CommandCount);
        Assert.Equal(1u, culled.Commands[0].InstanceCount);
        Assert.Equal((uint)inside.Index, culled.Instances[0]);
        Clear();

        GpuMesh strip = Mesh(5000 * 3, 0.005f);
        Add(strip, material, At(-8));
        DrawList chunks = Frame(Forward);
        Assert.Equal(2, chunks.CommandCount);
        Assert.Equal([6144u, 6144u], chunks.Commands[..2].Select(c => c.IndexCount));
        Clear();

        RenderableHandle moving = Add(_triangle, material);
        Frame(Forward);
        int buckets = _buckets.BucketCount;
        _world.SetTransform(moving, At(50));
        Assert.Equal(0, Frame(Forward).CommandCount);
        Assert.Empty(_world.Render.DrawChanged);
        Assert.Equal(buckets, _buckets.BucketCount);
        Clear();

        Random random = new(7);
        List<(RenderableHandle Handle, Float3 Position)> objects = new();
        for (int i = 0; i < 3000; i++)
        {
            Float3 position = new((float)(random.NextDouble() * 60 - 30), (float)(random.NextDouble() * 60 - 30), (float)(random.NextDouble() * 30 - 10));
            RenderableFlags flags = i % 3 == 0 ? RenderableFlags.CastShadows : RenderableFlags.Static | RenderableFlags.CastShadows;
            objects.Add((Add(_triangle, material, Float4x4.CreateTranslation(position), flags), position));
        }

        for (int frame = 0; frame < 3; frame++)
        {
            for (int i = 0; i < objects.Count; i += 7)
            {
                Float3 position = objects[i].Position + new Float3(frame * 3 - 3, 0, 0);
                objects[i] = (objects[i].Handle, position);
                _world.SetTransform(objects[i].Handle, Float4x4.CreateTranslation(position));
            }
            DrawList list = Frame(Shadows);
            HashSet<uint> expected = new();
            foreach ((RenderableHandle handle, _) in objects)
            {
                if (s_frustum.Sphere(_world.Render.WorldCenter[handle.Index], _world.Render.WorldRadius[handle.Index]))
                    expected.Add((uint)handle.Index);
            }
            Assert.NotEmpty(expected);
            Assert.Equal(expected.OrderBy(i => i), Instances(list).OrderBy(i => i));
        }
    }
}
