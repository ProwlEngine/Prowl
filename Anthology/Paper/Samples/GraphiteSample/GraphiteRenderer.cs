using System.Text;

using Prowl.Quill;
using Prowl.Vector;
using Prowl.Vector.Geometry;
using Prowl.Graphite;
using Prowl.Graphite.RenderGraph;
using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;

namespace GraphiteSample;


internal readonly struct CanvasView : IRenderView
{
    public CanvasView(uint width, uint height)
    {
        PixelWidth = width;
        PixelHeight = height;
    }

    public uint PixelWidth { get; }
    public uint PixelHeight { get; }
}


/// <summary>
/// Renders a Quill <see cref="Canvas"/> using Prowl.Graphite's render-graph Pass API. The graph has
/// two passes: Scene (draws the canvas into an offscreen color target, including any backdrop-blur
/// ping-pong needed mid-draw) and Present (blits Scene to the swapchain). Present depends on Scene
/// through the declared "Scene" texture, so the graph orders them without either pass needing to know
/// about the other's internals. The backdrop-blur mip chain is not itself part of the graph: its
/// iteration count is chosen per draw call at render time (from the brush's blur radius), which can't
/// be expressed as a fixed set of passes declared once in Setup, so it stays free-form scratch state
/// owned by the Scene pass, same as it always has been.
/// </summary>
public class GraphiteRenderer : ICanvasRenderer, IDisposable
{
    private struct CanvasVertexSource : IVertexSource
    {
        public DeviceBuffer VertexBuffer;
        public DeviceBuffer IndexBuffer;
        public uint IndexCount;

        public readonly PrimitiveTopology Topology => PrimitiveTopology.TriangleList;

        public readonly void ResolveSlot(uint layoutSlot, in VertexLayoutDescription layout, out VertexBinding binding)
            => binding = new VertexBinding(VertexBuffer);

        public readonly bool TryGetIndexBuffer(out DeviceBuffer buffer, out IndexFormat format, out uint indexCount)
        {
            buffer = IndexBuffer;
            format = IndexFormat.UInt32;
            indexCount = IndexCount;
            return true;
        }
    }


    private const int MaxBlurLevels = 6;
    private const PixelFormat TargetFormat = PixelFormat.R8_G8_B8_A8_UNorm;
    private static readonly Color ClearColor = new(0f, 0f, 0f, 1f);
    private static readonly Keyword UpsampleOn = new("Upsample", "true");
    private static readonly Keyword UpsampleOff = new("Upsample", "false");


    private readonly GraphicsDevice _gl;

    public bool SupportsBackdropBlur => true;

    private ShaderPass _canvasPass;
    private GraphicsProgram _canvasProgram;
    private ShaderPass _blurPass;

    private Float4x4 _projection;
    private TextureGraphite _defaultTexture;
    private Sampler _sampler;

    private int _fbWidth;
    private int _fbHeight;

    private readonly ScenePass _scenePass;
    private readonly PresentPass _presentPass;
    private readonly CanvasPipeline _pipeline;
    private CanvasView[] _views;


    private static Func<string, Memory<byte>?> s_fileLoader = (x) =>
    {
        x = Path.Join(AppContext.BaseDirectory, "Shaders", x);

        Console.WriteLine(x);

        if (!File.Exists(x))
            return null;

        return File.ReadAllBytes(x);
    };


    public GraphiteRenderer(GraphicsDevice gl)
    {
        _gl = gl;
        _gl.SyncToVerticalBlank = false;

        _scenePass = new ScenePass(this);
        _presentPass = new PresentPass(this, _scenePass);
        _pipeline = new CanvasPipeline(_scenePass, _presentPass);
    }


    public void Initialize(int width, int height, TextureGraphite defaultTexture)
    {
        _defaultTexture = defaultTexture;
        CreateShaderProgram();

        _sampler = _gl.ResourceFactory.CreateSampler(new SamplerDescription
        {
            AddressModeU = SamplerAddressMode.Clamp,
            AddressModeV = SamplerAddressMode.Clamp,
            AddressModeW = SamplerAddressMode.Clamp,
            Filter = SamplerFilter.MinLinear_MagLinear_MipLinear,
        });

        UpdateProjection(width, height);
    }

