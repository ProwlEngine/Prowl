// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI;

/// <summary>File dialog mode.</summary>
public enum FileDialogMode { Open, Save, SelectFolder }

/// <summary>
/// Configuration hooks for the Origami file dialog. The caller owns persistence
/// for favorites, recent files, and icon resolution. Origami just renders them.
/// </summary>
public sealed class FileDialogConfig
{
    public Func<string, bool, string>? GetIcon;
    public List<(string Label, string Icon, string Path)> QuickAccess = [];
    public List<(string Label, string Path)> Favorites = [];
    public Action<string>? OnAddFavorite;
    public Action<int>? OnRemoveFavorite;
    public List<string> RecentFiles = [];
    public Action<string>? OnFileOpened;
    public Func<(string Label, string Path)[]>? GetDrives;
}

/// <summary>
/// Static Origami file dialog. Only one can be open at a time.
/// Call Open() to show, Draw() each frame, handles its own close.
/// </summary>
public static class FileDialog
{
    // ── State ────────────────────────────────────────────────
    // The floating overlay is just the shared browser rendered on the modal stack over its own
    // EmbeddedState — the exact same code path as an inline DrawEmbedded.
    private static bool _isOpen;
    private static Action<string?>? _onComplete;
    private static FileDialogConfig? _config;
    private static FileDialogMode _overlayMode;
    private static EmbeddedState _overlayState = new();
    private static IModal? _modalHandle;

    public static bool IsOpen => _isOpen;

    // ── Nebula palette literals ──────────────────────────────
    // Frosted magenta-violet glass over a dark void. Ramps cover most of the surface;
    // these carry the exact alpha the prototype (.w2fd*) uses for glass and accents.
    private static readonly Color WindowBg   = Color.FromArgb(235, 14, 11, 22);    // window body (dark glass)
    private static readonly Color Selection  = Color.FromArgb(230, 168, 85, 247);  // selected sidebar item
    private static readonly Color SideBg     = Color.FromArgb(36, 0, 0, 0);        // sidebar background

    // ── API ──────────────────────────────────────────────────

    /// <summary>Open the file dialog as a floating modal. Renders the same browser as
    /// <see cref="DrawEmbedded"/>, centred on screen over the modal-stack backdrop.</summary>
    public static void Open(FileDialogMode mode, Action<string?> onComplete,
        string? startPath = null, string[]? filters = null, string[]? filterLabels = null,
        FileDialogConfig? config = null)
    {
        _isOpen = true;
        _onComplete = onComplete;
        _config = config;
        _overlayMode = mode;

        _overlayState = new EmbeddedState();
        string path = startPath ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        if (!Directory.Exists(path)) path = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        EnsureInit(_overlayState, path);

        _modalHandle = new CustomDrawModal((paper, layer, stackIndex) =>
            RenderBrowser(paper, "__fd_overlay", 820f, 560f, _overlayState, _config, _overlayMode,
                onChoose: p => Close(p),
                onClose: () => Close(null),
                floating: true, layer: layer))
        { CloseOnEscape = true };
        Modal.Push(_modalHandle);
    }

    public static void Close(string? result = null)
    {
        _isOpen = false;
        if (_modalHandle != null) { Modal.Remove(_modalHandle); _modalHandle = null; }
        if (result != null) _config?.OnFileOpened?.Invoke(result);
        var cb = _onComplete;
        _onComplete = null;
        cb?.Invoke(result);
    }

    // ── Embedded (inline) browser ────────────────────────────
    // A self-contained file browser rendered in normal layout flow. It keeps its own
    // per-id state, entirely separate from the static overlay above, so an embedded
    // browser and a modal FileDialog.Open() can be on screen at the same time.

    private sealed class EmbeddedState
    {
        public string Path = "";
        public string Selected = "";
        public string FileName = "";
        public string Search = "";
        public List<FileEntry> Entries = [];
        public int SortColumn;
        public bool SortAscending = true;
        public bool Initialized;

        // Navigation history (back / forward)
        public List<string> History = [];
        public int HistoryIndex = -1;

        // Inline new-folder creation
        public bool CreatingFolder;
        public string NewFolderName = "New Folder";

        // Inline rename (holds the entry's current full path while editing)
        public string Renaming = "";
        public string RenameName = "";
    }

    private static readonly Dictionary<string, EmbeddedState> _embedded = [];

    /// <summary>
    /// Draw a self-contained file browser inline at the current layout position (no modal
    /// stack, no backdrop). Maintains its own state keyed by <paramref name="id"/>, so it
    /// coexists with the overlay <see cref="Open"/> dialog.
    /// </summary>
    public static void DrawEmbedded(Paper paper, string id, float width, float height,
        string? startPath = null, FileDialogConfig? config = null)
    {
        if (!_embedded.TryGetValue(id, out var st))
            _embedded[id] = st = new EmbeddedState();
        EnsureInit(st, startPath);
        RenderBrowser(paper, id, width, height, st, config, FileDialogMode.Open,
            onChoose: config?.OnFileOpened, onClose: null, floating: false, layer: 0);
    }

    private static void EnsureInit(EmbeddedState st, string? startPath)
    {
        if (st.Initialized) return;
        string p = startPath ?? AppContext.BaseDirectory;
        if (!Directory.Exists(p)) p = AppContext.BaseDirectory;
        st.Path = Path.GetFullPath(p);
        st.Entries = LoadDir(st.Path, st.SortColumn, st.SortAscending);
        st.History = new List<string> { st.Path };
        st.HistoryIndex = 0;
        st.Initialized = true;
    }

