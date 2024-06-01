using Prowl.Editor.Assets;
using Prowl.Editor;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.GUI.Graphics;
using Prowl.Runtime.SceneManagement;

namespace Prowl.Editor
{
    // TODO: Cache the directories and files - Do same for AssetsBrowserWindow
    // Something like:
    //public class DirectoryCache
    //{
    //    private class DirectoryNode(DirectoryInfo directory)
    //    {
    //        public DirectoryInfo Directory = directory;
    //        public List<DirectoryNode> SubDirectories = [];
    //        public List<FileInfo> Files = [];
    //    }
    //
    //    private DirectoryNode _rootNode;
    //
    //    public void Refresh(string rootPath)
    //    {
    //        DirectoryInfo rootDirectory = new DirectoryInfo(rootPath);
    //        _rootNode = BuildDirectoryTree(rootDirectory);
    //    }
    //
    //    private DirectoryNode BuildDirectoryTree(DirectoryInfo directory)
    //    {
    //        DirectoryNode node = new(directory);
    //        try
    //        {
    //            var directories = directory.GetDirectories();
    //            foreach (DirectoryInfo subDirectory in directories)
    //            {
    //                if (!subDirectory.Exists)
    //                    continue;
    //
    //                DirectoryNode subNode = BuildDirectoryTree(subDirectory);
    //                node.SubDirectories.Add(subNode);
    //            }
    //
    //            var files = directory.GetFiles();
    //            foreach (FileInfo file in files)
    //            {
    //                if (!File.Exists(file.FullName))
    //                    continue;
    //
    //                node.Files.Add(file);
    //            }
    //        }
    //        catch (Exception)
    //        {
    //            // Handle any exceptions that occur during directory traversal
    //        }
    //
    //        return node;
    //    }
    //
    //    public void TraverseDirectoryTree(Action<DirectoryInfo, int> directoryAction, Action<FileInfo> fileAction)
    //    {
    //        TraverseDirectoryNode(_rootNode, 0, directoryAction, fileAction);
    //    }
    //
    //    private void TraverseDirectoryNode(DirectoryNode node, int depth, Action<DirectoryInfo, int> directoryAction, Action<FileInfo> fileAction)
    //    {
    //        directoryAction(node.Directory, depth);
    //
    //        foreach (DirectoryNode subNode in node.SubDirectories)
    //            TraverseDirectoryNode(subNode, depth + 1, directoryAction, fileAction);
    //
    //        foreach (FileInfo file in node.Files)
    //            fileAction(file);
    //    }
    //}
    public class AssetsTreeWindow : EditorWindow
    {
        const double entryHeight = 30;
        const double entryPadding = 4;

        private string _searchText = "";
        private readonly List<FileInfo> _found = new();

        public readonly static SelectHandler<FileSystemInfo> SelectHandler = new((item) => !item.Exists, (a, b) => a.FullName.Equals(b.FullName, StringComparison.OrdinalIgnoreCase));
        internal static string? RenamingEntry = null;

        private int _treeCounter = 0;

        public AssetsTreeWindow() : base()
        {
            Title = FontAwesome6.FolderTree + " Asset Tree";
        }

