using Hexa.NET.ImGui;
using Hexa.NET.ImGuizmo;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.Assets;
using Silk.NET.GLFW;
using Silk.NET.Input;
using System.Reflection;

namespace Prowl.Editor.EditorWindows;

public class FileNamingWindow : EditorWindow
{
    public static bool WindowDrawnThisFrame = false;

    private string _searchText = "";
    private string createName = "";

    protected override ImGuiWindowFlags Flags { get; } = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.Tooltip | ImGuiWindowFlags.NoDecoration;
    protected override bool Center { get; } = true;
    protected override int Width { get; } = 512;
    protected override int Height { get; } = 64;
    protected override bool BackgroundFade { get; } = true;

    DirectoryInfo? Directory;
    string fileType = "";

    public FileNamingWindow(DirectoryInfo? Directory , string fileType) : base()
    {
        Title = FontAwesome6.Book + " Name file";
        Width = (int)ImGui.GetMousePos().X - 512;
        Height = -(int)ImGui.GetMousePos().Y + 256;
        this.Directory = Directory;
        this.fileType = fileType;
    }

    protected override void Draw()
    {
        WindowDrawnThisFrame = true;
        ImGui.SetWindowSize(new Vector2(480,32));
        ImGui.InputText("File name", ref createName, 0x100);
        ImGui.SameLine();
        if (ImGui.Button("Save file"))
        {
            if (fileType == "Script")
            {
                FileInfo file = new FileInfo(Directory + "/" + createName + ".cs");
                while (file.Exists)
                {
                    file = new FileInfo(file.FullName.Replace(".cs", "") + " new.cs");
                }
                using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Prowl.Editor.EmbeddedResources.NewScript.txt");
                using StreamReader reader = new StreamReader(stream);
                string script = reader.ReadToEnd();
                script = script.Replace("%SCRIPTNAME%", Utilities.FilterAlpha(Path.GetFileNameWithoutExtension(file.Name)));
                File.WriteAllText(file.FullName, script);
                var r = AssetDatabase.FileToRelative(file);
                AssetDatabase.Reimport(r);
                AssetDatabase.Ping(r);
                

            } else if (fileType == "Material")
            {
                Material mat = new Material(Shader.Find("Defaults/Standard.shader"));
                FileInfo file = new FileInfo(Directory + "/" +  createName + ".mat");
                while (file.Exists)
                {
                    file = new FileInfo(file.FullName.Replace(".mat", "") + " new.mat");
                }
                StringTagConverter.WriteToFile((CompoundTag)TagSerializer.Serialize(mat), file);

                var r = AssetDatabase.FileToRelative(file);
                AssetDatabase.Reimport(r);
                AssetDatabase.Ping(r);

            }
            else if (fileType == "Folder")
            {
                DirectoryInfo dir = new DirectoryInfo(Directory + "/" + createName);
                while (dir.Exists)
                {
                    dir = new DirectoryInfo(dir.FullName.Replace("New Folder", "New Folder new"));
                }
                dir.Create();

            }

            isOpened = false;
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
            isOpened = false;

        ImGui.PopStyleColor();
        ImGui.EndChild();



    }

    

}
