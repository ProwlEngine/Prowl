using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Prowl.Editor.Prefabs;
using Prowl.Editor.Widgets;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.Editor.Inspector;

/// <summary>
/// Draws the inspector for a selected GameObject: header, transform, components, add component button.
/// </summary>
public static class GameObjectInspector
{
    public static void Draw(Paper paper, Prowl.Scribe.FontFile font, GameObject go)
    {
        // Prefab header bar (if this GO is a prefab instance)
        if (go.IsPrefabInstance)
            DrawPrefabHeader(paper, font, go);

        DrawHeader(paper, font, go);
        EditorGUI.Separator(paper, "gi_sep_header");
        DrawTransform(paper, font, go);
        EditorGUI.Separator(paper, "gi_sep_transform");
        DrawComponents(paper, font, go);

        // Only show Add Component if not a prefab instance (structure is fixed)
        if (!go.IsPrefabInstance || Application.IsPlaying)
            DrawAddComponentButton(paper, font, go);

        // Detect GO-level overrides (Name, Tag, Transform, etc.)
        if (go.IsPrefabInstance)
            PrefabUtility.DetectGOOverrides(go);

        paper.Box("gi_bottom_pad").Height(20);
    }

    // ================================================================
    //  Header: Name, Enabled, Tag, Layer
    // ================================================================

    private static void DrawHeader(Paper paper, Prowl.Scribe.FontFile font, GameObject go)
    {
        var goId = go.Identifier;

        // Enabled toggle + Name + Static
        using (paper.Row("gi_header")
            .Height(EditorTheme.RowHeight)
            .Margin(0, 6)
            .RowBetween(6)
            .Enter())
        {
            paper.Box("gi_icon").Margin(6, 6, 0, 6).FontSize(EditorTheme.FontSize * 1.5f).Width(UnitValue.Auto).Text(EditorIcons.Cube, font);

            EditorGUI.Toggle(paper, "gi_enabled", "", go.Enabled)
                .OnValueChanged(v => { var old = go.Enabled; Undo.RegisterAction("Toggle Enabled", () => { var g = Undo.FindGO(goId); if (g != null) g.Enabled = old; }, () => { var g = Undo.FindGO(goId); if (g != null) g.Enabled = v; }); go.Enabled = v; });

            EditorGUI.TextField(paper, "gi_name", "", go.Name)
                .OnValueChanged(v => { if (!string.IsNullOrWhiteSpace(v)) { var old = go.Name; Undo.RegisterCoalescableAction("Change Name", () => { var g = Undo.FindGO(goId); if (g != null) g.Name = old; }, () => { var g = Undo.FindGO(goId); if (g != null) g.Name = v; }); go.Name = v; } });

            EditorGUI.Toggle(paper, "gi_static", "Static", go.IsStatic)
                .OnValueChanged(v => { var old = go.IsStatic; Undo.RegisterAction("Toggle Static", () => { var g = Undo.FindGO(goId); if (g != null) g.IsStatic = old; }, () => { var g = Undo.FindGO(goId); if (g != null) g.IsStatic = v; }); go.IsStatic = v; });
        }

        // Tag + Layer row (dropdowns)
        using (paper.Row("gi_tag_layer")
            .Height(22)
            .RowBetween(6)
            .Enter())
        {
            // Tag dropdown
            var tagNames = TagLayerManager.tags.ToArray();
            int tagIdx = TagLayerManager.tags.IndexOf(go.Tag);
            if (tagIdx < 0) tagIdx = 0;

            EditorGUI.Dropdown(paper, "gi_tag", "Tag", tagIdx, tagNames, autoLabelWidth: true)
                .OnValueChanged(v => { if (v >= 0 && v < tagNames.Length) { var old = go.Tag; var newTag = tagNames[v]; Undo.RegisterAction("Change Tag", () => { var g = Undo.FindGO(goId); if (g != null) g.Tag = old; }, () => { var g = Undo.FindGO(goId); if (g != null) g.Tag = newTag; }); go.Tag = newTag; } });

            // Layer dropdown (filter out empty entries)
            var allLayers = TagLayerManager.layers;
            var layerNames = new List<string>();
            var layerIndices = new List<int>();
            for (int i = 0; i < allLayers.Length; i++)
            {
                if (!string.IsNullOrEmpty(allLayers[i]))
                {
                    layerNames.Add(allLayers[i]);
                    layerIndices.Add(i);
                }
            }

            int selectedLayerIdx = layerIndices.IndexOf(go.LayerIndex);
            if (selectedLayerIdx < 0) selectedLayerIdx = 0;

            EditorGUI.Dropdown(paper, "gi_layer", "Layer", selectedLayerIdx, layerNames.ToArray(), autoLabelWidth: true)
                .OnValueChanged(v => { if (v >= 0 && v < layerIndices.Count) { var old = go.LayerIndex; var newIdx = layerIndices[v]; Undo.RegisterAction("Change Layer", () => { var g = Undo.FindGO(goId); if (g != null) g.LayerIndex = old; }, () => { var g = Undo.FindGO(goId); if (g != null) g.LayerIndex = newIdx; }); go.LayerIndex = newIdx; } });
        }
    }

