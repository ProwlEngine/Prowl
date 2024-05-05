using Hexa.NET.ImGui;
using Prowl.Editor.Assets;
using Prowl.Editor.Editor.Preferences;
using Prowl.Editor.ImGUI.Widgets;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.SceneManagement;
using System.Security.Cryptography.X509Certificates;

namespace Prowl.Editor.EditorWindows;

/// <summary>
/// Project Assets Tree Window
/// Shows all Folder and Files in a Tree Format
/// </summary>
public class AssetsWindow : OldEditorWindow
{
    protected override ImGuiWindowFlags Flags => ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse;

    private string _searchText = "";
    private readonly List<FileInfo> _found = new();

    public readonly static SelectHandler<FileSystemInfo> SelectHandler = new((item) => !item.Exists, (a, b) => a.FullName.Equals(b.FullName, StringComparison.OrdinalIgnoreCase));
    internal static string? RenamingEntry = null;

    private int _treeCounter = 0;

    public AssetsWindow() : base()
    {
        Title = FontAwesome6.FolderTree + " Assets";
    }

    public static void StartRename(string? entry)
    {
        RenamingEntry = entry;
    }

    protected override void Draw()
    {
        if (!Project.HasProject)
            return;

        ImGui.PushStyleColor(ImGuiCol.Header, EditorGui.SelectedColor);
        SelectHandler.StartFrame();

        float cPX = ImGui.GetCursorPosX();
        if (GUIHelper.SearchOld("##searchBox", ref _searchText, ImGui.GetContentRegionAvail().X))
        {
            SelectHandler.Clear();
            _found.Clear();
            if (!string.IsNullOrEmpty(_searchText))
            {
                _found.AddRange(AssetDatabase.GetRootFolders()[2].EnumerateFiles("*", SearchOption.AllDirectories)); // Assets
                _found.AddRange(AssetDatabase.GetRootFolders()[0].EnumerateFiles("*", SearchOption.AllDirectories)); // Defaults
                _found.AddRange(AssetDatabase.GetRootFolders()[1].EnumerateFiles("*", SearchOption.AllDirectories)); // Packages Folder
                _found.RemoveAll(f => f.Extension.Equals(".meta", StringComparison.OrdinalIgnoreCase) || !f.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase));
            }
        }

        ImGui.BeginChild("Tree");
        _treeCounter = 0;
        if (!string.IsNullOrEmpty(_searchText))
        {
            foreach (var file in _found)
            {
                ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.SpanFullWidth | ImGuiTreeNodeFlags.FramePadding;
                if (AssetsWindow.SelectHandler.IsSelected(file))
                    flags |= ImGuiTreeNodeFlags.Selected;

                string ext = file.Extension.ToLower().Trim();

                var curPos = ImGui.GetCursorPos();
                bool opened = ImGui.TreeNodeEx($"      {Path.GetFileNameWithoutExtension(file.Name)}", flags);
                SelectHandler.HandleSelectable(_treeCounter++, file);
                if (_treeCounter % 2 == 0)
                    GUIHelper.ItemRectFilled(0.5f, 0.5f, 0.5f, 0.1f);

                GUIHelper.Tooltip(file.Name);
                ImGui.PushStyleColor(ImGuiCol.Text, GetFileColor(ext));
                ImGui.SetCursorPos(new System.Numerics.Vector2(curPos.X + 26, curPos.Y));
                ImGui.TextUnformatted(GetIcon(ext));
                ImGui.PopStyleColor();
                if (opened)
                    ImGui.TreePop();

                if (RenamingEntry == file.FullName)
                {
                    string newName = file.Name;
                    if (ImGui.InputText("##Rename", ref newName, 255, ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll))
                    {
                        string newPath = Path.Combine(file.Directory.FullName, newName);
                        if (File.Exists(newPath))
                            Debug.LogError("A file with the same name already exists.");
                        else
                        {
                            AssetDatabase.Rename(file, newName);
                        }
                        RenamingEntry = null;
                    }
                    if (!ImGui.IsItemActive() && (ImGui.IsMouseClicked(ImGuiMouseButton.Left) || ImGui.IsMouseClicked(ImGuiMouseButton.Right)))
                        RenamingEntry = null;
                    ImGui.SetKeyboardFocusHere(-1);
                }
            }
        }
        else
        {
            RenderRootFolder(true, AssetDatabase.GetRootFolders()[2]); // Assets Folder
            RenderRootFolder(false, AssetDatabase.GetRootFolders()[0]); // Defaults Folder
            RenderRootFolder(true, AssetDatabase.GetRootFolders()[1]); // Packages Folder
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private void RenderRootFolder(bool defaultOpen, DirectoryInfo root)
    {
        ImGuiTreeNodeFlags rootFlags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanFullWidth | ImGuiTreeNodeFlags.FramePadding;
        if (defaultOpen)
            rootFlags |= ImGuiTreeNodeFlags.DefaultOpen;

        ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(250f / 255f, 210f / 255f, 100f / 255f, 1f));
        bool opened = ImGui.TreeNodeEx($"{FontAwesome6.FolderTree} {root.Name}", rootFlags);
        SelectHandler.HandleSelectable(_treeCounter++, root);
        GUIHelper.ItemRectFilled(1f, 1f, 1f, 0.2f);
        HandleFileContextMenu(null, null, false);
        ImGui.PopStyleColor();

        if (opened)
        {
            DrawDirectory(root);
            ImGui.TreePop();
        }
    }

