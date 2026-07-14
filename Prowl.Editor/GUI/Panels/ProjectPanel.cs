using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.OrigamiUI;
using Prowl.Editor.GUI;
using Prowl.Editor.GUI.Popups;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Vector;

using Color = System.Drawing.Color;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.Utils;
using Prowl.Editor.GUI.Registries;
using Prowl.Editor.Core;
using Prowl.Editor.Theming;
using Prowl.Editor.Projects;

namespace Prowl.Editor.GUI.Panels;

[EditorWindow("General/Project")]
public class ProjectPanel : DockPanel
{
    public static ProjectPanel Instance;
    public override string Title => Loc.Get("panel.project");
    public override string Icon => EditorIcons.Folder;

    // Refresh control lives in the leaf's tab-bar header (right side), matching the Nebula design.
    public override float HeaderWidth => 28f;
    public override void OnHeaderContent(Paper paper, float width, float height)
    {
        EditorGUI.HeaderIconButton(paper, "proj_hdr_refresh", EditorIcons.ArrowRotateRight, () =>
        {
            if (EditorAssetDatabase.Instance != null)
            {
                Runtime.Debug.Log("Refreshing asset database...");
                var db = new EditorAssetDatabase(Project.Current!);
                db.Initialize();
            }
        });
    }

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
    //   null   -> mouse not over any folder drop target
    //   ""     -> over the Assets root (represented by an empty relative path)
    //   "Foo"  -> over folder 'Foo' (assets-relative)
    private string? _dragHoverFolder;      // current frame's resolved hover target (from last frame's callbacks)
    private string? _dragHoverFolderNext;  // written by deferred callbacks, promoted next frame
    private string? _dragDwellFolder;     // folder being dwelled on for auto-open
    private float _dragDwellTimer;        // seconds spent hovering the dwell folder
    private const float DragDwellOpenDelay = 0.75f;
    // True while the mouse is over the content-area background (not just a folder item) -
    // lets "drop on empty space" fall back to the currently-open folder.
    private bool _contentBgHovered;
    // Rename state is managed by RenameOverlay
    private static readonly HashSet<Guid> _expandedAssets = new(); // files with sub-assets expanded
    private static Guid _pendingPingNavigate; // Navigate to pinged asset's folder on next frame
    private static Guid _lastPingedGuid; // Track when a new ping starts
    private const float MinThumbSize = 20f;  // Below this = list mode
    private const float MaxThumbSize = 128f;
    private const float ListThreshold = 32f; // Below this = list view

    private const float ToolbarHeight = 34;
    private const float FooterHeight = 26;
    private const float FolderTreeWidth = 172f;

    // -- asset-browser view state --
    private enum SortMode { Name, Type, Size, Modified }
    private SortMode _sortBy = SortMode.Name;
    private bool _showExtensions;
    private bool _showHidden;
    private bool _groupByType;
    private readonly Stack<string> _navBack = new();
    private readonly Stack<string> _navForward = new();

    // -- palette helpers (accent/borders pull from EditorTheme so a retheme flows) --
    private static Color Col(int r, int g, int b, int a = 255) => Color.FromArgb(a, r, g, b);
    private static Color Amber => EditorTheme.Amber400;
    private static Color Green => EditorTheme.Green400;
    private static Color THi => EditorTheme.Ink500;
    private static Color TBody => EditorTheme.Ink400;
    private static Color TMid => EditorTheme.Ink300;
    private static Color TLo => EditorTheme.InkDim;
    private static Color TDim => EditorTheme.InkFaint;
    private static Color Raised => EditorTheme.Neutral400;
    private static Color GlassIn => EditorTheme.Glass;
    private static Color Popover => EditorTheme.Popover;
    private static Color BdSoft => EditorTheme.BorderSoft;
    private static Color BdStrong => EditorTheme.BorderStrong;
    private static Color Acc => EditorTheme.Accent;

    private static UnitValue ST => UnitValue.StretchOne;
    private bool IsListView => _thumbnailSize < ListThreshold;

    private void NavigateTo(string folder)
    {
        if (folder == _currentFolder) return;
        _navBack.Push(_currentFolder);
        _navForward.Clear();
        _currentFolder = folder;
    }

    private void NavBack()
    {
        if (_navBack.Count == 0) return;
        _navForward.Push(_currentFolder);
        _currentFolder = _navBack.Pop();
    }

    private void NavForward()
    {
        if (_navForward.Count == 0) return;
        _navBack.Push(_currentFolder);
        _currentFolder = _navForward.Pop();
    }

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

        var assetDb = EditorAssetDatabase.Instance;
        var entries = assetDb != null ? GetContentEntries(assetDb) : new List<ContentItem>();