    // ================================================================
    //  Transform
    // ================================================================

    private static void DrawTransform(Paper paper, Prowl.Scribe.FontFile font, GameObject go)
    {
        var t = go.Transform;
        var goId = go.Identifier;

        paper.Box("gi_transform_header").Height(22).ChildLeft(8)
            .Text($"{EditorIcons.ArrowsUpDownLeftRight}  Transform", font)
            .TextColor(EditorTheme.Ink500)
            .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

        // Position
        var pos = t.LocalPosition;
        EditorGUI.Vector3Field(paper, "gi_pos", "Position", pos)
            .OnValueChanged(v => { var old = t.LocalPosition; Undo.RegisterCoalescableAction("Change Position", () => { var g = Undo.FindGO(goId); if (g != null) g.Transform.LocalPosition = old; }, () => { var g = Undo.FindGO(goId); if (g != null) g.Transform.LocalPosition = v; }); t.LocalPosition = v; });

        // Rotation (as euler)
        var euler = t.LocalEulerAngles;
        EditorGUI.Vector3Field(paper, "gi_rot", "Rotation", euler)
            .OnValueChanged(v => { var old = t.LocalEulerAngles; Undo.RegisterCoalescableAction("Change Rotation", () => { var g = Undo.FindGO(goId); if (g != null) g.Transform.LocalEulerAngles = old; }, () => { var g = Undo.FindGO(goId); if (g != null) g.Transform.LocalEulerAngles = v; }); t.LocalEulerAngles = v; });

        // Scale
        var scale = t.LocalScale;
        EditorGUI.Vector3Field(paper, "gi_scale", "Scale", scale)
            .OnValueChanged(v => { var old = t.LocalScale; Undo.RegisterCoalescableAction("Change Scale", () => { var g = Undo.FindGO(goId); if (g != null) g.Transform.LocalScale = old; }, () => { var g = Undo.FindGO(goId); if (g != null) g.Transform.LocalScale = v; }); t.LocalScale = v; });
    }

    // ================================================================
    //  Components
    // ================================================================

