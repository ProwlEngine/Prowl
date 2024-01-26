using Hexa.NET.ImGui;
using Prowl.Editor.EditorWindows;
using Prowl.Runtime;
using System.Reflection;

namespace Prowl.Editor;

public static class EditorGui
{

    public static System.Numerics.Vector4 HoveredColor => new System.Numerics.Vector4(0.19f, 0.37f, 0.55f, 1.00f);
    public static System.Numerics.Vector4 SelectedColor => new System.Numerics.Vector4(0.06f, 0.53f, 0.98f, 1.00f);

    public static void Initialize()
    {
        // todo: make windows stay docked https://github.com/mellinoe/ImGui.NET/issues/202
        ImGui.GetIO().ConfigFlags = ImGuiConfigFlags.DockingEnable;
        ImGui.GetIO().BackendFlags = ImGuiBackendFlags.HasMouseCursors | ImGuiBackendFlags.RendererHasVtxOffset;
        ImGui.GetIO().ConfigInputTextCursorBlink = true;
        ImGui.GetIO().ConfigWindowsResizeFromEdges = true;
        ImGui.GetIO().ConfigWindowsMoveFromTitleBarOnly = true;
        ImGui.GetIO().MouseDrawCursor = true;
        Input.Mice[0].Cursor.CursorMode = Silk.NET.Input.CursorMode.Hidden;

        new EditorMainMenubar();
        new HierarchyWindow();
        new ViewportWindow();
        new GameWindow();
        new InspectorWindow();
        new ConsoleWindow();
        new AssetBrowserWindow();
        new AssetsWindow();
        //Program.EditorLayer.OnDraw += RenderDemoWindow;

        SetTheme();
    }

    public static void Update()
    {
        ImGuiFileDialog.UpdateDialogs();
        ImGuiNotify.RenderNotifications();
    }

