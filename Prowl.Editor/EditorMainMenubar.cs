using Prowl.Runtime;
using Prowl.Icons;
using HexaEngine.ImGuiNET;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Prowl.Editor.EditorWindows;

public class EditorMainMenubar {
    
    private struct Position {
        public int X;
        public int Y;
        public Position(int x, int y) {
            X = x;
            Y = y;
        }
    }

    private Position _windowPosRef;
    private Position _mousePosRef;

    private bool _dragging;
    
    public EditorMainMenubar() {
        EditorApplication.OnDrawEditor += Draw;
    }
    
    private void Draw() {
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8, 8));
        ImGui.PushStyleColor(ImGuiCol.MenuBarBg, new Vector4(0.11f, 0.11f, 0.11f, 1.0f));
        if(ImGui.BeginMainMenuBar()) {
            
            DrawPlayControls();

            //Texture2D appIcon = EditorResources.GetIcon("AppIcon");
            ImGui.SetCursorPos(new Vector2(8, -10));
            //ImGui.Image((IntPtr) appIcon.ID, new Vector2(16, 16));
            ImGui.SetWindowFontScale(2.0f);
            ImGui.Text($"{FontAwesome6.Shield}");
            ImGui.SetWindowFontScale(1.0f);


            DrawMenuItems();
            
            if(ImGui.Button($"{FontAwesome6.ArrowsSpin}"))
                EditorApplication.Instance.RegisterReloadOfExternalAssemblies();
            GUIHelper.Tooltip("Recompile Project Scripts.");
            
            //if(ImGui.Button($"{FontAwesome6.GroupArrowsRotate}##Shaders"))
            //    AssetProvider.RemoveAllOfType<Shader>(true);
            //GUIHelper.Tooltip("Recompile Project Shaders.");
            
            DrawWindowHandleButtons();

            //InstallDragArea();

            ImGui.EndMainMenuBar();   
        }
        // pop main menu bar size
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
    }

    private static void DrawPlayControls() {
        
        void AlignForWidth(float width, float alignment = 0.5f) {
            float avail = ImGui.GetContentRegionAvail().X;
            float off = (avail - width) * alignment;
            if(off > 0.0f)
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + off);
        }
        
        ImGuiStylePtr style = ImGui.GetStyle();
        float width = 0.0f;
        
        switch(PlayMode.Current) {
            case PlayMode.Mode.Editing:
                width += ImGui.CalcTextSize("Play").X;
                AlignForWidth(width);
                
                if(ImGui.Button("Play"))
                    PlayMode.Start();
                return;
            case PlayMode.Mode.Playing:
                width += ImGui.CalcTextSize("Pause").X;
                width += style.ItemSpacing.X;
                width += ImGui.CalcTextSize("Stop").X;
                AlignForWidth(width);
                
                if(ImGui.Button("Pause"))
                    PlayMode.Pause();
                if(ImGui.Button("Stop"))
                    PlayMode.Stop();
                return;
            case PlayMode.Mode.Paused:
                width += ImGui.CalcTextSize("Resume").X;
                width += style.ItemSpacing.X;
                width += ImGui.CalcTextSize("Stop").X;
                AlignForWidth(width);
                
                if(ImGui.Button("Resume"))
                    PlayMode.Resume();
                if(ImGui.Button("Stop"))
                    PlayMode.Stop();
                return;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
    
    public const int WM_NCLBUTTONDOWN = 0xA1;
    public const int HTCAPTION = 0x2;

    [DllImport("User32.dll")]
    public static extern bool ReleaseCapture();

    [DllImport("User32.dll")]
    public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
    
    [DllImport("user32.dll")]
    static extern IntPtr SetCapture(IntPtr hWnd);
    
    [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
    static extern bool SetForegroundWindow(IntPtr hWnd);
    
    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);
    
    public const int WM_LBUTTONDOWN = 0x201;
    public const int WM_LBUTTONUP = 0x0202;

    //private unsafe void InstallDragArea() {
    //    // push style to make invisible
    //    ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0);
    //    
    //    ImGui.SetCursorPos(new Vector2(0, 0));
    //    ImGui.Button("", ImGui.GetWindowSize());
    //    if(ImGui.IsItemActive()) {
    //        if(!_dragging) {
    //            _dragging = true;
//  //              _mousePosRef = new Position(CursorPosition.GetCursorPosition().X, CursorPosition.GetCursorPosition().Y);
//  //              Glfw.GetWindowPos(GlfwWindow.Handle, out int x, out int y);
//  //              _windowPosRef = new Position(x, y);
    //            
    //            GlfwNativeWindow test = new GlfwNativeWindow(Silk.NET.GLFW.Glfw.GetApi(), Application.Window.Handle);
    //            IntPtr hwnd = test.Win32.Value.Hwnd;
    //            ReleaseCapture();
    //            SendMessage(hwnd, WM_NCLBUTTONDOWN, HTCAPTION, 0);
    //            SetForegroundWindow(hwnd);
    //            SetFocus(hwnd);
    //            SendMessage(hwnd, WM_LBUTTONDOWN, 0, 0);
    //            SendMessage(hwnd, WM_LBUTTONUP, 0, 0);
    //        }
//  //          int offsetX = CursorPosition.GetCursorPosition().X - _mousePosRef.X;
//  //          int offsetY = CursorPosition.GetCursorPosition().Y - _mousePosRef.Y;
//  //          Glfw.SetWindowPos(GlfwWindow.Handle, _windowPosRef.X + offsetX, _windowPosRef.Y + offsetY);
    //    } else {
    //        _dragging = false;
    //    }
    //
    //    //pop button drag area alpha
    //    ImGui.PopStyleVar();
    //}

    private static void DrawWindowHandleButtons() {
        
        //ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0f, 0f, 0f, 0.0f));
        //ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(1f, 1f, 1f, 0.15f));
        //ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(1f, 1f, 1f, 0.2f));
        //ImGui.SetCursorPos(new Vector2(ImGui.GetWindowSize().X - 64, 0));
        //if(ImGui.Button($"Close", new Vector2(32, 32))) {
        //    unsafe {
        //        Renderer.GlfwWindow.Glfw.MaximizeWindow(Renderer.GlfwWindow.Handle);
        //    }
        //}
        //ImGui.PopStyleColor(3);


        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0f, 0f, 0f, 0.0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(1f, 0f, 0f, 0.75f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(1f, 0.4f, 0.4f, 0.75f));
        ImGui.SetCursorPos(new Vector2(ImGui.GetWindowSize().X - 37, -17));
        ImGui.SetWindowFontScale(2.0f);
        if(ImGui.Button($"{FontAwesome6.Xmark}", new Vector2(37, 64))) {
            EditorApplication.Instance.Terminate();
        }
        ImGui.SetWindowFontScale(1.0f);
        ImGui.PopStyleColor(3);
    }

    private static void DrawMenuItems() {
        ImGui.SetCursorPos(new Vector2(32, 0));

        if (ImGui.BeginMenu("File"))
        {
            if (ImGui.MenuItem("Open Project")) { new ProjectsWindow(); }
            if (ImGui.MenuItem("Preferences")) { }
            if (ImGui.MenuItem("Project Settings")) { new ProjectSettingsWindow(); }
            if (ImGui.MenuItem("Quit")) EditorApplication.Instance.Terminate();
            ImGui.EndMenu();
        }
        
        if(ImGui.BeginMenu("Project"))
        {
            //if (ImGui.MenuItem("New Scene")) Hierarchy.Clear();
            //if (ImGui.MenuItem("Save Scene"))
            //{
            //    if (Hierarchy.CurrentHierarchy != null)
            //    {
            //        Hierarchy.CurrentlyLoadedNodesAssetPath = Hierarchy.CurrentHierarchy;
            //        //Hierarchy.SaveToAssetPath();
            //    }
            //    else
            //    {
            //        ImGuiFileDialog.FileDialog(new ImFileDialogInfo()
            //        {
            //            type = ImGuiFileDialogType.SaveFile,
            //            title = "Save Scene",
            //            fileName = "New Hierarchy.hierarchy",
            //            OnComplete = (path) => { File.WriteAllText(path, Hierarchy.SaveToString()); },
            //            directoryPath = new DirectoryInfo(Project.ProjectAssetDirectory)
            //        });
            //    }
            //}
            ImGui.EndMenu();
        }

        MenuItem.DrawMenuRoot("Assets");

        MenuItem.DrawMenuRoot("Create");

        if (ImGui.BeginMenu("Window")) {
            if(ImGui.MenuItem("Asset Browser")) { new AssetBrowserWindow(); }
            if(ImGui.MenuItem("Console")) { new ConsoleWindow(); }
            if(ImGui.MenuItem("Hierarchy")) { new HierarchyWindow(); }
            if(ImGui.MenuItem("Inspector")) { new InspectorWindow(); }
            if(ImGui.MenuItem("Viewport")) { new ViewportWindow(); }
            ImGui.EndMenu();
        }
        
        if(ImGui.BeginMenu("Help")) {
            if(ImGui.MenuItem("Tip: Just get good.")) { }
            ImGui.EndMenu();
        }
        
        ImGui.Text(CursorPosition.GetCursorPosition().X + " " + CursorPosition.GetCursorPosition().Y);
    }
    
}
