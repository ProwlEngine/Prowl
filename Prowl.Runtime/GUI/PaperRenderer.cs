// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Graphite;
using Prowl.Graphite.Variants;
using Prowl.Quill;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Texture = Prowl.Graphite.Texture;
using IndexFormat = Prowl.Graphite.IndexFormat;
using Prowl.Echo;

namespace Prowl.Runtime.GUI;

/// <summary>
/// Renders a Quill <see cref="Canvas"/> with Prowl.Graphite. Ported from the Paper GraphiteRenderer sample,
/// but using Prowl <see cref="Texture2D"/> for canvas textures and offline-compiled shader blobs.
/// </summary>
public class PaperRenderer : ICanvasRenderer
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

    private GraphicsDevice _device;

    public bool SupportsBackdropBlur => true;

    /// <summary>
    /// When set, Present() renders into this framebuffer instead of the swapchain.
    /// WorldCanvas sets this to its own render texture so Paper renders off-screen.
    /// </summary>
    public Framebuffer? PresentTarget { get; set; }

    private CommandBuffer _buffer;

    private GraphicsProgram _shader;
    private GraphicsProgram[] _blurPrograms;
    private VariantSet<GraphicsProgram> _blurProgram;

    private Float4x4 _projection;
    private Texture2D _defaultTexture;
    private Sampler _sampler;

    private StreamingBuffer _activeVbo;
    private uint _vboCapacity;
    private StreamingBuffer _activeEbo;
    private uint _eboCapacity;

    private readonly PropertySet _properties = new();
    private CanvasVertexSource _fullscreenSource;

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
    private static readonly Color s_clearColor = new(0f, 0f, 0f, 1f);
    private static readonly Keyword s_upsampleOn = new("Upsample", "true");
    private static readonly Keyword s_upsampleOff = new("Upsample", "false");

    public void Initialize(int width, int height)
    {
        _device = Graphics.Device;

        CreateShaderPrograms();

        _buffer = _device.ResourceFactory.CreateCommandBuffer();
        _sampler = _device.ResourceFactory.CreateSampler(new SamplerDescription
        {
            AddressModeU = SamplerAddressMode.Clamp,
            AddressModeV = SamplerAddressMode.Clamp,
            AddressModeW = SamplerAddressMode.Clamp,
            Filter = SamplerFilter.MinLinear_MagLinear_MipLinear,
        });

        _defaultTexture = new Texture2D(1, 1);
        _defaultTexture.SetTextureFilters(SamplerFilter.MinLinear_MagLinear_MipLinear);
        _defaultTexture.SetWrapModes(SamplerAddressMode.Clamp, SamplerAddressMode.Clamp, SamplerAddressMode.Clamp);
        _defaultTexture.SetData(new Memory<byte>(new byte[] { 255, 255, 255, 255 }), 0, 0, 1, 1);

        UpdateProjection(width, height);
    }

    private void CreateShaderPrograms()
    {
        UIShaderBlobData ui = LoadBlob("Assets/Shaders/UI.shaderblob");
        UIShaderBlobData blur = LoadBlob("Assets/Shaders/Blur.shaderblob");

        // The shape draw composites over the blurred backdrop with premultiplied-alpha blending; the
        // shader source does not describe pipeline state, so it is applied here.
        BlendStateDescription oneMinusSrcAlphaBlend = new()
        {
            AttachmentStates =
            [
                new BlendAttachmentDescription
                {
                    BlendEnabled = true,
                    SourceColorFactor = BlendFactor.One,
                    DestinationColorFactor = BlendFactor.InverseSourceAlpha,
                    ColorFunction = BlendFunction.Add,
                    SourceAlphaFactor = BlendFactor.One,
                    DestinationAlphaFactor = BlendFactor.InverseSourceAlpha,
                    AlphaFunction = BlendFunction.Add,
                }
            ]
        };

        ShaderDescription uiDesc = PickBackend(ui.Variants[0]);
        uiDesc.BlendState = oneMinusSrcAlphaBlend;
        uiDesc.DepthStencilState = DepthStencilStateDescription.Disabled;
        uiDesc.RasterizerState = new RasterizerStateDescription(FaceCullMode.None, FrontFace.Clockwise, true, false);
        uiDesc.VertexLayouts =
        [
            new VertexLayoutDescription(0, (uint)Vertex.SizeInBytes,
                new VertexElementDescription("POSITION0", VertexElementFormat.Float2, 0),
                new VertexElementDescription("TEXCOORD0", VertexElementFormat.Float2, 8),
                new VertexElementDescription("COLOR0", VertexElementFormat.Byte4_Norm, 16))
        ];
        _shader = _device.ResourceFactory.CreateGraphicsProgram(uiDesc);

        _blurPrograms = new GraphicsProgram[blur.Variants.Length];
        var keywords = new Keyword[blur.Variants.Length][];
        for (int i = 0; i < blur.Variants.Length; i++)
        {
            ShaderDescription blurDesc = PickBackend(blur.Variants[i]);
            // Blur and present passes overwrite their target, so they use no blending.
            blurDesc.BlendState = BlendStateDescription.SingleDisabled;
            blurDesc.DepthStencilState = DepthStencilStateDescription.Disabled;
            blurDesc.RasterizerState = new RasterizerStateDescription(FaceCullMode.None, FrontFace.Clockwise, true, false);

            _blurPrograms[i] = _device.ResourceFactory.CreateGraphicsProgram(blurDesc);
            keywords[i] = blur.Variants[i].Keywords;
        }

        _blurProgram = new VariantSet<GraphicsProgram>(_blurPrograms, keywords);
    }

    private ShaderDescription PickBackend(UIShaderVariantData variant)
    {
        foreach (UIShaderBackendData entry in variant.Backends)
        {
            if (entry.Backend == _device.BackendType)
                return entry.Description;
        }

        throw new NotSupportedException(
            $"The GUI shader was not compiled for backend {_device.BackendType}. Re-run Tools/CompileUIShaders with that backend enabled.");
    }

    private static UIShaderBlobData LoadBlob(string resourcePath)
    {
        using Stream stream = EmbeddedResources.GetStream(resourcePath);

        using var reader = new BinaryReader(stream);
        EchoObject root = EchoObject.ReadFromBinary(reader);

        return Serializer.Deserialize<UIShaderBlobData>(root);
    }

    public void UpdateProjection(int width, int height)
    {
        _fbWidth = width;
        _fbHeight = height;
        _projection = Float4x4.CreateOrthoOffCenter(0, width, height, 0, -1, 1);
    }

    public object CreateTexture(uint width, uint height)
    {
        var tex = new Texture2D(width, height);
        tex.SetTextureFilters(SamplerFilter.MinLinear_MagLinear_MipLinear);
        tex.SetWrapModes(SamplerAddressMode.Clamp, SamplerAddressMode.Clamp, SamplerAddressMode.Clamp);
        return tex;
    }

    public Int2 GetTextureSize(object texture)
    {
        if (texture is not Texture2D tex) throw new ArgumentException("Invalid texture type");
        return new Int2((int)tex.Width, (int)tex.Height);
    }

    public void SetTextureData(object texture, IntRect bounds, byte[] data)
    {
        if (texture is not Texture2D tex) throw new ArgumentException("Invalid texture type");
        tex.SetData(new Memory<byte>(data), bounds.Min.X, bounds.Min.Y, (uint)bounds.Size.X, (uint)bounds.Size.Y);
    }

    public void RenderCalls(Canvas canvas, IReadOnlyList<DrawCall> drawCalls)
    {
        _buffer.Begin();

        EnsureTargets(_fbWidth, _fbHeight);

        bool hasGeometry = drawCalls.Count > 0 && canvas.Vertices.Count > 0 && canvas.Indices.Count > 0;

        // Upload geometry before binding a framebuffer: buffer uploads must happen outside a render pass.
        if (hasGeometry)
            UploadGeometry(canvas);

        _buffer.SetFramebuffer(_sceneFB);
        _buffer.ClearColorTarget(0, s_clearColor);

        if (hasGeometry)
        {
            int indexOffset = 0;
            foreach (DrawCall drawCall in drawCalls)
            {
                ProcessDrawCall(drawCall, indexOffset, (float)canvas.FramebufferScale);
                indexOffset += drawCall.ElementCount;
            }
        }

        // Present the offscreen scene to the swapchain.
        Present();

        _buffer.End();

        Graphics.CurrentFrame?.SubmitCommands(_buffer);
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
        buffer = _device.ResourceFactory.CreateStreamingBuffer(new BufferDescription(newCapacity, usage));
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

        Texture2D texture = (drawCall.Texture as Texture2D) ?? _defaultTexture;

        _properties.SetMatrix("projection", _projection);
        _properties.SetTexture("texture0", texture.Handle, texture.Sampler);

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
            _properties.SetTexture("backdropTexture", texture.Handle, texture.Sampler);

        CanvasVertexSource source = new()
        {
            VertexBuffer = _activeVbo.Current,
            IndexBuffer = _activeEbo.Current,
            IndexCount = (uint)drawCall.ElementCount,
        };

        _buffer.SetShader(_shader);
        _buffer.SetVertexSource(source);
        _buffer.SetProperties(_properties);

        _buffer.DrawIndexed(1, (uint)indexOffset, 0, 0);
    }

    /// <summary>
    /// Maps a pixel blur radius onto a number of dual-Kawase iterations plus a continuous sample offset
    /// so the effective blur scales smoothly with radius even as the iteration count steps.
    /// </summary>
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

        _blurProgram.SetKeyword(upsample ? s_upsampleOn : s_upsampleOff);

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
        _buffer.SetFramebuffer(PresentTarget ?? _device.SwapchainFramebuffer!);

        _blurProgram.SetKeyword(s_upsampleOff);

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
        _sceneTex = _device.ResourceFactory.CreateTexture(sceneDesc);
        _sceneFB = _device.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, _sceneTex));

        for (int i = 0; i < MaxBlurLevels; i++)
        {
            int w = Math.Max(1, width >> (i + 1));
            int h = Math.Max(1, height >> (i + 1));
            _blurSize[i] = new Int2(w, h);

            TextureDescription blurDesc = TextureDescription.Texture2D((uint)w, (uint)h, 1, 1, TargetFormat, TextureUsage.Sampled | TextureUsage.RenderTarget);
            _blurTex[i] = _device.ResourceFactory.CreateTexture(blurDesc);
            _blurFB[i] = _device.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, _blurTex[i]));
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

        _activeVbo?.Dispose();
        _activeEbo?.Dispose();

        _sampler?.Dispose();
        _shader?.Dispose();

        if (_blurPrograms != null)
            foreach (GraphicsProgram program in _blurPrograms)
                program?.Dispose();

        _defaultTexture?.Dispose();
        _buffer?.Dispose();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Cleanup();
    }
}
