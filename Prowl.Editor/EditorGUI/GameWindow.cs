using Hexa.NET.ImGui;
using ImageMagick;
using Prowl.Editor.Editor.Preferences;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.GUI.Graphics;
using Prowl.Runtime.Rendering.OpenGL;
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

    public static bool IsFocused;

    public GameWindow() : base()
    {
        Title = FontAwesome6.Gamepad + " Game";
        GeneralPreferences.Instance.CurrentWidth = (int)Width;
        GeneralPreferences.Instance.CurrentHeight = (int)Height - HeaderHeight;
        RefreshRenderTexture();
    }

    public void RefreshRenderTexture()
    {
        RenderTarget?.Dispose();
        RenderTarget = new RenderTexture(GeneralPreferences.Instance.CurrentWidth, GeneralPreferences.Instance.CurrentHeight);
    }

    protected override void Draw()
    {
        if (!Project.HasProject) return;

        // if (!previouslyPlaying && Application.isPlaying)
        // {
        //     previouslyPlaying = true;
        //     if (GeneralPreferences.Instance.AutoFocusGameView)
        //         ImGg.SetWindowFocus();
        // }
        // else if (previouslyPlaying && !Application.isPlaying)
        // {
        //     previouslyPlaying = false;
        // }

        // IsFocused |= ImGg.IsWindowFocused();

        g.CurrentNode.Layout(Runtime.GUI.LayoutType.Column).AutoScaleChildren();

        using (g.Node().MaxHeight(HeaderHeight).Enter())
        {
            var innerRect = g.CurrentNode.LayoutData.InnerRect.Position;
            g.DrawText(FontAwesome6.Display, 20, innerRect, Color.white);
        }

        using (g.Node().Width(Size.Percentage(1f)).Padding(5).Enter())
        {
            var innerRect = g.CurrentNode.LayoutData.InnerRect;

            g.DrawRectFilled(innerRect, Color.black);

            var renderSize = innerRect;
            renderSize.width = Mathf.Max(renderSize.width, 1);
            renderSize.height = Mathf.Max(renderSize.height, 1);

            // Find Camera to render
            var allCameras = EngineObject.FindObjectsOfType<Camera>();
            // Remove disabled ones
            allCameras = allCameras.Where(c => c.EnabledInHierarchy && !c.GameObject.Name.Equals("Editor-Camera", StringComparison.OrdinalIgnoreCase)).ToArray();
            // Find MainCamera
            var mainCam = allCameras.FirstOrDefault(c => c.GameObject.CompareTag("Main Camera") && c.Target.IsExplicitNull, allCameras.Length > 0 ? allCameras[0] : null);

            if (mainCam == null)
            {
                g.DrawRect(innerRect, Color.red);
                g.DrawText(UIDrawList.DefaultFont, "No Camera found", 20f, innerRect, Color.red);
                return;
            }

            if (GeneralPreferences.Instance.Resolution == Resolutions.fit)
            {
                if (renderSize.width != RenderTarget.Width || renderSize.height != RenderTarget.Height)
                {
                    GeneralPreferences.Instance.CurrentWidth = (int)renderSize.width;
                    GeneralPreferences.Instance.CurrentHeight = (int)renderSize.height;
                    RefreshRenderTexture();
                }
            }

            // We got a camera to visualize
            if (GeneralPreferences.Instance.AutoRefreshGameView)
            {
                if (Application.isPlaying || Time.frameCount % 8 == 0)
                {
                    var tmp = mainCam.Target;
                    mainCam.Target = RenderTarget;
                    mainCam.Render((int)renderSize.width, (int)renderSize.height);
                    mainCam.Target = tmp;
                }
            }

            // Letter box the image into the render size
            double aspect = RenderTarget.Width / RenderTarget.Height;
            double renderAspect = renderSize.width / renderSize.height;
            if (aspect > renderAspect)
            {
                double width = renderSize.width;
                double height = width / aspect;
                // ImGg.SetCursorPosY(ImGg.GetCursorPosY() + ((float)(renderSize.height - height) / 2f));
                double yMin = innerRect.Position.y + ((renderSize.height - height) / 2f);
                //g.DrawImage(RenderTarget.InternalTextures[0], new Vector2(innerRect.Min.x, yMin), innerRect.Max, Color.black);
            }
            else
            {
                double height = renderSize.height;
                double width = height * aspect;
                double xMin = innerRect.Position.x + ((renderSize.width - width) / 2f);
                //g.DrawImage(RenderTarget.InternalTextures[0], new Vector2(xMin, innerRect.Min.y), innerRect.Max, Color.black);
            }
        }

        /*
        ImGg.BeginChild("Header", new System.Numerics.Vector2(0, HeaderHeight), ImGuiChildFlags.Border, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        {
            bool changed = false;
            ImGg.SetCursorPosY(ImGg.GetCursorPosY() + 3);
            ImGg.SetCursorPosX(ImGg.GetCursorPosX() + 5);
            ImGg.Text(FontAwesome6.Display);
            ImGg.SameLine();
            ImGg.SetNextItemWidth(50);
            if (ImGg.InputInt("##Width", ref GeneralPreferences.Instance.CurrentWidth, 0, 0, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                GeneralPreferences.Instance.CurrentWidth = Math.Clamp(GeneralPreferences.Instance.CurrentWidth, 1, 7680);
                GeneralPreferences.Instance.Resolution = Resolutions.custom;
                changed = true;
                RefreshRenderTexture();
            }
            ImGg.SameLine();
            ImGg.SetNextItemWidth(50);
            if (ImGg.InputInt("##Height", ref GeneralPreferences.Instance.CurrentHeight, 0, 0, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                GeneralPreferences.Instance.CurrentHeight = Math.Clamp(GeneralPreferences.Instance.CurrentHeight, 1, 4320);
                GeneralPreferences.Instance.Resolution = Resolutions.custom;
                changed = true;
                RefreshRenderTexture();
            }

            ImGg.SameLine();
            ImGg.SetNextItemWidth(100);
            string[] resolutionNames = Enum.GetValues(typeof(Resolutions)).Cast<Resolutions>().Select(r => GetDescription(r)).ToArray();
            int currentIndex = (int)GeneralPreferences.Instance.Resolution;
            if (ImGg.Combo("##ResolutionCombo", ref currentIndex, resolutionNames, resolutionNames.Length))
            {
                GeneralPreferences.Instance.Resolution = (Resolutions)Enum.GetValues(typeof(Resolutions)).GetValue(currentIndex);
                UpdateResolution(GeneralPreferences.Instance.Resolution);
                changed = true;
                RefreshRenderTexture();
            }

            ImGg.SameLine();
            // Auto Focus
            ImGg.SetCursorPosX(ImGg.GetWindowWidth() - 200);
            changed |= ImGg.Checkbox("Auto Focus", ref GeneralPreferences.Instance.AutoFocusGameView);
            ImGg.SameLine();
            // Auto Refresh
            changed |= ImGg.Checkbox("Auto Refresh", ref GeneralPreferences.Instance.AutoRefreshGameView);

            if (changed)
            {
                GeneralPreferences.Instance.OnValidate();
                GeneralPreferences.Instance.Save();
            }
        }
        ImGg.EndChild();

        var renderSize = ImGg.GetContentRegionAvail();

        var min = ImGg.GetCursorScreenPos();
        var max = new System.Numerics.Vector2(min.X + renderSize.X, min.Y + renderSize.Y);
        ImGg.GetWindowDrawList().AddRectFilled(min, max, ImGg.GetColorU32(new System.Numerics.Vector4(0, 0, 0, 1)));

        // Find Camera to render
        var allCameras = EngineObject.FindObjectsOfType<Camera>();
        // Remove disabled ones
        allCameras = allCameras.Where(c => c.EnabledInHierarchy && !c.GameObject.Name.Equals("Editor-Camera", StringComparison.OrdinalIgnoreCase)).ToArray();
        // Find MainCamera
        var mainCam = allCameras.FirstOrDefault(c => c.GameObject.CompareTag("Main Camera") && c.Target.IsExplicitNull, allCameras.Length > 0 ? allCameras[0] : null);

        if (mainCam == null)
        {
            GUIHelper.TextCenter("No Camera found", 2f, true);
            return;
        }
        if (GeneralPreferences.Instance.Resolution == Resolutions.fit)
        {
            if (renderSize.X != RenderTarget.Width || renderSize.Y != RenderTarget.Height)
            {
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
                var tmp = mainCam.Target;
                mainCam.Target = RenderTarget;
                mainCam.Render((int)renderSize.X, (int)renderSize.Y);
                mainCam.Target = tmp;
            }
        }

        // Letter box the image into the render size
        float aspect = (float)RenderTarget.Width / (float)RenderTarget.Height;
        float renderAspect = renderSize.X / renderSize.Y;
        if (aspect > renderAspect)
        {
            float width = renderSize.X;
            float height = width / aspect;
            ImGg.SetCursorPosY(ImGg.GetCursorPosY() + ((renderSize.Y - height) / 2f));
            ImGg.Image((IntPtr)(RenderTarget.InternalTextures[0].Handle as GLTexture)!.Handle, new System.Numerics.Vector2(width, height), new System.Numerics.Vector2(0, 1), new System.Numerics.Vector2(1, 0));
        }
        else
        {
            float height = renderSize.Y;
            float width = height * aspect;
            ImGg.SetCursorPosX(ImGg.GetCursorPosX() + ((renderSize.X - width) / 2f));
            ImGg.Image((IntPtr)(RenderTarget.InternalTextures[0].Handle as GLTexture)!.Handle, new System.Numerics.Vector2(width, height), new System.Numerics.Vector2(0, 1), new System.Numerics.Vector2(1, 0));
        }
        */
    }

    protected override void Update()
    {

    }

    void UpdateResolution(Resolutions resolution)
    {
        switch (resolution)
        {
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
