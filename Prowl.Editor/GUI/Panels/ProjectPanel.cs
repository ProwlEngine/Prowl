using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.OrigamiUI;
using Prowl.Editor.GUI.Popups;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Vector;

using Color = System.Drawing.Color;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.AssetsDatabase;
using Prowl.Editor.Utils;
using Prowl.Editor.GUI.Registries;
using Prowl.Editor.Core;
using Prowl.Editor.Theming;

namespace Prowl.Editor.GUI.Panels;

[EditorWindow("General/Project")]
public class ProjectPanel : DockPanel
{
    public static ProjectPanel Instance;
    public override string Title => Loc.Get("panel.project");
    public override string Icon => EditorIcons.Folder;

    // Handled Virtual (Placeholder) content to be displayed with normal objects
    public List<ContentItem> VirtualContentItems = new();

    public string CurrentFolder => _currentFolder;
    private string _currentFolder = ""; // Relative to Assets/, empty = Assets root
    private string _searchText = "";
    private float _thumbnailSize = 64f;
    private Paper? _paper; // Cached for modifier key checks in callbacks

    // Drag-hover tracking the mouse's current drop target while a drag is active. Tree
    // nodes and folder items in the grid/list set this via OnHover (same pattern the
    // HierarchyPanel uses). Reset each frame before the body draws.
    //   null   → mouse not over any folder drop target
    //   ""     → over the Assets root (represented by an empty relative path)
    //   "Foo"  → over folder 'Foo' (assets-relative)
    private string? _dragHoverFolder;      // current frame's resolved hover target (from last frame's callbacks)
    private string? _dragHoverFolderNext;  // written by deferred callbacks, promoted next frame
    private string? _dragDwellFolder;     // folder being dwelled on for auto-open
    private float _dragDwellTimer;        // seconds spent hovering the dwell folder
    private const float DragDwellOpenDelay = 0.75f;
    // True while the mouse is over the content-area background (not just a folder item) —
    // lets "drop on empty space" fall back to the currently-open folder.
    private bool _contentBgHovered;
    // Rename state is managed by RenameOverlay
    private static readonly HashSet<Guid> _expandedAssets = new(); // files with sub-assets expanded
    private static readonly Dictionary<Guid, Runtime.Resources.Texture2D?> _thumbnailCache = new();
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

        // Sets itself as instance on start
        Instance ??= this;

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