        public static void StartRename(string? entry)
        {
            RenamingEntry = entry;
        }
        protected override void Draw()
        {
            if (!Project.HasProject)
                return;

            SelectHandler.StartFrame();

            g.CurrentNode.Layout(LayoutType.Column);
            g.CurrentNode.ScaleChildren();
            g.CurrentNode.Padding(0, 10, 10, 10);

            using (g.Node("Search").Width(Size.Percentage(1f)).MaxHeight(entryHeight).Clip().Enter())
            {
                if (g.Search("SearchInput", ref _searchText, 0, 0, Size.Percentage(1f, -entryHeight), entryHeight))
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
                var btnStyle = new GuiStyle();
                btnStyle.FontSize = 30;
                if (g.Button("CreateAssetBtn", FontAwesome6.CirclePlus, Offset.Percentage(1f, -entryHeight + 3), 0, entryHeight, entryHeight, btnStyle, true))
                {
                    g.OpenPopup("CreateOrImportAsset");
                }

                if (g.BeginPopup("CreateOrImportAsset", out var node))
                {
                    using (node.Width(100).Height(200).Layout(LayoutType.Column).FitContent().Enter())
                    {
                        // Import
                        // Create Menu
                    }
                }
            }


            using (g.Node("Tree").Width(Size.Percentage(1f)).MarginTop(5).Layout(LayoutType.Column, false).Enter())
            {
                //g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.WindowBackground * 0.5f, 10, 12);

                var dropInteract = g.GetInteractable();
                //HandleDrop();

                if (!SelectHandler.SelectedThisFrame && dropInteract.TakeFocus())
                    SelectHandler.Clear();

                //if (Hotkeys.IsHotkeyDown("Duplicate", new() { Key = Key.D, Ctrl = true }))
                //    DuplicateSelected();


                _treeCounter = 0;
                if (!string.IsNullOrEmpty(_searchText))
                {
                    foreach (var file in _found)
                    {
                        using (g.Node(file.FullName).Top(_treeCounter * (entryHeight + entryPadding)).Width(Size.Percentage(1f)).Height(entryHeight).Enter())
                        {
                            var interact = g.GetInteractable();
                            if (interact.TakeFocus())
                                SelectHandler.HandleSelectable(_treeCounter, file, true);

                            if (SelectHandler.IsSelected(file))
                                g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.Indigo);
                            else if (interact.IsHovered())
                                g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.Base5);

                            g.DrawText(UIDrawList.DefaultFont, file.Name, 20, new Vector2(g.CurrentNode.LayoutData.Rect.x + 40, g.CurrentNode.LayoutData.Rect.y + 7), GuiStyle.Base4);
                        }
                    }
                }
                else
                {
                    RenderRootFolder(true, AssetDatabase.GetRootFolders()[2], GuiStyle.Red); // Assets Folder
                    RenderRootFolder(false, AssetDatabase.GetRootFolders()[0], GuiStyle.Red); // Defaults Folder
                    RenderRootFolder(false, AssetDatabase.GetRootFolders()[1], GuiStyle.Red); // Packages Folder
                }

