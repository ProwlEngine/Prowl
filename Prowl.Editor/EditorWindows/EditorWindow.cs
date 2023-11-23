using ImGuiNET;
using System.Numerics;

namespace Prowl.Editor.EditorWindows;

public class EditorWindow {

    protected string Title = "Title";
    private readonly int _id;

    protected virtual ImGuiWindowFlags Flags { get; } = ImGuiWindowFlags.NoCollapse;
    protected virtual bool Center { get; } = false;
    protected virtual int Width { get; } = 256;
    protected virtual int Height { get; } = 256;
    protected virtual bool LockSize { get; } = false;
    protected virtual bool BackgroundFade { get; } = false;

    protected bool isOpened = true;

    public EditorWindow() {
        EditorApplication.OnDrawEditor += DrawWindow;
        EditorApplication.OnUpdateEditor += UpdateWindow;
        _id = GetHashCode();
    }
    
    private void DrawWindow() {
        isOpened = true;

        if (BackgroundFade)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0);
            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0, 0, 0, 0.5f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0, 0, 0, 0.5f));
            if (ImGui.Begin("Fader", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoDecoration))
            {
                ImGui.SetWindowSize(new Vector2(ImGui.GetIO().DisplaySize.X, ImGui.GetIO().DisplaySize.Y));
                ImGui.SetWindowPos(new Vector2(0, 0));
                ImGui.End();
            }
            ImGui.PopStyleColor(2);
            ImGui.PopStyleVar(1);

            // Next window should be focused
            ImGui.SetNextWindowFocus();
        }

        PreWindowDraw();

        if (Center)
        {
            var vp_size = ImGui.GetMainViewport().Size / 2;
            ImGui.SetNextWindowPos(new Vector2(vp_size.X - (Width/2), vp_size.Y - (Height / 2)));
        }
        if(LockSize)
            ImGui.SetNextWindowSize(new Vector2(Width, Height));
        else
            ImGui.SetNextWindowSize(new Vector2(Width, Height), ImGuiCond.FirstUseEver);
        // push id doesnt work with windows since it cant be handled with the id stack, c++ uses ## or ### to set an identifier
        ImGui.Begin(Title + "##" + _id + Project.HasProject, ref isOpened, Flags);

        DrawToolbar();
        
        Draw();
        ImGui.End();
        
        PostWindowDraw();

        if (!isOpened)
        {
            EditorApplication.OnDrawEditor -= DrawWindow;
            EditorApplication.OnUpdateEditor -= UpdateWindow;
        }
    }
    
    private void UpdateWindow() {
        Update();
    }

    protected virtual void PreWindowDraw() { }
    protected virtual void PostWindowDraw() { }

    protected virtual void DrawToolbar() {
    }
    
    protected virtual void Draw() { }
    protected virtual void Update() { }
    
}
