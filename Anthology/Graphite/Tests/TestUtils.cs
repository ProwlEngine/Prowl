using System;
using System.Collections.Generic;

using Prowl.Veldrid.OpenGL;

using Silk.NET.SDL;

namespace Prowl.Veldrid.Tests;

public static unsafe class TestUtils
{
    private static readonly WindowCreateInfo s_defaultWci = new WindowCreateInfo
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
        GraphicsDeviceOptions options = new GraphicsDeviceOptions(true, PixelFormat.R16_UNorm, false);
        SwapchainDescription scDesc = new SwapchainDescription(
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

        SdlContext context = new SdlContext(sdl, window.Handle);
        context.Create((GLattr.Doublebuffer, 1));

        OpenGLPlatformInfo platformInfo = new OpenGLPlatformInfo(
            glContext: context,
            setSyncToVerticalBlank: sync => sdl.GLSetSwapInterval(sync ? 1 : 0));

        gd = GraphicsDevice.CreateOpenGL(options, platformInfo, (uint)window.Width, (uint)window.Height);
    }
}

internal sealed class TrackingResourceFactory : ResourceFactory
{
    private readonly ResourceFactory _inner;
    private readonly List<IDisposable> _created = new List<IDisposable>();

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

    public override CommandBuffer CreateCommandList(ref CommandBufferDescription description)
        => Track(_inner.CreateCommandList(ref description));

    public override Framebuffer CreateFramebuffer(ref FramebufferDescription description)
        => Track(_inner.CreateFramebuffer(ref description));

    protected override DeviceBuffer CreateBufferCore(ref BufferDescription description)
        => Track(_inner.CreateBuffer(ref description));

    protected override Pipeline CreateGraphicsPipelineCore(ref GraphicsPipelineDescription description)
        => Track(_inner.CreateGraphicsPipeline(ref description));

    public override Pipeline CreateComputePipeline(ref ComputePipelineDescription description)
        => Track(_inner.CreateComputePipeline(ref description));

    public override ResourceLayout CreateResourceLayout(ref ResourceLayoutDescription description)
        => Track(_inner.CreateResourceLayout(ref description));

    public override ResourceSet CreateResourceSet(ref ResourceSetDescription description)
        => Track(_inner.CreateResourceSet(ref description));

    protected override Sampler CreateSamplerCore(ref SamplerDescription description)
        => Track(_inner.CreateSampler(ref description));

    protected override ShaderProgram CreateShaderCore(ref ShaderDescription description)
        => Track(_inner.CreateShader(ref description));

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
    private readonly RenderDoc _renderDoc;

    public GraphicsDevice GD => _gd;
    public ResourceFactory RF => _factory;
    public Sdl2Window Window => _window;
    public RenderDoc RenderDoc => _renderDoc;

    public GraphicsDeviceTestBase()
    {
        if (Environment.GetEnvironmentVariable("VELDRID_TESTS_ENABLE_RENDERDOC") == "1"
            && RenderDoc.Load(out _renderDoc))
        {
            _renderDoc.APIValidation = true;
            _renderDoc.DebugOutputMute = false;
        }
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
            CommandBuffer cl = RF.CreateCommandList();
            cl.Begin();
            cl.CopyBuffer(buffer, 0, readback, 0, buffer.SizeInBytes);
            cl.End();
            GD.SubmitCommands(cl);
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
            TextureDescription desc = new TextureDescription(
                texture.Width, texture.Height, texture.Depth,
                texture.MipLevels, layers,
                texture.Format,
                TextureUsage.Staging, texture.Type);
            Texture readback = RF.CreateTexture(ref desc);
            CommandBuffer cl = RF.CreateCommandList();
            cl.Begin();
            cl.CopyTexture(texture, readback);
            cl.End();
            GD.SubmitCommands(cl);
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
