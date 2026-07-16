using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Prowl.Editor.Core;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Editor.Theming;
using Prowl.Quill;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Scribe;
using Prowl.Vector;

using Color = System.Drawing.Color;
using TextAlignment = Prowl.PaperUI.TextAlignment;

namespace Prowl.Editor.GUI.Panels;

/// <summary>
/// Read-only view into the editor asset database's live state: every currently-resident asset, its
/// approximate memory footprint, how long since it was last touched, whether it's idle-eligible, and
/// whether it's locked. Sub-assets share their parent's status (see AssetDatabase.ResolveFamily), so
/// they're shown nested under their parent, same as the Project panel's List view.
/// <para/>
/// Right-click a row to "Track" it: pins it to the top and captures a stack trace on every future
/// touch, so selecting it shows exactly what's touching an asset you didn't expect to still be active.
/// The same menu can force-unload or lock/unlock a row, and reveal it in the Project panel.
/// </summary>
public class AssetDatabasePanel : DockPanel
{
    [MenuItem("Window/Debug/Asset Database", priority: 101)]
    static void Open() => EditorApplication.Instance?.OpenPanel(typeof(AssetDatabasePanel));

    public override string Title => "Asset Database";
    public override string Icon => EditorIcons.Database;

    private static float RowH => EditorTheme.RowHeight;
    private const float StackTraceHeight = 180f;
    private const float InfoOnlyHeight = 84f;

    private string _searchText = "";
    private bool _showOnlyIdle;
    private bool _showOnlyLocked;
    private string _typeFilter = "";
    private readonly HashSet<Guid> _expandedFamilies = new();
    private Guid? _selectedGuid;

    private enum SortMode { Default, Name, Type, Size, LastTouched }
    private SortMode _sortBy = SortMode.Default;

    private struct Row
    {
        public Guid Guid;
        public string Name;
        public string TypeName;
        public bool IsIdle;
        public bool IsLocked;
        public bool IsTracked;
        public TimeSpan? SinceTouched;
        public long SizeBytes;
        public int ReloadCount;
    }

    private sealed class FamilyGroup
    {
        public Row Root;
        public string RootPath = "";
        public readonly List<Row> Subs = new();
        public bool IsTracked => Root.IsTracked || Subs.Exists(s => s.IsTracked);
        public long TotalSizeBytes => Root.SizeBytes + Subs.Sum(s => s.SizeBytes);
    }

    private readonly List<FamilyGroup> _families = new();
    private int _totalCount, _idleCount, _lockedCount;
    private long _totalBytes;

    // Per-asset size estimate is opt-in (via the "Calc Sizes" toolbar button) rather than
    // recomputed every frame for every loaded asset - EstimateBytes itself is cheap per-asset, but
    // this panel is meant to be safe to leave open during a heavy import without adding its own
    // per-frame cost across potentially hundreds of resident assets. Empty/stale entries just read
    // as "-" (see FormatBytes) until the button is pressed.
    private readonly Dictionary<Guid, long> _cachedSizes = new();
    private bool _sizesComputed;

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
        _countHistory[_historyHead] = _totalCount;
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
        bool showStackTrace = hasSelection && AssetDatabase.IsCapturingStackTrace(_selectedGuid.Value);
        float detailsHeight = !hasSelection ? 0f : showStackTrace ? StackTraceHeight : InfoOnlyHeight;

