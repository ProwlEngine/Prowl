using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;
using Prowl.Vector;

using Xunit;

using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

namespace Prowl.Graphite.Rendering.Tests;

// Draws a lit quad filling the view and checks every pixel against Prowl's lighting math done on the CPU, so Graphite's
// port stays exact: point and spot lights from both trees, directional lights, ambient, fog and decals.
public sealed class LightingTests : IDisposable
{
    private const string Source = """
        Shader "Test/Lit"
        {
            Properties
            {
                _Color("Albedo", Color) = (0.8, 0.8, 0.8, 1)
                _Roughness("Roughness", Float) = 0.5
                _Metallic("Metallic", Float) = 0
            }

            Pass
            {
                Tags { "LightMode" = "Forward" }
                Cull Off
                GLSLPROGRAM
                Vertex
                {
                    #include "Graphite/Vertex.glsl"
                    layout(location = 0) out vec3 vWorld;
                    layout(location = 1) out vec3 vNormal;
                    void main()
                    {
                        vWorld = Graphite_ObjectToWorld(vertexPosition);
                        vNormal = Graphite_ObjectToWorldNormal(vertexNormal);
                        gl_Position = Graphite_RelativeToClip(vWorld);
                    }
                }
                Fragment
                {
                    #include "Graphite/Lighting.glsl"
                    #include "Graphite/Decals.glsl"
                    #include "Graphite/Passes.glsl"
                    uniform vec4 _Color;
                    uniform float _Roughness;
                    uniform float _Metallic;
                    layout(location = 0) in vec3 vWorld;
                    layout(location = 1) in vec3 vNormal;
                    layout(location = 0) out vec4 outColor;
                    void main()
                    {
                        vec3 n = normalize(vNormal);
                        vec3 albedo = _Color.rgb;
                        float roughness = _Roughness, metallic = _Metallic, ao = 1.0;
                        Graphite_ApplyDecals(vWorld, n, albedo, n, roughness, metallic, ao);
                        vec3 color = CalculateForwardLighting(vWorld, n, GetWorldViewDir(vWorld), albedo, metallic, roughness, ao) + CalculateAmbient(n) * albedo;
                        outColor = vec4(ApplyFog(color, vWorld), 1.0);
                        Graphite_WriteGBuffer(n, roughness, metallic);
                    }
                }
                ENDGLSL
            }
        }
        """;

    private const string IndirectSource = """
        Shader "Test/Indirect"
        {
            Pass
            {
                Tags { "LightMode" = "Forward" }
                Cull Off
                GLSLPROGRAM
                Vertex
                {
                    #include "Graphite/Vertex.glsl"
                    layout(location = 0) out vec3 vNormal;
                    layout(location = 1) out vec2 vLightmapUV;
                    void main()
                    {
                        vNormal = Graphite_ObjectToWorldNormal(vertexNormal);
                        vLightmapUV = Graphite_LightmapUV();
                        gl_Position = Graphite_ObjectToClip(vertexPosition);
                    }
                }
                Fragment
                {
                    #include "Graphite/Passes.glsl"
                    #include "Graphite/Lighting.glsl"
                    layout(location = 0) in vec3 vNormal;
                    layout(location = 1) in vec2 vLightmapUV;
                    layout(location = 0) out vec4 outColor;
                    void main()
                    { Graphite_WriteGBuffer(vec3(0.0, 0.0, -1.0), 1.0, 0.0);
                        outColor = vec4(Graphite_IndirectDiffuse(normalize(vNormal), vLightmapUV), 1.0);
                    }
                }
                ENDGLSL
            }
        }
        """;

    private const string SkySource = """
        Shader "Test/Sky"
        {
            Properties
            {
                _Tint("Tint", Color) = (1, 1, 1, 1)
                _Gradient("Gradient", Float) = 1
            }

            Pass
            {
                Tags { "LightMode" = "Sky" }
                GLSLPROGRAM
                Vertex
                {
                    #include "Graphite/Sky.glsl"
                    void main() { }
                }
                Fragment
                {
                    #include "Graphite/Sky.glsl"
                    uniform vec4 _Tint;
                    uniform float _Gradient;
                    layout(location = 0) out vec4 outColor;
                    void main()
                    {
                        vec3 d = Graphite_SkyDirection();
                        outColor = vec4(mix(vec3(1.0), d * 0.5 + 0.5, _Gradient) * _Tint.rgb, 1.0);
                    }
                }
                ENDGLSL
            }
        }
        """;

    // Shows the sky probe's sharpest mip in the direction each pixel looks
    private const string ProbeViewerSource = """
        Shader "Test/ProbeViewer"
        {
            Pass
            {
                Tags { "LightMode" = "Forward" }
                Cull Off
                GLSLPROGRAM
                Vertex
                {
                    #include "Graphite/Vertex.glsl"
                    layout(location = 0) out vec3 vWorld;
                    void main()
                    {
                        vWorld = Graphite_ObjectToWorld(vertexPosition);
                        gl_Position = Graphite_RelativeToClip(vWorld);
                    }
                }
                Fragment
                {
                    #include "Graphite/Passes.glsl"
                    #include "Graphite/Lighting.glsl"
                    layout(location = 0) in vec3 vWorld;
                    layout(location = 0) out vec4 outColor;
                    void main()
                    { Graphite_WriteGBuffer(vec3(0.0, 0.0, -1.0), 1.0, 0.0);
                        outColor = vec4(SampleSkyProbe(normalize(vWorld), 0.0), 1.0);
                    }
                }
                ENDGLSL
            }
        }
        """;

    private const string UnlitSource = """
        Shader "Test/Unlit"
        {
            Properties
            {
                _Color("Color", Color) = (1, 1, 1, 1)
            }

            Pass
            {
                Tags { "LightMode" = "Forward" }
                GLSLPROGRAM
                Vertex
                {
                    #include "Graphite/Vertex.glsl"
                    void main() { gl_Position = Graphite_ObjectToClip(vertexPosition); }
                }
                Fragment
                {
                    #include "Graphite/Passes.glsl"
                    #include "Graphite/Core.glsl"
                    uniform vec4 _Color;
                    layout(location = 0) out vec4 outColor;
                    void main() { Graphite_WriteGBuffer(vec3(0.0, 0.0, -1.0), 1.0, 0.0); outColor = vec4(_Color.rgb, 1.0); }
                }
                ENDGLSL
            }
        }
        """;

    // Shows only what the reflection probes reflect toward the camera
    private const string ReflectSource = """
        Shader "Test/Reflect"
        {
            Properties
            {
                _Roughness("Roughness", Float) = 0
            }

            Pass
            {
                Tags { "LightMode" = "Forward" }
                Cull Off
                GLSLPROGRAM
                Vertex
                {
                    #include "Graphite/Vertex.glsl"
                    layout(location = 0) out vec3 vWorld;
                    layout(location = 1) out vec3 vNormal;
                    void main()
                    {
                        vWorld = Graphite_ObjectToWorld(vertexPosition);
                        vNormal = Graphite_ObjectToWorldNormal(vertexNormal);
                        gl_Position = Graphite_RelativeToClip(vWorld);
                    }
                }
                Fragment
                {
                    #include "Graphite/Passes.glsl"
                    #include "Graphite/Lighting.glsl"
                    uniform float _Roughness;
                    layout(location = 0) in vec3 vWorld;
                    layout(location = 1) in vec3 vNormal;
                    layout(location = 0) out vec4 outColor;
                    void main()
                    { Graphite_WriteGBuffer(vec3(0.0, 0.0, -1.0), 1.0, 0.0);
                        outColor = vec4(EvaluateReflectionProbes(vWorld, normalize(vNormal), GetWorldViewDir(vWorld), _Roughness), 1.0);
                    }
                }
                ENDGLSL
            }
        }
        """;

    // Colours a quad from a buffer the host provides through Globals.SetBuffer
    private const string BufferSource = """
        Shader "Test/Buffer"
        {
            Pass
            {
                Tags { "LightMode" = "Forward" }
                Cull Off
                GLSLPROGRAM
                Vertex
                {
                    #include "Graphite/Vertex.glsl"
                    void main() { gl_Position = Graphite_ObjectToClip(vertexPosition); }
                }
                Fragment
                {
                    #include "Graphite/Passes.glsl"
                    #include "Graphite/Core.glsl"
                    layout(std430) readonly buffer WindField { vec4 wind[]; };
                    layout(location = 0) out vec4 outColor;
                    void main() { Graphite_WriteGBuffer(vec3(0.0, 0.0, -1.0), 1.0, 0.0); outColor = vec4(wind[1].rgb, 1.0); }
                }
                ENDGLSL
            }
        }
        """;

    private const int Size = 16;
    private const float QuadDepth = 10;

    // Orthographic and reversed Z: sees x and y from -4 to 4, and z from 0 (depth 1) to 100 (depth 0).
    private static readonly Float4x4 s_projection = new(new Float4(0.25f, 0, 0, 0), new Float4(0, 0.25f, 0, 0), new Float4(0, 0, -0.01f, 0), new Float4(0, 0, 1, 1));

    private readonly GraphicsDevice _device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
    private readonly GlslShaderCompiler _compiler = new();
    private readonly Texture _target;
    private readonly Framebuffer _framebuffer;
    private readonly DeviceBuffer _readback;
    private readonly Renderer _renderer;
    private readonly RenderWorld _world;
    private readonly Shader _shader;
    private readonly GpuMesh _quad;
    private readonly List<RendererError> _errors = new();
    private Double3 _origin;

