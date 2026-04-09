using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Editor.Docking;
using Prowl.Editor.Widgets;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.ParticleSystem;
using Prowl.Runtime.ParticleSystem.Modules;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.Editor.Panels;

[EditorWindow("General/Hierarchy")]
public class HierarchyPanel : DockPanel
{
    public override string Title => "Hierarchy";
    public override string Icon => EditorIcons.Sitemap;

    private string _searchText = "";
    private Paper? _paper;
    // Rename state is managed by RenameOverlay

    private const float ToolbarHeight = 30f;
    private const float IndentSize = 16f;

    // Drag-drop state
    private bool _assetDropTarget;
    private enum DropPosition { Into, Above, Below }
    private GameObject? _dragHoverTarget;
    private string? _dragHoverTargetId;
    private float _dragHoverNormalizedY;

    // Root display order — Scene uses HashSet so we track ordering ourselves
    private readonly List<string> _rootDisplayOrder = new();

    // Track expanded state per GO identifier
    private static readonly Dictionary<string, bool> _expandedState = new();

    public override void OnGUI(Paper paper, float width, float height)
    {
        _paper = paper;
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        var scene = Scene.Current;

        using (paper.Column("hier_root").Size(width, height).OnClick(0, (_, _) => Selection.Clear()).OnRightClick(0, (_, _) => Selection.Clear()).Enter())
        {
            // Prefab editing breadcrumb
            if (Prefabs.PrefabEditingMode.IsEditing)
            {
                using (paper.Row("hier_prefab_breadcrumb")
                    .Height(24)
                    .BackgroundColor(Color.FromArgb(40, EditorTheme.Purple400))
                    .Rounded(3).Margin(4, 4, 4, 0)
                    .ChildLeft(6).RowBetween(4)
                    .Enter())
                {
                    paper.Box("hier_prefab_back")
                        .Width(UnitValue.Auto).Height(24)
                        .Text($"{EditorIcons.ArrowLeft}  Back", font)
                        .TextColor(EditorTheme.Purple400)
                        .FontSize(EditorTheme.FontSize - 1).Alignment(TextAlignment.MiddleLeft)
                        .Hovered.TextColor(EditorTheme.Ink500).End()
                        .OnClick(0, (_, _) => Prefabs.PrefabEditingMode.Exit());

                    paper.Box("hier_prefab_sep_arrow")
                        .Width(UnitValue.Auto).Height(24)
                        .Text(EditorIcons.ChevronRight, font)
                        .TextColor(EditorTheme.Ink400)
                        .FontSize(8f).Alignment(TextAlignment.MiddleCenter);

                    string sceneName = Prefabs.PrefabEditingMode.OriginalSceneName ?? "Scene";
                    paper.Box("hier_prefab_scene")
                        .Width(UnitValue.Auto).Height(24)
                        .Text(sceneName, font)
                        .TextColor(EditorTheme.Ink400)
                        .FontSize(EditorTheme.FontSize - 2).Alignment(TextAlignment.MiddleLeft);

                    paper.Box("hier_prefab_sep_arrow2")
                        .Width(UnitValue.Auto).Height(24)
                        .Text(EditorIcons.ChevronRight, font)
                        .TextColor(EditorTheme.Ink400)
                        .FontSize(8f).Alignment(TextAlignment.MiddleCenter);

                    string prefabName = System.IO.Path.GetFileNameWithoutExtension(
                        Prefabs.PrefabEditingMode.EditingPrefabPath ?? "Prefab");
                    paper.Box("hier_prefab_name")
                        .Width(UnitValue.Auto).Height(24)
                        .Text(prefabName, font)
                        .TextColor(EditorTheme.Purple400)
                        .FontSize(EditorTheme.FontSize - 1).Alignment(TextAlignment.MiddleLeft);

                    paper.Box("hier_prefab_spacer");

                    EditorGUI.Button(paper, "hier_prefab_save_exit", $"{EditorIcons.FloppyDisk}  Save & Exit", width: 100)
                        .OnValueChanged(_ => Prefabs.PrefabEditingMode.SaveAndExit());
                }
            }

            // Toolbar
            DrawToolbar(paper, font, width);

            if (scene == null)
            {
                paper.Box("hier_empty")
                    .Height(60)
                    .Text("No Scene Loaded", font)
                    .TextColor(EditorTheme.Ink300)
                    .FontSize(EditorTheme.FontSize)
                    .Alignment(TextAlignment.MiddleCenter);

                EditorGUI.Button(paper, "hier_create_scene", $"{EditorIcons.Plus}  New Scene", width: 120)
                    .OnValueChanged(_ => SceneViewPanel.CreateAndLoadDefaultScene());
                return;
            }

            // Scene name header
            using (paper.Box("hier_scene_name")
                .Height(EditorTheme.RowHeight)
                .Margin(6, 6, 0, 6)
                .Rounded(4)
                .BackgroundColor(EditorTheme.Neutral200).Enter())
            {
                paper.Box("hier_scene_name_text")
                    .Margin(8, 0)
                    .Height(EditorTheme.RowHeight)
                    .Text($"{EditorIcons.Film}  {scene.Name}", font)
                    .TextColor(EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSize - 1)
                    .Alignment(TextAlignment.MiddleLeft);
            }

            using (paper.Box("hier_bg").Enter())
            {
                // Background right-click — create menu only
                BuildBackgroundContextMenu(paper);

                // Track if the background (hier_bg) is hovered for drop-on-empty-space
                bool bgHovered = paper.IsParentHovered;

                // Hierarchy keyboard shortcuts
                if (bgHovered && !ShortcutManager.IsRebinding)
                {
                    if (ShortcutManager.IsPressed("Hierarchy/Delete"))
                    {
                        foreach (var go in Selection.GetSelected<GameObject>().ToList())
                            DeleteGameObject(go);
                    }
                    else if (ShortcutManager.IsPressed("Hierarchy/Duplicate"))
                    {
                        GameObjectClipboard.Duplicate(Selection.GetSelected<GameObject>().ToList());
                    }
                    else if (ShortcutManager.IsPressed("Hierarchy/Copy"))
                    {
                        GameObjectClipboard.Copy(Selection.GetSelected<GameObject>().ToList());
                    }
                    else if (ShortcutManager.IsPressed("Hierarchy/Paste"))
                    {
                        // Paste as children of first selected, or at root
                        var parent = Selection.GetSelected<GameObject>().FirstOrDefault();
                        GameObjectClipboard.Paste(parent);
                    }
                    else if (ShortcutManager.IsPressed("Hierarchy/Rename"))
                    {
                        var first = Selection.GetSelected<GameObject>().FirstOrDefault();
                        if (first != null)
                            StartRenameGO(first, Selection.GetSelected<GameObject>());
                    }
                }

                // Tree view
                using (ScrollView.Begin(paper, "hier_scroll", width, height - EditorTheme.RowHeight - 22))
                {
                    var roots = GetDisplayRoots(scene);

                    if (roots.Count == 0)
                    {
                        paper.Box("hier_empty_scene").Height(40)
                            .Text("Scene is empty", font)
                            .TextColor(EditorTheme.Ink300)
                            .FontSize(EditorTheme.FontSize - 2)
                            .Alignment(TextAlignment.MiddleCenter);
                    }

                    // Build flat list for shift-select support
                    var flatList = new List<GameObject>();
                    foreach (var root in roots)
                        FlattenVisible(root, flatList);
                    var flatObjects = flatList.Select(g => (object)g).ToList();

                    int drawIndex = 0;
                    foreach (var root in roots)
                    {
                        DrawGameObjectNode(paper, font, root, 0, flatObjects, ref drawIndex);
                    }
                }

                // --- All drop handling uses hier_bg (the stable outer background) ---

                // Asset drops — show visual indicator and spawn at root
                if (DragDrop.IsDraggingType<AssetDragPayload>() && bgHovered)
                {
                    _assetDropTarget = true;
                    paper.Box("hier_drop_zone").Height(24)
                        .BackgroundColor(Color.FromArgb(40, EditorTheme.Purple400))
                        .Rounded(3)
                        .Text("Drop to spawn here", font)
                        .TextColor(EditorTheme.Purple400)
                        .FontSize(EditorTheme.FontSize - 2)
                        .Alignment(TextAlignment.MiddleCenter);
                }
                else if (DragDrop.IsDraggingType<AssetDragPayload>())
                {
                    _assetDropTarget = false;
                }

                if (DragDrop.IsDropFrame && _assetDropTarget && DragDrop.Payload is AssetDragPayload assetDrop)
                {
                    if (assetDrop.AssetType == typeof(Runtime.Resources.Scene))
                    {
                        var entry = EditorAssetDatabase.Instance?.GetEntry(assetDrop.AssetGuid);
                        if (entry != null)
                            EditorSceneManager.OpenScene(entry.Path);
                    }
                    else
                    {
                        SpawnAssetInScene(assetDrop, null, Float3.Zero);
                    }
                    DragDrop.EndDrag();
                    _assetDropTarget = false;
                }

                // GO drop — process using hover target tracked by OnHover callback
                if (DragDrop.IsDropFrame && DragDrop.Payload is GameObjectDragPayload goDrop)
                {
                    if (_dragHoverTarget != null && _dragHoverTargetId != null)
                    {
                        // Dropped on a GO row — use normalized Y to determine Above/Into/Below
                        DropPosition dropPos;
                        if (_dragHoverNormalizedY < 0.25f)
                            dropPos = DropPosition.Above;
                        else if (_dragHoverNormalizedY > 0.75f)
                            dropPos = DropPosition.Below;
                        else
                            dropPos = DropPosition.Into;

                        ProcessGODrop(goDrop, _dragHoverTarget, _dragHoverTargetId, dropPos);
                    }
                    else if (bgHovered)
                    {
                        // Dropped on empty background — unparent to root
                        foreach (var dragged in goDrop.GameObjects)
                        {
                            if (dragged.Parent != null && dragged.Parent.IsValid())
                                dragged.SetParent(default);
                        }
                        EditorSceneManager.IsDirty = true;
                        DragDrop.EndDrag();
                    }

                    _dragHoverTarget = null;
                    _dragHoverTargetId = null;
                }
            }
        }
    }