    private static void DrawComponents(Paper paper, Prowl.Scribe.FontFile font, GameObject go)
    {
        var components = go.GetComponents<MonoBehaviour>().ToList();

        for (int i = 0; i < components.Count; i++)
        {
            var comp = components[i];
            if (comp.HideFlags.HasFlag(HideFlags.Hide)) continue;

            string compId = $"gi_comp_{comp.Identifier}";
            string compName = comp.GetType().Name;
            string icon = GetComponentIcon(comp);

            // Component foldout header — draggable for component references
            using (paper.Row($"{compId}_header")
                .Height(24)
                .BackgroundColor(EditorTheme.Neutral300)
                .Rounded(3)
                .ChildLeft(4)
                .RowBetween(4)
                .OnDragStart((go, comp), (cap, _) =>
                {
                    DragDrop.StartDrag(new ComponentDragPayload(cap.Item1, cap.Item2));
                })
                .Enter())
            {
                // Enabled toggle
                EditorGUI.Toggle(paper, $"{compId}_en", "", comp.Enabled)
                    .OnValueChanged(v => { var old = comp.Enabled; var cId = comp.Identifier; Undo.RegisterAction("Toggle Component", () => { var c = Undo.FindComponent(cId); if (c != null) c.Enabled = old; }, () => { var c = Undo.FindComponent(cId); if (c != null) c.Enabled = v; }); comp.Enabled = v; });

                // Icon + Name (click to fold)
                paper.Box($"{compId}_label")
                    .Height(24)
                    .Text($"{icon}  {compName}", font)
                    .TextColor(comp.Enabled ? EditorTheme.Ink500 : EditorTheme.Ink300)
                    .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

                // Spacer
                paper.Box($"{compId}_spacer");

                // Context menu button
                using (paper.Box($"{compId}_gear")
                    .Width(20).Height(24).Rounded(3)
                    .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                    .Text(EditorIcons.EllipsisVertical, font).TextColor(EditorTheme.Ink400)
                    .FontSize(11f).Alignment(TextAlignment.MiddleCenter)
                    .Enter())
                {
                    if (paper.IsParentHovered)
                    {
                        var ctxBuilder = new ContextMenuBuilder();
                        BuildComponentContextMenu(ctxBuilder, go, comp, i);
                        ctxBuilder.Render(paper, $"{compId}_ctx", 0, 24);
                    }
                }
            }

            // Set overridden field names for PropertyGrid highlighting
            if (go.IsPrefabInstance)
            {
                string goPath = PrefabUtility.BuildGOPath(go);
                var allComps = go.GetComponents<MonoBehaviour>().ToList();
                int compIdx = allComps.IndexOf(comp);
                string pathPrefix = string.IsNullOrEmpty(goPath)
                    ? $"c{compIdx}."
                    : $"{goPath}.c{compIdx}.";

                var overridden = new HashSet<string>();
                foreach (var ov in go.PrefabOverrides)
                {
                    if (ov.Path.StartsWith(pathPrefix))
                        overridden.Add(ov.Path[pathPrefix.Length..].Split('.')[0]);
                }
                PropertyGrid.OverriddenFields = overridden.Count > 0 ? overridden : null;
            }

            // Component body — use custom editor or default PropertyGrid
            var customEditor = ComponentEditorRegistry.GetEditor(comp.GetType());
            if (customEditor != null)
            {
                customEditor.OnGUI(paper, compId, comp);
            }
            else
            {
                PropertyGrid.Draw(paper, compId, comp);
            }

            PropertyGrid.OverriddenFields = null;

            // Auto-detect overrides by comparing against prefab source (index-based)
            if (go.IsPrefabInstance)
                PrefabUtility.DetectComponentOverrides(go, comp);

            // Draw [Button] attributed methods
            DrawButtonMethods(paper, $"{compId}_btns", comp);

            EditorGUI.Separator(paper, $"{compId}_sep");
        }
    }

