using Hexa.NET.ImGui;
using Prowl.Editor.Editor.Preferences;
using Prowl.Icons;
using Prowl.Runtime;
using System.Reflection;

namespace Prowl.Editor.EditorWindows;

public class GameWindow : EditorWindow
{
    public enum Resolutions
    {
        [Text("Fit")] fit = 0,
        [Text("Custom")] custom,
        [Text("16:9 480p")] _480p,
        [Text("16:9 720p")] _720p,
        [Text("16:9 1080p")] _1080p,
        [Text("16:9 1440p")] _1440p,
        [Text("16:9 2160p")] _2160p,
        [Text("16:9 4320p")] _4320p,
        [Text("4:3 480p")] _480p_4_3,
        [Text("4:3 720p")] _720p_4_3,
        [Text("4:3 1080p")] _1080p_4_3,
        [Text("4:3 1440p")] _1440p_4_3,
        [Text("4:3 2160p")] _2160p_4_3,
        [Text("4:3 4320p")] _4320p_4_3,
    }

    const int HeaderHeight = 27;

    RenderTexture RenderTarget;
    bool previouslyPlaying = false;

    public GameWindow() : base()
    {
        Title = FontAwesome6.Gamepad + " Game";
        GeneralPreferences.Instance.CurrentWidth = Width;
        GeneralPreferences.Instance.CurrentHeight = Height - HeaderHeight;
        RefreshRenderTexture();
    }

    public void RefreshRenderTexture()
    {
        RenderTarget?.Dispose();
        RenderTarget = new RenderTexture(GeneralPreferences.Instance.CurrentWidth, GeneralPreferences.Instance.CurrentHeight);
    }

