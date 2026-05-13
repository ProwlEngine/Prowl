using System;
using System.Runtime.InteropServices;
using System.Text;

using NeoVeldrid;
using NeoVeldrid.OpenGL;

using Prowl.Vector;

using Silk.NET.SDL;

namespace NeoVeldrid.Samples.HelloTriangle;

internal static unsafe class Program
{
    private const string VertexShaderSource = @"#version 330 core
layout(location = 0) in vec2 a_Position;
layout(location = 1) in vec2 a_UV;

out vec2 v_UV;

void main()
{
    v_UV = a_UV;
    gl_Position = vec4(a_Position, 0.0, 1.0);
}
";

    private const string FragmentShaderSource = @"#version 330 core
in vec2 v_UV;

out vec4 fragColor;

void main()
{
    fragColor = vec4(v_UV, 0.0, 1.0);
}
";

    private static int Main()
    {
        Sdl sdl = Sdl.GetApi();
        if (sdl.Init(Sdl.InitVideo) != 0)
        {
            Console.Error.WriteLine($"SDL_Init failed: {sdl.GetErrorS()}");
            return 1;
        }

        sdl.GLSetAttribute(GLattr.ContextMajorVersion, 3);
        sdl.GLSetAttribute(GLattr.ContextMinorVersion, 3);
        sdl.GLSetAttribute(GLattr.ContextProfileMask, (int)GLprofile.Core);
        sdl.GLSetAttribute(GLattr.Doublebuffer, 1);
        sdl.GLSetAttribute(GLattr.DepthSize, 24);

        const uint Width = 960;
        const uint Height = 540;

        Window* window = sdl.CreateWindow(
            "NeoVeldrid - Hello Triangle",
            Sdl.WindowposCentered, Sdl.WindowposCentered,
            (int)Width, (int)Height,
            (uint)(WindowFlags.Opengl | WindowFlags.Resizable | WindowFlags.Shown));

        if (window == null)
        {
            Console.Error.WriteLine($"SDL_CreateWindow failed: {sdl.GetErrorS()}");
            sdl.Quit();
            return 1;
        }

        void* glContext = sdl.GLCreateContext(window);
        if (glContext == null)
        {
            Console.Error.WriteLine($"SDL_GL_CreateContext failed: {sdl.GetErrorS()}");
            sdl.DestroyWindow(window);
            sdl.Quit();
            return 1;
        }

        sdl.GLSetSwapInterval(0);

        OpenGLPlatformInfo platformInfo = new OpenGLPlatformInfo(
            openGLContextHandle: (IntPtr)glContext,
            getProcAddress: name =>
            {
                byte[] bytes = Encoding.ASCII.GetBytes(name + "\0");
                fixed (byte* p = bytes)
                {
                    return (IntPtr)sdl.GLGetProcAddress(p);
                }
            },
            makeCurrent: ctx => sdl.GLMakeCurrent(window, (void*)ctx),
            getCurrentContext: () => (IntPtr)sdl.GLGetCurrentContext(),
            clearCurrentContext: () => sdl.GLMakeCurrent(window, null),
            deleteContext: ctx => sdl.GLDeleteContext((void*)ctx),
            swapBuffers: () => sdl.GLSwapWindow(window),
            setSyncToVerticalBlank: sync => sdl.GLSetSwapInterval(sync ? 1 : 0));

        GraphicsDeviceOptions options = new GraphicsDeviceOptions(
            debug: false,
            swapchainDepthFormat: PixelFormat.R16_UNorm,
            syncToVerticalBlank: true);

        GraphicsDevice gd = GraphicsDevice.CreateOpenGL(options, platformInfo, Width, Height);
        ResourceFactory factory = gd.ResourceFactory;

        // Vertex buffer: 3 vertices, each (Position xy, UV xy) = 16 bytes
        VertexPositionUV[] vertices =
        {
            new VertexPositionUV(new Float2(  0.0f,  0.75f), new Float2(1.0f, 1.0f)),
            new VertexPositionUV(new Float2( 0.75f, -0.75f), new Float2(1.0f, 0.0f)),
            new VertexPositionUV(new Float2(-0.75f, -0.75f), new Float2(0.0f, 1.0f)),
        };

        uint vertexBufferSize = (uint)(vertices.Length * sizeof(VertexPositionUV));
        DeviceBuffer vertexBuffer = factory.CreateBuffer(new BufferDescription(vertexBufferSize, BufferUsage.VertexBuffer));
        gd.UpdateBuffer(vertexBuffer, 0, vertices);

        Shader vertexShader = factory.CreateShader(new ShaderDescription(
            ShaderStages.Vertex, Encoding.UTF8.GetBytes(VertexShaderSource), "main"));
        Shader fragmentShader = factory.CreateShader(new ShaderDescription(
            ShaderStages.Fragment, Encoding.UTF8.GetBytes(FragmentShaderSource), "main"));

        VertexLayoutDescription vertexLayout = new VertexLayoutDescription(
            new VertexElementDescription("a_Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
            new VertexElementDescription("a_UV", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2));

        GraphicsPipelineDescription pipelineDesc = new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = new DepthStencilStateDescription(
                depthTestEnabled: false,
                depthWriteEnabled: false,
                comparisonKind: ComparisonKind.Always),
            RasterizerState = RasterizerStateDescription.CullNone,
            PrimitiveTopology = PrimitiveTopology.TriangleList,
            ResourceLayouts = [],
            ShaderSet = new ShaderSetDescription(
                vertexLayouts: [vertexLayout],
                shaders: [vertexShader, fragmentShader]),
            Outputs = gd.SwapchainFramebuffer.OutputDescription,
        };

        Pipeline pipeline = factory.CreateGraphicsPipeline(pipelineDesc);
        CommandList cl = factory.CreateCommandList();

        bool running = true;
        Event evt;
        while (running)
        {
            while (sdl.PollEvent(&evt) != 0)
            {
                switch ((EventType)evt.Type)
                {
                    case EventType.Quit:
                        running = false;
                        break;
                    case EventType.Windowevent:
                        if (evt.Window.Event == (byte)WindowEventID.Close)
                        {
                            running = false;
                        }
                        else if (evt.Window.Event == (byte)WindowEventID.SizeChanged
                              || evt.Window.Event == (byte)WindowEventID.Resized)
                        {
                            gd.ResizeMainWindow((uint)evt.Window.Data1, (uint)evt.Window.Data2);
                        }
                        break;
                    case EventType.Keydown:
                        if (evt.Key.Keysym.Sym == (int)KeyCode.KEscape)
                        {
                            running = false;
                        }
                        break;
                }
            }

            cl.Begin();
            cl.SetFramebuffer(gd.SwapchainFramebuffer);
            cl.ClearColorTarget(0, new Prowl.Vector.Color(0.10f, 0.12f, 0.16f, 1.0f));
            cl.SetPipeline(pipeline);
            cl.SetVertexBuffer(0, vertexBuffer);
            cl.Draw(vertexCount: 3);
            cl.End();

            gd.SubmitCommands(cl);
            gd.SwapBuffers();
        }

        cl.Dispose();
        pipeline.Dispose();
        vertexShader.Dispose();
        fragmentShader.Dispose();
        vertexBuffer.Dispose();
        gd.Dispose();

        sdl.DestroyWindow(window);
        sdl.Quit();

        return 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VertexPositionUV
    {
        public Float2 Position;
        public Float2 UV;

        public VertexPositionUV(Float2 position, Float2 uv)
        {
            Position = position;
            UV = uv;
        }
    }
}
