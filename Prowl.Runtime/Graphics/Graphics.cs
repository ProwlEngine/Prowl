// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Vector;

using Silk.NET.Core.Native;
using Silk.NET.OpenGL;

namespace Prowl.Runtime;

/// <summary>
/// Facade over the GL context and the <see cref="CommandBuffer"/> system. Owns the
/// <c>GL</c> wrapper and capability constants, hosts the render thread, and exposes
/// resource constructors plus a few convenience encoders that wrap a one-op CB.
/// Every GL mutation flows through a CommandBuffer to the executor on the render
/// thread there are no direct GL calls outside this assembly.
/// </summary>
public static unsafe class Graphics
{
    public static int MaxTextureSize { get; internal set; }
    public static int MaxCubeMapTextureSize { get; internal set; }
    public static int MaxArrayTextureLayers { get; internal set; }
    public static int MaxFramebufferColorAttachments { get; internal set; }

    public static GL GL;

    public static GraphicsProgram CurrentProgram => GraphicsProgram.currentProgram;

    /// <summary>Long-lived executor so its raster-state cache survives across CBs.</summary>
    internal static readonly CommandExecutor Executor = new();

    public static CommandBuffer GetCommandBuffer(string? name = null) => CommandBufferPool.Rent(name);

    // Render thread protocol:
    //   The render thread holds the GL context for its whole life and continuously
    //   drains the queue, executing CBs in submit order as they arrive. This means
    //   resource creation and SubmitAndWait jobs enqueued at ANY time (between frames,
    //   or from background threads) are serviced promptly rather than waiting for the
    //   next BeginFrame.
    //   main: BeginFrame      -> arm frameDone for this frame
    //   main: encode CBs        (main has no context; render is draining)
    //   main: EndFrameAndWait -> push frame-end sentinel, block on frameDone
    //   render: hits sentinel, SwapBuffers, signal frameDone

    private sealed class CBJob
    {
        public CommandBuffer? Cmd;
        public System.Threading.ManualResetEventSlim? Done;
        public System.Runtime.ExceptionServices.ExceptionDispatchInfo? Error;
        public bool IsFrameEnd;
    }

    private static readonly System.Collections.Concurrent.BlockingCollection<CBJob> s_renderQueue = new();
    private static System.Threading.Thread? s_renderThread;
    private static readonly System.Threading.ManualResetEventSlim s_renderFrameDone = new(true);

    /// <summary>Enqueue a CB for the render thread to execute. Fire-and-forget.</summary>
    public static void Submit(CommandBuffer cmd)
    {
        if (cmd == null) return;
        if (cmd._inPool)
            throw new System.InvalidOperationException("CommandBuffer has already been submitted (it's in the pool).");
        cmd._submitted = true;
        cmd._ownerReleased = true;
        s_renderQueue.Add(new CBJob { Cmd = cmd });
    }

    /// <summary>Enqueue and block until the render thread has finished the CB.
    /// Use for read-backs, shader compile error propagation, and FBO completeness
    /// checks. Render-thread exceptions rethrow on the caller's thread.</summary>
    public static void SubmitAndWait(CommandBuffer cmd)
    {
        if (cmd == null) return;
        if (cmd._inPool)
            throw new System.InvalidOperationException("CommandBuffer has already been submitted (it's in the pool).");
        cmd._submitted = true;
        cmd._ownerReleased = true;
        var job = new CBJob { Cmd = cmd, Done = new System.Threading.ManualResetEventSlim(false) };
        s_renderQueue.Add(job);
        job.Done.Wait();
        job.Done.Dispose();
        job.Error?.Throw();
    }

    internal static void BeginFrame()
    {
        // Arm the frame-done gate so EndFrameAndWait blocks until THIS frame's
        // sentinel is processed. The render thread is always draining, so there's
        // nothing to wake.
        s_renderFrameDone.Reset();
    }

    /// <summary>Time the main thread spent blocked in <see cref="EndFrameAndWait"/>
    /// last frame. High = render thread is bottleneck. Near-zero = main is.</summary>
    public static float LastFrameWaitMs { get; private set; }

