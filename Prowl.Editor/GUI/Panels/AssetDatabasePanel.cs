using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Editor.Core;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Editor.Theming;
using Prowl.Runtime;
using Prowl.Scribe;

using Color = System.Drawing.Color;
using TextAlignment = Prowl.PaperUI.TextAlignment;

namespace Prowl.Editor.GUI.Panels;

/// <summary>
/// Read-only view into the editor asset database's live state: every currently-resident asset,
/// how long since it was last touched, whether it's idle-eligible, and whether it's locked.
/// Sub-assets share their parent's status (see AssetDatabase.ResolveFamily), so they're shown
/// nested under their parent, same as the Project panel's List view.
/// <para/>
/// Right-click a row to "Track" it: pins it to the top and captures a stack trace on every future
/// touch, so selecting it shows exactly what's touching an asset you didn't expect to still be active.
/// </summary>
public class AssetDatabasePanel : DockPanel
{
    [MenuItem("Window/Debug/Asset Database", priority: 101)]
    static void Open() => EditorApplication.Instance?.OpenPanel(typeof(AssetDatabasePanel));

    public override string Title => "Asset Database";
    public override string Icon => EditorIcons.Database;

    private static float RowH => EditorTheme.RowHeight;
    private const float DetailsHeight = 180f;

    private string _searchText = "";
    private bool _showOnlyIdle;
    private bool _showOnlyLocked;
    private readonly HashSet<Guid> _expandedFamilies = new();
    private Guid? _selectedGuid;

    private struct Row
    {
        public Guid Guid;
        public string Name;
        public string TypeName;
        public bool IsIdle;
        public bool IsLocked;
        public bool IsTracked;
        public TimeSpan? SinceTouched;
    }

    private sealed class FamilyGroup
    {
        public Row Root;
        public string RootPath = "";
        public readonly List<Row> Subs = new();
        public bool IsTracked => Root.IsTracked || Subs.Exists(s => s.IsTracked);
    }

    private readonly List<FamilyGroup> _families = new();
    private int _totalCount, _idleCount, _lockedCount;

    public override void OnGUI(Paper paper, float width, float height)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        var db = EditorAssetDatabase.Instance;
        if (db == null)
        {
            EditorGUI.EmptyState(paper, "adb_none", "No project open.", font);
            return;
        }

        RebuildRows(db);

        bool showDetails = _selectedGuid.HasValue && AssetDatabase.IsCapturingStackTrace(_selectedGuid.Value);
        float detailsHeight = showDetails ? DetailsHeight : 0f;

