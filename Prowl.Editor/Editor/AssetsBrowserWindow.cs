// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;

using Prowl.Editor.Assets;
using Prowl.Editor.Preferences;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;

namespace Prowl.Editor;

public class AssetsBrowserWindow : EditorWindow
{
    public AssetDirectoryCache.DirNode CurDirectoryNode;
    public bool Locked;

    double itemHeight => EditorStylePrefs.Instance.ItemSize;
    double itemPadding => 4;

    private string _searchText = "";
    private readonly List<FileInfo> _found = new();
    private readonly Dictionary<string, AssetRef<Texture2D>> _cachedThumbnails = new();
    private static (long, bool) _lastGenerated = (-1, false);
    internal static string? RenamingEntry;
    private static bool justStartedRename;

    private const float PingDuration = 3f;
    private float _pingTimer;
    private FileInfo _pingedFile;

    private float EntrySize => (1.0f + AssetPipelinePreferences.Instance.ThumbnailSize) * 90f;

    public AssetsBrowserWindow() : base()
    {
        Title = FontAwesome6.FolderTree + " Asset Browser";
        Project.OnProjectChanged += Invalidate;
        AssetDatabase.AssetCacheUpdated += Invalidate;
        AssetsTreeWindow.SelectHandler.OnSelectObject += SelectionChanged;
        AssetDatabase.Pinged += OnAssetPinged;
        Invalidate();
    }

    ~AssetsBrowserWindow()
    {
        Project.OnProjectChanged -= Invalidate;
        AssetDatabase.AssetCacheUpdated -= Invalidate;
        AssetsTreeWindow.SelectHandler.OnSelectObject -= SelectionChanged;
        AssetDatabase.Pinged -= OnAssetPinged;
    }

    public void Invalidate()
    {
        if (CurDirectoryNode == null)
            CurDirectoryNode = AssetDatabase.GetRootFolderCache(2).RootNode;

        // Ensure we always have a valid Directory, if the current one is deleted move to its parent
        // if theres no parent move to the Assets Directory
        // If theres no project directory well why the hell are we here? the line above should have stopped us
        while (!Path.Exists(CurDirectoryNode.Directory.FullName))
            CurDirectoryNode = CurDirectoryNode.Parent ?? AssetDatabase.GetRootFolderCache(2).RootNode;
    }

    private void SelectionChanged(object to)
    {
        if (Locked)
            return;

        string path = to switch
        {
            DirectoryInfo dir => dir.FullName,
            FileInfo file     => file.Directory.FullName,
            _                 => CurDirectoryNode.Directory.FullName
        };

        if (AssetDatabase.PathToCachedNode(path, out var node))
            CurDirectoryNode = node;
    }

    private void OnAssetPinged(FileInfo assetPath)
    {
        _pingTimer = PingDuration;
        _pingedFile = assetPath;
        if (AssetDatabase.PathToCachedNode(_pingedFile.Directory.FullName, out var node))
            CurDirectoryNode = node;
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

        CurDirectoryNode ??= AssetDatabase.GetRootFolderCache(2).RootNode;

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
            bool cantGoUp = CurDirectoryNode.Parent == null;

            using (gui.Node("DirUpBtn").Scale(itemHeight).Enter())
            {
                if (!cantGoUp && gui.IsNodePressed())
                    CurDirectoryNode = CurDirectoryNode.Parent!;
                gui.Draw2D.DrawText(FontAwesome6.ArrowUp, 30, gui.CurrentNode.LayoutData.Rect, cantGoUp ? EditorStylePrefs.Instance.LesserText : (gui.IsNodeHovered() ? EditorStylePrefs.Instance.Hovering : Color.white));
            }

            if (gui.Search("SearchInput", ref _searchText, itemHeight + itemPadding, 0, 200, itemHeight))
            {
                _found.Clear();
                if (!string.IsNullOrEmpty(_searchText))
                {
                    _found.AddRange(CurDirectoryNode.Directory.EnumerateFiles("*", SearchOption.AllDirectories));
                    _found.RemoveAll(f => f.Extension.Equals(".meta", StringComparison.OrdinalIgnoreCase) || !f.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase));
                }
            }