    private void DrawToolbar(Paper paper, Prowl.Scribe.FontFile font, float width)
    {
        using (paper.Row("hier_toolbar")
            .Height(ToolbarHeight)
            .Margin(4, 4, 4, 0)
            .RowBetween(4)
            .Enter())
        {
            // Create button
            using (paper.Box("hier_add")
                .Width(EditorTheme.RowHeight)
                .Height(EditorTheme.RowHeight)
                .Rounded(4)
                .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                .Text(EditorIcons.Plus, font)
                .TextColor(EditorTheme.Ink500)
                .FontSize(16f)
                .Alignment(TextAlignment.MiddleCenter)
                .Enter())
            {
                if (paper.IsParentHovered)
                {
                    var builder = new ContextMenuBuilder();
                    BuildCreateMenu(builder, null);
                    builder.Render(paper, "hier_add_menu", 0, EditorTheme.RowHeight - 4);
                }
            }

            // Search
            EditorGUI.SearchBar(paper, "hier_search", _searchText, "Search...")
                .OnValueChanged(v => _searchText = v);
        }
    }

    private void DrawGameObjectNode(Paper paper, Prowl.Scribe.FontFile font,
        GameObject go, int depth, List<object> flatList, ref int drawIndex)
    {
        // Skip hidden objects
        if (go.HideFlags.HasFlag(HideFlags.Hide) || go.HideFlags.HasFlag(HideFlags.HideAndDontSave))
            return;

        // Filter by search
        if (!string.IsNullOrEmpty(_searchText))
        {
            if (!go.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
                && !go.GetChildrenDeep().Any(c => c.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase)))
                return;
        }

