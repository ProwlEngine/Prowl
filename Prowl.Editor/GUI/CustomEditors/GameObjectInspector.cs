using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Prowl.Editor.Prefabs;
using Prowl.Editor.GUI;
using Prowl.Editor.GUI.Popups;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Vector;

using Color = System.Drawing.Color;

using PropertyGridUtils = Prowl.Editor.GUI.PropertyGridUtils;
using Prowl.Editor.GUI.Panels;
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
        Origami.Separator(paper, "gi_sep_header").Show();
        DrawTransform(paper, font, go);
        Origami.Separator(paper, "gi_sep_transform").Show();
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

            Origami.Checkbox(paper, "gi_enabled", go.Enabled,
                v => { Undo.RecordGameObjectChange(go, "Toggle Enabled", go.Enabled, v, (g, x) => g.Enabled = x); go.Enabled = v; })
                .NoLabel().Show();

            Origami.TextField(paper, "gi_name", go.Name, v =>
                {
                    if (!string.IsNullOrWhiteSpace(v))
                    {
                        Undo.RecordGameObjectChange(go, "Change Name", go.Name, v, (g, x) => g.Name = x, coalesce: true);
                        go.Name = v;
                    }
                })
                .Width(UnitValue.Stretch())
                .Show();

            Origami.Checkbox(paper, "gi_static", go.IsStatic,
                v => { Undo.RecordGameObjectChange(go, "Toggle Static", go.IsStatic, v, (g, x) => g.IsStatic = x); go.IsStatic = v; })
                .LabelRight(Loc.Get("inspector.static")).Show();
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

            DrawInlineLabeled(paper, "gi_tag_row", Loc.Get("inspector.tag"), font, () =>
                Origami.Dropdown(paper, "gi_tag", tagIdx,
                    v =>
                    {
                        if (v >= 0 && v < tagNames.Length)
                        {
                            var newTag = tagNames[v];
                            Undo.RecordGameObjectChange(go, "Change Tag", go.Tag, newTag, (g, x) => g.Tag = x);
                            go.Tag = newTag;
                        }
                    }, tagNames)
                    .Show());

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

            DrawInlineLabeled(paper, "gi_layer_row", Loc.Get("inspector.layer"), font, () =>
                Origami.Dropdown(paper, "gi_layer", selectedLayerIdx,
                    v =>
                    {
                        if (v >= 0 && v < layerIndices.Count)
                        {
                            var newIdx = layerIndices[v];
                            Undo.RecordGameObjectChange(go, "Change Layer", go.LayerIndex, newIdx, (g, x) => g.LayerIndex = x);
                            go.LayerIndex = newIdx;
                        }
                    }, layerNames.ToArray())
                    .Show());
        }
    }

    /// <summary>
    /// Renders an auto-width label followed by the caller's control filling the remainder.
    /// Used for the Tag/Layer header where we want compact label gutters, not the inspector's
    /// fixed-width LabelWidth gutter.
    /// </summary>
    private static void DrawInlineLabeled(Paper paper, string id, string label,
        Prowl.Scribe.FontFile font, Action drawControl)
    {
        using (paper.Row(id).Width(UnitValue.Stretch()).Height(EditorTheme.RowHeight).RowBetween(4).Enter())
        {
            paper.Box($"{id}_lbl")
                .Width(UnitValue.Auto).Height(EditorTheme.RowHeight)
                .Margin(4, 4, 0, 0)
                .IsNotInteractable()
                .Text(label, font).TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSize);

            using (paper.Box($"{id}_ctl").Width(UnitValue.Stretch()).Height(EditorTheme.RowHeight).Enter())
            {
                drawControl();
            }
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
            .Text($"{EditorIcons.ArrowsUpDownLeftRight}  {Loc.Get("inspector.transform")}", font)
            .TextColor(EditorTheme.Ink500)
            .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

        // Position
        var pos = t.LocalPosition;
        InspectorRow.Draw(paper, "gi_pos", Loc.Get("inspector.position"), () =>
            Origami.Float3Field(paper, "gi_pos_vf", pos, v => { Undo.RecordGameObjectChange(go, "Change Position", t.LocalPosition, v, (g, x) => g.Transform.LocalPosition = x, coalesce: true); t.LocalPosition = v; }).Show());

        // Rotation (as euler)
        var euler = t.LocalEulerAngles;
        InspectorRow.Draw(paper, "gi_rot", Loc.Get("inspector.rotation"), () =>
            Origami.Float3Field(paper, "gi_rot_vf", euler, v => { Undo.RecordGameObjectChange(go, "Change Rotation", t.LocalEulerAngles, v, (g, x) => g.Transform.LocalEulerAngles = x, coalesce: true); t.LocalEulerAngles = v; }).Show());

        // Scale
        var scale = t.LocalScale;
        InspectorRow.Draw(paper, "gi_scale", Loc.Get("inspector.scale"), () =>
            Origami.Float3Field(paper, "gi_scale_vf", scale, v => { Undo.RecordGameObjectChange(go, "Change Scale", t.LocalScale, v, (g, x) => g.Transform.LocalScale = x, coalesce: true); t.LocalScale = v; }).Show());
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

            // Component foldout header draggable for component references
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
                Origami.Checkbox(paper, $"{compId}_en", comp.Enabled,
                    v => { var old = comp.Enabled; var cId = comp.Identifier; Undo.RegisterAction("Toggle Component", () => { var c = Undo.FindComponent(cId); if (c != null) { c.Enabled = old; c.OnValidate(); } }, () => { var c = Undo.FindComponent(cId); if (c != null) { c.Enabled = v; c.OnValidate(); } }); comp.Enabled = v; comp.OnValidate(); })
                    .NoLabel().Show();

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
                    .OnClick(i, (ci, _) =>
                    {
                        Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, b =>
                            BuildComponentContextMenu(b, go, comp, ci));
                    })
                    .Enter())
                {
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
                PropertyGridUtils.OverriddenFields = overridden.Count > 0 ? overridden : null;
            }

            // Component body use custom editor or default PropertyGrid
            var customEditor = CustomEditorRegistry.GetEditor(comp.GetType());
            if (customEditor != null)
            {
                customEditor.OnGUI(paper, compId, comp);
            }
            else
            {
                PropertyGridUtils.Draw(paper, compId, comp);
            }

            PropertyGridUtils.OverriddenFields = null;

            // Auto-detect overrides by comparing against prefab source (index-based)
            if (go.IsPrefabInstance)
                PrefabUtility.DetectComponentOverrides(go, comp);

            // Draw [Button] attributed methods
            DrawButtonMethods(paper, $"{compId}_btns", comp);

            Origami.Separator(paper, $"{compId}_sep").Show();
        }
    }

    private static void BuildComponentContextMenu(ContextBuilder builder, GameObject go, MonoBehaviour comp, int index)
    {
        builder.Item(Loc.Get("inspector.reset"), () =>
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
        builder.Item(Loc.Get("inspector.remove_component"), () =>
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
        builder.Item(Loc.Get("inspector.move_up"), () =>
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

        builder.Item(Loc.Get("inspector.move_down"), () =>
        {
            var oldIdx = index; var newIdx = index + 1;
            Undo.RegisterAction("Move Component Down",
                () => { var c = Undo.FindComponent(moveCompId); c?.SetSiblingIndex(oldIdx); },
                () => { var c = Undo.FindComponent(moveCompId); c?.SetSiblingIndex(newIdx); });
            comp.SetSiblingIndex(newIdx);
        }, icon: EditorIcons.ArrowDown);

        builder.Separator();

        builder.Item(Loc.Get("inspector.pop_out"), () =>
        {
            ComponentPopoutPanel.PopOut(go, comp);
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
            Origami.Button(paper, $"{id}_{btnIdx++}", label, () => { method.Invoke(comp, null); }).Show();
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
                .Text($"{EditorIcons.Plus}  {Loc.Get("inspector.add_component")}", font)
                .TextColor(EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleCenter)
                .OnClick(go, (g, _) => AddComponentPopup.Open(g));
        }
    }

    private static void BuildAddComponentMenu(ContextBuilder builder, GameObject go)
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
        string prefabName = isMissing ? Loc.Get("inspector.missing") : System.IO.Path.GetFileNameWithoutExtension(entry!.Path);

        string label = isMissing ? Loc.Get("inspector.missing_prefab")
            : isNested ? Loc.Get("inspector.nested_prefab", new { name = prefabName })
            : Loc.Get("inspector.prefab", new { name = prefabName });

        var barColor = isMissing ? Color.FromArgb(40, 220, 80, 80) : Color.FromArgb(40, EditorTheme.Purple400);
        var borderColor = isMissing ? Color.FromArgb(255, 220, 80, 80) : EditorTheme.Purple300;
        var textColor = isMissing ? Color.FromArgb(255, 220, 80, 80) : EditorTheme.Purple400;

        // Entire prefab section in a purple-tinted container
        using (paper.Column("gi_prefab_section")
            .Height(UnitValue.Auto)
            .BackgroundColor(barColor)
            .BorderColor(borderColor).BorderWidth(1)
            .Rounded(4).Margin(0, 4, 0, 4)
            .Padding(4, 4, 4, 4)
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
                    Origami.Button(paper, "gi_prefab_select", Loc.Get("inspector.select"), () => { Selection.Ping(go.PrefabAssetId); }).Width(55).Show();

                    if (isRoot && hasOverrides)
                    {
                        Origami.Button(paper, "gi_prefab_revert", Loc.Get("inspector.revert"), () => { if (root != null) PrefabUtility.RevertOverrides(root); }).Width(55).Show();

                        Origami.Button(paper, "gi_prefab_apply", Loc.Get("inspector.apply"), () => { if (root != null) PrefabUtility.ApplyOverrides(root); }).Width(50).Show();
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
                        .Text(Loc.Get("inspector.revert"), font).TextColor(EditorTheme.Ink400)
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
                        .Text(Loc.Get("inspector.apply"), font).TextColor(EditorTheme.Ink400)
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

    private static string GetComponentIcon(MonoBehaviour comp) => ComponentIconRegistry.GetIcon(comp);

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
