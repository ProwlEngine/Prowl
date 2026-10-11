using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

using Prowl.Graphite.RenderGraph;
using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;
using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Rendering.Tests;

// Host features in a view's slots: which run, in what order, and what they draw onto
public sealed class FeatureTests : IDisposable
{
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
                Cull Off
                GLSLPROGRAM
                Vertex
                {
                    #include "Graphite/Vertex.glsl"
                    void main() { gl_Position = Graphite_ObjectToClip(vertexPosition); }
                }
                Fragment
                {
                    #include "Graphite/Core.glsl"
                    uniform vec4 _Color;
                    layout(location = 0) out vec4 outColor;
                    void main() { outColor = vec4(_Color.rgb, 1.0); }
                }
                ENDGLSL
            }
        }
        """;

    // Unlit, plus an Outline pass drawing green over everything wherever the renderable is
    private const string OutlinedSource = """
        Shader "Test/Outlined"
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
                    #include "Graphite/Core.glsl"
                    layout(location = 0) out vec4 outColor;
                    void main() { outColor = vec4(1.0, 0.0, 0.0, 1.0); }
                }
                ENDGLSL
            }

            Pass
            {
                Tags { "LightMode" = "Outline" }
                Cull Off
                ZTest Always
                ZWrite Off
                GLSLPROGRAM
                Vertex
                {
                    #include "Graphite/Vertex.glsl"
                    void main() { gl_Position = Graphite_ObjectToClip(vertexPosition); }
                }
                Fragment
                {
                    #include "Graphite/Core.glsl"
                    layout(location = 0) out vec4 outColor;
                    void main() { outColor = vec4(0.0, 1.0, 0.0, 1.0); }
                }
                ENDGLSL
            }
        }
        """;

    private const int Size = 16;

    // Orthographic and reversed Z: sees x and y from -4 to 4, and z from 0 (depth 1) to 100 (depth 0).
    private static readonly Float4x4 s_projection = new(new Float4(0.25f, 0, 0, 0), new Float4(0, 0.25f, 0, 0), new Float4(0, 0, -0.01f, 0), new Float4(0, 0, 1, 1));

    private readonly GraphicsDevice _device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
    private readonly GlslShaderCompiler _compiler = new();
    private readonly Texture _target;
    private readonly Framebuffer _framebuffer;
    private readonly DeviceBuffer _readback;
    private readonly Renderer _renderer;
    private readonly RenderWorld _world;
    private readonly Shader _unlit;
    private readonly GpuMesh _quad;
    private readonly List<RendererError> _errors = new();
    private readonly List<IDisposable> _owned = new();

    public FeatureTests()
    {
        _target = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(Size, Size, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.RenderTarget | TextureUsage.Sampled));
        _framebuffer = _device.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, _target));
        _readback = _device.ResourceFactory.CreateBuffer(new BufferDescription(Size * Size * 16, BufferUsage.Staging));
        _renderer = new Renderer(_device, _compiler, new RendererOptions { Threading = RenderThreading.Inline, Deterministic = true, PageVertexWords = 1 << 16, PageIndices = 1 << 16 });
        _renderer.Error += _errors.Add;
        _world = _renderer.CreateWorld();
        _world.Environment = new EnvironmentDesc { AmbientColor = default, Fog = default };
        _unlit = _renderer.LoadShader(ShaderParser.Parse(UnlitSource));

        Float3[] positions = [new(-4, -4, 0), new(4, -4, 0), new(4, 4, 0), new(-4, 4, 0)];
        _quad = _renderer.CreateMesh(new MeshData
        {
            VertexCount = 4,
            Streams = [new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(positions.AsSpan()).ToArray() }],
            Indices = new uint[] { 0, 1, 2, 0, 2, 3 },
        });
    }

    public void Dispose()
    {
        _renderer.Dispose();
        foreach (IDisposable owned in _owned)
            owned.Dispose();
        _readback.Dispose();
        _framebuffer.Dispose();
        _target.Dispose();
        _compiler.Dispose();
        _device.Dispose();
    }

    private void AddQuad(Float3 color, uint layer = ~0u, double scale = 1)
    {
        Material material = _renderer.CreateMaterial(_unlit);
        material.SetColor("_Color", new Float4(color.X, color.Y, color.Z, 1));
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)) * Double4x4.CreateScale(scale), LayerMask = layer }, [material]);
    }

    private static bool Centre(int i) => i % Size is >= 4 and < 12 && i / Size is >= 4 and < 12;

    private ViewDesc View(int id = 1)
    {
        ViewDesc view = new() { World = _world, ViewId = id, Target = _framebuffer, Sky = false, ClearColor = new Float4(0, 0, 0, 1) };
        view.Layers[0] = new ViewLayer { View = Float4x4.Identity, Projection = s_projection };
        return view;
    }

    private Float4[] Frame(params ViewDesc[] views)
    {
        _renderer.SubmitFrame(new FrameDesc { Views = views });
        _device.WaitForIdle();
        _device.Record(cmd => cmd.CopyTextureToBuffer(_target, _readback, 0, TextureRegion.Whole(_target, 0, 0))).Wait();
        Float4[] pixels = _device.Map<Float4>(_readback).ToArray();
        _device.Unmap(_readback);
        Assert.Empty(_errors);
        return pixels;
    }

    private static void AssertAll(Float3 expected, Float4[] pixels, Func<int, bool>? where = null)
    {
        for (int i = 0; i < pixels.Length; i++)
        {
            if (where != null && !where(i))
                continue;
            Float4 p = pixels[i];
            Assert.True(MathF.Abs(p.X - expected.X) < 1e-3f && MathF.Abs(p.Y - expected.Y) < 1e-3f && MathF.Abs(p.Z - expected.Z) < 1e-3f, $"pixel {i}: expected {expected}, got {p}");
        }
    }

    private FullscreenProgram Program(string fragment)
    {
        FullscreenProgram program = _renderer.CreateFullscreenProgram(fragment);
        _owned.Add(program);
        return program;
    }

    // A fullscreen program writing the next colour, with the order and slot given
    private sealed class Effect(FullscreenProgram program, RenderSlot slot, int order = 0, PropertySet? properties = null) : RenderFeature
    {
        public override RenderSlot Slot => slot;
        public override int Order => order;
        public override void Setup(FeatureBuilder builder, in FeatureView view) => builder.WriteColor();
        public override void Render(FeatureContext context) => context.Fullscreen(program, properties);
    }

    // Draws nothing onto the scene in place of a built in feature
    private sealed class Nothing(Type replaces, RenderSlot slot) : RenderFeature
    {
        public override RenderSlot Slot => slot;
        public override Type? Replaces => replaces;
        public override void Setup(FeatureBuilder builder, in FeatureView view) => builder.DrawToColor();
        public override void Render(FeatureContext context) => context.BeginOutput();
    }

    [Fact]
    public void Effects_RunInSlotThenOrder_EachReadingTheLastOnesColour()
    {
        AddQuad(new Float3(1, 0, 0));
        PropertySet half = new();
        half.SetFloat("_Scale", 0.5f);
        FullscreenProgram invert = Program("void main() { outColor = vec4(1.0 - Graphite_SampleSource(vUV).rgb, 1.0); }");
        FullscreenProgram scale = Program("uniform float _Scale; void main() { outColor = vec4(Graphite_SampleSource(vUV).rgb * _Scale, 1.0); }");

        // Added out of order: the tonemap slot runs after the HDR slot whatever the order inside it
        _world.Features.Add(new Effect(scale, RenderSlot.Tonemap, -100, half));
        _world.Features.Add(new Effect(invert, RenderSlot.HdrPost, 5));
        AssertAll(new Float3(0, 0.5f, 0.5f), Frame(View()));

        // Inside one slot, lower order first
        _world.Features.Add(new Effect(invert, RenderSlot.Tonemap, 10));
        AssertAll(new Float3(1, 0.5f, 0.5f), Frame(View()));
    }

    [Fact]
    public void ViewFeatures_RunOnlyOnTheirView_AndDisabledOnesNowhere()
    {
        AddQuad(new Float3(0.25f, 0.5f, 1));
        Effect invert = new(Program("void main() { outColor = vec4(1.0 - Graphite_SampleSource(vUV).rgb, 1.0); }"), RenderSlot.HdrPost);
        ViewDesc inverted = View(2);
        inverted.Features = new FeatureSet { invert };

        AssertAll(new Float3(0.75f, 0.5f, 0), Frame(inverted));
        AssertAll(new Float3(0.25f, 0.5f, 1), Frame(View(3)));

        invert.Enabled = false;
        AssertAll(new Float3(0.25f, 0.5f, 1), Frame(inverted));
    }

    [Fact]
    public void ReplacingTheOpaqueFeature_LeavesTheClearColour()
    {
        AddQuad(new Float3(1, 1, 1));
        ViewDesc view = View();
        view.ClearColor = new Float4(0.1f, 0.2f, 0.3f, 1);
        _world.Features.Add(new Nothing(typeof(OpaqueFeature), RenderSlot.Opaque));
        AssertAll(new Float3(0.1f, 0.2f, 0.3f), Frame(view));
    }

    // Shows the scene's depth, read after the opaque pass
    private sealed class ShowDepth(FullscreenProgram program, Sampler sampler) : RenderFeature
    {
        private readonly PropertySet _properties = new();

        public override RenderSlot Slot => RenderSlot.AfterOpaques;

        public override void Setup(FeatureBuilder builder, in FeatureView view)
        {
            builder.ReadScene();
            builder.WriteColor();
        }

        public override void Render(FeatureContext context)
        {
            _properties.SetTexture("_Depth", context.Depth!, sampler);
            context.Fullscreen(program, _properties);
        }
    }

    [Fact]
    public void Features_ReadTheScenesDepth()
    {
        AddQuad(new Float3(1, 1, 1));
        Sampler point = _device.ResourceFactory.CreateSampler(SamplerDescription.Point);
        _owned.Add(point);
        _world.Features.Add(new ShowDepth(Program("uniform sampler2D _Depth; void main() { outColor = vec4(texture(_Depth, vUV).rrr, 1.0); }"), point));

        // z 10 through the projection is 1 - 0.01 * 10
        AssertAll(new Float3(0.9f, 0.9f, 0.9f), Frame(View()));
    }

    [Fact]
    public void Viewports_DrawSideBySideIntoOneTarget()
    {
        AddQuad(new Float3(1, 0, 0), layer: 1);
        AddQuad(new Float3(0, 1, 0), layer: 2);
        ViewDesc left = View(1);
        left.CullingMask = 1;
        left.Viewport = new Float4(0, 0, 0.5f, 1);
        ViewDesc right = View(2);
        right.CullingMask = 2;
        right.Viewport = new Float4(0.5f, 0, 0.5f, 1);

        Float4[] pixels = Frame(left, right);
        AssertAll(new Float3(1, 0, 0), pixels, i => i % Size < Size / 2);
        AssertAll(new Float3(0, 1, 0), pixels, i => i % Size >= Size / 2);
    }

    // Remembers the render size of the views it runs on
    private sealed class Probe : RenderFeature
    {
        public uint Width, Height;

        public override RenderSlot Slot => RenderSlot.Overlay;

        public override void Setup(FeatureBuilder builder, in FeatureView view)
        {
            Width = view.Width;
            Height = view.Height;
        }

        public override void Render(FeatureContext context) { }
    }

    [Fact]
    public void RenderScale_DrawsSmaller_AndPresentsScaledToFit()
    {
        AddQuad(new Float3(0.5f, 0.25f, 1));
        Probe probe = new();
        _world.Features.Add(probe);
        ViewDesc view = View();
        view.RenderScale = 0.5f;
        view.Viewport = new Float4(0, 0, 1, 0.5f);

        Float4[] pixels = Frame(view);
        Assert.Equal((8u, 4u), (probe.Width, probe.Height));
        AssertAll(new Float3(0.5f, 0.25f, 1), pixels, i => i / Size < Size / 2);
    }

    [Fact]
    public void Views_DrawByOrder_AfterTheViewsTheyDependOn()
    {
        AddQuad(new Float3(1, 0, 0), layer: 1);
        AddQuad(new Float3(0, 1, 0), layer: 2);
        ViewDesc red = View(1);
        red.CullingMask = 1;
        ViewDesc green = View(2);
        green.CullingMask = 2;

        // The last view drawn covers the target
        AssertAll(new Float3(0, 1, 0), Frame(red, green));
        red.Order = 1;
        AssertAll(new Float3(1, 0, 0), Frame(red, green));
        green.DependsOn = [1];
        AssertAll(new Float3(0, 1, 0), Frame(red, green));
    }

    [Fact]
    public void ViewsDependingOnEachOther_AreReported_AndDrawInOrder()
    {
        AddQuad(new Float3(1, 0, 0), layer: 1);
        ViewDesc a = View(1);
        a.DependsOn = [2];
        ViewDesc b = View(2);
        b.CullingMask = 0;
        b.DependsOn = [1];
        _renderer.SubmitFrame(new FrameDesc { Views = new[] { a, b } });
        _device.WaitForIdle();
        _renderer.SubmitFrame(new FrameDesc { Views = new[] { a, b } });
        Assert.Equal("view.dependency.cycle", Assert.Single(_errors).Code);
    }

    private sealed class Named(string name, RenderSlot slot, int order = 0, ViewKinds runsOn = ViewKinds.Cameras, string? group = null, Type? replaces = null) : RenderFeature
    {
        public override RenderSlot Slot => slot;
        public override int Order => order;
        public override ViewKinds RunsOn => runsOn;
        public override string? ExclusiveGroup => group;
        public override Type? Replaces => replaces;
        public override void Setup(FeatureBuilder builder, in FeatureView view) { }
        public override void Render(FeatureContext context) { }
        public override string ToString() => name;
    }

    private static List<string> Select(RenderFeature[] world, RenderFeature[] view, ViewKind kind = ViewKind.Game)
    {
        foreach (RenderFeature feature in world.Concat(view))
            feature.TakeSnapshot();
        ViewDesc desc = new() { Kind = kind };
        List<RenderFeature> result = new();
        FeatureSelection.Select(result, world, view, new FeatureView(desc, 1, 1), _ => { });
        return result.Select(f => f is Named ? f.ToString()! : f.GetType().Name).ToList();
    }

    [Fact]
    public void AColourWrittenBeforeTheOpaques_IsCopiedIntoTheScene_ThatTheyDrawOnto()
    {
        AddQuad(new Float3(1, 0, 0), scale: 0.5);
        _world.Features.Add(new Effect(Program("void main() { outColor = vec4(1.0 - Graphite_SampleSource(vUV).rgb, 1.0); }"), RenderSlot.AfterPrepass));

        // The inverted black clear around a quad the opaque pass draws over it
        Float4[] pixels = Frame(View());
        AssertAll(new Float3(1, 0, 0), pixels, Centre);
        AssertAll(new Float3(1, 1, 1), pixels, i => !Centre(i));
    }

    private sealed class Throws(bool inSetup) : RenderFeature
    {
        public override RenderSlot Slot => RenderSlot.HdrPost;

        public override void Setup(FeatureBuilder builder, in FeatureView view)
        {
            if (inSetup)
                throw new InvalidOperationException("setup");
            builder.WriteColor();
        }

        public override void Render(FeatureContext context) => throw new InvalidOperationException("render");
    }

    [Fact]
    public void FeaturesThatThrow_AreReportedOnce_AndTheViewDrawsOn()
    {
        AddQuad(new Float3(0.25f, 0.5f, 1));
        _world.Features.Add(new Throws(inSetup: true));
        _renderer.SubmitFrame(new FrameDesc { Views = new[] { View() } });
        Assert.Equal("feature.setup", Assert.Single(_errors).Code);
        _errors.Clear();
        AssertAll(new Float3(0.25f, 0.5f, 1), Frame(View()));

        _world.Features.Add(new Throws(inSetup: false));
        for (int frame = 0; frame < 6; frame++)
            _renderer.SubmitFrame(new FrameDesc { Views = new[] { View() } });
        _device.WaitForIdle();
        Assert.Equal("feature.render", Assert.Single(_errors).Code);
    }

    // Inverts the colour once told to, which changes what it declares
    private sealed class Toggle(FullscreenProgram program) : RenderFeature
    {
        public bool On;

        public override RenderSlot Slot => RenderSlot.HdrPost;

        public override void Setup(FeatureBuilder builder, in FeatureView view)
        {
            if (On)
                builder.WriteColor();
        }

        public override void Render(FeatureContext context)
        {
            if (On)
                context.Fullscreen(program);
        }
    }

    [Fact]
    public void InvalidatingAFeature_RebuildsWhatItDeclares()
    {
        AddQuad(new Float3(0.25f, 0.5f, 1));
        Toggle toggle = new(Program("void main() { outColor = vec4(1.0 - Graphite_SampleSource(vUV).rgb, 1.0); }"));
        _world.Features.Add(toggle);
        AssertAll(new Float3(0.25f, 0.5f, 1), Frame(View()));
        toggle.On = true;
        toggle.Invalidate();
        AssertAll(new Float3(0.75f, 0.5f, 0), Frame(View()));
    }

    [Fact]
    public void AViewportNothingDrewInto_ShowsItsClearColour_AndTheRestOfTheTargetStays()
    {
        AddQuad(new Float3(1, 0, 0));
        Frame(View());

        ViewDesc empty = View(2);
        empty.Viewport = new Float4(0, 0, 0.5f, 1);
        empty.ClearColor = new Float4(0, 0, 1, 1);
        empty.Features = new FeatureSet
        {
            new Named("no prepass", RenderSlot.Prepass, replaces: typeof(PrepassFeature)),
            new Named("no opaque", RenderSlot.Opaque, replaces: typeof(OpaqueFeature)),
            new Named("no sky", RenderSlot.Sky, replaces: typeof(SkyFeature)),
            new Named("no transparent", RenderSlot.Transparent, replaces: typeof(TransparentFeature)),
        };
        Float4[] pixels = Frame(empty);
        AssertAll(new Float3(0, 0, 1), pixels, i => i % Size < Size / 2);
        AssertAll(new Float3(1, 0, 0), pixels, i => i % Size >= Size / 2);

        // Letterboxed, the rest of the target takes the clear colour too
        ViewDesc letterboxed = View(3);
        letterboxed.Viewport = new Float4(0, 0, 0.5f, 1);
        letterboxed.ClearColor = new Float4(0, 1, 0, 1);
        letterboxed.ClearTarget = true;
        pixels = Frame(letterboxed);
        AssertAll(new Float3(1, 0, 0), pixels, i => i % Size < Size / 2);
        AssertAll(new Float3(0, 1, 0), pixels, i => i % Size >= Size / 2);
    }

    [Fact]
    public void Selection_AReplacementTakesTheReplacedGroup()
    {
        Named worldSky = new("world sky", RenderSlot.Sky, replaces: typeof(SkyFeature));
        Named viewSky = new("view sky", RenderSlot.Sky, group: "Sky");
        List<string> both = Select([worldSky], [viewSky]);
        Assert.Contains("view sky", both);
        Assert.DoesNotContain("world sky", both);
        Assert.DoesNotContain("SkyFeature", both);

        // Only a concrete feature type can be replaced
        Named everything = new("everything", RenderSlot.Opaque, replaces: typeof(RenderFeature));
        Assert.Contains("OpaqueFeature", Select([everything], []));
    }

    private sealed class Outlines : RenderFeature
    {
        public static readonly PassType Outline = new("Outline");

        public override RenderSlot Slot => RenderSlot.AfterOpaques;

        public override void Setup(FeatureBuilder builder, in FeatureView view)
        {
            builder.DrawsPass(Outline);
            builder.DrawToScene();
        }

        public override void Render(FeatureContext context)
        {
            context.BeginOutput();
            context.DrawPass(Outline);
        }
    }

    [Fact]
    public void FeaturesDrawAnyPassType_IncludingRenderablesAddedLater()
    {
        Shader outlined = _renderer.LoadShader(ShaderParser.Parse(OutlinedSource));
        Material material = _renderer.CreateMaterial(outlined);
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)) * Double4x4.CreateScale(0.5) }, [material]);
        _world.Features.Add(new Outlines());

        Float4[] pixels = Frame(View());
        AssertAll(new Float3(0, 1, 0), pixels, Centre);
        AssertAll(new Float3(0, 0, 0), pixels, i => !Centre(i));

        // A plain renderable stays out of the pass, another outlined one behind everything joins it
        Material plain = _renderer.CreateMaterial(_unlit);
        plain.SetColor("_Color", new Float4(0, 0, 1, 1));
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 20)) }, [plain]);
        pixels = Frame(View());
        AssertAll(new Float3(0, 1, 0), pixels, Centre);
        AssertAll(new Float3(0, 0, 1), pixels, i => !Centre(i));

        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 30)) }, [material]);
        AssertAll(new Float3(0, 1, 0), Frame(View()));
    }

    // Adds a quarter to what it held last frame, and publishes it
    private sealed class Accumulate(FullscreenProgram program) : RenderFeature
    {
        private readonly PropertySet _properties = new();
        private TextureHandle _total;

        public override RenderSlot Slot => RenderSlot.HdrPost;

        public override void Setup(FeatureBuilder builder, in FeatureView view)
        {
            builder.ReadColor();
            _total = builder.History("Total", GraphTextureDesc.ViewSized(PixelFormat.R16_G16_B16_A16_Float));
            builder.Publish("Total", _total);
        }

        public override void Render(FeatureContext context)
        {
            RenderTexture? previous = context.History(_total);
            context.Command.SetFramebuffer(context.Get(_total).Framebuffer, new TargetLoadStoreOps(new AttachmentOps(LoadAction.DontCare, StoreAction.Store), AttachmentOps.Loaded));
            context.Command.SetFullViewport();
            context.Command.SetShader(program.For(1));
            context.Command.SetVertexSource(VertexSource.None);
            _properties.SetTexture("Graphite_Source", context.Color!);
            _properties.SetTexture("_Previous", previous?.ColorTextures[0] ?? context.Color!);
            _properties.SetFloat("_HasPrevious", previous != null ? 1 : 0);
            context.Command.SetProperties(_properties);
            context.Command.Draw(3);
        }
    }

    // Shows what an earlier feature published
    private sealed class Show(FullscreenProgram program) : RenderFeature
    {
        private readonly PropertySet _properties = new();
        private TextureHandle _shown;

        public override RenderSlot Slot => RenderSlot.Tonemap;

        public override void Setup(FeatureBuilder builder, in FeatureView view)
        {
            _shown = builder.Read("Total");
            builder.WriteColor();
        }

        public override void Render(FeatureContext context)
        {
            _properties.SetTexture("_Shown", context.Get(_shown).ColorTextures[0]);
            context.Fullscreen(program, _properties);
        }
    }

    [Fact]
    public void HistoryTextures_HoldWhatTheFeatureWroteLastFrame()
    {
        _world.Features.Add(new Accumulate(Program("uniform sampler2D _Previous; uniform float _HasPrevious; void main() { outColor = vec4(texture(_Previous, vUV).rgb * _HasPrevious + 0.25, 1.0); }")));
        _world.Features.Add(new Show(Program("uniform sampler2D _Shown; void main() { outColor = texture(_Shown, vUV) + 0.0 * Graphite_SampleSource(vUV); }")));
        AssertAll(new Float3(0.25f, 0.25f, 0.25f), Frame(View()));
        AssertAll(new Float3(0.5f, 0.5f, 0.5f), Frame(View()));
        AssertAll(new Float3(0.75f, 0.75f, 0.75f), Frame(View()));
    }

    private sealed class Counter
    {
        public int Frames;
    }

    private sealed class Counts : RenderFeature
    {
        public readonly Dictionary<int, int> Seen = new();

        public override RenderSlot Slot => RenderSlot.Overlay;
        public override void Setup(FeatureBuilder builder, in FeatureView view) { }
        public override void Render(FeatureContext context) => Seen[context.View.ViewId] = ++context.GetViewState<Counter>().Frames;
    }

    [Fact]
    public void ViewState_IsKeptPerView()
    {
        Counts counts = new();
        _world.Features.Add(counts);
        Frame(View(1), View(2));
        Frame(View(1));
        Frame(View(1));
        Assert.Equal(3, counts.Seen[1]);
        Assert.Equal(1, counts.Seen[2]);
    }

    [Fact]
    public void Selection_OrdersBySlotThenOrder_BuiltInBeforeWorldBeforeViewOnTies()
    {
        Named view = new("view", RenderSlot.Opaque);
        Named world = new("world", RenderSlot.Opaque);
        Named early = new("early", RenderSlot.Opaque, -1);
        Named post = new("post", RenderSlot.HdrPost);
        Assert.Equal(["PrepassFeature", "early", "OpaqueFeature", "world", "view", "SkyFeature", "TransparentFeature", "post"], Select([post, world, early], [view]));
    }

    [Fact]
    public void Selection_ViewBeatsWorldBeatsBuiltIn_ForGroupsAndReplacements()
    {
        Named worldSky = new("world sky", RenderSlot.Sky, group: "Sky");
        Named viewSky = new("view sky", RenderSlot.Sky, group: "Sky");
        Assert.DoesNotContain("SkyFeature", Select([worldSky], []));
        Assert.Contains("world sky", Select([worldSky], []));
        List<string> both = Select([worldSky], [viewSky]);
        Assert.Contains("view sky", both);
        Assert.DoesNotContain("world sky", both);

        Named opaque = new("opaque", RenderSlot.Opaque, replaces: typeof(OpaqueFeature));
        Assert.DoesNotContain("OpaqueFeature", Select([opaque], []));
        Assert.Contains("opaque", Select([opaque], []));
    }

    [Fact]
    public void Selection_SkipsDisabledFeatures_AndOnesNotRunningOnTheViewsKind()
    {
        Named cameras = new("cameras", RenderSlot.HdrPost);
        Named probes = new("probes", RenderSlot.HdrPost, runsOn: ViewKinds.ReflectionProbe);
        Named off = new("off", RenderSlot.HdrPost, runsOn: ViewKinds.All) { Enabled = false };

        Assert.Equal(["PrepassFeature", "OpaqueFeature", "SkyFeature", "TransparentFeature", "cameras"], Select([cameras, probes, off], []));
        Assert.Equal(["PrepassFeature", "OpaqueFeature", "SkyFeature", "TransparentFeature", "probes"], Select([cameras, probes, off], [], ViewKind.ReflectionProbe));
    }
}
