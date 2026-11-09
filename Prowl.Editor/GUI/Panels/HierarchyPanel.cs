using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.Editor.GUI.Popups;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.Prefabs;
using Prowl.Editor.Theming;
using Prowl.Editor.Utils;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.Editor.GUI.Panels;

public class HierarchyPanel : DockPanel
{
    [MenuItem("Window/General/Hierarchy", priority: 2)]
    static void Open() => EditorApplication.Instance?.OpenPanel<HierarchyPanel>();

    public override string Title => Loc.Get("panel.hierarchy");
    public override string Icon => EditorIcons.Sitemap;

    private string _searchText = "";
    private Paper? _paper;
    // Rename state is managed by RenameOverlay

    private const float ToolbarHeight = 30f;

    // Drag-drop state
    private bool _assetDropTarget;
    private enum DropPosition { Into, Above, Below }
    // Double-buffered drag hover state: deferred callbacks write to *Next,
    // promoted to current at frame start so layout reads the resolved value.
    private GameObject? _dragHoverTarget;
    private string? _dragHoverTargetId;
    private float _dragHoverNormalizedY;
    private GameObject? _dragHoverTargetNext;
    private string? _dragHoverTargetIdNext;
    private float _dragHoverNormalizedYNext;
    private SceneHeader? _dragHoverHeader;
    private SceneHeader? _dragHoverHeaderNext;

    // A header row: an open scene of the edit world, or a section of the play world.
    private sealed class SceneHeader
    {
        public OpenScene? Open;
        public SceneSection? Section;
        public readonly TreeNode Node = new() { DefaultExpanded = true };
    }

    private readonly ConditionalWeakTable<object, SceneHeader> _headers = [];

    // Ping state which GOs in the hierarchy match the pinged GUID
    private static Guid _lastHierarchyPingGuid;
    private static readonly HashSet<GameObject> _pingedGameObjects = [];

    // IDs of nodes that need force-expanding (parents of pinged GOs)
    private readonly HashSet<string> _forceExpandedIds = [];
    // Filled by the tree each frame with each node's resolved expanded state, so drag-drop can turn a
    // "below" drop on an expanded node into a "first child" drop (where its first child visually sits).
    private readonly Dictionary<string, bool> _expandState = [];

    // Rebuilt every frame from the scene. Nodes are kept per GameObject so their id string is only made once.
    private readonly List<TreeNode> _treeNodes = [];
    private readonly List<object> _flatObjects = [];
    private readonly ConditionalWeakTable<GameObject, TreeNode> _nodeCache = [];

