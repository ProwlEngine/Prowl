using Silk.NET.Maths;
using System;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace Prowl.Runtime
{
    public static class Screen
    {
        public static Sdl2Window InternalWindow { get; internal set; }
        public static GraphicsDevice InternalDevice { get; internal set; }
        public static InputSnapshot LatestInputSnapshot { get; internal set; }


        public static event Action? Load;
        public static event Action? Update;
        public static event Action<bool>? FocusChanged;
        public static event Action<Vector2Int>? Resize;
        public static event Action? Closing;

        public static event Action<Vector2Int>? Move;
        public static event Action<string[]>? FileDrop;

        public static Vector2Int Size {
            get { return new Vector2Int(InternalWindow.Width, InternalWindow.Height); }
            set { InternalWindow.Width = value.x; InternalWindow.Height = value.y; }
        }

        public static bool VSync {
            get { return InternalDevice.SyncToVerticalBlank; }
            set { InternalDevice.SyncToVerticalBlank = value; }
        }

        public static float FramesPerSecond {
            get { return InternalWindow.PollIntervalInMs / 1000.0f; }
            set { InternalWindow.LimitPollRate = value != double.MaxValue; InternalWindow.PollIntervalInMs = value * 1000.0f; }
        }

        public static bool IsVisible {
            get { return InternalWindow.Visible; }
            set { InternalWindow.Visible = value; }
        }

        public static nint Handle {
            get { return InternalWindow.Handle; }
        }

        private static bool isFocused = true;
        public static bool IsFocused {
            get { return isFocused; }
        }

        public static void Start(string name, Vector2Int size, Vector2Int position, WindowState initialState = WindowState.Normal, bool VSync = true, GraphicsBackend preferredBackend = GraphicsBackend.OpenGL)
        {
            WindowCreateInfo windowInfo = new()
            {
                WindowTitle = name,
                WindowInitialState = initialState,
                WindowWidth = size.x,
                WindowHeight = size.y,
                X = position.x,
                Y = position.y
            };

            GraphicsDeviceOptions deviceOptions = new()
            {
                SyncToVerticalBlank = VSync,
                PreferStandardClipSpaceYDirection = true,
                PreferDepthRangeZeroToOne = true,
                ResourceBindingModel = ResourceBindingModel.Default,
            };

            InternalWindow = VeldridStartup.CreateWindow(ref windowInfo);
            
            InternalDevice = VeldridStartup.CreateGraphicsDevice(InternalWindow, deviceOptions, preferredBackend);

            OnLoad();

            InternalWindow.DragDrop += (dragDropEvent) => { FileDrop?.Invoke([dragDropEvent.File]); };

            InternalWindow.Resized += OnResize;

            InternalWindow.FocusGained += () => { OnFocusChanged(isFocused = true); };
            InternalWindow.FocusLost += () => { OnFocusChanged(isFocused = false); };

            InternalWindow.Closing += OnClose;

            while (InternalWindow.Exists)
            {
                LatestInputSnapshot = InternalWindow.PumpEvents();  
            }
        }


        public static void Stop() => InternalWindow.Close();

        public static void OnLoad()
        {
            Load?.Invoke();
        }

        public static void OnFocusChanged(bool focused)
        {
            FocusChanged?.Invoke(focused);
        }

        public static void OnResize()
        {
            Resize?.Invoke(Size);
        }

        public static void OnUpdate()
        {
            Update?.Invoke();
        }

        public static void OnClose()
        {
            Closing?.Invoke();
        }
    }
}
