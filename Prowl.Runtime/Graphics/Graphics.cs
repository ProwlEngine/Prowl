// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;

using Prowl.Graphite;
using Prowl.Graphite.Rendering;
using Prowl.Graphite.ShaderDef;
using Prowl.Graphite.ShaderDef.Compiler;
using Prowl.Runtime.Resources;

using Silk.NET.Windowing;

using GTexture = Prowl.Graphite.Texture;
using RenderTexture = Prowl.Runtime.Resources.RenderTexture;
using Material = Prowl.Runtime.Resources.Material;
using GShader = Prowl.Graphite.Rendering.Shader;

namespace Prowl.Runtime;

/// <summary>
/// Prowl's link to the GPU: one Vulkan device and one Graphite renderer for the application, and the frame every view
/// and host pipeline joins before <see cref="SubmitFrame"/>. Without a window there is no device, and every GPU object
/// stays absent while its CPU state works as usual.
/// </summary>
public static class Graphics
{
    private static GlslShaderCompiler? s_compiler;
    private static readonly List<ViewDesc> s_views = [];
    private static readonly List<GraphDispatch> s_pipelines = [];
    private static readonly Dictionary<SamplerDescription, Sampler> s_samplers = [];
    private static readonly List<IDisposable> s_deferred = [];
    private static int s_nextViewId = 1;
    private static double s_time;

    /// <summary>The Vulkan device, null when headless.</summary>
    public static GraphicsDevice? Device { get; private set; }

    /// <summary>The renderer every scene draws through, null when headless.</summary>
    public static Renderer? Renderer { get; private set; }

    public static bool IsHeadless => Renderer == null;

    /// <summary>The window's swapchain, null when headless.</summary>
    public static Swapchain? Swapchain => Device?.MainSwapchain;

    /// <summary>The largest width or height a texture may have.</summary>
    public static uint MaxTextureSize => 16384;

    /// <summary>How long the last <see cref="SubmitFrame"/> waited for the render thread, in milliseconds.</summary>
    public static double LastFrameWaitMs { get; private set; }

    /// <summary>Frames submitted so far.</summary>
    public static ulong FrameIndex { get; private set; }

    /// <summary>The name shown for the backend, such as in the editor's status bar.</summary>
    public static string BackendName => Device == null ? "Headless" : $"Vulkan ({Device.DeviceName})";

    /// <summary>
    /// Resolves a shader include that is not part of Graphite's library: (name, including file) to its source, or null.
    /// Prowl's own includes resolve first, then this.
    /// </summary>
    public static Func<string, string, string?>? ResolveInclude;

    /// <summary>How cameras become views of a frame, Prowl's forward pipeline unless replaced.</summary>
    public static CameraPipeline CameraPipeline { get; set; } = new ForwardCameraPipeline();

    /// <summary>Raised on the main thread just before each frame is submitted, for work that pushes state into worlds.</summary>
    public static event Action? BeforeSubmit;

    /// <summary>The shader compiler, which exists headless too so shaders parse and declare their variants.</summary>
    public static GlslShaderCompiler Compiler => s_compiler ??= CreateCompiler();

    /// <summary>
    /// Where compiled shader stages are kept between runs, keyed by their source, so each variant compiles once per
    /// machine. Set before anything compiles; null keeps them in memory only.
    /// </summary>
    public static string? ShaderCacheDirectory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prowl", "ShaderCache");

    // Graphite's library is registered here too, so shaders parse and list their axes headless
    private static GlslShaderCompiler CreateCompiler()
    {
        ShaderLibrary.Register(typeof(Renderer).Assembly);
        // PROWL_SHADER_DEBUG_INFO=1 keeps source lines in the shaders, so GPU profilers map their findings back to the GLSL
        bool debugInfo = Environment.GetEnvironmentVariable("PROWL_SHADER_DEBUG_INFO") == "1";
        return new GlslShaderCompiler(new GlslCompilerOptions { ResolveInclude = Resolve, CacheDirectory = ShaderCacheDirectory, DebugInfo = debugInfo });
    }

