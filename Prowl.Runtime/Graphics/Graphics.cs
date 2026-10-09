// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Resources;
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
    // Defaulted to conservative real-world minimums so CPU-side validation (e.g. texture size
    // checks) passes before/without a GL context. Initialize() overwrites them with real device
    // limits when a graphics device is present.
    public static int MaxTextureSize { get; internal set; } = 16384;
    public static int MaxCubeMapTextureSize { get; internal set; } = 16384;
    public static int MaxArrayTextureLayers { get; internal set; } = 2048;
    public static int MaxFramebufferColorAttachments { get; internal set; } = 8;

    public static GL GL;

    /// <summary>
    /// The kind of context to create and write shaders for: OpenGL ES on mobile, desktop OpenGL everywhere else. Can be
    /// changed before the window opens, such as with --graphics-target OpenGLES to try the ES path on a desktop.
    /// </summary>
    public static GraphicsTarget Target { get; set; } = OperatingSystem.IsAndroid() || OperatingSystem.IsIOS()
        ? GraphicsTarget.OpenGLES
        : GraphicsTarget.OpenGL;

    /// <summary>What the context the engine is running on allows. Valid once the window has opened.</summary>
    public static GraphicsCapabilities Capabilities { get; internal set; } = new(GraphicsTarget.OpenGL, 4, 1, new());

    /// <summary>
    /// True when there is no graphics device (no window / render thread) - a dedicated server or a
    /// build launched with --headless. GPU command submission becomes a no-op so gameplay code that
    /// creates or touches GPU resources (materials, terrain, render textures, etc.) runs without
    /// crashing; read-backs return their default (zeroed) contents.
    /// </summary>
    public static bool IsHeadless => GL == null;

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
    //   main: encode CBs        (main has no context; render is draining)
    //   main: EndFrameAndWait -> push frame-end sentinel, block until all but MaxFramesInFlight - 1 frames are done
    //   render: hits sentinel, SwapBuffers, counts the frame as done

    private readonly record struct RenderJob(CommandBuffer? Cmd, WaitedJob? Waited = null, bool IsFrameEnd = false, System.Action? Callback = null);

    // A job whose submitter blocks until it has run, and sees any exception it threw.
    private sealed class WaitedJob
    {
        public readonly System.Threading.ManualResetEventSlim Done = new(false);
        public System.Runtime.ExceptionServices.ExceptionDispatchInfo? Error;
    }

    // Submitters enqueue without locking and only wake the render thread when it is asleep, since a
    // lock shared with the render thread makes every submit contend with it.
    private static readonly System.Collections.Concurrent.ConcurrentQueue<RenderJob> s_renderQueue = new();
    private static readonly System.Threading.ManualResetEventSlim s_renderWake = new(false);
    private static int s_renderSleeping;
    private static volatile bool s_renderQueueClosed;
    private static System.Threading.Thread? s_renderThread;

    private static void Enqueue(RenderJob job)
    {
        s_renderQueue.Enqueue(job);
        System.Threading.Interlocked.MemoryBarrier();
        if (System.Threading.Volatile.Read(ref s_renderSleeping) != 0)
            s_renderWake.Set();
    }

    /// <summary>The next job, spinning briefly then sleeping until one arrives. False once the queue is closed and empty.</summary>
    private static bool TakeJob(out RenderJob job)
    {
        var spin = new System.Threading.SpinWait();
        while (true)
        {
            if (s_renderQueue.TryDequeue(out job)) return true;
            if (s_renderQueueClosed) return false;
            if (!spin.NextSpinWillYield) { spin.SpinOnce(); continue; }

            s_renderWake.Reset();
            System.Threading.Interlocked.Exchange(ref s_renderSleeping, 1);
            if (s_renderQueue.IsEmpty && !s_renderQueueClosed)
                s_renderWake.Wait();
            System.Threading.Volatile.Write(ref s_renderSleeping, 0);
            spin.Reset();
        }
    }

    internal static bool IsRenderThread => s_renderThread != null && System.Threading.Thread.CurrentThread == s_renderThread;

    // Frames whose end sentinel was pushed, and frames the render thread has swapped. Guarded by s_frameLock.
    private static long s_framesSubmitted;
    private static long s_framesCompleted;
    private static readonly object s_frameLock = new();

    /// <summary>
    /// How many frames may be in the pipeline at once. With 2 the main thread builds the next frame while the render
    /// thread finishes the current one, so neither waits on the other, at one frame of extra input latency. 1 waits for
    /// every frame to finish before starting the next. Running XR always uses 1, since OpenXR orders its frame calls
    /// strictly and a headset should never show an older frame than it has to.
    /// </summary>
    public static int MaxFramesInFlight { get; set; } = 2;

    private static int s_wantedSwapInterval = -1;
    private static int s_appliedSwapInterval = -1;

    /// <summary>
    /// Asks for a swap interval, 1 being vsync and 0 being off. The render thread holds the GL
    /// context for the whole run, so it is the one that applies this, at the next frame end.
    /// Set through <see cref="Application.VSync"/> rather than here, so there is one answer to
    /// what vsync currently is.
    /// </summary>
    internal static void SetSwapInterval(int interval) => System.Threading.Volatile.Write(ref s_wantedSwapInterval, interval);

    /// <summary>Enqueue a CB for the render thread to execute. The buffer is recycled once it has run
    /// and the owner has disposed it, so rent it with <c>using</c>.</summary>
    public static void Submit(CommandBuffer cmd)
    {
        if (cmd == null) return;
        if (cmd._submitted || cmd._inPool)
            throw new System.InvalidOperationException("CommandBuffer has already been submitted.");
        cmd._submitted = true;
        // No graphics device: drop GPU work instead of queueing it for a render thread that will
        // never drain it.
        if (IsHeadless)
        {
            cmd.Release();
            return;
        }
        Enqueue(new RenderJob(cmd));
    }

    /// <summary>Enqueue and block until the render thread has finished the CB.
    /// Use for read-backs, shader compile error propagation, and FBO completeness
    /// checks. Render-thread exceptions rethrow on the caller's thread.</summary>
    public static void SubmitAndWait(CommandBuffer cmd)
    {
        if (cmd == null) return;
        if (IsRenderThread)
            throw new InvalidOperationException("SubmitAndWait was called on the render thread, which would wait on itself forever.");
        if (cmd._submitted || cmd._inPool)
            throw new System.InvalidOperationException("CommandBuffer has already been submitted.");
        cmd._submitted = true;
        // No graphics device: nothing executes, so don't block waiting on a render thread. Any
        // read-back this would have filled keeps its default (zeroed) contents.
        if (IsHeadless)
        {
            cmd.Release();
            return;
        }
        var waited = new WaitedJob();
        Enqueue(new RenderJob(cmd, waited));
        waited.Done.Wait();
        waited.Done.Dispose();
        waited.Error?.Throw();
    }

    /// <summary>Runs <paramref name="callback"/> on the render thread, in order with the command buffers submitted around it.</summary>
    internal static void SubmitRenderThreadCallback(System.Action callback)
    {
        if (IsHeadless) return;
        Enqueue(new RenderJob(null, Callback: callback));
    }

    /// <summary>Runs <paramref name="callback"/> on the render thread and blocks until it has run. Its exceptions rethrow here.</summary>
    internal static void SubmitRenderThreadCallbackAndWait(System.Action callback)
    {
        if (IsHeadless) return;
        if (IsRenderThread)
        {
            callback();
            return;
        }
        var waited = new WaitedJob();
        Enqueue(new RenderJob(null, waited, Callback: callback));
        waited.Done.Wait();
        waited.Done.Dispose();
        waited.Error?.Throw();
    }

    internal static void BeginFrame() { }

    /// <summary>Time the main thread spent blocked in <see cref="EndFrameAndWait"/>
    /// last frame. High = render thread is bottleneck. Near-zero = main is.</summary>
    public static float LastFrameWaitMs { get; private set; }

    internal static void EndFrameAndWait()
    {
        int inFlight = XR.IsRunning ? 1 : System.Math.Max(1, MaxFramesInFlight);
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        lock (s_frameLock)
        {
            long frame = ++s_framesSubmitted;
            Enqueue(new RenderJob(null, IsFrameEnd: true));

            // The frame inFlight - 1 frames back has to be on screen before this one may start the next
            long mustBeDone = frame - inFlight + 1;
            while (s_framesCompleted < mustBeDone)
                System.Threading.Monitor.Wait(s_frameLock);
        }
        long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - start;
        LastFrameWaitMs = (float)(elapsed * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
    }

    private static void CompleteFrame(long frames)
    {
        lock (s_frameLock)
        {
            s_framesCompleted += frames;
            System.Threading.Monitor.PulseAll(s_frameLock);
        }
    }

    // On a hybrid machine OpenGL silently picks an adapter for us, and picking the integrated one
    // costs far more performance than anything in the renderer. Worth a line in the log.
    private static unsafe void LogAdapter()
    {
        try
        {
            string vendor = GL.GetStringS(Silk.NET.OpenGL.StringName.Vendor);
            string renderer = GL.GetStringS(Silk.NET.OpenGL.StringName.Renderer);
            string version = GL.GetStringS(Silk.NET.OpenGL.StringName.Version);
            Debug.Log($"GPU: {renderer} ({vendor}) - OpenGL {version}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Could not query GL adapter strings: {ex.Message}");
        }
    }

    public static void Initialize(bool debug)
    {
        GL = GL.GetApi(Window.InternalWindow);

        LogAdapter();
        Capabilities = DetectCapabilities();
        Debug.Log($"Graphics: {Capabilities}");

        if (debug && Capabilities.Has(GraphicsFeature.DebugOutput))
        {
            GL.DebugMessageCallback(DebugCallback, null);
            GL.Enable(EnableCap.DebugOutput);
            GL.Enable(EnableCap.DebugOutputSynchronous);
        }

        // Neither exists on ES, where cubemaps are always seamless
        if (!Capabilities.IsES)
        {
            GL.Enable(EnableCap.LineSmooth);

            // Seamless cubemap filtering removes the visible face seams when sampling a
            // cubemap with linear/trilinear filtering. Required for clean reflection-probe
            // and prefiltered-environment sampling.
            GL.Enable(EnableCap.TextureCubeMapSeamless);
        }

        MaxTextureSize = GL.GetInteger(GLEnum.MaxTextureSize);
        MaxCubeMapTextureSize = GL.GetInteger(GLEnum.MaxCubeMapTextureSize);
        MaxArrayTextureLayers = GL.GetInteger(GLEnum.MaxArrayTextureLayers);
        MaxFramebufferColorAttachments = GL.GetInteger(GLEnum.MaxColorAttachments);
    }

    private static string? s_shaderPrelude;
    private static GraphicsCapabilities? s_preludeFor;

    /// <summary>
    /// What every shader starts with: the GLSL version of the context, ES precision defaults, and a define per optional
    /// feature (PROWL_GLES, PROWL_STORAGE_BUFFERS, PROWL_VERTEX_STORAGE_BUFFERS, PROWL_FRAGMENT_STORAGE_BUFFERS,
    /// PROWL_COMPUTE) so a shader can pick its data path.
    /// </summary>
    public static string ShaderPrelude
    {
        get
        {
            if (s_shaderPrelude != null && ReferenceEquals(s_preludeFor, Capabilities)) return s_shaderPrelude;
            GraphicsCapabilities caps = Capabilities;
            var sb = new System.Text.StringBuilder();
            if (caps.IsES)
            {
                sb.Append($"#version {caps.ShaderVersion} es\n");
                sb.Append("#define PROWL_GLES 1\n");
                foreach (string type in s_esPrecisionTypes)
                    sb.Append($"precision highp {type};\n");
            }
            else
            {
                sb.Append($"#version {caps.ShaderVersion} core\n");
            }
            if (caps.Has(GraphicsFeature.StorageBuffers)) sb.Append("#define PROWL_STORAGE_BUFFERS 1\n");
            if (caps.Has(GraphicsFeature.VertexStorageBuffers)) sb.Append("#define PROWL_VERTEX_STORAGE_BUFFERS 1\n");
            if (caps.Has(GraphicsFeature.FragmentStorageBuffers)) sb.Append("#define PROWL_FRAGMENT_STORAGE_BUFFERS 1\n");
            if (caps.Has(GraphicsFeature.ComputeShaders)) sb.Append("#define PROWL_COMPUTE 1\n");
            s_preludeFor = caps;
            return s_shaderPrelude = sb.ToString();
        }
    }

    // ES gives float and several sampler types no default precision
    private static readonly string[] s_esPrecisionTypes =
    [
        "float", "int", "sampler2D", "sampler3D", "samplerCube", "sampler2DShadow", "samplerCubeShadow",
        "sampler2DArray", "sampler2DArrayShadow", "isampler2D", "usampler2D", "isampler3D", "usampler3D",
        "samplerCubeArray",
    ];

    private static GraphicsCapabilities DetectCapabilities()
    {
        int major = GL.GetInteger(GLEnum.MajorVersion);
        int minor = GL.GetInteger(GLEnum.MinorVersion);
        var extensions = new System.Collections.Generic.HashSet<string>();
        int count = GL.GetInteger(GLEnum.NumExtensions);
        for (uint i = 0; i < count; i++)
            extensions.Add(GL.GetStringS(Silk.NET.OpenGL.StringName.Extensions, i));

        // Only queried where storage buffers exist, older contexts reject the enums
        bool storage = Target == GraphicsTarget.OpenGLES ? major > 3 || (major == 3 && minor >= 1) : major > 4 || (major == 4 && minor >= 3);
        int vertexBlocks = storage ? GL.GetInteger(GLEnum.MaxVertexShaderStorageBlocks) : 0;
        int fragmentBlocks = storage ? GL.GetInteger(GLEnum.MaxFragmentShaderStorageBlocks) : 0;
        return new GraphicsCapabilities(Target, major, minor, extensions, vertexBlocks, fragmentBlocks);
    }

    public static void StartRenderThread()
    {
        // Hand the context off the main thread so the render thread can MakeCurrent.
        Window.InternalWindow.GLContext!.Clear();
        s_appliedSwapInterval = -1;
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

    private static void ApplyPendingSwapInterval()
    {
        // The headset paces frames while XR runs, so the mirror window never waits on its own display.
        int wanted = XR.IsPacingFrames ? 0 : System.Threading.Volatile.Read(ref s_wantedSwapInterval);
        if (wanted < 0 || wanted == s_appliedSwapInterval) return;

        // Recorded either way, so a driver that refuses it is reported once per change rather than
        // once per frame.
        s_appliedSwapInterval = wanted;

        try { Window.InternalWindow.GLContext!.SwapInterval(wanted); }
        catch (Exception ex) { Debug.LogError($"SwapInterval failed: {ex}"); }
    }

    private static void RenderThreadLoop()
    {
        // Take the GL context once and hold it for the entire run. The frame-end
        // sentinel does SwapBuffers; the context never bounces back to main.
        try { Window.InternalWindow.GLContext!.MakeCurrent(); }
        catch (Exception ex)
        {
            Debug.LogError($"Render thread MakeCurrent failed: {ex}");
            CompleteFrame(long.MaxValue / 2);
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
                if (!TakeJob(out RenderJob job)) break;

                if (job.IsFrameEnd)
                {
                    ApplyPendingSwapInterval();
                    try { Window.InternalWindow.GLContext!.SwapBuffers(); }
                    catch (Exception ex) { Debug.LogError($"SwapBuffers failed: {ex}"); }
                    finally { CompleteFrame(1); }
                    continue;
                }
                if (job.Callback != null)
                {
                    try { job.Callback(); }
                    catch (Exception ex)
                    {
                        if (job.Waited != null) job.Waited.Error = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex);
                        else Debug.LogError($"Render thread callback failed: {ex}");
                    }
                    finally { job.Waited?.Done.Set(); }
                    continue;
                }
                if (job.Cmd == null) { job.Waited?.Done.Set(); continue; }

                var cmd = job.Cmd;
                bool pushed = PushCBDebugGroup(cmd.Name);
                try { Executor.Execute(cmd); }
                catch (Exception ex)
                {
                    if (job.Waited != null) job.Waited.Error = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex);
                    else
                        Debug.LogError($"Render thread CB '{cmd.Name ?? "<?>"}' execute failed: {ex}");
                }
                finally
                {
                    if (pushed) PopCBDebugGroup();
                    cmd.Release();
                    job.Waited?.Done.Set();
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
        // Closing the queue lets the render thread finish any pending work (including
        // shutdown resource disposes enqueued during Closing) and then exit cleanly.
        s_renderQueueClosed = true;
        s_renderWake.Set();
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

    public static GraphicsTexture CreateTexture(TextureType type, TextureImageFormat format, bool randomWrite = false, int levels = 1)
        => new GraphicsTexture(type, format, randomWrite, levels);

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
    public static void SetTextureMaxLevel(GraphicsTexture texture, int level) => EncodeOneOp(c => c.EncodeSetTextureMaxLevel(texture, level), "Texture.SetMaxLevel");
    public static void SetTextureCompareMode(GraphicsTexture texture, bool enabled) => EncodeOneOp(c => c.EncodeSetTextureCompareMode(texture, enabled), "Texture.SetCompareMode");
    public static void GenerateMipmap(GraphicsTexture texture) => EncodeOneOp(c => c.GenerateMipmap(texture), "Texture.GenerateMipmap");

    /// <summary>Synchronous texture read-back. Blocks until the destination is filled.</summary>
    public static unsafe void GetTexImage(GraphicsTexture texture, int mip, void* data)
    {
        using var cmd = GetCommandBuffer("Texture.GetTexImage");
        cmd.EncodeGetTextureDataPtr(texture, mip, (nint)data);
        SubmitAndWait(cmd);
    }

    /// <summary>Synchronous read-back of one cubemap face's mip level. Blocks until filled.</summary>
    public static void GetTexImageCubeFace(GraphicsTexture texture, int face, int mip, byte[] destination)
    {
        using var cmd = GetCommandBuffer("Texture.GetTexImageCubeFace");
        cmd.EncodeGetTextureCubeFaceData(texture, face, mip, destination);
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

    /// <summary>Allocate (and optionally upload) one face of a cubemap at a mip level.
    /// <paramref name="face"/> is 0..5 in GL order (+X, -X, +Y, -Y, +Z, -Z).</summary>
    public static unsafe void TexImageCubeFace(GraphicsTexture texture, int face, int mip, uint size, void* data)
    {
        int byteSize = data != null ? (int)(size * size * BytesPerPixel(texture)) : 0;
        ReadOnlySpan<byte> span = data != null ? new ReadOnlySpan<byte>(data, byteSize) : ReadOnlySpan<byte>.Empty;
        using var cmd = GetCommandBuffer("Texture.TexImageCubeFace");
        cmd.EncodeAllocateTextureCubeFace(texture, face, mip, size, span);
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

    /// <summary>
    /// Captures the currently bound default framebuffer as a <see cref="Texture2D"/>. The caller
    /// receives a normal texture and can use <see cref="Texture2D.GetData{T}"/> / sampling /
    /// serialization without managing raw byte buffers. The texture is stored bottom-up (GPU Y-up,
    /// matching other Prowl textures). Callers that encode to PNG or other top-down formats should
    /// flip rows before encoding.
    /// </summary>
    public static Texture2D Screenshot()
    {
        if (IsHeadless || Window.InternalWindow is null)
        {
            throw new InvalidOperationException("No graphical framebuffer is available.");
        }

        var size = Window.InternalWindow.FramebufferSize;
        int width = size.X;
        int height = size.Y;
        if (width is < 1 or > 16_384 || height is < 1 or > 16_384)
        {
            throw new InvalidOperationException("The graphical framebuffer has an invalid size.");
        }

        int rowBytes = checked(width * 4);
        ulong totalBytes = checked((ulong)rowBytes * (ulong)height);
        if (totalBytes > 64UL * 1024UL * 1024UL)
        {
            throw new InvalidOperationException("The graphical framebuffer exceeds the capture size limit.");
        }

        if (totalBytes > int.MaxValue)
        {
            throw new InvalidOperationException("The graphical framebuffer is too large for readback.");
        }

        var texture = new Texture2D((uint)width, (uint)height, false, TextureImageFormat.Color4b);
        using (var cmd = GetCommandBuffer("Screenshot"))
        {
            cmd.EncodeScreenshot(texture.Handle, width, height);
            SubmitAndWait(cmd);
        }

        return texture;
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
