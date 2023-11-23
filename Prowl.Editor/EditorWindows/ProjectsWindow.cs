using Prowl.Icons;
using ImGuiNET;
using System.Numerics;

namespace Prowl.Editor.EditorWindows;

public class ProjectsWindow : EditorWindow
{
    public static bool WindowDrawnThisFrame = false;

    public string SelectedProject = "";
    public List<string> Projects = new();
    private string _searchText = "";
    private string createName = "";

    protected override ImGuiWindowFlags Flags { get; } = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.Tooltip | ImGuiWindowFlags.NoDecoration;
    protected override bool Center { get; } = true;
    protected override int Width { get; } = 512;
    protected override int Height { get; } = 512;
    protected override bool BackgroundFade { get; } = true;

    public ProjectsWindow()
    {
        Title = "Projects";
        Refresh();
    }

    private void Refresh()
    {
        // Make sure project directory exists
        Directory.CreateDirectory(Project.Projects_Directory);

        Projects.Clear();
        foreach (var project in Directory.GetDirectories(Project.Projects_Directory))
            Projects.Add(Path.GetFileName(project));

        Projects.Sort((x, y) => DateTime.Compare(
            Directory.GetLastWriteTime(Project.GetPath(y)),
            Directory.GetLastWriteTime(Project.GetPath(x))));
    }

    protected override void Draw()
    {
        WindowDrawnThisFrame = true;

        ImGui.BeginChild("projectChild");

        ImGui.AlignTextToFramePadding();
        ImGui.Text($"Search:");
        ImGui.SameLine();
        ImGui.PushItemWidth(ImGui.GetColumnWidth());
        if (ImGui.InputText("##searchBox", ref _searchText, 0x100))
            Refresh();

        ImGui.PopItemWidth();
        ImGui.PushStyleColor(ImGuiCol.ChildBg, ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg]);
        ImGui.BeginChild("projectList", new Vector2(ImGui.GetWindowWidth(), ImGui.GetWindowHeight() - 75));

        foreach (var dir in Projects)
            DisplayProject(dir);

        ImGui.EndChild();

        //if (ImGui.Button($"{FontAwesome6.File} New Project", new Vector2(ImGui.GetWindowWidth() / (Project.HasProject ? 2 : 1), 43)))
        //    ImGui.OpenPopup("CreateNewProject");

        //if (ImGui.BeginPopup("CreateNewProject"))
        {
            ImGui.InputText("Project Name", ref createName, 0x100);
            ImGui.SameLine();
            if (ImGui.Button("Create"))
            {
                Project.CreateNew(createName);
                ImGui.CloseCurrentPopup();
                Refresh();
            }
            ImGui.EndPopup();
        }

        if (Project.HasProject)
        {
            ImGui.SameLine();
            if (ImGui.Button($"{FontAwesome6.Xmark} Cancel", new Vector2(ImGui.GetWindowWidth()/2, 43)))
                isOpened = false;
        }

        ImGui.PopStyleColor();
        ImGui.EndChild();
    }

    private void DisplayProject(string name)
    {
        var item_size = 200;
        var pos = ImGui.GetCursorPos();
        if (ImGui.Selectable($"##{name}", SelectedProject == name, ImGuiSelectableFlags.None, new Vector2(ImGui.GetWindowWidth(), 14)))
            SelectedProject = name;

        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(0))
        {
            Project.Open(name);
            isOpened = false;
        }

        var endpos = ImGui.GetCursorPos();
        ImGui.SetCursorPos(pos);

        //Project name
        ImGui.SetCursorPos(new Vector2(pos.X + 5, pos.Y));
        ImGui.Text(name);

        ImGui.SameLine();

        ImGui.SetCursorPos(new Vector2(pos.X + item_size + 5, pos.Y));
        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1.0f), $"{FontAwesome6.Calendar} Modified: {Directory.GetLastWriteTime(Project.GetPath(name))}");

        ImGui.SetCursorPos(endpos);
    }

}
