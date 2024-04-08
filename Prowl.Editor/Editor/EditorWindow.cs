using Hexa.NET.ImGui;
using System.Diagnostics;
using System.Numerics;

namespace Prowl.Editor.EditorWindows;

public class EditorWindow {

    protected string Title = "Title";
    private readonly int _id;
    private static readonly Dictionary<Type, int> _WindowCounter = [];
    private readonly int _windowCount = 0;

    protected virtual ImGuiWindowFlags Flags { get; } = ImGuiWindowFlags.NoCollapse;
    protected virtual bool Center { get; } = false;
    protected virtual int Width { get; } = 256;
    protected virtual int Height { get; } = 256;
    protected virtual bool LockSize { get; } = false;
    protected virtual bool BackgroundFade { get; } = false;

    protected ImGuiWindowPtr ImGUIWindow { get; private set; }

    protected bool isOpened = true;

    public EditorWindow() : base()
    {
        Program.OnDrawEditor += DrawWindow;
        Program.OnUpdateEditor += UpdateWindow;
        _id = GetHashCode();

        var t = this.GetType();
        _windowCount = 0;
        if (_WindowCounter.ContainsKey(t))
        {
            _WindowCounter[t] = _WindowCounter[t] + 1;
            _windowCount = _WindowCounter[t];
        }
        else
            _WindowCounter.Add(t, 0);
    }
    
    private void DrawWindow() {
        try
        {
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
                ImGui.SetNextWindowPos(new Vector2(vp_size.X - (Width / 2), vp_size.Y - (Height / 2)));
            }
            if (LockSize)
                ImGui.SetNextWindowSize(new Vector2(Width, Height));
            else
                ImGui.SetNextWindowSize(new Vector2(Width, Height), ImGuiCond.FirstUseEver);
            // push id doesnt work with windows since it cant be handled with the id stack, c++ uses ## or ### to set an identifier
            ImGui.PushID(_id + (Project.HasProject ? 1000 : 0));

            // Adding a ID to the title after the first iteration is required for Multi-Windows, the first cant have one for Docking
            if (_windowCount != 0)
                ImGui.Begin(Title + " " + _windowCount, ref isOpened, Flags);
            else
                ImGui.Begin(Title, ref isOpened, Flags);

            ImGUIWindow = ImGui.GetCurrentWindow();

            DrawToolbar();

            Draw();
            ImGui.End();
            ImGui.PopID();

            PostWindowDraw();

            if (!isOpened)
            {
                Program.OnDrawEditor -= DrawWindow;
                Program.OnUpdateEditor -= UpdateWindow;
                Close();
            }
        }
        catch(Exception e)
        {
            Runtime.Debug.LogError("Error in EditorWindow: " + e.Message + "\n" + e.StackTrace);
        }
    }

    private void UpdateWindow()
    {
        try
        {
            Update();
        }
        catch (Exception e)
        {
            Runtime.Debug.LogError("Error in UpdateWindow: " + e.Message + "\n" + e.StackTrace);
        }
    }

    protected virtual void PreWindowDraw() { }
    protected virtual void PostWindowDraw() { }

    protected virtual void DrawToolbar() {
    }
    
    protected virtual void Draw() { }
    protected virtual void Update() { }
    protected virtual void Close() { }
    
}