            //var pathPos = new Vector2(itemHeight + 200 + (itemPadding * 3), 7);
            //pathPos += g.CurrentNode.LayoutData.GlobalContentPosition;
            var pathPos = new Vector2(itemHeight + 200 + (itemPadding * 3), 0);
            string assetPath = Path.GetRelativePath(Project.Active.ProjectPath, CurDirectoryNode.Directory.FullName);
            //g.DrawText(Font.DefaultFont, assetPath, 20, pathPos, GuiStyle.Base11);
            string[] nodes = assetPath.Split(Path.DirectorySeparatorChar);
            double[] nodeSizes = new double[nodes.Length];
            for (int j = 0; j < nodes.Length; j++)
                nodeSizes[j] = Font.DefaultFont.CalcTextSize(nodes[j], 0).x + 10;

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
                    {
                        string path = Project.Active.ProjectPath + "/" + string.Join("/", nodes.Take(i + 1));
                        if (AssetDatabase.PathToCachedNode(path, out var node))
                            CurDirectoryNode = node;
                    }

                    gui.Draw2D.DrawText(nodes[i], 20, gui.CurrentNode.LayoutData.Rect, gui.IsNodeHovered() ? Color.white : EditorStylePrefs.Instance.LesserText);
                }

                gui.Draw2D.DrawText(Font.DefaultFont, "/", 20, gui.CurrentNode.LayoutData.GlobalContentPosition + pathPos + new Vector2(textSize, 6), EditorStylePrefs.Instance.LesserText);
                pathPos.x += textSize + 5;
            }

            using (gui.Node("LockBtn").Left(Offset.Percentage(1f, -itemHeight + 3)).Scale(itemHeight).Enter())
            {
                if (gui.IsNodePressed())
                    Locked = !Locked;

                gui.Draw2D.DrawText(Locked ? FontAwesome6.Lock : FontAwesome6.LockOpen, 30, gui.CurrentNode.LayoutData.Rect, gui.IsNodeHovered() ? Color.white : EditorStylePrefs.Instance.LesserText);
            }
        }
    }

    public void RenderBody()
    {
        using (gui.Node("Body").Width(Size.Percentage(1f)).MarginTop(5).Layout(LayoutType.Grid).Clip().Scroll().Enter())
        {
            gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.WindowBGTwo, (float)EditorStylePrefs.Instance.WindowRoundness);
            var dropInteract = gui.GetInteractable();
            //HandleDrop();

            if (gui.IsNodeHovered() && gui.IsPointerClick(MouseButton.Right))
                gui.OpenPopup("RightClickBodyBrowser");
            var popupHolder = gui.CurrentNode;
            if (gui.BeginPopup("RightClickBodyBrowser", out var node))
                using (node.Width(180).Padding(5).Layout(LayoutType.Column).Spacing(5).FitContentHeight().Enter())
                    AssetsTreeWindow.DrawContextMenu(null, CurDirectoryNode.Directory, true, popupHolder);

            if (DragnDrop.Drop<GameObject>(out var go))
            {
                if (go.AssetID == Guid.Empty)
                {
                    var prefab = new Prefab
                    {
                        GameObject = Serializer.Serialize(go),
                        Name = go.Name
                    };
                    FileInfo file = new FileInfo(CurDirectoryNode.Directory + $"/{prefab.Name}.prefab");
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
                //foreach (var entry in _found)
                //    RenderEntry(ref i, entry);
            }
            else
            {
                foreach (var folder in CurDirectoryNode.SubDirectories)
                    RenderEntry(ref i, folder);

                foreach (var file in CurDirectoryNode.Files)
                    RenderEntry(ref i, file);
            }
        }
    }

    public void RenderEntry(ref int index, AssetDirectoryCache.DirNode entry)
    {
        using (gui.Node(entry.Directory.Name).Scale(EntrySize).Margin(itemPadding).Enter())
        {
            var interact = gui.GetInteractable();

            if (gui.IsNodeHovered() && gui.IsPointerClick(MouseButton.Right))
                gui.OpenPopup("RightClickFileBrowser");
            var popupHolder = gui.CurrentNode;
            if (gui.BeginPopup("RightClickFileBrowser", out var node))
                using (node.Width(180).Padding(5).Layout(LayoutType.Column).FitContentHeight().Enter())
                    AssetsTreeWindow.DrawContextMenu(entry.Directory, null, true, popupHolder);

            if (interact.TakeFocus())
            {
                var old = CurDirectoryNode;
                AssetsTreeWindow.SelectHandler.Select(entry.Directory);
                CurDirectoryNode = old;
            }

            if (interact.IsHovered() && gui.IsPointerDoubleClick(MouseButton.Left))
            {
                CurDirectoryNode = entry;
            }

            DragnDrop.Drag(entry);

            if (DragnDrop.Drop<FileSystemInfo>(out var systeminfo))
            {
                string target = Path.Combine(entry.Directory.FullName, systeminfo.Name);
                if (systeminfo is FileInfo file)
                    AssetDatabase.Move(file, target);
                else if (systeminfo is DirectoryInfo d)
                    AssetDatabase.Move(d, target);
            }

            DrawFileEntry(index++, entry.Directory, interact);

            DrawPingEffect(entry.Directory.FullName);
        }
    }

    public void RenderEntry(ref int index, AssetDirectoryCache.FileNode entry)
    {

        AssetDatabase.SubAssetCache[] subAssets = entry.SubAssets;

        bool expanded = false;
        using (gui.Node(entry.File.Name).Scale(EntrySize).Margin(itemPadding).Enter())
        {
            var interact = gui.GetInteractable();
            AssetsTreeWindow.HandleFileClick(-1, interact, entry, 0, true);

            DrawFileEntry(index++, entry.File, interact);

            if (subAssets.Length > 1)
            {
                expanded = gui.GetNodeStorage(gui.CurrentNode.Parent, entry.File.FullName, false);

                using (gui.Node("ExpandBtn").TopLeft(Offset.Percentage(1f, -(itemHeight * 0.5)), 2).Scale(itemHeight * 0.5).Enter())
                {
                    if (gui.IsNodePressed())
                    {
                        expanded = !expanded;
                        gui.SetNodeStorage(gui.CurrentNode.Parent.Parent, entry.File.FullName, expanded);
                    }
                    gui.Draw2D.DrawText(expanded ? FontAwesome6.ChevronRight : FontAwesome6.ChevronLeft, 20, gui.CurrentNode.LayoutData.Rect, gui.IsNodeHovered() ? Color.white : EditorStylePrefs.Instance.LesserText);
                }
            }

            DrawPingEffect(entry.File.FullName);
        }

        if (expanded)
        {
            for (ushort i = 0; i < subAssets.Length; i++)
            {
                using (gui.Node(subAssets[i].name, i).Scale(EntrySize * 0.75).Margin(itemPadding).Enter())
                {
                    var interact = gui.GetInteractable();
                    AssetsTreeWindow.HandleFileClick(-1, interact, entry, i, true);

                    DrawFileEntry(index++, entry.File, interact, subAssets[i]);
                }
            }
        }
    }

    private void DrawFileEntry(int index, FileSystemInfo entry, Interactable interact, AssetDatabase.SubAssetCache? subAsset = null)
    {
        var rect = gui.CurrentNode.LayoutData.Rect;
        //if (hasSubAsset)
        //    rect.Expand(-10);
        var entrySize = rect.width;

        gui.Tooltip(subAsset is not null ? subAsset.Value.name : entry.FullName);

        var color = EditorStylePrefs.Instance.Borders;
        if (entry is FileInfo f)
        {
            color = AssetsTreeWindow.GetFileColor(f.Extension.ToLower().Trim());
            gui.Draw2D.DrawRectFilled(rect, color * 0.5f, (float)EditorStylePrefs.Instance.AssetRoundness);
        }
        else
        {
            if (index++ % 2 == 0)
                gui.Draw2D.DrawRectFilled(rect, EditorStylePrefs.Instance.LesserText * 0.6f, (float)EditorStylePrefs.Instance.AssetRoundness);
            else
                gui.Draw2D.DrawRectFilled(rect, EditorStylePrefs.Instance.LesserText * 0.8f, (float)EditorStylePrefs.Instance.AssetRoundness);
        }

        if (AssetsTreeWindow.SelectHandler.IsSelected(entry))
            gui.Draw2D.DrawRectFilled(rect, EditorStylePrefs.Instance.Highlighted, (float)EditorStylePrefs.Instance.AssetRoundness);
        else if (interact.IsHovered())
            gui.Draw2D.DrawRectFilled(rect, EditorStylePrefs.Instance.Hovering, (float)EditorStylePrefs.Instance.AssetRoundness);

        // Draw Thumbnail
        var size = entrySize - itemHeight; // Remove height so we can fit Name under thumbnails
        var thumbnailRect = new Rect(rect.x + (itemHeight * 0.5f), rect.y + (itemHeight * 0.25f), size, size);
        var thumbnail = GetEntryThumbnail(entry, false);
        if (thumbnail.IsAvailable)
            gui.Draw2D.DrawImage(thumbnail.Res, thumbnailRect.Position, thumbnailRect.Size, Color.white, true);

        // Draw Name
        var namePos = rect.Position + new Vector2(0, size + 5);
        var nameRect = new Rect(namePos.x, namePos.y, entrySize, itemHeight);


        if (RenamingEntry == entry.FullName)
        {
            var inputRect = new Rect(nameRect.x, nameRect.y + 4, nameRect.width, 30 - 8);
            gui.Draw2D.DrawRectFilled(inputRect, EditorStylePrefs.Instance.WindowBGTwo, 8);
            string name = Path.GetFileNameWithoutExtension(entry.FullName);
            bool changed = gui.InputField("RenameInput", ref name, 64, Gui.InputFieldFlags.EnterReturnsTrue, 0, size + 4, Size.Percentage(1f), null, EditorGUI.GetInputStyle(), true);
            if (justStartedRename)
                gui.FocusPreviousInteractable();
            if (!gui.PreviousInteractableIsFocus())
                RenamingEntry = null;

            if (changed && !string.IsNullOrEmpty(name))
            {
                if (entry is FileInfo file)
                    AssetDatabase.Rename(file, name);
                else if (entry is DirectoryInfo dir)
                    AssetDatabase.Rename(dir, name);
                RenamingEntry = null;
            }

            justStartedRename = false;
        }
        else
        {
            var text = AssetPipelinePreferences.Instance.HideExtensions ? Path.GetFileNameWithoutExtension(entry.FullName) : Path.GetFileName(entry.FullName);
            gui.Draw2D.DrawText(text, nameRect, false, true);
        }
    }

    private void DrawPingEffect(string fullPath)
    {
        if (_pingTimer > 0 && _pingedFile.FullName.Equals(fullPath, StringComparison.OrdinalIgnoreCase))
        {
            _pingTimer -= Time.deltaTimeF;
            if (_pingTimer > PingDuration - 1f)
            {
                if (AssetDatabase.PathToCachedNode(_pingedFile.Directory.FullName, out var node))
                    CurDirectoryNode = node;
                //ScrollToItem();
            }
            var pingRect = gui.CurrentNode.LayoutData.Rect;
            pingRect.Expand(MathF.Sin(_pingTimer) * 6f);
            gui.Draw2D.DrawRect(pingRect, EditorStylePrefs.Instance.Ping, 2f, 4f);
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

                    _cachedThumbnails[fileName].Res.Sampler.SetFilter(FilterType.Linear, FilterType.Linear);
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
                                tex.Res.Sampler.SetFilter(FilterType.Linear, FilterType.Linear);
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

            _cachedThumbnails[fileName].Res.Sampler.SetFilter(FilterType.Linear, FilterType.Linear);
        }

        return _cachedThumbnails[fileName].Res;
    }
}