    private static readonly Dictionary<string, GShader> s_libraryShaders = new(StringComparer.Ordinal);

    /// <summary>
    /// The renderer's one instance of a shader from Graphite's library, such as "Graphite/Standard/Lit.shader", so
    /// every Prowl material of it shares a material table. Null headless.
    /// </summary>
    internal static GShader? LibraryShader(string path)
    {
        if (Renderer == null) return null;
        lock (s_libraryShaders)
        {
            if (s_libraryShaders.TryGetValue(path, out GShader? cached)) return cached;
            GShader shader = Renderer.LoadShader(ShaderParser.Parse(ShaderLibrary.Get(path) ?? throw new ArgumentException($"Graphite's library has no shader {path}.")));
            s_libraryShaders[path] = shader;
            return shader;
        }
    }

    private static string? Resolve(string name, string from)
        => BuiltInShaders.Resolve(name) ?? ResolveInclude?.Invoke(name, from);

    /// <summary>Opens the device on the window's Vulkan surface and starts the renderer.</summary>
    internal static void Initialize(IWindow window, bool vsync)
    {
        if (window.VkSurface == null)
            throw new InvalidOperationException("The window has no Vulkan surface.");

        GraphicsDeviceOptions options = new() { MaxFramesInFlight = 2 };
#if DEBUG
        options.VulkanValidationLayers = Environment.GetEnvironmentVariable("PROWL_VULKAN_VALIDATION") == "1";
#endif
        SwapchainDescription swapchain = new()
        {
            Source = SwapchainSource.CreateVulkan(window.VkSurface),
            Width = (uint)Math.Max(1, window.FramebufferSize.X),
            Height = (uint)Math.Max(1, window.FramebufferSize.Y),
            SyncToVerticalBlank = vsync,
        };
        Device = GraphicsDevice.CreateVulkan(options, swapchain, XR.VulkanOptions());
        CreateRenderer();
    }

