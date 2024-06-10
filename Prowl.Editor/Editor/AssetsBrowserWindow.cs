using Prowl.Editor.Assets;
using Prowl.Editor.Preferences;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.GUI.Graphics;
using System.Reflection;

namespace Prowl.Editor
{

    public class AssetsBrowserWindow : EditorWindow
    {
        public DirectoryInfo CurDirectory;
        public bool Locked = false;

        double itemHeight => GuiStyle.ItemHeight;
        double itemPadding => GuiStyle.ItemPadding;

        private string _searchText = "";
        private readonly List<FileSystemInfo> _found = new();
        private readonly Dictionary<string, AssetRef<Texture2D>> _cachedThumbnails = new();
        private static (long, bool) _lastGenerated = (-1, false);
        internal static string? RenamingEntry = null;

        private const float PingDuration = 3f;
        private float _pingTimer = 0;
        private FileInfo _pingedFile;

        private float EntrySize => (1.0f + AssetPipelinePreferences.Instance.ThumbnailSize) * 90f;

        public AssetsBrowserWindow() : base()
        {
            Title = FontAwesome6.FolderTree + " Asset Browser";
            Project.OnProjectChanged += Invalidate;
            AssetsTreeWindow.SelectHandler.OnSelectObject += SelectionChanged;
            AssetDatabase.Pinged += OnAssetPinged;
            Invalidate();
        }

        ~AssetsBrowserWindow()
        {
            Project.OnProjectChanged -= Invalidate;
            AssetsTreeWindow.SelectHandler.OnSelectObject -= SelectionChanged;
            AssetDatabase.Pinged -= OnAssetPinged;
        }

        public void Invalidate()
        {
            CurDirectory = new DirectoryInfo(Project.ProjectAssetDirectory);
        }

        private void SelectionChanged(object to)
        {
            if (Locked)
                return;

            if (to is DirectoryInfo directory)
                CurDirectory = directory;
            else if (to is FileInfo file)
                CurDirectory = file.Directory;
        }

        private void OnAssetPinged(FileInfo assetPath)
        {
            _pingTimer = PingDuration;
            _pingedFile = assetPath;
            CurDirectory = _pingedFile.Directory;
        }

        public static void StartRename(string? entry)
        {
            RenamingEntry = entry;
        }

        protected override void Draw()
        {
            if (!Project.HasProject)
                return;

            // Ensure we always have a Directory, if the current one is deleted move to its parent
            // if theres no parent move to the Assets Directory
            // If theres no project directory well why the hell are we here? the line above should have stopped us
            while (!Path.Exists(CurDirectory.FullName))
                CurDirectory = CurDirectory.Parent ?? new DirectoryInfo(Project.ProjectAssetDirectory);

            gui.CurrentNode.Layout(LayoutType.Column);
            gui.CurrentNode.ScaleChildren();
            gui.CurrentNode.Padding(0, 10, 10, 10);

            RenderHeader();

            RenderBody();
        }

