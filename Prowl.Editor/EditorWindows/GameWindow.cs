using HexaEngine.ImGuiNET;
using Prowl.Icons;
using Prowl.Runtime;
using System.Numerics;

namespace Prowl.Editor.EditorWindows;

public class GameWindow : EditorWindow
{
    public EditorSettings Settings => Project.ProjectSettings.GetSetting<EditorSettings>();

    RenderTexture RenderTarget;
    bool previouslyPlaying = false;

    public GameWindow() : base()
    {
        Title = FontAwesome6.Gamepad + " Game";
        RefreshRenderTexture(Width, Height);
    }

    public void RefreshRenderTexture(int width, int height)
    {
        RenderTarget?.Dispose();
        RenderTarget = new RenderTexture(width, height);
    }

    protected override void PreWindowDraw() =>
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new System.Numerics.Vector2(0, 0));

    protected override void PostWindowDraw() =>
        ImGui.PopStyleVar(1);

    protected override void Draw()
    {
        if (!Project.HasProject) return;

        var cStart = ImGui.GetCursorPos();
        var windowSize = ImGui.GetWindowSize();
        if(!previouslyPlaying && Application.isPlaying) {
            previouslyPlaying = true;
            ImGui.SetWindowFocus();
        } else if(previouslyPlaying && !Application.isPlaying) {
            previouslyPlaying = false;
        }

        var renderSize = ImGui.GetContentRegionAvail();
        if (renderSize.X != RenderTarget.Width || renderSize.Y != RenderTarget.Height)
            RefreshRenderTexture((int)renderSize.X, (int)renderSize.Y);

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
            if (Time.frameCount % 8 == 0)
            {
                mainCam.Target = RenderTarget;
                mainCam.Render((int)renderSize.X, (int)renderSize.Y);
                mainCam.Target = null;
            }

            ImGui.Image((IntPtr)RenderTarget.InternalTextures[0].Handle, ImGui.GetContentRegionAvail(), new System.Numerics.Vector2(0, 1), new System.Numerics.Vector2(1, 0));

        }
    }

    protected override void Update()
    {
        
    }

}