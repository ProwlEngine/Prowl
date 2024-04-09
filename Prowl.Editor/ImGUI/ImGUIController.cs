using Hexa.NET.ImGui;
using Hexa.NET.ImGuizmo;
using Hexa.NET.ImNodes;
using Hexa.NET.ImPlot;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Rendering.OpenGL;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using System.Reflection;

namespace Prowl.Editor.ImGUI
{
    public class ImGUIController : IDisposable
    {
        private GL _gl;
        private IView _view;
        private IInputContext _input;
        private bool _frameBegun;
        private readonly List<char> _pressedChars = new List<char>();
        private IKeyboard _keyboard;

        private ImNodesContextPtr nodesContext;
        private ImPlotContextPtr plotContext;

        private int _attribLocationTex;
        private int _attribLocationProjMtx;
        private int _attribLocationVtxPos;
        private int _attribLocationVtxUV;
        private int _attribLocationVtxColor;
        private uint _vboHandle;
        private uint _elementsHandle;
        private uint _vertexArrayObject;

        private Texture2D _fontTexture;
        private GraphicsProgram _shader;

        private int _windowWidth;
        private int _windowHeight;

        public ImGuiContextPtr Context;

        /// <summary>
        /// Constructs a new ImGuiController.
        /// </summary>
        public ImGUIController(GL gl, IView view, IInputContext input) : this(gl, view, input, null)
        {
        }

        /// <summary>
        /// Constructs a new ImGuiController with font configuration and onConfigure Action.
        /// </summary>
        public ImGUIController(GL gl, IView view, IInputContext input, Action onConfigureIO = null)
        {
            Init(gl, view, input);

            var io = ImGui.GetIO();

            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Prowl.Editor.EmbeddedResources.font.ttf"))
            {
                string tempFilePath = Path.Combine(Path.GetTempPath(), "font.ttf");
                using (FileStream fileStream = File.Create(tempFilePath))
                    stream.CopyTo(fileStream);
                try
                {
                    io.Fonts.AddFontFromFileTTF(tempFilePath, 19);
                    AddEmbeddedFont(FontAwesome6.FontIconFileNameFAR, FontAwesome6.IconMin, FontAwesome6.IconMax);
                    AddEmbeddedFont(FontAwesome6.FontIconFileNameFAS, FontAwesome6.IconMin, FontAwesome6.IconMax);
                }
                finally
                {
                    File.Delete(tempFilePath);
                }
            }
            onConfigureIO?.Invoke();

            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;

            CreateDeviceResources();

            SetPerFrameImGuiData(1f / 60f);

            BeginFrame();
        }

        public void MakeCurrent()
        {
            ImGui.SetCurrentContext(Context);
        }

        private void Init(GL gl, IView view, IInputContext input)
        {
            _gl = gl;
            _view = view;
            _input = input;
            _windowWidth = view.Size.X;
            _windowHeight = view.Size.Y;

            Context = ImGui.CreateContext();
            ImGui.SetCurrentContext(Context);
            ImGui.StyleColorsDark();

            ImGuizmo.SetImGuiContext(Context);
            ImGuizmo.AllowAxisFlip(false);
            ImPlot.SetImGuiContext(Context);
            ImNodes.SetImGuiContext(Context);

            nodesContext = ImNodes.CreateContext();
            ImNodes.SetCurrentContext(nodesContext);
            ImNodes.StyleColorsDark(ImNodes.GetStyle());

            plotContext = ImPlot.CreateContext();
            ImPlot.SetCurrentContext(plotContext);
            ImPlot.StyleColorsDark(ImPlot.GetStyle());

        }

        private void BeginFrame()
        {
            ImGui.GetIO().ConfigFlags |= ImGuiConfigFlags.DockingEnable;
            ImGui.NewFrame();
            ImGuizmo.BeginFrame();
            _frameBegun = true;
            _keyboard = _input.Keyboards[0];
            _view.Resize += WindowResized;
            _keyboard.KeyChar += OnKeyChar;
        }

        private void OnKeyChar(IKeyboard arg1, char arg2)
        {
            _pressedChars.Add(arg2);
        }

