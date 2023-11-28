using Prowl.Runtime;
using Prowl.Editor.EditorWindows;
using HexaEngine.ImGuiNET;
using System.Numerics;

namespace Prowl.Editor; 

public static class EditorGui {

    public static void Initialize() {
        // todo: make windows stay docked https://github.com/mellinoe/ImGui.NET/issues/202
        ImGui.GetIO().ConfigFlags = ImGuiConfigFlags.DockingEnable;
        ImGui.GetIO().BackendFlags = ImGuiBackendFlags.HasMouseCursors;
        ImGui.GetIO().ConfigInputTextCursorBlink = true;
        ImGui.GetIO().ConfigWindowsResizeFromEdges = true;
        ImGui.GetIO().ConfigWindowsMoveFromTitleBarOnly = true;
        ImGui.GetIO().MouseDrawCursor = true;
        
        new EditorMainMenubar();
        new HierarchyWindow();
        new ViewportWindow();
        new InspectorWindow();
        new ConsoleWindow();
        new AssetBrowserWindow();
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
        var style = HexaEngine.ImGuiNET.ImGui.GetStyle();

        style.Alpha = 1.0f;
        style.DisabledAlpha = 0.5f;
        style.WindowPadding = new Vector2(4.0f, 3.0f);
        style.WindowRounding = 3.0f;
        style.WindowBorderSize = 0.01f;
        style.WindowMinSize = new Vector2(32.0f, 32.0f);
        style.WindowTitleAlign = new Vector2(0.0f, 0.5f);
        style.WindowMenuButtonPosition = ImGuiDir.Left;
        style.ChildRounding = 3.0f;
        style.ChildBorderSize = 0.0f;
        style.PopupRounding = 3.0f;
        style.PopupBorderSize = 1.0f;
        style.FramePadding = new Vector2(4.0f, 0.0f);
        style.FrameRounding = 3.0f;
        style.FrameBorderSize = 0.0f;
        style.ItemSpacing = new Vector2(4.0f, 4.0f);
        style.ItemInnerSpacing = new Vector2(4.0f, 4.0f);
        style.CellPadding = new Vector2(4.0f, 2.0f);
        style.IndentSpacing = 8.0f;
        style.ColumnsMinSpacing = 6.0f;
        style.ScrollbarSize = 15.0f;
        style.ScrollbarRounding = 6.0f;
        style.GrabMinSize = 10.0f;
        style.GrabRounding = 6.0f;
        style.TabRounding = 3.0f;
        style.TabBorderSize = 0.0f;
        //style.TabMinWidthForCloseButton = 0.0f;
        style.ColorButtonPosition = ImGuiDir.Right;
        style.ButtonTextAlign = new Vector2(0.5f, 0.5f);
        style.SelectableTextAlign = new Vector2(0.0f, 0.0f);

        style.WindowTitleAlign = new Vector2(0.5f, 0.5f);




        style.Colors[(int)ImGuiCol.Text] = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
        style.Colors[(int)ImGuiCol.TextDisabled] = new Vector4(0.5921568870544434f, 0.5921568870544434f, 0.5921568870544434f, 1.0f);
        style.Colors[(int)ImGuiCol.WindowBg] = new Vector4(0.13f, 0.13f, 0.14f, 1.00f);
        style.Colors[(int)ImGuiCol.ChildBg] = new Vector4(0.10f, 0.10f, 0.11f, 1.00f);
        style.Colors[(int)ImGuiCol.PopupBg] = new Vector4(0.10f, 0.10f, 0.11f, 1.00f);
        style.Colors[(int)ImGuiCol.Border] = new Vector4(0.00f, 0.00f, 0.00f, 0.50f);
        style.Colors[(int)ImGuiCol.BorderShadow] = new Vector4(0.20f, 0.20f, 0.20f, 1.00f);
        style.Colors[(int)ImGuiCol.FrameBg] = new Vector4(0.18f, 0.18f, 0.20f, 1.00f);
        style.Colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.1137254908680916f, 0.5921568870544434f, 0.9254902005195618f, 1.0f);
        style.Colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.0f, 0.4666666686534882f, 0.7843137383460999f, 1.0f);
        style.Colors[(int)ImGuiCol.TitleBg] = new Vector4(0.08f, 0.08f, 0.10f, 1.00f);
        style.Colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.10f, 0.10f, 0.13f, 1.00f);
        style.Colors[(int)ImGuiCol.TitleBgCollapsed] = new Vector4(0.10f, 0.10f, 0.13f, 1.00f);
        style.Colors[(int)ImGuiCol.MenuBarBg] = new Vector4(0.08f, 0.08f, 0.10f, 1.00f);
        style.Colors[(int)ImGuiCol.ScrollbarBg] = new Vector4(0.08f, 0.08f, 0.10f, 1.00f);
        style.Colors[(int)ImGuiCol.ScrollbarGrab] = new Vector4(0.14f, 0.14f, 0.18f, 1.00f);
        style.Colors[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4(0.14f, 0.14f, 0.22f, 1.00f);
        style.Colors[(int)ImGuiCol.ScrollbarGrabActive] = new Vector4(0.14f, 0.14f, 0.25f, 1.00f);
        style.Colors[(int)ImGuiCol.CheckMark] = new Vector4(0.0f, 0.4666666686534882f, 0.7843137383460999f, 1.0f);
        style.Colors[(int)ImGuiCol.SliderGrab] = new Vector4(0.00f, 0.47f, 0.78f, 1.00f);
        style.Colors[(int)ImGuiCol.SliderGrabActive] = new Vector4(0.00f, 0.60f, 1.00f, 1.00f);
        style.Colors[(int)ImGuiCol.Button] = new Vector4(0.22f, 0.22f, 0.22f, 1.00f);
        style.Colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.1137254908680916f, 0.5921568870544434f, 0.9254902005195618f, 1.0f);
        style.Colors[(int)ImGuiCol.ButtonActive] = new Vector4(0.1137254908680916f, 0.5921568870544434f, 0.9254902005195618f, 1.0f);
        style.Colors[(int)ImGuiCol.Header] = new Vector4(0.2000000029802322f, 0.2000000029802322f, 0.2156862765550613f, 1.0f);
        style.Colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.1137254908680916f, 0.5921568870544434f, 0.9254902005195618f, 1.0f);
        style.Colors[(int)ImGuiCol.HeaderActive] = new Vector4(0.0f, 0.4666666686534882f, 0.7843137383460999f, 1.0f);
        style.Colors[(int)ImGuiCol.Separator] = new Vector4(0.39f, 0.39f, 0.39f, 1.00f);
        style.Colors[(int)ImGuiCol.SeparatorHovered] = new Vector4(0.50f, 0.50f, 0.50f, 1.00f);
        style.Colors[(int)ImGuiCol.SeparatorActive] = new Vector4(0.71f, 0.71f, 0.71f, 1.00f);
        style.Colors[(int)ImGuiCol.ResizeGrip] = new Vector4(0.00f, 0.00f, 0.00f, 0.00f);
        style.Colors[(int)ImGuiCol.ResizeGripHovered] = new Vector4(0.25f, 0.25f, 0.50f, 1.00f);
        style.Colors[(int)ImGuiCol.ResizeGripActive] = new Vector4(0.50f, 0.50f, 1.00f, 1.00f);
        style.Colors[(int)ImGuiCol.Tab] = new Vector4(0.22f, 0.22f, 0.22f, 1.00f);
        style.Colors[(int)ImGuiCol.TabHovered] = new Vector4(0.1137254908680916f, 0.5921568870544434f, 0.9254902005195618f, 1.0f);
        style.Colors[(int)ImGuiCol.TabActive] = new Vector4(0.0f, 0.4666666686534882f, 0.7843137383460999f, 1.0f);
        style.Colors[(int)ImGuiCol.TabUnfocused] = new Vector4(0.1450980454683304f, 0.1450980454683304f, 0.1490196138620377f, 1.0f);
        style.Colors[(int)ImGuiCol.TabUnfocusedActive] = new Vector4(0.0f, 0.4666666686534882f, 0.7843137383460999f, 1.0f);
        style.Colors[(int)ImGuiCol.PlotLines] = new Vector4(0.0f, 0.4666666686534882f, 0.7843137383460999f, 1.0f);
        style.Colors[(int)ImGuiCol.PlotLinesHovered] = new Vector4(0.1137254908680916f, 0.5921568870544434f, 0.9254902005195618f, 1.0f);
        style.Colors[(int)ImGuiCol.PlotHistogram] = new Vector4(0.0f, 0.4666666686534882f, 0.7843137383460999f, 1.0f);
        style.Colors[(int)ImGuiCol.PlotHistogramHovered] = new Vector4(0.1137254908680916f, 0.5921568870544434f, 0.9254902005195618f, 1.0f);
        style.Colors[(int)ImGuiCol.TableHeaderBg] = new Vector4(0.08f, 0.08f, 0.10f, 1.00f);
        style.Colors[(int)ImGuiCol.TableBorderLight] = new Vector4(0.20f, 0.20f, 0.20f, 0.50f);
        style.Colors[(int)ImGuiCol.TextSelectedBg] = new Vector4(0.0f, 0.4666666686534882f, 0.7843137383460999f, 1.0f);
        style.Colors[(int)ImGuiCol.DragDropTarget] = new Vector4(0.1450980454683304f, 0.1450980454683304f, 0.1490196138620377f, 1.0f);
        style.Colors[(int)ImGuiCol.NavHighlight] = new Vector4(0.1450980454683304f, 0.1450980454683304f, 0.1490196138620377f, 1.0f);
        style.Colors[(int)ImGuiCol.NavWindowingHighlight] = new Vector4(1.0f, 1.0f, 1.0f, 0.699999988079071f);
        style.Colors[(int)ImGuiCol.NavWindowingDimBg] = new Vector4(0.800000011920929f, 0.800000011920929f, 0.800000011920929f, 0.2000000029802322f);
        style.Colors[(int)ImGuiCol.ModalWindowDimBg] = new Vector4(0.1450980454683304f, 0.1450980454683304f, 0.1490196138620377f, 1.0f);
    }

}
