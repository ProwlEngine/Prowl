using System;
using System.Collections.Generic;

using Prowl.Veldrid.OpenGL;

using Silk.NET.SDL;

namespace Prowl.Veldrid.Tests;

public static unsafe class TestUtils
{
    private static readonly WindowCreateInfo s_defaultWci = new()
    {
        WindowWidth = 200,
        WindowHeight = 200,
        WindowInitialState = WindowState.Hidden,
    };

    public static GraphicsDevice CreateVulkanDevice()
    {
        return GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true));
    }

    public static void CreateDeviceWithSwapchain(
        WindowCreateInfo wci,
        GraphicsDeviceOptions options,
        GraphicsBackend backend,
        out Sdl2Window window,
        out GraphicsDevice gd)
    {
        switch (backend)
        {
            case GraphicsBackend.Vulkan:
                window = Startup.CreateWindow(ref wci, WindowFlags.Vulkan);
                gd = GraphicsDevice.CreateVulkan(options, new SwapchainDescription(
                    Startup.GetSwapchainSource(window),
                    (uint)window.Width, (uint)window.Height,
                    options.SwapchainDepthFormat, options.SyncToVerticalBlank, options.SwapchainSrgbFormat));
                break;
#if TEST_D3D11
            case GraphicsBackend.Direct3D11:
                window = Startup.CreateWindow(ref wci);
                gd = GraphicsDevice.CreateD3D11(options, new SwapchainDescription(
                    Startup.GetSwapchainSource(window),
                    (uint)window.Width, (uint)window.Height,
                    options.SwapchainDepthFormat, options.SyncToVerticalBlank, options.SwapchainSrgbFormat));
                break;
#endif
            case GraphicsBackend.OpenGL:
            case GraphicsBackend.OpenGLES:
                CreateOpenGLDeviceCore(backend, options, ref wci, out window, out gd);
                break;
            default:
                throw new System.ArgumentOutOfRangeException(nameof(backend));
        }
    }

    public static void CreateVulkanDeviceWithSwapchain(out Sdl2Window window, out GraphicsDevice gd)
    {
        WindowCreateInfo wci = s_defaultWci;
        window = Startup.CreateWindow(ref wci, WindowFlags.Vulkan);
        GraphicsDeviceOptions options = new(true, PixelFormat.R16_UNorm, false);
        SwapchainDescription scDesc = new(
            Startup.GetSwapchainSource(window),
            (uint)window.Width, (uint)window.Height,
            options.SwapchainDepthFormat,
            options.SyncToVerticalBlank,
            options.SwapchainSrgbFormat);
        gd = GraphicsDevice.CreateVulkan(options, scDesc);
    }

#if TEST_D3D11
    public static GraphicsDevice CreateD3D11Device()
    {
        return GraphicsDevice.CreateD3D11(new GraphicsDeviceOptions(true));
    }

    public static void CreateD3D11DeviceWithSwapchain(out Sdl2Window window, out GraphicsDevice gd)
    {
        WindowCreateInfo wci = s_defaultWci;
        window = Startup.CreateWindow(ref wci, WindowFlags.None);
        GraphicsDeviceOptions options = new GraphicsDeviceOptions(true, PixelFormat.R16_UNorm, false);
        SwapchainDescription scDesc = new SwapchainDescription(
            Startup.GetSwapchainSource(window),
            (uint)window.Width, (uint)window.Height,
            options.SwapchainDepthFormat,
            options.SyncToVerticalBlank,
            options.SwapchainSrgbFormat);
        gd = GraphicsDevice.CreateD3D11(options, scDesc);
    }
