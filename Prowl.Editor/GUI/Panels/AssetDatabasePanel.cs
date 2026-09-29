using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

using Prowl.Editor.Core;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Runtime;
using Prowl.Scribe;
using Prowl.Vector;

using Color = System.Drawing.Color;
using TextAlignment = Prowl.PaperUI.TextAlignment;

namespace Prowl.Editor.GUI.Panels;

/// <summary>
/// Read-only view into what the asset database holds: every asset object made this session, whether it is loaded,
/// roughly how much memory it keeps, when the last walk reached it and how many times it has loaded. Sub-assets
/// are shown nested under their parent, same as the Project panel's List view.
/// <para/>
/// Selecting a row shows what it depends on, what uses it, who holds it, and with path recording on, the chain
/// the walk followed to reach it. Right-click a row to unload it or reveal it in the Project panel.
/// </summary>
public class AssetDatabasePanel : DockPanel
{
    [MenuItem("Window/Debug/Asset Database", priority: 101)]
    static void Open() => EditorApplication.Instance?.OpenPanel(typeof(AssetDatabasePanel));

    public override string Title => "Asset Database";
    public override string Icon => EditorIcons.Database;

    private static float RowH => EditorTheme.RowHeight;
    private const float DetailsHeight = 120f;

    private string _searchText = "";
    private bool _showUnloaded;
    private bool _showOnlyUnreached;
    private string _typeFilter = "";
    private readonly HashSet<Guid> _expandedFamilies = new();
    private Guid? _selectedGuid;

    private enum SortMode { Default, Name, Type, Size, SinceReached }
    private SortMode _sortBy = SortMode.Default;

    private struct Row
    {
        public Asset Asset;
        public Guid Guid;
        public string Name;
        public string TypeName;
        public AssetState State;
        public bool Reached;
        public TimeSpan SinceReached;
        public long SizeBytes;
        public int LoadCount;
    }

    private sealed class FamilyGroup
    {
        public Row Root;
        public string RootPath = "";
        public readonly List<Row> Subs = new();
        public long TotalSizeBytes => Root.SizeBytes + Subs.Sum(s => s.SizeBytes);
    }

    // Lists every asset, so it must not keep them loaded.
    [NotHeld] private readonly List<FamilyGroup> _families = new();
    private int _loadedCount, _unreachedCount;

    #region Loaded-count History (sparkline)

    private readonly int[] _countHistory = new int[60];
    private int _historyHead;
    private DateTime _lastHistorySample = DateTime.MinValue;

    private void SampleHistory()
    {
        var now = DateTime.UtcNow;
        if (now - _lastHistorySample < TimeSpan.FromSeconds(1)) return;
        _lastHistorySample = now;
        _historyHead = (_historyHead + 1) % _countHistory.Length;
        _countHistory[_historyHead] = _loadedCount;
    }

    #endregion

    public override void OnGUI(Paper paper, float width, float height)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        var db = EditorAssetBackend.Instance;
        if (db == null)
        {
            EditorGUI.EmptyState(paper, "adb_none", "No project open.", font);
            return;
        }

        RebuildRows(db);
        SampleHistory();