    private void CreateShaderProgram()
    {
        SlangShaderCompiler compiler = new();
        compiler.RegisterModule(_gl.BackendType switch
        {
            GraphicsBackend.Vulkan => new VulkanCompiler("spirv_1_4"),
            _ => throw new NotSupportedException($"Unsupported graphics backend: {_gl.BackendType}")
        });

        compiler.BeginSession([new DirectoryInfo("/Shaders")], s_fileLoader);

        ShaderDefinition canvasDef = ShaderParser.Parse(ReadShaderSource("Shader.shader"));
        canvasDef.Create(_gl, compiler, new Variant(), CompileMode.All);
        _canvasPass = canvasDef.Passes![0];
        _canvasProgram = CreateCanvasProgram(_canvasPass);

        ShaderDefinition blurDef = ShaderParser.Parse(ReadShaderSource("Blur.shader"));
        blurDef.Create(_gl, compiler, new Variant(), CompileMode.All);
        _blurPass = blurDef.Passes![0];

        compiler.EndSession();
    }


    // The canvas vertex buffer is a single interleaved struct with a byte-packed color
    // (Vertex.SizeInBytes, COLOR0 as Byte4_Norm). Slang reflection can only describe one
    // buffer slot per vertex-input field and has no way to express a packed UNORM color from
    // a `float4` field, so the reflected VertexLayouts are replaced by hand here instead of
    // going through ShaderPass.ResolveProgram/SetShader(ShaderPass).
    private GraphicsProgram CreateCanvasProgram(ShaderPass pass)
    {
        if (!pass.ActiveVariant.TryGetDescription(_gl.BackendType, out ShaderDescription description))
            throw new InvalidOperationException($"Canvas shader was not compiled for backend {_gl.BackendType}.");

        description.VertexLayouts =
        [
            new VertexLayoutDescription(0, (uint)Vertex.SizeInBytes,
                new VertexElementDescription("POSITION0", VertexElementFormat.Float2, 0),
                new VertexElementDescription("TEXCOORD0", VertexElementFormat.Float2, 8),
                new VertexElementDescription("COLOR0", VertexElementFormat.Byte4_Norm, 16))
        ];

        description.BlendState = pass.State.ToBlendState(BlendStateDescription.SingleDisabled);
        description.DepthStencilState = pass.State.ToDepthStencilState(DepthStencilStateDescription.DepthOnlyLessEqual);
        description.RasterizerState = pass.State.ToRasterizerState(new(FaceCullMode.Back, FrontFace.Clockwise, true, false));

        return _gl.ResourceFactory.CreateGraphicsProgram(description);
    }


    private static string ReadShaderSource(string fileName)
    {
        Memory<byte>? bytes = s_fileLoader(fileName);
        if (bytes == null)
            throw new FileNotFoundException(fileName);

        return Encoding.UTF8.GetString(bytes.Value.Span);
    }


    public void UpdateProjection(int width, int height)
    {
        _fbWidth = width;
        _fbHeight = height;
        _projection = Float4x4.CreateOrthoOffCenter(0, width, height, 0, -1, 1);
        _views = new[] { new CanvasView((uint)width, (uint)height) };
    }


    public object CreateTexture(uint width, uint height)
    {
        return TextureGraphite.CreateTexture(_gl, width, height);
    }


    public Int2 GetTextureSize(object texture)
    {
        if (texture is not TextureGraphite tex)
            throw new ArgumentException("Invalid texture type");

        return new Int2((int)tex.Width, (int)tex.Height);
    }


    public void SetTextureData(object texture, IntRect bounds, byte[] data)
    {
        if (texture is not TextureGraphite tex)
            throw new ArgumentException("Invalid texture type");

        tex.SetTextureData(_gl, bounds, data);
    }


    public void RenderCalls(Canvas canvas, IReadOnlyList<DrawCall> drawCalls)
    {
        _scenePass.SetFrame(canvas, drawCalls);
        _gl.DispatchGraph(_pipeline, _views);
    }


    private static Float4 ToFloat4(Color32 color)
        => new(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);


    public void Cleanup()
    {
        _scenePass.DisposeResources();

        _sampler?.Dispose();
        _canvasProgram?.Dispose();
    }


    public void Dispose()
    {
        Cleanup();
    }