        unsafe void AddEmbeddedFont(string name, ushort min, ushort max, float fontSize = 19 * 2.0f / 3.0f)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Prowl.Editor.EmbeddedResources.{name}"))
            {
                if (stream != null)
                {
                    string tempFilePath = Path.Combine(Path.GetTempPath(), "tempfont.ttf");
                    using (FileStream fileStream = File.Create(tempFilePath))
                        stream.CopyTo(fileStream);
                    try
                    {
                        ImFontConfigPtr fontConfig = ImGui.ImFontConfig();
                        fontConfig.MergeMode = true;
                        fontConfig.PixelSnapH = true;
                        fontConfig.GlyphMinAdvanceX = fontSize;
                        //var rangeHandle = GCHandle.Alloc(new ushort[] { min, max, 0 }, GCHandleType.Pinned);
                        char[] ranges = [(char)min, (char)max];
                        ImGui.GetIO().Fonts.AddFontFromFileTTF(tempFilePath, fontSize, fontConfig, ref ranges[0]);
                        //rangeHandle.Free();
                    }
                    finally
                    {
                        File.Delete(tempFilePath);
                    }
                }
                else
                {
                    Debug.LogWarning("Failed to load AwesomeFont.");
                }
            }
        }

        private void WindowResized(Vector2D<int> size)
        {
            _windowWidth = size.X;
            _windowHeight = size.Y;
        }

        /// <summary>
        /// Renders the ImGui draw list data.
        /// This method requires a <see cref="GraphicsDevice"/> because it may create new DeviceBuffers if the size of vertex
        /// or index data has increased beyond the capacity of the existing buffers.
        /// A <see cref="CommandList"/> is needed to submit drawing and resource update commands.
        /// </summary>
        public void Render()
        {
            if (_frameBegun)
            {
                var oldCtx = ImGui.GetCurrentContext();

                if (oldCtx != Context)
                {
                    ImGui.SetCurrentContext(Context);
                }

                _frameBegun = false;
                ImGui.Render();
                RenderImDrawData(ImGui.GetDrawData());

                if (oldCtx != Context)
                {
                    ImGui.SetCurrentContext(oldCtx);
                }
            }
        }

        /// <summary>
        /// Updates ImGui input and IO configuration state.
        /// </summary>
        public void Update(float deltaSeconds)
        {
            var oldCtx = ImGui.GetCurrentContext();

            if (oldCtx != Context)
            {
                ImGui.SetCurrentContext(Context);
            }

            if (_frameBegun)
            {
                ImGui.Render();
            }

            SetPerFrameImGuiData(deltaSeconds);
            UpdateImGuiInput();

            _frameBegun = true;
            ImGui.NewFrame();
            ImGuizmo.BeginFrame();

            if (oldCtx != Context)
            {
                ImGui.SetCurrentContext(oldCtx);
                ImGuizmo.SetImGuiContext(oldCtx);
                ImPlot.SetImGuiContext(oldCtx);
                ImNodes.SetImGuiContext(oldCtx);
            }
        }

        /// <summary>
        /// Sets per-frame data based on the associated window.
        /// This is called by Update(float).
        /// </summary>
        private void SetPerFrameImGuiData(float deltaSeconds)
        {
            var io = ImGui.GetIO();
            io.DisplaySize = new Vector2(_windowWidth, _windowHeight);

            if (_windowWidth > 0 && _windowHeight > 0)
            {
                io.DisplayFramebufferScale = new Vector2(_view.FramebufferSize.X / _windowWidth,
                    _view.FramebufferSize.Y / _windowHeight);
            }

            io.DeltaTime = deltaSeconds; // DeltaTime is in seconds.
        }