    private void DrawDirectory(DirectoryInfo directory)
    {
        var directories = directory.GetDirectories();
        foreach (DirectoryInfo subDirectory in directories)
        {
            if (!subDirectory.Exists)
                continue;

            ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanFullWidth | ImGuiTreeNodeFlags.FramePadding;
            bool isLeaf = subDirectory.GetFiles().Length == 0 && subDirectory.GetDirectories().Length == 0;
            if (isLeaf)
                flags |= ImGuiTreeNodeFlags.Leaf;

            if (AssetsWindow.SelectHandler.IsSelected(subDirectory))
                flags |= ImGuiTreeNodeFlags.Selected;

            bool opened = ImGui.TreeNodeEx($"{(isLeaf ? FontAwesome6.FolderOpen : FontAwesome6.Folder)} {subDirectory.Name}", flags);
            SelectHandler.HandleSelectable(_treeCounter++, subDirectory);
            HandleFileContextMenu(subDirectory, null, false);
            GUIHelper.Tooltip(subDirectory.Name);

            if (_treeCounter % 2 == 0)
                GUIHelper.ItemRectFilled(0.5f, 0.5f, 0.5f, 0.1f);

            if (opened)
            {
                DrawDirectory(subDirectory);
                ImGui.TreePop();
            }

            if (RenamingEntry == subDirectory.FullName)
            {
                string newName = subDirectory.Name;
                if (ImGui.InputText("##Rename", ref newName, 255, ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll))
                {
                    string newPath = Path.Combine(subDirectory.Parent.FullName, newName);
                    if (Directory.Exists(newPath))
                        Debug.LogError("A directory with the same name already exists.");
                    else
                    {
                        subDirectory.MoveTo(newPath);
                    }
                    RenamingEntry = null;
                }
                if (!ImGui.IsItemActive() && (ImGui.IsMouseClicked(ImGuiMouseButton.Left) || ImGui.IsMouseClicked(ImGuiMouseButton.Right)))
                    RenamingEntry = null;
                ImGui.SetKeyboardFocusHere(-1);
            }
        }

        var files = directory.GetFiles();
        foreach (FileInfo file in files)
        {
            if (!File.Exists(file.FullName))
                continue;

            AssetDatabase.SubAssetCache[] subassets = Array.Empty<AssetDatabase.SubAssetCache>();
            if (AssetDatabase.TryGetGuid(file, out var guid))
                subassets = AssetDatabase.GetSubAssetsCache(guid);

            ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.SpanFullWidth | ImGuiTreeNodeFlags.FramePadding;
            if (subassets.Length > 1)
                flags |= ImGuiTreeNodeFlags.OpenOnArrow;
            else
                flags |= ImGuiTreeNodeFlags.Leaf;
            if (SelectHandler.IsSelected(file))
                flags |= ImGuiTreeNodeFlags.Selected;

            string ext = file.Extension.ToLower().Trim();
            if (ext.Equals(".meta", StringComparison.OrdinalIgnoreCase))
                continue;

            var curPos = ImGui.GetCursorPos();
            var name = AssetPipelinePreferences.Instance.HideExtensions ? Path.GetFileNameWithoutExtension(file.Name) : file.Name;
            bool opened = ImGui.TreeNodeEx($"      {name}", flags);
            SelectHandler.HandleSelectable(_treeCounter++, file);
            if (_treeCounter % 2 == 0)
                GUIHelper.ItemRectFilled(0.5f, 0.5f, 0.5f, 0.1f);
            if (ImGui.IsItemHovered())
                HandleFileClick(file);
            HandleFileContextMenu(file, null, false);
            GUIHelper.Tooltip(file.Name);
            ImGui.PushStyleColor(ImGuiCol.Text, GetFileColor(ext));
            ImGui.SetCursorPos(new System.Numerics.Vector2(curPos.X + 26, curPos.Y));
            ImGui.TextUnformatted(GetIcon(ext));
            ImGui.PopStyleColor();

            if (opened)
            {
                if (subassets.Length > 1)
                {
                    for (int i = 0; i < subassets.Length; i++)
                    {
                        if (subassets[i].type == null) continue;
                        ImGuiTreeNodeFlags subFlags = ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.SpanFullWidth | ImGuiTreeNodeFlags.FramePadding;

                        // Disabled text color
                        curPos = ImGui.GetCursorPos();
                        ImGui.PushStyleColor(ImGuiCol.Text, GetTypeColor(subassets[i].type!, 0.1f));
                        bool subOpened = ImGui.TreeNodeEx($"      {subassets[i].name}", subFlags);
                        if (_treeCounter % 2 == 0)
                            GUIHelper.ItemRectFilled(0.5f, 0.5f, 0.5f, 0.1f);
                        if (ImGui.IsItemHovered())
                            HandleFileClick(file, (ushort)i);

                        GUIHelper.Tooltip(subassets[i].name + " - T:" + subassets[i].type!.Name);
                        ImGui.SetCursorPos(new System.Numerics.Vector2(curPos.X + 26, curPos.Y));
                        ImGui.TextUnformatted(GetIconForType(subassets[i].type!));
                        ImGui.PopStyleColor();

                        if (subOpened)
                            ImGui.TreePop();
                    }
                }
                ImGui.TreePop();
            }

            if (RenamingEntry == file.FullName)
            {
                string newName = file.Name;
                if (ImGui.InputText("##Rename", ref newName, 255, ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll))
                {
                    string newPath = Path.Combine(file.Directory.FullName, newName);
                    if (File.Exists(newPath))
                        Debug.LogError("A file with the same name already exists.");
                    else
                    {
                        AssetDatabase.Rename(file, newName);
                    }
                    RenamingEntry = null;
                }
                if (!ImGui.IsItemActive() && (ImGui.IsMouseClicked(ImGuiMouseButton.Left) || ImGui.IsMouseClicked(ImGuiMouseButton.Right)))
                    RenamingEntry = null;
                ImGui.SetKeyboardFocusHere(-1);
            }
        }
    }

