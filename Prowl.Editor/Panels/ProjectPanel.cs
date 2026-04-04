using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Editor.Docking;
using Prowl.Editor.Widgets;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using Color = System.Drawing.Color;

namespace Prowl.Editor.Panels;

[EditorWindow("General/Project")]
public class ProjectPanel : DockPanel
{
    public override string Title => "Project";
    public override string Icon => EditorIcons.Folder;

    private string _currentFolder = ""; // Relative to Assets/, empty = Assets root
    private string _searchText = "";
    private float _thumbnailSize = 64f;
    private Paper? _paper; // Cached for modifier key checks in callbacks
    private string? _renamingPath; // Relative path of item being renamed, null if not renaming
    private string _renameText = "";
    private bool _renameInTree; // true = renaming in folder tree, false = in content view
    private int _lastSelectionCount; // track selection changes to cancel rename
    private const float MinThumbSize = 20f;  // Below this = list mode
    private const float MaxThumbSize = 128f;
    private const float ListThreshold = 32f; // Below this = list view

    private const float ToolbarHeight = 30f;
    private const float FolderTreeWidth = 180f;

    public override void OnGUI(Paper paper, float width, float height)
    {
        _paper = paper;
        var font = EditorTheme.DefaultFont;
        if (font == null || Project.Current == null) return;

        // Cancel rename if selection changed
        if (_renamingPath != null)
        {
            int currentCount = Selection.Count;
            var active = Selection.ActiveObject;
            bool selChanged = currentCount != _lastSelectionCount
                || (active is ContentItem ci && ci.RelativePath != _renamingPath)
                || (active is not ContentItem && active != null);
            if (selChanged) _renamingPath = null;
        }
        _lastSelectionCount = Selection.Count;

        using (paper.Column("proj_root").Size(width, height).Enter())
        {
            DrawToolbar(paper, font, width);
            DrawBody(paper, font, width, height - ToolbarHeight);
        }
    }

    // ================================================================
    //  Toolbar
    // ================================================================

    private void DrawToolbar(Paper paper, Prowl.Scribe.FontFile font, float width)
    {
        using (paper.Row("proj_toolbar")
            .Height(ToolbarHeight)
            .ChildLeft(6).ChildRight(6).RowBetween(4)
            .ChildTop(3).ChildBottom(3)
            .Enter())
        {
            // Add button with context menu
            using (paper.Box("proj_add")
                .Width(ToolbarHeight - 6).Height(ToolbarHeight - 6)
                .Rounded(4)
                .Hovered.BackgroundColor(EditorTheme.ButtonHovered).End()
                .Text(EditorIcons.Plus, font).TextColor(EditorTheme.Text)
                .FontSize(14f).Alignment(TextAlignment.MiddleCenter)
                .Enter())
            {
                if (paper.IsParentHovered)
                {
                    var addBuilder = new ContextMenuBuilder();
                    AssetCreateMenu.Build(addBuilder, _currentFolder, OnCreated);
                    addBuilder.Render(paper, "proj_add_menu", 0, ToolbarHeight - 6);
                }
            }

            // Spacer
            paper.Box("proj_spacer").Width(UnitValue.Stretch(4f));

            // Thumbnail size slider
            paper.Box("proj_list_ico")
                .Width(16).Height(ToolbarHeight - 6)
                .Text(EditorIcons.List, font).TextColor(EditorTheme.TextDim)
                .FontSize(14f).Alignment(TextAlignment.MiddleCenter);

            EditorGUI.Slider(paper, "proj_thumb_slider", "", _thumbnailSize, MinThumbSize, MaxThumbSize, false)
                .OnValueChanged(v => _thumbnailSize = v);

            paper.Box("proj_grid_ico")
                .Width(16).Height(ToolbarHeight - 6)
                .Text(EditorIcons.TableCellsLarge, font).TextColor(EditorTheme.TextDim)
                .FontSize(14f).Alignment(TextAlignment.MiddleCenter);

            // Search
            EditorGUI.SearchBar(paper, "proj_search", _searchText, "Search...")
                .OnValueChanged(v => _searchText = v);

            // Refresh button
            paper.Box("proj_refresh")
                .Width(ToolbarHeight - 6).Height(ToolbarHeight - 6)
                .Rounded(4)
                .Hovered.BackgroundColor(EditorTheme.ButtonHovered).End()
                .Text(EditorIcons.ArrowRotateRight, font).TextColor(EditorTheme.Text)
                .FontSize(14f).Alignment(TextAlignment.MiddleCenter)
                .OnClick(0, (_, _) =>
                {
                    // Force full rescan
                    if (EditorAssetDatabase.Instance != null)
                    {
                        Runtime.Debug.Log("Refreshing asset database...");
                        var db = new EditorAssetDatabase(Project.Current!);
                        db.Initialize();
                    }
                });
        }
    }