        string goId = go.Identifier.ToString();
        bool isSelected = Selection.IsSelected(go);
        bool hasChildren = go.Children.Count > 0
            && go.Children.Any(c => !c.HideFlags.HasFlag(HideFlags.Hide) && !c.HideFlags.HasFlag(HideFlags.HideAndDontSave));
        bool isExpanded = _expandedState.GetValueOrDefault(goId, true);
        float indent = depth * IndentSize;
        int currentIndex = drawIndex++;

        string icon = GetGameObjectIcon(go);

        // Determine drop visual for this node during drag
        bool isDragTarget = DragDrop.IsDragging && _dragHoverTargetId == goId;
        DropPosition? dragDropPos = null;
        if (isDragTarget)
        {
            if (_dragHoverNormalizedY < 0.25f) dragDropPos = DropPosition.Above;
            else if (_dragHoverNormalizedY > 0.75f) dragDropPos = DropPosition.Below;
            else dragDropPos = DropPosition.Into;
        }

        // Drop indicator above
        if (dragDropPos == DropPosition.Above)
        {
            paper.Box($"hier_drop_above_{goId}")
                .Height(3).Margin(indent + 8, 4, 0, 0).Rounded(1)
                .BackgroundColor(EditorTheme.Purple400);
        }

        bool isDropInto = dragDropPos == DropPosition.Into;