    /// <summary>
    /// The single file-browser renderer, shared by the floating <see cref="Open"/> overlay and the
    /// inline <see cref="DrawEmbedded"/>. When <paramref name="floating"/> the window is positioned
    /// centre-screen on <paramref name="layer"/> (the modal stack draws the backdrop); otherwise it
    /// renders in normal layout flow. Both paths are identical apart from that outer wrapper.
    /// </summary>
    private static void RenderBrowser(Paper paper, string id, float width, float height,
        EmbeddedState st, FileDialogConfig? config, FileDialogMode mode,
        Action<string>? onChoose, Action? onClose, bool floating, int layer)
    {
        var theme = Origami.Current;
        var font = theme.Font;
        if (font == null) return;
        var ink = theme.Ink;
        var m = theme.Metrics;
        var titleFont = theme.SemiBold ?? font;
        var labelFont = theme.Medium ?? font;

        // ── navigation / sort (local, per-id) ────────────────────
        void Reload() => st.Entries = LoadDir(st.Path, st.SortColumn, st.SortAscending);

        void NavigateTo(string path, bool addHistory)
        {
            if (!Directory.Exists(path)) return;
            st.Path = Path.GetFullPath(path);
            st.Selected = "";
            st.CreatingFolder = false;
            st.Renaming = "";
            Reload();
            if (addHistory)
            {
                if (st.HistoryIndex < st.History.Count - 1)
                    st.History.RemoveRange(st.HistoryIndex + 1, st.History.Count - st.HistoryIndex - 1);
                st.History.Add(st.Path);
                st.HistoryIndex = st.History.Count - 1;
            }
        }
        void NavBack() { if (st.HistoryIndex > 0) { st.HistoryIndex--; st.Path = st.History[st.HistoryIndex]; st.Selected = ""; Reload(); } }
        void NavFwd() { if (st.HistoryIndex < st.History.Count - 1) { st.HistoryIndex++; st.Path = st.History[st.HistoryIndex]; st.Selected = ""; Reload(); } }
        void NavUp() { var p = Directory.GetParent(st.Path); if (p != null) NavigateTo(p.FullName, true); }
        void SetSort(int col)
        {
            if (st.SortColumn == col) st.SortAscending = !st.SortAscending;
            else { st.SortColumn = col; st.SortAscending = true; }
            Reload();
        }

        // ── file operations (right-click menu) ───────────────────
        void BeginRename(string fullPath, string name)
        {
            st.Renaming = fullPath; st.RenameName = name; st.CreatingFolder = false;
        }
        void CommitRename()
        {
            if (!string.IsNullOrEmpty(st.Renaming) && !string.IsNullOrWhiteSpace(st.RenameName))
            {
                try
                {
                    string dir = Path.GetDirectoryName(st.Renaming) ?? st.Path;
                    string dst = Path.Combine(dir, st.RenameName);
                    if (dst != st.Renaming)
                    {
                        if (Directory.Exists(st.Renaming)) Directory.Move(st.Renaming, dst);
                        else if (File.Exists(st.Renaming)) File.Move(st.Renaming, dst);
                    }
                }
                catch { }
            }
            st.Renaming = "";
            Reload();
        }
        void Duplicate(string fullPath)
        {
            try
            {
                if (File.Exists(fullPath))
                {
                    string dir = Path.GetDirectoryName(fullPath) ?? st.Path;
                    string stem = Path.GetFileNameWithoutExtension(fullPath), ext = Path.GetExtension(fullPath);
                    string dst = Path.Combine(dir, $"{stem} copy{ext}");
                    int n = 2;
                    while (File.Exists(dst) || Directory.Exists(dst)) dst = Path.Combine(dir, $"{stem} copy {n++}{ext}");
                    File.Copy(fullPath, dst);
                }
            }
            catch { }
            Reload();
        }
        void DeleteEntry(string fullPath, bool isDir)
        {
            try
            {
                if (isDir && Directory.Exists(fullPath)) Directory.Delete(fullPath, true);
                else if (File.Exists(fullPath)) File.Delete(fullPath);
            }
            catch { }
            if (st.Selected == fullPath) st.Selected = "";
            Reload();
        }

        Action<Canvas, Rect> Ico(IconPainter p, Color col) =>
            (canvas, rect) => DrawIcon(canvas, rect, p, (float)rect.Size.X, col);

        void RowContextMenu(int kind, string name, string path, bool isDir)
        {
            float px = (float)paper.PointerPos.X, py = (float)paper.PointerPos.Y;
            ContextMenu.Show(px, py, b =>
            {
                b.Header(name);
                b.Item("Open", () => { if (isDir) NavigateTo(path, true); else onChoose?.Invoke(path); },
                    iconDraw: Ico(isDir ? DrawFolder : DrawFile, ink.C400));
                b.Item("Copy Path", () => paper.SetClipboard(path), iconDraw: Ico(DrawCopy, ink.C400));
                if (kind == 0)
                {
                    b.Item("Rename", () => BeginRename(path, name), shortcut: "F2", iconDraw: Ico(DrawPencil, ink.C400));
                    if (!isDir) b.Item("Duplicate", () => Duplicate(path), shortcut: "Ctrl D", iconDraw: Ico(DrawCopy, ink.C400));
                    b.Separator();
                    b.Item("Delete", () => DeleteEntry(path, isDir), shortcut: "Del", danger: true, iconDraw: Ico(DrawTrash, theme.Red.C500));
                }
            });
        }

        // ── layout metrics ───────────────────────────────────────
        const float toolbarH = 34f, footH = 44f, sideW = 142f;
        const float iconW = 26f, rowPadL = 12f;
        float bodyH = height - toolbarH - footH - 2f;   // dividers below toolbar + above foot
        float listAreaW = width - sideW - 1f;           // vertical divider

        var display = string.IsNullOrEmpty(st.Search)
            ? st.Entries
            : st.Entries.Where(e => e.Name.Contains(st.Search, StringComparison.OrdinalIgnoreCase)).ToList();

        // ── small reusable pieces ────────────────────────────────
        void TbBtn(string bid, IconPainter painter, bool enabled, Action onClick)
        {
            var col = enabled ? ink.C400 : ink.C200;
            var b = paper.Box(bid).Width(30).Height(26).Rounded(6)
                .Margin(0, 0, UnitValue.Stretch(), UnitValue.Stretch());   // vertically center in the toolbar row
            if (enabled) { b.Hovered.BackgroundColor(theme.Hover).End(); b.OnClick(0, (_, _) => onClick()); }
            using (b.Enter())
                paper.Draw((canvas, rect) => DrawIcon(canvas, rect, painter, 15f, col));
        }

        // Wrap a self-rendering control (TextField/Button) so it centers vertically in a taller row.
        void VC(string wid, UnitValue w, float h, Action inner)
        {
            using (paper.Row(wid).Width(w).Height(h).ChildTop().ChildBottom().Enter())
                inner();
        }

        void Breadcrumb()
        {
            var parts = st.Path.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);
            var crumbs = new List<(string Label, string Path)>();
            string accum = "";
            for (int i = 0; i < parts.Length; i++)
            {
                accum = i == 0 ? parts[0] + Path.DirectorySeparatorChar : Path.Combine(accum, parts[i]);
                crumbs.Add((parts[i], accum));
            }

            const int maxCrumbs = 4;
            int start = Math.Max(0, crumbs.Count - maxCrumbs);

            using (paper.Row($"{id}_crumbs").Width(UnitValue.Stretch()).Height(toolbarH)
                .ChildLeft(4).RowBetween(1).Clip().Enter())
            {
                void Sep(string sid) => paper.Box(sid).Width(UnitValue.Auto).Height(toolbarH).ChildLeft(2).ChildRight(2)
                    .Text(">", font).TextColor(ink.C200).FontSize(m.FontSizeSmall - 1).Alignment(TextAlignment.MiddleCenter);

                if (start > 0)
                {
                    string tp = crumbs[start - 1].Path;
                    var eb = paper.Box($"{id}_cr_ell").Width(UnitValue.Auto).Height(toolbarH).ChildLeft(5).ChildRight(5).Rounded(5)
                        .Text("...", font).TextColor(ink.C300).FontSize(m.FontSizeSmall).Alignment(TextAlignment.MiddleCenter)
                        .Hovered.BackgroundColor(theme.Hover).End();
                    eb.OnClick(0, (_, _) => NavigateTo(tp, true));
                    Sep($"{id}_cr_ellsep");
                }

                for (int i = start; i < crumbs.Count; i++)
                {
                    var (label, cpath) = crumbs[i];
                    bool last = i == crumbs.Count - 1;
                    var cb = paper.Box($"{id}_cr_{i}").Width(UnitValue.Auto).Height(toolbarH).ChildLeft(5).ChildRight(5).Rounded(5)
                        .Text(label, last ? titleFont : font).TextColor(last ? ink.C500 : ink.C300)
                        .FontSize(m.FontSizeSmall).Alignment(TextAlignment.MiddleCenter);
                    if (!last)
                    {
                        cb.Hovered.BackgroundColor(theme.Hover).End();
                        cb.OnClick(0, (_, _) => NavigateTo(cpath, true));
                        Sep($"{id}_cr_s{i}");
                    }
                }
            }
        }