        private void UpdateImGuiInput()
        {
            var io = ImGui.GetIO();

            var keyState = _input.Keyboards[0];

            io.AddMouseButtonEvent(0, _input.Mice[0].IsButtonPressed(MouseButton.Left));
            io.AddMouseButtonEvent(1, _input.Mice[0].IsButtonPressed(MouseButton.Right));
            io.AddMouseButtonEvent(2, _input.Mice[0].IsButtonPressed(MouseButton.Middle));

            // io.MousePos = _input.Mice[0].Position;
            io.AddMousePosEvent(_input.Mice[0].Position.X, _input.Mice[0].Position.Y);

            var wheel = _input.Mice[0].ScrollWheels[0];
            // io.MouseWheel = wheel.Y;
            // io.MouseWheelH = wheel.X;
            io.AddMouseWheelEvent(wheel.X, wheel.Y);

            void KeyE(ImGuiKey imguiKey, Key silkKey) => io.AddKeyEvent(imguiKey, keyState.IsKeyPressed(silkKey));

            KeyE(ImGuiKey.Tab, Key.Tab);
            KeyE(ImGuiKey.LeftArrow, Key.Left);
            KeyE(ImGuiKey.RightArrow, Key.Right);
            KeyE(ImGuiKey.UpArrow, Key.Up);
            KeyE(ImGuiKey.DownArrow, Key.Down);
            KeyE(ImGuiKey.PageUp, Key.PageUp);
            KeyE(ImGuiKey.PageDown, Key.PageDown);
            KeyE(ImGuiKey.Home, Key.Home);
            KeyE(ImGuiKey.End, Key.End);
            KeyE(ImGuiKey.Delete, Key.Delete);
            KeyE(ImGuiKey.Backspace, Key.Backspace);
            KeyE(ImGuiKey.Enter, Key.Enter);
            KeyE(ImGuiKey.Escape, Key.Escape);
            KeyE(ImGuiKey.A, Key.A); KeyE(ImGuiKey.B, Key.B); KeyE(ImGuiKey.C, Key.C);
            KeyE(ImGuiKey.D, Key.D); KeyE(ImGuiKey.E, Key.E); KeyE(ImGuiKey.F, Key.F);
            KeyE(ImGuiKey.G, Key.G); KeyE(ImGuiKey.H, Key.H); KeyE(ImGuiKey.I, Key.I);
            KeyE(ImGuiKey.J, Key.J); KeyE(ImGuiKey.K, Key.K); KeyE(ImGuiKey.L, Key.L);
            KeyE(ImGuiKey.M, Key.M); KeyE(ImGuiKey.N, Key.N); KeyE(ImGuiKey.O, Key.O);
            KeyE(ImGuiKey.P, Key.P); KeyE(ImGuiKey.Q, Key.Q); KeyE(ImGuiKey.R, Key.R);
            KeyE(ImGuiKey.S, Key.S); KeyE(ImGuiKey.T, Key.T); KeyE(ImGuiKey.U, Key.U);
            KeyE(ImGuiKey.V, Key.V); KeyE(ImGuiKey.W, Key.W); KeyE(ImGuiKey.X, Key.X);
            KeyE(ImGuiKey.Y, Key.Y); KeyE(ImGuiKey.Z, Key.Z);

            foreach (var c in _pressedChars)
            {
                io.AddInputCharacter(c);
            }
            _pressedChars.Clear();

            io.KeyCtrl = keyState.IsKeyPressed(Key.ControlLeft) || keyState.IsKeyPressed(Key.ControlRight);
            io.KeyAlt = keyState.IsKeyPressed(Key.AltLeft) || keyState.IsKeyPressed(Key.AltRight);
            io.KeyShift = keyState.IsKeyPressed(Key.ShiftLeft) || keyState.IsKeyPressed(Key.ShiftRight);
            io.KeySuper = keyState.IsKeyPressed(Key.SuperLeft) || keyState.IsKeyPressed(Key.SuperRight);
        }

        private unsafe void SetupRenderState(ImDrawDataPtr drawDataPtr, int framebufferWidth, int framebufferHeight)
        {
            // Setup render state: alpha-blending enabled, no face culling, no depth testing, scissor enabled, polygon fill
            _gl.Enable(GLEnum.Blend);
            _gl.BlendEquation(GLEnum.FuncAdd);
            _gl.BlendFuncSeparate(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha, GLEnum.One, GLEnum.OneMinusSrcAlpha);
            _gl.Disable(GLEnum.CullFace);
            _gl.Disable(GLEnum.DepthTest);
            _gl.Disable(GLEnum.StencilTest);
            _gl.Enable(GLEnum.ScissorTest);
#if !GLES && !LEGACY
            _gl.Disable(GLEnum.PrimitiveRestart);
            _gl.PolygonMode(GLEnum.FrontAndBack, GLEnum.Fill);
#endif

            float L = drawDataPtr.DisplayPos.X;
            float R = drawDataPtr.DisplayPos.X + drawDataPtr.DisplaySize.X;
            float T = drawDataPtr.DisplayPos.Y;
            float B = drawDataPtr.DisplayPos.Y + drawDataPtr.DisplaySize.Y;

            Span<float> orthoProjection = stackalloc float[] {
                2.0f / (R - L), 0.0f, 0.0f, 0.0f,
                0.0f, 2.0f / (T - B), 0.0f, 0.0f,
                0.0f, 0.0f, -1.0f, 0.0f,
                (R + L) / (L - R), (T + B) / (B - T), 0.0f, 1.0f,
            };

            Graphics.Device.SetUniformI(_shader, _attribLocationTex, 0);
            Graphics.Device.SetUniformMatrix(_shader, _attribLocationProjMtx, 1, false, in orthoProjection[0]);

            _gl.BindSampler(0, 0);

            // Setup desired GL state
            // Recreate the VAO every time (this is to easily allow multiple GL contexts to be rendered to. VAO are not shared among GL contexts)
            // The renderer would actually work without any VAO bound, but then our VertexAttrib calls would overwrite the default one currently bound.
            _vertexArrayObject = _gl.GenVertexArray();
            _gl.BindVertexArray(_vertexArrayObject);

            // Bind vertex/index buffers and setup attributes for ImDrawVert
            _gl.BindBuffer(GLEnum.ArrayBuffer, _vboHandle);
            _gl.BindBuffer(GLEnum.ElementArrayBuffer, _elementsHandle);
            _gl.EnableVertexAttribArray((uint)_attribLocationVtxPos);
            _gl.EnableVertexAttribArray((uint)_attribLocationVtxUV);
            _gl.EnableVertexAttribArray((uint)_attribLocationVtxColor);
            _gl.VertexAttribPointer((uint)_attribLocationVtxPos, 2, GLEnum.Float, false, (uint)sizeof(ImDrawVert), (void*)0);
            _gl.VertexAttribPointer((uint)_attribLocationVtxUV, 2, GLEnum.Float, false, (uint)sizeof(ImDrawVert), (void*)8);
            _gl.VertexAttribPointer((uint)_attribLocationVtxColor, 4, GLEnum.UnsignedByte, true, (uint)sizeof(ImDrawVert), (void*)16);
        }

