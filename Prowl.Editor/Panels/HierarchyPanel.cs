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
    private string? _renamingId; // Identifier GUID string of object being renamed
    private string _renameText = "";
    private const float RowHeight = 22f;
    private const float IndentSize = 16f;

    // Track expanded state per GO identifier
    private static readonly Dictionary<string, bool> _expandedState = new();

    public override void OnGUI(Paper paper, float width, float height)
    {
        _paper = paper;
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        var scene = Scene.Current;

        using (paper.Column("hier_root").Size(width, height).Enter())
        {
            // Toolbar
            DrawToolbar(paper, font, width);

            if (scene == null)
            {
                paper.Box("hier_empty").Height(60)
                    .Text("No Scene Loaded", font)
                    .TextColor(EditorTheme.TextDisabled)
                    .FontSize(EditorTheme.FontSize)
                    .Alignment(TextAlignment.MiddleCenter);

                EditorGUI.Button(paper, "hier_create_scene", $"{EditorIcons.Plus}  New Scene", width: 120)
                    .OnValueChanged(_ => SceneViewPanel.CreateAndLoadDefaultScene());
                return;
            }

            // Scene name header
            paper.Box("hier_scene_name")
                .Height(22).ChildLeft(8)
                .BackgroundColor(EditorTheme.Darkest)
                .Text($"{EditorIcons.Film}  {scene.Name}", font)
                .TextColor(EditorTheme.Text)
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
                        .TextColor(EditorTheme.TextDisabled)
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

                // Right-click on empty space
                ContextMenuHelper.RightClickMenu(paper, "hier_bg_ctx", builder =>
                {
                    BuildCreateMenu(builder, null);
                });

                // Accept asset drops — spawn at root
                if (DragDrop.IsDraggingType<AssetDragPayload>() && paper.IsParentHovered)
                {
                    // Highlight drop zone
                    paper.Box("hier_drop_zone").Height(24)
                        .BackgroundColor(Color.FromArgb(40, EditorTheme.Accent))
                        .Rounded(3)
                        .Text("Drop to spawn here", font)
                        .TextColor(EditorTheme.Accent)
                        .FontSize(EditorTheme.FontSize - 2)
                        .Alignment(TextAlignment.MiddleCenter);
                }

                if (!DragDrop.IsDragging && DragDrop.Payload is AssetDragPayload assetDrop && paper.IsParentHovered)
                {
                    SpawnAssetInScene(assetDrop, null, Float3.Zero);
                    DragDrop.EndDrag();
                }
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
                .Text(EditorIcons.Plus, font).TextColor(EditorTheme.Text)
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

        using (paper.Row($"hier_go_{goId}")
            .Height(RowHeight)
            .BackgroundColor(isSelected ? EditorTheme.Accent : Color.Transparent)
            .Hovered.BackgroundColor(isSelected ? EditorTheme.Accent : EditorTheme.ButtonHovered).End()
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
            .Enter())
        {
            // Expand arrow
            if (hasChildren)
            {
                paper.Box($"hier_arr_{goId}")
                    .Width(14).Height(RowHeight)
                    .Text(isExpanded ? EditorIcons.AngleDown : EditorIcons.AngleRight, font)
                    .TextColor(EditorTheme.TextDim)
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
                .TextColor(go.EnabledInHierarchy ? EditorTheme.TextDim : EditorTheme.TextDisabled)
                .FontSize(11f).Alignment(TextAlignment.MiddleCenter);

            // Name or rename field
            if (_renamingId == goId)
            {
                EditorGUI.TextField(paper, $"hier_rename_{goId}", "", _renameText)
                    .OnValueChanged(v => _renameText = v);
                if (_paper?.IsKeyDown(PaperKey.Enter) == true || _paper?.IsKeyDown(PaperKey.KeypadEnter) == true)
                {
                    if (!string.IsNullOrWhiteSpace(_renameText))
                        go.Name = _renameText;
                    _renamingId = null;
                }
                else if (_paper?.IsKeyDown(PaperKey.Escape) == true)
                    _renamingId = null;
            }
            else
            {
                paper.Box($"hier_name_{goId}")
                    .Height(RowHeight).ChildLeft(4)
                    .Text(go.Name, font)
                    .TextColor(go.EnabledInHierarchy ? EditorTheme.Text : EditorTheme.TextDisabled)
                    .FontSize(EditorTheme.FontSize - 1)
                    .Alignment(TextAlignment.MiddleLeft);
            }

            // Enable/disable toggle (eye icon)
            paper.Box($"hier_vis_{goId}")
                .Width(18).Height(RowHeight)
                .Text(go.Enabled ? EditorIcons.Eye : EditorIcons.EyeSlash, font)
                .TextColor(go.Enabled ? EditorTheme.TextDim : EditorTheme.TextDisabled)
                .FontSize(9f).Alignment(TextAlignment.MiddleCenter)
                .StopEventPropagation()
                .OnClick(go, (g, _) => g.Enabled = !g.Enabled);

            // Right-click context menu
            ContextMenuHelper.RightClickMenu(paper, $"hier_ctx_{goId}", builder =>
            {
                if (!Selection.IsSelected(go))
                    Selection.Select(go);

                BuildCreateMenu(builder, go);
                builder.Separator();

                builder.Item("Duplicate", () => DuplicateGameObject(go), icon: EditorIcons.Copy);
                builder.Item("Rename", () =>
                {
                    _renamingId = goId;
                    _renameText = go.Name;
                }, icon: EditorIcons.PenToSquare);
                builder.Item("Delete", () => DeleteGameObject(go), icon: EditorIcons.Trash);

                builder.Separator();
                builder.Item(go.Enabled ? "Disable" : "Enable", () => go.Enabled = !go.Enabled,
                    icon: go.Enabled ? EditorIcons.EyeSlash : EditorIcons.Eye);
            });
        }

        // Draw children
        if (isExpanded && hasChildren)
        {
            foreach (var child in go.Children)
                DrawGameObjectNode(paper, font, child, depth + 1, flatList, ref drawIndex);
        }
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
        _renamingId = go.Identifier.ToString();
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

        // Simple duplication: create new GO with same name, position, components
        // TODO: Deep clone via serialization
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

        // Remove from selection
        if (Selection.IsSelected(go))
            Selection.RemoveFromSelection(go);

        // Remove all children recursively
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
            // Generic asset — just create an empty GO with the name
            Runtime.Debug.Log($"Spawned empty GameObject for asset type: {asset.GetType().Name}");
        }

        scene.Add(go);
        if (parent != null)
            go.SetParent(parent);

        Selection.Select(go);
    }
}
