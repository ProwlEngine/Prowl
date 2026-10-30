using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;
using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Rendering.Tests;

// Host features in a view's slots: which run, in what order, and what they draw onto
[Collection("GPU Tests")]
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
                    #include "Graphite/Passes.glsl"
                    #include "Graphite/Core.glsl"
                    uniform vec4 _Color;
                    layout(location = 0) out vec4 outColor;
                    void main() { Graphite_WriteGBuffer(vec3(0.0, 0.0, -1.0), vec3(0.0), 1.0); outColor = vec4(_Color.rgb, 1.0); }
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
                    #include "Graphite/Passes.glsl"
                    #include "Graphite/Core.glsl"
                    layout(location = 0) out vec4 outColor;
                    void main() { Graphite_WriteGBuffer(vec3(0.0, 0.0, -1.0), vec3(0.0), 1.0); outColor = vec4(1.0, 0.0, 0.0, 1.0); }
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

    // Transparent, drawing nothing in its forward pass and its tint half blended in a Glow pass
    private const string GlowSource = """
        Shader "Test/Glow"
        {
            Properties
            {
                _Color("Color", Color) = (1, 1, 1, 0.5)
            }

            Pass
            {
                Tags { "LightMode" = "Forward" "RenderQueue" = "3000" }
                Cull Off
                ZWrite Off
                Blend SourceAlpha InverseSourceAlpha
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
                    void main() { outColor = vec4(0.0); }
                }
                ENDGLSL
            }

            Pass
            {
                Tags { "LightMode" = "Glow" }
                Cull Off
                ZTest Always
                ZWrite Off
                Blend SourceAlpha InverseSourceAlpha
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
                    void main() { outColor = _Color; }
                }
                ENDGLSL
            }
        }
        """;

    // Colours with the object's Custom0, shifting each instance of a shared draw a quarter of the view along X
    private const string ObjectColorSource = """
        Shader "Test/ObjectColor"
        {
            Properties
            {
                _Shift("Shift", Float) = 0
            }

            Pass
            {
                Tags { "LightMode" = "Forward" }
                Cull Off
                GLSLPROGRAM
                Vertex
                {
                    #include "Graphite/Vertex.glsl"
                    uniform float _Shift;
                    void main()
                    {
                        vec3 p = vertexPosition + vec3(float(Graphite_InstanceIndex()) * _Shift, 0.0, 0.0);
                        gl_Position = Graphite_ObjectToClip(p);
                    }
                }
                Fragment
                {
                    #include "Graphite/Core.glsl"
                    #include "Graphite/Passes.glsl"
                    layout(location = 0) out vec4 outColor;
                    void main()
                    {
                        outColor = vec4(Graphite_Object().custom0.rgb, 1.0);
                        Graphite_WriteGBuffer(vec3(0.0, 0.0, -1.0), vec3(0.0), 1.0);
                    }
                }
                ENDGLSL
            }
        }
        """;

    // The vertex colour, for inline transient geometry
    private const string VertexColorSource = """
        Shader "Test/VertexColor"
        {
            Pass
            {
                Tags { "LightMode" = "Forward" }
                Cull Off
                GLSLPROGRAM
                Vertex
                {
                    #include "Graphite/Vertex.glsl"
                    layout(location = 0) out vec4 vColor;
                    void main()
                    {
                        vColor = vertexColor;
                        gl_Position = Graphite_ObjectToClip(vertexPosition);
                    }
                }
                Fragment
                {
                    #include "Graphite/Core.glsl"
                    #include "Graphite/Passes.glsl"
                    layout(location = 0) in vec4 vColor;
                    layout(location = 0) out vec4 outColor;
                    void main()
                    {
                        outColor = vec4(vColor.rgb, 1.0);
                        Graphite_WriteGBuffer(vec3(0.0, 0.0, -1.0), vec3(0.0), 1.0);
                    }
                }
                ENDGLSL
            }
        }
        """;

    // Half the colour of the object's Custom0, blended over what is behind
    private const string ObjectGlassSource = """
        Shader "Test/ObjectGlass"
        {
            Pass
            {
                Tags { "LightMode" = "Forward" "RenderQueue" = "3000" }
                Cull Off
                ZWrite Off
                Blend SourceAlpha InverseSourceAlpha
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
                    void main() { outColor = vec4(Graphite_Object().custom0.rgb, 0.5); }
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
    private readonly RendererOptions _options = new() { Threading = RenderThreading.Inline, Deterministic = true, PageVertexWords = 1 << 16, PageIndices = 1 << 16 };
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
        _renderer = new Renderer(_device, _compiler, _options);
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
    private sealed class Nothing(string group, RenderSlot slot) : RenderFeature
    {
        public override RenderSlot Slot => slot;
        public override string? ExclusiveGroup => group;
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
    public void LitFullscreenPrograms_ReadTheWorldsLights()
    {
        AddQuad(new Float3(0, 0, 0));
        _world.AddLight(new LightDesc { Type = LightType.Directional, Direction = new Float3(0, 0, 1), Intensity = 1f });
        FullscreenProgram lit = _renderer.CreateFullscreenProgram("""
            #include "Graphite/Lighting.glsl"
            uniform float _Scale;
            void main()
            {
                // The sun shines along +Z, so from the surface it lies along -Z
                Graphite_DirectionalLight sun = Graphite_GetDirectional(0u);
                outColor = vec4(float(Graphite_DirectionalCount()) * _Scale, sun.direction.z * 0.5 + 0.5, 0.5, 1.0);
            }
            """, "Lit", lit: true);
        _owned.Add(lit);
        Assert.True(lit.Lit);

        PropertySet properties = new();
        properties.SetFloat("_Scale", 0.25f);
        _world.Features.Add(new LitEffectWith(lit, properties));
        AssertAll(new Float3(0.25f, 0f, 0.5f), Frame(View()));
    }

    // A lit fullscreen program reading the world's lights through the library, as forward passes do
    private sealed class LitEffectWith(FullscreenProgram program, PropertySet properties) : RenderFeature
    {
        public override RenderSlot Slot => RenderSlot.HdrPost;
        public override void Setup(FeatureBuilder builder, in FeatureView view)
        {
            builder.DrawsRenderables();
            builder.WriteColor();
        }
        public override void Render(FeatureContext context) => context.Fullscreen(program, properties);
    }

    [Fact]
    public void SurfaceFunctions_ChangeTheStandardSurface_BeforeItDraws()
    {
        Shader shader = _renderer.LoadShader(ShaderParser.Parse("""
            Shader "Tests/Blue"
            {
                Properties { _Blue("Blue", Float) = 1.0 }
                Surface "Unlit"
                {
                    GLSLPROGRAM
                    uniform float _Blue;
                    void Surface(inout Unlit_Params p, Graphite_SurfaceData i) { p.BaseColor = vec3(0.0, p.BaseColor.g * 0.5, _Blue); }
                    ENDGLSL
                }
            }
            """));
        Material material = _renderer.CreateMaterial(shader);
        material.Surface = new SurfaceSettings(Cull: SurfaceCull.Off);
        material.SetColor("_MainColor", new Float4(0, 1, 0, 1));
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)) * Double4x4.CreateScale(0.5) }, [material]);

        Float4[] pixels = Frame(View());
        AssertAll(new Float3(0, 0.5f, 1), pixels, Centre);
    }

    [Theory]
    [InlineData(null, 0f, 0.5f, 1f)]
    [InlineData("ALBEDO", 0f, 0.5f, 1f)]
    [InlineData("NORMALS", 0.5f, 0.5f, 0.5f)]
    public void DebugViews_DrawSurfacesThroughTheirDebugPass_WithTheSurfaceFunction(string? mode, float r, float g, float b)
    {
        Shader shader = _renderer.LoadShader(ShaderParser.Parse("""
            Shader "Tests/DebugBlue"
            {
                Surface "Unlit"
                {
                    GLSLPROGRAM
                    void Surface(inout Unlit_Params p, Graphite_SurfaceData i) { p.BaseColor = vec3(0.0, p.BaseColor.g * 0.5, 1.0); }
                    ENDGLSL
                }
            }
            """));
        Material material = _renderer.CreateMaterial(shader);
        material.Surface = new SurfaceSettings(Cull: SurfaceCull.Off);
        material.SetColor("_MainColor", new Float4(0, 1, 0, 1));
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)) * Double4x4.CreateScale(0.5) }, [material]);

        ViewDesc view = View();
        if (mode != null)
            view.Keywords = [new Keyword(Shader.DebugAxis, mode)];
        Float4[] pixels = Frame(view);
        AssertAll(new Float3(r, g, b), pixels, Centre);
    }

    [Fact]
    public void OverlayViews_DrawOverTheViewsBefore_LeavingTheRestShowing()
    {
        AddQuad(new Float3(0, 1, 0), layer: 2u, scale: 0.5);
        ViewDesc under = View(1);
        under.ClearColor = new Float4(1, 0, 0, 1);
        under.CullingMask = 1u;
        ViewDesc over = View(2);
        over.ClearColor = new Float4(0, 0, 1, 1);
        over.CullingMask = 2u;
        over.Overlay = true;
        over.Order = 1;

        Float4[] pixels = Frame(under, over);
        AssertAll(new Float3(0, 1, 0), pixels, Centre);
        AssertAll(new Float3(1, 0, 0), pixels, i => !Centre(i) && i % Size is 0 or Size - 1);
    }

    [Fact]
    public void OverlayViews_BlendTheirTransparentSurfacesOverTheViewsBefore()
    {
        Material glass = _renderer.CreateMaterial(_renderer.Standard.Unlit);
        glass.Surface = new SurfaceSettings(SurfaceType.Transparent, Cull: SurfaceCull.Off);
        glass.SetColor("_MainColor", new Float4(0, 0, 1, 0.5f));
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)) * Double4x4.CreateScale(0.5), LayerMask = 2u }, [glass]);
        ViewDesc under = View(1);
        under.ClearColor = new Float4(1, 0, 0, 1);
        under.CullingMask = 1u;
        ViewDesc over = View(2);
        over.CullingMask = 2u;
        over.Overlay = true;
        over.Order = 1;

        Float4[] pixels = Frame(under, over);
        AssertAll(new Float3(0.5f, 0, 0.5f), pixels, Centre);
        AssertAll(new Float3(1, 0, 0), pixels, i => !Centre(i) && i % Size is 0 or Size - 1);
    }

    [Fact]
    public void ProceduralPrograms_DrawTheirOwnGeometry_BlendingOverTheTarget()
    {
        BlendStateDescription add = new(default, new BlendAttachmentDescription
        {
            BlendEnabled = true,
            SourceColorFactor = BlendFactor.One,
            DestinationColorFactor = BlendFactor.One,
            ColorFunction = BlendFunction.Add,
            SourceAlphaFactor = BlendFactor.One,
            DestinationAlphaFactor = BlendFactor.One,
            AlphaFunction = BlendFunction.Add,
        });
        FullscreenProgram quad = _renderer.CreateProceduralProgram("""
            #include "Graphite/Internal/PostView.glsl"
            uniform vec4 _Color;
            uniform float _Left;
            #ifdef GRAPHITE_VERTEX
            void main()
            {
                // Two triangles over the left half, scaled by the view's width over itself to read the view here
                Graphite_ViewData view = Graphite_PostView();
                int corner = int[](0, 1, 2, 1, 3, 2)[gl_VertexIndex];
                vec2 p = vec2(mix(-1.0, _Left, float(corner & 1)), (corner >> 1) == 0 ? -1.0 : 1.0);
                gl_Position = vec4(p * view.screen.x * view.screen.z, 0.0, 1.0);
            }
            #else
            void main() { outColor = _Color; }
            #endif
            """, "Quad", add);
        FullscreenProgram copy = Program("uniform sampler2D _Drawn; void main() { outColor = texture(_Drawn, vUV); }");
        _owned.Add(quad);
        Assert.True(quad.Procedural);

        Sampler point = _device.ResourceFactory.CreateSampler(SamplerDescription.Point);
        _owned.Add(point);
        _world.Features.Add(new ProceduralEffect(quad, copy, point));
        AssertAll(new Float3(0.5f, 0.25f, 0f), Frame(View()), i => i % Size < Size / 2);
        AssertAll(new Float3(0f, 0f, 0f), Frame(View()), i => i % Size >= Size / 2);
    }

    // Draws a procedural program twice into a texture of its own, once cleared and once blended over, then shows it
    private sealed class ProceduralEffect(FullscreenProgram quad, FullscreenProgram copy, Sampler sampler) : RenderFeature
    {
        private readonly PropertySet _properties = new();
        private TextureHandle _drawn;

        public override RenderSlot Slot => RenderSlot.HdrPost;
        public override void Setup(FeatureBuilder builder, in FeatureView view)
        {
            builder.WriteColor();
            _drawn = builder.Temporary("Drawn", view.Texture(PixelFormat.R32_G32_B32_A32_Float));
        }
        public override void Render(FeatureContext context)
        {
            RenderTexture drawn = context.Get(_drawn);
            _properties.SetFloat("_Left", 0f);
            _properties.SetFloat4("_Color", new Float4(0.25f, 0.125f, 0f, 0f));
            context.DrawProcedural(quad, drawn, 6, _properties, clear: true);
            context.DrawProcedural(quad, drawn, 6, _properties);
            _properties.SetTexture("_Drawn", drawn.ColorTextures[0], sampler);
            context.Fullscreen(copy, _properties);
        }
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
        _world.Features.Add(new Nothing("Opaque", RenderSlot.Opaque));
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

    // Shows the scene's motion in red and green, read after the opaque pass
    private sealed class ShowMotion(FullscreenProgram program, Sampler sampler) : RenderFeature
    {
        private readonly PropertySet _properties = new();

        public override RenderSlot Slot => RenderSlot.AfterOpaques;
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

    private void ShowingMotion()
    {
        Sampler point = _device.ResourceFactory.CreateSampler(SamplerDescription.Point);
        _owned.Add(point);
        _world.Features.Add(new ShowMotion(Program("uniform sampler2D _Motion; void main() { outColor = vec4(texture(_Motion, vUV).rg, 0.0, 1.0); }"), point));
    }

    private RenderableHandle AddMoving(Double3 at, bool motionVectors)
    {
        Material material = _renderer.CreateMaterial(_unlit);
        RenderableFlags flags = RenderableFlags.CastShadows | RenderableFlags.ReceiveShadows | (motionVectors ? RenderableFlags.MotionVectors : 0);
        return _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(at), Flags = flags }, [material]);
    }

    [Fact]
    public void MotionVectors_FollowTheCamera()
    {
        ShowingMotion();
        AddMoving(new Double3(0, 0, 10), motionVectors: false);
        Frame(View());

        // The camera a unit right moves the plane a quarter of the view left in clip space, an eighth of it in uv
        ViewDesc moved = View();
        moved.Layers[0] = moved.Layers[0] with { Position = new Double3(1, 0, 0) };
        AssertAll(new Float3(-0.125f, 0, 0), Frame(moved), i => i % Size < Size - 2);
    }

    private sealed class ViewPosition(FullscreenProgram program) : RenderFeature
    {
        public Double3 Seen;
        public override RenderSlot Slot => RenderSlot.HdrPost;
        public override void Setup(FeatureBuilder builder, in FeatureView view) => builder.WriteColor();
        public override void Render(FeatureContext context)
        {
            Seen = context.View.Position;
            context.Fullscreen(program);
        }
    }

    [Fact]
    public void FeatureView_FollowsTheCamera_WithoutARebuild()
    {
        ViewPosition feature = new(Program("void main() { outColor = Graphite_SampleSource(vUV); }"));
        _world.Features.Add(feature);
        Frame(View());

        ViewDesc moved = View();
        moved.Layers[0] = moved.Layers[0] with { Position = new Double3(5, 2, -3) };
        Frame(moved);
        Assert.Equal(new Double3(5, 2, -3), feature.Seen);
    }

    [Fact]
    public void MotionVectors_FollowObjectsThatKeepThem_AndStopWhenTheyDo()
    {
        ShowingMotion();
        RenderableHandle moving = AddMoving(new Double3(0, 0, 10), motionVectors: true);
        Frame(View());

        // Up a unit is a quarter of the view up in clip space, an eighth down the image in uv
        _world.SetTransform(moving, Double4x4.CreateTranslation(new Double3(0, 1, 10)));
        AssertAll(new Float3(0, -0.125f, 0), Frame(View()), i => i / Size >= 2);
        AssertAll(new Float3(0, 0, 0), Frame(View()), i => i / Size >= 2);
    }

    [Fact]
    public void MotionVectors_OfObjectsWithoutThemShowNoObjectMotion()
    {
        ShowingMotion();
        RenderableHandle moved = AddMoving(new Double3(0, 0, 10), motionVectors: false);
        Frame(View());
        _world.SetTransform(moved, Double4x4.CreateTranslation(new Double3(0, 1, 10)));
        AssertAll(new Float3(0, 0, 0), Frame(View()));
    }

    private Material Unlit(Float3 color)
    {
        Material material = _renderer.CreateMaterial(_unlit);
        material.SetColor("_Color", new Float4(color.X, color.Y, color.Z, 1));
        return material;
    }

    // A mesh with the bone streams given and a triangle list over its vertices in order
    private GpuMesh SkinnedMesh(Float3[] positions, ushort[] boneIndices, ushort[] boneWeights, Float3[]? normals = null, Float4[]? tangents = null, BlendShapeData[]? shapes = null)
    {
        uint count = (uint)positions.Length;
        List<VertexStream> streams =
        [
            new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(positions.AsSpan()).ToArray() },
            new VertexStream { Semantic = VertexSemantic.BoneIndices, Format = VertexElementFormat.UShort4, Data = MemoryMarshal.AsBytes(boneIndices.AsSpan()).ToArray() },
            new VertexStream { Semantic = VertexSemantic.BoneWeights, Format = VertexElementFormat.UShort4_Norm, Data = MemoryMarshal.AsBytes(boneWeights.AsSpan()).ToArray() },
        ];
        if (normals != null)
            streams.Add(new VertexStream { Semantic = VertexSemantic.Normal, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(normals.AsSpan()).ToArray() });
        if (tangents != null)
            streams.Add(new VertexStream { Semantic = VertexSemantic.Tangent, Format = VertexElementFormat.Float4, Data = MemoryMarshal.AsBytes(tangents.AsSpan()).ToArray() });
        uint[] indices = [.. Enumerable.Range(0, (int)(count / 3 * 3)).Select(i => (uint)i)];
        GpuMesh mesh = _renderer.CreateMesh(new MeshData { VertexCount = count, Streams = [.. streams], Indices = indices, BlendShapes = shapes ?? [] });
        _owned.Add(mesh);
        return mesh;
    }

    // The quad as two triangles, every corner on bone 1
    private GpuMesh SkinnedQuad()
    {
        Float3[] positions = [new(-4, -4, 0), new(4, -4, 0), new(4, 4, 0), new(-4, -4, 0), new(4, 4, 0), new(-4, 4, 0)];
        ushort[] indices = new ushort[positions.Length * 4];
        ushort[] weights = new ushort[positions.Length * 4];
        for (int v = 0; v < positions.Length; v++)
            (indices[v * 4], weights[v * 4]) = (1, 65535);
        return SkinnedMesh(positions, indices, weights);
    }

    private static Float4x4 Translation(float x, float y, float z) => new(new Float4(1, 0, 0, 0), new Float4(0, 1, 0, 0), new Float4(0, 0, 1, 0), new Float4(x, y, z, 1));

    private static Float3 Transform(in Float4x4 m, Float3 p, float w)
        => new(m.c0.X * p.X + m.c1.X * p.Y + m.c2.X * p.Z + m.c3.X * w,
               m.c0.Y * p.X + m.c1.Y * p.Y + m.c2.Y * p.Z + m.c3.Y * w,
               m.c0.Z * p.X + m.c1.Z * p.Y + m.c2.Z * p.Z + m.c3.Z * w);

    // Prowl's skinning: bone index 0 is none, as are indices past the bones; under 0.01 total weight keeps the rest pose
    private static Float3 CpuSkin(Float3 rest, float w, ReadOnlySpan<ushort> indices, ReadOnlySpan<ushort> weights, Float4x4[] bones)
    {
        Float3 sum = default;
        float total = 0;
        for (int i = 0; i < 4; i++)
        {
            float weight = weights[i] / 65535f;
            total += weight;
            if (indices[i] > 0 && weight > 0 && indices[i] <= bones.Length)
                sum += Transform(bones[indices[i] - 1], rest, w) * weight;
        }
        return total < 0.01f ? rest : sum;
    }

    // A stream of the renderable's deformed copy, read back from its page
    private float[] DeformedStream(RenderableHandle handle, VertexSemantic semantic)
    {
        MeshPlacement placement = _world.Render.Deformed[handle.Index]!;
        uint words = MeshLayout.WordsPerVertex(semantic) * placement.VertexCount;
        byte[]? bytes = null;
        _renderer.Readback(_renderer.Frame.Geometry.Pages[placement.Page].Vertices, data => bytes = data.Data, offset: placement.StreamOffsets[(int)semantic] * 4, size: words * 4);
        _renderer.SubmitFrame(new FrameDesc());
        _device.WaitForIdle();
        _renderer.SubmitFrame(new FrameDesc());
        Assert.NotNull(bytes);
        return MemoryMarshal.Cast<byte, float>(bytes).ToArray();
    }

    [Fact]
    public void Skinning_MatchesACpuSkinOfTheSameMesh()
    {
        Random random = new(7);
        const int count = 96;
        Float3[] positions = new Float3[count], normals = new Float3[count];
        Float4[] tangents = new Float4[count];
        ushort[] indices = new ushort[count * 4], weights = new ushort[count * 4];
        for (int v = 0; v < count; v++)
        {
            positions[v] = new Float3(random.NextSingle() * 4 - 2, random.NextSingle() * 4 - 2, random.NextSingle());
            normals[v] = new Float3(random.NextSingle() - 0.5f, random.NextSingle() - 0.5f, -1);
            tangents[v] = new Float4(1, random.NextSingle() - 0.5f, 0, random.Next(2) * 2 - 1);
            for (int i = 0; i < 4; i++)
            {
                // Bones 1 to 5 of 5, with 0 and 6 meaning none, and every tenth vertex unweighted
                indices[v * 4 + i] = (ushort)random.Next(0, 7);
                weights[v * 4 + i] = v % 10 == 0 ? (ushort)0 : (ushort)random.Next(0, 65536 / 4);
            }
        }
        Float4x4[] bones = new Float4x4[5];
        for (int b = 0; b < bones.Length; b++)
        {
            float angle = random.NextSingle() * 3, c = MathF.Cos(angle), s = MathF.Sin(angle);
            bones[b] = new Float4x4(new Float4(c, s, 0, 0), new Float4(-s, c, 0, 0), new Float4(0, 0, 1.5f, 0), new Float4(random.NextSingle(), random.NextSingle(), random.NextSingle(), 1));
        }

        RenderableHandle handle = _world.Add(new RenderableDesc { Mesh = SkinnedMesh(positions, indices, weights, normals, tangents), Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)) }, [Unlit(new Float3(1, 1, 1))]);
        _world.SetBounds(handle, new AABB(new Float3(-8), new Float3(8)));
        _world.SetBones(handle, bones);
        Frame(View());

        float[] skinnedPositions = DeformedStream(handle, VertexSemantic.Position);
        float[] skinnedNormals = DeformedStream(handle, VertexSemantic.Normal);
        float[] skinnedTangents = DeformedStream(handle, VertexSemantic.Tangent);
        for (int v = 0; v < count; v++)
        {
            ReadOnlySpan<ushort> vi = indices.AsSpan(v * 4, 4), vw = weights.AsSpan(v * 4, 4);
            Float3 position = CpuSkin(positions[v], 1, vi, vw, bones);
            Float3 normal = CpuSkin(normals[v], 0, vi, vw, bones);
            Float3 tangent = CpuSkin(new Float3(tangents[v].X, tangents[v].Y, tangents[v].Z), 0, vi, vw, bones);
            normal /= MathF.Sqrt(normal.X * normal.X + normal.Y * normal.Y + normal.Z * normal.Z);
            tangent /= MathF.Sqrt(tangent.X * tangent.X + tangent.Y * tangent.Y + tangent.Z * tangent.Z);
            for (int c = 0; c < 3; c++)
            {
                Assert.True(MathF.Abs(skinnedPositions[v * 3 + c] - position[c]) < 1e-4f, $"vertex {v} position {c}: {skinnedPositions[v * 3 + c]} against {position[c]}");
                Assert.True(MathF.Abs(skinnedNormals[v * 3 + c] - normal[c]) < 1e-4f, $"vertex {v} normal {c}: {skinnedNormals[v * 3 + c]} against {normal[c]}");
                Assert.True(MathF.Abs(skinnedTangents[v * 4 + c] - tangent[c]) < 1e-4f, $"vertex {v} tangent {c}: {skinnedTangents[v * 4 + c]} against {tangent[c]}");
            }
            Assert.Equal(tangents[v].W, skinnedTangents[v * 4 + 3]);
        }
    }

    [Fact]
    public void Skinning_MovesWhatIsDrawn_AndKeepsItWhileTheBonesStay()
    {
        RenderableHandle handle = _world.Add(new RenderableDesc { Mesh = SkinnedQuad(), Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)) * Double4x4.CreateScale(0.5) }, [Unlit(new Float3(1, 1, 1))]);
        _world.SetBounds(handle, new AABB(new Float3(-8), new Float3(8)));
        _world.SetBones(handle, [Float4x4.Identity]);
        AssertAll(new Float3(1, 1, 1), Frame(View()), Centre);

        // Four object units right is two world units, four pixels
        _world.SetBones(handle, [Translation(4, 0, 0)]);
        Float4[] moved = Frame(View());
        AssertAll(new Float3(1, 1, 1), moved, i => i % Size is >= 8 and < 16 && i / Size is >= 4 and < 12);
        AssertAll(new Float3(0, 0, 0), moved, i => i % Size < 8);
        AssertAll(new Float3(1, 1, 1), Frame(View()), i => i % Size is >= 8 and < 16 && i / Size is >= 4 and < 12);

        // Without bones it draws its mesh as it is
        _world.SetBones(handle, []);
        AssertAll(new Float3(1, 1, 1), Frame(View()), Centre);
    }

    [Fact]
    public void Skinning_WaitsForAViewToSeeTheRenderable()
    {
        RenderableHandle handle = _world.Add(new RenderableDesc { Mesh = SkinnedQuad(), Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)), LayerMask = 2 }, [Unlit(new Float3(1, 1, 1))]);
        _world.SetBounds(handle, new AABB(new Float3(-8), new Float3(8)));
        _world.SetBones(handle, [Translation(1, 0, 0)]);
        ViewDesc view = View();
        view.CullingMask = 1;
        Frame(view);
        Assert.Equal(-4f, DeformedStream(handle, VertexSemantic.Position)[0]);

        _world.SetLayerMask(handle, ~0u);
        Frame(view);
        Assert.Equal(-3f, DeformedStream(handle, VertexSemantic.Position)[0]);
    }

    // Prowl's frame pick: one frame scales by weight over its weight, several interpolate between the two around the
    // weight, clamped to the last, with near zero weights and pairs left out
    private static List<(BlendShapeFrame Frame, float Weight)> CpuActiveFrames(BlendShapeData[] shapes, float[] weights)
    {
        List<(BlendShapeFrame, float)> active = new();
        void Add(BlendShapeFrame frame, float weight)
        {
            if (MathF.Abs(weight) >= 1e-5f)
                active.Add((frame, weight));
        }
        for (int s = 0; s < shapes.Length; s++)
        {
            float w = weights[s];
            BlendShapeFrame[] frames = shapes[s].Frames;
            if (MathF.Abs(w) < 1e-4f || frames.Length == 0)
                continue;
            float first = frames[0].Weight, last = frames[^1].Weight;
            if (frames.Length == 1 || w <= first)
            {
                Add(frames[0], first != 0 ? w / first : 0);
                continue;
            }
            if (w >= last)
            {
                Add(frames[^1], 1);
                continue;
            }
            int i = 0;
            while (i < frames.Length - 1 && frames[i + 1].Weight <= w)
                i++;
            float a = frames[i].Weight, b = frames[i + 1].Weight;
            float t = b > a ? (w - a) / (b - a) : 0;
            Add(frames[i], 1 - t);
            Add(frames[i + 1], t);
        }
        return active;
    }

    private static Float3 CpuMorph(Float3 rest, int vertex, List<(BlendShapeFrame Frame, float Weight)> active, Func<BlendShapeFrame, Float3[]?> deltas)
    {
        foreach ((BlendShapeFrame frame, float weight) in active)
        {
            int at = Array.IndexOf(frame.Vertices, (uint)vertex);
            if (at >= 0 && deltas(frame) is { } d)
                rest += d[at] * weight;
        }
        return rest;
    }

    private static BlendShapeFrame RandomFrame(Random random, float weight, int vertexCount, int moved)
    {
        uint[] vertices = [.. Enumerable.Range(0, vertexCount).OrderBy(_ => random.Next()).Take(moved).Select(v => (uint)v)];
        Float3 Next() => new(random.NextSingle() - 0.5f, random.NextSingle() - 0.5f, random.NextSingle() - 0.5f);
        return new BlendShapeFrame
        {
            Weight = weight,
            Vertices = vertices,
            Positions = [.. vertices.Select(_ => Next())],
            Normals = [.. vertices.Select(_ => Next())],
            Tangents = [.. vertices.Select(_ => Next())],
        };
    }

    [Fact]
    public void BlendShapes_MorphBeforeSkinning_AsProwlDoes()
    {
        Random random = new(11);
        const int count = 60;
        Float3[] positions = new Float3[count], normals = new Float3[count];
        Float4[] tangents = new Float4[count];
        ushort[] indices = new ushort[count * 4], weights = new ushort[count * 4];
        for (int v = 0; v < count; v++)
        {
            positions[v] = new Float3(random.NextSingle() * 4 - 2, random.NextSingle() * 4 - 2, random.NextSingle());
            normals[v] = new Float3(random.NextSingle() - 0.5f, random.NextSingle() - 0.5f, -1);
            tangents[v] = new Float4(1, random.NextSingle() - 0.5f, 0, 1);
            (indices[v * 4], weights[v * 4]) = ((ushort)(v % 2 + 1), 65535);
        }

        // Frames that overlap on vertices, so the frames must add up one after another
        BlendShapeData[] shapes =
        [
            new BlendShapeData { Name = "Single", Frames = [RandomFrame(random, 100, count, 40)] },
            new BlendShapeData { Name = "Steps", Frames = [RandomFrame(random, 25, count, 40), RandomFrame(random, 50, count, 40), RandomFrame(random, 100, count, 40)] },
            new BlendShapeData { Name = "Idle", Frames = [RandomFrame(random, 100, count, 40)] },
        ];
        float[] shapeWeights = [60, 37.5f, 0];
        Float4x4[] bones = [Translation(0.5f, 0, 0), new Float4x4(new Float4(0, 1, 0, 0), new Float4(-1, 0, 0, 0), new Float4(0, 0, 1, 0), new Float4(0, 0, 0.25f, 1))];

        GpuMesh mesh = SkinnedMesh(positions, indices, weights, normals, tangents, shapes);
        RenderableHandle handle = _world.Add(new RenderableDesc { Mesh = mesh, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)) }, [Unlit(new Float3(1, 1, 1))]);
        _world.SetBounds(handle, new AABB(new Float3(-8), new Float3(8)));
        _world.SetBlendShapeWeights(handle, shapeWeights);
        _world.SetBones(handle, bones);
        Frame(View());

        List<(BlendShapeFrame Frame, float Weight)> active = CpuActiveFrames(shapes, shapeWeights);
        Assert.Equal(3, active.Count);
        float[] gpuPositions = DeformedStream(handle, VertexSemantic.Position);
        float[] gpuNormals = DeformedStream(handle, VertexSemantic.Normal);
        float[] gpuTangents = DeformedStream(handle, VertexSemantic.Tangent);
        for (int v = 0; v < count; v++)
        {
            ReadOnlySpan<ushort> vi = indices.AsSpan(v * 4, 4), vw = weights.AsSpan(v * 4, 4);
            Float3 position = CpuSkin(CpuMorph(positions[v], v, active, f => f.Positions), 1, vi, vw, bones);
            Float3 normal = CpuSkin(CpuMorph(normals[v], v, active, f => f.Normals), 0, vi, vw, bones);
            Float3 tangent = CpuSkin(CpuMorph(new Float3(tangents[v].X, tangents[v].Y, tangents[v].Z), v, active, f => f.Tangents), 0, vi, vw, bones);
            normal /= MathF.Sqrt(normal.X * normal.X + normal.Y * normal.Y + normal.Z * normal.Z);
            tangent /= MathF.Sqrt(tangent.X * tangent.X + tangent.Y * tangent.Y + tangent.Z * tangent.Z);
            for (int c = 0; c < 3; c++)
            {
                Assert.True(MathF.Abs(gpuPositions[v * 3 + c] - position[c]) < 1e-4f, $"vertex {v} position {c}: {gpuPositions[v * 3 + c]} against {position[c]}");
                Assert.True(MathF.Abs(gpuNormals[v * 3 + c] - normal[c]) < 1e-4f, $"vertex {v} normal {c}: {gpuNormals[v * 3 + c]} against {normal[c]}");
                Assert.True(MathF.Abs(gpuTangents[v * 4 + c] - tangent[c]) < 1e-4f, $"vertex {v} tangent {c}: {gpuTangents[v * 4 + c]} against {tangent[c]}");
            }
        }
    }

    [Fact]
    public void BlendShapes_WithoutBones_MorphWhatIsDrawn_AndRestAtZero()
    {
        Float3[] positions = [new(-4, -4, 0), new(4, -4, 0), new(4, 4, 0), new(-4, -4, 0), new(4, 4, 0), new(-4, 4, 0)];
        BlendShapeFrame right = new() { Weight = 100, Vertices = [0, 1, 2, 3, 4, 5], Positions = [.. Enumerable.Repeat(new Float3(4, 0, 0), 6)] };
        GpuMesh mesh = _renderer.CreateMesh(new MeshData
        {
            VertexCount = 6,
            Streams = [new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(positions.AsSpan()).ToArray() }],
            Indices = new uint[] { 0, 1, 2, 3, 4, 5 },
            BlendShapes = [new BlendShapeData { Name = "Right", Frames = [right] }],
        });
        _owned.Add(mesh);
        RenderableHandle handle = _world.Add(new RenderableDesc { Mesh = mesh, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)) * Double4x4.CreateScale(0.5) }, [Unlit(new Float3(1, 1, 1))]);
        _world.SetBounds(handle, new AABB(new Float3(-8), new Float3(8)));
        AssertAll(new Float3(1, 1, 1), Frame(View()), Centre);

        _world.SetBlendShapeWeights(handle, [100]);
        AssertAll(new Float3(1, 1, 1), Frame(View()), i => i % Size is >= 8 and < 16 && i / Size is >= 4 and < 12);
        _world.SetBlendShapeWeights(handle, [50]);
        AssertAll(new Float3(1, 1, 1), Frame(View()), i => i % Size is >= 6 and < 14 && i / Size is >= 4 and < 12);
        _world.SetBlendShapeWeights(handle, []);
        AssertAll(new Float3(1, 1, 1), Frame(View()), Centre);
    }

    [Fact]
    public void BlendShapes_RefuseFramesThatDoNotFitTheMesh()
    {
        Float3[] positions = [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)];
        MeshData Mesh(params BlendShapeFrame[] frames) => new()
        {
            VertexCount = 3,
            Streams = [new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(positions.AsSpan()).ToArray() }],
            Indices = new uint[] { 0, 1, 2 },
            BlendShapes = [new BlendShapeData { Name = "Shape", Frames = frames }],
        };
        Assert.Throws<ArgumentException>(() => _renderer.CreateMesh(Mesh(new BlendShapeFrame { Vertices = [3], Positions = [default] })));
        Assert.Throws<ArgumentException>(() => _renderer.CreateMesh(Mesh(new BlendShapeFrame { Vertices = [0, 1], Positions = [default] })));
        Assert.Throws<ArgumentException>(() => _renderer.CreateMesh(Mesh(new BlendShapeFrame { Weight = 50, Vertices = [0], Positions = [default] }, new BlendShapeFrame { Weight = 25, Vertices = [0], Positions = [default] })));
    }

    [Fact]
    public void BlendShapeFrames_FromDenseDeltas_KeepOnlyTheVerticesTheyMove()
    {
        BlendShapeFrame frame = BlendShapeFrame.FromDense(100, [default, new Float3(1, 0, 0), default], [default, default, new Float3(0, 1, 0)], null);
        Assert.Equal([1u, 2u], frame.Vertices);
        Assert.Equal([new Float3(1, 0, 0), default], frame.Positions);
        Assert.Equal([default, new Float3(0, 1, 0)], frame.Normals!);
        Assert.Null(frame.Tangents);
    }

    private RenderableHandle AddSkinned(bool motionVectors, uint layer = ~0u)
    {
        RenderableFlags flags = RenderableFlags.CastShadows | RenderableFlags.ReceiveShadows | (motionVectors ? RenderableFlags.MotionVectors : 0);
        RenderableHandle handle = _world.Add(new RenderableDesc { Mesh = SkinnedQuad(), Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)), Flags = flags, LayerMask = layer }, [Unlit(new Float3(1, 1, 1))]);
        _world.SetBounds(handle, new AABB(new Float3(-16), new Float3(16)));
        _world.SetBones(handle, [Float4x4.Identity]);
        return handle;
    }

    [Fact]
    public void SkinnedMotion_FollowsTheBones_AndStopsWhenTheyDo()
    {
        ShowingMotion();
        RenderableHandle handle = AddSkinned(motionVectors: true);
        Frame(View());

        // A unit up is an eighth of the view down the image in uv, as for a moving object
        _world.SetBones(handle, [Translation(0, 1, 0)]);
        AssertAll(new Float3(0, -0.125f, 0), Frame(View()), i => i / Size >= 2);
        AssertAll(new Float3(0, 0, 0), Frame(View()), i => i / Size >= 2);
        _world.SetBones(handle, [Translation(0, 2, 0)]);
        AssertAll(new Float3(0, -0.125f, 0), Frame(View()), i => i / Size >= 4);
    }

    [Fact]
    public void SkinnedMotion_AddsToTheObjectsOwnMotion()
    {
        ShowingMotion();
        RenderableHandle handle = AddSkinned(motionVectors: true);
        Frame(View());
        _world.SetBones(handle, [Translation(0, 1, 0)]);
        _world.SetTransform(handle, Double4x4.CreateTranslation(new Double3(0, 1, 10)));
        AssertAll(new Float3(0, -0.25f, 0), Frame(View()), i => i / Size >= 4);
    }

    [Fact]
    public void SkinnedMotion_IsLeftOutWithoutTheFlag()
    {
        ShowingMotion();
        RenderableHandle handle = AddSkinned(motionVectors: false);
        Frame(View());
        _world.SetBones(handle, [Translation(0, 1, 0)]);
        AssertAll(new Float3(0, 0, 0), Frame(View()));
    }

    [Fact]
    public void SkinnedMotion_StartsAtNothingWhenNoViewSawTheLastPose()
    {
        ShowingMotion();
        RenderableHandle handle = AddSkinned(motionVectors: true);
        ViewDesc view = View();
        view.CullingMask = 1;
        Frame(view);
        _world.SetLayerMask(handle, 2);
        _world.SetBones(handle, [Translation(0, 1, 0)]);
        Frame(view);

        _world.SetLayerMask(handle, ~0u);
        _world.SetBones(handle, [Translation(0, 2, 0)]);
        AssertAll(new Float3(0, 0, 0), Frame(view));
    }

    [Fact]
    public void MorphedMotion_IsRigidOnly()
    {
        ShowingMotion();
        Float3[] positions = [new(-4, -4, 0), new(4, -4, 0), new(4, 4, 0), new(-4, -4, 0), new(4, 4, 0), new(-4, 4, 0)];
        BlendShapeFrame up = new() { Weight = 100, Vertices = [0, 1, 2, 3, 4, 5], Positions = [.. Enumerable.Repeat(new Float3(0, 1, 0), 6)] };
        GpuMesh mesh = _renderer.CreateMesh(new MeshData
        {
            VertexCount = 6,
            Streams = [new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(positions.AsSpan()).ToArray() }],
            Indices = new uint[] { 0, 1, 2, 3, 4, 5 },
            BlendShapes = [new BlendShapeData { Name = "Up", Frames = [up] }],
        });
        _owned.Add(mesh);
        RenderableHandle handle = _world.Add(new RenderableDesc { Mesh = mesh, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)), Flags = RenderableFlags.MotionVectors }, [Unlit(new Float3(1, 1, 1))]);
        _world.SetBounds(handle, new AABB(new Float3(-16), new Float3(16)));
        Frame(View());
        _world.SetBlendShapeWeights(handle, [100]);
        AssertAll(new Float3(0, 0, 0), Frame(View()));
    }

    // A frame that fails, two views sharing an id, after its resources were applied
    private void FailedFrame()
    {
        _renderer.SubmitFrame(new FrameDesc { Views = new[] { View(), View() } });
        _device.WaitForIdle();
        Assert.Contains(_errors, e => e.Code == "frame.failed");
        _errors.Clear();
    }

    [Fact]
    public void AFailedFrame_KeepsTheUploadsItApplied()
    {
        Frame(View());
        AddQuad(new Float3(0.25f, 0.5f, 1));
        FailedFrame();
        AssertAll(new Float3(0.25f, 0.5f, 1), Frame(View()));
    }

    [Fact]
    public void AFailedFrame_KeepsTheDeformedCopiesItMade()
    {
        GpuMesh mesh = SkinnedQuad();
        Frame(View());
        RenderableHandle handle = _world.Add(new RenderableDesc { Mesh = mesh, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)) * Double4x4.CreateScale(0.5) }, [Unlit(new Float3(1, 1, 1))]);
        _world.SetBounds(handle, new AABB(new Float3(-16), new Float3(16)));
        _world.SetBones(handle, [Translation(4, 0, 0)]);
        FailedFrame();
        Float4[] pixels = Frame(View());
        AssertAll(new Float3(1, 1, 1), pixels, i => i % Size is >= 8 and < 16 && i / Size is >= 4 and < 12);
        AssertAll(new Float3(0, 0, 0), pixels, i => i % Size < 8);
    }

    [Fact]
    public void SkinnedMotion_OfARenderableAddedWhereOneWasRemoved_StartsAtNothing()
    {
        ShowingMotion();
        RenderableHandle first = AddSkinned(motionVectors: true);
        Frame(View());
        _world.SetBones(first, [Translation(0, 1, 0)]);
        Frame(View());

        // The same mesh, so the slot keeps its copy, posed back down
        GpuMesh mesh = _world.Render.Mesh[first.Index]!;
        _world.Remove(first);
        RenderableHandle second = _world.Add(new RenderableDesc { Mesh = mesh, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)), Flags = RenderableFlags.MotionVectors }, [Unlit(new Float3(1, 1, 1))]);
        Assert.Equal(first.Index, second.Index);
        _world.SetBounds(second, new AABB(new Float3(-16), new Float3(16)));
        _world.SetBones(second, [Float4x4.Identity]);
        AssertAll(new Float3(0, 0, 0), Frame(View()));
    }

    [Fact]
    public void Skinning_WithoutBounds_CullsByTheRestPoseAndSaysSoOnce()
    {
        RenderableHandle handle = _world.Add(new RenderableDesc { Mesh = SkinnedQuad(), Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)) }, [Unlit(new Float3(1, 1, 1))]);
        _world.SetBones(handle, [Float4x4.Identity]);
        _renderer.SubmitFrame(new FrameDesc { Views = new[] { View() } });
        _renderer.SubmitFrame(new FrameDesc { Views = new[] { View() } });
        RendererError error = Assert.Single(_errors);
        Assert.Equal(("deform.bounds", RendererErrorSeverity.Warning), (error.Code, error.Severity));
    }

    [Fact]
    public void TheScene_HasOnlyTheTargetsItsFeaturesRead()
    {
        AddQuad(new Float3(1, 1, 1));
        int Targets() => ViewGraph.SceneTargetCount(_renderer.Frame.Camera(1)!.ShapeKey.Targets);
        Frame(View());
        Assert.Equal(1, Targets());

        TemporalAntiAliasing taa = new(_renderer);
        _world.Features.Add(taa);
        Frame(View());
        Assert.Equal(2, Targets());

        ScreenSpaceReflections ssr = new(_renderer);
        _world.Features.Add(ssr);
        Frame(View());
        Assert.Equal(5, Targets());

        _world.Features.Remove(ssr);
        _world.Features.Remove(taa);
        AssertAll(new Float3(1, 1, 1), Frame(View()));
        Assert.Equal(1, Targets());
    }

    [Fact]
    public void Jitter_CyclesThroughHaltonPhases_OnlyWhileAFeatureAsks()
    {
        AddQuad(new Float3(1, 1, 1));
        Frame(View());
        Assert.Equal(default, _renderer.Frame.Camera(1)!.Jitter);

        TemporalAntiAliasing taa = new(_renderer) { JitterSpread = 4 };
        _world.Features.Add(taa);
        List<Float2> jitters = new();
        for (int frame = 0; frame < 5; frame++)
        {
            Frame(View());
            jitters.Add(_renderer.Frame.Camera(1)!.Jitter);
        }

        // Within half a pixel, four different offsets, then the first again
        Assert.All(jitters, j => Assert.True(MathF.Abs(j.X) <= 1f / Size && MathF.Abs(j.Y) <= 1f / Size, $"jitter {j}"));
        Assert.Equal(4, jitters.Take(4).Distinct().Count());
        Assert.Equal(jitters[0], jitters[4]);
    }

    [Fact]
    public void Taa_ConvergesAStillEdgeToItsCoverage()
    {
        // A white plane from a quarter into the middle column onwards: pixel 8 spans 0 to 0.5, three quarters covered
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(4.125, 0, 10)) }, [Unlit(new Float3(1, 1, 1))]);
        TemporalAntiAliasing taa = new(_renderer) { Sharpness = 0, JitterSpread = 8 };
        _world.Features.Add(taa);
        Float4[] pixels = [];
        for (int frame = 0; frame < 120; frame++)
            pixels = Frame(View());
        for (int y = 0; y < Size; y++)
        {
            Assert.InRange(pixels[y * Size + 8].X, 0.6f, 0.9f);
            Assert.True(pixels[y * Size + 7].X < 0.01f && pixels[y * Size + 9].X > 0.99f, $"row {y}: {pixels[y * Size + 7].X}, {pixels[y * Size + 9].X}");
        }
    }

    [Fact]
    public void Taa_LeavesNoGhostWhereAMovingObjectWas()
    {
        RenderableHandle square = AddMoving(new Double3(0, 0, 10), motionVectors: true);
        _world.SetTransform(square, Double4x4.CreateTranslation(new Double3(-2, 0, 10)) * Double4x4.CreateScale(0.25));
        TemporalAntiAliasing taa = new(_renderer) { Sharpness = 0 };
        _world.Features.Add(taa);
        for (int frame = 0; frame < 30; frame++)
            Frame(View());

        // Moved two units right, the pixels it left are background the next frame
        _world.SetTransform(square, Double4x4.CreateTranslation(new Double3(2, 0, 10)) * Double4x4.CreateScale(0.25));
        Float4[] pixels = Frame(View());
        Func<int, bool> left = i => MathF.Abs(4 * ((i % Size + 0.5f) / Size * 2 - 1) + 2) < 0.5f && MathF.Abs(4 * ((i / Size + 0.5f) / Size * 2 - 1)) < 0.5f;
        AssertAll(new Float3(0, 0, 0), pixels, left);
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

        public override void Setup(FeatureBuilder builder, in FeatureView view) { }

        public override void Render(FeatureContext context) => (Width, Height) = (context.View.Width, context.View.Height);
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

    // Writes the output colour from the render resolution one, keeping what it was given to check
    private sealed class Upscaler(FullscreenProgram program) : RenderFeature
    {
        public (uint, uint) Input, Output;

        public override RenderSlot Slot => RenderSlot.Upscale;

        public override void Setup(FeatureBuilder builder, in FeatureView view) => builder.WriteOutputColor();

        public override void Render(FeatureContext context)
        {
            (Input, Output) = ((context.View.Width, context.View.Height), (context.View.OutputWidth, context.View.OutputHeight));
            context.Fullscreen(program);
        }
    }

    // Records the size of the colour it writes and the view it was given
    private sealed class SizeProbe(FullscreenProgram program) : RenderFeature
    {
        public (uint, uint) View, Written;
        public bool Upscaled;

        public override RenderSlot Slot => RenderSlot.HdrPost;

        public override void Setup(FeatureBuilder builder, in FeatureView view) => builder.WriteColor();

        public override void Render(FeatureContext context)
        {
            (View, Upscaled) = ((context.View.Width, context.View.Height), context.View.Upscaled);
            Written = (context.Color!.Width, context.Color.Height);
            context.Fullscreen(program);
        }
    }

    // Keeps a history of the colour, noting each frame whether it had one and a previous frame
    private sealed class Remembers(FullscreenProgram program) : RenderFeature
    {
        private TextureHandle _history;
        public readonly List<(bool History, bool Previous)> Seen = new();

        public override RenderSlot Slot => RenderSlot.HdrPost;

        public override void Setup(FeatureBuilder builder, in FeatureView view)
        {
            builder.ReadColor();
            _history = builder.History("Kept", view.Texture(PixelFormat.R16_G16_B16_A16_Float));
        }

        public override void Render(FeatureContext context)
        {
            Seen.Add((context.History(_history) != null, context.HasPreviousFrame));
            context.Fullscreen(program, context.Get(_history));
        }
    }

    [Fact]
    public void ACut_StartsMotionAndHistoryOver()
    {
        AddQuad(new Float3(1, 1, 1));
        Remembers remembers = new(Program("void main() { outColor = Graphite_SampleSource(vUV); }"));
        _world.Features.Add(remembers);
        Frame(View());
        Frame(View());
        ViewDesc cut = View();
        cut.Cut = true;
        Frame(cut);
        Frame(View());

        Assert.Equal([(false, false), (true, true), (false, false), (true, true)], remembers.Seen);
    }

    [Fact]
    public void AnUpscaler_WritesTheOutputResolution_WhichLaterFeaturesRunAt()
    {
        AddQuad(new Float3(0.5f, 0.25f, 1));
        FullscreenProgram copy = Program("void main() { outColor = Graphite_SampleSource(vUV); }");
        Upscaler upscaler = new(copy);
        SizeProbe after = new(copy);
        _world.Features.Add(upscaler);
        _world.Features.Add(after);
        ViewDesc view = View();
        view.RenderScale = 0.5f;

        AssertAll(new Float3(0.5f, 0.25f, 1), Frame(view));
        Assert.Equal(((8u, 8u), ((uint)Size, (uint)Size)), (upscaler.Input, upscaler.Output));
        Assert.Equal(((uint)Size, (uint)Size), after.View);
        Assert.True(after.Upscaled);
        Assert.Equal(((uint)Size, (uint)Size), after.Written);
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

    private sealed class Named(string name, RenderSlot slot, int order = 0, ViewKinds runsOn = ViewKinds.Cameras, string? group = null) : RenderFeature
    {
        public override RenderSlot Slot => slot;
        public override int Order => order;
        public override ViewKinds RunsOn => runsOn;
        public override string? ExclusiveGroup => group;
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
        FeatureSelection.Select(result, world, view, new FeatureView(desc, 1, 1));
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
    public void FeaturesThatThrowInSetupOrWhileRecording_FailTheFrame()
    {
        AddQuad(new Float3(0.25f, 0.5f, 1));
        Throws setup = new(inSetup: true);
        _world.Features.Add(setup);
        _renderer.SubmitFrame(new FrameDesc { Views = new[] { View() } });
        _renderer.SubmitFrame(new FrameDesc { Views = new[] { View() } });
        _device.WaitForIdle();
        Assert.Equal("frame.failed", Assert.Single(_errors).Code);
        _errors.Clear();
        _world.Features.Remove(setup);
        AssertAll(new Float3(0.25f, 0.5f, 1), Frame(View()));

        _world.Features.Add(new Throws(inSetup: false));
        for (int frame = 0; frame < 6; frame++)
            _renderer.SubmitFrame(new FrameDesc { Views = new[] { View() } });
        _device.WaitForIdle();
        Assert.Equal("frame.failed", Assert.Single(_errors).Code);
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
    public void TogglingAFeature_ChangesWhatItDeclares()
    {
        AddQuad(new Float3(0.25f, 0.5f, 1));
        Toggle toggle = new(Program("void main() { outColor = vec4(1.0 - Graphite_SampleSource(vUV).rgb, 1.0); }"));
        _world.Features.Add(toggle);
        AssertAll(new Float3(0.25f, 0.5f, 1), Frame(View()));
        toggle.On = true;
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
            new Named("no prepass", RenderSlot.Prepass, group: "Prepass"),
            new Named("no opaque", RenderSlot.Opaque, group: "Opaque"),
            new Named("no sky", RenderSlot.Sky, group: "Sky"),
            new Named("no transparent", RenderSlot.Transparent, group: "Transparent"),
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
    public void FeaturesDrawAnyPassType_IncludingRenderablesAddedLater()
    {
        Shader outlined = _renderer.LoadShader(ShaderParser.Parse(OutlinedSource));
        Material material = _renderer.CreateMaterial(outlined);
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)) * Double4x4.CreateScale(0.5) }, [material]);
        _world.Features.Add(new DrawPassFeature(new PassType("Outline"), RenderSlot.AfterOpaques));

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

    [Theory]
    [InlineData(RenderSlot.AfterPrepass, false)]
    [InlineData(RenderSlot.AfterOpaques, true)]
    [InlineData(RenderSlot.AfterTransparents, true)]
    public void DrawPassFeature_DrawsItsPassInItsSlot(RenderSlot slot, bool overOpaques)
    {
        Shader outlined = _renderer.LoadShader(ShaderParser.Parse(OutlinedSource));
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)) * Double4x4.CreateScale(0.5) }, [_renderer.CreateMaterial(outlined)]);
        DrawPassFeature feature = new(new PassType("Outline"), slot);
        _world.Features.Add(feature);

        // Before the opaques, the object's own forward pass draws over its outline
        AssertAll(overOpaques ? new Float3(0, 1, 0) : new Float3(1, 0, 0), Frame(View()), Centre);

        feature.Enabled = false;
        AssertAll(new Float3(1, 0, 0), Frame(View()), Centre);
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
            _total = builder.History("Total", view.Texture(PixelFormat.R16_G16_B16_A16_Float));
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
    public void Readbacks_OfTexturesAndBuffers_CompleteOnALaterSubmit()
    {
        AddQuad(new Float3(0.25f, 0.5f, 1));
        using DeviceBuffer buffer = _device.ResourceFactory.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadOnly));
        _device.UpdateBuffer<uint>(buffer, 0, [1, 2, 3, 4]);

        TextureReadback? texture = null;
        byte[]? bytes = null;
        _renderer.Readback(_target, data => texture = data);
        _renderer.Readback(buffer, data => bytes = data.Data, offset: 4, size: 8);
        Frame(View());
        Assert.Null(texture);

        _renderer.SubmitFrame(new FrameDesc());
        Assert.NotNull(texture);
        Assert.Equal((16u, 16u, PixelFormat.R32_G32_B32_A32_Float), (texture!.Value.Width, texture.Value.Height, texture.Value.Format));
        Float4 pixel = MemoryMarshal.Cast<byte, Float4>(texture.Value.Data)[5 * Size + 7];
        Assert.Equal(new Float4(0.25f, 0.5f, 1, 1), pixel);
        Assert.Equal([2u, 3u], MemoryMarshal.Cast<byte, uint>(bytes).ToArray());
    }

    [Fact]
    public void AReadbackWhoseTextureIsDisposedFirst_FailsWithAReason()
    {
        Texture texture = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(4, 4, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        TextureReadback? result = null;
        _renderer.Readback(texture, data => result = data);
        texture.Dispose();
        _renderer.SubmitFrame(new FrameDesc());
        _renderer.SubmitFrame(new FrameDesc());
        Assert.True(result!.Value.Failed);
        Assert.Equal("readback.disposed", Assert.Single(_errors).Code);
    }

    [Fact]
    public void ReadbacksOutsideTheTexture_AreRefusedWhenAsked()
    {
        using Texture texture = _device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(4, 4, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        Assert.Throws<ArgumentOutOfRangeException>(() => _renderer.Readback(texture, _ => { }, region: new TextureRegion(0, 0, 0, 5, 4, 1, 0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => _renderer.Readback(texture, _ => { }, region: new TextureRegion(0, 0, 0, 0, 4, 1, 0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => _renderer.Readback(texture, _ => { }, region: new TextureRegion(0, 0, 0, 2, 2, 1, 1, 0)));
        using DeviceBuffer buffer = _device.ResourceFactory.CreateBuffer(new BufferDescription(16, BufferUsage.StructuredBufferReadOnly));
        Assert.Throws<ArgumentOutOfRangeException>(() => _renderer.Readback(buffer, _ => { }, offset: 8, size: uint.MaxValue - 4));
    }

    private Float4[] ReadTarget()
    {
        _device.Record(cmd => cmd.CopyTextureToBuffer(_target, _readback, 0, TextureRegion.Whole(_target, 0, 0))).Wait();
        Float4[] pixels = _device.Map<Float4>(_readback).ToArray();
        _device.Unmap(_readback);
        return pixels;
    }

    private sealed class Glows : RenderFeature
    {
        public static readonly PassType Glow = new("Glow");

        public override RenderSlot Slot => RenderSlot.AfterTransparents;

        public override void Setup(FeatureBuilder builder, in FeatureView view)
        {
            builder.DrawsPass(Glow);
            builder.DrawToScene();
        }

        public override void Render(FeatureContext context)
        {
            context.BeginOutput();
            context.DrawPass(Glow);
        }
    }

    [Fact]
    public void TransparentRenderablesInAFeaturesPass_BlendBackToFront()
    {
        Shader glow = _renderer.LoadShader(ShaderParser.Parse(GlowSource));
        Material Tint(Float3 c)
        {
            Material material = _renderer.CreateMaterial(glow);
            material.SetColor("_Color", new Float4(c.X, c.Y, c.Z, 0.5f));
            return material;
        }

        // The near one added first, so only sorting puts the far one under it
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)) }, [Tint(new Float3(0, 1, 0))]);
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 20)) }, [Tint(new Float3(1, 0, 0))]);
        _world.Features.Add(new Glows());
        AssertAll(new Float3(0.25f, 0.5f, 0), Frame(View()));
    }

    // Declares a read of a texture that is not in the frame, which the graph refuses
    private sealed class BreaksTheGraph : RenderFeature
    {
        public override RenderSlot Slot => RenderSlot.HdrPost;
        public override void Setup(FeatureBuilder builder, in FeatureView view) => builder.Graph.Read(default(TextureHandle));
        public override void Render(FeatureContext context) { }
    }

    [Fact]
    public void AFeatureBreakingTheGraphAfterItsSetup_FailsTheFrame_UntilItIsRemoved()
    {
        AddQuad(new Float3(0.25f, 0.5f, 1));
        BreaksTheGraph broken = new();
        _world.Features.Add(broken);
        _renderer.SubmitFrame(new FrameDesc { Views = new[] { View() } });
        _renderer.SubmitFrame(new FrameDesc { Views = new[] { View() } });
        _device.WaitForIdle();
        Assert.Equal("frame.failed", Assert.Single(_errors).Code);
        _errors.Clear();
        _world.Features.Remove(broken);
        AssertAll(new Float3(0.25f, 0.5f, 1), Frame(View()));
    }

    // Publishes the colour when on
    private sealed class Publisher : RenderFeature
    {
        public bool On = true;

        public override RenderSlot Slot => RenderSlot.HdrPost;
        public override bool IsActive(in FeatureView view) => On;

        public override void Setup(FeatureBuilder builder, in FeatureView view)
            => builder.Publish("Inverted", builder.Temporary("Copy", view.Texture(PixelFormat.R16_G16_B16_A16_Float)));

        public override void Render(FeatureContext context) { }
    }

    // Reads what the publisher published, and inverts the colour
    private sealed class Reader(FullscreenProgram program) : RenderFeature
    {
        public override RenderSlot Slot => RenderSlot.Tonemap;

        public override void Setup(FeatureBuilder builder, in FeatureView view)
        {
            builder.Read("Inverted");
            builder.WriteColor();
        }

        public override void Render(FeatureContext context) => context.Fullscreen(program);
    }

    [Fact]
    public void AFailedGraph_IsBuiltAgain_WhenTheViewsFeaturesChange()
    {
        AddQuad(new Float3(0.25f, 0.5f, 1));
        Publisher publisher = new() { On = false };
        _world.Features.Add(publisher);
        _world.Features.Add(new Reader(Program("void main() { outColor = vec4(1.0 - Graphite_SampleSource(vUV).rgb, 1.0); }")));
        _renderer.SubmitFrame(new FrameDesc { Views = new[] { View() } });
        _renderer.SubmitFrame(new FrameDesc { Views = new[] { View() } });
        _device.WaitForIdle();
        Assert.Equal("frame.failed", Assert.Single(_errors).Code);
        _errors.Clear();

        publisher.On = true;
        AssertAll(new Float3(0.75f, 0.5f, 0), Frame(View()));
    }

    private Material ObjectColor(float shift = 0)
    {
        Material material = _renderer.CreateMaterial(_renderer.LoadShader(ShaderParser.Parse(ObjectColorSource)));
        material.SetFloat("_Shift", shift);
        return material;
    }

    // A quarter size quad, two units across, at x and y in the view's -4 to 4
    private static Float4x4 Small(float x, float y) => Float4x4.CreateTranslation(new Float3(x, y, 0)) * Float4x4.CreateScale(0.25f);

    // Pixels whose centre lies inside a two unit square centred at x, y
    private static Func<int, bool> Inside(float x, float y) => i =>
    {
        float px = 4 * ((i % Size + 0.5f) / Size * 2 - 1), py = 4 * ((i / Size + 0.5f) / Size * 2 - 1);
        return MathF.Abs(px - x) < 1 && MathF.Abs(py - y) < 1;
    };

    [Fact]
    public void InstanceBatches_DrawEveryInstance_AndFollowTheirEdits()
    {
        Material material = ObjectColor();
        InstanceBatchDesc desc = new() { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)) };
        InstanceData[] instances =
        [
            new InstanceData { Transform = Small(-2, -2), Color = new Float4(1, 0, 0, 1) },
            new InstanceData { Transform = Small(2, 2), Color = new Float4(0, 1, 0, 1) },
        ];
        InstanceBatchHandle batch = _world.AddInstanceBatch(desc, [material], instances);
        Float4[] pixels = Frame(View());
        AssertAll(new Float3(1, 0, 0), pixels, Inside(-2, -2));
        AssertAll(new Float3(0, 1, 0), pixels, Inside(2, 2));
        AssertAll(new Float3(0, 0, 0), pixels, i => !Inside(-2, -2)(i) && !Inside(2, 2)(i));

        // One instance, recoloured, then the whole batch moved
        _world.SetInstances(batch, [new InstanceData { Transform = Small(-2, -2), Color = new Float4(0, 0, 1, 1) }]);
        Assert.Equal(1, _world.InstanceCount(batch));
        pixels = Frame(View());
        AssertAll(new Float3(0, 0, 1), pixels, Inside(-2, -2));
        AssertAll(new Float3(0, 0, 0), pixels, Inside(2, 2));

        desc.Transform = Double4x4.CreateTranslation(new Double3(4, 4, 10));
        _world.SetInstanceBatch(batch, desc);
        pixels = Frame(View());
        AssertAll(new Float3(0, 0, 1), pixels, Inside(2, 2));
        AssertAll(new Float3(0, 0, 0), pixels, Inside(-2, -2));

        _world.RemoveInstanceBatch(batch);
        Assert.False(_world.IsAlive(batch));
        AssertAll(new Float3(0, 0, 0), Frame(View()));
    }

    [Fact]
    public void TransientDraws_ShowForOneFrameOnly()
    {
        Material color = _renderer.CreateMaterial(_renderer.LoadShader(ShaderParser.Parse(VertexColorSource)));
        Float3[] square = [new(-3, -3, 10), new(-1, -3, 10), new(-1, -1, 10), new(-3, -1, 10)];
        uint red = 0xFF0000FFu;
        _world.DrawTransient(new TransientDraw { Positions = square, Colors = new uint[] { red, red, red, red }, Indices = new uint[] { 0, 1, 2, 0, 2, 3 }, Material = color });
        _world.DrawTransient(new TransientDraw { Mesh = _quad, Material = color, Transform = Double4x4.CreateTranslation(new Double3(2, 2, 10)) * Double4x4.CreateScale(0.25) });

        Float4[] pixels = Frame(View());
        AssertAll(new Float3(1, 0, 0), pixels, Inside(-2, -2));
        AssertAll(new Float3(1, 1, 1), pixels, Inside(2, 2));

        // Gone the frame after
        AssertAll(new Float3(0, 0, 0), Frame(View()));
    }

    private sealed class Items(GpuMesh mesh, Material material) : RenderProvider
    {
        public readonly List<(Double4x4 Transform, Float4 Color, uint Instances)> Draws = new();
        public Material Material = material;
        public int Culls;

        public override void Cull(in ProviderView view, DrawItemSink sink)
        {
            Culls++;
            foreach ((Double4x4 transform, Float4 color, uint instances) in Draws)
            {
                DrawItem item = new() { Transform = transform, Custom0 = color };
                if (instances > 1)
                    sink.AddInstanced(mesh, 0, Material, item, instances);
                else
                    sink.Add(mesh, 0, Material, item);
            }
        }
    }

    [Fact]
    public void Providers_DrawTheirItemsIntoEachView_SingleInstancedAndIndirect()
    {
        Items items = new(_quad, ObjectColor(shift: 16));
        _world.Providers.Add(items);
        items.Draws.Add((Double4x4.CreateTranslation(new Double3(-2, -2, 10)) * Double4x4.CreateScale(0.25), new Float4(1, 0, 0, 1), 1));
        Float4[] pixels = Frame(View());
        Assert.Equal(1, items.Culls);
        AssertAll(new Float3(1, 0, 0), pixels, Inside(-2, -2));
        AssertAll(new Float3(0, 0, 0), pixels, i => !Inside(-2, -2)(i));

        // Two instances of one object, the second shifted four units by the shader: a quad scaled by a quarter moves 16 units of its own
        items.Draws[0] = (items.Draws[0].Transform, new Float4(0, 1, 0, 1), 2);
        pixels = Frame(View());
        AssertAll(new Float3(0, 1, 0), pixels, Inside(-2, -2));
        AssertAll(new Float3(0, 1, 0), pixels, Inside(2, -2));

        items.Enabled = false;
        AssertAll(new Float3(0, 0, 0), Frame(View()));
    }

    [Fact]
    public void ProvidedTransparentItems_SortWithTheWorlds()
    {
        Shader glass = _renderer.LoadShader(ShaderParser.Parse(ObjectGlassSource));
        Material Glass() => _renderer.CreateMaterial(glass);

        // The world's far red glass and a provider's near green one, in either order back to front
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 20)), Custom0 = new Float4(1, 0, 0, 1) }, [Glass()]);
        Items items = new(_quad, Glass());
        items.Draws.Add((Double4x4.CreateTranslation(new Double3(0, 0, 10)), new Float4(0, 1, 0, 1), 1));
        _world.Providers.Add(items);
        AssertAll(new Float3(0.25f, 0.5f, 0), Frame(View()));

        items.Draws[0] = (Double4x4.CreateTranslation(new Double3(0, 0, 30)), new Float4(0, 1, 0, 1), 1);
        AssertAll(new Float3(0.5f, 0.25f, 0), Frame(View()));
    }

    private sealed class ComputedItems(GpuMesh mesh, Material material) : RenderProvider
    {
        public override void Cull(in ProviderView view, DrawItemSink sink)
            => sink.AddIndirect(mesh, 0, material, new DrawItem { Transform = Double4x4.CreateTranslation(new Double3(-2, -2, 10)) * Double4x4.CreateScale(0.25), Custom0 = new Float4(1, 0, 0, 1) }, "Test.ComputedCount", 4);
    }

    // Writes its count on the GPU each frame through a compute dispatch of its own
    private sealed class ComputedCount(ComputeProgram program) : FrameFeature
    {
        private readonly PropertySet _properties = new();
        private BufferHandle _count;
        public uint Instances;

        public override FrameSlot Slot => FrameSlot.BeforeViews;

        public override void Setup(FrameBuilder builder)
        {
            _count = builder.Buffer("Test.ComputedCount", GraphBufferDesc.Structured(4, 4));
            builder.Export(_count);
        }

        public override void Render(FrameContext context)
        {
            _properties.SetBuffer("Counts", context.Get(_count));
            context.Command.SetComputeShader(program);
            context.Command.SetProperties(_properties);
            context.Command.SetPushConstants(Instances);
            context.Command.Dispatch(1, 1, 1);
        }
    }

    [Fact]
    public void GpuCounts_AProviderComputes_DrawInTheSameFrame()
    {
        const string source = """
            Compute
            {
                layout(local_size_x = 1) in;
                layout(set = 0, binding = 0, std430) buffer Counts { uint words[]; };
                layout(push_constant) uniform Push { uint count; } push;
                void main() { words[1] = push.count; }
            }
            """;
        ComputeProgram program = _device.ResourceFactory.CreateComputeProgram(_compiler.CompileCompute(new ShaderPass { State = new PassState(), Source = source }, [], _device.BackendType));
        _owned.Add(program);

        // A world object draws first, so the provider's commands are not the view's first
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(2, 2, 10)) * Double4x4.CreateScale(0.25), Custom0 = new Float4(0, 0, 1, 1) }, [ObjectColor()]);
        ComputedCount count = new(program) { Instances = 1 };
        _world.FrameFeatures.Add(count);
        _world.Providers.Add(new ComputedItems(_quad, ObjectColor(shift: 16)));

        Float4[] pixels = Frame(View());
        AssertAll(new Float3(1, 0, 0), pixels, Inside(-2, -2));
        AssertAll(new Float3(0, 0, 0), pixels, Inside(2, -2));
        AssertAll(new Float3(0, 0, 1), pixels, Inside(2, 2));

        count.Instances = 2;
        pixels = Frame(View());
        AssertAll(new Float3(1, 0, 0), pixels, Inside(2, -2));
        count.Instances = 0;
        pixels = Frame(View());
        AssertAll(new Float3(0, 0, 0), pixels, Inside(-2, -2));
        AssertAll(new Float3(0, 0, 1), pixels, Inside(2, 2));
    }

    [Fact]
    public void ProvidedTransparentItems_InAQueueTheWorldDoesNotUse_DrawInQueueOrder()
    {
        Shader glass = _renderer.LoadShader(ShaderParser.Parse(ObjectGlassSource));
        Shader later = _renderer.LoadShader(ShaderParser.Parse(ObjectGlassSource.Replace("3000", "3100")));

        // The provider's green glass is farther but in a later queue, so it draws over the world's near red one
        _world.Add(new RenderableDesc { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)), Custom0 = new Float4(1, 0, 0, 1) }, [_renderer.CreateMaterial(glass)]);
        Items items = new(_quad, _renderer.CreateMaterial(later));
        items.Draws.Add((Double4x4.CreateTranslation(new Double3(0, 0, 20)), new Float4(0, 1, 0, 1), 1));
        _world.Providers.Add(items);
        AssertAll(new Float3(0.25f, 0.5f, 0), Frame(View()));
    }

    // Draws a mesh of its own glass, tinted by the material
    private sealed class Pane(GpuMesh mesh, Material material, double depth) : RenderProvider
    {
        public override void Cull(in ProviderView view, DrawItemSink sink)
            => sink.Add(mesh, 0, material, new DrawItem { Transform = Double4x4.CreateTranslation(new Double3(0, 0, depth)) });
    }

    [Fact]
    public void ProvidedTransparentItemsInAFeaturesPass_BlendBackToFront()
    {
        Shader glow = _renderer.LoadShader(ShaderParser.Parse(GlowSource));
        Material Tint(Float3 c)
        {
            Material material = _renderer.CreateMaterial(glow);
            material.SetColor("_Color", new Float4(c.X, c.Y, c.Z, 0.5f));
            return material;
        }

        // The near one added first, so only sorting puts the far one under it
        _world.Providers.Add(new Pane(_quad, Tint(new Float3(0, 1, 0)), 10));
        _world.Providers.Add(new Pane(_quad, Tint(new Float3(1, 0, 0)), 20));
        _world.Features.Add(new Glows());
        AssertAll(new Float3(0.25f, 0.5f, 0), Frame(View()));
    }

    private sealed class ThrowingProvider : RenderProvider
    {
        public override void Extract() => throw new InvalidOperationException("broken");
        public override void Cull(in ProviderView view, DrawItemSink sink) => throw new InvalidOperationException("never culled");
    }

    [Fact]
    public void AProviderWhoseExtractThrows_IsReported_AndTheWorldStillDraws()
    {
        AddQuad(new Float3(0, 1, 0));
        _world.Providers.Add(new ThrowingProvider());
        _renderer.SubmitFrame(new FrameDesc { Views = new[] { View() } });
        _device.WaitForIdle();
        Assert.Contains(_errors, e => e.Code == "provider.extract");
        Assert.DoesNotContain(_errors, e => e.Code == "provider.cull");
        _errors.Clear();
        AddQuad(new Float3(0, 0, 1), scale: 0.5);
        _renderer.SubmitFrame(new FrameDesc { Views = new[] { View() } });
        _device.WaitForIdle();
        _errors.Clear();
        AssertAll(new Float3(0, 0, 1), ReadTarget(), Centre);
    }

    [Fact]
    public void TransientDraws_WaitingForTheirUpload_DrawOnceUploaded()
    {
        Material color = _renderer.CreateMaterial(_renderer.LoadShader(ShaderParser.Parse(VertexColorSource)));
        AssertAll(new Float3(0, 0, 0), Frame(View()));

        // One mesh per frame: the one queued first takes this frame, the transient's geometry the next
        _options.UploadBudgetPerFrame = 1;
        _owned.Add(_renderer.CreateMesh(new MeshData
        {
            VertexCount = 3,
            Streams = [new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = new byte[36] }],
            Indices = new uint[] { 0, 1, 2 },
        }));
        uint red = 0xFF0000FFu;
        Float3[] square = [new(-3, -3, 10), new(-1, -3, 10), new(-1, -1, 10), new(-3, -1, 10)];
        _world.DrawTransient(new TransientDraw { Positions = square, Colors = new uint[] { red, red, red, red }, Indices = new uint[] { 0, 1, 2, 0, 2, 3 }, Material = color });
        AssertAll(new Float3(0, 0, 0), Frame(View()));
        AssertAll(new Float3(1, 0, 0), Frame(View()), Inside(-2, -2));
        AssertAll(new Float3(0, 0, 0), Frame(View()));
    }

    [Fact]
    public void InstanceBatches_ScaledToNothingAndBack_KeepTheirInstances()
    {
        InstanceBatchDesc desc = new() { Mesh = _quad, Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)) };
        InstanceBatchHandle batch = _world.AddInstanceBatch(desc, [ObjectColor()], [new InstanceData { Transform = Small(-2, -2), Color = new Float4(1, 0, 0, 1) }]);
        desc.Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10)) * Double4x4.CreateScale(0);
        _world.SetInstanceBatch(batch, desc);
        AssertAll(new Float3(0, 0, 0), Frame(View()));
        desc.Transform = Double4x4.CreateTranslation(new Double3(0, 0, 10));
        _world.SetInstanceBatch(batch, desc);
        AssertAll(new Float3(1, 0, 0), Frame(View()), Inside(-2, -2));
    }

    [Fact]
    public void SpatialSetNames_ComeBackWhenTheirWorldsGo()
    {
        for (int i = 0; i < 20; i++)
        {
            RenderWorld world = _renderer.CreateWorld();
            world.CreateSpatialSet<Float4>("Set " + i);
            world.Dispose();
            Frame(View());
        }
    }

    [Fact]
    public void HostViewsOfTheShadowKind_DrawLikeAnyView()
    {
        AddQuad(new Float3(0, 1, 0));
        ViewDesc view = View();
        view.Kind = ViewKind.Shadow;
        AssertAll(new Float3(0, 1, 0), Frame(view));
    }

    [Fact]
    public void Selection_OrdersBySlotThenOrder_BuiltInBeforeWorldBeforeViewOnTies()
    {
        Named view = new("view", RenderSlot.Opaque);
        Named world = new("world", RenderSlot.Opaque);
        Named early = new("early", RenderSlot.Opaque, -1);
        Named post = new("post", RenderSlot.HdrPost);
        Assert.Equal(["SceneClearFeature", "CascadeFeature", "PrepassFeature", "early", "OpaqueFeature", "world", "view", "SkyFeature", "SceneColorFeature", "SceneDepthFeature", "TransmissionDepthFeature", "TransparentFeature", "post", "PresentFeature"], Select([post, world, early], [view]));
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

        Named opaque = new("opaque", RenderSlot.Opaque, group: "Opaque");
        Assert.DoesNotContain("OpaqueFeature", Select([opaque], []));
        Assert.Contains("opaque", Select([opaque], []));
    }

    [Fact]
    public void Selection_SkipsDisabledFeatures_AndOnesNotRunningOnTheViewsKind()
    {
        Named cameras = new("cameras", RenderSlot.HdrPost);
        Named probes = new("probes", RenderSlot.HdrPost, runsOn: ViewKinds.ReflectionProbe);
        Named off = new("off", RenderSlot.HdrPost, runsOn: ViewKinds.All) { Enabled = false };

        Assert.Equal(["SceneClearFeature", "CascadeFeature", "PrepassFeature", "OpaqueFeature", "SkyFeature", "SceneColorFeature", "SceneDepthFeature", "TransmissionDepthFeature", "TransparentFeature", "cameras", "PresentFeature"], Select([cameras, probes, off], []));
        Assert.Equal(["SceneClearFeature", "CascadeFeature", "PrepassFeature", "OpaqueFeature", "SkyFeature", "SceneColorFeature", "SceneDepthFeature", "TransmissionDepthFeature", "TransparentFeature", "probes", "PresentFeature"], Select([cameras, probes, off], [], ViewKind.ReflectionProbe));
    }

    // Takes a built in frame feature's group and provides what its consumers read
    private static RenderResourceID? ProvidedBy(string group) => group switch
    {
        "Deform" => RendererOutputs.DeformedVertices,
        "LightProbes" => RendererOutputs.ObjectProbes,
        "LocalShadows" => RendererOutputs.ShadowPages,
        "CaptureFill" => RendererOutputs.CaptureCubes,
        _ => RendererOutputs.ReadbackStaging,
    };

    private sealed class FrameStand(FrameSlot slot, string group, RenderResourceID? provides) : FrameFeature
    {
        public int Runs;
        public override FrameSlot Slot => slot;
        public override string? ExclusiveGroup => group;
        public override void Setup(FrameBuilder builder)
        {
            if (provides is { } id)
                builder.Export(builder.Buffer(id, GraphBufferDesc.Structured(4, 4)));
        }
        public override void Render(FrameContext context) => Runs++;
    }

    [Theory]
    [InlineData("Deform", FrameSlot.Deform, false)]
    [InlineData("LightProbes", FrameSlot.LightProbes, true)]
    [InlineData("LocalShadows", FrameSlot.Shadows, true)]
    [InlineData("CaptureFill", FrameSlot.Captures, true)]
    [InlineData("Readback", FrameSlot.Readback, false)]
    public void AFrameFeatureOfABuiltInsGroup_ReplacesIt(string group, FrameSlot slot, bool inWorld)
    {
        FrameStand stand = new(slot, group, ProvidedBy(group));
        if (inWorld)
            _world.FrameFeatures.Add(stand);
        else
            _renderer.FrameFeatures.Add(stand);
        AddQuad(new Float3(1, 0, 0));
        AssertAll(new Float3(1, 0, 0), Frame(View()), Centre);

        FrameFeature[] active = [.. _renderer.Frame.ActiveFrameFeatures];
        Assert.Contains(stand, active);
        Assert.DoesNotContain(active, f => f.IsBuiltIn && f.ExclusiveGroup == group);
        Assert.Equal(1, stand.Runs);
    }

    [Fact]
    public void AReplacementThatProvidesNothing_FailsTheFrameNamingWhatTheDrawsRead()
    {
        _renderer.FrameFeatures.Add(new FrameStand(FrameSlot.Deform, "Deform", null));
        AddQuad(new Float3(1, 0, 0));
        _renderer.SubmitFrame(new FrameDesc { Views = new[] { View() } });
        _device.WaitForIdle();
        Assert.Contains(_errors, e => e.Code == "frame.failed" && e.Message.Contains("Graphite.Deform.Vertices"));
        _errors.Clear();
    }

    private sealed class ViewStand(RenderSlot slot, string group, bool scene = false) : RenderFeature
    {
        public int Setups;
        public override RenderSlot Slot => slot;
        public override string? ExclusiveGroup => group;
        public override ViewKinds RunsOn => ViewKinds.All;

        public override void Setup(FeatureBuilder builder, in FeatureView view)
        {
            Setups++;
            if (scene)
            {
                GraphTextureDesc desc = new() { Width = (int)view.Width, Height = (int)view.Height, ColorFormats = [PixelFormat.R16_G16_B16_A16_Float], EnableDepth = true, DepthFormat = PixelFormat.D32_Float, ArrayLayers = view.LayerCount > 1 ? (uint)view.LayerCount : 0 };
                builder.Publish(RendererOutputs.Scene, builder.Temporary("Scene", desc, TextureState.Attachment));
            }
            else if (group == "Present")
            {
                builder.ReadColor();
            }
        }

        public override void Render(FeatureContext context) { }
    }

    [Theory]
    [InlineData(RenderSlot.Clear, "SceneClear")]
    [InlineData(RenderSlot.Shadows, "Cascades")]
    [InlineData(RenderSlot.Present, "Present")]
    public void AViewFeatureOfABuiltInsGroup_ReplacesIt(RenderSlot slot, string group)
    {
        ViewStand stand = new(slot, group, scene: slot == RenderSlot.Clear);
        _world.Features.Add(stand);
        AddQuad(new Float3(1, 0, 0));
        Float4[] pixels = Frame(View());

        Assert.True(stand.Setups > 0);
        List<RenderFeature> selected = _renderer.Frame.Camera(1)!.Selected;
        Assert.Contains(stand, selected);
        Assert.DoesNotContain(selected, f => f.ExclusiveGroup == group && f != stand);
        if (slot == RenderSlot.Present)
            AssertAll(new Float3(0, 0, 0), pixels);
        else if (slot != RenderSlot.Clear)
            AssertAll(new Float3(1, 0, 0), pixels, Centre);
    }

    [Fact]
    public void ASceneClearReplacement_ThatPublishesNoScene_FailsTheFrame()
    {
        _world.Features.Add(new ViewStand(RenderSlot.Clear, "SceneClear"));
        AddQuad(new Float3(1, 0, 0));
        _renderer.SubmitFrame(new FrameDesc { Views = new[] { View() } });
        _device.WaitForIdle();
        Assert.Contains(_errors, e => e.Code == "frame.failed" && e.Message.Contains("Graphite.Scene"));
        _errors.Clear();
    }
}