    // Draws the canvas into the graph's "Scene" texture, including any backdrop-blur ping-pong
    // needed mid-draw. The blur mip chain is scratch state private to this pass: its iteration count
    // varies per draw call at render time, so it can't be expressed as fixed graph resources declared
    // once in Setup.
    private sealed class ScenePass : IPass<CanvasView, int>
    {
        private readonly GraphiteRenderer _owner;

        private TextureHandle _sceneHandle;
        public TextureHandle SceneHandle => _sceneHandle;

        private Canvas _canvas;
        private IReadOnlyList<DrawCall> _drawCalls;

        private StreamingBuffer _activeVbo;
        private uint _vboCapacity;
        private StreamingBuffer _activeEbo;
        private uint _eboCapacity;

        private readonly PropertySet _properties = new();
        private readonly CanvasVertexSource _fullscreenSource = new();

        private readonly Texture[] _blurTex = new Texture[MaxBlurLevels];
        private readonly Framebuffer[] _blurFB = new Framebuffer[MaxBlurLevels];
        private readonly Int2[] _blurSize = new Int2[MaxBlurLevels];
        private int _blurBuiltW;
        private int _blurBuiltH;

        public ScenePass(GraphiteRenderer owner) => _owner = owner;

        public string Name => "Scene";

        public void SetFrame(Canvas canvas, IReadOnlyList<DrawCall> drawCalls)
        {
            _canvas = canvas;
            _drawCalls = drawCalls;
        }

        public void Setup(RenderContextBuilder builder)
            => _sceneHandle = builder.GetOutputTexture("Scene", GraphTextureDesc.ViewSized(false, 1f, TargetFormat));

        public void Render(RenderContext<CanvasView, int> context)
        {
            EnsureBlurTargets(_owner._fbWidth, _owner._fbHeight);

            bool hasGeometry = _drawCalls.Count > 0 && _canvas.Vertices.Count > 0 && _canvas.Indices.Count > 0;

            CommandBuffer cmd = context.GetCommandBuffer(Name);
            cmd.Begin();

            // Upload geometry before binding a framebuffer: buffer uploads must happen outside a render pass.
            if (hasGeometry)
                UploadGeometry(cmd, context.Task);

            RenderTexture scene = context.GetRenderTexture(_sceneHandle);

            cmd.SetFramebuffer(scene.Framebuffer);
            cmd.ClearColorTarget(0, ClearColor);

            if (hasGeometry)
            {
                float dpiScale = (float)_canvas.FramebufferScale;
                int indexOffset = 0;
                foreach (DrawCall drawCall in _drawCalls)
                {
                    ProcessDrawCall(cmd, context, scene, drawCall, indexOffset, dpiScale);
                    indexOffset += drawCall.ElementCount;
                }
            }

            cmd.End();
            context.SubmitCommandBuffer(cmd);
        }


        private void UploadGeometry(CommandBuffer cmd, ExecutionTask task)
        {
            Vertex[] vertices = [.. _canvas.Vertices];
            uint[] indices = [.. _canvas.Indices];

            EnsureBuffer(ref _activeVbo, ref _vboCapacity, (uint)(vertices.Length * Vertex.SizeInBytes), BufferUsage.VertexBuffer);
            EnsureBuffer(ref _activeEbo, ref _eboCapacity, (uint)(indices.Length * sizeof(uint)), BufferUsage.IndexBuffer);

            cmd.UpdateBuffer(_activeVbo.ForExecution(task), 0, vertices);
            cmd.UpdateBuffer(_activeEbo.ForExecution(task), 0, indices);
        }


        private void EnsureBuffer(ref StreamingBuffer buffer, ref uint capacity, uint sizeInBytes, BufferUsage usage)
        {
            if (buffer != null && sizeInBytes <= capacity)
                return;

            buffer?.Dispose();
            uint newCapacity = (uint)(sizeInBytes * 1.5f) + 256;
            buffer = _owner._gl.ResourceFactory.CreateStreamingBuffer(new BufferDescription(newCapacity, usage));
            capacity = newCapacity;
        }


