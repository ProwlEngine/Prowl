// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Graphite;
using Prowl.Graphite.RenderGraph;
using Prowl.Graphite.ShaderDef;
using Prowl.Quill;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Texture = Prowl.Graphite.Texture;
using RenderTexture = Prowl.Graphite.RenderTexture;
using IndexFormat = Prowl.Graphite.IndexFormat;
using Prowl.Echo;

namespace Prowl.Runtime.GUI;

/// <summary>
/// Renders a Quill <see cref="Canvas"/> with Prowl.Graphite. Ported from the Paper GraphiteRenderer sample,
/// but using Prowl <see cref="Texture2D"/> for canvas textures and offline-compiled shader blobs.
/// <para>
/// Is itself a graph pass (<see cref="IPass{TView}"/>), generic over the host view type, so it can be
/// added via <c>AddPass</c> into any existing <see cref="Graphite.RenderPipeline{TView}"/> that wants
/// Paper's UI drawn as part of its own graph. Its scene texture and backdrop-blur ping-pong chain are
/// declared as regular graph outputs in <see cref="Setup"/> and resolved fresh from the render context
/// each execution (<see cref="Render"/>) - never manually created here. A host's own present/compositing
/// pass reads the drawn UI back by declaring <see cref="SceneResourceId"/> as a graph input and resolving
/// it the same way, then compositing it with <see cref="CompositeInto"/> wherever it wants. When there is
/// no existing pipeline to inject into (editor UI, the pre-render project launcher), use
/// <see cref="Rendering.PaperPipeline"/> instead, which wraps a <see cref="PaperRenderer{TView}"/> and a
/// present pass into a small standalone pipeline.
/// </para>
/// <para>
/// <see cref="RenderCalls"/> just stashes the canvas/draw-call list - it does not dispatch a graph itself,
/// whatever pipeline this is added to decides when <see cref="Render"/> runs.
/// </para>
/// </summary>
public class PaperRenderer<TView> : ICanvasRenderer, IPass<TView> where TView : IRenderView
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

    /// <summary>Number of backdrop-blur downsample/upsample levels the graph declares scratch textures for.</summary>
    public const int MaxBlurLevels = 6;

    private GraphicsDevice _device;

    public bool SupportsBackdropBlur => true;

    public string Name => "Paper UI";

    /// <summary>Pixel width last passed to <see cref="Initialize"/> or <see cref="UpdateProjection"/>.</summary>
    public int PixelWidth => _fbWidth;

    /// <summary>Pixel height last passed to <see cref="Initialize"/> or <see cref="UpdateProjection"/>.</summary>
    public int PixelHeight => _fbHeight;

    /// <summary>
    /// Graph resource ID this pass draws Paper's UI into. A host pipeline's present (or compositing) pass
    /// declares this as an input to read the result back.
    /// </summary>
    public RenderResourceID SceneResourceId => _sceneId;

    private readonly RenderResourceID _sceneId;
    private readonly RenderResourceID[] _blurIds = new RenderResourceID[MaxBlurLevels];
    private readonly RenderResourceID _vboId;
    private readonly RenderResourceID _eboId;

    private readonly uint _vertexBufferBytes;
    private readonly uint _indexBufferBytes;

    private TextureHandle _sceneHandle;
    private readonly TextureHandle[] _blurHandles = new TextureHandle[MaxBlurLevels];
    private BufferHandle _vboHandle;
    private BufferHandle _eboHandle;

    private Canvas _pendingCanvas;
    private IReadOnlyList<DrawCall> _pendingDrawCalls;

    private GraphicsProgram _shader;
    private ShaderPass _blurPass;
    private GraphicsProgram _blurProgramOff;
    private GraphicsProgram _blurProgramOn;
    private GraphicsProgram _compositeProgram;

    private Float4x4 _projection;
    private Texture2D _defaultTexture;
    private Sampler _sampler;

    private readonly PropertySet _properties = new();
    private CanvasVertexSource _fullscreenSource;

    private int _fbWidth;
    private int _fbHeight;

    // Transparent: the scene texture is composited (premultiplied-alpha) over whatever a host present
    // pass is drawing underneath it, so undrawn pixels must contribute nothing.
    private static readonly Color s_clearColor = new(0f, 0f, 0f, 0f);
    private static readonly Keyword s_upsampleOn = new("Upsample", "true");
    private static readonly Keyword s_upsampleOff = new("Upsample", "false");

    /// <param name="vertexBufferBytes">
    /// Fixed byte capacity of the graph-declared vertex buffer this pass draws from. Graph buffers are
    /// sized once (in <see cref="Setup"/>), not per frame, so this needs to be big enough for the most
    /// complex UI this renderer will ever draw in one frame. Defaults to 12 MiB (~600K vertices).
    /// </param>
    /// <param name="indexBufferBytes">
    /// Fixed byte capacity of the graph-declared index buffer. Defaults to 2.5 MiB (~650K indices).
    /// </param>
    /// <param name="resourceId">
    /// Graph resource ID root for this renderer's scene/blur/vertex/index resources. Only needs changing
    /// if more than one <see cref="PaperRenderer{TView}"/> is added to the same pipeline.
    /// </param>
    public PaperRenderer(uint vertexBufferBytes = 12u * 1024 * 1024, uint indexBufferBytes = 2_621_440u, string resourceId = "_PaperScene")
    {
        _vertexBufferBytes = vertexBufferBytes;
        _indexBufferBytes = indexBufferBytes;

        _sceneId = resourceId;
        _vboId = $"{resourceId}Vbo";
        _eboId = $"{resourceId}Ebo";
        for (int i = 0; i < _blurIds.Length; i++)
            _blurIds[i] = $"{resourceId}Blur{i}";
    }

    public void Initialize(int width, int height)
    {
        _device = Graphics.Device;

        CreateShaderPrograms();

        _sampler = _device.ResourceFactory.CreateSampler(new SamplerDescription
        {
            AddressModeU = SamplerAddressMode.Clamp,
            AddressModeV = SamplerAddressMode.Clamp,
            AddressModeW = SamplerAddressMode.Clamp,
            Filter = SamplerFilter.MinLinear_MagLinear_MipLinear,
        });
        _sampler.Name = "PaperRenderer Sampler";

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

        ui.Definition.Create(_device, ui.Snapshot);
        blur.Definition.Create(_device, blur.Snapshot);

        ShaderPass uiPass = ui.Definition.Passes![0];
        ShaderDescription uiDesc = ResolveDescription(uiPass);
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
        _shader.Name = "PaperRenderer UI Shader";

        _blurPass = blur.Definition.Passes![0];
        _blurProgramOff = BuildBlurProgram(s_upsampleOff);
        _blurProgramOn = BuildBlurProgram(s_upsampleOn);
        _compositeProgram = BuildCompositeProgram(oneMinusSrcAlphaBlend);
    }

    /// <summary>
    /// Same shader as the non-upsampling blur pass (plain texture sample, offset=0 in
    /// <see cref="CompositeInto"/> means no blur), but with premultiplied-alpha blending enabled instead
    /// of disabled, since <see cref="CompositeInto"/> draws the scene texture over a host's existing
    /// framebuffer content rather than overwriting it.
    /// </summary>
    private GraphicsProgram BuildCompositeProgram(BlendStateDescription blend)
    {
        _blurPass.SetKeyword(s_upsampleOff);
        ShaderDescription desc = ResolveDescription(_blurPass);

        desc.BlendState = blend;
        desc.DepthStencilState = DepthStencilStateDescription.Disabled;
        desc.RasterizerState = new RasterizerStateDescription(FaceCullMode.None, FrontFace.Clockwise, true, false);

        GraphicsProgram program = _device.ResourceFactory.CreateGraphicsProgram(desc);
        program.Name = "PaperRenderer Composite Shader";
        return program;
    }

    private GraphicsProgram BuildBlurProgram(Keyword upsample)
    {
        _blurPass.SetKeyword(upsample);
        ShaderDescription desc = ResolveDescription(_blurPass);

        // Blur and present passes overwrite their target, so they use no blending.
        desc.BlendState = BlendStateDescription.SingleDisabled;
        desc.DepthStencilState = DepthStencilStateDescription.Disabled;
        desc.RasterizerState = new RasterizerStateDescription(FaceCullMode.None, FrontFace.Clockwise, true, false);

        GraphicsProgram program = _device.ResourceFactory.CreateGraphicsProgram(desc);
        program.Name = $"PaperRenderer Blur Shader (Upsample={upsample.Value})";
        return program;
    }

    private ShaderDescription ResolveDescription(ShaderPass pass)
    {
        if (!pass.ActiveVariant.TryGetDescription(_device.BackendType, out ShaderDescription description))
        {
            throw new NotSupportedException(
                $"A GUI shader was not compiled for backend {_device.BackendType}. Re-run Tools/CompileUIShaders with that backend enabled.");
        }

        return description;
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

    /// <summary>
    /// Stashes the canvas and draw calls for the next <see cref="Render"/> call. Does not dispatch a graph
    /// itself - whatever pipeline this pass is added to decides when that runs.
    /// </summary>
    public void RenderCalls(Canvas canvas, IReadOnlyList<DrawCall> drawCalls)
    {
        _pendingCanvas = canvas;
        _pendingDrawCalls = drawCalls;
    }

    public void Setup(RenderContextBuilder builder)
    {
        _sceneHandle = builder.GetOutputTexture(_sceneId, GraphTextureDesc.ViewSized(depth: false));

        for (int i = 0; i < _blurHandles.Length; i++)
            _blurHandles[i] = builder.GetOutputTexture(_blurIds[i], GraphTextureDesc.ViewSized(depth: false, scale: 1f / (1 << (i + 1))));

        _vboHandle = builder.GetOutputBuffer(_vboId, GraphBufferDesc.Of(_vertexBufferBytes, BufferUsage.VertexBuffer));
        _eboHandle = builder.GetOutputBuffer(_eboId, GraphBufferDesc.Of(_indexBufferBytes, BufferUsage.IndexBuffer));
    }

    /// <summary>
    /// Draws the canvas stashed by <see cref="RenderCalls"/> into the graph-pooled scene texture, using a
    /// graph-pooled vertex/index buffer pair and backdrop-blur ping-pong chain (all declared in
    /// <see cref="Setup"/>, resolved here, never manually created). Does not present or touch any
    /// framebuffer beyond its own scene texture - use <see cref="CompositeInto"/> for that.
    /// </summary>
    public void Render(RenderContext<TView> context)
    {
        Canvas canvas = _pendingCanvas;
        IReadOnlyList<DrawCall> drawCalls = _pendingDrawCalls;

        RenderTexture sceneRT = context.GetRenderTexture(_sceneHandle);
        RenderTexture ResolveBlur(int level) => context.GetRenderTexture(_blurHandles[level]);
        DeviceBuffer vbo = context.GetRenderBuffer(_vboHandle);
        DeviceBuffer ebo = context.GetRenderBuffer(_eboHandle);

        CommandBuffer cmd = context.GetCommandBuffer("Paper");

        bool hasGeometry = drawCalls.Count > 0 && canvas.Vertices.Count > 0 && canvas.Indices.Count > 0;

        int includedDrawCalls = drawCalls.Count;
        int vertexCount = canvas.Vertices.Count;
        int indexCount = canvas.Indices.Count;

        if (hasGeometry)
        {
            uint neededVertexBytes = (uint)(vertexCount * Vertex.SizeInBytes);
            uint neededIndexBytes = (uint)(indexCount * sizeof(uint));

            if (neededVertexBytes > _vertexBufferBytes || neededIndexBytes > _indexBufferBytes)
            {
                includedDrawCalls = ClampToCapacity(canvas, drawCalls, out vertexCount, out indexCount);

                Debug.LogWarning(
                    $"PaperRenderer: canvas geometry ({canvas.Vertices.Count} vertices = {neededVertexBytes}B, " +
                    $"{canvas.Indices.Count} indices = {neededIndexBytes}B) exceeds the configured buffer capacity " +
                    $"({_vertexBufferBytes}B vertex / {_indexBufferBytes}B index) - drawing only the first " +
                    $"{includedDrawCalls}/{drawCalls.Count} draw calls this frame ({vertexCount} vertices, " +
                    $"{indexCount} indices). Construct this PaperRenderer with larger vertexBufferBytes/indexBufferBytes.");

                hasGeometry = includedDrawCalls > 0 && vertexCount > 0 && indexCount > 0;
            }
        }

        // Upload geometry before binding a framebuffer: buffer uploads must happen outside a render pass.
        if (hasGeometry)
            UploadGeometry(cmd, canvas, vertexCount, indexCount, vbo, ebo);

        cmd.SetFramebuffer(sceneRT.Framebuffer);
        cmd.ClearColorTarget(0, s_clearColor);

        if (hasGeometry)
        {
            int indexOffset = 0;
            for (int i = 0; i < includedDrawCalls; i++)
            {
                DrawCall drawCall = drawCalls[i];
                ProcessDrawCall(cmd, drawCall, indexOffset, (float)canvas.FramebufferScale, sceneRT, ResolveBlur, vbo, ebo);
                indexOffset += drawCall.ElementCount;
            }
        }

        context.SubmitCommandBuffer(cmd);
    }

    /// <summary>
    /// Finds the longest prefix of <paramref name="drawCalls"/> whose vertex/index usage fits within this
    /// renderer's configured buffer capacity, so an over-budget canvas still gets a partial draw instead
    /// of none. A draw call is only included whole - never split - so its indices always resolve against
    /// vertices already included in <paramref name="vertexCount"/>.
    /// </summary>
    private int ClampToCapacity(Canvas canvas, IReadOnlyList<DrawCall> drawCalls, out int vertexCount, out int indexCount)
    {
        int maxVertices = (int)(_vertexBufferBytes / Vertex.SizeInBytes);
        int maxIndices = (int)(_indexBufferBytes / sizeof(uint));

        int includedDrawCalls = 0;
        int includedIndices = 0;
        int maxVertexIndex = -1;

        foreach (DrawCall drawCall in drawCalls)
        {
            int nextIndices = includedIndices + drawCall.ElementCount;
            if (nextIndices > maxIndices)
                break;

            int nextMaxVertexIndex = maxVertexIndex;
            for (int i = includedIndices; i < nextIndices; i++)
                nextMaxVertexIndex = Math.Max(nextMaxVertexIndex, (int)canvas.Indices[i]);

            if (nextMaxVertexIndex + 1 > maxVertices)
                break;

            includedIndices = nextIndices;
            maxVertexIndex = nextMaxVertexIndex;
            includedDrawCalls++;
        }

        vertexCount = maxVertexIndex + 1;
        indexCount = includedIndices;
        return includedDrawCalls;
    }

    /// <summary>
    /// Composites an already-resolved scene texture (see <see cref="Render"/> / <see cref="SceneResourceId"/>)
    /// over <paramref name="target"/> (the swapchain framebuffer, or any other render target a host
    /// pipeline's present pass wants to composite Paper's UI onto), using premultiplied-alpha blending so
    /// whatever <paramref name="target"/> already holds shows through where the UI didn't draw.
    /// </summary>
    public void CompositeInto(CommandBuffer cmd, Texture sceneTexture, Framebuffer target)
    {
        cmd.SetFramebuffer(target);

        _properties.SetTexture("sourceTexture", sceneTexture, _sampler);
        _properties.SetFloat2("halfPixel", new Float2(0f, 0f));
        _properties.SetFloat("offset", 0f);

        cmd.SetShader(_compositeProgram);
        cmd.SetVertexSource(_fullscreenSource);
        cmd.SetProperties(_properties);
        cmd.Draw(3);
    }

    private static void UploadGeometry(CommandBuffer cmd, Canvas canvas, int vertexCount, int indexCount, DeviceBuffer vbo, DeviceBuffer ebo)
    {
        Vertex[] vertices = new Vertex[vertexCount];
        for (int i = 0; i < vertexCount; i++)
            vertices[i] = canvas.Vertices[i];

        uint[] indices = new uint[indexCount];
        for (int i = 0; i < indexCount; i++)
            indices[i] = canvas.Indices[i];

        cmd.UpdateBuffer(vbo, 0, vertices);
        cmd.UpdateBuffer(ebo, 0, indices);
    }

    private void ProcessDrawCall(
        CommandBuffer cmd, DrawCall drawCall, int indexOffset, float dpiScale,
        RenderTexture sceneRT, Func<int, RenderTexture> resolveBlur, DeviceBuffer vbo, DeviceBuffer ebo)
    {
        Brush brush = drawCall.Brush;
        float blur = brush.BackdropBlur;

        // Backdrop blur: blur the scene drawn so far into blur level 0, then composite the shape over it.
        if (blur > 0f)
        {
            RenderBackdropBlur(cmd, blur, sceneRT, resolveBlur);
            cmd.SetFramebuffer(sceneRT.Framebuffer);
        }

        Texture2D texture = (drawCall.Texture as Texture2D) ?? _defaultTexture;
        // Font atlas on its own sampler so text batches with shapes (text samples fontTexture).
        Texture2D fontTex = (drawCall.FontAtlas as Texture2D) ?? _defaultTexture;

        if (drawCall.Texture != null && texture == _defaultTexture)
            Debug.LogWarning($"PaperRenderer: drawCall.Texture was a {drawCall.Texture.GetType()}, not a Texture2D - falling back to the 1x1 default texture.");

        _properties.SetMatrix("projection", _projection);
        _properties.SetTexture("texture0", texture.Handle, texture.Sampler);
        _properties.SetTexture("fontTexture", fontTex.Handle, fontTex.Sampler);

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
            _properties.SetTexture("backdropTexture", resolveBlur(0).ColorTextures[0], _sampler);
        else
            _properties.SetTexture("backdropTexture", texture.Handle, texture.Sampler);

        CanvasVertexSource source = new()
        {
            VertexBuffer = vbo,
            IndexBuffer = ebo,
            IndexCount = (uint)drawCall.ElementCount,
        };

        cmd.SetShader(_shader);
        cmd.SetVertexSource(source);
        cmd.SetProperties(_properties);

        cmd.DrawIndexed(1, (uint)indexOffset, 0, 0);
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

    private void RenderBackdropBlur(CommandBuffer cmd, float radius, RenderTexture sceneRT, Func<int, RenderTexture> resolveBlur)
    {
        ComputeBlurParams(radius, out int iterations, out float offset);

        // Downsample pass
        BlurPass(cmd, sceneRT.ColorTextures[0], TexelSize(sceneRT), resolveBlur, 0, false, offset);
        for (int i = 0; i < iterations; i++)
            BlurPass(cmd, resolveBlur(i).ColorTextures[0], TexelSize(resolveBlur(i)), resolveBlur, i + 1, false, offset);

        // Upsample pass
        for (int i = iterations; i > 0; i--)
            BlurPass(cmd, resolveBlur(i).ColorTextures[0], TexelSize(resolveBlur(i)), resolveBlur, i - 1, true, offset);
    }

    private static Int2 TexelSize(RenderTexture rt) => new((int)rt.Desc.Width, (int)rt.Desc.Height);

    private void BlurPass(CommandBuffer cmd, Texture source, Int2 sourceSize, Func<int, RenderTexture> resolveBlur, int dstLevel, bool upsample, float offset)
    {
        RenderTexture dst = resolveBlur(dstLevel);
        Int2 dstSize = TexelSize(dst);
        Int2 basis = upsample ? dstSize : sourceSize;

        cmd.SetFramebuffer(dst.Framebuffer);

        _properties.SetTexture("sourceTexture", source, _sampler);
        _properties.SetFloat2("halfPixel", new Float2(0.5f / basis.X, 0.5f / basis.Y));
        _properties.SetFloat("offset", offset);

        cmd.SetShader(upsample ? _blurProgramOn : _blurProgramOff);
        cmd.SetVertexSource(_fullscreenSource);
        cmd.SetProperties(_properties);
        cmd.Draw(3);
    }

    private static Float4 ToFloat4(Color32 color)
        => new(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);

    public void Cleanup()
    {
        _sampler?.Dispose();
        _shader?.Dispose();

        _blurProgramOff?.Dispose();
        _blurProgramOn?.Dispose();
        _compositeProgram?.Dispose();

        _defaultTexture?.Dispose();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Cleanup();
    }
}
