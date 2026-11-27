using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;
using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Rendering.Tests;

// What the renderer frees once nothing needs it: bindless slots, draw program ids, material tables and table logs.
[Collection("GPU Tests")]
public sealed class LifetimeTests : IDisposable
{
    private const string Source = """
        Shader "Test/Textured"
        {
            Properties
            {
                _MainTex("Albedo", Texture2D) = "white" {}
            }

            Pass
            {
                Tags { "LightMode" = "Forward" }
                GLSLPROGRAM
                Shared { #pragma variant _TINT }
                Vertex
                {
                    #include "Graphite/Vertex.glsl"
                    void main() { gl_Position = Graphite_ObjectToClip(vertexPosition); }
                }
                Fragment
                {
                    #include "Graphite/Passes.glsl"
                    #include "Graphite/Core.glsl"
                    uniform sampler2D _MainTex;
                    layout(location = 0) out vec4 outColor;
                    void main() { Graphite_WriteGBuffer(vec3(0.0, 0.0, -1.0), vec3(0.0), 1.0); outColor = texture(_MainTex, vec2(0.5)); }
                }
                ENDGLSL
            }

            Pass { Tags { "LightMode" = "ShadowCaster" } GLSLPROGRAM ENDGLSL }
        }
        """;

    private readonly GraphicsDevice _device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
    private readonly RenderStore _store = new();
    private readonly GlslShaderCompiler _compiler = TestCompiler.Shared;
    private readonly Sampler _sampler;
    private readonly RendererTextures _textures;
    private readonly MaterialSystem _materials;
    private readonly Shader _shader;

    public LifetimeTests()
    {
        InternalShaders.Register();
        _sampler = _device.ResourceFactory.CreateSampler(SamplerDescription.Linear);
        _textures = new RendererTextures(_device, _sampler);
        _materials = new MaterialSystem(_device, _store, _textures);
        _shader = new Shader(ShaderParser.Parse(Source), _compiler);
    }

    public void Dispose()
    {
        _textures.Dispose();
        _sampler.Dispose();
        _store.Dispose();
        _device.Dispose();
    }

    private Texture Texture() => _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));

    private uint SlotOf(Material material) => material.Table!.Records.Read(material.Slot)[(int)(_shader.Properties[0].Offset / 4)];

    [Fact]
    public void DisposedTextures_FreeTheirSlot_AfterRetire_AndMaterialsFallBackToTheDefault()
    {
        Texture texture = Texture();
        Material material = _materials.Create(_shader);
        material.SetTexture("_MainTex", texture);
        _materials.Extract(new List<Material>());
        uint slot = SlotOf(material);

        texture.Dispose();
        HashSet<Texture> freed = new();
        Assert.True(_textures.Sweep(lastDispatched: 5, completed: 4, freed));
        Assert.Equal(texture, Assert.Single(freed));
        _materials.MarkAllRecords();
        _materials.Extract(new List<Material>());
        uint fallback = SlotOf(material);
        Assert.NotEqual(slot, fallback);

        using Texture early = Texture();
        Material other = _materials.Create(_shader);
        other.SetTexture("_MainTex", early);
        _materials.Extract(new List<Material>());
        Assert.NotEqual(slot, SlotOf(other));

        Assert.False(_textures.Sweep(lastDispatched: 6, completed: 5, freed));
        using Texture late = Texture();
        Material third = _materials.Create(_shader);
        third.SetTexture("_MainTex", late);
        _materials.Extract(new List<Material>());
        Assert.Equal(slot, SlotOf(third));
    }

    [Fact]
    public void MaterialTables_GoWhenTheirLastMaterialRetires()
    {
        InternalShaders.Register();
        using Uploader uploader = new(_device, InternalShaders.Compute(_device, _compiler, "Internal/Scatter.glsl"));
        Material material = _materials.Create(_shader);
        _materials.Extract(new List<Material>());
        Assert.True(_materials.Flush(uploader, 0));
        Assert.True(_materials.Tables.ContainsKey(_shader));

        material.Dispose();
        _materials.Extract(new List<Material>());
        _materials.Flush(uploader, 3);
        _materials.Reclaim(2);
        Assert.True(_materials.Tables.ContainsKey(_shader));

        _materials.Reclaim(3);
        Assert.False(_materials.Tables.ContainsKey(_shader));
        Assert.True(_materials.Flush(uploader, 3));
        uploader.Clear();
    }

    [Fact]
    public void DrawProgramIds_AreFreedWhenUnreferenced_AndReusedForTheNextProgram()
    {
        DrawPrograms programs = new();
        List<ushort> freed = new();
        programs.Freed += freed.Add;
        Material plain = _materials.Create(_shader);
        Material tinted = _materials.Create(_shader);
        tinted.Set(_shader.Axis("_TINT"), true);
        _materials.Extract(new List<Material>());

        Assert.True(programs.TryGet(plain.Draw, PassType.Forward, out ushort first));
        programs.Acquire(first);
        programs.Acquire(first);
        programs.Release(first);
        programs.Sweep();
        Assert.Empty(freed);

        programs.Release(first);
        programs.Sweep();
        Assert.Equal([first], freed);
        Assert.Equal(0, programs.Count);

        Assert.True(programs.TryGet(tinted.Draw, PassType.Forward, out ushort reused));
        Assert.Equal(first, reused);
        Assert.Equal(tinted.Draw.Keywords, programs[reused].Keywords);

        using Uploader uploader = new(_device, InternalShaders.Compute(_device, _compiler, "Internal/Scatter.glsl"));
        GeometryPool geometry = new(_device, _store, 1 << 16, 1 << 16);
        using RenderWorld world = new(_device, _store);
        DrawPrograms bucketPrograms = new();
        BucketSet buckets = new(world.Render, bucketPrograms);
        Float3[] positions = [new(0, 0, 0), new(0.001f, 0, 0), new(0, 0.001f, 0)];
        GpuMesh mesh = geometry.Create(new MeshData
        {
            VertexCount = 3,
            Streams = [new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(positions.AsSpan()).ToArray() }],
            Indices = new uint[] { 0, 1, 2 },
        });
        geometry.Apply(uploader, 0, long.MaxValue, new List<GpuMesh>());
        Material bucketed = _materials.Create(_shader);
        world.Add(new RenderableDesc { Mesh = mesh, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 5)), LayerMask = 1 }, [bucketed]);
        CullFrustum frustum = new(Float4x4.CreateScale(0.1f), default);
        DrawList list = new();
        void Frame()
        {
            List<Material> changed = new();
            _materials.Extract(changed);
            world.Extract();
            buckets.Update(world.Render.DrawChanged, world.Render.BoundsChanged, changed);
            buckets.Cull(frustum, ~0u);
            buckets.Build(0, frustum, list);
        }

        Frame();
        int ids = bucketPrograms.Count;
        int reading = 0;
        for (int p = 0; p < _shader.Definition.Passes!.Length; p++)
            reading += _shader.PassHasAxis(p, "_TINT") ? 1 : 0;

        // Each switch takes the new variant's ids and frees the old ones at the next sweep, passes without the axis keep one id
        for (int i = 0; i < 4; i++)
        {
            bucketed.Set(_shader.Axis("_TINT"), i % 2 == 0);
            bucketPrograms.Sweep();
            Frame();
        }
        Assert.True(reading < ids);
        Assert.Equal(ids + reading, bucketPrograms.Count);

        bucketPrograms.Sweep();
        Assert.Equal(ids, bucketPrograms.Count);
    }
}
