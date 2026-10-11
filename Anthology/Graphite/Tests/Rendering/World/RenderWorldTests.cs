using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;
using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Rendering.Tests;

file sealed class NoResources : IMaterialResources
{
    public uint TextureSlot(Texture? texture, MaterialPropertyType type, string defaultName) => 0;

    public uint SamplerSlot(SamplerDescription? sampler) => 0;
}

public sealed class RenderWorldTests : IDisposable
{
    private readonly GraphicsDevice _device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
    private readonly GlslShaderCompiler _compiler = new();
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
        _geometry = new GeometryPool(_device, 1 << 16, 1 << 16);
        _materials = new MaterialSystem(_device, new NoResources());
        _world = new RenderWorld(_device);
        _shader = new Shader(ShaderParser.Parse("""Shader "Test/Plain" { Pass { GLSLPROGRAM ENDGLSL } }"""), _compiler);
        _cube = Mesh(new Float3(-1, -1, -1), new Float3(1, 1, 1));
    }

    public void Dispose()
    {
        _world.Dispose();
        _materials.Dispose();
        _geometry.Dispose();
        _uploader.Dispose();
        _compiler.Dispose();
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

    private static Float4x4 Translate(float x, float y, float z) => Float4x4.CreateTranslation(new Float3(x, y, z));

    [Fact]
    public void AddedRenderables_GetTheirRecords_AndAreReportedAsDrawChanges()
    {
        Material material = NewMaterial();
        RenderableHandle handle = _world.Add(new RenderableDesc
        {
            Mesh = _cube,
            Transform = Translate(10, 0, 0) * Float4x4.CreateScale(2),
            ObjectId = 77,
            Custom0 = new Float4(1, 2, 3, 4),
        }, [material]);

        RenderWorld.RenderSide render = Frame();
        uint index = (uint)handle.Index;
        Assert.Equal([handle.Index], render.DrawChanged);

        ObjectTransform transform = render.Transforms.Get<ObjectTransform>(index);
        Assert.Equal(new Float4(2, 0, 0, 10), transform.Row0);
        Assert.Equal(new Float4(0, 2, 0, 0), transform.Row1);

        ObjectAttributes attributes = render.Attributes.Get<ObjectAttributes>(index);
        Assert.Equal(77u, attributes.ObjectId);
        Assert.Equal(new Float4(1, 2, 3, 4), attributes.Custom0);
        Assert.Equal(ObjectAttributes.NoPrevious, attributes.PreviousTransform);
        Assert.Equal(material.Slot, render.MaterialSlots.Get<uint>(attributes.MaterialSliceBase));

        ObjectBounds bounds = render.Bounds.Get<ObjectBounds>(index);
        Assert.Equal(new Float3(8, -2, -2), bounds.Min);
        Assert.Equal(new Float3(12, 2, 2), bounds.Max);
        Assert.Equal(new Float4(10, 0, 0, MathF.Sqrt(12)), bounds.Sphere);
    }

    [Fact]
    public void EqualTransforms_RecordNothing()
    {
        RenderableHandle handle = _world.Add(new RenderableDesc { Mesh = _cube, Transform = Translate(1, 2, 3) }, [NewMaterial()]);
        Frame();

        _world.SetTransform(handle, Translate(1, 2, 3));
        Assert.Equal(0, _world.PendingChanges);

        _world.SetTransform(handle, Translate(1, 2, 4));
        Assert.Equal(1, _world.PendingChanges);
        Assert.Empty(Frame().DrawChanged);
    }

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
    public void ObjectsWithoutMotionVectors_HaveNoPreviousTransform()
    {
        RenderableHandle handle = _world.Add(new RenderableDesc { Mesh = _cube, Flags = RenderableFlags.MotionVectors }, [NewMaterial()]);
        Frame();
        Assert.NotEqual(ObjectAttributes.NoPrevious, _world.Render.Attributes.Get<ObjectAttributes>((uint)handle.Index).PreviousTransform);

        _world.SetFlags(handle, RenderableFlags.None);
        Frame();
        Assert.Equal(ObjectAttributes.NoPrevious, _world.Render.Attributes.Get<ObjectAttributes>((uint)handle.Index).PreviousTransform);
    }

    [Fact]
    public void NegativeDeterminants_MarkTheObjectMirrored_AndChangeItsDraws()
    {
        RenderableHandle handle = _world.Add(new RenderableDesc { Mesh = _cube }, [NewMaterial()]);
        Frame();
        Assert.False(_world.Render.Mirrored[handle.Index]);

        _world.SetTransform(handle, Float4x4.CreateScale(-1, 1, 1));
        RenderWorld.RenderSide render = Frame();
        Assert.True(render.Mirrored[handle.Index]);
        Assert.Equal([handle.Index], render.DrawChanged);
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

    [Fact]
    public void RemoveThenAdd_InOneFrame_ReplacesTheObject()
    {
        Material a = NewMaterial();
        Material b = NewMaterial();
        RenderableHandle first = _world.Add(new RenderableDesc { Mesh = _cube }, [a]);
        Frame();

        _world.Remove(first);
        RenderableHandle second = _world.Add(new RenderableDesc { Mesh = _cube, Transform = Translate(5, 0, 0) }, [b]);
        RenderWorld.RenderSide render = Frame();

        Assert.Equal(first.Index, second.Index);
        Assert.True(render.Alive[second.Index]);
        Assert.Equal(b, render.SliceMaterials[render.SliceBase[second.Index]]);
        Assert.Equal(5f, render.Transforms.Get<ObjectTransform>((uint)second.Index).Row0.W);
    }

    [Fact]
    public void MaterialSlices_KeepTheMaterialsAsGiven()
    {
        Material a = NewMaterial();
        Material b = NewMaterial();
        RenderableHandle handle = _world.Add(new RenderableDesc { Mesh = _cube }, [a, b]);
        RenderWorld.RenderSide render = Frame();

        uint sliceBase = render.SliceBase[handle.Index];
        Assert.Equal(2, render.SliceCount[handle.Index]);
        Assert.Equal([a.Slot, b.Slot], [render.MaterialSlots.Get<uint>(sliceBase), render.MaterialSlots.Get<uint>(sliceBase + 1)]);

        _world.SetMaterials(handle, [b]);
        render = Frame();
        Assert.Equal([handle.Index], render.DrawChanged);
        Assert.Equal(b.Slot, render.MaterialSlots.Get<uint>(render.SliceBase[handle.Index]));
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
        Assert.Equal(new Float3(4, 4, 4), render.Bounds.Get<ObjectBounds>((uint)handle.Index).Max);
    }

    [Fact]
    public void MaterialsMovingToAnotherShader_UpdateTheirSlots()
    {
        Shader other = new(ShaderParser.Parse("""Shader "Test/Other" { Properties { _A("A", Float) = 1 } Pass { GLSLPROGRAM ENDGLSL } }"""), _compiler);
        _materials.Create(other);
        Material material = NewMaterial();
        RenderableHandle handle = _world.Add(new RenderableDesc { Mesh = _cube }, [material]);
        Frame();
        uint entry = _world.Render.SliceBase[handle.Index];
        uint before = _world.Render.MaterialSlots.Get<uint>(entry);

        material.Shader = other;
        List<Material> drawChanged = new();
        _materials.Extract(drawChanged);
        _world.Extract();
        _world.Render.Update([], drawChanged);

        Assert.Equal(material.Slot, _world.Render.MaterialSlots.Get<uint>(entry));
        Assert.NotEqual(before, material.Slot);
    }

    [Fact]
    public void BoundsOverrides_ReplaceTheMeshBounds()
    {
        RenderableHandle handle = _world.Add(new RenderableDesc { Mesh = _cube }, [NewMaterial()]);
        _world.SetBounds(handle, new AABB { Min = new Float3(0, 0, 0), Max = new Float3(3, 3, 3) });
        Assert.Equal(new Float3(3, 3, 3), Frame().Bounds.Get<ObjectBounds>((uint)handle.Index).Max);

        _world.ClearBounds(handle);
        Assert.Equal(new Float3(1, 1, 1), Frame().Bounds.Get<ObjectBounds>((uint)handle.Index).Max);
    }
}
