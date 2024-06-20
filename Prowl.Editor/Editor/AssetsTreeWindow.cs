using Prowl.Editor.Assets;
using Prowl.Editor.Preferences;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.GUI.Graphics;
using Prowl.Runtime.GUI.Layout;
using Prowl.Runtime.SceneManagement;

namespace Prowl.Editor
{
    public class AssetsTreeWindow : EditorWindow
    {
        const double entryPadding = 4;

        private string _searchText = "";
        private readonly List<FileInfo> _found = new();

        public readonly static SelectHandler<FileSystemInfo> SelectHandler = new((item) => !item.Exists, (a, b) => a.FullName.Equals(b.FullName, StringComparison.OrdinalIgnoreCase));
        internal static string? RenamingEntry = null;

        private int _treeCounter = 0;
        private static bool justStartedRename = false;

        public AssetsTreeWindow() : base()
        {
            Title = FontAwesome6.FolderTree + " Asset Tree";
        }

        public static void StartRename(string? entry)
        {
            RenamingEntry = entry;
            justStartedRename = true;
        }

        protected override void Draw()
        {
            if (!Project.HasProject)
                return;

            SelectHandler.StartFrame();

            gui.CurrentNode.Layout(LayoutType.Column);
            gui.CurrentNode.ScaleChildren();
            gui.CurrentNode.Padding(0, 10, 10, 10);

            using (gui.Node("Search").Width(Size.Percentage(1f)).MaxHeight(GuiStyle.ItemHeight).Clip().Enter())
            {
                if (gui.Search("SearchInput", ref _searchText, 0, 0, Size.Percentage(1f, -GuiStyle.ItemHeight), GuiStyle.ItemHeight))
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

                using (gui.Node("CreateAssetBtn").Left(Offset.Percentage(1f, -GuiStyle.ItemHeight + 3)).Scale(GuiStyle.ItemHeight).Enter())
                {
                    if (gui.IsNodePressed())
                        gui.OpenPopup("CreateOrImportAsset");
                    gui.Draw2D.DrawText(FontAwesome6.CirclePlus, 30, gui.CurrentNode.LayoutData.Rect, (gui.IsNodeHovered() ? GuiStyle.Base11 : GuiStyle.Base5));

                    var popupHolder = gui.CurrentNode;
                    if (gui.BeginPopup("CreateOrImportAsset", out var node))
                    {
                        using (node.Width(180).Padding(5).Layout(LayoutType.Column).Spacing(5).FitContentHeight().Enter())
                        {
                            DrawContextMenu(null, null, false, popupHolder);
                        }
                    }
                }

            }


            using (gui.Node("Tree").Width(Size.Percentage(1f)).MarginTop(5).Layout(LayoutType.Column, false).Spacing(5).Clip().Enter())
            {
                gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GuiStyle.WindowBackground * 0.8f, 4);

                var dropInteract = gui.GetInteractable();
                //HandleDrop();

                //if (Hotkeys.IsHotkeyDown("Duplicate", new() { Key = Key.D, Ctrl = true }))
                //    DuplicateSelected();

                _treeCounter = 0;
                if (!string.IsNullOrEmpty(_searchText))
                {
                    foreach (var file in _found)
                    {
                        using (gui.Node(file.FullName).Top(_treeCounter * (GuiStyle.ItemHeight + entryPadding)).ExpandWidth(-gui.VScrollBarWidth()).Height(GuiStyle.ItemHeight).Enter())
                        {
                            SelectHandler.AddSelectableAtIndex(_treeCounter, file);
                            var interact = gui.GetInteractable();
                            if (interact.TakeFocus())
                                SelectHandler.Select(_treeCounter, file);

                            if (SelectHandler.IsSelected(file))
                                gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GuiStyle.Indigo);
                            else if (interact.IsHovered())
                                gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GuiStyle.Base5);

                            var textRect = gui.CurrentNode.LayoutData.Rect;
                            textRect.width -= GuiStyle.ItemHeight;
                            gui.Draw2D.DrawText(UIDrawList.DefaultFont, file.Name, 20, new Vector2(gui.CurrentNode.LayoutData.Rect.x + 40, gui.CurrentNode.LayoutData.Rect.y + 7), GuiStyle.Base4, 0, textRect);

                            _treeCounter++;
                        }
                    }
                }
                else
                {
                    RenderRootFolder(true, AssetDatabase.GetRootFolderCache(2), GuiStyle.RandomPastelColor(100)); // Assets Folder
                    RenderRootFolder(false, AssetDatabase.GetRootFolderCache(0), GuiStyle.RandomPastelColor(200)); // Defaults Folder
                    RenderRootFolder(false, AssetDatabase.GetRootFolderCache(1), GuiStyle.RandomPastelColor(500)); // Packages Folder
                }