    internal static void EndFrameAndWait()
    {
        s_renderQueue.Add(new CBJob { IsFrameEnd = true });
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        s_renderFrameDone.Wait();
        long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - start;
        LastFrameWaitMs = (float)(elapsed * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
    }

    public static void Initialize(bool debug)
    {
        GL = GL.GetApi(Window.InternalWindow);

        if (debug)
        {
            if (OperatingSystem.IsWindows())
            {
                GL.DebugMessageCallback(DebugCallback, null);
                GL.Enable(EnableCap.DebugOutput);
                GL.Enable(EnableCap.DebugOutputSynchronous);
            }
        }

        GL.Enable(EnableCap.LineSmooth);

        MaxTextureSize = GL.GetInteger(GLEnum.MaxTextureSize);
        MaxCubeMapTextureSize = GL.GetInteger(GLEnum.MaxCubeMapTextureSize);
        MaxArrayTextureLayers = GL.GetInteger(GLEnum.MaxArrayTextureLayers);
        MaxFramebufferColorAttachments = GL.GetInteger(GLEnum.MaxColorAttachments);
    }

    public static void StartRenderThread()
    {
        // Hand the context off the main thread so the render thread can MakeCurrent.
        Window.InternalWindow.GLContext!.Clear();
        s_renderThread = new System.Threading.Thread(RenderThreadLoop)
        {
            IsBackground = true,
            Name = "Prowl GL Render Thread",
        };
        s_renderThread.Start();
    }

    // Per-CB debug groups for RenderDoc / apitrace. Compiled out in release
    // because the per-call overhead adds up across hundreds of CBs per frame.
#if DEBUG
    private static bool PushCBDebugGroup(string? label)
    {
        if (string.IsNullOrEmpty(label)) return false;
        try { GL.PushDebugGroup(Silk.NET.OpenGL.DebugSource.DebugSourceApplication, 0, (uint)label.Length, label); return true; }
        catch { return false; }
    }
    private static void PopCBDebugGroup() { try { GL.PopDebugGroup(); } catch { } }
#else
    private static bool PushCBDebugGroup(string? label) => false;
    private static void PopCBDebugGroup() { }
#endif

    private static void RenderThreadLoop()
    {
        // Take the GL context once and hold it for the entire run. The frame-end
        // sentinel does SwapBuffers; the context never bounces back to main.
        try { Window.InternalWindow.GLContext!.MakeCurrent(); }
        catch (Exception ex)
        {
            Debug.LogError($"Render thread MakeCurrent failed: {ex}");
            s_renderFrameDone.Set();
            return;
        }

        try
        {
            // Single continuous drain loop. Jobs execute in submit order as they
            // arrive, so resource-creation and SubmitAndWait jobs enqueued between
            // frames or from background threads are serviced without waiting for the
            // next BeginFrame. SwapBuffers + frame-done signalling happen only on the
            // frame-end sentinel pushed by EndFrameAndWait.
            while (true)
            {
                CBJob job;
                try { job = s_renderQueue.Take(); }
                catch (System.InvalidOperationException) { break; } // CompleteAdding + drained

                if (job.IsFrameEnd)
                {
                    try { Window.InternalWindow.GLContext!.SwapBuffers(); }
                    catch (Exception ex) { Debug.LogError($"SwapBuffers failed: {ex}"); }
                    finally { s_renderFrameDone.Set(); }
                    continue;
                }
                if (job.Cmd == null) { job.Done?.Set(); continue; }

                var cmd = job.Cmd;
                bool pushed = PushCBDebugGroup(cmd.Name);
                try { Executor.Execute(cmd); }
                catch (Exception ex)
                {
                    job.Error = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex);
                    if (job.Done == null)
                        Debug.LogError($"Render thread CB '{cmd.Name ?? "<?>"}' execute failed: {ex}");
                }
                finally
                {
                    if (pushed) PopCBDebugGroup();
                    CommandBufferPool.Return(cmd);
                    job.Done?.Set();
                }
            }
        }
        finally
        {
            try { Window.InternalWindow.GLContext!.Clear(); } catch { }
        }
    }

    private static void DebugCallback(GLEnum source, GLEnum type, int id, GLEnum severity, int length, nint message, nint userParam)
    {
        string? msg = SilkMarshal.PtrToString(message, NativeStringEncoding.UTF8);
        if (type == GLEnum.DebugTypeError || type == GLEnum.DebugTypeUndefinedBehavior)
            Debug.LogError($"OpenGL Error: {msg}");
        else if (type == GLEnum.DebugTypePerformance || type == GLEnum.DebugTypeMarker || type == GLEnum.DebugTypePortability)
            Debug.LogWarning($"OpenGL Warning: {msg}");
    }

