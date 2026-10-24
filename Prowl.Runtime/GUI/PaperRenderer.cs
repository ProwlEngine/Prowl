// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Graphite;
using Prowl.Graphite.ShaderDef;
using Prowl.Quill;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using GTexture = Prowl.Graphite.Texture;
using RenderTexture = Prowl.Runtime.Resources.RenderTexture;
using IndexFormat = Prowl.Graphite.IndexFormat;

namespace Prowl.Runtime.GUI;

/// <summary>
/// Draws Paper's canvas through Graphite. Each frame's geometry and draw calls are copied when Paper hands them over,
/// and drawn by a host pass after the frame's views, into <see cref="Target"/> or the window, so the UI can show
/// what the views drew. Backdrop blur captures what is under a frosted shape and blurs it with dual Kawase.
/// </summary>
public class PaperRenderer : ICanvasRenderer
{
    // How far below the target the blur pyramid starts: 1 is half resolution, 2 a quarter
    private const int BlurBaseShift = 2;
    private const int MaxBlurLevels = 6;

    // Measured blur of the pipeline, as a standard deviation in screen pixels. Rows are 1 to 4 iterations, columns are
    // offsets 1 to 3 in steps of 0.25. Offset 3 on one row matches offset 1 on the next, so the table is one smooth range.
    private static readonly float[,] BlurSigmaTable =
    {
        { 4.17f, 5.02f, 5.69f, 6.36f, 6.93f, 7.43f, 7.86f, 8.32f, 8.72f },
        { 8.72f, 11.07f, 12.64f, 14.04f, 15.15f, 16.09f, 16.93f, 17.89f, 18.80f },
        { 17.63f, 23.15f, 26.25f, 28.84f, 30.90f, 32.71f, 34.39f, 36.37f, 38.25f },
        { 35.35f, 47.56f, 53.39f, 58.16f, 62.10f, 65.67f, 69.04f, 73.03f, 76.81f },
    };
    private const float BlurOffsetMin = 1f;
    private const float BlurOffsetStep = 0.25f;
    private const float CaptureSigma = 0.866f;
    private const float SigmaPerRadius = 0.25f;

    private static readonly Keyword BlurDown = new("MODE", "DOWN");
    private static readonly Keyword BlurUp = new("MODE", "UP");
    private static readonly Keyword BlurCopy = new("MODE", "COPY");

    private Texture2D _defaultTexture = null!;
    private int _fbWidth;
    private int _fbHeight;
    private readonly Dictionary<Framebuffer, UiPass> _passes = [];

    public bool SupportsBackdropBlur => true;

    /// <summary>Where the UI draws, null for the window.</summary>
    public RenderTexture? Target { get; set; }

    // A pass per target, since it keeps the blur chain built for that target's size
    private UiPass PassFor(Framebuffer target)
    {
        if (_passes.TryGetValue(target, out UiPass? existing))
            return existing;

        List<Framebuffer> stale = [];
        foreach (Framebuffer framebuffer in _passes.Keys)
            if (framebuffer.IsDisposed) stale.Add(framebuffer);
        foreach (Framebuffer framebuffer in stale)
        {
            Graphics.DeferDispose(_passes[framebuffer]);
            _passes.Remove(framebuffer);
        }

        UiPass created = new(new Graphite.RenderTexture(target));
        _passes[target] = created;
        return created;
    }

    public void Initialize(int width, int height)
    {
        _defaultTexture = new Texture2D(1, 1) { Name = "Paper White" };
        _defaultTexture.SetData(new Memory<byte>([255, 255, 255, 255]), 0, 0, 1, 1);
        UpdateProjection(width, height);
    }

    public void UpdateProjection(int width, int height)
    {
        _fbWidth = Math.Max(1, width);
        _fbHeight = Math.Max(1, height);
    }

    public object CreateTexture(uint width, uint height)
    {
        var texture = new Texture2D(width, height) { Name = "Paper Texture" };
        texture.SetWrap(WrapMode.Clamp);
        return texture;
    }

    public Int2 GetTextureSize(object texture)
    {
        if (texture is GTexture raw) return new Int2((int)raw.Width, (int)raw.Height);
        if (texture is not Texture2D tex) throw new ArgumentException("Invalid texture type");
        return new Int2((int)tex.Width, (int)tex.Height);
    }