        public void RenderHeader()
        {
            using (gui.Node("Search").Width(Size.Percentage(1f)).MaxHeight(itemHeight).Clip().Enter())
            {
                bool cantGoUp = CurDirectory.FullName.Equals(Project.ProjectAssetDirectory, StringComparison.OrdinalIgnoreCase)
                    || CurDirectory.FullName.Equals(Project.ProjectDefaultsDirectory, StringComparison.OrdinalIgnoreCase)
                    || CurDirectory.FullName.Equals(Project.ProjectPackagesDirectory, StringComparison.OrdinalIgnoreCase)
                    || CurDirectory.Parent == null;

                using (gui.Node("DirUpBtn").Scale(itemHeight).Enter())
                {
                    if (!cantGoUp && gui.IsNodePressed())
                        CurDirectory = CurDirectory.Parent!;
                    gui.Draw2D.DrawText(FontAwesome6.ArrowUp, 30, gui.CurrentNode.LayoutData.Rect, cantGoUp ? GuiStyle.Base4 : (gui.IsNodeHovered() ? GuiStyle.Base11 * 0.8f : GuiStyle.Base11));
                }

                if (gui.Search("SearchInput", ref _searchText, itemHeight + itemPadding, 0, 200, itemHeight))
                {
                    _found.Clear();
                    if (!string.IsNullOrEmpty(_searchText))
                    {
                        _found.AddRange(CurDirectory.EnumerateFiles("*", SearchOption.AllDirectories));
                        _found.AddRange(CurDirectory.EnumerateDirectories("*", SearchOption.AllDirectories));
                        _found.RemoveAll(f => f.Extension.Equals(".meta", StringComparison.OrdinalIgnoreCase) || !f.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase));
                    }
                }

                //var pathPos = new Vector2(itemHeight + 200 + (itemPadding * 3), 7);
                //pathPos += g.CurrentNode.LayoutData.GlobalContentPosition;
                var pathPos = new Vector2(itemHeight + 200 + (itemPadding * 3), 0);
                string assetPath = Path.GetRelativePath(Project.ProjectDirectory, CurDirectory.FullName);
                //g.DrawText(UIDrawList.DefaultFont, assetPath, 20, pathPos, GuiStyle.Base11);
                string[] nodes = assetPath.Split(Path.DirectorySeparatorChar);
                double[] nodeSizes = new double[nodes.Length];
                for (int j = 0; j < nodes.Length; j++)
                    nodeSizes[j] = UIDrawList.DefaultFont.CalcTextSize(nodes[j], 0).x + 10;

                // start node, we may need to skip nodes if the total length exceeds the window
                int i = 0;
                int iters = 10;
                while (true)
                {
                    if (iters <= 0)
                        break;
                    iters--;

                    double total = 0;
                    for (int j = i; j < nodes.Length; j++)
                    {
                        var textSize = nodeSizes[j];
                        if (total + textSize > gui.CurrentNode.LayoutData.InnerRect.width - pathPos.x - itemHeight)
                        {
                            i++;
                            break;
                        }
                        total += textSize + 5;
                    }
                }

                // draw each node in the path
                for (; i < nodes.Length; i++)
                {
                    var textSize = nodeSizes[i];

                    using (gui.Node($"PathNode{i}").Left(pathPos.x).Scale(textSize, itemHeight).Enter())
                    {
                        if (gui.IsNodePressed())
                            CurDirectory = new(Project.ProjectDirectory + "/" + string.Join("/", nodes.Take(i + 1)));

                        gui.Draw2D.DrawText(nodes[i], 20, gui.CurrentNode.LayoutData.Rect, gui.IsNodeHovered() ? GuiStyle.Base11 : GuiStyle.Base5);
                    }

                    gui.Draw2D.DrawText(UIDrawList.DefaultFont, "/", 20, gui.CurrentNode.LayoutData.GlobalContentPosition + pathPos + new Vector2(textSize, 6), GuiStyle.Base5);
                    pathPos.x += textSize + 5;
                }

                using (gui.Node("LockBtn").Left(Offset.Percentage(1f, -itemHeight + 3)).Scale(itemHeight).Enter())
                {
                    if (gui.IsNodePressed())
                        Locked = !Locked;

                    gui.Draw2D.DrawText(Locked ? FontAwesome6.Lock : FontAwesome6.LockOpen, 30, gui.CurrentNode.LayoutData.Rect, gui.IsNodeHovered() ? GuiStyle.Base11 : GuiStyle.Base5);
                }
            }
        }

