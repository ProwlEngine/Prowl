using Prowl.Editor.Preferences;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.GUI.Graphics;
using static Prowl.Editor.EditorGUI;

namespace Prowl.Editor;

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

    public static WeakReference LastFocused;

    public GameWindow() : base()
    {
        Title = FontAwesome6.Gamepad + " Game";
        GeneralPreferences.Instance.CurrentWidth = (int)Width;
        GeneralPreferences.Instance.CurrentHeight = (int)Height - HeaderHeight;
        RefreshRenderTexture();
        LastFocused = new WeakReference(this);
    }

    public void RefreshRenderTexture()
    {
        RenderTarget?.Dispose();
        RenderTarget = new RenderTexture(GeneralPreferences.Instance.CurrentWidth, GeneralPreferences.Instance.CurrentHeight);
    }

    protected override void Draw()
    {
        if (!Project.HasProject) return;

        if(IsFocused)
            LastFocused = new WeakReference(this);

        gui.CurrentNode.Layout(Runtime.GUI.LayoutType.Column).ScaleChildren();

        using (gui.Node("MenuBar").ExpandWidth().MaxHeight(EditorStylePrefs.Instance.ItemSize).Layout(LayoutType.Row).Enter())
        {
            gui.TextNode("displayIcon", FontAwesome6.Display).Scale(EditorStylePrefs.Instance.ItemSize);

            bool changed = false;

            PropertyGridConfig config = PropertyGridConfig.NoLabel;
            if (EditorGUI.DrawProperty(0, "Width", ref GeneralPreferences.Instance.CurrentWidth, config))
            {
                GeneralPreferences.Instance.CurrentWidth = Math.Clamp(GeneralPreferences.Instance.CurrentWidth, 1, 7680);
                GeneralPreferences.Instance.Resolution = Resolutions.custom;
                changed = true;
                RefreshRenderTexture();
            }
            gui.PreviousNode.Width(50);
            if (EditorGUI.DrawProperty(1, "Height", ref GeneralPreferences.Instance.CurrentHeight, config))
            {
                GeneralPreferences.Instance.CurrentHeight = Math.Clamp(GeneralPreferences.Instance.CurrentHeight, 1, 4320);
                GeneralPreferences.Instance.Resolution = Resolutions.custom;
                changed = true;
                RefreshRenderTexture();
            }
            gui.PreviousNode.Width(50);

            if (EditorGUI.DrawProperty(2, "Resolution", ref GeneralPreferences.Instance.Resolution, config))
            {
                UpdateResolution(GeneralPreferences.Instance.Resolution);
                changed = true;
                RefreshRenderTexture();
            }
            gui.PreviousNode.Width(100);

            //changed |= EditorGUI.DrawProperty(3, "Auto Focus", ref GeneralPreferences.Instance.AutoFocusGameView, config);
            //g.PreviousNode.Width(100);
            //g.SimpleTooltip("Auto Focus will automatically focus the Game View when the game starts playing.");
            //changed |= EditorGUI.DrawProperty(4, "Auto Refresh", ref GeneralPreferences.Instance.AutoRefreshGameView, config);
            //g.PreviousNode.Width(100);
            //g.SimpleTooltip("Auto Refresh will automatically refresh the Game View.");

            if (changed)
            {
                GeneralPreferences.Instance.OnValidate();
                GeneralPreferences.Instance.Save();
            }
        }

        using (gui.Node("Main").Width(Size.Percentage(1f)).Padding(5).Enter())
        {
            var innerRect = gui.CurrentNode.LayoutData.InnerRect;

            gui.Draw2D.DrawRectFilled(innerRect, Color.black);

            var renderSize = innerRect.Size;
            renderSize.x = MathD.Max(renderSize.x, 1);
            renderSize.y = MathD.Max(renderSize.y, 1);

            // Find Camera to render
            var allCameras = EngineObject.FindObjectsOfType<Camera>();
            // Remove disabled ones
            allCameras = allCameras.Where(c => c.EnabledInHierarchy && !c.GameObject.Name.Equals("Editor-Camera", StringComparison.OrdinalIgnoreCase)).ToArray();
            // Find MainCamera
            var mainCam = allCameras.FirstOrDefault(c => c.GameObject.CompareTag("Main Camera") && c.Target.IsExplicitNull, allCameras.Length > 0 ? allCameras[0] : null);

            if (mainCam == null)
            {
                gui.Draw2D.DrawRect(innerRect, Color.red, 2);
                gui.Draw2D.DrawText(UIDrawList.DefaultFont, "No Camera found", 40f, innerRect, Color.red);
                return;
            }

            if (GeneralPreferences.Instance.Resolution == Resolutions.fit)
            {
                if (renderSize.x != RenderTarget.Width || renderSize.y != RenderTarget.Height)
                {
                    GeneralPreferences.Instance.CurrentWidth = (int)renderSize.x;
                    GeneralPreferences.Instance.CurrentHeight = (int)renderSize.y;
                    RefreshRenderTexture();
                }
            }

            // We got a camera to visualize
            if (GeneralPreferences.Instance.AutoRefreshGameView)
            {
                if (Application.isPlaying || Time.frameCount % 8 == 0)
                {
                    var tmp = mainCam.Target;
                    try
                    {
                        mainCam.Target = RenderTarget;
                        mainCam.Render((int)renderSize.x, (int)renderSize.y);
                    }
                    finally
                    {
                        mainCam.Target = tmp;
                    }
                    
                }
            }

            // Letter box the image into the render size
            gui.Draw2D.DrawImage(RenderTarget.InternalTextures[0], innerRect.Position, innerRect.Size, Color.white, true);
        }

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

}