        bool hasSelection = _selectedGuid.HasValue;
        using (paper.Column("adb_root").Size(width, height).Enter())
        {
            DrawToolbar(paper, font);
            DrawList(paper, font, width, height - 66 - (hasSelection ? DetailsHeight : 0));
            if (hasSelection)
                DrawDetails(paper, font, width, db, _selectedGuid!.Value);
        }
    }

    #region Row Model

    private void RebuildRows(EditorAssetBackend db)
    {
        _loadedCount = 0;
        _unreachedCount = 0;

        var groups = new Dictionary<Guid, FamilyGroup>();
        var pendingSubs = new List<(Guid parentGuid, Row row)>();
        var typeNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Asset asset in AssetDatabase.All)
        {
            if (!asset.Registered || AssetDatabase.IsBuiltIn(asset)) continue;
            if (asset.IsLoaded) _loadedCount++;
            if (!_showUnloaded && !asset.IsLoaded) continue;

            Row row = MakeRow(asset);
            if (asset.IsLoaded && !row.Reached) _unreachedCount++;
            typeNames.Add(row.TypeName);

            if (db.TryGetParentGuid(asset.AssetID, out var parentGuid)) pendingSubs.Add((parentGuid, row));
            else groups[asset.AssetID] = new FamilyGroup { Root = row, RootPath = asset.AssetPath };
        }

        _typeOptions.Clear();
        _typeOptions.Add("All Types");
        _typeOptions.AddRange(typeNames);
        if (!_typeOptions.Contains(_typeFilter)) _typeFilter = "";

        // A sub-asset loads on its own, so its parent often is not loaded. The parent still gets a row to nest under.
        foreach (var (parentGuid, row) in pendingSubs)
        {
            if (!groups.TryGetValue(parentGuid, out var group))
            {
                if (AssetDatabase.Get(parentGuid) is not { } parent)
                {
                    groups[row.Guid] = new FamilyGroup { Root = row, RootPath = row.Asset.AssetPath };
                    continue;
                }
                groups[parentGuid] = group = new FamilyGroup { Root = MakeRow(parent), RootPath = parent.AssetPath };
            }
            group.Subs.Add(row);
        }

        bool SearchMatches(Row r) => string.IsNullOrEmpty(_searchText)
            || r.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase);

        _families.Clear();
        foreach (var g in groups.Values)
        {
            if (_showOnlyUnreached && g.Root.Reached && g.Subs.TrueForAll(s => s.Reached)) continue;
            if (!string.IsNullOrEmpty(_typeFilter) && _typeFilter != "All Types"
                && g.Root.TypeName != _typeFilter && !g.Subs.Exists(s => s.TypeName == _typeFilter))
                continue;
            if (!SearchMatches(g.Root) && !g.RootPath.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
                && !g.Subs.Any(SearchMatches))
                continue;

            g.Subs.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            _families.Add(g);
        }

        _families.Sort((a, b) => _sortBy switch
        {
            SortMode.Name => string.Compare(a.Root.Name, b.Root.Name, StringComparison.OrdinalIgnoreCase),
            SortMode.Type => string.Compare(a.Root.TypeName, b.Root.TypeName, StringComparison.OrdinalIgnoreCase),
            SortMode.Size => b.TotalSizeBytes.CompareTo(a.TotalSizeBytes),
            SortMode.SinceReached => b.Root.SinceReached.CompareTo(a.Root.SinceReached),
            _ => string.Compare(a.RootPath, b.RootPath, StringComparison.OrdinalIgnoreCase),
        });
    }

    private static Row MakeRow(Asset asset)
    {
        AssetResidency residency = AssetDatabase.Explain(asset);
        return new Row
        {
            Asset = asset,
            Guid = asset.AssetID,
            Name = string.IsNullOrEmpty(asset.Name) ? "(unnamed)" : asset.Name,
            TypeName = asset.GetType().Name,
            State = asset.State,
            Reached = residency.Reached,
            SinceReached = residency.SinceReached,
            SizeBytes = residency.Bytes,
            LoadCount = residency.LoadCount,
        };
    }

    private bool IsFiltering => !string.IsNullOrEmpty(_searchText) || !string.IsNullOrEmpty(_typeFilter);

    #endregion

    #region Toolbar

    private readonly List<string> _typeOptions = new();

    private void DrawToolbar(Paper paper, FontFile font)
    {
        using (paper.Column("adb_tb_col").Height(65).Enter())
        {
            using (paper.Row("adb_tb1").Height(33).Padding(10, 8, 6, 0).Gap(6).Enter())
            {
                using (paper.Row("adb_search_wrap").Width(160).Height(24).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).Enter())
                    Origami.SearchField(paper, "adb_search", _searchText, v => _searchText = v, "Filter by name/path").Width(160).Height(24).Show();

                EditorGUI.ToolbarIconBtn(paper, "adb_unloaded_f", EditorIcons.Circle, _showUnloaded, () => _showUnloaded = !_showUnloaded);
                EditorGUI.ToolbarIconBtn(paper, "adb_unreached_f", EditorIcons.Hourglass, _showOnlyUnreached, () => _showOnlyUnreached = !_showOnlyUnreached);
                EditorGUI.ToolbarIconBtn(paper, "adb_paths_f", EditorIcons.Crosshairs, AssetDatabase.RecordReachPaths,
                    () => AssetDatabase.RecordReachPaths = !AssetDatabase.RecordReachPaths);

                using (paper.Row("adb_type_wrap").Width(120).Height(UnitValue.Auto).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).Enter())
                    Origami.Dropdown(paper, "adb_type_dd",
                        Math.Max(0, _typeOptions.IndexOf(string.IsNullOrEmpty(_typeFilter) ? "All Types" : _typeFilter)),
                        v => _typeFilter = v == 0 ? "" : _typeOptions[v],
                        _typeOptions.ToArray()).Width(120).Show();

                paper.Box("adb_graph").Width(70).Height(20).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).IsNotInteractable()
                    .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) => DrawSparkline(canvas, r)));

                paper.Box("adb_sp");

                EditorGUI.ToolbarIconBtn(paper, "adb_export", EditorIcons.Clipboard, false, () => ExportToClipboard(paper));
                EditorGUI.CtaButton(paper, "adb_unused", "Unload Unused", EditorTheme.Accent, () =>
                    Runtime.Debug.Log($"[Assets] Unloaded {AssetDatabase.UnloadUnused()} assets nothing was using."));
            }

            using (paper.Row("adb_tb2").Height(28).Padding(10, 8, 0, 4).Gap(6).Enter())
            {
                EditorGUI.StatChip(paper, "adb_chip_total", $"Loaded: {_loadedCount}", font);
                EditorGUI.StatChip(paper, "adb_chip_unreached", $"Unreached: {_unreachedCount}", font);
                EditorGUI.StatChip(paper, "adb_chip_mem", $"Memory: {FormatBytes(AssetDatabase.ResidentBytes)}", font);
                EditorGUI.StatChip(paper, "adb_chip_grace", $"Grace: {AssetDatabase.GracePeriod.TotalSeconds:0}s", font);
            }
            EditorGUI.Divider(paper, "adb_tb_div");
        }
    }

    private void DrawSparkline(Canvas canvas, Rect r)
    {
        float x = (float)r.Min.X, y = (float)r.Min.Y, w = (float)r.Size.X, h = (float)r.Size.Y;
        canvas.RectFilled(x, y, w, h, Color32.FromArgb(255, 10, 10, 14));

        int len = _countHistory.Length;
        int max = 1;
        for (int i = 0; i < len; i++) if (_countHistory[i] > max) max = _countHistory[i];

        float barW = w / len;
        for (int i = 0; i < len; i++)
        {
            int value = _countHistory[(_historyHead + 1 + i) % len];
            if (value <= 0) continue;
            float barH = MathF.Min((value / (float)max) * h, h);
            canvas.RectFilled(x + i * barW, y + h - barH, MathF.Max(1, barW - 0.5f), barH,
                Color32.FromArgb(200, 90, 140, 220));
        }
    }

    private void ExportToClipboard(Paper paper)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Name\tType\tPath\tState\tSize\tSinceReached\tLoads");
        foreach (var g in _families)
        {
            AppendRow(sb, g.Root, g.RootPath);
            foreach (var s in g.Subs)
                AppendRow(sb, s, g.RootPath);
        }
        paper.SetClipboard(sb.ToString());
    }

    private static void AppendRow(StringBuilder sb, Row row, string path)
        => sb.AppendLine($"{row.Name}\t{row.TypeName}\t{path}\t{StatusOf(row)}\t{FormatBytes(row.SizeBytes)}\t{FormatSince(row.SinceReached)}\t{row.LoadCount}");

    #endregion

    #region List

    private void DrawList(Paper paper, FontFile font, float width, float height)
    {
        if (_families.Count == 0)
        {
            EditorGUI.EmptyState(paper, "adb_empty", "No assets match the current filter.", font);
            return;
        }

        var mono = EditorTheme.FontMono ?? font;

        // Flat visible list: every root, plus its sub-assets when expanded (or always, while
        // filtering, so a match nested in a collapsed family is still visible).
        var visible = new List<(FamilyGroup group, Row row, bool isSub)>();
        foreach (var g in _families)
        {
            visible.Add((g, g.Root, false));
            bool expanded = IsFiltering || _expandedFamilies.Contains(g.Root.Guid);
            if (g.Subs.Count > 0 && expanded)
                foreach (var s in g.Subs) visible.Add((g, s, true));
        }

        int activeCol = _sortBy switch { SortMode.Name => 0, SortMode.Type => 2, SortMode.Size => 3, SortMode.SinceReached => 4, _ => -1 };
        bool ascending = _sortBy != SortMode.Size && _sortBy != SortMode.SinceReached;

        Origami.Table(paper, "adb_table", -1, _ => { })
            .Bordered(false)
            .Scroll(width, height)
            .RowHeight(RowH)
            .Column("Name", 2.0f, sortable: true)
            .Column("Parent Path", 1.6f, sortable: false)
            .Column("Type", 0.8f, sortable: true)
            .Column("Size", 0.7f, sortable: true, align: TextAlignment.MiddleRight)
            .Column("Reached", 1.3f, sortable: true)
            .Column("State", 0.9f, sortable: false, align: TextAlignment.MiddleRight)
            .Sort(activeCol, ascending, col => _sortBy = col switch
            {
                0 => SortMode.Name,
                2 => SortMode.Type,
                3 => SortMode.Size,
                4 => SortMode.SinceReached,
                _ => _sortBy,
            })
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
                Row row = visible[i].row;
                _selectedGuid = row.Guid;
                Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, menu => BuildRowContextMenu(menu, row.Asset));
            })
            .RowCount(visible.Count)
            .CellContent((rowIdx, col) => DrawCell(paper, font, mono, visible[rowIdx], col))
            .Show();
    }

    private static void BuildRowContextMenu(ContextBuilder menu, Asset asset)
    {
        menu.Item("Unload Now", () => AssetDatabase.Unload(asset), enabled: asset.IsLoaded, icon: EditorIcons.Trash);
        menu.Item("Explain", () => Runtime.Debug.Log(Explain(asset)), icon: EditorIcons.CircleInfo);

        menu.Separator();

        menu.Item("Reveal in Project", () =>
        {
            var db = EditorAssetBackend.Instance;
            Selection.Ping(db != null && db.TryGetParentGuid(asset.AssetID, out var parentGuid) ? parentGuid : asset.AssetID);
        }, icon: EditorIcons.ArrowUpRightFromSquare);
    }

    private static string Explain(Asset asset)
    {
        AssetResidency r = AssetDatabase.Explain(asset);
        var sb = new StringBuilder($"{asset.GetType().Name} '{asset.Name}' ({asset.AssetID}): {r.State}, {FormatBytes(r.Bytes)}, loaded {r.LoadCount} times, ");
        sb.Append(r.Reached ? "reached by the last walk" : $"unreached for {FormatSince(r.SinceReached)}");
        if (r.ReachedBy != null) sb.Append($"\n  Reached by: {r.ReachedBy}");
        if (r.HeldBy.Count > 0) sb.Append($"\n  Held by: {string.Join(", ", r.HeldBy.Select(h => h.GetType().Name))}");
        return sb.ToString();
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

                paper.Box($"adb_name_{id}").Width(UnitValue.Auto).Height(RowH).Margin(6, 0, 0, 0).Clip()
                    .Text(row.Name, font).TextColor(isSub ? EditorTheme.Ink400 : EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

                if (hasSubs)
                    paper.Box($"adb_tag_{id}").Width(UnitValue.Auto).Height(17).Rounded(Origami.Current.Metrics.SmallRounding).Padding(6, 6, 0, 0).Margin(7, 0, UnitValue.StretchOne, UnitValue.StretchOne)
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
                long size = isSub ? row.SizeBytes : group.TotalSizeBytes;
                paper.Box($"adb_size_{id}").Height(RowH).IsNotInteractable()
                    .Text(FormatBytes(size), mono).TextColor(EditorTheme.InkDim)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleRight);
                break;

            case 4:
                string reached = row.Reached ? "now" : FormatSince(row.SinceReached);
                string loads = row.LoadCount >= 2 ? $", {row.LoadCount} loads" : "";
                paper.Box($"adb_since_{id}").Height(RowH).IsNotInteractable()
                    .Text(reached + loads, font).TextColor(EditorTheme.InkDim)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
                break;

            default:
                Color statusColor = row.State switch
                {
                    AssetState.Missing or AssetState.Failed => EditorTheme.Red400,
                    AssetState.Loaded when !row.Reached => EditorTheme.Amber400,
                    AssetState.Loaded => EditorTheme.Green400,
                    _ => EditorTheme.InkDim,
                };
                paper.Box($"adb_status_{id}").Height(RowH).IsNotInteractable()
                    .Text(StatusOf(row), font).TextColor(statusColor)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleRight);
                break;
        }
    }

    private static string StatusOf(Row row) => row.State == AssetState.Loaded && !row.Reached ? "Unreached" : row.State.ToString();

    private static string FormatSince(TimeSpan ts)
    {
        if (ts.TotalSeconds < 2) return "just now";
        if (ts.TotalMinutes < 1) return $"{(int)ts.TotalSeconds}s ago";
        if (ts.TotalHours < 1) return $"{(int)ts.TotalMinutes}m ago";
        return $"{(int)ts.TotalHours}h ago";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "-";
        string[] u = { "B", "KB", "MB", "GB" };
        double s = bytes;
        int i = 0;
        while (s >= 1024 && i < u.Length - 1) { s /= 1024; i++; }
        return $"{(i == 0 ? s.ToString("0") : s.ToString("0.#"))} {u[i]}";
    }

    #endregion

    #region Details

    private void DrawDetails(Paper paper, FontFile font, float width, EditorAssetBackend db, Guid guid)
    {
        var mono = EditorTheme.FontMono ?? font;

        using (paper.Column("adb_details").Width(width).Height(DetailsHeight).Padding(10, 10, 6, 6).Enter())
        {
            EditorGUI.Divider(paper, "adb_details_div");

            var deps = db.Dependencies.GetDependencies(guid);
            var dependents = db.Dependencies.GetDependents(guid);
            using (paper.Row("adb_dep_row").Height(60).Enter())
            {
                DrawGuidList(paper, font, mono, db, "adb_dep_out", $"Depends on ({deps.Count})", deps);
                DrawGuidList(paper, font, mono, db, "adb_dep_in", $"Used by ({dependents.Count})", dependents);
            }

            if (AssetDatabase.TryGetExisting(guid, out Asset asset))
            {
                AssetResidency r = AssetDatabase.Explain(asset);
                string reachedBy = r.ReachedBy ?? (AssetDatabase.RecordReachPaths ? "not reached" : "turn on path recording to see what reaches it");
                string held = r.HeldBy.Count > 0 ? string.Join(", ", r.HeldBy.Select(h => h.GetType().Name)) : "nothing";
                paper.Box("adb_details_reach").Height(UnitValue.Auto).IsNotInteractable()
                    .Text($"Reached by: {reachedBy}\nHeld by: {held}", mono).TextColor(EditorTheme.InkDim)
                    .Wrap(TextWrapMode.Wrap).FontSize(EditorTheme.FontSizeSmall - 1);
            }
        }
    }

    private void DrawGuidList(Paper paper, FontFile font, FontFile mono, EditorAssetBackend db, string id, string title, IReadOnlySet<Guid> guids)
    {
        using (paper.Column(id).Width(UnitValue.Stretch()).Height(60).Enter())
        {
            paper.Box($"{id}_t").Height(16).IsNotInteractable()
                .Text(title, EditorTheme.FontSemiBold ?? font).TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSizeSmall - 1).Alignment(TextAlignment.MiddleLeft);

            if (guids.Count == 0)
            {
                paper.Box($"{id}_none").Height(16).IsNotInteractable()
                    .Text("-", font).TextColor(EditorTheme.InkDim)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
                return;
            }

            Origami.ScrollView(paper, $"{id}_scroll", 0, 42).Body(() =>
            {
                int i = 0;
                foreach (var g in guids)
                {
                    paper.Box($"{id}_item_{i}").Height(16).Clip()
                        .OnClick(g, (guid, _) => { _selectedGuid = guid; Selection.Ping(guid); })
                        .Text(LabelFor(db, g), mono).TextColor(EditorTheme.AccentText)
                        .FontSize(EditorTheme.FontSizeSmall - 1).Alignment(TextAlignment.MiddleLeft);
                    i++;
                }
            });
        }
    }

    private static string LabelFor(EditorAssetBackend db, Guid guid) => db.GetAssetPath(guid) ?? guid.ToString()[..8];

    #endregion
}