        using (paper.Row("proj_root").Size(width, height).Enter())
        {
            DrawFolderTree(paper, font, height);

            float mainW = width - FolderTreeWidth;
            using (paper.Column("proj_main").Width(mainW).Height(height).Enter())
            {
                DrawToolbar(paper, font, mainW);
                DrawContent(paper, font, entries, mainW, height - ToolbarHeight - FooterHeight);
                DrawFooter(paper, font, entries, mainW);
            }
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
        using (paper.Column("proj_toolbar_col").Height(ToolbarHeight).Enter())
        {
            using (paper.Row("proj_toolbar")
                .Height(ToolbarHeight - 1)
                .ChildLeft(8).ChildRight(8).RowBetween(5)
                .Enter())
            {
                NavBtn(paper, font, "proj_back", EditorIcons.ChevronLeft, _navBack.Count > 0, NavBack);
                NavBtn(paper, font, "proj_fwd", EditorIcons.ChevronRight, _navForward.Count > 0, NavForward);

                DrawCrumbs(paper, font);

                paper.Box("proj_tb_spacer").Width(ST);

                IconBtn(paper, font, "proj_view", IsListView ? EditorIcons.TableCellsLarge : EditorIcons.List,
                    false, () => _thumbnailSize = IsListView ? 64f : 24f);

                using (paper.Row("proj_search_wrap").Width(150).Height(25).Margin(0, 0, ST, ST).Enter())
                    Origami.SearchField(paper, "proj_search", _searchText, v => _searchText = v, Loc.Get("project.search")).Width(150).Height(25).Show();

                IconBtn(paper, font, "proj_opts", EditorIcons.EllipsisVertical, false,
                    () => Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, BuildOptionsMenu));
            }
            paper.Box("proj_tb_div").Height(1).BackgroundColor(BdSoft).IsNotInteractable();
        }
    }

    // View / sort / filter menu, built with Origami's ContextMenu so it uses the theme's default fonts.
    private void BuildOptionsMenu(ContextBuilder b)
    {
        b.Header(Loc.Get("project.menu_view"));
        b.Toggle(Loc.Get("project.show_extensions"), () => _showExtensions = !_showExtensions, () => _showExtensions);
        b.Item(IsListView ? Loc.Get("project.grid_view") : Loc.Get("project.list_view"), () => _thumbnailSize = IsListView ? 64f : 24f);

        b.Header(Loc.Get("project.sort_by"));
        foreach (SortMode s in Enum.GetValues<SortMode>())
        {
            var sc = s;
            b.Item(s.ToString(), () => _sortBy = sc, on: _sortBy == s);
        }

        b.Header(Loc.Get("project.filter"));
        b.Toggle(Loc.Get("project.show_hidden"), () => _showHidden = !_showHidden, () => _showHidden);
        b.Toggle(Loc.Get("project.group_by_type"), () => _groupByType = !_groupByType, () => _groupByType);
    }

    // a2 "ib" icon button: hover glass, optional accent 'on' state.
    private void IconBtn(Paper p, Scribe.FontFile font, string id, string glyph, bool on, Action onClick)
    {
        p.Box(id).Width(27).Height(27).Rounded(7).Margin(0, 0, ST, ST)
            .BackgroundColor(on ? EditorTheme.Selected : Color.Transparent)
            .Hovered.BackgroundColor(on ? EditorTheme.Selected : EditorTheme.Hover).End()
            .Text(glyph, font).TextColor(on ? Acc : TMid).FontSize(14f).Alignment(TextAlignment.MiddleCenter)
            .OnClick(_ => onClick());
    }

    private void NavBtn(Paper p, Scribe.FontFile font, string id, string glyph, bool enabled, Action onClick)
    {
        var b = p.Box(id).Width(24).Height(24).Rounded(6).Margin(0, 0, ST, ST)
            .Text(glyph, font).TextColor(enabled ? TMid : TDim).FontSize(13f).Alignment(TextAlignment.MiddleCenter);
        if (enabled) { b.Hovered.BackgroundColor(EditorTheme.Hover).End(); b.OnClick(_ => onClick()); }
        else b.IsNotInteractable();
    }

    // Breadcrumb trail (Origami widget); the Assets root carries a folder icon, deeper segments are labels.
    private void DrawCrumbs(Paper p, Scribe.FontFile font)
    {
        var items = new List<BreadcrumbItem> { new("Assets", EditorIcons.Folder_I, (object)"") };
        if (!string.IsNullOrEmpty(_currentFolder))
        {
            string acc = "";
            foreach (var seg in _currentFolder.Split('/'))
            {
                acc = acc.Length > 0 ? acc + "/" + seg : seg;
                items.Add(new BreadcrumbItem(seg, "", acc));
            }
        }

        using (p.Row("proj_crumbs").Width(UnitValue.Auto).Height(24).Clip().Margin(4, 0, ST, ST).Enter())
            Origami.Breadcrumb(p, "proj_crumbs_bc", items, it => NavigateTo((string)it.UserData!))
                .Width(UnitValue.Auto).Height(24).Show();
    }

    // ================================================================
    //  Footer (count + thumbnail size slider)
    // ================================================================

    private void DrawFooter(Paper paper, Scribe.FontFile font, List<ContentItem> entries, float width)
    {
        var mono = EditorTheme.FontMono ?? font;
        int selCount = Selection.GetSelected<ContentItem>().Count();

        using (paper.Row("proj_footer").Height(FooterHeight)
            .Padding(11, 11, 0, 0)
            .BackgroundColor(Col(0, 0, 0, 36))
            .Enter())
        {
            paper.Box("proj_foot_div").PositionType(PositionType.SelfDirected).Position(0, 0).Size(width, 1)
                .BackgroundColor(BdSoft).IsNotInteractable();

            paper.Box("proj_foot_count").Width(UnitValue.Auto).Height(FooterHeight).Margin(0, 0, ST, ST)
                .Text(Loc.Get("project.item_count", new { count = entries.Count }), mono).TextColor(TLo).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
            if (selCount > 0)
                paper.Box("proj_foot_sel").Width(UnitValue.Auto).Height(FooterHeight).Margin(6, 0, ST, ST)
                    .Text("- " + Loc.Get("project.selected_count", new { count = selCount }), mono).TextColor(EditorTheme.AccentText).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

            paper.Box("proj_foot_spacer").Width(ST);

            paper.Box("proj_foot_ico1").Width(14).Height(FooterHeight).Margin(0, 0, ST, ST).IsNotInteractable()
                .Text(EditorIcons.Image, font).TextColor(TDim).FontSize(11f).Alignment(TextAlignment.MiddleCenter);
            using (paper.Row("proj_foot_sld_wrap").Width(90).Height(FooterHeight).ChildTop(ST).ChildBottom(ST).Margin(7, 0, ST, ST).Enter())
                Origami.Slider(paper, "proj_thumb_slider", _thumbnailSize, v => _thumbnailSize = v, MinThumbSize, MaxThumbSize)
                    .ShowValue(false).Width(90f).TrackThickness(4).ThumbSize(12).Height(18).Show();
            paper.Box("proj_foot_ico2").Width(16).Height(FooterHeight).Margin(7, 0, ST, ST).IsNotInteractable()
                .Text(EditorIcons.Image, font).TextColor(TLo).FontSize(13f).Alignment(TextAlignment.MiddleCenter);
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
            // Thumbnails are keyed by GUID, not path, so a move/rename doesn't invalidate them.
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

    // ================================================================
    //  Folder Tree (left)
    // ================================================================

    private void DrawFolderTree(Paper paper, Scribe.FontFile font, float height)
    {
        using (paper.Box("proj_tree_bg")
            .Size(FolderTreeWidth, height)
            .BackgroundColor(Col(0, 0, 0, 26))
            .OnClick(0, (_, _) => Selection.Clear())
            .Enter())
        {
            // Right-edge divider (a2 tree border-right).
            paper.Box("proj_tree_div").PositionType(PositionType.SelfDirected).Position(FolderTreeWidth - 1, 0)
                .Size(1, height).BackgroundColor(BdSoft).IsNotInteractable();

            // Right-click background show create/explorer menu
            BuildBackgroundContextMenu(paper, "proj_tree_bg_ctx");

            // Build flat node list by walking directories recursively
            var nodes = new List<OrigamiUI.TreeNode>();
            BuildFolderNodes(nodes, "", "Assets", 0);

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
                    NavigateTo((string)e.Node.UserData!);
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
                        .TextColor(EditorTheme.Amber400)
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

    private static void BuildFolderNodes(List<OrigamiUI.TreeNode> nodes, string relativePath, string displayName, int depth)
    {
        // Read the folder structure from the asset database's cached index instead of walking the
        // filesystem every frame.
        var db = EditorAssetDatabase.Instance;
        var subDirs = db != null
            ? db.GetSubFolders(relativePath)
                .Where(f => !f.Name.StartsWith('.'))
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : new List<EditorAssetDatabase.FolderRecord>();

        nodes.Add(new OrigamiUI.TreeNode
        {
            Id = relativePath,
            Label = displayName,
            Icon = EditorIcons.Folder,
            IconColor = EditorTheme.Amber400,
            HasChildren = subDirs.Count > 0,
            DefaultExpanded = depth < 2,
            Depth = depth,
            UserData = relativePath
        });

        foreach (var subDir in subDirs)
            BuildFolderNodes(nodes, subDir.RelativePath, subDir.Name, depth + 1);
    }

    // ================================================================
    //  Content Area (right)
    // ================================================================

    private void DrawContent(Paper paper, Scribe.FontFile font, List<ContentItem> entries, float width, float height)
    {
        bool isList = IsListView;

        // Transparent background so the dock panel's glass (over the nebula) shows through.
        using (paper.Box("proj_content_bg")
            .Size(width, height)
            .OnClick(0, (_, _) => Selection.Clear())
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
                    .Text(Loc.Get("project.drop_create_prefab", new { folder = string.IsNullOrEmpty(_currentFolder) ? "Assets" : _currentFolder }), EditorTheme.DefaultFont)
                    .TextColor(EditorTheme.Purple400)
                    .FontSize(EditorTheme.FontSizeSmall)
                    .Alignment(TextAlignment.MiddleCenter);
            }
            else if (DragDrop.IsDraggingType<AssetDragPayload>() && paper.IsParentHovered
                && CanAcceptAssetDropInto(_currentFolder))
            {
                paper.Box("proj_asset_drop_hint")
                    .Height(24)
                    .BackgroundColor(Color.FromArgb(40, EditorTheme.Purple400))
                    .Rounded(3)
                    .Text(Loc.Get("project.drop_move", new { folder = string.IsNullOrEmpty(_currentFolder) ? "Assets" : _currentFolder }), EditorTheme.DefaultFont)
                    .TextColor(EditorTheme.Purple400)
                    .FontSize(EditorTheme.FontSizeSmall)
                    .Alignment(TextAlignment.MiddleCenter);
            }

            if (isList)
            {
                // List mode uses the tree widget which has its own scroll
                DrawListView(paper, font, entries, width, height);
            }
            else
            {
                // Grid mode uses a plain scroll view
                Origami.ScrollView(paper, "proj_content", width, height).Body(() =>
                {
                    using (paper.Column("proj_content_inner")
                        .Margin(9, 9, 9, 9)
                        .Height(UnitValue.Auto)
                        .Enter())
                    {
                        if (entries.Count == 0)
                        {
                            paper.Box("proj_empty")
                                .Height(60)
                                .Text(Loc.Get("project.folder_empty"), font)
                                .TextColor(TMid)
                                .FontSize(EditorTheme.FontSizeSmall)
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

    // ================================================================
    //  List View
    // ================================================================

    private static float TableRowH => EditorTheme.RowHeight;

    private void DrawListView(Paper paper, Scribe.FontFile font, List<ContentItem> entries, float width, float height)
    {
        var mono = EditorTheme.FontMono ?? font;
        var semi = EditorTheme.FontSemiBold ?? font;

        // Flat visible list (top-level rows + the sub-asset rows of any expanded parent).
        var visible = new List<(ContentItem item, bool isSub)>();
        foreach (var e in entries)
        {
            visible.Add((e, false));
            if (e.Subs.Count > 0 && _expandedAssets.Contains(e.Guid))
                foreach (var s in e.Subs) visible.Add((s, true));
        }
        var flatObjects = visible.Select(v => (object)v.item).ToList();

        int activeCol = _sortBy switch { SortMode.Name => 0, SortMode.Type => 1, SortMode.Size => 2, _ => -1 };

        Origami.Table(paper, "proj_table", -1, _ => { })
            .Bordered(false)
            .Scroll(width, height)
            .RowHeight(TableRowH)
            .Column("Name", 2f, sortable: true)
            .Column("Type", 1f, sortable: true)
            .Column("Size", 0.7f, sortable: true, align: TextAlignment.MiddleRight)
            .Sort(activeCol, _sortBy != SortMode.Size, col =>
                _sortBy = col switch { 0 => SortMode.Name, 1 => SortMode.Type, 2 => SortMode.Size, _ => _sortBy })
            .MultiSelect()
            .IsSelected(i => Selection.IsSelected(visible[i].item))
            .OnSelectModified((i, ctrl, shift) => Selection.HandleListClick(visible[i].item, flatObjects, i, ctrl, shift))
            .OnRowActivate(i =>
            {
                var it = visible[i].item;
                if (it.IsFolder) NavigateTo(it.RelativePath);
                else if (it.Subs.Count > 0)
                {
                    if (_expandedAssets.Contains(it.Guid)) _expandedAssets.Remove(it.Guid);
                    else _expandedAssets.Add(it.Guid);
                }
                else EditorSceneManager.HandleAssetDoubleClick(it.RelativePath, it.Guid);
            })
            .OnRowContext(i =>
            {
                var it = visible[i].item;
                if (!Selection.IsSelected(it)) Selection.AddToSelection(it);
                Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, ItemContextMenu(it));
            })
            .OnRowDragStart(i =>
            {
                var it = visible[i].item;
                if (it.IsSubAsset && it.Guid != Guid.Empty)
                {
                    var db = EditorAssetDatabase.Instance;
                    Type? subType = db?.GetSubAssets(it.ParentGuid).FirstOrDefault(s => s.Guid == it.Guid)?.Type;
                    DragDrop.StartDrag(new AssetDragPayload(it.Guid, it.Name, subType));
                }
                else
                {
                    var payload = BuildAssetDragPayload(it);
                    if (payload != null) DragDrop.StartDrag(payload);
                }
            })
            .OnRowHover((i, _) =>
            {
                var it = visible[i].item;
                if (it.IsFolder && (DragDrop.IsDragging || DragDrop.IsDropFrame)) _dragHoverFolderNext = it.RelativePath;
            })
            .IsPinged(i => { var g = visible[i].item.Guid; return g != Guid.Empty && g == Selection.PingedGuid; })
            .PingAlpha(() => Selection.GetPingAlpha())
            .RowCount(visible.Count)
            .CellContent((rowIdx, col) => DrawTableCell(paper, font, mono, semi, visible[rowIdx].item, visible[rowIdx].isSub, col))
            .Show();
    }

    // Host-drawn content for one table cell (the table owns the column width, selection bg, scrolling).
    private void DrawTableCell(Paper paper, Scribe.FontFile font, Scribe.FontFile mono, Scribe.FontFile semi,
        ContentItem item, bool isSub, int col)
    {
        string id = item.Guid != Guid.Empty ? item.Guid.ToString() : item.RelativePath;
        bool isSelected = Selection.IsSelected(item);
        bool hasSubs = item.Subs.Count > 0;
        bool expanded = hasSubs && _expandedAssets.Contains(item.Guid);
        var style = item.IsFolder ? AssetTypeStyles.Folder : AssetTypeStyles.For(Path.GetExtension(item.Name), item.TypeLabel);

        if (col == 0)
        {
            if (isSub)
                paper.Box($"proj_tcind_{id}").Width(20).Height(TableRowH).IsNotInteractable();
            else if (hasSubs)
                paper.Box($"proj_tccar_{id}").Width(15).Height(TableRowH)
                    .StopEventPropagation()
                    .OnClick(item.Guid, (g, _) =>
                    {
                        if (_expandedAssets.Contains(g)) _expandedAssets.Remove(g);
                        else _expandedAssets.Add(g);
                    })
                    .Icon(paper, expanded ? EditorIcons.ChevronDown_I : EditorIcons.ChevronRight_I, TLo, size: 11f);
            else
                paper.Box($"proj_tcsp_{id}").Width(15).Height(TableRowH).IsNotInteractable();

            var ic = paper.Box($"proj_tcico_{id}").Width(18).Height(TableRowH).Margin(2, 0, 0, 0);
            if (style.Badge != null)
                ic.Text(style.Badge, mono).TextColor(style.Color).FontSize(10f).Alignment(TextAlignment.MiddleCenter);
            else
                ic.Icon(paper, style.Icon, style.Color, size: 15f);

            if (RenameOverlay.IsRenaming($"proj_asset_{item.RelativePath}"))
            {
                RenameOverlay.Draw(paper, $"proj_tcrn_{id}");
            }
            else
            {
                // Name hugs its text so the sub-count tag sits right beside it; a trailing spacer fills the rest.
                paper.Box($"proj_tclbl_{id}").Width(UnitValue.Auto).Height(TableRowH).Margin(6, 0, 0, 0).Clip()
                    .Text(DisplayName(item), font)
                    .TextColor(isSub ? TBody : THi)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

                if (hasSubs)
                    paper.Box($"proj_tctag_{id}").Width(UnitValue.Auto).Height(17).Rounded(5).Padding(6, 6, 0, 0).Margin(7, 0, ST, ST)
                        .BackgroundColor(EditorTheme.Selected).BorderColor(Color.FromArgb(77, EditorTheme.Purple400)).BorderWidth(1)
                        .Text(item.Subs.Count.ToString(), semi).TextColor(EditorTheme.AccentText)
                        .FontSize(11f).Alignment(TextAlignment.MiddleCenter);
                else if (isSub)
                    paper.Box($"proj_tctag_{id}").Width(UnitValue.Auto).Height(17).Rounded(5).Padding(6, 6, 0, 0).Margin(7, 0, ST, ST)
                        .BackgroundColor(EditorTheme.Selected).BorderColor(Color.FromArgb(77, EditorTheme.Purple400)).BorderWidth(1)
                        .Text("sub", semi).TextColor(EditorTheme.AccentText)
                        .FontSize(10f).Alignment(TextAlignment.MiddleCenter);

                paper.Box($"proj_tcnsp_{id}").Width(ST).Height(TableRowH).IsNotInteractable();
            }
        }
        else if (col == 1)
        {
            paper.Box($"proj_tctype_{id}").Width(ST).Height(TableRowH).IsNotInteractable()
                .Text(item.IsFolder ? Loc.Get("inspector.folder") : item.TypeLabel, font).TextColor(TMid)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
        }
        else
        {
            paper.Box($"proj_tcsize_{id}").Width(ST).Height(TableRowH).IsNotInteractable()
                .Text(item.IsFolder || isSub ? "-" : FormatSize(item.Size), mono)
                .TextColor(isSub ? TDim : TLo).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleRight);
        }
    }

    private void BuildItemContextMenu(Paper paper, string id, ContentItem item, bool inTree = false)
        => Origami.RightClickMenu(paper, id, ItemContextMenu(item, inTree));

    // The item context menu as a reusable ContextBuilder action (for RightClickMenu on an element, or
    // Origami.ContextMenu at a point - the table opens it from OnRowContext).
    private Action<ContextBuilder> ItemContextMenu(ContentItem item, bool inTree = false) => builder =>
        {
            // Right-click should select the item if not already selected
            if (!Selection.IsSelected(item))
                Selection.AddToSelection(item);

            bool isMulti = Selection.Count > 1;
            bool isRoot = string.IsNullOrEmpty(item.RelativePath);
            string folder = item.IsFolder ? item.RelativePath : _currentFolder;
            var titleStyle = item.IsFolder ? AssetTypeStyles.Folder : AssetTypeStyles.For(Path.GetExtension(item.Name), item.TypeLabel);

            // Subject of the menu.
            builder.Title(isMulti ? Loc.Get("project.item_count", new { count = Selection.Count }) : item.Name, iconDraw: titleStyle.Icon);

            // Open / reveal.
            if (item.IsFolder)
                builder.Item(Loc.Get("launcher.open"), () => NavigateTo(item.RelativePath), icon: EditorIcons.FolderOpen);
            else
                builder.Item(Loc.Get("launcher.open"), () => OpenWithSystem(item), icon: EditorIcons.FolderOpen);
            builder.Item(Loc.Get("project.show_in_explorer"), () => ShowInExplorer(item), icon: EditorIcons.FolderTree);

            builder.Separator();

            builder.Submenu(Loc.Get("project.create"), sub => AssetCreateMenu.Build(sub, folder, OnCreated), icon: EditorIcons.FileCirclePlus);

            builder.Separator();

            builder.Item(Loc.Get("project.rename"), () => StartRename(item, inTree), enabled: !isMulti && !isRoot && CanRename(item), icon: EditorIcons.PenToSquare);
            builder.Item(Loc.Get("project.copy_path"), () => paper_SetClipboard(item.RelativePath), icon: EditorIcons.Copy);
            if (!item.IsFolder && item.Guid != Guid.Empty)
                builder.Item(Loc.Get("project.copy_guid"), () => paper_SetClipboard(item.Guid.ToString()), icon: EditorIcons.Fingerprint);
            if (!item.IsFolder)
                builder.Item(Loc.Get("project.reimport"), () =>
                {
                    var db = EditorAssetDatabase.Instance;
                    if (db == null) return;
                    foreach (var sel in Selection.GetSelected<ContentItem>())
                        if (sel.Guid != Guid.Empty) db.Reimport(sel.Guid);
                }, icon: EditorIcons.ArrowsRotate);

            builder.Separator();

            builder.Item(Loc.Get("project.export_package"), () =>
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

            builder.Item(Loc.Get("project.delete"), () => DeleteSelectedItems(), enabled: !isRoot, danger: true, icon: EditorIcons.Trash);
        };

    private void paper_SetClipboard(string text) => _paper?.SetClipboard(text);

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

            builder.Submenu(Loc.Get("project.create"), sub => AssetCreateMenu.Build(sub, folder, OnCreated), icon: EditorIcons.FileCirclePlus);
            builder.Separator();

            builder.Item(Loc.Get("project.show_in_explorer"), () =>
            {
                string absPath = Path.Combine(Project.Current!.AssetsPath, folder);
                EditorUtils.OpenFileSystemPath(absPath);
            }, icon: EditorIcons.FolderOpen);

            builder.Separator();

            builder.Item(Loc.Get("menu.assets.reimport_all"), () =>
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

        string names = selected.Count == 1 ? selected[0].Name : Loc.Get("project.item_count", new { count = selected.Count });

        Origami.Confirm(Loc.Get("dialog.delete_assets"), Loc.Get("project.delete_confirm_body", new { names = names }), () =>
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
                        // Delete every tracked asset (and nested folder entry) under this folder through
                        // DeleteAsset so recompile triggers, instances get disposed, and the GUID index
                        // stays consistent, then sweep any untracked leftovers off disk.
                        string prefix = sel.RelativePath + "/";
                        var toDelete = db.GetAllAssetPaths()
                            .Where(p => p.Equals(sel.RelativePath, StringComparison.OrdinalIgnoreCase)
                                     || p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                            .ToList();
                        foreach (var path in toDelete)
                            db.DeleteAsset(path);

                        if (Directory.Exists(absPath))
                            Directory.Delete(absPath, true);
                        string metaPath = MetaFile.GetMetaPath(absPath);
                        if (File.Exists(metaPath)) File.Delete(metaPath);
                        db.InvalidateFolderIndex();
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

    // Renaming a .cs file only renames the file, not the class it declares, breaking the file-name =
    // class-name convention and every serialized reference to that component type. Disabled until proper
    // script renaming (rename the class + serialized type aliasing) is supported.
    private static bool CanRename(ContentItem item)
        => item.IsFolder || !string.Equals(Path.GetExtension(item.Name), ".cs", StringComparison.OrdinalIgnoreCase);

    private void StartRename(ContentItem item, bool inTree = false)
    {
        if (!CanRename(item))
        {
            Toasts.Show(Loc.Get("toast.rename_failed"), "Renaming script files isn't supported yet - it would break the class/file-name link.", ToastType.Warning, 4f);
            return;
        }

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
                bool success = EditorAssetDatabase.Instance?.MoveFolder(item.RelativePath, newRelPath) ?? false;
                if (!success)
                    Toasts.Show(Loc.Get("toast.rename_failed"), Loc.Get("toast.rename_exists", new { name = newName }), ToastType.Warning, 3f);
                else if (_currentFolder == item.RelativePath)
                    _currentFolder = newRelPath;
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
        var itemObjects = entries.Select(e => (object)e).ToList();

        // Flex-wrap: cards flow left-to-right and wrap; each card auto-grows in height to fit its
        // (wrapping) name. A full-width sub-asset drawer inserted after an expanded card forces a
        // line break, so it lands on its own row (like the CSS grid's `grid-column: 1/-1`).
        using (paper.Row("proj_grid_wrap").Width(UnitValue.Stretch()).Height(UnitValue.Auto)
            .WrapContent().RowBetween(6).Enter())
        {
            for (int i = 0; i < entries.Count; i++)
            {
                DrawGridItem(paper, font, $"proj_gc_{i}", entries[i], i, itemObjects, cellSize);
                if (entries[i].Subs.Count > 0 && _expandedAssets.Contains(entries[i].Guid))
                    DrawSubDrawer(paper, font, entries[i], width);
            }
        }
    }

    private void DrawSubDrawer(Paper paper, Scribe.FontFile font, ContentItem parent, float width)
    {
        string pid = parent.Guid.ToString();
        using (paper.Column($"proj_drawer_{pid}").Width(UnitValue.Percentage(100)).Height(UnitValue.Auto)
            .Margin(0, 0, 3, 7).Padding(11, 11, 10, 10).Rounded(10)
            .BackgroundColor(Col(0, 0, 0, 61)).BorderColor(BdSoft).BorderWidth(1)
            .Enter())
        {
            using (paper.Row($"proj_drawerlbl_{pid}").Height(18).Margin(0, 0, 0, 9).Enter())
            {
                paper.Box($"proj_drawerico_{pid}").Width(14).Height(18).Margin(0, 6, ST, ST).IsNotInteractable()
                    .Icon(paper, EditorIcons.LayerGroup_I, EditorTheme.AccentText, size: 12f);
                paper.Box($"proj_drawernm_{pid}").Width(UnitValue.Auto).Height(18).Margin(0, 6, ST, ST)
                    .Text(parent.Name, EditorTheme.FontSemiBold ?? font).TextColor(EditorTheme.AccentText)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
                paper.Box($"proj_drawercnt_{pid}").Width(UnitValue.Auto).Height(18).Margin(0, 0, ST, ST)
                    .Text("- " + Loc.Get("project.sub_asset_count", new { count = parent.Subs.Count }), font).TextColor(TLo)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
            }

            using (paper.Row($"proj_drawerrow_{pid}").Width(UnitValue.Stretch()).Height(UnitValue.Auto).WrapContent().Enter())
            {
                var subObjects = parent.Subs.Select(s => (object)s).ToList();
                for (int i = 0; i < parent.Subs.Count; i++)
                    DrawSubThumb(paper, font, parent.Subs[i], i, subObjects);
            }
        }
    }

    private void DrawSubThumb(Paper paper, Scribe.FontFile font, ContentItem sub, int idx, List<object> subObjects)
    {
        var style = AssetTypeStyles.For(Path.GetExtension(sub.Name), sub.TypeLabel);
        bool isSelected = Selection.IsSelected(sub);

        using (paper.Column($"proj_sub_{sub.Guid}").Width(62).Height(UnitValue.Auto).Margin(0, 6, 0, 6)
            .Padding(3, 3, 5, 5).Rounded(8).ColBetween(5)
            .BackgroundColor(isSelected ? EditorTheme.Selected : Color.Transparent)
            .Hovered.BackgroundColor(isSelected ? EditorTheme.Selected : EditorTheme.Hover).End()
            .OnClick((sub, idx, subObjects), (cap, e) =>
            {
                e.StopPropagation();
                bool ctrl = _paper?.IsKeyDown(PaperKey.LeftControl) == true || _paper?.IsKeyDown(PaperKey.RightControl) == true;
                bool shift = _paper?.IsKeyDown(PaperKey.LeftShift) == true || _paper?.IsKeyDown(PaperKey.RightShift) == true;
                Selection.HandleListClick(cap.Item1, (IReadOnlyList<object>)cap.Item3, cap.Item2, ctrl, shift);
            })
            .OnDragStart(sub, (s, _) =>
            {
                var db = EditorAssetDatabase.Instance;
                Type? subType = db?.GetSubAssets(s.ParentGuid).FirstOrDefault(x => x.Guid == s.Guid)?.Type;
                DragDrop.StartDrag(new AssetDragPayload(s.Guid, s.Name, subType));
            })
            .Tooltip(sub.Name)
            .Enter())
        {
            var thumbTex = EditorAssetDatabase.Instance?.GetThumbnailTexture(sub.Guid);
            if (thumbTex != null)
            {
                paper.Box($"proj_subth_{sub.Guid}").Width(42).Height(42).Margin(ST, ST, 0, 0)
                    .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
                    {
                        float x = (float)r.Min.X, y = (float)r.Min.Y, w = (float)r.Size.X, h = (float)r.Size.Y;
                        canvas.DrawImageRounded(thumbTex, x, y, w, h, 8f);
                        var bd = Prowl.Vector.Color32.FromArgb(BdSoft.A, BdSoft.R, BdSoft.G, BdSoft.B);
                        canvas.SaveState();
                        canvas.SetStrokeColor(bd);
                        canvas.SetStrokeWidth(1f);
                        canvas.BeginPath();
                        canvas.RoundedRect(x + 0.5f, y + 0.5f, w - 1f, h - 1f, 8f);
                        canvas.Stroke();
                        canvas.RestoreState();
                    }));
            }
            else
            {
                paper.Box($"proj_subth_{sub.Guid}").Width(42).Height(42).Margin(ST, ST, 0, 0).Rounded(8)
                    .BackgroundLinearGradient(0, 0, 1, 1, Color.FromArgb(58, style.Color), Color.FromArgb(16, style.Color))
                    .BorderColor(Color.FromArgb(68, style.Color)).BorderWidth(1)
                    .Icon(paper, style.Icon, style.Color, size: 20f);
            }

            paper.Box($"proj_subnm_{sub.Guid}").Width(UnitValue.Stretch()).Height(14).Clip()
                .Text(sub.Name, font).TextColor(TMid).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter);

            BuildItemContextMenu(paper, $"proj_sub_ctx_{sub.Guid}", sub);
        }
    }

    private void DrawGridItem(Paper paper, Scribe.FontFile font, string id, ContentItem item,
        int idx, List<object> itemObjects, float cellSize)
    {
        bool isSelected = Selection.IsSelected(item);
        bool isSubAsset = item.IsSubAsset;
        bool isPinged = item.Guid != Guid.Empty && item.Guid == Selection.PingedGuid;

        using (paper.Column(id)
            .Width(cellSize).Height(UnitValue.Auto)
            .BackgroundColor(isSelected ? EditorTheme.Selected : (isSubAsset ? Color.FromArgb(20, Acc) : Color.Transparent))
            .BorderColor(isSelected ? Color.FromArgb(102, EditorTheme.Purple400) : Color.Transparent).BorderWidth(1)
            .Hovered.BackgroundColor(isSelected ? EditorTheme.Selected : EditorTheme.Hover).End()
            .Rounded(9)
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
                    NavigateTo(it.RelativePath);
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
            bool hasSubs = item.Subs.Count > 0;

            // Stacked "cards" peeking out behind the thumbnail signal that this asset has sub-assets.
            if (hasSubs)
            {
                float ts = cellSize - 8;
                paper.Box($"{id}_stk2").PositionType(PositionType.SelfDirected).Position(10, 10).Size(ts, ts)
                    .Rounded(10).BackgroundColor(Col(30, 24, 44, 130)).BorderColor(BdSoft).BorderWidth(1).IsNotInteractable();
                paper.Box($"{id}_stk1").PositionType(PositionType.SelfDirected).Position(7, 7).Size(ts, ts)
                    .Rounded(10).BackgroundColor(Col(30, 24, 44, 235)).BorderColor(BdSoft).BorderWidth(1).IsNotInteractable();
            }

            // Thumbnail area
            var thumbTex = EditorAssetDatabase.Instance?.GetThumbnailTexture(item.Guid);
            if (thumbTex != null)
            {
                // Rounded image tile (texture-brushed rounded rect) + a matching rounded border.
                paper.Box($"{id}_t")
                    .Width(cellSize - 8).Height(cellSize - 8)
                    .Margin(4, 4, 4, 0)
                    .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
                    {
                        float x = (float)r.Min.X, y = (float)r.Min.Y, w = (float)r.Size.X, h = (float)r.Size.Y;
                        canvas.DrawImageRounded(thumbTex, x, y, w, h, 10f);
                        var bd = Prowl.Vector.Color32.FromArgb(BdSoft.A, BdSoft.R, BdSoft.G, BdSoft.B);
                        canvas.SaveState();
                        canvas.SetStrokeColor(bd);
                        canvas.SetStrokeWidth(1f);
                        canvas.BeginPath();
                        canvas.RoundedRect(x + 0.5f, y + 0.5f, w - 1f, h - 1f, 10f);
                        canvas.Stroke();
                        canvas.RestoreState();
                    }));
            }
            else
            {
                var style = item.IsFolder ? AssetTypeStyles.Folder
                    : item.IsSubAsset ? AssetTypeStyles.SubAsset
                    : AssetTypeStyles.For(Path.GetExtension(item.Name), item.TypeLabel);

                float tileSz = cellSize - 8;
                var tile = paper.Box($"{id}_t").Width(tileSz).Height(tileSz).Margin(4, 4, 4, 0).Rounded(10);

                if (style.Bare)
                {
                    // Folder: a bare icon, no tile.
                    tile.Icon(paper, style.Icon, style.Color, size: tileSz * 0.62f);
                }
                else
                {
                    // Tinted gradient tile
                    tile.BackgroundLinearGradient(0, 0, 1, 1, Color.FromArgb(58, style.Color), Color.FromArgb(16, style.Color))
                        .BorderColor(Color.FromArgb(80, style.Color)).BorderWidth(1);
                    if (style.Badge != null)
                        tile.Text(style.Badge, EditorTheme.FontMono ?? font).TextColor(style.Color)
                            .FontSize(tileSz * 0.4f).Alignment(TextAlignment.MiddleCenter);
                    else
                        tile.Icon(paper, style.Icon, style.Color, size: tileSz * 0.5f);
                }
            }

            // Expand indicator for items with sub-assets
            // Sub-asset count badge (top-right pill); clicking toggles the sub-asset drawer below the row.
            if (hasSubs)
            {
                bool expanded = _expandedAssets.Contains(item.Guid);
                using (paper.Row($"{id}_sb").PositionType(PositionType.SelfDirected).Position(cellSize - 34, -2)
                    .Width(UnitValue.Auto).Height(17).Rounded(9).Padding(6, 6, 0, 0).RowBetween(3)
                    .BackgroundColor(Acc).DropShadow(0, 2, 8, 0, Col(0, 0, 0, 128))
                    .StopEventPropagation()
                    .OnClick(item.Guid, (guid, _) =>
                    {
                        if (_expandedAssets.Contains(guid)) _expandedAssets.Remove(guid);
                        else _expandedAssets.Add(guid);
                    })
                    .Enter())
                {
                    paper.Box($"{id}_sbico").Width(11).Height(17).Margin(0, 0, ST, ST).IsNotInteractable()
                        .Icon(paper, expanded ? EditorIcons.ChevronDown_I : EditorIcons.LayerGroup_I, Color.White, size: 10f);
                    paper.Box($"{id}_sbn").Width(UnitValue.Auto).Height(17).Margin(0, 0, ST, ST).IsNotInteractable()
                        .Text(item.Subs.Count.ToString(), EditorTheme.FontBold ?? font).TextColor(Color.White)
                        .FontSize(9.5f).Alignment(TextAlignment.MiddleCenter);
                }
            }

            // Label - a flow child that WRAPS and grows the card's height (no clipping/truncation).
            if (RenameOverlay.IsRenaming($"proj_asset_{item.RelativePath}"))
            {
                RenameOverlay.Draw(paper, $"{id}_rename", RenameOverlay.Position.Bottom);
            }
            else
            {
                paper.Box($"{id}_l")
                    .Width(UnitValue.Stretch()).Height(UnitValue.Auto)
                    .Margin(3, 3, 4, 6)
                    .Wrap(Prowl.Scribe.TextWrapMode.Wrap)
                    .Text(DisplayName(item), EditorTheme.FontMedium ?? font)
                    .TextColor(isSubAsset ? EditorTheme.AccentText : (isSelected ? THi : TBody))
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.Center);
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
        // Folders and files come from the asset database's cached index (single source of truth),
        // not per-frame filesystem calls.
        var folders = new List<ContentItem>();
        foreach (var sub in db.GetSubFolders(_currentFolder ?? ""))
        {
            if (!_showHidden && sub.Name.StartsWith('.')) continue;
            folders.Add(new ContentItem
            {
                Name = sub.Name, RelativePath = sub.RelativePath, IsFolder = true,
                Icon = EditorIcons.Folder, TypeLabel = "Folder",
            });
        }
        folders = folders.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();

        // Files gathered as units (a top-level file plus any expanded sub-assets) so sorting keeps
        // sub-assets attached to their parent.
        var units = new List<(ContentItem item, List<ContentItem> subs)>();
        foreach (var fileRec in db.GetFolderFiles(_currentFolder ?? ""))
        {
            string fileName = fileRec.Name;
            if (!_showHidden && fileName.StartsWith('.')) continue;

            string relPath = fileRec.RelativePath;
            string ext = Path.GetExtension(fileName).ToLowerInvariant();
            var entry = db.GetEntry(relPath);
            bool hasSubAssets = entry?.SubAssets != null && entry.SubAssets.Length > 0;

            var item = new ContentItem
            {
                Name = fileName, RelativePath = relPath, IsFolder = false,
                Icon = GetFileIcon(ext),
                TypeLabel = entry?.MainAssetType?.Name ?? ext.TrimStart('.').ToUpperInvariant(),
                Guid = entry?.Guid ?? Guid.Empty, HasSubAssets = hasSubAssets,
                Size = fileRec.Size, Modified = fileRec.Modified,
            };

            // Sub-assets are ALWAYS gathered onto the parent (item.Subs); the views decide whether
            // to reveal them (grid drawer / expandable table rows) based on _expandedAssets.
            if (hasSubAssets && entry != null)
                foreach (var sub in entry.SubAssets)
                    item.Subs.Add(new ContentItem
                    {
                        Name = sub.Name, RelativePath = $"{relPath}#{sub.Name}", IsFolder = false,
                        IsSubAsset = true, Icon = GetSubAssetIcon(sub.Type),
                        TypeLabel = sub.Type?.Name ?? "Unknown", Guid = sub.Guid, ParentGuid = entry.Guid,
                    });

            units.Add((item, item.Subs));
        }

        IEnumerable<(ContentItem item, List<ContentItem> subs)> sorted = _sortBy switch
        {
            SortMode.Type     => units.OrderBy(u => u.item.TypeLabel, StringComparer.OrdinalIgnoreCase).ThenBy(u => u.item.Name, StringComparer.OrdinalIgnoreCase),
            SortMode.Size     => units.OrderByDescending(u => u.item.Size),
            SortMode.Modified => units.OrderByDescending(u => u.item.Modified),
            _                 => units.OrderBy(u => u.item.Name, StringComparer.OrdinalIgnoreCase),
        };
        if (_groupByType && _sortBy != SortMode.Type)
            sorted = sorted.OrderBy(u => u.item.TypeLabel, StringComparer.OrdinalIgnoreCase);

        var items = new List<ContentItem>();
        items.AddRange(folders);
        items.AddRange(VirtualContentItems);
        foreach (var u in sorted) items.Add(u.item);  // subs live on item.Subs, not flattened

        if (!string.IsNullOrEmpty(_searchText))
            items = items.Where(i => i.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase)).ToList();

        return items;
    }

    // Display name honouring the "Show Extensions" toggle (folders + sub-assets always show as-is).
    private string DisplayName(ContentItem item) =>
        (_showExtensions || item.IsFolder || item.IsSubAsset) ? item.Name : Path.GetFileNameWithoutExtension(item.Name);

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "-";
        string[] u = { "B", "KB", "MB", "GB" };
        double s = bytes; int i = 0;
        while (s >= 1024 && i < u.Length - 1) { s /= 1024; i++; }
        return $"{(i == 0 ? s.ToString("0") : s.ToString("0.#"))} {u[i]}";
    }

    private static string GetFileIcon(string ext) => FileIconRegistry.GetIconForExtension(ext);

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
    public long Size;         // File size in bytes (0 for folders/virtual)
    public DateTime Modified; // Last write time (UTC)
    public List<ContentItem> Subs = new(); // Sub-assets of this file (populated for both views)

    public override bool Equals(object? obj) => obj is ContentItem c && c.Guid == Guid && c.RelativePath == RelativePath;
    public override int GetHashCode() => Guid != Guid.Empty ? Guid.GetHashCode() : RelativePath.GetHashCode();
}
