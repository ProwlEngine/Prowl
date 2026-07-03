using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTKSample;
using Prowl.PaperUI;

namespace OrigamiSample
{
    public class PaperTKWindow : GameWindow
    {
        private PaperRenderer _renderer;
        private Paper _paper;
        private NebulaEditor _editor;

        private readonly string _shotPath;
        private readonly int _shotFrames;
        private readonly bool _textCompare;
        private int _frame;

        public PaperTKWindow(GameWindowSettings gws, NativeWindowSettings nws, string shotPath, int shotFrames, int initialCat, bool textCompare = false)
            : base(gws, nws)
        {
            _shotPath = shotPath;
            _shotFrames = shotFrames;
            _textCompare = textCompare;
            _initialCat = initialCat;
        }

        private readonly int _initialCat;

        protected override void OnLoad()
        {
            base.OnLoad();
            _renderer = new PaperRenderer();
            _renderer.Initialize(FramebufferSize.X, FramebufferSize.Y);
            _paper = new Paper(_renderer, ClientSize.X, ClientSize.Y, new Prowl.Quill.FontAtlasSettings());
            _editor = new NebulaEditor(_paper);
            _editor.SetPlaygroundCategory(_initialCat);
        }

        protected override void OnRenderFrame(FrameEventArgs args)
        {
            base.OnRenderFrame(args);

            GL.ClearColor(0.024f, 0.016f, 0.035f, 1.0f);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);

            // Optional hover simulation for screenshot verification (env PROWL_HOVER="x,y")
            var hoverEnv = Environment.GetEnvironmentVariable("PROWL_HOVER");
            if (hoverEnv != null)
            {
                var parts = hoverEnv.Split(',');
                if (parts.Length == 2 && float.TryParse(parts[0], out var hx) && float.TryParse(parts[1], out var hy))
                    _paper.SetPointerState(PaperMouseBtn.Unknown, hx, hy, false, true);
            }

            // Optional click simulation for screenshotting popovers: PROWL_CLICK="x,y" clicks once.
            var clickEnv = Environment.GetEnvironmentVariable("PROWL_CLICK");
            if (clickEnv != null)
            {
                var cp = clickEnv.Split(',');
                if (cp.Length == 2 && float.TryParse(cp[0], out var qx) && float.TryParse(cp[1], out var qy))
                {
                    if (_frame == 3) _paper.SetPointerState(PaperMouseBtn.Left, qx, qy, true, false);
                    else if (_frame == 4) _paper.SetPointerState(PaperMouseBtn.Left, qx, qy, false, false);
                    else if (hoverEnv == null) _paper.SetPointerState(PaperMouseBtn.Unknown, qx, qy, false, true);
                    // else: after the click, let PROWL_HOVER own the pointer (click-then-hover testing).
                }
            }

            // Optional drag simulation: PROWL_DRAG="x1,y1,x2,y2" presses at (x1,y1), drags to (x2,y2), releases.
            var dragEnv = Environment.GetEnvironmentVariable("PROWL_DRAG");
            if (dragEnv != null)
            {
                var dp = dragEnv.Split(',');
                if (dp.Length == 4 && float.TryParse(dp[0], out var ax) && float.TryParse(dp[1], out var ay)
                    && float.TryParse(dp[2], out var bx) && float.TryParse(dp[3], out var by))
                {
                    int downF = 3, upF = Math.Max(downF + 4, _shotFrames - 2);
                    int reachF = upF - 2;  // reach the target early, then hold it so hover settles before release
                    if (_frame < downF) _paper.SetPointerState(PaperMouseBtn.Unknown, ax, ay, false, true);
                    else if (_frame == downF) _paper.SetPointerState(PaperMouseBtn.Left, ax, ay, true, false);
                    else if (_frame < upF)
                    {
                        float t = Math.Min(1f, (float)(_frame - downF) / Math.Max(1, reachF - downF));
                        _paper.SetPointerState(PaperMouseBtn.Left, ax + (bx - ax) * t, ay + (by - ay) * t, true, true);
                    }
                    else if (_frame == upF) _paper.SetPointerState(PaperMouseBtn.Left, bx, by, false, false);
                    else _paper.SetPointerState(PaperMouseBtn.Unknown, bx, by, false, true);
                }
            }