    public static void Dispose()
    {
        // CompleteAdding makes the render thread's Take throw once the queue is
        // drained, so it finishes any pending work (including shutdown resource
        // disposes enqueued during Closing) and then exits cleanly.
        try { s_renderQueue.CompleteAdding(); } catch { }
        s_renderThread?.Join();
        try { Window.InternalWindow.GLContext?.MakeCurrent(); } catch { }
        GL.Dispose();
    }

    // ─────────────────────── Resource creation ───────────────────────

    public static GraphicsBuffer CreateBuffer<T>(BufferType bufferType, T[] data, bool dynamic = false) where T : unmanaged
    {
        // Convert the typed array to a byte span. The GraphicsBuffer constructor
        // copies the bytes into a CommandBuffer's transient store so the caller's
        // T[] can be freed/reused immediately after this returns.
        return new GraphicsBuffer(bufferType, System.Runtime.InteropServices.MemoryMarshal.AsBytes(data.AsSpan()), dynamic);
    }

    public static GraphicsVertexArray CreateVertexArray(
        VertexFormat format,
        GraphicsBuffer vertices,
        GraphicsBuffer? indices,
        VertexFormat? instanceFormat = null,
        GraphicsBuffer? instanceBuffer = null)
    {
        return new GraphicsVertexArray(format, vertices, indices, instanceFormat, instanceBuffer);
    }

    public static GraphicsFrameBuffer CreateFramebuffer(GraphicsFrameBuffer.Attachment[] attachments, uint width, uint height)
        => new GraphicsFrameBuffer(attachments, width, height);

    public static GraphicsTexture CreateTexture(TextureType type, TextureImageFormat format)
        => new GraphicsTexture(type, format);

    public static GraphicsProgram CompileProgram(string fragment, string vertex, string geometry)
        => new GraphicsProgram(fragment, vertex, geometry);

    // Resources replaced mid-frame (e.g. an instance buffer that grows) can't be
    // disposed immediately because earlier encoded CBs still reference the old
    // handle. DeferDispose queues them FlushDeferredDisposes runs once per frame
    // after all CBs have executed.
    private static readonly System.Collections.Generic.List<System.IDisposable> s_deferredDisposes = new();

    public static void DeferDispose(System.IDisposable resource)
    {
        if (resource == null) return;
        lock (s_deferredDisposes)
            s_deferredDisposes.Add(resource);
    }

    public static void FlushDeferredDisposes()
    {
        lock (s_deferredDisposes)
        {
            for (int i = 0; i < s_deferredDisposes.Count; i++)
                s_deferredDisposes[i].Dispose();
            s_deferredDisposes.Clear();
        }
    }

    // Convenience encoders for sticky texture state. Each rents a one-op CB and
    // submits it so the mutation runs on the render thread in submit order.

    public static void SetWrapS(GraphicsTexture texture, TextureWrap wrap) => EncodeOneOp(c => c.EncodeSetTextureWrap(texture, 0, wrap), "Texture.SetWrapS");
    public static void SetWrapT(GraphicsTexture texture, TextureWrap wrap) => EncodeOneOp(c => c.EncodeSetTextureWrap(texture, 1, wrap), "Texture.SetWrapT");
    public static void SetWrapR(GraphicsTexture texture, TextureWrap wrap) => EncodeOneOp(c => c.EncodeSetTextureWrap(texture, 2, wrap), "Texture.SetWrapR");
    public static void SetTextureFilters(GraphicsTexture texture, TextureMin min, TextureMag mag) => EncodeOneOp(c => c.EncodeSetTextureFilters(texture, min, mag), "Texture.SetFilters");
    public static void GenerateMipmap(GraphicsTexture texture) => EncodeOneOp(c => c.GenerateMipmap(texture), "Texture.GenerateMipmap");

    /// <summary>Synchronous texture read-back. Blocks until the destination is filled.</summary>
    public static unsafe void GetTexImage(GraphicsTexture texture, int mip, void* data)
    {
        using var cmd = GetCommandBuffer("Texture.GetTexImage");
        cmd.EncodeGetTextureDataPtr(texture, mip, (nint)data);
        SubmitAndWait(cmd);
    }