    private static void BuildComponentContextMenu(ContextMenuBuilder builder, GameObject go, MonoBehaviour comp, int index)
    {
        builder.Item("Reset", () =>
        {
            // TODO: Reset to default values
            Runtime.Debug.Log($"Reset {comp.GetType().Name}");
        }, icon: EditorIcons.ArrowsRotate);

        builder.Separator();

        bool canRemove = comp.CanDestroy();
        // Block removing prefab components in editor
        if (go.IsPrefabInstance && go.PrefabComponentCount >= 0)
        {
            int compIdx = go._components.IndexOf(comp);
            if (compIdx >= 0 && compIdx < go.PrefabComponentCount)
                canRemove = false;
        }
        builder.Item("Remove Component", () =>
        {
            var serialized = Echo.Serializer.Serialize(comp.GetType(), comp);
            var compType = comp.GetType();
            var compId = comp.Identifier;
            var goId = go.Identifier;
            Undo.RegisterAction("Remove Component",
                undo: () =>
                {
                    var g = Undo.FindGO(goId);
                    if (g == null) return;
                    var restored = Echo.Serializer.Deserialize(serialized, compType) as MonoBehaviour;
                    if (restored != null) { restored.Identifier = compId; g.AddComponent(restored); }
                },
                redo: () =>
                {
                    var g = Undo.FindGO(goId);
                    if (g == null) return;
                    var c = g.GetComponentByIdentifier(compId);
                    if (c != null) g.RemoveComponent(c);
                });
            go.RemoveComponent(comp);
        }, icon: EditorIcons.Trash, enabled: canRemove);

        builder.Separator();

        var moveCompId = comp.Identifier;
        builder.Item("Move Up", () =>
        {
            if (index > 0)
            {
                var oldIdx = index; var newIdx = index - 1;
                Undo.RegisterAction("Move Component Up",
                    () => { var c = Undo.FindComponent(moveCompId); c?.SetSiblingIndex(oldIdx); },
                    () => { var c = Undo.FindComponent(moveCompId); c?.SetSiblingIndex(newIdx); });
                comp.SetSiblingIndex(newIdx);
            }
        }, icon: EditorIcons.ArrowUp, enabled: index > 0);

        builder.Item("Move Down", () =>
        {
            var oldIdx = index; var newIdx = index + 1;
            Undo.RegisterAction("Move Component Down",
                () => { var c = Undo.FindComponent(moveCompId); c?.SetSiblingIndex(oldIdx); },
                () => { var c = Undo.FindComponent(moveCompId); c?.SetSiblingIndex(newIdx); });
            comp.SetSiblingIndex(newIdx);
        }, icon: EditorIcons.ArrowDown);

        builder.Separator();

        builder.Item("Pop Out", () =>
        {
            Panels.ComponentPopoutPanel.PopOut(go, comp);
        }, icon: EditorIcons.ArrowUpRightFromSquare);
    }

    // ================================================================
    //  [Button] Methods
    // ================================================================

    public static void DrawButtonMethods(Paper paper, string id, MonoBehaviour comp)
    {
        var methods = comp.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        int btnIdx = 0;
        foreach (var method in methods)
        {
            var btnAttr = method.GetCustomAttribute<ButtonAttribute>();
            if (btnAttr == null) continue;
            if (method.GetParameters().Length > 0) continue; // Only parameterless methods

            string label = btnAttr.Label ?? NicifyName(method.Name);
            EditorGUI.Button(paper, $"{id}_{btnIdx++}", label)
                .OnValueChanged(_ => method.Invoke(comp, null));
        }
    }

    // ================================================================
    //  Add Component
    // ================================================================

    private static void DrawAddComponentButton(Paper paper, Prowl.Scribe.FontFile font, GameObject go)
    {
        using (paper.Row("gi_add_comp_row").Height(28).ChildLeft(20).ChildRight(20).Enter())
        {
            paper.Box("gi_add_comp")
                .Height(28).Rounded(4)
                .BackgroundColor(EditorTheme.Ink100)
                .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                .Text($"{EditorIcons.Plus}  Add Component", font)
                .TextColor(EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleCenter)
                .OnClick(go, (g, _) => AddComponentPopup.Open(g));
        }
    }