        using (paper
            .Row($"hier_go_{goId}")
            .Height(EditorTheme.RowHeight)
            .BackgroundColor(isSelected ? EditorTheme.Purple400 :
                isDropInto ? Color.FromArgb(60, EditorTheme.Purple400) : Color.Transparent)
            .Hovered.BackgroundColor(isSelected ? EditorTheme.Purple400 : EditorTheme.Ink200).End()
            .Rounded(4)
            .Margin(indent + 8, 0, 0, 0)
            .StopEventPropagation()
            .OnClick((go, currentIndex, flatList), (cap, e) =>
            {
                if (DragDrop.IsDragging || DragDrop.IsDropFrame) return;
                bool ctrl = _paper?.IsKeyDown(PaperKey.LeftControl) == true || _paper?.IsKeyDown(PaperKey.RightControl) == true;
                bool shift = _paper?.IsKeyDown(PaperKey.LeftShift) == true || _paper?.IsKeyDown(PaperKey.RightShift) == true;
                Selection.HandleListClick(cap.Item1, (IReadOnlyList<object>)cap.Item3, cap.Item2, ctrl, shift);
            })
            .OnDoubleClick(goId, (id, _) =>
            {
                _expandedState[id] = !_expandedState.GetValueOrDefault(id, true);
            })
            .OnRightClick(go, (g, _) => { if (!Selection.IsSelected(g)) Selection.Select(g); })
            .OnDragStart(go, (dragGO, _) =>
            {
                if (DragDrop.IsDragging) return;
                var selected = Selection.GetSelected<GameObject>().ToArray();
                if (selected.Length > 0 && Selection.IsSelected(dragGO))
                    DragDrop.StartDrag(new GameObjectDragPayload(selected));
                else
                    DragDrop.StartDrag(new GameObjectDragPayload(dragGO));
            })
            .OnHover(go, (g, e) =>
            {
                if (!DragDrop.IsDragging || DragDrop.Payload is not GameObjectDragPayload) return;
                _dragHoverTarget = g;
                _dragHoverTargetId = g.Identifier.ToString();
                _dragHoverNormalizedY = (float)e.NormalizedPosition.Y;
            })
            .Enter())
        {
            // Expand arrow
            if (hasChildren)
            {
                paper.Box($"hier_arr_{goId}")
                    .Width(14)
                    .Height(EditorTheme.RowHeight)
                    .Text(isExpanded ? EditorIcons.AngleDown : EditorIcons.AngleRight, font)
                    .TextColor(EditorTheme.Ink400)
                    .FontSize(9f).Alignment(TextAlignment.MiddleCenter)
                    .OnClick(goId, (id, _) =>
                    {
                        _expandedState[id] = !_expandedState.GetValueOrDefault(id, true);
                    })
                    .StopEventPropagation();
            }
            else
            {
                paper.Box($"hier_arr_{goId}").Width(14).Height(EditorTheme.RowHeight);
            }

            // Icon
            paper.Box($"hier_ico_{goId}")
                .Width(16).Height(EditorTheme.RowHeight)
                .Text(icon, font)
                .TextColor(go.EnabledInHierarchy ? EditorTheme.Ink400 : EditorTheme.Ink300)
                .FontSize(11f).Alignment(TextAlignment.MiddleCenter);

            // Name or rename field
            if (RenameOverlay.IsRenaming(goId))
            {
                RenameOverlay.Draw(paper, $"hier_rename_{goId}");
            }
            else
            {
                paper.Box($"hier_name_{goId}")
                    .Height(EditorTheme.RowHeight).ChildLeft(4)
                    .Text(go.Name, font)
                    .TextColor(GetPrefabTextColor(go))
                    .FontSize(EditorTheme.FontSize - 1)
                    .Alignment(TextAlignment.MiddleLeft);
            }

            // Enable/disable toggle (eye icon)
            paper.Box($"hier_vis_{goId}")
                .Width(18).Height(EditorTheme.RowHeight)
                .Text(go.Enabled ? EditorIcons.Eye : EditorIcons.EyeSlash, font)
                .TextColor(go.Enabled ? EditorTheme.Ink400 : EditorTheme.Ink300)
                .FontSize(9f).Alignment(TextAlignment.MiddleCenter)
                .StopEventPropagation()
                .OnClick(go, (g, _) => g.Enabled = !g.Enabled);


            // Per-GameObject right-click menu
            BuildGameObjectContextMenu(paper, $"hier_go_ctx_{goId}");
        }