    public void SetTextureData(object texture, IntRect bounds, byte[] data)
    {
        if (texture is not Texture2D tex) throw new ArgumentException("Invalid texture type");
        tex.SetData(new Memory<byte>(data), bounds.Min.X, bounds.Min.Y, (uint)bounds.Size.X, (uint)bounds.Size.Y);
    }

    private static Float4 ToFloat4(Color32 color) => new(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);

    // One draw call's values, worked out on the main thread so the render thread reads nothing of Paper's
    private struct Call
    {
        public int ElementCount;
        public GTexture? Texture, Font;
        public TextureView? TextureView;
        public Sampler? TextureSampler, FontSampler;
        public Float2 AtlasTexel;
        public Float4 ScissorTransform; public Float2 ScissorTranslation, ScissorExtent;
        public Float4 BrushTransform; public Float2 BrushTranslation;
        public int BrushType;
        public Float4 BrushColor1, BrushColor2, BrushParams;
        public Float2 BrushParams2;
        public Float4 TextureTransform; public Float2 TextureTranslation;
        public float Blur;
    }

    /// <summary>A frame of UI as copied from Paper, and the target it draws into.</summary>
    private sealed class UiView
    {
        public required Framebuffer Framebuffer;
        public required uint Width, Height;
        public required byte[] Vertices;
        public required uint[] Indices;
        public required Call[] Calls;
        public required GTexture[] Sampled;
        public required float SdfRange;
        public required bool Clear;
        public required Sampler LinearClamp;
        public required GTexture White;
    }

    public void RenderCalls(Canvas canvas, IReadOnlyList<DrawCall> drawCalls)
    {
        if (Graphics.Device == null) return;
        RenderTexture? target = Target.IsValid() ? Target : Graphics.WindowTarget;
        if (target.IsNotValid() || target.Framebuffer is not { } framebuffer) return;

        float fbScale = canvas.FramebufferScale;
        GTexture white = _defaultTexture.GpuTexture!;
        Call[] calls = new Call[drawCalls.Count];
        for (int i = 0; i < calls.Length; i++)
        {
            DrawCall drawCall = drawCalls[i];
            Texture2D? texture = drawCall.Texture as Texture2D;
            Texture2D? font = drawCall.FontAtlas as Texture2D;
            ref Call call = ref calls[i];
            call.ElementCount = drawCall.ElementCount;
            call.Texture = texture.IsValid() ? texture.GpuIfLoaded : drawCall.Texture as GTexture;
            call.TextureView = texture.IsValid() ? texture.DisplayViewIfLoaded : null;
            call.TextureSampler = texture.IsValid() ? Graphics.GetSampler(texture.SamplerUnchecked) : null;
            call.Font = font.IsValid() ? font.GpuIfLoaded : null;
            call.FontSampler = font.IsValid() ? Graphics.GetSampler(font.SamplerUnchecked) : null;
            Int2 atlasSize = font.IsValid() ? new Int2((int)font.Width, (int)font.Height) : new Int2(1, 1);
            call.AtlasTexel = new Float2(atlasSize.X > 0 ? 1f / atlasSize.X : 0f, atlasSize.Y > 0 ? 1f / atlasSize.Y : 0f);
            drawCall.GetScissor(fbScale, out call.ScissorTransform, out call.ScissorTranslation, out call.ScissorExtent);
            drawCall.GetBrushTransform(fbScale, out call.BrushTransform, out call.BrushTranslation);
            call.BrushType = (int)drawCall.Brush.Type;
            call.BrushColor1 = ToFloat4(drawCall.Brush.Color1);
            call.BrushColor2 = ToFloat4(drawCall.Brush.Color2);
            call.BrushParams = new Float4(drawCall.Brush.Point1.X, drawCall.Brush.Point1.Y, drawCall.Brush.Point2.X, drawCall.Brush.Point2.Y);
            call.BrushParams2 = new Float2(drawCall.Brush.CornerRadii, drawCall.Brush.Feather);
            drawCall.GetTextureTransform(fbScale, out call.TextureTransform, out call.TextureTranslation);
            call.Blur = (float)drawCall.Brush.BackdropBlur;
        }

        List<GTexture> sampled = [];
        foreach (Call call in calls)
        {
            if (call.Texture is { } gpu && (gpu.Usage & TextureUsage.RenderTarget) != 0 && !sampled.Contains(gpu))
                sampled.Add(gpu);
        }
        foreach (FramebufferAttachment attachment in framebuffer.ColorTargets)
            sampled.Remove(attachment.Target);

        bool drawnUnder = false;
        foreach (Graphite.Rendering.ViewDesc view in Graphics.FrameViews)
            if (view.Target == framebuffer) drawnUnder = true;

        UiView frame = new()
        {
            Framebuffer = framebuffer,
            Width = (uint)target.Width,
            Height = (uint)target.Height,
            Vertices = MemoryMarshal.AsBytes(canvas.Vertices).ToArray(),
            Indices = canvas.Indices.ToArray(),
            Calls = calls,
            Sampled = sampled.ToArray(),
            SdfRange = canvas.Text.FontEngine.DistanceRange,
            Clear = !drawnUnder,
            LinearClamp = Graphics.GetSampler(SamplerDescription.Linear with { AddressModeU = SamplerAddressMode.Clamp, AddressModeV = SamplerAddressMode.Clamp, AddressModeW = SamplerAddressMode.Clamp }),
            White = white,
        };
        UiPass pass = PassFor(framebuffer);
        Graphics.AddHostPass(graph => pass.Add(graph, frame));
    }

