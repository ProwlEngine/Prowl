using System;
using System.Collections.Generic;

using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace Prowl.Graphite.Tests;

// Device/window creation for the test suite. The device-creation switch is duplicated from
// Samples/Shared/DeviceCreateUtilities so the tests exercise the same path the samples do.
// Most GPU tests run on a headless device (no window/swapchain); only the *WithMainSwapchain
// creators build a window.
public static class TestUtils
{
    private static readonly GraphicsDeviceOptions s_headlessOptions = new(true) { EnableProfiling = true };
    private static readonly GraphicsDeviceOptions s_swapchainOptions = new(true, PixelFormat.R16_UNorm, false) { EnableProfiling = true };

    public static GraphicsDevice CreateVulkanDevice()
        => GraphicsDevice.CreateVulkan(s_headlessOptions);

    public static void CreateVulkanDeviceWithSwapchain(out IWindow window, out GraphicsDevice gd)
    {
        window = CreateWindow(GraphicsBackend.Vulkan);
        gd = CreateDevice(window, s_swapchainOptions, GraphicsBackend.Vulkan);
    }

#if TEST_D3D11
    public static GraphicsDevice CreateD3D11Device()
        => GraphicsDevice.CreateD3D11(s_headlessOptions);

    public static void CreateD3D11DeviceWithSwapchain(out IWindow window, out GraphicsDevice gd)
    {
        window = CreateWindow(GraphicsBackend.Direct3D11);
        gd = CreateDevice(window, s_swapchainOptions, GraphicsBackend.Direct3D11);
    }
#endif

    internal static void CreateOpenGLDevice(out IWindow window, out GraphicsDevice gd)
    {
        window = CreateWindow(GraphicsBackend.OpenGL);
        gd = CreateDevice(window, s_swapchainOptions, GraphicsBackend.OpenGL);
    }

    internal static void CreateOpenGLESDevice(out IWindow window, out GraphicsDevice gd)
    {
        window = CreateWindow(GraphicsBackend.OpenGLES);
        gd = CreateDevice(window, s_swapchainOptions, GraphicsBackend.OpenGLES);
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
        GraphicsBackend.OpenGL =>
            new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(4, 5)),
        GraphicsBackend.OpenGLES =>
            new GraphicsAPI(ContextAPI.OpenGLES, ContextProfile.Core, ContextFlags.Default, new APIVersion(3, 2)),
        GraphicsBackend.Direct3D11 =>
            new GraphicsAPI(ContextAPI.None, ContextProfile.Core, ContextFlags.Default, new APIVersion(0, 0)),
        _ => throw new ArgumentOutOfRangeException(nameof(backend))
    };

    // Duplicated from Samples/Shared/DeviceCreateUtilities.CreateDevice.
    public static GraphicsDevice CreateDevice(IWindow window, GraphicsDeviceOptions options, GraphicsBackend backend)
    {
        if (!window.IsInitialized)
            throw new InvalidOperationException("Cannot create graphics device with an uninitialized window!");

        switch (backend)
        {
            case GraphicsBackend.OpenGLES:
            case GraphicsBackend.OpenGL:
                if (window.API.API != ContextAPI.OpenGLES && window.API.API != ContextAPI.OpenGL)
                    throw new InvalidOperationException("Attempted to make a GL graphics device without an available GL or GLES context");

                OpenGL.OpenGLPlatformInfo glInfo = new(
                    glContext: window.GLContext!,
                    setSyncToVerticalBlank: sync =>
                    {
                        window.VSync = sync;
                        window.GLContext!.SwapInterval(window.VSync ? 1 : 0);
                    });

                return GraphicsDevice.CreateOpenGL(options, glInfo, (uint)window.Size.X, (uint)window.Size.Y);

            case GraphicsBackend.Direct3D11:
                if (window.Native!.Win32 == null)
                    throw new InvalidOperationException("Attempted to make a D3D11 graphics device without a Win32 window!");

                (nint Hwnd, nint HDC, nint HInstance) = window.Native!.Win32!.Value;

                D3D11DeviceOptions d3dOptions = default;

                SwapchainDescription d3dDesc = new()
                {
                    DepthFormat = options.SwapchainDepthFormat,
                    ColorSrgb = options.SwapchainSrgbFormat,
                    Width = (uint)window.FramebufferSize.X,
                    Height = (uint)window.FramebufferSize.Y,
                    SyncToVerticalBlank = options.SyncToVerticalBlank,
                    Source = SwapchainSource.CreateWin32(Hwnd, HInstance)
                };

                return GraphicsDevice.CreateD3D11(options, d3dOptions, d3dDesc);

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

    public TrackingResourceFactory(ResourceFactory inner) : base(inner.Features)
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
            CommandBuffer cl = RF.CreateCommandBuffer();
            cl.Begin();
            cl.CopyBuffer(buffer, 0, readback, 0, buffer.SizeInBytes);
            cl.End();
            { Frame _f = GD.BeginFrame(); _f.SubmitCommands(cl); GD.EndFrame(_f); }
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
            CommandBuffer cl = RF.CreateCommandBuffer();
            cl.Begin();
            cl.CopyTexture(texture, readback);
            cl.End();
            { Frame _f = GD.BeginFrame(); _f.SubmitCommands(cl); GD.EndFrame(_f); }
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

#if TEST_D3D11
public class D3D11DeviceCreator : GraphicsDeviceCreator
{
    public void CreateGraphicsDevice(out IWindow window, out GraphicsDevice gd)
    {
        window = null;
        gd = TestUtils.CreateD3D11Device();
    }
}

public class D3D11DeviceCreatorWithMainSwapchain : GraphicsDeviceCreator
{
    public void CreateGraphicsDevice(out IWindow window, out GraphicsDevice gd)
    {
        TestUtils.CreateD3D11DeviceWithSwapchain(out window, out gd);
    }
}
#endif

public class OpenGLDeviceCreator : GraphicsDeviceCreator
{
    public void CreateGraphicsDevice(out IWindow window, out GraphicsDevice gd)
    {
        TestUtils.CreateOpenGLDevice(out window, out gd);
    }
}

public class OpenGLESDeviceCreator : GraphicsDeviceCreator
{
    public void CreateGraphicsDevice(out IWindow window, out GraphicsDevice gd)
    {
        TestUtils.CreateOpenGLESDevice(out window, out gd);
    }
}