        void SideSection(string key, string label) =>
            paper.Box($"{id}_sl_{key}").Height(m.CompactHeight).ChildLeft(6)
                .Text(label.ToUpperInvariant(), labelFont).TextColor(ink.C200)
                .FontSize(m.FontSizeSmall - 1).Alignment(TextAlignment.MiddleLeft);

        void SideSep(string key) =>
            paper.Box($"{id}_ss_{key}").Height(1).Margin(4, 5, 4, 5).BackgroundColor(theme.BorderSoft);

        void SideRow(string sid, IconPainter painter, Color iconCol, string label, string path)
        {
            bool sel = st.Path.Equals(path, StringComparison.OrdinalIgnoreCase);
            var r = paper.Row(sid).Height(m.RowHeight)
                .BackgroundColor(sel ? Selection : Color.Transparent)
                .Hovered.BackgroundColor(sel ? Selection : theme.Hover).End()
                .Rounded(6).ChildLeft(7).RowBetween(6);
            r.OnClick(0, (_, _) => NavigateTo(path, true));
            using (r.Enter())
            {
                var ic = sel ? ink.C600 : iconCol;
                using (paper.Box(sid + "_i").Width(18).Height(m.RowHeight).IsNotInteractable().Enter())
                    paper.Draw((canvas, rect) => DrawIcon(canvas, rect, painter, 14f, ic));
                paper.Box(sid + "_l").Width(UnitValue.Stretch()).Height(m.RowHeight)
                    .Text(label, font).TextColor(sel ? ink.C600 : ink.C300)
                    .FontSize(m.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
            }
        }

        // ── window ───────────────────────────────────────────────
        var win = paper.Column($"{id}_win").Size(width, height)
            .BackgroundColor(WindowBg)
            .BorderColor(theme.BorderSoft).BorderWidth(1).Rounded(9).Clip();
        if (floating)
        {
            float sw = (float)paper.ScreenRect.Size.X, sh = (float)paper.ScreenRect.Size.Y;
            win.PositionType(PositionType.SelfDirected)
               .Position((sw - width) * 0.5f, (sh - height) * 0.5f)
               .BoxShadow(0, 24, 64, 0, Color.FromArgb(166, 0, 0, 0))
               .Layer(layer).StopEventPropagation();
        }

        using (win.Enter())
        {
            // Toolbar: back / fwd / up + breadcrumb + search + new folder (+ close X when floating).
            // Use Padding (not ChildLeft/Right): the edge buttons set an explicit Margin for vertical
            // centering, which would override container child-margins and touch the edges.
            using (paper.Row($"{id}_tb").Height(toolbarH)
                .BackgroundColor(theme.Glass).RoundedTop(9f)
                .Padding(10, 10, 0, 0).RowBetween(4).Enter())
            {
                TbBtn($"{id}_back", DrawBack, st.HistoryIndex > 0, NavBack);
                TbBtn($"{id}_fwd", DrawForward, st.HistoryIndex < st.History.Count - 1, NavFwd);
                TbBtn($"{id}_up", DrawUp, Directory.GetParent(st.Path) != null, NavUp);
                Breadcrumb();
                VC($"{id}_swrap", UnitValue.Pixels(132), toolbarH, () =>
                    Origami.TextField(paper, $"{id}_search", st.Search, v => st.Search = v)
                        .Search("Search").Width(UnitValue.Stretch()).Height(26).Show());
                TbBtn($"{id}_newf", DrawPlus, true, () => { st.CreatingFolder = !st.CreatingFolder; st.NewFolderName = "New Folder"; });
                if (onClose != null)
                    TbBtn($"{id}_close", DrawClose, true, onClose);
            }
            paper.Box($"{id}_tbsep").Height(1).BackgroundColor(theme.BorderSoft);

            // Body: sidebar + (column header + list)
            using (paper.Row($"{id}_body").Height(bodyH).Enter())
            {
                using (paper.Column($"{id}_side").Width(sideW).Height(UnitValue.Stretch())
                    .BackgroundColor(SideBg)
                    .Padding(7, 7, 7, 7).ColBetween(1).Enter())
                {
                    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    string downloads = string.IsNullOrEmpty(home) ? "" : Path.Combine(home, "Downloads");

                    SideSection("qa", "Quick Access");
                    if (Directory.Exists(home)) SideRow($"{id}_qa_home", DrawHome, theme.Primary.C500, "Home", home);
                    if (Directory.Exists(desktop)) SideRow($"{id}_qa_desk", DrawFolder, theme.Amber.C500, "Desktop", desktop);
                    if (Directory.Exists(docs)) SideRow($"{id}_qa_docs", DrawDoc, theme.Blue.C500, "Documents", docs);
                    if (Directory.Exists(downloads)) SideRow($"{id}_qa_dl", DrawFolder, theme.Amber.C500, "Downloads", downloads);

                    SideSep("drv");
                    SideSection("drv", "Drives");
                    try
                    {
                        foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady))
                        {
                            string dn = d.Name;
                            string dlabel = string.IsNullOrWhiteSpace(d.VolumeLabel) ? dn : $"{d.VolumeLabel} ({dn.TrimEnd(Path.DirectorySeparatorChar)})";
                            SideRow($"{id}_drv_{dn}", DrawDrive, ink.C400, dlabel, dn);
                        }
                    }
                    catch { }
                }
                paper.Box($"{id}_vsep").Width(1).Height(UnitValue.Stretch()).BackgroundColor(theme.BorderSoft);

                using (paper.Column($"{id}_list_area").Width(UnitValue.Stretch()).Height(UnitValue.Stretch()).Enter())
                {
                    // Right-click on empty list space: new folder / refresh (row clicks open their own menu).
                    ContextMenu.RightClickMenu(paper, $"{id}_bgctx", b => b
                        .Item("New Folder", () => { st.CreatingFolder = true; st.NewFolderName = "New Folder"; st.Renaming = ""; }, iconDraw: Ico(DrawPlus, ink.C400))
                        .Item("Refresh", () => Reload(), iconDraw: Ico(DrawRefresh, ink.C400))
                        .Separator()
                        .Item("Paste", () => { }, enabled: false, iconDraw: Ico(DrawCopy, ink.C200)));

                    float tableH = bodyH;

                    // inline rename (above the table; Table can't host an editor row)
                    if (!string.IsNullOrEmpty(st.Renaming))
                    {
                        tableH -= m.RowHeight + 4f;
                        using (paper.Row($"{id}_rn").Height(m.RowHeight).BackgroundColor(theme.Selected)
                            .ChildLeft(rowPadL).RowBetween(6).Margin(0, 0, 0, 4).Enter())
                        {
                            using (paper.Box($"{id}_rn_i").Width(iconW).Height(m.RowHeight).IsNotInteractable().Enter())
                                paper.Draw((canvas, rect) => DrawIcon(canvas, rect, DrawPencil, 15f, theme.Primary.C500));
                            Origami.TextField(paper, $"{id}_rn_n", st.RenameName, v => st.RenameName = v)
                                .Placeholder("New name").Width(UnitValue.Stretch()).Show();
                            Origami.Button(paper, $"{id}_rn_ok", "Rename", CommitRename).Width(64).Show();
                            Origami.Button(paper, $"{id}_rn_x", "Cancel", () => st.Renaming = "").Width(64).Show();
                        }
                    }

                    // inline new-folder creation (above the table; Table can't host an editor row)
                    if (st.CreatingFolder)
                    {
                        tableH -= m.RowHeight + 4f;
                        using (paper.Row($"{id}_nf").Height(m.RowHeight).BackgroundColor(theme.Selected)
                            .ChildLeft(rowPadL).RowBetween(6).Margin(0, 0, 0, 4).Enter())
                        {
                            using (paper.Box($"{id}_nf_i").Width(iconW).Height(m.RowHeight).IsNotInteractable().Enter())
                                paper.Draw((canvas, rect) => DrawIcon(canvas, rect, DrawFolder, 15f, theme.Amber.C500));
                            Origami.TextField(paper, $"{id}_nf_n", st.NewFolderName, v => st.NewFolderName = v)
                                .Placeholder("Name").Width(UnitValue.Stretch()).Show();
                            Origami.Button(paper, $"{id}_nf_ok", "Create", () =>
                            {
                                try { Directory.CreateDirectory(Path.Combine(st.Path, st.NewFolderName)); } catch { }
                                st.CreatingFolder = false;
                                Reload();
                            }).Width(64).Show();
                            Origami.Button(paper, $"{id}_nf_x", "Cancel", () => st.CreatingFolder = false).Width(64).Show();
                        }
                    }

                    // Row model: optional ".." parent + entries, or an empty-message sentinel.
                    // Kind: 0 = entry, 1 = parent (".."), 2 = message.
                    var parent = Directory.GetParent(st.Path);
                    var rows = new List<(int Kind, string Name, string Path, bool IsDir, long Size, DateTime Mod)>();
                    if (parent != null) rows.Add((1, "..", parent.FullName, true, 0L, default));
                    foreach (var e in display) rows.Add((0, e.Name, e.FullPath, e.IsDirectory, e.Size, e.LastModified));
                    if (display.Count == 0)
                        rows.Add((2, string.IsNullOrEmpty(st.Search) ? "This folder is empty" : "No matches", "", false, 0L, default));

                    int selIndex = -1;
                    for (int i = 0; i < rows.Count; i++)
                        if (rows[i].Kind == 0 && rows[i].Path == st.Selected) { selIndex = i; break; }

                    var table = Origami.Table(paper, $"{id}_tbl", selIndex, i =>
                        {
                            if (i < 0 || i >= rows.Count) return;
                            var it = rows[i];
                            if (it.Kind != 0) { st.Selected = ""; return; }
                            st.Selected = it.Path;
                            if (!it.IsDir) st.FileName = it.Name;
                        })
                        .Scroll(listAreaW, tableH)
                        .Bordered(false)
                        .Virtualize()
                        .RowHeight(m.RowHeight)
                        .Column("Name", 2f, true)
                        .Column("Size", 0.8f, true, TextAlignment.MiddleRight)
                        .Column("Modified", 1.1f, true)
                        .Sort(st.SortColumn, st.SortAscending, col => SetSort(col))
                        .OnRowActivate(i =>
                        {
                            if (i < 0 || i >= rows.Count) return;
                            var it = rows[i];
                            if (it.Kind == 2) return;
                            if (it.IsDir) NavigateTo(it.Path, true);
                            else onChoose?.Invoke(it.Path);
                        })
                        .OnRowContext(i =>
                        {
                            if (i < 0 || i >= rows.Count) return;
                            var it = rows[i];
                            if (it.Kind == 2) return;
                            RowContextMenu(it.Kind, it.Name, it.Path, it.IsDir);
                        });

                    foreach (var it in rows)
                    {
                        if (it.Kind == 2) { table.Row().Cell(it.Name, ink.C200); continue; }

                        string ext = it.IsDir ? "" : Path.GetExtension(it.Name);
                        var painter = it.IsDir ? DrawFolder : FileGlyph(ext);
                        var iconCol = it.IsDir ? theme.Amber.C500 : FileTint(theme, ink, ext);
                        table.Row()
                            .Cell(it.Name, it.Kind == 1 ? ink.C300 : ink.C400,
                                  (canvas, rect) => DrawIcon(canvas, rect, painter, (float)rect.Size.X, iconCol))
                            .CellRight(it.IsDir ? "" : FormatSize(it.Size), ink.C200)
                            .Cell(it.Kind == 1 ? "" : it.Mod.ToString("yyyy-MM-dd  HH:mm"), ink.C200);
                    }

                    table.Show();
                }
            }

