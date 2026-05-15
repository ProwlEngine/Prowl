using System;
using System.Text.Json;

using Prowl.Editor.Docking;
using Prowl.Editor.Widgets;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;

using Color = System.Drawing.Color;

namespace Prowl.Editor.Panels;

[EditorWindow("General/Project Settings")]
public class ProjectSettingsPanel : DockPanel
{
    public override string Title => "Project Settings";
    public override string Icon => EditorIcons.Gear;

    private int _selectedIndex;

    // Shared with ProjectSettingsRegistry.Save keeping the serializer options identical
    // means the before/after JSON comparison is stable field-by-field.
    private static readonly JsonSerializerOptions s_jsonOpts = new()
    {
        WriteIndented = false,
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Register an undo record if <paramref name="instance"/> changed between the captured
    /// JSON and its current state. Used by the panel to wrap a settings OnGUI call so any
    /// widget mutation becomes undoable without each settings class needing to know
    /// about Undo.
    /// </summary>
    private static void DiffAndRegisterUndo(ProjectSettingsRegistry.SettingsEntry entry, string beforeJson)
    {
        if (Application.IsPlaying) return;

        string afterJson;
        try { afterJson = JsonSerializer.Serialize(entry.Instance, entry.Type, s_jsonOpts); }
        catch { return; }

        if (beforeJson == afterJson) return;

        var capturedEntry = entry;
        Undo.RegisterCoalescableAction($"Modify {entry.Name}",
            undo: () => ApplyJsonToEntry(capturedEntry, beforeJson),
            redo: () => ApplyJsonToEntry(capturedEntry, afterJson));
    }

    private static void ApplyJsonToEntry(ProjectSettingsRegistry.SettingsEntry entry, string json)
    {
        try
        {
            var loaded = (ProjectSettingsBase?)JsonSerializer.Deserialize(json, entry.Type, s_jsonOpts);
            if (loaded == null) return;
            ProjectSettingsRegistry.CopyFields(loaded, entry.Instance);
            entry.Instance.Apply();
            ProjectSettingsRegistry.Save(entry);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"ProjectSettings undo/redo failed for '{entry.Name}': {ex.Message}");
        }
    }


    public override void OnGUI(Paper paper, float width, float height)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        var entries = ProjectSettingsRegistry.Entries;
        if (entries.Count == 0)
        {
            paper.Box("ps_empty").Size(width, height)
                .Text("No settings registered", font)
                .TextColor(EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSize)
                .Alignment(TextAlignment.MiddleCenter);
            return;
        }

        if (_selectedIndex >= entries.Count) _selectedIndex = 0;

        using (paper.Row("ps_root").Size(width, height).Enter())
        {
            // Left sidebar category list
            float sidebarW = 220f;
            using (paper.Column("ps_sidebar")
                .Padding(new UnitValue(EditorTheme.SidePixelPadding))
                .Width(sidebarW).Height(height)
                .BackgroundColor(EditorTheme.Neutral200)
                .Enter())
            {
                paper.Box("ps_sidebar_header")
                    .Height(28).ChildLeft(8)
                    .Text("Settings", font).TextColor(EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

                Origami.Separator(paper, "ps_sidebar_sep").Show();

                for (int i = 0; i < entries.Count; i++)
                {
                    int idx = i;
                    var entry = entries[i];

                    if (entry.Instance == null || !entry.Instance.DrawInProjectSettingsPanel) continue;

                    bool isSelected = _selectedIndex == i;
                    string icon = string.IsNullOrEmpty(entry.Icon) ? EditorIcons.Gear : entry.Icon;

                    paper.Box($"ps_cat_{i}")
                        .Height(30).ChildLeft(8).Rounded(3)
                        .Margin(0,0,0,EditorTheme.VerticalNavbarSpacing)
                        .BackgroundColor(isSelected ? EditorTheme.Purple400 : Color.Transparent)
                        .Hovered.BackgroundColor(isSelected ? EditorTheme.Purple400 : EditorTheme.Ink200).End()
                        .Text($" {icon}  {entry.Name}", font)
                        .TextColor(isSelected ? EditorTheme.Ink500 : EditorTheme.Ink400)
                        .FontSize(EditorTheme.FontSize - 1)
                        .Alignment(TextAlignment.MiddleLeft)
                        .OnClick(idx, (id, _) => _selectedIndex = id);
                }
            }

            // Separator
            paper.Box("ps_divider").Width(1).Height(height).BackgroundColor(EditorTheme.Ink200);

            // Right content selected settings.
            // Wrap the draw with a JSON snapshot: System.Text.Json is the same serializer
            // used to persist settings, so it handles properties, fields, lists, arrays
            // uniformly (unlike Echo which is fields-only). Anything the user changes in
            // OnGUI becomes a coalescable undo step keyed by the settings name.
            float contentW = width - sidebarW - 1;
            var currentEntry = entries[_selectedIndex];
            string? beforeJson = null;
            if (!Application.IsPlaying)
            {
                try { beforeJson = JsonSerializer.Serialize(currentEntry.Instance, currentEntry.Type, s_jsonOpts); }
                catch { beforeJson = null; }
            }

            Origami.ScrollView(paper, "ps_content", contentW, height)
                .Padding(EditorTheme.SidePixelPadding, EditorTheme.SidePixelPadding, EditorTheme.SidePixelPadding, EditorTheme.SidePixelPadding)
                .Body(() =>
            {
                paper.Box("ps_content_pad").Height(8);
                currentEntry.Instance.OnGUI(paper, contentW - 16);
                paper.Box("ps_content_pad2").Height(16);
            });

            if (beforeJson != null) DiffAndRegisterUndo(currentEntry, beforeJson);
        }
    }
}
