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

public class ProjectSettingsPanel : DockPanel
{
    [MenuItem("Window/General/Project Settings", priority: 8)]
    static void Open() => EditorApplication.Instance?.OpenPanel<ProjectSettingsPanel>();

    public override string Title => Loc.Get("panel.project_settings");
    public override string Icon => EditorIcons.Gear;

    private int _selectedIndex;

    public static void OpenCategory<T>() where T : ProjectSettingsBase
    {
        if (EditorApplication.Instance?.OpenPanel<ProjectSettingsPanel>() is not { } panel) return;
        IReadOnlyList<EditorRegistries.SettingsEntry> entries = EditorRegistries.SettingsEntries;
        for (int i = 0; i < entries.Count; i++)
            if (entries[i].Instance is T) { panel._selectedIndex = i; break; }
    }

    // Shared with EditorRegistries.SaveSettings keeping the serializer options identical
    // means the before/after JSON comparison is stable field-by-field.
    private static readonly JsonSerializerOptions s_jsonOpts = new()
    {
        WriteIndented = false,
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
    };

    private sealed class Snapshot(EditorRegistries.SettingsEntry entry, string json, long frame)
    {
        public EditorRegistries.SettingsEntry Entry = entry;
        public string Json = json;
        public long Frame = frame;
    }

    private static Snapshot? s_snapshot;

    static ProjectSettingsPanel() => Undo.undoRedoPerformed += () => s_snapshot = null;

    /// <summary>
    /// Records the change the last frame made to the entry it drew, then captures <paramref name="entry"/> for the next frame.
    /// Paper callbacks run at the end of a frame, so an edit is only visible on the frame after it was drawn.
    /// </summary>
    internal static void TrackEdits(EditorRegistries.SettingsEntry? entry)
    {
        long frame = Time.FrameCount;
        Snapshot? previous = s_snapshot;
        s_snapshot = null;

        if (previous != null && previous.Frame == frame - 1 && !Application.IsPlaying && Serialize(previous.Entry) is { } after && after != previous.Json)
            Undo.RegisterOperation(new SettingsOperation($"Modify {previous.Entry.Name}", previous.Entry.Type, previous.Json, after));

        if (entry is { } current && !Application.IsPlaying && Serialize(current) is { } json)
            s_snapshot = new Snapshot(current, json, frame);
    }

    private static string? Serialize(EditorRegistries.SettingsEntry entry)
    {
        try { return JsonSerializer.Serialize(entry.Instance, entry.Type, s_jsonOpts); }
        catch { return null; }
    }

    private sealed class SettingsOperation(string name, Type type, string beforeJson, string afterJson) : IUndoOperation
    {
        public string Name => name;
        public IReadOnlyList<UndoOwner> Owners { get; } = [UndoOwner.OfSettings(type)];
        public void Undo() => ApplyJson(type, beforeJson);
        public void Redo() => ApplyJson(type, afterJson);
    }

    private static void ApplyJson(Type type, string json)
    {
        foreach (EditorRegistries.SettingsEntry entry in EditorRegistries.SettingsEntries)
            if (entry.Type == type) ApplyJson(entry, json);
    }

    private static void ApplyJson(EditorRegistries.SettingsEntry entry, string json)
    {
        try
        {
            var loaded = (ProjectSettingsBase?)JsonSerializer.Deserialize(json, entry.Type, s_jsonOpts);
            if (loaded == null) return;
            EditorRegistries.CopySettingsFields(loaded, entry.Instance);
            entry.Instance.Apply();
            if (s_snapshot != null && s_snapshot.Entry.Type == entry.Type && Serialize(entry) is { } applied) s_snapshot.Json = applied;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"ProjectSettings undo/redo failed for '{entry.Name}': {ex.Message}");
        }
    }

    public override void OnGUI(Paper paper, float width, float height)
    {
        Scribe.FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;

        IReadOnlyList<EditorRegistries.SettingsEntry> entries = EditorRegistries.SettingsEntries;
        if (entries.Count == 0)
        {
            paper.Box("ps_empty").Size(width, height)
                .Text(Loc.Get("projset.none"), font)
                .TextColor(EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSize)
                .Alignment(TextAlignment.MiddleCenter);
            return;
        }

        if (_selectedIndex >= entries.Count || entries[_selectedIndex].Instance == null || !entries[_selectedIndex].Instance.DrawInProjectSettingsPanel) _selectedIndex = 0;

        // The entry index is used as the string id so the sidebar selection maps straight back to _selectedIndex.
        var cats = new List<(string id, string label, string icon)>();
        for (int i = 0; i < entries.Count; i++)
        {
            EditorRegistries.SettingsEntry e = entries[i];
            if (e.Instance == null || !e.Instance.DrawInProjectSettingsPanel) continue;
            string icon = string.IsNullOrEmpty(e.Icon) ? EditorIcons.Gear : e.Icon;
            cats.Add((i.ToString(), e.Name, icon));
        }

        using (paper.Row("ps_root").Size(width, height).Clip().Enter())
        {
            float side = EditorGUI.Sidebar(paper, "ps_side", [.. cats], _selectedIndex.ToString(),
                c => { if (int.TryParse(c, out int idx)) _selectedIndex = idx; });
            paper.Box("ps_vdiv").Width(1).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();

            // TODO: Port to using Echo to serialize to match how Project Settings serialize
            float contentW = width - side - 1;
            EditorRegistries.SettingsEntry currentEntry = entries[_selectedIndex];
            TrackEdits(currentEntry);

            Origami.ScrollView(paper, "ps_scroll", contentW, height).Body(() =>
            {
                using (paper.Column("ps_content").Height(UnitValue.Auto).Padding(0, 0, 8, 12).Enter())
                {
                    EditorGUI.SectionHeader(paper, "ps_content_h", currentEntry.Name, first: true);
                    DrawEntry(paper, currentEntry, contentW);
                    paper.Box("ps_content_pad").Height(EditorTheme.Padding * 4);
                }
            });
        }
    }

    // The panel records settings edits itself, so the property grid takes no inspector capture of its own.
    private static void DrawEntry(Paper paper, EditorRegistries.SettingsEntry entry, float width)
    {
        PropertyGridConfig config = EditorApplication.PropertyGridConfig;
        Action<object>? baseline = config.OnBeginRoot;
        config.OnBeginRoot = null;
        try { entry.Instance.OnGUI(paper, width); }
        finally { config.OnBeginRoot = baseline; }
    }
}
