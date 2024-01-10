using HexaEngine.ImGuiNET;
using Prowl.Icons;
using Prowl.Runtime;
using System.Numerics;
using System.Reflection;

namespace Prowl.Editor.EditorWindows;

public class GameWindow : EditorWindow
{
    public EditorSettings Settings => Project.ProjectSettings.GetSetting<EditorSettings>();

    enum Resolutions
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
    Resolutions curResolution = Resolutions.fit;

    const int HeaderHeight = 30;

    RenderTexture RenderTarget;
    bool previouslyPlaying = false;

    int rtWidth;
    int rtHeight;

    public GameWindow() : base()
    {
        Title = FontAwesome6.Gamepad + " Game";
        rtWidth = Width;
        rtHeight = Height - HeaderHeight;
        RefreshRenderTexture();
    }

    public void RefreshRenderTexture()
    {
        RenderTarget?.Dispose();
        RenderTarget = new RenderTexture(rtWidth, rtHeight);
    }

    protected override void PreWindowDraw() =>
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new System.Numerics.Vector2(0, 0));

    protected override void PostWindowDraw() =>
        ImGui.PopStyleVar(1);

    protected override void Draw()
    {
        if (!Project.HasProject) return;

        if(!previouslyPlaying && Application.isPlaying) {
            previouslyPlaying = true;
            ImGui.SetWindowFocus();
        } else if(previouslyPlaying && !Application.isPlaying) {
            previouslyPlaying = false;
        }

        // Header Bar with resolution settings, then Image under it
        ImGui.BeginChild("Header", new System.Numerics.Vector2(0, HeaderHeight), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        {
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 5);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 5);
            ImGui.Text(FontAwesome6.Display);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(50);
            if (ImGui.InputInt("##Width", ref rtWidth, 0, 0, ImGuiInputTextFlags.EnterReturnsTrue)) {
                rtWidth = Math.Clamp(rtWidth, 1, 7680);
                curResolution = Resolutions.custom;
                RefreshRenderTexture();
            }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(50);
            if (ImGui.InputInt("##Height", ref rtHeight, 0, 0, ImGuiInputTextFlags.EnterReturnsTrue)) {
                rtHeight = Math.Clamp(rtHeight, 1, 4320);
                curResolution = Resolutions.custom;
                RefreshRenderTexture();
            }

            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            string[] resolutionNames = Enum.GetValues(typeof(Resolutions)).Cast<Resolutions>().Select(r => GetDescription(r)).ToArray();
            int currentIndex = (int)curResolution;
            if (ImGui.Combo("##ResolutionCombo", ref currentIndex, resolutionNames, resolutionNames.Length)) {
                curResolution = (Resolutions)Enum.GetValues(typeof(Resolutions)).GetValue(currentIndex);
                UpdateResolution(curResolution);
                RefreshRenderTexture();
            }
        }
        ImGui.EndChild();

        var renderSize = ImGui.GetContentRegionAvail();

        if (curResolution == Resolutions.fit) {
            if (renderSize.X != RenderTarget.Width || renderSize.Y != RenderTarget.Height) {
                rtWidth = (int)renderSize.X;
                rtHeight = (int)renderSize.Y;
                RefreshRenderTexture();
            }
        }

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
            if (Application.isPlaying || Time.frameCount % 8 == 0)
            {
                mainCam.Target = RenderTarget;
                mainCam.Render((int)renderSize.X, (int)renderSize.Y);
                mainCam.Target = null;
            }

            //ImGui.Image((IntPtr)RenderTarget.InternalTextures[0].Handle, renderSize, new System.Numerics.Vector2(0, 1), new System.Numerics.Vector2(1, 0));
            // Draw a Black background covering the full render size
            var min = ImGui.GetCursorScreenPos();
            var max = new System.Numerics.Vector2(min.X + renderSize.X, min.Y + renderSize.Y);
            ImGui.GetWindowDrawList().AddRectFilled(min, max, ImGui.GetColorU32(new System.Numerics.Vector4(0, 0, 0, 1)));
            // Letter box the image into the render size
            float aspect = (float)RenderTarget.Width / (float)RenderTarget.Height;
            float renderAspect = renderSize.X / renderSize.Y;
            if (aspect > renderAspect) {
                float width = renderSize.X;
                float height = width / aspect;
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + ((renderSize.Y - height) / 2f));
                ImGui.Image((IntPtr)RenderTarget.InternalTextures[0].Handle, new System.Numerics.Vector2(width, height), new System.Numerics.Vector2(0, 1), new System.Numerics.Vector2(1, 0));
            }
            else {
                float height = renderSize.Y;
                float width = height * aspect;
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ((renderSize.X - width) / 2f));
                ImGui.Image((IntPtr)RenderTarget.InternalTextures[0].Handle, new System.Numerics.Vector2(width, height), new System.Numerics.Vector2(0, 1), new System.Numerics.Vector2(1, 0));
            }


        
        }
    }

    protected override void Update()
    {

    }

    void UpdateResolution(Resolutions resolution)
    {
        switch (resolution) {
            case Resolutions._480p:
                rtWidth = 854;
                rtHeight = 480;
                break;
            case Resolutions._720p:
                rtWidth = 1280;
                rtHeight = 720;
                break;
            case Resolutions._1080p:
                rtWidth = 1920;
                rtHeight = 1080;
                break;
            case Resolutions._1440p:
                rtWidth = 2560;
                rtHeight = 1440;
                break;
            case Resolutions._2160p:
                rtWidth = 3840;
                rtHeight = 2160;
                break;
            case Resolutions._4320p:
                rtWidth = 7680;
                rtHeight = 4320;
                break;
            case Resolutions._480p_4_3:
                rtWidth = 640;
                rtHeight = 480;
                break;
            case Resolutions._720p_4_3:
                rtWidth = 960;
                rtHeight = 720;
                break;
            case Resolutions._1080p_4_3:
                rtWidth = 1440;
                rtHeight = 1080;
                break;
            case Resolutions._1440p_4_3:
                rtWidth = 1920;
                rtHeight = 1440;
                break;
            case Resolutions._2160p_4_3:
                rtWidth = 2880;
                rtHeight = 2160;
                break;
            case Resolutions._4320p_4_3:
                rtWidth = 5760;
                rtHeight = 4320;
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