        // Drop indicator below
        if (dragDropPos == DropPosition.Below)
        {
            paper.Box($"hier_drop_below_{goId}")
                .Height(3).Margin(indent + 8, 4, 0, 0).Rounded(1)
                .BackgroundColor(EditorTheme.Purple400);
        }

        // Draw children
        if (isExpanded && hasChildren)
        {
            foreach (var child in go.Children)
                DrawGameObjectNode(paper, font, child, depth + 1, flatList, ref drawIndex);
        }
    }

    // ================================================================
    //  Drag & Drop for reparenting/reordering
    // ================================================================

    private void ProcessGODrop(GameObjectDragPayload goDrop, GameObject target, string targetId, DropPosition dropPos)
    {
        var targetParent = target.Parent;
        bool targetIsRoot = targetParent == null || !targetParent.IsValid();

        foreach (var dragged in goDrop.GameObjects)
        {
            if (dragged == target || IsDescendantOf(target, dragged))
                continue;

            // Block moving a prefab child out of its parent
            if (dragged.Parent != null && dragged.Parent.IsPrefabInstance && dragged.Parent.PrefabChildCount >= 0)
            {
                int dragChildIdx = dragged.Parent.Children.IndexOf(dragged);
                if (dragChildIdx >= 0 && dragChildIdx < dragged.Parent.PrefabChildCount)
                {
                    Widgets.Toasts.Show("Prefab Structure", "Cannot move a prefab child. Break the prefab first.", Widgets.ToastType.Warning, 3f);
                    continue;
                }
            }

            switch (dropPos)
            {
                case DropPosition.Into:
                    if (target.IsPrefabInstance && target.PrefabChildCount >= 0)
                    {
                        Widgets.Toasts.Show("Prefab Structure", "Cannot add children to a prefab instance. Break the prefab first.", Widgets.ToastType.Warning, 3f);
                        continue;
                    }
                    dragged.SetParent(target);
                    _expandedState[targetId] = true;
                    break;

                case DropPosition.Above:
                case DropPosition.Below:
                    if (targetIsRoot)
                    {
                        // Root reorder — unparent if needed, then reorder in display list
                        if (dragged.Parent != null && dragged.Parent.IsValid())
                            dragged.SetParent(default);

                        string dragId = dragged.Identifier.ToString();
                        _rootDisplayOrder.Remove(dragId);
                        int insertIdx = _rootDisplayOrder.IndexOf(targetId);
                        if (insertIdx < 0) insertIdx = _rootDisplayOrder.Count;
                        if (dropPos == DropPosition.Below) insertIdx++;
                        insertIdx = Math.Min(insertIdx, _rootDisplayOrder.Count);
                        _rootDisplayOrder.Insert(insertIdx, dragId);
                    }
                    else
                    {
                        // Child reorder — reparent to target's parent, then set sibling index
                        if (dragged.Parent != targetParent)
                            dragged.SetParent(targetParent!);

                        int targetIdx = target.GetSiblingIndex() ?? 0;
                        if (dropPos == DropPosition.Below) targetIdx++;
                        int dragIdx = dragged.GetSiblingIndex() ?? 0;
                        if (dragIdx < targetIdx) targetIdx--;
                        dragged.SetSiblingIndex(Math.Max(0, targetIdx));
                    }
                    break;
            }
        }

        EditorSceneManager.IsDirty = true;
        DragDrop.EndDrag();
    }

    private static bool IsDescendantOf(GameObject potentialChild, GameObject potentialParent)
    {
        var current = potentialChild.Parent;
        while (current != null && current.IsValid())
        {
            if (current == potentialParent) return true;
            current = current.Parent;
        }
        return false;
    }

    // ================================================================
    //  Context Menus
    // ================================================================

    private void BuildBackgroundContextMenu(Paper paper)
    {
        ContextMenuHelper.RightClickMenu(paper, "hier_bg_ctx", builder =>
        {
            BuildCreateMenu(builder, null);
        });
    }

    private void BuildGameObjectContextMenu(Paper paper, string id)
    {
        ContextMenuHelper.RightClickMenu(paper, id, builder =>
        {
            var selectedGOs = Selection.GetSelected<GameObject>().ToList();
            var firstSelected = selectedGOs.FirstOrDefault();
            if (selectedGOs.Count == 0) return;

            bool multiSelect = selectedGOs.Count > 1;

            // Move to View / Move View To
            var cam = SceneViewPanel.ActiveCamera;
            if (cam != null)
            {
                builder.Item("Move to View", () =>
                {
                    foreach (var go in selectedGOs)
                    {
                        go.Transform.Position = cam.Position;
                        go.Transform.LocalEulerAngles = new Float3(cam.Pitch, cam.Yaw, 0);
                    }
                    EditorSceneManager.IsDirty = true;
                }, icon: EditorIcons.ArrowRight);

                builder.Item("Move View To", () =>
                {
                    cam.SetPosition(firstSelected!.Transform.Position);
                    cam.SetOrientation((float)firstSelected!.Transform.LocalEulerAngles.Y, (float)firstSelected!.Transform.LocalEulerAngles.X);
                }, icon: EditorIcons.Eye);

                builder.Separator();
            }

            // Prefab operations
            if (!multiSelect && firstSelected!.IsPrefabInstance)
            {
                builder.Item("Select Prefab Asset", () =>
                {
                    Selection.FocusAsset(firstSelected.PrefabAssetId);
                }, icon: EditorIcons.Cubes);

                bool hasOverrides = Prefabs.PrefabUtility.HasAnyOverrides(firstSelected);

                builder.Item("Apply Prefab Overrides", () =>
                {
                    var root = Prefabs.PrefabUtility.GetPrefabInstanceRoot(firstSelected);
                    if (root != null) Prefabs.PrefabUtility.ApplyOverrides(root);
                }, enabled: hasOverrides, icon: EditorIcons.Check);

                builder.Item("Revert to Prefab", () =>
                {
                    var root = Prefabs.PrefabUtility.GetPrefabInstanceRoot(firstSelected);
                    if (root != null) Prefabs.PrefabUtility.RevertOverrides(root);
                }, enabled: hasOverrides, icon: EditorIcons.ArrowsRotate);

                builder.Item("Break Prefab Instance", () =>
                {
                    Prefabs.PrefabUtility.BreakPrefabInstance(firstSelected);
                }, icon: EditorIcons.LinkSlash);

                builder.Separator();
            }

            // Create — parent to first selected
            BuildCreateMenu(builder, firstSelected);
            builder.Separator();

            if (multiSelect)
            {
                builder.Item($"Duplicate ({selectedGOs.Count})", () =>
                {
                    GameObjectClipboard.Duplicate(selectedGOs);
                }, icon: EditorIcons.Copy);

                builder.Item($"Rename ({selectedGOs.Count})", () =>
                {
                    StartRenameGO(firstSelected!, selectedGOs);
                }, icon: EditorIcons.PenToSquare);

                builder.Item($"Delete ({selectedGOs.Count})", () =>
                {
                    foreach (var go in selectedGOs.ToList()) DeleteGameObject(go);
                }, icon: EditorIcons.Trash);

                builder.Separator();

                bool anyEnabled = selectedGOs.Any(g => g.Enabled);
                builder.Item(anyEnabled ? "Disable All" : "Enable All", () =>
                {
                    bool newState = !anyEnabled;
                    foreach (var go in selectedGOs) go.Enabled = newState;
                }, icon: anyEnabled ? EditorIcons.EyeSlash : EditorIcons.Eye);
            }
            else
            {
                var go = firstSelected!;
                builder.Item("Duplicate", () => GameObjectClipboard.Duplicate([go]), icon: EditorIcons.Copy);
                builder.Item("Rename", () =>
                {
                    StartRenameGO(go, [go]);
                }, icon: EditorIcons.PenToSquare);
                builder.Item("Delete", () => DeleteGameObject(go), icon: EditorIcons.Trash);
                builder.Separator();
                builder.Item(go.Enabled ? "Disable" : "Enable", () => go.Enabled = !go.Enabled,
                    icon: go.Enabled ? EditorIcons.EyeSlash : EditorIcons.Eye);
            }
        });
    }

    // ================================================================
    //  Create Menu
    // ================================================================

    private void BuildCreateMenu(ContextMenuBuilder builder, GameObject? parent)
    {
        CreateGameObjectMenuRegistry.BuildMenu(builder, parent);
    }

    internal static GameObject CreateGameObject(string name, GameObject? parent)
    {
        var scene = Scene.Current;
        if (scene == null) return new GameObject(name);

        var go = new GameObject(name);
        scene.Add(go);
        if (parent != null)
            go.SetParent(parent);
        Selection.Select(go);

        // Enter rename via global overlay
        string goId = go.Identifier.ToString();
        RenameOverlay.Begin(goId, go.Name, newName =>
        {
            go.Name = newName;
            EditorSceneManager.IsDirty = true;
        });

        return go;
    }

    private void StartRenameGO(GameObject primary, IEnumerable<GameObject> allTargets)
    {
        var targets = allTargets.ToList();
        string goId = primary.Identifier.ToString();
        RenameOverlay.Begin(goId, primary.Name, newName =>
        {
            foreach (var go in targets)
                go.Name = newName;
            EditorSceneManager.IsDirty = true;
        });
    }

    private void DeleteGameObject(GameObject go)
    {
        // Block deleting prefab children that are part of the prefab structure
        if (go.Parent != null && go.Parent.IsPrefabInstance && go.Parent.PrefabChildCount >= 0)
        {
            int childIdx = go.Parent.Children.IndexOf(go);
            if (childIdx >= 0 && childIdx < go.Parent.PrefabChildCount)
            {
                Widgets.Toasts.Show("Prefab Structure", "Cannot delete a prefab child. Break the prefab first.", Widgets.ToastType.Warning, 3f);
                return;
            }
        }

        var scene = Scene.Current;
        if (scene == null) return;

        if (Selection.IsSelected(go))
            Selection.RemoveFromSelection(go);

        foreach (var child in go.GetChildrenDeep().ToList())
            scene.Remove(child);

        scene.Remove(go);
        go.Dispose();
    }

    // ================================================================
    //  Helpers
    // ================================================================

    private List<GameObject> GetDisplayRoots(Scene scene)
    {
        var roots = scene.RootObjects
            .Where(go => !go.HideFlags.HasFlag(HideFlags.Hide)
                      && !go.HideFlags.HasFlag(HideFlags.HideAndDontSave))
            .ToList();

        // Sync display order: remove stale entries, add new ones at end
        var rootIds = new HashSet<string>(roots.Select(r => r.Identifier.ToString()));
        _rootDisplayOrder.RemoveAll(id => !rootIds.Contains(id));
        foreach (var root in roots)
        {
            string id = root.Identifier.ToString();
            if (!_rootDisplayOrder.Contains(id))
                _rootDisplayOrder.Add(id);
        }

        // Return roots in display order
        var lookup = roots.ToDictionary(r => r.Identifier.ToString());
        var ordered = new List<GameObject>();
        foreach (var id in _rootDisplayOrder)
        {
            if (lookup.TryGetValue(id, out var go))
                ordered.Add(go);
        }
        return ordered;
    }

    private void FlattenVisible(GameObject go, List<GameObject> list)
    {
        if (go.HideFlags.HasFlag(HideFlags.Hide) || go.HideFlags.HasFlag(HideFlags.HideAndDontSave))
            return;

        list.Add(go);

        string goId = go.Identifier.ToString();
        bool isExpanded = _expandedState.GetValueOrDefault(goId, true);
        if (isExpanded)
        {
            foreach (var child in go.Children)
                FlattenVisible(child, list);
        }
    }

    private static Color GetPrefabTextColor(GameObject go)
    {
        if (!go.IsPrefabInstance)
            return go.EnabledInHierarchy ? EditorTheme.Ink500 : EditorTheme.Ink300;

        // Check if the prefab asset still exists
        var entry = EditorAssetDatabase.Instance?.GetEntry(go.PrefabAssetId);
        if (entry == null)
        {
            // Broken prefab link — red text
            return go.EnabledInHierarchy
                ? Color.FromArgb(255, 220, 80, 80)
                : Color.FromArgb(255, 160, 60, 60);
        }

        // Valid prefab — purple text
        return go.EnabledInHierarchy ? EditorTheme.Purple400 : EditorTheme.Purple300;
    }

    private static string GetGameObjectIcon(GameObject go)
    {
        if (go.GetComponent<Camera>() != null) return EditorIcons.Camera;
        if (go.GetComponent<Light>() != null) return EditorIcons.Sun;
        if (go.GetComponent<MeshRenderer>() != null) return EditorIcons.Cube;
        if (go.GetComponent<ModelRenderer>() != null) return EditorIcons.Cubes;
        return EditorIcons.Circle;
    }

    private GameObject? FindGOByIdentifier(string id)
    {
        var scene = Scene.Current;
        if (scene == null) return null;
        return scene.AllObjects.FirstOrDefault(g => g.Identifier.ToString() == id);
    }

    // ================================================================
    //  Asset Drop → Spawn in Scene
    // ================================================================

    public static void SpawnAssetInScene(AssetDragPayload payload, GameObject? parent, Float3 position)
    {
        var scene = Scene.Current;
        if (scene == null) return;

        var asset = Runtime.AssetDatabase.Get(payload.AssetGuid);
        if (asset == null) return;

        string name = System.IO.Path.GetFileNameWithoutExtension(payload.AssetName);

        if (asset is Model model)
        {
            var go = new GameObject(name);
            go.Transform.Position = position;
            var renderer = go.AddComponent<ModelRenderer>();
            renderer.Model = model;
            scene.Add(go);
            if (parent != null) go.SetParent(parent);
            Selection.Select(go);
        }
        else if (asset is Mesh mesh)
        {
            var go = new GameObject(name);
            go.Transform.Position = position;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.Mesh = mesh;
            renderer.Material = new Material(Shader.LoadDefault(DefaultShader.Standard));
            scene.Add(go);
            if (parent != null) go.SetParent(parent);
            Selection.Select(go);
        }
        else if (asset is PrefabAsset)
        {
            var instance = Prefabs.PrefabUtility.InstantiatePrefab(payload.AssetGuid);
            if (instance != null)
            {
                instance.Transform.Position = position;
                scene.Add(instance);
                if (parent != null) instance.SetParent(parent);
                Selection.Select(instance);
            }
        }
        else
        {
            Runtime.Debug.LogWarning($"Cannot spawn asset of type {asset.GetType().Name} in scene.");
        }
    }
}
