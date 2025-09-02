using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using Prowl.PaperUI;

namespace OpenTKSample
{
    public class PaperTKWindow : GameWindow
    {
        private PaperRenderer _renderer;
        private Paper P;

        public PaperTKWindow(GameWindowSettings gameWindowSettings, NativeWindowSettings nativeWindowSettings) : base(gameWindowSettings, nativeWindowSettings)
        {
        }

        protected override void OnLoad()
        {
            base.OnLoad();
            _renderer = new PaperRenderer();
            _renderer.Initialize(ClientRectangle.Size.X, ClientRectangle.Size.Y);
            P = new Paper(_renderer, ClientRectangle.Size.X, ClientRectangle.Size.Y, new Prowl.Quill.FontAtlasSettings());
            Shared.PaperDemo.Initialize(P);
        }

        protected override void OnRenderFrame(FrameEventArgs args)
        {
            base.OnRenderFrame(args);

            GL.ClearColor(0.3f, 0.3f, 0.32f, 1.0f);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);

            P.BeginFrame((float)args.Time);

            Shared.PaperDemo.RenderUI();

            P.EndFrame();

            SwapBuffers();
        }

        protected override void OnResize(ResizeEventArgs e)
        {
            base.OnResize(e);

            GL.Viewport(0, 0, ClientRectangle.Size.X, ClientRectangle.Size.Y);

            P.SetResolution(ClientRectangle.Size.X, ClientRectangle.Size.Y);
            _renderer.UpdateProjection(ClientRectangle.Size.X, ClientRectangle.Size.Y);
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            PaperMouseBtn button = TranslateMouseButton(e.Button);
            P.SetPointerState(button, MouseState.X, MouseState.Y, true, false);
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            PaperMouseBtn button = TranslateMouseButton(e.Button);
            P.SetPointerState(button, MouseState.X, MouseState.Y, false, false);
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            P.SetPointerWheel(e.OffsetY);
        }

        protected override void OnMouseMove(MouseMoveEventArgs e)
        {
            base.OnMouseMove(e);
            P.SetPointerState(PaperMouseBtn.Unknown, MouseState.X, MouseState.Y, false, true);
        }

        protected override void OnKeyDown(KeyboardKeyEventArgs e)
        {
            base.OnKeyDown(e);
            P.SetKeyState(TranslateKey(e.Key), true);
        }

        protected override void OnKeyUp(KeyboardKeyEventArgs e)
        {
            base.OnKeyUp(e);
            P.SetKeyState(TranslateKey(e.Key), false);
        }

        protected override void OnTextInput(TextInputEventArgs e)
        {
            base.OnTextInput(e);
            P.AddInputCharacter(e.AsString);
        }

        private PaperMouseBtn TranslateMouseButton(OpenTK.Windowing.GraphicsLibraryFramework.MouseButton button)
        {
            return button switch {
                OpenTK.Windowing.GraphicsLibraryFramework.MouseButton.Left => PaperMouseBtn.Left,
                OpenTK.Windowing.GraphicsLibraryFramework.MouseButton.Right => PaperMouseBtn.Right,
                OpenTK.Windowing.GraphicsLibraryFramework.MouseButton.Middle => PaperMouseBtn.Middle,
                _ => PaperMouseBtn.Unknown
            };
        }