        using (paper.Column("adb_root").Size(width, height).Enter())
        {
            DrawToolbar(paper, font, db);
            DrawList(paper, font, width, height - 66 - detailsHeight);
            if (hasSelection)
                DrawDetails(paper, font, width, detailsHeight, db, _selectedGuid!.Value, showStackTrace);
        }
    }

    #region Row Model

    private void RebuildRows(EditorAssetBackend db)
    {
        _totalCount = 0; _idleCount = 0; _lockedCount = 0; _totalBytes = 0;

        // Idle/locked/tracked all resolve through AssetDatabase.ResolveFamily internally, so a
        // sub-asset's own guid already reports its PARENT's status - the whole family shares one
        // lifecycle (and one captured stack trace, regardless of which specific member was touched).
        var groups = new Dictionary<Guid, FamilyGroup>();
        var pendingSubs = new List<(Guid parentGuid, Row row)>();
        var typeNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (guid, asset) in db.GetLoadedAssets())
        {
            _totalCount++;
            bool idle = AssetDatabase.IsIdle(guid, EditorAssetBackend.IdleTimeout);
            bool locked = AssetDatabase.IsLocked(guid);
            if (idle) _idleCount++;
            if (locked) _lockedCount++;

            TimeSpan? since = AssetDatabase.TryGetLastTouched(guid, out var last)
                ? DateTime.UtcNow - last
                : null;

            long sizeBytes = _cachedSizes.GetValueOrDefault(guid);
            _totalBytes += sizeBytes;

            bool isSub = db.TryGetParentGuid(guid, out var parentGuid);
            string fullPath = asset.AssetPath ?? string.Empty;
            int hashIdx = fullPath.IndexOf('#');
            string typeName = asset.GetType().Name;
            typeNames.Add(typeName);

            var row = new Row
            {
                Guid = guid,
                Name = isSub && hashIdx >= 0
                    ? fullPath[(hashIdx + 1)..]
                    : (string.IsNullOrEmpty(asset.Name) ? "(unnamed)" : asset.Name),
                TypeName = typeName,
                IsIdle = idle,
                IsLocked = locked,
                IsTracked = AssetDatabase.IsCapturingStackTrace(guid),
                SinceTouched = since,
                SizeBytes = sizeBytes,
                ReloadCount = db.GetReloadCount(guid),
            };

            if (isSub)
                pendingSubs.Add((parentGuid, row));
            else
                groups[guid] = new FamilyGroup { Root = row, RootPath = fullPath };
        }

        _typeOptions.Clear();
        _typeOptions.Add("All Types");
        _typeOptions.AddRange(typeNames);
        if (!_typeOptions.Contains(_typeFilter)) _typeFilter = "";

        // A sub-asset never loads except as a side effect of loading its parent, so the parent
        // group should always exist by the time we get here; skip defensively if not.
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
            SortMode.Size => b.TotalSizeBytes.CompareTo(a.TotalSizeBytes), // biggest first
            SortMode.LastTouched => Nullable.Compare(a.Root.SinceTouched, b.Root.SinceTouched), // most recent first
            // Default: tracked pinned to the top, then alphabetical by path.
            _ => a.IsTracked != b.IsTracked ? (a.IsTracked ? -1 : 1)
                : string.Compare(a.RootPath, b.RootPath, StringComparison.OrdinalIgnoreCase),
        });
    }

    #endregion

    #region Toolbar

    private readonly List<string> _typeOptions = new();

    private void DrawToolbar(Paper paper, FontFile font, EditorAssetBackend db)
    {
        using (paper.Column("adb_tb_col").Height(65).Enter())
        {
            using (paper.Row("adb_tb1").Height(33).Padding(10, 8, 6, 0).RowBetween(6).Enter())
            {
                using (paper.Row("adb_search_wrap").Width(160).Height(24).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).Enter())
                    Origami.SearchField(paper, "adb_search", _searchText, v => _searchText = v, "Filter by name/path").Width(160).Height(24).Show();

                EditorGUI.ToolbarIconBtn(paper, "adb_idle_f", EditorIcons.Hourglass, _showOnlyIdle, () => _showOnlyIdle = !_showOnlyIdle);
                EditorGUI.ToolbarIconBtn(paper, "adb_lock_f", EditorIcons.Lock, _showOnlyLocked, () => _showOnlyLocked = !_showOnlyLocked);

                Origami.Dropdown(paper, "adb_type_dd",
                    Math.Max(0, _typeOptions.IndexOf(string.IsNullOrEmpty(_typeFilter) ? "All Types" : _typeFilter)),
                    v => _typeFilter = v == 0 ? "" : _typeOptions[v],
                    _typeOptions.ToArray()).Width(120).Show();

                paper.Box("adb_graph").Width(70).Height(20).IsNotInteractable()
                    .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) => DrawSparkline(canvas, r)));

                paper.Box("adb_sp");

                EditorGUI.ToolbarIconBtn(paper, "adb_export", EditorIcons.Clipboard, false, () => ExportToClipboard(paper));
                EditorGUI.CtaButton(paper, "adb_calcsize", "Calc Sizes", EditorTheme.Accent, () => RecalculateSizes(db));
                EditorGUI.CtaButton(paper, "adb_sweep", "Sweep Now", EditorTheme.Accent, () => db.ForceIdleSweep());
            }

            using (paper.Row("adb_tb2").Height(28).Padding(10, 8, 0, 4).RowBetween(6).Enter())
            {
                EditorGUI.StatChip(paper, "adb_chip_total", $"Loaded: {_totalCount}", font);
                EditorGUI.StatChip(paper, "adb_chip_idle", $"Idle: {_idleCount}", font);
                EditorGUI.StatChip(paper, "adb_chip_locked", $"Locked: {_lockedCount}", font);
                EditorGUI.StatChip(paper, "adb_chip_mem", _sizesComputed ? $"Memory: {FormatBytes(_totalBytes)}" : "Memory: not calculated", font);
                EditorGUI.StatChip(paper, "adb_chip_timeout", $"Timeout: {EditorAssetBackend.IdleTimeout.TotalSeconds:0}s", font);
            }
            EditorGUI.Divider(paper, "adb_tb_div");
        }
    }

    /// <summary>Computes (or refreshes) every currently-loaded asset's size estimate. Reads data
    /// from every resident asset, so this only runs when explicitly requested rather than every
    /// frame the panel happens to be open.</summary>
    private void RecalculateSizes(EditorAssetBackend db)
    {
        _cachedSizes.Clear();
        foreach (var (guid, asset) in db.GetLoadedAssets())
            _cachedSizes[guid] = EstimateBytes(asset);
        _sizesComputed = true;
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
        sb.AppendLine("Name\tType\tPath\tSize\tLastTouched\tStatus\tReloads");
        foreach (var g in _families)
        {
            AppendRow(sb, g.Root, g.RootPath);
            foreach (var s in g.Subs)
                AppendRow(sb, s, g.RootPath);
        }
        paper.SetClipboard(sb.ToString());
    }

    private static void AppendRow(StringBuilder sb, Row row, string path)
    {
        string since = row.SinceTouched is { } ts ? FormatSince(ts) : "never";
        string status = row.IsLocked ? "Locked" : row.IsIdle ? "Idle" : "Active";
        sb.AppendLine($"{row.Name}\t{row.TypeName}\t{path}\t{FormatBytes(row.SizeBytes)}\t{since}\t{status}\t{Math.Max(0, row.ReloadCount - 1)}");
    }

    #endregion

    #region List

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

        int activeCol = _sortBy switch { SortMode.Name => 0, SortMode.Type => 2, SortMode.Size => 3, SortMode.LastTouched => 4, _ => -1 };
        bool ascending = _sortBy != SortMode.Size && _sortBy != SortMode.LastTouched;

        Origami.Table(paper, "adb_table", -1, _ => { })
            .Bordered(false)
            .Scroll(width, height)
            .RowHeight(RowH)
            .Column("Name", 2.0f, sortable: true)
            .Column("Parent Path", 1.6f, sortable: false)
            .Column("Type", 0.8f, sortable: true)
            .Column("Size", 0.7f, sortable: true, align: TextAlignment.MiddleRight)
            .Column("Last Touched", 1.3f, sortable: true)
            .Column("Status", 0.9f, sortable: false, align: TextAlignment.MiddleRight)
            .Sort(activeCol, ascending, col => _sortBy = col switch
            {
                0 => SortMode.Name,
                2 => SortMode.Type,
                3 => SortMode.Size,
                4 => SortMode.LastTouched,
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

        menu.Separator();

        bool locked = AssetDatabase.IsLocked(guid);
        if (locked)
            menu.Item("Unlock", () => AssetDatabase.Unlock(guid), icon: EditorIcons.LockOpen);
        else
            menu.Item("Lock Permanently", () => AssetDatabase.LockPermanent(guid), icon: EditorIcons.Lock);

        menu.Item("Force Unload Now", () =>
        {
            AssetDatabase.ForceIdle(guid);
            EditorAssetBackend.Instance?.ForceIdleSweep();
        }, enabled: !locked, icon: EditorIcons.Trash);

        menu.Separator();

        menu.Item("Reveal in Project", () =>
        {
            var db = EditorAssetBackend.Instance;
            var target = db != null && db.TryGetParentGuid(guid, out var parentGuid) ? parentGuid : guid;
            Selection.Ping(target);
        }, icon: EditorIcons.ArrowUpRightFromSquare);
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
                long size = isSub ? row.SizeBytes : group.TotalSizeBytes;
                paper.Box($"adb_size_{id}").Height(RowH).IsNotInteractable()
                    .Text(FormatBytes(size), mono).TextColor(EditorTheme.InkDim)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleRight);
                break;

            case 4:
                paper.Box($"adb_since_{id}").Height(RowH).IsNotInteractable()
                    .Text(FormatActivity(row), font).TextColor(EditorTheme.InkDim)
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

    private static string FormatActivity(Row row)
    {
        string since = row.SinceTouched is { } ts ? FormatSince(ts) : "never";
        int reloads = Math.Max(0, row.ReloadCount - 1);
        string suffix;
        if (row.IsLocked)
            suffix = "locked";
        else if (row.IsIdle)
            suffix = "idle";
        else if (row.SinceTouched is { } sts)
            suffix = $"evicts in {FormatDuration(EditorAssetBackend.IdleTimeout - sts)}";
        else
            suffix = "";

        string text = string.IsNullOrEmpty(suffix) ? since : $"{since} ({suffix})";
        return reloads >= 2 ? $"{text} · {reloads} reloads" : text;
    }

    private static string FormatDuration(TimeSpan ts)
    {
        if (ts.TotalSeconds < 0) return "0s";
        if (ts.TotalMinutes < 1) return $"{(int)ts.TotalSeconds}s";
        return $"{(int)ts.TotalMinutes}m {(int)ts.TotalSeconds % 60}s";
    }

    private static string FormatSince(TimeSpan ts)
    {
        if (ts.TotalSeconds < 2) return "just now";
        if (ts.TotalMinutes < 1) return $"{(int)ts.TotalSeconds}s ago";
        if (ts.TotalHours < 1) return $"{(int)ts.TotalMinutes}m ago";
        return $"{(int)ts.TotalHours}h ago";
    }

    #endregion

    #region Memory Estimation

    // Rough resident-memory estimate for a loaded asset. Approximate by design (ignores mip chains,
    // GPU alignment/padding, driver overhead) - good enough to spot which assets are actually heavy,
    // not a byte-accurate profiler.
    private static long EstimateBytes(EngineObject asset) => asset switch
    {
        Texture2D tex => (long)tex.Width * tex.Height * Texture.GetBytesPerPixel(tex.ImageFormat),
        Texture3D tex => (long)tex.Width * tex.Height * tex.Depth * Texture.GetBytesPerPixel(tex.ImageFormat),
        Cubemap cube => (long)cube.FaceByteSize(0) * 6,
        RenderTexture rt => EstimateRenderTexture(rt),
        Mesh mesh => EstimateMesh(mesh),
        AudioClip clip => (long)clip.DataSize,
        _ => 0,
    };

    private static long EstimateRenderTexture(RenderTexture rt)
    {
        long total = 0;
        foreach (var tex in rt.InternalTextures)
            total += (long)tex.Width * tex.Height * Texture.GetBytesPerPixel(tex.ImageFormat);
        return total;
    }

    private static long EstimateMesh(Mesh mesh)
    {
        long total = 0;
        int vc = mesh.VertexCount;

        total += vc * 12; // Vertices (Float3)
        if (mesh.HasNormals) total += vc * 12;
        if (mesh.HasTangents) total += vc * 16;
        if (mesh.HasColors) total += vc * 16;
        if (mesh.HasColors32) total += vc * 4;
        if (mesh.HasUV) total += vc * 8;
        if (mesh.HasUV2) total += vc * 8;
        if (mesh.HasBoneIndices) total += vc * 16;
        if (mesh.HasBoneWeights) total += vc * 16;

        total += mesh.IndexCount * (mesh.IndexFormat == IndexFormat.UInt16 ? 2 : 4);
        return total;
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

    private void DrawDetails(Paper paper, FontFile font, float width, float height, EditorAssetBackend db, Guid guid, bool showStackTrace)
    {
        var mono = EditorTheme.FontMono ?? font;

        using (paper.Column("adb_details").Width(width).Height(height).Padding(10, 10, 6, 6).Enter())
        {
            EditorGUI.Divider(paper, "adb_details_div");

            DrawDependencySection(paper, font, mono, db, guid);

            if (showStackTrace)
            {
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
                    Origami.ScrollView(paper, "adb_details_scroll", width, height - 32 - InfoOnlyHeight).Body(() =>
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
    }

    private void DrawDependencySection(Paper paper, FontFile font, FontFile mono, EditorAssetBackend db, Guid guid)
    {
        var deps = db.Dependencies.GetDependencies(guid);
        var dependents = db.Dependencies.GetDependents(guid);

        using (paper.Row("adb_dep_row").Height(60).ColBetween(16).Enter())
        {
            DrawGuidList(paper, font, mono, db, "adb_dep_out", $"Depends on ({deps.Count})", deps);
            DrawGuidList(paper, font, mono, db, "adb_dep_in", $"Used by ({dependents.Count})", dependents);
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
                    string label = LabelFor(db, g);
                    paper.Box($"{id}_item_{i}").Height(16).Clip()
                        .OnClick(g, (guid, _) => { _selectedGuid = guid; Selection.Ping(guid); })
                        .Text(label, mono).TextColor(EditorTheme.AccentText)
                        .FontSize(EditorTheme.FontSizeSmall - 1).Alignment(TextAlignment.MiddleLeft);
                    i++;
                }
            });
        }
    }

    private static string LabelFor(EditorAssetBackend db, Guid guid)
    {
        var entry = db.GetEntry(guid);
        if (entry != null) return entry.Path;

        if (db.TryGetParentGuid(guid, out var parentGuid))
        {
            var parentEntry = db.GetEntry(parentGuid);
            var sub = parentEntry?.SubAssets.FirstOrDefault(s => s.Guid == guid);
            if (parentEntry != null && sub != null) return $"{parentEntry.Path}#{sub.Name}";
        }

        return guid.ToString()[..8];
    }

    #endregion
}
