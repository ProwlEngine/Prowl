using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

using Prowl.Editor.Core;
using Prowl.Editor.GUI.Popups;
using Prowl.Editor.GUI.Registries;
using Prowl.Editor.Projects;
using Prowl.Editor.Theming;
using Prowl.Editor.Utils;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using static Prowl.Editor.GUI.EditorGUI;

using Color = System.Drawing.Color;

namespace Prowl.Editor.GUI.Panels;

public class ProjectPanel : DockPanel
{
    [MenuItem("Window/General/Project", priority: 4)]
    static void Open() => EditorApplication.Instance?.OpenPanel<ProjectPanel>();

    public static ProjectPanel Instance;
    public override string Title => Loc.Get("panel.project");
    public override string Icon => EditorIcons.Folder;

    // Refresh control lives in the leaf's tab-bar header (right side), matching the Nebula design.
    public override float HeaderWidth => 28f;
    public override void OnHeaderContent(Paper paper, float width, float height)
    {
        EditorGUI.HeaderIconButton(paper, "proj_hdr_refresh", EditorIcons.ArrowRotateRight, Loc.Get("project.refresh"), () =>
        {
            if (EditorAssetBackend.Instance != null)
            {
                Runtime.Debug.Log("Refreshing asset database...");
                EditorAssetBackend.Instance.Dispose();
                var db = new EditorAssetBackend(Project.Current!);
                db.Initialize();
            }
        });
    }

    // Handled Virtual (Placeholder) content to be displayed with normal objects
    public List<ContentItem> VirtualContentItems = [];

    // The content model is rebuilt only when something it derives from moves, since the panel itself
    // redraws every frame.
    private readonly record struct ContentCacheKey(
        bool IsTree, int Version, string Folder, string Search,
        int VirtualCount, SortMode Sort, bool GroupByType, bool ShowHidden);

    private List<ContentItem> _topLevelContent = [];
    private List<ContentItem> _flattenedContent = [];
    private ContentCacheKey _contentCacheKey = new(false, -1, "", "", -1, SortMode.Name, false, false);

    // The folder tree depends only on the folder index. Expansion and selection live outside the nodes
    // (Paper element storage and Selection), so they survive the nodes being reused.
    private List<TreeNode>? _folderTreeNodes;
    private readonly Dictionary<string, ContentItem> _folderTreeItemsByPath = [];
    private int _folderTreeVersion = -1;

    // Unified content tree view (One-Column Layout) displaying all folders, assets, and sub-assets.
    private bool _isTreeView;
    private List<TreeNode>? _contentTreeNodes;
    private int _contentTreeItemCount;
    private readonly HashSet<string> _forceExpandedTreeIds = [];
    private readonly Dictionary<string, bool> _contentTreeExpandState = [];

    public string CurrentFolder
    {
        get
        {
            if (_isTreeView && Selection.GetActiveAs<ContentItem>() is { } selected)
                return FolderForItem(selected);
            return _currentFolder;
        }
    }
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
    private static readonly HashSet<Guid> _expandedAssets = []; // files with sub-assets expanded
    private static Guid _pendingPingNavigate; // Navigate to pinged asset's folder on next frame
    private static int _lastPingVersion;
    private static int _revealedPingVersion;
    private Guid _revealGuid; // Pinged item to scroll into view once it is laid out
    private Rect _contentViewRect;
    private const float MinThumbSize = 20f;  // Below this = list mode
    private const float MaxThumbSize = 128f;
    private const float ListThreshold = 32f; // Below this = list view

    private const float ToolbarHeight = 34;
    private const float FooterHeight = 26;
    private const float FolderTreeWidth = 172f;
    private const float CutOpacity = 0.5f;

    // -- asset-browser view state --
    private enum ViewMode { Grid, List, Tree }
    private static readonly ViewMode[] s_viewModes = [ViewMode.Grid, ViewMode.List, ViewMode.Tree];

    private enum SortMode { Name, Type, Size, Modified }
    private SortMode _sortBy = SortMode.Name;
    private bool _showExtensions;
    private bool _showHidden;
    private bool _groupByType;
    private readonly Stack<string> _navBack = new();
    private readonly Stack<string> _navForward = new();

    private bool IsListView => _thumbnailSize < ListThreshold;
    private ViewMode CurrentViewMode => _isTreeView ? ViewMode.Tree : (IsListView ? ViewMode.List : ViewMode.Grid);

    private void ApplyViewMode(ViewMode mode)
    {
        switch (mode)
        {
            case ViewMode.Grid:
                _isTreeView = false;
                if (IsListView) _thumbnailSize = 64f;
                break;
            case ViewMode.List:
                _isTreeView = false;
                _thumbnailSize = 24f;
                break;
            case ViewMode.Tree:
                _isTreeView = true;
                break;
        }
    }

    private void SetViewMode(ViewMode mode)
    {
        if (mode == CurrentViewMode) return;

        if (_isTreeView && mode != ViewMode.Tree && Selection.GetActiveAs<ContentItem>() is { } selected)
            _currentFolder = FolderForItem(selected);

        ApplyViewMode(mode);

        if (!Application.IsPlaying)
            EditorApplication.Instance?.SaveDockLayout();
    }

    public override bool SerializeState(JsonObject state)
    {
        state["viewMode"] = CurrentViewMode.ToString();
        state["thumbnailSize"] = (double)_thumbnailSize;
        return true;
    }

    public override void RestoreState(JsonObject state)
    {
        if (state["thumbnailSize"] is JsonValue sizeVal && sizeVal.TryGetValue(out double size))
            _thumbnailSize = Math.Clamp((float)size, MinThumbSize, MaxThumbSize);

        if (state["viewMode"] is JsonValue modeVal && modeVal.TryGetValue(out string? modeStr)
            && Enum.TryParse<ViewMode>(modeStr, ignoreCase: true, out ViewMode mode))
            ApplyViewMode(mode);
    }

    private static string ViewModeLabel(ViewMode mode) => mode switch
    {
        ViewMode.Grid => Loc.Get("project.grid_view"),
        ViewMode.List => Loc.Get("project.list_view"),
        ViewMode.Tree => Loc.Get("project.tree_view"),
        _ => "",
    };

    private static string ViewModeIcon(ViewMode mode) => mode switch
    {
        ViewMode.Grid => EditorIcons.TableCellsLarge,
        ViewMode.List => EditorIcons.List,
        ViewMode.Tree => EditorIcons.FolderTree,
        _ => EditorIcons.TableCellsLarge,
    };

    // An OS file drop carries no drag payload, so the hover gates open for it too.
    private static bool DropHoverTrackingActive
        => DragDrop.IsDragging || DragDrop.IsDropFrame || ExternalAssetDrop.IsResolvingDropTarget;

    // Drop target for an OS file drop, cleared before each one resolves.
    internal string? ExternalDropHoverFolder => _dragHoverFolderNext ?? _dragHoverFolder;
    internal void ClearDropHover() { _dragHoverFolder = null; _dragHoverFolderNext = null; }