    /// <summary>
    /// Maps a pixel blur radius onto dual Kawase iterations and a sample offset using the measured
    /// <see cref="BlurSigmaTable"/>. <paramref name="blurMix"/> is how much of the blurred result to use over the sharp
    /// capture, below 1 only for radii weaker than the pyramid's lightest blur.
    /// </summary>
    private static void ComputeBlurParams(float radius, out int iterations, out float offset, out float blurMix)
    {
        float sigma = radius * SigmaPerRadius;
        float minSigma = BlurSigmaTable[0, 0];
        iterations = 1;
        offset = BlurOffsetMin;
        blurMix = 1f;

        if (sigma <= minSigma)
        {
            float capture = CaptureSigma * CaptureSigma;
            blurMix = Math.Clamp((sigma * sigma - capture) / (minSigma * minSigma - capture), 0f, 1f);
            return;
        }

        int rows = BlurSigmaTable.GetLength(0), cols = BlurSigmaTable.GetLength(1);
        int row = 0;
        while (row < rows - 1 && sigma > BlurSigmaTable[row, cols - 1]) row++;
        iterations = row + 1;

        int col = 1;
        while (col < cols - 1 && sigma > BlurSigmaTable[row, col]) col++;
        float lo = BlurSigmaTable[row, col - 1], hi = BlurSigmaTable[row, col];
        float t = Math.Clamp((sigma - lo) / (hi - lo), 0f, 1f);
        offset = BlurOffsetMin + (col - 1 + t) * BlurOffsetStep;
    }

    // Draws a frame of UI into its target. Render thread.
    private sealed class UiPass(Graphite.RenderTexture target) : IDisposable
    {
        private ShaderPass? _canvasPass;
        private GraphicsProgram? _canvas;
        private ShaderPass? _blur;
        private readonly PropertySet _properties = new();
        private readonly PropertySet _blurProperties = new();

        private readonly GTexture?[] _blurTex = new GTexture?[MaxBlurLevels];
        private readonly Framebuffer?[] _blurFB = new Framebuffer?[MaxBlurLevels];
        private GTexture? _capture;
        private Framebuffer? _captureFB;
        private uint _builtW, _builtH;
        private PixelFormat _builtFormat;

        // Sampled as well so the backdrop blur may read what is already in the target
        public void Add(FrameGraph graph, UiView view)
        {
            TextureHandle handle = graph.Import(target);
            TargetLoadStoreOps ops = new(view.Clear ? AttachmentOps.Clear(new Color(0f, 0f, 0f, 1f)) : AttachmentOps.Loaded, AttachmentOps.Loaded);
            TextureHandle[] reads = new TextureHandle[view.Sampled.Length];
            for (int i = 0; i < reads.Length; i++) reads[i] = graph.Import(view.Sampled[i]);
            graph.AddPass("Paper", pass =>
            {
                foreach (TextureHandle read in reads) pass.Read(read);
                pass.Write(handle, TextureState.Attachment | TextureState.Sampled, firstWrite: ops);
            }, context => Render(context, view));
        }

