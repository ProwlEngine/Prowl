using Prowl.Editor.Assets;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.GUI.Graphics;

namespace Prowl.Editor
{
    // TODO: Cache the directories and files - Do same for AssetBrowserWindow
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

            SelectHandler.StartFrame();

            g.CurrentNode.Layout(LayoutType.Column);
            g.CurrentNode.AutoScaleChildren();
            g.CurrentNode.Padding(0, 10, 10, 10);

            using (g.Node().Width(Size.Percentage(1f)).MaxHeight(entryHeight).Clip().Enter())
            {
                if (g.Search(ref _searchText, 0, 0, Size.Percentage(1f, -entryHeight), entryHeight))
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
                if (g.Button(FontAwesome6.CirclePlus, Offset.Percentage(1f, -entryHeight + 3), 0, entryHeight, entryHeight, btnStyle, true))
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


            using (g.Node().Width(Size.Percentage(1f)).MarginTop(5).Layout(LayoutType.Column, false).Enter())
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
                        using (g.Node().Top(_treeCounter * (entryHeight + entryPadding)).Width(Size.Percentage(1f)).Height(entryHeight).Enter())
                        {
                            if (_treeCounter++ % 2 == 0)
                                g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.Base4 * 0.8f);

                            var interact = g.GetInteractable();
                            if (interact.TakeFocus())
                                SelectHandler.HandleSelectable(_treeCounter, file, true);

                            if (SelectHandler.IsSelected(file))
                                g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.Indigo);
                            else if (interact.IsHovered())
                                g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.Base5);

                            g.DrawText(UIDrawList.DefaultFont, file.Name, 20, new Vector2(g.CurrentNode.LayoutData.Rect.x + 40, g.CurrentNode.LayoutData.Rect.y + 7), GuiStyle.Base4);
                            _treeCounter++;
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
            using (g.Node().Top(_treeCounter * (entryHeight + entryPadding)).Width(Size.Percentage(1f)).Height(entryHeight).Margin(2, 0).Enter())
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
                if (g.Button(expanded ? FontAwesome6.ChevronDown : FontAwesome6.ChevronRight, 5, 0, entryHeight, entryHeight, null, true))
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
                if (!subDirectory.Exists)
                    continue;

                bool expanded = false;
                var left = depth * entryHeight;
                using (g.Node().Left(left).Top(_treeCounter * (entryHeight + entryPadding)).Width(Size.Percentage(1f, -left)).Height(entryHeight * scaleHeight).Margin(2, 0).Enter())
                {
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
                    if (g.Button(expanded ? FontAwesome6.ChevronDown : FontAwesome6.ChevronRight, 5, 0, entryHeight, entryHeight, null, true))
                    {
                        expanded = !expanded;
                        g.SetStorage(g.CurrentNode.Parent, subDirectory.FullName, expanded);
                    }

                    g.DrawText(UIDrawList.DefaultFont, subDirectory.Name, 20, new Vector2(g.CurrentNode.LayoutData.Rect.x + 40, g.CurrentNode.LayoutData.Rect.y + 7), Color.white);
                }

                float scaleAnim = g.AnimateBool((ulong)subDirectory.FullName.GetHashCode(), expanded, 0.15f, EaseType.Linear);
                if (expanded || scaleAnim > 0f)
                    DrawDirectory(subDirectory, depth + 1, scaleAnim);
            }

            var files = directory.GetFiles();
            foreach (FileInfo file in files)
            {
                if (!File.Exists(file.FullName))
                    continue;

                AssetDatabase.SubAssetCache[] subassets = Array.Empty<AssetDatabase.SubAssetCache>();
                if (AssetDatabase.TryGetGuid(file, out var guid))
                    subassets = AssetDatabase.GetSubAssetsCache(guid);



            }
        }
    }
}