                if (!SelectHandler.SelectedThisFrame && dropInteract.TakeFocus())
                    SelectHandler.Clear();


                gui.ScrollV();
            }
        }

        public static void DrawContextMenu(FileSystemInfo? fileInfo, DirectoryInfo? directory = null, bool fromAssetBrowser = false, LayoutNode popupHolder = null)
        {
            bool closePopup = false;

            if (fileInfo == null)
            {
                closePopup |= DrawCreateContextMenu(directory, fromAssetBrowser);
                DrawProjectContextMenu(ref closePopup);
            }
            else if (fileInfo is FileInfo file)
            {
                if (EditorGUI.StyledButton("Rename"))
                    if (fromAssetBrowser)
                    {
                        AssetsBrowserWindow.StartRename(file.FullName);
                        closePopup = true;
                    }
                    else
                    {
                        StartRename(file.FullName);
                        closePopup = true;
                    }
                if (EditorGUI.StyledButton("Reimport"))
                {
                    AssetDatabase.Reimport(file);
                    closePopup = true;
                }
                EditorGUI.Separator();
                if (EditorGUI.StyledButton("Show In Explorer"))
                {
                    AssetDatabase.OpenPath(file.Directory);
                    closePopup = true;
                }
                if (EditorGUI.StyledButton("Open"))
                {
                    AssetDatabase.OpenPath(file);
                    closePopup = true;
                }
                if (EditorGUI.StyledButton("Delete"))
                {
                    file.Delete();
                    closePopup = true;
                }

                closePopup |= DrawCreateContextMenu(file.Directory, fromAssetBrowser);
                EditorGUI.Separator();
                DrawProjectContextMenu(ref closePopup);
            }
            else if (fileInfo is DirectoryInfo dir)
            {
                if (EditorGUI.StyledButton("Rename"))
                    if (fromAssetBrowser)
                    {
                        AssetsBrowserWindow.StartRename(dir.FullName);
                        closePopup = true;
                    }
                    else
                    {
                        StartRename(dir.FullName);
                        closePopup = true;
                    }
                if (EditorGUI.StyledButton("Reimport"))
                {
                    AssetDatabase.ReimportFolder(dir);
                    closePopup = true;
                }
                EditorGUI.Separator();
                if (EditorGUI.StyledButton("Show In Explorer"))
                {
                    AssetDatabase.OpenPath(dir.Parent!);
                    closePopup = true;
                }
                if (EditorGUI.StyledButton("Delete"))
                {
                    dir.Delete(true);
                    closePopup = true;
                }
                EditorGUI.Separator();

                closePopup |= DrawCreateContextMenu(dir, fromAssetBrowser);
                EditorGUI.Separator();
                DrawProjectContextMenu(ref closePopup);
            }

            if (closePopup)
                Gui.ActiveGUI.ClosePopup(popupHolder);
        }

        private static bool DrawCreateContextMenu(DirectoryInfo? directory, bool fromAssetBrowser)
        {
            EditorGuiManager.Directory = directory;
            EditorGuiManager.fromAssetBrowser = fromAssetBrowser;
            return MenuItem.DrawMenuRoot("Create");
        }

        private static void DrawProjectContextMenu(ref bool closePopup)
        {
            EditorGUI.Text("Editor");

            if (EditorGUI.StyledButton("Refresh"))
            {
                AssetDatabase.Update(true, true);
                closePopup = true;
            }

            EditorGUI.Text("Project");

            if (EditorGUI.StyledButton("Reimport All"))
            {
                AssetDatabase.ReimportAll();
                closePopup = true;
            }
            if (EditorGUI.StyledButton("Recompile Project"))
            {
                Program.RegisterReloadOfExternalAssemblies();
                closePopup = true;
            }
            if (EditorGUI.StyledButton("Show Project In Explorer"))
            {
                AssetDatabase.OpenPath(new DirectoryInfo(Project.ProjectAssetDirectory));
                closePopup = true;
            }
        }

        private void RenderRootFolder(bool defaultOpen, AssetDirectoryCache root, Color col)
        {
            bool expanded = false;
            using (gui.Node(root.RootName).Top(_treeCounter * (GuiStyle.ItemHeight + entryPadding)).ExpandWidth(-gui.VScrollBarWidth()).Height(GuiStyle.ItemHeight).Enter())
            {
                gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, col, 4);

                SelectHandler.AddSelectableAtIndex(_treeCounter, root.Root);
                var interact = gui.GetInteractable();
                if (interact.TakeFocus())
                    SelectHandler.Select(_treeCounter, root.Root);

                if (SelectHandler.IsSelected(root.Root))
                    gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GuiStyle.Indigo, 4);
                else if (interact.IsHovered())
                    gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GuiStyle.Base5, 4);

                if (DragnDrop.Drop<FileSystemInfo>(out var systeminfo))
                {
                    string target = Path.Combine(root.RootDirectoryPath, systeminfo.Name);
                    if (systeminfo is FileInfo file)
                        AssetDatabase.Move(file, target);
                    else if (systeminfo is DirectoryInfo dir)
                        AssetDatabase.Move(dir, target);
                }

                expanded = gui.GetNodeStorage<bool>(root.RootDirectoryPath, defaultOpen);
                using (gui.Node("ExpandBtn").TopLeft(5, 0).Scale(GuiStyle.ItemHeight).Enter())
                {
                    if (gui.IsNodePressed())
                    {
                        expanded = !expanded;
                        gui.SetNodeStorage(gui.CurrentNode.Parent, root.RootDirectoryPath, expanded);
                    }
                    gui.Draw2D.DrawText(expanded ? FontAwesome6.ChevronDown : FontAwesome6.ChevronRight, 20, gui.CurrentNode.LayoutData.Rect, gui.IsNodeHovered() ? GuiStyle.Base4 : GuiStyle.Base11);
                }

                gui.Draw2D.DrawText(UIDrawList.DefaultFont, root.RootName, 20, new Vector2(gui.CurrentNode.LayoutData.Rect.x + 40, gui.CurrentNode.LayoutData.Rect.y + 7), Color.white);

                _treeCounter++;
            }

            float scaleAnim = gui.AnimateBool((ulong)root.RootDirectoryPath.GetHashCode(), expanded, 0.15f, EaseType.Linear);
            if (expanded || scaleAnim > 0f)
                DrawDirectory(root.RootNode, 1, scaleAnim);
        }

        private Dictionary<string, bool> _ = new();

        private void DrawDirectory(AssetDirectoryCache.DirNode dirNode, int depth, float scaleHeight)
        {
            var subDirs = dirNode.SubDirectories;
            foreach (var subDirNode in subDirs)
            {
                var subDirectory = subDirNode.Directory;
                bool expanded = false;
                var left = depth * GuiStyle.ItemHeight;
                ulong subDirID = 0;
                // Directory Entry
                using (gui.Node(subDirectory.Name, depth).Left(left).Top(_treeCounter * (GuiStyle.ItemHeight + entryPadding)).ExpandWidth(-(left + gui.VScrollBarWidth())).Height(GuiStyle.ItemHeight * scaleHeight).Enter())
                {
                    subDirID = gui.CurrentNode.ID;
                    if (_treeCounter % 2 == 0)
                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GuiStyle.Base4 * 0.6f, 4);
                    else
                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GuiStyle.Base4 * 0.8f, 4);

                    SelectHandler.AddSelectableAtIndex(_treeCounter, subDirectory);
                    var interact = gui.GetInteractable();
                    if (interact.TakeFocus())
                        SelectHandler.Select(_treeCounter, subDirectory);

                    if (SelectHandler.IsSelected(subDirectory))
                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GuiStyle.Indigo, 4);
                    else if (interact.IsHovered())
                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GuiStyle.Base5, 4);

                    DragnDrop.Drag(subDirectory);

                    if (gui.IsNodeHovered() && gui.IsPointerClick(Silk.NET.Input.MouseButton.Right))
                        gui.OpenPopup("TreeRightClickAsset");
                    var popupHolder = gui.CurrentNode;
                    if (gui.BeginPopup("TreeRightClickAsset", out var node))
                        using (node.Width(180).Padding(5).Layout(LayoutType.Column).Spacing(5).FitContentHeight().Enter())
                            DrawContextMenu(subDirectory, null, false, popupHolder);

                    if (subDirectory.Exists)
                    {
                        if (DragnDrop.Drop<FileSystemInfo>(out var systeminfo))
                        {
                            string target = Path.Combine(subDirectory.FullName, systeminfo.Name);
                            if(systeminfo is FileInfo file)
                                AssetDatabase.Move(file, target);
                            else if(systeminfo is DirectoryInfo dir)
                                AssetDatabase.Move(dir, target);
                        }
                    }

                    expanded = gui.GetNodeStorage<bool>(gui.CurrentNode.Parent, subDirectory.FullName, false);
                    using (gui.Node("ExpandBtn").TopLeft(5, 0).Scale(GuiStyle.ItemHeight).Enter())
                    {
                        if (gui.IsNodePressed())
                        {
                            expanded = !expanded;
                            gui.SetNodeStorage(gui.CurrentNode.Parent.Parent, subDirectory.FullName, expanded);
                        }
                        gui.Draw2D.DrawText(expanded ? FontAwesome6.ChevronDown : FontAwesome6.ChevronRight, 20, gui.CurrentNode.LayoutData.Rect, gui.IsNodeHovered() ? GuiStyle.Base4 : GuiStyle.Base11);
                    }

                    if (RenamingEntry == subDirectory.FullName)
                    {
                        var rect = gui.CurrentNode.LayoutData.Rect;
                        var inputRect = new Rect(rect.x + 33, rect.y + 4, rect.width - 40, 30 - 8);
                        gui.Draw2D.DrawRectFilled(inputRect, GuiStyle.WindowBackground, 8);
                        var name = Path.GetFileNameWithoutExtension(subDirectory.Name);
                        bool changed = gui.InputField("RenameInput", ref name, 64, Gui.InputFieldFlags.EnterReturnsTrue, 30, 0, Size.Percentage(1f), null, null, true);
                        if (justStartedRename)
                            gui.FocusPreviousInteractable();
                        if (!gui.PreviousInteractableIsFocus())
                            RenamingEntry = null;
                        //subDirectory.Name = name;
                        if (changed && !string.IsNullOrEmpty(name))
                        {
                            AssetDatabase.Rename(subDirectory, name);
                            RenamingEntry = null;
                        }

                        justStartedRename = false;
                    }
                    else
                    {
                        gui.Draw2D.DrawText(subDirectory.Name, new Vector2(gui.CurrentNode.LayoutData.Rect.x + 40, gui.CurrentNode.LayoutData.Rect.y + 7));
                    }
                    _treeCounter++;
                }

                gui.PushID(subDirID);
                float scaleAnim = gui.AnimateBool((ulong)subDirectory.FullName.GetHashCode(), expanded, 0.15f, EaseType.Linear);
                if (expanded || scaleAnim > 0f)
                    DrawDirectory(subDirNode, depth + 1, scaleAnim);
                gui.PopID();
            }

            var subFiles = dirNode.Files;
            foreach (var subFileNode in subFiles)
            {
                var subFile = subFileNode.File;
                string ext = subFile.Extension.ToLower().Trim();
                if (ext.Equals(".meta", StringComparison.OrdinalIgnoreCase))
                    continue;

                bool expanded = false;
                var left = depth * GuiStyle.ItemHeight;
                ulong fileNodeID = 0;
                // File Entry
                using (gui.Node(subFile.Name, depth).Left(left).Top(_treeCounter * (GuiStyle.ItemHeight + entryPadding)).ExpandWidth(-(left + gui.VScrollBarWidth())).Height(GuiStyle.ItemHeight * scaleHeight).Enter())
                {
                    fileNodeID = gui.CurrentNode.ID;
                    //if (_treeCounter++ % 2 == 0)
                    gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GetFileColor(ext) * 0.5f, 4);
                    //else
                    //    g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GetFileColor(ext, 0.6f, 1f), 4);

                    var interact = gui.GetInteractable();
                    HandleFileClick(_treeCounter, interact, subFileNode, 0);

                    if (SelectHandler.IsSelected(subFile))
                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GuiStyle.Indigo, 4);
                    else if (interact.IsHovered())
                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GuiStyle.Base5, 4);

                    if (subFileNode.SubAssets.Length > 1)
                    {
                        expanded = gui.GetNodeStorage<bool>(gui.CurrentNode.Parent, subFile.FullName, false);
                        using (gui.Node("ExpandBtn").TopLeft(Offset.Percentage(1f, -GuiStyle.ItemHeight), 0).Scale(GuiStyle.ItemHeight).Enter())
                        {
                            if (gui.IsNodePressed())
                            {
                                expanded = !expanded;
                                gui.SetNodeStorage(gui.CurrentNode.Parent.Parent, subFile.FullName, expanded);
                            }
                            gui.Draw2D.DrawText(expanded ? FontAwesome6.ChevronDown : FontAwesome6.ChevronRight, 20, gui.CurrentNode.LayoutData.Rect, gui.IsNodeHovered() ? GuiStyle.Base4 : GuiStyle.Base11);
                        }
                    }

                    var textRect = gui.CurrentNode.LayoutData.Rect;
                    if (subFileNode.SubAssets.Length > 1)
                        textRect.width -= GuiStyle.ItemHeight;
                    gui.Draw2D.DrawText(UIDrawList.DefaultFont, GetIcon(ext), 20, new Vector2(gui.CurrentNode.LayoutData.Rect.x + (GuiStyle.ItemHeight / 2), gui.CurrentNode.LayoutData.Rect.y + 7), GetFileColor(ext), 0, textRect);

                    // Display Name
                    if (RenamingEntry == subFile.FullName)
                    {
                        var rect = gui.CurrentNode.LayoutData.Rect;
                        var inputRect = new Rect(rect.x + 33, rect.y + 4, rect.width - 40, 30 - 8);
                        gui.Draw2D.DrawRectFilled(inputRect, GuiStyle.WindowBackground, 8);
                        var name = Path.GetFileNameWithoutExtension(subFile.Name);
                        bool changed = gui.InputField("RenameInput", ref name, 64, Gui.InputFieldFlags.EnterReturnsTrue, 30, 0, Size.Percentage(1f), null, null, true);
                        if (justStartedRename)
                            gui.FocusPreviousInteractable();
                        if (!gui.PreviousInteractableIsFocus())
                            RenamingEntry = null;

                        if (changed && !string.IsNullOrEmpty(name))
                        {
                            AssetDatabase.Rename(subFile, name);
                            RenamingEntry = null;
                        }
                        

                        justStartedRename = false;
                    }
                    else
                    {
                        var text = AssetPipelinePreferences.Instance.HideExtensions ? Path.GetFileNameWithoutExtension(subFile.FullName) : Path.GetFileName(subFile.FullName);
                        gui.Draw2D.DrawText(UIDrawList.DefaultFont, text, 20, new Vector2(gui.CurrentNode.LayoutData.Rect.x + 40, gui.CurrentNode.LayoutData.Rect.y + 7), Color.white, 0, textRect);
                    }
                }

                _treeCounter++;

                // SubAssets
                if (expanded)
                {
                    gui.PushID(fileNodeID);
                    left = (depth + 1) * GuiStyle.ItemHeight;

                    for (ushort i = 0; i < subFileNode.SubAssets.Length; i++)
                    {
                        if (subFileNode.SubAssets[i].type == null) continue;

                        // SubAsset Entry
                        using (gui.Node(subFileNode.SubAssets[i].name, depth + 1 + i).Left(left).Top(_treeCounter * (GuiStyle.ItemHeight + entryPadding)).Width(Size.Percentage(1f, -(left + gui.VScrollBarWidth()))).Height(GuiStyle.ItemHeight * scaleHeight).Enter())
                        {
                            gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GetTypeColor(subFileNode.SubAssets[i].type!) * 0.5f, 4);

                            var interact = gui.GetInteractable();
                            HandleFileClick(_treeCounter, interact, subFileNode, i);

                            if (SelectHandler.IsSelected(subFile))
                                gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GuiStyle.Indigo, 4);
                            else if (interact.IsHovered())
                                gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GuiStyle.Base5, 4);

                            gui.Draw2D.DrawText(UIDrawList.DefaultFont, GetIconForType(subFileNode.SubAssets[i].type!), 20, new Vector2(gui.CurrentNode.LayoutData.Rect.x + (GuiStyle.ItemHeight / 2), gui.CurrentNode.LayoutData.Rect.y + 7), GetTypeColor(subFileNode.SubAssets[i].type!));
                            gui.Draw2D.DrawText(UIDrawList.DefaultFont, subFileNode.SubAssets[i].name, 20, new Vector2(gui.CurrentNode.LayoutData.Rect.x + 40, gui.CurrentNode.LayoutData.Rect.y + 7), Color.white);
                        }

                        _treeCounter++;
                    }

                    gui.PopID();
                }

            }
        }

        public static void HandleFileClick(int index, Interactable interact, AssetDirectoryCache.FileNode entryNode, ushort fileID = 0, bool fromAssetBrowser = false)
        {
            var entry = entryNode.File;

            if (entryNode.AssetID != Guid.Empty && ImporterAttribute.SupportsExtension(entry.Extension))
            {
                if (DragnDrop.OnBeginDrag(out var node))
                {
                    var serialized = AssetDatabase.LoadAsset(entryNode.AssetID);
                    using (node.Width(20).Height(20).Enter())
                    {
                        Gui.ActiveGUI.Draw2D.DrawList.PushClipRectFullScreen();
                        Gui.ActiveGUI.Draw2D.DrawText(UIDrawList.DefaultFont, FontAwesome6.BoxesPacking + "  " + GetIcon(entry.Extension) + "  " + serialized.GetAsset(fileID).Name, 30, node.LayoutData.InnerRect.Position, Color.white);
                        Gui.ActiveGUI.Draw2D.DrawList.PopClipRect();
                    }
                    DragnDrop.SetPayload(serialized.GetAsset(fileID), entry);
                }
            }
            else if (fileID == 0)
            {
                DragnDrop.Drag(entry);
            }

            // Skip handling Sub Assets
            if (fileID != 0) return;

            if(index != -1)
                SelectHandler.AddSelectableAtIndex(index, entry);
            if (interact.TakeFocus(true))
                SelectHandler.Select(index, entry);

            if (interact.IsHovered() && Gui.ActiveGUI.IsPointerClick(Silk.NET.Input.MouseButton.Right))
                Gui.ActiveGUI.OpenPopup("RightClickFile");
            var popupHolder = Gui.ActiveGUI.CurrentNode;
            if (Gui.ActiveGUI.BeginPopup("RightClickFile", out var node2))
                using (node2.Width(180).Padding(5).Layout(LayoutType.Column).Spacing(5).FitContentHeight().Enter())
                    DrawContextMenu(entry, null, fromAssetBrowser, popupHolder);

            if (entryNode.AssetID != Guid.Empty && interact.IsHovered() && Gui.ActiveGUI.IsPointerDoubleClick(Silk.NET.Input.MouseButton.Left))
            {
                if (entry.Extension.Equals(".scene", StringComparison.OrdinalIgnoreCase))
                    SceneManager.LoadScene(new AssetRef<Runtime.Scene>(entryNode.AssetID));
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
                {
                    return GuiStyle.RandomPastelColor(type.GetHashCode());
                }
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