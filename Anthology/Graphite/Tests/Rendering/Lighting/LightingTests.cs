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

// Draws a quad shaded by the PBR closure filling the view and checks every pixel against the same math on the CPU,
// PbrReference: point and spot lights from both trees, directional lights, ambient, fog and decals.
[Collection("GPU Tests")]
public sealed partial class LightingTests : IDisposable
{
    private const string Source = """
        Shader "Test/PBR"
        {
            Properties
            {
                _Color("Albedo", Color) = (0.8, 0.8, 0.8, 1)
                _Roughness("Roughness", Float) = 0.5
                _Metallic("Metallic", Float) = 0
                _Occlusion("Occlusion", Float) = 1
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
                        vWorld = Graphite_ObjectToRelative(vertexPosition);
                        vNormal = Graphite_ObjectToWorldNormal(vertexNormal);
                        gl_Position = Graphite_RelativeToClip(vWorld);
                    }
                }
                Fragment
                {
                    #pragma variant global PBR_TIER FULL LITE MAX
                    #include "Graphite/PBR/Closure.glsl"
                    #include "Graphite/Decals.glsl"
                    #include "Graphite/Passes.glsl"
                    uniform vec4 _Color;
                    uniform float _Roughness;
                    uniform float _Metallic;
                    uniform float _Occlusion;
                    layout(location = 0) in vec3 vWorld;
                    layout(location = 1) in vec3 vNormal;
                    layout(location = 0) out vec4 outColor;
                    void main()
                    {
                        vec3 n = normalize(vNormal);
                        PBR_Params p = PBR_DefaultParams(n, vec3(1.0, 0.0, 0.0));
                        p.baseColor = _Color.rgb;
                        p.specularRoughness = _Roughness;
                        p.baseMetalness = _Metallic;
                        p.occlusion = _Occlusion;
                        Graphite_ApplyDecals(vWorld, n, p.baseColor, p.geometryNormal, p.specularRoughness, p.baseMetalness, p.occlusion);
                        PBR_Closure c = PBR_Prepare(p, vWorld, Graphite_ViewDir(vWorld), n, 0.0);
                        vec3 color = PBR_ShadeLights(c) + Graphite_AmbientLight(c.normal) * p.baseColor;
                        outColor = vec4(Graphite_ApplyFog(color, vWorld), 1.0);
                        Graphite_WriteGBuffer(c.normal, c.specularAlbedo, c.roughness);
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
                    #define GRAPHITE_SUN_DISK
                    #include "Graphite/Lighting.glsl"
                    layout(location = 0) in vec3 vNormal;
                    layout(location = 1) in vec2 vLightmapUV;
                    layout(location = 0) out vec4 outColor;
                    void main()
                    { Graphite_WriteGBuffer(vec3(0.0, 0.0, -1.0), vec3(0.0), 1.0);
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
                        outColor = vec4(mix(vec3(1.0), d * 0.5 + 0.5, _Gradient) * _Tint.rgb * Graphite_Exposure(), 1.0);
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
                        vWorld = Graphite_ObjectToRelative(vertexPosition);
                        gl_Position = Graphite_RelativeToClip(vWorld);
                    }
                }
                Fragment
                {
                    #include "Graphite/Passes.glsl"
                    #define GRAPHITE_SUN_DISK
                    #include "Graphite/Lighting.glsl"
                    layout(location = 0) in vec3 vWorld;
                    layout(location = 0) out vec4 outColor;
                    void main()
                    { Graphite_WriteGBuffer(vec3(0.0, 0.0, -1.0), vec3(0.0), 1.0);
                        outColor = vec4(Graphite_SampleSkyProbe(normalize(vWorld), 0.0), 1.0);
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
                _Color("Radiance", Color) = (1, 1, 1, 1)
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
                    void main() { Graphite_WriteGBuffer(vec3(0.0, 0.0, -1.0), vec3(0.0), 1.0); outColor = vec4(_Color.rgb * Graphite_Exposure(), 1.0); }
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
                        vWorld = Graphite_ObjectToRelative(vertexPosition);
                        vNormal = Graphite_ObjectToWorldNormal(vertexNormal);
                        gl_Position = Graphite_RelativeToClip(vWorld);
                    }
                }
                Fragment
                {
                    #include "Graphite/Passes.glsl"
                    #define GRAPHITE_SUN_DISK
                    #include "Graphite/Lighting.glsl"
                    uniform float _Roughness;
                    layout(location = 0) in vec3 vWorld;
                    layout(location = 1) in vec3 vNormal;
                    layout(location = 0) out vec4 outColor;
                    void main()
                    { Graphite_WriteGBuffer(vec3(0.0, 0.0, -1.0), vec3(0.0), 1.0);
                        outColor = vec4(Graphite_ProbeRadiance(Graphite_FindProbes(vWorld), normalize(vNormal), Graphite_ViewDir(vWorld), _Roughness * _Roughness), 1.0);
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
                    void main() { Graphite_WriteGBuffer(vec3(0.0, 0.0, -1.0), vec3(0.0), 1.0); outColor = vec4(wind[1].rgb, 1.0); }
                }
                ENDGLSL
            }
        }
        """;

    private const int Size = RendererHarness.Size;
    private const float QuadDepth = 10;

    // Views and captures expose by 1, so pixels hold radiance as it is
    private static readonly float UnitEV100 = -MathF.Log2(1.2f);

    private readonly RendererHarness _harness = new();
    private readonly GraphicsDevice _device;
    private readonly Framebuffer _framebuffer;
    private readonly Renderer _renderer;
    private readonly RenderWorld _world;
    private readonly Shader _shader;
    private readonly GpuMesh _quad;
    private readonly List<RendererError> _errors;
    private Double3 _origin;

    public LightingTests()
    {
        (_device, _framebuffer, _renderer, _world, _errors) = (_harness.Device, _harness.Framebuffer, _harness.Renderer, _harness.World, _harness.Errors);
        _world.Environment = new EnvironmentDesc { CaptureEV100 = UnitEV100, AmbientColor = default, Fog = default };
        // The MAX tier is the one these tests describe: energy compensated lobes, exact Fresnel, rough diffuse and the sun as a disk
        _world.Globals.Set(GlobalAxis.PbrTier, "MAX");
        _shader = _renderer.LoadShader(ShaderParser.Parse(Source));
        _quad = _harness.CreateQuad(normals: true);
    }

