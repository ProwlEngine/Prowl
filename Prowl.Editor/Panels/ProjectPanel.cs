using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

using Prowl.Editor.Docking;
using Prowl.Editor.Widgets;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;

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
    // Rename state is managed by RenameOverlay
    private static readonly HashSet<Guid> _expandedAssets = new(); // files with sub-assets expanded
    private static readonly Dictionary<Guid, Prowl.Runtime.Resources.Texture2D?> _thumbnailCache = new();
    private static Guid _pendingPingNavigate; // Navigate to pinged asset's folder on next frame
    private static Guid _lastPingedGuid; // Track when a new ping starts
    private const float MinThumbSize = 20f;  // Below this = list mode
    private const float MaxThumbSize = 128f;
    private const float ListThreshold = 32f; // Below this = list view

    private const float ToolbarHeight = 30;
    private const float FolderTreeWidth = 180f;

    public override void OnGUI(Paper paper, float width, float height)
    {
        _paper = paper;
        var font = EditorTheme.DefaultFont;
        if (font == null || Project.Current == null) return;

        // Detect new ping and navigate to the pinged asset's folder
        if (Selection.PingedGuid != Guid.Empty && Selection.PingedGuid != _lastPingedGuid)
        {
            _lastPingedGuid = Selection.PingedGuid;
            _pendingPingNavigate = Selection.PingedGuid;
        }
        if (Selection.PingedGuid == Guid.Empty)
            _lastPingedGuid = Guid.Empty;

        if (_pendingPingNavigate != Guid.Empty)
        {
            var pingGuid = _pendingPingNavigate;
            _pendingPingNavigate = Guid.Empty;
            var db = EditorAssetDatabase.Instance;
            if (db != null)
            {
                string? path = db.GuidToPath(pingGuid);
                if (path != null)
                {
                    // Navigate to the folder containing this asset
                    string folder = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "";
                    _currentFolder = folder;

                    // Reset the content scroll so the pinged item lands in view — stored scroll
                    // from a previous folder can otherwise leave the row off-screen.
                    ScrollView.ScrollTo("proj_content", 0f);
                }
            }
        }

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
            .ChildLeft(4)
            .ChildRight(4)
            .RowBetween(4)
            .ChildTop(4)
            .ChildBottom(0)
            .Enter())
        {
            // Add button with context menu
            using (paper.Box("proj_add")
                .Size(ToolbarHeight - 6)
                .Rounded(4)
                .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                .Text(EditorIcons.FileCirclePlus, font).TextColor(EditorTheme.Ink400)
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
            paper.Box("proj_spacer").Width(UnitValue.Stretch(2f));

            // Thumbnail size slider
            paper.Box("proj_list_ico")
                .Size(ToolbarHeight - 6)
                .Text(EditorIcons.List, font).TextColor(EditorTheme.Ink400)
                .FontSize(14f).Alignment(TextAlignment.MiddleCenter);

            EditorGUI.Slider(paper, "proj_thumb_slider", "", _thumbnailSize, MinThumbSize, MaxThumbSize, false)
                .OnValueChanged(v => _thumbnailSize = v);

            paper.Box("proj_grid_ico")
                .Size(ToolbarHeight - 6)
                .Text(EditorIcons.TableCellsLarge, font).TextColor(EditorTheme.Ink400)
                .FontSize(14f).Alignment(TextAlignment.MiddleCenter);

            // Search
            EditorGUI.SearchBar(paper, "proj_search", _searchText, "Search...")
                .OnValueChanged(v => _searchText = v);

            // Refresh button
            paper.Box("proj_refresh")
                .Size(ToolbarHeight - 6)
                .Rounded(4)
                .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                .Text(EditorIcons.ArrowRotateRight, font).TextColor(EditorTheme.Ink400)
                .FontSize(14f).Alignment(TextAlignment.MiddleCenter)
                .OnClick((_) =>
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
            .BackgroundColor(EditorTheme.Neutral400)
            .OnClick(0, (_, _) => Selection.Clear())
            .OnRightClick(0, (_, _) => Selection.Clear())
            .Enter())
        {
            // Right-click background — show create/explorer menu
            BuildBackgroundContextMenu(paper, "proj_tree_bg_ctx");

            using (ScrollView.Begin(paper, "proj_tree", FolderTreeWidth, height, 4, 4, 4, 4))
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
            .BackgroundColor(isSelected ? EditorTheme.Ink100 : Color.Transparent)
            .Hovered.BackgroundColor(EditorTheme.Ink200).End()
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
                    .TextColor(EditorTheme.Ink500)
                    .FontSize(10f).Alignment(TextAlignment.MiddleCenter)
                    .OnClick(stateKey, (key, e) =>
                    {
                        e.StopPropagation();
                        _folderOpenState[key] = !_folderOpenState.GetValueOrDefault(key, depth < 2);
                    });
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

            // Name (inline rename or label)
            if (RenameOverlay.IsRenaming($"proj_folder_{relativePath}"))
            {
                RenameOverlay.Draw(paper, $"proj_ft_rename_{relativePath.GetHashCode()}");
            }
            else
            {
                paper.Box($"proj_fl_{relativePath.GetHashCode()}")
                    .Height(22)
                    .Margin(4, 0, 0, 0)
                    .Text(displayName, font)
                    .TextColor(EditorTheme.Ink500)
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
            .BackgroundColor(EditorTheme.Neutral300)
            .OnClick(0, (_, _) => Selection.Clear())
            .OnRightClick(0, (_, _) => Selection.Clear())
            .Enter())
        {
            // Right-click background — show create/explorer menu
            BuildBackgroundContextMenu(paper, "proj_content_bg_ctx");

            // Project keyboard shortcuts
            if (paper.IsParentHovered && !ShortcutManager.IsRebinding)
            {
                if (ShortcutManager.IsPressed("Project/Delete"))
                    DeleteSelectedItems();
                else if (ShortcutManager.IsPressed("Project/Rename"))
                {
                    var first = Selection.GetSelected<ContentItem>().FirstOrDefault();
                    if (first != null) StartRename(first);
                }
            }

            // Accept GameObjectDragPayload to create prefabs
            if (DragDrop.IsDraggingType<GameObjectDragPayload>() && paper.IsParentHovered)
            {
                paper.Box("proj_prefab_drop")
                    .Height(24)
                    .BackgroundColor(System.Drawing.Color.FromArgb(40, EditorTheme.Purple400))
                    .Rounded(3)
                    .Text("Drop to create Prefab", EditorTheme.DefaultFont)
                    .TextColor(EditorTheme.Purple400)
                    .FontSize(EditorTheme.FontSize - 2)
                    .Alignment(TextAlignment.MiddleCenter);
            }

            var goDrop = DragDrop.AcceptDrop<GameObjectDragPayload>(paper.IsParentHovered);
            if (goDrop != null)
            {
                var go = goDrop.GameObjects.FirstOrDefault();
                if (go != null)
                {
                    string folder = _currentFolder;
                    string absFolder = string.IsNullOrEmpty(folder)
                        ? Project.Current!.AssetsPath
                        : Path.Combine(Project.Current!.AssetsPath, folder);
                    string uniqueName = AssetCreateMenu.FindUniqueName(absFolder, go.Name, ".prefab");
                    string relPath = string.IsNullOrEmpty(folder) ? uniqueName : folder + "/" + uniqueName;
                    Prefabs.PrefabUtility.CreatePrefab(go, relPath);
                }
            }

            // Breadcrumb
            DrawBreadcrumb(paper, font, width, 20);

            using (ScrollView.Begin(paper, "proj_content", width, height - 31))
            {
                using (paper.Column("proj_content_inner")
                    .Margin(6, 0, 0, 6)
                    .Height(UnitValue.Auto)
                    .Enter())
                {
                    if (entries.Count == 0)
                    {
                        paper.Box("proj_empty")
                            .Height(60)
                            .Text("This folder is empty", font)
                            .TextColor(EditorTheme.Ink300)
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
            .Height(height)
            .Margin(6, 6)
            .RowBetween(2)
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
                        .Width(UnitValue.Auto)
                        .Height(EditorTheme.RowHeight)
                        .Text(EditorIcons.AngleRight, font)
                        .TextColor(EditorTheme.Ink300)
                        .FontSize(8f)
                        .Alignment(TextAlignment.MiddleCenter);
                }
                
                using (paper.Box($"proj_bc_{i}")
                    .Width(UnitValue.Auto)
                    .Height(EditorTheme.RowHeight)
                    .Hovered.BackgroundColor(EditorTheme.Neutral500).End()
                    .Rounded(3)
                    .OnClick(path, (p, _) => _currentFolder = p)
                    .Enter())
                {
                    paper.Box("breadcrump")
                        .Width(UnitValue.Auto)
                        .Height(EditorTheme.RowHeight)
                        .Margin(5, 0)
                        .Text(name, font)
                        .TextColor(i == parts.Count - 1 ? EditorTheme.Ink500 : EditorTheme.Ink300)
                        .FontSize(EditorTheme.FontSize - 2)
                        .Alignment(TextAlignment.MiddleCenter);
                }
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
            bool isPingedList = item.Guid != Guid.Empty && item.Guid == Selection.PingedGuid;
            int idx = i;

            using (paper.Row($"proj_li_{i}")
                .Height(22)
                .BackgroundColor(isSelected ? EditorTheme.Purple300 : (i % 2 == 0 ? Color.Transparent : EditorTheme.Neutral400))
                .Hovered.BackgroundColor(isSelected ? EditorTheme.Purple300 : EditorTheme.Neutral500).End()
                .Rounded(3)
                .RowBetween(4)
                .OnClick((item, idx, itemObjects), (cap, e) =>
                {
                    e.StopPropagation();
                    bool ctrl = _paper?.IsKeyDown(PaperKey.LeftControl) == true || _paper?.IsKeyDown(PaperKey.RightControl) == true;
                    bool shift = _paper?.IsKeyDown(PaperKey.LeftShift) == true || _paper?.IsKeyDown(PaperKey.RightShift) == true;
                    Selection.HandleListClick(cap.Item1, (IReadOnlyList<object>)cap.Item3, cap.Item2, ctrl, shift);
                })
                .OnDoubleClick(item, (it, _) =>
                {
                    if (it.IsFolder)
                        _currentFolder = it.RelativePath;
                    else
                        EditorSceneManager.HandleAssetDoubleClick(it.RelativePath, it.Guid);
                })
                .OnDragStart(item, (it, _) =>
                {
                    if (!it.IsFolder && it.Guid != Guid.Empty)
                    {
                        Type? assetType = null;
                        if (it.IsSubAsset)
                        {
                            // Find sub-asset type from parent entry
                            var db = EditorAssetDatabase.Instance;
                            if (db != null)
                            {
                                var subs = db.GetSubAssets(it.ParentGuid);
                                var sub = subs.FirstOrDefault(s => s.Guid == it.Guid);
                                assetType = sub?.Type;
                            }
                        }
                        else
                        {
                            var entry = EditorAssetDatabase.Instance?.GetEntry(it.RelativePath);
                            assetType = entry?.MainAssetType;
                        }
                        DragDrop.StartDrag(new AssetDragPayload(it.Guid, it.Name, assetType));
                    }
                })
                .OnPostLayout((handle, rect) =>
                {
                    if (!isPingedList) return;
                    paper.Draw(ref handle, (canvas, r) =>
                    {
                        float alpha = Selection.GetPingAlpha();
                        if (alpha <= 0f) return;
                        int fillA = (int)(alpha * 60);
                        int borderA = (int)(alpha * 200);
                        var fillColor = Color.FromArgb(fillA, 255, 220, 50);
                        var borderColor = Color.FromArgb(borderA, 255, 200, 0);
                        float x = (float)r.Min.X, y = (float)r.Min.Y;
                        float w = (float)r.Size.X, h = (float)r.Size.Y;
                        canvas.RoundedRectFilled(x, y, w, h, 3, 3, 3, 3, fillColor);
                        canvas.SetStrokeColor(borderColor);
                        canvas.SetStrokeWidth(2f);
                        canvas.BeginPath();
                        canvas.RoundedRect(x + 1, y + 1, w - 2, h - 2, 2, 2, 2, 2);
                        canvas.Stroke();
                    });
                })
                .Enter())
            {
                // Sub-asset indent
                if (item.IsSubAsset)
                    paper.Box($"proj_li_indent_{i}").Width(16).Height(22);

                // Expand arrow for items with sub-assets
                if (item.HasSubAssets)
                {
                    bool expanded = _expandedAssets.Contains(item.Guid);
                    paper.Box($"proj_li_arrow_{i}")
                        .Width(14).Height(22)
                        .Text(expanded ? EditorIcons.AngleDown : EditorIcons.AngleRight, font)
                        .TextColor(EditorTheme.Ink400)
                        .FontSize(9f).Alignment(TextAlignment.MiddleCenter)
                        .OnClick(item.Guid, (guid, _) =>
                        {
                            if (_expandedAssets.Contains(guid)) _expandedAssets.Remove(guid);
                            else _expandedAssets.Add(guid);
                        });
                }

                // Icon
                paper.Box($"proj_li_ico_{i}")
                    .Width(18).Height(22)
                    .Text(item.Icon, font)
                    .TextColor(item.IsFolder ? Color.FromArgb(255, 220, 180, 80) : (item.IsSubAsset ? EditorTheme.Purple300 : EditorTheme.Ink400))
                    .FontSize(12f).Alignment(TextAlignment.MiddleCenter);

                // Name (inline rename or label)
                if (RenameOverlay.IsRenaming($"proj_asset_{item.RelativePath}"))
                {
                    RenameOverlay.Draw(paper, $"proj_li_rename_{i}");
                }
                else
                {
                    paper.Box($"proj_li_name_{i}")
                        .Height(22)
                        .Text(item.Name, font)
                        .TextColor(EditorTheme.Ink500)
                        .FontSize(EditorTheme.FontSize - 3)
                        .Alignment(TextAlignment.MiddleLeft);
                }

                // Type
                if (!item.IsFolder)
                {
                    paper.Box($"proj_li_type_{i}")
                        .Width(80).Height(22)
                        .Text(item.TypeLabel, font)
                        .TextColor(EditorTheme.Ink400)
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
                builder.Item($"Open", () => OpenWithSystem(item), icon: EditorIcons.FolderOpen);
                builder.Separator();
            }

            builder.Submenu($"Create", sub => AssetCreateMenu.Build(sub, folder, OnCreated), icon: EditorIcons.FileCirclePlus);

            builder.Separator();

            builder.Item($"Show in Explorer", () => ShowInExplorer(item), icon: EditorIcons.FolderOpen);

            if (!item.IsFolder)
            {
                builder.Item($"Reimport", () =>
                {
                    var db = EditorAssetDatabase.Instance;
                    if (db == null) return;
                    foreach (var sel in Selection.GetSelected<ContentItem>())
                        if (sel.Guid != Guid.Empty) db.Reimport(sel.Guid);
                }, icon: EditorIcons.ArrowsRotate);
            }

            builder.Item($"Copy Path", () =>
            {
                Runtime.Debug.Log($"Path: {item.RelativePath}");
            }, icon: EditorIcons.Copy);

            if (!item.IsFolder && item.Guid != Guid.Empty)
            {
                builder.Item($"Copy GUID", () =>
                {
                    Runtime.Debug.Log($"GUID: {item.Guid}");
                }, icon: EditorIcons.Fingerprint);
            }

            builder.Separator();

            bool isRoot = string.IsNullOrEmpty(item.RelativePath);
            builder.Item($"Rename", () => StartRename(item, inTree), enabled: !isMulti && !isRoot, icon: EditorIcons.PenToSquare);

            builder.Item($"Delete", () => DeleteSelectedItems(), enabled: !isRoot, icon: EditorIcons.Trash);
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

    private void BuildBackgroundContextMenu(Paper paper, string id)
    {
        ContextMenuHelper.RightClickMenu(paper, id, builder =>
        {
            string folder = _currentFolder;

            builder.Submenu("Create", sub => AssetCreateMenu.Build(sub, folder, OnCreated), icon: EditorIcons.FileCirclePlus);
            builder.Separator();

            builder.Item("Show in Explorer", () =>
            {
                string absPath = Path.Combine(Project.Current!.AssetsPath, folder);
                ReferenceOpenerService.OpenFileSystemPath(absPath);
            }, icon: EditorIcons.FolderOpen);

            builder.Separator();

            builder.Item("Reimport All", () =>
            {
                var db = EditorAssetDatabase.Instance;
                if (db == null) return;
                foreach (var e in db.GetAllEntries().ToList())
                    db.Reimport(e.Guid);
                Runtime.Debug.Log("[AssetDatabase] Reimported all assets.");
            }, icon: EditorIcons.ArrowsRotate);
        });
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

    private void DeleteSelectedItems()
    {
        var selected = Selection.GetSelected<ContentItem>().ToList();
        if (selected.Count == 0) return;

        string names = selected.Count == 1 ? selected[0].Name : $"{selected.Count} items";

        ModalDialog.Confirm("Delete Assets", $"Are you sure you want to delete {names}?\nThis cannot be undone.", () =>
        {
            var db = EditorAssetDatabase.Instance;
            if (db == null) return;
            foreach (var sel in selected)
            {
                if (string.IsNullOrEmpty(sel.RelativePath)) continue;
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
        });
    }

    private void StartRename(ContentItem item, bool inTree = false)
    {
        string id = inTree ? $"proj_folder_{item.RelativePath}" : $"proj_asset_{item.RelativePath}";
        string editName = item.IsFolder ? item.Name : Path.GetFileNameWithoutExtension(item.Name);

        RenameOverlay.Begin(id, editName, newText =>
        {
            string ext = item.IsFolder ? "" : Path.GetExtension(item.Name);
            string newName = newText + ext;
            if (newName == item.Name || string.IsNullOrWhiteSpace(newText))
                return;

            string parentFolder = Path.GetDirectoryName(item.RelativePath)?.Replace('\\', '/') ?? "";
            string newRelPath = string.IsNullOrEmpty(parentFolder) ? newName : parentFolder + "/" + newName;

            if (item.IsFolder)
            {
                string oldAbs = Path.Combine(Project.Current!.AssetsPath, item.RelativePath);
                string newAbs = Path.Combine(Project.Current.AssetsPath, newRelPath);
                if (Directory.Exists(newAbs) || File.Exists(newAbs))
                {
                    Widgets.Toasts.Show("Rename Failed", $"A file or folder named '{newName}' already exists.", Widgets.ToastType.Warning, 3f);
                }
                else if (Directory.Exists(oldAbs))
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
                bool success = EditorAssetDatabase.Instance?.MoveAsset(item.RelativePath, newRelPath) ?? false;
                if (!success)
                    Widgets.Toasts.Show("Rename Failed", $"A file named '{newName}' already exists.", Widgets.ToastType.Warning, 3f);
            }
        });
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
        ReferenceOpenerService.OpenFileSystemPath(absPath);
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
                .Height(totalCellH)
                .RowBetween(6)
                .Enter())
            {
                for (int j = 0; j < cols && i + j < entries.Count; j++)
                {
                    int idx = i + j;
                    var item = entries[idx];
                    bool isSelected = Selection.IsSelected(item);

                    DrawGridItem(paper, font, $"proj_gc_{idx}", item, idx, itemObjects, cellSize, labelH, totalCellH);
                }
            }
            row++;
        }
    }

    private void DrawGridItem(Paper paper, Prowl.Scribe.FontFile font, string id, ContentItem item,
        int idx, List<object> itemObjects, float cellSize, float labelH, float totalCellH)
    {
        bool isSelected = Selection.IsSelected(item);
        bool isSubAsset = item.IsSubAsset;
        bool isPinged = item.Guid != Guid.Empty && item.Guid == Selection.PingedGuid;

        using (paper.Column(id)
            .Width(cellSize).Height(totalCellH)
            .BackgroundColor(isSelected ? EditorTheme.Purple300 : (isSubAsset ? Color.FromArgb(20, EditorTheme.Purple400) : Color.Transparent))
            .Hovered.BackgroundColor(isSelected ? EditorTheme.Purple300 : Color.FromArgb(30, 255, 255, 255)).End()
            .Rounded(4)
            .OnClick((item, idx, itemObjects), (cap, e) =>
            {
                e.StopPropagation();
                bool ctrl = _paper?.IsKeyDown(PaperKey.LeftControl) == true || _paper?.IsKeyDown(PaperKey.RightControl) == true;
                bool shift = _paper?.IsKeyDown(PaperKey.LeftShift) == true || _paper?.IsKeyDown(PaperKey.RightShift) == true;
                Selection.HandleListClick(cap.Item1, (IReadOnlyList<object>)cap.Item3, cap.Item2, ctrl, shift);
            })
            .OnDoubleClick(item, (it, _) =>
            {
                if (it.IsFolder)
                    _currentFolder = it.RelativePath;
                else if (it.HasSubAssets)
                {
                    if (_expandedAssets.Contains(it.Guid)) _expandedAssets.Remove(it.Guid);
                    else _expandedAssets.Add(it.Guid);
                }
                else
                    EditorSceneManager.HandleAssetDoubleClick(it.RelativePath, it.Guid);
            })
            .OnDragStart(item, (it, _) =>
            {
                if (!it.IsFolder && it.Guid != Guid.Empty)
                {
                    var entry = EditorAssetDatabase.Instance?.GetEntry(it.RelativePath);
                    Type? assetType = entry?.MainAssetType;
                    if (it.IsSubAsset)
                    {
                        var db = EditorAssetDatabase.Instance;
                        if (db != null)
                        {
                            var subs = db.GetSubAssets(it.ParentGuid);
                            var sub = subs.FirstOrDefault(s => s.Guid == it.Guid);
                            assetType = sub?.Type;
                        }
                    }
                    DragDrop.StartDrag(new AssetDragPayload(it.Guid, it.Name, assetType));
                }
            })
            .Tooltip(item.Name)
            .OnPostLayout((handle, rect) =>
            {
                if (!isPinged) return;
                paper.Draw(ref handle, (canvas, r) =>
                {
                    float alpha = Selection.GetPingAlpha();
                    if (alpha <= 0f) return;
                    int fillA = (int)(alpha * 60);
                    int borderA = (int)(alpha * 200);
                    var fillColor = Color.FromArgb(fillA, 255, 220, 50);
                    var borderColor = Color.FromArgb(borderA, 255, 200, 0);
                    float x = (float)r.Min.X, y = (float)r.Min.Y;
                    float w = (float)r.Size.X, h = (float)r.Size.Y;
                    canvas.RoundedRectFilled(x, y, w, h, 4, 4, 4, 4, fillColor);
                    canvas.SetStrokeColor(borderColor);
                    canvas.SetStrokeWidth(2f);
                    canvas.BeginPath();
                    canvas.RoundedRect(x + 1, y + 1, w - 2, h - 2, 3, 3, 3, 3);
                    canvas.Stroke();
                });
            })
            .Enter())
        {
            // Thumbnail area
            var thumbTex = GetThumbnailTexture(item.Guid);
            if (thumbTex != null)
            {
                paper.Box($"{id}_t")
                    .Width(cellSize - 4).Height(cellSize - 4)
                    .Margin(2, 2, 2, 0)
                    .Rounded(4)
                    .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
                    {
                        canvas.DrawImage(thumbTex,
                            (float)r.Min.X, (float)r.Min.Y,
                            (float)r.Size.X, (float)r.Size.Y);
                    }));
            }
            else
            {
                paper.Box($"{id}_t")
                    .Width(cellSize - 4).Height(cellSize - 4)
                    .Margin(2, 2, 2, 0)
                    .Rounded(4)
                    .Text(item.Icon, font)
                    .TextColor(item.IsFolder ? Color.FromArgb(255, 220, 180, 80) : (isSubAsset ? EditorTheme.Purple300 : EditorTheme.Ink400))
                    .FontSize(_thumbnailSize * 0.6f)
                    .Alignment(TextAlignment.MiddleCenter);
            }

            // Expand indicator for items with sub-assets
            if (item.HasSubAssets)
            {
                bool expanded = _expandedAssets.Contains(item.Guid);
                paper.Box($"{id}_exp")
                    .PositionType(PositionType.SelfDirected)
                    .Position(2, 2)
                    .Size(16, 16).Rounded(3)
                    .BackgroundColor(Color.FromArgb(160, 30, 30, 30))
                    .Text(expanded ? EditorIcons.AngleDown : EditorIcons.AngleRight, font)
                    .TextColor(EditorTheme.Ink400)
                    .FontSize(8f).Alignment(TextAlignment.MiddleCenter)
                    .OnClick(item.Guid, (guid, _) =>
                    {
                        if (_expandedAssets.Contains(guid)) _expandedAssets.Remove(guid);
                        else _expandedAssets.Add(guid);
                    });
            }

            // Label
            if (RenameOverlay.IsRenaming($"proj_asset_{item.RelativePath}"))
            {
                RenameOverlay.Draw(paper, $"{id}_rename");
            }
            else
            {
                paper.Box($"{id}_l")
                    .Width(cellSize).Height(labelH)
                    .Clip()
                    .Text(item.Name, font)
                    .TextColor(isSubAsset ? EditorTheme.Purple300 : EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSize - 2).Alignment(TextAlignment.MiddleCenter);
            }

            BuildItemContextMenu(paper, $"{id}_ctx", item);
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

                bool hasSubAssets = entry?.SubAssets != null && entry.SubAssets.Length > 0;

                items.Add(new ContentItem
                {
                    Name = fileName,
                    RelativePath = relPath,
                    IsFolder = false,
                    Icon = GetFileIcon(ext),
                    TypeLabel = entry?.MainAssetType?.Name ?? ext.TrimStart('.').ToUpperInvariant(),
                    Guid = entry?.Guid ?? Guid.Empty,
                    HasSubAssets = hasSubAssets
                });

                // Insert sub-assets if expanded
                if (hasSubAssets && entry != null && _expandedAssets.Contains(entry.Guid))
                {
                    foreach (var sub in entry.SubAssets)
                    {
                        items.Add(new ContentItem
                        {
                            Name = sub.Name,
                            RelativePath = $"{relPath}#{sub.Name}",
                            IsFolder = false,
                            IsSubAsset = true,
                            Icon = GetSubAssetIcon(sub.Type),
                            TypeLabel = sub.Type?.Name ?? "Unknown",
                            Guid = sub.Guid,
                            ParentGuid = entry.Guid
                        });
                    }
                }
            }
        }
        catch { }

        // Apply search filter
        if (!string.IsNullOrEmpty(_searchText))
            items = items.Where(i => i.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase)).ToList();

        return items;
    }

    private static string GetFileIcon(string ext) => FileIconRegistry.GetIconForExtension(ext);

    private static Prowl.Runtime.Resources.Texture2D? GetThumbnailTexture(Guid guid)
    {
        if (guid == Guid.Empty) return null;

        if (_thumbnailCache.TryGetValue(guid, out var cached))
            return cached;

        // Try loading from disk — don't cache null so we retry when thumbnail is generated
        var db = EditorAssetDatabase.Instance;
        if (db == null) return null;

        byte[]? pixels = db.LoadThumbnail(guid);
        if (pixels == null || pixels.Length == 0) return null;

        try
        {
            int size = ThumbnailGenerator.ThumbnailSize;
            var tex = new Prowl.Runtime.Resources.Texture2D((uint)size, (uint)size, false, TextureImageFormat.Color4b);
            tex.SetData<byte>(pixels);
            tex.SetTextureFilters(TextureMin.Linear, TextureMag.Linear);
            _thumbnailCache[guid] = tex;
            return tex;
        }
        catch
        {
            _thumbnailCache[guid] = null;
            return null;
        }
    }

    /// <summary>Clear the thumbnail cache (e.g. after reimport).</summary>
    public static void ClearThumbnailCache()
    {
        foreach (var tex in _thumbnailCache.Values)
            tex?.Dispose();
        _thumbnailCache.Clear();
    }

    /// <summary>Invalidate a single thumbnail so it reloads from disk on next access.</summary>
    public static void InvalidateThumbnail(Guid guid)
    {
        if (_thumbnailCache.TryGetValue(guid, out var tex))
        {
            tex?.Dispose();
            _thumbnailCache.Remove(guid);
        }
    }

    private static string GetSubAssetIcon(Type? type)
    {
        if (type == null) return EditorIcons.File;
        if (typeof(Prowl.Runtime.Resources.Mesh).IsAssignableFrom(type)) return EditorIcons.VectorSquare;
        if (typeof(Prowl.Runtime.Resources.Material).IsAssignableFrom(type)) return EditorIcons.Palette;
        if (typeof(Prowl.Runtime.AnimationClip).IsAssignableFrom(type)) return EditorIcons.Film;
        if (typeof(Prowl.Runtime.Resources.Texture2D).IsAssignableFrom(type)) return EditorIcons.FileImage;
        return EditorIcons.File;
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
    public bool IsSubAsset;
    public string Icon = "";
    public string TypeLabel = "";
    public Guid Guid;
    public Guid ParentGuid; // For sub-assets: the parent file's GUID
    public bool HasSubAssets; // True if this file has expandable sub-assets

    public override bool Equals(object? obj) => obj is ContentItem c && c.Guid == Guid && c.RelativePath == RelativePath;
    public override int GetHashCode() => Guid != Guid.Empty ? Guid.GetHashCode() : RelativePath.GetHashCode();
}