    private static void BuildAddComponentMenu(ContextMenuBuilder builder, GameObject go)
    {
        // Gather all MonoBehaviour types
        var componentTypes = new List<(string path, string icon, Type type)>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch { continue; }

            foreach (var type in types)
            {
                if (!typeof(MonoBehaviour).IsAssignableFrom(type) || type.IsAbstract) continue;
                if (type == typeof(MonoBehaviour)) continue;

                var menuAttr = type.GetCustomAttribute<AddComponentMenuAttribute>();
                string path = menuAttr?.Path ?? type.Name;
                string icon = menuAttr?.Icon ?? EditorIcons.PuzzlePiece;
                componentTypes.Add((path, icon, type));
            }
        }

        // Build menu tree from paths
        var categories = new Dictionary<string, List<(string name, string icon, Type type)>>();

        foreach (var (path, icon, type) in componentTypes.OrderBy(c => c.path))
        {
            int lastSlash = path.LastIndexOf('/');
            if (lastSlash >= 0)
            {
                string category = path[..lastSlash];
                string name = path[(lastSlash + 1)..];
                if (!categories.ContainsKey(category))
                    categories[category] = new();
                categories[category].Add((name, icon, type));
            }
            else
            {
                if (!categories.ContainsKey(""))
                    categories[""] = new();
                categories[""].Add((path, icon, type));
            }
        }

        // Root items
        if (categories.TryGetValue("", out var rootItems))
        {
            foreach (var (name, icon, type) in rootItems)
                builder.Item(name, () => go.AddComponent(type), icon: icon);
        }

