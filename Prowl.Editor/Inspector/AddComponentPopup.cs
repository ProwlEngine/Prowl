using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Prowl.Editor.Widgets;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;

using Color = System.Drawing.Color;

namespace Prowl.Editor.Inspector;

/// <summary>
/// Searchable modal popup for adding components to a GameObject.
/// Components are organized by [AddComponentMenu] categories.
/// </summary>
public static class AddComponentPopup
{
    private static bool _isOpen;
    private static GameObject? _targetGo;
    private static string _searchText = "";
    private static List<ComponentEntry>? _cachedComponents;

    private struct ComponentEntry
    {
        public string Path;     // Full path e.g. "Physics/Colliders/Box Collider"
        public string Category; // e.g. "Physics/Colliders"
        public string Name;     // e.g. "Box Collider"
        public string Icon;
        public Type Type;
    }

    /// <summary>Clear cached component list so it re-scans assemblies on next Open.</summary>
    public static void Reinitialize() => _cachedComponents = null;

    public static bool IsOpen => _isOpen;

    private static OrigamiUI.IModal? _modal;

    public static void Open(GameObject target)
    {
        _isOpen = true;
        _targetGo = target;
        _searchText = "";
        _cachedComponents ??= GatherComponents();
        _modal = new OrigamiUI.CustomDrawModal((p, layer, _) => DrawInternal(p, layer)) { CloseOnBackdrop = true };
        Modal.Push(_modal);
    }

    public static void Close()
    {
        _isOpen = false;
        _targetGo = null;
        if (_modal != null) { Modal.Remove(_modal); _modal = null; }
    }

    public static void Draw(Paper paper) { } // Now handled by modal stack

