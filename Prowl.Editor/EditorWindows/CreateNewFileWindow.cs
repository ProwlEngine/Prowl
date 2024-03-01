using Hexa.NET.ImGui;
using Hexa.NET.ImGuizmo;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.Assets;
using Silk.NET.GLFW;
using Silk.NET.Input;
using System.IO;
using System.Reflection;

namespace Prowl.Editor.EditorWindows;

public class CreateNewFileWindow : EditorWindow
{
    public static bool WindowDrawnThisFrame = false;

    private string _searchText = "";
    private string createName = "";

    protected override ImGuiWindowFlags Flags { get; } = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.Tooltip | ImGuiWindowFlags.NoDecoration;
    protected override bool Center { get; } = true;
    protected override int Width { get; } = 512;
    protected override int Height { get; } = 64;
    protected override bool BackgroundFade { get; } = true;

    DirectoryInfo? directory;
    string fileType = "";
    Action<string, string> fileAction;

    public CreateNewFileWindow(DirectoryInfo? directory , Action<string, string> fileAction) : base()
    {
        Title = FontAwesome6.Book + " Name file";
        Width = (int)ImGui.GetMousePos().X - 512;
        Height = -(int)ImGui.GetMousePos().Y + 256;
        this.directory = directory;
        this.fileAction = fileAction;
         
    }

    protected override void Draw()
    {
        WindowDrawnThisFrame = true;
        ImGui.SetWindowSize(new Vector2(480,32));
        ImGui.InputText("File name", ref createName, 0x100);
        ImGui.SameLine();
        if (ImGui.Button("Save file"))
        {
            fileAction(directory.FullName, createName);
            isOpened = false;
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
            isOpened = false;

        ImGui.PopStyleColor();
        ImGui.EndChild();



    }

    

}
