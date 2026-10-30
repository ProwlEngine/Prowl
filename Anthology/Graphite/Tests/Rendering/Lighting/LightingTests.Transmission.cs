using System;
using System.Runtime.InteropServices;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

using Xunit;

using Vector3 = System.Numerics.Vector3;

namespace Prowl.Graphite.Rendering.Tests;

// The PBR model's transmission on the GPU: the white furnace through transmissive quads and volumes at both tiers, a
// glass slab's absorption over a flat backdrop, dispersion's order, and which views copy their colour and depth.
public sealed partial class LightingTests
{
    // Colour 0.5 + 0.1 x in every channel, x camera relative, so a shifted read shows as a changed value
    private const string RampSource = """
        Shader "Test/Ramp"
        {
            Pass
            {
                Tags { "LightMode" = "Forward" }
                Cull Off
                GLSLPROGRAM
                Vertex
                {
                    #include "Graphite/Vertex.glsl"
                    layout(location = 0) out vec3 vRelative;
                    void main()
                    {
                        vRelative = Graphite_ObjectToRelative(vertexPosition);
                        gl_Position = Graphite_RelativeToClip(vRelative);
                    }
                }
                Fragment
                {
                    #include "Graphite/Passes.glsl"
                    layout(location = 0) in vec3 vRelative;
                    layout(location = 0) out vec4 outColor;
                    void main()
                    {
                        Graphite_WriteGBuffer(vec3(0.0, 0.0, -1.0), vec3(0.0), 1.0);
                        outColor = vec4(vec3(0.5 + 0.1 * vRelative.x), 1.0);
                    }
                }
                ENDGLSL
            }
        }
        """;