        private void ProcessDrawCall(CommandBuffer cmd, RenderContext<CanvasView, int> context, RenderTexture scene, DrawCall drawCall, int indexOffset, float dpiScale)
        {
            Brush brush = drawCall.Brush;
            float blur = brush.BackdropBlur;

            // Backdrop blur: blur the scene drawn so far into _blurTex[0], then composite the shape over it.
            if (blur > 0f)
            {
                RenderBackdropBlur(cmd, scene, blur);
                cmd.SetFramebuffer(scene.Framebuffer);
            }

            TextureGraphite texture = (TextureGraphite)(drawCall.Texture ?? _owner._defaultTexture);
            // Font atlas on its own sampler so text batches with shapes (text samples fontTexture).
            TextureGraphite fontTex = (TextureGraphite)(drawCall.FontAtlas ?? _owner._defaultTexture);

            _properties.SetMatrix("projection", _owner._projection);
            texture.SetTexture(_properties, "texture0");
            fontTex.SetTexture(_properties, "fontTexture");

            // 1 / font atlas size, so the text shader's distance-field screen range is correct at any zoom.
            Int2 texSize = _owner.GetTextureSize(fontTex);
            _properties.SetFloat2("atlasTexelSize", new Float2(texSize.X > 0 ? 1f / texSize.X : 0f, texSize.Y > 0 ? 1f / texSize.Y : 0f));

            drawCall.GetScissor(out Float4x4 scissorMat, out Float2 scissorExt);
            _properties.SetMatrix("scissorMat", scissorMat);
            _properties.SetFloat2("scissorExt", scissorExt);

            _properties.SetMatrix("brushMat", brush.BrushMatrix);
            _properties.SetInt("brushType", (int)brush.Type);
            _properties.SetFloat4("brushColor1", ToFloat4(brush.Color1));
            _properties.SetFloat4("brushColor2", ToFloat4(brush.Color2));
            _properties.SetFloat4("brushParams", new Float4(brush.Point1.X, brush.Point1.Y, brush.Point2.X, brush.Point2.Y));
            _properties.SetFloat2("brushParams2", new Float2(brush.CornerRadii, brush.Feather));
            _properties.SetMatrix("brushTextureMat", brush.TextureMatrix);
            _properties.SetFloat("dpiScale", dpiScale);

            _properties.SetFloat2("viewportSize", new Float2(_owner._fbWidth, _owner._fbHeight));
            _properties.SetFloat("backdropBlurAmount", blur);

            // backdropTexture always needs a bound sampler; use the blurred scene when blurring, else any texture.
            if (blur > 0f)
                _properties.SetTexture("backdropTexture", _blurTex[0], _owner._sampler);
            else
                texture.SetTexture(_properties, "backdropTexture");

            CanvasVertexSource source = new()
            {
                VertexBuffer = _activeVbo.ForExecution(context.Task),
                IndexBuffer = _activeEbo.ForExecution(context.Task),
                IndexCount = (uint)drawCall.ElementCount
            };

            cmd.SetShader(_owner._canvasProgram);
            cmd.SetVertexSource(source);
            cmd.SetProperties(_properties);

            cmd.DrawIndexed(1, (uint)indexOffset, 0, 0);
        }


        private static void ComputeBlurParams(float radius, out int iterations, out float offset)
        {
            float r = MathF.Max(radius, 2f);
            iterations = Math.Clamp((int)MathF.Floor(MathF.Log2(r)) - 1, 1, MaxBlurLevels - 1);
            offset = Math.Clamp(r / (1 << (iterations + 1)), 0.5f, 6f);
        }


        private void RenderBackdropBlur(CommandBuffer cmd, RenderTexture scene, float radius)
        {
            ComputeBlurParams(radius, out int iterations, out float offset);

            // Downsample pass
            BlurPass(cmd, scene.ColorTextures[0], new Int2(_owner._fbWidth, _owner._fbHeight), 0, false, offset);
            for (int i = 0; i < iterations; i++)
                BlurPass(cmd, _blurTex[i], _blurSize[i], i + 1, false, offset);

            // Upsample pass
            for (int i = iterations; i > 0; i--)
                BlurPass(cmd, _blurTex[i], _blurSize[i], i - 1, true, offset);
        }


