using System;
using System.Collections.Generic;

using Prowl.Graphite.RenderGraph;

using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace Prowl.Graphite.Tests;

// A RenderContext is normally only handed to a pass while the render graph executes it. Low-level
// GPU tests have no passes, so this harness stands up a throwaway single-view graph with no passes
// and hands the caller its RenderContext directly, giving tests the same recording/submission surface
// (GetCommandBuffer, SubmitCommandBuffer, AllocateTransient) real passes use.
public readonly struct TestRenderView : IRenderView
{
    public uint PixelWidth => 256;
    public uint PixelHeight => 256;
}

internal sealed class NoOpTestPresentPass : IPresentPass<TestRenderView>
{
    public string Name => "TestNoOpPresent";

    public void Setup(PresentContextBuilder builder) { }

    public void Present(RenderContext<TestRenderView> context) { }
}

public static class TestGraphExtensions
{
    public static ExecutionTask RunTestGraph(this GraphicsDevice gd, Action<RenderContext<TestRenderView>> record)
    {
        ExecutionTask task = gd.BeginExecution();
        RenderGraph<TestRenderView> graph = RenderGraph<TestRenderView>.Build(
            Array.Empty<IPass<TestRenderView>>(), new NoOpTestPresentPass());
        var context = new RenderContext<TestRenderView>(gd, task, graph, default);

        try
        {
            record(context);
        }
        finally
        {
            gd.CompleteExecution(task);
        }

        return task;
    }
}

// Device/window creation for the test suite. The device-creation switch is duplicated from
// Samples/Shared/DeviceCreateUtilities so the tests exercise the same path the samples do.
// Most GPU tests run on a headless device (no window/swapchain); only the *WithMainSwapchain
// creators build a window.
public static class TestUtils
{
    // Each device gets its own profiler instance - state must not leak across devices/tests.
    private static GraphicsDeviceOptions HeadlessOptions() => new(true) { Profiler = new TestCountingProfiler() };
    private static GraphicsDeviceOptions SwapchainOptions() => new(true, PixelFormat.R16_UNorm, false) { Profiler = new TestCountingProfiler() };

    public static GraphicsDevice CreateVulkanDevice()
        => GraphicsDevice.CreateVulkan(HeadlessOptions());

    public static void CreateVulkanDeviceWithSwapchain(out IWindow window, out GraphicsDevice gd)
    {
        window = CreateWindow(GraphicsBackend.Vulkan);
        gd = CreateDevice(window, SwapchainOptions(), GraphicsBackend.Vulkan);
    }

    // Creates a hidden, initialized window for the given backend. Initialize() performs the
    // one-time setup the device needs (GL context, Vulkan surface, native handles) without
    // entering the blocking run loop the samples use.
    public static IWindow CreateWindow(GraphicsBackend backend)
    {
        WindowOptions options = WindowOptions.Default;
        options.Title = "Prowl.Graphite.Tests";
        options.Size = new Vector2D<int>(200, 200);
        options.IsVisible = false;
        options.WindowState = WindowState.Normal;
        options.ShouldSwapAutomatically = false;
        options.API = GetApi(backend);

        IWindow window = Window.Create(options);
        window.Initialize();
        return window;
    }

    private static GraphicsAPI GetApi(GraphicsBackend backend) => backend switch
    {
        GraphicsBackend.Vulkan =>
            new GraphicsAPI(ContextAPI.Vulkan, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(2, 1)),
        _ => throw new ArgumentOutOfRangeException(nameof(backend))
    };

    // Duplicated from Samples/Shared/DeviceCreateUtilities.CreateDevice.
    public static GraphicsDevice CreateDevice(IWindow window, GraphicsDeviceOptions options, GraphicsBackend backend)
    {
        if (!window.IsInitialized)
            throw new InvalidOperationException("Cannot create graphics device with an uninitialized window!");

        switch (backend)
        {
            case GraphicsBackend.Vulkan:
                if (window.API.API != ContextAPI.Vulkan)
                    throw new InvalidOperationException("Attempted to make a Vulkan graphics device without an available Vulkan API");

                VulkanDeviceOptions vkOptions = default;
                SwapchainDescription vkDescription = new()
                {
                    DepthFormat = options.SwapchainDepthFormat,
                    ColorSrgb = options.SwapchainSrgbFormat,
                    Width = (uint)window.Size.X,
                    Height = (uint)window.Size.Y,
                    SyncToVerticalBlank = options.SyncToVerticalBlank,
                    Source = SwapchainSource.CreateVulkan(window.VkSurface!)
                };

                return GraphicsDevice.CreateVulkan(options, vkDescription, vkOptions);
        }

        throw new InvalidOperationException($"Unsupported graphics backend: {backend}");
    }
}