        private unsafe void RenderImDrawData(ImDrawDataPtr drawDataPtr)
        {
            int framebufferWidth = (int)(drawDataPtr.DisplaySize.X * drawDataPtr.FramebufferScale.X);
            int framebufferHeight = (int)(drawDataPtr.DisplaySize.Y * drawDataPtr.FramebufferScale.Y);
            if (framebufferWidth <= 0 || framebufferHeight <= 0)
                return;

            // Backup GL state
            _gl.GetInteger(GLEnum.ActiveTexture, out int lastActiveTexture);
            _gl.ActiveTexture(GLEnum.Texture0);

            _gl.GetInteger(GLEnum.TextureBinding2D, out int lastTexture);

            _gl.GetInteger(GLEnum.SamplerBinding, out int lastSampler);

            _gl.GetInteger(GLEnum.ArrayBufferBinding, out int lastArrayBuffer);
            _gl.GetInteger(GLEnum.VertexArrayBinding, out int lastVertexArrayObject);

#if !GLES
            Span<int> lastPolygonMode = stackalloc int[2];
            _gl.GetInteger(GLEnum.PolygonMode, lastPolygonMode);
#endif

            Span<int> lastScissorBox = stackalloc int[4];
            _gl.GetInteger(GLEnum.ScissorBox, lastScissorBox);

            _gl.GetInteger(GLEnum.BlendSrcRgb, out int lastBlendSrcRgb);
            _gl.GetInteger(GLEnum.BlendDstRgb, out int lastBlendDstRgb);

            _gl.GetInteger(GLEnum.BlendSrcAlpha, out int lastBlendSrcAlpha);
            _gl.GetInteger(GLEnum.BlendDstAlpha, out int lastBlendDstAlpha);

            _gl.GetInteger(GLEnum.BlendEquationRgb, out int lastBlendEquationRgb);
            _gl.GetInteger(GLEnum.BlendEquationAlpha, out int lastBlendEquationAlpha);

            bool lastEnableBlend = _gl.IsEnabled(GLEnum.Blend);
            bool lastEnableCullFace = _gl.IsEnabled(GLEnum.CullFace);
            bool lastEnableDepthTest = _gl.IsEnabled(GLEnum.DepthTest);
            bool lastEnableStencilTest = _gl.IsEnabled(GLEnum.StencilTest);
            bool lastEnableScissorTest = _gl.IsEnabled(GLEnum.ScissorTest);

#if !GLES && !LEGACY
            bool lastEnablePrimitiveRestart = _gl.IsEnabled(GLEnum.PrimitiveRestart);
#endif

            SetupRenderState(drawDataPtr, framebufferWidth, framebufferHeight);

            // Will project scissor/clipping rectangles into framebuffer space
            Vector2 clipOff = drawDataPtr.DisplayPos;         // (0,0) unless using multi-viewports
            Vector2 clipScale = drawDataPtr.FramebufferScale; // (1,1) unless using retina display which are often (2,2)

            // Render command lists
            for (int n = 0; n < drawDataPtr.CmdListsCount; n++)
            {
                ImDrawListPtr cmdListPtr = drawDataPtr.CmdLists.Data[n];

                // Upload vertex/index buffers

                _gl.BufferData(GLEnum.ArrayBuffer, (nuint)(cmdListPtr.VtxBuffer.Size * sizeof(ImDrawVert)), (void*)cmdListPtr.VtxBuffer.Data, GLEnum.StreamDraw);
                _gl.BufferData(GLEnum.ElementArrayBuffer, (nuint)(cmdListPtr.IdxBuffer.Size * sizeof(ushort)), (void*)cmdListPtr.IdxBuffer.Data, GLEnum.StreamDraw);

                for (int cmd_i = 0; cmd_i < cmdListPtr.CmdBuffer.Size; cmd_i++)
                {
                    var cmdPtr = cmdListPtr.CmdBuffer.Data[cmd_i];

                    if (cmdPtr.UserCallback != null)
                    {
                        throw new NotImplementedException();
                    }
                    else
                    {
                        Vector4 clipRect;
                        clipRect.x = (cmdPtr.ClipRect.X - clipOff.x) * clipScale.x;
                        clipRect.y = (cmdPtr.ClipRect.Y - clipOff.y) * clipScale.y;
                        clipRect.z = (cmdPtr.ClipRect.Z - clipOff.x) * clipScale.x;
                        clipRect.w = (cmdPtr.ClipRect.W - clipOff.y) * clipScale.y;

                        if (clipRect.x < framebufferWidth && clipRect.y < framebufferHeight && clipRect.z >= 0.0f && clipRect.w >= 0.0f)
                        {
                            // Apply scissor/clipping rectangle
                            _gl.Scissor((int)clipRect.x, (int)(framebufferHeight - clipRect.w), (uint)(clipRect.z - clipRect.x), (uint)(clipRect.w - clipRect.y));

                            // Bind texture, Draw
                            _gl.BindTexture(GLEnum.Texture2D, (uint)cmdPtr.TextureId.Handle);

                            _gl.DrawElementsBaseVertex(GLEnum.Triangles, cmdPtr.ElemCount, GLEnum.UnsignedShort, (void*)(cmdPtr.IdxOffset * sizeof(ushort)), (int)cmdPtr.VtxOffset);
                        }
                    }
                }
            }

            // Destroy the temporary VAO
            _gl.DeleteVertexArray(_vertexArrayObject);
            _vertexArrayObject = 0;

            // Restore modified GL state
            _gl.BindTexture(GLEnum.Texture2D, (uint)lastTexture);

            _gl.BindSampler(0, (uint)lastSampler);
            _gl.ActiveTexture((GLEnum)lastActiveTexture);
            _gl.BindVertexArray((uint)lastVertexArrayObject);

            _gl.BindBuffer(GLEnum.ArrayBuffer, (uint)lastArrayBuffer);
            _gl.BlendEquationSeparate((GLEnum)lastBlendEquationRgb, (GLEnum)lastBlendEquationAlpha);
            _gl.BlendFuncSeparate((GLEnum)lastBlendSrcRgb, (GLEnum)lastBlendDstRgb, (GLEnum)lastBlendSrcAlpha, (GLEnum)lastBlendDstAlpha);

            if (lastEnableBlend)
            {
                _gl.Enable(GLEnum.Blend);
            }
            else
            {
                _gl.Disable(GLEnum.Blend);
            }

            if (lastEnableCullFace)
            {
                _gl.Enable(GLEnum.CullFace);
            }
            else
            {
                _gl.Disable(GLEnum.CullFace);
            }

            if (lastEnableDepthTest)
            {
                _gl.Enable(GLEnum.DepthTest);
            }
            else
            {
                _gl.Disable(GLEnum.DepthTest);
            }
            if (lastEnableStencilTest)
            {
                _gl.Enable(GLEnum.StencilTest);
            }
            else
            {
                _gl.Disable(GLEnum.StencilTest);
            }

            if (lastEnableScissorTest)
            {
                _gl.Enable(GLEnum.ScissorTest);
            }
            else
            {
                _gl.Disable(GLEnum.ScissorTest);
            }

#if !GLES && !LEGACY
            if (lastEnablePrimitiveRestart)
            {
                _gl.Enable(GLEnum.PrimitiveRestart);
            }
            else
            {
                _gl.Disable(GLEnum.PrimitiveRestart);
            }

            _gl.PolygonMode(GLEnum.FrontAndBack, (GLEnum)lastPolygonMode[0]);
#endif

            _gl.Scissor(lastScissorBox[0], lastScissorBox[1], (uint)lastScissorBox[2], (uint)lastScissorBox[3]);
        }