    // ================================================================
    //  Body: Folder Tree + Content
    // ================================================================

    private void DrawBody(Paper paper, Prowl.Scribe.FontFile font, float width, float height)
    {
        using (paper.Row("proj_body").Height(height).Enter())
        {
            DrawFolderTree(paper, font, height);
            DrawContent(paper, font, width - FolderTreeWidth, height);
        }
    }

    // ================================================================
    //  Folder Tree (left)
    // ================================================================

    private void DrawFolderTree(Paper paper, Prowl.Scribe.FontFile font, float height)
    {
        using (paper.Box("proj_tree_bg")
            .Size(FolderTreeWidth, height)
            .BackgroundColor(EditorTheme.Darkest)
            .Enter())
        {
            using (ScrollView.Begin(paper, "proj_tree", FolderTreeWidth, height))
            {
                // Root "Assets" node
                using (paper.Column("proj_tree_inner")
                    .MinHeight(22)
                    .Height(UnitValue.Auto)
                    .Enter())
                {
                    DrawFolderNode(paper, font, Project.Current!.AssetsPath, "Assets", 0);
                }
            }
        }
    }

    private void DrawFolderNode(Paper paper, Prowl.Scribe.FontFile font, string absolutePath, string displayName, int depth)
    {
        string relativePath = absolutePath == Project.Current!.AssetsPath
            ? ""
            : Path.GetRelativePath(Project.Current.AssetsPath, absolutePath).Replace('\\', '/');

        bool isSelected = _currentFolder == relativePath;
        float indent = depth * 16f;

        // Get subdirectories
        string[] subDirs;
        try { subDirs = Directory.GetDirectories(absolutePath).Where(d => !Path.GetFileName(d).StartsWith('.')).OrderBy(d => d).ToArray(); }
        catch { subDirs = Array.Empty<string>(); }

        bool hasChildren = subDirs.Length > 0;
        string arrowIcon = hasChildren ? EditorIcons.AngleRight : "";

        // Use element storage for open/close state
        string stateKey = $"proj_fo_{relativePath}";

        using (paper.Row($"proj_fn_{relativePath.GetHashCode()}")
            .Height(22)
            .BackgroundColor(isSelected ? EditorTheme.ButtonActive : Color.Transparent)
            .Hovered.BackgroundColor(isSelected ? EditorTheme.Bright : EditorTheme.ButtonHovered).End()
            .Rounded(3)
            .ChildLeft(indent + 4)
            .OnClick(relativePath, (path, _) => _currentFolder = path)
            .OnDoubleClick(stateKey, (key, _) =>
            {
                // Toggle folder open/close state via a static dictionary
                _folderOpenState[key] = !_folderOpenState.GetValueOrDefault(key, depth < 2);
            })
            .Enter())
        {
            // Arrow
            if (hasChildren)
            {
                bool isOpen = _folderOpenState.GetValueOrDefault(stateKey, depth < 2);
                paper.Box($"proj_fa_{relativePath.GetHashCode()}")
                    .Width(16).Height(22)
                    .Text(isOpen ? EditorIcons.AngleDown : EditorIcons.AngleRight, font)
                    .TextColor(EditorTheme.Text)
                    .FontSize(10f).Alignment(TextAlignment.MiddleCenter)
                    .OnClick(stateKey, (key, _) =>
                    {
                        _folderOpenState[key] = !_folderOpenState.GetValueOrDefault(key, depth < 2);
                    })
                    .StopEventPropagation();
            }
            else
            {
                paper.Box($"proj_fa_{relativePath.GetHashCode()}")
                    .Width(16).Height(22);
            }

            // Folder icon
            paper.Box($"proj_fi_{relativePath.GetHashCode()}")
                .Width(18).Height(22)
                .Text(EditorIcons.Folder, font)
                .TextColor(Color.FromArgb(255, 220, 180, 80))
                .FontSize(12f).Alignment(TextAlignment.MiddleCenter);

            // Name (inline rename in tree or label)
            if (_renamingPath == relativePath && _renameInTree)
            {
                EditorGUI.TextField(paper, $"proj_ft_rename_{relativePath.GetHashCode()}", "", _renameText)
                    .OnValueChanged(v => _renameText = v);
                if (_paper?.IsKeyDown(PaperKey.Enter) == true || _paper?.IsKeyDown(PaperKey.KeypadEnter) == true)
                {
                    var renameItem = new ContentItem { Name = displayName, RelativePath = relativePath, IsFolder = true };
                    CommitRename(renameItem);
                }
                else if (_paper?.IsKeyDown(PaperKey.Escape) == true)
                    _renamingPath = null;
            }
            else
            {
                paper.Box($"proj_fl_{relativePath.GetHashCode()}")
                    .Height(22)
                    .Margin(4, 0, 0, 0)
                    .Text(displayName, font)
                    .TextColor(EditorTheme.Text)
                    .FontSize(EditorTheme.FontSize)
                    .Alignment(TextAlignment.MiddleLeft);
            }

            // Right-click context menu on folder tree
            BuildFolderTreeContextMenu(paper, $"proj_ft_ctx_{relativePath.GetHashCode()}", relativePath);
        }

        // Children
        bool open = _folderOpenState.GetValueOrDefault(stateKey, depth < 2);
        if (open && hasChildren)
        {
            foreach (var subDir in subDirs)
                DrawFolderNode(paper, font, subDir, Path.GetFileName(subDir), depth + 1);
        }
    }

