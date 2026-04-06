using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Editor.Docking;
using Prowl.Editor.Widgets;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
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
    private HashSet<string> _renamingIds = new(); // GOs currently being renamed
    private string _renameText = "";
    private const float RowHeight = 22f;
    private const float IndentSize = 16f;

    // Drag state
    private bool _dragInitiated;
    private Float2 _dragStartPos;
    private const float DragThreshold = 5f;
    private string? _dropTargetId; // GO identifier for the current drop target
    private enum DropPosition { Into, Above, Below }
    private DropPosition _dropPos;

    // Track expanded state per GO identifier
    private static readonly Dictionary<string, bool> _expandedState = new();

    public override void OnGUI(Paper paper, float width, float height)
    {
        _paper = paper;
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        var scene = Scene.Current;

        // Reset drop target each frame — but only if still actively dragging GOs
        // (on release frame, we need the last target to process the drop)
        if (DragDrop.IsDraggingType<GameObjectDragPayload>())
            _dropTargetId = null;
        else if (!DragDrop.IsDragging && DragDrop.Payload is not GameObjectDragPayload)
            _dropTargetId = null;

        using (paper.Column("hier_root").Size(width, height).Enter())
        {
            // Toolbar
            DrawToolbar(paper, font, width);

            if (scene == null)
            {
                paper.Box("hier_empty").Height(60)
                    .Text("No Scene Loaded", font)
                    .TextColor(EditorTheme.Ink300)
                    .FontSize(EditorTheme.FontSize)
                    .Alignment(TextAlignment.MiddleCenter);

                EditorGUI.Button(paper, "hier_create_scene", $"{EditorIcons.Plus}  New Scene", width: 120)
                    .OnValueChanged(_ => SceneViewPanel.CreateAndLoadDefaultScene());
                return;
            }

            // Scene name header
            paper.Box("hier_scene_name")
                .Height(22).ChildLeft(8)
                .BackgroundColor(EditorTheme.Neutral200)
                .Text($"{EditorIcons.Film}  {scene.Name}", font)
                .TextColor(EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSize - 1)
                .Alignment(TextAlignment.MiddleLeft);

            // Tree view
            using (ScrollView.Begin(paper, "hier_scroll", width, height - RowHeight - 22))
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

                // Single right-click menu — selection-aware
                ContextMenuHelper.RightClickMenu(paper, "hier_ctx", builder =>
                {
                    var selectedGOs = Selection.GetSelected<GameObject>().ToList();
                    var firstSelected = selectedGOs.FirstOrDefault();
                    bool multiSelect = selectedGOs.Count > 1;

                    // Create submenu — parent to first selected (or root)
                    BuildCreateMenu(builder, firstSelected);

                    if (selectedGOs.Count > 0)
                    {
                        builder.Separator();

                        if (multiSelect)
                        {
                            builder.Item($"Duplicate ({selectedGOs.Count})", () =>
                            {
                                foreach (var go in selectedGOs) DuplicateGameObject(go);
                            }, icon: EditorIcons.Copy);

                            builder.Item($"Rename ({selectedGOs.Count})", () =>
                            {
                                _renamingIds.Clear();
                                foreach (var go in selectedGOs)
                                    _renamingIds.Add(go.Identifier.ToString());
                                _renameText = firstSelected!.Name;
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
                                _renamingIds.Clear();
                                _renamingIds.Add(go.Identifier.ToString());
                                _renameText = go.Name;
                            }, icon: EditorIcons.PenToSquare);
                            builder.Item("Delete", () => DeleteGameObject(go), icon: EditorIcons.Trash);
                            builder.Separator();
                            builder.Item(go.Enabled ? "Disable" : "Enable", () => go.Enabled = !go.Enabled,
                                icon: go.Enabled ? EditorIcons.EyeSlash : EditorIcons.Eye);
                        }
                    }
                });

                // Accept asset drops — spawn at root
                if (DragDrop.IsDraggingType<AssetDragPayload>() && paper.IsParentHovered)
                {
                    paper.Box("hier_drop_zone").Height(24)
                        .BackgroundColor(Color.FromArgb(40, EditorTheme.Purple400))
                        .Rounded(3)
                        .Text("Drop to spawn here", font)
                        .TextColor(EditorTheme.Purple400)
                        .FontSize(EditorTheme.FontSize - 2)
                        .Alignment(TextAlignment.MiddleCenter);
                }

                if (!DragDrop.IsDragging && DragDrop.Payload is AssetDragPayload assetDrop && paper.IsParentHovered)
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
                }

                // Handle GO drag drop
                HandleGODragDrop(scene);
            }
        }
    }

    private void DrawToolbar(Paper paper, Prowl.Scribe.FontFile font, float width)
    {
        using (paper.Row("hier_toolbar")
            .Height(RowHeight)
            .ChildLeft(4).ChildRight(4).RowBetween(4)
            .ChildTop(2).ChildBottom(2)
            .Enter())
        {
            // Create button
            using (paper.Box("hier_add")
                .Width(RowHeight - 4).Height(RowHeight - 4).Rounded(4)
                .Hovered.BackgroundColor(EditorTheme.ButtonHovered).End()
                .Text(EditorIcons.Plus, font).TextColor(EditorTheme.Ink500)
                .FontSize(12f).Alignment(TextAlignment.MiddleCenter)
                .Enter())
            {
                if (paper.IsParentHovered)
                {
                    var builder = new ContextMenuBuilder();
                    BuildCreateMenu(builder, null);
                    builder.Render(paper, "hier_add_menu", 0, RowHeight - 4);
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

        using (paper.Row($"hier_go_{goId}")
            .Height(RowHeight)
            .BackgroundColor(isSelected ? EditorTheme.Purple400 :
                (_dropTargetId == goId && _dropPos == DropPosition.Into)
                    ? Color.FromArgb(60, EditorTheme.Purple400) : Color.Transparent)
            .Hovered.BackgroundColor(isSelected ? EditorTheme.Purple400 : EditorTheme.ButtonHovered).End()
            .Rounded(2)
            .ChildLeft(indent + 2)
            .OnClick((go, currentIndex, flatList), (cap, e) =>
            {
                bool ctrl = _paper?.IsKeyDown(PaperKey.LeftControl) == true || _paper?.IsKeyDown(PaperKey.RightControl) == true;
                bool shift = _paper?.IsKeyDown(PaperKey.LeftShift) == true || _paper?.IsKeyDown(PaperKey.RightShift) == true;
                Selection.HandleListClick(cap.Item1, (IReadOnlyList<object>)cap.Item3, cap.Item2, ctrl, shift);
            })
            .OnDoubleClick(goId, (id, _) =>
            {
                _expandedState[id] = !_expandedState.GetValueOrDefault(id, true);
            })
            .OnRightClick(go, (g, _) => { if (!Selection.IsSelected(g)) Selection.Select(g); })
            .Enter())
        {
            // Initiate drag on mouse down + movement
            HandleDragStart(paper, go);

            // Expand arrow
            if (hasChildren)
            {
                paper.Box($"hier_arr_{goId}")
                    .Width(14).Height(RowHeight)
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
                paper.Box($"hier_arr_{goId}").Width(14).Height(RowHeight);
            }

            // Icon
            paper.Box($"hier_ico_{goId}")
                .Width(16).Height(RowHeight)
                .Text(icon, font)
                .TextColor(go.EnabledInHierarchy ? EditorTheme.Ink400 : EditorTheme.Ink300)
                .FontSize(11f).Alignment(TextAlignment.MiddleCenter);

            // Name or rename field
            if (_renamingIds.Contains(goId))
            {
                EditorGUI.TextField(paper, $"hier_rename_{goId}", "", _renameText)
                    .OnValueChanged(v => _renameText = v);

                bool confirm = _paper?.IsKeyDown(PaperKey.Enter) == true || _paper?.IsKeyDown(PaperKey.KeypadEnter) == true;
                bool cancel = _paper?.IsKeyDown(PaperKey.Escape) == true;

                if (confirm)
                {
                    if (!string.IsNullOrWhiteSpace(_renameText))
                    {
                        // Apply to all renaming objects
                        foreach (var rid in _renamingIds)
                        {
                            var rgo = FindGOByIdentifier(rid);
                            if (rgo != null) rgo.Name = _renameText;
                        }
                    }
                    _renamingIds.Clear();
                }
                else if (cancel)
                {
                    _renamingIds.Clear();
                }
            }
            else
            {
                paper.Box($"hier_name_{goId}")
                    .Height(RowHeight).ChildLeft(4)
                    .Text(go.Name, font)
                    .TextColor(go.EnabledInHierarchy ? EditorTheme.Ink500 : EditorTheme.Ink300)
                    .FontSize(EditorTheme.FontSize - 1)
                    .Alignment(TextAlignment.MiddleLeft);
            }

            // Enable/disable toggle (eye icon)
            paper.Box($"hier_vis_{goId}")
                .Width(18).Height(RowHeight)
                .Text(go.Enabled ? EditorIcons.Eye : EditorIcons.EyeSlash, font)
                .TextColor(go.Enabled ? EditorTheme.Ink400 : EditorTheme.Ink300)
                .FontSize(9f).Alignment(TextAlignment.MiddleCenter)
                .StopEventPropagation()
                .OnClick(go, (g, _) => g.Enabled = !g.Enabled);

            // Handle drop target detection
            HandleDropTarget(paper, go, goId);
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

    private void HandleDragStart(Paper paper, GameObject go)
    {
        if (DragDrop.IsDragging) return;

        // Start tracking on mouse down
        if (Input.GetMouseButtonDown(0) && paper.IsParentHovered)
        {
            _dragInitiated = true;
            _dragStartPos = new Float2(Input.MousePosition.X, Input.MousePosition.Y);
        }

        // Check threshold
        if (_dragInitiated && Input.GetMouseButton(0))
        {
            var mousePos = new Float2(Input.MousePosition.X, Input.MousePosition.Y);
            if (Float2.Distance(mousePos, _dragStartPos) > DragThreshold)
            {
                _dragInitiated = false;

                // Drag all selected objects (if this GO is selected), otherwise just this one
                var selected = Selection.GetSelected<GameObject>().ToArray();
                if (selected.Length > 0 && Selection.IsSelected(go))
                    DragDrop.StartDrag(new GameObjectDragPayload(selected));
                else
                    DragDrop.StartDrag(new GameObjectDragPayload(go));
            }
        }

        if (Input.GetMouseButtonUp(0))
            _dragInitiated = false;
    }

    private void HandleDropTarget(Paper paper, GameObject go, string goId)
    {
        if (!DragDrop.IsDraggingType<GameObjectDragPayload>()) return;
        if (!paper.IsParentHovered) return;

        // Determine drop position based on mouse Y within the row
        var parentData = paper.CurrentParent.Data;
        float rowY = parentData.Y;
        float mouseY = Input.MousePosition.Y;
        float relY = mouseY - rowY;

        float topZone = RowHeight * 0.25f;
        float bottomZone = RowHeight * 0.75f;

        if (relY < topZone)
        {
            _dropTargetId = goId;
            _dropPos = DropPosition.Above;
        }
        else if (relY > bottomZone)
        {
            _dropTargetId = goId;
            _dropPos = DropPosition.Below;
        }
        else
        {
            _dropTargetId = goId;
            _dropPos = DropPosition.Into;
        }
    }

    private void HandleGODragDrop(Scene scene)
    {
        // Process drop when mouse released
        if (!DragDrop.IsDragging && DragDrop.Payload is GameObjectDragPayload goDrop && _dropTargetId != null)
        {
            var target = FindGOByIdentifier(_dropTargetId);
            if (target != null)
            {
                var draggedObjects = goDrop.GameObjects;

                foreach (var dragged in draggedObjects)
                {
                    // Can't drop onto self or own descendant
                    if (dragged == target || IsDescendantOf(target, dragged))
                        continue;

                    switch (_dropPos)
                    {
                        case DropPosition.Into:
                            dragged.SetParent(target);
                            _expandedState[_dropTargetId!] = true;
                            break;

                        case DropPosition.Above:
                        case DropPosition.Below:
                            // Reparent to same parent as target
                            var targetParent = target.Parent;
                            if (targetParent != null && targetParent.IsValid())
                            {
                                if (dragged.Parent != targetParent)
                                    dragged.SetParent(targetParent);

                                // Reorder: place before/after target
                                int targetIdx = target.GetSiblingIndex() ?? 0;
                                if (_dropPos == DropPosition.Below) targetIdx++;
                                // If dragged was before target in same parent, adjust index
                                int dragIdx = dragged.GetSiblingIndex() ?? 0;
                                if (dragIdx < targetIdx) targetIdx--;
                                dragged.SetSiblingIndex(Math.Max(0, targetIdx));
                            }
                            else
                            {
                                // Target is a root object — unparent dragged to become root too
                                if (dragged.Parent != null && dragged.Parent.IsValid())
                                    dragged.SetParent(default); // null parent = root
                            }
                            break;
                    }
                }

                EditorSceneManager.IsDirty = true;
            }

            DragDrop.EndDrag();
        }
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
    //  Create Menu
    // ================================================================

    private void BuildCreateMenu(ContextMenuBuilder builder, GameObject? parent)
    {
        builder.Item("Empty Object", () => CreateGameObject("GameObject", parent), icon: EditorIcons.Cube);
        builder.Separator();
        builder.Submenu("3D Object", sub =>
        {
            sub.Item("Cube", () => CreatePrimitive("Cube", Float3.One, parent), icon: EditorIcons.Cube);
            sub.Item("Sphere", () => CreatePrimitive("Sphere", Float3.One, parent), icon: EditorIcons.CircleDot);
            sub.Item("Plane", () => CreatePrimitive("Plane", new Float3(10, 1, 10), parent), icon: EditorIcons.Square);
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
        _renamingIds.Clear();
        _renamingIds.Add(go.Identifier.ToString());
        _renameText = go.Name;
        return go;
    }

    private void CreatePrimitive(string name, Float3 size, GameObject? parent)
    {
        var go = CreateGameObject(name, parent);
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.Mesh = Mesh.CreateCube(size);
        renderer.Material = new Material(Shader.LoadDefault(DefaultShader.Standard));
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
        var go = new GameObject(name);
        go.Transform.Position = position;

        if (asset is Model model)
        {
            var renderer = go.AddComponent<ModelRenderer>();
            renderer.Model = model;
        }
        else if (asset is Mesh mesh)
        {
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.Mesh = mesh;
            renderer.Material = new Material(Shader.LoadDefault(DefaultShader.Standard));
        }
        else
        {
            Runtime.Debug.Log($"Spawned empty GameObject for asset type: {asset.GetType().Name}");
        }

        scene.Add(go);
        if (parent != null)
            go.SetParent(parent);

        Selection.Select(go);
    }
}
