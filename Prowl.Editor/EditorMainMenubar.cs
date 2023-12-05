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
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new System.Numerics.Vector2(4, 4));
        if(ImGui.BeginMainMenuBar()) {

            DrawPlayControls();

            ImGui.SetCursorPosX(0);
            DrawMenuItems();

            if(ImGui.Button($"{FontAwesome6.ArrowsSpin}"))
                EditorApplication.Instance.RegisterReloadOfExternalAssemblies();
            GUIHelper.Tooltip("Recompile Project Scripts.");


            ImGui.EndMainMenuBar();   
        }
        // pop main menu bar size
        ImGui.PopStyleVar();
    }

    private static void DrawPlayControls() {
        
        void AlignForWidth(float width, float alignment = 0.5f) {
            float avail = ImGui.GetContentRegionAvail().X;
            float off = (avail - width) * alignment;
            if(off > 0.0f)
                ImGui.SetCursorPosX(off);
        }
        
        ImGuiStylePtr style = ImGui.GetStyle();
        float width = 0.0f;
        
        switch(PlayMode.Current) {
            case PlayMode.Mode.Editing:
                width += ImGui.CalcTextSize(" " + FontAwesome6.Play).X;
                AlignForWidth(width);

                if (ImGui.Button(" " + FontAwesome6.Play))
                    PlayMode.Start();
                return;
            case PlayMode.Mode.Playing:
                width += ImGui.CalcTextSize(" " + FontAwesome6.Pause).X;
                width += style.ItemSpacing.X;
                width += ImGui.CalcTextSize(" " + FontAwesome6.Stop).X;
                AlignForWidth(width);
                
                if(ImGui.Button(" " + FontAwesome6.Pause))
                    PlayMode.Pause();
                if(ImGui.Button(" " + FontAwesome6.Stop))
                    PlayMode.Stop();
                return;
            case PlayMode.Mode.Paused:
                width += ImGui.CalcTextSize(" " + FontAwesome6.Play).X;
                width += style.ItemSpacing.X;
                width += ImGui.CalcTextSize(" " + FontAwesome6.Stop).X;
                AlignForWidth(width);
                
                if(ImGui.Button(" " + FontAwesome6.Play))
                    PlayMode.Resume();
                if(ImGui.Button(" " + FontAwesome6.Stop))
                    PlayMode.Stop();
                return;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
    
    private static void DrawMenuItems()
    {
        ImGui.SetCursorPosX(2);
        if (ImGui.BeginMenu("File"))
        {
            if (ImGui.MenuItem("Open Project")) { new ProjectsWindow(); }
            ImGui.Separator();
            MenuItem.DrawMenuRoot("Scene");
            ImGui.Separator();
            if (ImGui.MenuItem("Preferences")) { }
            if (ImGui.MenuItem("Project Settings")) { new ProjectSettingsWindow(); }
            ImGui.Separator();
            if (ImGui.MenuItem("Quit")) EditorApplication.Instance.Terminate();
            ImGui.EndMenu();
        }

        MenuItem.DrawMenuRoot("Scene");

        MainMenuItems.Directory = null;
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
    }
    
}