    private static readonly Dictionary<string, bool> _folderOpenState = new();

    // ================================================================
    //  Content Area (right)
    // ================================================================

    private void DrawContent(Paper paper, Prowl.Scribe.FontFile font, float width, float height)
    {
        var db = EditorAssetDatabase.Instance;
        if (db == null) return;

        // Gather entries for the current folder
        var entries = GetContentEntries(db);

        bool isList = _thumbnailSize < ListThreshold;

        using (paper.Box("proj_content_bg")
            .Size(width, height)
            .BackgroundColor(EditorTheme.Dark)
            .Enter())
        {
            // Breadcrumb
            DrawBreadcrumb(paper, font, width, 20);

            using (ScrollView.Begin(paper, "proj_content", width, height - 20))
            {
                using (paper.Column("proj_content_inner")
                    .Height(UnitValue.Auto)
                    .Enter())
                {
                    if (entries.Count == 0)
                    {
                        paper.Box("proj_empty").Height(60)
                            .Text("This folder is empty", font)
                            .TextColor(EditorTheme.TextDisabled)
                            .FontSize(EditorTheme.FontSize - 2)
                            .Alignment(TextAlignment.MiddleCenter);
                    }
                    else if (isList)
                    {
                        DrawListView(paper, font, entries, width);
                    }
                    else
                    {
                        DrawGridView(paper, font, entries, width);
                    }
                }
            }
        }
    }

    private void DrawBreadcrumb(Paper paper, Prowl.Scribe.FontFile font, float width, float height)
    {
        using (paper.Row("proj_breadcrumb")
            .Height(height).ChildLeft(4).RowBetween(2)
            .Enter())
        {
            // Split current folder into parts
            var parts = new List<(string name, string path)> { ("Assets", "") };
            if (!string.IsNullOrEmpty(_currentFolder))
            {
                string accumulated = "";
                foreach (var part in _currentFolder.Split('/'))
                {
                    accumulated = accumulated.Length > 0 ? accumulated + "/" + part : part;
                    parts.Add((part, accumulated));
                }
            }

            for (int i = 0; i < parts.Count; i++)
            {
                var (name, path) = parts[i];
                if (i > 0)
                {
                    paper.Box($"proj_bc_sep_{i}")
                        .Width(UnitValue.Auto).Height(20)
                        .Text(EditorIcons.AngleRight, font)
                        .TextColor(EditorTheme.TextDisabled)
                        .FontSize(8f).Alignment(TextAlignment.MiddleCenter);
                }

                paper.Box($"proj_bc_{i}")
                    .Width(UnitValue.Auto).Height(20)
                    .ChildLeft(4).ChildRight(4)
                    .Hovered.BackgroundColor(EditorTheme.ButtonHovered).End()
                    .Rounded(3)
                    .Text(name, font)
                    .TextColor(i == parts.Count - 1 ? EditorTheme.Text : EditorTheme.TextDim)
                    .FontSize(EditorTheme.FontSize - 2)
                    .Alignment(TextAlignment.MiddleCenter)
                    .OnClick(path, (p, _) => _currentFolder = p);
            }
        }
    }