        public void RenderBody()
        {
            using (gui.Node("Body").Width(Size.Percentage(1f)).MarginTop(5).Layout(LayoutType.Grid).Clip().Enter())
            {
                var dropInteract = gui.GetInteractable();
                //HandleDrop();

                if (DragnDrop.Drop<GameObject>(out var go))
                {
                    if (go.AssetID == Guid.Empty)
                    {
                        var prefab = new Prefab {
                            GameObject = Serializer.Serialize(go),
                            Name = go.Name
                        };
                        FileInfo file = new FileInfo(CurDirectory + $"/{prefab.Name}.prefab");
                        while (File.Exists(file.FullName))
                            file = new FileInfo(file.FullName.Replace(".prefab", "") + " new.prefab");

                        StringTagConverter.WriteToFile(Serializer.Serialize(prefab), file);

                        AssetDatabase.Update();
                        AssetDatabase.Ping(file);
                    }
                }

                if (!AssetsTreeWindow.SelectHandler.SelectedThisFrame && dropInteract.TakeFocus())
                    AssetsTreeWindow.SelectHandler.Clear();

                //if (Hotkeys.IsHotkeyDown("Duplicate", new() { Key = Key.D, Ctrl = true }))
                //    DuplicateSelected();

                int i = 0;
                if (!string.IsNullOrEmpty(_searchText))
                {
                    foreach (var entry in _found)
                        RenderEntry(ref i, entry);
                }
                else
                {
                    var directories = CurDirectory.GetDirectories();
                    foreach (var folder in directories)
                    {
                        if (folder.Exists)
                            RenderEntry(ref i, folder);
                    }

                    var files = CurDirectory.GetFiles();
                    foreach (var file in files)
                    {
                        if (file.Exists && !file.Extension.Equals(".meta", StringComparison.OrdinalIgnoreCase))
                            RenderEntry(ref i, file);
                    }
                }

                gui.ScrollV();
            }
        }

        public void RenderEntry(ref int index, FileSystemInfo entry)
        {
            if (entry is DirectoryInfo dir)
            {

                using (gui.Node(dir.Name).Scale(EntrySize).Margin(itemPadding).Enter())
                {
                    var interact = gui.GetInteractable();

                    if (interact.TakeFocus())
                    {
                        var old = CurDirectory;
                        AssetsTreeWindow.SelectHandler.Select(entry);
                        CurDirectory = old;
                    }

                    if (interact.IsHovered() && gui.IsPointerDoubleClick(Veldrid.MouseButton.Left))
                    {
                        CurDirectory = new DirectoryInfo(entry.FullName);
                    }

                    DragnDrop.Drag(entry);

                    if (dir.Exists)
                    {
                        if (DragnDrop.Drop<FileSystemInfo>(out var systeminfo))
                        {
                            string target = RuntimeUtils.GetUniquePath(Path.Combine(dir.FullName, systeminfo.Name));
                            if (systeminfo is FileInfo fileinfo)
                                fileinfo?.MoveTo(target);
                            else if (systeminfo is DirectoryInfo dirinfo)
                                dirinfo?.MoveTo(target);
                        }
                    }

                    DrawFileEntry(index++, entry, interact);

                    DrawPingEffect(entry);
                }

            }
            else if (entry is FileInfo file)
            {
                AssetDatabase.SubAssetCache[] subAssets = Array.Empty<AssetDatabase.SubAssetCache>();
                if (AssetDatabase.TryGetGuid(file, out var guid))
                    subAssets = AssetDatabase.GetSubAssetsCache(guid);

                bool expanded = false;
                using (gui.Node(file.Name).Scale(EntrySize).Margin(itemPadding).Enter())
                {
                    var interact = gui.GetInteractable();
                    AssetsTreeWindow.HandleFileClick(-1, interact, file, 0);

                    DrawFileEntry(0, entry, interact);

                    if (subAssets.Length > 1)
                    {
                        expanded = gui.GetStorage<bool>(gui.CurrentNode.Parent, file.FullName, false);

                        using (gui.Node("ExpandBtn").TopLeft(Offset.Percentage(1f, -(itemHeight * 0.5)), 2).Scale(itemHeight * 0.5).Enter())
                        {
                            if (gui.IsNodePressed())
                            {
                                expanded = !expanded;
                                gui.SetNodeStorage(gui.CurrentNode.Parent.Parent, file.FullName, expanded);
                            }
                            gui.Draw2D.DrawText(expanded ? FontAwesome6.ChevronRight : FontAwesome6.ChevronLeft, 20, gui.CurrentNode.LayoutData.Rect, gui.IsNodeHovered() ? GuiStyle.Base11 : GuiStyle.Base5);
                        }
                    }

                    DrawPingEffect(entry);
                }

                if (expanded)
                {
                    for (ushort i = 0; i < subAssets.Length; i++)
                    {
                        using (gui.Node(subAssets[i].name, i).Scale(EntrySize * 0.75).Margin(itemPadding).Enter())
                        {
                            var interact = gui.GetInteractable();
                            AssetsTreeWindow.HandleFileClick(-1, interact, file, i);

                            DrawFileEntry(0, entry, interact, true, subAssets[i]);
                        }
                    }
                }
            }
        }