    public override void OnGUI(Paper paper, float width, float height)
    {
        _paper = paper;
        Scribe.FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;

        // Promote deferred hover state then clear the next slot. OnHover callbacks
        // fire every frame the mouse is over a node, so clearing is safe - the callback
        // will re-set it if still hovering. On the drop frame keep the current value.
        if (DragDrop.IsDropFrame)
        {
            // Drop frame: _dragHoverTarget already has the right value
        }
        else
        {
            _dragHoverTarget = _dragHoverTargetNext;
            _dragHoverTargetId = _dragHoverTargetIdNext;
            _dragHoverNormalizedY = _dragHoverNormalizedYNext;
            _dragHoverHeader = _dragHoverHeaderNext;
            _dragHoverTargetNext = null;
            _dragHoverTargetIdNext = null;
            _dragHoverNormalizedYNext = 0f;
            _dragHoverHeaderNext = null;
        }

        SceneInstance scene = EditorSceneManager.EditedWorld;

        using (paper.Column("hier_root")
            .Size(width, height)
            .OnClick(0, (_, _) => Selection.Clear())
            //.OnRightClick(0, (_, _) => Selection.Clear())
            .Enter())
        {
            // Prefab editing breadcrumb
            if (PrefabEditingMode.IsEditing)
            {
                using (paper.Row("hier_prefab_breadcrumb")
                    .Height(24)
                    .BackgroundColor(Color.FromArgb(40, EditorTheme.Purple400))
                    .Rounded(Origami.Current.Metrics.SmallRounding).Margin(4, 4, 4, 0)
                    .PaddingLeft(6).Gap(4)
                    .Enter())
                {
                    paper.Box("hier_prefab_back")
                        .Width(UnitValue.Auto).Height(24)
                        .Text($"{EditorIcons.ArrowLeft}  {Loc.Get("hierarchy.back")}", font)
                        .TextColor(EditorTheme.Purple400)
                        .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft)
                        .Hovered.TextColor(EditorTheme.Ink500).End()
                        .OnClick(0, (_, _) => PrefabEditingMode.RequestExit());

                    paper.Box("hier_prefab_sep_arrow")
                        .Width(UnitValue.Auto).Height(24)
                        .Text(EditorIcons.ChevronRight, font)
                        .TextColor(EditorTheme.Ink400)
                        .FontSize(8f).Alignment(TextAlignment.MiddleCenter);

                    string sceneName = PrefabEditingMode.OriginalSceneName ?? Loc.Get("panel.scene");
                    paper.Box("hier_prefab_scene")
                        .Width(UnitValue.Auto).Height(24)
                        .Text(sceneName, font)
                        .TextColor(EditorTheme.Ink400)
                        .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

                    paper.Box("hier_prefab_sep_arrow2")
                        .Width(UnitValue.Auto).Height(24)
                        .Text(EditorIcons.ChevronRight, font)
                        .TextColor(EditorTheme.Ink400)
                        .FontSize(8f).Alignment(TextAlignment.MiddleCenter);

                    string prefabName = PrefabEditingMode.EditingPrefabPath != null
                        ? System.IO.Path.GetFileNameWithoutExtension(PrefabEditingMode.EditingPrefabPath)
                        : Loc.Get("hierarchy.prefab_fallback");

                    paper.Box("hier_prefab_name")
                        .Width(UnitValue.Auto).Height(24)
                        .Text(prefabName, font)
                        .TextColor(EditorTheme.Purple400)
                        .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

                    paper.Box("hier_prefab_spacer");

                    Origami.Button(paper, "hier_prefab_save_exit", $"{EditorIcons.FloppyDisk}  {Loc.Get("hierarchy.save_and_exit")}", () => PrefabEditingMode.SaveAndExit()).Width(100).Show();
                }
            }

            // Toolbar
            DrawToolbar(paper, font, width);

            if (scene.IsNotValid())
            {
                EditorGUI.EmptyState(paper, "hier_empty", Loc.Get("hierarchy.no_scene_loaded"), font);
                Origami.Button(paper, "hier_create_scene", $"{EditorIcons.Plus}  {Loc.Get("hierarchy.new_scene")}", EditorSceneManager.NewWithPrompt).Width(120).Show();
                return;
            }

            using (paper.Box("hier_bg").Enter())
            {
                // Background right-click create menu only
                BuildBackgroundContextMenu(paper);

                // Track if the background (hier_bg) is hovered for drop-on-empty-space
                bool bgHovered = paper.IsParentHovered;

                // Hierarchy keyboard shortcuts
                if (bgHovered && !ShortcutManager.IsRebinding)
                {
                    if (ShortcutManager.IsPressed("Hierarchy/Delete"))
                    {
                        foreach (GameObject go in ExcludeNestedSelections([.. Selection.GetSelected<GameObject>()]))
                            DeleteGameObject(go);
                    }
                    else if (ShortcutManager.IsPressed("Hierarchy/Duplicate"))
                    {
                        List<GameObject> dupes = GameObjectClipboard.Duplicate([.. Selection.GetSelected<GameObject>()]);
                        foreach (GameObject d in dupes) Undo.RegisterCreatedObjectUndo(d, "Duplicate");
                    }
                    else if (ShortcutManager.IsPressed("Hierarchy/Copy"))
                    {
                        GameObjectClipboard.Copy([.. Selection.GetSelected<GameObject>()]);
                    }
                    else if (ShortcutManager.IsPressed("Hierarchy/Paste"))
                    {
                        // Paste as children of first selected, or at root
                        GameObject? parent = Selection.GetSelected<GameObject>().FirstOrDefault();
                        List<GameObject> pasted = GameObjectClipboard.Paste(parent);
                        foreach (GameObject p in pasted) Undo.RegisterCreatedObjectUndo(p, "Paste");
                    }
                    else if (ShortcutManager.IsPressed("Hierarchy/Rename"))
                    {
                        GameObject? first = Selection.GetSelected<GameObject>().FirstOrDefault();
                        if (first.IsValid())
                            StartRenameGO(first, Selection.GetSelected<GameObject>());
                    }
                    else if (ShortcutManager.IsPressed("Hierarchy/CreateEmptyChild"))
                    {
                        // A shortcut has no menu behind it, so it acts on the selection directly.
                        // With nothing selected the new object lands at the scene root.
                        CreateGameObject("GameObject", Selection.GetSelected<GameObject>().FirstOrDefault());
                    }
                    else if (ShortcutManager.IsPressed("Hierarchy/CreateEmptyParent"))
                    {
                        CreateEmptyParent();
                    }
                }

                // Handle ping search for the pinged GUID among GameObjects and their component AssetRefs.
                bool pingIsNew = false;
                if (Selection.PingedGuid != Guid.Empty && Selection.PingedGuid != _lastHierarchyPingGuid)
                {
                    _lastHierarchyPingGuid = Selection.PingedGuid;
                    _pingedGameObjects.Clear();
                    FindGameObjectsWithGuid(scene, Selection.PingedGuid, _pingedGameObjects);

                    // Collect parent and scene header IDs to force-expand so pinged GOs are visible
                    _forceExpandedIds.Clear();
                    foreach (GameObject pinged in _pingedGameObjects)
                    {
                        _forceExpandedIds.Add(HeaderId(SectionKey(pinged.Section)));
                        GameObject? parent = pinged.Parent;
                        while (parent.IsValid())
                        {
                            _forceExpandedIds.Add(NodeId(parent));
                            parent = parent.Parent;
                        }
                    }
                    pingIsNew = _pingedGameObjects.Count > 0 || EditorSceneManager.OpenScenes.Any(IsPingedScene);
                }
                if (Selection.PingedGuid == Guid.Empty)
                {
                    _lastHierarchyPingGuid = Guid.Empty;
                    _pingedGameObjects.Clear();
                    _forceExpandedIds.Clear();
                }

                // Account for toolbar + margins + optional prefab breadcrumb
                float usedHeight = ToolbarHeight + 8;
                if (PrefabEditingMode.IsEditing)
                    usedHeight += 28; // prefab breadcrumb row + margins
                float scrollHeight = height - usedHeight;
                List<TreeNode> treeNodes = _treeNodes;
                List<object> flatObjects = _flatObjects;
                treeNodes.Clear();
                flatObjects.Clear();
                BuildSceneNodes(scene, treeNodes, flatObjects);

                // Scroll-to-ping: when a newly-pinged GO lives in the scene, center its row in the
                // scroll view so the yellow highlight is actually visible.
                if (pingIsNew)
                {
                    int pingIndex = VisibleRowOf(treeNodes, IsPingedNode);
                    if (pingIndex >= 0)
                    {
                        float rowTotal = EditorTheme.RowHeight + 2f; // row + vertical spacing
                        float targetY = pingIndex * rowTotal - (scrollHeight * 0.5f) + rowTotal * 0.5f;
                        Origami.ScrollTo("hier_tree_scroll", new Float2(0, targetY));
                    }
                }

                // Tree view
                Origami.Tree(paper, "hier_tree", width, scrollHeight)
                    .Nodes(treeNodes)
                    .MultiSelect()
                    .Reorderable()
                    .IsSelected(n => n.UserData is GameObject g && Selection.IsSelected(g))
                    .OnSelectModified((e, ctrl, shift) =>
                    {
                        if (DragDrop.IsDragging || DragDrop.IsDropFrame) return;
                        if (e.Node.UserData is not GameObject go) return;
                        Selection.HandleListClick(go, (IReadOnlyList<object>)flatObjects, e.Index, ctrl, shift);
                        foreach (object picked in Selection.Selected.ToArray())
                            if (picked is SceneHeader) Selection.RemoveFromSelection(picked);
                    })
                    .OnDoubleClick(e =>
                    {
                        if (e.Node.UserData is SceneHeader { Open: { IsLoaded: false } unloaded })
                        {
                            if (EditorSceneManager.CanEditScenes) EditorSceneManager.Load(unloaded);
                            return;
                        }
                        // Focus the scene view camera on the double-clicked object
                        if (e.Node.UserData is not GameObject go) return;
                        Selection.Select(go);
                        SceneViewPanel.ActiveCamera?.FocusSelection();
                    })
                    .OnRightClick(e =>
                    {
                        if (e.Node.UserData is not GameObject go) return;
                        // Select the object with Right click as well if we're not performing a multiple selection action
                        if (!paper.IsKeyDown(PaperKey.LeftControl) && !paper.IsKeyDown(PaperKey.LeftShift))
                            Selection.Select(go);
                        if (!Selection.IsSelected(go)) Selection.AddToSelection(go);
                    })
                    .OnDragStart(n =>
                    {
                        if (DragDrop.IsDragging || n.UserData is not GameObject go) return;
                        GameObject[] selected = [.. Selection.GetSelected<GameObject>()];
                        if (selected.Length > 0 && Selection.IsSelected(go))
                            DragDrop.StartDrag(new GameObjectDragPayload(selected));
                        else
                            DragDrop.StartDrag(new GameObjectDragPayload(go));
                    })
                    .OnHover((n, normY) =>
                    {
                        if (!DragDrop.IsDragging || DragDrop.Payload is not GameObjectDragPayload) return;
                        if (n.UserData is SceneHeader header)
                        {
                            _dragHoverHeaderNext = header;
                            return;
                        }
                        if (n.UserData is not GameObject go) return;
                        _dragHoverTargetNext = go;
                        _dragHoverTargetIdNext = NodeId(go);
                        _dragHoverNormalizedYNext = normY;
                    })
                    .CustomRowContent((paper, node, isSel, isExp) =>
                    {
                        if (font == null) return;
                        if (node.UserData is SceneHeader header)
                        {
                            DrawSceneHeader(paper, font, header);
                            return;
                        }
                        if (node.UserData is not GameObject go) return;
                        string goId = node.Id;

                        // Rows are their own elements, so these ids only need to be unique within the row.
                        // Icon (vector, chosen from the GameObject's first component + coloured)
                        (IOrigamiIcon? goIcon, Color goColor) = GetGoStyle(go);
                        if (!go.EnabledInHierarchy) goColor = Color.FromArgb(120, goColor);
                        paper.Box("hier_ico")
                            .Width(18).Height(EditorTheme.RowHeight).IsNotInteractable()
                            .Icon(paper, goIcon, goColor, size: 14f);

                        // Name or rename field
                        if (RenameOverlay.IsRenaming(goId))
                        {
                            using (paper.Box("hier_renamebox")
                                       .Width(UnitValue.StretchOne)
                                       .Height(EditorTheme.RowHeight)
                                       .Enter())
                            {
                                RenameOverlay.Draw(paper, $"hier_rename_{goId}");
                            }
                        }
                        else
                        {
                            paper.Box("hier_name")
                                .Height(EditorTheme.RowHeight).PaddingLeft(4)
                                .Text(go.Name, font)
                                .TextColor(GetPrefabTextColor(go))
                                .FontSize(EditorTheme.FontSizeSmall)
                                .Alignment(TextAlignment.MiddleLeft);
                        }

                        // Visibility eye
                        paper.Box("hier_vis")
                            .Width(18).Height(EditorTheme.RowHeight)
                            .Text(node.TrailingIcon ?? "", font)
                            .TextColor(node.TrailingIconColor ?? EditorTheme.Ink400)
                            .FontSize(9f).Alignment(TextAlignment.MiddleCenter)
                            .StopEventPropagation()
                            .OnClick(go, (g, _) =>
                            {
                                Undo.RecordObject(g, "Toggle Visibility");
                                g.Enabled = !g.Enabled;
                            });

                        // Per-GameObject right-click menu
                        BuildGameObjectContextMenu(paper, "hier_go_ctx");
                    })
                    .IsPinged(n => IsPingedNode(n) && Selection.PingedGuid != Guid.Empty)
                    .PingAlpha(() => Selection.GetPingAlpha())
                    .ExpandStateSink(_expandState)
                    .EmptyMessage(Loc.Get("hierarchy.scene_empty"))
                    .Show();

                // --- All drop handling uses hier_bg (the stable outer background) ---

                // Asset drops show visual indicator and spawn at root
                if (DragDrop.IsDraggingType<AssetDragPayload>() && bgHovered)
                {
                    _assetDropTarget = true;
                    EditorGUI.DropBanner(paper, "hier_drop_zone", Loc.Get("hierarchy.drop_to_spawn"));
                }
                else if (DragDrop.IsDraggingType<AssetDragPayload>())
                {
                    _assetDropTarget = false;
                }

                if (DragDrop.IsDropFrame && _assetDropTarget && DragDrop.Payload is AssetDragPayload assetDrop)
                {
                    if (assetDrop.AssetType == typeof(Runtime.Resources.SceneInstance))
                    {
                        AssetEntry? entry = EditorAssetBackend.Instance?.GetEntry(assetDrop.AssetGuid);
                        if (entry != null && EditorSceneManager.SceneAt(entry.Path) is { } dropped && EditorSceneManager.CanEditScenes)
                            EditorSceneManager.OpenAdditive(dropped, load: !Input.GetKey(KeyCode.AltLeft) && !Input.GetKey(KeyCode.AltRight));
                    }
                    else
                    {
                        SpawnAssetInScene(assetDrop, null, Float3.Zero);
                    }
                    DragDrop.EndDrag();
                    _assetDropTarget = false;
                }

                // GO drop process using hover target tracked by OnHover callback
                // Only process drops that land inside the hierarchy panel
                if (DragDrop.IsDropFrame && bgHovered && DragDrop.Payload is GameObjectDragPayload goDrop)
                {
                    if (_dragHoverHeader != null)
                    {
                        List<GameObject> dragged = [.. ExcludeNestedSelections(goDrop.GameObjects)];
                        SceneHeader header = _dragHoverHeader;
                        DragDrop.EndDrag();
                        // Unparenting a child a prefab provides breaks the instance first, as a row drop does.
                        if (PrefabUtility.NeedsBreaking(dragged))
                            PrefabUtility.BreakThenRun(dragged, () => MoveToScene(dragged, header));
                        else
                            MoveToScene(dragged, header);
                    }
                    else if (_dragHoverTarget.IsValid() && _dragHoverTargetId != null)
                    {
                        // Dropped on a GO row use normalized Y to determine Above/Into/Below
                        DropPosition dropPos;
                        if (_dragHoverNormalizedY < 0.25f)
                            dropPos = DropPosition.Above;
                        else if (_dragHoverNormalizedY > 0.75f)
                            dropPos = DropPosition.Below;
                        else
                            dropPos = DropPosition.Into;

                        // The gap under an expanded node is visually its first-child slot, so drop
                        // there as child index 0 instead of as a sibling after the node.
                        if (dropPos == DropPosition.Below && IsTargetExpanded(_dragHoverTarget, _dragHoverTargetId))
                            ProcessGODrop(goDrop, _dragHoverTarget, _dragHoverTargetId, DropPosition.Into, 0);
                        else
                            ProcessGODrop(goDrop, _dragHoverTarget, _dragHoverTargetId, dropPos);
                    }
                    else if (bgHovered)
                    {
                        // Dropped on empty background unparent to root
                        foreach (GameObject dragged in goDrop.GameObjects)
                        {
                            if (dragged.Parent.IsValid())
                                Undo.SetTransformParent(dragged, null, true, "Unparent");
                        }
                        DragDrop.EndDrag();
                    }

                    _dragHoverTarget = null;
                    _dragHoverTargetId = null;
                    _dragHoverHeader = null;
                }
            }
        }
    }

    // ================================================================
    //  Scene headers
    // ================================================================

    // One header per open scene in the edit world, per section in the play world (Persistent once it holds anything),
    // and none in the prefab world, which is one prefab.
    private void BuildSceneNodes(SceneInstance world, List<TreeNode> nodes, List<object> flatObjects)
    {
        if (PrefabEditingMode.IsEditing)
        {
            foreach (GameObject root in GetDisplayRoots(world.Roots))
                BuildNodeList(root, 0, nodes, flatObjects);
            return;
        }

        if (EditorSceneManager.PlayWorld.IsNotValid() && ReferenceEquals(world, EditorSceneManager.EditWorld))
        {
            foreach (OpenScene open in EditorSceneManager.OpenScenes)
                AddScene(_headers.GetValue(open, static o => new SceneHeader { Open = (OpenScene)o }), open.Section);
            return;
        }

        foreach (SceneSection section in world.Sections)
        {
            if (section.IsPersistent && section.Roots.Count == 0) continue;
            AddScene(_headers.GetValue(section, static s => new SceneHeader { Section = (SceneSection)s }), section);
        }

        void AddScene(SceneHeader header, SceneSection? section)
        {
            header.Section = section is { IsLoaded: true } ? section : null;
            List<GameObject> roots = header.Section != null ? GetDisplayRoots(header.Section.Roots) : [];
            TreeNode node = header.Node;
            node.Id = HeaderId(header.Open is { } open ? SceneKey(open) : SectionKey(section));
            node.OverrideExpanded = _forceExpandedIds.Contains(node.Id) ? true : null;
            node.UserData = header;
            node.Label = HeaderName(header);
            node.Depth = 0;
            node.HasChildren = roots.Count > 0;
            node.DropIndicator = DragDrop.IsDragging && ReferenceEquals(_dragHoverHeader, header) ? TreeDropPosition.Into : null;
            nodes.Add(node);
            flatObjects.Add(header);
            foreach (GameObject root in roots)
                BuildNodeList(root, 1, nodes, flatObjects);
        }
    }

    // A scene's key in node ids: its asset, else its name. The same in edit and play, so expand state carries over.
    private static string SceneKey(OpenScene open) => open.Scene.IsValid() ? open.Scene.AssetID.ToString("N") : open.Name;

    private static string SectionKey(SceneSection? section)
    {
        if (section == null) return "";
        if (section.IsPersistent) return "persistent";
        return section.Source.IsValid() ? section.Source.AssetID.ToString("N") : section.Name;
    }

    private static string HeaderId(string sceneKey) => $"hier_scene_{sceneKey}";

    // Unique per scene, so rows of duplicated scenes never share state.
    private static string NodeId(GameObject go) => $"{SectionKey(go.Section)}/{go.Identifier}";

    // The row a node shows on, counting only rows whose parents are expanded. -1 when none matches.
    private int VisibleRowOf(List<TreeNode> nodes, Func<TreeNode, bool> match)
    {
        int row = 0;
        int collapsedDepth = int.MaxValue;
        foreach (TreeNode node in nodes)
        {
            if (node.Depth > collapsedDepth) continue;
            collapsedDepth = int.MaxValue;
            if (match(node)) return row;
            row++;
            bool expanded = node.OverrideExpanded ?? (_expandState.TryGetValue(node.Id, out bool e) ? e : node.DefaultExpanded);
            if (!expanded) collapsedDepth = node.Depth;
        }
        return -1;
    }

    // A pinged object row, or the header of a scene whose asset is pinged.
    private static bool IsPingedNode(TreeNode node) => node.UserData switch
    {
        GameObject go => _pingedGameObjects.Contains(go),
        SceneHeader { Open: { } open } => IsPingedScene(open),
        _ => false,
    };

    private static bool IsPingedScene(OpenScene open) => open.Scene.IsValid() && open.Scene.AssetID == Selection.PingedGuid;

    private static string HeaderName(SceneHeader header)
    {
        if (header.Open is { } open) return open.IsDirty ? open.Name + "*" : open.Name;
        return header.Section is { IsPersistent: true } ? Loc.Get("hierarchy.persistent") : header.Section?.Name ?? "";
    }

    private static bool IsActiveHeader(SceneHeader header)
        => header.Open?.IsActive ?? header.Section is { } s && ReferenceEquals(s, s.Instance.ActiveSection);

    private void DrawSceneHeader(Paper paper, Scribe.FontFile font, SceneHeader header)
    {
        bool loaded = header.Section != null;
        bool active = IsActiveHeader(header);

        paper.Box("hier_scene_icon")
            .Width(18).Height(EditorTheme.RowHeight).IsNotInteractable()
            .Icon(paper, EditorIcons.Shapes_I, active ? EditorTheme.Amber400 : loaded ? EditorTheme.Ink400 : EditorTheme.Ink300, size: 14f);

        bool hidden = header.Open is { IsHiddenInSceneView: true };
        paper.Box("hier_scene_name")
            .Width(UnitValue.StretchOne).Height(EditorTheme.RowHeight).PaddingLeft(4)
            .Text(hidden ? $"{HeaderName(header)}  {EditorIcons.EyeSlash}" : HeaderName(header), font)
            .TextColor(loaded ? EditorTheme.Ink500 : EditorTheme.Ink300)
            .FontSize(EditorTheme.FontSizeSmall)
            .Alignment(TextAlignment.MiddleLeft);

        // The create menu on the active scene, without hunting for empty space to right click.
        if (loaded && active)
        {
            float addSize = EditorTheme.RowHeight - 2f;
            paper.Box("hier_scene_add")
                .Width(addSize).Height(addSize).Rounded(EditorTheme.Roundness)
                .Hovered.BackgroundColor(EditorTheme.Hover).End()
                .Text(EditorIcons.Plus, font)
                .TextColor(EditorTheme.Ink400)
                .Hovered.TextColor(EditorTheme.Ink500).End()
                .FontSize(addSize).Alignment(TextAlignment.MiddleCenter)
                .Tooltip(Loc.Get("hierarchy.create"))
                .StopEventPropagation()
                .OnClick(0, (_, _) => Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, b =>
                {
                    b.Header(Loc.Get("hierarchy.create"));
                    BuildCreateMenuForSelection(b);
                }));
        }

        if (header.Open is { } open && EditorSceneManager.CanEditScenes)
            BuildSceneHeaderMenu(paper, open);
    }

    private static void BuildSceneHeaderMenu(Paper paper, OpenScene open)
    {
        Origami.RightClickMenu(paper, "hier_scene_ctx", builder =>
        {
            builder.Title(open.Name, iconDraw: EditorIcons.Shapes_I);
            if (open.IsLoaded && !open.IsActive)
                builder.Item(Loc.Get("hierarchy.set_active_scene"), () => Guarded(() => EditorSceneManager.SetActive(open)));
            if (open.IsLoaded)
            {
                builder.Item(Loc.Get("hierarchy.save_scene"), () => { if (!EditorSceneManager.Save(open)) EditorApplication.PromptSaveAs(open); });
                builder.Item(Loc.Get("hierarchy.save_scene_as"), () => EditorApplication.PromptSaveAs(open));
            }
            builder.Item(Loc.Get("hierarchy.save_all"), () => Projects.SaveManager.RequestSave());
            builder.Separator();
            if (open.IsLoaded)
                builder.Item(Loc.Get("hierarchy.unload_scene"), () => EditorSceneManager.ConfirmClosing(() => Guarded(() => EditorSceneManager.Unload(open, force: true)), [open]));
            else
                builder.Item(Loc.Get("hierarchy.load_scene"), () => Guarded(() => EditorSceneManager.Load(open)));
            builder.Item(Loc.Get("hierarchy.remove_scene"), () => EditorSceneManager.ConfirmClosing(() => Guarded(() => EditorSceneManager.Remove(open, force: true)), [open]));
            builder.Separator();
            if (open.Scene.IsValid())
            {
                Guid asset = open.Scene.AssetID;
                builder.Item(Loc.Get("hierarchy.select_scene_asset"), () => Selection.Ping(asset));
            }
            builder.Item(Loc.Get(open.IsHiddenInSceneView ? "hierarchy.show_in_scene_view" : "hierarchy.hide_in_scene_view"), () =>
            {
                open.IsHiddenInSceneView = !open.IsHiddenInSceneView;
                EditorSceneManager.RememberSetup();
            });
        });
    }

    // A refusal from a menu, like unloading the last loaded scene, is told rather than thrown into the frame.
    private static void Guarded(Action action)
    {
        try { action(); }
        catch (InvalidOperationException e) { Toasts.Warning(Loc.Get("hierarchy.scene_refused"), e.Message); }
    }

    // Dragged roots onto a scene header: each moves into that scene as one undo step with its unparent.
    private static void MoveToScene(List<GameObject> dragged, SceneHeader header)
    {
        if (header.Section == null) return;
        foreach (GameObject go in dragged)
        {
            if (go.IsNotValid() || ReferenceEquals(go.Section, header.Section)) continue;
            if (go.Parent.IsValid()) Undo.SetTransformParent(go, null, true, "Move To Scene");
            if (header.Open != null) Undo.MoveGameObjectToScene(go, header.Open, "Move To Scene");
            else go.MoveTo(header.Section);
        }
    }

    // ================================================================
    //  Where new objects go
    // ================================================================

    /// <summary>
    /// The section a new object goes into: its parent's, else the selected object's scene, else the active scene.
    /// </summary>
    internal static SceneSection SectionForNew(SceneInstance world, GameObject? parent)
    {
        if (parent.IsValid() && parent.Section is { } parentSection && ReferenceEquals(parentSection.Instance, world)) return parentSection;
        GameObject? selected = Selection.GetSelected<GameObject>().FirstOrDefault(go => go.IsValid() && ReferenceEquals(go.Scene, world));
        if (selected.IsValid() && selected.Section is { IsPersistent: false } selectedSection) return selectedSection;
        return world.ActiveSection;
    }

    private void DrawToolbar(Paper paper, Scribe.FontFile font, float width)
    {
        using (paper.Row("hier_toolbar")
            .Height(ToolbarHeight)
            .Margin(4, 4, 4, 0)
            .Enter())
        {
            using (paper.Row("hier_search_wrap")
                .Width(UnitValue.StretchOne).Height(25)
                .Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne)
                .Enter())
                Origami.SearchField(paper, "hier_search", _searchText, v => _searchText = v)
                    .Width(UnitValue.StretchOne).Height(25).Show();
        }
    }

    private void BuildNodeList(GameObject go, int depth, List<TreeNode> nodes, List<object> flatObjects)
    {
        if (go.HideFlags.HasFlag(HideFlags.Hide) || go.HideFlags.HasFlag(HideFlags.HideAndDontSave))
            return;

        if (!EditorUtils.MatchesSearch(go.Name, _searchText)
            && !go.GetChildrenDeep().Any(c => EditorUtils.MatchesSearch(c.Name, _searchText)))
            return;

        TreeNode node = _nodeCache.GetValue(go, static g => new TreeNode { UserData = g });
        node.Id = NodeId(go);
        string goId = node.Id;
        bool hasVisibleChildren = go.Children.Count > 0
            && go.Children.Any(c => !c.HideFlags.HasFlag(HideFlags.Hide) && !c.HideFlags.HasFlag(HideFlags.HideAndDontSave));

        // Determine drop indicator for this node
        TreeDropPosition? dropInd = null;
        if (DragDrop.IsDragging && _dragHoverTargetId == goId)
        {
            if (_dragHoverNormalizedY < 0.25f) dropInd = TreeDropPosition.Above;
            else if (_dragHoverNormalizedY > 0.75f) dropInd = IsTargetExpanded(go, goId) ? TreeDropPosition.IntoFirst : TreeDropPosition.Below;
            else dropInd = TreeDropPosition.Into;
        }

        node.Label = go.Name;
        node.HasChildren = hasVisibleChildren;
        node.Depth = depth;
        node.TrailingIcon = go.Enabled ? EditorIcons.Eye : EditorIcons.EyeSlash;
        node.TrailingIconColor = go.Enabled ? EditorTheme.Ink400 : EditorTheme.Ink300;
        node.DropIndicator = dropInd;

        // Force expand parents of pinged nodes
        node.OverrideExpanded = _forceExpandedIds.Contains(goId) ? true : null;

        nodes.Add(node);
        flatObjects.Add(go);

        // Recurse children (the tree widget handles skipping collapsed children internally)
        foreach (GameObject child in go.Children)
            BuildNodeList(child, depth + 1, nodes, flatObjects);
    }

    // ================================================================
    //  Drag & Drop for reparenting/reordering
    // ================================================================

    // True when the target node is expanded and actually has children (its first-child slot is visible).
    private bool IsTargetExpanded(GameObject target, string targetId)
        => target.Children.Count > 0 && _expandState.TryGetValue(targetId, out bool e) && e;

    private void ProcessGODrop(GameObjectDragPayload goDrop, GameObject target, string targetId, DropPosition dropPos, int insertIndex = -1)
    {
        // Captured into a list first: the drag is over by the time the answer comes back.
        List<GameObject> dragged = [.. ExcludeNestedSelections(goDrop.GameObjects)];
        if (PrefabUtility.NeedsBreaking(dragged))
        {
            DragDrop.EndDrag();
            PrefabUtility.BreakThenRun(dragged,
                () => ProcessGODropCore(dragged, target, targetId, dropPos, insertIndex));
            return;
        }

        ProcessGODropCore(dragged, target, targetId, dropPos, insertIndex);
    }

    private void ProcessGODropCore(List<GameObject> draggedObjects, GameObject target, string targetId, DropPosition dropPos, int insertIndex)
    {
        GameObject? targetParent = target.Parent;
        bool targetIsRoot = targetParent.IsNotValid();

        // A descendant dragged alongside its own ancestor moves implicitly with it; reparenting it
        // again here would yank it out from under the ancestor and flatten it as a sibling instead.
        foreach (GameObject dragged in draggedObjects)
        {
            if (dragged.IsNotValid() || dragged == target || IsDescendantOf(target, dragged))
                continue;

            switch (dropPos)
            {
                case DropPosition.Into:
                    // A prefab instance can be given children of its own; they belong to the instance
                    // and survive a refresh.
                    if (dragged.Parent != target)
                        Undo.SetTransformParent(dragged, target, true, "Reparent");
                    if (insertIndex >= 0)
                        Undo.SetSiblingIndex(dragged, insertIndex, "Reparent");
                    break;

                case DropPosition.Above:
                case DropPosition.Below:
                    if (targetIsRoot)
                    {
                        SceneInstance scene = EditorSceneManager.EditedWorld;
                        if (scene.IsNotValid()) break;

                        // Unparent if needed, then into the target's scene
                        if (dragged.Parent.IsValid())
                            Undo.SetTransformParent(dragged, null, true, "Reparent");
                        if (EditorSceneManager.SceneOf(target) is { } targetScene && !ReferenceEquals(EditorSceneManager.SceneOf(dragged), targetScene))
                            Undo.MoveGameObjectToScene(dragged, targetScene, "Reparent");
                        // In play the sections are copies with no open scene of their own, so the root moves straight over.
                        else if (target.Section is { } targetSection && !ReferenceEquals(dragged.Section, targetSection))
                            dragged.MoveTo(targetSection);

                        // Root reorder via Scene list
                        int targetRootIdx = target.SiblingIndex;
                        if (dropPos == DropPosition.Below) targetRootIdx++;
                        int dragRootIdx = dragged.SiblingIndex;
                        if (dragRootIdx >= 0 && dragRootIdx < targetRootIdx) targetRootIdx--;
                        Undo.SetSiblingIndex(dragged, Math.Max(0, targetRootIdx), "Reparent");
                    }
                    else
                    {
                        // Child reorder reparent to target's parent, then set sibling index
                        if (dragged.Parent != targetParent)
                            Undo.SetTransformParent(dragged, targetParent, true, "Reparent");

                        int targetIdx = target.GetSiblingIndex() ?? 0;
                        if (dropPos == DropPosition.Below) targetIdx++;
                        int dragIdx = dragged.GetSiblingIndex() ?? 0;
                        if (dragIdx < targetIdx) targetIdx--;
                        Undo.SetSiblingIndex(dragged, Math.Max(0, targetIdx), "Reparent");
                    }
                    break;
            }
        }

        DragDrop.EndDrag();
    }

    private static bool IsDescendantOf(GameObject potentialChild, GameObject potentialParent)
    {
        GameObject? current = potentialChild.Parent;
        while (current.IsValid())
        {
            if (current == potentialParent) return true;
            current = current.Parent;
        }
        return false;
    }

    /// <summary>
    /// Filters out any GameObject whose ancestor is also present in the same collection. A
    /// per-item operation (delete, reparent) applied to an ancestor already cascades to its
    /// descendants structurally, so re-applying it to a separately-selected descendant corrupts
    /// undo (double-serializes it) or flattens it out of its parent during a reparent.
    /// </summary>
    internal static List<GameObject> ExcludeNestedSelections(IReadOnlyCollection<GameObject> selection)
    {
        var result = new List<GameObject>(selection.Count);
        foreach (GameObject go in selection)
        {
            bool hasSelectedAncestor = false;
            foreach (GameObject other in selection)
            {
                if (!ReferenceEquals(other, go) && IsDescendantOf(go, other))
                {
                    hasSelectedAncestor = true;
                    break;
                }
            }
            if (!hasSelectedAncestor)
                result.Add(go);
        }
        return result;
    }

    // ================================================================
    //  Context Menus
    // ================================================================

    private void BuildBackgroundContextMenu(Paper paper)
    {
        Origami.RightClickMenu(paper, "hier_bg_ctx", builder =>
        {
            builder.Header(Loc.Get("hierarchy.create"));
            BuildCreateMenuFor(builder, (GameObject?)null);
        });
    }

    private void BuildGameObjectContextMenu(Paper paper, string id)
    {
        Origami.RightClickMenu(paper, id, builder =>
        {
            var selectedGOs = Selection.GetSelected<GameObject>().ToList();
            GameObject? firstSelected = selectedGOs.FirstOrDefault();
            if (selectedGOs.Count == 0) return;

            bool multiSelect = selectedGOs.Count > 1;

            builder.Title(multiSelect ? Loc.Get("project.item_count", new { count = Selection.Count }) : firstSelected.Name, iconDraw: GetGoStyle(firstSelected).icon);

            builder.Submenu(Loc.Get("hierarchy.create"), (b) => { BuildCreateMenuFor(b, selectedGOs); }, EditorIcons.Plus);

            builder.Separator();

            if (multiSelect)
            {
                builder.Item($"{Loc.Get("hierarchy.duplicate")} ({selectedGOs.Count})", () =>
                {
                    List<GameObject> dupes = GameObjectClipboard.Duplicate(selectedGOs);
                    foreach (GameObject d in dupes) Undo.RegisterCreatedObjectUndo(d, "Duplicate");
                }, icon: EditorIcons.Copy);

                builder.Item($"{Loc.Get("hierarchy.rename")} ({selectedGOs.Count})", () =>
                {
                    StartRenameGO(firstSelected!, selectedGOs);
                }, icon: EditorIcons.PenToSquare);

                builder.Item($"{Loc.Get("hierarchy.delete")} ({selectedGOs.Count})", () =>
                {
                    DeleteGameObjects([.. ExcludeNestedSelections(selectedGOs)]);
                }, icon: EditorIcons.Trash);

                builder.Separator();

                bool anyEnabled = selectedGOs.Any(g => g.Enabled);
                builder.Item(anyEnabled ? Loc.Get("hierarchy.disable_all") : Loc.Get("hierarchy.enable_all"), () =>
                {
                    bool newState = !anyEnabled;
                    Undo.RecordObjects(selectedGOs, newState ? "Enable All" : "Disable All");
                    foreach (GameObject? go in selectedGOs) go.Enabled = newState;
                }, icon: anyEnabled ? EditorIcons.EyeSlash : EditorIcons.Eye);
            }
            else
            {
                GameObject go = firstSelected!;
                builder.Item(Loc.Get("hierarchy.duplicate"), () =>
                {
                    List<GameObject> dupes = GameObjectClipboard.Duplicate([go]);
                    foreach (GameObject d in dupes) Undo.RegisterCreatedObjectUndo(d, "Duplicate");
                }, icon: EditorIcons.Copy);
                builder.Item(Loc.Get("hierarchy.rename"), () =>
                {
                    StartRenameGO(go, [go]);
                }, icon: EditorIcons.PenToSquare);
                builder.Item(Loc.Get("hierarchy.delete"), () => DeleteGameObject(go), icon: EditorIcons.Trash);
                builder.Separator();
                builder.Item(go.Enabled ? Loc.Get("hierarchy.disable") : Loc.Get("hierarchy.enable"), () =>
                {
                    Undo.RecordObject(go, "Toggle Visibility");
                    go.Enabled = !go.Enabled;
                }, icon: go.Enabled ? EditorIcons.EyeSlash : EditorIcons.Eye);
            }

            builder.Separator();

            // Move to View / Align With View / Move View To
            EditorCamera? cam = SceneViewPanel.ActiveCamera;
            if (cam != null)
            {
                // Pose writes go to the top-level selection only: a GameObject whose ancestor is
                // also selected already travels with that ancestor, so writing its world pose
                // separately would fight the parent's write (order-dependent) and bloat the undo step.
                List<GameObject> poseTargets = ExcludeNestedSelections(selectedGOs);

                // Position only, at the centre of the view - rotation is deliberately untouched.
                builder.Item(Loc.Get("hierarchy.move_to_view"), () =>
                {
                    Undo.RecordObjects(poseTargets, "Move to View");
                    foreach (GameObject g in poseTargets) g.Transform.Position = cam.ViewFocusPoint;
                }, icon: EditorIcons.ArrowRight);

                // Position *and* rotation, so the object looks exactly where the view looks.
                builder.Item(Loc.Get("hierarchy.align_with_view"), () =>
                {
                    Undo.RecordObjects(poseTargets, "Align With View");
                    foreach (GameObject g in poseTargets) g.Transform.SetPositionAndRotation(cam.Position, cam.Rotation);
                }, icon: EditorIcons.ArrowsToEye);

                builder.Item(Loc.Get("hierarchy.move_view_to"), () =>
                {
                    cam.SetPosition(firstSelected!.Transform.Position);
                    cam.SetOrientation((float)firstSelected!.Transform.LocalEulerAngles.Y, (float)firstSelected!.Transform.LocalEulerAngles.X);
                }, icon: EditorIcons.Eye);

                builder.Separator();
            }

            // Creating a prefab from what is selected, rather than only by dragging into the project
            // panel, which nothing advertises.
            builder.Item(Loc.Get("hierarchy.create_prefab"), () => CreatePrefabsFrom(selectedGOs),
                icon: EditorIcons.Cubes);

            // Prefab operations, over every selected instance rather than only the first.
            List<GameObject> prefabRoots = PrefabInstanceRootsOf(selectedGOs);
            if (prefabRoots.Count > 0)
            {
                string suffix = prefabRoots.Count > 1 ? $" ({prefabRoots.Count})" : "";

                if (prefabRoots.Count == 1)
                {
                    Guid assetId = prefabRoots[0].PrefabAssetId;

                    // Straight into the prefab, rather than pinging the asset and leaving the user to
                    // find it in the project panel and double click it.
                    builder.Item(Loc.Get("hierarchy.open_prefab"), () => PrefabEditingMode.Enter(assetId),
                        icon: EditorIcons.PenToSquare, enabled: PrefabUtility.IsEditablePrefab(assetId));

                    builder.Item(Loc.Get("hierarchy.select_prefab_asset"),
                        () => Selection.Ping(assetId), icon: EditorIcons.Cubes);
                }

                bool anyOverrides = prefabRoots.Any(PrefabUtility.HasAnyOverrides);
                // A generated prefab (a model) is rebuilt from its source on every import, so there
                // is nothing to apply to. Reverting still works.
                bool anyApplyable = prefabRoots.Any(r =>
                    PrefabUtility.HasAnyOverrides(r) && PrefabUtility.IsEditablePrefab(r.PrefabAssetId));

                builder.Item(Loc.Get("hierarchy.apply_prefab_overrides") + suffix, () =>
                {
                    foreach (GameObject root in prefabRoots)
                        if (PrefabUtility.IsEditablePrefab(root.PrefabAssetId))
                            PrefabUtility.ApplyOverrides(root);
                }, enabled: anyApplyable, icon: EditorIcons.Check);

                builder.Item(Loc.Get("hierarchy.revert_to_prefab") + suffix, () =>
                {
                    foreach (GameObject root in prefabRoots) PrefabUtility.RevertOverrides(root);
                }, enabled: anyOverrides, icon: EditorIcons.ArrowsRotate);

                builder.Item(Loc.Get("hierarchy.break_prefab_instance") + suffix, () =>
                {
                    foreach (GameObject root in prefabRoots) PrefabUtility.UnpackPrefabInstance(root);
                }, icon: EditorIcons.LinkSlash);

                builder.Separator();
            }

            if (multiSelect)
            {
                builder.Item($"{Loc.Get("hierarchy.delete")} ({selectedGOs.Count})", () =>
                {
                    DeleteGameObjects([.. ExcludeNestedSelections(selectedGOs)]);
                }, icon: EditorIcons.Trash, danger: true);
            }
            else
            {
                GameObject go = firstSelected!;
                builder.Item(Loc.Get("hierarchy.delete"), () => DeleteGameObject(go), icon: EditorIcons.Trash, danger: true);
            }

        });
    }

    // ================================================================
    //  Create Menu
    // ================================================================

    /// <summary>Create menu for a menu that knows what it was opened over: <paramref name="parent"/>
    /// for a GameObject row, null for empty space (meaning the scene root).</summary>
    private static void BuildCreateMenuFor(ContextBuilder builder, GameObject? parent) =>
        BuildCreateMenuFor(builder, parent.IsValid() ? [parent] : []);

    private static void BuildCreateMenuFor(ContextBuilder builder, IReadOnlyList<GameObject> parents)
    {
        MenuContext.SetMany(parents);
        MenuItemAttribute.BuildContextMenu(builder, "GameObject");
    }

    /// <summary>Create menu with no object of its own - new objects land under the active selection,
    /// matching the main menu bar's GameObject menu.</summary>
    private static void BuildCreateMenuForSelection(ContextBuilder builder)
    {
        MenuContext.Clear();
        MenuItemAttribute.BuildContextMenu(builder, "GameObject");
    }

    /// <summary>
    /// Creates a GameObject in the current scene and registers it for undo.
    /// </summary>
    /// <param name="select">When true, the new object becomes the current selection.</param>
    /// <param name="beginRename">When true, the inline rename overlay opens on the new object.
    /// Pass false for objects created as a side effect (e.g. an auto-generated parent Canvas)
    /// so they don't steal selection / rename focus from the object the user actually asked for.</param>
    internal static GameObject CreateGameObject(string name, GameObject? parent, bool select = true, bool beginRename = true)
    {
        SceneInstance scene = EditorSceneManager.EditedWorld;
        if (scene.IsNotValid()) return new GameObject(name);

        var go = new GameObject(name);
        scene.Add(go, SectionForNew(scene, parent));
        if (parent.IsValid())
            go.SetParent(parent);
        if (select)
            Selection.Select(go);

        Undo.RegisterCreatedObjectUndo(go, "Create GameObject");

        if (beginRename)
            BeginRenameNewGameObject(go);

        return go;
    }

    /// <summary>Ping a freshly created GameObject and open the inline rename overlay on it, so the
    /// user can type the name straight away.</summary>
    private static void BeginRenameNewGameObject(GameObject go)
    {
        Selection.FastPing(go.Identifier);
        // Enter rename via global overlay
        string goIdStr = NodeId(go);
        RenameOverlay.Begin(goIdStr, go.Name, newName =>
        {
            string oldName = go.Name;
            if (newName == oldName) return;
            Undo.RecordObject(go, "Rename");
            go.Name = newName;
        });
    }

    /// <summary>
    /// Wraps the current selection in a new empty GameObject, as Create Empty Parent. The new
    /// parent takes over the first selected object's slot - same parent, same sibling index - and
    /// sits at the centre of the selection so the group's pivot lands among the objects rather than
    /// at the world origin. Children keep their world transforms.
    /// </summary>
    internal static void CreateEmptyParent()
    {
        SceneInstance scene = EditorSceneManager.EditedWorld;
        if (scene.IsNotValid()) return;

        // Only the top-level selection moves: an object whose ancestor is also selected already
        // travels with that ancestor, and reparenting it too would flatten it out of its own parent.
        List<GameObject> targets = ExcludeNestedSelections([.. Selection.GetSelected<GameObject>()]);
        if (targets.Count == 0) return;

        if (PrefabUtility.NeedsBreaking(targets))
        {
            PrefabUtility.BreakThenRun(targets, () => CreateEmptyParentCore(targets));
            return;
        }

        CreateEmptyParentCore(targets);
    }

    private static void CreateEmptyParentCore(List<GameObject> targets)
    {
        SceneInstance scene = EditorSceneManager.EditedWorld;
        if (scene.IsNotValid()) return;

        // The first selected object anchors the group: the new parent drops into its place in the
        // hierarchy, so the wrapped objects stay where they were in the tree.
        GameObject anchor = targets[0];
        GameObject? anchorParent = anchor.Parent.IsValid() ? anchor.Parent : null;
        int anchorIndex = anchorParent.IsValid() ? (anchor.GetSiblingIndex() ?? -1) : anchor.SiblingIndex;

        Float3 centre = Float3.Zero;
        foreach (GameObject target in targets)
            centre += target.Transform.Position;
        centre /= targets.Count;

        var newParent = new GameObject("GameObject");
        scene.Add(newParent, anchorParent.IsValid() ? SectionForNew(scene, anchorParent) : anchor.Section ?? scene.ActiveSection);
        if (anchorParent.IsValid())
        {
            newParent.SetParent(anchorParent);
            if (anchorIndex >= 0) newParent.SetSiblingIndex(anchorIndex);
        }
        else if (anchorIndex >= 0)
        {
            newParent.SiblingIndex = anchorIndex;
        }
        newParent.Transform.Position = centre;

        Undo.RegisterCreatedObjectUndo(newParent, "Create Empty Parent");
        foreach (GameObject target in targets)
            Undo.SetTransformParent(target, newParent, true, "Create Empty Parent");

        Selection.Select(newParent);
        BeginRenameNewGameObject(newParent);
    }

    /// <summary>
    /// The distinct prefab instances a selection touches. Selecting several objects inside one
    /// instance, or an instance and its own child, is one instance to act on, not several.
    /// </summary>
    private static List<GameObject> PrefabInstanceRootsOf(IEnumerable<GameObject> gameObjects)
    {
        var roots = new List<GameObject>();
        foreach (GameObject go in gameObjects)
        {
            if (go.IsNotValid() || !go.IsPrefabInstance) continue;

            GameObject? instanceRoot = PrefabUtility.GetPrefabInstanceRoot(go);
            GameObject target = instanceRoot.IsValid() ? instanceRoot! : go;
            if (!roots.Any(r => ReferenceEquals(r, target)))
                roots.Add(target);
        }
        return roots;
    }

    /// <summary>Save each selected hierarchy as a prefab, into the folder the project panel is on.</summary>
    private static void CreatePrefabsFrom(IEnumerable<GameObject> gameObjects)
    {
        string folder = AssetCreateMenu.GetCurrentFolder();

        // Roots only: a prefab of a parent already contains its children, and making one of a child
        // afterwards would tear that subtree back out of the parent's new instance.
        foreach (GameObject go in GameObjectClipboard.FilterToRoots(gameObjects))
            AssetCreateMenu.CreatePrefabIn(go, folder);
    }

    private void StartRenameGO(GameObject primary, IEnumerable<GameObject> allTargets)
    {
        var targets = allTargets.ToList();
        string goId = NodeId(primary);
        RenameOverlay.Begin(goId, primary.Name, newName =>
        {
            if (targets.All(g => g.Name == newName)) return;
            Undo.RecordObjects(targets, "Rename");
            foreach (GameObject? go in targets)
                go.Name = newName;
        });
    }

    /// <summary>Delete a GameObject, blocking deletion of prefab-structural children (with a toast)
    /// and registering proper undo. Shared with SceneViewPanel's in-viewport Delete shortcut so both
    /// entry points enforce the same rules instead of the viewport bypassing them.</summary>
    internal static void DeleteGameObject(GameObject go) => DeleteGameObjects([go]);

    /// <summary>
    /// Delete a set of GameObjects as one action, asking once for the whole set when any of them is
    /// structure a prefab provides.
    /// </summary>
    internal static void DeleteGameObjects(IReadOnlyList<GameObject> gameObjects)
    {
        if (PrefabUtility.NeedsBreaking(gameObjects))
        {
            PrefabUtility.BreakThenRun(gameObjects, () => DeleteGameObjectsCore(gameObjects));
            return;
        }

        DeleteGameObjectsCore(gameObjects);
    }

    private static void DeleteGameObjectsCore(IReadOnlyList<GameObject> gameObjects)
    {
        foreach (GameObject go in gameObjects)
            if (go.IsValid())
                DeleteOneGameObject(go);
    }

    internal static void DeleteOneGameObject(GameObject go)
    {
        SceneInstance scene = EditorSceneManager.EditedWorld;
        if (scene.IsNotValid()) return;

        Undo.DestroyObjectImmediate(go);
    }

    // ================================================================
    //  Helpers
    // ================================================================

    private static List<GameObject> GetDisplayRoots(IEnumerable<GameObject> roots)
    {
        return [.. roots
            .Where(go => !go.HideFlags.HasFlag(HideFlags.Hide)
                      && !go.HideFlags.HasFlag(HideFlags.HideAndDontSave))];
    }

    private static Color GetPrefabTextColor(GameObject go)
    {
        if (!go.IsPrefabInstance)
            return go.EnabledInHierarchy ? EditorTheme.Ink500 : EditorTheme.Ink300;

        // Check if the prefab asset still exists
        AssetEntry? entry = EditorAssetBackend.Instance?.GetEntry(go.PrefabAssetId);
        if (entry == null)
        {
            // Broken prefab link red text
            return go.EnabledInHierarchy ? EditorTheme.Red400 : EditorTheme.Red300;
        }

        // Valid prefab purple text
        return go.EnabledInHierarchy ? EditorTheme.Purple400 : EditorTheme.Purple300;
    }

    // Vector icon + accent colour chosen from the GameObject's defining (first) component.
    private static (IOrigamiIcon icon, Color color) GetGoStyle(GameObject go)
    {
        Component? first = go.GetComponents().FirstOrDefault();
        if (first is Camera)               return (EditorIcons.Camera_I, EditorTheme.Blue400);    // blue
        if (first is Light)                return (EditorIcons.Lightbulb_I, EditorTheme.Amber400);    // amber
        if (first is SkinnedMeshRenderer)  return (EditorIcons.Cubes_I, EditorTheme.Purple400);    // purple
        if (first is MeshRenderer)         return (EditorIcons.Cube_I, EditorTheme.Purple400);    // purple
        if (first.IsValid())                 return (EditorIcons.FileCode_I, EditorTheme.Green400);   // any other component = green script
        // Empty GameObject: a group icon when it parents others, else a dim generic mark.
        return go.Children.Count > 0
            ? (EditorIcons.ObjectGroup_I, EditorTheme.Ink300)
            : (EditorIcons.Cube_I, EditorTheme.InkDim);
    }

    /// <summary>
    /// Search all GameObjects in the scene for any that reference the given GUID.
    /// Checks each component's AssetID and all AssetRef fields.
    /// </summary>
    private static void FindGameObjectsWithGuid(SceneInstance scene, Guid guid, HashSet<GameObject> results)
    {
        foreach (GameObject go in scene.Objects)
        {
            // Direct GO match (e.g. scene-view click -> Ping(go.Identifier))
            if (go.Identifier == guid)
            {
                results.Add(go);
                continue;
            }

            foreach (Component comp in go.GetComponents<Component>())
            {
                // A field holding the asset, or naming it through an AssetRef.
                bool found = false;
                foreach (FieldInfo field in comp.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
                {
                    Guid referenced = field.GetValue(comp) switch
                    {
                        Asset asset => asset.AssetID,
                        IAssetRef assetRef => assetRef.AssetID,
                        _ => Guid.Empty,
                    };
                    if (referenced != guid) continue;

                    results.Add(go);
                    found = true;
                    break;
                }
                if (found) break;
            }
        }
    }

    // ================================================================
    //  Asset Drop -> Spawn in Scene
    // ================================================================

    public static void SpawnAssetInScene(AssetDragPayload payload, GameObject? parent, Float3 position)
    {
        SceneInstance scene = EditorSceneManager.EditedWorld;
        if (scene.IsNotValid()) return;

        Asset? asset = Runtime.AssetDatabase.Get(payload.AssetGuid);
        if (asset.IsNotValid()) return;

        string name = System.IO.Path.GetFileNameWithoutExtension(payload.AssetName);

        if (asset is Mesh mesh)
        {
            var go = new GameObject(name);
            go.Transform.Position = position;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.Mesh = mesh;
            renderer.Material = new Material(Shader.LoadDefault(DefaultShader.Lit));
            scene.Add(go, SectionForNew(scene, parent));
            if (parent.IsValid()) go.SetParent(parent);
            Selection.Select(go);
            Undo.RegisterCreatedObjectUndo(go, "Spawn Mesh");
        }
        else if (asset is Sprite sprite)
        {
            var go = new GameObject(string.IsNullOrEmpty(sprite.Name) ? name : sprite.Name);
            go.Transform.Position = position;
            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.Sprite = sprite;
            scene.Add(go, SectionForNew(scene, parent));
            if (parent.IsValid()) go.SetParent(parent);
            Selection.Select(go);
            Undo.RegisterCreatedObjectUndo(go, "Spawn Sprite");
        }
        else if (asset is PrefabAsset)
        {
            // A prefab cannot contain another prefab, so inside a session this can only mean "add these
            // objects to what I am editing".
            if (PrefabEditingMode.IsEditing)
            {
                Origami.Confirm(
                    Loc.Get("dialog.flatten_prefab"),
                    Loc.Get("dialog.flatten_prefab_body", new { name }),
                    onYes: () => SpawnPrefab(payload, parent, position, scene, flatten: true));
                return;
            }

            SpawnPrefab(payload, parent, position, scene, flatten: false);
        }
        else
        {
            Runtime.Debug.LogWarning($"Cannot spawn asset of type {asset.GetType().Name} in scene.");
        }
    }

    /// <summary>Put a prefab into the scene, as an instance or as a copy of its contents with no link.</summary>
    private static void SpawnPrefab(AssetDragPayload payload, GameObject? parent, Float3 position,
        Runtime.Resources.SceneInstance scene, bool flatten)
    {
        GameObject? instance = PrefabUtility.InstantiatePrefab(payload.AssetGuid);
        if (instance.IsNotValid()) return;

        if (flatten) PrefabUtility.DropPrefabLink(instance);

        instance.Transform.Position = position;
        scene.Add(instance, SectionForNew(scene, parent));
        if (parent.IsValid()) instance.SetParent(parent);
        Selection.Select(instance);
        Undo.RegisterCreatedObjectUndo(instance, flatten ? "Add Prefab Contents" : "Spawn Prefab");
    }
}