#endif

    internal static void CreateOpenGLDevice(out Sdl2Window window, out GraphicsDevice gd)
    {
        WindowCreateInfo wci = s_defaultWci;
        CreateOpenGLDeviceCore(GraphicsBackend.OpenGL, new GraphicsDeviceOptions(true, PixelFormat.R16_UNorm, false), ref wci, out window, out gd);
    }

    internal static void CreateOpenGLESDevice(out Sdl2Window window, out GraphicsDevice gd)
    {
        WindowCreateInfo wci = s_defaultWci;
        CreateOpenGLDeviceCore(GraphicsBackend.OpenGLES, new GraphicsDeviceOptions(true, PixelFormat.R16_UNorm, false), ref wci, out window, out gd);
    }

    private static void CreateOpenGLDeviceCore(
        GraphicsBackend backend,
        GraphicsDeviceOptions options,
        ref WindowCreateInfo wci,
        out Sdl2Window window,
        out GraphicsDevice gd)
    {
        Sdl sdl = Startup.Sdl;
        bool gles = backend == GraphicsBackend.OpenGLES;

        sdl.GLSetAttribute(GLattr.ContextFlags, (int)(GLcontextFlag.DebugFlag | GLcontextFlag.ForwardCompatibleFlag));
        sdl.GLSetAttribute(GLattr.ContextProfileMask, (int)(gles ? GLprofile.ES : GLprofile.Core));
        sdl.GLSetAttribute(GLattr.ContextMajorVersion, gles ? 3 : 4);
        sdl.GLSetAttribute(GLattr.ContextMinorVersion, gles ? 2 : 3);
        sdl.GLSetAttribute(GLattr.DepthSize, 16);
        sdl.GLSetAttribute(GLattr.StencilSize, 0);
        sdl.GLSetAttribute(GLattr.FramebufferSrgbCapable, options.SwapchainSrgbFormat ? 1 : 0);

        window = Startup.CreateWindow(ref wci, WindowFlags.Opengl);

        SdlContext context = new(sdl, window.Handle);
        context.Create((GLattr.Doublebuffer, 1));

        OpenGLPlatformInfo platformInfo = new(
            glContext: context,
            setSyncToVerticalBlank: sync => sdl.GLSetSwapInterval(sync ? 1 : 0));

        gd = GraphicsDevice.CreateOpenGL(options, platformInfo, (uint)window.Width, (uint)window.Height);
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

    protected override ShaderProgram CreateShaderProgramCore(ref ShaderDescription description)
        => Track(_inner.CreateShaderProgram(ref description));

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
    private readonly Sdl2Window _window;
    private readonly GraphicsDevice _gd;
    private readonly TrackingResourceFactory _factory;

    public GraphicsDevice GD => _gd;
    public ResourceFactory RF => _factory;
    public Sdl2Window Window => _window;

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
        _window?.Close();
    }
}

public interface GraphicsDeviceCreator
{
    void CreateGraphicsDevice(out Sdl2Window window, out GraphicsDevice gd);
}

public class VulkanDeviceCreator : GraphicsDeviceCreator
{
    public void CreateGraphicsDevice(out Sdl2Window window, out GraphicsDevice gd)
    {
        window = null;
        gd = TestUtils.CreateVulkanDevice();
    }
}

public class VulkanDeviceCreatorWithMainSwapchain : GraphicsDeviceCreator
{
    public void CreateGraphicsDevice(out Sdl2Window window, out GraphicsDevice gd)
    {
        TestUtils.CreateVulkanDeviceWithSwapchain(out window, out gd);
    }
}

#if TEST_D3D11
public class D3D11DeviceCreator : GraphicsDeviceCreator
{
    public void CreateGraphicsDevice(out Sdl2Window window, out GraphicsDevice gd)
    {
        window = null;
        gd = TestUtils.CreateD3D11Device();
    }
}

public class D3D11DeviceCreatorWithMainSwapchain : GraphicsDeviceCreator
{
    public void CreateGraphicsDevice(out Sdl2Window window, out GraphicsDevice gd)
    {
        TestUtils.CreateD3D11DeviceWithSwapchain(out window, out gd);
    }
}
#endif

public class OpenGLDeviceCreator : GraphicsDeviceCreator
{
    public void CreateGraphicsDevice(out Sdl2Window window, out GraphicsDevice gd)
    {
        TestUtils.CreateOpenGLDevice(out window, out gd);
    }
}

public class OpenGLESDeviceCreator : GraphicsDeviceCreator
{
    public void CreateGraphicsDevice(out Sdl2Window window, out GraphicsDevice gd)
    {
        TestUtils.CreateOpenGLESDevice(out window, out gd);
    }
}
