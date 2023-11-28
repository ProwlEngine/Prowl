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
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 4));
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
    
    private static void DrawMenuItems() {
    private static void DrawMenuItems()
    {
        ImGui.SetCursorPosX(2);
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