        private void CreateDeviceResources()
        {
            // Backup GL state

            _gl.GetInteger(GLEnum.TextureBinding2D, out int lastTexture);
            _gl.GetInteger(GLEnum.ArrayBufferBinding, out int lastArrayBuffer);
            _gl.GetInteger(GLEnum.VertexArrayBinding, out int lastVertexArray);

            string vertexSource =
        @"#version 330
        layout (location = 0) in vec2 Position;
        layout (location = 1) in vec2 UV;
        layout (location = 2) in vec4 Color;
        uniform mat4 ProjMtx;
        out vec2 Frag_UV;
        out vec4 Frag_Color;
        void main()
        {
            Frag_UV = UV;
            Frag_Color = Color;
            gl_Position = ProjMtx * vec4(Position.xy,0,1);
        }";


            string fragmentSource =
        @"#version 330
        in vec2 Frag_UV;
        in vec4 Frag_Color;
        uniform sampler2D Texture;
        layout (location = 0) out vec4 Out_Color;
        void main()
        {
            Out_Color = Frag_Color * texture(Texture, Frag_UV.st);
        }";

            _shader = Graphics.Device.CompileProgram(fragmentSource, vertexSource, "");

            _attribLocationTex = Graphics.Device.GetUniformLocation(_shader, "Texture");
            _attribLocationProjMtx = Graphics.Device.GetUniformLocation(_shader, "ProjMtx");
            _attribLocationVtxPos = Graphics.Device.GetAttribLocation(_shader, "Position");
            _attribLocationVtxUV = Graphics.Device.GetAttribLocation(_shader, "UV");
            _attribLocationVtxColor = Graphics.Device.GetAttribLocation(_shader, "Color");

            _vboHandle = _gl.GenBuffer();
            _elementsHandle = _gl.GenBuffer();

            RecreateFontDeviceTexture();

            // Restore modified GL state
            _gl.BindTexture(GLEnum.Texture2D, (uint)lastTexture);
            _gl.BindBuffer(GLEnum.ArrayBuffer, (uint)lastArrayBuffer);

            _gl.BindVertexArray((uint)lastVertexArray);
        }