    public void Dispose() => _harness.Dispose();

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

    private float _ev100 = UnitEV100;

    private Float4[] Frame(params IProfiler[] profilers)
    {
        RenderView view = _harness.BareView(1);
        view.EV100 = _ev100;
        view.Camera = new ViewCamera(new ViewLayer { View = Float4x4.Identity, Projection = RendererHarness.Projection, Position = _origin });
        return _harness.Frame(new FrameDesc { Views = new[] { view }, Profilers = profilers });
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

        Vector3 albedo = new(0.7f, 0.7f, 0.7f);
        _origin = new Double3(149_597_870_700.0, 1_234_567.25, -42_000_000.5);
        AddQuad(Surface(new Float3(albedo.X, albedo.Y, albedo.Z), 0.6f));
        _world.AddLight(Point(new Double3(-149_597_870_700.0, 0, 0), 5, 1, new Float3(1, 1, 1)));
        _world.AddLight(Point(new Double3(3, 0, 0), 5, 1, new Float3(1, 1, 1)));
        LightDesc nearSolarSystem = Point(_origin + new Double3(0.3, -0.2, 8.5), 2, 0.02f, new Float3(1, 0.8f, 0.6f));
        _world.AddLight(nearSolarSystem);

        AssertMatches(p => Reference.Local(Relative(nearSolarSystem), p, s_normal, albedo, 0, 0.6f), Frame());
    }

