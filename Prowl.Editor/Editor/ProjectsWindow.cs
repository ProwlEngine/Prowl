using Assimp;
using Hexa.NET.ImGui;
using Prowl.Icons;
using Prowl.Runtime;

namespace Prowl.Editor.EditorWindows;

public class ProjectsWindow : EditorWindow
{
    public static bool WindowDrawnThisFrame = false;
    public string SelectedProject = "";
    private string _searchText = "";
    private string createName = "";
    private string[] tabNames = [FontAwesome6.RectangleList + "  Projects", FontAwesome6.PuzzlePiece + "  Create", FontAwesome6.BookOpen + "  Learn", FontAwesome6.DoorOpen + "  Quit"];
    private int currentTab = 0;

    protected override ImGuiWindowFlags Flags { get; } = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.Tooltip | ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoResize;
    protected override bool Center { get; } = true;
    protected override int Width { get; } = 512 + (512/2);
    protected override int Height { get; } = 512;
    protected override bool BackgroundFade { get; } = true;

    public ProjectsWindow() : base()
    {
        Title = FontAwesome6.Book + " Project Window";
    }

    protected override void PreWindowDraw()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new System.Numerics.Vector2(0, 0));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new System.Numerics.Vector2(0, 0));
        ImGui.PushStyleVar(ImGuiStyleVar.SeparatorTextPadding, new System.Numerics.Vector2(0, 0));
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 0);
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new System.Numerics.Vector2(0, 0));

        // rounding
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0);

    }

    protected override void PostWindowDraw()
    {
        ImGui.PopStyleVar(6);
        ImGui.PopStyleVar(1);
    }

    protected override void Draw()
    {
        WindowDrawnThisFrame = true;

        if (Project.HasProject)
            isOpened = false;


        ImGui.BeginChild("mainChild", new System.Numerics.Vector2(ImGui.GetWindowWidth(), ImGui.GetWindowHeight()), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar);

        ImGui.Columns(2, false);
        ImGui.SetColumnOffset(1, 150);

        // Side panel
        DrawSidePanel();

        ImGui.NextColumn();

        // Main content
        ImGui.BeginChild("mainContent");

        // Draw Background for child
        var drawList = ImGui.GetWindowDrawList();
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        drawList.AddRectFilled(windowPos, windowPos + windowSize, ImGui.GetColorU32(new System.Numerics.Vector4(0.1f, 0.1f, 0.1f, 0.5f)));

        if (currentTab == 0) // Projects tab
        {
            DrawProjectsTab();
        }
        else if (currentTab == 1) // Create tab
        {
            CreateProjectTab();
        }

        ImGui.EndChild();

        ImGui.EndChild();
    }

    private static void DrawShadow(float start, float height, float strength = 1f)
    {
        var windowPos = ImGui.GetWindowPos();
        var foregroundDrawList = ImGui.GetForegroundDrawList();
        //var sidePanelMax = ImGui.GetItemRectMax();
        var contentMin = windowPos + new System.Numerics.Vector2(-1, start);
        var contentMax = windowPos + new System.Numerics.Vector2(25 * strength, height);
        var gradientStart = ImGui.GetColorU32(new System.Numerics.Vector4(0.1f, 0.1f, 0.1f, 1f));
        var gradientEnd = ImGui.GetColorU32(new System.Numerics.Vector4(0.1f, 0.1f, 0.1f, 0f));
        int vertStartIdx = foregroundDrawList.VtxBuffer.Size;
        foregroundDrawList.AddRectFilled(contentMin, contentMax, gradientStart);
        int vertEndIdx = foregroundDrawList.VtxBuffer.Size;
        GUIHelper.ShadeVertsLinearColorGradient(foregroundDrawList, vertStartIdx, vertEndIdx, contentMin, new System.Numerics.Vector2(contentMax.X, contentMin.Y), gradientStart, gradientEnd);
    }

    private void CreateProjectTab()
    {

        var drawList = ImGui.GetWindowDrawList();
        // Draw background for bottom half of window
        var windowSize = ImGui.GetWindowSize();
        var windowPos = ImGui.GetWindowPos() + new System.Numerics.Vector2(0, windowSize.Y * 0.85f);
        var windowBG = ImGui.GetStyle().Colors[(int)ImGuiCol.WindowBg];
        drawList.AddRectFilled(windowPos, windowPos + windowSize, ImGui.GetColorU32(windowBG));

        DrawShadow(0, windowSize.Y * 0.85f);
        DrawShadow(windowSize.Y * 0.85f, windowSize.Y, 0.6f);

        ImGui.SetCursorPos(new System.Numerics.Vector2(30, 450));
        ImGui.Text("Name:");
        ImGui.SetCursorPos(new System.Numerics.Vector2(80, 450));
        ImGui.SetNextItemWidth(340);
        ImGui.InputText("##ProjectName", ref createName, 0x100);

        string path = Project.GetPath(createName).FullName;
        if (path.Length > 48)
            path = string.Concat("...", path.AsSpan(path.Length - 48));
        ImGui.SetCursorPos(new System.Numerics.Vector2(30, 480));
        ImGui.TextDisabled(path);

        // disable button rounding
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0);
        if (string.IsNullOrWhiteSpace(createName) || createName.Length < 3)
            ImGui.BeginDisabled();
        else
        {
            var style = ImGui.GetStyle();
            ImGui.PushStyleColor(ImGuiCol.Button, style.Colors[(int)ImGuiCol.ButtonHovered]);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, style.Colors[(int)ImGuiCol.ButtonActive]);
        }

        ImGui.SetCursorPos(new System.Numerics.Vector2(445, 435));
        if (ImGui.Button("Create", new System.Numerics.Vector2(172, 77)))
            Project.CreateNew(createName);

        if (string.IsNullOrWhiteSpace(createName) || createName.Length < 3)
            ImGui.EndDisabled();
        else
            ImGui.PopStyleColor(2);
        ImGui.PopStyleVar(1);
    }

    private void DrawProjectsTab()
    {
        var drawList = ImGui.GetWindowDrawList();
        // Draw background for bottom half of window
        var windowSize = ImGui.GetWindowSize();
        var windowPos = ImGui.GetWindowPos() + new System.Numerics.Vector2(0, windowSize.Y * 0.9f);
        var windowBG = ImGui.GetStyle().Colors[(int)ImGuiCol.WindowBg];
        drawList.AddRectFilled(windowPos, windowPos + windowSize, ImGui.GetColorU32(windowBG));

        DrawShadow(0, windowSize.Y * 0.9f);
        DrawShadow(windowSize.Y * 0.9f, windowSize.Y, 0.6f);

        //DrawShadow(0, ImGui.GetWindowHeight());

        ImGui.SetCursorPos(new System.Numerics.Vector2(25, 50));
        GUIHelper.Search("##searchBox", ref _searchText, 150);

        //ImGui.PushStyleColor(ImGuiCol.ChildBg, ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg]);
        ImGui.SetCursorPos(new System.Numerics.Vector2(25, 80));
        ImGui.BeginChild("projectList", new System.Numerics.Vector2(600, 380));

        var folders = new DirectoryInfo(Project.Projects_Directory).EnumerateDirectories();
        folders = folders.OrderByDescending((x) => x.LastWriteTimeUtc);

        foreach (var projectFolder in folders)
        {
            if (string.IsNullOrEmpty(_searchText) || projectFolder.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
            {
                DisplayProject(projectFolder.Name);
            }
            ImGui.Dummy(new(0, 10));
        }

        ImGui.EndChild();
        //ImGui.PopStyleColor();

        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0);
        if (string.IsNullOrWhiteSpace(SelectedProject))
            ImGui.BeginDisabled();
        else
        {
            var style = ImGui.GetStyle();
            ImGui.PushStyleColor(ImGuiCol.Button, style.Colors[(int)ImGuiCol.ButtonHovered]);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, style.Colors[(int)ImGuiCol.ButtonActive]);
        }

        ImGui.SetCursorPos(new System.Numerics.Vector2(455, 461));
        if (ImGui.Button("Open", new System.Numerics.Vector2(162, 51)))
        {
            Project.Open(SelectedProject);
            isOpened = false;
        }

        if (string.IsNullOrWhiteSpace(SelectedProject))
            ImGui.EndDisabled();
        else
            ImGui.PopStyleColor(2);
        ImGui.PopStyleVar(1);
    }

    private void DrawSidePanel()
    {
        ImGui.BeginChild("sidePanel", new System.Numerics.Vector2(150, ImGui.GetWindowHeight()), ImGuiChildFlags.Border);

        ImGui.SetWindowFontScale(2f);
        ImGui.SetCursorPosX((ImGui.GetWindowWidth() - ImGui.CalcTextSize("Prowl").X) * 0.45f);
        ImGui.Text("Prowl");
        ImGui.SetWindowFontScale(1f);

        for (int i = 0; i < tabNames.Length; i++)
        {
            if (i == 3)
                ImGui.SetCursorPosY(ImGui.GetWindowHeight() - (48 + 10));

            ImGui.SetCursorPosX(ImGui.GetCursorPosX() - 0);
            ImGui.BeginChild("##SidePanel" + i, new System.Numerics.Vector2(0, 48), ImGuiWindowFlags.NoScrollbar);
            GUIHelper.TextCenter(tabNames[i], 1f, true);
            ImGui.EndChild();
            if (currentTab == i)
                GUIHelper.ItemRectFilled(new Vector4(0.1f, 0.1f, 0.1f, 1f));
            else if (ImGui.IsItemHovered())
                GUIHelper.ItemRectFilled(new Vector4(0.1f, 0.1f, 0.1f, 0.5f));
            if (ImGui.IsItemClicked())
            {
                currentTab = i;
                if (i == 3)
                {
                    // Quit
#warning TODO: This actually crashes the editor rather then closing gracefully, Reason being is ImGUI Gets Disposed when this gets called, but we immediately then call ImGUI functions again causing a crash
                    Application.Quit();
                }
            }
        }
        ImGui.EndChild();
    }

    private void DisplayProject(string name)
    {
        var proj = Project.GetPath(name);

        ImGui.BeginChild(name, new System.Numerics.Vector2(570, 48));

        ImGui.SetCursorPos(new(8, 5));
        ImGui.Text(name);
        ImGui.SetCursorPos(new(8, 22));
        string path = proj.FullName;
        // Cut of the path if it's too long
        if (path.Length > 48)
            path = string.Concat("...", path.AsSpan(path.Length - 48));
        ImGui.TextDisabled(path);

        ImGui.SetCursorPos(new(ImGui.GetWindowWidth() - 125, 14));
        ImGui.TextDisabled(GetFormattedLastModifiedTime(proj.LastWriteTime));


        ImGui.EndChild();

        //GUIHelper.ItemRectFilled(new Vector4(0.1f, 0.1f, 0.1f, 1f), 0, 5);
        var drawList = ImGui.GetForegroundDrawList();
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        // Rectangle and Border

        if (SelectedProject == name)
        {
            drawList.AddRect(min, max, ImGui.GetColorU32(new Vector4(0.7f, 0.7f, 0.7f, 1f)), 5f);
        }
        else if(!ImGui.IsItemHovered())
        {
            drawList.AddRectFilled(min, max, ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.1f, 0.4f)));
        }

        // Select
        if (ImGui.IsItemClicked())
        {
            SelectedProject = name;
        }

        // Double click to open
        if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && ImGui.IsItemHovered())
        {
            Project.Open(name);
            isOpened = false;
        }
    }

    private string GetFormattedLastModifiedTime(DateTime lastModified)
    {
        TimeSpan timeSinceLastModified = DateTime.Now - lastModified;

        if (timeSinceLastModified.TotalMinutes < 1)
            return "Just now";
        else if (timeSinceLastModified.TotalMinutes < 60)
            return $"{(int)timeSinceLastModified.TotalMinutes} minutes ago";
        else if (timeSinceLastModified.TotalHours < 24)
            return $"{(int)timeSinceLastModified.TotalHours} hours ago";
        else
            return $"{(int)timeSinceLastModified.TotalDays} days ago";
    }
}