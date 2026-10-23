using System.Text;

using Prowl.Quill;
using Prowl.Vector;
using Prowl.Vector.Geometry;
using Prowl.Graphite;
using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;

namespace GraphiteExample;


/// <summary>
/// Renders a Quill <see cref="Canvas"/> through a Prowl.Graphite <see cref="FrameGraph"/> built every frame:
/// a Scene pass draws the canvas (and any backdrop blur) into an offscreen target, then a Present pass
/// blits it to the swapchain. The blur mip chain is scratch state owned by the Scene pass, since its
/// iteration count varies per draw call.
/// </summary>
public class GraphiteRenderer : ICanvasRenderer, IDisposable
{
    // How far below the framebuffer the blur pyramid starts: 1 = half res, 2 = quarter. The canvas
    // composites straight from level 0, so this also decides the resolution the backdrop is sampled
    // at. Quarter is four times cheaper across every pass and is imperceptible above roughly an
    // eight pixel radius, since detail finer than the blur is destroyed anyway.
    private const int BlurBaseShift = 2;
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

    private readonly FrameGraph _graph;
    private readonly ScenePass _scenePass;
    private readonly PresentPass _presentPass;


    private static Func<string, Memory<byte>?> s_fileLoader = (x) =>
    {
        x = Path.Join(AppContext.BaseDirectory, x);

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
        _presentPass = new PresentPass(this);
        _graph = new FrameGraph(gl, "Quill");
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
            MinFilter = FilterMode.Linear,
            MagFilter = FilterMode.Linear,
            MipFilter = FilterMode.Linear,
        });

        UpdateProjection(width, height);
    }

    private void CreateShaderProgram()
    {
        using GlslShaderCompiler compiler = new();

        ShaderDefinition canvasDef = ShaderParser.Parse(ReadShaderSource("Shader.shader"));
        canvasDef.Create(_gl, compiler, new Variant(), CompileMode.All);
        _canvasPass = canvasDef.Passes![0];
        _canvasProgram = CreateCanvasProgram(_canvasPass);

        ShaderDefinition blurDef = ShaderParser.Parse(ReadShaderSource("Blur.shader"));
        blurDef.Create(_gl, compiler, new Variant(), CompileMode.All);
        _blurPass = blurDef.Passes![0];
    }


    // The canvas vertex buffer is one interleaved struct with a byte packed color (COLOR0 as
    // Byte4_Norm). Reflection gives one buffer slot per input and reads a vec4 input as floats,
    // so the reflected VertexLayouts are replaced by hand here.
    private GraphicsProgram CreateCanvasProgram(ShaderPass pass)
    {
        if (!pass.GetVariant(0).TryGetDescription(_gl.BackendType, out ShaderDescription description))
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
        TextureHandle target = _graph.ImportTarget(_gl.MainSwapchain.Framebuffer, PixelFormat.D24_UNorm_S8_UInt);
        TextureHandle scene = _graph.Create("Scene", GraphTextureDesc.Sized(_fbWidth, _fbHeight, TargetFormat));

        scene = _scenePass.AddTo(_graph, canvas, drawCalls, scene);
        _presentPass.AddTo(_graph, scene, target);
        _graph.Execute();
    }


    private static Float4 ToFloat4(Color32 color)
        => new(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);


    public void Cleanup()
    {
        _scenePass.DisposeResources();

        _graph?.Dispose();
        _sampler?.Dispose();
        _canvasProgram?.Dispose();
    }


    public void Dispose()
    {
        Cleanup();
    }


    private sealed class ScenePass
    {
        private readonly GraphiteRenderer _owner;

        private TextureHandle _sceneHandle;
        private Canvas _canvas;
        private IReadOnlyList<DrawCall> _drawCalls;

        private DeviceBuffer _activeVbo;
        private DeviceBuffer _activeEbo;

        private readonly PropertySet _properties = new();

        private readonly Texture[] _blurTex = new Texture[MaxBlurLevels];
        private readonly Framebuffer[] _blurFB = new Framebuffer[MaxBlurLevels];
        private readonly Int2[] _blurSize = new Int2[MaxBlurLevels];
        private bool _backdropDirty = true;
        private float _lastBlurRadius = -1f;
        private int _blurBuiltW;
        private int _blurBuiltH;

        public ScenePass(GraphiteRenderer owner) => _owner = owner;

        public TextureHandle AddTo(FrameGraph graph, Canvas canvas, IReadOnlyList<DrawCall> drawCalls, TextureHandle scene)
        {
            _canvas = canvas;
            _drawCalls = drawCalls;

            graph.AddPass("Scene",
                pass => _sceneHandle = pass.Write(scene, firstWrite: TargetLoadStoreOps.Clear(ClearColor)),
                Render);
            return _sceneHandle;
        }

        private void Render(PassContext context)
        {
            CommandBuffer cmd = context.Command;
            EnsureBlurTargets(_owner._fbWidth, _owner._fbHeight);

            _backdropDirty = true;

            bool hasGeometry = _drawCalls.Count > 0 && _canvas.VertexCount > 0 && _canvas.IndexCount > 0;

            // Upload geometry before binding a framebuffer: buffer uploads must happen outside a render pass.
            if (hasGeometry)
                UploadGeometry(cmd, context.Task);

            RenderTexture scene = context.Get(_sceneHandle);

            cmd.SetFramebuffer(scene.Framebuffer, new TargetLoadStoreOps(AttachmentOps.Clear(ClearColor), AttachmentOps.Loaded));

            if (hasGeometry)
            {
                float dpiScale = (float)_canvas.FramebufferScale;
                int indexOffset = 0;
                foreach (DrawCall drawCall in _drawCalls)
                {
                    ProcessDrawCall(cmd, scene, drawCall, indexOffset, dpiScale);
                    indexOffset += drawCall.ElementCount;
                }
            }
        }


        private void UploadGeometry(CommandBuffer cmd, ExecutionTask task)
        {
            int vertexCount = _canvas.VertexCount;
            int indexCount = _canvas.IndexCount;

            _activeVbo = _owner._gl.RentTransientBuffer(task, new BufferDescription((uint)(vertexCount * Vertex.SizeInBytes), BufferUsage.VertexBuffer));
            _activeEbo = _owner._gl.RentTransientBuffer(task, new BufferDescription((uint)(indexCount * sizeof(uint)), BufferUsage.IndexBuffer));

            cmd.UpdateBuffer(_activeVbo, 0, _canvas.Vertices);
            cmd.UpdateBuffer(_activeEbo, 0, _canvas.Indices);
        }


        private void ProcessDrawCall(CommandBuffer cmd, RenderTexture scene, DrawCall drawCall, int indexOffset, float dpiScale)
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

            // Scissor and brush transforms are 2D affines with the framebuffer scale already folded
            // in, so the shader needs neither a matrix nor a dpi divide.
            drawCall.GetScissor(dpiScale, out Float4 scissorXf, out Float2 scissorT, out Float2 scissorExt);
            _properties.SetFloat4("scissorTransform", scissorXf);
            _properties.SetFloat2("scissorTranslation", scissorT);
            _properties.SetFloat2("scissorExt", scissorExt);

            drawCall.GetBrushTransform(dpiScale, out Float4 brushXf, out Float2 brushT);
            _properties.SetFloat4("brushTransform", brushXf);
            _properties.SetFloat2("brushTranslation", brushT);
            _properties.SetInt("brushType", (int)brush.Type);
            _properties.SetFloat4("brushColor1", ToFloat4(brush.Color1));
            _properties.SetFloat4("brushColor2", ToFloat4(brush.Color2));
            _properties.SetFloat4("brushParams", new Float4(brush.Point1.X, brush.Point1.Y, brush.Point2.X, brush.Point2.Y));
            _properties.SetFloat2("brushParams2", new Float2(brush.CornerRadii, brush.Feather));
            drawCall.GetTextureTransform(dpiScale, out Float4 texXf, out Float2 texT);
            _properties.SetFloat4("textureTransform", texXf);
            _properties.SetFloat2("textureTranslation", texT);
            _properties.SetFloat("sdfPxRange", _canvas.Text.FontEngine.DistanceRange);

            _properties.SetFloat2("viewportSize", new Float2(_owner._fbWidth, _owner._fbHeight));
            _properties.SetFloat("backdropBlurAmount", blur);
            // Vulkan stores the scene top-left origin, so the backdrop samples upright.
            _properties.SetInt("backdropFlipY", 0);

            // backdropTexture always needs a bound sampler; use the blurred scene when blurring, else any texture.
            if (blur > 0f)
                _properties.SetTexture("backdropTexture", _blurTex[0], _owner._sampler);
            else
                texture.SetTexture(_properties, "backdropTexture");

            VertexSource source = new VertexSource()
                .SetBuffer("POSITION0", _activeVbo)
                .SetIndexBuffer(_activeEbo, IndexFormat.UInt32, (uint)drawCall.ElementCount);

            cmd.SetShader(_owner._canvasProgram);
            cmd.SetVertexSource(source);
            cmd.SetProperties(_properties);

            cmd.DrawIndexed(1, (uint)indexOffset, 0, 0);


            _backdropDirty = true;
        }


        private static void ComputeBlurParams(float radius, out int iterations, out float offset)
        {
            // radius is in screen pixels, but the pyramid maths below works in level-0 texels, and one of

            // those spans 1 << BlurBaseShift pixels. Converting here is what makes SetBackdropBlur(22)

            // actually mean 22 pixels regardless of what resolution the pyramid starts at.

            float r = MathF.Max(radius / (1 << BlurBaseShift), 2f);
            iterations = Math.Clamp((int)MathF.Floor(MathF.Log2(r)) - 1, 1, MaxBlurLevels - 1);
            offset = Math.Clamp(r / (1 << (iterations + 1)), 0.5f, 6f);
        }


        private void RenderBackdropBlur(CommandBuffer cmd, RenderTexture scene, float radius)
        {
            // Two frosted shapes in a row see the same scene behind them, so the pyramid only needs
            // rebuilding when something has been drawn since the last one, or the radius moved.
            if (!_backdropDirty && radius == _lastBlurRadius)
                return;

            _backdropDirty = false;
            _lastBlurRadius = radius;

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


            _properties.SetTexture("sourceTexture", source, _owner._sampler);
            _properties.SetFloat2("halfPixel", new Float2(0.5f / basis.X, 0.5f / basis.Y));
            _properties.SetFloat("offset", offset);

            cmd.SetShader(_owner._blurPass, [upsample ? UpsampleOn : UpsampleOff]);
            cmd.SetVertexSource(VertexSource.None);
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
                int w = Math.Max(1, width >> (i + BlurBaseShift));
                int h = Math.Max(1, height >> (i + BlurBaseShift));
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
        }
    }


    // Blits Scene to the swapchain, reusing the blur shader at zero offset as a plain fullscreen copy.
    private sealed class PresentPass
    {
        private readonly GraphiteRenderer _owner;
        private readonly PropertySet _properties = new();
        private TextureHandle _sceneHandle;

        public PresentPass(GraphiteRenderer owner)
        {
            _owner = owner;
        }

        public void AddTo(FrameGraph graph, TextureHandle scene, TextureHandle target)
        {
            graph.AddPass("Present",
                pass =>
                {
                    _sceneHandle = pass.Read(scene);
                    pass.Write(target);
                },
                Render);
        }

        private void Render(PassContext context)
        {
            CommandBuffer cmd = context.Command;
            RenderTexture scene = context.Get(_sceneHandle);

            _properties.SetTexture("sourceTexture", scene.ColorTextures[0], _owner._sampler);
            _properties.SetFloat2("halfPixel", new Float2(0f, 0f));
            _properties.SetFloat("offset", 0f);

            cmd.SetShader(_owner._blurPass, [UpsampleOff]);
            cmd.SetVertexSource(VertexSource.None);
            cmd.SetProperties(_properties);
            cmd.Draw(3);
        }
    }
}