    public static void HandleFileClick(FileInfo entry, ushort fileID = 0)
    {
        Guid guid;
        bool isAsset = AssetDatabase.TryGetGuid(entry, out guid);

        if (isAsset && ImporterAttribute.SupportsExtension(entry.Extension))
        {
            if (DragnDrop.OnBeginDrag())
            {
                var serialized = AssetDatabase.LoadAsset(guid);
                DragnDrop.SetPayload(serialized.GetAsset(fileID), entry);
                DragnDrop.EndDrag();
            }
            //DragnDrop.Drag(serialized.Main, entry);
        }
        else if(fileID == 0)
        {
            DragnDrop.Drag(entry);
        }

        if (fileID != 0) return;

        if (ImGui.IsMouseReleased(0))
            AssetsWindow.SelectHandler.Select(entry);

        if (isAsset && ImGui.IsMouseDoubleClicked(0))
        {
            if (entry.Extension.Equals(".scene", StringComparison.OrdinalIgnoreCase))
                SceneManager.LoadScene(new AssetRef<Runtime.Scene>(guid));
            else
                AssetDatabase.OpenPath(entry);
        }
    }

    public static void HandleFileContextMenu(FileSystemInfo? fileInfo, DirectoryInfo? directory = null, bool fromAssetBrowser = false)
    {
        if (fileInfo == null)
        {
            if (ImGui.BeginPopupContextItem())
            {
                MainMenuItems.Directory = directory;
                MainMenuItems.fromAssetBrowser = fromAssetBrowser;
                MenuItem.DrawMenuRoot("Create");
                if (ImGui.MenuItem("Show In Explorer"))
                    AssetDatabase.OpenPath(new DirectoryInfo(Project.ProjectAssetDirectory));
                ImGui.Separator();
                if (ImGui.MenuItem("Reimport All"))
                    AssetDatabase.ReimportAll();

                ImGui.EndPopup();
            }
        }
        else if (fileInfo is FileInfo file)
        {
            if (ImGui.BeginPopupContextItem())
            {
                if (ImGui.MenuItem("Rename"))
                    if(fromAssetBrowser)
                    {
                        AssetBrowserWindow.StartRename(file.FullName);
                    }
                    else
                    {
                        StartRename(file.FullName);
                    }
                if (ImGui.MenuItem("Reimport"))
                    AssetDatabase.Reimport(file);
                ImGui.Separator();
                MainMenuItems.Directory = file.Directory;
                MainMenuItems.fromAssetBrowser = fromAssetBrowser;
                MenuItem.DrawMenuRoot("Create");
                if (ImGui.MenuItem("Show In Explorer"))
                    AssetDatabase.OpenPath(file.Directory);
                if (ImGui.MenuItem("Open"))
                    AssetDatabase.OpenPath(file);
                if (ImGui.MenuItem("Delete"))
                    file.Delete();
                ImGui.Separator();
                if (ImGui.MenuItem("Reimport All"))
                    AssetDatabase.ReimportAll();

                ImGui.EndPopup();
            }
        }
        else if (fileInfo is DirectoryInfo dir)
        {
            if (ImGui.BeginPopupContextItem())
            {
                if (ImGui.MenuItem("Rename"))
                    if (fromAssetBrowser)
                    {
                        AssetBrowserWindow.StartRename(dir.FullName);
                    }
                    else
                    {
                        StartRename(dir.FullName);
                    }
                if (ImGui.MenuItem("Reimport"))
                    AssetDatabase.ReimportFolder(dir);
                ImGui.Separator();
                MainMenuItems.Directory = dir;
                MainMenuItems.fromAssetBrowser = fromAssetBrowser;
                MenuItem.DrawMenuRoot("Create");
                if (ImGui.MenuItem("Show In Explorer"))
                    AssetDatabase.OpenPath(dir.Parent!);
                if (ImGui.MenuItem("Open"))
                    AssetDatabase.OpenPath(dir);
                if (ImGui.MenuItem("Delete"))
                    dir.Delete(true);
                ImGui.Separator();
                if (ImGui.MenuItem("Reimport All"))
                    AssetDatabase.ReimportAll();

                ImGui.EndPopup();
            }
        }
    }