    // A light as the reference sees it, its position relative to the camera
    private LightDesc Relative(LightDesc light)
    {
        light.Position -= _origin;
        return light;
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
            LightDesc light = Point(new Double3(random.NextDouble() * 9 - 4.5, random.NextDouble() * 9 - 4.5, 9 + random.NextDouble() * 0.9), 0.1f + random.NextSingle() * 0.15f,
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
    public void NormalizedSpotCones_FocusThePointLightsFlux_IntoTheCone()
    {
        Vector3 albedo = new(0.8f, 0.8f, 0.8f);
        AddQuad(Surface(new Float3(albedo.X, albedo.Y, albedo.Z), 0.4f));
        LightDesc spot = new() { Type = LightType.Spot, Position = new Double3(0, 0, 5), Direction = new Float3(0, 0, 1), Range = 10, Intensity = 2, InnerAngle = 20, OuterAngle = 30, Static = true };
        LightHandle handle = _world.AddLight(spot);
        Float4[] plain = Frame();

        // A cone of half angle 30 holds 2 pi (1 - cos 30) of the sphere's 4 pi
        spot.NormalizeSpotCone = true;
        _world.SetLight(handle, spot);
        Float4[] focused = Frame();
        float gain = 2 / (1 - MathF.Cos(30 * MathF.PI / 180));
        Assert.Contains(plain, p => p.X > 0.01f);
        for (int i = 0; i < plain.Length; i++)
            Assert.True(MathF.Abs(focused[i].X - plain[i].X * gain) <= 1e-4f + 0.01f * plain[i].X * gain, $"pixel {i}: {focused[i].X} against {plain[i].X * gain}");
    }

    // Brute force cosine weighted solid angle of a shape, sampled over its area: sum of cos at the point times cos at the light over d^2
    private static float Integrate(Func<float, float, Vector3> point, Vector3 lightNormal, float area, Vector3 n, int steps, bool twoSided = false, Func<float, float, Vector3>? outward = null)
    {
        double sum = 0;
        for (int i = 0; i < steps; i++)
        {
            for (int j = 0; j < steps; j++)
            {
                (float u, float v) = ((i + 0.5f) / steps, (j + 0.5f) / steps);
                Vector3 q = point(u, v);
                float d2 = q.LengthSquared();
                Vector3 l = q / MathF.Sqrt(d2);
                float atLight = twoSided ? 1 : MathF.Max(Vector3.Dot(-l, outward?.Invoke(u, v) ?? lightNormal), 0);
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

            // A thin tube, against summing its side as seen from the point, its caps left out
            Vector3 a = center - right * w, b = center + up * h;
            const float tubeRadius = 0.01f;
            Vector3 axis = Vector3.Normalize(b - a);
            Vector3 side = Vector3.Normalize(Vector3.Cross(axis, MathF.Abs(axis.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX)), other = Vector3.Cross(axis, side);
            float tube = Integrate((u, v) =>
            {
                float angle = 2 * MathF.PI * v;
                return Vector3.Lerp(a, b, u) + (side * MathF.Cos(angle) + other * MathF.Sin(angle)) * tubeRadius;
            }, Vector3.Zero, 2 * MathF.PI * tubeRadius * (b - a).Length(), n, 1000, outward: (u, v) => side * MathF.Cos(2 * MathF.PI * v) + other * MathF.Sin(2 * MathF.PI * v));
            float tubeFormula = Reference.TubeIrradiance(a, b, tubeRadius, n);
            Assert.True(MathF.Abs(tubeFormula - tube) <= 1e-4f + 0.03f * tube, $"tube {k}: {tubeFormula} against {tube}");
        }
    }

    [Fact]
    public void TubeIrradiance_OfALongTubeFacingThePoint_IsPiTimesItsRadiusOverItsDistance()
    {
        // An endless cylinder of radius R at distance d seen square on subtends a cosine weighted solid angle of pi R / d
        Vector3 n = new(0, 0, 1);
        Assert.Equal(MathF.PI * 0.05f / 2, Reference.TubeIrradiance(new Vector3(-2000, 0, 2), new Vector3(2000, 0, 2), 0.05f, n), 1e-5f);
    }

    [Fact]
    public void AreaSpecular_FollowsTheShapeAcrossItsHighlight_IntoItsEdgesCornersAndGrazingViews()
    {
        Vector3 n = new(0, 1, 0);
        Vector3 Facing(Vector3 center) => Vector3.Normalize(-center);
        Vector3 Up(Vector3 axis) => Vector3.Normalize(Vector3.Cross(axis, Vector3.UnitX));
        Vector3 ahead = new(0, 1.5f, 3), wall = new(0, 1, 4);
        // A light, then the views a sweep takes from 0 to 1, the mirror direction crossing the light
        var sweeps = new (string Name, LightType Type, Vector3 Center, Vector3 Axis, Vector3 Up, float Radius, Vector2 Size, Func<float, Vector3> View)[]
        {
            ("rect ahead", LightType.Rect, ahead, Facing(ahead), Up(Facing(ahead)), 0, new(1.2f, 0.6f), t => new(0, 0.1f + 1.4f * t, -1)),
            ("rect corner", LightType.Rect, ahead, Facing(ahead), Up(Facing(ahead)), 0, new(1.2f, 0.6f), t => new(-0.8f + t, 0.5f + 0.4f * t, -1)),
            ("wall grazing", LightType.Rect, wall, new(0, 0, -1), Vector3.UnitY, 0, new(2, 2), t => new(-0.9f + 1.8f * t, 0.15f, -1)),
            ("disk ahead", LightType.Disk, ahead, Facing(ahead), Up(Facing(ahead)), 0, new(0.5f, 0), t => new(0.3f * t, 0.1f + 1.4f * t, -1)),
            ("disk grazing", LightType.Disk, wall, new(0, 0, -1), Vector3.UnitY, 0, new(1, 0), t => new(-0.9f + 1.8f * t, 0.15f, -1)),
            ("tube across", LightType.Tube, wall, Vector3.UnitX, Vector3.UnitY, 0.03f, new(2.5f, 0), t => new(-1.2f + 2.4f * t, 0.25f, -1)),
        };

        List<string> report = new();
        bool failed = false;
        foreach (float roughness in new[] { 0.2f, 0.4f, 0.6f })
        {
            foreach ((string name, LightType type, Vector3 center, Vector3 axis, Vector3 up, float radius, Vector2 size, Func<float, Vector3> view) in sweeps)
            {
                const int samples = 13;
                var pairs = new (float Truth, float Estimate)[samples];
                for (int s = 0; s < samples; s++)
                    pairs[s] = Reference.Specular(type, center, axis, up, radius, size, Vector3.Normalize(view(s / (samples - 1f))), n, roughness);
                float peak = pairs.Max(p => p.Truth);
                float worst = pairs.Max(p => MathF.Abs(p.Estimate - p.Truth)) / peak;
                float mean = pairs.Average(p => MathF.Abs(p.Estimate - p.Truth)) / peak;
                failed |= worst > 0.25f || mean > 0.1f;
                report.Add($"{name} roughness {roughness}: worst {worst:F3}, mean {mean:F3} of the peak");
            }
        }
        Assert.False(failed, string.Join(Environment.NewLine, report));
    }

    [Fact]
    public void SmallShapesInNits_LightLikePointsOfTheirLuminanceTimesTheirArea()
    {
        Vector3 albedo = new(0.7f, 0.7f, 0.7f);
        AddQuad(Surface(new Float3(albedo.X, albedo.Y, albedo.Z), 0.5f));
        LightDesc point = Point(new Double3(0, 0, 4), 30, 0.2f, new Float3(1, 1, 1));
        LightHandle handle = _world.AddLight(point);
        Float4[] want = Frame();

        // A small sphere in candela, and a rect, a disk and a tube of no length in nits, shine like a point of their
        // intensity: a shape's luminance times the area it shows
        foreach ((LightType type, float radius, Float2 size, float area, float tolerance) in new[]
        {
            (LightType.Point, 0.01f, new Float2(0, 0), 1f, 0.01f),
            (LightType.Tube, 0.01f, new Float2(0.001f, 0), MathF.PI * 0.01f * 0.01f, 0.01f),
            (LightType.Rect, 0f, new Float2(0.05f, 0.05f), 0.05f * 0.05f, 0.02f),
            (LightType.Disk, 0f, new Float2(0.03f, 0), MathF.PI * 0.03f * 0.03f, 0.02f),
        })
        {
            LightDesc shaped = point;
            shaped.Type = type;
            shaped.SourceRadius = radius;
            shaped.Size = size;
            shaped.Intensity = point.Intensity / area;
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

    private DecalDesc Decal(Double3 center, Double3 size, Float4 color, int priority = 0, bool isStatic = false, uint layer = ~0u) => new()
    {
        Transform = Double4x4.CreateTranslation(_origin + center) * Double4x4.CreateScale(size.X, size.Y, -size.Z),
        Color = color,
        Priority = priority,
        Static = isStatic,
        LayerMask = layer,
    };

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
                        vWorld = Graphite_ObjectToRelative(vertexPosition);
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
                        Graphite_WriteGBuffer(vec3(0.0, 0.0, -1.0), vec3(0.0), 1.0);
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

    private static Float3 Normalize(Float3 v) => v / MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);

    private LightDesc ShadowSpot() => new()
    {
        Type = LightType.Spot,
        Position = new Double3(0, 0, 2),
        Direction = new Float3(0, 0, 1),
        Range = 20,
        Intensity = 1.6f,
        InnerAngle = 35,
        OuterAngle = 40,
        Static = true,
        Shadow = new ShadowDesc { Enabled = true },
    };

    private FrameStats Stats => _renderer.LastFrameStats;

    [Fact]
    public void CachedShadows_DrawAgainOnlyWhenTheirLightOrACasterNearItChanges()
    {
        Vector3 albedo = new(0.8f, 0.8f, 0.8f);
        AddQuad(Surface(new Float3(albedo.X, albedo.Y, albedo.Z)));
        LightDesc light = ShadowSpot();
        LightHandle handle = _world.AddLight(light);
        RenderableHandle caster = AddCaster(1, 0.5, 6);
        Frame();
        Assert.Equal(1, Stats.ShadowViewsDrawn);
        Frame();
        Assert.Equal(0, Stats.ShadowViewsDrawn);

        // A caster far outside the light's reach changes nothing, the one in it draws the light again
        AddCaster(200, 0, 6);
        Frame();
        Assert.Equal(0, Stats.ShadowViewsDrawn);
        _world.SetTransform(caster, Double4x4.CreateTranslation(new Double3(-1, -0.5, 6)) * Double4x4.CreateScale(0.25));
        Float4[] pixels = Frame();
        Assert.Equal(1, Stats.ShadowViewsDrawn);
        AssertShadowed(p => Reference.Local(light, p, s_normal, albedo, 0, 0.5f), p => SquareEdge(p, -2, -1, 2), pixels, 0.6f);

        _world.SetLight(handle, light with { Intensity = 0.3f });
        Frame();
        Assert.Equal(0, Stats.ShadowViewsDrawn);
        _world.SetLight(handle, light with { Direction = Normalize(new Float3(0.05f, 0, 1)) });
        Frame();
        Assert.Equal(1, Stats.ShadowViewsDrawn);
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
        Assert.Equal(2, Stats.ShadowViewsDrawn);
        Assert.Equal(1, Stats.ShadowCopiesMade);
        AssertShadowed(p => Reference.Local(light, p, s_normal, albedo, 0, 0.5f), p => MathF.Min(SquareEdge(p, 2, 1, 2), SquareEdge(p, -3, -3, 1)), pixels, 0.6f);

        // From 2 through 6 to 10 the square doubles, the moving one's shadow goes from -3, -3 to -3, 3
        _world.SetTransform(moving, Double4x4.CreateTranslation(new Double3(-1.5, 1.5, 6)) * Double4x4.CreateScale(0.5 / 4));
        pixels = Frame();
        Assert.Equal(1, Stats.ShadowViewsDrawn);
        Assert.Equal(1, Stats.ShadowCopiesMade);
        AssertShadowed(p => Reference.Local(light, p, s_normal, albedo, 0, 0.5f), p => MathF.Min(SquareEdge(p, 2, 1, 2), SquareEdge(p, -3, 3, 1)), pixels, 0.6f);

        Frame();
        Assert.Equal(0, Stats.ShadowViewsDrawn);
    }

    private sealed class IndirectCount() : FrameFeature(FrameSlot.BeforeViews)
    {
        private BufferHandle _count;

        public override void Setup(FrameBuilder builder)
        {
            _count = builder.Buffer(RenderResourceID.Intern("Test.IndirectCount"), GraphBufferDesc.Structured(4, 4), usage: BufferAccess.TransferWrite);
            builder.Export(_count);
        }

        public override void Render(FrameContext context) => context.Command.UpdateBuffer<uint>(context.Get(_count), 0, [0, 0, 0, 0]);
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

    // A receiver that takes shadows but casts none, so a light close to it cannot shadow it with itself
    private void AddReceiver(Vector3 albedo)
        => _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(_origin + new Double3(0, 0, QuadDepth)), Flags = RenderableFlags.ReceiveShadows }, [Surface(new Float3(albedo.X, albedo.Y, albedo.Z))]);

    private static ShadowPlan Plan(int light, float wanted, int held = 0, bool split = false, int faces = 1)
        => new() { Light = light, Faces = faces, Priority = 1000 - light, Wanted = wanted, MaxTile = 1024, Held = held, Split = split };

    [Fact]
    public void ShadowPlans_ShrinkTogether_ThenDropTheLeastImportant_StaticCopiesOutsideTheBudget()
    {
        // Light 0 holds 512 but wants 340 now, which rounds to 256, light 1 has a static copy, light 2 is least important
        ShadowPlanner planner = new();
        List<ShadowPlan> plans = [Plan(0, 340, held: 512), Plan(1, 256, held: 256, split: true), Plan(2, 256, held: 256)];
        planner.Plan(plans, 512 * 512 + 2 * 256 * 256, 64);
        Assert.Equal((512, true), (plans[0].Target, plans[1].Split));

        plans = [Plan(0, 340, held: 512), Plan(1, 256, held: 256, split: true), Plan(2, 256, held: 256)];
        planner = new();
        planner.Plan(plans, 256 * 256 * 3, 64);
        Assert.Equal((256, true, 256, 256), (plans[0].Target, plans[1].Split, plans[1].Target, plans[2].Target));

        plans = [Plan(0, 340, held: 512), Plan(1, 256, held: 256, split: true), Plan(2, 256, held: 256)];
        planner = new();
        planner.Plan(plans, 256 * 256 * 2, 64);
        Assert.Equal((128, 128, 128, true), (plans[0].Target, plans[1].Target, plans[2].Target, plans[1].Split));

        plans = [Plan(0, 256), Plan(1, 256), Plan(2, 256)];
        planner = new();
        planner.Plan(plans, 64 * 64 * 2, 64);
        Assert.Equal((64, 64, 0), (plans[0].Target, plans[1].Target, plans[2].Target));
    }

    [Fact]
    public void AKeywordACasterReads_RedrawsTheCachedShadow()
    {
        AddReceiver(new Vector3(0.8f));
        _world.AddLight(ShadowSpot());
        string source = CasterSource.Replace("Test/Caster", "Test/KeywordCaster").Replace("#include \"Graphite/Vertex.glsl\"", "#include \"Graphite/Vertex.glsl\"\n#pragma variant global CASTER_WIND");
        Assert.Contains("CASTER_WIND", source);
        Material caster = _renderer.CreateMaterial(_renderer.LoadShader(ShaderParser.Parse(source)));
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(1, 0.5, 6)) * Double4x4.CreateScale(0.25), Flags = RenderableFlags.CastShadows | RenderableFlags.ShadowsOnly }, [caster]);
        Frame();
        Frame();
        Assert.Equal(0, Stats.ShadowViewsDrawn);

        _world.Globals.Set(GlobalAxis.Toggle("CASTER_WIND"), true);
        Frame();
        Assert.Equal(1, Stats.ShadowViewsDrawn);
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

    [StructLayout(LayoutKind.Sequential)]
    private struct Water
    {
        public Float4 Tint;
        public Float4 Depth;
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

    [Fact]
    public void LayeredViews_DrawEachLayerExactlyLikeAViewOfItsOwn()
    {
        _world.Environment = new EnvironmentDesc { CaptureEV100 = UnitEV100, AmbientColor = new Float4(0.3f, 0.3f, 0.3f, 1), Fog = default };
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
        RenderView cube = _harness.BareView(7);
        cube.EV100 = UnitEV100;
        cube.Target = layered;
        ViewLayer[] cameraLayers = new ViewLayer[6];
        for (int f = 0; f < 6; f++)
            cameraLayers[f] = new ViewLayer { View = Look(s_faces[f].Right, s_faces[f].Up, s_faces[f].Forward), Projection = s_perspective, Position = _origin + new Double3(0.03 * f, -0.02 * f, 0.01 * f) };
        cube.Camera = new ViewCamera(cameraLayers);
        _renderer.SubmitFrame(new FrameDesc { Views = new[] { cube } });
        _device.WaitForIdle();
        Assert.Empty(_errors);

        for (int f = 0; f < 6; f++)
        {
            Float4[] got = _harness.ReadLayer(layers, (uint)f);
            RenderView mono = _harness.BareView(8);
            mono.EV100 = UnitEV100;
            mono.Camera = new ViewCamera(cube.Camera.Layers[f]);
            _renderer.SubmitFrame(new FrameDesc { Views = new[] { mono } });
            _device.WaitForIdle();
            Float4[] want = _harness.ReadTarget();

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
    public void SkyProbe_HoldsTheSkyInEveryDirection()
    {
        _world.Environment = new EnvironmentDesc { CaptureEV100 = UnitEV100, Fog = default, Sky = SkyMaterial(new Float3(1, 1, 1), 1) };
        using Texture layers = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(Size, Size, 1, 6, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget | TextureUsage.Sampled));
        using Framebuffer layered = _device.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, layers) { ViewCount = 6 });
        RenderView cube = _harness.BareView(11);
        cube.EV100 = UnitEV100;
        cube.Target = layered;
        ViewLayer[] cameraLayers = new ViewLayer[6];
        for (int f = 0; f < 6; f++)
            cameraLayers[f] = new ViewLayer { View = Look(s_faces[f].Right, s_faces[f].Up, s_faces[f].Forward), Projection = s_perspective, Position = _origin };
        cube.Camera = new ViewCamera(cameraLayers);

        Float4[][] Capture()
        {
            _renderer.SubmitFrame(new FrameDesc { Views = new[] { cube } });
            _device.WaitForIdle();
            Assert.Empty(_errors);
            return Enumerable.Range(0, 6).Select(f => _harness.ReadLayer(layers, (uint)f)).ToArray();
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

    private static readonly Vector3 s_skyColor = new(0.1f, 0.2f, 0.4f);

    // A room of single sided unlit walls, one colour per side, facing in on a centre; the camera's reflector sits on layer 2
    private void Room(Double3 center, double half, Func<int, Float3> color, bool standardUnlit = false)
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
            Material wall = _renderer.CreateMaterial(standardUnlit ? _renderer.Standard.Unlit : unlit);
            Float3 c = color(f);
            wall.SetColor(standardUnlit ? "base_color" : "_Color", new Float4(c.X, c.Y, c.Z, 1));
            Double3 at = _origin + center + new Double3(forward.X, forward.Y, forward.Z) * half;
            _world.Add(new RenderableDesc { Mesh = facing, Transform = Double4x4.CreateTranslation(at) * basis * Double4x4.CreateScale(half / 4), LayerMask = 1, Flags = RenderableFlags.ReceiveShadows }, [wall]);
        }
        _world.Environment = new EnvironmentDesc { CaptureEV100 = UnitEV100, Fog = default, AmbientColor = default, Sky = SkyMaterial(new Float3(s_skyColor.X, s_skyColor.Y, s_skyColor.Z), 0) };
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

    // A Graphite/Unlit wall reflects in a probe as it shows directly, whatever exposure the probe is captured at
    [Fact]
    public void UnlitSurfaces_ReflectAsTheyShowDirectly_AtAnyCaptureExposure()
    {
        Room(new Double3(0, 0, 10), 20, _ => new Float3(0.8f, 0.5f, 0.2f), standardUnlit: true);
        _world.AddReflectionProbe(RoomProbe(new Double3(-10, 0, 10), new Float3(20, 40, 40)));
        _ev100 = 8;
        Frame();
        Float4 wall = Frame()[Size * Size / 2 + Size / 2];
        Vector3 direct = new(wall.X, wall.Y, wall.Z);
        Vector3 sky = s_skyColor * RenderView.ExposureOf(8);

        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(_origin + new Double3(0, 0, QuadDepth)), LayerMask = 2 }, [Reflector()]);
        foreach (float captureEV100 in new[] { UnitEV100, 6f, 14f })
        {
            _world.Environment.CaptureEV100 = captureEV100;
            Frame();
            Frame();
            AssertMatches(p => p.X < 0 ? direct : sky, Frame());
        }
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
    public void EvaluateAmbient_GivesTheUniformAndHemisphereLight_TheShadersUse()
    {
        _world.Environment = new EnvironmentDesc { CaptureEV100 = UnitEV100, Ambient = AmbientMode.Uniform, AmbientColor = new Float4(0.2f, 0.4f, 0.8f, 1), AmbientIntensity = 0.5f };
        Assert.Equal(new Float3(0.1f, 0.2f, 0.4f), _world.EvaluateAmbient(new Float3(0, 1, 0)));

        _world.Environment = new EnvironmentDesc { CaptureEV100 = UnitEV100, Ambient = AmbientMode.Hemisphere, AmbientSky = new Float4(1, 0, 0, 1), AmbientGround = new Float4(0, 0, 1, 1), AmbientIntensity = 1 };
        Assert.Equal(new Float3(1, 0, 0), _world.EvaluateAmbient(new Float3(0, 1, 0)));
        Assert.Equal(new Float3(0.5f, 0, 0.5f), _world.EvaluateAmbient(new Float3(1, 0, 0)));

        AddQuad(Surface(new Float3(0.5f, 1, 0.25f)));
        _world.Environment = new EnvironmentDesc { CaptureEV100 = UnitEV100, Ambient = AmbientMode.Uniform, AmbientColor = new Float4(0.2f, 0.4f, 0.8f, 1), Fog = default };
        AssertMatches(_ => new Vector3(0.1f, 0.4f, 0.2f), Frame());

        _world.Environment = new EnvironmentDesc { CaptureEV100 = UnitEV100, Ambient = AmbientMode.Hemisphere, AmbientSky = new Float4(1, 0, 0, 1), AmbientGround = new Float4(0, 0, 1, 1), Fog = default };
        AssertMatches(_ => new Vector3(0.25f, 0, 0.125f), Frame());
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
            RenderView view = _harness.BareView(16);
            view.EV100 = UnitEV100;
            view.Camera = new ViewCamera(new ViewLayer { View = Float4x4.Identity, Projection = RendererHarness.Projection, Position = _origin });
            _renderer.SubmitFrame(new FrameDesc { Views = new[] { view } });
            _device.WaitForIdle();
            _renderer.SubmitFrame(new FrameDesc { Views = new[] { view } });
            return _harness.ReadTarget();
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

    private sealed class WriteHostBuffer(DeviceBuffer buffer, Float4 second) : FrameFeature(FrameSlot.BeforeViews)
    {
        public override void Setup(FrameBuilder builder) => builder.Write(buffer, BufferAccess.TransferWrite);

        public override void Render(FrameContext context) => context.Command.UpdateBuffer<Float4>(buffer, 0, [new Float4(9, 9, 9, 1), second]);
    }

    [Fact]
    public void FrameFeatures_WriteHostBuffers_BeforeTheDrawsThatReadThem()
    {
        Material material = _renderer.CreateMaterial(_renderer.LoadShader(ShaderParser.Parse(BufferSource)));
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(_origin + new Double3(0, 0, QuadDepth)) }, [material]);
        using DeviceBuffer buffer = _device.ResourceFactory.CreateBuffer(new BufferDescription(32, BufferUsage.StructuredBufferReadOnly));
        _renderer.Globals.SetBuffer("WindField", buffer);
        WriteHostBuffer feature = new(buffer, new Float4(0.25f, 0.5f, 0.75f, 1));
        _world.FrameFeatures.Add(feature);
        AssertMatches(_ => new Vector3(0.25f, 0.5f, 0.75f), Frame());
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

    private Texture Lightmap<T>(uint width, PixelFormat format, T[] texels) where T : unmanaged
    {
        Texture texture = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(width, 1, 1, 1, format, TextureUsage.Sampled));
        _device.UpdateTexture<T>(texture, texels, new TextureRegion(0, 0, 0, width, 1, 1, 0, 0));
        _harness.Owned.Add(texture);
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

        byte[] texelFallback = [128, 64, 255, 128];
        AddIndirect(IndirectQuad(secondUVs: false), lightmap: new LightmapBinding(Lightmap<byte>(1, PixelFormat.R8_G8_B8_A8_UNorm, texelFallback), new Float4(1, 1, 0, 0), LightmapEncoding.RGBM));

        float scale = texelFallback[3] / 255f * 8;
        AssertMatches(_ => new Vector3(texelFallback[0], texelFallback[1], texelFallback[2]) / 255f * scale, Frame());
    }

    /// <summary>The light loop on the CPU, the same expressions the shaders use, every light shaded by PbrReference.</summary>
    private static partial class Reference
    {
        // The PBR closure of a surface of this base colour, metalness and roughness, the rest at the spec's defaults
        private static PbrReference Closure(Vector3 n, Vector3 v, Vector3 albedo, float metallic, float roughness)
            => new(new PbrInputs(albedo, metallic, roughness), n, v);

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

            Vector3 radiance = new Vector3(light.Color.X, light.Color.Y, light.Color.Z) * light.Intensity * attenuation;
            return Closure(n, Vector3.Normalize(-p), albedo, metallic, roughness).Evaluate(l) * radiance * nl;
        }

        // Graphite's procedural sky in nits under a white sun of sunLux, at its defaults unless given
        public static Vector3 ProceduralSky(Vector3 view, Vector3 sun, float sunLux, float sunSize = 1, float sunGlow = 1, float intensity = 1, Vector3? tint = null, Vector3? ground = null, bool disk = true)
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
            Vector3 color = (single + bounce * (Vector3.One - viewT)) * 1.7557f * (tint ?? Vector3.One) * (sunLux * SkyNitsPerLux);

            // The disk's solid angle is 2 pi times its rim blend's mean width in cosine, so it shines the sun's illuminance
            if (disk && sunSize > 0)
                color += new Vector3(Smooth(1 - 1.5e-4f * sunSize * sunSize, 1 - 1e-5f * sunSize * sunSize, Vector3.Dot(view, sun)) * (view.Y >= 0 ? 1 : 0) * sunLux / (2 * MathF.PI * 8e-5f * sunSize * sunSize));
            color = Vector3.Lerp(color, color * (ground ?? new Vector3(0.25f)), Smooth(0, -0.02f, view.Y));
            return color * intensity;
        }

