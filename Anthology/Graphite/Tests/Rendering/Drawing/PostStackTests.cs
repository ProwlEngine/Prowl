using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

using Xunit;

using static Prowl.Graphite.Rendering.Tests.RendererHarness;

namespace Prowl.Graphite.Rendering.Tests;

// The stock post effects against CPU versions of Prowl's math, on small views
[Collection("GPU Tests")]
public sealed class PostStackTests : IDisposable
{
    // A nearly smooth surface writing its turned normal and _Reflectance into the G-buffer, shaded grey plus _Reflectance
    // times the probes, as a lit surface's top lobe is
    private const string MirrorSource = """
        Shader "Test/Mirror"
        {
            Properties
            {
                _Reflectance("Reflectance", Color) = (1, 1, 1, 1)
            }

            Pass
            {
                Tags { "LightMode" = "Forward" }
                Cull Off
                GLSLPROGRAM
                Vertex
                {
                    #include "Graphite/Vertex.glsl"
                    layout(location = 0) out vec3 vNormal;
                    layout(location = 1) out vec3 vRelative;
                    void main()
                    {
                        vNormal = Graphite_ObjectToWorldNormal(vec3(0.0, 0.0, -1.0));
                        vRelative = Graphite_ObjectToRelative(vertexPosition);
                        gl_Position = Graphite_RelativeToClip(vRelative);
                    }
                }
                Fragment
                {
                    #include "Graphite/Passes.glsl"
                    #include "Graphite/Lighting.glsl"
                    uniform vec4 _Reflectance;
                    layout(location = 0) in vec3 vNormal;
                    layout(location = 1) in vec3 vRelative;
                    layout(location = 0) out vec4 outColor;
                    void main()
                    {
                        vec3 n = normalize(vNormal);
                        vec3 probes = Graphite_ReflectionProbesReady() ? Graphite_ProbeRadiance(Graphite_FindProbes(vRelative), n, Graphite_ViewDir(vRelative), 0.01) : vec3(0.0);
                        Graphite_WriteGBuffer(n, _Reflectance.rgb, 0.1);
                        outColor = vec4(vec3(0.1) + _Reflectance.rgb * probes, 1.0);
                    }
                }
                ENDGLSL
            }
        }
        """;

    // The same colour in every direction
    private const string SkySource = """
        Shader "Test/Sky"
        {
            Properties
            {
                _Tint("Tint", Color) = (1, 1, 1, 1)
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
                    layout(location = 0) out vec4 outColor;
                    void main() { outColor = vec4(_Tint.rgb * Graphite_Exposure(), 1.0); }
                }
                ENDGLSL
            }
        }
        """;

    // Views expose by 1, so the unlit surfaces' radiance reaches the effects as it is
    private static readonly float UnitEV100 = -MathF.Log2(1.2f);

    private readonly RendererHarness _harness = new();
    private readonly GraphicsDevice _device;
    private readonly Renderer _renderer;
    private readonly RenderWorld _world;
    private readonly Shader _unlit;
    private readonly GpuMesh _quad;
    private readonly List<RendererError> _errors;

    public PostStackTests()
    {
        (_device, _renderer, _world, _errors) = (_harness.Device, _harness.Renderer, _harness.World, _harness.Errors);
        _unlit = _renderer.LoadShader(ShaderParser.Parse(UnlitSource(exposed: true)));
        _quad = _harness.CreateQuad();
    }

    public void Dispose() => _harness.Dispose();

    private T Effect<T>(T effect) where T : RenderFeature
    {
        _world.Features.Add(effect);
        return effect;
    }

    private Material Unlit(Float3 color)
    {
        Material material = _renderer.CreateMaterial(_unlit);
        material.SetColor("_Color", new Float4(color.X, color.Y, color.Z, 1));
        return material;
    }