        private void DrawFileEntry(int index, FileSystemInfo entry, Interactable interact, bool hasSubAsset = false, AssetDatabase.SubAssetCache? subAsset = null)
        {
            var rect = gui.CurrentNode.LayoutData.Rect;
            //if (hasSubAsset)
            //    rect.Expand(-10);
            var entrySize = rect.width;

            gui.Tooltip(hasSubAsset ? subAsset.Value.name : entry.FullName);

            var color = GuiStyle.Borders;
            if (entry is FileInfo f)
            {
                color = AssetsTreeWindow.GetFileColor(f.Extension.ToLower().Trim());
                gui.Draw2D.DrawRectFilled(rect, color * 0.5f, 4f);
            }
            else
            {
                if (index++ % 2 == 0)
                    gui.Draw2D.DrawRectFilled(rect, GuiStyle.Base4 * 0.6f, 4);
                else
                    gui.Draw2D.DrawRectFilled(rect, GuiStyle.Base4 * 0.8f, 4);
            }

            //var gradientStart = UIDrawList.ColorConvertFloat4ToU32(GuiStyle.WindowBackground);
            //var gradientEnd = UIDrawList.ColorConvertFloat4ToU32(GuiStyle.Borders);
            //if (entry is FileInfo f)
            //{
            //    gradientEnd = UIDrawList.ColorConvertFloat4ToU32(AssetsTreeWindow.GetFileColor(f.Extension.ToLower().Trim()) * 0.5f);
            //}
            //
            //int vertStartIdx = g.DrawList.VtxBuffer.Count;
            //g.DrawRectFilled(rect, GuiStyle.Borders, 4f);
            //int vertEndIdx = g.DrawList.VtxBuffer.Count;
            //g.DrawList.ShadeVertsLinearColorGradientKeepAlpha(vertStartIdx, vertEndIdx, rect.Min, new Vector2(rect.Min.x, rect.Max.y), gradientStart, gradientEnd);


            if (AssetsTreeWindow.SelectHandler.IsSelected(entry))
                gui.Draw2D.DrawRectFilled(rect, GuiStyle.Indigo, 4);
            else if (interact.IsHovered())
                gui.Draw2D.DrawRectFilled(rect, GuiStyle.Base5, 4);

            // Draw Thumbnail
            var size = entrySize - itemHeight; // Remove height so we can fit Name under thumbnails
            var thumbnailRect = new Rect(rect.x + (itemHeight * 0.5f), rect.y + (itemHeight * 0.25f), size, size);
            var thumbnail = GetEntryThumbnail(entry, false);
            if (thumbnail.IsAvailable)
                gui.Draw2D.DrawImage(thumbnail.Res, thumbnailRect.Position, thumbnailRect.Size, Color.white, true);

            // Draw Name
            var namePos = rect.Position + new Vector2(0, size + 5);
            var nameRect = new Rect(namePos.x, namePos.y, entrySize, itemHeight);
            var text = AssetPipelinePreferences.Instance.HideExtensions ? Path.GetFileNameWithoutExtension(entry.FullName) : Path.GetFileName(entry.FullName);
            gui.Draw2D.DrawText(UIDrawList.DefaultFont, text, 20, nameRect, GuiStyle.Base11, false);
        }