            // Foot: filename field + Cancel + primary action (label follows the mode)
            paper.Box($"{id}_fsep").Height(1).BackgroundColor(theme.BorderSoft);
            string primaryLabel = mode switch
            {
                FileDialogMode.Save => "Save",
                FileDialogMode.SelectFolder => "Select Folder",
                _ => "Open",
            };
            using (paper.Row($"{id}_foot").Height(footH)
                .BackgroundColor(theme.Glass).RoundedBottom(9f)
                .ChildLeft(11).ChildRight(11).RowBetween(8).Enter())
            {
                paper.Box($"{id}_foot_l").Width(UnitValue.Auto).Height(footH)
                    .Text(mode == FileDialogMode.SelectFolder ? "Folder:" : "File name:", font).TextColor(ink.C200)
                    .FontSize(m.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
                VC($"{id}_fnwrap", UnitValue.Stretch(), footH, () =>
                    Origami.TextField(paper, $"{id}_fn", st.FileName, v => st.FileName = v)
                        .Width(UnitValue.Stretch()).Show());
                VC($"{id}_cwrap", UnitValue.Auto, footH, () =>
                    Origami.Button(paper, $"{id}_cancel", "Cancel", () =>
                    {
                        if (onClose != null) onClose();
                        else { st.Selected = ""; st.FileName = ""; }
                    }).Show());
                VC($"{id}_owrap", UnitValue.Auto, footH, () =>
                    Origami.Button(paper, $"{id}_open", primaryLabel, () =>
                    {
                        string target = mode == FileDialogMode.SelectFolder
                            ? (!string.IsNullOrEmpty(st.Selected) && Directory.Exists(st.Selected) ? st.Selected : st.Path)
                            : (!string.IsNullOrEmpty(st.Selected) ? st.Selected
                                : (!string.IsNullOrEmpty(st.FileName) ? Path.Combine(st.Path, st.FileName) : ""));
                        if (string.IsNullOrEmpty(target)) return;
                        if (Directory.Exists(target) && mode != FileDialogMode.SelectFolder) NavigateTo(target, true);
                        else onChoose?.Invoke(target);
                    }).Primary().Show());
            }
        }
    }

    // ── Vector icons (Origami's icon font is empty, so these stroke onto the canvas) ──

    private delegate void IconPainter(Canvas canvas, float cx, float cy, float size, Color color);

    private static readonly HashSet<string> _imgExts = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".svg", ".webp", ".ico", ".tga", ".tiff" };
    private static readonly HashSet<string> _codeExts = new(StringComparer.OrdinalIgnoreCase)
        { ".cs", ".js", ".ts", ".py", ".cpp", ".c", ".h", ".hpp", ".java", ".json", ".xml",
          ".html", ".css", ".go", ".rs", ".rb", ".php", ".sh", ".lua", ".glsl", ".shader", ".yml", ".yaml", ".toml" };

    private static IconPainter FileGlyph(string ext) =>
        _imgExts.Contains(ext) ? DrawImage : _codeExts.Contains(ext) ? DrawCode : DrawFile;

    private static Color FileTint(OrigamiTheme theme, OrigamiRamp ink, string ext) =>
        _imgExts.Contains(ext) ? theme.Blue.C500 : _codeExts.Contains(ext) ? theme.Green.C500 : ink.C300;

    private static void DrawIcon(Canvas canvas, Rect rect, IconPainter painter, float size, Color color)
    {
        float cx = (float)(rect.Min.X + rect.Size.X * 0.5);
        float cy = (float)(rect.Min.Y + rect.Size.Y * 0.5);
        painter(canvas, cx, cy, size, color);
    }

    private static void Pen(Canvas c, Color color, float w)
    {
        c.SetStrokeColor(Color32.FromArgb(color.A, color.R, color.G, color.B));
        c.SetStrokeWidth(w);
        c.SetStrokeCap(EndCapStyle.Round);
        c.SetStrokeJoint(JointStyle.Round);
    }

    private static void DrawFolder(Canvas c, float cx, float cy, float s, Color col)
    {
        float w = s * 0.92f, h = s * 0.74f;
        float l = cx - w / 2, r = cx + w / 2, t = cy - h / 2, b = cy + h / 2;
        float tabW = w * 0.42f, lip = t + h * 0.24f;
        c.SaveState(); Pen(c, col, 1.5f);
        c.BeginPath();
        c.MoveTo(l, b); c.LineTo(l, t); c.LineTo(l + tabW, t);
        c.LineTo(l + tabW + w * 0.12f, lip); c.LineTo(r, lip); c.LineTo(r, b);
        c.ClosePath(); c.Stroke();
        c.RestoreState();
    }

    private static void DrawFile(Canvas c, float cx, float cy, float s, Color col)
    {
        float w = s * 0.72f, h = s * 0.92f;
        float l = cx - w / 2, r = cx + w / 2, t = cy - h / 2, b = cy + h / 2;
        float f = s * 0.28f;
        c.SaveState(); Pen(c, col, 1.4f);
        c.BeginPath();
        c.MoveTo(l, t); c.LineTo(r - f, t); c.LineTo(r, t + f); c.LineTo(r, b); c.LineTo(l, b); c.ClosePath();
        c.MoveTo(r - f, t); c.LineTo(r - f, t + f); c.LineTo(r, t + f);
        c.Stroke();
        c.RestoreState();
    }

    private static void DrawDoc(Canvas c, float cx, float cy, float s, Color col)
    {
        float w = s * 0.72f, h = s * 0.92f;
        float l = cx - w / 2, r = cx + w / 2, t = cy - h / 2, b = cy + h / 2;
        float f = s * 0.26f;
        c.SaveState(); Pen(c, col, 1.4f);
        c.BeginPath();
        c.MoveTo(l, t); c.LineTo(r - f, t); c.LineTo(r, t + f); c.LineTo(r, b); c.LineTo(l, b); c.ClosePath();
        c.MoveTo(r - f, t); c.LineTo(r - f, t + f); c.LineTo(r, t + f);
        float lx0 = l + w * 0.22f, lx1 = r - w * 0.18f;
        c.MoveTo(lx0, cy); c.LineTo(lx1, cy);
        c.MoveTo(lx0, cy + h * 0.2f); c.LineTo(lx1, cy + h * 0.2f);
        c.Stroke();
        c.RestoreState();
    }

    private static void DrawImage(Canvas c, float cx, float cy, float s, Color col)
    {
        float w = s * 0.9f, h = s * 0.78f;
        float l = cx - w / 2, r = cx + w / 2, t = cy - h / 2, b = cy + h / 2;
        c.SaveState(); Pen(c, col, 1.4f);
        c.BeginPath();
        c.MoveTo(l, t); c.LineTo(r, t); c.LineTo(r, b); c.LineTo(l, b); c.ClosePath();
        c.Stroke();
        c.BeginPath(); c.Arc(l + w * 0.28f, t + h * 0.3f, s * 0.08f, 0f, MathF.PI * 2f); c.Stroke();
        c.BeginPath();
        c.MoveTo(l, b); c.LineTo(l + w * 0.4f, cy); c.LineTo(l + w * 0.62f, b - h * 0.2f); c.LineTo(r, t + h * 0.5f);
        c.Stroke();
        c.RestoreState();
    }

    private static void DrawCode(Canvas c, float cx, float cy, float s, Color col)
    {
        float ex = s * 0.44f, ey = s * 0.28f;
        c.SaveState(); Pen(c, col, 1.5f);
        c.BeginPath();
        c.MoveTo(cx - ex * 0.28f, cy - ey); c.LineTo(cx - ex, cy); c.LineTo(cx - ex * 0.28f, cy + ey);
        c.MoveTo(cx + ex * 0.28f, cy - ey); c.LineTo(cx + ex, cy); c.LineTo(cx + ex * 0.28f, cy + ey);
        c.Stroke();
        c.RestoreState();
    }

    private static void DrawDrive(Canvas c, float cx, float cy, float s, Color col)
    {
        float w = s * 0.92f, h = s * 0.6f;
        float l = cx - w / 2, r = cx + w / 2, t = cy - h / 2, b = cy + h / 2;
        c.SaveState(); Pen(c, col, 1.4f);
        c.BeginPath();
        c.MoveTo(l, t); c.LineTo(r, t); c.LineTo(r, b); c.LineTo(l, b); c.ClosePath();
        c.Stroke();
        c.BeginPath(); c.Arc(r - w * 0.16f, cy, s * 0.05f, 0f, MathF.PI * 2f); c.Stroke();
        c.RestoreState();
    }

    private static void DrawHome(Canvas c, float cx, float cy, float s, Color col)
    {
        float w = s * 0.9f, h = s * 0.85f;
        float l = cx - w / 2, r = cx + w / 2, t = cy - h / 2, b = cy + h / 2;
        float eave = t + h * 0.42f;
        c.SaveState(); Pen(c, col, 1.5f);
        c.BeginPath();
        c.MoveTo(l, eave); c.LineTo(cx, t); c.LineTo(r, eave);
        c.MoveTo(l + w * 0.14f, eave); c.LineTo(l + w * 0.14f, b);
        c.LineTo(r - w * 0.14f, b); c.LineTo(r - w * 0.14f, eave);
        c.Stroke();
        c.RestoreState();
    }

    private static void DrawBack(Canvas c, float cx, float cy, float s, Color col)
    {
        float ex = s * 0.42f, ah = s * 0.24f;
        c.SaveState(); Pen(c, col, 1.6f);
        c.BeginPath();
        c.MoveTo(cx + ex, cy); c.LineTo(cx - ex, cy);
        c.MoveTo(cx - ex + ah, cy - ah); c.LineTo(cx - ex, cy); c.LineTo(cx - ex + ah, cy + ah);
        c.Stroke();
        c.RestoreState();
    }

    private static void DrawForward(Canvas c, float cx, float cy, float s, Color col)
    {
        float ex = s * 0.42f, ah = s * 0.24f;
        c.SaveState(); Pen(c, col, 1.6f);
        c.BeginPath();
        c.MoveTo(cx - ex, cy); c.LineTo(cx + ex, cy);
        c.MoveTo(cx + ex - ah, cy - ah); c.LineTo(cx + ex, cy); c.LineTo(cx + ex - ah, cy + ah);
        c.Stroke();
        c.RestoreState();
    }

    private static void DrawUp(Canvas c, float cx, float cy, float s, Color col)
    {
        float ey = s * 0.42f, ah = s * 0.24f;
        c.SaveState(); Pen(c, col, 1.6f);
        c.BeginPath();
        c.MoveTo(cx, cy + ey); c.LineTo(cx, cy - ey);
        c.MoveTo(cx - ah, cy - ey + ah); c.LineTo(cx, cy - ey); c.LineTo(cx + ah, cy - ey + ah);
        c.Stroke();
        c.RestoreState();
    }

    private static void DrawSearch(Canvas c, float cx, float cy, float s, Color col)
    {
        float rr = s * 0.3f;
        float ccx = cx - s * 0.1f, ccy = cy - s * 0.1f;
        c.SaveState(); Pen(c, col, 1.5f);
        c.BeginPath(); c.Arc(ccx, ccy, rr, 0f, MathF.PI * 2f); c.Stroke();
        c.BeginPath(); c.MoveTo(ccx + rr * 0.72f, ccy + rr * 0.72f); c.LineTo(cx + s * 0.42f, cy + s * 0.42f); c.Stroke();
        c.RestoreState();
    }

    private static void DrawPlus(Canvas c, float cx, float cy, float s, Color col)
    {
        float e = s * 0.34f;
        c.SaveState(); Pen(c, col, 1.6f);
        c.BeginPath();
        c.MoveTo(cx - e, cy); c.LineTo(cx + e, cy);
        c.MoveTo(cx, cy - e); c.LineTo(cx, cy + e);
        c.Stroke();
        c.RestoreState();
    }

    private static void DrawChevDown(Canvas c, float cx, float cy, float s, Color col)
    {
        float w = s * 0.36f, hh = s * 0.2f;
        c.SaveState(); Pen(c, col, 1.5f);
        c.BeginPath(); c.MoveTo(cx - w, cy - hh); c.LineTo(cx, cy + hh); c.LineTo(cx + w, cy - hh); c.Stroke();
        c.RestoreState();
    }

    private static void DrawChevUp(Canvas c, float cx, float cy, float s, Color col)
    {
        float w = s * 0.36f, hh = s * 0.2f;
        c.SaveState(); Pen(c, col, 1.5f);
        c.BeginPath(); c.MoveTo(cx - w, cy + hh); c.LineTo(cx, cy - hh); c.LineTo(cx + w, cy + hh); c.Stroke();
        c.RestoreState();
    }

    private static void DrawClose(Canvas c, float cx, float cy, float s, Color col)
    {
        float r = s * 0.28f;
        c.SaveState(); Pen(c, col, 1.5f);
        c.BeginPath(); c.MoveTo(cx - r, cy - r); c.LineTo(cx + r, cy + r);
        c.MoveTo(cx + r, cy - r); c.LineTo(cx - r, cy + r); c.Stroke();
        c.RestoreState();
    }

    private static void DrawCopy(Canvas c, float cx, float cy, float s, Color col)
    {
        float w = s * 0.5f, h = s * 0.58f, o = s * 0.16f;
        c.SaveState(); Pen(c, col, 1.4f);
        // back sheet (up-left), front sheet (down-right)
        c.BeginPath(); c.MoveTo(cx - w / 2 - o, cy - h / 2 - o); c.LineTo(cx + w / 2 - o, cy - h / 2 - o);
        c.LineTo(cx + w / 2 - o, cy + h / 2 - o); c.LineTo(cx - w / 2 - o, cy + h / 2 - o); c.ClosePath(); c.Stroke();
        c.BeginPath(); c.MoveTo(cx - w / 2 + o, cy - h / 2 + o); c.LineTo(cx + w / 2 + o, cy - h / 2 + o);
        c.LineTo(cx + w / 2 + o, cy + h / 2 + o); c.LineTo(cx - w / 2 + o, cy + h / 2 + o); c.ClosePath(); c.Stroke();
        c.RestoreState();
    }

    private static void DrawPencil(Canvas c, float cx, float cy, float s, Color col)
    {
        float e = s * 0.42f;
        c.SaveState(); Pen(c, col, 1.5f);
        // shaft
        c.BeginPath(); c.MoveTo(cx - e, cy + e); c.LineTo(cx + e * 0.62f, cy - e * 0.78f); c.Stroke();
        // tip
        c.BeginPath(); c.MoveTo(cx + e * 0.62f, cy - e * 0.78f); c.LineTo(cx + e, cy - e * 0.42f);
        c.LineTo(cx - e * 0.64f, cy + e); c.LineTo(cx - e, cy + e); c.LineTo(cx - e, cy + e * 0.64f); c.Stroke();
        c.RestoreState();
    }

    private static void DrawTrash(Canvas c, float cx, float cy, float s, Color col)
    {
        float w = s * 0.5f, h = s * 0.56f;
        float l = cx - w / 2, r = cx + w / 2, t = cy - h * 0.28f, b = cy + h / 2;
        c.SaveState(); Pen(c, col, 1.4f);
        // lid + handle
        c.BeginPath(); c.MoveTo(l - w * 0.12f, t); c.LineTo(r + w * 0.12f, t); c.Stroke();
        c.BeginPath(); c.MoveTo(cx - w * 0.18f, t); c.LineTo(cx - w * 0.14f, t - h * 0.16f);
        c.LineTo(cx + w * 0.14f, t - h * 0.16f); c.LineTo(cx + w * 0.18f, t); c.Stroke();
        // can
        c.BeginPath(); c.MoveTo(l, t); c.LineTo(l + w * 0.1f, b); c.LineTo(r - w * 0.1f, b); c.LineTo(r, t); c.Stroke();
        // ribs
        c.BeginPath(); c.MoveTo(cx, t + h * 0.14f); c.LineTo(cx, b - h * 0.1f); c.Stroke();
        c.RestoreState();
    }

    private static void DrawRefresh(Canvas c, float cx, float cy, float s, Color col)
    {
        float rr = s * 0.34f;
        c.SaveState(); Pen(c, col, 1.4f);
        c.BeginPath(); c.Arc(cx, cy, rr, -MathF.PI * 0.35f, MathF.PI * 1.15f); c.Stroke();
        // arrow head at the arc start (top-right)
        float ax = cx + rr * MathF.Cos(-MathF.PI * 0.35f), ay = cy + rr * MathF.Sin(-MathF.PI * 0.35f);
        c.BeginPath();
        c.MoveTo(ax - s * 0.16f, ay - s * 0.02f); c.LineTo(ax, ay); c.LineTo(ax + s * 0.02f, ay - s * 0.18f);
        c.Stroke();
        c.RestoreState();
    }

    private static List<FileEntry> LoadDir(string path, int sortColumn, bool ascending)
    {
        var list = new List<FileEntry>();
        try
        {
            var di = new DirectoryInfo(path);
            foreach (var dir in di.EnumerateDirectories())
            {
                if ((dir.Attributes & FileAttributes.Hidden) != 0) continue;
                list.Add(new FileEntry { Name = dir.Name, FullPath = dir.FullName, IsDirectory = true, LastModified = dir.LastWriteTime });
            }
            foreach (var file in di.EnumerateFiles())
            {
                if ((file.Attributes & FileAttributes.Hidden) != 0) continue;
                list.Add(new FileEntry { Name = file.Name, FullPath = file.FullName, IsDirectory = false, Size = file.Length, LastModified = file.LastWriteTime });
            }
        }
        catch { }

        var dirs = list.Where(e => e.IsDirectory);
        var files = list.Where(e => !e.IsDirectory);
        IEnumerable<FileEntry> Sort(IEnumerable<FileEntry> items) => sortColumn switch
        {
            1 => ascending ? items.OrderBy(e => e.Size) : items.OrderByDescending(e => e.Size),
            2 => ascending ? items.OrderBy(e => e.LastModified) : items.OrderByDescending(e => e.LastModified),
            _ => ascending ? items.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase) : items.OrderByDescending(e => e.Name, StringComparer.OrdinalIgnoreCase),
        };
        return Sort(dirs).Concat(Sort(files)).ToList();
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
    }

    private struct FileEntry
    {
        public string Name;
        public string FullPath;
        public bool IsDirectory;
        public long Size;
        public DateTime LastModified;
    }
}