    public static uint GetFileColor(string ext, float darkness = 0, float alpha = 0.6f)
    {
        byte a = (byte)(alpha * 255);
        float dark = 1 - darkness;
        switch (ext) {
            case ".png":
            case ".bmp":
            case ".jpg":
            case ".jpeg":
            case ".qoi":
            case ".psd":
            case ".tga":
            case ".dds":
            case ".hdr":
            case ".ktx":
            case ".pkm":
            case ".pvr":
                return ImGui.GetColorU32(new Color(31, 230, 71, a) * dark);
            case ".obj":
            case ".blend":
            case ".dae":
            case ".fbx":
            case ".gltf":
            case ".ply":
            case ".pmx":
            case ".stl":
                return ImGui.GetColorU32(new Color(243, 232, 47, a) * dark);
            case ".scriptobj":
                return ImGui.GetColorU32(new Color(245, 245, 1, a) * dark);
            case ".mat":
                return ImGui.GetColorU32(new Color(43, 211, 212, a) * dark);
            case ".shader":
                return ImGui.GetColorU32(new Color(239, 12, 106, a) * dark);
            case ".glsl":
                return ImGui.GetColorU32(new Color(254, 22, 2, a) * dark);
            case ".md":
            case ".txt":
                return ImGui.GetColorU32(new Color(228, 238, 5, a) * dark);
            case ".cs":
                return ImGui.GetColorU32(new Color(244, 101, 2, a) * dark);
            default:
                return ImGui.GetColorU32(new Color(1.0f, 1.0f, 1.0f, alpha) * dark);
        }
    }