    private static void SetTheme()
    {
        // Fork of Rounded Visual Studio style from ImThemes
        var style = ImGui.GetStyle();

        style.Colors[(int)ImGuiCol.Text] = new(1.00f, 1.00f, 1.00f, 1.00f);
        style.Colors[(int)ImGuiCol.TextDisabled] = new(0.50f, 0.50f, 0.50f, 1.00f);
        style.Colors[(int)ImGuiCol.WindowBg] = new(0.17f, 0.17f, 0.18f, 1f);
        style.Colors[(int)ImGuiCol.ChildBg] = new(0.17f, 0.17f, 0.18f, 0.00f);
        style.Colors[(int)ImGuiCol.PopupBg] = new(0.17f, 0.17f, 0.18f, 1f);
        style.Colors[(int)ImGuiCol.Border] = new(0.15f, 0.16f, 0.17f, 1.00f);
        style.Colors[(int)ImGuiCol.BorderShadow] = new(0.10f, 0.11f, 0.11f, 1.00f);
        style.Colors[(int)ImGuiCol.FrameBg] = new(0.10f, 0.11f, 0.11f, 1.00f);
        style.Colors[(int)ImGuiCol.FrameBgHovered] = HoveredColor;
        style.Colors[(int)ImGuiCol.FrameBgActive] = new(0.10f, 0.11f, 0.11f, 1.00f);
        style.Colors[(int)ImGuiCol.TitleBg] = new(0.08f, 0.08f, 0.09f, 1.00f);
        style.Colors[(int)ImGuiCol.TitleBgActive] = new(0.08f, 0.08f, 0.09f, 1.00f);
        style.Colors[(int)ImGuiCol.TitleBgCollapsed] = new(0.08f, 0.08f, 0.09f, 1.00f);
        style.Colors[(int)ImGuiCol.MenuBarBg] = new(0.08f, 0.08f, 0.09f, 1.00f);
        style.Colors[(int)ImGuiCol.ScrollbarBg] = new(0.10f, 0.11f, 0.11f, 1.00f);
        style.Colors[(int)ImGuiCol.ScrollbarGrab] = new(0.31f, 0.31f, 0.31f, 1.00f);
        style.Colors[(int)ImGuiCol.ScrollbarGrabHovered] = HoveredColor;
        style.Colors[(int)ImGuiCol.ScrollbarGrabActive] = SelectedColor;
        style.Colors[(int)ImGuiCol.CheckMark] = new(0.26f, 0.59f, 0.98f, 1.00f);
        style.Colors[(int)ImGuiCol.SliderGrab] = new(0.24f, 0.24f, 0.25f, 1.00f);
        style.Colors[(int)ImGuiCol.SliderGrabActive] = SelectedColor;
        style.Colors[(int)ImGuiCol.Button] = new(0.24f, 0.24f, 0.25f, 1.00f);
        style.Colors[(int)ImGuiCol.ButtonHovered] = HoveredColor;
        style.Colors[(int)ImGuiCol.ButtonActive] = SelectedColor;
        style.Colors[(int)ImGuiCol.Header] = new(0.10f, 0.11f, 0.11f, 1.00f);
        style.Colors[(int)ImGuiCol.HeaderHovered] = HoveredColor;
        style.Colors[(int)ImGuiCol.HeaderActive] = SelectedColor;
        style.Colors[(int)ImGuiCol.Separator] = new(0.43f, 0.43f, 0.50f, 0.50f);
        style.Colors[(int)ImGuiCol.SeparatorHovered] = HoveredColor;
        style.Colors[(int)ImGuiCol.SeparatorActive] = SelectedColor;
        style.Colors[(int)ImGuiCol.ResizeGrip] = new(0.26f, 0.59f, 0.98f, 0.20f);
        style.Colors[(int)ImGuiCol.ResizeGripHovered] = HoveredColor;
        style.Colors[(int)ImGuiCol.ResizeGripActive] = SelectedColor;
        style.Colors[(int)ImGuiCol.Tab] = new(0.08f, 0.08f, 0.09f, 1.00f);
        style.Colors[(int)ImGuiCol.TabHovered] = HoveredColor;
        style.Colors[(int)ImGuiCol.TabActive] = new(0.17f, 0.17f, 0.18f, 1.00f);
        style.Colors[(int)ImGuiCol.TabUnfocused] = new(0.08f, 0.08f, 0.09f, 1.00f);
        style.Colors[(int)ImGuiCol.TabUnfocusedActive] = new(0.17f, 0.17f, 0.18f, 1.00f);
        style.Colors[(int)ImGuiCol.DockingPreview] = new(0.26f, 0.59f, 0.98f, 0.70f);
        style.Colors[(int)ImGuiCol.DockingEmptyBg] = new(0.20f, 0.20f, 0.20f, 1.00f);
        style.Colors[(int)ImGuiCol.PlotLines] = new(0.61f, 0.61f, 0.61f, 1.00f);
        style.Colors[(int)ImGuiCol.PlotLinesHovered] = HoveredColor;
        style.Colors[(int)ImGuiCol.PlotHistogram] = new(0.90f, 0.70f, 0.00f, 1.00f);
        style.Colors[(int)ImGuiCol.PlotHistogramHovered] = HoveredColor;
        style.Colors[(int)ImGuiCol.TableHeaderBg] = new(0.19f, 0.19f, 0.20f, 1.00f);
        style.Colors[(int)ImGuiCol.TableBorderStrong] = new(0.31f, 0.31f, 0.35f, 1.00f);
        style.Colors[(int)ImGuiCol.TableBorderLight] = new(0.23f, 0.23f, 0.25f, 1.00f);
        style.Colors[(int)ImGuiCol.TableRowBg] = new(0.00f, 0.00f, 0.00f, 0.00f);
        style.Colors[(int)ImGuiCol.TableRowBgAlt] = new(1.00f, 1.00f, 1.00f, 0.06f);
        style.Colors[(int)ImGuiCol.TextSelectedBg] = new(0.26f, 0.59f, 0.98f, 0.35f);
        style.Colors[(int)ImGuiCol.DragDropTarget] = new(1.00f, 1.00f, 0.00f, 0.90f);
        style.Colors[(int)ImGuiCol.NavHighlight] = new(0.26f, 0.59f, 0.98f, 1.00f);
        style.Colors[(int)ImGuiCol.NavWindowingHighlight] = new(1.00f, 1.00f, 1.00f, 0.70f);
        style.Colors[(int)ImGuiCol.NavWindowingDimBg] = new(0.80f, 0.80f, 0.80f, 0.20f);
        style.Colors[(int)ImGuiCol.ModalWindowDimBg] = new(0.80f, 0.80f, 0.80f, 0.35f);

        style.WindowPadding = new Vector2(3.0f, 3.0f);
        style.FramePadding = new Vector2(6.0f, 2.0f);
        style.CellPadding = new Vector2(4.0f, 0.0f);
        style.ItemSpacing = new Vector2(4.0f, 3.0f);
        style.ItemInnerSpacing = new Vector2(4.0f, 4.0f);
        style.IndentSpacing = 10.0f;
        style.ScrollbarSize = 10.0f;
        style.GrabMinSize = 10.0f;

        style.WindowBorderSize = 0.0f;
        style.ChildBorderSize = 0.0f;
        style.PopupBorderSize = 0.0f;
        style.FrameBorderSize = 0.0f;
        style.TabBorderSize = 0.0f;

        style.WindowRounding = 3.0f;
        style.ChildRounding = 3.0f;
        style.PopupRounding = 3.0f;
        style.FrameRounding = 3.0f;
        style.GrabRounding = 6.0f;
        style.TabRounding = 3.0f;
        style.ScrollbarRounding = 6.0f;

        style.Alpha = 1.0f;
        style.DisabledAlpha = 0.5f;
        style.WindowMinSize = new Vector2(32.0f, 32.0f);
        style.WindowTitleAlign = new Vector2(0.5f, 0.5f);
        style.WindowMenuButtonPosition = ImGuiDir.None;
        style.ColumnsMinSpacing = 6.0f;
        //style.TabMinWidthForCloseButton = 0.0f;
        style.ColorButtonPosition = ImGuiDir.Right;
        // Causes assert failure on macOS
        //style.ButtonTextAlign = new Vector2(0.5f, 0.5f);
        style.SelectableTextAlign = new Vector2(0.0f, 0.0f);

    }

