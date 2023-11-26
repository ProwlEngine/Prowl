using Prowl.Icons;
using HexaEngine.ImGuiNET;
using HexaEngine.ImGuizmoNET;
using Raylib_cs;
using System;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Prowl.Runtime.ImGUI
{
    /// <summary>
    /// ImGui controller using Raylib-cs
    /// </summary>
    public class ImGUIController : IDisposable
    {
        ImGuiContextPtr context;
        Texture2D fontTexture;
        Vector2 scaleFactor = Vector2.One;

        public ImGUIController()
        {
            context = ImGui.CreateContext();
            ImGui.SetCurrentContext(context);
        }

        public void Dispose()
        {
            ImGui.DestroyContext(context);
            Raylib.UnloadTexture(fontTexture);
        }

        /// <summary>
        /// Creates a texture and loads the font data from ImGui.
        /// </summary>
        public void Load(int width, int height)
        {
            ImGuiIOPtr io = ImGui.GetIO();
            //io.Fonts.AddFontDefault();

            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Prowl.Runtime.EmbeddedResources.font.ttf"))
            {
                string tempFilePath = Path.Combine(Path.GetTempPath(), "font.ttf");
                using (FileStream fileStream = File.Create(tempFilePath))
                    stream.CopyTo(fileStream);
                try
                {
                    io.Fonts.AddFontFromFileTTF(tempFilePath, 17);
                    AddEmbeddedFont(FontAwesome6.FontIconFileNameFAR, FontAwesome6.IconMin, FontAwesome6.IconMax);
                    AddEmbeddedFont(FontAwesome6.FontIconFileNameFAS, FontAwesome6.IconMin, FontAwesome6.IconMax);
                }
                finally
                {
                    File.Delete(tempFilePath);
                }
            }

            Resize(width, height);
            LoadFontTexture();
            SetupInput();

            ImGuizmo.SetImGuiContext(context);
            ImGuizmo.AllowAxisFlip(false);

            ImGui.NewFrame();
            ImGuizmo.BeginFrame();
        }

        unsafe void AddEmbeddedFont(string name, ushort min, ushort max, float fontSize = 17 * 2.0f / 3.0f)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Prowl.Runtime.EmbeddedResources.{name}"))
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

        unsafe void LoadFontTexture()
        {
            ImGuiIOPtr io = ImGui.GetIO();
            byte* pixels;
            int width;
            int height;
            ImGui.GetTexDataAsRGBA32(io.Fonts, &pixels, &width, &height, null);

            // Upload texture to graphics system
            Image image = new Image
            {
                data = pixels,
                width = width,
                height = height,
                mipmaps = 1,
                format = PixelFormat.PIXELFORMAT_UNCOMPRESSED_R8G8B8A8,
            };
            fontTexture = Raylib.LoadTextureFromImage(image);

            // Store texture id in imgui font
            io.Fonts.SetTexID(new IntPtr(fontTexture.id));

            // Clears font data on the CPU side
            io.Fonts.ClearTexData();
        }

        void SetupInput()
        {
            ImGuiIOPtr io = ImGui.GetIO();

            // Setup config flags
            io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;

            // Setup back-end capabilities flags
            io.BackendFlags |= ImGuiBackendFlags.HasMouseCursors;
            io.BackendFlags |= ImGuiBackendFlags.HasSetMousePos;

            // Keyboard mapping. ImGui will use those indices to peek into the io.KeysDown[] array.
            io.KeyMap[(int)ImGuiKey.Tab] = (int)KeyboardKey.KEY_TAB;
            io.KeyMap[(int)ImGuiKey.LeftArrow] = (int)KeyboardKey.KEY_LEFT;
            io.KeyMap[(int)ImGuiKey.RightArrow] = (int)KeyboardKey.KEY_RIGHT;
            io.KeyMap[(int)ImGuiKey.UpArrow] = (int)KeyboardKey.KEY_UP;
            io.KeyMap[(int)ImGuiKey.DownArrow] = (int)KeyboardKey.KEY_DOWN;
            io.KeyMap[(int)ImGuiKey.PageUp] = (int)KeyboardKey.KEY_PAGE_UP;
            io.KeyMap[(int)ImGuiKey.PageDown] = (int)KeyboardKey.KEY_PAGE_DOWN;
            io.KeyMap[(int)ImGuiKey.Home] = (int)KeyboardKey.KEY_HOME;
            io.KeyMap[(int)ImGuiKey.End] = (int)KeyboardKey.KEY_END;
            io.KeyMap[(int)ImGuiKey.Insert] = (int)KeyboardKey.KEY_INSERT;
            io.KeyMap[(int)ImGuiKey.Delete] = (int)KeyboardKey.KEY_DELETE;
            io.KeyMap[(int)ImGuiKey.Backspace] = (int)KeyboardKey.KEY_BACKSPACE;
            io.KeyMap[(int)ImGuiKey.Space] = (int)KeyboardKey.KEY_SPACE;
            io.KeyMap[(int)ImGuiKey.Enter] = (int)KeyboardKey.KEY_ENTER;
            io.KeyMap[(int)ImGuiKey.Escape] = (int)KeyboardKey.KEY_ESCAPE;
            io.KeyMap[(int)ImGuiKey.A] = (int)KeyboardKey.KEY_A;
            io.KeyMap[(int)ImGuiKey.C] = (int)KeyboardKey.KEY_C;
            io.KeyMap[(int)ImGuiKey.V] = (int)KeyboardKey.KEY_V;
            io.KeyMap[(int)ImGuiKey.X] = (int)KeyboardKey.KEY_X;
            io.KeyMap[(int)ImGuiKey.Y] = (int)KeyboardKey.KEY_Y;
            io.KeyMap[(int)ImGuiKey.Z] = (int)KeyboardKey.KEY_Z;
        }

        /// <summary>
        /// Update imgui internals(input, frameData)
        /// </summary>
        /// <param name="dt"></param>
        public void Update(float dt)
        {
            ImGuiIOPtr io = ImGui.GetIO();

            io.DisplayFramebufferScale = Vector2.One;
            io.DeltaTime = dt;

            UpdateKeyboard();
            UpdateMouse();
            UpdateGamepad();

            if (Raylib.IsWindowResized())
            {
                Resize(Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
            }

            ImGui.NewFrame();
            ImGuizmo.BeginFrame();
        }

        /// <summary>
        /// Resize imgui display
        /// </summary>
        public void Resize(int width, int height)
        {
            ImGuiIOPtr io = ImGui.GetIO();
            io.DisplaySize = new Vector2(width, height) / scaleFactor;
        }

        void UpdateKeyboard()
        {
            ImGuiIOPtr io = ImGui.GetIO();

            // Modifiers are not reliable across systems
            io.KeyCtrl = io.KeysDown[(int)KeyboardKey.KEY_LEFT_CONTROL] || io.KeysDown[(int)KeyboardKey.KEY_RIGHT_CONTROL];
            io.KeyShift = io.KeysDown[(int)KeyboardKey.KEY_LEFT_SHIFT] || io.KeysDown[(int)KeyboardKey.KEY_RIGHT_SHIFT];
            io.KeyAlt = io.KeysDown[(int)KeyboardKey.KEY_LEFT_ALT] || io.KeysDown[(int)KeyboardKey.KEY_RIGHT_ALT];
            io.KeySuper = io.KeysDown[(int)KeyboardKey.KEY_LEFT_SUPER] || io.KeysDown[(int)KeyboardKey.KEY_RIGHT_SUPER];

            // Key states
            for (int i = (int)KeyboardKey.KEY_SPACE; i < (int)KeyboardKey.KEY_KB_MENU + 1; i++)
            {
                io.KeysDown[i] = Raylib.IsKeyDown((KeyboardKey)i);
            }

            // Key input
            int keyPressed = Raylib.GetCharPressed();
            if (keyPressed != 0)
            {
                io.AddInputCharacter((uint)keyPressed);
            }
        }

        void UpdateMouse()
        {
            ImGuiIOPtr io = ImGui.GetIO();

            // Store button states
            for (int i = 0; i < io.MouseDown.Length; i++)
            {
                io.MouseDown[i] = Raylib.IsMouseButtonDown((MouseButton)i);
            }

            // Mouse scroll
            io.MouseWheel += Raylib.GetMouseWheelMove();

            // Mouse position
            Vector2 mousePosition = io.MousePos;
            bool focused = Raylib.IsWindowFocused();

            if (focused)
            {
                if (io.WantSetMousePos)
                {
                    Raylib.SetMousePosition((int)mousePosition.X, (int)mousePosition.Y);
                }
                else
                {
                    io.MousePos = Raylib.GetMousePosition();
                }
            }

            // Mouse cursor state
            if ((io.ConfigFlags & ImGuiConfigFlags.NoMouseCursorChange) == 0 || Raylib.IsCursorHidden())
            {
                ImGuiMouseCursor cursor = ImGui.GetMouseCursor();
                if (cursor == ImGuiMouseCursor.None || io.MouseDrawCursor)
                {
                    Raylib.HideCursor();
                }
                else
                {
                    Raylib.ShowCursor();
                }
            }
        }

        void UpdateGamepad()
        {
            ImGuiIOPtr io = ImGui.GetIO();
        }

        /// <summary>
        /// Gets the geometry as set up by ImGui and sends it to the graphics device
        /// </summary>
        public unsafe void Draw()
        {
            ImGui.Render();
            RenderCommandLists(ImGui.GetDrawData());
        }

        // Returns a Color struct from hexadecimal value
        Raylib_cs.Color GetColor(uint hexValue)
        {
            Raylib_cs.Color color;

            color.r = (byte)(hexValue & 0xFF);
            color.g = (byte)((hexValue >> 8) & 0xFF);
            color.b = (byte)((hexValue >> 16) & 0xFF);
            color.a = (byte)((hexValue >> 24) & 0xFF);

            return color;
        }

        void DrawTriangleVertex(ImDrawVert idxVert)
        {
            Raylib_cs.Color c = GetColor(idxVert.Col);
            Rlgl.rlColor4ub(c.r, c.g, c.b, c.a);
            Rlgl.rlTexCoord2f(idxVert.Uv.X, idxVert.Uv.Y);
            Rlgl.rlVertex2f(idxVert.Pos.X, idxVert.Pos.Y);
        }

        // Draw the imgui triangle data
        unsafe void DrawTriangles(uint count, int idxOffset, int vtxOffset, ImVectorImDrawIdx idxBuffer, ImVectorImDrawVert idxVert, ImTextureID textureId)
        {
            ushort index = 0;
            ImDrawVert vertex;

            if (Rlgl.rlCheckRenderBatchLimit((int)count * 3))
            {
                Rlgl.rlDrawRenderBatchActive();
            }

            Rlgl.rlBegin(DrawMode.TRIANGLES);
            Rlgl.rlSetTexture((uint)textureId.Handle);

            for (int i = 0; i <= (count - 3); i += 3)
            {
                index = idxBuffer.Data[idxOffset + i];
                vertex = idxVert.Data[vtxOffset + index];
                DrawTriangleVertex(vertex);

                index = idxBuffer.Data[idxOffset + i + 1];
                vertex = idxVert.Data[vtxOffset + index];
                DrawTriangleVertex(vertex);

                index = idxBuffer.Data[idxOffset + i + 2];
                vertex = idxVert.Data[vtxOffset + index];
                DrawTriangleVertex(vertex);
            }
            Rlgl.rlEnd();
        }

        unsafe void RenderCommandLists(ImDrawData* data)
        {
            // Scale coordinates for retina displays (screen coordinates != framebuffer coordinates)
            int fbWidth = (int)(data->DisplaySize.X * data->FramebufferScale.X);
            int fbHeight = (int)(data->DisplaySize.Y * data->FramebufferScale.Y);

            // Avoid rendering if display is minimized or if the command list is empty
            if (fbWidth <= 0 || fbHeight <= 0 || data->CmdListsCount == 0)
            {
                return;
            }

            Rlgl.rlDrawRenderBatchActive();
            Rlgl.rlDisableBackfaceCulling();
            Rlgl.rlEnableScissorTest();

            data->ScaleClipRects(ImGui.GetIO().DisplayFramebufferScale);

            for (int n = 0; n < data->CmdListsCount; n++)
            {
                int idxOffset = 0;
                var cmdList = data->CmdLists.Data[n];

                // Vertex buffer and index buffer generated by DearImGui
                var vtxBuffer = cmdList->VtxBuffer;
                var idxBuffer = cmdList->IdxBuffer;

                for (int cmdi = 0; cmdi < cmdList->CmdBuffer.Size; cmdi++)
                {
                    var pcmd = cmdList->CmdBuffer.Data[cmdi];

                    // Scissor rect
                    Vector2 pos = data->DisplayPos;
                    int rectX = (int)((pcmd.ClipRect.X - pos.X) * data->FramebufferScale.X);
                    int rectY = (int)((pcmd.ClipRect.Y - pos.Y) * data->FramebufferScale.Y);
                    int rectW = (int)((pcmd.ClipRect.Z - rectX) * data->FramebufferScale.Y);
                    int rectH = (int)((pcmd.ClipRect.W - rectY) * data->FramebufferScale.Y);
                    Rlgl.rlScissor(rectX, Raylib.GetScreenHeight() - (rectY + rectH), rectW, rectH);

                    if (pcmd.UserCallback != null)
                    {
                        // pcmd.UserCallback(cmdList, pcmd);
                        idxOffset += (int)pcmd.ElemCount;
                    }
                    else
                    {
                        DrawTriangles(pcmd.ElemCount, idxOffset, (int)pcmd.VtxOffset, idxBuffer, vtxBuffer, pcmd.TextureId);
                        idxOffset += (int)pcmd.ElemCount;
                        Rlgl.rlDrawRenderBatchActive();
                    }
                }
            }

            Rlgl.rlSetTexture(0);
            Rlgl.rlDisableScissorTest();
            Rlgl.rlEnableBackfaceCulling();
        }
    }
}