    public static uint GetTypeColor(Type type, float darkness = 0, float alpha = 0.6f)
    {
        byte a = (byte)(alpha * 255);
        float dark = 1 - darkness;
        switch (type)
        {
            case Type t when t == typeof(Texture2D):
                return ImGui.GetColorU32(new Color(31, 230, 71, a) * dark);
            case Type t when t == typeof(Mesh):
                return ImGui.GetColorU32(new Color(243, 232, 47, a) * dark);
            case Type t when t == typeof(ScriptableObject):
                return ImGui.GetColorU32(new Color(245, 245, 1, a) * dark);
            case Type t when t == typeof(Material):
                return ImGui.GetColorU32(new Color(43, 211, 212, a) * dark);
            case Type t when t == typeof(Shader):
                return ImGui.GetColorU32(new Color(239, 12, 106, a) * dark);
            case Type t when t == typeof(TextAsset):
                return ImGui.GetColorU32(new Color(228, 238, 5, a) * dark);
            case Type t when t == typeof(MonoScript):
                return ImGui.GetColorU32(new Color(244, 101, 2, a) * dark);
            default:
                return ImGui.GetColorU32(new Color(1.0f, 1.0f, 1.0f, alpha) * dark);
        }
    }

    private static string GetIcon(string ext)
    {
        switch (ext) {
            case ".png":
            case ".bmp":
            case ".jpg":
            case ".jpeg":
            case ".qoi":
            case ".psd":
            case ".tga":
            case ".dds":
            case ".hdr":
            case ".ktx":
            case ".pkm":
            case ".pvr":
                return FontAwesome6.Image;
            case ".obj":
            case ".blend":
            case ".dae":
            case ".fbx":
            case ".gltf":
            case ".ply":
            case ".pmx":
            case ".stl":
                return FontAwesome6.Cube;
            case ".scriptobj":
                return FontAwesome6.Database;
            case ".mat":
                return FontAwesome6.Circle;
            case ".shader":
                return FontAwesome6.CameraRetro;
            case ".glsl":
                return FontAwesome6.DiagramNext;
            case ".md":
            case ".txt":
                return FontAwesome6.FileLines;
            case ".cs":
                return FontAwesome6.Code;
            default:
                return FontAwesome6.File;
        }
    }

    public string GetIconForType(Type type)
    {
        if (type == typeof(Texture2D))
            return FontAwesome6.Image;
        if (type == typeof(Mesh))
            return FontAwesome6.Cube;
        if (type == typeof(ScriptableObject))
            return FontAwesome6.Database;
        if (type == typeof(Material))
            return FontAwesome6.Circle;
        if (type == typeof(Shader))
            return FontAwesome6.CameraRetro;
        if (type == typeof(TextAsset))
            return FontAwesome6.FileLines;
        if (type == typeof(MonoScript))
            return FontAwesome6.Code;
        return FontAwesome6.File;
    }
}