        private void BlurPass(CommandBuffer cmd, Texture source, Int2 sourceSize, int dstLevel, bool upsample, float offset)
        {
            Int2 dstSize = _blurSize[dstLevel];
            Int2 basis = upsample ? dstSize : sourceSize;

            cmd.SetFramebuffer(_blurFB[dstLevel]);

            _owner._blurPass.SetKeyword(upsample ? UpsampleOn : UpsampleOff);

            _properties.SetTexture("sourceTexture", source, _owner._sampler);
            _properties.SetFloat2("halfPixel", new Float2(0.5f / basis.X, 0.5f / basis.Y));
            _properties.SetFloat("offset", offset);

            cmd.SetShader(_owner._blurPass);
            cmd.SetVertexSource(_fullscreenSource);
            cmd.SetProperties(_properties);
            cmd.Draw(3);
        }


        private void EnsureBlurTargets(int width, int height)
        {
            if (_blurTex[0] != null && _blurBuiltW == width && _blurBuiltH == height)
                return;

            DisposeBlurTargets();

            for (int i = 0; i < MaxBlurLevels; i++)
            {
                int w = Math.Max(1, width >> (i + 1));
                int h = Math.Max(1, height >> (i + 1));
                _blurSize[i] = new Int2(w, h);

                TextureDescription blurDesc = TextureDescription.Texture2D((uint)w, (uint)h, 1, 1, TargetFormat, TextureUsage.Sampled | TextureUsage.RenderTarget);
                _blurTex[i] = _owner._gl.ResourceFactory.CreateTexture(blurDesc);
                _blurFB[i] = _owner._gl.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, _blurTex[i]));
            }

            _blurBuiltW = width;
            _blurBuiltH = height;
        }


        private void DisposeBlurTargets()
        {
            for (int i = 0; i < MaxBlurLevels; i++)
            {
                _blurFB[i]?.Dispose();
                _blurTex[i]?.Dispose();
                _blurFB[i] = null;
                _blurTex[i] = null;
            }
        }


        public void DisposeResources()
        {
            DisposeBlurTargets();

            _activeVbo?.Dispose();
            _activeEbo?.Dispose();
        }
    }


    // Blits the graph's "Scene" texture to the swapchain, reusing the blur shader at zero offset as a
    // plain fullscreen copy. Depends on Scene through the declared texture handle: the graph runs
    // ScenePass first because this pass reads what that one writes.
    private sealed class PresentPass : IPresentPass<CanvasView, int>
    {
        private readonly GraphiteRenderer _owner;
        private readonly ScenePass _scenePass;
        private readonly PropertySet _properties = new();
        private readonly CanvasVertexSource _fullscreenSource = new();

        public PresentPass(GraphiteRenderer owner, ScenePass scenePass)
        {
            _owner = owner;
            _scenePass = scenePass;
        }

        public string Name => "Present";

        public void Setup(PresentContextBuilder builder)
        {
            builder.RequestSwapchain();
        }

        public void Present(RenderContext<CanvasView, int> context)
        {
            Framebuffer? target = context.SwapchainTarget;
            if (target == null)
                return;

            RenderTexture scene = context.GetRenderTexture(_scenePass.SceneHandle);

            CommandBuffer cmd = context.GetCommandBuffer(Name);
            cmd.Begin();
            cmd.SetFramebuffer(target);

            _owner._blurPass.SetKeyword(UpsampleOff);

            _properties.SetTexture("sourceTexture", scene.ColorTextures[0], _owner._sampler);
            _properties.SetFloat2("halfPixel", new Float2(0f, 0f));
            _properties.SetFloat("offset", 0f);

            cmd.SetShader(_owner._blurPass);
            cmd.SetVertexSource(_fullscreenSource);
            cmd.SetProperties(_properties);
            cmd.Draw(3);

            cmd.End();
            context.SubmitCommandBuffer(cmd);
            context.Present();
        }
    }


    private sealed class CanvasPipeline : RenderPipeline<CanvasView, int>
    {
        private readonly ScenePass _scenePass;
        private readonly PresentPass _presentPass;

        public CanvasPipeline(ScenePass scenePass, PresentPass presentPass)
        {
            _scenePass = scenePass;
            _presentPass = presentPass;
        }

        protected override void InitializePasses()
        {
            AddPass(_scenePass);
            SetPresentPass(_presentPass);
        }
    }
}