    public static unsafe void TexImage2D(GraphicsTexture texture, int mip, uint width, uint height, int border, void* data)
    {
        int size = data != null ? (int)(width * height * BytesPerPixel(texture)) : 0;
        ReadOnlySpan<byte> span = data != null ? new ReadOnlySpan<byte>(data, size) : ReadOnlySpan<byte>.Empty;
        using var cmd = GetCommandBuffer("Texture.TexImage2D");
        cmd.EncodeAllocateTexture2D(texture, mip, width, height, border, span);
        Submit(cmd);
    }

    public static unsafe void TexSubImage2D(GraphicsTexture texture, int mip, int x, int y, uint width, uint height, void* data)
    {
        if (data == null) return;
        int size = (int)(width * height * BytesPerPixel(texture));
        var span = new ReadOnlySpan<byte>(data, size);
        using var cmd = GetCommandBuffer("Texture.TexSubImage2D");
        cmd.EncodeUpdateTexture2D(texture, mip, x, y, width, height, span);
        Submit(cmd);
    }

    public static unsafe void TexImage3D(GraphicsTexture texture, int level, uint width, uint height, uint depth, void* data)
    {
        int size = data != null ? (int)(width * height * depth * BytesPerPixel(texture)) : 0;
        ReadOnlySpan<byte> span = data != null ? new ReadOnlySpan<byte>(data, size) : ReadOnlySpan<byte>.Empty;
        using var cmd = GetCommandBuffer("Texture.TexImage3D");
        cmd.EncodeAllocateTexture3D(texture, level, width, height, depth, span);
        Submit(cmd);
    }

    public static unsafe void TexSubImage3D(GraphicsTexture texture, int level, int x, int y, int z, uint width, uint height, uint depth, void* data)
    {
        if (data == null) return;
        int size = (int)(width * height * depth * BytesPerPixel(texture));
        var span = new ReadOnlySpan<byte>(data, size);
        using var cmd = GetCommandBuffer("Texture.TexSubImage3D");
        cmd.EncodeUpdateTexture3D(texture, level, x, y, z, width, height, depth, span);
        Submit(cmd);
    }

    /// <summary>Bytes per pixel under tight packing, used to size copies into the
    /// transient store. Mirrors the GL spec's pixel cost (no row alignment).</summary>
    private static int BytesPerPixel(GraphicsTexture tex) => tex.PixelInternalFormat switch
    {
        InternalFormat.R8 or InternalFormat.R8i or InternalFormat.R8ui => 1,
        InternalFormat.RG8 or InternalFormat.RG8i or InternalFormat.RG8ui => 2,
        InternalFormat.Rgb8 or InternalFormat.Rgb8i or InternalFormat.Rgb8ui => 3,
        InternalFormat.Rgba8 or InternalFormat.Rgba8i or InternalFormat.Rgba8ui => 4,
        InternalFormat.R16 or InternalFormat.R16f or InternalFormat.R16i or InternalFormat.R16ui => 2,
        InternalFormat.RG16 or InternalFormat.RG16f or InternalFormat.RG16i or InternalFormat.RG16ui => 4,
        InternalFormat.Rgb16 or InternalFormat.Rgb16f or InternalFormat.Rgb16i or InternalFormat.Rgb16ui => 6,
        InternalFormat.Rgba16 or InternalFormat.Rgba16f or InternalFormat.Rgba16i or InternalFormat.Rgba16ui => 8,
        InternalFormat.R32f or InternalFormat.R32i or InternalFormat.R32ui => 4,
        InternalFormat.RG32f or InternalFormat.RG32i or InternalFormat.RG32ui => 8,
        InternalFormat.Rgb32f or InternalFormat.Rgb32i or InternalFormat.Rgb32ui => 12,
        InternalFormat.Rgba32f or InternalFormat.Rgba32i or InternalFormat.Rgba32ui => 16,
        InternalFormat.DepthComponent16 => 2,
        InternalFormat.DepthComponent24 => 4,
        InternalFormat.DepthComponent32f => 4,
        InternalFormat.Depth24Stencil8 => 4,
        _ => 4,
    };

    private static void EncodeOneOp(Action<CommandBuffer> encode, string name)
    {
        using var cmd = GetCommandBuffer(name);
        encode(cmd);
        Submit(cmd);
    }
}