        // Categories as submenus
        foreach (var (category, items) in categories.Where(kv => kv.Key != "").OrderBy(kv => kv.Key))
        {
            builder.Submenu(category, sub =>
            {
                foreach (var (name, icon, type) in items)
                    sub.Item(name, () => go.AddComponent(type), icon: icon);
            });
        }
    }

    // ================================================================
    //  Prefab Header
    // ================================================================

    private static void DrawPrefabHeader(Paper paper, Prowl.Scribe.FontFile font, GameObject go)
    {
        var root = PrefabUtility.GetPrefabInstanceRoot(go);
        bool isRoot = PrefabUtility.IsInstanceRoot(go);
        bool isNested = PrefabUtility.IsNestedPrefabRoot(go);
        bool hasOverrides = PrefabUtility.HasAnyOverrides(go);

        var entry = EditorAssetDatabase.Instance?.GetEntry(go.PrefabAssetId);
        bool isMissing = entry == null;
        string prefabName = isMissing ? "Missing" : System.IO.Path.GetFileNameWithoutExtension(entry!.Path);

        string label = isMissing ? "Missing Prefab!"
            : isNested ? $"Nested Prefab: {prefabName}"
            : $"Prefab: {prefabName}";

        var barColor = isMissing ? Color.FromArgb(40, 220, 80, 80) : Color.FromArgb(40, EditorTheme.Purple400);
        var borderColor = isMissing ? Color.FromArgb(255, 220, 80, 80) : EditorTheme.Purple300;
        var textColor = isMissing ? Color.FromArgb(255, 220, 80, 80) : EditorTheme.Purple400;

        // Entire prefab section in a purple-tinted container
        using (paper.Column("gi_prefab_section")
            .Height(UnitValue.Auto)
            .BackgroundColor(barColor)
            .BorderColor(borderColor).BorderWidth(1)
            .Rounded(4).Margin(0, 4, 0, 4)
            .ChildLeft(4).ChildRight(4).ChildTop(4).ChildBottom(4)
            .Enter())
        {
            // Top row: label + buttons
            using (paper.Row("gi_prefab_bar")
                .Height(24).RowBetween(4)
                .Enter())
            {
                paper.Box("gi_prefab_icon")
                    .Width(UnitValue.Stretch()).Height(24)
                    .Text($"{EditorIcons.Cubes}  {label}", font)
                    .TextColor(textColor)
                    .FontSize(EditorTheme.FontSize - 1).Alignment(TextAlignment.MiddleLeft);

                if (!isMissing)
                {
                    EditorGUI.Button(paper, "gi_prefab_select", "Select", width: 55)
                        .OnValueChanged(_ => Selection.FocusAsset(go.PrefabAssetId));

                    if (isRoot && hasOverrides)
                    {
                        EditorGUI.Button(paper, "gi_prefab_revert", "Revert", width: 55)
                            .OnValueChanged(_ => { if (root != null) PrefabUtility.RevertOverrides(root); });

                        EditorGUI.Button(paper, "gi_prefab_apply", "Apply", width: 50)
                            .OnValueChanged(_ => { if (root != null) PrefabUtility.ApplyOverrides(root); });
                    }
                }
            }

            // Overrides list inline
            if (hasOverrides)
            {
                paper.Box("gi_prefab_ov_sep").Height(1)
                    .BackgroundColor(borderColor).Margin(0, 2, 0, 2);

                DrawOverridesContent(paper, font, go);
            }
        }
    }

    private static void DrawOverridesContent(Paper paper, Prowl.Scribe.FontFile font, GameObject go)
    {
        float fs = EditorTheme.FontSize;
        var overrides = go.PrefabOverrides;

        using (paper.Column("gi_prefab_ov_list")
            .Height(UnitValue.Auto)
            .Margin(8, 0, 4, 4)
            .Enter())
        {
            for (int i = 0; i < overrides.Count; i++)
            {
                int idx = i;
                var ov = overrides[i];

                using (paper.Row($"gi_ov_{i}")
                    .Height(EditorTheme.RowHeight)
                    .BackgroundColor(EditorTheme.Neutral300)
                    .Rounded(3).Margin(0, 0, 0, 1)
                    .ChildLeft(6).RowBetween(4)
                    .Enter())
                {
                    // Override path
                    paper.Box($"gi_ov_path_{i}")
                        .Width(UnitValue.Stretch()).Height(EditorTheme.RowHeight)
                        .Text(ov.Path, font).TextColor(EditorTheme.Purple400)
                        .FontSize(fs - 2).Alignment(TextAlignment.MiddleLeft);

                    // Revert single
                    paper.Box($"gi_ov_revert_{i}")
                        .Width(50).Height(EditorTheme.RowHeight).Rounded(3)
                        .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                        .Text("Revert", font).TextColor(EditorTheme.Ink400)
                        .FontSize(fs - 2).Alignment(TextAlignment.MiddleCenter)
                        .OnClick((go, idx), (cap, _) =>
                        {
                            if (cap.idx < cap.go.PrefabOverrides.Count)
                            {
                                var overridePath = cap.go.PrefabOverrides[cap.idx].Path;
                                PrefabUtility.RevertSingleOverride(cap.go, overridePath);
                            }
                        });

                    // Apply single
                    paper.Box($"gi_ov_apply_{i}")
                        .Width(45).Height(EditorTheme.RowHeight).Rounded(3)
                        .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                        .Text("Apply", font).TextColor(EditorTheme.Ink400)
                        .FontSize(fs - 2).Alignment(TextAlignment.MiddleCenter)
                        .OnClick((go, idx), (cap, _) =>
                        {
                            if (cap.idx < cap.go.PrefabOverrides.Count)
                            {
                                var singleOv = cap.go.PrefabOverrides[cap.idx];
                                PrefabUtility.ApplySingleOverride(cap.go, singleOv);
                            }
                        });
                }
            }
        }
    }

    // ================================================================
    //  Helpers
    // ================================================================

    private static string GetComponentIcon(MonoBehaviour comp) => comp switch
    {
        Camera => EditorIcons.Camera,
        Light => EditorIcons.Sun,
        MeshRenderer => EditorIcons.Cube,
        ModelRenderer => EditorIcons.Cubes,
        _ => EditorIcons.PuzzlePiece
    };

    private static string NicifyName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        // Insert spaces before capitals: "MyMethod" -> "My Method"
        var result = new System.Text.StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                result.Append(' ');
            result.Append(name[i]);
        }
        return result.ToString();
    }
}