internal sealed class TrackingResourceFactory : ResourceFactory
{
    private readonly ResourceFactory _inner;
    private readonly List<IDisposable> _created = [];

    public TrackingResourceFactory(ResourceFactory inner) : base(inner.Device, inner.Features)
    {
        _inner = inner;
    }

    public override GraphicsBackend BackendType => _inner.BackendType;

    public void DisposeAll()
    {
        for (int i = _created.Count - 1; i >= 0; i--)
        {
            _created[i].Dispose();
        }
        _created.Clear();
    }

    private T Track<T>(T resource) where T : IDisposable
    {
        _created.Add(resource);
        return resource;
    }

    public override CommandBuffer CreateCommandBuffer(ref CommandBufferDescription description)
        => Track(_inner.CreateCommandBuffer(ref description));

    public override Framebuffer CreateFramebuffer(ref FramebufferDescription description)
        => Track(_inner.CreateFramebuffer(ref description));

    protected override DeviceBuffer CreateBufferCore(ref BufferDescription description)
        => Track(_inner.CreateBuffer(ref description));

    protected override GraphicsProgram CreateGraphicsProgramCore(ref ShaderDescription description)
        => Track(_inner.CreateGraphicsProgram(ref description));

    protected override ComputeProgram CreateComputeProgramCore(ref ComputeDescription description)
        => Track(_inner.CreateComputeProgram(ref description));

    protected override Sampler CreateSamplerCore(ref SamplerDescription description)
        => Track(_inner.CreateSampler(ref description));

    protected override Texture CreateTextureCore(ref TextureDescription description)
        => Track(_inner.CreateTexture(ref description));

    protected override Texture CreateTextureCore(ulong nativeTexture, ref TextureDescription description)
        => Track(_inner.CreateTexture(nativeTexture, ref description));

    protected override TextureView CreateTextureViewCore(ref TextureViewDescription description)
        => Track(_inner.CreateTextureView(ref description));

    public override Swapchain CreateSwapchain(ref SwapchainDescription description)
        => Track(_inner.CreateSwapchain(ref description));

    public override Fence CreateFence(bool signaled)
        => Track(_inner.CreateFence(signaled));
}

public abstract class GraphicsDeviceTestBase<T> : IDisposable where T : GraphicsDeviceCreator
{
    private readonly IWindow _window;
    private readonly GraphicsDevice _gd;
    private readonly TrackingResourceFactory _factory;

    public GraphicsDevice GD => _gd;
    public ResourceFactory RF => _factory;
    public IWindow Window => _window;

    // Non-null for every device TestUtils builds - see HeadlessOptions/SwapchainOptions.
    public TestCountingProfiler Profiler => (TestCountingProfiler)_gd.Profiler!;

    public GraphicsDeviceTestBase()
    {
        Activator.CreateInstance<T>().CreateGraphicsDevice(out _window, out _gd);
        _factory = new TrackingResourceFactory(_gd.ResourceFactory);
    }

    protected DeviceBuffer GetReadback(DeviceBuffer buffer)
    {
        DeviceBuffer readback;
        if ((buffer.Usage & BufferUsage.Staging) != 0)
        {
            readback = buffer;
        }
        else
        {
            readback = RF.CreateBuffer(new BufferDescription(buffer.SizeInBytes, BufferUsage.Staging));
            GD.RunTestGraph(context =>
            {
                CommandBuffer cl = context.GetCommandBuffer();
                cl.CopyBuffer(buffer, 0, readback, 0, buffer.SizeInBytes);
                context.SubmitCommandBuffer(cl);
            });
            GD.WaitForIdle();
        }

        return readback;
    }