                    // Reset the content scroll so the pinged item lands in view stored scroll
                    // from a previous folder can otherwise leave the row off-screen.
                    Origami.ScrollTo("proj_content", new Float2(0, 0));
                }
            }
        }

        // Promote deferred hover target to current, then always clear the next slot.
        // Hover callbacks will re-set it this frame if the mouse is still over a folder.
        // On the drop frame, we keep the last promoted value (don't null it out).
        if (DragDrop.IsDropFrame)
        {
            // Drop frame: _dragHoverFolder already has the right value from last promotion
        }
        else
        {
            _dragHoverFolder = _dragHoverFolderNext;
            _dragHoverFolderNext = null;
        }
        _contentBgHovered = false;

        // Drag-dwell auto-open: if hovering the same folder for DragDwellOpenDelay, navigate into it
        if (DragDrop.IsDragging && _dragHoverFolder != null)
        {
            if (_dragHoverFolder == _dragDwellFolder)
            {
                _dragDwellTimer += (float)Runtime.Time.UnscaledDeltaTime;
                if (_dragDwellTimer >= DragDwellOpenDelay)
                {
                    _currentFolder = _dragDwellFolder;
                    _dragDwellFolder = null;
                    _dragDwellTimer = 0f;
                }
            }
            else
            {
                _dragDwellFolder = _dragHoverFolder;
                _dragDwellTimer = 0f;
            }
        }
        else
        {
            _dragDwellFolder = null;
            _dragDwellTimer = 0f;
        }

        using (paper.Column("proj_root").Size(width, height).Enter())
        {
            DrawToolbar(paper, font, width);
            DrawBody(paper, font, width, height - ToolbarHeight);
        }

        // Process drops at the end of the frame after every OnHover callback from the
        // tree and content panels has fired. Uses the captured drop target (_dragHoverFolder)
        // or falls back to the currently-open folder when the drop lands on empty space in
        // the content area.
        if (DragDrop.IsDropFrame && DragDrop.Payload != null)
        {
            string? target = _dragHoverFolder;
            if (target == null && _contentBgHovered) target = _currentFolder;
            if (target != null) DispatchProjectDrop(target, DragDrop.Payload);
        }
    }

    private void DispatchProjectDrop(string targetFolder, DragPayload payload)
    {
        switch (payload)
        {
            case AssetDragPayload ap when CanAcceptAssetDropInto(targetFolder):
                PerformAssetMove(ap, targetFolder);
                DragDrop.EndDrag();
                break;
            case GameObjectDragPayload gp:
                foreach (var go in gp.GameObjects)
                    if (go != null) CreatePrefabInFolder(go, targetFolder);
                DragDrop.EndDrag();
                break;
        }
    }

    // ================================================================
    //  Toolbar
    // ================================================================

    private void DrawToolbar(Paper paper, Scribe.FontFile font, float width)
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
                    Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, b =>
                        AssetCreateMenu.Build(b, _currentFolder, OnCreated));
                }
            }

            // Spacer
            paper.Box("proj_spacer").Width(UnitValue.Stretch(2f));

            // Thumbnail size slider
            paper.Box("proj_list_ico")
                .Size(ToolbarHeight - 6)
                .Text(EditorIcons.List, font).TextColor(EditorTheme.Ink400)
                .FontSize(14f).Alignment(TextAlignment.MiddleCenter);

            Origami.Slider(paper, "proj_thumb_slider", _thumbnailSize, v => _thumbnailSize = v,
                    MinThumbSize, MaxThumbSize)
                .ShowValue(false).Width(120f).Show();

            paper.Box("proj_grid_ico")
                .Size(ToolbarHeight - 6)
                .Text(EditorIcons.TableCellsLarge, font).TextColor(EditorTheme.Ink400)
                .FontSize(14f).Alignment(TextAlignment.MiddleCenter);

            // Search
            Origami.SearchField(paper, "proj_search", _searchText, v => _searchText = v).Show();

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
    //  Drag & Drop helpers
    // ================================================================

    /// <summary>
    /// Build an <see cref="AssetDragPayload"/> for starting a drag from <paramref name="item"/>.
    /// If the item is part of the current selection, the payload includes every selected
    /// item so a multi-select drag moves the whole set. Returns null for undraggable items
    /// (sub-assets they live inside a parent asset and can't be moved independently).
    /// </summary>
    private static AssetDragPayload? BuildAssetDragPayload(ContentItem item)
    {
        if (item.IsSubAsset) return null;

        // Primary lookup the item the user actually grabbed.
        Type? primaryType = null;
        if (!item.IsFolder && item.Guid != Guid.Empty)
            primaryType = EditorAssetDatabase.Instance?.GetEntry(item.RelativePath)?.MainAssetType;

        // Expand to the full selection ONLY if the grabbed item is inside it. That matches
        // how Explorer / Finder behave: clicking-and-dragging an unselected item drags just
        // that item, not the prior selection.
        var bundle = new List<ContentItem>();
        if (Selection.IsSelected(item))
        {
            foreach (var s in Selection.GetSelected<ContentItem>())
                if (!s.IsSubAsset) bundle.Add(s);
            // Ensure the grabbed item is first (payload.AssetGuid/Name reflect primary).
            bundle.Remove(item);
            bundle.Insert(0, item);
        }
        else
        {
            bundle.Add(item);
        }

        var guids = bundle.Select(b => b.Guid).ToArray();
        var paths = bundle.Select(b => b.RelativePath).ToArray();
        return new AssetDragPayload(item.Guid, item.Name, primaryType, guids, paths);
    }

    /// <summary>
    /// Move every asset/folder in <paramref name="payload"/> into <paramref name="destRelFolder"/>
    /// (assets-relative; empty = root). Skips no-ops and self-containment cycles, resolves
    /// name collisions with " (N)" suffixes, and refreshes thumbnails after the move.
    /// </summary>
    private void PerformAssetMove(AssetDragPayload payload, string destRelFolder)
    {
        var db = EditorAssetDatabase.Instance;
        if (db == null || Project.Current == null) return;

        destRelFolder = (destRelFolder ?? "").Replace('\\', '/').TrimEnd('/');
        string destAbs = string.IsNullOrEmpty(destRelFolder)
            ? Project.Current.AssetsPath
            : Path.Combine(Project.Current.AssetsPath, destRelFolder);
        if (!Directory.Exists(destAbs)) return;

        int moved = 0;
        foreach (var rawSrc in payload.AssetPaths)
        {
            if (string.IsNullOrEmpty(rawSrc)) continue;
            string src = rawSrc.Replace('\\', '/').TrimEnd('/');
            string srcName = Path.GetFileName(src);
            string srcDir = Path.GetDirectoryName(src)?.Replace('\\', '/') ?? "";

            // Already in target folder nothing to do.
            if (srcDir.Equals(destRelFolder, StringComparison.OrdinalIgnoreCase)) continue;

            string srcAbs = Path.Combine(Project.Current.AssetsPath, src);
            bool isFolder = Directory.Exists(srcAbs) && !File.Exists(srcAbs);

            if (isFolder)
            {
                // Can't drop a folder on itself or into one of its descendants that would
                // delete the folder mid-move.
                string srcWithSlash = src + "/";
                if (destRelFolder.Equals(src, StringComparison.OrdinalIgnoreCase)
                    || destRelFolder.StartsWith(srcWithSlash, StringComparison.OrdinalIgnoreCase))
                {
                    Runtime.Debug.LogWarning($"Skipped: can't move folder '{src}' into itself.");
                    continue;
                }

                string unique = AssetCreateMenu.FindUniqueName(destAbs, srcName, "");
                string newRel = string.IsNullOrEmpty(destRelFolder) ? unique : destRelFolder + "/" + unique;
                if (db.MoveFolder(src, newRel)) moved++;
            }
            else if (File.Exists(srcAbs))
            {
                string ext = Path.GetExtension(srcName);
                string baseName = Path.GetFileNameWithoutExtension(srcName);
                string unique = AssetCreateMenu.FindUniqueName(destAbs, baseName, ext);
                string newRel = string.IsNullOrEmpty(destRelFolder) ? unique : destRelFolder + "/" + unique;
                if (db.MoveAsset(src, newRel)) moved++;
            }
        }

        if (moved > 0)
        {
            _thumbnailCache.Clear(); // paths changed thumbnail lookup may be stale
            Runtime.Debug.Log($"Moved {moved} item(s) to '{(string.IsNullOrEmpty(destRelFolder) ? "Assets" : destRelFolder)}'.");
        }
    }

    /// <summary>
    /// Create a prefab for <paramref name="go"/> inside <paramref name="destRelFolder"/>
    /// (assets-relative; empty = root). Uniquifies the filename against what's already there.
    /// </summary>
    private static void CreatePrefabInFolder(GameObject go, string destRelFolder)
    {
        if (Project.Current == null) return;
        string absFolder = string.IsNullOrEmpty(destRelFolder)
            ? Project.Current.AssetsPath
            : Path.Combine(Project.Current.AssetsPath, destRelFolder);
        if (!Directory.Exists(absFolder)) return;

        string uniqueName = AssetCreateMenu.FindUniqueName(absFolder, go.Name, ".prefab");
        string relPath = string.IsNullOrEmpty(destRelFolder) ? uniqueName : destRelFolder + "/" + uniqueName;
        Prefabs.PrefabUtility.CreatePrefab(go, relPath);
    }

    /// <summary>
    /// True when an <see cref="AssetDragPayload"/> could meaningfully land in
    /// <paramref name="destRelFolder"/> used to gate the hover highlight so folders that
    /// would no-op (already the parent) or cycle (self/descendant) don't light up.
    /// </summary>
    private static bool CanAcceptAssetDropInto(string destRelFolder)
    {
        // Use HasPayloadType (not IsDraggingType) so this works on the drop frame
        // when IsDragging is already false but the payload is still available.
        if (!DragDrop.HasPayloadType<AssetDragPayload>()) return false;
        var payload = (AssetDragPayload)DragDrop.Payload!;

        foreach (var rawSrc in payload.AssetPaths)
        {
            if (string.IsNullOrEmpty(rawSrc)) continue;
            string src = rawSrc.Replace('\\', '/').TrimEnd('/');
            string srcDir = Path.GetDirectoryName(src)?.Replace('\\', '/') ?? "";

            if (srcDir.Equals(destRelFolder, StringComparison.OrdinalIgnoreCase)) continue;

            // Self / descendant cycle check (folders only).
            string srcWithSlash = src + "/";
            if (destRelFolder.Equals(src, StringComparison.OrdinalIgnoreCase)
                || destRelFolder.StartsWith(srcWithSlash, StringComparison.OrdinalIgnoreCase))
                continue;

            return true;
        }
        return false;
    }

    // ================================================================
    //  Body: Folder Tree + Content
    // ================================================================

    private void DrawBody(Paper paper, Scribe.FontFile font, float width, float height)
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

    private void DrawFolderTree(Paper paper, Scribe.FontFile font, float height)
    {
        using (paper.Box("proj_tree_bg")
            .Size(FolderTreeWidth, height)
            .BackgroundColor(EditorTheme.Neutral400)
            .OnClick(0, (_, _) => Selection.Clear())
            .Enter())
        {
            // Right-click background show create/explorer menu
            BuildBackgroundContextMenu(paper, "proj_tree_bg_ctx");

            // Build flat node list by walking directories recursively
            var nodes = new List<OrigamiUI.TreeNode>();
            BuildFolderNodes(nodes, Project.Current!.AssetsPath, "Assets", 0);

            // Build a parallel ContentItem list for multi-select via Selection.HandleListClick
            var folderItems = new List<object>();
            foreach (var n in nodes)
            {
                string relPath = (string)n.UserData!;
                folderItems.Add(new ContentItem
                {
                    Name = n.Label,
                    RelativePath = relPath,
                    IsFolder = true,
                    Icon = EditorIcons.Folder,
                    TypeLabel = "Folder"
                });
            }

            Origami.Tree(paper, "proj_tree", FolderTreeWidth, height)
                .Nodes(nodes)
                .MultiSelect()
                .IsSelected(n =>
                {
                    string relPath = (string)n.UserData!;
                    // Check if any selected ContentItem matches this folder
                    foreach (var sel in Selection.GetSelected<ContentItem>())
                        if (sel.IsFolder && sel.RelativePath == relPath) return true;
                    return _currentFolder == relPath;
                })
                .OnSelectModified((e, ctrl, shift) =>
                {
                    _currentFolder = (string)e.Node.UserData!;
                    Selection.HandleListClick(folderItems[e.Index], (IReadOnlyList<object>)folderItems, e.Index, ctrl, shift);
                })
                .OnDoubleClick(e => { /* tree handles expand toggle internally */ })
                .OnRightClick(e =>
                {
                    var item = (ContentItem)folderItems[e.Index];
                    if (!Selection.IsSelected(item))
                        Selection.Select(item);
                })
                .OnHover((n, normY) =>
                {
                    if (DragDrop.IsDragging || DragDrop.IsDropFrame)
                        _dragHoverFolderNext = (string)n.UserData!;
                })
                .CustomRowContent((p, node, isSel, isExp) =>
                {
                    string relativePath = (string)node.UserData!;

                    // Folder icon
                    p.Box($"proj_fi_{node.Id.GetHashCode()}")
                        .Width(18).Height(22)
                        .Text(EditorIcons.Folder, font)
                        .TextColor(Color.FromArgb(255, 220, 180, 80))
                        .FontSize(12f).Alignment(TextAlignment.MiddleCenter);

                    // Name (inline rename or label)
                    if (RenameOverlay.IsRenaming($"proj_folder_{relativePath}"))
                    {
                        RenameOverlay.Draw(p, $"proj_ft_rename_{node.Id.GetHashCode()}");
                    }
                    else
                    {
                        p.Box($"proj_fl_{node.Id.GetHashCode()}")
                            .Height(22)
                            .Margin(4, 0, 0, 0)
                            .Text(node.Label, font)
                            .TextColor(EditorTheme.Ink500)
                            .FontSize(EditorTheme.FontSize)
                            .Alignment(TextAlignment.MiddleLeft);
                    }

                    // Right-click context menu on folder tree
                    BuildFolderTreeContextMenu(p, $"proj_ft_ctx_{node.Id.GetHashCode()}", relativePath);

                    // Drop-target highlight
                    if (_dragHoverFolder == relativePath
                        && (DragDrop.IsDraggingType<GameObjectDragPayload>()
                            || (DragDrop.IsDraggingType<AssetDragPayload>() && CanAcceptAssetDropInto(relativePath))))
                    {
                        p.Box($"proj_fn_drop_{node.Id.GetHashCode()}")
                            .PositionType(PositionType.SelfDirected)
                            .Position(0, 0).Size(UnitValue.Stretch(), UnitValue.Stretch())
                            .Rounded(3).IsNotInteractable()
                            .BackgroundColor(Color.FromArgb(40, EditorTheme.Purple400))
                            .BorderColor(EditorTheme.Purple400).BorderWidth(1);
                    }
                })
                .Show();
        }
    }

    private static void BuildFolderNodes(List<OrigamiUI.TreeNode> nodes, string absolutePath, string displayName, int depth)
    {
        string relativePath = depth == 0
            ? ""
            : Path.GetRelativePath(Project.Current!.AssetsPath, absolutePath).Replace('\\', '/');

        string[] subDirs;
        try
        {
            subDirs = Directory.GetDirectories(absolutePath)
                .Where(d => !Path.GetFileName(d).StartsWith('.'))
                .OrderBy(d => d)
                .ToArray();
        }
        catch { subDirs = Array.Empty<string>(); }

        nodes.Add(new OrigamiUI.TreeNode
        {
            Id = relativePath,
            Label = displayName,
            Icon = EditorIcons.Folder,
            IconColor = Color.FromArgb(255, 220, 180, 80),
            HasChildren = subDirs.Length > 0,
            DefaultExpanded = depth < 2,
            Depth = depth,
            UserData = relativePath
        });

        foreach (var subDir in subDirs)
            BuildFolderNodes(nodes, subDir, Path.GetFileName(subDir), depth + 1);
    }

    // ================================================================
    //  Content Area (right)
    // ================================================================

    private void DrawContent(Paper paper, Scribe.FontFile font, float width, float height)
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
            //.OnRightClick(0, (_, _) => Selection.Clear())
            .Enter())
        {
            // Remember that the mouse is over the content background the central drop
            // dispatcher uses this as a fallback ("drop on empty content area" means drop
            // into the currently-open folder).
            if (DragDrop.IsDragging || DragDrop.IsDropFrame)
                _contentBgHovered = paper.IsParentHovered;

            // Right-click background show create/explorer menu
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

            // While any drag is active, show a banner at the top of the content area
            // summarizing what the user will get by dropping on the background. This is a
            // visual hint only the actual Accept runs AFTER the items are drawn so per-item
            // drop targets (folders in the grid) can win before the background fallback.
            if (DragDrop.IsDraggingType<GameObjectDragPayload>() && paper.IsParentHovered)
            {
                paper.Box("proj_prefab_drop")
                    .Height(24)
                    .BackgroundColor(Color.FromArgb(40, EditorTheme.Purple400))
                    .Rounded(3)
                    .Text($"Drop to create Prefab in {(string.IsNullOrEmpty(_currentFolder) ? "Assets" : _currentFolder)}", EditorTheme.DefaultFont)
                    .TextColor(EditorTheme.Purple400)
                    .FontSize(EditorTheme.FontSize - 2)
                    .Alignment(TextAlignment.MiddleCenter);
            }
            else if (DragDrop.IsDraggingType<AssetDragPayload>() && paper.IsParentHovered
                && CanAcceptAssetDropInto(_currentFolder))
            {
                paper.Box("proj_asset_drop_hint")
                    .Height(24)
                    .BackgroundColor(Color.FromArgb(40, EditorTheme.Purple400))
                    .Rounded(3)
                    .Text($"Drop to move into {(string.IsNullOrEmpty(_currentFolder) ? "Assets" : _currentFolder)}", EditorTheme.DefaultFont)
                    .TextColor(EditorTheme.Purple400)
                    .FontSize(EditorTheme.FontSize - 2)
                    .Alignment(TextAlignment.MiddleCenter);
            }

            // Breadcrumb
            DrawBreadcrumb(paper, font, width, 20);

            float contentHeight = height - 31;

            if (isList)
            {
                // List mode uses the tree widget which has its own scroll
                DrawListView(paper, font, entries, width, contentHeight);
            }
            else
            {
                // Grid mode uses a plain scroll view
                Origami.ScrollView(paper, "proj_content", width, contentHeight).Body(() =>
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
                        else
                        {
                            DrawGridView(paper, font, entries, width);
                        }
                    }
                });
            }

        }
    }

    private void DrawBreadcrumb(Paper paper, Scribe.FontFile font, float width, float height)
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

    private void DrawListView(Paper paper, Scribe.FontFile font, List<ContentItem> entries, float width, float height)
    {
        // Build flat TreeNode list + parallel object list for Selection.HandleListClick
        var treeNodes = new List<OrigamiUI.TreeNode>();
        var flatObjects = new List<object>();

        foreach (var item in entries)
        {
            var node = new OrigamiUI.TreeNode
            {
                Id = item.Guid != Guid.Empty ? item.Guid.ToString() : item.RelativePath,
                Label = item.Name,
                Icon = item.Icon,
                IconColor = item.IsFolder ? Color.FromArgb(255, 220, 180, 80)
                    : item.IsSubAsset ? EditorTheme.Purple300 : EditorTheme.Ink400,
                Depth = item.IsSubAsset ? 1 : 0,
                HasChildren = item.HasSubAssets,
                IsLeaf = !item.HasSubAssets, // folders, files without subs, and sub-assets are all leaves
                UserData = item,
                Badge = item.IsFolder ? null : item.TypeLabel,
                BadgeColor = EditorTheme.Ink400,
            };
            treeNodes.Add(node);
            flatObjects.Add(item);
        }

        Origami.Tree(paper, "proj_list_tree", width, height)
            .Nodes(treeNodes)
            .MultiSelect()
            .IsSelected(n => Selection.IsSelected((ContentItem)n.UserData!))
            .OnSelectModified((e, ctrl, shift) =>
            {
                Selection.HandleListClick((ContentItem)e.Node.UserData!, (IReadOnlyList<object>)flatObjects, e.Index, ctrl, shift);
            })
            .OnDoubleClick(e =>
            {
                var it = (ContentItem)e.Node.UserData!;
                if (it.IsFolder)
                    _currentFolder = it.RelativePath;
                else
                    EditorSceneManager.HandleAssetDoubleClick(it.RelativePath, it.Guid);
            })
            .OnRightClick(e =>
            {
                var it = (ContentItem)e.Node.UserData!;
                if (!Selection.IsSelected(it))
                    Selection.AddToSelection(it);
            })
            .OnDragStart(n =>
            {
                var it = (ContentItem)n.UserData!;
                if (it.IsSubAsset && it.Guid != Guid.Empty)
                {
                    Type? subType = null;
                    var db = EditorAssetDatabase.Instance;
                    if (db != null)
                    {
                        var subs = db.GetSubAssets(it.ParentGuid);
                        subType = subs.FirstOrDefault(s => s.Guid == it.Guid)?.Type;
                    }
                    DragDrop.StartDrag(new AssetDragPayload(it.Guid, it.Name, subType));
                    return;
                }
                var payload = BuildAssetDragPayload(it);
                if (payload != null) DragDrop.StartDrag(payload);
            })
            .OnHover((n, _) =>
            {
                var it = (ContentItem)n.UserData!;
                if (it.IsFolder && (DragDrop.IsDragging || DragDrop.IsDropFrame))
                    _dragHoverFolderNext = it.RelativePath;
            })
            .IsPinged(n =>
            {
                var it = (ContentItem)n.UserData!;
                return it.Guid != Guid.Empty && it.Guid == Selection.PingedGuid;
            })
            .PingAlpha(() => Selection.GetPingAlpha())
            .CustomRowContent((p, node, isSel, isExp) =>
            {
                var it = (ContentItem)node.UserData!;

                // Icon
                p.Box($"proj_li_ico_{node.Id}")
                    .Width(18).Height(22)
                    .Text(node.Icon, font)
                    .TextColor(node.IconColor ?? EditorTheme.Ink400)
                    .FontSize(12f).Alignment(TextAlignment.MiddleCenter);

                // Name (inline rename or label)
                if (RenameOverlay.IsRenaming($"proj_asset_{it.RelativePath}"))
                {
                    RenameOverlay.Draw(p, $"proj_li_rename_{node.Id}");
                }
                else
                {
                    p.Box($"proj_li_name_{node.Id}")
                        .Height(22)
                        .Text(it.Name, font)
                        .TextColor(EditorTheme.Ink500)
                        .FontSize(EditorTheme.FontSize - 3)
                        .Alignment(TextAlignment.MiddleLeft);
                }

                // Type label (right-aligned)
                if (!it.IsFolder)
                {
                    p.Box($"proj_li_type_{node.Id}")
                        .Width(80).Height(22)
                        .Text(it.TypeLabel, font)
                        .TextColor(EditorTheme.Ink400)
                        .FontSize(EditorTheme.FontSize - 4)
                        .Alignment(TextAlignment.MiddleRight);
                }

                // Context menu
                BuildItemContextMenu(p, $"proj_li_ctx_{node.Id}", it);

                // Folder drop target highlight
                if (it.IsFolder && _dragHoverFolder == it.RelativePath
                    && (DragDrop.IsDraggingType<GameObjectDragPayload>()
                        || (DragDrop.IsDraggingType<AssetDragPayload>() && CanAcceptAssetDropInto(it.RelativePath))))
                {
                    p.Box($"proj_li_drop_{node.Id}")
                        .PositionType(PositionType.SelfDirected)
                        .Position(0, 0).Size(UnitValue.Stretch(), UnitValue.Stretch())
                        .Rounded(3).IsNotInteractable()
                        .BackgroundColor(Color.FromArgb(40, EditorTheme.Purple400))
                        .BorderColor(EditorTheme.Purple400).BorderWidth(1);
                }
            })
            .EmptyMessage("This folder is empty")
            .Show();
    }

    private void BuildItemContextMenu(Paper paper, string id, ContentItem item, bool inTree = false)
    {
        Origami.RightClickMenu(paper, id, builder =>
        {
            // Right-click should select the item if not already selected
            if (!Selection.IsSelected(item))
                Selection.AddToSelection(item);

            bool isMulti = Selection.Count > 1;
            string folder = item.IsFolder ? item.RelativePath : _currentFolder;

            if (!item.IsFolder)
            {
                builder.Item($"Open", () => OpenWithSystem(item), icon: EditorIcons.FolderOpen);
                builder.Separator();
            }

            builder.Submenu($"Create", sub => AssetCreateMenu.Build(sub, folder, OnCreated), icon: EditorIcons.FileCirclePlus);

            builder.Separator();

            builder.Item(Loc.Get("project.show_in_explorer"), () => ShowInExplorer(item), icon: EditorIcons.FolderOpen);

            if (!item.IsFolder)
            {
                builder.Item(Loc.Get("project.reimport"), () =>
                {
                    var db = EditorAssetDatabase.Instance;
                    if (db == null) return;
                    foreach (var sel in Selection.GetSelected<ContentItem>())
                        if (sel.Guid != Guid.Empty) db.Reimport(sel.Guid);
                }, icon: EditorIcons.ArrowsRotate);
            }

            builder.Item(Loc.Get("project.copy_path"), () =>
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

            builder.Item($"Export Package...", () =>
            {
                var paths = Selection.GetSelected<ContentItem>()
                    .Where(c => !c.IsSubAsset)
                    .SelectMany(c =>
                    {
                        if (c.IsFolder)
                            return ProwlPackage.CollectFolderAssets(c.RelativePath);
                        if (c.Guid != Guid.Empty)
                            return new[] { c.RelativePath };
                        return Enumerable.Empty<string>();
                    })
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (paths.Count > 0)
                    PackageExportDialog.Open(paths);
            }, icon: EditorIcons.FileZipper);

            builder.Separator();

            bool isRoot = string.IsNullOrEmpty(item.RelativePath);
            builder.Item(Loc.Get("project.rename"), () => StartRename(item, inTree), enabled: !isMulti && !isRoot, icon: EditorIcons.PenToSquare);

            builder.Item(Loc.Get("project.delete"), () => DeleteSelectedItems(), enabled: !isRoot, icon: EditorIcons.Trash);
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
        Origami.RightClickMenu(paper, id, builder =>
        {
            string folder = _currentFolder;

            builder.Submenu("Create", sub => AssetCreateMenu.Build(sub, folder, OnCreated), icon: EditorIcons.FileCirclePlus);
            builder.Separator();

            builder.Item(Loc.Get("project.show_in_explorer"), () =>
            {
                string absPath = Path.Combine(Project.Current!.AssetsPath, folder);
                EditorUtils.OpenFileSystemPath(absPath);
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

        Origami.Confirm(Loc.Get("dialog.delete_assets"), $"Are you sure you want to delete {names}?\nThis cannot be undone.", () =>
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
                    Toasts.Show(Loc.Get("toast.rename_failed"), Loc.Get("toast.rename_exists", new { name = newName }), ToastType.Warning, 3f);
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
                    Toasts.Show(Loc.Get("toast.rename_failed"), Loc.Get("toast.rename_exists", new { name = newName }), ToastType.Warning, 3f);
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
        EditorUtils.OpenFileSystemPath(absPath);
    }

    // ================================================================
    //  Grid View
    // ================================================================

    private void DrawGridView(Paper paper, Scribe.FontFile font, List<ContentItem> entries, float width)
    {
        float cellSize = _thumbnailSize + 8f;
        float labelH = 18f;


        float totalCellH = cellSize + labelH;
        float gap = 6f;
        // Available width minus padding (12 = 6 left margin + 6 right margin from parent)
        // Each column takes cellSize + gap, minus one gap for the last column
        float available = width - 12f;
        int cols = Math.Max(1, (int)((available + gap) / (cellSize + gap)));

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


                    var size = paper.MeasureText(item.Name, EditorTheme.FontSize - 3, font);

                    if (size.X > cellSize)
                    {
                        size *= 2;
                    }

                    //Runtime.Debug.Log($"Size: ({item.Name}) {labelH} - {sizeY} - {sizeYo} ({(EditorTheme.FontSize - 5)/paper.Canvas.Scale})");

                    DrawGridItem(paper, font, $"proj_gc_{idx}", item, idx, itemObjects, cellSize, size.Y, totalCellH);
                }
            }
            row++;
        }
    }

    private void DrawGridItem(Paper paper, Scribe.FontFile font, string id, ContentItem item,
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
                if (it.IsSubAsset && it.Guid != Guid.Empty)
                {
                    var db = EditorAssetDatabase.Instance;
                    Type? subType = db?.GetSubAssets(it.ParentGuid).FirstOrDefault(s => s.Guid == it.Guid)?.Type;
                    DragDrop.StartDrag(new AssetDragPayload(it.Guid, it.Name, subType));
                    return;
                }

                var payload = BuildAssetDragPayload(it);
                if (payload != null) DragDrop.StartDrag(payload);
            })
            .OnHover(item, (it, _) =>
            {
                if (!it.IsFolder) return;
                if (DragDrop.IsDragging || DragDrop.IsDropFrame) _dragHoverFolderNext = it.RelativePath;
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
                RenameOverlay.Draw(paper, $"{id}_rename", RenameOverlay.Position.Bottom);
            }
            else
            {
                paper.Box($"{id}_l")
                    .PositionType(PositionType.SelfDirected)
                    .Position(0,UnitValue.Stretch())
                    .Width(cellSize).Height(EditorTheme.FontSize)
                    .Clip()
                    .Text(item.Name, font)
                    .TextColor(isSubAsset ? EditorTheme.Purple300 : EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSize - 3).Alignment(TextAlignment.MiddleLeft);
            }

            BuildItemContextMenu(paper, $"{id}_ctx", item);

            // Folder grid items are drop targets highlight overlay painted when the cursor
            // is over this item during a valid drag. Central dispatch in OnGUI.
            if (item.IsFolder && _dragHoverFolder == item.RelativePath
                && (DragDrop.IsDraggingType<GameObjectDragPayload>()
                    || (DragDrop.IsDraggingType<AssetDragPayload>() && CanAcceptAssetDropInto(item.RelativePath))))
            {
                paper.Box($"{id}_drop")
                    .PositionType(PositionType.SelfDirected)
                    .Position(0, 0).Size(UnitValue.Stretch(), UnitValue.Stretch())
                    .Rounded(4).IsNotInteractable()
                    .BackgroundColor(Color.FromArgb(40, EditorTheme.Purple400))
                    .BorderColor(EditorTheme.Purple400).BorderWidth(2);
            }
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

        items.AddRange(VirtualContentItems);

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

                // Insert sub-assets if expanded (grid mode) or always (list mode uses tree widget)
                if (hasSubAssets && entry != null && (_expandedAssets.Contains(entry.Guid) || _thumbnailSize < ListThreshold))
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

    private static Runtime.Resources.Texture2D? GetThumbnailTexture(Guid guid)
    {
        if (guid == Guid.Empty) return null;

        if (_thumbnailCache.TryGetValue(guid, out var cached))
            return cached;

        // Try loading from disk don't cache null so we retry when thumbnail is generated
        var db = EditorAssetDatabase.Instance;
        if (db == null) return null;

        var thumb = db.LoadThumbnail(guid);
        if (thumb == null) return null;

        try
        {
            var (w, h, pixels) = thumb.Value;
            var tex = new Runtime.Resources.Texture2D((uint)w, (uint)h, false, TextureImageFormat.Color4b);
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
        if (typeof(Runtime.Resources.Mesh).IsAssignableFrom(type)) return EditorIcons.VectorSquare;
        if (typeof(Runtime.Resources.Material).IsAssignableFrom(type)) return EditorIcons.Palette;
        if (typeof(AnimationClip).IsAssignableFrom(type)) return EditorIcons.Film;
        if (typeof(Runtime.Resources.Texture2D).IsAssignableFrom(type)) return EditorIcons.FileImage;
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
