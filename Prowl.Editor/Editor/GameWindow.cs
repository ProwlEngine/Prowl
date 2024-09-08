// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Preferences;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.SceneManagement;

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
    bool hasFrame = false;

    public static WeakReference LastFocused;
    public static Vector2 FocusedPosition;

    public readonly GameViewInputHandler InputHandler;

    public GameWindow() : base()
    {
        Title = FontAwesome6.Gamepad + " Game";
        GeneralPreferences.Instance.CurrentWidth = (int)Width;
        GeneralPreferences.Instance.CurrentHeight = (int)Height - HeaderHeight;

        RefreshRenderTexture();

        LastFocused = new WeakReference(this);
        InputHandler = new GameViewInputHandler(this);
    }

    ~GameWindow()
    {
        InputHandler.Dispose();
    }

    public void RefreshRenderTexture()
    {
        RenderTarget?.Dispose();

        RenderTarget = new RenderTexture(
            (uint)GeneralPreferences.Instance.CurrentWidth,
            (uint)GeneralPreferences.Instance.CurrentHeight,
            [Veldrid.PixelFormat.R8_G8_B8_A8_UNorm],
            Veldrid.PixelFormat.D24_UNorm_S8_UInt,
            true);

        hasFrame = false;
    }

    protected override void Draw()
    {
        if (!Project.HasProject) return;

        if (IsFocused)
            LastFocused = new WeakReference(this);
        InputHandler.EarlyUpdate();

        gui.CurrentNode.Layout(LayoutType.Column).ScaleChildren();

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

            if (GeneralPreferences.Instance.Resolution == Resolutions.fit)
            {
                var renderSize = innerRect.Size;
                renderSize.x = MathD.Max(renderSize.x, 1);
                renderSize.y = MathD.Max(renderSize.y, 1);
                if (renderSize.x != RenderTarget.Width || renderSize.y != RenderTarget.Height)
                {
                    GeneralPreferences.Instance.CurrentWidth = (int)renderSize.x;
                    GeneralPreferences.Instance.CurrentHeight = (int)renderSize.y;
                    RefreshRenderTexture();
                }
            }

            if (GeneralPreferences.Instance.AutoRefreshGameView || !hasFrame)
                if (!SceneManager.Draw(RenderTarget))
                {
                    gui.Draw2D.DrawRect(innerRect, Color.red, 2);
                    gui.Draw2D.DrawText(Font.DefaultFont, "No Camera found", 40f, innerRect, Color.red);
                    return;
                }

            hasFrame = true;

            // Letter box the image into the render size
            gui.Draw2D.DrawImage(RenderTarget.ColorBuffers[0], innerRect.Position, innerRect.Size, Color.white, true);

            if (IsFocused || LastFocused.Target == this)
            {
                FocusedPosition = innerRect.Position;
            }
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
