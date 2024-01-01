using HexaEngine.ImGuiNET;
using Prowl.Icons;
using Prowl.Runtime;

namespace Prowl.Editor.EditorWindows;

public class ProjectsWindow : EditorWindow
{
    public static bool WindowDrawnThisFrame = false;

    public string SelectedProject = "";
    private string _searchText = "";
    private string createName = "";

    protected override ImGuiWindowFlags Flags { get; } = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.Tooltip | ImGuiWindowFlags.NoDecoration;
    protected override bool Center { get; } = true;
    protected override int Width { get; } = 512;
    protected override int Height { get; } = 512;
    protected override bool BackgroundFade { get; } = true;

    public ProjectsWindow() : base()
    {
        Title = FontAwesome6.Book + " Projects";
    }

    protected override void Draw()
    {
        WindowDrawnThisFrame = true;

        ImGui.BeginChild("projectChild");

        GUIHelper.Search("##searchBox", ref _searchText, ImGui.GetContentRegionAvail().X);

        ImGui.PushStyleColor(ImGuiCol.ChildBg, ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg]);
        ImGui.BeginChild("projectList", new System.Numerics.Vector2(ImGui.GetWindowWidth(), ImGui.GetWindowHeight() - 75));

        var folders = new DirectoryInfo(Project.Projects_Directory).EnumerateDirectories();
        // sort by modified date
        folders = folders.OrderByDescending((x) => x.LastWriteTimeUtc);
        foreach (var projectFolder in folders)
            if (string.IsNullOrEmpty(_searchText) || projectFolder.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
                DisplayProject(projectFolder.Name);

        ImGui.EndChild();

        ImGui.InputText("Project Name", ref createName, 0x100);
        ImGui.SameLine();
        if (ImGui.Button("Create"))
        {
            Project.CreateNew(createName);
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();

        if (Project.HasProject)
        {
            if (ImGui.Button($"{FontAwesome6.Xmark} Cancel", new System.Numerics.Vector2(ImGui.GetWindowWidth() / 2, 43)))
                isOpened = false;
        }

        ImGui.PopStyleColor();
        ImGui.EndChild();
    }

    private void DisplayProject(string name)
    {
        var item_size = 200;
        var pos = ImGui.GetCursorPos();
        if (ImGui.Selectable($"##{name}", SelectedProject == name, ImGuiSelectableFlags.None, new System.Numerics.Vector2(ImGui.GetWindowWidth(), 14)))
            SelectedProject = name;

        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(0))
        {
            Project.Open(name);
            isOpened = false;
        }

        var endpos = ImGui.GetCursorPos();
        ImGui.SetCursorPos(pos);

        //Project name
        ImGui.SetCursorPos(new System.Numerics.Vector2(pos.X + 5, pos.Y));
        ImGui.Text(name);

        ImGui.SameLine();

        ImGui.SetCursorPos(new System.Numerics.Vector2(pos.X + item_size + 5, pos.Y));
        ImGui.TextColored(new System.Numerics.Vector4(0.5f, 0.5f, 0.5f, 1.0f), $"{FontAwesome6.Calendar} Modified: {Project.GetPath(name).LastWriteTime}");

        ImGui.SetCursorPos(endpos);
    }

}