    protected Texture GetReadback(Texture texture)
    {
        if ((texture.Usage & TextureUsage.Staging) != 0)
        {
            return texture;
        }
        else
        {
            uint layers = texture.ArrayLayers;
            if ((texture.Usage & TextureUsage.Cubemap) != 0)
            {
                layers *= 6;
            }
            TextureDescription desc = new(
                texture.Width, texture.Height, texture.Depth,
                texture.MipLevels, layers,
                texture.Format,
                TextureUsage.Staging, texture.Type);
            Texture readback = RF.CreateTexture(ref desc);
            GD.RunTestGraph(context =>
            {
                CommandBuffer cl = context.GetCommandBuffer();
                cl.CopyTexture(texture, readback);
                context.SubmitCommandBuffer(cl);
            });
            GD.WaitForIdle();
            return readback;
        }
    }

    public void Dispose()
    {
        GD.WaitForIdle();
        _factory.DisposeAll();
        GD.Dispose();
        _window?.Dispose();
    }
}

public interface GraphicsDeviceCreator
{
    void CreateGraphicsDevice(out IWindow window, out GraphicsDevice gd);
}

public class VulkanDeviceCreator : GraphicsDeviceCreator
{
    public void CreateGraphicsDevice(out IWindow window, out GraphicsDevice gd)
    {
        window = null;
        gd = TestUtils.CreateVulkanDevice();
    }
}

public class VulkanDeviceCreatorWithMainSwapchain : GraphicsDeviceCreator
{
    public void CreateGraphicsDevice(out IWindow window, out GraphicsDevice gd)
    {
        TestUtils.CreateVulkanDeviceWithSwapchain(out window, out gd);
    }
}

// Minimal IProfiler that reproduces just enough of the old built-in counters (live allocation
// gauge, buffer-role memory gauge) for tests that assert on resource lifetime, e.g. orphaning in
// BufferSafetyTests. Everything else Graphite might report is a no-op; Graphite no longer ships
// any counting profiler of its own, so tests that need one bring their own.
public sealed class TestCountingProfiler : IProfiler
{
    private readonly long[] _live = new long[Enum.GetValues<AllocBin>().Length];
    private readonly long[] _bufferMem = new long[Enum.GetValues<BufferRoleBin>().Length];
    private readonly object _lock = new();

    public long Live(AllocBin bin)
    {
        lock (_lock) return _live[(int)bin];
    }

    public long Memory(BufferRoleBin bin)
    {
        lock (_lock) return _bufferMem[(int)bin];
    }

    public void Allocate(AllocBin type, long bytes)
    {
        lock (_lock) _live[(int)type]++;
    }

    public void Free(AllocBin type, long bytes)
    {
        lock (_lock) _live[(int)type]--;
    }

    public void AllocateMemory(BufferRoleBin role, long bytes)
    {
        lock (_lock) _bufferMem[(int)role]++;
    }

    public void FreeMemory(BufferRoleBin role, long bytes)
    {
        lock (_lock) _bufferMem[(int)role]--;
    }

    public void Record(BufferOpBin op, long bytes) { }
    public void RecordSwap(SwapBin evt, long bytes) { }

    public void BeginPass(in PassInfo pass) { }
    public void EndPass(in PassInfo pass) { }
#nullable enable
    public void RecordPassRead(in PassInfo pass, RenderResourceID resource, RenderTexture? texture, DeviceBuffer? buffer) { }
#nullable restore

    public void BeginSample(string name) { }
    public void EndSample() { }

    public bool RequestCapture => false;
    public void Capture(in PassInfo pass, IReadOnlyList<Framebuffer> passOutputs, TransferCommandBuffer transfer) { }

    public void RecordDraw(in CommandBufferInfo commandBuffer, in DrawCallInfo info) { }
    public void RecordDrawBuffers(in CommandBufferInfo commandBuffer, in DrawBufferInfo info) { }
    public void RecordDispatch(in CommandBufferInfo commandBuffer, in DispatchCallInfo info) { }
    public void RecordPipelineSwitch(in CommandBufferInfo commandBuffer, in PipelineBindInfo info) { }
    public void RecordResourceSetBind(uint setCount) { }
    public void RecordBarrier(BarrierBin kind, uint count) { }
    public void RecordSubmit(in ProfilerSubmitInfo info) { }

    public bool RequestExecutionTiming => false;
    public void RecordExecutionTime(PassInfo? pass, ulong commandBufferId, string bufferName, bool isTransfer, double milliseconds) { }
}