            // Optional right-click simulation for context menus: PROWL_RCLICK="x,y" right-clicks once.
            var rclickEnv = Environment.GetEnvironmentVariable("PROWL_RCLICK");
            if (rclickEnv != null)
            {
                var cp = rclickEnv.Split(',');
                if (cp.Length == 2 && float.TryParse(cp[0], out var qx) && float.TryParse(cp[1], out var qy))
                {
                    if (_frame == 3) _paper.SetPointerState(PaperMouseBtn.Right, qx, qy, true, false);
                    else if (_frame == 4) _paper.SetPointerState(PaperMouseBtn.Right, qx, qy, false, false);
                    else _paper.SetPointerState(PaperMouseBtn.Unknown, qx, qy, false, true);
                }
            }

            float dpiScale = (float)FramebufferSize.X / ClientSize.X;
            _paper.BeginFrame((float)args.Time, dpiScale);
            if (_textCompare) TextCompare.Render(_paper, ClientSize.X, ClientSize.Y);
            else _editor.Render(ClientSize.X, ClientSize.Y);
            _paper.EndFrame();

            if (_shotPath != null && ++_frame >= _shotFrames)
            {
                CaptureScreenshot(_shotPath);
                Close();
                return;
            }

            SwapBuffers();
        }

        private void CaptureScreenshot(string path)
        {
            int w = FramebufferSize.X, h = FramebufferSize.Y;
            var pixels = new byte[w * h * 4];
            GL.ReadBuffer(ReadBufferMode.Back);
            GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
            GL.ReadPixels(0, 0, w, h, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);

            // OpenGL origin is bottom-left; flip vertically for image output.
            var flipped = new byte[pixels.Length];
            int stride = w * 4;
            for (int y = 0; y < h; y++)
                Array.Copy(pixels, y * stride, flipped, (h - 1 - y) * stride, stride);

            using var fs = File.OpenWrite(path);
            var writer = new StbImageWriteSharp.ImageWriter();
            writer.WritePng(flipped, w, h, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, fs);
            Console.WriteLine($"Saved screenshot: {path} ({w}x{h})");
        }

        protected override void OnResize(ResizeEventArgs e)
        {
            base.OnResize(e);
            GL.Viewport(0, 0, FramebufferSize.X, FramebufferSize.Y);
            _paper.SetResolution(ClientSize.X, ClientSize.Y);
            _renderer.UpdateProjection(FramebufferSize.X, FramebufferSize.Y);
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            _paper.SetPointerState(Translate(e.Button), MouseState.X, MouseState.Y, true, false);
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            _paper.SetPointerState(Translate(e.Button), MouseState.X, MouseState.Y, false, false);
        }

        protected override void OnMouseMove(MouseMoveEventArgs e)
        {
            base.OnMouseMove(e);
            _paper.SetPointerState(PaperMouseBtn.Unknown, MouseState.X, MouseState.Y, false, true);
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            _paper.SetPointerWheel(e.OffsetY);
        }

        // ── Keyboard + text input ────────────────────────────────────────────
        protected override void OnKeyDown(KeyboardKeyEventArgs e)
        {
            base.OnKeyDown(e);
            var pk = MapKey(e.Key);
            if (pk != PaperKey.Unknown) _paper.SetKeyState(pk, true);
        }

        protected override void OnKeyUp(KeyboardKeyEventArgs e)
        {
            base.OnKeyUp(e);
            var pk = MapKey(e.Key);
            if (pk != PaperKey.Unknown) _paper.SetKeyState(pk, false);
        }

        protected override void OnTextInput(TextInputEventArgs e)
        {
            base.OnTextInput(e);
            _paper.AddInputCharacter(e.AsString);
        }

        // OpenTK's Keys enum names line up with PaperKey for letters/nav/modifiers; only the
        // number row (D0..D9 -> Num0..Num9) needs remapping.
        private static PaperKey MapKey(OpenTK.Windowing.GraphicsLibraryFramework.Keys k)
        {
            string s = k.ToString();
            if (s.Length == 2 && s[0] == 'D' && s[1] >= '0' && s[1] <= '9')
                s = "Num" + s[1];
            return Enum.TryParse<PaperKey>(s, out var pk) ? pk : PaperKey.Unknown;
        }

        private static PaperMouseBtn Translate(OpenTK.Windowing.GraphicsLibraryFramework.MouseButton b) => b switch
        {
            OpenTK.Windowing.GraphicsLibraryFramework.MouseButton.Left => PaperMouseBtn.Left,
            OpenTK.Windowing.GraphicsLibraryFramework.MouseButton.Right => PaperMouseBtn.Right,
            OpenTK.Windowing.GraphicsLibraryFramework.MouseButton.Middle => PaperMouseBtn.Middle,
            _ => PaperMouseBtn.Unknown
        };
    }
}