                g.ScrollV();
            }
        }

        private void RenderRootFolder(bool defaultOpen, DirectoryInfo root, Color col)
        {
            bool expanded = false;
            using (g.Node(root.Name).Top(_treeCounter * (entryHeight + entryPadding)).Width(Size.Percentage(1f)).Height(entryHeight).Margin(2, 0).Enter())
            {
                g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, col, 4);

                var interact = g.GetInteractable();
                if (interact.TakeFocus())
                    SelectHandler.HandleSelectable(_treeCounter++, root);

                if (SelectHandler.IsSelected(root))
                    g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.Indigo, 4);
                else if (interact.IsHovered())
                    g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.Base5, 4);

                expanded = g.GetStorage<bool>(root.FullName, defaultOpen);
                if (g.Button("ExpandBtn", expanded ? FontAwesome6.ChevronDown : FontAwesome6.ChevronRight, 5, 0, entryHeight, entryHeight, null, true))
                {
                    expanded = !expanded;
                    g.SetStorage(root.FullName, expanded);
                }

                g.DrawText(UIDrawList.DefaultFont, root.Name, 20, new Vector2(g.CurrentNode.LayoutData.Rect.x + 40, g.CurrentNode.LayoutData.Rect.y + 7), Color.white);
            }

            float scaleAnim = g.AnimateBool((ulong)root.FullName.GetHashCode(), expanded, 0.15f, EaseType.Linear);
            if (expanded || scaleAnim > 0f)
                DrawDirectory(root, 1, scaleAnim);
        }

        private void DrawDirectory(DirectoryInfo directory, int depth, float scaleHeight)
        {
            var directories = directory.GetDirectories();
            foreach (DirectoryInfo subDirectory in directories)
            {
                bool expanded = false;
                var left = depth * entryHeight;
                ulong subDirID = 0;
                // Directory Entry
                using (g.Node(subDirectory.Name, depth).Left(left).Top(_treeCounter * (entryHeight + entryPadding)).Width(Size.Percentage(1f, -left)).Height(entryHeight * scaleHeight).Margin(2, 0).Enter())
                {
                    subDirID = g.CurrentNode.ID;
                    if (_treeCounter++ % 2 == 0)
                        g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.Base4 * 0.6f, 4);
                    else
                        g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.Base4 * 0.8f, 4);

                    var interact = g.GetInteractable();
                    if (interact.TakeFocus())
                        SelectHandler.HandleSelectable(_treeCounter, subDirectory);

                    if (SelectHandler.IsSelected(subDirectory))
                        g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.Indigo, 4);
                    else if (interact.IsHovered())
                        g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.Base5, 4);

                    expanded = g.GetStorage<bool>(g.CurrentNode.Parent, subDirectory.FullName, false);
                    if (g.Button("ExpandBtn", expanded ? FontAwesome6.ChevronDown : FontAwesome6.ChevronRight, 5, 0, entryHeight, entryHeight, null, true))
                    {
                        expanded = !expanded;
                        g.SetStorage(g.CurrentNode.Parent, subDirectory.FullName, expanded);
                    }

                    g.DrawText(UIDrawList.DefaultFont, subDirectory.Name, 20, new Vector2(g.CurrentNode.LayoutData.Rect.x + 40, g.CurrentNode.LayoutData.Rect.y + 7), Color.white);
                }

                g.PushID(subDirID);
                float scaleAnim = g.AnimateBool((ulong)subDirectory.FullName.GetHashCode(), expanded, 0.15f, EaseType.Linear);
                if (expanded || scaleAnim > 0f)
                    DrawDirectory(subDirectory, depth + 1, scaleAnim);
                g.PopID();
            }

            var files = directory.GetFiles();
            foreach (FileInfo file in files)
            {
                string ext = file.Extension.ToLower().Trim();
                if (ext.Equals(".meta", StringComparison.OrdinalIgnoreCase))
                    continue;

                AssetDatabase.SubAssetCache[] subassets = Array.Empty<AssetDatabase.SubAssetCache>();
                if (AssetDatabase.TryGetGuid(file, out var guid))
                    subassets = AssetDatabase.GetSubAssetsCache(guid);

                bool expanded = false;
                var left = depth * entryHeight;
                ulong fileNodeID = 0;
                // File Entry
                using (g.Node(file.Name, depth).Left(left).Top(_treeCounter * (entryHeight + entryPadding)).Width(Size.Percentage(1f, -left)).Height(entryHeight * scaleHeight).Margin(2, 0).Enter())
                {
                    fileNodeID = g.CurrentNode.ID;
                    //if (_treeCounter++ % 2 == 0)
                    g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GetFileColor(ext) * 0.5f, 4);
                    //else
                    //    g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GetFileColor(ext, 0.6f, 1f), 4);

                    var interact = g.GetInteractable();
                    HandleFileClick(interact, file, 0);

                    if (SelectHandler.IsSelected(file))
                        g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.Indigo, 4);
                    else if (interact.IsHovered())
                        g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.Base5, 4);

                    if (subassets.Length > 1)
                    {
                        expanded = g.GetStorage<bool>(g.CurrentNode.Parent, file.FullName, false);
                        if (g.Button("ExpandBtn", expanded ? FontAwesome6.ChevronDown : FontAwesome6.ChevronRight, Offset.Percentage(1f, -entryHeight), 0, entryHeight, entryHeight, null, true))
                        {
                            expanded = !expanded;
                            g.SetStorage(g.CurrentNode.Parent, file.FullName, expanded);
                        }
                    }

                    g.DrawText(UIDrawList.DefaultFont, GetIcon(ext), 20, new Vector2(g.CurrentNode.LayoutData.Rect.x + (entryHeight / 2), g.CurrentNode.LayoutData.Rect.y + 7), GetFileColor(ext));
                    g.DrawText(UIDrawList.DefaultFont, file.Name, 20, new Vector2(g.CurrentNode.LayoutData.Rect.x + 40, g.CurrentNode.LayoutData.Rect.y + 7), Color.white);
                }

                // SubAssets
                if (expanded)
                {
                    g.PushID(fileNodeID);
                    left = (depth + 1) * entryHeight;

                    for (ushort i = 0; i < subassets.Length; i++)
                    {
                        if (subassets[i].type == null) continue;

                        // SubAsset Entry
                        using (g.Node(subassets[i].name, depth + 1 + i).Left(left).Top(_treeCounter * (entryHeight + entryPadding)).Width(Size.Percentage(1f, -left)).Height(entryHeight * scaleHeight).Margin(2, 0).Enter())
                        {
                            g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GetTypeColor(subassets[i].type!) * 0.5f, 4);

                            var interact = g.GetInteractable();
                            HandleFileClick(interact, file, i);

                            if (SelectHandler.IsSelected(file))
                                g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.Indigo, 4);
                            else if (interact.IsHovered())
                                g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.Base5, 4);

                            g.DrawText(UIDrawList.DefaultFont, GetIconForType(subassets[i].type!), 20, new Vector2(g.CurrentNode.LayoutData.Rect.x + (entryHeight / 2), g.CurrentNode.LayoutData.Rect.y + 7), GetTypeColor(subassets[i].type!));
                            g.DrawText(UIDrawList.DefaultFont, subassets[i].name, 20, new Vector2(g.CurrentNode.LayoutData.Rect.x + 40, g.CurrentNode.LayoutData.Rect.y + 7), Color.white);
                        }
                    }

                    g.PopID();
                }

            }
        }

        public static void HandleFileClick(Interactable interact, FileInfo entry, ushort fileID = 0)
        {
            Guid guid;
            bool isAsset = AssetDatabase.TryGetGuid(entry, out guid);

            if (isAsset && ImporterAttribute.SupportsExtension(entry.Extension))
            {
                if (DragnDrop.OnBeginDrag(out var node))
                {
                    var serialized = AssetDatabase.LoadAsset(guid);
                    using (node.Width(20).Height(20).Enter())
                    {
                        Gui.ActiveGUI.DrawList.PushClipRectFullScreen();
                        Gui.ActiveGUI.DrawText(UIDrawList.DefaultFont, FontAwesome6.BoxesPacking + "  " + GetIcon(entry.Extension) + "  " + serialized.GetAsset(fileID).Name, 30, node.LayoutData.InnerRect.Position, Color.white);
                        Gui.ActiveGUI.DrawList.PopClipRect();
                    }
                    DragnDrop.SetPayload(serialized.GetAsset(fileID), entry);
                }
            }
            else if (fileID == 0)
            {
                DragnDrop.Drag(entry);
            }

            if (fileID != 0) return;

            if (interact.TakeFocus())
                SelectHandler.Select(entry);

            if (isAsset && interact.IsHovered() && Gui.ActiveGUI.IsPointerDoubleClick(Silk.NET.Input.MouseButton.Left))
            {
                if (entry.Extension.Equals(".scene", StringComparison.OrdinalIgnoreCase))
                    SceneManager.LoadScene(new AssetRef<Runtime.Scene>(guid));
                else
                    AssetDatabase.OpenPath(entry);
            }
        }

        public static Color GetFileColor(string ext)
        {
            switch (ext)
            {
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
                    return GuiStyle.Sky;
                case ".ttf":
                    return GuiStyle.Pink;
                case ".obj":
                case ".blend":
                case ".dae":
                case ".fbx":
                case ".gltf":
                case ".ply":
                case ".pmx":
                case ".stl":
                    return GuiStyle.Orange;
                case ".scriptobj":
                    return GuiStyle.Yellow;
                case ".mat":
                    return GuiStyle.Fuchsia;
                case ".shader":
                    return GuiStyle.Red;
                case ".glsl":
                    return GuiStyle.Red;
                case ".md":
                case ".txt":
                    return GuiStyle.Emerald;
                case ".cs":
                    return GuiStyle.Blue;
                default:
                    return new Color(1.0f, 1.0f, 1.0f);
            }
        }

        public static Color GetTypeColor(Type type)
        {
            switch (type)
            {
                case Type t when t == typeof(Texture2D):
                    return GuiStyle.Sky;
                case Type t when t == typeof(Font):
                    return GuiStyle.Pink;
                case Type t when t == typeof(Mesh):
                    return GuiStyle.Orange;
                case Type t when t == typeof(ScriptableObject):
                    return GuiStyle.Yellow;
                case Type t when t == typeof(Material):
                    return GuiStyle.Fuchsia;
                case Type t when t == typeof(Shader):
                    return GuiStyle.Red;
                case Type t when t == typeof(TextAsset):
                    return GuiStyle.Emerald;
                case Type t when t == typeof(MonoScript):
                    return GuiStyle.Blue;
                default:
                    return new Color(1.0f, 1.0f, 1.0f);
            }
        }

        private static string GetIcon(string ext)
        {
            switch (ext)
            {
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
}