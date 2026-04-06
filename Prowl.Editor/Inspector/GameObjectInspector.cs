using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

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
        DrawHeader(paper, font, go);
        EditorGUI.Separator(paper, "gi_sep_header");
        DrawTransform(paper, font, go);
        EditorGUI.Separator(paper, "gi_sep_transform");
        DrawComponents(paper, font, go);
        DrawAddComponentButton(paper, font, go);
        paper.Box("gi_bottom_pad").Height(20);
    }

    // ================================================================
    //  Header: Name, Enabled, Tag, Layer
    // ================================================================

    private static void DrawHeader(Paper paper, Prowl.Scribe.FontFile font, GameObject go)
    {
        // Enabled toggle + Name
        using (paper.Row("gi_header")
            .Height(EditorTheme.RowHeight)
            .Margin(0, 6)
            .RowBetween(6)
            .Enter())
        {
            paper.Box("gi_icon").Margin(6, 6, 0, 6).FontSize(EditorTheme.FontSize * 1.5f).Width(UnitValue.Auto).Text(EditorIcons.Cube, font);

            // Enabled checkbox
            EditorGUI.Toggle(paper, "gi_enabled", "", go.Enabled)
                .OnValueChanged(v => go.Enabled = v);

            // Name
            EditorGUI.TextField(paper, "gi_name", "", go.Name)
                .OnValueChanged(v => { if (!string.IsNullOrWhiteSpace(v)) go.Name = v; });
        }

        // Tag + Layer row
        using (paper.Row("gi_tag_layer")
            .Height(22)
            .RowBetween(6)
            .RowBetween(6)
            .Enter())
        {
            paper.Box("gi_tag_lbl").Width(30).Height(22)
                .Text("Tag", font).TextColor(EditorTheme.TextDim)
                .FontSize(EditorTheme.FontSize - 2).Alignment(TextAlignment.MiddleRight);

            EditorGUI.TextField(paper, "gi_tag", "", go.Tag)
                .OnValueChanged(v => go.Tag = v);

            paper.Box("gi_layer_lbl").Width(36).Height(22)
                .Text("Layer", font).TextColor(EditorTheme.TextDim)
                .FontSize(EditorTheme.FontSize - 2).Alignment(TextAlignment.MiddleRight);

            EditorGUI.TextField(paper, "gi_layer", "", go.Layer)
                .OnValueChanged(v => go.Layer = v);
        }

        // Static toggle
        using (paper.Row("gi_static_row")
            .Height(20)
            .RowBetween(6)
            .RowBetween(4)
            .Enter())
        {
            EditorGUI.Toggle(paper, "gi_static", "Static", go.IsStatic)
                .OnValueChanged(v => go.IsStatic = v);
        }
    }

    // ================================================================
    //  Transform
    // ================================================================

    private static void DrawTransform(Paper paper, Prowl.Scribe.FontFile font, GameObject go)
    {
        var t = go.Transform;
        
        paper.Box("gi_transform_header").Height(22).ChildLeft(8)
            .Text($"{EditorIcons.ArrowsUpDownLeftRight}  Transform", font)
            .TextColor(EditorTheme.Ink500)
            .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

        // Position
        var pos = t.LocalPosition;
        EditorGUI.Vector3Field(paper, "gi_pos", "Position", pos)
            .OnValueChanged(v => t.LocalPosition = v);

        // Rotation (as euler)
        var euler = t.LocalEulerAngles;
        EditorGUI.Vector3Field(paper, "gi_rot", "Rotation", euler)
            .OnValueChanged(v => t.LocalEulerAngles = v);

        // Scale
        var scale = t.LocalScale;
        EditorGUI.Vector3Field(paper, "gi_scale", "Scale", scale)
            .OnValueChanged(v => t.LocalScale = v);
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

            // Component foldout header
            using (paper.Row($"{compId}_header")
                .Height(24)
                .BackgroundColor(EditorTheme.Neutral300)
                .Rounded(3)
                .ChildLeft(4)
                .RowBetween(4)
                .Enter())
            {
                // Enabled toggle
                EditorGUI.Toggle(paper, $"{compId}_en", "", comp.Enabled)
                    .OnValueChanged(v => comp.Enabled = v);

                // Icon + Name (click to fold)
                paper.Box($"{compId}_label")
                    .Height(24)
                    .Text($"{icon}  {compName}", font)
                    .TextColor(comp.Enabled ? EditorTheme.Text : EditorTheme.TextDisabled)
                    .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

                // Spacer
                paper.Box($"{compId}_spacer");

                // Context menu button
                using (paper.Box($"{compId}_gear")
                    .Width(20).Height(24).Rounded(3)
                    .Hovered.BackgroundColor(EditorTheme.ButtonHovered).End()
                    .Text(EditorIcons.EllipsisVertical, font).TextColor(EditorTheme.TextDim)
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

        builder.Item("Remove Component", () =>
        {
            go.RemoveComponent(comp);
        }, icon: EditorIcons.Trash, enabled: comp.CanDestroy());

        builder.Separator();

        builder.Item("Move Up", () =>
        {
            if (index > 0) comp.SetSiblingIndex(index - 1);
        }, icon: EditorIcons.ArrowUp, enabled: index > 0);

        builder.Item("Move Down", () =>
        {
            comp.SetSiblingIndex(index + 1);
        }, icon: EditorIcons.ArrowDown);
    }

    // ================================================================
    //  [Button] Methods
    // ================================================================

    private static void DrawButtonMethods(Paper paper, string id, MonoBehaviour comp)
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
                .BackgroundColor(EditorTheme.ButtonNormal)
                .Hovered.BackgroundColor(EditorTheme.ButtonHovered).End()
                .Text($"{EditorIcons.Plus}  Add Component", font)
                .TextColor(EditorTheme.Text)
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