    // A slab of thickness d facing the camera: its front at z 0 and its back at z d, wider than the view
    private GpuMesh Slab(float d)
    {
        const float w = 40;
        Float3[] positions = [new(-w, -w, 0), new(w, -w, 0), new(w, w, 0), new(-w, w, 0), new(-w, -w, d), new(w, -w, d), new(w, w, d), new(-w, w, d)];
        Float3[] normals = [new(0, 0, -1), new(0, 0, -1), new(0, 0, -1), new(0, 0, -1), new(0, 0, 1), new(0, 0, 1), new(0, 0, 1), new(0, 0, 1)];
        return _renderer.CreateMesh(new MeshData
        {
            VertexCount = 8,
            Streams =
            [
                new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(positions.AsSpan()).ToArray() },
                new VertexStream { Semantic = VertexSemantic.Normal, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(normals.AsSpan()).ToArray() },
            ],
            Indices = new uint[] { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7 },
        });
    }

    private RenderableHandle AddSlab(float d, Material material)
        => _world.Add(new RenderableDesc { Mesh = Slab(d), Transform = Double4x4.CreateTranslation(_origin + new Double3(0, 0, QuadDepth)) }, [material]);

    // A one sided transmissive material, whose slab draws its front in the Transmission pass and its back in BackDepth
    private Material Glass(Action<Material> set) => PBR(m =>
    {
        m.Surface = new SurfaceSettings();
        set(m);
    });

    private static readonly Action<Material>[] s_whiteTransmission =
    [
        m => { s_whiteBases[0](m); m.SetFloat("transmission_weight", 1); },
        m => { s_whiteBases[0](m); m.SetFloat("transmission_weight", 0.5f); m.SetFloat("specular_ior", 1.8f); },
        m => { s_whiteBases[0](m); m.SetFloat("transmission_weight", 0.7f); m.SetFloat("base_metalness", 0.3f); m.SetFloat("subsurface_weight", 0.5f); m.SetColor("subsurface_color", new Float4(1, 1, 1, 1)); },
        m => { s_whiteBases[0](m); m.SetFloat("transmission_weight", 1); m.SetFloat("geometry_opacity", 0.5f); },
        m => { s_whiteBases[0](m); m.SetFloat("transmission_weight", 1); m.SetInt("geometry_thin_walled", 1); },
    ];

    private static readonly Action<Material>[] s_whiteVolumes =
    [
        m => { s_whiteBases[0](m); m.SetFloat("transmission_weight", 1); m.SetFloat("transmission_depth", 0.5f); m.SetColor("transmission_scatter", new Float4(1, 1, 1, 1)); },
        m => { s_whiteBases[0](m); m.SetFloat("transmission_weight", 1); m.SetFloat("transmission_depth", 2); m.SetColor("transmission_scatter", new Float4(1, 1, 1, 1)); m.SetFloat("transmission_scatter_anisotropy", 0.7f); },
        m => { s_whiteBases[0](m); m.SetFloat("transmission_weight", 0.6f); m.SetFloat("transmission_depth", 1); m.SetColor("transmission_scatter", new Float4(1, 1, 1, 1)); m.SetFloat("transmission_scatter_anisotropy", -0.5f); },
    ];

    // Transmissive quads with depth 0 and a white colour, under coverage and thin walled too, over the white sky
    [Theory]
    [InlineData("FULL")]
    [InlineData("LITE")]
    public void TheFurnace_WhiteTransmission_IsOne(string tier)
    {
        _world.Globals.SetKeyword("PBR_TIER", tier);
        for (int i = 0; i < s_whiteTransmission.Length; i++)
            AssertFurnace($"{tier} transmission {i}", s_whiteTransmission[i]);
    }

    // Volumes deeper than 0 scattering white, so mu_a is 0, seen through a slab with a path through it
    [Theory]
    [InlineData("FULL")]
    [InlineData("LITE")]
    public void TheFurnace_WhiteScatteringVolumes_AreOne(string tier)
    {
        _world.Globals.SetKeyword("PBR_TIER", tier);
        for (int i = 0; i < s_whiteVolumes.Length; i++)
        {
            _world.Environment = new EnvironmentDesc { CaptureEV100 = UnitEV100, Ambient = AmbientMode.Sky, AmbientIntensity = 1, Fog = default, Sky = SkyMaterial(new Float3(1, 1, 1), 0) };
            RenderableHandle slab = AddSlab(3, Glass(s_whiteVolumes[i]));
            Frame();
            Float4[] pixels = Frame();
            _world.Remove(slab);
            for (int p = 0; p < pixels.Length; p++)
            {
                for (int c = 0; c < 3; c++)
                    Assert.True(MathF.Abs(pixels[p][c] - 1) <= 2e-3f, $"{tier} volume {i} pixel {p} channel {c}: {pixels[p][c]}");
            }
            if (tier == "FULL")
                Assert.True(_renderer.LastFrameStats.View(1)!.BackDepthUsed);
        }
    }

    private void DarkWorld()
        => _world.Environment = new EnvironmentDesc { CaptureEV100 = UnitEV100, AmbientColor = default, Fog = default, Sky = SkyMaterial(new Float3(0, 0, 0), 0) };

    private void AddBackdrop(Material material)
        => _world.Add(new RenderableDesc { Mesh = FacingQuad, Transform = Double4x4.CreateTranslation(_origin + new Double3(0, 0, 20)) * Double4x4.CreateScale(4) }, [material]);

    // A slab of thickness d absorbing as colour c over depth D passes exp(-mu_t d / mu_refracted) of a flat backdrop,
    // mu_t = -ln(c) / D, mu_refracted the view's cosine refracted into it, times what the dielectric lets in
    [Theory]
    [InlineData(1f, 1f)]
    [InlineData(3f, 2f)]
    public void AGlassSlab_AbsorbsAlongItsRefractedPath(float d, float depth) => AssertSlabAbsorbs(d, depth, 0);

    // Under a coat light still refracts into the glass at its own IOR, Snell's law through parallel layers
    [Fact]
    public void ACoatedGlassSlab_RefractsAtTheGlassIor() => AssertSlabAbsorbs(3, 2, 1);

    private void AssertSlabAbsorbs(float d, float depth, float coat)
    {
        DarkWorld();
        Vector3 backdrop = new(0.2f, 0.4f, 0.8f);
        Material flat = _renderer.CreateMaterial(_renderer.Standard.Unlit);
        flat.SetColor("_MainColor", new Float4(backdrop.X, backdrop.Y, backdrop.Z, 1));
        AddBackdrop(flat);

        Vector3 color = new(0.9f, 0.6f, 0.3f);
        AddSlab(d, Glass(m =>
        {
            m.SetFloat("transmission_weight", 1);
            m.SetColor("transmission_color", new Float4(color.X, color.Y, color.Z, 1));
            m.SetFloat("transmission_depth", depth);
            m.SetFloat("specular_roughness", 0.2f);
            if (coat > 0)
            {
                m.SetFloat("coat_weight", coat);
                m.SetFloat("coat_ior", 1.6f);
            }
        }));
        Frame();
        Float4[] pixels = Frame();

        PbrInputs inputs = new(new Vector3(0.8f), Roughness: 0.2f) { TransmissionWeight = 1, TransmissionColor = color, TransmissionDepth = depth, CoatWeight = coat };
        Vector3 n = new(0, 0, -1);
        int checkedPixels = 0;
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                Vector3 position = PixelPosition(x, y);
                Vector3 v = Vector3.Normalize(-position);
                double nv = Vector3.Dot(n, v);
                double mu = PbrReference.RefractedCosine(nv, 1.5);
                Vector3 exit = position + Vector3.Normalize(new Vector3(-v.X, -v.Y, 0)) * (float)(Math.Sqrt(1 - mu * mu) / mu * d);
                if (MathF.Abs(exit.X) > 3.7f || MathF.Abs(exit.Y) > 3.7f)
                    continue;
                checkedPixels++;
                Vector3 transmittance = new PbrReference(inputs, n, v).Transmittance;
                Float4 got = pixels[y * Size + x];
                for (int c = 0; c < 3; c++)
                {
                    double want = backdrop[c] * transmittance[c] * Math.Exp(Math.Log(color[c]) / depth * d / mu);
                    Assert.True(Math.Abs(got[c] - want) <= 2e-3 + 0.01 * want, $"d {d} depth {depth} coat {coat} pixel {x},{y} channel {c}: expected {want}, got {got[c]}");
                }
            }
        }
        Assert.True(checkedPixels > Size * Size / 2, $"only {checkedPixels} pixels refract onto the view");
    }

    // A volume with no back face drawn behind its pixel absorbs over transmission_depth along the refracted ray, so a
    // lone quad of coloured glass stays coloured
    [Fact]
    public void AVolumeWithoutABackFace_AbsorbsOverItsDepth()
    {
        DarkWorld();
        Vector3 backdrop = new(0.2f, 0.4f, 0.8f);
        Material flat = _renderer.CreateMaterial(_renderer.Standard.Unlit);
        flat.SetColor("_MainColor", new Float4(backdrop.X, backdrop.Y, backdrop.Z, 1));
        AddBackdrop(flat);

        Vector3 color = new(0.9f, 0.6f, 0.3f);
        AddFacingQuad(Glass(m =>
        {
            m.SetFloat("transmission_weight", 1);
            m.SetColor("transmission_color", new Float4(color.X, color.Y, color.Z, 1));
            m.SetFloat("transmission_depth", 2);
            m.SetFloat("specular_roughness", 0.2f);
        }));
        Frame();
        Float4[] pixels = Frame();

        PbrInputs inputs = new(new Vector3(0.8f), Roughness: 0.2f) { TransmissionWeight = 1, TransmissionColor = color, TransmissionDepth = 2 };
        Vector3 n = new(0, 0, -1);
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                Vector3 v = Vector3.Normalize(-PixelPosition(x, y));
                double mu = PbrReference.RefractedCosine(Vector3.Dot(n, v), 1.5);
                Vector3 transmittance = new PbrReference(inputs, n, v).Transmittance;
                Float4 got = pixels[y * Size + x];
                for (int c = 0; c < 3; c++)
                {
                    double want = backdrop[c] * transmittance[c] * Math.Pow(color[c], 1 / mu);
                    Assert.True(Math.Abs(got[c] - want) <= 2e-3 + 0.01 * want, $"pixel {x},{y} channel {c}: expected {want}, got {got[c]}");
                }
            }
        }
    }

    // Dispersion refracts blue most and red least: through a slab over a ramp, green reads where it does without
    // dispersion, and red and blue fall on either side of it, red toward the straight line and blue away
    [Fact]
    public void Dispersion_SeparatesTheChannelsInOrder()
    {
        DarkWorld();
        AddBackdrop(_renderer.CreateMaterial(_renderer.LoadShader(ShaderParser.Parse(RampSource))));
        Material glass = Glass(m =>
        {
            m.SetColor("base_color", new Float4(0, 0, 0, 1));
            m.SetFloat("transmission_weight", 1);
            m.SetFloat("specular_roughness", 0.05f);
            m.SetFloat("transmission_dispersion_abbe_number", 9);
        });
        AddSlab(8, glass);
        Frame();
        Float4[] plain = Frame();
        glass.SetFloat("transmission_dispersion_scale", 1);
        Frame();
        Float4[] dispersed = Frame();

        int seen = 0;
        for (int y = 6; y < 10; y++)
        {
            foreach (int x in new[] { 3, 4, 11, 12 })
            {
                int i = y * Size + x;
                float side = x < Size / 2 ? -1 : 1;
                Float4 d = dispersed[i];
                Assert.True(MathF.Abs(d.Y - plain[i].Y) <= 1e-3f, $"pixel {x},{y}: green moved from {plain[i].Y} to {d.Y}");
                Assert.True((d.X - d.Y) * side > 3e-4f, $"pixel {x},{y}: red {d.X} should lie past green {d.Y} on side {side}");
                Assert.True((d.Y - d.Z) * side > 3e-4f, $"pixel {x},{y}: blue {d.Z} should lie short of green {d.Y} on side {side}");
                seen++;
            }
        }
        Assert.Equal(16, seen);
    }

    // Only views drawing a transmissive surface copy their colour and depth and draw back depth, volumes alone the
    // latter, and LITE views none of it
    [Fact]
    public void ScreenCopies_RunOnlyForViewsDrawingTransmission()
    {
        DarkWorld();
        CameraFrame Camera() => _renderer.Frame.Camera(1)!;
        ViewStats Drawn() => _renderer.LastFrameStats.View(1)!;
        Material material = Glass(m => m.Surface = new SurfaceSettings(SurfaceType.Transparent));
        AddSlab(1, material);
        Frame();
        Assert.False(Drawn().SceneColorUsed || Drawn().SceneDepthUsed || Drawn().BackDepthUsed);
        Assert.Null(Camera().SceneColor);
        Assert.Null(Camera().BackDepth);

        material.SetFloat("transmission_weight", 1);
        Frame();
        Assert.True(Drawn().SceneColorUsed && Drawn().SceneDepthUsed && Drawn().BackDepthUsed);
        Assert.NotNull(Camera().SceneColor);
        Assert.NotNull(Camera().BackDepth);

        material.SetInt("geometry_thin_walled", 1);
        Frame();
        Assert.True(Drawn().SceneColorUsed);
        Assert.False(Drawn().BackDepthUsed);

        material.SetInt("geometry_thin_walled", 0);
        _world.Globals.SetKeyword("PBR_TIER", "LITE");
        Frame();
        Assert.False(Drawn().SceneColorUsed || Drawn().SceneDepthUsed || Drawn().BackDepthUsed);
    }

    // Shows the scene's reactive mask in red, read after the transparent surfaces
    private sealed class ShowReactive(FullscreenProgram program, Sampler sampler) : RenderFeature
    {
        private readonly PropertySet _properties = new();

        public override RenderSlot Slot => RenderSlot.AfterTransparents;
        public override SceneTargets SceneReads => SceneTargets.Reactive;

        public override void Setup(FeatureBuilder builder, in FeatureView view)
        {
            builder.ReadScene();
            builder.WriteColor();
        }

        public override void Render(FeatureContext context)
        {
            _properties.SetTexture("_Reactive", context.Reactive!, sampler);
            context.Fullscreen(program, _properties);
        }
    }

    // The reactive mask holds the coverage of transmissive surfaces: geometry_opacity where they refract, times
    // transmission_weight where they do not, thin walled, and 0 elsewhere
    [Theory]
    [InlineData(false, 0.6f)]
    [InlineData(true, 0.42f)]
    public void TransmissiveSurfaces_WriteTheirCoverageAsTheReactiveMask(bool thinWalled, float coverage)
    {
        DarkWorld();
        using FullscreenProgram program = _renderer.CreateFullscreenProgram("""
            uniform sampler2D _Reactive;
            void main() { outColor = vec4(texture(_Reactive, vUV).r, 0.0, 0.0, 1.0); }
            """, source: false);
        _world.Features.Add(new ShowReactive(program, _renderer.Effects.PointClamp));
        AddBackdrop(_renderer.CreateMaterial(_renderer.Standard.Unlit));
        _world.Add(new RenderableDesc { Mesh = FacingQuad, Transform = Double4x4.CreateTranslation(_origin + new Double3(0, 0, QuadDepth)) * Double4x4.CreateScale(0.5) }, [Glass(m =>
        {
            m.SetFloat("transmission_weight", 0.7f);
            m.SetFloat("geometry_opacity", 0.6f);
            m.SetInt("geometry_thin_walled", thinWalled ? 1 : 0);
        })]);
        Frame();
        Float4[] pixels = Frame();

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                Vector3 position = PixelPosition(x, y);
                float want = MathF.Abs(position.X) < 2 && MathF.Abs(position.Y) < 2 ? coverage : 0;
                float got = pixels[y * Size + x].X;
                Assert.True(MathF.Abs(got - want) <= 3e-3f, $"pixel {x},{y}: expected {want}, got {got}");
            }
        }
    }

    private Float4[] FrameOf(ViewDesc view) => _harness.Frame(view);

    private ViewDesc ViewWith(int id, params RenderFeature[] features)
    {
        FeatureSet set = new();
        foreach (RenderFeature feature in features)
            set.Add(feature);
        ViewDesc view = new() { World = _world, EV100 = _ev100, ViewId = id, Target = _framebuffer, Features = set };
        view.Layers[0] = new ViewLayer { View = Float4x4.Identity, Projection = RendererHarness.Projection, Position = _origin };
        return view;
    }

    // A view running TAA has the reactive target only on frames it draws a transmissive surface
    [Fact]
    public void TheReactiveTarget_ExistsOnlyWithTAAAndTransmission()
    {
        DarkWorld();
        AddBackdrop(_renderer.CreateMaterial(_renderer.Standard.Unlit));
        int Targets(int id) => ViewGraph.SceneTargetCount(_renderer.Frame.Camera(id)!.ShapeKey.Targets);
        ViewDesc taa = ViewWith(7, new TemporalAntiAliasing(_renderer));
        FrameOf(taa);
        Assert.Equal(2, Targets(7));

        Material glass = Glass(m => m.SetFloat("transmission_weight", 1));
        RenderableHandle handle = _world.Add(new RenderableDesc { Mesh = FacingQuad, Transform = Double4x4.CreateTranslation(_origin + new Double3(0, 0, QuadDepth)) }, [glass]);
        FrameOf(taa);
        Assert.Equal(3, Targets(7));
        FrameOf(ViewWith(8));
        Assert.Equal(1, Targets(8));

        glass.SetFloat("transmission_weight", 0);
        FrameOf(taa);
        Assert.Equal(2, Targets(7));
        _world.Remove(handle);
        FrameOf(taa);
        Assert.Equal(2, Targets(7));
    }

    // Shows the scene's motion in red and green, read after the transparent surfaces
    private sealed class ShowMotion(FullscreenProgram program, Sampler sampler) : RenderFeature
    {
        private readonly PropertySet _properties = new();

        public override RenderSlot Slot => RenderSlot.AfterTransparents;
        public override SceneTargets SceneReads => SceneTargets.Motion;

        public override void Setup(FeatureBuilder builder, in FeatureView view)
        {
            builder.ReadScene();
            builder.WriteColor();
        }

        public override void Render(FeatureContext context)
        {
            _properties.SetTexture("_Motion", context.Motion!, sampler);
            context.Fullscreen(program, _properties);
        }
    }

    // A transmissive quad moving 1 to the right over a still backdrop: the uv it moved, an eighth of the view, where it
    // is, and none where only the backdrop shows
    [Fact]
    public void TransmissiveSurfaces_WriteTheirMotion()
    {
        DarkWorld();
        using FullscreenProgram program = _renderer.CreateFullscreenProgram("""
            uniform sampler2D _Motion;
            void main() { outColor = vec4(texture(_Motion, vUV).rg, 0.0, 1.0); }
            """, source: false);
        _world.Features.Add(new ShowMotion(program, _renderer.Effects.PointClamp));
        AddBackdrop(_renderer.CreateMaterial(_renderer.Standard.Unlit));
        Double4x4 At(double x) => Double4x4.CreateTranslation(_origin + new Double3(x, 0, QuadDepth)) * Double4x4.CreateScale(0.5);
        RenderableHandle glass = _world.Add(new RenderableDesc { Mesh = FacingQuad, Transform = At(0), Flags = RenderableFlags.MotionVectors }, [Glass(m => m.SetFloat("transmission_weight", 1))]);
        Frame();
        Frame();
        _world.SetTransform(glass, At(1));
        Float4[] pixels = Frame();

        int moving = 0, still = 0;
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                Vector3 position = PixelPosition(x, y);
                bool inside = position.X > -0.5f && position.X < 2.5f && MathF.Abs(position.Y) < 1.5f;
                bool outside = position.X < -1.5f || position.X > 3.5f || MathF.Abs(position.Y) > 2.5f;
                if (!inside && !outside)
                    continue;
                Float4 got = pixels[y * Size + x];
                float want = inside ? 0.125f : 0;
                Assert.True(MathF.Abs(got.X - want) <= 1e-3f && MathF.Abs(got.Y) <= 1e-3f, $"pixel {x},{y}: expected ({want}, 0), got ({got.X}, {got.Y})");
                if (inside)
                    moving++;
                else
                    still++;
            }
        }
        Assert.True(moving > 0 && still > 0);
    }

    // A bright stripe in the backdrop's own space, so it moves with the backdrop
    private const string StripeSource = """
        Shader "Test/Stripe"
        {
            Pass
            {
                Tags { "LightMode" = "Forward" }
                Cull Off
                GLSLPROGRAM
                Vertex
                {
                    #include "Graphite/Vertex.glsl"
                    layout(location = 0) out float vObjectX;
                    void main()
                    {
                        vObjectX = vertexPosition.x;
                        gl_Position = Graphite_RelativeToClip(Graphite_ObjectToRelative(vertexPosition));
                    }
                }
                Fragment
                {
                    #include "Graphite/Passes.glsl"
                    layout(location = 0) in float vObjectX;
                    layout(location = 0) out vec4 outColor;
                    void main()
                    {
                        Graphite_WriteGBuffer(vec3(0.0, 0.0, -1.0), vec3(0.0), 1.0);
                        outColor = vec4(vec3(abs(vObjectX) < 0.25 ? 1.0 : 0.05), 1.0);
                    }
                }
                ENDGLSL
            }
        }
        """;

    // Under TAA a stripe sliding behind a still, clear pane ghosts: the pane writes its own motion, so history is
    // reprojected from where the stripe was. Through the reactive mask the pane keeps less history, so its error against
    // the frame drawn without TAA falls well below what it is without the mask, toward the bare backdrop's.
    [Fact]
    public void TheReactiveMask_CutsGhostingBehindTransmission()
    {
        DarkWorld();
        Material stripe = _renderer.CreateMaterial(_renderer.LoadShader(ShaderParser.Parse(StripeSource)));
        Double4x4 At(double x) => Double4x4.CreateTranslation(_origin + new Double3(x, 0, 20)) * Double4x4.CreateScale(4);
        RenderableHandle backdrop = _world.Add(new RenderableDesc { Mesh = FacingQuad, Transform = At(-3), Flags = RenderableFlags.MotionVectors }, [stripe]);
        Material glass = Glass(m =>
        {
            m.SetColor("base_color", new Float4(0, 0, 0, 1));
            m.SetFloat("specular_weight", 0);
            m.SetFloat("transmission_weight", 1);
            m.SetInt("geometry_thin_walled", 1);
        });
        RenderableHandle? pane = null;

        float Ghosting(int id, float reactiveBlend, bool withPane)
        {
            if (withPane && pane == null)
                pane = _world.Add(new RenderableDesc { Mesh = FacingQuad, Transform = Double4x4.CreateTranslation(_origin + new Double3(0, 0, QuadDepth)) * Double4x4.CreateScale(0.75) }, [glass]);
            ViewDesc view = ViewWith(id, new TemporalAntiAliasing(_renderer) { ReactiveBlendFactor = reactiveBlend });
            Float4[] taa = [];
            for (int frame = 0; frame <= 12; frame++)
            {
                _world.SetTransform(backdrop, At(-3 + frame * 0.5));
                taa = FrameOf(view);
            }
            Float4[] reference = FrameOf(ViewWith(id + 100));
            float error = 0;
            int count = 0;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    Vector3 position = PixelPosition(x, y);
                    if (MathF.Abs(position.X) > 2.5f || MathF.Abs(position.Y) > 2.5f)
                        continue;
                    error += MathF.Abs(taa[y * Size + x].X - reference[y * Size + x].X);
                    count++;
                }
            }
            return error / count;
        }

        float bare = Ghosting(20, 0.5f, withPane: false);
        float unmasked = Ghosting(30, 0.99f, withPane: true);
        float masked = Ghosting(40, 0.5f, withPane: true);
        string numbers = $"masked {masked}, unmasked {unmasked}, bare backdrop {bare}";
        Assert.True(masked < 0.75f * unmasked, numbers);
        Assert.True(masked < unmasked - 0.5f * (unmasked - bare), numbers);
    }
}
