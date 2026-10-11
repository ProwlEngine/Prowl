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
        _world.Environment = new EnvironmentDesc { AmbientColor = default };
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
    public void Ambient_UniformAndHemisphere()
    {
        AddQuad(Surface(new Float3(0.5f, 1, 0.25f)));
        _world.Environment = new EnvironmentDesc { Ambient = AmbientMode.Uniform, AmbientColor = new Float4(0.2f, 0.4f, 0.8f, 1) };
        AssertMatches(_ => new Vector3(0.1f, 0.4f, 0.2f), Frame());

        _world.Environment = new EnvironmentDesc { Ambient = AmbientMode.Hemisphere, AmbientSky = new Float4(1, 0, 0, 1), AmbientGround = new Float4(0, 0, 1, 1) };
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
        Transform = Double4x4.CreateTranslation(_origin + center) * Double4x4.CreateScale(size),
        Color = color,
        Priority = priority,
        Static = isStatic,
        LayerMask = layer,
    };

    private void LitByAmbient() => _world.Environment = new EnvironmentDesc { AmbientColor = new Float4(1, 1, 1, 1) };

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

        public static Vector3 Directional(in LightDesc light, Vector3 p, Vector3 n, Vector3 albedo, float metallic, float roughness)
        {
            Vector3 l = Vector3.Normalize(-new Vector3(light.Direction.X, light.Direction.Y, light.Direction.Z));
            float nl = MathF.Max(Vector3.Dot(n, l), 0);
            Vector3 radiance = new Vector3(light.Color.X, light.Color.Y, light.Color.Z) * light.Intensity * 8;
            return Brdf(l, Vector3.Normalize(-p), n, albedo, metallic, roughness, nl) * radiance * nl;
        }
    }
}