    public void NavigateTo(string folder)
    {
        ExpandTreeAncestorsForPath(folder);
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
        Scribe.FontFile? font = EditorTheme.DefaultFont;
        if (font == null || Project.Current == null) return;

        // Last-drawn wins, so a re-created panel replaces its dead predecessor.
        Instance = this;

        // A new ping, even of the same GUID again, navigates to the asset's folder.
        if (Selection.PingVersion != _lastPingVersion)
        {
            _lastPingVersion = Selection.PingVersion;
            if (Selection.PingedGuid != Guid.Empty) _pendingPingNavigate = Selection.PingedGuid;
        }

        if (_pendingPingNavigate != Guid.Empty)
        {
            Guid pingGuid = _pendingPingNavigate;
            _pendingPingNavigate = Guid.Empty;
            EditorAssetBackend? db = EditorAssetBackend.Instance;
            if (db != null)
            {
                // A sub-asset lives in its parent file's row, which opens to show it.
                string? path = db.GuidToPath(pingGuid);
                bool isSubAsset = false;
                if (path == null && db.TryGetParentGuid(pingGuid, out Guid parentGuid) && db.GuidToPath(parentGuid) is { } parentPath)
                {
                    path = parentPath;
                    isSubAsset = true;
                    _expandedAssets.Add(parentGuid);
                    RebuildFlattenedContent();
                }

                if (path != null)
                {
                    string folder = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "";
                    if (folder != _currentFolder) Origami.ScrollTo("proj_content", new Float2(0, 0));
                    _currentFolder = folder;
                    _searchText = "";
                    _revealGuid = pingGuid;

                    ExpandTreeAncestorsForPath(folder);
                    if (isSubAsset)
                        _forceExpandedTreeIds.Add(TreeAssetNodeId(path));
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
                    ExpandTreeAncestorsForPath(_dragDwellFolder);
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

        UpdateContent();
        using (paper.Row("proj_root").Size(width, height).Enter())
        {
            if (!_isTreeView)
                DrawFolderTree(paper, font, height);

            float mainW = _isTreeView ? width : width - FolderTreeWidth;
            using (paper.Column("proj_main").Width(mainW).Height(height).Enter())
            {
                DrawToolbar(paper, font, mainW);
                DrawContent(paper, font, mainW, height - ToolbarHeight - FooterHeight);
                DrawFooter(paper, font, mainW);
            }
        }

        // Process drops at the end of the frame after every OnHover callback from the
        // tree and content panels has fired. Uses the captured drop target (_dragHoverFolder)
        // or falls back to the currently-open folder when the drop lands on empty space in
        // the content area.
        if (DragDrop.IsDropFrame && DragDrop.Payload != null)
        {
            string? target = _dragHoverFolder;
            if (target == null && _contentBgHovered) target = _isTreeView ? "" : _currentFolder;
            if (target != null) DispatchProjectDrop(target, DragDrop.Payload);
        }
    }

    private void DispatchProjectDrop(string targetFolder, DragPayload payload)
    {
        switch (payload)
        {
            case AssetDragPayload ap when CanAcceptAssetDropInto(targetFolder):
                EditorAssetBackend.Instance?.MoveAssets(ap.AssetPaths, targetFolder);
                ExpandTreeAncestorsForPath(targetFolder);
                DragDrop.EndDrag();
                break;
            case GameObjectDragPayload gp:
                // Roots only: making a prefab of a parent already captures its children, and making
                // one of a child afterwards would tear that subtree back out of the parent's instance.
                foreach (GameObject go in GameObjectClipboard.FilterToRoots(gp.GameObjects.Where(g => g.IsValid())))
                    CreatePrefabInFolder(go, targetFolder);
                ExpandTreeAncestorsForPath(targetFolder);
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
                .PaddingLeft(8).PaddingRight(8).Gap(5)
                .Enter())
            {
                if (_isTreeView)
                {
                    using (paper.Row("proj_search_wrap").Width(UnitValue.StretchOne).Height(25).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).Enter())
                        Origami.SearchField(paper, "proj_search", _searchText, v => _searchText = v, Loc.Get("project.search")).Width(UnitValue.StretchOne).Height(25).Show();

                    DrawViewModeDropdown(paper, font);
                }
                else
                {
                    NavBtn(paper, "proj_back", EditorIcons.ChevronLeft_I, _navBack.Count > 0, NavBack);
                    NavBtn(paper, "proj_fwd", EditorIcons.ChevronRight_I, _navForward.Count > 0, NavForward);

                    DrawCrumbs(paper, font);

                    paper.Box("proj_tb_spacer");

                    DrawViewModeDropdown(paper, font);

                    using (paper.Row("proj_search_wrap").Width(150).Height(25).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).Enter())
                        Origami.SearchField(paper, "proj_search", _searchText, v => _searchText = v, Loc.Get("project.search")).Width(150).Height(25).Show();
                }

                ToolbarIconBtn(paper, "proj_opts", EditorIcons.EllipsisVertical, Loc.Get("project.options"), false,
                    () => Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, BuildOptionsMenu));
            }
            EditorGUI.Divider(paper, "proj_tb_div");
        }
    }

    private void DrawViewModeDropdown(Paper paper, Scribe.FontFile font)
    {
        ViewMode cur = CurrentViewMode;
        using (paper.Row("proj_view_wrap").Width(42).Height(25).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne)
            .Tooltip(ViewModeLabel(cur)).Enter())
        {
            Origami.Dropdown(paper, "proj_view", cur, SetViewMode, s_viewModes)
                .Width(42).Height(25)
                .PopoverWidth(165)
                .Display(ViewModeLabel)
                .Icon(ViewModeIcon)
                .CustomTrigger(ctx =>
                {
                    paper.Box("proj_view_ic")
                        .Width(22).Height(25)
                        .Margin(3, 0, 0, 0)
                        .IsNotInteractable()
                        .Text(ViewModeIcon(cur), font)
                        .TextColor(EditorTheme.Ink400)
                        .FontSize(13f)
                        .Alignment(TextAlignment.MiddleCenter);
                    paper.Box("proj_view_chev")
                        .Width(14).Height(25)
                        .Margin(0, 3, 0, 0)
                        .IsNotInteractable()
                        .Text(ctx.IsOpen ? EditorIcons.ChevronUp : EditorIcons.ChevronDown, font)
                        .TextColor(EditorTheme.Ink300)
                        .FontSize(9f)
                        .Alignment(TextAlignment.MiddleCenter);
                })
                .Show();
        }
    }

    // View / sort / filter menu, built with Origami's ContextMenu so it uses the theme's default fonts.
    private void BuildOptionsMenu(ContextBuilder b)
    {
        b.Header(Loc.Get("project.menu_view"));
        b.Toggle(Loc.Get("project.show_extensions"), () => _showExtensions = !_showExtensions, () => _showExtensions);
        foreach (ViewMode vm in s_viewModes)
        {
            ViewMode captured = vm;
            b.Item(ViewModeLabel(vm), () => SetViewMode(captured), on: CurrentViewMode == vm);
        }

        b.Header(Loc.Get("project.sort_by"));
        foreach (SortMode s in Enum.GetValues<SortMode>())
        {
            SortMode sc = s;
            b.Item(s.ToString(), () => _sortBy = sc, on: _sortBy == s);
        }

        b.Header(Loc.Get("project.filter"));
        b.Toggle(Loc.Get("project.show_hidden"), () => _showHidden = !_showHidden, () => _showHidden);
        b.Toggle(Loc.Get("project.group_by_type"), () => _groupByType = !_groupByType, () => _groupByType);
    }

    private static void NavBtn(Paper p, string id, IOrigamiIcon icon, bool enabled, Action onClick)
    {
        using (p.Box($"{id}_w").Width(UnitValue.Auto).Height(UnitValue.Auto).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).Enter())
            Origami.IconButton(p, id, icon, onClick).Small().Width(24).Height(24).Ghost().Disabled(!enabled).Show();
    }

    // Breadcrumb trail (Origami widget); the Assets root carries a folder icon, deeper segments are labels.
    private void DrawCrumbs(Paper p, Scribe.FontFile font)
    {
        var items = new List<BreadcrumbItem> { new("Assets", EditorIcons.Folder_I, (object)"") };
        if (!string.IsNullOrEmpty(_currentFolder))
        {
            string acc = "";
            foreach (string seg in _currentFolder.Split('/'))
            {
                acc = acc.Length > 0 ? acc + "/" + seg : seg;
                items.Add(new BreadcrumbItem(seg, "", acc));
            }
        }

        using (p.Row("proj_crumbs").Width(UnitValue.Auto).Height(24).Clip().Margin(4, 0, UnitValue.StretchOne, UnitValue.StretchOne).Enter())
            Origami.Breadcrumb(p, "proj_crumbs_bc", items, it => NavigateTo((string)it.UserData!))
                .Width(UnitValue.Auto).Height(24).Show();
    }

    // ================================================================
    //  Footer (count + thumbnail size slider)
    // ================================================================

    private void DrawFooter(Paper paper, Scribe.FontFile font, float width)
    {
        Scribe.FontFile mono = EditorTheme.FontMono ?? font;
        int selCount = Selection.GetSelected<ContentItem>().Count();
        int itemCount = _isTreeView ? _contentTreeItemCount : _topLevelContent.Count;

        using (paper.Row("proj_footer").Height(FooterHeight)
            .Padding(11, 11, 0, 0)
            .BackgroundColor(Color.FromArgb(36, 0, 0, 0))
            .Enter())
        {
            paper.Box("proj_foot_div").PositionType(PositionType.SelfDirected).Position(0, 0).Size(width, 1)
                .BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();

            paper.Box("proj_foot_count").Width(UnitValue.Auto).Height(FooterHeight).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne)
                .Text(Loc.Get("project.item_count", new { count = itemCount }), mono).TextColor(EditorTheme.InkDim).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
            if (selCount > 0)
                paper.Box("proj_foot_sel").Width(UnitValue.Auto).Height(FooterHeight).Margin(6, 0, UnitValue.StretchOne, UnitValue.StretchOne)
                    .Text("- " + Loc.Get("project.selected_count", new { count = selCount }), mono).TextColor(EditorTheme.AccentText).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

            paper.Box("proj_foot_spacer");

            if (!_isTreeView)
            {
                paper.Box("proj_foot_ico1").Width(14).Height(FooterHeight).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).IsNotInteractable()
                    .Text(EditorIcons.Image, font).TextColor(EditorTheme.InkFaint).FontSize(11f).Alignment(TextAlignment.MiddleCenter);
                using (paper.Row("proj_foot_sld_wrap").Width(90).Height(FooterHeight).AlignItems(LayoutAlignment.Center).Margin(7, 0, UnitValue.StretchOne, UnitValue.StretchOne).Enter())
                    Origami.Slider(paper, "proj_thumb_slider", _thumbnailSize, v => _thumbnailSize = v, MinThumbSize, MaxThumbSize)
                        .ShowValue(false).Width(90f).TrackThickness(4).ThumbSize(12).Height(18).Show();
                paper.Box("proj_foot_ico2").Width(16).Height(FooterHeight).Margin(7, 0, UnitValue.StretchOne, UnitValue.StretchOne).IsNotInteractable()
                    .Text(EditorIcons.Image, font).TextColor(EditorTheme.InkDim).FontSize(13f).Alignment(TextAlignment.MiddleCenter);
            }
        }
    }

    // ================================================================
    //  Drag & Drop helpers
    // ================================================================

    /// <summary>
    /// Build an <see cref="AssetDragPayload"/> for starting a drag from <paramref name="item"/>.
    /// If the item is part of the current selection, the payload includes every selected
    /// item so a multi-select drag moves the whole set. Returns null for undraggable items.
    /// </summary>
    private static AssetDragPayload? BuildAssetDragPayload(ContentItem item)
    {
        if (item.IsFolder && string.IsNullOrEmpty(item.RelativePath)) return null;

        if (item.IsSubAsset)
        {
            if (item.Guid == Guid.Empty) return null;
            Type? subType = EditorAssetBackend.Instance?.GetSubAssets(item.ParentGuid).FirstOrDefault(s => s.Guid == item.Guid)?.Type;
            return new AssetDragPayload(item.Guid, item.Name, item.RelativePath, subType);
        }

        // Primary lookup the item the user actually grabbed.
        Type? primaryType = null;
        if (!item.IsFolder && item.Guid != Guid.Empty)
            primaryType = EditorAssetBackend.Instance?.GetEntry(item.RelativePath)?.MainAssetType;

        // _selectionHelper has all selected items prior to the mouse button being released.
        var bundle = new List<ContentItem>();
        foreach (ContentItem s in SelectionHelper.GetSelected<ContentItem>())
            if (!s.IsSubAsset && !(s.IsFolder && string.IsNullOrEmpty(s.RelativePath))) bundle.Add(s);
        // Ensure the grabbed item is first (payload.AssetGuid/Name reflect primary).
        bundle.Remove(item);
        bundle.Insert(0, item);

        Guid[] guids = [.. bundle.Select(b => b.Guid)];
        string[] paths = [.. bundle.Select(b => b.RelativePath)];
        return new AssetDragPayload(item.Guid, item.Name, primaryType, guids, paths);
    }

    private static void StartItemDrag(ContentItem item)
    {
        if (BuildAssetDragPayload(item) is { } payload)
            DragDrop.StartDrag(payload);
    }

    private void DrawFolderDropHighlight(Paper p, string id, string relativePath, float round, float borderWidth = 1f)
    {
        if (_dragHoverFolder == relativePath
            && (DragDrop.IsDraggingType<GameObjectDragPayload>()
                || (DragDrop.IsDraggingType<AssetDragPayload>() && CanAcceptAssetDropInto(relativePath))))
        {
            p.Box(id)
                .PositionType(PositionType.SelfDirected)
                .Position(0, 0).Size(UnitValue.Stretch(), UnitValue.Stretch())
                .Rounded(round).IsNotInteractable()
                .BackgroundColor(Color.FromArgb(40, EditorTheme.Purple400))
                .BorderColor(EditorTheme.Purple400).BorderWidth(borderWidth);
        }
    }

    /// <summary>
    /// Create a prefab for <paramref name="go"/> inside <paramref name="destRelFolder"/>
    /// (assets-relative; empty = root). Uniquifies the filename against what's already there.
    /// </summary>
    private static void CreatePrefabInFolder(GameObject go, string destRelFolder)
        => AssetCreateMenu.CreatePrefabIn(go, destRelFolder);

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

        foreach (string rawSrc in payload.AssetPaths)
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
            .BackgroundColor(Color.FromArgb(26, 0, 0, 0))
            .Enter())
        {
            // Right-edge divider (a2 tree border-right).
            paper.Box("proj_tree_div").PositionType(PositionType.SelfDirected).Position(FolderTreeWidth - 1, 0)
                .Size(1, height).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();

            // Right-click background show create/explorer menu
            BuildBackgroundContextMenu(paper, "proj_tree_bg_ctx");

            // Flat node list from a recursive walk, plus a parallel ContentItem list for multi-select
            // via Selection.HandleListClick. Both depend only on the folder index, so they are rebuilt
            // when it moves rather than every frame.
            int treeVersion = EditorAssetBackend.Instance?.IndexVersion ?? -1;
            if (_folderTreeNodes == null || _folderTreeVersion != treeVersion)
            {
                var builtNodes = new List<OrigamiUI.TreeNode>();
                BuildFolderNodes(builtNodes, "", "Assets", 0);

                _folderTreeItemsByPath.Clear();
                foreach (TreeNode n in builtNodes)
                {
                    string relPath = (string)n.UserData!;
                    _folderTreeItemsByPath[relPath] = new ContentItem
                    {
                        Name = n.Label,
                        RelativePath = relPath,
                        IsFolder = true,
                        Icon = EditorIcons.Folder,
                        TypeLabel = "Folder"
                    };
                }

                _folderTreeNodes = builtNodes;
                _folderTreeVersion = treeVersion;
            }

            List<TreeNode> nodes = _folderTreeNodes;

            Origami.Tree(paper, "proj_tree", FolderTreeWidth, height)
                .Nodes(nodes)
                .MultiSelect()
                .IsSelected(n =>
                {
                    string relPath = (string)n.UserData!;
                    return _currentFolder == relPath;
                })
                .OnSelectModified((e, ctrl, shift) =>
                {
                    NavigateTo((string)e.Node.UserData!);
                })
                .OnDoubleClick(e => { /* tree handles expand toggle internally */ })
                .OnRightClick(e =>
                {
                    NavigateTo((string)e.Node.UserData!);
                })
                .OnHover((n, normY) =>
                {
                    if (DropHoverTrackingActive)
                        _dragHoverFolderNext = (string)n.UserData!;
                })
                .OnRightClick(e =>
                {
                    // Right-click context menu on folder tree.
                    string relativePath = (string)e.Node.UserData!;
                    NavigateTo(relativePath);
                    Origami.ContextMenu(paper.PointerPos.X, paper.PointerPos.Y, ItemContextMenu(_folderTreeItemsByPath[relativePath], inTree: true));
                })
                .CustomRowContent((p, node, isSel, isExp) =>
                {
                    string relativePath = (string)node.UserData!;
                    float opacity = AssetClipboard.IsCut(_folderTreeItemsByPath[relativePath]) ? CutOpacity : 1f;

                    // Rows are their own elements, so these ids only need to be unique within the row.
                    // Folder icon
                    p.Box("proj_fi")
                        .Width(18).Height(22).Opacity(opacity)
                        .Text(EditorIcons.Folder, font)
                        .TextColor(EditorTheme.Amber400)
                        .FontSize(12f).Alignment(TextAlignment.MiddleCenter);

                    // Name (inline rename or label)
                    if (RenameOverlay.IsRenaming($"proj_folder_{relativePath}"))
                    {
                        using (p.Box("proj_ft_rename_slot").Width(UnitValue.Stretch()).Height(22).Margin(4, 4, 0, 0).Opacity(opacity).Enter())
                            RenameOverlay.Draw(p, $"proj_ft_rename_{node.Id.GetHashCode()}", 20);
                    }
                    else
                    {
                        p.Box("proj_fl")
                            .Height(22)
                            .Margin(4, 0, 0, 0)
                            .Opacity(opacity)
                            .Text(node.Label, font)
                            .TextColor(EditorTheme.Ink500)
                            .FontSize(EditorTheme.FontSize)
                            .Alignment(TextAlignment.MiddleLeft);
                    }

                    // Drop-target highlight
                    DrawFolderDropHighlight(p, "proj_fn_drop", relativePath, EditorTheme.Roundness);
                })
                .Show();
        }
    }

    /// <summary>True when a folder holds no visible files or subfolders. Served from the cached folder index.</summary>
    private static bool IsFolderEmpty(string relativePath)
    {
        EditorAssetBackend? db = EditorAssetBackend.Instance;
        if (db == null) return false;

        IReadOnlyList<EditorAssetBackend.FolderRecord> subs = db.GetSubFolders(relativePath);
        for (int i = 0; i < subs.Count; i++)
            if (!subs[i].Name.StartsWith('.')) return false;

        IReadOnlyList<EditorAssetBackend.FileRecord> files = db.GetFolderFiles(relativePath);
        for (int i = 0; i < files.Count; i++)
            if (!files[i].Name.StartsWith('.')) return false;

        return true;
    }

    private static AssetTypeStyle FolderStyle(string relativePath)
        => IsFolderEmpty(relativePath) ? AssetTypeStyles.EmptyFolder : AssetTypeStyles.Folder;

    private static void BuildFolderNodes(List<OrigamiUI.TreeNode> nodes, string relativePath, string displayName, int depth)
    {
        // Read the folder structure from the asset database's cached index instead of walking the
        // filesystem every frame.
        EditorAssetBackend? db = EditorAssetBackend.Instance;
        List<EditorAssetBackend.FolderRecord> subDirs = db != null
            ? [.. db.GetSubFolders(relativePath)
                .Where(f => !f.Name.StartsWith('.'))
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)]
            : [];

        nodes.Add(new OrigamiUI.TreeNode
        {
            Id = relativePath,
            Label = displayName,
            Icon = IsFolderEmpty(relativePath) ? EditorIcons.FolderOpen : EditorIcons.Folder,
            IconColor = EditorTheme.Amber400,
            HasChildren = subDirs.Count > 0,
            DefaultExpanded = depth < 2,
            Depth = depth,
            UserData = relativePath
        });

        foreach (EditorAssetBackend.FolderRecord subDir in subDirs)
            BuildFolderNodes(nodes, subDir.RelativePath, subDir.Name, depth + 1);
    }

    // ================================================================
    //  Content Tree View (unified single-column tree)
    // ================================================================

    private static string TreeFolderNodeId(string relativePath, bool isSearching = false) =>
        isSearching ? $"search:ct_folder:{relativePath}" : $"ct_folder:{relativePath}";

    private static string TreeAssetNodeId(string relativePath, bool isSearching = false) =>
        isSearching ? $"search:ct_asset:{relativePath}" : $"ct_asset:{relativePath}";

    private static string TreeSubAssetNodeId(Guid guid, string relativePath, bool isSearching = false) =>
        isSearching ? $"search:ct_sub:{(guid != Guid.Empty ? guid.ToString() : relativePath)}" : $"ct_sub:{(guid != Guid.Empty ? guid.ToString() : relativePath)}";

    private static string FolderForItem(ContentItem item)
    {
        if (item.IsFolder)
            return item.RelativePath;
        return Path.GetDirectoryName(item.RelativePath)?.Replace('\\', '/') ?? "";
    }

    private void ExpandTreeAncestorsForPath(string folderPath)
    {
        _forceExpandedTreeIds.Add(TreeFolderNodeId(""));
        if (string.IsNullOrEmpty(folderPath)) return;

        string[] segments = folderPath.Split('/');
        string acc = "";
        foreach (string seg in segments)
        {
            acc = string.IsNullOrEmpty(acc) ? seg : $"{acc}/{seg}";
            _forceExpandedTreeIds.Add(TreeFolderNodeId(acc));
        }
    }

    private void BuildContentTreeFolder(
        EditorAssetBackend db,
        List<OrigamiUI.TreeNode> nodes,
        ref int itemCount,
        string relativePath,
        string displayName,
        int depth,
        bool isSearching)
    {
        var folderItem = new ContentItem
        {
            Name = displayName,
            RelativePath = relativePath,
            IsFolder = true,
            Icon = EditorIcons.Folder,
            TypeLabel = "Folder",
        };

        int folderNodeIndex = nodes.Count;
        var folderNode = new OrigamiUI.TreeNode
        {
            Id = TreeFolderNodeId(relativePath, isSearching),
            Label = displayName,
            Depth = depth,
            DefaultExpanded = isSearching || depth == 0,
            OverrideExpanded = isSearching ? true : null,
            UserData = folderItem,
        };
        nodes.Add(folderNode);
        int childrenStartIndex = nodes.Count;
        int prevItemCount = itemCount;

        // 1. Subfolders
        foreach (EditorAssetBackend.FolderRecord sub in GetSortedSubFolders(db, relativePath))
            BuildContentTreeFolder(db, nodes, ref itemCount, sub.RelativePath, sub.Name, depth + 1, isSearching);

        // 2. Virtual items belonging to this folder (e.g., pending CreateAssetTask rename placeholder)
        foreach (ContentItem virt in VirtualContentItems)
        {
            string virtParent = Path.GetDirectoryName(virt.RelativePath)?.Replace('\\', '/') ?? "";
            if (!string.Equals(virtParent, relativePath, StringComparison.OrdinalIgnoreCase))
                continue;

            nodes.Add(new OrigamiUI.TreeNode
            {
                Id = TreeAssetNodeId(virt.RelativePath, isSearching),
                Label = DisplayName(virt),
                Depth = depth + 1,
                HasChildren = false,
                IsLeaf = true,
                UserData = virt,
            });
            itemCount++;
        }

        // 3. Files in this folder
        foreach (ContentItem item in GetSortedFolderFiles(db, relativePath, filterSubsBySearch: isSearching))
        {
            if (isSearching && !EditorUtils.MatchesSearch(item.Name, _searchText) && item.Subs.Count == 0)
                continue;

            bool hasSubs = item.Subs.Count > 0;
            bool searchMatchSub = isSearching && !EditorUtils.MatchesSearch(item.Name, _searchText) && hasSubs;

            nodes.Add(new OrigamiUI.TreeNode
            {
                Id = TreeAssetNodeId(item.RelativePath, isSearching),
                Label = DisplayName(item),
                Depth = depth + 1,
                HasChildren = hasSubs,
                IsLeaf = !hasSubs,
                DefaultExpanded = searchMatchSub,
                OverrideExpanded = searchMatchSub ? true : null,
                UserData = item,
            });
            itemCount++;

            foreach (ContentItem sub in item.Subs)
            {
                nodes.Add(new OrigamiUI.TreeNode
                {
                    Id = TreeSubAssetNodeId(sub.Guid, sub.RelativePath, isSearching),
                    Label = DisplayName(sub),
                    Depth = depth + 2,
                    HasChildren = false,
                    IsLeaf = true,
                    UserData = sub,
                });
            }
        }

        bool hasEmittedChildren = nodes.Count > childrenStartIndex;
        if (isSearching && depth > 0 && !hasEmittedChildren && !EditorUtils.MatchesSearch(displayName, _searchText))
        {
            nodes.RemoveRange(folderNodeIndex, nodes.Count - folderNodeIndex);
            itemCount = prevItemCount;
            return;
        }

        folderNode.HasChildren = hasEmittedChildren;
        folderNode.IsLeaf = !hasEmittedChildren;
        if (depth > 0)
            itemCount++;
    }

    private void DrawTreeView(Paper paper, Scribe.FontFile font, float width, float height)
    {
        Scribe.FontFile mono = EditorTheme.FontMono ?? font;
        Scribe.FontFile semi = EditorTheme.FontSemiBold ?? font;
        List<OrigamiUI.TreeNode> nodes = _contentTreeNodes ?? [];
        bool isSearching = !string.IsNullOrEmpty(_searchText);

        for (int i = 0; i < nodes.Count; i++)
        {
            OrigamiUI.TreeNode node = nodes[i];
            if (node.UserData is ContentItem ci)
            {
                node.Label = DisplayName(ci);
                node.IsRenaming = IsRenamingItem(ci);
            }
            if (!isSearching)
                node.OverrideExpanded = _forceExpandedTreeIds.Contains(node.Id) ? true : null;
        }
        if (!isSearching && _forceExpandedTreeIds.Count > 0)
            _forceExpandedTreeIds.Clear();

        if (_revealGuid != Guid.Empty)
        {
            int visibleRow = VisibleTreeRowOf(nodes, _contentTreeExpandState, n => n.UserData is ContentItem ci && ci.Guid == _revealGuid);
            if (visibleRow >= 0)
            {
                float rowTotal = EditorTheme.RowHeight + 2f;
                float targetY = MathF.Max(0, visibleRow * rowTotal - (height * 0.5f) + rowTotal * 0.5f);
                Origami.ScrollTo("proj_content_tree_scroll", new Float2(0, targetY));
                _revealGuid = Guid.Empty;
            }
        }

        Origami.Tree(paper, "proj_content_tree", width, height)
            .Nodes(nodes)
            .IsSelected(n => SelectionHelper.IsSelected(n.UserData!))
            .HandleMultiSelect(paper, nodes, ItemContextMenu)
            .OnDoubleClick(e =>
            {
                if (e.Node.UserData is ContentItem it && !it.IsFolder && !it.HasSubAssets)
                    OpenItem(it);
            })
            .OnDragStart(n =>
            {
                if (n.UserData is ContentItem it)
                    StartItemDrag(it);
            })
            .OnHover((n, _) =>
            {
                if (n.UserData is ContentItem it && it.IsFolder && DropHoverTrackingActive)
                    _dragHoverFolderNext = it.RelativePath;
            })
            .IsPinged(n => n.UserData is ContentItem it && it.Guid != Guid.Empty && it.Guid == Selection.PingedGuid)
            .PingAlpha(() => Selection.GetPingAlpha())
            .ExpandStateSink(_contentTreeExpandState)
            .CustomRowContent((p, node, isSel, isExp) =>
            {
                var item = (ContentItem)node.UserData!;
                DrawItemRowLabelAndBadges(p, font, mono, semi, item, "ct", EditorTheme.RowHeight);
                if (item.IsFolder)
                    DrawFolderDropHighlight(p, "proj_ct_drop", item.RelativePath, EditorTheme.Roundness);
            })
            .Show();
    }

    private static int VisibleTreeRowOf(List<OrigamiUI.TreeNode> nodes, Dictionary<string, bool> expandState, Func<OrigamiUI.TreeNode, bool> match)
    {
        int visibleRow = 0;
        int collapsedDepth = -1;
        for (int i = 0; i < nodes.Count; i++)
        {
            OrigamiUI.TreeNode n = nodes[i];
            if (collapsedDepth >= 0)
            {
                if (n.Depth > collapsedDepth) continue;
                collapsedDepth = -1;
            }
            if (match(n)) return visibleRow;
            visibleRow++;
            if (n.HasChildren && expandState.TryGetValue(n.Id, out bool exp) && !exp)
                collapsedDepth = n.Depth;
        }
        return -1;
    }

    // ================================================================
    //  Content Area (right / unified)
    // ================================================================

    private void DrawContent(Paper paper, Scribe.FontFile font, float width, float height)
    {
        // Transparent background so the dock panel's glass (over the nebula) shows through.
        using (paper.Box("proj_content_bg")
            .Size(width, height)
            .OnPress(_ => { Selection.Clear(); if (_isTreeView) _currentFolder = ""; })
            .OnPostLayout((_, rect) => _contentViewRect = rect)
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
                    ContentItem? first = Selection.GetSelected<ContentItem>().FirstOrDefault();
                    if (first != null) StartRename(first);
                }
                else if (ShortcutManager.IsPressed("Project/Duplicate"))
                    AssetClipboard.Duplicate(Selection.GetSelected<ContentItem>());
                else if (ShortcutManager.IsPressed("Project/Copy"))
                    AssetClipboard.Copy(Selection.GetSelected<ContentItem>());
                else if (ShortcutManager.IsPressed("Project/Cut"))
                    AssetClipboard.Cut(Selection.GetSelected<ContentItem>());
                else if (ShortcutManager.IsPressed("Project/Paste"))
                    AssetClipboard.Paste(CurrentFolder);
                else if (ShortcutManager.IsPressed("Project/CancelCut"))
                    AssetClipboard.CancelCut();
            }

            // While any drag is active, show a banner at the top of the content area
            // summarizing what the user will get by dropping on the background. This is a
            // visual hint only the actual Accept runs AFTER the items are drawn so per-item
            // drop targets (folders in the grid) can win before the background fallback.
            string targetFolder = CurrentFolder;
            string folderDisplay = string.IsNullOrEmpty(targetFolder) ? "Assets" : targetFolder;
            if (DragDrop.IsDraggingType<GameObjectDragPayload>() && paper.IsParentHovered)
            {
                DropBanner(paper, "proj_prefab_drop", Loc.Get("project.drop_create_prefab", new { folder = folderDisplay }));
            }
            else if (DragDrop.IsDraggingType<AssetDragPayload>() && paper.IsParentHovered
                && CanAcceptAssetDropInto(targetFolder))
            {
                DropBanner(paper, "proj_asset_drop_hint", Loc.Get("project.drop_move", new { folder = folderDisplay }));
            }

            if (_isTreeView)
            {
                DrawTreeView(paper, font, width, height);
            }
            else if (IsListView)
            {
                DrawListView(paper, font, width, height);
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
                        if (_topLevelContent.Count == 0)
                        {
                            paper.Box("proj_empty")
                                .Height(60)
                                .Text(Loc.Get("project.folder_empty"), font)
                                .TextColor(EditorTheme.Ink300)
                                .FontSize(EditorTheme.FontSizeSmall)
                                .Alignment(TextAlignment.MiddleCenter);
                        }
                        else
                        {
                            DrawGridView(paper, font, width);
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

    private void DrawListView(Paper paper, Scribe.FontFile font, float width, float height)
    {
        Scribe.FontFile mono = EditorTheme.FontMono ?? font;
        Scribe.FontFile semi = EditorTheme.FontSemiBold ?? font;

        if (_revealGuid != Guid.Empty)
        {
            int row = _flattenedContent.FindIndex(v => v.Guid == _revealGuid);
            if (row >= 0)
            {
                Origami.ScrollTo("proj_table_sv", new Float2(0, MathF.Max(0, row * TableRowH - height * 0.5f)));
                _revealGuid = Guid.Empty;
            }
        }

        int activeCol = _sortBy switch { SortMode.Name => 0, SortMode.Type => 1, SortMode.Size => 2, _ => -1 };

        Origami.Table(paper, "proj_table")
            .Bordered(false)
            .Scroll(width, height)
            .RowHeight(TableRowH)
            .Column("Name", 2f, sortable: true)
            .Column("Type", 1f, sortable: true)
            .Column("Size", 0.7f, sortable: true, align: TextAlignment.MiddleRight)
            .Sort(activeCol, _sortBy != SortMode.Size, col =>
                _sortBy = col switch { 0 => SortMode.Name, 1 => SortMode.Type, 2 => SortMode.Size, _ => _sortBy })
            .HandleMultiSelect(paper, _flattenedContent, ItemContextMenu)
            .OnRowActivate(i =>
            {
                ContentItem it = _flattenedContent[i];
                if (!it.IsFolder && it.Subs.Count > 0)
                {
                    if (!_expandedAssets.Remove(it.Guid)) _expandedAssets.Add(it.Guid);
                    RebuildFlattenedContent();
                }
                else OpenItem(it);
            })
            .OnRowDragStart(i => StartItemDrag(_flattenedContent[i]))
            .OnRowHover((i, _) =>
            {
                ContentItem it = _flattenedContent[i];
                if (it.IsFolder && DropHoverTrackingActive) _dragHoverFolderNext = it.RelativePath;
            })
            .IsPinged(i => { Guid g = _flattenedContent[i].Guid; return g != Guid.Empty && g == Selection.PingedGuid; })
            .PingAlpha(() => Selection.GetPingAlpha())
            .RowCount(_flattenedContent.Count)
            .CellContent((rowIdx, col) => DrawTableCell(paper, font, mono, semi, _flattenedContent[rowIdx], _flattenedContent[rowIdx].IsSubAsset, col))
            .Show();
    }

    // Host-drawn content for one table cell (the table owns the column width, selection bg, scrolling).
    private void DrawTableCell(Paper paper, Scribe.FontFile font, Scribe.FontFile mono, Scribe.FontFile semi,
        ContentItem item, bool isSub, int col)
    {
        string id = item.Guid != Guid.Empty ? item.Guid.ToString() : item.RelativePath;
        bool hasSubs = item.Subs.Count > 0;
        bool expanded = hasSubs && _expandedAssets.Contains(item.Guid);
        float opacity = AssetClipboard.IsCut(item) ? CutOpacity : 1f;

        if (col == 0)
        {
            if (isSub)
                paper.Box($"proj_tcind_{id}").Width(20).Height(TableRowH).Opacity(opacity).IsNotInteractable();
            else if (hasSubs)
                paper.Box($"proj_tccar_{id}").Width(15).Height(TableRowH).Opacity(opacity)
                    .StopEventPropagation()
                    .OnClick(item.Guid, (g, _) =>
                    {
                        if (!_expandedAssets.Remove(g)) _expandedAssets.Add(g);
                        RebuildFlattenedContent();
                    })
                    .Icon(paper, expanded ? EditorIcons.ChevronDown_I : EditorIcons.ChevronRight_I, EditorTheme.InkDim, size: 11f);
            else
                paper.Box($"proj_tcsp_{id}").Width(15).Height(TableRowH).Opacity(opacity).IsNotInteractable();

            DrawItemRowLabelAndBadges(paper, font, mono, semi, item, $"tc_{id}", TableRowH);
        }
        else if (col == 1)
        {
            paper.Box($"proj_tctype_{id}").Height(TableRowH).Opacity(opacity).IsNotInteractable()
                .Text(item.IsFolder ? Loc.Get("inspector.folder") : item.TypeLabel, font).TextColor(EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
        }
        else
        {
            paper.Box($"proj_tcsize_{id}").Height(TableRowH).Opacity(opacity).IsNotInteractable()
                .Text(item.IsFolder || isSub ? "-" : FormatSize(item.Size), mono)
                .TextColor(isSub ? EditorTheme.InkFaint : EditorTheme.InkDim).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleRight);
        }
    }

    private static bool IsRenamingItem(ContentItem item) =>
        RenameOverlay.IsRenaming($"proj_asset_{item.RelativePath}")
        || RenameOverlay.IsRenaming($"proj_folder_{item.RelativePath}");

    // Shared icon/badge + inline rename or name label + sub-asset pill used by both Tree View and List View.
    private void DrawItemRowLabelAndBadges(Paper p, Scribe.FontFile font, Scribe.FontFile mono, Scribe.FontFile semi,
        ContentItem item, string idKey, float rowH)
    {
        bool isSub = item.IsSubAsset;
        bool hasSubs = item.Subs.Count > 0;
        float opacity = AssetClipboard.IsCut(item) ? CutOpacity : 1f;
        AssetTypeStyle style = item.IsFolder
            ? FolderStyle(item.RelativePath)
            : AssetTypeStyles.For(Path.GetExtension(item.Name), item.TypeLabel);

        ElementBuilder ic = p.Box($"proj_rico_{idKey}").Width(18).Height(rowH).Margin(2, 0, 0, 0).Opacity(opacity).IsNotInteractable();
        if (!item.IsFolder && style.Badge != null)
            ic.Text(style.Badge, mono).TextColor(style.Color).FontSize(10f).Alignment(TextAlignment.MiddleCenter);
        else
            ic.Icon(p, style.Icon, style.Color, size: 15f);

        if (IsRenamingItem(item))
        {
            using (p.Box($"proj_rrn_slot_{idKey}").Width(UnitValue.Stretch()).Height(rowH).Margin(6, 4, 0, 0).Opacity(opacity).Enter())
                RenameOverlay.Draw(p, $"proj_rrn_{idKey}", rowH - 2);
        }
        else
        {
            p.Box($"proj_rlbl_{idKey}").Width(UnitValue.Auto).Height(rowH).Margin(6, 0, 0, 0).Opacity(opacity).Clip().IsNotInteractable()
                .Text(DisplayName(item), font)
                .TextColor(isSub ? EditorTheme.Ink400 : EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

            if (hasSubs || isSub)
            {
                p.Box($"proj_rtag_{idKey}").Width(UnitValue.Auto).Height(17)
                    .Rounded(Origami.Current.Metrics.SmallRounding)
                    .Padding(6, 6, 0, 0)
                    .Margin(7, 0, UnitValue.StretchOne, UnitValue.StretchOne)
                    .Opacity(opacity).IsNotInteractable()
                    .BackgroundColor(EditorTheme.Selected)
                    .BorderColor(Color.FromArgb(77, EditorTheme.Purple400)).BorderWidth(1)
                    .Text(hasSubs ? item.Subs.Count.ToString() : "sub", semi)
                    .TextColor(EditorTheme.AccentText)
                    .FontSize(hasSubs ? 11f : 10f).Alignment(TextAlignment.MiddleCenter);
            }

            p.Box($"proj_rsp_{idKey}").Height(rowH).Opacity(opacity).IsNotInteractable();
        }
    }

    // Called when right-clicking on an item in the content browser (but not the folder tree).
    private Action<ContextBuilder> ItemContextMenu(object clickedItem) => ItemContextMenu((ContentItem)clickedItem, inTree: false);

    // Builds the context menu for one or more ContentItems.
    private Action<ContextBuilder> ItemContextMenu(ContentItem item, bool inTree) => builder =>
        {
            bool isMulti = Selection.Count > 1;
            bool isRoot = string.IsNullOrEmpty(item.RelativePath);
            string folder = FolderForItem(item);
            AssetTypeStyle titleStyle = item.IsFolder ? FolderStyle(item.RelativePath) : AssetTypeStyles.For(Path.GetExtension(item.Name), item.TypeLabel);

            // Subject of the menu.
            builder.Title(isMulti ? Loc.Get("project.item_count", new { count = Selection.Count }) : item.Name, iconDraw: titleStyle.Icon);

            // Open / reveal.
            builder.Item(Loc.Get("launcher.open"), () => OpenItem(item), icon: EditorIcons.FolderOpen);
            builder.Item(Loc.Get("project.show_in_explorer"), () => ShowInExplorer(item), icon: EditorIcons.FolderTree);
            if (!item.IsFolder && !isMulti)
                builder.Submenu(Loc.Get("project.open_with"), sub => OpenWithMenu(sub, item), icon: EditorIcons.ArrowUpRightFromSquare);
            if (item.IsFolder && !isMulti)
                builder.Item(Loc.Get("project.open_terminal"), () => ExternalTools.OpenTerminal(AbsolutePath(item)), icon: EditorIcons.Terminal);

            var selectedFiles = Selection.GetSelected<ContentItem>().Where(c => !c.IsFolder).ToList();
            if (selectedFiles.Count == 2 && Selection.Count == 2 && ExternalTools.HasDiffTool)
                builder.Item(Loc.Get("project.compare"), () => ExternalTools.Diff(AbsolutePath(selectedFiles[0]), AbsolutePath(selectedFiles[1])), icon: EditorIcons.CodeCompare);

            builder.Separator();

            builder.Submenu(Loc.Get("project.create"), sub => MenuItemAttribute.BuildContextMenu(sub, "Assets/Create"), icon: EditorIcons.FileCirclePlus);

            builder.Separator();

            builder.Item(Loc.Get("project.rename"), () => StartRename(item, inTree), enabled: !isMulti && !isRoot && CanRename(item), icon: EditorIcons.PenToSquare);
            builder.Item(Loc.Get("project.cut"), () => AssetClipboard.Cut(Selection.GetSelected<ContentItem>()),
                enabled: !isRoot && !item.IsSubAsset, shortcut: ShortcutText("Project/Cut"), icon: EditorIcons.Scissors);
            builder.Item(Loc.Get("project.copy"), () => AssetClipboard.Copy(Selection.GetSelected<ContentItem>()),
                enabled: !isRoot, shortcut: ShortcutText("Project/Copy"), icon: EditorIcons.Copy);
            builder.Item(Loc.Get("project.paste"), () => AssetClipboard.Paste(folder),
                enabled: AssetClipboard.HasContent, shortcut: ShortcutText("Project/Paste"), icon: EditorIcons.Paste);
            builder.Item(Loc.Get("project.duplicate"), () => AssetClipboard.Duplicate(Selection.GetSelected<ContentItem>()),
                enabled: !isRoot, shortcut: ShortcutText("Project/Duplicate"), icon: EditorIcons.Clone);
            builder.Item(Loc.Get("project.copy_path"), () => paper_SetClipboard(item.RelativePath), icon: EditorIcons.Copy);
            if (!item.IsFolder && item.Guid != Guid.Empty)
                builder.Item(Loc.Get("project.copy_guid"), () => paper_SetClipboard(item.Guid.ToString()), icon: EditorIcons.Fingerprint);
            if (!item.IsFolder)
                builder.Item(Loc.Get("project.reimport"), () =>
                {
                    EditorAssetBackend? db = EditorAssetBackend.Instance;
                    if (db == null) return;
                    foreach (ContentItem sel in Selection.GetSelected<ContentItem>())
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

    private static string ShortcutText(string id) => ShortcutManager.GetDisplayString(ShortcutManager.GetBinding(id));

    private void BuildBackgroundContextMenu(Paper paper, string id)
    {
        Origami.RightClickMenu(paper, id, builder =>
        {
            string folder = CurrentFolder;

            builder.Submenu(Loc.Get("project.create"), sub => MenuItemAttribute.BuildContextMenu(sub, "Assets/Create"), icon: EditorIcons.FileCirclePlus);
            builder.Item(Loc.Get("project.paste"), () => AssetClipboard.Paste(folder),
                enabled: AssetClipboard.HasContent, shortcut: ShortcutText("Project/Paste"), icon: EditorIcons.Paste);
            builder.Separator();

            builder.Item(Loc.Get("project.show_in_explorer"), () =>
            {
                string absPath = Path.Combine(Project.Current!.AssetsPath, folder);
                EditorUtils.OpenFileSystemPath(absPath);
            }, icon: EditorIcons.FolderOpen);
            builder.Item(Loc.Get("project.open_terminal"), () => ExternalTools.OpenTerminal(Path.Combine(Project.Current!.AssetsPath, folder)), icon: EditorIcons.Terminal);

            builder.Separator();

            builder.Item(Loc.Get("menu.assets.reimport_all"), () =>
            {
                EditorAssetBackend? db = EditorAssetBackend.Instance;
                if (db == null) return;
                foreach (AssetEntry? e in db.GetAllEntries().ToList())
                    db.Reimport(e.Guid);
                Runtime.Debug.Log("[AssetDatabase] Reimported all assets.");
            }, icon: EditorIcons.ArrowsRotate);

            builder.Item(Loc.Get("menu.assets.reload_scripts"),
                () => Prowl.Editor.Projects.Scripting.ScriptAssemblyManager.RequestRecompile(),
                icon: EditorIcons.ArrowsRotate);
            builder.Item(Loc.Get("project.regenerate_project_files"),
                () => Prowl.Editor.Projects.Scripting.ScriptCompiler.GenerateProjectFiles(Project.Current!),
                enabled: !Prowl.Editor.Projects.Scripting.ScriptAssemblyManager.IsCompiling,
                icon: EditorIcons.FileCode);
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
            EditorAssetBackend? db = EditorAssetBackend.Instance;
            if (db == null) return;
            foreach (ContentItem? sel in selected)
            {
                if (string.IsNullOrEmpty(sel.RelativePath) || sel.IsSubAsset) continue;
                if (sel.IsFolder)
                {
                    string absPath = Path.Combine(Project.Current!.AssetsPath, sel.RelativePath);
                    if (Directory.Exists(absPath))
                        db.DeleteAssetFolder(sel.RelativePath);
                    if (_currentFolder == sel.RelativePath || _currentFolder.StartsWith(sel.RelativePath + "/"))
                        _currentFolder = "";
                }
                else
                {
                    string absPath = Path.Combine(Project.Current!.AssetsPath, sel.RelativePath);
                    if (File.Exists(absPath))
                        db.DeleteAsset(sel.RelativePath);
                }
            }
            Selection.Clear();
        });
    }

    // A sub-asset is named by its importer, so only files and folders on disk can be renamed.
    private static bool CanRename(ContentItem item) => !item.IsSubAsset && !string.IsNullOrEmpty(item.RelativePath);

    private void StartRename(ContentItem item, bool inTree = false)
    {
        if (!CanRename(item)) return;

        // The folder being browsed is not in its own content, so it renames in the folder tree.
        if (!_isTreeView && item.IsFolder && item.RelativePath == _currentFolder) inTree = true;

        string id = inTree ? $"proj_folder_{item.RelativePath}" : $"proj_asset_{item.RelativePath}";
        string editName = item.IsFolder ? item.Name : Path.GetFileNameWithoutExtension(item.Name);

        RenameOverlay.Begin(id, editName, newText =>
        {
            string ext = item.IsFolder ? "" : Path.GetExtension(item.Name);

            // Typed text becoming a path segment. A separator in it would move the asset rather than
            // rename it, and the characters the filesystem refuses would surface as an IO exception.
            string newName = EditorUtils.SafeFileName(newText, "") + ext;
            if (newName == item.Name || string.IsNullOrWhiteSpace(newText) || newName == ext)
                return;

            string parentFolder = Path.GetDirectoryName(item.RelativePath)?.Replace('\\', '/') ?? "";
            string newRelPath = string.IsNullOrEmpty(parentFolder) ? newName : parentFolder + "/" + newName;

            if (item.IsFolder)
            {
                bool success = EditorAssetBackend.Instance?.MoveFolder(item.RelativePath, newRelPath) ?? false;
                if (!success)
                    Toasts.Show(Loc.Get("toast.rename_failed"), Loc.Get("toast.rename_exists", new { name = newName }), ToastType.Warning, 3f);
                else if (_currentFolder == item.RelativePath)
                    _currentFolder = newRelPath;
            }
            else
            {
                bool success = EditorAssetBackend.Instance?.MoveAsset(item.RelativePath, newRelPath) ?? false;
                if (!success)
                    Toasts.Show(Loc.Get("toast.rename_failed"), Loc.Get("toast.rename_exists", new { name = newName }), ToastType.Warning, 3f);
            }
        });
    }

    /// <summary>
    /// Open an item: folders navigate, assets go to their registered
    /// <see cref="AssetDoubleClickHandlerAttribute"/> handler, and anything without one
    /// falls back to the system default application.
    /// </summary>
    private void OpenItem(ContentItem item)
    {
        if (item.IsFolder)
        {
            NavigateTo(item.RelativePath);
            return;
        }

        if (EditorRegistries.DispatchDoubleClick(item.RelativePath, item.Guid)) return;
        string absPath = AbsolutePath(item);
        if (!ExternalTools.OpenOnDoubleClick(absPath)) ExternalTools.OpenWithSystem(absPath);
    }

    private static string AbsolutePath(ContentItem item) => Path.Combine(Project.Current!.AssetsPath, item.RelativePath);

    // The system app, then every tool set up for the file's extension.
    private static void OpenWithMenu(ContextBuilder menu, ContentItem item)
    {
        string absPath = AbsolutePath(item);
        menu.Item(Loc.Get("project.open_with_default"), () => ExternalTools.OpenWithSystem(absPath));
        var tools = ExternalTools.ToolsFor(absPath).ToList();
        if (tools.Count > 0) menu.Separator();
        foreach (ExternalTool tool in tools)
            menu.Item(string.IsNullOrEmpty(tool.Name) ? Path.GetFileNameWithoutExtension(tool.Path) : tool.Name, () => ExternalTools.OpenWith(tool, absPath));
    }

    private static void ShowInExplorer(ContentItem item)
    {
        string absPath = Path.Combine(Project.Current!.AssetsPath, item.RelativePath);
        EditorUtils.OpenFileSystemPath(absPath);
    }

    // ================================================================
    //  Grid View
    // ================================================================

    private void DrawGridView(Paper paper, Scribe.FontFile font, float width)
    {
        float cellSize = _thumbnailSize + 8f;

        // Flex-wrap: cards flow left-to-right and wrap; each card auto-grows in height to fit its
        // (wrapping) name. A full-width sub-asset drawer inserted after an expanded card forces a
        // line break, so it lands on its own row (like the CSS grid's `grid-column: 1/-1`).
        using (paper.Row("proj_grid_wrap").Width(UnitValue.Stretch()).Height(UnitValue.Auto)
            .WrapContent().Gap(12).LineGap(6).Enter())
        {
            for (int i = 0; i < _flattenedContent.Count; i++)
            {
                ContentItem item = _flattenedContent[i];
                if (item.IsSubAsset)
                    continue;
                DrawGridItem(paper, font, item, i, cellSize);
                if (item.Subs.Count > 0 && _expandedAssets.Contains(item.Guid))
                {
                    DrawSubDrawer(paper, font, item, globalIndex: i);
                    while (i < _flattenedContent.Count - 1 && _flattenedContent[i + 1].IsSubAsset)
                        i++;
                }
            }
        }
    }

    private static void DrawPingHighlight(Paper paper, ElementHandle handle, float round)
    {
        paper.Draw(ref handle, (canvas, r) =>
        {
            float alpha = Selection.GetPingAlpha();
            if (alpha <= 0f) return;
            var fillColor = Color.FromArgb((int)(alpha * 60), 255, 220, 50);
            var borderColor = Color.FromArgb((int)(alpha * 200), 255, 200, 0);
            float x = (float)r.Min.X, y = (float)r.Min.Y;
            float w = (float)r.Size.X, h = (float)r.Size.Y;
            canvas.RoundedRectFilled(x, y, w, h, round, round, round, round, fillColor);
            canvas.SetStrokeColor(borderColor);
            canvas.SetStrokeWidth(2f);
            canvas.BeginPath();
            float inner = MathF.Max(0f, round - 1f);
            canvas.RoundedRect(x + 1, y + 1, w - 2, h - 2, inner, inner, inner, inner);
            canvas.Stroke();
        });
    }

    // Scrolls the grid so a pinged item that was laid out outside the visible content comes into view.
    private void RevealInGrid(Guid guid, Rect rect)
    {
        if (guid != _revealGuid) return;
        _revealGuid = Guid.Empty;
        double above = rect.Min.Y - _contentViewRect.Min.Y;
        double below = rect.Max.Y - _contentViewRect.Max.Y;
        if (above < 0) Origami.ScrollBy("proj_content", new Float2(0, (float)above - 8));
        else if (below > 0) Origami.ScrollBy("proj_content", new Float2(0, (float)below + 8));
    }

    /// <summary>Brings the Project panel forward when an asset is pinged, so a ping from the inspector shows even when its tab is hidden.</summary>
    internal static void RevealPings()
    {
        if (Selection.PingVersion == _revealedPingVersion) return;
        _revealedPingVersion = Selection.PingVersion;
        if (Instance == null || Selection.PingTimer < 0.5f) return;
        if (EditorAssetBackend.Instance?.GuidToPathIncludingSubAssets(Selection.PingedGuid) == null) return;
        EditorApplication.Instance?.FocusPanelInstance(Instance);
    }

    private void DrawSubDrawer(Paper paper, Scribe.FontFile font, ContentItem parent, int globalIndex)
    {
        string pid = parent.Guid.ToString();
        using (paper.Column($"proj_drawer_{pid}").Width(UnitValue.Percentage(100)).Height(UnitValue.Auto)
            .Margin(0, 0, 3, 7).Padding(11, 11, 10, 10).Rounded(Origami.Current.Metrics.ContainerRounding)
            .BackgroundColor(Color.FromArgb(61, 0, 0, 0)).BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
            .Enter())
        {
            using (paper.Row($"proj_drawerlbl_{pid}").Height(18).Margin(0, 0, 0, 9).Enter())
            {
                paper.Box($"proj_drawerico_{pid}").Width(14).Height(18).Margin(0, 6, UnitValue.StretchOne, UnitValue.StretchOne).IsNotInteractable()
                    .Icon(paper, EditorIcons.LayerGroup_I, EditorTheme.AccentText, size: 12f);
                paper.Box($"proj_drawernm_{pid}").Width(UnitValue.Auto).Height(18).Margin(0, 6, UnitValue.StretchOne, UnitValue.StretchOne)
                    .Text(parent.Name, EditorTheme.FontSemiBold ?? font).TextColor(EditorTheme.AccentText)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
                paper.Box($"proj_drawercnt_{pid}").Width(UnitValue.Auto).Height(18).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne)
                    .Text("- " + Loc.Get("project.sub_asset_count", new { count = parent.Subs.Count }), font).TextColor(EditorTheme.InkDim)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
            }

            using (paper.Row($"proj_drawerrow_{pid}").Width(UnitValue.Stretch()).Height(UnitValue.Auto).WrapContent().Enter())
            {
                for (int i = 0; i < parent.Subs.Count; i++)
                    DrawSubThumb(paper, font, parent.Subs[i], globalIndex + 1 + i);
            }
        }
    }

    private void DrawSubThumb(Paper paper, Scribe.FontFile font, ContentItem sub, int globalIndex)
    {
        AssetTypeStyle style = AssetTypeStyles.For(Path.GetExtension(sub.Name), sub.TypeLabel);
        bool isSelected = SelectionHelper.IsSelected(sub);

        using (paper.Column($"proj_sub_{sub.Guid}").Width(62).Height(UnitValue.Auto).Margin(0, 6, 0, 6)
            .Padding(3, 3, 5, 5).Rounded(Origami.Current.Metrics.ContainerRounding).Gap(5)
            .BackgroundColor(isSelected ? EditorTheme.Selected : Color.Transparent)
            .Hovered.BackgroundColor(isSelected ? EditorTheme.Selected : EditorTheme.Hover).End()
            .HandleMultiSelect(paper, sub, _flattenedContent, globalIndex, ItemContextMenu)
            .OnDragStart(sub, (s, _) => StartItemDrag(s))
            .Tooltip(sub.Name)
            .OnPostLayout((handle, rect) =>
            {
                if (sub.Guid == Guid.Empty || sub.Guid != Selection.PingedGuid) return;
                RevealInGrid(sub.Guid, rect);
                DrawPingHighlight(paper, handle, Origami.Current.Metrics.ContainerRounding);
            })
            .Enter())
        {
            Texture2D? thumbTex = EditorAssetBackend.Instance?.GetThumbnailTexture(sub.Guid);
            float thumbRound = EditorTheme.Roundness;
            if (thumbTex.IsValid())
            {
                paper.Box($"proj_subth_{sub.Guid}").Width(42).Height(42).Margin(UnitValue.StretchOne, UnitValue.StretchOne, 0, 0)
                    .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
                    {
                        float x = (float)r.Min.X, y = (float)r.Min.Y, w = (float)r.Size.X, h = (float)r.Size.Y;
                        canvas.DrawImageRounded(thumbTex, x, y, w, h, thumbRound);
                        Vector.Color bd = Prowl.Vector.Color32.FromArgb(EditorTheme.BorderSoft.A, EditorTheme.BorderSoft.R, EditorTheme.BorderSoft.G, EditorTheme.BorderSoft.B);
                        canvas.SaveState();
                        canvas.SetStrokeColor(bd);
                        canvas.SetStrokeWidth(1f);
                        canvas.BeginPath();
                        canvas.RoundedRect(x + 0.5f, y + 0.5f, w - 1f, h - 1f, thumbRound);
                        canvas.Stroke();
                        canvas.RestoreState();
                    }));
            }
            else
            {
                paper.Box($"proj_subth_{sub.Guid}").Width(42).Height(42).Margin(UnitValue.StretchOne, UnitValue.StretchOne, 0, 0).Rounded(thumbRound)
                    .BackgroundLinearGradient(0, 0, 1, 1, Color.FromArgb(58, style.Color), Color.FromArgb(16, style.Color))
                    .BorderColor(Color.FromArgb(68, style.Color)).BorderWidth(1)
                    .Icon(paper, style.Icon, style.Color, size: 20f);
            }

            paper.Box($"proj_subnm_{sub.Guid}").Width(UnitValue.Stretch()).Height(14).Clip()
                .Text(sub.Name, font).TextColor(EditorTheme.Ink300).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter);
        }
    }

    private void DrawGridItem(Paper paper, Scribe.FontFile font, ContentItem item, int globalIndex, float cellSize)
    {
        bool isSelected = SelectionHelper.IsSelected(item);
        bool isSubAsset = item.IsSubAsset;
        bool isPinged = item.Guid != Guid.Empty && item.Guid == Selection.PingedGuid;
        float cellRound = Origami.Current.Metrics.ContainerRounding;

        // Everything below is scoped to the cell, so its element ids are constants.
        using (paper.Column("proj_gc", globalIndex)
            .Width(cellSize).Height(UnitValue.Auto)
            .BackgroundColor(isSelected ? EditorTheme.Selected : (isSubAsset ? Color.FromArgb(20, EditorTheme.Accent) : Color.Transparent))
            .BorderColor(isSelected ? Color.FromArgb(102, EditorTheme.Purple400) : Color.Transparent).BorderWidth(1)
            .Hovered.BackgroundColor(isSelected ? EditorTheme.Selected : EditorTheme.Hover).End()
            .Rounded(cellRound)
            .Opacity(AssetClipboard.IsCut(item) ? CutOpacity : 1f)
            .HandleMultiSelect(paper, item, _flattenedContent, globalIndex, ItemContextMenu)
            .OnDoubleClick(item, (it, _) =>
            {
                if (!it.IsFolder && it.HasSubAssets)
                {
                    if (!_expandedAssets.Remove(it.Guid)) _expandedAssets.Add(it.Guid);
                    RebuildFlattenedContent();
                }
                else
                    OpenItem(it);
            })
            .OnDragStart(item, (it, _) => StartItemDrag(it))
            .OnHover(item, (it, _) =>
            {
                if (!it.IsFolder) return;
                if (DropHoverTrackingActive) _dragHoverFolderNext = it.RelativePath;
            })
            .Tooltip(item.Name)
            .OnPostLayout((handle, rect) =>
            {
                if (!isPinged) return;
                RevealInGrid(item.Guid, rect);
                DrawPingHighlight(paper, handle, cellRound);
            })
            .Enter())
        {
            bool hasSubs = item.Subs.Count > 0;

            // Stacked "cards" peeking out behind the thumbnail signal that this asset has sub-assets.
            if (hasSubs)
            {
                float ts = cellSize - 8;
                paper.Box("stk2").PositionType(PositionType.SelfDirected).Position(10, 10).Size(ts, ts)
                    .Rounded(cellRound).BackgroundColor(Color.FromArgb(130, 30, 24, 44)).BorderColor(EditorTheme.BorderSoft).BorderWidth(1).IsNotInteractable();
                paper.Box("stk1").PositionType(PositionType.SelfDirected).Position(7, 7).Size(ts, ts)
                    .Rounded(cellRound).BackgroundColor(Color.FromArgb(235, 30, 24, 44)).BorderColor(EditorTheme.BorderSoft).BorderWidth(1).IsNotInteractable();
            }

            // Thumbnail area
            Texture2D? thumbTex = EditorAssetBackend.Instance?.GetThumbnailTexture(item.Guid);
            if (thumbTex.IsValid())
            {
                // Rounded image tile (texture-brushed rounded rect) + a matching rounded border.
                paper.Box("t")
                    .Width(cellSize - 8).Height(cellSize - 8)
                    .Margin(4, 4, 4, 0)
                    .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
                    {
                        float x = (float)r.Min.X, y = (float)r.Min.Y, w = (float)r.Size.X, h = (float)r.Size.Y;
                        canvas.DrawImageRounded(thumbTex, x, y, w, h, cellRound);
                        Vector.Color bd = Prowl.Vector.Color32.FromArgb(EditorTheme.BorderSoft.A, EditorTheme.BorderSoft.R, EditorTheme.BorderSoft.G, EditorTheme.BorderSoft.B);
                        canvas.SaveState();
                        canvas.SetStrokeColor(bd);
                        canvas.SetStrokeWidth(1f);
                        canvas.BeginPath();
                        canvas.RoundedRect(x + 0.5f, y + 0.5f, w - 1f, h - 1f, cellRound);
                        canvas.Stroke();
                        canvas.RestoreState();
                    }));
            }
            else
            {
                AssetTypeStyle style = item.IsFolder ? FolderStyle(item.RelativePath)
                    : item.IsSubAsset ? AssetTypeStyles.SubAsset
                    : AssetTypeStyles.For(Path.GetExtension(item.Name), item.TypeLabel);

                float tileSz = cellSize - 8;
                ElementBuilder tile = paper.Box("t").Width(tileSz).Height(tileSz).Margin(4, 4, 4, 0).Rounded(cellRound);

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
                using (paper.Row("sb").PositionType(PositionType.SelfDirected).Position(cellSize - 34, -2)
                    .Width(UnitValue.Auto).Height(17).Rounded(EditorTheme.Roundness > 0f ? 9 : 0).Padding(6, 6, 0, 0).Gap(3)
                    .BackgroundColor(EditorTheme.Accent).DropShadow(0, 2, 8, 0, Color.FromArgb(128, 0, 0, 0))
                    .StopEventPropagation()
                    .OnClick(item.Guid, (guid, _) =>
                    {
                        if (!_expandedAssets.Remove(guid)) _expandedAssets.Add(guid);
                        RebuildFlattenedContent();
                    })
                    .Enter())
                {
                    paper.Box("sbico").Width(11).Height(17).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).IsNotInteractable()
                        .Icon(paper, expanded ? EditorIcons.ChevronDown_I : EditorIcons.LayerGroup_I, Color.White, size: 10f);
                    paper.Box("sbn").Width(UnitValue.Auto).Height(17).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).IsNotInteractable()
                        .Text(item.Subs.Count.ToString(), EditorTheme.FontBold ?? font).TextColor(Color.White)
                        .FontSize(9.5f).Alignment(TextAlignment.MiddleCenter);
                }
            }

            // Label - a flow child that WRAPS and grows the card's height (no clipping/truncation).
            if (RenameOverlay.IsRenaming($"proj_asset_{item.RelativePath}"))
            {
                using (paper.Box("rename_slot").Width(UnitValue.Stretch()).Height(UnitValue.Auto).Margin(3, 3, 4, 6).Enter())
                    RenameOverlay.Draw(paper, "rename");
            }
            else
            {
                paper.Box("l")
                    .Width(UnitValue.Stretch()).Height(UnitValue.Auto)
                    .Margin(3, 3, 4, 6)
                    .Wrap(Prowl.Scribe.TextWrapMode.Wrap)
                    .Text(DisplayName(item), EditorTheme.FontMedium ?? font)
                    .TextColor(isSubAsset ? EditorTheme.AccentText : (isSelected ? EditorTheme.Ink500 : EditorTheme.Ink400))
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.Center);
            }

            // Folder grid items are drop targets highlight overlay painted when the cursor
            // is over this item during a valid drag. Central dispatch in OnGUI.
            if (item.IsFolder)
                DrawFolderDropHighlight(paper, "drop", item.RelativePath, cellRound, borderWidth: 2f);
        }
    }

    // ================================================================
    //  Content Item Helpers
    // ================================================================

    private void UpdateContent()
    {
        EditorAssetBackend? db = EditorAssetBackend.Instance;
        if (db is null)
            return;

        if (_isTreeView)
        {
            if (Selection.GetActiveAs<ContentItem>() is { } activeItem)
                _currentFolder = FolderForItem(activeItem);

            foreach (ContentItem virt in VirtualContentItems)
                ExpandTreeAncestorsForPath(FolderForItem(virt));
        }

        var key = new ContentCacheKey(
            _isTreeView, db.IndexVersion, _isTreeView ? "" : _currentFolder,
            _searchText, VirtualContentItems.Count, _sortBy, _groupByType, _showHidden);

        if (_contentCacheKey == key && (!_isTreeView || _contentTreeNodes != null))
            return;

        _contentCacheKey = key;

        if (_isTreeView)
        {
            var nodes = new List<OrigamiUI.TreeNode>();
            int itemCount = 0;
            BuildContentTreeFolder(db, nodes, ref itemCount, "", "Assets", 0, !string.IsNullOrEmpty(_searchText));
            _contentTreeNodes = nodes;
            _contentTreeItemCount = itemCount;
        }
        else
        {
            _topLevelContent = BuildContentEntries(db);
            RebuildFlattenedContent();
        }
    }

    private void RebuildFlattenedContent()
    {
        // Flat visible list (top-level rows + the sub-asset rows of any expanded parent).
        _flattenedContent.Clear();
        foreach (ContentItem ci in _topLevelContent)
        {
            _flattenedContent.Add(ci);
            if (ci.Subs.Count > 0 && _expandedAssets.Contains(ci.Guid))
                foreach (ContentItem s in ci.Subs) _flattenedContent.Add(s);
        }
    }

    private List<EditorAssetBackend.FolderRecord> GetSortedSubFolders(EditorAssetBackend db, string relativePath) =>
        [.. db.GetSubFolders(relativePath)
            .Where(f => _showHidden || !f.Name.StartsWith('.'))
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)];

    // Gathers and sorts files (with their sub-assets attached on item.Subs) for a single folder.
    private IEnumerable<ContentItem> GetSortedFolderFiles(EditorAssetBackend db, string relativePath, bool filterSubsBySearch = false)
    {
        var files = new List<ContentItem>();
        foreach (EditorAssetBackend.FileRecord fileRec in db.GetFolderFiles(relativePath))
        {
            string fileName = fileRec.Name;
            if (!_showHidden && fileName.StartsWith('.')) continue;

            string relPath = fileRec.RelativePath;
            string ext = Path.GetExtension(fileName).ToLowerInvariant();
            AssetEntry? entry = db.GetEntry(relPath);
            bool hasSubAssets = entry?.SubAssets != null && entry.SubAssets.Length > 0;

            var item = new ContentItem
            {
                Name = fileName, RelativePath = relPath, IsFolder = false,
                Icon = GetFileIcon(ext),
                TypeLabel = entry?.MainAssetType?.Name ?? ext.TrimStart('.').ToUpperInvariant(),
                Guid = entry?.Guid ?? Guid.Empty, HasSubAssets = hasSubAssets,
                Size = fileRec.Size, Modified = fileRec.Modified,
            };

            if (hasSubAssets && entry != null)
            {
                foreach (SubAssetEntry sub in entry.SubAssets)
                {
                    var subItem = new ContentItem
                    {
                        Name = sub.Name, RelativePath = $"{relPath}#{sub.Name}", IsFolder = false,
                        IsSubAsset = true, Icon = GetSubAssetIcon(sub.Type),
                        TypeLabel = sub.Type?.Name ?? "Unknown", Guid = sub.Guid, ParentGuid = entry.Guid,
                    };
                    if (!filterSubsBySearch || EditorUtils.MatchesSearch(subItem.Name, _searchText) || EditorUtils.MatchesSearch(item.Name, _searchText))
                        item.Subs.Add(subItem);
                }
            }

            files.Add(item);
        }

        IEnumerable<ContentItem> sorted = _sortBy switch
        {
            SortMode.Type     => files.OrderBy(i => i.TypeLabel, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase),
            SortMode.Size     => files.OrderByDescending(i => i.Size),
            SortMode.Modified => files.OrderByDescending(i => i.Modified),
            _                 => files.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase),
        };
        if (_groupByType && _sortBy != SortMode.Type)
            sorted = sorted.OrderBy(i => i.TypeLabel, StringComparer.OrdinalIgnoreCase);

        return sorted;
    }

    private List<ContentItem> BuildContentEntries(EditorAssetBackend db)
    {
        var items = new List<ContentItem>();
        foreach (EditorAssetBackend.FolderRecord sub in GetSortedSubFolders(db, _currentFolder ?? ""))
        {
            items.Add(new ContentItem
            {
                Name = sub.Name, RelativePath = sub.RelativePath, IsFolder = true,
                Icon = EditorIcons.Folder, TypeLabel = "Folder",
            });
        }

        items.AddRange(VirtualContentItems);
        items.AddRange(GetSortedFolderFiles(db, _currentFolder ?? ""));

        return [.. items.Where(i => VirtualContentItems.Contains(i) || EditorUtils.MatchesSearch(i.Name, _searchText))];
    }

    // Display name honouring the "Show Extensions" toggle (folders + sub-assets always show as-is).
    private string DisplayName(ContentItem item) =>
        (_showExtensions || item.IsFolder || item.IsSubAsset) ? item.Name : Path.GetFileNameWithoutExtension(item.Name);

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "-";
        string[] u = ["B", "KB", "MB", "GB"];
        double s = bytes; int i = 0;
        while (s >= 1024 && i < u.Length - 1) { s /= 1024; i++; }
        return $"{(i == 0 ? s.ToString("0") : s.ToString("0.#"))} {u[i]}";
    }

    private static string GetFileIcon(string ext) => EditorRegistries.GetFileIconForExtension(ext);

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
    public List<ContentItem> Subs = []; // Sub-assets of this file (populated for both views)

    public override bool Equals(object? obj) => obj is ContentItem c && c.Guid == Guid && c.RelativePath == RelativePath;
    public override int GetHashCode() => Guid != Guid.Empty ? Guid.GetHashCode() : RelativePath.GetHashCode();
}
