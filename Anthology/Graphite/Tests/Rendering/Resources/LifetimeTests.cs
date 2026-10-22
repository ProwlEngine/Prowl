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
                    void main() { Graphite_WriteGBuffer(vec3(0.0, 0.0, -1.0), 1.0, 0.0); outColor = texture(_MainTex, vec2(0.5)); }
                }
                ENDGLSL
            }
        }
        """;

    private readonly GraphicsDevice _device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
    private readonly RenderStore _store = new();
    private readonly GlslShaderCompiler _compiler = new();
    private readonly RendererTextures _textures;
    private readonly MaterialSystem _materials;
    private readonly Shader _shader;

    public LifetimeTests()
    {
        InternalShaders.Register();
        _textures = new RendererTextures(_device);
        _materials = new MaterialSystem(_device, _store, _textures);
        _shader = new Shader(ShaderParser.Parse(Source), _compiler);
    }

    public void Dispose()
    {
        _textures.Dispose();
        _compiler.Dispose();
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
        DrawPrograms programs = new() { IdleFrames = 0 };
        List<ushort> freed = new();
        programs.Freed += freed.Add;
        Material plain = _materials.Create(_shader);
        Material tinted = _materials.Create(_shader);
        tinted.EnableKeyword("_TINT");
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
    }

    [Fact]
    public void Frustums_AllocateNothing()
    {
        Float4x4 projection = Float4x4.CreateScale(0.1f);
        _ = new CullFrustum(projection, default);
        long before = GC.GetAllocatedBytesForCurrentThread();
        CullFrustum frustum = new(projection, new Double3(1, 2, 3));
        bool visible = frustum.Sphere(new Double3(1, 2, 5), 1);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(visible);
    }

    [Fact]
    public void TableLogs_StayBounded_WithoutTables()
    {
        using TableRing ring = new(_device, GraphiteBindings.ViewTable(4), "Unused");
        using Sampler sampler = _device.ResourceFactory.CreateSampler(SamplerDescription.Point);
        using Sampler other = _device.ResourceFactory.CreateSampler(SamplerDescription.Linear);
        for (int i = 0; i < 5000; i++)
            ring.Set("Graphite_Samplers", 0, i % 2 == 0 ? sampler : other);

        Assert.True(ring.LogCount < 1024);
        Assert.NotNull(ring.Get(0));
        Assert.Equal(0, ring.LogCount);
    }
}