        /// <summary>
        /// Creates the texture used to render text.
        /// </summary>
        private unsafe void RecreateFontDeviceTexture()
        {
            // Build texture atlas
            var io = ImGui.GetIO();
            byte* pixels;
            int width;
            int height;
            io.Fonts.GetTexDataAsRGBA32(&pixels, &width, &height);   // Load as RGBA 32-bit (75% of the memory is wasted, but default font is so small) because it is more likely to be compatible with user's existing shaders. If your ImTextureId represent a higher-level concept than just a GL texture id, consider calling GetTexDataAsAlpha8() instead to save on GPU memory.

            // Upload texture to graphics system
            _gl.GetInteger(GLEnum.TextureBinding2D, out int lastTexture);

            _fontTexture = new Texture2D((uint)width, (uint)height, false, TextureImageFormat.Color4b);
            Graphics.Device.TexSubImage2D(_fontTexture.Handle, 0, 0, 0, (uint)width, (uint)height, pixels);
            _fontTexture.SetTextureFilters(TextureMin.Linear, TextureMag.Linear);

            // Store our identifier
            io.Fonts.SetTexID((IntPtr)(_fontTexture.Handle as GLTexture).Handle);

            // Restore state
            _gl.BindTexture(GLEnum.Texture2D, (uint)lastTexture);
        }

        /// <summary>
        /// Frees all graphics resources used by the renderer.
        /// </summary>
        public void Dispose()
        {
            _view.Resize -= WindowResized;
            _keyboard.KeyChar -= OnKeyChar;

            _gl.DeleteBuffer(_vboHandle);
            _gl.DeleteBuffer(_elementsHandle);
            _gl.DeleteVertexArray(_vertexArrayObject);

            _fontTexture.Dispose();
            _shader.Dispose();

            ImNodes.DestroyContext(nodesContext);
            ImGui.DestroyContext(Context);
        }
    }
}