    public static void Notify(string title, string content = "", ImGuiToastType type = ImGuiToastType.None) => Notify(title, content, Color.white, type);
    public static void Notify(string title, string content, Color color, ImGuiToastType type = ImGuiToastType.None)
    {
        ImGuiNotify.InsertNotification(new ImGuiToast() { Title = title, Content = content, Color = color, Type = type });
    }


    #region ImGUI attributes

    public static void HandleBeginImGUIAttributes(IEnumerable<IImGUIAttri> attribs)
    {
        foreach (IImGUIAttri imGuiAttribute in attribs)
            switch (imGuiAttribute.AttribType()) {

                case GuiAttribType.Space:
                    ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 5);
                    break;

                case GuiAttribType.Text:
                    ImGui.Text((imGuiAttribute as TextAttribute).text);
                    break;

                case GuiAttribType.Separator:
                    ImGui.Separator();
                    break;

                case GuiAttribType.Sameline:
                    ImGui.SameLine();
                    break;

                case GuiAttribType.Disabled:
                    ImGui.BeginDisabled();
                    break;

                case GuiAttribType.Header:
                    ImGui.CollapsingHeader((imGuiAttribute as HeaderAttribute).name, ImGuiTreeNodeFlags.Leaf);
                    break;

                case GuiAttribType.StartGroup:
                    var group = (imGuiAttribute as StartGroupAttribute);
                    GUIHelper.TextCenter(group.name, group.headerSize);
                    ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 2);
                    ImGui.BeginChild(group.name, new System.Numerics.Vector2(-1, group.height), true, group.collapsable ? ImGuiWindowFlags.None : ImGuiWindowFlags.NoCollapse);
                    break;

            }
    }

    public static void HandleEndImGUIAttributes(IEnumerable<IImGUIAttri> attribs)
    {
        foreach (IImGUIAttri imGuiAttribute in attribs)
            switch (imGuiAttribute.AttribType()) {

                case GuiAttribType.Disabled:
                    ImGui.EndDisabled();
                    break;

                case GuiAttribType.EndGroup:
                    ImGui.EndChild();
                    break;

                case GuiAttribType.Tooltip:
                    GUIHelper.Tooltip((imGuiAttribute as TooltipAttribute).tooltip);
                    break;

            }
    }

    public static bool HandleAttributeButtons(object target)
    {
        foreach (MethodInfo method in target.GetType().GetMethods()) {
            var attribute = method.GetCustomAttribute<ImGUIButtonAttribute>();
            if (attribute != null)
                if (ImGui.Button(attribute.buttonText)) {
                    method.Invoke(target, null);
                    return true;
                }
        }
        return false;
    }

    #endregion
}