    public LightingTests()
    {
        _target = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(Size, Size, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget | TextureUsage.Sampled));
        _framebuffer = _device.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, _target));
        _readback = _device.ResourceFactory.CreateBuffer(new BufferDescription(Size * Size * 16, BufferUsage.Staging));
        _renderer = new Renderer(_device, _compiler, new RendererOptions { Threading = RenderThreading.Inline, Deterministic = true, PageVertexWords = 1 << 16, PageIndices = 1 << 16 });
        _renderer.Error += _errors.Add;
        _world = _renderer.CreateWorld();
        _world.Environment = new EnvironmentDesc { AmbientColor = default, Fog = default };
        _shader = _renderer.LoadShader(ShaderParser.Parse(Source));

        Float3[] positions = [new(-4, -4, 0), new(4, -4, 0), new(4, 4, 0), new(-4, 4, 0)];
        Float3[] normals = [new(0, 0, -1), new(0, 0, -1), new(0, 0, -1), new(0, 0, -1)];
        _quad = _renderer.CreateMesh(new MeshData
        {
            VertexCount = 4,
            Streams =
            [
                new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(positions.AsSpan()).ToArray() },
                new VertexStream { Semantic = VertexSemantic.Normal, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(normals.AsSpan()).ToArray() },
            ],
            Indices = new uint[] { 0, 1, 2, 0, 2, 3 },
        });
    }

    public void Dispose()
    {
        _renderer.Dispose();
        foreach (Texture texture in _textures)
            texture.Dispose();
        _readback.Dispose();
        _framebuffer.Dispose();
        _target.Dispose();
        _compiler.Dispose();
        _device.Dispose();
    }

    private Material Surface(Float3 albedo, float roughness = 0.5f, float metallic = 0)
    {
        Material material = _renderer.CreateMaterial(_shader);
        material.SetColor("_Color", new Float4(albedo.X, albedo.Y, albedo.Z, 1));
        material.SetFloat("_Roughness", roughness);
        material.SetFloat("_Metallic", metallic);
        return material;
    }

    private RenderableHandle AddQuad(Material material, uint layer = ~0u)
        => _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(_origin + new Double3(0, 0, QuadDepth)), LayerMask = layer }, [material]);

    private Float4[] Frame()
    {
        ViewDesc view = new() { World = _world, ViewId = 1, Target = _framebuffer };
        view.Layers[0] = new ViewLayer { View = Float4x4.Identity, Projection = s_projection, Position = _origin };
        _renderer.SubmitFrame(new FrameDesc { Views = new[] { view } });
        _device.WaitForIdle();
        _device.Record(cmd => cmd.CopyTextureToBuffer(_target, _readback, 0, TextureRegion.Whole(_target, 0, 0))).Wait();
        Float4[] pixels = _device.Map<Float4>(_readback).ToArray();
        _device.Unmap(_readback);
        Assert.Empty(_errors);
        return pixels;
    }

    // Camera relative position of a pixel's centre on the quad
    private static Vector3 PixelPosition(int x, int y) => new(4 * ((x + 0.5f) / Size * 2 - 1), 4 * ((y + 0.5f) / Size * 2 - 1), QuadDepth);

    private static void AssertMatches(Func<Vector3, Vector3> expected, Float4[] pixels)
    {
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                Vector3 want = expected(PixelPosition(x, y));
                Float4 got = pixels[y * Size + x];
                for (int c = 0; c < 3; c++)
                {
                    float w = c == 0 ? want.X : c == 1 ? want.Y : want.Z;
                    float g = c == 0 ? got.X : c == 1 ? got.Y : got.Z;
                    Assert.True(MathF.Abs(w - g) <= 2e-4f + 0.01f * MathF.Abs(w), $"pixel {x},{y} channel {c}: expected {w}, got {g}");
                }
            }
        }
    }

    private static readonly Vector3 s_normal = new(0, 0, -1);

    private static LightDesc Point(Double3 position, float range, float intensity, Float3 color, bool isStatic = true) => new()
    {
        Type = LightType.Point,
        Position = position,
        Range = range,
        Intensity = intensity,
        Color = color,
        Static = isStatic,
    };

    [Fact]
    public void PointLights_MatchProwlsLighting()
    {
        Vector3 albedo = new(0.8f, 0.7f, 0.6f);
        AddQuad(Surface(new Float3(albedo.X, albedo.Y, albedo.Z), 0.5f));
        LightDesc light = Point(new Double3(0.5, -0.5, 8), 6, 0.05f, new Float3(1, 0.8f, 0.6f));
        _world.AddLight(light);

        AssertMatches(p => Reference.Local(light, p, s_normal, albedo, 0, 0.5f), Frame());
    }

    [Fact]
    public void MovingLights_LightExactlyLikeStaticOnes()
    {
        AddQuad(Surface(new Float3(0.8f, 0.8f, 0.8f)));
        LightHandle handle = _world.AddLight(Point(new Double3(-1, 1, 8.5), 5, 0.04f, new Float3(1, 1, 1)));
        Float4[] still = Frame();

        _world.SetLight(handle, Point(new Double3(-1, 1, 8.5), 5, 0.04f, new Float3(1, 1, 1), isStatic: false));
        Float4[] moving = Frame();
        for (int i = 0; i < still.Length; i++)
            Assert.True(Vector3.Distance(new Vector3(still[i].X, still[i].Y, still[i].Z), new Vector3(moving[i].X, moving[i].Y, moving[i].Z)) < 1e-5f);
    }

    [Fact]
    public void LightsFarFromTheOrigin_LightLikeNearIt()
    {
        RenderableHandle quad = AddQuad(Surface(new Float3(0.8f, 0.8f, 0.8f)));
        LightHandle still = _world.AddLight(Point(new Double3(0.25, 0.75, 8), 5, 0.05f, new Float3(1, 1, 1)));
        LightHandle moving = _world.AddLight(Point(new Double3(-1.5, -0.5, 9), 3, 0.02f, new Float3(1, 0.5f, 0.25f), isStatic: false));
        Float4[] near = Frame();

        _origin = new Double3(10_000_000.25, 0, -3_000_000);
        _world.SetTransform(quad, Double4x4.CreateTranslation(_origin + new Double3(0, 0, QuadDepth)));
        _world.SetLight(still, Point(_origin + new Double3(0.25, 0.75, 8), 5, 0.05f, new Float3(1, 1, 1)));
        _world.SetLight(moving, Point(_origin + new Double3(-1.5, -0.5, 9), 3, 0.02f, new Float3(1, 0.5f, 0.25f), isStatic: false));
        Float4[] distant = Frame();

        for (int i = 0; i < near.Length; i++)
            Assert.True(MathF.Abs(near[i].X - distant[i].X) < 2e-4f + 0.01f * near[i].X, $"pixel {i}: {near[i].X} near, {distant[i].X} far");
    }

    // A light as the reference sees it, its position relative to the camera
    private LightDesc Relative(LightDesc light)
    {
        light.Position -= _origin;
        return light;
    }

    [Fact]
    public void StaticLightsAcrossASolarSystem_LightAsPreciselyAsNearTheOrigin()
    {
        Vector3 albedo = new(0.7f, 0.7f, 0.7f);
        _origin = new Double3(149_597_870_700.0, 1_234_567.25, -42_000_000.5);
        AddQuad(Surface(new Float3(albedo.X, albedo.Y, albedo.Z), 0.6f));
        _world.AddLight(Point(new Double3(-149_597_870_700.0, 0, 0), 5, 1, new Float3(1, 1, 1)));
        _world.AddLight(Point(new Double3(3, 0, 0), 5, 1, new Float3(1, 1, 1)));
        LightDesc near = Point(_origin + new Double3(0.3, -0.2, 8.5), 2, 0.02f, new Float3(1, 0.8f, 0.6f));
        _world.AddLight(near);

        AssertMatches(p => Reference.Local(Relative(near), p, s_normal, albedo, 0, 0.6f), Frame());
    }

    [Fact]
    public void ManyStaticLightsAcrossRegionCorners_FarAway_EveryOneIsFound()
    {
        Vector3 albedo = new(0.5f, 0.5f, 0.5f);
        _origin = new Double3(StaticRegions<LightRecord>.RegionSize * 2_000_000, StaticRegions<LightRecord>.RegionSize * -3_000, 0) - new Double3(0, 0, QuadDepth);
        AddQuad(Surface(new Float3(albedo.X, albedo.Y, albedo.Z), 0.7f));
        Random random = new(11);
        List<LightDesc> lights = new();
        for (int i = 0; i < 600; i++)
        {
            LightDesc light = Point(_origin + new Double3(random.NextDouble() * 9 - 4.5, random.NextDouble() * 9 - 4.5, 9 + random.NextDouble() * 0.9), 0.3f + random.NextSingle() * 1.2f,
                0.0005f + random.NextSingle() * 0.002f, new Float3(random.NextSingle(), random.NextSingle(), random.NextSingle()), isStatic: i < 500);
            lights.Add(light);
            _world.AddLight(light);
        }

        AssertMatches(p =>
        {
            Vector3 sum = Vector3.Zero;
            foreach (LightDesc light in lights)
                sum += Reference.Local(Relative(light), p, s_normal, albedo, 0, 0.7f);
            return sum;
        }, Frame());
    }

    [Fact]
    public void StaticLights_MovingBetweenRegions_EmptyingAndRefillingThem_LightCorrectly()
    {
        Vector3 albedo = new(0.6f, 0.6f, 0.6f);
        double region = StaticRegions<LightRecord>.RegionSize;
        AddQuad(Surface(new Float3(albedo.X, albedo.Y, albedo.Z), 0.5f));
        LightDesc near = Point(new Double3(0.5, 0.5, 8.5), 5, 0.03f, new Float3(1, 0.9f, 0.8f));
        LightDesc far = Point(new Double3(region * 3.5, 0, 0), 5, 1, new Float3(1, 1, 1));
        LightHandle moving = _world.AddLight(far);
        LightHandle other = _world.AddLight(Point(new Double3(-region * 2.5, 0, 0), 5, 1, new Float3(1, 1, 1)));
        AssertMatches(_ => Vector3.Zero, Frame());

        _world.SetLight(moving, near);
        AssertMatches(p => Reference.Local(near, p, s_normal, albedo, 0, 0.5f), Frame());

        _world.RemoveLight(other);
        LightDesc second = Point(new Double3(-0.5, -1, 9), 4, 0.02f, new Float3(0.3f, 0.6f, 1));
        _world.RemoveLight(moving);
        _world.AddLight(second);
        AssertMatches(p => Reference.Local(second, p, s_normal, albedo, 0, 0.5f), Frame());

        LightHandle back = _world.AddLight(far);
        AssertMatches(p => Reference.Local(second, p, s_normal, albedo, 0, 0.5f), Frame());
        _world.SetLight(back, near);
        AssertMatches(p => Reference.Local(second, p, s_normal, albedo, 0, 0.5f) + Reference.Local(near, p, s_normal, albedo, 0, 0.5f), Frame());
    }

    [Fact]
    public void AViewSeeingThousandsOfRegions_StillFindsTheLightsNearIt()
    {
        AddQuad(Surface(new Float3(0.7f, 0.7f, 0.7f), 0.5f));
        _world.AddLight(Point(new Double3(0.5, -0.5, 8), 6, 0.05f, new Float3(1, 1, 1)));
        _world.AddLight(Point(new Double3(-1, 1, 9), 3, 0.02f, new Float3(1, 0.5f, 0.2f), isStatic: false));

        Float4[] Perspective()
        {
            ViewDesc view = new() { World = _world, ViewId = 9, Target = _framebuffer };
            view.Layers[0] = new ViewLayer { View = Float4x4.Identity, Projection = s_perspective, Position = _origin };
            _renderer.SubmitFrame(new FrameDesc { Views = new[] { view } });
            _device.WaitForIdle();
            Assert.Empty(_errors);
            return ReadLayer(_target, 0);
        }

        Float4[] alone = Perspective();
        double region = StaticRegions<LightRecord>.RegionSize;
        for (int x = -19; x <= 19; x++)
        {
            for (int y = -19; y <= 19; y++)
                _world.AddLight(Point(new Double3(x * region, y * region, 20 * region), 100, 1, new Float3(1, 1, 1)));
        }
        Float4[] crowded = Perspective();

        Assert.True(alone.Any(p => p.X > 0.01f));
        for (int i = 0; i < alone.Length; i++)
            Assert.True(MathF.Abs(alone[i].X - crowded[i].X) <= 1e-5f + 1e-4f * alone[i].X, $"pixel {i}: {alone[i].X} alone, {crowded[i].X} among 1521 regions");
    }

    [Fact]
    public void ManyLights_EveryOneReachingAPixelIsFound()
    {
        Vector3 albedo = new(0.5f, 0.5f, 0.5f);
        AddQuad(Surface(new Float3(albedo.X, albedo.Y, albedo.Z), 0.7f));
        Random random = new(4);
        List<LightDesc> lights = new();
        for (int i = 0; i < 3500; i++)
        {
            LightDesc light = Point(new Double3(random.NextDouble() * 9 - 4.5, random.NextDouble() * 9 - 4.5, 9 + random.NextDouble() * 0.9), 0.3f + random.NextSingle() * 1.2f,
                0.0005f + random.NextSingle() * 0.002f, new Float3(random.NextSingle(), random.NextSingle(), random.NextSingle()), isStatic: i < 3000);
            lights.Add(light);
            _world.AddLight(light);
        }

        AssertMatches(p =>
        {
            Vector3 sum = Vector3.Zero;
            foreach (LightDesc light in lights)
                sum += Reference.Local(light, p, s_normal, albedo, 0, 0.7f);
            return sum;
        }, Frame());
    }

    [Fact]
    public void Lights_OnlyLightTheirLayers()
    {
        AddQuad(Surface(new Float3(1, 1, 1)), layer: 1);
        LightDesc light = Point(new Double3(0, 0, 8), 6, 0.05f, new Float3(1, 1, 1));
        light.LayerMask = 2;
        _world.AddLight(light);
        light.LayerMask = 3;
        light.Static = false;
        light.Color = new Float3(0, 1, 0);
        _world.AddLight(light);

        AssertMatches(p => Reference.Local(light, p, s_normal, Vector3.One, 0, 0.5f), Frame());
    }

    [Fact]
    public void SpotLights_LightOnlyInsideTheirCone()
    {
        Vector3 albedo = new(0.9f, 0.9f, 0.9f);
        AddQuad(Surface(new Float3(albedo.X, albedo.Y, albedo.Z), 0.3f, 0.5f));
        LightDesc spot = new()
        {
            Type = LightType.Spot,
            Position = new Double3(0.5, 0, 5),
            Direction = new Float3(0, 0, 1),
            Range = 8,
            Intensity = 0.2f,
            Color = new Float3(1, 1, 1),
            InnerAngle = 10,
            OuterAngle = 20,
            Static = true,
        };
        _world.AddLight(spot);

        Float4[] pixels = Frame();
        AssertMatches(p => Reference.Local(spot, p, s_normal, albedo, 0.5f, 0.3f), pixels);
        Assert.Equal(0f, pixels[0].X);
        Assert.True(pixels[8 * Size + 9].X > 0.01f);
    }

    [Fact]
    public void DirectionalLights_MatchProwlsLighting()
    {
        Vector3 albedo = new(0.6f, 0.6f, 0.6f);
        AddQuad(Surface(new Float3(albedo.X, albedo.Y, albedo.Z), 0.4f));
        LightDesc sun = new() { Type = LightType.Directional, Direction = new Float3(0.3f, -0.2f, 1), Color = new Float3(1, 0.9f, 0.8f), Intensity = 0.05f };
        LightDesc fill = new() { Type = LightType.Directional, Direction = new Float3(-0.5f, 0.5f, 1), Color = new Float3(0.2f, 0.3f, 1), Intensity = 0.02f };
        _world.AddLight(sun);
        _world.AddLight(fill);

        AssertMatches(p => Reference.Directional(sun, p, s_normal, albedo, 0, 0.4f) + Reference.Directional(fill, p, s_normal, albedo, 0, 0.4f), Frame());
    }

    // Brute force cosine weighted solid angle of a shape, sampled over its area: sum of cos at the point times cos at the light over d^2
    private static float Integrate(Func<float, float, Vector3> point, Vector3 lightNormal, float area, Vector3 n, int steps, bool twoSided = false)
    {
        double sum = 0;
        for (int i = 0; i < steps; i++)
        {
            for (int j = 0; j < steps; j++)
            {
                Vector3 q = point((i + 0.5f) / steps, (j + 0.5f) / steps);
                float d2 = q.LengthSquared();
                Vector3 l = q / MathF.Sqrt(d2);
                float atLight = twoSided ? 1 : MathF.Max(Vector3.Dot(-l, lightNormal), 0);
                sum += MathF.Max(Vector3.Dot(n, l), 0) * atLight / d2;
            }
        }
        return (float)(sum * area / (steps * steps));
    }

    [Fact]
    public void AreaIrradiance_MatchesIntegratingTheShape_AcrossTheHorizon()
    {
        Random random = new(5);
        for (int k = 0; k < 40; k++)
        {
            Vector3 n = Vector3.Normalize(new Vector3(random.NextSingle() - 0.5f, random.NextSingle() - 0.5f, random.NextSingle() - 0.5f));
            Vector3 center = new(random.NextSingle() * 4 - 2, random.NextSingle() * 4 - 2, random.NextSingle() * 2 + 0.6f);
            float w = 0.5f + random.NextSingle() * 1.5f, h = 0.5f + random.NextSingle() * 1.5f;
            Vector3 face = new(0, 0, -1), right = new(1, 0, 0), up = new(0, 1, 0);

            Vector3[] quad = [center - right * w / 2 - up * h / 2, center + right * w / 2 - up * h / 2, center + right * w / 2 + up * h / 2, center - right * w / 2 + up * h / 2];
            float want = Integrate((u, v) => center + right * (u - 0.5f) * w + up * (v - 0.5f) * h, face, w * h, n, 400);
            Assert.True(MathF.Abs(Reference.QuadIrradiance(quad, n) - want) <= 1e-3f + 0.01f * want, $"rect {k}: {Reference.QuadIrradiance(quad, n)} against {want}");

            // A disk facing the point is a sphere cap as seen from it
            float radius = 0.3f + random.NextSingle();
            Vector3 toward = Vector3.Normalize(-center);
            Vector3 dRight = Vector3.Normalize(Vector3.Cross(toward, new Vector3(0.3f, 1, 0.1f))), dUp = Vector3.Cross(dRight, toward);
            float disk = Integrate((u, v) =>
            {
                float rr = radius * MathF.Sqrt(u), a = 2 * MathF.PI * v;
                return center + dRight * (rr * MathF.Cos(a)) + dUp * (rr * MathF.Sin(a));
            }, toward, MathF.PI * radius * radius, n, 400);
            float d2 = center.LengthSquared();
            float formula = Reference.SphereIrradiance(Vector3.Dot(n, Vector3.Normalize(center)), radius * radius / (radius * radius + d2));
            Assert.True(MathF.Abs(formula - disk) <= 1e-3f + 0.01f * disk, $"disk {k}: {formula} against {disk}");

            // A line of unit intensity spread along it, against summing points along it
            Vector3 a = center - right * w, b = center + up * h;
            double line = 0;
            for (int i = 0; i < 20000; i++)
            {
                Vector3 q = Vector3.Lerp(a, b, (i + 0.5f) / 20000);
                float q2 = q.LengthSquared();
                line += MathF.Max(Vector3.Dot(n, q), 0) / (q2 * MathF.Sqrt(q2));
            }
            line /= 20000;
            Assert.True(MathF.Abs(Reference.LineIrradiance(a, b, n) - (float)line) <= 1e-3f + 0.01f * (float)line, $"line {k}: {Reference.LineIrradiance(a, b, n)} against {line}");
        }
    }

    [Fact]
    public void LineIrradiance_StaysExactOnItsOwnAxis()
    {
        // Along the line's axis only its end on the normal's side lights: the integral of 1 / s^2 from 1 to 3, over its length
        Vector3 n = new(0, 0, 1);
        Assert.Equal(1f / 3, Reference.LineIrradiance(new Vector3(0, 0, 1), new Vector3(0, 0, 3), n), 1e-4f);
        Assert.Equal(1f / 3, Reference.LineIrradiance(new Vector3(1e-4f, 0, 1), new Vector3(1e-4f, 0, 3), n), 1e-3f);
    }

    [Fact]
    public void IesProfiles_TurnAroundTheirDirectionFromUp()
    {
        Vector3 albedo = new(0.8f, 0.8f, 0.8f);
        AddQuad(Surface(new Float3(albedo.X, albedo.Y, albedo.Z), 0.5f));
        LightDesc spot = new()
        {
            Type = LightType.Spot,
            Position = new Double3(0, 0, 6),
            Direction = new Float3(0, 0, 1),
            Up = new Float3(0, 1, 0),
            Range = 10,
            Intensity = 0.1f,
            InnerAngle = 40,
            OuterAngle = 50,
            Static = true,
        };
        LightHandle handle = _world.AddLight(spot);
        Float4[] plain = Frame();

        // V's first half covers from up round to down through right, the second back through left: full, then dark
        byte[] texels = [.. Enumerable.Repeat((byte)255, 8), .. Enumerable.Repeat((byte)0, 8)];
        Texture profile = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(2, 2, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        _device.UpdateTexture<byte>(profile, texels, new TextureRegion(0, 0, 0, 2, 2, 1, 0, 0));
        _textures.Add(profile);
        spot.IesProfile = profile;
        spot.CookieSampler = SamplerDescription.Point;
        _world.SetLight(handle, spot);
        Float4[] profiled = Frame();
        for (int i = 0; i < plain.Length; i++)
        {
            Vector3 p = PixelPosition(i % Size, i / Size);
            if (MathF.Abs(p.X) < 0.4f)
                continue;
            Assert.True(MathF.Abs(profiled[i].X - (p.X > 0 ? plain[i].X : 0)) <= 1e-4f + 0.01f * plain[i].X, $"pixel {i}: {profiled[i].X} against {plain[i].X} at x {p.X}");
        }
    }

    private LightDesc Shaped(LightType type, Float3 color, float intensity) => new()
    {
        Type = type,
        Position = _origin + new Double3(0.6, -0.4, 8.5),
        Direction = new Float3(0.2f, 0.1f, 1),
        Up = new Float3(0, 1, 0),
        Range = 9,
        Intensity = intensity,
        Color = color,
        InnerAngle = 25,
        OuterAngle = 40,
        Static = true,
    };

    [Theory]
    [InlineData(LightType.Point, 0.6f, 0f, 0f)]
    [InlineData(LightType.Spot, 0.4f, 0f, 0f)]
    [InlineData(LightType.Tube, 0.15f, 2.5f, 0f)]
    [InlineData(LightType.Rect, 0f, 2f, 1f)]
    [InlineData(LightType.Disk, 0f, 0.8f, 0f)]
    public void AreaLights_LightByTheirShape(LightType type, float sourceRadius, float sizeX, float sizeY)
    {
        Vector3 albedo = new(0.7f, 0.6f, 0.5f);
        AddQuad(Surface(new Float3(albedo.X, albedo.Y, albedo.Z), 0.35f, 0.2f));
        LightDesc light = Shaped(type, new Float3(1, 0.85f, 0.7f), 0.08f);
        light.SourceRadius = sourceRadius;
        light.Size = new Float2(sizeX, sizeY);
        _world.AddLight(light);

        Float4[] pixels = Frame();
        Assert.Contains(pixels, p => p.X > 0.01f);
        AssertMatches(p => Reference.Area(Relative(light), p, s_normal, albedo, 0.2f, 0.35f), pixels);
    }

    [Fact]
    public void AreaSpecular_StaysCloseToIntegratingTheShape()
    {
        Vector3 n = new(0, 0, -1);
        List<string> report = new();
        float worst = 0;
        foreach (float roughness in new[] { 0.15f, 0.3f, 0.6f })
        {
            foreach ((float tilt, float distance) in new[] { (0f, 3f), (0.4f, 3f), (0.8f, 3f), (0f, 1.5f), (0.6f, 1.5f) })
            {
                // The light sits along the mirror direction of a view tilted off the normal, so the highlight is on it
                Vector3 v = Vector3.Normalize(new Vector3(tilt, 0.1f, -1));
                Vector3 r = Vector3.Reflect(-v, n);
                Vector3 center = r * distance;
                Vector3 axis = Vector3.Normalize(-center);
                Vector3 up = Vector3.Normalize(Vector3.Cross(axis, Vector3.UnitX));
                foreach ((LightType type, float radius, Vector2 size) in new[] { (LightType.Point, 0.5f, Vector2.Zero), (LightType.Rect, 0f, new Vector2(1.2f, 0.6f)), (LightType.Disk, 0f, new Vector2(0.5f, 0)) })
                {
                    (float truth, float estimate) = Reference.Specular(type, center, axis, up, radius, size, v, n, roughness);
                    float error = MathF.Abs(estimate / truth - 1);
                    worst = MathF.Max(worst, error);
                    report.Add($"{type} roughness {roughness} tilt {tilt} distance {distance}: {estimate / truth:F3}");
                }
            }
        }
        Assert.True(worst < 0.15f, string.Join(Environment.NewLine, report));
    }

    [Fact]
    public void SmallAreaLights_LightLikePoints_AndFarOnesLikeThem()
    {
        Vector3 albedo = new(0.7f, 0.7f, 0.7f);
        AddQuad(Surface(new Float3(albedo.X, albedo.Y, albedo.Z), 0.5f));
        LightDesc point = Point(new Double3(0, 0, 4), 30, 0.2f, new Float3(1, 1, 1));
        LightHandle handle = _world.AddLight(point);
        Float4[] want = Frame();

        // Facing the surface a rect and a disk emit like a point there, a sphere and a tube of no length are one
        foreach ((LightType type, float radius, Float2 size, float tolerance) in new[]
        {
            (LightType.Point, 0.01f, new Float2(0, 0), 0.01f),
            (LightType.Tube, 0.01f, new Float2(0.001f, 0), 0.01f),
            (LightType.Rect, 0f, new Float2(0.05f, 0.05f), 0.02f),
            (LightType.Disk, 0f, new Float2(0.03f, 0), 0.02f),
        })
        {
            LightDesc shaped = point;
            shaped.Type = type;
            shaped.SourceRadius = radius;
            shaped.Size = size;
            shaped.Direction = new Float3(0, 0, 1);
            _world.SetLight(handle, shaped);
            Float4[] got = Frame();
            for (int i = 0; i < want.Length; i++)
            {
                // A disk or rect's emission falls with the cosine away from its axis, a point's does not
                float cos = type is LightType.Rect or LightType.Disk ? 6 / Vector3.Distance(PixelPosition(i % Size, i / Size), new Vector3(0, 0, 4)) : 1;
                Assert.True(MathF.Abs(got[i].X - want[i].X * cos) <= 2e-4f + tolerance * want[i].X, $"{type} pixel {i}: {got[i].X} against {want[i].X * cos}");
            }
        }
    }

    [Fact]
    public void TheSunsDisk_WidensItsHighlight_KeepingItsDiffuse()
    {
        Vector3 albedo = new(0.6f, 0.6f, 0.6f);
        AddQuad(Surface(new Float3(albedo.X, albedo.Y, albedo.Z), 0.1f));
        LightDesc sun = new() { Type = LightType.Directional, Direction = new Float3(0.1f, -0.05f, 1), Color = new Float3(1, 0.95f, 0.9f), Intensity = 0.05f, AngularRadius = 3 };
        _world.AddLight(sun);
        AssertMatches(p => Reference.Directional(sun, p, s_normal, albedo, 0, 0.1f), Frame());
    }

    [Fact]
    public void ManyDirectionalLights_AllLight()
    {
        Vector3 albedo = new(0.5f, 0.5f, 0.5f);
        AddQuad(Surface(new Float3(albedo.X, albedo.Y, albedo.Z), 0.6f));
        List<LightDesc> lights = new();
        for (int i = 0; i < 12; i++)
        {
            float a = i * 0.5f;
            LightDesc light = new() { Type = LightType.Directional, Direction = new Float3(0.3f * MathF.Cos(a), 0.3f * MathF.Sin(a), 1), Intensity = 0.004f + 0.001f * i };
            lights.Add(light);
            _world.AddLight(light);
        }
        AssertMatches(p => lights.Aggregate(Vector3.Zero, (sum, l) => sum + Reference.Directional(l, p, s_normal, albedo, 0, 0.6f)), Frame());
    }

    [Fact]
    public void SpotCookies_AndProfiles_ShapeTheirLight()
    {
        Vector3 albedo = new(0.8f, 0.8f, 0.8f);
        AddQuad(Surface(new Float3(albedo.X, albedo.Y, albedo.Z), 0.5f));
        LightDesc spot = new()
        {
            Type = LightType.Spot,
            Position = new Double3(0, 0, 6),
            Direction = new Float3(0, 0, 1),
            Up = new Float3(0, 1, 0),
            Range = 10,
            Intensity = 0.1f,
            InnerAngle = 40,
            OuterAngle = 50,
            Static = true,
        };
        LightHandle handle = _world.AddLight(spot);
        Float4[] plain = Frame();

        // A cookie lit only on its right half, and a profile at half strength everywhere
        Texture cookie = Lightmap(2, PixelFormat.R8_G8_B8_A8_UNorm, new byte[] { 0, 0, 0, 255, 255, 255, 255, 255, 0, 0, 0, 255, 255, 255, 255, 255 });
        Texture profile = Lightmap(2, PixelFormat.R8_G8_B8_A8_UNorm, Enumerable.Repeat((byte)128, 16).ToArray());
        spot.Cookie = cookie;
        spot.CookieSampler = SamplerDescription.Point;
        _world.SetLight(handle, spot);
        Float4[] cookied = Frame();
        for (int i = 0; i < plain.Length; i++)
        {
            Vector3 p = PixelPosition(i % Size, i / Size);
            if (MathF.Abs(p.X) < 0.4f)
                continue;
            Assert.True(MathF.Abs(cookied[i].X - (p.X > 0 ? plain[i].X : 0)) <= 1e-4f + 0.01f * plain[i].X, $"cookie pixel {i}: {cookied[i].X} against {plain[i].X} at x {p.X}");
        }

        spot.Cookie = null;
        spot.IesProfile = profile;
        _world.SetLight(handle, spot);
        Float4[] profiled = Frame();
        for (int i = 0; i < plain.Length; i++)
            Assert.True(MathF.Abs(profiled[i].X - plain[i].X * 128 / 255f) <= 1e-4f + 0.01f * plain[i].X, $"profile pixel {i}: {profiled[i].X} against {plain[i].X}");
    }

    // Shades every light with its own BRDF: its radiance times what reaches the surface diffusely, plus the light's host data
    private const string CustomBrdfSource = """
        Shader "Test/CustomBRDF"
        {
            Pass
            {
                Tags { "LightMode" = "Forward" }
                Cull Off
                GLSLPROGRAM
                Vertex
                {
                    #include "Graphite/Vertex.glsl"
                    layout(location = 0) out vec3 vWorld;
                    void main()
                    {
                        vWorld = Graphite_ObjectToWorld(vertexPosition);
                        gl_Position = Graphite_RelativeToClip(vWorld);
                    }
                }
                Fragment
                {
                    #define GRAPHITE_CUSTOM_BRDF
                    #include "Graphite/Lighting.glsl"
                    #include "Graphite/Passes.glsl"
                    layout(location = 0) in vec3 vWorld;
                    layout(location = 0) out vec4 outColor;

                    vec3 Graphite_BRDF(Graphite_Surface s, Graphite_LightContribution c)
                    {
                        return vec3(c.diffuse, 0.0, 0.0) + vec3(0.0, 0.25, 0.0);
                    }

                    void main()
                    {
                        Graphite_Surface s = Graphite_MakeSurface(vWorld, vec3(0.0, 0.0, -1.0), vec3(0.0, 0.0, -1.0), GetWorldViewDir(vWorld), vec3(1.0), 0.0, 0.5, 1.0);
                        outColor = vec4(Graphite_ShadeLights(s), 1.0);
                        Graphite_WriteGBuffer(vec3(0.0, 0.0, -1.0), 1.0, 0.0);
                    }
                }
                ENDGLSL
            }
        }
        """;

    [Fact]
    public void ACustomBRDF_ShadesEveryLight()
    {
        AddQuad(_renderer.CreateMaterial(_renderer.LoadShader(ShaderParser.Parse(CustomBrdfSource))));
        LightDesc point = Point(new Double3(0.5, 0, 7), 10, 0.1f, new Float3(1, 1, 1));
        _world.AddLight(point);
        _world.AddLight(new LightDesc { Type = LightType.Directional, Direction = new Float3(0, 0, 1), Intensity = 0.1f });

        // The directional light's N.L is 1 everywhere, the point's N.L over distance squared within its window
        AssertMatches(p =>
        {
            Vector3 toLight = new Vector3(0.5f, 0, 7) - p;
            float d2 = toLight.LengthSquared();
            float factor = d2 / 100;
            float window = Math.Clamp(1 - factor * factor, 0, 1);
            return new Vector3(1 + MathF.Max(-toLight.Z / MathF.Sqrt(d2), 0) * window * window / MathF.Max(d2, 0.01f), 0.5f, 0);
        }, Frame());
    }

    [Fact]
    public void DirectionalLights_OnlyLightTheirLayers()
    {
        AddQuad(Surface(new Float3(0.6f, 0.6f, 0.6f), 0.4f), layer: 1);
        LightDesc other = new() { Type = LightType.Directional, Direction = new Float3(0, 0, 1), Intensity = 0.5f, LayerMask = 2 };
        LightDesc sun = new() { Type = LightType.Directional, Direction = new Float3(0.3f, -0.2f, 1), Color = new Float3(1, 0.9f, 0.8f), Intensity = 0.05f, LayerMask = 1 };
        _world.AddLight(other);
        _world.AddLight(sun);

        AssertMatches(p => Reference.Directional(sun, p, s_normal, new Vector3(0.6f), 0, 0.4f), Frame());
    }

    [Fact]
    public void EditingTheWorldsEnvironment_AppliesAtTheNextFrame()
    {
        AddQuad(Surface(new Float3(1, 1, 1)));
        Frame();

        _world.Environment.AmbientColor = new Float4(0.25f, 0.5f, 0.75f, 1);
        AssertMatches(_ => new Vector3(0.25f, 0.5f, 0.75f), Frame());
    }

    [Fact]
    public void Ambient_UniformAndHemisphere()
    {
        AddQuad(Surface(new Float3(0.5f, 1, 0.25f)));
        _world.Environment = new EnvironmentDesc { Ambient = AmbientMode.Uniform, AmbientColor = new Float4(0.2f, 0.4f, 0.8f, 1), Fog = default };
        AssertMatches(_ => new Vector3(0.1f, 0.4f, 0.2f), Frame());

        _world.Environment = new EnvironmentDesc { Ambient = AmbientMode.Hemisphere, AmbientSky = new Float4(1, 0, 0, 1), AmbientGround = new Float4(0, 0, 1, 1), Fog = default };
        AssertMatches(_ => new Vector3(0.25f, 0, 0.125f), Frame());
    }

    [Fact]
    public void LinearFog_FadesToItsColourWithDistance()
    {
        AddQuad(Surface(new Float3(1, 1, 1)));
        _world.Environment = new EnvironmentDesc
        {
            AmbientColor = new Float4(0.5f, 0.5f, 0.5f, 1),
            Fog = new FogDesc { Mode = FogMode.Linear, Color = new Float4(1, 0, 0, 1), Start = 0, End = 20 },
        };

        AssertMatches(p =>
        {
            float visible = Math.Clamp(1 - p.Length() / 20, 0, 1);
            return Vector3.Lerp(new Vector3(1, 0, 0), new Vector3(0.5f), visible);
        }, Frame());
    }

    private DecalDesc Decal(Double3 center, Double3 size, Float4 color, int priority = 0, bool isStatic = false, uint layer = ~0u) => new()
    {
        Transform = Double4x4.CreateTranslation(_origin + center) * Double4x4.CreateScale(size.X, size.Y, -size.Z),
        Color = color,
        Priority = priority,
        Static = isStatic,
        LayerMask = layer,
    };

    private void LitByAmbient() => _world.Environment = new EnvironmentDesc { AmbientColor = new Float4(1, 1, 1, 1), Fog = default };

    [Fact]
    public void Decals_ColourTheSurfaceInsideTheirBox()
    {
        LitByAmbient();
        AddQuad(Surface(new Float3(1, 1, 1)));
        _world.AddDecal(Decal(new Double3(-2, 0, QuadDepth), new Double3(4, 8, 2), new Float4(1, 0, 0, 1)));

        AssertMatches(p => p.X < 0 ? new Vector3(1, 0, 0) : Vector3.One, Frame());
    }

    [Fact]
    public void OverlappingDecals_ApplyLowestPriorityFirst()
    {
        LitByAmbient();
        AddQuad(Surface(new Float3(1, 1, 1)));
        _world.AddDecal(Decal(new Double3(0, 0, QuadDepth), new Double3(8, 8, 2), new Float4(0, 0, 1, 1), priority: 5));
        _world.AddDecal(Decal(new Double3(0, 0, QuadDepth), new Double3(8, 8, 2), new Float4(1, 0, 0, 1), priority: 1, isStatic: true));
        _world.AddDecal(Decal(new Double3(0, 0, QuadDepth), new Double3(8, 8, 2), new Float4(0, 1, 0, 0.5f), priority: 9));

        AssertMatches(_ => new Vector3(0, 0.5f, 0.5f), Frame());
    }

    [Fact]
    public void Decals_LandOnlyOnTheirLayers_AndStaticOnesLikeMovingOnes()
    {
        LitByAmbient();
        AddQuad(Surface(new Float3(1, 1, 1)), layer: 1);
        _world.AddDecal(Decal(new Double3(0, 0, QuadDepth), new Double3(8, 8, 2), new Float4(1, 0, 0, 1), layer: 2));
        _world.AddDecal(Decal(new Double3(2, 0, QuadDepth), new Double3(4, 8, 2), new Float4(0, 1, 0, 1), isStatic: true, layer: 1));

        AssertMatches(p => p.X > 0 ? new Vector3(0, 1, 0) : Vector3.One, Frame());
    }

    [Fact]
    public void Decals_SkipSurfacesFacingAway()
    {
        LitByAmbient();
        AddQuad(Surface(new Float3(1, 1, 1)));
        DecalDesc decal = Decal(new Double3(0, 0, QuadDepth), new Double3(8, 8, 2), new Float4(1, 0, 0, 1));
        decal.Transform = Double4x4.CreateTranslation(_origin + new Double3(0, 0, QuadDepth)) * Double4x4.CreateScale(8, 8, 2);
        _world.AddDecal(decal);

        AssertMatches(_ => Vector3.One, Frame());
    }

    // Colours the surface with its decals' host data times their first extra texture, through the decal hook
    private const string DecalExtraSource = """
        Shader "Test/DecalExtra"
        {
            Pass
            {
                Tags { "LightMode" = "Forward" }
                Cull Off
                GLSLPROGRAM
                Vertex
                {
                    #include "Graphite/Vertex.glsl"
                    layout(location = 0) out vec3 vWorld;
                    void main()
                    {
                        vWorld = Graphite_ObjectToWorld(vertexPosition);
                        gl_Position = Graphite_RelativeToClip(vWorld);
                    }
                }
                Fragment
                {
                    #define GRAPHITE_DECAL_EXTRA
                    #include "Graphite/Decals.glsl"
                    #include "Graphite/Passes.glsl"
                    layout(location = 0) in vec3 vWorld;
                    layout(location = 0) out vec4 outColor;
                    vec3 extraColor = vec3(0.0);

                    void Graphite_ApplyDecalExtra(Graphite_DecalRecord d, vec2 uv, float coverage)
                    {
                        extraColor = mix(extraColor, d.hostData.rgb * GRAPHITE_DECAL_TEXTURE(d, d.extra.x, uv).r, coverage);
                    }

                    void main()
                    {
                        vec3 n = vec3(0.0, 0.0, -1.0), albedo = vec3(1.0);
                        float roughness = 0.5, metallic = 0.0, ao = 1.0;
                        Graphite_ApplyDecals(vWorld, n, albedo, n, roughness, metallic, ao);
                        outColor = vec4(extraColor, 1.0);
                        Graphite_WriteGBuffer(n, roughness, metallic);
                    }
                }
                ENDGLSL
            }
        }
        """;

    [Fact]
    public void DecalsCarryHostDataAndExtraTextures_ToCustomSurfaceShaders()
    {
        AddQuad(_renderer.CreateMaterial(_renderer.LoadShader(ShaderParser.Parse(DecalExtraSource))));
        Texture half = Lightmap(2, PixelFormat.R8_G8_B8_A8_UNorm, Enumerable.Repeat((byte)128, 16).ToArray());
        DecalDesc decal = Decal(new Double3(-2, 0, QuadDepth), new Double3(4, 8, 2), new Float4(1, 1, 1, 1));
        decal.HostData = new Float4(1, 0.5f, 0.25f, 0);
        decal.Extra0 = half;
        _world.AddDecal(decal);

        AssertMatches(p => p.X < 0 ? new Vector3(1, 0.5f, 0.25f) * (128 / 255f) : Vector3.Zero, Frame());
    }

    // Adds up the colours of a host set's volumes holding each point
    private const string VolumesSource = """
        Shader "Test/Volumes"
        {
            Properties
            {
                _Set("Set", Float) = 0
                _Stride("Stride", Float) = 0
            }

            Pass
            {
                Tags { "LightMode" = "Forward" }
                Cull Off
                GLSLPROGRAM
                Vertex
                {
                    #include "Graphite/Vertex.glsl"
                    layout(location = 0) out vec3 vWorld;
                    void main()
                    {
                        vWorld = Graphite_ObjectToWorld(vertexPosition);
                        gl_Position = Graphite_RelativeToClip(vWorld);
                    }
                }
                Fragment
                {
                    #include "Graphite/Trees.glsl"
                    #include "Graphite/Passes.glsl"
                    uniform float _Set;
                    uniform float _Stride;
                    layout(location = 0) in vec3 vWorld;
                    layout(location = 0) out vec4 outColor;
                    void main()
                    {
                        vec3 sum = vec3(0.0);
                        Graphite_ItemIterator it = Graphite_BeginItems(uint(_Set), uint(_Stride), vWorld);
                        uint array, index;
                        vec3 center;
                        float radius;
                        while (Graphite_NextItem(it, array, index, center, radius))
                            sum += Graphite_SpatialVec4(array, index * uint(_Stride) + 1u).rgb;
                        outColor = vec4(sum, 1.0);
                        Graphite_WriteGBuffer(vec3(0.0, 0.0, -1.0), 1.0, 0.0);
                    }
                }
                ENDGLSL
            }
        }
        """;

    [StructLayout(LayoutKind.Sequential)]
    private struct Volume
    {
        public Float4 Color;
    }

    [Fact]
    public void HostSpatialSets_FindTheirItemsHoldingEachPoint_StaticAndMoving_FarFromTheOrigin()
    {
        _origin = new Double3(5_000_000.25, -30, -2_000_000);
        SpatialSet<Volume> volumes = _world.CreateSpatialSet<Volume>("Volumes");
        Material material = _renderer.CreateMaterial(_renderer.LoadShader(ShaderParser.Parse(VolumesSource)));
        material.SetFloat("_Set", volumes.Index);
        material.SetFloat("_Stride", volumes.Stride);
        AddQuad(material);

        SpatialHandle red = volumes.Add(_origin + new Double3(-2, 0, QuadDepth), 2.5f, new Volume { Color = new Float4(1, 0, 0, 0) }, isStatic: true);
        SpatialHandle green = volumes.Add(_origin + new Double3(2, 1, QuadDepth), 2f, new Volume { Color = new Float4(0, 1, 0, 0) });

        static Vector3 Expected(Vector3 p, (Vector3 Center, float Radius, Vector3 Color)[] items)
            => items.Where(i => Vector3.Distance(p, i.Center) <= i.Radius).Aggregate(Vector3.Zero, (sum, i) => sum + i.Color);

        (Vector3, float, Vector3)[] both = [(new Vector3(-2, 0, QuadDepth), 2.5f, new Vector3(1, 0, 0)), (new Vector3(2, 1, QuadDepth), 2f, new Vector3(0, 1, 0))];
        Float4[] pixels = Frame();
        Assert.Contains(pixels, p => p.X > 0.5f);
        Assert.Contains(pixels, p => p.Y > 0.5f);
        AssertSpatial(p => Expected(p, both), pixels);

        // Moving the moving one, recolouring the static one in place, then removing it
        volumes.Set(green, _origin + new Double3(-1, -2, QuadDepth), 2f, new Volume { Color = new Float4(0, 1, 0, 0) });
        volumes.Set(red, _origin + new Double3(-2, 0, QuadDepth), 2.5f, new Volume { Color = new Float4(0, 0, 1, 0) }, isStatic: true);
        AssertSpatial(p => Expected(p, [(new Vector3(-2, 0, QuadDepth), 2.5f, new Vector3(0, 0, 1)), (new Vector3(-1, -2, QuadDepth), 2f, new Vector3(0, 1, 0))]), Frame());
        volumes.Remove(red);
        AssertSpatial(p => Expected(p, [(new Vector3(-1, -2, QuadDepth), 2f, new Vector3(0, 1, 0))]), Frame());
    }

    // Exact where a pixel centre is well inside or outside every sphere, skipping those a float's width from an edge
    private static void AssertSpatial(Func<Vector3, Vector3> expected, Float4[] pixels)
    {
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                Vector3 p = PixelPosition(x, y);
                Vector3 want = expected(p);
                Vector3 nearby = expected(p + new Vector3(0.01f, 0.01f, 0));
                if (want != nearby)
                    continue;
                Float4 got = pixels[y * Size + x];
                Assert.True(Vector3.Distance(want, new Vector3(got.X, got.Y, got.Z)) < 1e-4f, $"pixel {x},{y}: want {want}, got {got}");
            }
        }
    }

    // Depth alone
    private const string CasterPass = """
            Pass
            {
                Tags { "LightMode" = "ShadowCaster" }
                Cull Off
                GLSLPROGRAM
                Vertex
                {
                    #include "Graphite/Vertex.glsl"
                    void main() { gl_Position = Graphite_ObjectToClip(vertexPosition); }
                }
                Fragment
                {
                    void main() { }
                }
                ENDGLSL
            }
        """;

    // For shadow casters the views never see
    private const string CasterSource = "Shader \"Test/Caster\"\n{\n" + CasterPass + "\n}";

    // The lit surface casting too, so it can only shadow itself through acne
    private static readonly string LitCasterSource = WithPass(Source.Replace("Test/Lit", "Test/LitCaster"), CasterPass);

    private static string WithPass(string shader, string pass) => shader.Insert(shader.LastIndexOf('}'), pass + "\n");

    // A square of half size half only shadows see, centred at x, y on the plane at depth z
    private RenderableHandle AddCaster(double x, double y, double z, double half = 1, bool isStatic = false)
    {
        Material caster = _renderer.CreateMaterial(_renderer.LoadShader(ShaderParser.Parse(CasterSource)));
        return _world.Add(new RenderableDesc
        {
            Mesh = _quad,
            Transform = Double4x4.CreateTranslation(_origin + new Double3(x, y, z)) * Double4x4.CreateScale(half / 4),
            Flags = RenderableFlags.CastShadows | RenderableFlags.ShadowsOnly | (isStatic ? RenderableFlags.Static : 0),
        }, [caster]);
    }

    // Lit as the reference says, dark where blocked, skipping pixels within margin of the shadow's edge
    private static void AssertShadowed(Func<Vector3, Vector3> lit, Func<Vector3, float> edgeDistance, Float4[] pixels, float margin)
    {
        int checkedLit = 0, checkedDark = 0;
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                Vector3 p = PixelPosition(x, y);
                float edge = edgeDistance(p);
                if (MathF.Abs(edge) < margin)
                    continue;
                Vector3 want = edge < 0 ? Vector3.Zero : lit(p);
                Float4 got = pixels[y * Size + x];
                Assert.True(Vector3.Distance(want, new Vector3(got.X, got.Y, got.Z)) <= 2e-4f + 0.01f * want.Length(), $"pixel {x},{y}: expected {want}, got {got}");
                if (edge < 0)
                    checkedDark++;
                else if (want.Length() > 1e-3f)
                    checkedLit++;
            }
        }
        Assert.True(checkedDark > 4 && checkedLit > 4, $"{checkedDark} shadowed and {checkedLit} lit pixels checked");
    }

    // Signed distance from a square's edge, negative inside
    private static float SquareEdge(Vector3 p, float cx, float cy, float half) => MathF.Max(MathF.Abs(p.X - cx), MathF.Abs(p.Y - cy)) - half;

    [Fact]
    public void SpotLightShadows_FallWhereTheCasterBlocksTheLight()
    {
        Vector3 albedo = new(0.8f, 0.8f, 0.8f);
        AddQuad(Surface(new Float3(albedo.X, albedo.Y, albedo.Z)));
        LightDesc light = new()
        {
            Type = LightType.Spot,
            Position = new Double3(0, 0, 2),
            Direction = new Float3(0, 0, 1),
            Range = 20,
            Intensity = 0.2f,
            InnerAngle = 35,
            OuterAngle = 40,
            Static = true,
            Shadow = new ShadowDesc { Enabled = true },
        };
        _world.AddLight(light);

        // Off centre in both axes, so a flipped map shows: from 2 through 6 to 10 the square doubles
        AddCaster(1, 0.5, 6);
        AssertShadowed(p => Reference.Local(light, p, s_normal, albedo, 0, 0.5f), p => SquareEdge(p, 2, 1, 2), Frame(), 0.6f);
    }

    [Fact]
    public void DirectionalShadows_FallAlongTheLight_FarFromTheOriginAsNearIt()
    {
        Vector3 albedo = new(0.8f, 0.8f, 0.8f);
        Material surface = Surface(new Float3(albedo.X, albedo.Y, albedo.Z));
        RenderableHandle quad = AddQuad(surface);
        LightDesc sun = new()
        {
            Type = LightType.Directional,
            Direction = new Float3(0.5f, 0.25f, 1),
            Intensity = 0.1f,
            Shadow = new ShadowDesc { Enabled = true },
        };
        _world.AddLight(sun);

        // From depth 8 to 10 the light moves a unit in x and half in y
        AddCaster(-1, -1, 8, half: 2);
        Float4[] near = Frame();
        LightDesc normalized = sun with { Direction = Normalize(sun.Direction) };
        AssertShadowed(p => Reference.Directional(normalized, p, s_normal, albedo, 0, 0.5f), p => SquareEdge(p, 0, -0.5f, 2), near, 0.6f);

        _origin = new Double3(10_000_000.25, 0, -3_000_000);
        _world.SetTransform(quad, Double4x4.CreateTranslation(_origin + new Double3(0, 0, QuadDepth)));
        AddCaster(-1, -1, 8, half: 2);

        // The cascades refit on their stagger, every one within four frames of the jump
        for (int frame = 0; frame < 3; frame++)
            Frame();
        Float4[] far = Frame();
        for (int i = 0; i < near.Length; i++)
            Assert.True(MathF.Abs(near[i].X - far[i].X) < 2e-3f + 0.01f * near[i].X, $"pixel {i}: {near[i].X} near, {far[i].X} far");
    }

    private static Float3 Normalize(Float3 v) => v / MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);

    [Fact]
    public void SurfacesCastingOnThemselves_AtAGrazingAngle_ShowNoAcne()
    {
        Vector3 albedo = new(0.8f, 0.8f, 0.8f);
        Material material = _renderer.CreateMaterial(_renderer.LoadShader(ShaderParser.Parse(LitCasterSource)));
        material.SetColor("_Color", new Float4(albedo.X, albedo.Y, albedo.Z, 1));
        AddQuad(material);
        LightDesc sun = new() { Type = LightType.Directional, Direction = Normalize(new Float3(1, 0.2f, 0.3f)), Intensity = 0.1f, Shadow = new ShadowDesc { Enabled = true } };
        LightHandle handle = _world.AddLight(sun);
        AssertMatches(p => Reference.Directional(sun, p, s_normal, albedo, 0, 0.5f), Frame());

        _world.SetLight(handle, sun with { Intensity = 0 });
        LightDesc spot = new()
        {
            Type = LightType.Spot,
            Position = new Double3(-6, 0, 8),
            Direction = Normalize(new Float3(1, 0, 0.45f)),
            Range = 30,
            Intensity = 0.3f,
            InnerAngle = 50,
            OuterAngle = 60,
            Static = true,
            Shadow = new ShadowDesc { Enabled = true },
        };
        _world.AddLight(spot);
        AssertMatches(p => Reference.Local(spot, p, s_normal, albedo, 0, 0.5f), Frame());
    }

    [Fact]
    public void ProbeCaptures_SeeTheSunsShadows()
    {
        // A lit wall behind the camera, which the mirror shows through the probe, the sun shining onto it past a caster
        Room(new Double3(0, 0, 10), 20, _ => new Float3(0, 0, 0));
        Material wall = Surface(new Float3(0.8f, 0.8f, 0.8f));
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(_origin + new Double3(0, 0, -9.9)) * Double4x4.CreateScale(new Double3(-5, 5, -5)), LayerMask = 1 }, [wall]);
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(_origin + new Double3(0, 0, QuadDepth)), LayerMask = 2, Flags = RenderableFlags.ReceiveShadows }, [Reflector()]);
        ReflectionProbeHandle probe = _world.AddReflectionProbe(RoomProbe(new Double3(0, 0, 10), new Float3(40, 40, 40)));
        _world.AddLight(new LightDesc { Type = LightType.Directional, Direction = new Float3(0, 0, -1), Intensity = 0.1f, Shadow = new ShadowDesc { Enabled = true } });
        RenderableHandle caster = AddCaster(0, 0, 0, half: 30);

        Frame();
        Float4[] shadowed = Frame();
        Assert.All(shadowed, p => Assert.True(p.X < 0.01f, $"shadowed reflection {p}"));

        _world.Remove(caster);
        _world.RenderReflectionProbe(probe);
        Frame();
        Float4[] lit = Frame();
        Assert.All(lit, p => Assert.True(p.X > 0.05f, $"lit reflection {p}"));
    }

    private LightDesc ShadowSpot() => new()
    {
        Type = LightType.Spot,
        Position = new Double3(0, 0, 2),
        Direction = new Float3(0, 0, 1),
        Range = 20,
        Intensity = 0.2f,
        InnerAngle = 35,
        OuterAngle = 40,
        Static = true,
        Shadow = new ShadowDesc { Enabled = true },
    };

    private FramePipeline Pipeline => _renderer.Frame;

    [Fact]
    public void CachedShadows_DrawAgainOnlyWhenTheirLightOrACasterNearItChanges()
    {
        Vector3 albedo = new(0.8f, 0.8f, 0.8f);
        AddQuad(Surface(new Float3(albedo.X, albedo.Y, albedo.Z)));
        LightDesc light = ShadowSpot();
        LightHandle handle = _world.AddLight(light);
        RenderableHandle caster = AddCaster(1, 0.5, 6);
        Frame();
        Assert.Equal(1, Pipeline.ShadowViewsDrawn);
        Frame();
        Assert.Equal(0, Pipeline.ShadowViewsDrawn);

        // A caster far outside the light's reach changes nothing, the one in it draws the light again
        AddCaster(200, 0, 6);
        Frame();
        Assert.Equal(0, Pipeline.ShadowViewsDrawn);
        _world.SetTransform(caster, Double4x4.CreateTranslation(new Double3(-1, -0.5, 6)) * Double4x4.CreateScale(0.25));
        Float4[] pixels = Frame();
        Assert.Equal(1, Pipeline.ShadowViewsDrawn);
        AssertShadowed(p => Reference.Local(light, p, s_normal, albedo, 0, 0.5f), p => SquareEdge(p, -2, -1, 2), pixels, 0.6f);

        _world.SetLight(handle, light with { Intensity = 0.3f });
        Frame();
        Assert.Equal(0, Pipeline.ShadowViewsDrawn);
        _world.SetLight(handle, light with { Direction = Normalize(new Float3(0.05f, 0, 1)) });
        Frame();
        Assert.Equal(1, Pipeline.ShadowViewsDrawn);
    }

    [Fact]
    public void SkinnedCasters_DrawTheirLightAgainOnlyWhenTheirBonesChange()
    {
        Vector3 albedo = new(0.8f, 0.8f, 0.8f);
        AddQuad(Surface(new Float3(albedo.X, albedo.Y, albedo.Z)));
        LightDesc light = ShadowSpot();
        _world.AddLight(light);
        RenderableHandle caster = AddCaster(1, 0.5, 6);
        Float3[] positions = [new(-4, -4, 0), new(4, -4, 0), new(4, 4, 0), new(-4, -4, 0), new(4, 4, 0), new(-4, 4, 0)];
        ushort[] bones = new ushort[24], weights = new ushort[24];
        for (int v = 0; v < 6; v++)
            (bones[v * 4], weights[v * 4]) = (1, 65535);
        using GpuMesh skinned = _renderer.CreateMesh(new MeshData
        {
            VertexCount = 6,
            Streams =
            [
                new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(positions.AsSpan()).ToArray() },
                new VertexStream { Semantic = VertexSemantic.BoneIndices, Format = VertexElementFormat.UShort4, Data = MemoryMarshal.AsBytes(bones.AsSpan()).ToArray() },
                new VertexStream { Semantic = VertexSemantic.BoneWeights, Format = VertexElementFormat.UShort4_Norm, Data = MemoryMarshal.AsBytes(weights.AsSpan()).ToArray() },
            ],
            Indices = new uint[] { 0, 1, 2, 3, 4, 5 },
        });
        _world.SetMesh(caster, skinned);
        _world.SetBounds(caster, new AABB(new Float3(-16, -16, -1), new Float3(16, 16, 1)));
        Float4x4 identity = Float4x4.Identity;
        _world.SetBones(caster, [identity]);
        Frame();
        Assert.Equal(1, Pipeline.ShadowViewsDrawn);
        _world.SetBones(caster, [identity]);
        Frame();
        Assert.Equal(0, Pipeline.ShadowViewsDrawn);

        // Eight object units left and four down at a quarter scale puts the caster where the moved caster test does
        _world.SetBones(caster, [new Float4x4(new Float4(1, 0, 0, 0), new Float4(0, 1, 0, 0), new Float4(0, 0, 1, 0), new Float4(-8, -4, 0, 1))]);
        Float4[] pixels = Frame();
        Assert.Equal(1, Pipeline.ShadowViewsDrawn);
        AssertShadowed(p => Reference.Local(light, p, s_normal, albedo, 0, 0.5f), p => SquareEdge(p, -2, -1, 2), pixels, 0.6f);
    }

    [Fact]
    public void CastersTaggedToUpdateEveryFrame_KeepTheirLightDrawing()
    {
        AddQuad(Surface(new Float3(0.8f, 0.8f, 0.8f)));
        _world.AddLight(ShadowSpot());
        string source = CasterSource.Replace("Test/Caster", "Test/AnimatedCaster").Replace("\"LightMode\" = \"ShadowCaster\"", "\"LightMode\" = \"ShadowCaster\" \"ShadowUpdate\" = \"EveryFrame\"");
        Material animated = _renderer.CreateMaterial(_renderer.LoadShader(ShaderParser.Parse(source)));
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(1, 0.5, 6)) * Double4x4.CreateScale(0.25), Flags = RenderableFlags.CastShadows | RenderableFlags.ShadowsOnly }, [animated]);
        Frame();
        Frame();
        Assert.Equal(1, Pipeline.ShadowViewsDrawn);
    }

    [Fact]
    public void StaticCasters_AreCopiedBack_WhileOnlyMovingOnesDrawAgain()
    {
        Vector3 albedo = new(0.8f, 0.8f, 0.8f);
        AddQuad(Surface(new Float3(albedo.X, albedo.Y, albedo.Z)));
        LightDesc light = ShadowSpot();
        _world.AddLight(light);
        AddCaster(1, 0.5, 6, isStatic: true);
        RenderableHandle moving = AddCaster(-1.5, -1.5, 6, half: 0.5);

        Float4[] pixels = Frame();
        Assert.Equal(2, Pipeline.ShadowViewsDrawn);
        Assert.Equal(1, Pipeline.ShadowCopiesMade);
        AssertShadowed(p => Reference.Local(light, p, s_normal, albedo, 0, 0.5f), p => MathF.Min(SquareEdge(p, 2, 1, 2), SquareEdge(p, -3, -3, 1)), pixels, 0.6f);

        // From 2 through 6 to 10 the square doubles, the moving one's shadow goes from -3, -3 to -3, 3
        _world.SetTransform(moving, Double4x4.CreateTranslation(new Double3(-1.5, 1.5, 6)) * Double4x4.CreateScale(0.5 / 4));
        pixels = Frame();
        Assert.Equal(1, Pipeline.ShadowViewsDrawn);
        Assert.Equal(1, Pipeline.ShadowCopiesMade);
        AssertShadowed(p => Reference.Local(light, p, s_normal, albedo, 0, 0.5f), p => MathF.Min(SquareEdge(p, 2, 1, 2), SquareEdge(p, -3, 3, 1)), pixels, 0.6f);

        Frame();
        Assert.Equal(0, Pipeline.ShadowViewsDrawn);
    }

    [Fact]
    public void PointLightShadows_FallFromTheFaceTheyLieIn()
    {
        Vector3 albedo = new(0.8f, 0.8f, 0.8f);
        AddQuad(Surface(new Float3(albedo.X, albedo.Y, albedo.Z)));
        LightDesc light = Point(new Double3(0, 0, 2), 20, 0.2f, new Float3(1, 1, 1)) with { Shadow = new ShadowDesc { Enabled = true } };
        _world.AddLight(light);
        AddCaster(1, 0.5, 6);
        Float4[] pixels = Frame();
        Assert.Equal(6, Pipeline.ShadowViewsDrawn);
        AssertShadowed(p => Reference.Local(light, p, s_normal, albedo, 0, 0.5f), p => SquareEdge(p, 2, 1, 2), pixels, 0.6f);
    }

    // An item drawn with an instance count the GPU holds, here none
    private sealed class IndirectItem(GpuMesh mesh, Material material, DeviceBuffer count) : RenderProvider
    {
        public override void Cull(in ProviderView view, DrawItemSink sink)
            => sink.AddIndirect(mesh, 0, material, new DrawItem { Transform = Double4x4.CreateTranslation(new Double3(0, 0, 30)) }, count, 4);
    }

    [Fact]
    public void IndirectProviderItems_LeaveShadowViewsCastersAlone()
    {
        Vector3 albedo = new(0.8f, 0.8f, 0.8f);
        Material surface = Surface(new Float3(albedo.X, albedo.Y, albedo.Z));
        AddQuad(surface);
        LightDesc light = ShadowSpot();
        _world.AddLight(light);
        AddCaster(1, 0.5, 6);
        using DeviceBuffer count = _device.ResourceFactory.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadWrite));
        _device.UpdateBuffer<uint>(count, 0, [0, 0, 0, 0]);
        _world.Providers.Add(new IndirectItem(_quad, surface, count));
        AssertShadowed(p => Reference.Local(light, p, s_normal, albedo, 0, 0.5f), p => SquareEdge(p, 2, 1, 2), Frame(), 0.6f);
    }

    // A two unit square only shadows see, drawn with a material that has no caster pass of its own
    private RenderableHandle AddSurfaceCaster(Material material, double x, double y, double z)
        => _world.Add(new RenderableDesc
        {
            Mesh = _quad,
            Transform = Double4x4.CreateTranslation(_origin + new Double3(x, y, z)) * Double4x4.CreateScale(0.25),
            Flags = RenderableFlags.CastShadows | RenderableFlags.ShadowsOnly,
        }, [material]);

    [Fact]
    public void OpaqueMaterialsWithoutACasterPass_CastThroughOneDepthProgram()
    {
        Vector3 albedo = new(0.8f, 0.8f, 0.8f);
        Material receiver = Surface(new Float3(albedo.X, albedo.Y, albedo.Z));
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(0, 0, QuadDepth)), Flags = RenderableFlags.ReceiveShadows }, [receiver]);
        LightDesc light = ShadowSpot();
        _world.AddLight(light);

        // Three casters of three materials of one shader, which draw as one call
        AddSurfaceCaster(Surface(new Float3(1, 0, 0)), 1, 0.5, 6);
        AddSurfaceCaster(Surface(new Float3(0, 1, 0)), -1.5, -1.5, 6);
        AddSurfaceCaster(Surface(new Float3(0, 0, 1)), -1.5, 1.5, 6);
        Float4[] pixels = Frame();
        Assert.Equal(1, Pipeline.ShadowCallsDrawn);
        AssertShadowed(p => Reference.Local(light, p, s_normal, albedo, 0, 0.5f),
            p => MathF.Min(SquareEdge(p, 2, 1, 2), MathF.Min(SquareEdge(p, -3, -3, 2), SquareEdge(p, -3, 3, 2))), pixels, 0.6f);
    }

    [Fact]
    public void MaterialsOptingOut_CastNothing()
    {
        Vector3 albedo = new(0.8f, 0.8f, 0.8f);
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(0, 0, QuadDepth)), Flags = RenderableFlags.ReceiveShadows }, [Surface(new Float3(albedo.X, albedo.Y, albedo.Z))]);
        LightDesc light = ShadowSpot();
        _world.AddLight(light);

        Material off = _renderer.CreateMaterial(_renderer.LoadShader(ShaderParser.Parse(Source.Replace("Test/Lit", "Test/NoShadows").Replace("\"LightMode\" = \"Forward\"", "\"LightMode\" = \"Forward\" \"Shadows\" = \"Off\""))));
        Material uncast = Surface(new Float3(1, 1, 1));
        uncast.CastShadows = false;
        AddSurfaceCaster(off, 1, 0.5, 6);
        AddSurfaceCaster(uncast, -1.5, -1.5, 6);
        AssertMatches(p => Reference.Local(light, p, s_normal, albedo, 0, 0.5f), Frame());
    }

    [Fact]
    public void DepthOnlyCasters_KeepTheirMaterialsCulling()
    {
        Vector3 albedo = new(0.8f, 0.8f, 0.8f);
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(0, 0, QuadDepth)), Flags = RenderableFlags.ReceiveShadows }, [Surface(new Float3(albedo.X, albedo.Y, albedo.Z))]);
        _world.AddLight(ShadowSpot());
        Material Culled(string cull)
            => _renderer.CreateMaterial(_renderer.LoadShader(ShaderParser.Parse(Source.Replace("Test/Lit", "Test/Cull" + cull).Replace("Cull Off", "Cull " + cull))));

        // The test quad winds counter clockwise toward the light, its back face to a clockwise front, so front culling casts and
        // back does not, and mirrored it culls the same faces
        bool Casts(Material material)
        {
            RenderableHandle caster = AddSurfaceCaster(material, 0, 0, 6);
            Float4[] pixels = Frame();
            _world.Remove(caster);
            Float4 centre = pixels[Size / 2 * Size + Size / 2];
            return centre.X < 1e-4f;
        }
        Assert.True(Casts(Culled("Off")));
        Assert.False(Casts(Culled("Back")));
        Assert.True(Casts(Culled("Front")));
        Material back = Culled("Back");
        RenderableHandle mirrored = _world.Add(new RenderableDesc
        {
            Mesh = _quad,
            Transform = Double4x4.CreateTranslation(new Double3(0, 0, 6)) * Double4x4.CreateScale(new Double3(-0.25, 0.25, 0.25)),
            Flags = RenderableFlags.CastShadows | RenderableFlags.ShadowsOnly,
        }, [back]);
        Float4 centre = Frame()[Size / 2 * Size + Size / 2];
        Assert.True(centre.X > 1e-3f, $"mirrored back culled caster casts {centre}");
    }

    // A square only shadows see, from a provider
    private sealed class ShadowItem(GpuMesh mesh, Material material) : RenderProvider
    {
        public override void Cull(in ProviderView view, DrawItemSink sink) => sink.Add(mesh, 0, material, new DrawItem
        {
            Transform = Double4x4.CreateTranslation(new Double3(1, 0.5, 6)) * Double4x4.CreateScale(0.25),
            Flags = RenderableFlags.CastShadows | RenderableFlags.ShadowsOnly,
        });
    }

    [Fact]
    public void ProviderItems_CastShadows()
    {
        Vector3 albedo = new(0.8f, 0.8f, 0.8f);
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(0, 0, QuadDepth)), Flags = RenderableFlags.ReceiveShadows }, [Surface(new Float3(albedo.X, albedo.Y, albedo.Z))]);
        LightDesc light = ShadowSpot();
        _world.AddLight(light);
        _world.Providers.Add(new ShadowItem(_quad, Surface(new Float3(1, 1, 1))));
        AssertShadowed(p => Reference.Local(light, p, s_normal, albedo, 0, 0.5f), p => SquareEdge(p, 2, 1, 2), Frame(), 0.6f);
        AssertShadowed(p => Reference.Local(light, p, s_normal, albedo, 0, 0.5f), p => SquareEdge(p, 2, 1, 2), Frame(), 0.6f);
    }

    // A receiver that takes shadows but casts none, so a light close to it cannot shadow it with itself
    private void AddReceiver(Vector3 albedo)
        => _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(_origin + new Double3(0, 0, QuadDepth)), Flags = RenderableFlags.ReceiveShadows }, [Surface(new Float3(albedo.X, albedo.Y, albedo.Z))]);

    [Fact]
    public void StaticCastersTaggedToUpdateEveryFrame_KeepDrawingBesideMovingOnes()
    {
        AddReceiver(new Vector3(0.8f));
        _world.AddLight(ShadowSpot());
        string source = CasterSource.Replace("Test/Caster", "Test/AnimatedStatic").Replace("\"LightMode\" = \"ShadowCaster\"", "\"LightMode\" = \"ShadowCaster\" \"ShadowUpdate\" = \"EveryFrame\"");
        Material animated = _renderer.CreateMaterial(_renderer.LoadShader(ShaderParser.Parse(source)));
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(1, 0.5, 6)) * Double4x4.CreateScale(0.25), Flags = RenderableFlags.CastShadows | RenderableFlags.ShadowsOnly | RenderableFlags.Static }, [animated]);
        AddCaster(-1.5, -1.5, 6, half: 0.5);
        Frame();
        Frame();
        Assert.Equal(2, Pipeline.ShadowViewsDrawn);
        Frame();
        Assert.Equal(2, Pipeline.ShadowViewsDrawn);
    }

    [Fact]
    public void AMovingCasterReachingAStaticOnlyLight_SplitsIt()
    {
        AddReceiver(new Vector3(0.8f));
        _world.AddLight(ShadowSpot());
        AddCaster(1, 0.5, 6, isStatic: true);
        Frame();
        Frame();
        Assert.Equal(0, Pipeline.ShadowViewsDrawn);

        RenderableHandle moving = AddCaster(-1.5, -1.5, 6, half: 0.5);
        Frame();
        _world.SetTransform(moving, Double4x4.CreateTranslation(new Double3(-1.5, 1.5, 6)) * Double4x4.CreateScale(0.5 / 4));
        Frame();
        Assert.Equal(1, Pipeline.ShadowViewsDrawn);
        Assert.Equal(1, Pipeline.ShadowCopiesMade);
    }

    [Fact]
    public void ALightGivenASmallerTileThanItWants_KeepsIt()
    {
        _world.Shadows.PageSize = 256;
        _world.Shadows.Budget = 256 * 256 * 2;
        AddReceiver(new Vector3(0.8f));
        _world.AddLight(ShadowSpot() with { Range = 20 });
        Frame();

        // The second light wants the whole page, three quarters are left
        _world.AddLight(ShadowSpot() with { Position = new Double3(0.5, 0, 2), Range = 60 });
        Frame();
        Frame();
        Frame();
        Assert.Equal(0, Pipeline.ShadowViewsDrawn);
    }

    [Fact]
    public void ALightOutOfViewForAMoment_KeepsItsShadow()
    {
        AddReceiver(new Vector3(0.8f));
        _world.AddLight(ShadowSpot());
        AddCaster(1, 0.5, 6);
        Frame();
        Double3 at = _origin;
        _origin = new Double3(1000, 0, 0);
        Frame();
        _origin = at;
        Frame();
        Assert.Equal(0, Pipeline.ShadowViewsDrawn);
    }

    [Fact]
    public void PointLightShadows_FallFromEverySideFace()
    {
        Vector3 albedo = new(0.8f, 0.8f, 0.8f);
        AddReceiver(albedo);
        LightDesc light = Point(new Double3(0, 0, 8), 20, 0.05f, new Float3(1, 1, 1)) with { Shadow = new ShadowDesc { Enabled = true, Resolution = 512 } };
        _world.AddLight(light);

        // Halfway from the light to the plane each square doubles, landing on the +X, -X, +Y and -Y faces
        AddCaster(1.5, 0, 9, half: 0.5);
        AddCaster(-1.5, 0, 9, half: 0.5);
        AddCaster(0, 1.5, 9, half: 0.5);
        AddCaster(0, -1.5, 9, half: 0.5);
        float Edge(Vector3 p) => MathF.Min(MathF.Min(SquareEdge(p, 3, 0, 1), SquareEdge(p, -3, 0, 1)), MathF.Min(SquareEdge(p, 0, 3, 1), SquareEdge(p, 0, -3, 1)));
        AssertShadowed(p => Reference.Local(light, p, s_normal, albedo, 0, 0.5f), Edge, Frame(), 0.3f);
    }

    [Fact]
    public void TheLightGrid_FindsLightsAroundAnyChange_HugeOrEndless()
    {
        LightGrid grid = new();
        grid.Add(0, new Double3(-100, -100, -100), 5);
        grid.Add(1, new Double3(1e6, 0, 0), 2000);
        grid.Add(2, Double3.Zero, float.PositiveInfinity);
        // Candidates for the exact test: every light a sphere touches, perhaps more
        Assert.True(grid.Near(new Double3(-103, -100, -100), 1).IsSupersetOf([0, 2]));
        Assert.True(grid.Near(new Double3(1e6 + 1500, 0, 0), 1).IsSupersetOf([1, 2]));
        Assert.True(grid.Near(Double3.Zero, float.PositiveInfinity).IsSupersetOf([0, 1, 2]));
        Assert.True(grid.Near(Double3.Zero, float.MaxValue).IsSupersetOf([0, 1, 2]));
    }

    [Fact]
    public void ShadowTileSizes_RoundToTheNearestPowerOfTwo_AndHoldWithinThreeQuartersOfAnOctave()
    {
        Assert.Equal(256, ShadowPlanner.RoundPow2(300, 64, 1024));
        Assert.Equal(512, ShadowPlanner.RoundPow2(400, 64, 1024));
        Assert.Equal(64, ShadowPlanner.RoundPow2(10, 64, 1024));
        Assert.Equal(1024, ShadowPlanner.RoundPow2(5000, 64, 1024));

        Assert.Equal(256, ShadowPlanner.Keep(400, 256, 64, 1024));
        Assert.Equal(512, ShadowPlanner.Keep(450, 256, 64, 1024));
        Assert.Equal(256, ShadowPlanner.Keep(160, 256, 64, 1024));
        Assert.Equal(128, ShadowPlanner.Keep(150, 256, 64, 1024));
        Assert.Equal(512, ShadowPlanner.Keep(450, 0, 64, 1024));
    }

    private static ShadowPlan Plan(int light, float wanted, int held = 0, bool split = false, int faces = 1)
        => new() { Light = light, Faces = faces, Priority = 1000 - light, Wanted = wanted, MaxTile = 1024, Held = held, Split = split };

    [Fact]
    public void ShadowPlans_GiveWayInOrder_HeldSizesThenStaticCopiesThenHalvingThenDropping()
    {
        // Light 0 holds 512 but wants 340 now, which rounds to 256, light 1 has a static copy, light 2 is least important
        ShadowPlanner planner = new();
        List<ShadowPlan> plans = [Plan(0, 340, held: 512), Plan(1, 256, held: 256, split: true), Plan(2, 256, held: 256)];
        planner.Plan(plans, 512 * 512 + 2 * 256 * 256 + 256 * 256, 64);
        Assert.Equal((512, true), (plans[0].Target, plans[1].Split));

        plans = [Plan(0, 340, held: 512), Plan(1, 256, held: 256, split: true), Plan(2, 256, held: 256)];
        planner = new();
        planner.Plan(plans, 256 * 256 * 4, 64);
        Assert.Equal((256, true, 256, 256), (plans[0].Target, plans[1].Split, plans[1].Target, plans[2].Target));

        plans = [Plan(0, 340, held: 512), Plan(1, 256, held: 256, split: true), Plan(2, 256, held: 256)];
        planner = new();
        planner.Plan(plans, 256 * 256 * 3, 64);
        Assert.Equal((256, false, 256, 256), (plans[0].Target, plans[1].Split, plans[1].Target, plans[2].Target));

        plans = [Plan(0, 340, held: 512), Plan(1, 256, held: 256, split: true), Plan(2, 256, held: 256)];
        planner = new();
        planner.Plan(plans, 256 * 256 * 2, 64);
        Assert.Equal((256, 128, 128), (plans[0].Target, plans[1].Target, plans[2].Target));

        plans = [Plan(0, 256), Plan(1, 256), Plan(2, 256)];
        planner = new();
        planner.Plan(plans, 64 * 64 * 2, 64);
        Assert.Equal((64, 64, 0), (plans[0].Target, plans[1].Target, plans[2].Target));
    }

    [Fact]
    public void TheShadowScale_DropsAtOnce_AndRisesAStepAfterThirtyFramesOfRoom()
    {
        ShadowPlanner planner = new();
        List<ShadowPlan> Wanting() => [Plan(0, 320), Plan(1, 320), Plan(2, 320)];

        // Three lights wanting 256 fit four fifths of a 256 page only at 128, two steps down
        planner.Plan(Wanting(), (long)(256 * 256 * 0.8), 64);
        Assert.Equal(0.5f, planner.Scale, 4);

        long room = 256 * 256 * 4;
        for (int frame = 0; frame < ShadowPlanner.FramesBeforeScaleRises - 1; frame++)
            planner.Plan(Wanting(), room, 64);
        Assert.Equal(0.5f, planner.Scale, 4);
        planner.Plan(Wanting(), room, 64);
        Assert.Equal(1 / ShadowPlanner.ScaleStep, planner.Scale, 4);
    }

    [Fact]
    public void ATightBudget_ScalesEveryLightDown_AndKeepsThemShadowed()
    {
        _world.Shadows.PageSize = 256;
        _world.Shadows.Budget = 256 * 256 * 2;
        _world.Shadows.TexelsPerPixel = 8;
        AddReceiver(new Vector3(0.8f));
        for (int i = 0; i < 3; i++)
            _world.AddLight(ShadowSpot() with { Position = new Double3(i - 1, 0, 2) });
        Frame();
        Frame();
        WorldShadows shadows = _renderer.ShadowsOf(_world)!;
        Assert.Equal(0.5f, shadows.Planner.Scale, 4);
        Assert.All(shadows.Lights.Values, light => Assert.Equal(128, light.Size));
        Frame();
        Assert.Equal(0, Pipeline.ShadowViewsDrawn);
    }

    [Fact]
    public void SizeChanges_ThatOnlySharpen_ArePacedByTheResizeBudget()
    {
        AddReceiver(new Vector3(0.8f));
        _world.AddLight(ShadowSpot());
        _world.AddLight(ShadowSpot() with { Position = new Double3(0.5, 0, 2) });
        Frame();
        Frame();
        _world.Shadows.MaxResizesPerFrame = 1;
        _world.Shadows.TexelsPerPixel = 8;
        Frame();
        Assert.Equal(1, Pipeline.ShadowViewsDrawn);
        Frame();
        Assert.Equal(1, Pipeline.ShadowViewsDrawn);
        Frame();
        Assert.Equal(0, Pipeline.ShadowViewsDrawn);
    }

    [Fact]
    public void ALightOutOfView_GivesItsTilesToLightsInViewWhenPagesRunOut()
    {
        _world.Shadows.PageSize = 512;
        _world.Shadows.Budget = 512 * 512 * 2;
        _world.Shadows.TexelsPerPixel = 100;
        AddReceiver(new Vector3(0.8f));
        LightDesc Spot(double x, uint resolution) => ShadowSpot() with { Position = _origin + new Double3(x, 0, 2), Shadow = new ShadowDesc { Enabled = true, Resolution = resolution } };
        LightHandle away = _world.AddLight(Spot(0, 256));
        Frame();

        // Moved off, three lights take three quarters of the page and a fourth needs the quarter the first still holds
        _origin = new Double3(1000, 0, 0);
        AddReceiver(new Vector3(0.8f));
        _world.AddLight(Spot(-1, 256));
        _world.AddLight(Spot(0, 256));
        _world.AddLight(Spot(1, 256));
        LightHandle small = _world.AddLight(Spot(0.5, 64));
        Frame();
        WorldShadows shadows = _renderer.ShadowsOf(_world)!;
        Assert.False(shadows.Lights[away.Index].HasTiles);
        Assert.True(shadows.Lights[small.Index].HasTiles);
    }

    [Fact]
    public void UnderBudgetPressure_StaticCopiesGiveTheirPagesToNewLights()
    {
        _world.Shadows.PageSize = 256;
        _world.Shadows.Budget = 256 * 256 * 2 * 2;
        _world.Shadows.TexelsPerPixel = 100;
        AddReceiver(new Vector3(0.8f));
        _world.AddLight(ShadowSpot() with { Shadow = new ShadowDesc { Enabled = true, Resolution = 128 } });
        AddCaster(1, 0.5, 6, isStatic: true);
        AddCaster(-1.5, -1.5, 6, half: 0.5);
        Frame();
        Assert.Equal(1, Pipeline.ShadowCopiesMade);

        // The live page holds the first light's tile and the static page its copy, so the new light needs the static page
        LightHandle added = _world.AddLight(ShadowSpot() with { Position = new Double3(0.5, 0, 2), Shadow = new ShadowDesc { Enabled = true, Resolution = 256 } });
        Frame();
        Frame();
        Frame();
        WorldShadows shadows = _renderer.ShadowsOf(_world)!;
        Assert.True(shadows.Lights[added.Index].HasTiles);
        Assert.Equal(0, Pipeline.ShadowViewsDrawn);
    }

    [Fact]
    public void CastersCloserToALightThanItsNearPlane_StillCast()
    {
        Vector3 albedo = new(0.8f, 0.8f, 0.8f);
        AddReceiver(albedo);
        LightDesc light = Point(new Double3(0, 0, 2), 100, 0.2f, new Float3(1, 1, 1)) with { Shadow = new ShadowDesc { Enabled = true } };
        _world.AddLight(light);

        // A metre from the light, inside its two metre near plane, the square grows eightfold by the plane
        AddCaster(0, 0, 3, half: 0.3);
        AssertShadowed(p => Reference.Local(light, p, s_normal, albedo, 0, 0.5f), p => SquareEdge(p, 0, 0, 2.4f), Frame(), 0.6f);
    }

    [Fact]
    public void ForwardPassesThatDiscardOrBlend_CastNothingWithoutACasterPass()
    {
        Vector3 albedo = new(0.8f, 0.8f, 0.8f);
        AddReceiver(albedo);
        LightDesc light = ShadowSpot();
        _world.AddLight(light);
        Material cutout = _renderer.CreateMaterial(_renderer.LoadShader(ShaderParser.Parse(Source.Replace("Test/Lit", "Test/Cutout").Replace("vec3 n = normalize(vNormal);", "if (vWorld.x > 100.0) discard;\n                        vec3 n = normalize(vNormal);"))));
        Material faded = _renderer.CreateMaterial(_renderer.LoadShader(ShaderParser.Parse(Source.Replace("Test/Lit", "Test/Faded").Replace("Cull Off", "Cull Off\n                ZWrite Off"))));
        AddSurfaceCaster(cutout, 1, 0.5, 6);
        AddSurfaceCaster(faded, -1.5, -1.5, 6);
        AssertMatches(p => Reference.Local(light, p, s_normal, albedo, 0, 0.5f), Frame());
    }

    [Fact]
    public void RenderablesTheShadowPassDoesNotDraw_DoNotSplitALight()
    {
        AddReceiver(new Vector3(0.8f));
        _world.AddLight(ShadowSpot());
        Material uncast = Surface(new Float3(1, 1, 1));
        uncast.CastShadows = false;
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(1, 0.5, 6)) * Double4x4.CreateScale(0.25), Flags = RenderableFlags.CastShadows | RenderableFlags.ShadowsOnly | RenderableFlags.Static }, [uncast]);
        AddCaster(-1.5, -1.5, 6, half: 0.5);
        Frame();
        Assert.Equal(0, Pipeline.ShadowCopiesMade);
    }

    [Fact]
    public void KeywordsNoCasterReads_LeaveCachedShadowsAlone()
    {
        AddReceiver(new Vector3(0.8f));
        _world.AddLight(ShadowSpot());
        AddCaster(1, 0.5, 6);
        Frame();
        Frame();
        _world.Globals.SetKeyword("UNUSED_BY_CASTERS", "On");
        Frame();
        Assert.Equal(0, Pipeline.ShadowViewsDrawn);
    }

    [Fact]
    public void ALightPastTheShadowDistance_LosesItsShadowAtOnce()
    {
        _world.Shadows.MinScreenRadius = 0;
        AddReceiver(new Vector3(0.8f));
        _world.AddLight(ShadowSpot() with { Position = new Double3(0, 0, 8), Range = 3 });
        AddCaster(0, 0, 9, half: 0.5);
        Float4 centre = Frame()[Size / 2 * Size + Size / 2];
        Assert.True(centre.X < 1e-4f, $"shadowed centre {centre}");

        _world.Shadows.LocalDistance = 1;
        centre = Frame()[Size / 2 * Size + Size / 2];
        Assert.True(centre.X > 1e-3f, $"lit centre {centre}");
    }

    [Fact]
    public void ALightSmallerOnScreenThanTheLimit_CastsNoShadow()
    {
        AddReceiver(new Vector3(0.8f));
        _world.AddLight(ShadowSpot() with { Position = new Double3(0, 0, 8), Range = 3 });
        AddCaster(0, 0, 9, half: 0.5);
        _world.Shadows.MinScreenRadius = 0;
        Float4 centre = Frame()[Size / 2 * Size + Size / 2];
        Assert.True(centre.X < 1e-4f, $"shadowed centre {centre}");

        _world.Shadows.MinScreenRadius = 1000;
        centre = Frame()[Size / 2 * Size + Size / 2];
        Assert.True(centre.X > 1e-3f, $"lit centre {centre}");
    }

    // Pixels between a tenth and nine tenths lit, against what the light alone gives them
    private static int Penumbra(Float4[] pixels, Func<Vector3, Vector3> lit)
    {
        int count = 0;
        for (int i = 0; i < pixels.Length; i++)
        {
            float full = lit(PixelPosition(i % Size, i / Size)).X;
            float ratio = full > 1e-6f ? pixels[i].X / full : 1;
            if (ratio > 0.1f && ratio < 0.9f)
                count++;
        }
        return count;
    }

    // Covers the half of the plane left of x = 0 for a light shining straight down the view, from a depth
    private RenderableHandle AddHalfCaster(double z) => AddCaster(-10, 0, z, half: 10);

    [Fact]
    public void ContactHardening_SoftensDirectionalShadowsWithDistanceFromTheCaster()
    {
        Vector3 albedo = new(0.8f, 0.8f, 0.8f);
        AddReceiver(albedo);
        LightDesc sun = new() { Type = LightType.Directional, Direction = new Float3(0, 0, 1), Intensity = 0.1f, AngularRadius = 20, Shadow = new ShadowDesc { Enabled = true, Filter = ShadowFilter.ContactHardening } };
        _world.AddLight(sun);
        Func<Vector3, Vector3> lit = p => Reference.Directional(sun, p, s_normal, albedo, 0, 0.5f);

        RenderableHandle caster = AddHalfCaster(9.8);
        for (int frame = 0; frame < 4; frame++)
            Frame();
        int near = Penumbra(Frame(), lit);
        _world.Remove(caster);
        AddHalfCaster(6);
        for (int frame = 0; frame < 4; frame++)
            Frame();
        Float4[] pixels = Frame();
        int far = Penumbra(pixels, lit);
        Assert.True(near <= 2 && far >= Size, $"penumbra pixels {near} from a caster 0.2 away, {far} from one 4 away");

        // Well inside and outside the penumbra the shadow is whole and the light untouched
        AssertShadowed(lit, p => p.X, pixels, 1.6f);
    }

    [Fact]
    public void ContactHardening_SoftensLocalShadowsBySourceRadius_AndPointSourcesMatchTheTent()
    {
        Vector3 albedo = new(0.8f, 0.8f, 0.8f);
        _world.Shadows.TexelsPerPixel = 8;
        AddReceiver(albedo);
        LightDesc spot = ShadowSpot() with { InnerAngle = 50, OuterAngle = 60, SourceRadius = 0.5f, Shadow = new ShadowDesc { Enabled = true, Filter = ShadowFilter.ContactHardening } };
        LightHandle handle = _world.AddLight(spot);
        Func<Vector3, Vector3> lit = p => Reference.Local(spot, p, s_normal, albedo, 0, 0.5f);

        RenderableHandle caster = AddHalfCaster(9.5);
        int near = Penumbra(Frame(), lit);
        _world.Remove(caster);
        AddHalfCaster(4);
        int far = Penumbra(Frame(), lit);
        Assert.True(near <= 2 && far >= Size, $"penumbra pixels {near} from a caster 0.5 away, {far} from one 4 away");

        // A light of no size has no penumbra to measure, so it filters as the tent does
        _world.SetLight(handle, spot with { SourceRadius = 0 });
        Float4[] contact = Frame();
        _world.SetLight(handle, spot with { SourceRadius = 0, Shadow = new ShadowDesc { Enabled = true, Filter = ShadowFilter.Tent } });
        Float4[] tent = Frame();
        for (int i = 0; i < contact.Length; i++)
            Assert.Equal(tent[i].X, contact[i].X, 6);
    }

    [Fact]
    public void TheShadowScale_StopsWhereSmallerTilesNoLongerHelp_AndComesBackAfterAnOverload()
    {
        ShadowPlanner planner = new();
        List<ShadowPlan> Wanting() => [Plan(0, 1024), Plan(1, 1024), Plan(2, 1024)];

        // Even at 64 the three overflow, so the scale sinks only until every light is at 64, four octaves, about eight steps
        for (int frame = 0; frame < 100; frame++)
            planner.Plan(Wanting(), 64 * 64, 64);
        Assert.True(planner.Scale < 0.2f && planner.Scale >= MathF.Pow(ShadowPlanner.ScaleStep, -9), $"scale {planner.Scale}");

        for (int frame = 0; frame < 9 * ShadowPlanner.FramesBeforeScaleRises; frame++)
            planner.Plan(Wanting(), 1L << 30, 64);
        Assert.Equal(1f, planner.Scale, 4);
    }

    // A frame whose errors are expected, cleared after
    private void FrameExpectingErrors()
    {
        ViewDesc view = new() { World = _world, ViewId = 1, Target = _framebuffer };
        view.Layers[0] = new ViewLayer { View = Float4x4.Identity, Projection = s_projection, Position = _origin };
        _renderer.SubmitFrame(new FrameDesc { Views = new[] { view } });
        _device.WaitForIdle();
        _errors.Clear();
    }

    [Fact]
    public void CastersWhoseShaderFailsToCompile_LeaveTheirLightCached()
    {
        AddReceiver(new Vector3(0.8f));
        _world.AddLight(ShadowSpot());
        string broken = CasterSource.Replace("Test/Caster", "Test/Broken").Replace("void main() { gl_Position", "void main() { NotAFunction(); gl_Position");
        Material material = _renderer.CreateMaterial(_renderer.LoadShader(ShaderParser.Parse(broken)));
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(1, 0.5, 6)) * Double4x4.CreateScale(0.25), Flags = RenderableFlags.CastShadows | RenderableFlags.ShadowsOnly }, [material]);
        FrameExpectingErrors();
        FrameExpectingErrors();
        FrameExpectingErrors();
        Assert.Equal(0, Pipeline.ShadowViewsDrawn);
    }

    [Fact]
    public void AMinimumTileLargerThanAPage_StillLeavesLightsATile()
    {
        _world.Shadows.PageSize = 256;
        _world.Shadows.MinTileSize = 8192;
        AddReceiver(new Vector3(0.8f));
        LightHandle light = _world.AddLight(ShadowSpot());
        AddCaster(1, 0.5, 6);
        Frame();
        Assert.Equal(256, _renderer.ShadowsOf(_world)!.Lights[light.Index].Size);
    }

    [Fact]
    public void ADiscardInAComment_LeavesAMaterialCastingThroughDepthAlone()
    {
        Vector3 albedo = new(0.8f, 0.8f, 0.8f);
        AddReceiver(albedo);
        LightDesc light = ShadowSpot();
        _world.AddLight(light);
        Material commented = _renderer.CreateMaterial(_renderer.LoadShader(ShaderParser.Parse(Source.Replace("Test/Lit", "Test/Commented").Replace("vec3 n = normalize(vNormal);", "// no discard here\n                        vec3 n = normalize(vNormal);"))));
        AddSurfaceCaster(commented, 1, 0.5, 6);
        AssertShadowed(p => Reference.Local(light, p, s_normal, albedo, 0, 0.5f), p => SquareEdge(p, 2, 1, 2), Frame(), 0.6f);
    }

    [Fact]
    public void ContactHardening_KeepsTheShadowOfACasterNarrowerThanItsSearch()
    {
        Vector3 albedo = new(0.8f, 0.8f, 0.8f);
        AddReceiver(albedo);
        LightDesc sun = new() { Type = LightType.Directional, Direction = new Float3(0, 0, 1), Intensity = 0.1f, AngularRadius = 45, Shadow = new ShadowDesc { Enabled = true, Filter = ShadowFilter.ContactHardening } };
        _world.AddLight(sun);

        // A small square just above the plane over one pixel: a wide disc's search would step over it, its penumbra is tiny
        AddCaster(0.25, 0.25, 9.9, half: 0.15);
        for (int frame = 0; frame < 4; frame++)
            Frame();
        float full = Reference.Directional(sun, PixelPosition(8, 8), s_normal, albedo, 0, 0.5f).X;
        Float4 under = Frame()[8 * Size + 8];
        Assert.True(under.X < 0.2f * full, $"under the caster {under.X} of {full}");
    }

    [Fact]
    public void ContactHardening_LeavesASlopeUnderAWideLightLit()
    {
        Vector3 albedo = new(0.8f, 0.8f, 0.8f);
        double s = Math.Sqrt(0.5);
        Double4x4 tilt = new(new Double4(s, 0, -s, 0), new Double4(0, 1, 0, 0), new Double4(s, 0, s, 0), new Double4(0, 0, 0, 1));
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(0, 0, QuadDepth)) * tilt * Double4x4.CreateScale(2) }, [Surface(new Float3(albedo.X, albedo.Y, albedo.Z))]);
        LightDesc spot = ShadowSpot() with { InnerAngle = 50, OuterAngle = 60, SourceRadius = 0.5f, Shadow = new ShadowDesc { Enabled = true, Filter = ShadowFilter.ContactHardening } };
        _world.AddLight(spot);

        // The plane casts onto itself and must not shade itself across its own slope
        Vector3 n = Vector3.Normalize(new Vector3(-1, 0, -1));
        Float4[] pixels = Frame();
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                Vector3 flat = PixelPosition(x, y);
                Vector3 p = new(flat.X, flat.Y, QuadDepth - flat.X);
                Vector3 want = Reference.Local(spot, p, n, albedo, 0, 0.5f);
                Float4 got = pixels[y * Size + x];
                Assert.True(MathF.Abs(want.X - got.X) <= 2e-4f + 0.02f * want.X, $"pixel {x},{y}: expected {want.X}, got {got.X}");
            }
        }
    }

    [Fact]
    public void ContactHardening_ReachesTheWholePenumbraOfACasterNearTheLight()
    {
        Vector3 albedo = new(0.8f, 0.8f, 0.8f);
        _world.Shadows.TexelsPerPixel = 8;
        AddReceiver(albedo);
        LightDesc spot = ShadowSpot() with { InnerAngle = 50, OuterAngle = 60, SourceRadius = 0.5f, Shadow = new ShadowDesc { Enabled = true, Filter = ShadowFilter.ContactHardening } };
        _world.AddLight(spot);

        // Two from the light and eight from the plane the penumbra spreads one and a half either side of the edge, so a
        // pixel three quarters into the light still sees part of the caster
        AddHalfCaster(4);
        Float4[] pixels = Frame();
        float sum = 0, full = 0;
        for (int y = 0; y < Size; y++)
        {
            sum += pixels[y * Size + 9].X;
            full += Reference.Local(spot, PixelPosition(9, y), s_normal, albedo, 0, 0.5f).X;
        }
        Assert.True(sum < 0.95f * full, $"three quarters out from the edge {sum} of {full}");
    }

    [Fact]
    public void ShadowTiles_FreedToTrySomething_GoBackWhereTheyWere()
    {
        ShadowTileAllocator tiles = new(1024);
        Assert.True(tiles.TryAllocate(256, out ShadowTile a));
        Assert.True(tiles.TryAllocate(256, out ShadowTile b));
        Assert.True(tiles.TryAllocate(512, out ShadowTile c));
        long used = tiles.UsedTexels;
        tiles.Free(b);
        Assert.True(tiles.TryClaim(b));
        Assert.Equal(used, tiles.UsedTexels);
        Assert.False(tiles.TryClaim(a));

        // Everything given back merges whole, the claimed square included
        foreach (ShadowTile tile in new[] { a, b, c })
            tiles.Free(tile);
        Assert.True(tiles.TryAllocate(1024, out _));
    }

    [Fact]
    public void ShadowTiles_PackLargestFirst_AndMergeBackWhenFreed()
    {
        ShadowTileAllocator tiles = new(1024);
        Assert.True(tiles.TryAllocate(512, out ShadowTile a));
        Assert.True(tiles.TryAllocate(300, out ShadowTile b));
        Assert.Equal(512, b.Size);
        Assert.True(tiles.TryAllocate(256, out ShadowTile c));
        Assert.True(tiles.TryAllocate(256, out ShadowTile d));
        Assert.True(tiles.TryAllocate(512, out ShadowTile e));
        Assert.False(tiles.TryAllocate(512, out _));
        Assert.True(tiles.TryAllocate(256, out ShadowTile f));
        Assert.True(tiles.TryAllocate(256, out ShadowTile g));
        Assert.False(tiles.TryAllocate(16, out _));

        HashSet<(int, int)> corners = [(a.X, a.Y), (b.X, b.Y), (c.X, c.Y), (d.X, d.Y), (e.X, e.Y), (f.X, f.Y), (g.X, g.Y)];
        Assert.Equal(7, corners.Count);
        foreach (ShadowTile tile in new[] { a, b, c, d, e, f, g })
            tiles.Free(tile);
        Assert.Equal(0, tiles.UsedTexels);
        Assert.True(tiles.TryAllocate(1024, out ShadowTile whole));
        Assert.Equal((0, 0, 1024), (whole.X, whole.Y, whole.Size));
    }

    [Fact]
    public void CascadeSpheres_HoldTheirSliceAndStayStillAsTheViewTurns()
    {
        float depth = ShadowMath.SliceSphere(2, 10, 0.5f, 0.3f, false, out float radius);
        float k = 0.5f * 0.5f + 0.3f * 0.3f;

        // Every corner of the slice is inside, the far ones on the sphere when the centre sits inside the slice
        foreach (float z in new[] { 2f, 10f })
        {
            float corner = MathF.Sqrt((z - depth) * (z - depth) + z * z * k);
            Assert.True(corner <= radius + 1e-4f, $"corner at {z} is {corner} from the centre, radius {radius}");
        }
        Assert.Equal(1.0, Math.Abs(ShadowMath.Split(0, 4, 0.1f, 50) / 0.1f), 5);
        Assert.Equal(50f, ShadowMath.Split(4, 4, 0.1f, 50));

        // Snapped centres a texel apart move by exactly one texel
        Double3 c0 = ShadowMath.Snap(new Double3(1000.3, 5, 7), new Float3(1, 0, 0), new Float3(0, 1, 0), new Float3(0, 0, 1), 0.5);
        Double3 c1 = ShadowMath.Snap(new Double3(1000.8, 5, 7), new Float3(1, 0, 0), new Float3(0, 1, 0), new Float3(0, 0, 1), 0.5);
        Assert.Equal(0.5, c1.X - c0.X, 9);
        Assert.Equal(7, c0.Z, 9);
    }

    [Fact]
    public void SpatialSetRecords_MustBeWholeWords()
    {
        Assert.Throws<ArgumentException>(() => _world.CreateSpatialSet<float>("Bad"));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Water
    {
        public Float4 Tint;
        public Float4 Depth;
    }

    [Fact]
    public void SpatialSets_ShareTheirIndexByNameAcrossWorlds_AndRejectBadItems()
    {
        using RenderWorld other = _renderer.CreateWorld();
        SpatialSet<Water> water = other.CreateSpatialSet<Water>("Water");
        SpatialSet<Volume> volumes = _world.CreateSpatialSet<Volume>("Volumes");
        SpatialSet<Volume> otherVolumes = other.CreateSpatialSet<Volume>("Volumes");
        Assert.Equal(volumes.Index, otherVolumes.Index);
        Assert.Equal(water.Index, _world.CreateSpatialSet<Water>("Water").Index);
        Assert.NotEqual(water.Index, volumes.Index);

        Assert.Throws<InvalidOperationException>(() => _world.CreateSpatialSet<Volume>("Volumes"));
        using RenderWorld third = _renderer.CreateWorld();
        Assert.Throws<InvalidOperationException>(() => third.CreateSpatialSet<Volume>("Water"));

        Assert.Throws<ArgumentOutOfRangeException>(() => volumes.Add(new Double3(double.NaN, 0, 0), 1, default));
        Assert.Throws<ArgumentOutOfRangeException>(() => volumes.Add(Double3.Zero, float.PositiveInfinity, default));
        Assert.Throws<ArgumentOutOfRangeException>(() => volumes.Add(Double3.Zero, -1, default));
        SpatialHandle item = volumes.Add(Double3.Zero, 1, default);
        Assert.Throws<ArgumentOutOfRangeException>(() => volumes.Set(item, new Double3(0, double.PositiveInfinity, 0), 1, default));
    }

    [Fact]
    public void DecalsOfEqualPriority_LayInTheOrderTheyWereAdded()
    {
        LitByAmbient();
        AddQuad(Surface(new Float3(1, 1, 1)));
        DecalHandle under = _world.AddDecal(Decal(new Double3(0, 0, QuadDepth), new Double3(8, 8, 2), new Float4(1, 0, 0, 1)));
        _world.AddDecal(Decal(new Double3(0, 0, QuadDepth), new Double3(8, 8, 2), new Float4(0, 0, 1, 1)));
        _world.AddDecal(Decal(new Double3(0, 0, QuadDepth), new Double3(8, 8, 2), new Float4(0, 1, 0, 1), isStatic: true));
        AssertMatches(_ => new Vector3(0, 1, 0), Frame());

        _world.RemoveDecal(under);
        _world.AddDecal(Decal(new Double3(0, 0, QuadDepth), new Double3(8, 8, 2), new Float4(1, 1, 0, 1)));
        AssertMatches(_ => new Vector3(1, 1, 0), Frame());
    }

    [Fact]
    public void StaticDecalsFarFromTheirOrigin_StayInPlace()
    {
        LitByAmbient();
        AddQuad(Surface(new Float3(1, 1, 1)));
        _world.AddDecal(Decal(new Double3(-20_000_000, 0, 0), new Double3(1, 1, 1), new Float4(0, 0, 1, 1), isStatic: true));
        _world.AddDecal(Decal(new Double3(1, 0, QuadDepth), new Double3(1, 8, 2), new Float4(1, 0, 0, 1), isStatic: true));

        AssertMatches(p => p.X > 0.5f && p.X < 1.5f ? new Vector3(1, 0, 0) : Vector3.One, Frame());
    }

    // A reversed Z perspective with an infinite far plane and a 90 degree field of view, looking down +Z
    private static readonly Float4x4 s_perspective = new(new Float4(1, 0, 0, 0), new Float4(0, 1, 0, 0), new Float4(0, 0, 0, 1), new Float4(0, 0, 0.1f, 0));

    // A camera relative view looking along forward: the rows are its right, up and forward axes
    private static Float4x4 Look(Float3 right, Float3 up, Float3 forward)
        => new(new Float4(right.X, up.X, forward.X, 0), new Float4(right.Y, up.Y, forward.Y, 0), new Float4(right.Z, up.Z, forward.Z, 0), new Float4(0, 0, 0, 1));

    private static readonly (Float3 Right, Float3 Up, Float3 Forward)[] s_faces =
    [
        (new(0, 0, -1), new(0, 1, 0), new(1, 0, 0)),
        (new(0, 0, 1), new(0, 1, 0), new(-1, 0, 0)),
        (new(1, 0, 0), new(0, 0, -1), new(0, 1, 0)),
        (new(1, 0, 0), new(0, 0, 1), new(0, -1, 0)),
        (new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)),
        (new(-1, 0, 0), new(0, 1, 0), new(0, 0, -1)),
    ];

    private Float4[] ReadLayer(Texture texture, uint layer)
    {
        _device.Record(cmd => cmd.CopyTextureToBuffer(texture, _readback, 0, TextureRegion.Whole(texture, 0, layer))).Wait();
        Float4[] pixels = _device.Map<Float4>(_readback).ToArray();
        _device.Unmap(_readback);
        return pixels;
    }

    [Fact]
    public void LayeredViews_DrawEachLayerExactlyLikeAViewOfItsOwn()
    {
        _world.Environment = new EnvironmentDesc { AmbientColor = new Float4(0.3f, 0.3f, 0.3f, 1), Fog = default };
        _origin = new Double3(40_000_000.5, -12.25, 9_000_000);
        for (int f = 0; f < 6; f++)
        {
            (Float3 right, Float3 up, Float3 forward) = s_faces[f];
            Double4x4 basis = new(new Double4(right.X, right.Y, right.Z, 0), new Double4(up.X, up.Y, up.Z, 0), new Double4(forward.X, forward.Y, forward.Z, 0), new Double4(0, 0, 0, 1));
            Double3 center = _origin + new Double3(forward.X, forward.Y, forward.Z) * 10;
            _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(center) * basis }, [Surface(new Float3(0.2f + 0.13f * f, 0.9f - 0.1f * f, 0.5f), 0.4f)]);
            _world.AddLight(Point(center - new Double3(forward.X, forward.Y, forward.Z) * 2 + new Double3(right.X, right.Y, right.Z), 5, 0.05f, new Float3(1, 0.9f, 0.8f), isStatic: f % 2 == 0));
            DecalDesc decal = Decal(center - _origin, new Double3(3, 3, 3), new Float4(0, 0, 1, 0.5f), isStatic: f % 3 == 0);
            decal.Transform = Double4x4.CreateTranslation(center + new Double3(right.X, right.Y, right.Z) * 2) * basis * Double4x4.CreateScale(3, 3, -3);
            _world.AddDecal(decal);
        }

        using Texture layers = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(Size, Size, 1, 6, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget | TextureUsage.Sampled));
        using Framebuffer layered = _device.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, layers) { ViewCount = 6 });
        ViewDesc cube = new() { World = _world, ViewId = 7, Target = layered, LayerCount = 6 };
        for (int f = 0; f < 6; f++)
            cube.Layers[f] = new ViewLayer { View = Look(s_faces[f].Right, s_faces[f].Up, s_faces[f].Forward), Projection = s_perspective, Position = _origin + new Double3(0.03 * f, -0.02 * f, 0.01 * f) };
        _renderer.SubmitFrame(new FrameDesc { Views = new[] { cube } });
        _device.WaitForIdle();
        Assert.Empty(_errors);

        for (int f = 0; f < 6; f++)
        {
            Float4[] got = ReadLayer(layers, (uint)f);
            ViewDesc mono = new() { World = _world, ViewId = 8, Target = _framebuffer };
            mono.Layers[0] = cube.Layers[f];
            _renderer.SubmitFrame(new FrameDesc { Views = new[] { mono } });
            _device.WaitForIdle();
            Float4[] want = ReadLayer(_target, 0);

            Assert.True(want.Any(p => p.X > 0.01f), $"layer {f} sees nothing");
            for (int i = 0; i < want.Length; i++)
            {
                for (int c = 0; c < 3; c++)
                {
                    float w = c == 0 ? want[i].X : c == 1 ? want[i].Y : want[i].Z;
                    float g = c == 0 ? got[i].X : c == 1 ? got[i].Y : got[i].Z;
                    Assert.True(MathF.Abs(w - g) <= 1e-4f + 1e-3f * MathF.Abs(w), $"layer {f} pixel {i} channel {c}: alone {w}, layered {g}");
                }
            }
        }
    }

    private Material SkyMaterial(Float3 tint, float gradient)
    {
        Material sky = _renderer.CreateMaterial(_renderer.LoadShader(ShaderParser.Parse(SkySource)));
        sky.SetColor("_Tint", new Float4(tint.X, tint.Y, tint.Z, 1));
        sky.SetFloat("_Gradient", gradient);
        return sky;
    }

    // The clip position of a pixel's centre, the first row at the top
    private static (float X, float Y) PixelClip(int i) => ((i % Size + 0.5f) / Size * 2 - 1, (i / Size + 0.5f) / Size * 2 - 1);

    [Fact]
    public void Sky_FillsWhatNothingCovers_LookingAlongEachPixel()
    {
        _world.Environment = new EnvironmentDesc { AmbientColor = new Float4(1, 1, 1, 1), Fog = default, Sky = SkyMaterial(new Float3(1, 0.8f, 0.6f), 1) };
        AddQuad(Surface(new Float3(0.2f, 0.3f, 0.4f)));

        ViewDesc view = new() { World = _world, ViewId = 10, Target = _framebuffer };
        view.Layers[0] = new ViewLayer { View = Float4x4.Identity, Projection = s_perspective, Position = _origin };
        _renderer.SubmitFrame(new FrameDesc { Views = new[] { view } });
        _device.WaitForIdle();
        Assert.Empty(_errors);
        Float4[] pixels = ReadLayer(_target, 0);

        for (int i = 0; i < pixels.Length; i++)
        {
            (float x, float y) = PixelClip(i);
            Vector3 want = MathF.Abs(x) < 0.4f && MathF.Abs(y) < 0.4f
                ? new Vector3(0.2f, 0.3f, 0.4f)
                : (Vector3.Normalize(new Vector3(x, y, 1)) * 0.5f + new Vector3(0.5f)) * new Vector3(1, 0.8f, 0.6f);
            Assert.True(Vector3.Distance(want, new Vector3(pixels[i].X, pixels[i].Y, pixels[i].Z)) < 2e-3f, $"pixel {i}: want {want}, got {pixels[i]}");
        }
    }

    [Fact]
    public void ViewsLeavingTheSkyOut_ShowTheirClearColour()
    {
        _world.Environment = new EnvironmentDesc { Fog = default, Sky = SkyMaterial(new Float3(1, 1, 1), 0) };
        ViewDesc view = new() { World = _world, ViewId = 12, Target = _framebuffer, Sky = false, ClearColor = new Float4(0.25f, 0.5f, 0.75f, 1) };
        view.Layers[0] = new ViewLayer { View = Float4x4.Identity, Projection = s_perspective, Position = _origin };
        _renderer.SubmitFrame(new FrameDesc { Views = new[] { view } });
        _device.WaitForIdle();
        Assert.Empty(_errors);
        Assert.All(ReadLayer(_target, 0), p => Assert.True(Vector3.Distance(new Vector3(p.X, p.Y, p.Z), new Vector3(0.25f, 0.5f, 0.75f)) < 1e-3f));
    }

    [Fact]
    public void SkyProbe_HoldsTheSkyInEveryDirection()
    {
        _world.Environment = new EnvironmentDesc { Fog = default, Sky = SkyMaterial(new Float3(1, 1, 1), 1) };
        using Texture layers = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(Size, Size, 1, 6, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget | TextureUsage.Sampled));
        using Framebuffer layered = _device.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, layers) { ViewCount = 6 });
        ViewDesc cube = new() { World = _world, ViewId = 11, Target = layered, LayerCount = 6 };
        for (int f = 0; f < 6; f++)
            cube.Layers[f] = new ViewLayer { View = Look(s_faces[f].Right, s_faces[f].Up, s_faces[f].Forward), Projection = s_perspective, Position = _origin };

        Float4[][] Capture()
        {
            _renderer.SubmitFrame(new FrameDesc { Views = new[] { cube } });
            _device.WaitForIdle();
            Assert.Empty(_errors);
            return Enumerable.Range(0, 6).Select(f => ReadLayer(layers, (uint)f)).ToArray();
        }

        Float4[][] sky = Capture();
        Material viewer = _renderer.CreateMaterial(_renderer.LoadShader(ShaderParser.Parse(ProbeViewerSource)));
        for (int f = 0; f < 6; f++)
        {
            (Float3 right, Float3 up, Float3 forward) = s_faces[f];
            Double4x4 basis = new(new Double4(right.X, right.Y, right.Z, 0), new Double4(up.X, up.Y, up.Z, 0), new Double4(forward.X, forward.Y, forward.Z, 0), new Double4(0, 0, 0, 1));
            Double3 center = _origin + new Double3(forward.X, forward.Y, forward.Z) * 4.2;
            _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(center) * basis }, [viewer]);
        }
        Float4[][] probe = Capture();

        for (int f = 0; f < 6; f++)
        {
            for (int i = 0; i < Size * Size; i++)
            {
                Vector3 want = new(sky[f][i].X, sky[f][i].Y, sky[f][i].Z);
                Vector3 got = new(probe[f][i].X, probe[f][i].Y, probe[f][i].Z);
                Assert.True(Vector3.Distance(want, got) < 0.01f, $"face {f} pixel {i}: sky {want}, probe {got}");
            }
        }
    }

    [Fact]
    public void FogUsingTheSky_TakesItsColour_AndFollowsItsChanges()
    {
        Material sky = SkyMaterial(new Float3(0.9f, 0.4f, 0.1f), 0);
        AddQuad(Surface(new Float3(1, 1, 1)));
        _world.Environment = new EnvironmentDesc
        {
            AmbientColor = new Float4(0.5f, 0.5f, 0.5f, 1),
            Fog = new FogDesc { Mode = FogMode.Linear, Color = new Float4(0, 1, 0, 1), Start = 0, End = 20, UseSky = true },
            Sky = sky,
        };

        Vector3 Fogged(Vector3 p, Vector3 skyColor) => Vector3.Lerp(skyColor, new Vector3(0.5f), Math.Clamp(1 - p.Length() / 20, 0, 1));
        AssertMatches(p => Fogged(p, new Vector3(0.9f, 0.4f, 0.1f)), Frame());

        sky.SetColor("_Tint", new Float4(0.2f, 0.3f, 1, 1));
        Frame();
        AssertMatches(p => Fogged(p, new Vector3(0.2f, 0.3f, 1)), Frame());

        _world.Environment.SkyCapture = SkyCaptureMode.Never;
        sky.SetColor("_Tint", new Float4(1, 1, 1, 1));
        Frame();
        Frame();
        AssertMatches(p => Fogged(p, new Vector3(0.2f, 0.3f, 1)), Frame());
    }

    private static readonly Vector3 s_skyColor = new(0.1f, 0.2f, 0.4f);

    // A room of single sided unlit walls, one colour per side, facing in on a centre; the camera's reflector sits on layer 2
    private void Room(Double3 center, double half, Func<int, Float3> color)
    {
        Shader unlit = _renderer.LoadShader(ShaderParser.Parse(UnlitSource));
        Float3[] positions = [new(-4, -4, 0), new(4, -4, 0), new(4, 4, 0), new(-4, 4, 0)];
        GpuMesh facing = _renderer.CreateMesh(new MeshData
        {
            VertexCount = 4,
            Streams = [new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(positions.AsSpan()).ToArray() }],
            Indices = new uint[] { 0, 2, 1, 0, 3, 2 },
        });
        for (int f = 0; f < 6; f++)
        {
            (Float3 right, Float3 up, Float3 forward) = s_faces[f];
            Double4x4 basis = new(new Double4(right.X, right.Y, right.Z, 0), new Double4(up.X, up.Y, up.Z, 0), new Double4(forward.X, forward.Y, forward.Z, 0), new Double4(0, 0, 0, 1));
            Material wall = _renderer.CreateMaterial(unlit);
            Float3 c = color(f);
            wall.SetColor("_Color", new Float4(c.X, c.Y, c.Z, 1));
            Double3 at = _origin + center + new Double3(forward.X, forward.Y, forward.Z) * half;
            _world.Add(new RenderableDesc { Mesh = facing, Transform = Double4x4.CreateTranslation(at) * basis * Double4x4.CreateScale(half / 4), LayerMask = 1, Flags = RenderableFlags.ReceiveShadows }, [wall]);
        }
        _world.Environment = new EnvironmentDesc { Fog = default, AmbientColor = default, Sky = SkyMaterial(new Float3(s_skyColor.X, s_skyColor.Y, s_skyColor.Z), 0) };
    }

    private Material Reflector(float roughness = 0)
    {
        Material material = _renderer.CreateMaterial(_renderer.LoadShader(ShaderParser.Parse(ReflectSource)));
        material.SetFloat("_Roughness", roughness);
        return material;
    }

    private ReflectionProbeDesc RoomProbe(Double3 center, Float3 size, int importance = 1, float intensity = 1, float blend = 0) => new()
    {
        Position = _origin + center,
        BoxSize = size,
        BlendDistance = blend,
        Importance = importance,
        Intensity = intensity,
        Mode = ReflectionProbeMode.Realtime,
        CullingMask = 1,
    };

    [Fact]
    public void RealtimeProbes_ReflectTheirRoom_AndTheSkyOutsideTheirBox()
    {
        Room(new Double3(0, 0, 10), 20, _ => new Float3(0.8f, 0.5f, 0.2f));
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(_origin + new Double3(0, 0, QuadDepth)), LayerMask = 2 }, [Reflector()]);
        _world.AddReflectionProbe(RoomProbe(new Double3(-10, 0, 10), new Float3(20, 40, 40)));

        Frame();
        AssertMatches(p => p.X < 0 ? new Vector3(0.8f, 0.5f, 0.2f) : s_skyColor, Frame());
    }

    [Fact]
    public void BoxProjection_ReflectsTheWallEachRayReallyHits()
    {
        Float3[] colors = [new(1, 0, 0), new(0, 1, 0), new(0, 0, 1), new(1, 1, 0), new(0, 1, 1), new(1, 0, 1)];
        Room(new Double3(0, 0, 10), 20, f => colors[f]);
        _world.Add(new RenderableDesc { Mesh = IndirectQuad(secondUVs: false), Transform = Double4x4.CreateTranslation(_origin + new Double3(0, 0, QuadDepth)), LayerMask = 2 }, [Reflector()]);
        _world.AddReflectionProbe(RoomProbe(new Double3(0, 0, 10), new Float3(40, 40, 40)));
        Frame();
        Float4[] pixels = Frame();

        int checkedPixels = 0;
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                Vector3 p = PixelPosition(x, y);
                Vector3 n = IndirectNormal(p);
                Vector3 v = Vector3.Normalize(-p);
                float a = 0;
                Vector3 r = Vector3.Normalize(Vector3.Lerp(n, Vector3.Reflect(-v, n), (1 - a) * (MathF.Sqrt(1 - a) + a)));

                // The ray from the point to the room's walls, at x, y in -20..20 and z in -10..30
                Vector3 local = p - new Vector3(0, 0, 10);
                Vector3 far = Vector3.Max((new Vector3(20) - local) / r, (new Vector3(-20) - local) / r);
                float t = MathF.Min(far.X, MathF.Min(far.Y, far.Z));
                Vector3 hit = local + r * t;
                Vector3 edge = new Vector3(20) - Vector3.Abs(hit);
                int axis = far.X <= far.Y && far.X <= far.Z ? 0 : far.Y <= far.Z ? 1 : 2;
                float[] margins = [edge.X, edge.Y, edge.Z];
                if (margins.Where((m, i) => i != axis).Min() < 1.5f)
                    continue;

                int face = axis * 2 + ((axis == 0 ? hit.X : axis == 1 ? hit.Y : hit.Z) > 0 ? 0 : 1);
                Float3 want = colors[face];
                Float4 got = pixels[y * Size + x];
                Assert.True(Vector3.Distance(new Vector3(want.X, want.Y, want.Z), new Vector3(got.X, got.Y, got.Z)) < 0.02f, $"pixel {x},{y}: wall {face} {want}, got {got}");
                checkedPixels++;
            }
        }
        Assert.True(checkedPixels > 100);
    }

    [Fact]
    public void OverlappingProbes_BlendByImportanceAndBlendDistance()
    {
        Vector3 wall = new(0.8f, 0.5f, 0.2f);
        Room(new Double3(0, 0, 10), 20, _ => new Float3(wall.X, wall.Y, wall.Z));
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(_origin + new Double3(0, 0, QuadDepth)), LayerMask = 2 }, [Reflector()]);
        ReflectionProbeDesc small = RoomProbe(new Double3(-8, 0, 10), new Float3(24, 40, 40), importance: 1, intensity: 1, blend: 2);
        small.Resolution = 32;
        ReflectionProbeDesc large = RoomProbe(new Double3(8, 0, 10), new Float3(24, 40, 40), importance: 2, intensity: 0.5f, blend: 2);
        large.Resolution = 256;
        _world.AddReflectionProbe(small);
        _world.AddReflectionProbe(large);
        Frame();
        Frame();
        Float4[] pixels = Frame();

        AssertMatches(p =>
        {
            float w1 = Math.Clamp((12 - MathF.Abs(p.X + 8)) / 2, 0, 1);
            float w2 = Math.Clamp((12 - MathF.Abs(p.X - 8)) / 2, 0, 1);
            float total = w2 + w1 * (1 - w2);
            return wall * (0.5f * w2 + w1 * (1 - w2)) + s_skyColor * (1 - total);
        }, pixels);
    }

    [Fact]
    public void SlicedCaptures_TakeSevenFrames_AndRequestsCaptureAgain()
    {
        Room(new Double3(0, 0, 10), 20, _ => new Float3(0.8f, 0.5f, 0.2f));
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(_origin + new Double3(0, 0, QuadDepth)), LayerMask = 2 }, [Reflector()]);
        ReflectionProbeDesc desc = RoomProbe(new Double3(0, 0, 10), new Float3(40, 40, 40));
        desc.TimeSlicing = ReflectionProbeTimeSlicing.IndividualFaces;
        ReflectionProbeHandle probe = _world.AddReflectionProbe(desc);

        for (int frame = 0; frame < 6; frame++)
            AssertMatches(_ => s_skyColor, Frame());
        AssertMatches(_ => new Vector3(0.8f, 0.5f, 0.2f), Frame());

        _world.Environment.SkyColor = new Float4(0, 0, 0, 1);
        _world.Environment.Sky!.SetColor("_Tint", new Float4(0, 1, 0, 1));
        foreach (RenderableHandle wall in Enumerable.Range(0, 6).Select(i => new RenderableHandle(i, 1)))
            _world.SetLayerMask(wall, 4);
        for (int frame = 0; frame < 4; frame++)
            Frame();
        AssertMatches(_ => new Vector3(0.8f, 0.5f, 0.2f), Frame());

        _world.RenderReflectionProbe(probe);
        for (int frame = 0; frame < 6; frame++)
            Frame();
        AssertMatches(_ => new Vector3(0, 1, 0), Frame());
    }

    [Fact]
    public void BakedProbes_ShowWhatABakeReadBack()
    {
        Room(new Double3(0, 0, 10), 20, f => new Float3(0.2f + 0.1f * f, 0.5f, 0.9f - 0.1f * f));
        _world.Add(new RenderableDesc { Mesh = IndirectQuad(secondUVs: false), Transform = Double4x4.CreateTranslation(_origin + new Double3(0, 0, QuadDepth)), LayerMask = 2 }, [Reflector(0.3f)]);
        ReflectionProbeDesc desc = RoomProbe(new Double3(0, 0, 10), new Float3(40, 40, 40));
        ReflectionProbeHandle probe = _world.AddReflectionProbe(desc);
        Frame();
        Float4[] realtime = Frame();

        BakedReflectionProbe? baked = null;
        _world.BakeReflectionProbe(probe, data => baked = data);
        for (int frame = 0; frame < 4 && baked == null; frame++)
            Frame();
        Assert.NotNull(baked);
        Assert.Equal(128, baked!.Size);
        Assert.Equal(6, baked.Mips);
        Assert.Equal(128 * 128 * 8, baked.Faces[0].Length);
        Assert.Equal(4 * 4 * 8, baked.Faces[6 * 6 - 1].Length);

        foreach (RenderableHandle wall in Enumerable.Range(0, 6).Select(i => new RenderableHandle(i, 1)))
            _world.Remove(wall);
        desc.Mode = ReflectionProbeMode.Baked;
        desc.Cubemap = _renderer.CreateReflectionCubemap(baked);
        _textures.Add(desc.Cubemap);
        _world.SetReflectionProbe(probe, desc);
        Frame();
        Float4[] shown = Frame();

        Assert.True(realtime.Any(p => p.X > 0.1f));
        for (int i = 0; i < realtime.Length; i++)
            Assert.True(Vector3.Distance(new Vector3(realtime[i].X, realtime[i].Y, realtime[i].Z), new Vector3(shown[i].X, shown[i].Y, shown[i].Z)) < 0.01f, $"pixel {i}: realtime {realtime[i]}, baked {shown[i]}");
    }

    [Fact]
    public void AddingProbes_LeavesCapturedOnesAsTheyAre()
    {
        Room(new Double3(0, 0, 10), 20, _ => new Float3(0.8f, 0.5f, 0.2f));
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(_origin + new Double3(0, 0, QuadDepth)), LayerMask = 2 }, [Reflector()]);
        ReflectionProbeDesc desc = RoomProbe(new Double3(0, 0, 10), new Float3(40, 40, 40));
        _world.AddReflectionProbe(desc);
        Frame();
        AssertMatches(_ => new Vector3(0.8f, 0.5f, 0.2f), Frame());

        foreach (RenderableHandle wall in Enumerable.Range(0, 6).Select(i => new RenderableHandle(i, 1)))
            _world.SetLayerMask(wall, 4);
        for (int i = 0; i < 5; i++)
            _world.AddReflectionProbe(RoomProbe(new Double3(100 + i * 50, 0, 0), new Float3(10, 10, 10)));
        for (int frame = 0; frame < 6; frame++)
            Frame();
        AssertMatches(_ => new Vector3(0.8f, 0.5f, 0.2f), Frame());
    }

    private Float4[] PerspectiveFrame(int viewId)
    {
        ViewDesc view = new() { World = _world, ViewId = viewId, Target = _framebuffer };
        view.Layers[0] = new ViewLayer { View = Float4x4.Identity, Projection = s_perspective, Position = _origin };
        _renderer.SubmitFrame(new FrameDesc { Views = new[] { view } });
        _device.WaitForIdle();
        Assert.Empty(_errors);
        return ReadLayer(_target, 0);
    }

    private static void AssertPixels(Func<int, Vector3> expected, Float4[] pixels, float tolerance)
    {
        for (int i = 0; i < pixels.Length; i++)
        {
            Vector3 want = expected(i);
            Vector3 got = new(pixels[i].X, pixels[i].Y, pixels[i].Z);
            Assert.True(Vector3.Distance(want, got) <= tolerance * MathF.Max(1, want.Length()), $"pixel {i}: want {want}, got {got}");
        }
    }

    [Fact]
    public void ProceduralSky_IsProwlsSky_LitByTheWorldsSun()
    {
        Vector3 sun = Vector3.Normalize(new Vector3(0.3f, 0.25f, 1));
        _world.AddLight(new LightDesc { Type = LightType.Directional, Direction = new Float3(-sun.X, -sun.Y, -sun.Z), Intensity = 1 });
        _world.Environment = new EnvironmentDesc { Fog = default, Sky = _renderer.CreateMaterial(_renderer.Skies.Procedural) };

        AssertPixels(i =>
        {
            (float x, float y) = PixelClip(i);
            return Reference.ProceduralSky(Vector3.Normalize(new Vector3(x, y, 1)), sun);
        }, PerspectiveFrame(13), 4e-3f);
    }

    [Fact]
    public void GradientSky_BlendsItsColours_AndOrthographicViewsSeeItThroughAPerspective()
    {
        Material gradient = _renderer.CreateMaterial(_renderer.Skies.Gradient);
        gradient.SetColor("_TopColor", new Float4(0.1f, 0.3f, 0.9f, 1));
        gradient.SetColor("_BottomColor", new Float4(0.9f, 0.6f, 0.2f, 1));
        gradient.SetFloat("_Exponent", 2);
        _world.Environment = new EnvironmentDesc { Fog = default, Sky = gradient };

        Vector3 Gradient(Vector3 dir)
        {
            float t = MathF.Pow(Math.Clamp(dir.Y * 0.5f + 0.5f, 0, 1), 2);
            return Vector3.Lerp(new Vector3(0.9f, 0.6f, 0.2f), new Vector3(0.1f, 0.3f, 0.9f), t);
        }
        AssertPixels(i =>
        {
            (float x, float y) = PixelClip(i);
            return Gradient(Vector3.Normalize(new Vector3(x, y, 1)));
        }, PerspectiveFrame(14), 2e-3f);

        float f = 1 / MathF.Tan(1.0471975512f * 0.5f);
        AssertPixels(i =>
        {
            (float x, float y) = PixelClip(i);
            return Gradient(Vector3.Normalize(new Vector3(x / f, y / f, 1)));
        }, Frame(), 2e-3f);
    }

    [Fact]
    public void SixSidedSky_ShowsEachImageOnItsSide()
    {
        Float3[] colors = [new(1, 0, 0), new(0, 1, 0), new(0, 0, 1), new(1, 1, 0), new(0, 1, 1), new(1, 0, 1)];
        string[] names = ["_CubeRight", "_CubeLeft", "_CubeTop", "_CubeBottom", "_CubeFront", "_CubeBack"];
        Material cubemap = _renderer.CreateMaterial(_renderer.Skies.SixSided);
        for (int f = 0; f < 6; f++)
            cubemap.SetTexture(names[f], Lightmap<Float4>(1, PixelFormat.R32_G32_B32_A32_Float, [new Float4(colors[f].X, colors[f].Y, colors[f].Z, 1)]));
        cubemap.SetFloat("_Exposure", 0.5f);
        _world.Environment = new EnvironmentDesc { Fog = default, Sky = cubemap };

        using Texture layers = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(Size, Size, 1, 6, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget | TextureUsage.Sampled));
        using Framebuffer layered = _device.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, layers) { ViewCount = 6 });
        ViewDesc cube = new() { World = _world, ViewId = 15, Target = layered, LayerCount = 6 };
        for (int f = 0; f < 6; f++)
            cube.Layers[f] = new ViewLayer { View = Look(s_faces[f].Right, s_faces[f].Up, s_faces[f].Forward), Projection = s_perspective, Position = _origin };
        _renderer.SubmitFrame(new FrameDesc { Views = new[] { cube } });
        _device.WaitForIdle();
        Assert.Empty(_errors);

        for (int f = 0; f < 6; f++)
            AssertPixels(_ => new Vector3(colors[f].X, colors[f].Y, colors[f].Z) * 0.5f, ReadLayer(layers, (uint)f), 1e-3f);
    }

    [Fact]
    public void SkyAmbient_LightsWithTheSkysColour()
    {
        AddQuad(Surface(new Float3(0.5f, 0.8f, 1)));
        _world.Environment = new EnvironmentDesc { Ambient = AmbientMode.Sky, AmbientIntensity = 1, Fog = default, Sky = SkyMaterial(new Float3(0.4f, 0.3f, 0.2f), 0) };
        Frame();
        AssertMatches(_ => new Vector3(0.2f, 0.24f, 0.2f), Frame());
    }

    [Fact]
    public void ProceduralSky_TakesItsSunExposureTintAndGround()
    {
        Vector3 sun = Vector3.Normalize(new Vector3(0.3f, 0.25f, 1));
        _world.AddLight(new LightDesc { Type = LightType.Directional, Direction = new Float3(-sun.X, -sun.Y, -sun.Z), Intensity = 1 });
        Material sky = _renderer.CreateMaterial(_renderer.Skies.Procedural);
        sky.SetFloat("_SunSize", 3);
        sky.SetFloat("_SunGlow", 0.5f);
        sky.SetFloat("_Exposure", 0.7f);
        sky.SetColor("_Tint", new Float4(1, 0.8f, 0.6f, 1));
        sky.SetColor("_GroundColor", new Float4(0.1f, 0.3f, 0.2f, 1));
        _world.Environment = new EnvironmentDesc { Fog = default, Sky = sky };

        AssertPixels(i =>
        {
            (float x, float y) = PixelClip(i);
            return Reference.ProceduralSky(Vector3.Normalize(new Vector3(x, y, 1)), sun, 3, 0.5f, 0.7f, new Vector3(1, 0.8f, 0.6f), new Vector3(0.1f, 0.3f, 0.2f));
        }, PerspectiveFrame(16), 4e-3f);

        sky.SetFloat("_Exposure", 0);
        AssertPixels(_ => Vector3.Zero, PerspectiveFrame(16), 1e-4f);
    }

    // The six faces of a cube view from the origin, in the cube's face order
    private Float4[][] CubeFrame(int viewId)
    {
        using Texture layers = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(Size, Size, 1, 6, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget | TextureUsage.Sampled));
        using Framebuffer layered = _device.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, layers) { ViewCount = 6 });
        ViewDesc cube = new() { World = _world, ViewId = viewId, Target = layered, LayerCount = 6 };
        for (int f = 0; f < 6; f++)
            cube.Layers[f] = new ViewLayer { View = Look(s_faces[f].Right, s_faces[f].Up, s_faces[f].Forward), Projection = s_perspective, Position = _origin };
        _renderer.SubmitFrame(new FrameDesc { Views = new[] { cube } });
        _device.WaitForIdle();
        Assert.Empty(_errors);
        return Enumerable.Range(0, 6).Select(f => ReadLayer(layers, (uint)f)).ToArray();
    }

    [Fact]
    public void CubemapSky_ShowsTheCubeTurnedByItsRotation()
    {
        Float4[] colors = [new(1, 0, 0, 1), new(0, 1, 0, 1), new(0, 0, 1, 1), new(1, 1, 0, 1), new(0, 1, 1, 1), new(1, 0, 1, 1)];
        Texture texture = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled) with { Type = TextureType.TextureCube });
        for (uint f = 0; f < 6; f++)
            _device.UpdateTexture<Float4>(texture, [colors[f]], new TextureRegion(0, 0, 0, 1, 1, 1, 0, f));
        _textures.Add(texture);
        Material sky = _renderer.CreateMaterial(_renderer.Skies.Cubemap);
        sky.SetTexture("_Cubemap", texture, SamplerDescription.Point);
        sky.SetFloat("_Exposure", 0.5f);
        _world.Environment = new EnvironmentDesc { Fog = default, Sky = sky };

        Float4[][] faces = CubeFrame(17);
        for (int f = 0; f < 6; f++)
            AssertPixels(_ => new Vector3(colors[f].X, colors[f].Y, colors[f].Z) * 0.5f, faces[f], 1e-3f);

        // A quarter turn shows on each side what was a quarter turn to its right
        sky.SetFloat("_Rotation", 90);
        int[] shown = [5, 4, 2, 3, 0, 1];
        faces = CubeFrame(17);
        for (int f = 0; f < 6; f++)
            AssertPixels(_ => new Vector3(colors[shown[f]].X, colors[shown[f]].Y, colors[shown[f]].Z) * 0.5f, faces[f], 1e-3f);
    }

    [Fact]
    public void PanoramaSky_MapsForwardToTheImagesCentre_AndTurnsByItsRotation()
    {
        Float4[] columns = [new(1, 0, 0, 1), new(0, 1, 0, 1), new(0, 0, 1, 1), new(1, 1, 0, 1)];
        Material sky = _renderer.CreateMaterial(_renderer.Skies.Panorama);
        sky.SetTexture("_Panorama", Lightmap<Float4>(4, PixelFormat.R32_G32_B32_A32_Float, columns), SamplerDescription.Point);
        sky.SetColor("_Tint", new Float4(1, 0.5f, 1, 1));
        _world.Environment = new EnvironmentDesc { Fog = default, Sky = sky };

        Vector3 Column(int c) => new(columns[c].X, columns[c].Y * 0.5f, columns[c].Z);
        AssertPixels(i => PixelClip(i).X < 0 ? Column(1) : Column(2), PerspectiveFrame(18), 1e-3f);

        sky.SetFloat("_Rotation", 90);
        AssertPixels(i => PixelClip(i).X < 0 ? Column(2) : Column(3), PerspectiveFrame(18), 1e-3f);
    }

    [Fact]
    public void SkyAmbient_IsReadBackToTheHost_AfterTheSkyIsCaptured()
    {
        _world.Environment = new EnvironmentDesc { Ambient = AmbientMode.Sky, AmbientIntensity = 2, Fog = default, Sky = SkyMaterial(new Float3(1, 1, 1), 1) };
        Assert.Equal(0, _world.SkyAmbientVersion);
        for (int frame = 0; frame < 3; frame++)
            Frame();
        Assert.True(_world.SkyAmbientVersion > 0);

        // The sky is d * 0.5 + 0.5 per channel, whose cosine weighted mean over a normal's hemisphere is 0.5 + n / 3
        foreach (Float3 n in new Float3[] { new(0, 1, 0), new(1, 0, 0), new(0, 0, -1), Float3.Normalize(new Float3(1, -1, 1)) })
        {
            Float3 want = new Float3(0.5f, 0.5f, 0.5f) + n / 3;
            Float3 got = _world.SkyAmbient.Evaluate(n);
            Assert.True(Float3.Length(got - want) < 0.02f, $"normal {n}: want {want}, got {got}");
            Assert.True(Float3.Length(_world.EvaluateAmbient(n) - want * 2) < 0.04f, $"normal {n}: ambient {_world.EvaluateAmbient(n)}");
        }
    }

    [Fact]
    public void EvaluateAmbient_GivesTheUniformAndHemisphereLight_TheShadersUse()
    {
        _world.Environment = new EnvironmentDesc { Ambient = AmbientMode.Uniform, AmbientColor = new Float4(0.2f, 0.4f, 0.8f, 1), AmbientIntensity = 0.5f };
        Assert.Equal(new Float3(0.1f, 0.2f, 0.4f), _world.EvaluateAmbient(new Float3(0, 1, 0)));

        _world.Environment = new EnvironmentDesc { Ambient = AmbientMode.Hemisphere, AmbientSky = new Float4(1, 0, 0, 1), AmbientGround = new Float4(0, 0, 1, 1), AmbientIntensity = 1 };
        Assert.Equal(new Float3(1, 0, 0), _world.EvaluateAmbient(new Float3(0, 1, 0)));
        Assert.Equal(new Float3(0.5f, 0, 0.5f), _world.EvaluateAmbient(new Float3(1, 0, 0)));
    }

    [Fact]
    public void ProbeCaptures_GoToTheNearestProbesFirst()
    {
        Room(new Double3(0, 0, 10), 20, _ => new Float3(0.8f, 0.5f, 0.2f));
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(_origin + new Double3(0, 0, QuadDepth)), LayerMask = 2 }, [Reflector()]);
        _world.AddReflectionProbe(RoomProbe(new Double3(10, 0, 18), new Float3(20, 40, 24)));
        _world.AddReflectionProbe(RoomProbe(new Double3(-10, 0, 10), new Float3(20, 40, 40)));

        Frame();
        AssertMatches(p => p.X < 0 ? new Vector3(0.8f, 0.5f, 0.2f) : s_skyColor, Frame());
        AssertMatches(_ => new Vector3(0.8f, 0.5f, 0.2f), Frame());
    }

    [Fact]
    public void ProbesCapturedOnce_KeepTheirSkyAndPlace_UntilAskedAgain()
    {
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(_origin + new Double3(0, 0, QuadDepth)), LayerMask = 2 }, [Reflector()]);
        Material sky = SkyMaterial(new Float3(0.1f, 0.2f, 0.4f), 0);
        _world.Environment = new EnvironmentDesc { Fog = default, AmbientColor = default, Sky = sky };
        ReflectionProbeDesc desc = RoomProbe(new Double3(0, 0, 10), new Float3(40, 40, 40));
        desc.Intensity = 0.5f;
        ReflectionProbeHandle probe = _world.AddReflectionProbe(desc);
        Frame();
        AssertMatches(_ => new Vector3(0.05f, 0.1f, 0.2f), Frame());

        sky.SetColor("_Tint", new Float4(1, 0.5f, 0, 1));
        desc.Position += new Double3(1, 0, 0);
        _world.SetReflectionProbe(probe, desc);
        for (int frame = 0; frame < 4; frame++)
            Frame();
        AssertMatches(_ => new Vector3(0.05f, 0.1f, 0.2f), Frame());

        _world.RenderReflectionProbe(probe);
        Frame();
        AssertMatches(_ => new Vector3(0.5f, 0.25f, 0), Frame());
    }

    [Fact]
    public void TheCaptureBudget_FitsMoreSmallProbesThanBigOnes()
    {
        int Captured(uint resolution)
        {
            ReflectionProbeHandle[] probes = new ReflectionProbeHandle[4];
            for (int i = 0; i < probes.Length; i++)
            {
                ReflectionProbeDesc desc = RoomProbe(new Double3(100 * i, 0, 0), new Float3(10, 10, 10));
                desc.Resolution = resolution;
                probes[i] = _world.AddReflectionProbe(desc);
            }
            Frame();
            int count = _world.Reflections.Captures.Count;
            foreach (ReflectionProbeHandle probe in probes)
                _world.RemoveReflectionProbe(probe);
            return count;
        }

        // A 64 probe and its filter is about 57k texels, a 256 one about 917k, against 256k a frame
        Assert.Equal(4, Captured(64));
        Assert.Equal(1, Captured(256));
    }

    [Fact]
    public void ProbesSlicedAcrossFrames_StartNoNewCapture_OnceTheBudgetIsSpent()
    {
        for (int i = 0; i < 4; i++)
        {
            ReflectionProbeDesc desc = RoomProbe(new Double3(100 * i, 0, 0), new Float3(10, 10, 10));
            desc.Resolution = 512;
            desc.Refresh = ReflectionProbeRefresh.EveryFrame;
            desc.TimeSlicing = ReflectionProbeTimeSlicing.IndividualFaces;
            _world.AddReflectionProbe(desc);
        }

        // A 512 face is the whole budget, so each frame draws one face of one probe
        for (int frame = 0; frame < 4; frame++)
        {
            Frame();
            Assert.Single(_world.Reflections.Captures);
        }
    }

    [Fact]
    public void SkyCapturedEveryFrame_ReusesItsScratchCube()
    {
        _world.Environment = new EnvironmentDesc { Fog = default, AmbientColor = default, Sky = SkyMaterial(new Float3(0.1f, 0.2f, 0.4f), 0), SkyCapture = SkyCaptureMode.EveryFrame };
        for (int frame = 0; frame < 8; frame++)
            Frame();
        Assert.Equal(1, _world.Reflections.ScratchCount);
    }

    [Fact]
    public void BakingABakedProbeWithoutACubemap_ReadsItBack_AndDropsItsCaptureCube()
    {
        Room(new Double3(0, 0, 10), 20, _ => new Float3(0.8f, 0.5f, 0.2f));
        ReflectionProbeDesc desc = RoomProbe(new Double3(0, 0, 10), new Float3(40, 40, 40));
        desc.Mode = ReflectionProbeMode.Baked;
        ReflectionProbeHandle probe = _world.AddReflectionProbe(desc);
        Frame();

        BakedReflectionProbe? baked = null;
        _world.BakeReflectionProbe(probe, data => baked = data);
        for (int frame = 0; frame < 4 && baked == null; frame++)
            Frame();
        Assert.NotNull(baked);
        Assert.Equal(PixelFormat.R16_G16_B16_A16_Float, baked!.Format);
        Frame();
        Frame();
        Assert.False(_world.Reflections.HasTarget(probe.Index));
    }

    [Fact]
    public void ChangingAProbesResolution_RightAfterItsCapture_CapturesItAgain()
    {
        Room(new Double3(0, 0, 10), 20, _ => new Float3(0.8f, 0.5f, 0.2f));
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(_origin + new Double3(0, 0, QuadDepth)), LayerMask = 2 }, [Reflector()]);
        ReflectionProbeDesc desc = RoomProbe(new Double3(0, 0, 10), new Float3(40, 40, 40));
        ReflectionProbeHandle probe = _world.AddReflectionProbe(desc);
        Frame();

        desc.Resolution = 64;
        _world.SetReflectionProbe(probe, desc);
        Frame();
        Frame();
        AssertMatches(_ => new Vector3(0.8f, 0.5f, 0.2f), Frame());
    }

    [Fact]
    public void AProbeAddedWhereOneWasRemoved_CapturesItself()
    {
        Room(new Double3(0, 0, 10), 20, _ => new Float3(0.8f, 0.5f, 0.2f));
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(_origin + new Double3(0, 0, QuadDepth)), LayerMask = 2 }, [Reflector()]);
        ReflectionProbeDesc desc = RoomProbe(new Double3(0, 0, 10), new Float3(40, 40, 40));
        ReflectionProbeHandle first = _world.AddReflectionProbe(desc);
        Frame();
        AssertMatches(_ => new Vector3(0.8f, 0.5f, 0.2f), Frame());

        foreach (RenderableHandle wall in Enumerable.Range(0, 6).Select(i => new RenderableHandle(i, 1)))
            _world.SetLayerMask(wall, 4);
        _world.RemoveReflectionProbe(first);
        ReflectionProbeHandle second = _world.AddReflectionProbe(desc);
        Assert.Equal(first.Index, second.Index);
        Frame();
        Frame();
        AssertMatches(_ => s_skyColor, Frame());
    }

    [Fact]
    public void HostBuffers_ReachShadersByName_WorldOverRenderer_AndMissingOnesAreReported()
    {
        Material material = _renderer.CreateMaterial(_renderer.LoadShader(ShaderParser.Parse(BufferSource)));
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(_origin + new Double3(0, 0, QuadDepth)) }, [material]);
        DeviceBuffer Buffer(Float4 second)
        {
            DeviceBuffer buffer = _device.ResourceFactory.CreateBuffer(new BufferDescription(32, BufferUsage.StructuredBufferReadOnly));
            _device.UpdateBuffer<Float4>(buffer, 0, [new Float4(9, 9, 9, 1), second]);
            return buffer;
        }

        Float4[] Draw()
        {
            ViewDesc view = new() { World = _world, ViewId = 16, Target = _framebuffer };
            view.Layers[0] = new ViewLayer { View = Float4x4.Identity, Projection = s_projection, Position = _origin };
            _renderer.SubmitFrame(new FrameDesc { Views = new[] { view } });
            _device.WaitForIdle();
            _renderer.SubmitFrame(new FrameDesc { Views = new[] { view } });
            return ReadLayer(_target, 0);
        }

        Float4[] missing = Draw();
        RendererError error = Assert.Single(_errors);
        Assert.Equal("global.buffer.missing", error.Code);
        Assert.Equal(RendererErrorSeverity.Error, error.Severity);
        Assert.All(missing, p => Assert.Equal(0f, p.X));
        _errors.Clear();

        using DeviceBuffer renderer = Buffer(new Float4(0.25f, 0.5f, 0.75f, 1));
        using DeviceBuffer world = Buffer(new Float4(1, 0, 0.5f, 1));
        _renderer.Globals.SetBuffer("WindField", renderer);
        AssertMatches(_ => new Vector3(0.25f, 0.5f, 0.75f), Frame());

        _world.Globals.SetBuffer("WindField", world);
        AssertMatches(_ => new Vector3(1, 0, 0.5f), Frame());

        // A disposed buffer drops out, the renderer's showing through again
        world.Dispose();
        AssertMatches(_ => new Vector3(0.25f, 0.5f, 0.75f), Frame());
    }

    [Fact]
    public void OpaqueForwardPasses_WriteTheGBuffer_AndMustStoreIt()
    {
        ShaderPass pass = ShaderParser.Parse(Source).Passes![0];
        ShaderDescription lit = _compiler.Compile(pass, [], _device.BackendType);
        ShaderDescription unlit = _compiler.Compile(ShaderParser.Parse(Source.Replace("Graphite_WriteGBuffer(n, roughness, metallic);", "")).Passes![0], [], _device.BackendType);
        PassState opaque = new();
        PassState blended = ShaderParser.ParsePassState("Blend SourceAlpha InverseSourceAlpha");

        static ColorWriteMask[] Masks(BlendStateDescription blend) => blend.AttachmentStates.Select(a => a.ColorWriteMask).ToArray();
        Assert.Equal([ColorWriteMask.All, ColorWriteMask.All, ColorWriteMask.All, ColorWriteMask.All], Masks(ProgramCache.SceneBlend(opaque, forward: true, prepass: false)));
        Assert.Equal([ColorWriteMask.All, ColorWriteMask.None, ColorWriteMask.None, ColorWriteMask.None], Masks(ProgramCache.SceneBlend(blended, forward: true, prepass: false)));
        Assert.Equal([ColorWriteMask.All, ColorWriteMask.None, ColorWriteMask.None, ColorWriteMask.None], Masks(ProgramCache.SceneBlend(opaque, forward: false, prepass: false)));
        Assert.Equal([ColorWriteMask.None, ColorWriteMask.None, ColorWriteMask.None, ColorWriteMask.None], Masks(ProgramCache.SceneBlend(opaque, forward: false, prepass: true)));
        Assert.True(ProgramCache.StoresGBuffer(lit));
        Assert.False(ProgramCache.StoresGBuffer(unlit));

        // An opaque forward pass that never writes it does not load, and says why
        Material material = _renderer.CreateMaterial(_renderer.LoadShader(ShaderParser.Parse(Source.Replace("Graphite_WriteGBuffer(n, roughness, metallic);", ""))));
        AddQuad(material);
        ViewDesc view = new() { World = _world, ViewId = 1, Target = _framebuffer };
        view.Layers[0] = new ViewLayer { View = Float4x4.Identity, Projection = s_projection, Position = _origin };
        _renderer.SubmitFrame(new FrameDesc { Views = new[] { view } });
        _renderer.SubmitFrame(new FrameDesc { Views = new[] { view } });
        Assert.Equal("shader.gbuffer.missing", Assert.Single(_errors).Code);
        _errors.Clear();
    }

    // A quad whose normal leans with position, (0.2x, 0.2y, -1) before normalizing, with UVs running 0 to 1 across it
    private GpuMesh IndirectQuad(bool secondUVs)
    {
        Float3[] positions = [new(-4, -4, 0), new(4, -4, 0), new(4, 4, 0), new(-4, 4, 0)];
        Float3[] normals = [new(-0.8f, -0.8f, -1), new(0.8f, -0.8f, -1), new(0.8f, 0.8f, -1), new(-0.8f, 0.8f, -1)];
        Float2[] uvs = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];
        Float2[] junk = [new(9, 9), new(9, 9), new(9, 9), new(9, 9)];
        List<VertexStream> streams =
        [
            new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(positions.AsSpan()).ToArray() },
            new VertexStream { Semantic = VertexSemantic.Normal, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(normals.AsSpan()).ToArray() },
            new VertexStream { Semantic = VertexSemantic.UV0, Format = VertexElementFormat.Float2, Data = MemoryMarshal.AsBytes((secondUVs ? junk : uvs).AsSpan()).ToArray() },
        ];
        if (secondUVs)
            streams.Add(new VertexStream { Semantic = VertexSemantic.UV1, Format = VertexElementFormat.Float2, Data = MemoryMarshal.AsBytes(uvs.AsSpan()).ToArray() });
        return _renderer.CreateMesh(new MeshData { VertexCount = 4, Streams = [.. streams], Indices = new uint[] { 0, 1, 2, 0, 2, 3 } });
    }

    private static Vector3 IndirectNormal(Vector3 p) => Vector3.Normalize(new Vector3(0.2f * p.X, 0.2f * p.Y, -1));

    private RenderableHandle AddIndirect(GpuMesh mesh, double depth = QuadDepth, LightmapBinding? lightmap = null)
        => _world.Add(new RenderableDesc
        {
            Mesh = mesh,
            Transform = Double4x4.CreateTranslation(_origin + new Double3(0, 0, depth)),
            Lightmap = lightmap ?? LightmapBinding.None,
        }, [_renderer.CreateMaterial(_renderer.LoadShader(ShaderParser.Parse(IndirectSource)))]);

    private readonly List<Texture> _textures = new();

    private Texture Lightmap<T>(uint width, PixelFormat format, T[] texels) where T : unmanaged
    {
        Texture texture = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(width, 1, 1, 1, format, TextureUsage.Sampled));
        _device.UpdateTexture<T>(texture, texels, new TextureRegion(0, 0, 0, width, 1, 1, 0, 0));
        _textures.Add(texture);
        return texture;
    }

    [Fact]
    public void Lightmaps_LightThroughTheSecondUVSet_PlacedByTheirScaleAndOffset()
    {
        Texture gradient = Lightmap<Float4>(2, PixelFormat.R32_G32_B32_A32_Float, [new Float4(0, 0, 0, 1), new Float4(1, 0.5f, 0.25f, 1)]);
        AddIndirect(IndirectQuad(secondUVs: true), lightmap: new LightmapBinding(gradient, new Float4(0.5f, 1, 0.5f, 0), LightmapEncoding.Linear));

        // Bilinear between the two texel centres, clamped at the edge
        AssertMatches(p =>
        {
            float u = (p.X + 4) / 8 * 0.5f + 0.5f;
            return new Vector3(1, 0.5f, 0.25f) * Math.Clamp(u * 2 - 0.5f, 0, 1);
        }, Frame());
    }

    [Fact]
    public void Lightmaps_FallBackToTheFirstUVSet_AndDecodeRGBM()
    {
        byte[] texel = [128, 64, 255, 128];
        AddIndirect(IndirectQuad(secondUVs: false), lightmap: new LightmapBinding(Lightmap<byte>(1, PixelFormat.R8_G8_B8_A8_UNorm, texel), new Float4(1, 1, 0, 0), LightmapEncoding.RGBM));

        float scale = texel[3] / 255f * 8;
        AssertMatches(_ => new Vector3(texel[0], texel[1], texel[2]) / 255f * scale, Frame());
    }

    [Fact]
    public void WithoutBakedLight_AmbientLightsTheSurface_AtItsIntensity()
    {
        _world.Environment = new EnvironmentDesc { AmbientColor = new Float4(0.2f, 0.4f, 0.6f, 1), AmbientIntensity = 0.5f, Fog = default };
        AddIndirect(IndirectQuad(secondUVs: false));
        AssertMatches(_ => new Vector3(0.1f, 0.2f, 0.3f), Frame());
    }

    // Eight probes on the corners of a box split into six tetrahedra along its diagonal, after a flat one the search must skip
    private LightProbeData BoxProbes(Double3 min, Double3 max, int seed)
    {
        Random random = new(seed);
        Double3[] positions = new Double3[8];
        SphericalHarmonicsL2[] coefficients = new SphericalHarmonicsL2[8];
        Float3 Next(float scale) => new Float3(random.NextSingle() - 0.5f, random.NextSingle() - 0.5f, random.NextSingle() - 0.5f) * scale;
        for (int i = 0; i < 8; i++)
        {
            positions[i] = _origin + new Double3((i & 1) != 0 ? max.X : min.X, (i & 2) != 0 ? max.Y : min.Y, (i & 4) != 0 ? max.Z : min.Z);
            coefficients[i] = new SphericalHarmonicsL2
            {
                C0 = new Float3(1, 1, 1) + Next(0.5f), C1 = Next(0.4f), C2 = Next(0.4f), C3 = Next(0.4f),
                C4 = Next(0.2f), C5 = Next(0.2f), C6 = Next(0.2f), C7 = Next(0.2f), C8 = Next(0.2f),
            };
        }

        int[] tetrahedra = [0, 1, 2, 3, 0, 1, 3, 7, 0, 1, 5, 7, 0, 2, 3, 7, 0, 2, 6, 7, 0, 4, 5, 7, 0, 4, 6, 7];
        return new LightProbeData { Positions = positions, Coefficients = coefficients, Tetrahedra = tetrahedra, Neighbours = Reference.Neighbours(tetrahedra) };
    }

    private static readonly Double3 s_boxMin = new(-1.5, -2.5, 7), s_boxMax = new(2.5, 1.5, 13);

    [Fact]
    public void LightProbes_BlendTheTetrahedronAroundAMovingRenderable()
    {
        LightProbeData probes = BoxProbes(s_boxMin, s_boxMax, 3);
        _world.SetLightProbes(probes);
        RenderableHandle quad = AddIndirect(IndirectQuad(secondUVs: false), depth: 8);

        foreach (double depth in new[] { 8.0, 10.0, 12.0, 15.0, 9.0 })
        {
            _world.SetTransform(quad, Double4x4.CreateTranslation(_origin + new Double3(0, 0, depth)));
            SphericalHarmonicsL2 sh = Reference.SampleSH(probes, _origin + new Double3(0, 0, depth));
            AssertMatches(p => Reference.EvaluateSH(sh, IndirectNormal(p)), Frame());
        }
    }

    [Fact]
    public void LightProbes_FarFromTheOrigin_AndReplaced_StillMatch()
    {
        _origin = new Double3(-7_000_000.5, 120, 4_000_000.25);
        AddIndirect(IndirectQuad(secondUVs: false), depth: 10.5);
        _world.SetLightProbes(BoxProbes(s_boxMin, s_boxMax, 5));
        Frame();

        LightProbeData probes = BoxProbes(s_boxMin, s_boxMax, 6);
        _world.SetLightProbes(probes);
        SphericalHarmonicsL2 sh = Reference.SampleSH(probes, _origin + new Double3(0, 0, 10.5));
        AssertMatches(p => Reference.EvaluateSH(sh, IndirectNormal(p)), Frame());
    }

    [Fact]
    public void LightProbes_WithoutTetrahedra_BlendTheNearestFour()
    {
        LightProbeData probes = BoxProbes(new Double3(-3, -1, 6), new Double3(1, 2, 16), 8);
        probes.Tetrahedra = [];
        probes.Neighbours = [];
        _world.SetLightProbes(probes);
        AddIndirect(IndirectQuad(secondUVs: false));

        SphericalHarmonicsL2 sh = Reference.SampleSH(probes, _origin + new Double3(0, 0, QuadDepth));
        AssertMatches(p => Reference.EvaluateSH(sh, IndirectNormal(p)), Frame());
    }

    [Fact]
    public void ManyProbeLitRenderables_GrowAndReuseTheirSlots()
    {
        LightProbeData probes = BoxProbes(s_boxMin, s_boxMax, 12);
        _world.SetLightProbes(probes);
        GpuMesh mesh = IndirectQuad(secondUVs: false);
        List<RenderableHandle> hidden = new();
        for (int i = 0; i < 100; i++)
            hidden.Add(AddIndirect(mesh, depth: 50 + i * 0.1));
        Frame();

        foreach (RenderableHandle handle in hidden.Take(70))
            _world.Remove(handle);
        for (int i = 0; i < 80; i++)
            AddIndirect(mesh, depth: 60 + i * 0.1);
        AddIndirect(mesh, depth: 11);

        SphericalHarmonicsL2 sh = Reference.SampleSH(probes, _origin + new Double3(0, 0, 11));
        AssertMatches(p => Reference.EvaluateSH(sh, IndirectNormal(p)), Frame());
    }

    [Fact]
    public void Lightmaps_StillLoadingLightWithAmbient_EachCarryTheirEncoding_AndNoneUsesTheProbes()
    {
        _world.Environment = new EnvironmentDesc { AmbientColor = new Float4(0.2f, 0.4f, 0.6f, 1), Fog = default };
        LightProbeData probes = BoxProbes(s_boxMin, s_boxMax, 13);
        _world.SetLightProbes(probes);
        RenderableHandle quad = AddIndirect(IndirectQuad(secondUVs: true), lightmap: new LightmapBinding(null, new Float4(1, 1, 0, 0)));
        AssertMatches(_ => new Vector3(0.2f, 0.4f, 0.6f), Frame());

        _world.SetLightmap(quad, new LightmapBinding(Lightmap<Float4>(1, PixelFormat.R32_G32_B32_A32_Float, [new Float4(0.1f, 0.2f, 0.3f, 1)]), new Float4(1, 1, 0, 0), LightmapEncoding.Linear));
        AssertMatches(_ => new Vector3(0.1f, 0.2f, 0.3f), Frame());

        byte[] texel = [64, 128, 32, 64];
        _world.SetLightmap(quad, new LightmapBinding(Lightmap<byte>(1, PixelFormat.R8_G8_B8_A8_UNorm, texel), new Float4(1, 1, 0, 0), LightmapEncoding.RGBM, SamplerDescription.Point));
        float scale = texel[3] / 255f * 8;
        AssertMatches(_ => new Vector3(texel[0], texel[1], texel[2]) / 255f * scale, Frame());

        _world.SetLightmap(quad, LightmapBinding.None);
        SphericalHarmonicsL2 sh = Reference.SampleSH(probes, _origin + new Double3(0, 0, QuadDepth));
        AssertMatches(p => Reference.EvaluateSH(sh, IndirectNormal(p)), Frame());
    }

    [Fact]
    public void LightProbeData_WithCornersOrNeighboursOutOfRange_IsRefused()
    {
        LightProbeData corners = BoxProbes(s_boxMin, s_boxMax, 14);
        corners.Tetrahedra[5] = 8;
        Assert.Throws<ArgumentException>(() => _world.SetLightProbes(corners));

        LightProbeData neighbours = BoxProbes(s_boxMin, s_boxMax, 14);
        neighbours.Neighbours[3] = 7;
        Assert.Throws<ArgumentException>(() => _world.SetLightProbes(neighbours));
    }

    [Fact]
    public void LightmappedRenderables_IgnoreTheProbes()
    {
        _world.SetLightProbes(BoxProbes(s_boxMin, s_boxMax, 9));
        Texture lightmap = Lightmap<Float4>(1, PixelFormat.R32_G32_B32_A32_Float, [new Float4(0.3f, 0.6f, 0.9f, 1)]);
        RenderableHandle quad = AddIndirect(IndirectQuad(secondUVs: true));
        Frame();

        _world.SetLightmap(quad, new LightmapBinding(lightmap, new Float4(1, 1, 0, 0), LightmapEncoding.Linear));
        AssertMatches(_ => new Vector3(0.3f, 0.6f, 0.9f), Frame());
    }

    /// <summary>Prowl's lighting math on the CPU, the same expressions the shaders use.</summary>
    private static class Reference
    {
        private const float MinRoughness = 0.045f;

        private static float Ggx(Vector3 n, Vector3 h, float roughness)
        {
            roughness = MathF.Max(roughness, MinRoughness);
            float a2 = roughness * roughness;
            float nh = MathF.Max(Vector3.Dot(n, h), 0);
            float d = nh * nh * (a2 - 1) + 1;
            return a2 / (MathF.PI * d * d);
        }

        private static float SchlickGgx(float nv, float roughness)
        {
            float r = roughness + 1;
            float k = r * r / 8;
            return nv / (nv * (1 - k) + k);
        }

        private static float Smith(Vector3 n, Vector3 v, Vector3 l, float roughness)
            => SchlickGgx(MathF.Max(Vector3.Dot(n, l), 0), roughness) * SchlickGgx(MathF.Abs(Vector3.Dot(n, v)), roughness);

        private static Vector3 Fresnel(float cos, Vector3 f0) => f0 + (Vector3.One - f0) * MathF.Pow(2, -9.28f * cos);

        private static float Disney(float nv, float nl, float lh, float roughness)
        {
            float fd90 = 0.5f + 2 * lh * lh * roughness;
            return (1 + (fd90 - 1) * MathF.Pow(2, -8.35f * nl)) * (1 + (fd90 - 1) * MathF.Pow(2, -8.35f * nv)) / MathF.PI;
        }

        private static Vector3 Brdf(Vector3 l, Vector3 v, Vector3 n, Vector3 albedo, float metallic, float roughness, float nl)
        {
            Vector3 f0 = Vector3.Lerp(new Vector3(0.04f), albedo, metallic);
            Vector3 h = Vector3.Normalize(l + v);
            float nv = MathF.Abs(Vector3.Dot(n, v));
            float lh = MathF.Max(Vector3.Dot(l, h), 0);
            Vector3 f = Fresnel(lh, f0);
            Vector3 kd = (Vector3.One - f) * (1 - metallic);
            Vector3 specular = Ggx(n, h, roughness) * Smith(n, v, l, roughness) * f / (4 * nv * nl + 0.0001f);
            return kd * albedo * Disney(nv, nl, lh, roughness) + specular;
        }

        public static Vector3 Local(in LightDesc light, Vector3 p, Vector3 n, Vector3 albedo, float metallic, float roughness)
        {
            Vector3 position = new((float)light.Position.X, (float)light.Position.Y, (float)light.Position.Z);
            Vector3 toPixel = p - position;
            float dist2 = toPixel.LengthSquared();
            if (dist2 > light.Range * light.Range)
                return Vector3.Zero;

            float dist = MathF.Sqrt(dist2);
            Vector3 l = -toPixel / MathF.Max(dist, 1e-6f);
            float axisCos = 0;
            if (light.Type == LightType.Spot)
            {
                axisCos = Vector3.Dot(Vector3.Normalize(new Vector3(light.Direction.X, light.Direction.Y, light.Direction.Z)), -l);
                if (axisCos <= MathF.Cos(light.OuterAngle * MathF.PI / 180))
                    return Vector3.Zero;
            }

            float nl = Vector3.Dot(n, l);
            if (nl <= 0)
                return Vector3.Zero;

            float factor = dist2 / MathF.Max(light.Range * light.Range, 1e-6f);
            float window = Math.Clamp(1 - factor * factor, 0, 1);
            float attenuation = window * window / MathF.Max(dist2, 0.01f);
            if (light.Type == LightType.Spot)
            {
                float outer = MathF.Cos(light.OuterAngle * MathF.PI / 180), inner = MathF.Cos(light.InnerAngle * MathF.PI / 180);
                float t = Math.Clamp((axisCos - outer) / (inner - outer), 0, 1);
                attenuation *= t * t * (3 - 2 * t);
            }
            if (attenuation <= 0.0001f)
                return Vector3.Zero;

            Vector3 radiance = new Vector3(light.Color.X, light.Color.Y, light.Color.Z) * (light.Intensity * 8) * attenuation;
            return Brdf(l, Vector3.Normalize(-p), n, albedo, metallic, roughness, nl) * radiance * nl;
        }

        // Graphite's procedural sky as drawn, at its defaults unless given
        public static Vector3 ProceduralSky(Vector3 view, Vector3 sun, float sunSize = 1, float sunGlow = 1, float exposure = 1, Vector3? tint = null, Vector3? ground = null)
        {
            static Vector3 Exp(Vector3 v) => new(MathF.Exp(v.X), MathF.Exp(v.Y), MathF.Exp(v.Z));
            static float Smooth(float a, float b, float x) { float t = Math.Clamp((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t); }

            float up = MathF.Max(sun.Y, 0), dep = MathF.Max(-sun.Y, 0) * 28.2f;
            float mu = MathF.Max(view.Y, 0), nu = Vector3.Dot(view, sun);
            float sunAir = 1.0279f / (up + 0.027943f);
            float viewAir = 1.0279f / (mu + 0.027943f);
            Vector3 sunT = Exp(-new Vector3(0.088977f, 0.16327f, 0.31751f) * sunAir);
            Vector3 viewT = Exp(-new Vector3(0.046745f, 0.097582f, 0.24505f) * viewAir);
            Vector3 h = new Vector3(0.026626f, 0.055859f, 0.13174f) * (viewAir - sunAir);
            Vector3 hh = Vector3.One + 0.8839f * h * h;
            Vector3 lit = 0.5f * (sunT + viewT) * new Vector3(1 / MathF.Sqrt(hh.X), 1 / MathF.Sqrt(hh.Y), 1 / MathF.Sqrt(hh.Z));
            Vector3 ozone = Exp(-new Vector3(-0.0026066f, 0.01647f, 0.0064102f) * sunAir);
            float spread = 1 / MathF.Sqrt(1.6156f - 1.5692f * nu);
            float glow = 0.00010368f * sunGlow * spread * spread * spread;
            Vector3 single = ozone * lit * viewAir * (new Vector3(0.001631f, 0.0038803f, 0.010984f) * (1 + nu * nu) + new Vector3(glow));
            Vector3 bounce = new Vector3(0.031648f, 0.033978f, 0.03542f) * (up + 0.022059f) * Exp(-(new Vector3(1.8692f) + new Vector3(-0.0023863f, 0.020262f, 0.05909f)) * dep);
            Vector3 color = (single + bounce * (Vector3.One - viewT)) * 1.7557f * (tint ?? Vector3.One);

            float sunTransmittance = MathF.Exp(-Vector3.Dot(new Vector3(0.33f), new Vector3(5.802e-6f, 13.558e-6f, 33.1e-6f)) * 8000 / MathF.Max(view.Y, 0.02f));
            float disk = sunSize > 0 ? Smooth(1 - 1.5e-4f * sunSize * sunSize, 1 - 1e-5f * sunSize * sunSize, Vector3.Dot(view, sun)) * (view.Y >= 0 ? 1 : 0) : 0;
            color += disk * 2000 * sunTransmittance * new Vector3(1, 0.9f, 0.8f);
            color = Vector3.Lerp(color, color * (ground ?? new Vector3(0.25f)), Smooth(0, -0.02f, view.Y));

            Vector3 c = color * (40 * exposure);
            float l = Vector3.Dot(c, new Vector3(0.2126f, 0.7152f, 0.0722f));
            Vector3 tc = c / (c + Vector3.One);
            return Vector3.Lerp(c / (l + 1), tc, tc);
        }

        // Prowl's light probe sampler: the tetrahedron holding the point by barycentrics, else the nearest four by inverse squared distance
        public static SphericalHarmonicsL2 SampleSH(LightProbeData data, Double3 point)
        {
            for (int t = 0; t < data.Tetrahedra.Length / 4; t++)
            {
                int[] c = data.Tetrahedra.AsSpan(t * 4, 4).ToArray();
                Double3 a = data.Positions[c[0]];
                Double3 v1 = data.Positions[c[1]] - a, v2 = data.Positions[c[2]] - a, v3 = data.Positions[c[3]] - a, vp = point - a;
                double d = Det(v1, v2, v3);
                if (Math.Abs(d) < 1e-12)
                    continue;
                double b1 = Det(vp, v2, v3) / d, b2 = Det(v1, vp, v3) / d, b3 = Det(v1, v2, vp) / d, b0 = 1 - b1 - b2 - b3;
                if (b0 >= -1e-4 && b1 >= -1e-4 && b2 >= -1e-4 && b3 >= -1e-4)
                    return Blend(data, c, [(float)b0, (float)b1, (float)b2, (float)b3]);
            }

            int[] nearest = Enumerable.Range(0, data.Positions.Length).OrderBy(i => Length2(data.Positions[i] - point)).Take(4).ToArray();
            float[] weights = nearest.Select(i => 1f / ((float)Length2(data.Positions[i] - point) + 1e-6f)).ToArray();
            float total = weights.Sum();
            return Blend(data, nearest, weights.Select(w => w / total).ToArray());
        }

        private static double Length2(Double3 v) => v.X * v.X + v.Y * v.Y + v.Z * v.Z;

        private static double Det(Double3 a, Double3 b, Double3 c)
            => a.X * (b.Y * c.Z - b.Z * c.Y) - a.Y * (b.X * c.Z - b.Z * c.X) + a.Z * (b.X * c.Y - b.Y * c.X);

        private static SphericalHarmonicsL2 Blend(LightProbeData data, int[] probes, float[] weights)
        {
            SphericalHarmonicsL2 r = default;
            for (int i = 0; i < probes.Length; i++)
            {
                SphericalHarmonicsL2 p = data.Coefficients[probes[i]];
                float w = weights[i];
                r.C0 += p.C0 * w; r.C1 += p.C1 * w; r.C2 += p.C2 * w; r.C3 += p.C3 * w; r.C4 += p.C4 * w;
                r.C5 += p.C5 * w; r.C6 += p.C6 * w; r.C7 += p.C7 * w; r.C8 += p.C8 * w;
            }
            return r;
        }

        // Prowl's irradiance over pi from raw coefficients
        public static Vector3 EvaluateSH(in SphericalHarmonicsL2 sh, Vector3 n)
        {
            const float K0 = 0.2820947918f, K1 = 0.4886025119f, K2a = 1.0925484306f, K2b = 0.3153915652f, K2c = 0.5462742153f;
            const float A1 = 2f / 3f, A2 = 0.25f;
            static Vector3 V(Float3 f) => new(f.X, f.Y, f.Z);
            Vector3 e = V(sh.C0) * K0
                + V(sh.C1) * (A1 * K1 * n.Y) + V(sh.C2) * (A1 * K1 * n.Z) + V(sh.C3) * (A1 * K1 * n.X)
                + V(sh.C4) * (A2 * K2a * n.X * n.Y) + V(sh.C5) * (A2 * K2a * n.Y * n.Z)
                + V(sh.C6) * (A2 * K2b * (3 * n.Z * n.Z - 1))
                + V(sh.C7) * (A2 * K2a * n.X * n.Z) + V(sh.C8) * (A2 * K2c * (n.X * n.X - n.Y * n.Y));
            return Vector3.Max(e, Vector3.Zero);
        }

        // The tetrahedron across the face opposite each corner, -1 on the hull
        public static int[] Neighbours(int[] tetrahedra)
        {
            int count = tetrahedra.Length / 4;
            int[] neighbours = Enumerable.Repeat(-1, tetrahedra.Length).ToArray();
            for (int t = 0; t < count; t++)
            {
                for (int k = 0; k < 4; k++)
                {
                    int[] face = Enumerable.Range(0, 4).Where(i => i != k).Select(i => tetrahedra[t * 4 + i]).ToArray();
                    for (int o = 0; o < count; o++)
                    {
                        if (o != t && face.All(tetrahedra.AsSpan(o * 4, 4).ToArray().Contains))
                            neighbours[t * 4 + k] = o;
                    }
                }
            }
            return neighbours;
        }

        // The specular a light of unit intensity reflects toward v, its shape integrated by brute force, against the most
        // representative point's estimate. Fresnel is left at one. Returns (true, estimate).
        public static (float Truth, float Estimate) Specular(LightType type, Vector3 center, Vector3 axis, Vector3 up, float radius, Vector2 size, Vector3 v, Vector3 n, float roughness)
        {
            static float Lobe(Vector3 l, Vector3 v, Vector3 n, float roughness)
            {
                float nl = Vector3.Dot(n, l);
                if (nl <= 0)
                    return 0;
                Vector3 h = Vector3.Normalize(l + v);
                return Ggx(n, h, roughness) * Smith(n, v, l, roughness) / (4 * MathF.Abs(Vector3.Dot(n, v)) * nl + 0.0001f) * nl;
            }

            Vector3 surface = -v * 5;
            Vector3 at = surface + center;
            LightDesc light = new() { Type = type, Position = new Double3(at.X, at.Y, at.Z), Direction = new Float3(axis.X, axis.Y, axis.Z), Up = new Float3(up.X, up.Y, up.Z),
                SourceRadius = radius, Size = new Float2(size.X, size.Y), Range = 1e6f, Intensity = 1 / 8f, Color = new Float3(1, 1, 1), DiffuseScale = 0 };
            float estimate = Area(light, surface, n, Vector3.One, 1, roughness).X;

            const int steps = 300;
            double truth = 0;
            Vector3 right = Vector3.Cross(up, axis);
            for (int i = 0; i < steps; i++)
            {
                for (int j = 0; j < steps; j++)
                {
                    float u = (i + 0.5f) / steps, w = (j + 0.5f) / steps;
                    if (type == LightType.Point)
                    {
                        // Uniform over the cone the sphere fills, radiance 1 over its projected area
                        float d = center.Length();
                        float cosMax = MathF.Sqrt(MathF.Max(1 - radius * radius / (d * d), 0));
                        float cos = 1 - u * (1 - cosMax), sin = MathF.Sqrt(1 - cos * cos), a = 2 * MathF.PI * w;
                        Vector3 c = center / d;
                        Vector3 t = Vector3.Normalize(Vector3.Cross(c, MathF.Abs(c.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX)), b = Vector3.Cross(c, t);
                        Vector3 l = c * cos + t * (sin * MathF.Cos(a)) + b * (sin * MathF.Sin(a));
                        truth += Lobe(l, v, n, roughness) * 2 * MathF.PI * (1 - cosMax) / (steps * steps) / (MathF.PI * radius * radius);
                    }
                    else
                    {
                        // Over the surface, radiance 1 over its area, seen with its cosine
                        Vector3 q;
                        float area;
                        if (type == LightType.Rect)
                        {
                            q = center + right * ((u - 0.5f) * size.X) + up * ((w - 0.5f) * size.Y);
                            area = size.X * size.Y;
                        }
                        else
                        {
                            float rr = size.X * MathF.Sqrt(u), a = 2 * MathF.PI * w;
                            q = center + right * (rr * MathF.Cos(a)) + up * (rr * MathF.Sin(a));
                            area = MathF.PI * size.X * size.X;
                        }
                        float d2 = q.LengthSquared();
                        Vector3 l = q / MathF.Sqrt(d2);
                        truth += Lobe(l, v, n, roughness) * MathF.Max(Vector3.Dot(-l, axis), 0) / d2 * area / (steps * steps) / area;
                    }
                }
            }
            return ((float)truth, estimate);
        }

        public static Vector3 Directional(in LightDesc light, Vector3 p, Vector3 n, Vector3 albedo, float metallic, float roughness)
        {
            Vector3 l = Vector3.Normalize(-new Vector3(light.Direction.X, light.Direction.Y, light.Direction.Z));
            float nl = MathF.Max(Vector3.Dot(n, l), 0);
            Vector3 radiance = new Vector3(light.Color.X, light.Color.Y, light.Color.Z) * light.Intensity * 8;
            if (light.AngularRadius <= 0)
                return Brdf(l, Vector3.Normalize(-p), n, albedo, metallic, roughness, nl) * radiance * nl;

            // The sun's disk: specular from the nearest direction inside it, its lobe widened by the disk
            float radius = light.AngularRadius * MathF.PI / 180;
            float sin = MathF.Sin(radius), cos = MathF.Cos(radius);
            Vector3 v = Vector3.Normalize(-p);
            Vector3 r = Vector3.Reflect(-v, n);
            float rl = Vector3.Dot(r, l);
            Vector3 across = r - l * rl;
            Vector3 ls = rl >= cos ? r : across.LengthSquared() > 1e-8f ? Vector3.Normalize(l * cos + Vector3.Normalize(across) * sin) : l;
            float specular = MathF.Max(Vector3.Dot(n, ls), 0) * AreaEnergy(roughness, 0.5f * sin, 0.5f * sin);
            return Shade(l, nl, ls, specular, v, n, albedo, metallic, roughness) * radiance;
        }

        // Prowl's BRDF with the diffuse and specular lobes each from their own direction
        private static Vector3 Shade(Vector3 ld, float diffuse, Vector3 ls, float specular, Vector3 v, Vector3 n, Vector3 albedo, float metallic, float roughness)
        {
            Vector3 f0 = Vector3.Lerp(new Vector3(0.04f), albedo, metallic);
            float nv = MathF.Abs(Vector3.Dot(n, v));
            Vector3 hs = Vector3.Normalize(ls + v);
            float nls = MathF.Max(Vector3.Dot(n, ls), 0);
            Vector3 f = Fresnel(MathF.Max(Vector3.Dot(ls, hs), 0), f0);
            Vector3 spec = Ggx(n, hs, roughness) * Smith(n, v, ls, roughness) * f / (4 * nv * nls + 0.0001f);
            Vector3 hd = Vector3.Normalize(ld + v);
            Vector3 kd = (Vector3.One - f) * (1 - metallic);
            Vector3 diff = kd * albedo * Disney(nv, MathF.Max(Vector3.Dot(n, ld), 0), MathF.Max(Vector3.Dot(ld, hd), 0), roughness);
            return diff * diffuse + spec * specular;
        }

        private static float Window(float dist2, float range)
        {
            float factor = dist2 / MathF.Max(range * range, 1e-6f);
            float window = Math.Clamp(1 - factor * factor, 0, 1);
            return window * window;
        }

        public static float AreaEnergy(float roughness, float ex, float ey)
        {
            float a = MathF.Max(roughness, MinRoughness);
            return a * a / (MathF.Min(MathF.Sqrt(a * a + ex * ex), 1) * MathF.Min(MathF.Sqrt(a * a + ey * ey), 1));
        }

        // The cosine weighted solid angle of a sphere, or of a disk facing the point, clipped by the horizon
        public static float SphereIrradiance(float cosTheta, float sinSigma2)
        {
            if (sinSigma2 >= 1)
                return MathF.PI;
            if (cosTheta * cosTheta > sinSigma2)
                return MathF.PI * sinSigma2 * MathF.Max(cosTheta, 0);
            float sinTheta = MathF.Sqrt(MathF.Max(1 - cosTheta * cosTheta, 0));
            float x = MathF.Sqrt(MathF.Max(1 / sinSigma2 - 1, 0));
            float y = Math.Clamp(-x * cosTheta / MathF.Max(sinTheta, 1e-6f), -1, 1);
            float sinThetaSqrtY = sinTheta * MathF.Sqrt(MathF.Max(1 - y * y, 0));
            return MathF.Max((cosTheta * MathF.Acos(y) - x * sinThetaSqrtY) * sinSigma2 + MathF.Atan2(sinThetaSqrtY, MathF.Max(x, 1e-6f)), 0);
        }

        // The cosine weighted solid angle of a quad of corners relative to the point, clipped by the horizon
        public static float QuadIrradiance(Vector3[] q, Vector3 n)
        {
            List<Vector3> v = new();
            for (int i = 0; i < 4; i++)
            {
                Vector3 a = q[i], b = q[(i + 1) & 3];
                float da = Vector3.Dot(a, n), db = Vector3.Dot(b, n);
                if (da >= 0)
                    v.Add(a);
                if ((da >= 0) != (db >= 0))
                    v.Add(a + (b - a) * (da / (da - db)));
            }
            if (v.Count < 3)
                return 0;
            float sum = 0;
            for (int i = 0; i < v.Count; i++)
            {
                Vector3 a = Vector3.Normalize(v[i]), b = Vector3.Normalize(v[(i + 1) % v.Count]);
                Vector3 axb = Vector3.Cross(a, b);
                float s = axb.Length();
                if (s > 1e-12f)
                    sum += MathF.Atan2(s, Vector3.Dot(a, b)) * Vector3.Dot(axb, n) / s;
            }
            return 0.5f * MathF.Abs(sum);
        }

        // The irradiance per unit intensity of a line from a to b relative to the point, its intensity spread along it
        public static float LineIrradiance(Vector3 a, Vector3 b, Vector3 n)
        {
            Vector3 d = b - a;
            float len = d.Length();
            if (len < 1e-3f)
            {
                Vector3 m = 0.5f * (a + b);
                float m2 = MathF.Max(m.LengthSquared(), 1e-4f);
                return MathF.Max(Vector3.Dot(n, m), 0) / (m2 * MathF.Sqrt(m2));
            }
            Vector3 u = d / len;
            float na = Vector3.Dot(n, a), nu = Vector3.Dot(n, u);
            float s0 = 0, s1 = len;
            if (MathF.Abs(nu) > 1e-6f)
            {
                float horizon = -na / nu;
                if (nu > 0)
                    s0 = MathF.Max(s0, horizon);
                else
                    s1 = MathF.Min(s1, horizon);
            }
            else if (na < 0)
            {
                return 0;
            }
            if (s1 <= s0)
                return 0;
            float bu = Vector3.Dot(a, u);
            Vector3 perp = a - u * bu;
            float dd = MathF.Max(perp.LengthSquared(), 1e-20f);
            float np = Vector3.Dot(n, perp);
            float q0 = MathF.Max((a + u * s0).Length(), 1e-6f), q1 = MathF.Max((a + u * s1).Length(), 1e-6f);
            float t0 = s0 + bu, t1 = s1 + bu;
            float spread = t0 * t1 > 0
                ? (t1 * t1 - t0 * t0) / (q0 * q1 * (t1 * q0 + t0 * q1))
                : (t1 / q1 - t0 / q0) / dd;
            return MathF.Max(np * spread - nu / q1 + nu / q0, 0) / len;
        }

        private static Vector3 SpherePoint(Vector3 center, float radius, Vector3 r)
        {
            Vector3 toRay = Vector3.Dot(center, r) * r - center;
            return center + toRay * Math.Clamp(radius / MathF.Max(toRay.Length(), 1e-6f), 0, 1);
        }

        private static Vector3 PlanePoint(Vector3 center, Vector3 n, Vector3 right, Vector3 up, Vector2 half, bool disk, Vector3 r)
        {
            float facing = Vector3.Dot(r, n);
            Vector3 hit = facing < -1e-4f ? r * (Vector3.Dot(center, n) / facing) : center + (r - n * facing) * 1e4f;
            Vector3 local = hit - center;
            Vector2 p = new(Vector3.Dot(local, right), Vector3.Dot(local, up));
            if (disk)
                p *= MathF.Min(1, half.X / MathF.Max(p.Length(), 1e-6f));
            else
                p = Vector2.Clamp(p, -half, half);
            return center + right * p.X + up * p.Y;
        }

        private static Vector3 V(Float3 f) => new(f.X, f.Y, f.Z);

        // Spheres, tubes, rects and disks: diffuse from their irradiance, specular from the most representative point
        public static Vector3 Area(in LightDesc light, Vector3 p, Vector3 n, Vector3 albedo, float metallic, float roughness)
        {
            Vector3 toLight = new Vector3((float)light.Position.X, (float)light.Position.Y, (float)light.Position.Z) - p;
            float dist2 = toLight.LengthSquared();
            if (dist2 > light.Range * light.Range)
                return Vector3.Zero;
            float dist = MathF.Sqrt(dist2);
            Vector3 lc = toLight / MathF.Max(dist, 1e-6f);
            float window = Window(dist2, light.Range);
            Vector3 v = Vector3.Normalize(-p);
            Vector3 r = Vector3.Reflect(-v, n);
            Vector3 dir = Vector3.Normalize(V(light.Direction));
            Vector3 up = Vector3.Normalize(V(light.Up) - dir * Vector3.Dot(V(light.Up), dir));
            Vector3 ld = lc, ls = lc;
            float diffuse, specular;

            if (light.Type is LightType.Point or LightType.Spot)
            {
                float cone = 1;
                if (light.Type == LightType.Spot)
                {
                    float outer = MathF.Cos(light.OuterAngle * MathF.PI / 180), inner = MathF.Max(MathF.Cos(light.InnerAngle * MathF.PI / 180), outer + 1e-4f);
                    float axisCos = Vector3.Dot(dir, -lc);
                    if (axisCos <= outer)
                        return Vector3.Zero;
                    float t = Math.Clamp((axisCos - outer) / (inner - outer), 0, 1);
                    cone = t * t * (3 - 2 * t);
                }
                float rad = light.SourceRadius;
                float attenuation = window / MathF.Max(dist2, 0.01f) * cone;
                diffuse = SphereIrradiance(Vector3.Dot(n, lc), MathF.Min(rad * rad / dist2, 1)) / (MathF.PI * rad * rad) * window * cone;
                ls = Vector3.Normalize(SpherePoint(toLight, rad, r));
                specular = MathF.Max(Vector3.Dot(n, ls), 0) * attenuation * AreaEnergy(roughness, rad / (2 * dist), rad / (2 * dist));
            }
            else if (light.Type == LightType.Tube)
            {
                Vector3 axis = dir * (0.5f * light.Size.X);
                Vector3 a = toLight - axis, b = toLight + axis, ab = b - a;
                float t = Math.Clamp(-Vector3.Dot(a, ab) / MathF.Max(ab.LengthSquared(), 1e-8f), 0, 1);
                ld = Vector3.Normalize(a + ab * t);
                diffuse = LineIrradiance(a, b, n) * window;
                float rab = Vector3.Dot(r, ab);
                float tr = Math.Clamp((Vector3.Dot(r, a) * rab - Vector3.Dot(a, ab)) / MathF.Max(ab.LengthSquared() - rab * rab, 1e-8f), 0, 1);
                Vector3 onLine = a + ab * tr;
                ls = Vector3.Normalize(SpherePoint(onLine, light.SourceRadius, r));
                float lineDist = MathF.Max(onLine.Length(), 1e-4f);
                specular = MathF.Max(Vector3.Dot(n, ls), 0) * window / MathF.Max(onLine.LengthSquared(), 0.01f)
                    * AreaEnergy(roughness, light.SourceRadius / (2 * lineDist), light.Size.X / (MathF.PI * lineDist));
            }
            else
            {
                float front = Vector3.Dot(-lc, dir);
                if (front <= 0)
                    return Vector3.Zero;
                Vector3 right = Vector3.Cross(up, dir);
                bool disk = light.Type == LightType.Disk;
                Vector2 half = disk ? new Vector2(light.Size.X) : 0.5f * new Vector2(light.Size.X, light.Size.Y);
                float area = disk ? MathF.PI * half.X * half.X : light.Size.X * light.Size.Y;
                if (disk)
                {
                    float r2 = half.X * half.X;
                    diffuse = SphereIrradiance(Vector3.Dot(n, lc), r2 / (r2 + dist2)) * front / area * window;
                }
                else
                {
                    Vector3 hx = right * half.X, hy = up * half.Y;
                    diffuse = QuadIrradiance([toLight - hx - hy, toLight + hx - hy, toLight + hx + hy, toLight - hx + hy], n) / area * window;
                }
                Vector3 q = PlanePoint(toLight, dir, right, up, half, disk, r);
                float qDist2 = MathF.Max(q.LengthSquared(), 1e-8f), qDist = MathF.Sqrt(qDist2);
                ls = q / qDist;
                float qFront = MathF.Max(Vector3.Dot(-ls, dir), 0);
                Vector2 extent = disk ? new Vector2(half.X / (2 * qDist)) : new Vector2(light.Size.X, light.Size.Y) / (2 * MathF.Sqrt(MathF.PI) * qDist);
                specular = MathF.Max(Vector3.Dot(n, ls), 0) * qFront * window / MathF.Max(qDist2, 0.01f) * AreaEnergy(roughness, extent.X, extent.Y);
            }

            Vector3 radiance = V(light.Color) * (light.Intensity * 8);
            return Shade(ld, diffuse * light.DiffuseScale, ls, specular * light.SpecularScale, v, n, albedo, metallic, roughness) * radiance;
        }
    }
}