    /// <summary>Opens a device without a window, for tools and tests that draw offscreen.</summary>
    public static void InitializeOffscreen()
    {
        if (Device != null) return;
        Device = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions { MaxFramesInFlight = 2 });
        CreateRenderer();
    }

    private static void CreateRenderer()
    {
        Renderer = new Renderer(Device!, Compiler);
        Renderer.Error += error =>
        {
            if (error.Severity == RendererErrorSeverity.Warning) Debug.LogWarning($"[Graphite] {error.Message}");
            else Debug.LogError($"[Graphite] {error.Message}");
        };
        BuiltInShaders.RegisterDefaults(Renderer);
        RegisterFallbackBuffers();
    }

    private static readonly List<DeviceBuffer> s_fallbackBuffers = [];

    // Globals shaders read through storage blocks, bound to small buffers so a world without their owner still draws
    private static void RegisterFallbackBuffers()
    {
        foreach ((string name, uint size) in (ReadOnlySpan<(string, uint)>)[("ProwlParticleRecords", 96u), ("ProwlParticleOrder", 16u)])
        {
            DeviceBuffer buffer = Device!.ResourceFactory.CreateBuffer(new BufferDescription(size, BufferUsage.StructuredBufferReadOnly));
            buffer.Name = name;
            Renderer!.Globals.SetBuffer(name, buffer);
            s_fallbackBuffers.Add(buffer);
        }
    }

    /// <summary>Whether vertical sync paces presents.</summary>
    public static bool VSync
    {
        get => Device?.SyncToVerticalBlank ?? false;
        set { if (Device != null) Device.SyncToVerticalBlank = value; }
    }

    internal static void Resize(int width, int height)
    {
        if (Device == null || width <= 0 || height <= 0) return;
        Renderer!.WaitForRenderThread();
        Device.ResizeMainWindow((uint)width, (uint)height);
    }

    /// <summary>A view id unique for the application, for state Graphite keeps per view such as history.</summary>
    public static int NewViewId() => s_nextViewId++;

    /// <summary>Adds a view to this frame. Main thread.</summary>
    public static void AddView(in ViewDesc view)
    {
        if (Renderer == null) return;
        s_views.Add(view);
    }

    /// <summary>Adds a host pipeline to this frame, run after every view. Main thread.</summary>
    public static void AddPipeline(GraphDispatch pipeline)
    {
        if (Renderer == null) return;
        s_pipelines.Add(pipeline);
    }

    private static Action? s_frameStarted;
    private static Action<bool>? s_frameSubmitted;

    /// <summary>
    /// Runs <paramref name="started"/> on the render thread as this frame starts rendering, and <paramref name="submitted"/>
    /// once its command buffers are on the graphics queue, false when it failed. For work such as OpenXR's frame calls that
    /// has to wrap the frame's submission. Main thread.
    /// </summary>
    internal static void AddFrameCallbacks(Action started, Action<bool> submitted)
    {
        if (Renderer == null) return;
        s_frameStarted += started;
        s_frameSubmitted += submitted;
    }

    private static RenderTexture? s_windowTarget;
    private static WindowPresent? s_present;

    /// <summary>
    /// What the window shows: cameras without a target and the game's UI draw here, and it is copied to the swapchain
    /// at the end of the frame. Null headless.
    /// </summary>
    public static RenderTexture? WindowTarget
    {
        get
        {
            if (Device?.MainSwapchain is not { } swapchain) return null;
            int width = (int)Math.Max(1, swapchain.Framebuffer.Width), height = (int)Math.Max(1, swapchain.Framebuffer.Height);
            if (s_windowTarget.IsNotValid())
                s_windowTarget = new RenderTexture(width, height, false, [TextureFormat.RGBA8]) { Name = "Window" };
            else if (s_windowTarget.Width != width || s_windowTarget.Height != height)
                s_windowTarget.Configure(width, height, false, [TextureFormat.RGBA8]);
            return s_windowTarget;
        }
    }

    /// <summary>The views added to this frame so far.</summary>
    public static IReadOnlyList<ViewDesc> FrameViews => s_views;

    /// <summary>Hands this frame to the renderer. Main thread, once per frame.</summary>
    internal static void SubmitFrame(float deltaTime)
    {
        if (Renderer == null)
        {
            s_views.Clear();
            s_pipelines.Clear();
            return;
        }

        BeforeSubmit?.Invoke();
        Material.SyncAll();

        if (Device!.MainSwapchain is { } swapchain && WindowTarget is { } window && window.MainTexture.GpuTexture is { } source && swapchain.Framebuffer != null)
        {
            s_present ??= new WindowPresent();
            s_pipelines.Add(s_present.Dispatch(swapchain.Framebuffer, source));
        }

        s_time += deltaTime;
        long start = Stopwatch.GetTimestamp();
        Renderer.SubmitFrame(new FrameDesc
        {
            Views = s_views.ToArray(),
            Pipelines = s_pipelines.ToArray(),
            Time = new FrameTime { Time = s_time, DeltaTime = deltaTime, FrameIndex = FrameIndex },
            Started = s_frameStarted,
            Submitted = s_frameSubmitted,
        });
        s_frameStarted = null;
        s_frameSubmitted = null;
        LastFrameWaitMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        FrameIndex++;
        s_views.Clear();
        s_pipelines.Clear();
        FlushDeferred();
    }

    /// <summary>A sampler for a description, made once and kept, for host pipelines binding textures by hand.</summary>
    public static Sampler GetSampler(SamplerDescription description)
    {
        lock (s_samplers)
        {
            if (!s_samplers.TryGetValue(description, out Sampler? sampler))
            {
                sampler = Device!.ResourceFactory.CreateSampler(description);
                s_samplers[description] = sampler;
            }
            return sampler;
        }
    }

    /// <summary>Records transfer work (updates, copies, mips) and waits for the GPU to finish it.</summary>
    public static void RunNow(Action<CommandBuffer> record, string name = "Prowl")
    {
        if (Device == null) return;
        Device.Record(record, name).Wait();
    }

    /// <summary>
    /// Reads a texture region back to the CPU, waiting for the copy. Rows are tightly packed in the texture's format, top
    /// row first. Empty when headless.
    /// </summary>
    public static byte[] ReadTexture(GTexture texture, uint mip = 0, uint layer = 0, TextureRegion? region = null)
    {
        if (Device == null) return [];
        TextureRegion r = region ?? new TextureRegion(0, 0, 0, Math.Max(texture.Width >> (int)mip, 1), Math.Max(texture.Height >> (int)mip, 1), Math.Max(texture.Depth >> (int)mip, 1), mip, layer);
        uint bytes = r.Width * r.Height * r.Depth * texture.Format.GetSizeInBytes();
        using DeviceBuffer staging = Device.ResourceFactory.CreateBuffer(new BufferDescription(bytes, BufferUsage.Staging));
        Device.Record(cmd => cmd.CopyTextureToBuffer(texture, staging, 0, r), "Readback").Wait();
        byte[] data = Device.Map<byte>(staging).ToArray();
        Device.Unmap(staging);
        return data;
    }

    /// <summary>Reads a buffer range back to the CPU, waiting for the copy. Empty when headless.</summary>
    public static byte[] ReadBuffer(DeviceBuffer buffer, uint offset = 0, uint size = 0)
    {
        if (Device == null) return [];
        if (size == 0) size = buffer.SizeInBytes - offset;
        using DeviceBuffer staging = Device.ResourceFactory.CreateBuffer(new BufferDescription(size, BufferUsage.Staging));
        Device.Record(cmd => cmd.CopyBuffer(buffer, offset, staging, 0, size), "Readback").Wait();
        byte[] data = Device.Map<byte>(staging).ToArray();
        Device.Unmap(staging);
        return data;
    }

    /// <summary>Disposes a GPU object once the frames that may use it have finished.</summary>
    public static void DeferDispose(IDisposable? resource)
    {
        if (resource == null) return;
        if (Device == null) { resource.Dispose(); return; }
        lock (s_deferred) s_deferred.Add(resource);
    }

    private static readonly List<(ulong Frame, IDisposable Resource)> s_retiring = [];

    private static void FlushDeferred()
    {
        lock (s_deferred)
        {
            foreach (IDisposable resource in s_deferred)
                s_retiring.Add((FrameIndex, resource));
            s_deferred.Clear();
        }
        // A frame Graphite has finished extracting can still be executing, so wait two more submits
        for (int i = s_retiring.Count - 1; i >= 0; i--)
        {
            if (FrameIndex - s_retiring[i].Frame < 3) continue;
            try { s_retiring[i].Resource.Dispose(); }
            catch (Exception e) { Debug.LogError($"Disposing a GPU resource threw: {e.Message}"); }
            s_retiring.RemoveAt(i);
        }
    }

    internal static void Dispose()
    {
        if (Renderer == null) return;
        Renderer.WaitForRenderThread();
        Device!.WaitForIdle();
        foreach ((_, IDisposable resource) in s_retiring) resource.Dispose();
        s_retiring.Clear();
        lock (s_deferred)
        {
            foreach (IDisposable resource in s_deferred) resource.Dispose();
            s_deferred.Clear();
        }
        foreach (Sampler sampler in s_samplers.Values) sampler.Dispose();
        s_samplers.Clear();
        s_present?.Dispose();
        s_present = null;
        if (s_windowTarget.IsValid()) s_windowTarget.Dispose();
        s_windowTarget = null;
        BuiltInShaders.Release();
        foreach (DeviceBuffer buffer in s_fallbackBuffers) buffer.Dispose();
        s_fallbackBuffers.Clear();
        lock (s_libraryShaders) s_libraryShaders.Clear();
        Renderer.Dispose();
        Renderer = null;
        Device.Dispose();
        Device = null;
    }
}