        public PaperKey TranslateKey(OpenTK.Windowing.GraphicsLibraryFramework.Keys tk)
        {
            return tk switch {
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Unknown => PaperKey.Unknown,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Space => PaperKey.Space,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Apostrophe => PaperKey.Apostrophe,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Comma => PaperKey.Comma,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Minus => PaperKey.Minus,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Period => PaperKey.Period,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Slash => PaperKey.Slash,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.D0 => PaperKey.Num0,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.D1 => PaperKey.Num1,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.D2 => PaperKey.Num2,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.D3 => PaperKey.Num3,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.D4 => PaperKey.Num4,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.D5 => PaperKey.Num5,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.D6 => PaperKey.Num6,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.D7 => PaperKey.Num7,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.D8 => PaperKey.Num8,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.D9 => PaperKey.Num9,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Semicolon => PaperKey.Semicolon,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Equal => PaperKey.Equals,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.A => PaperKey.A,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.B => PaperKey.B,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.C => PaperKey.C,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.D => PaperKey.D,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.E => PaperKey.E,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.F => PaperKey.F,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.G => PaperKey.G,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.H => PaperKey.H,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.I => PaperKey.I,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.J => PaperKey.J,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.K => PaperKey.K,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.L => PaperKey.L,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.M => PaperKey.M,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.N => PaperKey.N,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.O => PaperKey.O,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.P => PaperKey.P,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Q => PaperKey.Q,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.R => PaperKey.R,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.S => PaperKey.S,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.T => PaperKey.T,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.U => PaperKey.U,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.V => PaperKey.V,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.W => PaperKey.W,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.X => PaperKey.X,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Y => PaperKey.Y,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Z => PaperKey.Z,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.LeftBracket => PaperKey.LeftBracket,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Backslash => PaperKey.Backslash,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.RightBracket => PaperKey.RightBracket,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.GraveAccent => PaperKey.Grave,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Escape => PaperKey.Escape,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Enter => PaperKey.Enter,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Tab => PaperKey.Tab,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Backspace => PaperKey.Backspace,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Insert => PaperKey.Insert,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Delete => PaperKey.Delete,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Right => PaperKey.Right,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Left => PaperKey.Left,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Down => PaperKey.Down,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Up => PaperKey.Up,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.PageUp => PaperKey.PageUp,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.PageDown => PaperKey.PageDown,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Home => PaperKey.Home,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.End => PaperKey.End,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.CapsLock => PaperKey.CapsLock,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.ScrollLock => PaperKey.ScrollLock,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.NumLock => PaperKey.NumLock,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.PrintScreen => PaperKey.PrintScreen,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Pause => PaperKey.Pause,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.F1 => PaperKey.F1,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.F2 => PaperKey.F2,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.F3 => PaperKey.F3,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.F4 => PaperKey.F4,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.F5 => PaperKey.F5,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.F6 => PaperKey.F6,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.F7 => PaperKey.F7,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.F8 => PaperKey.F8,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.F9 => PaperKey.F9,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.F10 => PaperKey.F10,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.F11 => PaperKey.F11,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.F12 => PaperKey.F12,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.KeyPad0 => PaperKey.Keypad0,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.KeyPad1 => PaperKey.Keypad1,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.KeyPad2 => PaperKey.Keypad2,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.KeyPad3 => PaperKey.Keypad3,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.KeyPad4 => PaperKey.Keypad4,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.KeyPad5 => PaperKey.Keypad5,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.KeyPad6 => PaperKey.Keypad6,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.KeyPad7 => PaperKey.Keypad7,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.KeyPad8 => PaperKey.Keypad8,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.KeyPad9 => PaperKey.Keypad9,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.KeyPadDecimal => PaperKey.KeypadDecimal,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.KeyPadDivide => PaperKey.KeypadDivide,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.KeyPadMultiply => PaperKey.KeypadMultiply,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.KeyPadSubtract => PaperKey.KeypadMinus,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.KeyPadAdd => PaperKey.KeypadPlus,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.KeyPadEnter => PaperKey.KeypadEnter,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.KeyPadEqual => PaperKey.KeypadEquals,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.LeftShift => PaperKey.LeftShift,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.LeftControl => PaperKey.LeftControl,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.LeftAlt => PaperKey.LeftAlt,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.LeftSuper => PaperKey.LeftSuper,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.RightShift => PaperKey.RightShift,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.RightControl => PaperKey.RightControl,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.RightAlt => PaperKey.RightAlt,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.RightSuper => PaperKey.RightSuper,
                OpenTK.Windowing.GraphicsLibraryFramework.Keys.Menu => PaperKey.Menu,
                _ => PaperKey.Unknown
            };
        }
    }
}
