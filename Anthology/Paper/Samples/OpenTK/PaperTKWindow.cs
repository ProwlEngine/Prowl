using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using Prowl.PaperUI;

namespace OpenTKSample
{
    public class PaperTKWindow : GameWindow
    {
        private PaperRenderer _renderer;

        public PaperTKWindow(GameWindowSettings gameWindowSettings, NativeWindowSettings nativeWindowSettings) : base(gameWindowSettings, nativeWindowSettings)
        {
        }

        protected override void OnLoad()
        {
            base.OnLoad();
            _renderer = new PaperRenderer();
            Shared.DemoWindow.Initialize();
            Paper.Initialize(_renderer, ClientRectangle.Size.X, ClientRectangle.Size.Y);
        }

        protected override void OnRenderFrame(FrameEventArgs args)
        {
            base.OnRenderFrame(args);

            GL.ClearColor(0.3f, 0.3f, 0.32f, 1.0f);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);

            Paper.BeginFrame((float)args.Time);

            Shared.DemoWindow.RenderUI(ClientRectangle.Size.X, ClientRectangle.Size.Y);

            Paper.EndFrame();

            SwapBuffers();
        }

        protected override void OnResize(ResizeEventArgs e)
        {
            base.OnResize(e);

            GL.Viewport(0, 0, ClientRectangle.Size.X, ClientRectangle.Size.Y);

            Paper.SetResolution(ClientRectangle.Size.X, ClientRectangle.Size.Y);
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            MouseButton button = TranslateMouseButton(e.Button);
            Paper.SetPointerState(button, MouseState.X, MouseState.Y, true, false);
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            MouseButton button = TranslateMouseButton(e.Button);
            Paper.SetPointerState(button, MouseState.X, MouseState.Y, false, false);
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            Paper.SetPointerWheel(e.OffsetY);
        }

        protected override void OnMouseMove(MouseMoveEventArgs e)
        {
            base.OnMouseMove(e);
            Paper.SetPointerState(MouseButton.Unknown, MouseState.X, MouseState.Y, false, true);
        }

        protected override void OnKeyDown(KeyboardKeyEventArgs e)
        {
            base.OnKeyDown(e);
            Paper.SetKeyState(TranslateKey(e.Key), true);
        }

        protected override void OnKeyUp(KeyboardKeyEventArgs e)
        {
            base.OnKeyUp(e);
            Paper.SetKeyState(TranslateKey(e.Key), false);
        }

        protected override void OnTextInput(TextInputEventArgs e)
        {
            base.OnTextInput(e);
            Paper.AddInputCharacter(e.AsString);
        }

        private MouseButton TranslateMouseButton(OpenTK.Windowing.GraphicsLibraryFramework.MouseButton button)
        {
            return button switch {
                OpenTK.Windowing.GraphicsLibraryFramework.MouseButton.Left => MouseButton.Left,
                OpenTK.Windowing.GraphicsLibraryFramework.MouseButton.Right => MouseButton.Right,
                OpenTK.Windowing.GraphicsLibraryFramework.MouseButton.Middle => MouseButton.Middle,
                _ => MouseButton.Unknown
            };
        }

        public Key TranslateKey(OpenTK.Windowing.GraphicsLibraryFramework.Keys tk)
        {
            return tk switch {
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Unknown => Key.Unknown,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Space => Key.Space,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Apostrophe => Key.Apostrophe,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Comma => Key.Comma,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Minus => Key.Minus,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Period => Key.Period,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Slash => Key.Slash,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.D0 => Key.Num0,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.D1 => Key.Num1,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.D2 => Key.Num2,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.D3 => Key.Num3,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.D4 => Key.Num4,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.D5 => Key.Num5,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.D6 => Key.Num6,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.D7 => Key.Num7,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.D8 => Key.Num8,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.D9 => Key.Num9,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Semicolon => Key.Semicolon,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Equal => Key.Equals,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.A => Key.A,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.B => Key.B,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.C => Key.C,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.D => Key.D,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.E => Key.E,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.F => Key.F,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.G => Key.G,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.H => Key.H,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.I => Key.I,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.J => Key.J,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.K => Key.K,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.L => Key.L,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.M => Key.M,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.N => Key.N,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.O => Key.O,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.P => Key.P,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Q => Key.Q,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.R => Key.R,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.S => Key.S,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.T => Key.T,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.U => Key.U,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.V => Key.V,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.W => Key.W,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.X => Key.X,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Y => Key.Y,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Z => Key.Z,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.LeftBracket => Key.LeftBracket,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Backslash => Key.Backslash,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.RightBracket => Key.RightBracket,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.GraveAccent => Key.Grave,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Escape => Key.Escape,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Enter => Key.Return,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Tab => Key.Tab,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Backspace => Key.Backspace,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Insert => Key.Insert,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Delete => Key.Delete,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Right => Key.Right,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Left => Key.Left,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Down => Key.Down,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Up => Key.Up,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.PageUp => Key.PageUp,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.PageDown => Key.PageDown,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Home => Key.Home,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.End => Key.End,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.CapsLock => Key.CapsLock,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.ScrollLock => Key.ScrollLock,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.NumLock => Key.NumLock,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.PrintScreen => Key.PrintScreen,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Pause => Key.Pause,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.F1 => Key.F1,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.F2 => Key.F2,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.F3 => Key.F3,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.F4 => Key.F4,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.F5 => Key.F5,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.F6 => Key.F6,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.F7 => Key.F7,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.F8 => Key.F8,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.F9 => Key.F9,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.F10 => Key.F10,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.F11 => Key.F11,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.F12 => Key.F12,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.KeyPad0 => Key.Keypad0,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.KeyPad1 => Key.Keypad1,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.KeyPad2 => Key.Keypad2,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.KeyPad3 => Key.Keypad3,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.KeyPad4 => Key.Keypad4,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.KeyPad5 => Key.Keypad5,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.KeyPad6 => Key.Keypad6,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.KeyPad7 => Key.Keypad7,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.KeyPad8 => Key.Keypad8,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.KeyPad9 => Key.Keypad9,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.KeyPadDecimal => Key.KeypadPeriod,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.KeyPadDivide => Key.KeypadDivide,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.KeyPadMultiply => Key.KeypadMultiply,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.KeyPadSubtract => Key.KeypadMinus,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.KeyPadAdd => Key.KeypadPlus,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.KeyPadEnter => Key.KeypadEnter,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.KeyPadEqual => Key.KeypadEquals,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.LeftShift => Key.LeftShift,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.LeftControl => Key.LeftControl,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.LeftAlt => Key.LeftAlt,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.LeftSuper => Key.LeftSuper,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.RightShift => Key.RightShift,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.RightControl => Key.RightControl,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.RightAlt => Key.RightAlt,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.RightSuper => Key.RightSuper,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Menu => Key.Menu,
                _ => Key.Unknown
            };
        }
    }
}