        using (paper.Column("adb_root").Size(width, height).Enter())
        {
            DrawToolbar(paper, font, db);
            DrawList(paper, font, width, height - 66 - detailsHeight);
            if (showDetails)
                DrawDetails(paper, font, width, detailsHeight, _selectedGuid!.Value);
        }
    }

    private void RebuildRows(EditorAssetDatabase db)
    {
        _totalCount = 0; _idleCount = 0; _lockedCount = 0;

        // Idle/locked/tracked all resolve through AssetDatabase.ResolveFamily internally, so a
        // sub-asset's own guid already reports its PARENT's status - the whole family shares one
        // lifecycle (and one captured stack trace, regardless of which specific member was touched).
        var groups = new Dictionary<Guid, FamilyGroup>();
        var pendingSubs = new List<(Guid parentGuid, Row row)>();

        foreach (var (guid, asset) in db.GetLoadedAssets())
        {
            _totalCount++;
            bool idle = AssetDatabase.IsIdle(guid, EditorAssetDatabase.IdleTimeout);
            bool locked = AssetDatabase.IsLocked(guid);
            if (idle) _idleCount++;
            if (locked) _lockedCount++;

            TimeSpan? since = AssetDatabase.TryGetLastTouched(guid, out var last)
                ? DateTime.UtcNow - last
                : null;

            bool isSub = db.TryGetParentGuid(guid, out var parentGuid);
            string fullPath = asset.AssetPath ?? string.Empty;
            int hashIdx = fullPath.IndexOf('#');

            var row = new Row
            {
                Guid = guid,
                Name = isSub && hashIdx >= 0
                    ? fullPath[(hashIdx + 1)..]
                    : (string.IsNullOrEmpty(asset.Name) ? "(unnamed)" : asset.Name),
                TypeName = asset.GetType().Name,
                IsIdle = idle,
                IsLocked = locked,
                IsTracked = AssetDatabase.IsCapturingStackTrace(guid),
                SinceTouched = since,
            };

            if (isSub)
                pendingSubs.Add((parentGuid, row));
            else
                groups[guid] = new FamilyGroup { Root = row, RootPath = fullPath };
        }

        // A sub-asset never loads except as a side effect of loading its parent (see GetInternal), so
        // the parent group should always exist by the time we get here; skip defensively if not.
        foreach (var (parentGuid, row) in pendingSubs)
            if (groups.TryGetValue(parentGuid, out var g))
                g.Subs.Add(row);

        bool SearchMatches(Row r) => string.IsNullOrEmpty(_searchText)
            || r.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase);

        _families.Clear();
        foreach (var g in groups.Values)
        {
            if (_showOnlyIdle && !g.Root.IsIdle) continue;
            if (_showOnlyLocked && !g.Root.IsLocked) continue;
            if (!SearchMatches(g.Root) && !g.RootPath.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
                && !g.Subs.Any(SearchMatches))
                continue;

            g.Subs.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            _families.Add(g);
        }

        // Tracked families first (pinned to the top so they're easy to find), then alphabetical.
        _families.Sort((a, b) =>
        {
            if (a.IsTracked != b.IsTracked) return a.IsTracked ? -1 : 1;
            return string.Compare(a.RootPath, b.RootPath, StringComparison.OrdinalIgnoreCase);
        });
    }

    private void DrawToolbar(Paper paper, FontFile font, EditorAssetDatabase db)
    {
        using (paper.Column("adb_tb_col").Height(65).Enter())
        {
            using (paper.Row("adb_tb1").Height(33).Padding(10, 8, 6, 0).RowBetween(6).Enter())
            {
                using (paper.Row("adb_search_wrap").Width(180).Height(24).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).Enter())
                    Origami.SearchField(paper, "adb_search", _searchText, v => _searchText = v, "Filter by name/path").Width(180).Height(24).Show();

                EditorGUI.ToolbarIconBtn(paper, "adb_idle_f", EditorIcons.Hourglass, _showOnlyIdle, () => _showOnlyIdle = !_showOnlyIdle);
                EditorGUI.ToolbarIconBtn(paper, "adb_lock_f", EditorIcons.Lock, _showOnlyLocked, () => _showOnlyLocked = !_showOnlyLocked);

                paper.Box("adb_sp");

                EditorGUI.CtaButton(paper, "adb_sweep", "Sweep Now", EditorTheme.Accent, () => db.ForceIdleSweep());
            }

            using (paper.Row("adb_tb2").Height(28).Padding(10, 8, 0, 4).RowBetween(6).Enter())
            {
                EditorGUI.StatChip(paper, "adb_chip_total", $"Loaded: {_totalCount}", font);
                EditorGUI.StatChip(paper, "adb_chip_idle", $"Idle: {_idleCount}", font);
                EditorGUI.StatChip(paper, "adb_chip_locked", $"Locked: {_lockedCount}", font);
                EditorGUI.StatChip(paper, "adb_chip_timeout", $"Timeout: {EditorAssetDatabase.IdleTimeout.TotalSeconds:0}s", font);
            }
            EditorGUI.Divider(paper, "adb_tb_div");
        }
    }

    private void DrawList(Paper paper, FontFile font, float width, float height)
    {
        if (_families.Count == 0)
        {
            EditorGUI.EmptyState(paper, "adb_empty", "No loaded assets match the current filter.", font);
            return;
        }

        var mono = EditorTheme.FontMono ?? font;

        // Flat visible list: every root, plus its sub-assets when expanded (or always, while
        // searching, so a match nested in a collapsed family is still visible).
        var visible = new List<(FamilyGroup group, Row row, bool isSub)>();
        foreach (var g in _families)
        {
            visible.Add((g, g.Root, false));
            bool expanded = !string.IsNullOrEmpty(_searchText) || _expandedFamilies.Contains(g.Root.Guid);
            if (g.Subs.Count > 0 && expanded)
                foreach (var s in g.Subs) visible.Add((g, s, true));
        }

        Origami.Table(paper, "adb_table", -1, _ => { })
            .Bordered(false)
            .Scroll(width, height)
            .RowHeight(RowH)
            .Column("Name", 2.2f, sortable: false)
            .Column("Parent Path", 2f, sortable: false)
            .Column("Type", 1f, sortable: false)
            .Column("Last Touched", 1f, sortable: false)
            .Column("Status", 0.8f, sortable: false, align: TextAlignment.MiddleRight)
            .IsSelected(i => _selectedGuid.HasValue && visible[i].row.Guid == _selectedGuid.Value)
            .OnSelectModified((i, _, _) => _selectedGuid = visible[i].row.Guid)
            .OnRowActivate(i =>
            {
                var (g, _, isSub) = visible[i];
                if (!isSub && g.Subs.Count > 0)
                {
                    if (_expandedFamilies.Contains(g.Root.Guid)) _expandedFamilies.Remove(g.Root.Guid);
                    else _expandedFamilies.Add(g.Root.Guid);
                }
            })
            .OnRowContext(i =>
            {
                var guid = visible[i].row.Guid;
                _selectedGuid = guid;
                Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, menu => BuildRowContextMenu(menu, guid));
            })
            .RowCount(visible.Count)
            .CellContent((rowIdx, col) => DrawCell(paper, font, mono, visible[rowIdx], col))
            .Show();
    }

    private static void BuildRowContextMenu(ContextBuilder menu, Guid guid)
    {
        if (AssetDatabase.IsCapturingStackTrace(guid))
            menu.Item("Untrack", () => AssetDatabase.SetStackTraceCapture(guid, false));
        else
            menu.Item("Track (capture stack trace on touch)", () => AssetDatabase.SetStackTraceCapture(guid, true));
    }

    private void DrawCell(Paper paper, FontFile font, FontFile mono, (FamilyGroup group, Row row, bool isSub) v, int col)
    {
        var (group, row, isSub) = v;
        string id = row.Guid.ToString();
        bool hasSubs = !isSub && group.Subs.Count > 0;
        bool expanded = _expandedFamilies.Contains(group.Root.Guid);

        switch (col)
        {
            case 0:
                if (isSub)
                    paper.Box($"adb_ind_{id}").Width(20).Height(RowH).IsNotInteractable();
                else if (hasSubs)
                    paper.Box($"adb_car_{id}").Width(15).Height(RowH)
                        .StopEventPropagation()
                        .OnClick(group.Root.Guid, (g, _) =>
                        {
                            if (_expandedFamilies.Contains(g)) _expandedFamilies.Remove(g);
                            else _expandedFamilies.Add(g);
                        })
                        .Icon(paper, expanded ? EditorIcons.ChevronDown_I : EditorIcons.ChevronRight_I, EditorTheme.InkDim, size: 11f);
                else
                    paper.Box($"adb_sp_{id}").Width(15).Height(RowH).IsNotInteractable();

                if (row.IsTracked)
                    paper.Box($"adb_trk_{id}").Width(16).Height(RowH).IsNotInteractable()
                        .Icon(paper, EditorIcons.Crosshairs_I, EditorTheme.AccentText, size: 11f);

                paper.Box($"adb_name_{id}").Width(UnitValue.Auto).Height(RowH).Margin(6, 0, 0, 0).Clip()
                    .Text(row.Name, font).TextColor(isSub ? EditorTheme.Ink400 : EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

                if (hasSubs)
                    paper.Box($"adb_tag_{id}").Width(UnitValue.Auto).Height(17).Rounded(5).Padding(6, 6, 0, 0).Margin(7, 0, UnitValue.StretchOne, UnitValue.StretchOne)
                        .BackgroundColor(EditorTheme.Selected).BorderColor(Color.FromArgb(77, EditorTheme.Purple400)).BorderWidth(1)
                        .Text(group.Subs.Count.ToString(), EditorTheme.FontSemiBold ?? font).TextColor(EditorTheme.AccentText)
                        .FontSize(11f).Alignment(TextAlignment.MiddleCenter);
                break;

            case 1:
                paper.Box($"adb_path_{id}").Height(RowH).IsNotInteractable()
                    .Text(group.RootPath, mono).TextColor(EditorTheme.InkDim)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
                break;

            case 2:
                paper.Box($"adb_type_{id}").Height(RowH).IsNotInteractable()
                    .Text(row.TypeName, font).TextColor(EditorTheme.InkDim)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
                break;

            case 3:
                string since = row.SinceTouched is { } ts ? FormatSince(ts) : "never";
                paper.Box($"adb_since_{id}").Height(RowH).IsNotInteractable()
                    .Text(since, font).TextColor(EditorTheme.InkDim)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
                break;

            default:
                string statusText = row.IsLocked ? "Locked" : row.IsIdle ? "Idle" : "Active";
                Color statusColor = row.IsLocked ? EditorTheme.Blue400 : row.IsIdle ? EditorTheme.Amber400 : EditorTheme.Green400;
                paper.Box($"adb_status_{id}").Height(RowH).IsNotInteractable()
                    .Text(statusText, font).TextColor(statusColor)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleRight);
                break;
        }
    }

    private void DrawDetails(Paper paper, FontFile font, float width, float height, Guid guid)
    {
        var mono = EditorTheme.FontMono ?? font;

        using (paper.Column("adb_details").Width(width).Height(height).Padding(10, 10, 6, 6).Enter())
        {
            EditorGUI.Divider(paper, "adb_details_div");

            using (paper.Row("adb_details_hdr").Height(24).RowBetween(8).Enter())
            {
                paper.Box("adb_details_title").Width(UnitValue.Auto).Height(24).IsNotInteractable()
                    .Text("Last Touch Stack Trace", EditorTheme.FontSemiBold ?? font).TextColor(EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

                paper.Box("adb_details_sp");

                EditorGUI.Chip(paper, "adb_details_stop", "Stop Tracking", () => AssetDatabase.SetStackTraceCapture(guid, false));
            }

            if (!AssetDatabase.TryGetLastTouchStackTrace(guid, out var trace) || string.IsNullOrEmpty(trace))
            {
                EditorGUI.EmptyState(paper, "adb_details_empty", "No touch captured yet since tracking began.", font);
            }
            else
            {
                Origami.ScrollView(paper, "adb_details_scroll", width, height - 32).Body(() =>
                {
                    var lines = trace.Replace("\r\n", "\n").Split('\n');
                    using (paper.Column("adb_details_lines").Width(width - 12).Height(UnitValue.Auto).Enter())
                    {
                        for (int i = 0; i < lines.Length; i++)
                        {
                            if (string.IsNullOrWhiteSpace(lines[i])) continue;
                            paper.Box($"adb_details_line_{i}").Width(UnitValue.Stretch()).Height(UnitValue.Auto).MinHeight(16)
                                .Text(lines[i], mono).TextColor(EditorTheme.InkDim)
                                .Wrap(Scribe.TextWrapMode.Wrap).FontSize(EditorTheme.FontSizeSmall - 1);
                        }
                    }
                });
            }
        }
    }

    private static string FormatSince(TimeSpan ts)
    {
        if (ts.TotalSeconds < 2) return "just now";
        if (ts.TotalMinutes < 1) return $"{(int)ts.TotalSeconds}s ago";
        if (ts.TotalHours < 1) return $"{(int)ts.TotalMinutes}m ago";
        return $"{(int)ts.TotalHours}h ago";
    }
}
