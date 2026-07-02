using Prowl.Quill;
using Prowl.Vector;
using Prowl.Vector.Geometry;
using Prowl.Graphite;
using Prowl.Graphite.Compiler;
using Prowl.Graphite.Variants;

namespace GraphiteSample;

/// <summary>
/// Renders a Quill <see cref="Canvas"/> using Prowl.Graphite's CommandBuffer / PropertySet API.
/// The canvas is drawn into an offscreen color target so that backdrop-blur draws can sample the
/// content behind them; the offscreen target is then presented to the swapchain. Rendering records
/// into an owned <see cref="CommandBuffer"/>.
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


    private readonly GraphicsDevice _gl;

    public bool SupportsBackdropBlur => true;

    private CommandBuffer _buffer;
    public CommandBuffer CommandBuffer => _buffer;

    private GraphicsProgram _shader;
    private GraphicsProgram[] _blurPrograms;
    private VariantSet<GraphicsProgram> _blurProgram;

    private Float4x4 _projection;
    private TextureGraphite _defaultTexture;
    private Sampler _sampler;

    private StreamingBuffer _activeVbo; // the current frame's geometry buffers
    private uint _vboCapacity;
    private StreamingBuffer _activeEbo;
    private uint _eboCapacity;

    private PropertySet _properties = new();
    private int _propIndex;

    private readonly CanvasVertexSource _fullscreenSource = new();

    private Texture _sceneTex;
    private Framebuffer _sceneFB;

    private const int MaxBlurLevels = 6;
    private readonly Texture[] _blurTex = new Texture[MaxBlurLevels];
    private readonly Framebuffer[] _blurFB = new Framebuffer[MaxBlurLevels];
    private readonly Int2[] _blurSize = new Int2[MaxBlurLevels];

    private int _fbWidth;
    private int _fbHeight;
    private int _targetW;
    private int _targetH;

    private const PixelFormat TargetFormat = PixelFormat.R8_G8_B8_A8_UNorm;
    private static readonly Color ClearColor = new(0f, 0f, 0f, 1f);
    private static readonly Keyword UpsampleOn = new("Upsample", "true");
    private static readonly Keyword UpsampleOff = new("Upsample", "false");


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
    }


    public void Initialize(int width, int height, TextureGraphite defaultTexture)
    {
        _defaultTexture = defaultTexture;
        CreateShaderProgram();

        _buffer = _gl.ResourceFactory.CreateCommandBuffer();
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
        CompilationSession session = new();
        session.RegisterModule(_gl.BackendType switch
        {
            GraphicsBackend.OpenGL => new GLCompiler("glsl_450", GraphicsBackend.OpenGL),
            GraphicsBackend.OpenGLES => new GLCompiler("glsl_es_310", GraphicsBackend.OpenGLES),
            GraphicsBackend.Vulkan => new VulkanCompiler("spirv_1_4"),
            GraphicsBackend.Direct3D11 => new DXCompiler("sm_5_0", GraphicsBackend.Direct3D11),
            _ => throw new NotSupportedException($"Unsupported graphics backend: {_gl.BackendType}")
        });

        session.BeginSession([new DirectoryInfo("/Shaders")], s_fileLoader);

        CompilationResult result = session.CompileShader("Shader.slang", ShaderType.Rasterization);
        CompilationResult blurResult = session.CompileShader("Blur.slang", ShaderType.Rasterization);

        session.EndSession();

        BlendStateDescription oneMinusSrcAlphaBlend = new()
        {
            AttachmentStates = [
                new BlendAttachmentDescription()
                {
                    BlendEnabled = true,
                    SourceColorFactor = BlendFactor.One,
                    DestinationColorFactor = BlendFactor.InverseSourceAlpha,
                    ColorFunction = BlendFunction.Add,
                    SourceAlphaFactor = BlendFactor.One,
                    DestinationAlphaFactor = BlendFactor.InverseSourceAlpha,
                    AlphaFunction = BlendFunction.Add
                }
            ]
        };

        ShaderDescription resultDesc = result.CompiledVariants[0].Backends[0].Description;
        resultDesc.BlendState = oneMinusSrcAlphaBlend;
        resultDesc.DepthStencilState = DepthStencilStateDescription.Disabled;
        resultDesc.RasterizerState = new(FaceCullMode.None, FrontFace.Clockwise, true, false);

        resultDesc.VertexLayouts =
        [
            new VertexLayoutDescription(0, (uint)Vertex.SizeInBytes,
                new VertexElementDescription("POSITION0", VertexElementFormat.Float2, 0),
                new VertexElementDescription("TEXCOORD0", VertexElementFormat.Float2, 8),
                new VertexElementDescription("COLOR0", VertexElementFormat.Byte4_Norm, 16))
        ];

        _shader = _gl.ResourceFactory.CreateGraphicsProgram(resultDesc);
        _blurPrograms = new GraphicsProgram[blurResult.CompiledVariants.Length];

        for (int i = 0; i < blurResult.CompiledVariants.Length; i++)
        {
            ShaderDescription blurDesc = blurResult.CompiledVariants[i].Backends[0].Description;
            // Blur and present passes overwrite their target, so they use no blending.
            blurDesc.BlendState = BlendStateDescription.SingleDisabled;
            blurDesc.DepthStencilState = DepthStencilStateDescription.Disabled;
            blurDesc.RasterizerState = new(FaceCullMode.None, FrontFace.Clockwise, true, false);

            _blurPrograms[i] = _gl.ResourceFactory.CreateGraphicsProgram(blurDesc);
        }

        _blurProgram = new VariantSet<GraphicsProgram>(_blurPrograms, [.. blurResult.CompiledVariants.Select(v => v.Variants)]);
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
        _propIndex = 0;

        _buffer.Begin();

        EnsureTargets(_fbWidth, _fbHeight);

        bool hasGeometry = drawCalls.Count > 0 && canvas.Vertices.Count > 0 && canvas.Indices.Count > 0;

        // Upload geometry before binding a framebuffer: buffer uploads must happen outside a render pass.
        if (hasGeometry)
            UploadGeometry(canvas);

        _buffer.SetFramebuffer(_sceneFB);
        _buffer.ClearColorTarget(0, ClearColor);

        if (hasGeometry)
        {
            int indexOffset = 0;
            foreach (var drawCall in drawCalls)
            {
                ProcessDrawCall(drawCall, indexOffset, (float)canvas.FramebufferScale);
                indexOffset += drawCall.ElementCount;
            }
        }

        // Present the offscreen scene to the swapchain.
        Present();

        _buffer.End();
    }


    private void UploadGeometry(Canvas canvas)
    {
        Vertex[] vertices = [.. canvas.Vertices];
        uint[] indices = [.. canvas.Indices];

        EnsureBuffer(ref _activeVbo, ref _vboCapacity, (uint)(vertices.Length * Vertex.SizeInBytes), BufferUsage.VertexBuffer);
        EnsureBuffer(ref _activeEbo, ref _eboCapacity, (uint)(indices.Length * sizeof(uint)), BufferUsage.IndexBuffer);

        _buffer.UpdateBuffer(_activeVbo.Current, 0, vertices);
        _buffer.UpdateBuffer(_activeEbo.Current, 0, indices);
    }


    private void EnsureBuffer(ref StreamingBuffer buffer, ref uint capacity, uint sizeInBytes, BufferUsage usage)
    {
        if (buffer != null && sizeInBytes <= capacity)
            return;

        buffer?.Dispose();
        uint newCapacity = (uint)(sizeInBytes * 1.5f) + 256;
        buffer = _gl.ResourceFactory.CreateStreamingBuffer(new BufferDescription(newCapacity, usage));
        capacity = newCapacity;
    }


    private void ProcessDrawCall(DrawCall drawCall, int indexOffset, float dpiScale)
    {
        Brush brush = drawCall.Brush;
        float blur = brush.BackdropBlur;

        // Backdrop blur: blur the scene drawn so far into _blurTex[0], then composite the shape over it.
        if (blur > 0f)
        {
            RenderBackdropBlur(blur);
            _buffer.SetFramebuffer(_sceneFB);
        }

        TextureGraphite texture = (TextureGraphite)(drawCall.Texture ?? _defaultTexture);
        // Font atlas on its own sampler so text batches with shapes (text samples fontTexture).
        TextureGraphite fontTex = (TextureGraphite)(drawCall.FontAtlas ?? _defaultTexture);

        _properties.SetMatrix("projection", _projection);
        texture.SetTexture(_properties, "texture0");
        fontTex.SetTexture(_properties, "fontTexture");

        // 1 / font atlas size, so the text shader's distance-field screen range is correct at any zoom.
        Int2 texSize = GetTextureSize(fontTex);
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

        _properties.SetFloat2("viewportSize", new Float2(_fbWidth, _fbHeight));
        _properties.SetFloat("backdropBlurAmount", blur);

        // backdropTexture always needs a bound sampler; use the blurred scene when blurring, else any texture.
        if (blur > 0f)
            _properties.SetTexture("backdropTexture", _blurTex[0], _sampler);
        else
            texture.SetTexture(_properties, "backdropTexture");

        CanvasVertexSource source = new()
        {
            VertexBuffer = _activeVbo.Current,
            IndexBuffer = _activeEbo.Current,
            IndexCount = (uint)drawCall.ElementCount
        };

        _buffer.SetShader(_shader);
        _buffer.SetVertexSource(source);
        _buffer.SetProperties(_properties);

        _buffer.DrawIndexed(1, (uint)indexOffset, 0, 0);
    }


    private static void ComputeBlurParams(float radius, out int iterations, out float offset)
    {
        float r = MathF.Max(radius, 2f);
        iterations = Math.Clamp((int)MathF.Floor(MathF.Log2(r)) - 1, 1, MaxBlurLevels - 1);
        offset = Math.Clamp(r / (1 << (iterations + 1)), 0.5f, 6f);
    }


    private void RenderBackdropBlur(float radius)
    {
        ComputeBlurParams(radius, out int iterations, out float offset);

        // Downsample pass
        BlurPass(_sceneTex, new Int2(_targetW, _targetH), 0, false, offset);
        for (int i = 0; i < iterations; i++)
            BlurPass(_blurTex[i], _blurSize[i], i + 1, false, offset);

        // Upsample pass
        for (int i = iterations; i > 0; i--)
            BlurPass(_blurTex[i], _blurSize[i], i - 1, true, offset);
    }


    private void BlurPass(Texture source, Int2 sourceSize, int dstLevel, bool upsample, float offset)
    {
        Int2 dstSize = _blurSize[dstLevel];
        Int2 basis = upsample ? dstSize : sourceSize;

        _buffer.SetFramebuffer(_blurFB[dstLevel]);

        _blurProgram.SetKeyword(upsample ? UpsampleOn : UpsampleOff);

        _properties.SetTexture("sourceTexture", source, _sampler);
        _properties.SetFloat2("halfPixel", new Float2(0.5f / basis.X, 0.5f / basis.Y));
        _properties.SetFloat("offset", offset);

        _buffer.SetShader(_blurProgram.ActiveVariant);
        _buffer.SetVertexSource(_fullscreenSource);
        _buffer.SetProperties(_properties);
        _buffer.Draw(3);
    }


    private void Present()
    {
        _buffer.SetFramebuffer(_gl.SwapchainFramebuffer!);

        _blurProgram.SetKeyword(UpsampleOff);

        _properties.SetTexture("sourceTexture", _sceneTex, _sampler);
        _properties.SetFloat2("halfPixel", new Float2(0f, 0f));
        _properties.SetFloat("offset", 0f);

        _buffer.SetShader(_blurProgram.ActiveVariant);
        _buffer.SetVertexSource(_fullscreenSource);
        _buffer.SetProperties(_properties);
        _buffer.Draw(3);
    }


    private void EnsureTargets(int width, int height)
    {
        if (_sceneTex != null && _targetW == width && _targetH == height)
            return;

        DisposeTargets();

        TextureDescription sceneDesc = TextureDescription.Texture2D((uint)width, (uint)height, 1, 1, TargetFormat, TextureUsage.Sampled | TextureUsage.RenderTarget);
        _sceneTex = _gl.ResourceFactory.CreateTexture(sceneDesc);
        _sceneFB = _gl.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, _sceneTex));

        for (int i = 0; i < MaxBlurLevels; i++)
        {
            int w = Math.Max(1, width >> (i + 1));
            int h = Math.Max(1, height >> (i + 1));
            _blurSize[i] = new Int2(w, h);

            TextureDescription blurDesc = TextureDescription.Texture2D((uint)w, (uint)h, 1, 1, TargetFormat, TextureUsage.Sampled | TextureUsage.RenderTarget);
            _blurTex[i] = _gl.ResourceFactory.CreateTexture(blurDesc);
            _blurFB[i] = _gl.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, _blurTex[i]));
        }

        _targetW = width;
        _targetH = height;
    }


    private static Float4 ToFloat4(Color32 color)
        => new(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);


    private void DisposeTargets()
    {
        _sceneFB?.Dispose();
        _sceneTex?.Dispose();
        _sceneFB = null;
        _sceneTex = null;

        for (int i = 0; i < MaxBlurLevels; i++)
        {
            _blurFB[i]?.Dispose();
            _blurTex[i]?.Dispose();
            _blurFB[i] = null;
            _blurTex[i] = null;
        }
    }


    public void Cleanup()
    {
        DisposeTargets();

        if (_activeVbo != null)
            _activeVbo?.Dispose();

        if (_activeEbo != null)
            _activeEbo?.Dispose();

        _sampler?.Dispose();
        _shader?.Dispose();

        if (_blurPrograms != null)
            foreach (GraphicsProgram program in _blurPrograms)
                program?.Dispose();

        _buffer?.Dispose();
    }


    public void Dispose()
    {
        Cleanup();
    }
}