        private void EnsurePrograms(GraphicsDevice device)
        {
            if (_canvas != null) return;
            ShaderDefinition definition = ShaderParser.Parse(EmbeddedResources.ReadAllText("Assets/Defaults/Shaders/PaperCanvas.shader"));
            definition.Create(device, Graphics.Compiler, new Variant(), CompileMode.All);
            _canvasPass = definition.Passes![0];
            _blur = definition.Passes[1];

            if (!_canvasPass.GetVariant(0).TryGetDescription(device.BackendType, out ShaderDescription description))
                throw new InvalidOperationException("The Paper canvas shader did not compile.");
            description.VertexLayouts =
            [
                new VertexLayoutDescription(0, (uint)Vertex.SizeInBytes,
                    new VertexElementDescription("aPosition", VertexElementFormat.Float2, 0),
                    new VertexElementDescription("aTexCoord", VertexElementFormat.Float2, 8),
                    new VertexElementDescription("aColor", VertexElementFormat.Byte4_Norm, 16))
            ];
            description.BlendState = _canvasPass.State.ToBlendState(BlendStateDescription.SingleDisabled);
            description.DepthStencilState = _canvasPass.State.ToDepthStencilState(DepthStencilStateDescription.Disabled);
            description.RasterizerState = _canvasPass.State.ToRasterizerState(new(FaceCullMode.None, FrontFace.Clockwise, true, false));
            _canvas = device.ResourceFactory.CreateGraphicsProgram(description);
        }

        private void Render(PassContext context, UiView view)
        {
            CommandBuffer cmd = context.Command;
            GraphicsDevice device = Graphics.Device!;
            EnsurePrograms(device);
            bool hasGeometry = view.Calls.Length > 0 && view.Vertices.Length > 0 && view.Indices.Length > 0;

            DeviceBuffer? vertices = null, indices = null;
            if (hasGeometry)
            {
                vertices = device.RentTransientBuffer(context.Task, new BufferDescription((uint)view.Vertices.Length, BufferUsage.VertexBuffer));
                indices = device.RentTransientBuffer(context.Task, new BufferDescription((uint)(view.Indices.Length * sizeof(uint)), BufferUsage.IndexBuffer));
                cmd.UpdateBuffer<byte>(vertices, 0, view.Vertices);
                cmd.UpdateBuffer<uint>(indices, 0, view.Indices);
            }

            Framebuffer target = view.Framebuffer;
            if (!hasGeometry) return;

            // Targets hold their bottom row first, as Graphite presents views
            Float4x4 projection = Float4x4.CreateOrthoOffCenter(0, view.Width, 0, view.Height, -1, 1);
            bool backdropDirty = true;
            float lastRadius = -1f;
            int indexOffset = 0;
            foreach (Call call in view.Calls)
            {
                float blurMix = 1f;
                GTexture? backdrop = null;
                if (call.Blur > 0f)
                {
                    backdrop = Backdrop(cmd, device, view, call.Blur, ref backdropDirty, ref lastRadius, out blurMix);
                    cmd.SetFramebuffer(target, new TargetLoadStoreOps(AttachmentOps.Loaded, AttachmentOps.Loaded));
                }

                _properties.SetMatrix("projection", projection);
                // Paper composes in display space, so sRGB textures are read through their undecoded view
                if (call.TextureView != null) _properties.SetTexture("texture0", call.TextureView, call.TextureSampler ?? view.LinearClamp);
                else _properties.SetTexture("texture0", call.Texture ?? view.White, call.TextureSampler ?? view.LinearClamp);
                _properties.SetTexture("fontTexture", call.Font ?? view.White, call.FontSampler ?? view.LinearClamp);
                _properties.SetFloat2("atlasTexelSize", call.AtlasTexel);
                _properties.SetFloat("sdfPxRange", view.SdfRange);
                _properties.SetFloat4("scissorTransform", call.ScissorTransform);
                _properties.SetFloat2("scissorTranslation", call.ScissorTranslation);
                _properties.SetFloat2("scissorExt", call.ScissorExtent);
                _properties.SetFloat4("brushTransform", call.BrushTransform);
                _properties.SetFloat2("brushTranslation", call.BrushTranslation);
                _properties.SetInt("brushType", call.BrushType);
                _properties.SetFloat4("brushColor1", call.BrushColor1);
                _properties.SetFloat4("brushColor2", call.BrushColor2);
                _properties.SetFloat4("brushParams", call.BrushParams);
                _properties.SetFloat2("brushParams2", call.BrushParams2);
                _properties.SetFloat4("textureTransform", call.TextureTransform);
                _properties.SetFloat2("textureTranslation", call.TextureTranslation);
                _properties.SetFloat2("viewportSize", new Float2(view.Width, view.Height));
                _properties.SetFloat("backdropBlurAmount", call.Blur);
                _properties.SetFloat("backdropMix", blurMix);
                _properties.SetTexture("backdropTexture", backdrop ?? view.White, view.LinearClamp);
                _properties.SetTexture("backdropSharpTexture", backdrop != null ? _capture! : view.White, view.LinearClamp);

                if (call.ElementCount > 0)
                {
                    VertexSource source = new VertexSource()
                        .SetBuffer("aPosition", vertices!)
                        .SetIndexBuffer(indices!, IndexFormat.UInt32, (uint)call.ElementCount);
                    cmd.SetShader(_canvas!);
                    cmd.SetVertexSource(source);
                    cmd.SetProperties(_properties);
                    cmd.DrawIndexed(1, (uint)indexOffset, 0, 0);
                    backdropDirty = true;
                }
                indexOffset += call.ElementCount;
            }
        }