    // A quad over the whole view at depth 10
    private RenderableHandle AddBackdrop(Float3 color, double depth = 10, double scale = 1)
        => _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(0, 0, depth)) * Double4x4.CreateScale(scale) }, [Unlit(color)]);

    private RenderView View(int id = 1)
    {
        RenderView view = _harness.View(id);
        view.EV100 = UnitEV100;
        return view;
    }

    private Float4[] Frame(float deltaTime = 1f / 60, RenderView? view = null)
        => _harness.Frame(new FrameDesc { Views = new[] { view ?? View() }, Time = new FrameTime { DeltaTime = deltaTime } });

    private static Float3 Map(Float3 c, Func<float, float> f) => new(f(c.X), f(c.Y), f(c.Z));

    private static float Luminance(Float3 c) => c.X * 0.2126f + c.Y * 0.7152f + c.Z * 0.0722f;

    // Prowl's tonemap operators, then its sRGB encode
    private static Float3 CpuTonemap(TonemapOperator op, Float3 c)
    {
        static Float3 Mul(Float3 v, float[] m) => new(v.X * m[0] + v.Y * m[1] + v.Z * m[2], v.X * m[3] + v.Y * m[4] + v.Z * m[5], v.X * m[6] + v.Y * m[7] + v.Z * m[8]);
        static Float3 MulColumns(float[] m, Float3 v) => new(m[0] * v.X + m[3] * v.Y + m[6] * v.Z, m[1] * v.X + m[4] * v.Y + m[7] * v.Z, m[2] * v.X + m[5] * v.Y + m[8] * v.Z);
        Float3 mapped = op switch
        {
            TonemapOperator.Reinhard => c *(Luminance(c) * (1 + Luminance(c) / 4) / (1 + Luminance(c)) / MathF.Max(Luminance(c), 1e-5f)),
            TonemapOperator.RomBinDaHouse => Map(c, x => MathF.Exp(-1 / (2.72f * x + 0.15f))),
            TonemapOperator.Uncharted2 => Map(c, x =>
            {
                static float Curve(float v) => (v * (0.15f * v + 0.05f) + 0.004f) / (v * (0.15f * v + 0.5f) + 0.06f) - 0.02f / 0.3f;
                return Curve(x * 2) / Curve(11.2f);
            }),
            TonemapOperator.Aces => Mul(Map(Mul(c, [0.59719f, 0.35458f, 0.04823f, 0.07600f, 0.90834f, 0.01566f, 0.02840f, 0.13383f, 0.83777f]),
                v => (v * (v + 0.0245786f) - 0.000090537f) / (v * (0.983729f * v + 0.4329510f) + 0.238081f)),
                [1.60475f, -0.53108f, -0.07367f, -0.10208f, 1.10813f, -0.00605f, -0.00327f, -0.07276f, 1.07602f]),
            TonemapOperator.AgXNeutral => AgX(c, false),
            _ => AgX(c, true),
        };

        static Float3 AgX(Float3 c, bool punchy)
        {
            const float minEv = -12.47393f, maxEv = 4.026069f;
            Float3 v = MulColumns([0.842479062253094f, 0.0423282422610123f, 0.0423756549057051f, 0.0784335999999992f, 0.878468636469772f, 0.0784336f, 0.0792237451477643f, 0.0791661274605434f, 0.879142973793104f], c);
            v = Map(v, x => (Math.Clamp(MathF.Log2(MathF.Max(x, 1e-10f)), minEv, maxEv) - minEv) / (maxEv - minEv));
            v = Map(v, x => 15.5f * MathF.Pow(x, 6) - 40.14f * MathF.Pow(x, 5) + 31.96f * MathF.Pow(x, 4) - 6.868f * x * x * x + 0.4298f * x * x + 0.1191f * x - 0.00232f);
            if (punchy)
            {
                float luma = Luminance(v);
                v = Map(v, x => MathF.Pow(MathF.Max(x, 0), 1.35f));
                v = new Float3(luma) + (v - new Float3(luma)) * 1.4f;
            }
            return MulColumns([1.19687900512017f, -0.0528968517574562f, -0.0529716355144438f, -0.0980208811401368f, 1.15190312990417f, -0.0980434501171241f, -0.0990297440797205f, -0.0989611768448433f, 1.15107367264116f], v);
        }

        Float3 display = Map(mapped, x => Math.Clamp(x, 0, 1));
        return op is TonemapOperator.AgXNeutral or TonemapOperator.AgXPunchy ? display : Map(display, x => x <= 0.0031308f ? x * 12.92f : 1.055f * MathF.Pow(x, 1 / 2.4f) - 0.055f);
    }

    // A float target holds linear light, so the present undoes the tonemapper's display encode
    private static Float3 Decode(Float3 display) => Map(display, x => MathF.Max(x, 0) <= 0.04045f ? MathF.Max(x, 0) / 12.92f : MathF.Pow((x + 0.055f) / 1.055f, 2.4f));

    [Theory]
    [InlineData(TonemapOperator.Aces)]
    [InlineData(TonemapOperator.AgXNeutral)]
    [InlineData(TonemapOperator.AgXPunchy)]
    [InlineData(TonemapOperator.Reinhard)]
    [InlineData(TonemapOperator.RomBinDaHouse)]
    [InlineData(TonemapOperator.Uncharted2)]
    public void Tonemapper_MatchesProwlsOperator(TonemapOperator op)
    {
        Float3 color = new(0.3f, 1.4f, 4.5f);
        AddBackdrop(color);
        Effect(new Tonemapper(_renderer) { Operator = op });
        AssertAll(Decode(CpuTonemap(op, color)), Frame(), 4e-3f);
    }

    // Draws the view into an sRGB target and returns its first pixel's stored bytes
    private byte[] SrgbPixel()
    {
        using Texture srgb = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(Size, Size, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm_SRgb, TextureUsage.RenderTarget | TextureUsage.Sampled));
        using Framebuffer target = _device.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, srgb));
        RenderView view = View();
        view.Target = target;
        _renderer.SubmitFrame(new FrameDesc { Views = new[] { view } });
        _device.WaitForIdle();
        Assert.Empty(_errors);
        using DeviceBuffer readback = _device.ResourceFactory.CreateBuffer(new BufferDescription(Size * Size * 4, BufferUsage.Staging));
        _device.Record(cmd => cmd.CopyTextureToBuffer(srgb, readback, 0, TextureRegion.Whole(srgb, 0, 0))).Wait();
        byte[] bytes = _device.Map<byte>(readback).ToArray()[..4];
        _device.Unmap(readback);
        return bytes;
    }

    [Fact]
    public void Tonemapper_ReachesAnSrgbTarget_EncodedOnce()
    {
        Float3 color = new(0.3f, 1.4f, 4.5f);
        RenderableHandle backdrop = AddBackdrop(color);
        Tonemapper tonemapper = Effect(new Tonemapper(_renderer));
        byte[] bytes = SrgbPixel();

        // The stored bytes are the tonemapper's display values, clamped by the target, its encode undone and the target's applied
        Float3 display = Map(CpuTonemap(TonemapOperator.Aces, color), x => Math.Clamp(x, 0, 1));
        Assert.InRange(bytes[0], display.X * 255 - 2, display.X * 255 + 2);
        Assert.InRange(bytes[1], display.Y * 255 - 2, display.Y * 255 + 2);
        Assert.InRange(bytes[2], display.Z * 255 - 2, display.Z * 255 + 2);

        // Display effects without a tonemapper leave the colour linear, for the target to encode
        _world.Remove(backdrop);
        _world.Features.Remove(tonemapper);
        AddBackdrop(new Float3(0.5f, 0.5f, 0.5f));
        Effect(new Fxaa(_renderer));
        byte[] pixel = SrgbPixel();
        float encoded = 1.055f * MathF.Pow(0.5f, 1 / 2.4f) - 0.055f;
        Assert.InRange(pixel[0], encoded * 255 - 2, encoded * 255 + 2);
    }

    [Fact]
    public void AutoExposure_MetersAboveItsLowPercentile_LeavingOutADarkQuarter()
    {
        // A near black quarter, as a night sky, sits below the low percentile, so the lit rest alone is metered
        Float3 color = new(20, 20, 20);
        RenderableHandle lit = AddBackdrop(color);
        RenderableHandle dark = AddBackdrop(new Float3(1e-5f), depth: 9, scale: 0.45);
        Effect(new AutoExposure(_renderer) { ExposureCompensation = 0 });
        RenderView view = View();
        Frame(view: view);
        float expected = color.X * RenderView.ExposureOf(MathF.Log2(Luminance(color) * 100 / 12.5f));
        AssertAll(new Float3(expected), Frame(view: view), expected * 0.01f, i => i % Size < 4 || i / Size < 4);

        // An infinite quarter of a new view meters as the largest half float, pre-exposure undone, beside the rest
        _world.Remove(lit);
        _world.Remove(dark);
        Float3 rest = new(2, 2, 2);
        AddBackdrop(rest);
        AddBackdrop(new Float3(float.PositiveInfinity), depth: 9, scale: 0.45);
        RenderView overflowed = View(2);
        overflowed.EV100 = 0;
        Frame(view: overflowed);
        float saturated = 65504 / RenderView.ExposureOf(0);
        float mean = MathF.Exp((0.05f * MathF.Log(2) + 0.23f * MathF.Log(saturated)) / 0.28f);
        float overflowExpected = rest.X * RenderView.ExposureOf(MathF.Log2(mean * 100 / 12.5f));
        AssertAll(new Float3(overflowExpected), Frame(view: overflowed), overflowExpected * 0.05f, i => i % Size < 4 || i / Size < 4);
    }

    // A white Lit surface without specular, lit by the ambient light alone, emitting a constant that stands in for
    // direct light, which screen lighting never touches
    private RenderableHandle AddIndirect(float direct, double x = 0, double depth = 10)
    {
        Material material = _renderer.CreateMaterial(_renderer.Standard.Lit);
        material.Surface = new SurfaceSettings(Cull: SurfaceCull.Off);
        material.SetColor("base_color", new Float4(1, 1, 1, 1));
        material.SetFloat("specular_weight", 0);
        material.SetFloat("emission_luminance", direct);
        return _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(x, 0, depth)) }, [material]);
    }

    [Fact]
    public void Gtao_DarkensIndirectLightBesideAStep_AndNeverDirectLight()
    {
        // A step two units toward the camera over the left half, so the floor beside it sees its wall
        _world.Environment = new EnvironmentDesc { AmbientColor = new Float4(0.5f, 0.5f, 0.5f, 1), Fog = default };
        AddIndirect(0.25f);
        AddIndirect(0.25f, x: -4, depth: 8);
        Float4[] open = Frame();
        Effect(new Gtao(_renderer) { Resolution = EffectResolution.Full, Radius = 2 });
        Frame();
        Float4[] pixels = Frame();
        float ambient = open[Size * 8 + 12].X - 0.25f;
        for (int y = 2; y < Size - 2; y++)
        {
            Assert.True(pixels[y * Size + 8].X < 0.25f + ambient * 0.95f, $"row {y} beside the step: {pixels[y * Size + 8].X}");
            Assert.True(pixels[y * Size + 15].X > 0.25f + ambient * 0.97f, $"row {y} far from the step: {pixels[y * Size + 15].X}");
        }

        // Without ambient light only the direct light is left, and GTAO takes none of it
        _world.Environment = new EnvironmentDesc { AmbientColor = default, Fog = default };
        AssertAll(new Float3(0.25f), Frame(), 2e-3f);
    }

    // A mirror over the lower half turned 45 degrees about x, so it faces the camera and up, and reflects up the view.
    // The test projection does not flip y, so the view's bottom is the image's first rows.
    private void AddMirror(Float3? reflectance = null)
    {
        double c = Math.Cos(Math.PI / 4), s = Math.Sin(Math.PI / 4);
        Double4x4 turn = new(new Double4(1, 0, 0, 0), new Double4(0, c, s, 0), new Double4(0, -s, c, 0), new Double4(0, -2, 10, 1));
        Material mirror = _renderer.CreateMaterial(_renderer.LoadShader(ShaderParser.Parse(MirrorSource)));
        Float3 r = reflectance ?? new Float3(1, 1, 1);
        mirror.SetColor("_Reflectance", new Float4(r.X, r.Y, r.Z, 1));
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = turn * Double4x4.CreateScale(new Double3(1, 0.5, 1)) }, [mirror]);
    }

    // A sky of one colour, captured at the views' exposure so the probes hold it as it is
    private void SkyOf(Float3 color)
    {
        Material sky = _renderer.CreateMaterial(_renderer.LoadShader(ShaderParser.Parse(SkySource)));
        sky.SetColor("_Tint", new Float4(color.X, color.Y, color.Z, 1));
        _world.Environment = new EnvironmentDesc { CaptureEV100 = UnitEV100, AmbientColor = default, Fog = default, Sky = sky };
    }

    private static readonly Float3 s_mirrorReflectance = new(0.5f, 0.25f, 1);
    private static readonly Float3 s_sky = new(0, 0.5f, 1);

    private static bool OnMirror(int i) => i / Size is >= 2 and < 6 && i % Size is >= 4 and < 12;

    private static float Distance(Float4 a, Float4 b) => MathF.Max(MathF.Max(MathF.Abs(a.X - b.X), MathF.Abs(a.Y - b.Y)), MathF.Max(MathF.Abs(a.Z - b.Z), MathF.Abs(a.W - b.W)));

    [Fact]
    public void Ssr_SwapsTheProbesForTheTrace_ByTheStoredReflectance()
    {
        SkyOf(s_sky);
        AddMirror(s_mirrorReflectance);
        Frame();
        Float4[] unhit = Frame();
        for (int i = 0; i < unhit.Length; i++)
        {
            if (OnMirror(i))
                Assert.True(unhit[i].Z > 0.6f, $"pixel {i} shows no probes: {unhit[i]}");
        }

        // With nothing for the trace to hit the probe shading stays
        ScreenSpaceReflections missing = Effect(new ScreenSpaceReflections(_renderer));
        Frame();
        Frame();
        Float4[] missed = Frame();
        for (int i = 0; i < unhit.Length; i++)
            Assert.True(Distance(unhit[i], missed[i]) < 2e-3f, $"pixel {i}: {unhit[i]} became {missed[i]}");
        _world.Features.Remove(missing);

        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(0, 4, 9)) * Double4x4.CreateScale(new Double3(1, 0.5, 1)) }, [Unlit(new Float3(1, 0, 0))]);
        Frame();
        Float4[] plain = Frame();
        Effect(new ScreenSpaceReflections(_renderer) { UseTemporal = false, Normalization = false });
        Float4[] reflected = Frame();
        Float3 R = s_mirrorReflectance;
        for (int i = 0; i < plain.Length; i++)
        {
            if (!OnMirror(i))
                continue;
            Float4 before = plain[i], after = reflected[i];
            Float3 probes = new(0, (before.Y - 0.1f) / R.Y, (before.Z - 0.1f) / R.Z);
            Assert.True(MathF.Abs(before.X - 0.1f) < 1e-3f && probes.Z > 0.5f, $"pixel {i} is not the probe lit mirror: {before}");

            // The trace holds only red, so green and blue keep the probes' share the trace left, the same in both, and red
            // gains at most the reflectance times that confidence
            float keptGreen = (after.Y - 0.1f) / (R.Y * probes.Y), keptBlue = (after.Z - 0.1f) / (R.Z * probes.Z);
            float confidence = 1 - keptBlue;
            Assert.True(confidence > 0.25f && confidence < 1.01f, $"pixel {i} reflects {after}");
            Assert.True(MathF.Abs(keptGreen - keptBlue) < 0.05f, $"pixel {i}: green keeps {keptGreen}, blue {keptBlue}");
            Assert.True(after.X > 0.1f + 0.1f * R.X && after.X < 0.1f + R.X * confidence + 0.01f, $"pixel {i}: red {after.X} at confidence {confidence}");
        }
    }

    [Fact]
    public void LayeredViews_RunTheStackPerLayer_AsTheirMonoViewsDo()
    {
        _world.Environment = new EnvironmentDesc { AmbientColor = new Float4(0.5f, 0.5f, 0.5f, 1), Fog = default };
        AddIndirect(0.3f);
        AddIndirect(0.1f, x: -4, depth: 8);
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(2, 3, 6)) * Double4x4.CreateScale(0.25) }, [Unlit(new Float3(3, 2, 1))]);
        Effect(new Gtao(_renderer) { Resolution = EffectResolution.Half, Radius = 2 });
        Effect(new DepthOfField(_renderer) { UseAutoFocus = false, ManualFocusPoint = 8, FocusStrength = 20, MaxBlurRadius = 20 });
        Effect(new Bloom(_renderer) { Threshold = 1, Iterations = 2 });
        Effect(new Tonemapper(_renderer));
        Effect(new Vignette(_renderer));
        Effect(new TemporalAntiAliasing(_renderer));

        using Texture layers = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(Size, Size, 1, 2, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget | TextureUsage.Sampled));
        using Framebuffer layered = _device.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, layers) { ViewCount = 2 });
        RenderView stereo = _harness.BareView(5);
        stereo.EV100 = UnitEV100;
        stereo.Target = layered;
        stereo.Sky = false;
        stereo.ClearColor = new Float4(0, 0, 0, 1);
        ViewLayer[] cameraLayers = new ViewLayer[2];
        for (int layer = 0; layer < 2; layer++)
            cameraLayers[layer] = new ViewLayer { View = Float4x4.Identity, Projection = RendererHarness.Projection, Position = new Double3(layer * 0.5 - 0.25, 0, 0) };
        stereo.Camera = new ViewCamera(cameraLayers);
        _renderer.SubmitFrame(new FrameDesc { Views = new[] { stereo }, Time = new FrameTime { DeltaTime = 1f / 60 } });
        _device.WaitForIdle();
        Assert.Empty(_errors);

        for (int layer = 0; layer < 2; layer++)
        {
            using DeviceBuffer readback = _device.ResourceFactory.CreateBuffer(new BufferDescription(Size * Size * 16, BufferUsage.Staging));
            _device.Record(cmd => cmd.CopyTextureToBuffer(layers, readback, 0, TextureRegion.Whole(layers, 0, (uint)layer))).Wait();
            Float4[] got = _device.Map<Float4>(readback).ToArray();
            _device.Unmap(readback);

            RenderView mono = View(10 + layer);
            mono.Camera = new ViewCamera(stereo.Camera.Layers[layer]);
            Float4[] want = Frame(view: mono);
            Assert.True(want.Any(p => p.X > 0.05f) && want.Distinct().Count() > 4, $"layer {layer} shows too little");
            for (int i = 0; i < want.Length; i++)
                Assert.True(MathF.Abs(want[i].X - got[i].X) < 2e-3f && MathF.Abs(want[i].Y - got[i].Y) < 2e-3f && MathF.Abs(want[i].Z - got[i].Z) < 2e-3f, $"layer {layer} pixel {i}: mono {want[i]}, layered {got[i]}");
        }
    }
}
