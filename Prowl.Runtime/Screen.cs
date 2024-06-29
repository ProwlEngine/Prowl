using System;
using Veldrid;
using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace Prowl.Runtime
{
    public static class Screen
    {
        public static Sdl2Window InternalWindow { get; internal set; }

        // Hacky way to give Input access to the polled input snapshot object.
        public static InputSnapshot LatestInputSnapshot { get; private set; } 


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

        public static Vector2Int Position {
            get { return new Vector2Int(InternalWindow.X, InternalWindow.Y); }
            set { InternalWindow.X = value.x; InternalWindow.Y = value.y; }
        }

        public static Rect ScreenRect => new(Position, Size);

        public static float FramesPerSecond {
            get { return InternalWindow.PollIntervalInMs / 1000.0f; }
            set { InternalWindow.LimitPollRate = value != 0 && value != double.MaxValue; InternalWindow.PollIntervalInMs = value * 1000.0f; }
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

        public static DefaultInputHandler WindowInputHandler { get; private set; }


        public static void Start(string name, Vector2Int size, Vector2Int position, WindowState initialState = WindowState.Normal)
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

            InternalWindow = VeldridStartup.CreateWindow(ref windowInfo);

            LatestInputSnapshot = InternalWindow.PumpEvents();
            WindowInputHandler = new DefaultInputHandler();
            
            Input.PushHandler(WindowInputHandler);

            Load?.Invoke();

            InternalWindow.DragDrop += (dragDropEvent) => { FileDrop?.Invoke([dragDropEvent.File]); };

            InternalWindow.Resized += () => Resize?.Invoke(Size);

            InternalWindow.FocusGained += () => FocusChanged?.Invoke(isFocused = true); 
            InternalWindow.FocusLost += () => FocusChanged?.Invoke(isFocused = false); 

            InternalWindow.Closing += Closing;
            InternalWindow.Closed += () => Environment.Exit(0); 

            while (InternalWindow.Exists)
            {
                Sdl2Events.ProcessEvents();

                LatestInputSnapshot = InternalWindow.PumpEvents();

                WindowInputHandler.EarlyUpdate(); 
                
                Update?.Invoke();
            }
        }

        public static void Stop() => InternalWindow.Close();
    }
}