    private static void DrawInternal(Paper paper, int layer)
    {
        if (!_isOpen || _targetGo == null) return;
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        // Modal window (backdrop handled by modal stack)
        using (paper.Column("acp_modal")
            .Size(320, 450)
            .Margin(UnitValue.StretchOne)
            .BackgroundColor(EditorTheme.Neutral300)
            .BorderColor(EditorTheme.Ink200).BorderWidth(1).Rounded(8)
            .Layer(layer)
            .StopEventPropagation()
            .Enter())
        {
            // Header
            using (paper.Row("acp_header")
                .Height(32).ChildLeft(12).ChildRight(8).RowBetween(8)
                .BackgroundColor(EditorTheme.Neutral200)
                .Enter())
            {
                paper.Box("acp_title").Height(32)
                    .Text("Add Component", font)
                    .TextColor(EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

                paper.Box("acp_spacer");

                paper.Box("acp_close")
                    .Width(24).Height(24).Rounded(4)
                    .Hovered.BackgroundColor(Color.FromArgb(255, 180, 60, 60)).End()
                    .Text(EditorIcons.Xmark, font).TextColor(EditorTheme.Ink400)
                    .FontSize(12f).Alignment(TextAlignment.MiddleCenter)
                    .OnClick(0, (_, _) => Close());
            }

            // Search bar
            using (paper.Row("acp_search_row")
                .Height(28).ChildLeft(8).ChildRight(8).ChildTop(4).ChildBottom(4)
                .Enter())
            {
                Origami.SearchField(paper, "acp_search", _searchText, v => _searchText = v, "Search components...").Show();
            }

            // Component list
            Origami.ScrollView(paper, "acp_scroll", 320, 450 - 32 - 28 - 4)
                .Padding(4, 4, 4, 0)
                .Body(() =>
            {
                var components = _cachedComponents ?? new List<ComponentEntry>();
                bool hasSearch = !string.IsNullOrEmpty(_searchText);

                if (hasSearch)
                {
                    // Flat filtered list
                    var filtered = components.Where(c =>
                        c.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ||
                        c.Path.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (filtered.Count == 0)
                    {
                        paper.Box("acp_empty").Height(40)
                            .Text("No matching components", font)
                            .TextColor(EditorTheme.Ink300)
                            .FontSize(EditorTheme.FontSize - 2).Alignment(TextAlignment.MiddleCenter);
                    }

                    for (int i = 0; i < filtered.Count; i++)
                    {
                        var comp = filtered[i];
                        DrawComponentItem(paper, font, $"acp_item_{i}", comp);
                    }
                }
                else
                {
                    // Grouped by category
                    var grouped = components
                        .GroupBy(c => c.Category)
                        .OrderBy(g => g.Key)
                        .ToList();

                    foreach (var group in grouped)
                    {
                        string category = string.IsNullOrEmpty(group.Key) ? "Uncategorized" : group.Key;

                        // Category header
                        paper.Box($"acp_cat_{category.GetHashCode()}")
                            .Height(20).ChildLeft(8)
                            .Text(category, font)
                            .TextColor(EditorTheme.Ink400)
                            .FontSize(EditorTheme.FontSize - 3).Alignment(TextAlignment.MiddleLeft);

                        foreach (var comp in group.OrderBy(c => c.Name))
                        {
                            DrawComponentItem(paper, font, $"acp_item_{comp.Type.Name}", comp);
                        }

                        // Small separator
                        paper.Box($"acp_sep_{category.GetHashCode()}")
                            .Height(1).Margin(8, 3, 8, 3)
                            .BackgroundColor(EditorTheme.Ink200);
                    }
                }
            });
        }
    }

    private static void DrawComponentItem(Paper paper, Prowl.Scribe.FontFile font, string id, ComponentEntry comp)
    {
        using (paper.Row(id)
            .Height(EditorTheme.RowHeight)
            .Hovered.BackgroundColor(EditorTheme.Purple400).End()
            .Rounded(3).ChildLeft(8).RowBetween(6)
            .OnClick(comp.Type, (type, _) =>
            {
                if (_targetGo != null)
                {
                    var addedComp = _targetGo.AddComponent(type);
                    if (addedComp != null)
                    {
                        var compId = addedComp.Identifier;
                        var goId = _targetGo.Identifier;
                        var serialized = Echo.Serializer.Serialize(addedComp.GetType(), addedComp);
                        var compType = addedComp.GetType();
                        Undo.RegisterAction("Add Component",
                            undo: () => { var g = Undo.FindGO(goId); if (g == null) return; var c = g.GetComponentByIdentifier(compId); if (c != null) g.RemoveComponent(c); },
                            redo: () => { var g = Undo.FindGO(goId); if (g == null) return; var c = Echo.Serializer.Deserialize(serialized, compType) as MonoBehaviour; if (c != null) { c.Identifier = compId; g.AddComponent(c); } });
                    }
                    Close();
                }
            })
            .Enter())
        {
            // Icon
            paper.Box($"{id}_ico")
                .Width(16).Height(EditorTheme.RowHeight)
                .Text(comp.Icon, font).TextColor(EditorTheme.Ink400)
                .FontSize(11f).Alignment(TextAlignment.MiddleCenter);

            // Name
            paper.Box($"{id}_name")
                .Height(EditorTheme.RowHeight)
                .Text(comp.Name, font).TextColor(EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSize - 1).Alignment(TextAlignment.MiddleLeft);
        }
    }

    private static List<ComponentEntry> GatherComponents()
    {
        var result = new List<ComponentEntry>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch { continue; }

            foreach (var type in types)
            {
                if (!typeof(MonoBehaviour).IsAssignableFrom(type) || type.IsAbstract) continue;
                if (type == typeof(MonoBehaviour)) continue;
                if (type.Name == "MissingMonobehaviour") continue;

                var menuAttr = type.GetCustomAttribute<AddComponentMenuAttribute>();
                string path = menuAttr?.Path ?? type.Name;
                string icon = menuAttr?.Icon ?? "";

                int lastSlash = path.LastIndexOf('/');
                string category = lastSlash >= 0 ? path[..lastSlash] : "";
                string name = lastSlash >= 0 ? path[(lastSlash + 1)..] : path;

                // Default icons by category
                if (string.IsNullOrEmpty(icon))
                {
                    icon = category switch
                    {
                        var c when c.StartsWith("Rendering") => EditorIcons.Cube,
                        var c when c.StartsWith("Audio") => EditorIcons.VolumeHigh,
                        var c when c.StartsWith("Light") => EditorIcons.Sun,
                        var c when c.Contains("Collider") => EditorIcons.VectorSquare,
                        var c when c.Contains("Constraint") || c.Contains("Joint") => EditorIcons.Link,
                        var c when c.StartsWith("Physics") => EditorIcons.Atom,
                        var c when c.StartsWith("UI") => EditorIcons.Desktop,
                        var c when c.StartsWith("Effects") => EditorIcons.Burst,
                        var c when c.StartsWith("Terrain") => EditorIcons.Mountain,
                        _ => EditorIcons.PuzzlePiece
                    };
                }

                result.Add(new ComponentEntry
                {
                    Path = path,
                    Category = category,
                    Name = name,
                    Icon = icon,
                    Type = type
                });
            }
        }

        return result;
    }
}