        private void DrawPingEffect(FileSystemInfo entry)
        {
            if (_pingTimer > 0 && _pingedFile.FullName.Equals(entry.FullName, StringComparison.OrdinalIgnoreCase))
            {
                _pingTimer -= Time.deltaTimeF;
                if (_pingTimer > PingDuration - 1f)
                {
                    CurDirectory = _pingedFile.Directory;
                    //ScrollToItem();
                }
                var pingRect = gui.CurrentNode.LayoutData.Rect;
                pingRect.Expand(MathF.Sin(_pingTimer) * 6f);
                gui.Draw2D.DrawRect(pingRect, GuiStyle.Yellow, 2f, 4f);
            }
        }

        private AssetRef<Texture2D> GetEntryThumbnail(FileSystemInfo entry, bool subAsset)
        {
            string fileName = "FileIcon.png";

            if (subAsset)
            {
                fileName = "SubFileIcon.png";
            }
            else
            {
                if (entry is DirectoryInfo directory)
                {
                    fileName = directory.EnumerateFiles().Any() || directory.EnumerateDirectories().Any() ? "FolderFilledIcon.png" : "FolderEmptyIcon.png";
                    if (!_cachedThumbnails.ContainsKey(fileName))
                    {
                        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Prowl.Editor.EmbeddedResources." + fileName);
                        _cachedThumbnails[fileName] = Texture2DLoader.FromStream(stream);
                        _cachedThumbnails[fileName].Res.SetTextureFilters(Runtime.Rendering.Primitives.TextureMin.Linear, Runtime.Rendering.Primitives.TextureMag.Linear);
                    }
                }
                else if (entry is FileInfo file)
                {
                    if (_lastGenerated.Item1 != Time.frameCount || !_lastGenerated.Item2)
                    {
                        if (TextureImporter.Supported.Contains(file.Extension, StringComparer.OrdinalIgnoreCase))
                        {
                            string relativeAssetPath = AssetDatabase.GetRelativePath(file.FullName);
                            if (_cachedThumbnails.TryGetValue(file.FullName, out var value))
                                return value;

                            if (relativeAssetPath != null)
                            {
                                _lastGenerated = (Time.frameCount, true);
                                var tex = Application.AssetProvider.LoadAsset<Texture2D>(relativeAssetPath);
                                if (tex.IsAvailable)
                                {
                                    _cachedThumbnails[file.FullName] = tex;
                                    tex.Res.SetTextureFilters(Runtime.Rendering.Primitives.TextureMin.Linear, Runtime.Rendering.Primitives.TextureMag.Linear);
                                    return tex.Res!;
                                }
                            }
                        }
                        else if (ImporterAttribute.SupportsExtension(file.Extension))
                            fileName = ImporterAttribute.GetIconForExtension(file.Extension);
                        else if (file.Extension.Equals(".cs", StringComparison.OrdinalIgnoreCase))
                            fileName = "CSharpIcon.png";
                    }
                }
            }

            if (!_cachedThumbnails.ContainsKey(fileName))
            {
                _lastGenerated = (Time.frameCount, true);
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Prowl.Editor.EmbeddedResources." + fileName))
                    _cachedThumbnails[fileName] = Texture2DLoader.FromStream(stream);

                _cachedThumbnails[fileName].Res.SetTextureFilters(Runtime.Rendering.Primitives.TextureMin.Linear, Runtime.Rendering.Primitives.TextureMag.Linear);
            }

            return _cachedThumbnails[fileName].Res;
        }
    }
}