using HexaEngine.ImGuiNET;
using Prowl.Runtime;
using Prowl.Runtime.Components;
using Prowl.Runtime.Resources;
using System.Numerics;

namespace Prowl.Editor.EditorWindows;

public class GameWindow : EditorWindow
{
    public EditorSettings Settings => Project.ProjectSettings.GetSetting<EditorSettings>();

    RenderTexture RenderTarget;

    public GameWindow()
    {
        Title = "Game";
        RefreshRenderTexture(Width, Height);
    }

    public void RefreshRenderTexture(int width, int height)
    {
        RenderTarget?.Dispose();
        RenderTarget = new RenderTexture(width, height);
    }

    protected override void PreWindowDraw() =>
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));

    protected override void PostWindowDraw() =>
        ImGui.PopStyleVar(1);

    protected override void Draw()
    {
        if (!Project.HasProject) return;

        var cStart = ImGui.GetCursorPos();
        var windowSize = ImGui.GetWindowSize();
        if (windowSize.X != RenderTarget.Width || windowSize.Y != RenderTarget.Height)
            RefreshRenderTexture((int)windowSize.X, (int)windowSize.Y);

        // Find Camera to render
        var allCameras = EngineObject.FindObjectsOfType<Camera>();
        // Remove disabled ones
        allCameras = allCameras.Where(c => c.EnabledInHierarchy && !c.GameObject.Name.Equals("Editor-Camera", StringComparison.OrdinalIgnoreCase)).ToArray();
        // Find MainCamera
        var mainCam = allCameras.FirstOrDefault(c => c.GameObject.CompareTag("Main Camera") && c.Target.IsExplicitNull, allCameras.Length > 0 ? allCameras[0] : null);

        if(mainCam == null)
        {
            GUIHelper.TextCenter("No Camera found");
            return;
        }
        else
        {
            // We got a camera to visualize
            mainCam.Target = RenderTarget;
            mainCam.Render((int)windowSize.X, (int)windowSize.Y);
            mainCam.Target = null;

            ImGui.Image((IntPtr)RenderTarget.InternalTextures[0].id, ImGui.GetContentRegionAvail(), new Vector2(0, 1), new Vector2(1, 0));

        }
    }

    protected override void Update()
    {
        
    }

}