using System;
using System.Collections.Generic;
using System.Text.Json;

using Prowl.Editor.Core;
using Prowl.Editor.Projects.Settings;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Runtime;

using Color = System.Drawing.Color;

namespace Prowl.Editor.GUI.Panels;

[EditorWindow("General/Project Settings")]
public class ProjectSettingsPanel : DockPanel
{
    public override string Title => Loc.Get("panel.project_settings");
    public override string Icon => EditorIcons.Gear;

    private int _selectedIndex;

    // Shared with EditorRegistries.SaveSettings keeping the serializer options identical
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
    private static void DiffAndRegisterUndo(EditorRegistries.SettingsEntry entry, string beforeJson)
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

    private static void ApplyJsonToEntry(EditorRegistries.SettingsEntry entry, string json)
    {
        try
        {
            var loaded = (ProjectSettingsBase?)JsonSerializer.Deserialize(json, entry.Type, s_jsonOpts);
            if (loaded == null) return;
            EditorRegistries.CopySettingsFields(loaded, entry.Instance);
            entry.Instance.Apply();
            EditorRegistries.SaveSettings(entry);
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

        var entries = EditorRegistries.SettingsEntries;
        if (entries.Count == 0)
        {
            paper.Box("ps_empty").Size(width, height)
                .Text(Loc.Get("projset.none"), font)
                .TextColor(EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSize)
                .Alignment(TextAlignment.MiddleCenter);
            return;
        }

        if (_selectedIndex >= entries.Count) _selectedIndex = 0;

        // The entry index is used as the string id so the sidebar selection maps straight back to _selectedIndex.
        var cats = new List<(string id, string label, string icon)>();
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e.Instance == null || !e.Instance.DrawInProjectSettingsPanel) continue;
            string icon = string.IsNullOrEmpty(e.Icon) ? EditorIcons.Gear : e.Icon;
            cats.Add((i.ToString(), e.Name, icon));
        }

        using (paper.Row("ps_root").Size(width, height).Clip().Enter())
        {
            float side = EditorGUI.Sidebar(paper, "ps_side", cats.ToArray(), _selectedIndex.ToString(),
                c => { if (int.TryParse(c, out int idx)) _selectedIndex = idx; });
            paper.Box("ps_vdiv").Width(1).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();

            // TODO: Port to using Echo to serialize to match how Project Settings serialize
            float contentW = width - side - 1;
            var currentEntry = entries[_selectedIndex];
            string? beforeJson = null;
            if (!Application.IsPlaying)
            {
                try { beforeJson = JsonSerializer.Serialize(currentEntry.Instance, currentEntry.Type, s_jsonOpts); }
                catch { beforeJson = null; }
            }

            Origami.ScrollView(paper, "ps_scroll", contentW, height).Body(() =>
            {
                using (paper.Column("ps_content").Height(UnitValue.Auto).Padding(0, 0, 8, 12).Enter())
                {
                    EditorGUI.SectionHeader(paper, "ps_content_h", currentEntry.Name, first: true);
                    currentEntry.Instance.OnGUI(paper, contentW);
                    paper.Box("ps_content_pad").Height(EditorTheme.Padding * 4);
                }
            });

            if (beforeJson != null) DiffAndRegisterUndo(currentEntry, beforeJson);
        }
    }
}