    // ================================================================
    //  List View
    // ================================================================

    private void DrawListView(Paper paper, Prowl.Scribe.FontFile font, List<ContentItem> entries, float width)
    {
        // Build object list for Selection.HandleListClick
        var itemObjects = entries.Select(e => (object)e).ToList();

        for (int i = 0; i < entries.Count; i++)
        {
            var item = entries[i];
            bool isSelected = Selection.IsSelected(item);
            int idx = i;

            using (paper.Row($"proj_li_{i}")
                .Height(22)
                .BackgroundColor(isSelected ? EditorTheme.Accent : (i % 2 == 0 ? Color.Transparent : Color.FromArgb(10, 255, 255, 255)))
                .Hovered.BackgroundColor(isSelected ? EditorTheme.Accent : EditorTheme.ButtonHovered).End()
                .Rounded(3)
                .ChildLeft(4).RowBetween(4)
                .OnClick((item, idx, itemObjects), (cap, e) =>
                {
                    bool ctrl = _paper?.IsKeyDown(PaperKey.LeftControl) == true || _paper?.IsKeyDown(PaperKey.RightControl) == true;
                    bool shift = _paper?.IsKeyDown(PaperKey.LeftShift) == true || _paper?.IsKeyDown(PaperKey.RightShift) == true;
                    Selection.HandleListClick(cap.Item1, (IReadOnlyList<object>)cap.Item3, cap.Item2, ctrl, shift);
                })
                .OnDoubleClick(item, (it, _) =>
                {
                    if (it.IsFolder)
                        _currentFolder = it.RelativePath;
                })
                .Enter())
            {
                // Icon
                paper.Box($"proj_li_ico_{i}")
                    .Width(18).Height(22)
                    .Text(item.Icon, font)
                    .TextColor(item.IsFolder ? Color.FromArgb(255, 220, 180, 80) : EditorTheme.TextDim)
                    .FontSize(12f).Alignment(TextAlignment.MiddleCenter);

                // Name (inline rename or label)
                if (_renamingPath == item.RelativePath && !_renameInTree)
                {
                    EditorGUI.TextField(paper, $"proj_li_rename_{i}", "", _renameText)
                        .OnValueChanged(v => _renameText = v);
                    // Enter to confirm, Escape to cancel
                    if (_paper?.IsKeyDown(PaperKey.Enter) == true || _paper?.IsKeyDown(PaperKey.KeypadEnter) == true)
                        CommitRename(item);
                    else if (_paper?.IsKeyDown(PaperKey.Escape) == true)
                        _renamingPath = null;
                }
                else
                {
                    paper.Box($"proj_li_name_{i}")
                        .Height(22)
                        .Text(item.Name, font)
                        .TextColor(EditorTheme.Text)
                        .FontSize(EditorTheme.FontSize - 3)
                        .Alignment(TextAlignment.MiddleLeft);
                }

                // Type
                if (!item.IsFolder)
                {
                    paper.Box($"proj_li_type_{i}")
                        .Width(80).Height(22)
                        .Text(item.TypeLabel, font)
                        .TextColor(EditorTheme.TextDim)
                        .FontSize(EditorTheme.FontSize - 4)
                        .Alignment(TextAlignment.MiddleRight);
                }

                // Right-click context menu
                BuildItemContextMenu(paper, $"proj_li_ctx_{i}", item);
            }
        }
    }