    protected override void PreWindowDraw() =>
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new System.Numerics.Vector2(0, 0));

    protected override void PostWindowDraw() =>
        ImGui.PopStyleVar(1);

    protected override void Draw()
    {
        if (!Project.HasProject) return;

        if (!previouslyPlaying && Application.isPlaying) {
            previouslyPlaying = true;
            if(GeneralPreferences.Instance.AutoFocusGameView)
                ImGui.SetWindowFocus();
        } else if (previouslyPlaying && !Application.isPlaying) {
            previouslyPlaying = false;
        }

        // Header Bar with resolution settings, then Image under it
        ImGui.BeginChild("Header", new System.Numerics.Vector2(0, HeaderHeight), ImGuiChildFlags.Border, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        {
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 3);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 5);
            ImGui.Text(FontAwesome6.Display);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(50);
            if (ImGui.InputInt("##Width", ref GeneralPreferences.Instance.CurrentWidth, 0, 0, ImGuiInputTextFlags.EnterReturnsTrue)) {
                GeneralPreferences.Instance.CurrentWidth = Math.Clamp(GeneralPreferences.Instance.CurrentWidth, 1, 7680);
                GeneralPreferences.Instance.Resolution = Resolutions.custom;
                RefreshRenderTexture();
            }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(50);
            if (ImGui.InputInt("##Height", ref GeneralPreferences.Instance.CurrentHeight, 0, 0, ImGuiInputTextFlags.EnterReturnsTrue)) {
                GeneralPreferences.Instance.CurrentHeight = Math.Clamp(GeneralPreferences.Instance.CurrentHeight, 1, 4320);
                GeneralPreferences.Instance.Resolution = Resolutions.custom;
                RefreshRenderTexture();
            }

            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            string[] resolutionNames = Enum.GetValues(typeof(Resolutions)).Cast<Resolutions>().Select(r => GetDescription(r)).ToArray();
            int currentIndex = (int)GeneralPreferences.Instance.Resolution;
            if (ImGui.Combo("##ResolutionCombo", ref currentIndex, resolutionNames, resolutionNames.Length)) {
                GeneralPreferences.Instance.Resolution = (Resolutions)Enum.GetValues(typeof(Resolutions)).GetValue(currentIndex);
                UpdateResolution(GeneralPreferences.Instance.Resolution);
                RefreshRenderTexture();
            }

            ImGui.SameLine();
            // Auto Focus
            ImGui.SetCursorPosX(ImGui.GetWindowWidth() - 200);
            ImGui.Checkbox("Auto Focus", ref GeneralPreferences.Instance.AutoFocusGameView);
            ImGui.SameLine();
            // Auto Refresh
            ImGui.Checkbox("Auto Refresh", ref GeneralPreferences.Instance.AutoRefreshGameView);
        }
        ImGui.EndChild();

        var renderSize = ImGui.GetContentRegionAvail();

        var min = ImGui.GetCursorScreenPos();
        var max = new System.Numerics.Vector2(min.X + renderSize.X, min.Y + renderSize.Y);
        ImGui.GetWindowDrawList().AddRectFilled(min, max, ImGui.GetColorU32(new System.Numerics.Vector4(0, 0, 0, 1)));

        // Find Camera to render
        var allCameras = EngineObject.FindObjectsOfType<Camera>();
        // Remove disabled ones
        allCameras = allCameras.Where(c => c.EnabledInHierarchy && !c.GameObject.Name.Equals("Editor-Camera", StringComparison.OrdinalIgnoreCase)).ToArray();
        // Find MainCamera
        var mainCam = allCameras.FirstOrDefault(c => c.GameObject.CompareTag("Main Camera") && c.Target.IsExplicitNull, allCameras.Length > 0 ? allCameras[0] : null);

        if (mainCam == null) {
            GUIHelper.TextCenter("No Camera found", 2f, true);
            return;
        }
        if (GeneralPreferences.Instance.Resolution == Resolutions.fit) {
            if (renderSize.X != RenderTarget.Width || renderSize.Y != RenderTarget.Height) {
                GeneralPreferences.Instance.CurrentWidth = (int)renderSize.X;
                GeneralPreferences.Instance.CurrentHeight = (int)renderSize.Y;
                RefreshRenderTexture();
            }
        }

        // We got a camera to visualize
        if (GeneralPreferences.Instance.AutoRefreshGameView)
        {
            if (Application.isPlaying || Time.frameCount % 8 == 0)
            {
                mainCam.Target = RenderTarget;
                mainCam.Render((int)renderSize.X, (int)renderSize.Y);
                mainCam.Target = null;
            }
        }

        // Letter box the image into the render size
        float aspect = (float)RenderTarget.Width / (float)RenderTarget.Height;
        float renderAspect = renderSize.X / renderSize.Y;
        if (aspect > renderAspect) {
            float width = renderSize.X;
            float height = width / aspect;
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + ((renderSize.Y - height) / 2f));
            ImGui.Image((IntPtr)RenderTarget.InternalTextures[0].Handle, new System.Numerics.Vector2(width, height), new System.Numerics.Vector2(0, 1), new System.Numerics.Vector2(1, 0));
        } else {
            float height = renderSize.Y;
            float width = height * aspect;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ((renderSize.X - width) / 2f));
            ImGui.Image((IntPtr)RenderTarget.InternalTextures[0].Handle, new System.Numerics.Vector2(width, height), new System.Numerics.Vector2(0, 1), new System.Numerics.Vector2(1, 0));
        }
    }

    protected override void Update()
    {

    }

    void UpdateResolution(Resolutions resolution)
    {
        switch (resolution) {
            case Resolutions._480p:
                GeneralPreferences.Instance.CurrentWidth = 854;
                GeneralPreferences.Instance.CurrentHeight = 480;
                break;
            case Resolutions._720p:
                GeneralPreferences.Instance.CurrentWidth = 1280;
                GeneralPreferences.Instance.CurrentHeight = 720;
                break;
            case Resolutions._1080p:
                GeneralPreferences.Instance.CurrentWidth = 1920;
                GeneralPreferences.Instance.CurrentHeight = 1080;
                break;
            case Resolutions._1440p:
                GeneralPreferences.Instance.CurrentWidth = 2560;
                GeneralPreferences.Instance.CurrentHeight = 1440;
                break;
            case Resolutions._2160p:
                GeneralPreferences.Instance.CurrentWidth = 3840;
                GeneralPreferences.Instance.CurrentHeight = 2160;
                break;
            case Resolutions._4320p:
                GeneralPreferences.Instance.CurrentWidth = 7680;
                GeneralPreferences.Instance.CurrentHeight = 4320;
                break;
            case Resolutions._480p_4_3:
                GeneralPreferences.Instance.CurrentWidth = 640;
                GeneralPreferences.Instance.CurrentHeight = 480;
                break;
            case Resolutions._720p_4_3:
                GeneralPreferences.Instance.CurrentWidth = 960;
                GeneralPreferences.Instance.CurrentHeight = 720;
                break;
            case Resolutions._1080p_4_3:
                GeneralPreferences.Instance.CurrentWidth = 1440;
                GeneralPreferences.Instance.CurrentHeight = 1080;
                break;
            case Resolutions._1440p_4_3:
                GeneralPreferences.Instance.CurrentWidth = 1920;
                GeneralPreferences.Instance.CurrentHeight = 1440;
                break;
            case Resolutions._2160p_4_3:
                GeneralPreferences.Instance.CurrentWidth = 2880;
                GeneralPreferences.Instance.CurrentHeight = 2160;
                break;
            case Resolutions._4320p_4_3:
                GeneralPreferences.Instance.CurrentWidth = 5760;
                GeneralPreferences.Instance.CurrentHeight = 4320;
                break;
        }
    }

    string GetDescription(Enum value)
    {
        FieldInfo field = value.GetType().GetField(value.ToString());
        TextAttribute attribute = Attribute.GetCustomAttribute(field, typeof(TextAttribute)) as TextAttribute;
        return attribute == null ? value.ToString() : attribute.text;
    }

}
