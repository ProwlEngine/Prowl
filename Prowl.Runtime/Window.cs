using Silk.NET.Core;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Prowl.Runtime
{
    public static class Window
    {

        public static IWindow InternalWindow { get; internal set; }
        public static IInputContext InternalInput { get; internal set; }

        public static event Action? Load;
        public static event Action<double>? Update;
        public static event Action<double>? Render;
        public static event Action<double>? PostRender;
        public static event Action<bool>? FocusChanged;
        public static event Action<Vector2D<int>>? Resize;
        public static event Action<Vector2D<int>>? FramebufferResize;
        public static event Action? Closing;

        public static event Action<Vector2D<int>>? Move;
        public static event Action<WindowState>? StateChanged;
        public static event Action<string[]>? FileDrop;

        public static Vector2D<int> Size {
            get { return InternalWindow.Size; }
            set { InternalWindow.Size = value; }
        }

        public static bool IsVisible {
            get { return InternalWindow.IsVisible; }
            set { InternalWindow.IsVisible = value; }
        }

        public static bool VSync {
            get { return InternalWindow.VSync; }
            set { InternalWindow.VSync = value; }
        }

        public static double FramesPerSecond {
            get { return InternalWindow.FramesPerSecond; }
            set { InternalWindow.FramesPerSecond = value; InternalWindow.UpdatesPerSecond = value; }
        }

        public static nint Handle {
            get { return InternalWindow.Handle; }
        }

        private static bool isFocused = true;
        private static DefaultInputHandler WindowInputHandler;

        public static bool IsFocused {
            get { return isFocused; }
        }

        public static void InitWindow(string title, int width, int height, WindowState startState = WindowState.Normal, bool VSync = true)
        {
            WindowOptions options = WindowOptions.Default;
            options.Title = title;
            options.Size = new Vector2D<int>(width, height);
            options.WindowState = startState;
            options.VSync = VSync;
            var api = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(4, 1));
            options.API = api;
            InternalWindow = Silk.NET.Windowing.Window.Create(options);
            InternalWindow.Load += OnLoad;
            InternalWindow.Update += OnUpdate;
            InternalWindow.Render += OnRender;
            InternalWindow.FocusChanged += OnFocusChanged;
            InternalWindow.Resize += OnResize;
            InternalWindow.FramebufferResize += OnFramebufferResize;
            InternalWindow.Closing += OnClose;

            InternalWindow.StateChanged += (state) => { StateChanged?.Invoke(state); };
            InternalWindow.FileDrop += (files) => { FileDrop?.Invoke(files); };

            InternalWindow.FocusChanged += (focused) => { isFocused = focused; };
        }

        public static void Start() => InternalWindow.Run();
        public static void Stop() => InternalWindow.Close();
        public static void OnLoad()
        {
            LoadIcon();
            InternalInput = InternalWindow.CreateInput();
            WindowInputHandler = new DefaultInputHandler(InternalInput);
            Graphics.Initialize();
            //Audio.Initialize();

            // Push Default Handler
            Input.PushHandler(WindowInputHandler);
            Load?.Invoke();
        }

        // code reference https://github.com/dotnet/Silk.NET/blob/b079b28cd51ce447183cfedde0a85412b9b226ee/src/Lab/Experiments/BlankWindow/Program.cs#L82
        public static unsafe void LoadIcon(){
            Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Prowl.Runtime.EmbeddedResources.Logo.png");
            if(stream != null)
            using (var memoryStream = new MemoryStream())
            {
                stream.CopyTo(memoryStream);
                using var image = Image.Load<Rgba32>(memoryStream.ToArray());
                var memoryGroup = image.GetPixelMemoryGroup();

                Memory<byte> array = new byte[memoryGroup.TotalLength * sizeof(Rgba32)];
                var block = MemoryMarshal.Cast<byte, Rgba32>(array.Span);
                foreach (var memory in memoryGroup)
                {
                    memory.Span.CopyTo(block);
                    block = block.Slice(memory.Length);
                }
                
                var icon = new RawImage(image.Width, image.Height, array);
                InternalWindow.SetWindowIcon(ref icon);
            }
        }

        public static void OnRender(double delta)
        {
            Render?.Invoke(delta);
            PostRender?.Invoke(delta);
        }

        public static void OnFocusChanged(bool focused)
        {
            FocusChanged?.Invoke(focused);
        }

        public static void OnResize(Vector2D<int> size)
        {
            Resize?.Invoke(size);
        }

        public static void OnFramebufferResize(Vector2D<int> size)
        {
            FramebufferResize?.Invoke(size);
        }

        public static void OnUpdate(double delta)
        {
            Update?.Invoke(delta);
            WindowInputHandler.LateUpdate();
        }

        public static void OnClose()
        {
            Closing?.Invoke();
            WindowInputHandler.Dispose();
            Input.PopHandler();
            Graphics.Dispose();
        }

    }
}