    private void BuildItemContextMenu(Paper paper, string id, ContentItem item, bool inTree = false)
    {
        ContextMenuHelper.RightClickMenu(paper, id, builder =>
        {
            // Right-click should select the item if not already selected
            if (!Selection.IsSelected(item))
                Selection.Select(item);

            bool isMulti = Selection.Count > 1;
            string folder = item.IsFolder ? item.RelativePath : _currentFolder;

            if (!item.IsFolder)
            {
                builder.Item($"{EditorIcons.FolderOpen}  Open with System", () => OpenWithSystem(item));
                builder.Separator();
            }

            builder.Submenu($"{EditorIcons.Plus}  Create", sub => AssetCreateMenu.Build(sub, folder, OnCreated));

            builder.Separator();

            builder.Item($"{EditorIcons.FolderOpen}  Show in Explorer", () => ShowInExplorer(item));

            if (!item.IsFolder)
            {
                builder.Item($"{EditorIcons.ArrowsRotate}  Reimport", () =>
                {
                    var db = EditorAssetDatabase.Instance;
                    if (db == null) return;
                    foreach (var sel in Selection.GetSelected<ContentItem>())
                        if (sel.Guid != Guid.Empty) db.Reimport(sel.Guid);
                });
            }

            builder.Item($"{EditorIcons.Copy}  Copy Path", () =>
            {
                Runtime.Debug.Log($"Path: {item.RelativePath}");
            });

            if (!item.IsFolder && item.Guid != Guid.Empty)
            {
                builder.Item($"{EditorIcons.Fingerprint}  Copy GUID", () =>
                {
                    Runtime.Debug.Log($"GUID: {item.Guid}");
                });
            }

            builder.Separator();

            bool isRoot = string.IsNullOrEmpty(item.RelativePath);
            builder.Item($"{EditorIcons.PenToSquare}  Rename", () => StartRename(item, inTree), enabled: !isMulti && !isRoot);

            builder.Item($"{EditorIcons.Trash}  Delete", () =>
            {
                var db = EditorAssetDatabase.Instance;
                if (db == null) return;
                foreach (var sel in Selection.GetSelected<ContentItem>().ToList())
                {
                    if (string.IsNullOrEmpty(sel.RelativePath)) continue; // Can't delete root
                    if (sel.IsFolder)
                    {
                        string absPath = Path.Combine(Project.Current!.AssetsPath, sel.RelativePath);
                        if (Directory.Exists(absPath))
                        {
                            Directory.Delete(absPath, true);
                            string metaPath = MetaFile.GetMetaPath(absPath);
                            if (File.Exists(metaPath)) File.Delete(metaPath);
                        }
                        if (_currentFolder == sel.RelativePath || _currentFolder.StartsWith(sel.RelativePath + "/"))
                            _currentFolder = "";
                    }
                    else
                    {
                        db.DeleteAsset(sel.RelativePath);
                    }
                }
                Selection.Clear();
            }, enabled: !isRoot);
        });
    }

    private void BuildFolderTreeContextMenu(Paper paper, string id, string relativePath)
    {
        var item = new ContentItem
        {
            Name = string.IsNullOrEmpty(relativePath) ? "Assets" : Path.GetFileName(relativePath),
            RelativePath = relativePath,
            IsFolder = true,
            Icon = EditorIcons.Folder,
            TypeLabel = "Folder"
        };
        BuildItemContextMenu(paper, id, item, inTree: true);
    }

    private void OnCreated(string relativePath)
    {
        // Select the newly created item and enter rename
        var newItem = new ContentItem
        {
            Name = Path.GetFileName(relativePath),
            RelativePath = relativePath,
            IsFolder = Directory.Exists(Path.Combine(Project.Current!.AssetsPath, relativePath)),
            Icon = EditorIcons.File
        };
        Selection.Select(newItem);
        StartRename(newItem);
    }

    private void StartRename(ContentItem item, bool inTree = false)
    {
        _renamingPath = item.RelativePath;
        _renameText = item.IsFolder ? item.Name : Path.GetFileNameWithoutExtension(item.Name);
        _renameInTree = inTree;
    }

    private void CommitRename(ContentItem item)
    {
        if (_renamingPath == null) return;
        string ext = item.IsFolder ? "" : Path.GetExtension(item.Name);
        string newName = _renameText + ext;
        if (newName == item.Name || string.IsNullOrWhiteSpace(_renameText))
        {
            _renamingPath = null;
            return;
        }

        string parentFolder = Path.GetDirectoryName(item.RelativePath)?.Replace('\\', '/') ?? "";
        string newRelPath = string.IsNullOrEmpty(parentFolder) ? newName : parentFolder + "/" + newName;

        if (item.IsFolder)
        {
            string oldAbs = Path.Combine(Project.Current!.AssetsPath, item.RelativePath);
            string newAbs = Path.Combine(Project.Current.AssetsPath, newRelPath);
            if (Directory.Exists(oldAbs) && !Directory.Exists(newAbs))
            {
                Directory.Move(oldAbs, newAbs);
                string oldMeta = MetaFile.GetMetaPath(oldAbs);
                string newMeta = MetaFile.GetMetaPath(newAbs);
                if (File.Exists(oldMeta)) File.Move(oldMeta, newMeta);
                if (_currentFolder == item.RelativePath)
                    _currentFolder = newRelPath;
            }
        }
        else
        {
            EditorAssetDatabase.Instance?.MoveAsset(item.RelativePath, newRelPath);
        }
        _renamingPath = null;
    }