        public const float SkyNitsPerLux = 2.8f;

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
        // representative point's estimate, on a white metal. Returns (true, estimate).
        public static (float Truth, float Estimate) Specular(LightType type, Vector3 center, Vector3 axis, Vector3 up, float radius, Vector2 size, Vector3 v, Vector3 n, float roughness)
        {
            PbrReference metal = Closure(n, v, Vector3.One, 1, roughness);
            float Lobe(Vector3 l, Vector3 v, Vector3 n, float roughness)
            {
                float nl = Vector3.Dot(n, l);
                return nl <= 0 ? 0 : metal.Specular(l).X * nl;
            }

            Vector3 surface = -v * 5;
            Vector3 at = surface + center;
            LightDesc light = new() { Type = type, Position = new Double3(at.X, at.Y, at.Z), Direction = new Float3(axis.X, axis.Y, axis.Z), Up = new Float3(up.X, up.Y, up.Z),
                SourceRadius = radius, Size = new Float2(size.X, size.Y), Range = 1e6f, Intensity = 1, Color = new Float3(1, 1, 1) };
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
                        // Uniform over the cone the sphere fills, one candela, so radiance 1 over its projected area
                        float d = center.Length();
                        float cosMax = MathF.Sqrt(MathF.Max(1 - radius * radius / (d * d), 0));
                        float cos = 1 - u * (1 - cosMax), sin = MathF.Sqrt(1 - cos * cos), a = 2 * MathF.PI * w;
                        Vector3 c = center / d;
                        Vector3 t = Vector3.Normalize(Vector3.Cross(c, MathF.Abs(c.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX)), b = Vector3.Cross(c, t);
                        Vector3 l = c * cos + t * (sin * MathF.Cos(a)) + b * (sin * MathF.Sin(a));
                        truth += Lobe(l, v, n, roughness) * 2 * MathF.PI * (1 - cosMax) / (steps * steps) / (MathF.PI * radius * radius);
                    }
                    else if (type == LightType.Tube)
                    {
                        // Along its length, one nit, a thin tube as wide as it looks
                        if (j > 0)
                            continue;
                        Vector3 q = center + axis * ((u - 0.5f) * size.X);
                        float d2 = q.LengthSquared();
                        Vector3 l = q / MathF.Sqrt(d2);
                        float along = Vector3.Dot(l, axis);
                        truth += Lobe(l, v, n, roughness) * 2 * radius * MathF.Sqrt(MathF.Max(1 - along * along, 0)) / d2 * size.X / steps;
                    }
                    else
                    {
                        // Over the surface, one nit, seen with its cosine
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
                        truth += Lobe(l, v, n, roughness) * MathF.Max(Vector3.Dot(-l, axis), 0) / d2 * area / (steps * steps);
                    }
                }
            }
            return ((float)truth, estimate);
        }

        public static Vector3 Directional(in LightDesc light, Vector3 p, Vector3 n, Vector3 albedo, float metallic, float roughness)
        {
            Vector3 l = Vector3.Normalize(-new Vector3(light.Direction.X, light.Direction.Y, light.Direction.Z));
            float nl = MathF.Max(Vector3.Dot(n, l), 0);
            Vector3 radiance = new Vector3(light.Color.X, light.Color.Y, light.Color.Z) * light.Intensity;
            if (light.AngularRadius <= 0)
                return Closure(n, Vector3.Normalize(-p), albedo, metallic, roughness).Evaluate(l) * radiance * nl;

            // The sun's disk: specular from the nearest direction inside it, its lobe widened by the disk
            float radius = light.AngularRadius * MathF.PI / 180;
            float sin = MathF.Sin(radius), cos = MathF.Cos(radius);
            Vector3 v = Vector3.Normalize(-p);
            Vector3 r = Vector3.Reflect(-v, n);
            float rl = Vector3.Dot(r, l);
            Vector3 across = r - l * rl;
            Vector3 ls = rl >= cos ? r : across.LengthSquared() > 1e-8f ? Vector3.Normalize(l * cos + Vector3.Normalize(across) * sin) : l;
            float specular = MathF.Max(Vector3.Dot(n, ls), 0) * AreaEnergy(PbrReference.Alpha(roughness), 0.5f * sin, 0.5f * sin);
            return Shade(l, nl, ls, specular, v, n, albedo, metallic, roughness) * radiance;
        }

        // The closure with the diffuse and specular lobes each from their own direction
        private static readonly ushort[] s_ltc = LtcTables.Embedded();

        private static LtcTables.D3 D(Vector3 w) => new(w.X, w.Y, w.Z);

        // The share of a lobe of normal n and alpha a tube, rect or disk covers, as Graphite/Ltc.glsl and Lights.glsl give it.
        // A rect or disk takes its half axes, a tube its ends and radius.
        public static float LtcShare(LightType type, Vector3 center, Vector3 h1, Vector3 h2, float radius, Vector3 n, Vector3 v, float alpha)
        {
            float mu = Math.Clamp(Vector3.Dot(n, v), 1e-4f, 1);
            Vector3 t1 = v - n * Vector3.Dot(n, v);
            t1 = t1.Length() > 1e-5f ? Vector3.Normalize(t1) : Vector3.Normalize(Vector3.Cross(n, MathF.Abs(n.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX));
            Vector3 t2 = Vector3.Cross(n, t1);
            LtcTables.M3 inverse = LtcTables.Lookup(s_ltc, mu, MathF.Sqrt(MathF.Max(alpha, 0)));
            LtcTables.D3 Space(Vector3 w) => inverse * new LtcTables.D3(Vector3.Dot(w, t1), Vector3.Dot(w, t2), Vector3.Dot(w, n));
            if (type == LightType.Tube)
            {
                // h1 and h2 the tube's ends, each cap a small half sphere there
                Vector3 direction = Vector3.Normalize(h2 - h1);
                Vector3 across = Vector3.Cross(direction, h1 + h2);
                across = across.LengthSquared() > 1e-12f ? Vector3.Normalize(across) : Vector3.Normalize(Vector3.Cross(direction, MathF.Abs(direction.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX));
                double Cap(Vector3 end)
                {
                    float end2 = MathF.Max(end.LengthSquared(), 1e-8f);
                    LtcTables.D3 o = Space(end / MathF.Sqrt(end2));
                    double o2 = LtcTables.D3.Dot(o, o);
                    return Math.Max(o.Z, 0) * Math.Abs(inverse.Determinant) / (Math.PI * o2 * o2) * 0.5 * Math.PI * radius * radius / end2;
                }
                return (float)(LtcTables.Strip(Space(h1), Space(h2), Space(across * (2 * radius))) + Cap(h1) + Cap(h2));
            }
            LtcTables.D3 c = Space(center), x = Space(h1), y = Space(h2);
            return (float)(type == LightType.Disk ? LtcTables.Disk(c, x, y) : LtcTables.Polygon([c - x - y, c + x - y, c + x + y, c - x + y]));
        }

        private static Vector3 Shade(Vector3 ld, float diffuse, Vector3 ls, float specular, Vector3 v, Vector3 n, Vector3 albedo, float metallic, float roughness, bool share = false)
        {
            if (share)
            {
                PbrReference lit = Closure(n, v, albedo, metallic, roughness);
                return lit.Diffuse(ld) * diffuse + lit.SpecularAlbedo * specular;
            }
            PbrReference c = Closure(n, v, albedo, metallic, roughness);
            return c.Diffuse(ld) * diffuse + c.Specular(ls) * specular;
        }

        private static float Window(float dist2, float range)
        {
            float factor = dist2 / MathF.Max(range * range, 1e-6f);
            float window = Math.Clamp(1 - factor * factor, 0, 1);
            return window * window;
        }

        public static float AreaEnergy(float alpha, float ex, float ey)
        {
            float a = MathF.Max(alpha, PbrReference.MinAlpha);
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

        // The cosine weighted solid angle of the side of a thin tube of radius from a to b relative to the point, clipped by the horizon
        public static float TubeIrradiance(Vector3 a, Vector3 b, float radius, Vector3 n)
        {
            Vector3 d = b - a;
            float len = d.Length();
            if (len < 1e-4f)
                return 0;
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
            float perpLength = perp.Length();
            float h = MathF.Max(perpLength, radius);
            float np = Vector3.Dot(n, perp) / MathF.Max(perpLength, 1e-6f);
            float t0 = MathF.Atan((s0 + bu) / h), t1 = MathF.Atan((s1 + bu) / h);
            float across = 0.5f * (t1 - t0) + 0.25f * (MathF.Sin(2 * t1) - MathF.Sin(2 * t0));
            float along = 0.5f * (MathF.Sin(t1) * MathF.Sin(t1) - MathF.Sin(t0) * MathF.Sin(t0));
            return MathF.Max(2 * radius * (np * across + nu * along) / h, 0);
        }

        private static Vector3 SpherePoint(Vector3 center, float radius, Vector3 r)
        {
            Vector3 toRay = Vector3.Dot(center, r) * r - center;
            return center + toRay * Math.Clamp(radius / MathF.Max(toRay.Length(), 1e-6f), 0, 1);
        }

        private static Vector3 V(Float3 f) => new(f.X, f.Y, f.Z);

        // Spheres, tubes, rects and disks: diffuse from their luminance times their cosine weighted solid angle, a sphere's
        // luminance its intensity over its disk's area, specular from the most representative point
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
            bool share = false;

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
                specular = MathF.Max(Vector3.Dot(n, ls), 0) * attenuation * AreaEnergy(PbrReference.Alpha(roughness), rad / (2 * dist), rad / (2 * dist));
            }
            else if (light.Type == LightType.Tube)
            {
                Vector3 axis = dir * (0.5f * light.Size.X);
                Vector3 a = toLight - axis, b = toLight + axis, ab = b - a;
                float t = Math.Clamp(-Vector3.Dot(a, ab) / MathF.Max(ab.LengthSquared(), 1e-8f), 0, 1);
                ld = Vector3.Normalize(a + ab * t);
                float caps = SphereIrradiance(Vector3.Dot(n, lc), MathF.Min(light.SourceRadius * light.SourceRadius / dist2, 1));
                diffuse = (TubeIrradiance(a, b, light.SourceRadius, n) + caps) * window;
                specular = LtcShare(LightType.Tube, toLight, a, b, light.SourceRadius, n, v, PbrReference.Alpha(roughness)) * window;
                share = true;
            }
            else
            {
                float front = Vector3.Dot(-lc, dir);
                if (front <= 0)
                    return Vector3.Zero;
                Vector3 right = Vector3.Cross(up, dir);
                bool disk = light.Type == LightType.Disk;
                Vector2 half = disk ? new Vector2(light.Size.X) : 0.5f * new Vector2(light.Size.X, light.Size.Y);
                if (disk)
                {
                    float r2 = half.X * half.X;
                    diffuse = SphereIrradiance(Vector3.Dot(n, lc), r2 / (r2 + dist2)) * front * window;
                }
                else
                {
                    Vector3 hx = right * half.X, hy = up * half.Y;
                    diffuse = QuadIrradiance([toLight - hx - hy, toLight + hx - hy, toLight + hx + hy, toLight - hx + hy], n) * window;
                }
                specular = LtcShare(light.Type, toLight, right * half.X, up * half.Y, 0, n, v, PbrReference.Alpha(roughness)) * window;
                share = true;
            }

            return Shade(ld, diffuse, ls, specular, v, n, albedo, metallic, roughness, share) * (V(light.Color) * light.Intensity);
        }
    }

    [Fact]
    public void KeywordsNoCasterReads_LeaveCachedShadowsAlone()
    {
        AddReceiver(new Vector3(0.8f));
        _world.AddLight(ShadowSpot());
        AddCaster(1, 0.5, 6);
        Frame();
        Frame();
        _world.Globals.Set(GlobalAxis.Toggle("UNUSED_BY_CASTERS"), true);
        Frame();
        Assert.Equal(0, Stats.ShadowViewsDrawn);
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
        Assert.Equal(1, Stats.ShadowViewsDrawn);
        _world.SetBones(caster, [identity]);
        Frame();
        Assert.Equal(0, Stats.ShadowViewsDrawn);

        // Eight object units left and four down at a quarter scale puts the caster where the moved caster test does
        _world.SetBones(caster, [new Float4x4(new Float4(1, 0, 0, 0), new Float4(0, 1, 0, 0), new Float4(0, 0, 1, 0), new Float4(-8, -4, 0, 1))]);
        Float4[] pixels = Frame();
        Assert.Equal(1, Stats.ShadowViewsDrawn);
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
        Assert.Equal(1, Stats.ShadowViewsDrawn);
    }

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
        Assert.Equal(2, Stats.ShadowViewsDrawn);
        Frame();
        Assert.Equal(2, Stats.ShadowViewsDrawn);
    }

    [Fact]
    public void AProviderCastingIntoACachedLight_RedrawsIt()
    {
        AddReceiver(new Vector3(0.8f));
        _world.AddLight(ShadowSpot());
        AddCaster(-1.5, -1.5, 6, half: 0.5);
        Frame();
        Frame();
        Assert.Equal(0, Stats.ShadowViewsDrawn);

        _world.Providers.Add(new ShadowItem(_quad, Surface(new Float3(1, 1, 1))));
        Frame();
        Assert.True(Stats.ShadowViewsDrawn > 0);
    }

    [Fact]
    public void AMovingCasterReachingAStaticOnlyLight_SplitsIt()
    {
        AddReceiver(new Vector3(0.8f));
        _world.AddLight(ShadowSpot());
        AddCaster(1, 0.5, 6, isStatic: true);
        Frame();
        Frame();
        Assert.Equal(0, Stats.ShadowViewsDrawn);

        RenderableHandle moving = AddCaster(-1.5, -1.5, 6, half: 0.5);
        Frame();
        _world.SetTransform(moving, Double4x4.CreateTranslation(new Double3(-1.5, 1.5, 6)) * Double4x4.CreateScale(0.5 / 4));
        Frame();
        Assert.Equal(1, Stats.ShadowViewsDrawn);
        Assert.Equal(1, Stats.ShadowCopiesMade);
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
        _harness.Owned.Add(profile);
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
        _world.AddLight(Point(new Double3(0.5, -0.5, 8), 6, 0.4f, new Float3(1, 1, 1)));
        _world.AddLight(Point(new Double3(-1, 1, 9), 3, 0.16f, new Float3(1, 0.5f, 0.2f), isStatic: false));

        Float4[] Perspective()
        {
            RenderView view = _harness.BareView(9);
            view.EV100 = UnitEV100;
            view.Camera = new ViewCamera(new ViewLayer { View = Float4x4.Identity, Projection = s_perspective, Position = _origin });
            _renderer.SubmitFrame(new FrameDesc { Views = new[] { view } });
            _device.WaitForIdle();
            Assert.Empty(_errors);
            return _harness.ReadTarget();
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
}
