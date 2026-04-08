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
    private string? _dropTargetId; // GO identifier for the current drop target
    private enum DropPosition { Into, Above, Below }
    private DropPosition _dropPos;
    private bool _assetDropTarget; // True if hierarchy was hovered during asset drag

    // Track expanded state per GO identifier
    private static readonly Dictionary<string, bool> _expandedState = new();

    public override void OnGUI(Paper paper, float width, float height)
    {
        _paper = paper;
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        var scene = Scene.Current;

        // Reset drop target each frame while actively dragging GOs.
        // On the drop frame (IsDropFrame), preserve _dropTargetId so HandleGODragDrop can read it.
        if (DragDrop.IsDraggingType<GameObjectDragPayload>())
            _dropTargetId = null;
        else if (!DragDrop.HasPayloadType<GameObjectDragPayload>())
            _dropTargetId = null;

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

                    // Accept asset drops — spawn at root
                    if (DragDrop.IsDraggingType<AssetDragPayload>() && paper.IsParentHovered)
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

                    // Handle GO drag drop
                    HandleGODragDrop(scene);
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

        // Drop indicator above
        if (_dropTargetId == goId && _dropPos == DropPosition.Above)
        {
            paper.Box($"hier_drop_above_{goId}")
                .Height(2).ChildLeft(indent)
                .BackgroundColor(EditorTheme.Purple400);
        }

        bool isDropInto = _dropTargetId == goId && _dropPos == DropPosition.Into;

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
                // Don't select on click if we just finished a drag
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
                // Drag all selected objects if the source is selected, otherwise just the source
                var selected = Selection.GetSelected<GameObject>().ToArray();
                if (selected.Length > 0 && Selection.IsSelected(dragGO))
                    DragDrop.StartDrag(new GameObjectDragPayload(selected));
                else
                    DragDrop.StartDrag(new GameObjectDragPayload(dragGO));
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

            // Handle drop target detection
            HandleDropTarget(paper, go, goId);

            // Per-GameObject right-click menu
            BuildGameObjectContextMenu(paper, $"hier_go_ctx_{goId}");
        }

        // Drop indicator below
        if (_dropTargetId == goId && _dropPos == DropPosition.Below)
        {
            paper.Box($"hier_drop_below_{goId}")
                .Height(2).ChildLeft(indent)
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

    private void HandleDropTarget(Paper paper, GameObject go, string goId)
    {
        if (!DragDrop.IsDraggingType<GameObjectDragPayload>()) return;
        if (!paper.IsParentHovered) return;

        _dropTargetId = goId;

        bool targetIsRoot = go.Parent == null || !go.Parent.IsValid();

        // Root objects can't be reordered (Scene uses HashSet), so always show "Into"
        if (targetIsRoot)
        {
            _dropPos = DropPosition.Into;
            return;
        }

        // For non-root objects: top 25% = above, bottom 25% = below, middle = into
        var parentData = paper.CurrentParent.Data;
        float rowY = parentData.Y;
        float mouseY = Input.MousePosition.Y;
        float relY = mouseY - rowY;

        float topZone = EditorTheme.RowHeight * 0.25f;
        float bottomZone = EditorTheme.RowHeight * 0.75f;

        if (relY < topZone)
            _dropPos = DropPosition.Above;
        else if (relY > bottomZone)
            _dropPos = DropPosition.Below;
        else
            _dropPos = DropPosition.Into;
    }

    private void HandleGODragDrop(Scene scene)
    {
        if (!DragDrop.IsDropFrame || DragDrop.Payload is not GameObjectDragPayload goDrop || _dropTargetId == null)
            return;

        var target = FindGOByIdentifier(_dropTargetId);
        if (target == null)
        {
            DragDrop.EndDrag();
            return;
        }

        foreach (var dragged in goDrop.GameObjects)
        {
            // Can't drop onto self or own descendant
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

            var dropPos = _dropPos;
            var targetParent = target.Parent;
            bool targetIsRoot = targetParent == null || !targetParent.IsValid();

            // For Above/Below on root objects, there's no sibling ordering
            // (Scene uses a HashSet), so treat as Into (make child of target)
            if (targetIsRoot && dropPos != DropPosition.Into)
                dropPos = DropPosition.Into;

            switch (dropPos)
            {
                case DropPosition.Into:
                    // Block reparenting into prefab instances (structure is fixed)
                    if (target.IsPrefabInstance && target.PrefabChildCount >= 0)
                    {
                        Widgets.Toasts.Show("Prefab Structure", "Cannot add children to a prefab instance. Break the prefab first.", Widgets.ToastType.Warning, 3f);
                        continue;
                    }
                    dragged.SetParent(target);
                    _expandedState[_dropTargetId!] = true;
                    break;

                case DropPosition.Above:
                case DropPosition.Below:
                    // Reparent to target's parent, then reorder as sibling
                    if (dragged.Parent != targetParent)
                        dragged.SetParent(targetParent!);

                    int targetIdx = target.GetSiblingIndex() ?? 0;
                    if (dropPos == DropPosition.Below) targetIdx++;
                    int dragIdx = dragged.GetSiblingIndex() ?? 0;
                    if (dragIdx < targetIdx) targetIdx--;
                    dragged.SetSiblingIndex(Math.Max(0, targetIdx));
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
                    foreach (var go in selectedGOs) DuplicateGameObject(go);
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
                builder.Item("Duplicate", () => DuplicateGameObject(go), icon: EditorIcons.Copy);
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
        builder.Item("Empty Object", () => CreateGameObject("GameObject", parent), icon: EditorIcons.Cube);
        builder.Separator();
        builder.Submenu("3D Object", sub =>
        {
            sub.Item("Cube", () => CreatePrimitive("Cube", DefaultModel.Cube, parent), icon: EditorIcons.Cube);
            sub.Item("Sphere", () => CreatePrimitive("Sphere", DefaultModel.Sphere, parent), icon: EditorIcons.CircleDot);
            sub.Item("Cylinder", () => CreatePrimitive("Cylinder", DefaultModel.Cylinder, parent), icon: EditorIcons.Circle);
            sub.Item("Plane", () => CreatePrimitive("Plane", DefaultModel.Plane, parent), icon: EditorIcons.Square);
        }, icon: EditorIcons.Cubes);
        builder.Submenu("Light", sub =>
        {
            sub.Item("Directional Light", () =>
            {
                var go = CreateGameObject("Directional Light", parent);
                go.Transform.Rotation = Quaternion.FromEuler(new Float3(50, 30, 0));
                go.AddComponent<DirectionalLight>();
            }, icon: EditorIcons.Sun);
        }, icon: EditorIcons.Lightbulb);
        builder.Item("Camera", () =>
        {
            var go = CreateGameObject("Camera", parent);
            go.AddComponent<Camera>();
        }, icon: EditorIcons.Camera);
        builder.Item("Particle System", () =>
        {
            var go = CreateGameObject("Particle System", parent);
            var ps = go.AddComponent<ParticleSystemComponent>();
            ps.Material = new AssetRef<Material>(BuiltInAssets.GuidFor(DefaultMaterial.Particle));
            ps.Emission.Enabled = true;
            ps.Emission.RateOverTime = new MinMaxCurve(10f);
            ps.Emission.Shape = EmissionShape.Cone;
            ps.Initial.Enabled = true;
            ps.Initial.StartLifetime = new MinMaxCurve(2f);
            ps.Initial.StartSpeed = new MinMaxCurve(3f);
            ps.Initial.StartSize = new MinMaxCurve(0.2f);
        }, icon: EditorIcons.SprayCanSparkles);
    }

    private GameObject CreateGameObject(string name, GameObject? parent)
    {
        var scene = Scene.Current;
        if (scene == null) return new GameObject(name);

        var go = new GameObject(name);
        scene.Add(go);
        if (parent != null)
            go.SetParent(parent);
        Selection.Select(go);

        // Enter rename
        StartRenameGO(go, [go]);
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

    private void CreatePrimitive(string name, DefaultModel model, GameObject? parent)
    {
        var go = CreateGameObject(name, parent);
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.Mesh = new AssetRef<Mesh>(BuiltInAssets.GuidForMesh(model));
        renderer.Material = new AssetRef<Material>(BuiltInAssets.GuidFor(DefaultMaterial.Standard));
    }

    private void DuplicateGameObject(GameObject source)
    {
        var scene = Scene.Current;
        if (scene == null) return;

        var go = new GameObject(source.Name + " (Copy)");
        go.Transform.Position = source.Transform.Position;
        go.Transform.Rotation = source.Transform.Rotation;
        go.Transform.LocalScale = source.Transform.LocalScale;
        scene.Add(go);
        if (source.Parent != null)
            go.SetParent(source.Parent);
        Selection.Select(go);
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
        return scene.RootObjects
            .Where(go => !go.HideFlags.HasFlag(HideFlags.Hide)
                      && !go.HideFlags.HasFlag(HideFlags.HideAndDontSave))
            .ToList();
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