    private static void OpenWithSystem(ContentItem item)
    {
        string absPath = Path.Combine(Project.Current!.AssetsPath, item.RelativePath);
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(absPath) { UseShellExecute = true });
        }
        catch { }
    }

    private static void ShowInExplorer(ContentItem item)
    {
        string absPath = Path.Combine(Project.Current!.AssetsPath, item.RelativePath);
        ShowInExplorerPath(absPath);
    }

    private static void ShowInExplorerPath(string absPath)
    {
        try
        {
            if (Directory.Exists(absPath))
                System.Diagnostics.Process.Start("explorer.exe", absPath);
            else if (File.Exists(absPath))
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{absPath}\"");
        }
        catch { }
    }

    // ================================================================
    //  Grid View
    // ================================================================

    private void DrawGridView(Paper paper, Prowl.Scribe.FontFile font, List<ContentItem> entries, float width)
    {
        float cellSize = _thumbnailSize + 8f;
        float labelH = 18f;
        float totalCellH = cellSize + labelH;
        int cols = Math.Max(1, (int)((width - 16) / cellSize));

        var itemObjects = entries.Select(e => (object)e).ToList();

        int row = 0;
        for (int i = 0; i < entries.Count; i += cols)
        {
            using (paper.Row($"proj_gr_{row}")
                .Height(totalCellH).RowBetween(4).ChildLeft(4)
                .Enter())
            {
                for (int j = 0; j < cols && i + j < entries.Count; j++)
                {
                    int idx = i + j;
                    var item = entries[idx];
                    bool isSelected = Selection.IsSelected(item);

                    using (paper.Column($"proj_gc_{idx}")
                        .Width(cellSize).Height(totalCellH)
                        .BackgroundColor(isSelected ? EditorTheme.Accent : Color.Transparent)
                        .Hovered.BackgroundColor(isSelected ? EditorTheme.Accent : Color.FromArgb(30, 255, 255, 255)).End()
                        .Rounded(4)
                        .OnClick((item, idx, itemObjects), (cap, e) =>
                        {
                            bool ctrl = _paper?.IsKeyDown(PaperKey.LeftControl) == true || _paper?.IsKeyDown(PaperKey.RightControl) == true;
                            bool shift = _paper?.IsKeyDown(PaperKey.LeftShift) == true || _paper?.IsKeyDown(PaperKey.RightShift) == true;
                            Selection.HandleListClick(cap.Item1, (IReadOnlyList<object>)cap.Item3, cap.Item2, ctrl, shift);
                        })
                        .OnDoubleClick(item, (it, _) =>
                        {
                            if (it.IsFolder)
                                _currentFolder = it.RelativePath;
                        })
                        .Enter())
                    {
                        // Thumbnail area
                        paper.Box($"proj_gt_{idx}")
                            .Width(cellSize - 4).Height(cellSize - 4)
                            .Margin(2, 2, 2, 0)
                            .BackgroundColor(Color.FromArgb(30, 0, 0, 0))
                            .Rounded(4)
                            .Text(item.Icon, font)
                            .TextColor(item.IsFolder ? Color.FromArgb(255, 220, 180, 80) : EditorTheme.TextDim)
                            .FontSize(_thumbnailSize * 0.4f)
                            .Alignment(TextAlignment.MiddleCenter);

                        // Label (inline rename or text)
                        if (_renamingPath == item.RelativePath && !_renameInTree)
                        {
                            EditorGUI.TextField(paper, $"proj_gl_rename_{idx}", "", _renameText)
                                .OnValueChanged(v => _renameText = v);
                            if (_paper?.IsKeyDown(PaperKey.Enter) == true || _paper?.IsKeyDown(PaperKey.KeypadEnter) == true)
                                CommitRename(item);
                            else if (_paper?.IsKeyDown(PaperKey.Escape) == true)
                                _renamingPath = null;
                        }
                        else
                        {
                            paper.Box($"proj_gl_{idx}")
                                .Width(cellSize).Height(labelH)
                                .Clip()
                                .Text(item.Name, font)
                                .TextColor(EditorTheme.Text)
                                .FontSize(EditorTheme.FontSize - 4)
                                .Alignment(TextAlignment.MiddleCenter);
                        }

                        // Right-click context menu
                        BuildItemContextMenu(paper, $"proj_gc_ctx_{idx}", item);
                    }
                }
            }
            row++;
        }
    }

    // ================================================================
    //  Content Item Helpers
    // ================================================================

    private List<ContentItem> GetContentEntries(EditorAssetDatabase db)
    {
        var items = new List<ContentItem>();
        string folderAbsPath = string.IsNullOrEmpty(_currentFolder)
            ? Project.Current!.AssetsPath
            : Path.Combine(Project.Current!.AssetsPath, _currentFolder);

        if (!Directory.Exists(folderAbsPath)) return items;

        // Subdirectories first
        try
        {
            foreach (var dir in Directory.GetDirectories(folderAbsPath).OrderBy(d => d))
            {
                string dirName = Path.GetFileName(dir);
                if (dirName.StartsWith('.')) continue;
                string relPath = Path.GetRelativePath(Project.Current.AssetsPath, dir).Replace('\\', '/');

                items.Add(new ContentItem
                {
                    Name = dirName,
                    RelativePath = relPath,
                    IsFolder = true,
                    Icon = EditorIcons.Folder,
                    TypeLabel = "Folder"
                });
            }
        }
        catch { }

        // Files
        try
        {
            foreach (var file in Directory.GetFiles(folderAbsPath).OrderBy(f => f))
            {
                string fileName = Path.GetFileName(file);
                if (fileName.StartsWith('.') || fileName.EndsWith(".meta")) continue;

                string relPath = Path.GetRelativePath(Project.Current.AssetsPath, file).Replace('\\', '/');
                string ext = Path.GetExtension(fileName).ToLowerInvariant();
                var entry = db.GetEntry(relPath);

                items.Add(new ContentItem
                {
                    Name = fileName,
                    RelativePath = relPath,
                    IsFolder = false,
                    Icon = GetFileIcon(ext),
                    TypeLabel = entry?.MainAssetType?.Name ?? ext.TrimStart('.').ToUpperInvariant(),
                    Guid = entry?.Guid ?? Guid.Empty
                });
            }
        }
        catch { }

        // Apply search filter
        if (!string.IsNullOrEmpty(_searchText))
            items = items.Where(i => i.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase)).ToList();

        return items;
    }

    private static string GetFileIcon(string ext)
    {
        return ext switch
        {
            ".cs" => EditorIcons.FileCode,
            ".shader" or ".glsl" or ".hlsl" => EditorIcons.WandMagicSparkles,
            ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tga" or ".psd" or ".hdr" => EditorIcons.FileImage,
            ".mp3" or ".wav" or ".ogg" or ".flac" => EditorIcons.FileAudio,
            ".mp4" or ".avi" or ".mkv" or ".mov" => EditorIcons.FileVideo,
            ".fbx" or ".obj" or ".gltf" or ".glb" or ".dae" => EditorIcons.VectorSquare,
            ".scene" => EditorIcons.Cubes,
            ".mat" => EditorIcons.Palette,
            ".pdf" => EditorIcons.FilePdf,
            ".txt" or ".md" or ".json" or ".xml" or ".yaml" => EditorIcons.FileLines,
            ".zip" or ".rar" or ".7z" => EditorIcons.FileZipper,
            _ => EditorIcons.File,
        };
    }
}

/// <summary>
/// Represents a single item (file or folder) in the Project panel content view.
/// </summary>
public class ContentItem
{
    public string Name = "";
    public string RelativePath = "";
    public bool IsFolder;
    public string Icon = "";
    public string TypeLabel = "";
    public Guid Guid;

    public override bool Equals(object? obj) => obj is ContentItem c && c.RelativePath == RelativePath;
    public override int GetHashCode() => RelativePath.GetHashCode();
}
