using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Graphite.RenderGraph;

using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace Prowl.Graphite.Tests;

// A RenderContext is normally only handed to a pass while the render graph executes it. Low-level
// GPU tests have no passes, so this harness stands up a throwaway single-view graph with no passes
// and hands the caller its RenderContext directly, giving tests the same recording/submission surface
// (the per-pass command buffer, AllocateTransient) real passes use.
public readonly struct TestRenderView : IRenderView
{
    public uint PixelWidth => 256;
    public uint PixelHeight => 256;
    public int ViewId => 0;
}

public static class TestGraphExtensions
{
    public static ExecutionTask RunTestGraph(this GraphicsDevice gd, Action<RenderContext, CommandBuffer> record)
        => gd.RunTestGraphPasses(1, (context, cmd, _) => record(context, cmd));

    public static ExecutionTask RunTestGraphPasses(this GraphicsDevice gd, int passCount, Action<RenderContext, CommandBuffer, int> record)
    {
        ExecutionTask task = gd.BeginExecution();
        Prowl.Graphite.RenderGraph.RenderGraph graph = Prowl.Graphite.RenderGraph.RenderGraph.Build(
            Array.Empty<IPass>());
        var context = new RenderContext(gd, task, graph, default);

        try
        {
            for (int i = 0; i < passCount; i++)
            {
                CommandBuffer cmd = context.BeginPassCommandBuffer($"Pass{i}");
                record(context, cmd, i);
                context.EndCommandBuffer(cmd);
            }
        }
        finally
        {
            gd.CompleteExecution(task);
        }

        return task;
    }

