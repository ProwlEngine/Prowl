using System.Runtime.InteropServices;
using System.Text;

using Prowl.Veldrid.OpenGL;

using Prowl.Vector;

using Silk.NET.SDL;

namespace Prowl.Veldrid.Samples.HelloTriangle;

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

    private struct Provider : IVertexSource
    {
        public DeviceBuffer Buffer;

        public PrimitiveTopology Topology => PrimitiveTopology.TriangleList;

        public void ResolveSlot(uint layoutSlot, in VertexLayoutDescription layout, out VertexBinding binding)
        {
            binding = new(Buffer);
        }

        public bool TryGetIndexBuffer(out DeviceBuffer buffer, out IndexFormat format, out uint offset)
        {
            buffer = null;
            format = IndexFormat.UInt32;
            offset = 0;
            return false;
        }
    }

    private static int Main()
    {
        WindowCreateInfo wci = new WindowCreateInfo
        {
            WindowWidth = 960,
            WindowHeight = 540,
            WindowTitle = "Hello Triangle",
            WindowInitialState = WindowState.Normal,
        };

        Sdl2Window window = Startup.CreateWindow(ref wci, WindowFlags.Opengl);
        Sdl sdl = Startup.Sdl;

        SdlContext context = new SdlContext(sdl, window.Handle);
        context.Create(
            (GLattr.ContextMajorVersion, 3),
            (GLattr.ContextMinorVersion, 3),
            (GLattr.ContextProfileMask, (int)GLprofile.Core),
            (GLattr.Doublebuffer, 1),
            (GLattr.DepthSize, 24));

        sdl.GLSetSwapInterval(0);

        OpenGLPlatformInfo platformInfo = new OpenGLPlatformInfo(
            glContext: context,
            setSyncToVerticalBlank: sync => sdl.GLSetSwapInterval(sync ? 1 : 0));

        GraphicsDeviceOptions options = new GraphicsDeviceOptions(
            debug: false,
            swapchainDepthFormat: PixelFormat.R16_UNorm,
            syncToVerticalBlank: true);

        GraphicsDevice gd = GraphicsDevice.CreateOpenGL(options, platformInfo, (uint)window.Width, (uint)window.Height);
        ResourceFactory factory = gd.ResourceFactory;

        VertexPositionUV[] vertices =
        [
            new(new(  0.0f,  0.75f), new(1.0f, 1.0f)),
            new(new( 0.75f, -0.75f), new(1.0f, 0.0f)),
            new(new(-0.75f, -0.75f), new(0.0f, 1.0f)),
        ];

        uint vertexBufferSize = (uint)(vertices.Length * sizeof(VertexPositionUV));
        DeviceBuffer vertexBuffer = factory.CreateBuffer(new BufferDescription(vertexBufferSize, BufferUsage.VertexBuffer));
        gd.UpdateBuffer(vertexBuffer, 0, vertices);

        Provider provider = new()
        {
            Buffer = vertexBuffer
        };

        ShaderDescription shaderDesc = new()
        {
            Stages =
            [
                new ShaderStageDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(VertexShaderSource), "main"),
                new ShaderStageDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(FragmentShaderSource), "main")
            ],

            BlendState = BlendStateDescription.SingleOverrideBlend,
            DepthStencilState = DepthStencilStateDescription.Disabled,
            RasterizerState = RasterizerStateDescription.CullNone,
            ResourceLayouts = [],
            VertexLayouts =
            [
                new VertexLayoutDescription(
                    0u,
                    new VertexElementDescription("a_Position", VertexElementFormat.Float2),
                    new VertexElementDescription("a_UV", VertexElementFormat.Float2))
            ]
        };

        ShaderProgram shader = factory.CreateShaderProgram(new ShaderDescription(
            new ShaderStageDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(VertexShaderSource), "main"),
            new ShaderStageDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(FragmentShaderSource), "main")));

        CommandBuffer cl = factory.CreateCommandList();

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
                            window.Resized(evt.Window.Data1, evt.Window.Data2);
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
            cl.ClearColorTarget(0, new Vector.Color(0.10f, 0.12f, 0.16f, 1.0f));
            cl.SetShader(shader);
            cl.SetVertexSource(provider);
            cl.Draw(vertexCount: 3);
            cl.End();

            gd.SubmitCommands(cl);
            gd.SwapBuffers();
        }

        cl.Dispose();
        shader.Dispose();
        vertexBuffer.Dispose();
        gd.Dispose();

        window.Close();
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
