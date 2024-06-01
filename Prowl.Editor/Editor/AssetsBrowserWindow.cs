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

            g.CurrentNode.Layout(LayoutType.Column);
            g.CurrentNode.ScaleChildren();
            g.CurrentNode.Padding(0, 10, 10, 10);

            RenderHeader();

            RenderBody();
        }

        public void RenderHeader()
        {
            using (g.Node("Search").Width(Size.Percentage(1f)).MaxHeight(itemHeight).Clip().Enter())
            {
                bool cantGoUp = CurDirectory.FullName.Equals(Project.ProjectAssetDirectory, StringComparison.OrdinalIgnoreCase)
                    || CurDirectory.FullName.Equals(Project.ProjectDefaultsDirectory, StringComparison.OrdinalIgnoreCase)
                    || CurDirectory.FullName.Equals(Project.ProjectPackagesDirectory, StringComparison.OrdinalIgnoreCase)
                    || CurDirectory.Parent == null;
                if (g.Button("DirUpBtn", FontAwesome6.ArrowUp, 0, 0, itemHeight, itemHeight, cantGoUp ? new GuiStyle() { TextColor = Color.grey } : null, true))
                    if(!cantGoUp)
                        CurDirectory = CurDirectory.Parent!;

                if (g.Search("SearchInput", ref _searchText, itemHeight + itemPadding, 0, 200, itemHeight))
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
                        if (total + textSize > g.CurrentNode.LayoutData.InnerRect.width - pathPos.x - itemHeight)
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
                    if (g.Button($"PathNode{i}", nodes[i], pathPos.x, 0, textSize, itemHeight, null, true))
                    {
                        string newPath = Project.ProjectDirectory + "/" + string.Join("/", nodes.Take(i + 1));
                        CurDirectory = new DirectoryInfo(newPath);
                    }
                    g.DrawText(UIDrawList.DefaultFont, "/", 20, g.CurrentNode.LayoutData.GlobalContentPosition + pathPos + new Vector2(textSize, 6), GuiStyle.Base11);
                    pathPos.x += textSize + 5;
                }

                var btnStyle = new GuiStyle();
                btnStyle.FontSize = 30;
                btnStyle.TextColor = GuiStyle.Base4;
                if (g.Button("LockBtn", Locked ? FontAwesome6.Lock : FontAwesome6.LockOpen, Offset.Percentage(1f, -itemHeight + 3), 0, itemHeight, itemHeight, btnStyle, true))
                    Locked = !Locked;
            }
        }

        public void RenderBody()
        {
            using (g.Node("Body").Width(Size.Percentage(1f)).MarginTop(5).Layout(LayoutType.Grid).Enter())
            {
                var dropInteract = g.GetInteractable();
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

                g.ScrollV();
            }
        }

        public void RenderEntry(ref int index, FileSystemInfo entry)
        {
            if (entry is DirectoryInfo dir)
            {

                using (g.Node(dir.Name).Scale(EntrySize).Margin(itemPadding).Enter())
                {
                    var interact = g.GetInteractable();

                    if (interact.TakeFocus())
                    {
                        var old = CurDirectory;
                        AssetsTreeWindow.SelectHandler.Select(entry);
                        CurDirectory = old;
                    }

                    if (interact.IsHovered() && g.IsPointerDoubleClick(Silk.NET.Input.MouseButton.Left))
                    {
                        CurDirectory = new DirectoryInfo(entry.FullName);
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
                using (g.Node(file.Name).Scale(EntrySize).Margin(itemPadding).Enter())
                {
                    var interact = g.GetInteractable();
                    AssetsTreeWindow.HandleFileClick(interact, file, 0);

                    DrawFileEntry(0, entry, interact);

                    if (subAssets.Length > 1)
                    {
                        expanded = g.GetStorage<bool>(g.CurrentNode.Parent, file.FullName, false);
                        if (g.Button("ExpandBtn", expanded ? FontAwesome6.ChevronRight : FontAwesome6.ChevronLeft, Offset.Percentage(1f, -(itemHeight * 0.5)), 2, itemHeight * 0.5, itemHeight * 0.5, null, true))
                        {
                            expanded = !expanded;
                            g.SetStorage(g.CurrentNode.Parent, file.FullName, expanded);
                        }
                    }

                    DrawPingEffect(entry);
                }

                if (expanded)
                {
                    for (ushort i = 0; i < subAssets.Length; i++)
                    {
                        using (g.Node(subAssets[i].name, i).Scale(EntrySize * 0.75).Margin(itemPadding).Enter())
                        {
                            var interact = g.GetInteractable();
                            AssetsTreeWindow.HandleFileClick(interact, file, i);

                            DrawFileEntry(0, entry, interact, true, subAssets[i]);
                        }
                    }
                }
            }
        }

        private void DrawFileEntry(int index, FileSystemInfo entry, Interactable interact, bool hasSubAsset = false, AssetDatabase.SubAssetCache? subAsset = null)
        {
            var rect = g.CurrentNode.LayoutData.Rect;
            //if (hasSubAsset)
            //    rect.Expand(-10);
            var entrySize = rect.width;

            g.SimpleTooltip(hasSubAsset ? subAsset.Value.name : entry.FullName);

            var color = GuiStyle.Borders;
            if (entry is FileInfo f)
            {
                color = AssetsTreeWindow.GetFileColor(f.Extension.ToLower().Trim());
                g.DrawRectFilled(rect, color * 0.5f, 4f);
            }
            else
            {
                if (index++ % 2 == 0)
                    g.DrawRectFilled(rect, GuiStyle.Base4 * 0.6f, 4);
                else
                    g.DrawRectFilled(rect, GuiStyle.Base4 * 0.8f, 4);
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
                g.DrawRectFilled(rect, GuiStyle.Indigo, 4);
            else if (interact.IsHovered())
                g.DrawRectFilled(rect, GuiStyle.Base5, 4);

            // Draw Thumbnail
            var size = entrySize - itemHeight; // Remove height so we can fit Name under thumbnails
            var thumbnailRect = new Rect(rect.x + (itemHeight * 0.5f), rect.y + (itemHeight * 0.25f), size, size);
            var thumbnail = GetEntryThumbnail(entry, false);
            if (thumbnail.IsAvailable)
                g.DrawImage(thumbnail.Res, thumbnailRect.Position, thumbnailRect.Size, Color.white, true);

            // Draw Name
            var namePos = rect.Position + new Vector2(0, size + 5);
            var nameRect = new Rect(namePos.x, namePos.y, entrySize, itemHeight);
            var text = AssetPipelinePreferences.Instance.HideExtensions ? Path.GetFileNameWithoutExtension(entry.FullName) : Path.GetFileName(entry.FullName);
            g.DrawText(UIDrawList.DefaultFont, text, 20, nameRect, GuiStyle.Base11, false);
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
                var pingRect = g.CurrentNode.LayoutData.Rect;
                pingRect.Expand(MathF.Sin(_pingTimer) * 6f);
                g.DrawRect(pingRect, GuiStyle.Yellow, 2f, 4f);
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