    public static ExecutionTask RunTestGraph(this GraphicsDevice gd, Action<RenderContext> record)
    {
        ExecutionTask task = gd.BeginExecution();
        Prowl.Graphite.RenderGraph.RenderGraph graph = Prowl.Graphite.RenderGraph.RenderGraph.Build(
            Array.Empty<IPass>());
        var context = new RenderContext(gd, task, graph, default);

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
    private static GraphicsDeviceOptions HeadlessOptions() => new(true);
    private static GraphicsDeviceOptions SwapchainOptions() => new(true);
    private static SwapchainDescription SwapchainConfig() => new();

    public static GraphicsDevice CreateVulkanDevice()
        => GraphicsDevice.CreateVulkan(HeadlessOptions());

    public static void CreateVulkanDeviceWithSwapchain(out IWindow window, out GraphicsDevice gd)
    {
        window = CreateWindow(GraphicsBackend.Vulkan);
        gd = CreateDevice(window, SwapchainOptions(), SwapchainConfig(), GraphicsBackend.Vulkan);
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
    public static GraphicsDevice CreateDevice(IWindow window, GraphicsDeviceOptions options, SwapchainDescription swapchain, GraphicsBackend backend)
    {
        if (!window.IsInitialized)
            throw new InvalidOperationException("Cannot create graphics device with an uninitialized window!");

        switch (backend)
        {
            case GraphicsBackend.Vulkan:
                if (window.API.API != ContextAPI.Vulkan)
                    throw new InvalidOperationException("Attempted to make a Vulkan graphics device without an available Vulkan API");

                VulkanDeviceOptions vkOptions = default;
                SwapchainDescription vkDescription = swapchain;
                vkDescription.Width = (uint)window.Size.X;
                vkDescription.Height = (uint)window.Size.Y;
                vkDescription.Source = SwapchainSource.CreateVulkan(window.VkSurface!);

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

    public override Framebuffer CreateFramebuffer(in FramebufferDescription description)
        => Track(_inner.CreateFramebuffer(description));

    protected override DeviceBuffer CreateBufferCore(in BufferDescription description)
        => Track(_inner.CreateBuffer(description));

    protected override GraphicsProgram CreateGraphicsProgramCore(in ShaderDescription description)
        => Track(_inner.CreateGraphicsProgram(description));

    protected override ComputeProgram CreateComputeProgramCore(in ComputeDescription description)
        => Track(_inner.CreateComputeProgram(description));

    protected override Sampler CreateSamplerCore(in SamplerDescription description)
        => Track(_inner.CreateSampler(description));

    protected override Texture CreateTextureCore(in TextureDescription description)
        => Track(_inner.CreateTexture(description));

    public override Texture CreateTexture(ulong nativeTexture, in TextureDescription description)
        => Track(_inner.CreateTexture(nativeTexture, description));

    protected override TextureView CreateTextureViewCore(in TextureViewDescription description)
        => Track(_inner.CreateTextureView(description));

    public override Swapchain CreateSwapchain(in SwapchainDescription description)
        => Track(_inner.CreateSwapchain(description));
}

public sealed class TexelData<T> where T : unmanaged
{
    public readonly T[] Data;
    public readonly uint Width;
    public readonly uint Height;
    public readonly uint Depth;

    public TexelData(T[] data, uint width, uint height, uint depth)
    {
        Data = data;
        Width = width;
        Height = height;
        Depth = depth;
    }

    public int Length => Data.Length;

    public ref T this[int index] => ref Data[index];
    public ref T this[uint index] => ref Data[index];
    public ref T this[int x, int y] => ref Data[y * (int)Width + x];
    public ref T this[uint x, uint y] => ref Data[y * Width + x];
    public ref T this[int x, int y, int z] => ref Data[(z * (int)Height + y) * (int)Width + x];
    public ref T this[uint x, uint y, uint z] => ref Data[(z * Height + y) * Width + x];
}

public abstract class GraphicsDeviceTestBase<T> : IDisposable where T : GraphicsDeviceCreator
{
    private readonly IWindow _window;
    private readonly GraphicsDevice _gd;
    private readonly TrackingResourceFactory _factory;

    public GraphicsDevice GD => _gd;
    public ResourceFactory RF => _factory;
    private Sampler? _pointSampler;
    public Sampler PointSampler => _pointSampler ??= RF.CreateSampler(SamplerDescription.Point);
    public IWindow Window => _window;

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
            GD.RunTestGraph((context, cl) =>
            {
                cl.CopyBuffer(buffer, 0, readback, 0, buffer.SizeInBytes);
            });
            GD.WaitForIdle();
        }

        return readback;
    }

    protected unsafe TexelData<TTexel> ReadTexture<TTexel>(Texture texture, uint mipLevel = 0, uint arrayLayer = 0) where TTexel : unmanaged
        => ReadTexture<TTexel>(texture, TextureRegion.Whole(texture, mipLevel, arrayLayer));

    protected unsafe TexelData<TTexel> ReadTexture<TTexel>(Texture texture, in TextureRegion region) where TTexel : unmanaged
    {
        byte[] bytes = ReadTextureBytes(texture, region, region.Width * region.Height * region.Depth * (uint)sizeof(TTexel));
        TTexel[] data = new TTexel[region.Width * region.Height * region.Depth];
        MemoryMarshal.Cast<byte, TTexel>(bytes).CopyTo(data);
        return new TexelData<TTexel>(data, region.Width, region.Height, region.Depth);
    }

    protected byte[] ReadTextureBytes(Texture texture, in TextureRegion region, uint sizeInBytes)
    {
        DeviceBuffer buffer = RF.CreateBuffer(new BufferDescription(sizeInBytes, BufferUsage.Staging));
        TextureRegion copy = region;
        GD.Record(cmd => cmd.CopyTextureToBuffer(texture, buffer, 0, copy)).Wait();
        byte[] bytes = GD.Map(buffer).Slice(0, (int)sizeInBytes).ToArray();
        GD.Unmap(buffer);
        return bytes;
    }

    protected TexelData<TTexel> ReadTexels<TTexel>(DeviceBuffer staging, uint width, uint height = 1, uint depth = 1) where TTexel : unmanaged
    {
        TTexel[] data = GD.Map<TTexel>(staging).Slice(0, (int)(width * height * depth)).ToArray();
        GD.Unmap(staging);
        return new TexelData<TTexel>(data, width, height, depth);
    }

    protected unsafe DeviceBuffer CreateTexelReadbackBuffer<TTexel>(uint width, uint height = 1, uint depth = 1) where TTexel : unmanaged
        => RF.CreateBuffer(new BufferDescription(width * height * depth * (uint)sizeof(TTexel), BufferUsage.Staging));

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
