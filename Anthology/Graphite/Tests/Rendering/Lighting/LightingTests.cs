using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;
using Prowl.Vector;

using Xunit;

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
                    #include "Graphite/Lighting.glsl"
                    layout(location = 0) in vec3 vNormal;
                    layout(location = 1) in vec2 vLightmapUV;
                    layout(location = 0) out vec4 outColor;
                    void main()
                    {
                        outColor = vec4(Graphite_IndirectDiffuse(normalize(vNormal), vLightmapUV), 1.0);
                    }
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

    [Fact]
    public void OnlyOpaquePassesWritingTheGBuffer_WriteIt()
    {
        ShaderPass pass = ShaderParser.Parse(Source).Passes![0];
        ShaderDescription lit = _compiler.Compile(pass, [], _device.BackendType);
        ShaderDescription unlit = _compiler.Compile(ShaderParser.Parse(Source.Replace("Graphite_WriteGBuffer(n, roughness, metallic);", "")).Passes![0], [], _device.BackendType);
        PassState opaque = new();
        PassState blended = ShaderParser.ParsePassState("Blend SourceAlpha InverseSourceAlpha");

        static ColorWriteMask[] Masks(BlendStateDescription blend) => blend.AttachmentStates.Select(a => a.ColorWriteMask).ToArray();
        Assert.Equal([ColorWriteMask.All, ColorWriteMask.All, ColorWriteMask.All], Masks(ProgramCache.SceneBlend(lit, opaque, prepass: false)));
        Assert.Equal([ColorWriteMask.All, ColorWriteMask.None, ColorWriteMask.None], Masks(ProgramCache.SceneBlend(unlit, opaque, prepass: false)));
        Assert.Equal([ColorWriteMask.All, ColorWriteMask.None, ColorWriteMask.None], Masks(ProgramCache.SceneBlend(lit, blended, prepass: false)));
        Assert.Equal([ColorWriteMask.None, ColorWriteMask.None, ColorWriteMask.None], Masks(ProgramCache.SceneBlend(lit, opaque, prepass: true)));
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
    public void Lightmaps_LightThroughTheSecondUVSet_PlacedOnTheirPage()
    {
        Texture other = Lightmap<Float4>(1, PixelFormat.R32_G32_B32_A32_Float, [new Float4(5, 5, 5, 1)]);
        Texture gradient = Lightmap<Float4>(2, PixelFormat.R32_G32_B32_A32_Float, [new Float4(0, 0, 0, 1), new Float4(1, 0.5f, 0.25f, 1)]);
        _world.SetLightmaps([other, gradient], LightmapEncoding.Linear);
        AddIndirect(IndirectQuad(secondUVs: true), lightmap: new LightmapBinding(1, new Float4(0.5f, 1, 0.5f, 0)));

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
        _world.SetLightmaps([Lightmap<byte>(1, PixelFormat.R8_G8_B8_A8_UNorm, texel)], LightmapEncoding.RGBM);
        AddIndirect(IndirectQuad(secondUVs: false), lightmap: new LightmapBinding(0, new Float4(1, 1, 0, 0)));

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
    public void LightmapPages_NotReadyLightWithAmbient_AndPastTheEndUseTheProbes()
    {
        _world.Environment = new EnvironmentDesc { AmbientColor = new Float4(0.2f, 0.4f, 0.6f, 1), Fog = default };
        LightProbeData probes = BoxProbes(s_boxMin, s_boxMax, 13);
        _world.SetLightProbes(probes);
        _world.SetLightmaps([null], LightmapEncoding.Linear);
        RenderableHandle quad = AddIndirect(IndirectQuad(secondUVs: true), lightmap: new LightmapBinding(0, new Float4(1, 1, 0, 0)));
        AssertMatches(_ => new Vector3(0.2f, 0.4f, 0.6f), Frame());

        _world.SetLightmap(quad, new LightmapBinding(3, new Float4(1, 1, 0, 0)));
        SphericalHarmonicsL2 sh = Reference.SampleSH(probes, _origin + new Double3(0, 0, QuadDepth));
        AssertMatches(p => Reference.EvaluateSH(sh, IndirectNormal(p)), Frame());

        _world.SetLightmaps([Lightmap<Float4>(1, PixelFormat.R32_G32_B32_A32_Float, [new Float4(0.1f, 0.2f, 0.3f, 1)])], LightmapEncoding.Linear);
        _world.SetLightmap(quad, new LightmapBinding(0, new Float4(1, 1, 0, 0)));
        AssertMatches(_ => new Vector3(0.1f, 0.2f, 0.3f), Frame());

        _world.SetLightmap(quad, LightmapBinding.None);
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
        _world.SetLightmaps([Lightmap<Float4>(1, PixelFormat.R32_G32_B32_A32_Float, [new Float4(0.3f, 0.6f, 0.9f, 1)])], LightmapEncoding.Linear);
        RenderableHandle quad = AddIndirect(IndirectQuad(secondUVs: true));
        Frame();

        _world.SetLightmap(quad, new LightmapBinding(0, new Float4(1, 1, 0, 0)));
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

        public static Vector3 Directional(in LightDesc light, Vector3 p, Vector3 n, Vector3 albedo, float metallic, float roughness)
        {
            Vector3 l = Vector3.Normalize(-new Vector3(light.Direction.X, light.Direction.Y, light.Direction.Z));
            float nl = MathF.Max(Vector3.Dot(n, l), 0);
            Vector3 radiance = new Vector3(light.Color.X, light.Color.Y, light.Color.Z) * light.Intensity * 8;
            return Brdf(l, Vector3.Normalize(-p), n, albedo, metallic, roughness, nl) * radiance * nl;
        }
    }
}