        // Copies what is in the target at half size, then dual Kawase blurs it into the pyramid's base level
        private GTexture Backdrop(CommandBuffer cmd, GraphicsDevice device, UiView view, float radius, ref bool dirty, ref float lastRadius, out float blurMix)
        {
            EnsureTargets(device, view);
            ComputeBlurParams(radius, out int iterations, out float offset, out blurMix);
            if (!dirty && radius == lastRadius)
                return _blurTex[0]!;
            dirty = false;
            lastRadius = radius;

            GTexture source = view.Framebuffer.ColorTargets[0].Target;
            Blit(cmd, source, _captureFB!, BlurCopy, 1f, view.LinearClamp);
            Blit(cmd, _capture!, _blurFB[0]!, BlurDown, 1f, view.LinearClamp);
            for (int i = 0; i < iterations; i++)
                Blit(cmd, _blurTex[i]!, _blurFB[i + 1]!, BlurDown, offset, view.LinearClamp);
            for (int i = iterations; i > 0; i--)
                Blit(cmd, _blurTex[i]!, _blurFB[i - 1]!, BlurUp, offset, view.LinearClamp);
            return _blurTex[0]!;
        }

        private void Blit(CommandBuffer cmd, GTexture source, Framebuffer target, Keyword mode, float offset, Sampler sampler)
        {
            cmd.SetFramebuffer(target, new TargetLoadStoreOps(new AttachmentOps(LoadAction.DontCare, StoreAction.Store), AttachmentOps.Discard));
            _blurProperties.SetTexture("_MainTex", source, sampler);
            _blurProperties.SetFloat("_Offset", offset);
            cmd.SetShader(_blur!, [mode]);
            cmd.SetVertexSource(VertexSource.None);
            cmd.SetProperties(_blurProperties);
            cmd.Draw(3);
        }

        private void EnsureTargets(GraphicsDevice device, UiView view)
        {
            PixelFormat format = view.Framebuffer.ColorTargets[0].Target.Format;
            if (_capture != null && _builtW == view.Width && _builtH == view.Height && _builtFormat == format) return;
            Release();
            _builtW = view.Width;
            _builtH = view.Height;
            _builtFormat = format;
            (_capture, _captureFB) = Make(device, Math.Max(1, view.Width >> 1), Math.Max(1, view.Height >> 1), format);
            for (int i = 0; i < MaxBlurLevels; i++)
                (_blurTex[i], _blurFB[i]) = Make(device, Math.Max(1, view.Width >> (i + BlurBaseShift)), Math.Max(1, view.Height >> (i + BlurBaseShift)), format);
        }

        private static (GTexture, Framebuffer) Make(GraphicsDevice device, uint width, uint height, PixelFormat format)
        {
            GTexture texture = device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(width, height, 1, 1, format, TextureUsage.Sampled | TextureUsage.RenderTarget));
            texture.Name = "Paper Blur";
            return (texture, device.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, texture)));
        }

        public void Release()
        {
            _captureFB?.Dispose();
            _capture?.Dispose();
            (_capture, _captureFB) = (null, null);
            for (int i = 0; i < MaxBlurLevels; i++)
            {
                _blurFB[i]?.Dispose();
                _blurTex[i]?.Dispose();
                _blurFB[i] = null;
                _blurTex[i] = null;
            }
        }

        public void Dispose()
        {
            Release();
            _canvas?.Dispose();
        }
    }

    public void Cleanup()
    {
        Graphics.Renderer?.WaitForRenderThread();
        foreach (UiPass pass in _passes.Values)
            pass.Dispose();
        _passes.Clear();
        if (_defaultTexture.IsValid()) _defaultTexture.Dispose();
    }

    public void Dispose() => Cleanup();
}
