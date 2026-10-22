using Prowl.Graphite;
using Prowl.Graphite.Debugger;
using Prowl.Graphite.Debugging;
using Prowl.Graphite.ShaderDef;
using Prowl.Quill;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace DebuggerViewer.Rendering;

/// <summary>Draws a Quill canvas with a Scene pass into an offscreen target and a Present pass to the swapchain.</summary>
internal sealed class GraphiteRenderer : ICanvasRenderer, IDisposable
{
    private const int BlurBaseShift = 2;
    private const int MaxBlurLevels = 6;
    private const PixelFormat TargetFormat = PixelFormat.R8_G8_B8_A8_UNorm;
    private static readonly Color ClearColor = new(0f, 0f, 0f, 1f);
    private static readonly Keyword UpsampleOn = new("Upsample", "true");
    private static readonly Keyword UpsampleOff = new("Upsample", "false");
    private static readonly RenderResourceID SceneSlot = "Scene";

    private readonly GraphicsDevice _device;
    private readonly ScenePass _scenePass;
    private readonly PresentPass _presentPass;
    private readonly FrameGraph _graph;
    private GraphicsProgram _canvasProgram = null!;
    private ShaderPass _blurPass = null!;
    private Float4x4 _projection;
    private TextureGraphite _defaultTexture = null!;
    private Sampler _sampler = null!;
    private readonly List<TextureGraphite> _created = new();
    private int _fbWidth;
    private int _fbHeight;
    private string? _screenshot;
    private TextureGraphite? _preview;

    public GraphiteRenderer(GraphicsDevice device)
    {
        _device = device;
        _scenePass = new ScenePass(this);
        _presentPass = new PresentPass(this);
        _graph = new FrameGraph(device, "Viewer UI");
    }

    public bool SupportsBackdropBlur => true;

    public void Initialize(int width, int height)
    {
        _defaultTexture = TextureGraphite.Create(_device, 1, 1);
        _defaultTexture.SetData(_device, new IntRect(0, 0, 1, 1), [255, 255, 255, 255]);

        ShaderPass[] passes = ShaderFiles.Load(_device, "Shader.shader", "Blur.shader");
        _canvasProgram = ShaderFiles.Interleaved(_device, passes[0], "Quill/Canvas/Canvas",
            new VertexLayoutDescription(0, (uint)Vertex.SizeInBytes,
                new VertexElementDescription("POSITION0", VertexElementFormat.Float2, 0),
                new VertexElementDescription("TEXCOORD0", VertexElementFormat.Float2, 8),
                new VertexElementDescription("COLOR0", VertexElementFormat.Byte4_Norm, 16)),
            BlendStateDescription.SingleDisabled,
            DepthStencilStateDescription.DepthOnlyLessEqual,
            new(FaceCullMode.Back, FrontFace.Clockwise, true, false));
        _blurPass = passes[1];

        _sampler = _device.ResourceFactory.CreateSampler(new SamplerDescription
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

    public void UpdateProjection(int width, int height)
    {
        _fbWidth = width;
        _fbHeight = height;
        _projection = Float4x4.CreateOrthoOffCenter(0, width, height, 0, -1, 1);
    }

    /// <summary>A texture rendered before the UI that the canvas may draw, or null. The UI graph reads it through an import.</summary>
    public object? Preview(Texture? texture)
    {
        if (_preview?.Texture != texture)
            _preview = texture != null ? TextureGraphite.Wrap(texture, _sampler) : null;
        return _preview;
    }

    /// <summary>Writes the next rendered UI frame to a PNG at the path.</summary>
    public void Screenshot(string path) => _screenshot = path;

    public object CreateTexture(uint width, uint height)
    {
        TextureGraphite texture = TextureGraphite.Create(_device, width, height);
        _created.Add(texture);
        return texture;
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

        tex.SetData(_device, bounds, data);
    }

    public void RenderCalls(Canvas canvas, IReadOnlyList<DrawCall> drawCalls)
    {
        _scenePass.SetFrame(canvas, drawCalls);
        using (_graph.Group("Canvas"))
        {
            TextureHandle preview = _graph.Import((_preview ?? _defaultTexture).Texture);
            TextureHandle target = _graph.ImportTarget(_device.MainSwapchain.Framebuffer, PixelFormat.D24_UNorm_S8_UInt);
            TextureHandle scene = _graph.Create(SceneSlot, GraphTextureDesc.Sized(_fbWidth, _fbHeight, TargetFormat));
            _scenePass.Add(_graph, preview, scene);
            _presentPass.Add(_graph, scene, target);
        }

        if (_screenshot == null)
        {
            _graph.Execute();
            return;
        }

        using DeepRecording recording = new(_device, DeepMode.Full);
        _graph.Execute(recording);
        recording.Wait();
        WritePng(recording, _screenshot);
        _screenshot = null;
    }

    private static void WritePng(DeepRecording recording, string path)
    {
        RecordedCopy copy = recording.Groups.First(g => g.Name == "Canvas").Passes[0].Copies.First(c => c.Placement == CopyPlacement.AfterPass);
        CopyRegion region = copy.Regions[0];
        byte[] data = recording.Blobs.First(b => b.Ref == copy.Blob).Data.Items.ToArray();
        byte[] pixels = data.AsSpan((int)region.Offset, (int)(region.Width * region.Height * 4)).ToArray();

        using FileStream stream = File.Create(path);
        new StbImageWriteSharp.ImageWriter().WritePng(pixels, (int)region.Width, (int)region.Height, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
        Console.WriteLine($"Saved screenshot: {path} ({region.Width}x{region.Height})");
    }

    private static Float4 ToFloat4(Color32 color) => new(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);

    public void Dispose()
    {
        _scenePass.DisposeResources();
        _graph.Dispose();
        _sampler?.Dispose();
        _canvasProgram?.Dispose();
        _blurPass?.Dispose();
        _defaultTexture?.Dispose();
        foreach (TextureGraphite texture in _created)
            texture.Dispose();
        _created.Clear();
    }

    private sealed class ScenePass
    {
        private readonly GraphiteRenderer _owner;
        private readonly PropertySet _properties = new();
        private readonly Texture?[] _blurTex = new Texture?[MaxBlurLevels];
        private readonly Framebuffer?[] _blurFB = new Framebuffer?[MaxBlurLevels];
        private readonly Int2[] _blurSize = new Int2[MaxBlurLevels];
        private Canvas _canvas = null!;
        private IReadOnlyList<DrawCall> _drawCalls = [];
        private DeviceBuffer _activeVbo = null!;
        private DeviceBuffer _activeEbo = null!;
        private bool _backdropDirty = true;
        private float _lastBlurRadius = -1f;
        private int _blurBuiltW;
        private int _blurBuiltH;

        public ScenePass(GraphiteRenderer owner) => _owner = owner;

        public void SetFrame(Canvas canvas, IReadOnlyList<DrawCall> drawCalls)
        {
            _canvas = canvas;
            _drawCalls = drawCalls;
        }

        public void Add(FrameGraph graph, TextureHandle preview, TextureHandle scene)
            => graph.AddPass("Scene", pass =>
            {
                pass.Read(preview);
                return pass.Write(scene);
            }, Render);

        private void Render(PassContext context, TextureHandle sceneHandle)
        {
            CommandBuffer cmd = context.Command;
            EnsureBlurTargets(_owner._fbWidth, _owner._fbHeight);
            _backdropDirty = true;

            bool hasGeometry = _drawCalls.Count > 0 && _canvas.VertexCount > 0 && _canvas.IndexCount > 0;
            if (hasGeometry)
                UploadGeometry(cmd, context.Task);

            RenderTexture scene = context.Get(sceneHandle);
            cmd.SetFramebuffer(scene.Framebuffer, new TargetLoadStoreOps(AttachmentOps.Clear(ClearColor), AttachmentOps.Loaded));

            if (!hasGeometry)
                return;

            float dpiScale = (float)_canvas.FramebufferScale;
            int indexOffset = 0;
            foreach (DrawCall drawCall in _drawCalls)
            {
                ProcessDrawCall(cmd, scene, drawCall, indexOffset, dpiScale);
                indexOffset += drawCall.ElementCount;
            }
        }

        private void UploadGeometry(CommandBuffer cmd, ExecutionTask task)
        {
            _activeVbo = _owner._device.RentTransientBuffer(task, new BufferDescription((uint)(_canvas.VertexCount * Vertex.SizeInBytes), BufferUsage.VertexBuffer));
            _activeEbo = _owner._device.RentTransientBuffer(task, new BufferDescription((uint)(_canvas.IndexCount * sizeof(uint)), BufferUsage.IndexBuffer));
            cmd.UpdateBuffer(_activeVbo, 0, _canvas.Vertices);
            cmd.UpdateBuffer(_activeEbo, 0, _canvas.Indices);
        }

        private void ProcessDrawCall(CommandBuffer cmd, RenderTexture scene, DrawCall drawCall, int indexOffset, float dpiScale)
        {
            Brush brush = drawCall.Brush;
            float blur = brush.BackdropBlur;
            if (blur > 0f)
            {
                RenderBackdropBlur(cmd, scene, blur);
                cmd.SetFramebuffer(scene.Framebuffer);
            }

            TextureGraphite texture = (TextureGraphite)(drawCall.Texture ?? _owner._defaultTexture);
            TextureGraphite fontTex = (TextureGraphite)(drawCall.FontAtlas ?? _owner._defaultTexture);

            _properties.SetMatrix("projection", _owner._projection);
            texture.SetTexture(_properties, "texture0");
            fontTex.SetTexture(_properties, "fontTexture");

            Int2 texSize = _owner.GetTextureSize(fontTex);
            _properties.SetFloat2("atlasTexelSize", new Float2(texSize.X > 0 ? 1f / texSize.X : 0f, texSize.Y > 0 ? 1f / texSize.Y : 0f));

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
            _properties.SetInt("backdropFlipY", 0);

            if (blur > 0f)
                _properties.SetTexture("backdropTexture", _blurTex[0]!, _owner._sampler);
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
            float r = MathF.Max(radius / (1 << BlurBaseShift), 2f);
            iterations = Math.Clamp((int)MathF.Floor(MathF.Log2(r)) - 1, 1, MaxBlurLevels - 1);
            offset = Math.Clamp(r / (1 << (iterations + 1)), 0.5f, 6f);
        }

        private void RenderBackdropBlur(CommandBuffer cmd, RenderTexture scene, float radius)
        {
            if (!_backdropDirty && radius == _lastBlurRadius)
                return;

            _backdropDirty = false;
            _lastBlurRadius = radius;
            ComputeBlurParams(radius, out int iterations, out float offset);

            BlurPass(cmd, scene.ColorTextures[0], new Int2(_owner._fbWidth, _owner._fbHeight), 0, false, offset);
            for (int i = 0; i < iterations; i++)
                BlurPass(cmd, _blurTex[i]!, _blurSize[i], i + 1, false, offset);

            for (int i = iterations; i > 0; i--)
                BlurPass(cmd, _blurTex[i]!, _blurSize[i], i - 1, true, offset);
        }

        private void BlurPass(CommandBuffer cmd, Texture source, Int2 sourceSize, int dstLevel, bool upsample, float offset)
        {
            Int2 basis = upsample ? _blurSize[dstLevel] : sourceSize;
            cmd.SetFramebuffer(_blurFB[dstLevel]!);

            _properties.SetTexture("src", source, _owner._sampler);
            _properties.SetFloat2("halfpixel", new Float2(0.5f / basis.X, 0.5f / basis.Y));
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

            DisposeResources();
            for (int i = 0; i < MaxBlurLevels; i++)
            {
                int w = Math.Max(1, width >> (i + BlurBaseShift));
                int h = Math.Max(1, height >> (i + BlurBaseShift));
                _blurSize[i] = new Int2(w, h);
                _blurTex[i] = _owner._device.ResourceFactory.CreateTexture(TextureDescription.Texture2D((uint)w, (uint)h, 1, 1, TargetFormat, TextureUsage.Sampled | TextureUsage.RenderTarget));
                _blurFB[i] = _owner._device.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, _blurTex[i]!));
            }

            _blurBuiltW = width;
            _blurBuiltH = height;
        }

        public void DisposeResources()
        {
            for (int i = 0; i < MaxBlurLevels; i++)
            {
                _blurFB[i]?.Dispose();
                _blurTex[i]?.Dispose();
                _blurFB[i] = null;
                _blurTex[i] = null;
            }
        }
    }

    private sealed class PresentPass
    {
        private readonly GraphiteRenderer _owner;
        private readonly PropertySet _properties = new();

        public PresentPass(GraphiteRenderer owner) => _owner = owner;

        public void Add(FrameGraph graph, TextureHandle scene, TextureHandle target)
            => graph.AddPass("Present", pass =>
            {
                pass.Write(target);
                return pass.Read(scene);
            }, Render);

        private void Render(PassContext context, TextureHandle sceneHandle)
        {
            CommandBuffer cmd = context.Command;
            RenderTexture scene = context.Get(sceneHandle);
            _properties.SetTexture("src", scene.ColorTextures[0], _owner._sampler);
            _properties.SetFloat2("halfpixel", new Float2(0f, 0f));
            _properties.SetFloat("offset", 0f);

            cmd.SetShader(_owner._blurPass, [UpsampleOff]);
            cmd.SetVertexSource(VertexSource.None);
            cmd.SetProperties(_properties);
            cmd.Draw(3);
        }
    }
}
