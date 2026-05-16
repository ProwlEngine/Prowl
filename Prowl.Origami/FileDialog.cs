// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

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
    private static bool _isOpen;
    private static FileDialogMode _mode;
    private static Action<string?>? _onComplete;
    private static FileDialogConfig? _config;
    private static string[] _typeFilters = ["*.*"];
    private static string[] _typeFilterLabels = ["All Files (*.*)"];

    private static string _currentPath = "";
    private static string _selectedPath = "";
    private static string _fileName = "";
    private static string _searchFilter = "";
    private static int _activeFilterIndex;
    private static int _sortColumn;
    private static bool _sortAscending = true;
    private static bool _creatingFolder;
    private static string _newFolderName = "New Folder";
    private static string _renamingPath = "";
    private static string _renameBuf = "";

    private static List<string> _history = [];
    private static int _historyIndex = -1;
    private static List<FileEntry> _entries = [];
    private static IModal? _modalHandle;

    // Drag state
    private static string _dragPath = "";
    private static string _dragName = "";
    private static bool _isDragging;
    private static string _dropTargetPath = "";
    private static string _hoverDirPath = "";
    private static float _hoverDirTime;

    public static bool IsOpen => _isOpen;

    // ── API ──────────────────────────────────────────────────

    public static void Open(FileDialogMode mode, Action<string?> onComplete,
        string? startPath = null, string[]? filters = null, string[]? filterLabels = null,
        FileDialogConfig? config = null)
    {
        _isOpen = true;
        _mode = mode;
        _onComplete = onComplete;
        _config = config;
        _selectedPath = "";
        _fileName = "";
        _searchFilter = "";
        _sortColumn = 0;
        _sortAscending = true;
        _creatingFolder = false;
        _renamingPath = "";
        _activeFilterIndex = 0;

        if (filters != null && filterLabels != null && filters.Length == filterLabels.Length)
        {
            _typeFilters = filters;
            _typeFilterLabels = filterLabels;
        }
        else
        {
            _typeFilters = ["*.*"];
            _typeFilterLabels = ["All Files (*.*)"];
        }

        string path = startPath ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        if (!Directory.Exists(path)) path = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        _history = [path];
        _historyIndex = 0;
        NavigateTo(path, addHistory: false);

        // Push onto the modal stack so backdrops and layering are managed centrally
        _modalHandle = new CustomDrawModal((paper, layer, stackIndex) => DrawInternal(paper, layer)) { CloseOnEscape = false };
        Modal.Push(_modalHandle);
    }

    public static void Close(string? result = null)
    {
        _isOpen = false;
        _isDragging = false; _dragPath = ""; _dragName = ""; _dropTargetPath = "";
        _hoverDirPath = ""; _hoverDirTime = 0;
        if (_modalHandle != null) { Modal.Remove(_modalHandle); _modalHandle = null; }
        if (result != null) _config?.OnFileOpened?.Invoke(result);
        _onComplete?.Invoke(result);
        _onComplete = null;
    }

    // ── Navigation ───────────────────────────────────────────

    private static void NavigateTo(string path, bool addHistory = true)
    {
        if (!Directory.Exists(path)) return;
        _currentPath = Path.GetFullPath(path);
        _selectedPath = "";
        _renamingPath = "";

        if (addHistory)
        {
            if (_historyIndex < _history.Count - 1)
                _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
            _history.Add(_currentPath);
            _historyIndex = _history.Count - 1;
        }

        RefreshEntries();
    }

    private static void NavigateBack()
    {
        if (_historyIndex > 0) { _historyIndex--; _currentPath = _history[_historyIndex]; RefreshEntries(); }
    }

    private static void NavigateForward()
    {
        if (_historyIndex < _history.Count - 1) { _historyIndex++; _currentPath = _history[_historyIndex]; RefreshEntries(); }
    }

    private static void NavigateUp()
    {
        var parent = Directory.GetParent(_currentPath);
        if (parent != null) NavigateTo(parent.FullName);
    }

    private static void RefreshEntries()
    {
        _entries.Clear();
        try
        {
            var dirInfo = new DirectoryInfo(_currentPath);
            foreach (var dir in dirInfo.EnumerateDirectories())
            {
                if ((dir.Attributes & FileAttributes.Hidden) != 0) continue;
                _entries.Add(new FileEntry { Name = dir.Name, FullPath = dir.FullName, IsDirectory = true, LastModified = dir.LastWriteTime });
            }

            if (_mode != FileDialogMode.SelectFolder)
            {
                var patterns = _typeFilters[_activeFilterIndex].Split(';').Select(f => f.Trim()).ToArray();
                foreach (var file in dirInfo.EnumerateFiles())
                {
                    if ((file.Attributes & FileAttributes.Hidden) != 0) continue;
                    if (!patterns.Any(p => p == "*.*" || MatchesPattern(file.Name, p))) continue;
                    _entries.Add(new FileEntry { Name = file.Name, FullPath = file.FullName, IsDirectory = false, Size = file.Length, LastModified = file.LastWriteTime });
                }
            }

            ApplySort();
        }
        catch { }
    }

    private static void ApplySort()
    {
        var dirs = _entries.Where(e => e.IsDirectory);
        var files = _entries.Where(e => !e.IsDirectory);
        IEnumerable<FileEntry> Sort(IEnumerable<FileEntry> items) => _sortColumn switch
        {
            1 => _sortAscending ? items.OrderBy(e => e.Size) : items.OrderByDescending(e => e.Size),
            2 => _sortAscending ? items.OrderBy(e => e.LastModified) : items.OrderByDescending(e => e.LastModified),
            _ => _sortAscending ? items.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase) : items.OrderByDescending(e => e.Name, StringComparer.OrdinalIgnoreCase),
        };
        _entries = Sort(dirs).Concat(Sort(files)).ToList();
    }

    private static void ConfirmSelection()
    {
        string result;
        if (_mode == FileDialogMode.SelectFolder)
            result = !string.IsNullOrEmpty(_selectedPath) && Directory.Exists(_selectedPath) ? _selectedPath : _currentPath;
        else if (_mode == FileDialogMode.Save)
            result = !string.IsNullOrEmpty(_fileName) ? Path.Combine(_currentPath, _fileName) : "";
        else
        {
            if (!string.IsNullOrEmpty(_selectedPath) && File.Exists(_selectedPath)) result = _selectedPath;
            else if (!string.IsNullOrEmpty(_fileName)) result = Path.Combine(_currentPath, _fileName);
            else return;
        }
        if (!string.IsNullOrEmpty(result)) Close(result);
    }

    // ── Draw ─────────────────────────────────────────────────

    private const float DialogW = 800f, DialogH = 550f;
    private const float SidebarW = 170f, ToolbarH = 34f, BottomH = 72f;

    private static void DrawInternal(Paper paper, int layer)
    {
        if (!_isOpen) return;
        var theme = Origami.Current;
        var font = theme.Font;
        var ink = theme.Ink;
        var icons = theme.Icons;
        if (font == null) return;

        var m = theme.Metrics;

        var displayEntries = string.IsNullOrEmpty(_searchFilter) ? _entries
            : _entries.Where(e => e.Name.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        // Drag tooltip (rendered OUTSIDE the dialog window so it's not clipped)
        if (_isDragging && !string.IsNullOrEmpty(_dragName))
        {
            float mx = (float)paper.PointerPos.X + 14;
            float my = (float)paper.PointerPos.Y + 4;
            paper.Box("ofd_drag_tip")
                .PositionType(PositionType.SelfDirected)
                .Position(mx, my)
                .Width(UnitValue.Auto).Height(m.CompactHeight)
                .BackgroundColor(Color.FromArgb(200, 40, 40, 45))
                .BorderColor(theme.Primary.C400).BorderWidth(1)
                .Rounded(m.Rounding).ChildLeft(m.Padding).ChildRight(m.Padding)
                .IsNotInteractable()
                .Layer(layer + 1)
                .Text($"{icons.File}  {_dragName}", font)
                .TextColor(ink.C500)
                .FontSize(m.FontSizeSmall)
                .Alignment(TextAlignment.MiddleLeft);
        }

        // Window (backdrop is managed by the modal stack)
        using (paper.Column("ofd_win")
            .Size(DialogW, DialogH)
            .Margin(UnitValue.StretchOne)
            .BackgroundColor(theme.Neutral.C300)
            .BorderColor(ink.C200).BorderWidth(1).Rounded(m.ContainerRounding)
            .Layer(layer)
            .StopEventPropagation()
            .Enter())
        {
            DrawToolbar(paper, font, icons, ink, theme);
            using (paper.Row("ofd_body").Enter())
            {
                DrawSidebar(paper, font, icons, ink, theme);
                using (paper.Column("ofd_list_area").Enter())
                {
                    DrawColumnHeaders(paper, font, ink);
                    DrawFileList(paper, font, icons, ink, theme, displayEntries);
                }
            }
            DrawBottomBar(paper, font, ink, theme);
        }

        if (paper.IsKeyPressed(PaperKey.Escape)) Close(null);
    }

    // ── Toolbar ──────────────────────────────────────────────

    private static void DrawToolbar(Paper paper, Scribe.FontFile font, OrigamiIcons icons, OrigamiRamp ink, OrigamiTheme theme)
    {
        var m = theme.Metrics;
        using (paper.Row("ofd_toolbar").Height(ToolbarH)
            .BackgroundColor(theme.Neutral.C200)
            .ChildLeft(m.Padding).ChildRight(m.Padding).RowBetween(m.Spacing).Enter())
        {
            TbBtn(paper, "ofd_back", icons.ArrowLeft, font, ink, _historyIndex > 0, NavigateBack);
            TbBtn(paper, "ofd_fwd", icons.ArrowRight, font, ink, _historyIndex < _history.Count - 1, NavigateForward);
            TbBtn(paper, "ofd_up", icons.ArrowUp, font, ink, true, NavigateUp);

            Origami.TextField(paper, "ofd_path", _currentPath, v => { if (Directory.Exists(v)) NavigateTo(v); })
                .Width(UnitValue.Stretch()).Show();

            Origami.TextField(paper, "ofd_search", _searchFilter, v => _searchFilter = v)
                .Placeholder("Search...").Width(140).Show();

            TbBtn(paper, "ofd_newf", icons.FolderPlus, font, ink, true, () =>
            {
                _creatingFolder = !_creatingFolder;
                _newFolderName = "New Folder";
            });
        }
    }

    private static void TbBtn(Paper paper, string id, string icon, Scribe.FontFile font, OrigamiRamp ink, bool enabled, Action onClick)
    {
        var m = Origami.Current.Metrics;
        var box = paper.Box(id).Width(28).Height(28)
            .Text(icon, font).TextColor(enabled ? ink.C500 : ink.C300).FontSize(14f)
            .Alignment(TextAlignment.MiddleCenter).Rounded(m.Rounding);
        if (enabled)
            box.Hovered.BackgroundColor(ink.C200).End().OnClick(0, (_, _) => onClick());
    }

    // ── Sidebar ──────────────────────────────────────────────

    private static void DrawSidebar(Paper paper, Scribe.FontFile font, OrigamiIcons icons, OrigamiRamp ink, OrigamiTheme theme)
    {
        var m = theme.Metrics;
        using (paper.Column("ofd_side").Width(SidebarW)
            .BackgroundColor(theme.Neutral.C200)
            .Padding(m.Spacing, m.Spacing, m.SpacingLarge, 0).ColBetween(m.SpacingSmall).Enter())
        {
            if (_config?.QuickAccess.Count > 0)
            {
                SLabel(paper, "qa", "Quick Access", font, ink);
                foreach (var (label, icon, path) in _config.QuickAccess)
                    if (Directory.Exists(path))
                        SItem(paper, $"ofd_qa_{label}", $"{icon}  {label}", font, ink, theme, path, () => NavigateTo(path));
                SSep(paper, "qa", ink);
            }

            if (_config != null && (_config.Favorites.Count > 0 || _config.OnAddFavorite != null))
            {
                using (paper.Row("ofd_fav_h").Height(m.CompactHeight).RowBetween(m.Spacing).Enter())
                {
                    paper.Box("ofd_fav_l").Height(m.CompactHeight).ChildLeft(m.SpacingLarge)
                        .Text("Favorites", font).TextColor(ink.C400)
                        .FontSize(m.FontSizeSmall - 1).Alignment(TextAlignment.MiddleLeft);
                    if (_config.OnAddFavorite != null)
                        paper.Box("ofd_fav_a").Width(m.IconBoxWidth).Height(m.CompactHeight).Rounded(m.SmallRounding)
                            .Text(icons.Plus, font).TextColor(ink.C400).FontSize(m.FontSizeSmall)
                            .Alignment(TextAlignment.MiddleCenter)
                            .Hovered.BackgroundColor(ink.C200).End()
                            .OnClick(0, (_, _) => _config.OnAddFavorite(_currentPath));
                }
                for (int i = 0; i < _config.Favorites.Count; i++)
                {
                    int idx = i;
                    var (label, path) = _config.Favorites[i];
                    SItem(paper, $"ofd_fav_{i}", $"{icons.Star}  {label}", font, ink, theme, path,
                        () => NavigateTo(path), () => _config.OnRemoveFavorite?.Invoke(idx));
                }
                SSep(paper, "fav", ink);
            }

            if (_config?.RecentFiles.Count > 0)
            {
                SLabel(paper, "rec", "Recent", font, ink);
                int shown = 0;
                foreach (var recent in _config.RecentFiles)
                {
                    if (shown >= 8) break;
                    string dir = Path.GetDirectoryName(recent) ?? "";
                    if (!Directory.Exists(dir)) continue;
                    SItem(paper, $"ofd_rec_{shown}", $"{icons.Clock}  {Path.GetFileName(recent)}", font, ink, theme, dir, () => NavigateTo(dir));
                    shown++;
                }
                SSep(paper, "rec", ink);
            }

            SLabel(paper, "drv", "Drives", font, ink);
            try
            {
                var drives = _config?.GetDrives?.Invoke();
                if (drives != null)
                    foreach (var (label, path) in drives)
                        SItem(paper, $"ofd_drv_{label}", $"{icons.Drive}  {label}", font, ink, theme, path, () => NavigateTo(path));
                else
                    foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady))
                        SItem(paper, $"ofd_drv_{d.Name}", $"{icons.Drive}  {d.Name}", font, ink, theme, d.Name, () => NavigateTo(d.Name));
            }
            catch { }
        }
    }

    private static void SLabel(Paper paper, string key, string text, Scribe.FontFile font, OrigamiRamp ink)
    {
        var m = Origami.Current.Metrics;
        paper.Box($"ofd_sl_{key}").Height(m.CompactHeight).ChildLeft(m.SpacingLarge)
            .Text(text, font).TextColor(ink.C400)
            .FontSize(m.FontSizeSmall - 1).Alignment(TextAlignment.MiddleLeft);
    }

    private static void SSep(Paper paper, string key, OrigamiRamp ink)
    {
        var m = Origami.Current.Metrics;
        paper.Box($"ofd_ss_{key}").Height(1).Margin(m.Spacing, m.SpacingLarge, m.Spacing, m.SpacingLarge).BackgroundColor(ink.C200);
    }

    private static void SItem(Paper paper, string id, string text, Scribe.FontFile font, OrigamiRamp ink, OrigamiTheme theme,
        string targetPath, Action onClick, Action? onRightClick = null)
    {
        var m = theme.Metrics;
        bool isCurrent = _currentPath.Equals(targetPath, StringComparison.OrdinalIgnoreCase);
        var box = paper.Box(id).Height(m.RowHeight)
            .BackgroundColor(isCurrent ? theme.Primary.C400 : Color.Transparent)
            .Hovered.BackgroundColor(isCurrent ? theme.Primary.C400 : ink.C200).End()
            .Rounded(m.Rounding).ChildLeft(m.SpacingLarge)
            .Text(text, font).TextColor(ink.C500)
            .FontSize(m.FontSizeSmall).Alignment(TextAlignment.MiddleLeft)
            .OnClick(0, (_, _) => onClick());
        if (onRightClick != null)
            box.OnRightClick(0, (_, _) => onRightClick());
    }

    // ── Column Headers ───────────────────────────────────────

    private static void DrawColumnHeaders(Paper paper, Scribe.FontFile font, OrigamiRamp ink)
    {
        var theme = Origami.Current;
        var icons = theme.Icons;
        var m = theme.Metrics;

        using (paper.Row("ofd_colh").Height(m.HeaderHeight).BackgroundColor(theme.Neutral.C200).ChildLeft(m.SpacingLarge).RowBetween(0).Enter())
        {
            // Icon placeholder to match file row icon width
            paper.Box("ofd_ch_ico").Width(m.IconBoxWidth).Height(m.HeaderHeight);

            void Col(string label, int col, float? w = null)
            {
                string arrow = _sortColumn == col
                    ? (_sortAscending ? $" {icons.ChevronUp}" : $" {icons.ChevronDown}")
                    : "";

                var el = paper.Box($"ofd_ch_{col}").Height(m.HeaderHeight);
                if (w.HasValue) el.Width(w.Value);
                else el.Width(UnitValue.Stretch());

                el.ChildLeft(m.Spacing).Text(label + arrow, font).TextColor(ink.C400)
                    .FontSize(m.FontSizeSmall).Alignment(TextAlignment.MiddleLeft)
                    .Hovered.BackgroundColor(ink.C200).End()
                    .OnClick(col, (c, _) =>
                    {
                        if (_sortColumn == c) _sortAscending = !_sortAscending;
                        else { _sortColumn = c; _sortAscending = true; }
                        ApplySort();
                    });
            }
            Col("Name", 0);
            Col("Size", 1, 80f);
            Col("Modified", 2, 140f);
        }
    }

    // ── File List ────────────────────────────────────────────

    private static float _scrollAreaTop, _scrollAreaBottom;

    private static void DrawFileList(Paper paper, Scribe.FontFile font, OrigamiIcons icons, OrigamiRamp ink, OrigamiTheme theme, List<FileEntry> display)
    {
        var m = theme.Metrics;
        float listW = DialogW - SidebarW;
        float listH = DialogH - ToolbarH - m.HeaderHeight - BottomH;

        // Reset drop target each frame - will be set by hovered dir rows below.
        _dropTargetPath = "";
        if (!_isDragging) { _hoverDirPath = ""; _hoverDirTime = 0; }

        // Global drag end detection: if mouse released while dragging, complete/cancel
        // the drag. This handles the case where hover-to-open destroyed the drag source
        // element so OnDragEnd never fires.
        if (_isDragging && !paper.IsPointerDown(PaperMouseBtn.Left))
        {
            // If no specific directory is hovered, drop into the current folder
            string target = !string.IsNullOrEmpty(_dropTargetPath) ? _dropTargetPath : _currentPath;
            if (!string.IsNullOrEmpty(_dragPath) && target != Path.GetDirectoryName(_dragPath))
                TryMoveEntry(_dragPath, target);
            _isDragging = false; _dragPath = ""; _dragName = ""; _dropTargetPath = "";
            _hoverDirPath = ""; _hoverDirTime = 0;
        }

        // Compute scroll area bounds from the dialog position for auto-scroll
        float screenH = (float)paper.ScreenRect.Size.Y;
        float dialogTop = (screenH - DialogH) / 2f; // centered dialog
        _scrollAreaTop = dialogTop + ToolbarH + m.HeaderHeight;
        _scrollAreaBottom = dialogTop + DialogH - BottomH;

        Origami.ScrollView(paper, "ofd_scroll", listW, listH).Body(() =>
        {
            // ".." parent directory entry
            var parentDir = Directory.GetParent(_currentPath);
            if (parentDir != null)
            {
                bool isParentDropTarget = _isDragging;
                var dotdotBg = Color.Transparent;
                var dotdotHover = isParentDropTarget ? theme.Green.C400 : ink.C200;

                using (paper.Row("ofd_dotdot").Height(m.RowHeight)
                    .BackgroundColor(dotdotBg)
                    .Hovered.BackgroundColor(dotdotHover).End()
                    .ChildLeft(m.SpacingLarge).RowBetween(0)
                    .OnDoubleClick(0, (_, _) => NavigateUp())
                    .Enter())
                {
                    // Track as drop target + hover-to-open during drag
                    if (_isDragging && paper.IsParentHovered)
                    {
                        _dropTargetPath = parentDir.FullName;

                        if (_hoverDirPath == parentDir.FullName)
                        {
                            if (paper.Time - _hoverDirTime > 2.0f)
                            {
                                NavigateUp();
                                _hoverDirPath = "";
                                _hoverDirTime = 0;
                            }
                        }
                        else
                        {
                            _hoverDirPath = parentDir.FullName;
                            _hoverDirTime = (float)paper.Time;
                        }
                    }

                    paper.Box("ofd_dotdot_ico").Width(m.IconBoxWidth).Height(m.RowHeight)
                        .Text(icons.Folder, font).TextColor(Color.FromArgb(255, 220, 180, 80))
                        .FontSize(m.FontSizeSmall).Alignment(TextAlignment.MiddleCenter);
                    paper.Box("ofd_dotdot_name").Width(UnitValue.Stretch()).Height(m.RowHeight).ChildLeft(m.Spacing)
                        .Text("..", font).TextColor(ink.C500)
                        .FontSize(m.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
                }
            }

            if (_creatingFolder)
            {
                using (paper.Row("ofd_nf").Height(m.RowHeight).BackgroundColor(theme.Primary.C400).ChildLeft(m.SpacingLarge).RowBetween(m.SpacingLarge).Enter())
                {
                    paper.Box("ofd_nf_i").Width(m.IconBoxWidth).Height(m.RowHeight).Text(icons.Folder, font).TextColor(ink.C500)
                        .FontSize(m.FontSizeSmall).Alignment(TextAlignment.MiddleCenter);
                    Origami.TextField(paper, "ofd_nf_n", _newFolderName, v => _newFolderName = v)
                        .Placeholder("Name").Width(UnitValue.Stretch()).Show();
                    Origami.Button(paper, "ofd_nf_ok", "Create", () =>
                    {
                        try { Directory.CreateDirectory(Path.Combine(_currentPath, _newFolderName)); }
                        catch { }
                        _creatingFolder = false;
                        RefreshEntries();
                    }).Width(60).Show();
                    Origami.Button(paper, "ofd_nf_x", "Cancel", () => _creatingFolder = false).Width(60).Show();
                }
            }

            for (int i = 0; i < display.Count; i++)
            {
                var entry = display[i];
                bool isSel = entry.FullPath == _selectedPath;
                bool isRen = entry.FullPath == _renamingPath;
                int idx = i;

                // Highlight directory rows as drop targets during drag
                bool isDropTarget = _isDragging && entry.IsDirectory && entry.FullPath != _dragPath;
                var rowBg = isSel ? theme.Primary.C400 : (i % 2 == 0 ? Color.Transparent : Color.FromArgb(15, 255, 255, 255));
                var rowHoverBg = isDropTarget ? theme.Green.C400 : (isSel ? theme.Primary.C400 : ink.C200);

                var row = paper.Row($"ofd_r_{i}").Height(m.RowHeight)
                    .BackgroundColor(rowBg)
                    .Hovered.BackgroundColor(rowHoverBg).End()
                    .ChildLeft(m.SpacingLarge).RowBetween(0);

                row.OnClick(entry, (e, _) => { _selectedPath = e.FullPath; if (!e.IsDirectory) _fileName = e.Name; });
                row.OnDoubleClick(entry, (e, _) => { if (e.IsDirectory) NavigateTo(e.FullPath); else ConfirmSelection(); });
                row.OnDragStart(entry, (e, _) => { _isDragging = true; _dragPath = e.FullPath; _dragName = e.Name; _dropTargetPath = ""; });
                row.OnDragEnd(entry, (e, _) =>
                {
                    if (_isDragging && !string.IsNullOrEmpty(_dropTargetPath) && _dropTargetPath != _dragPath)
                        TryMoveEntry(_dragPath, _dropTargetPath);
                    // No target = drop on background = cancel (file stays in place)
                    _isDragging = false; _dragPath = ""; _dragName = ""; _dropTargetPath = "";
                    _hoverDirPath = ""; _hoverDirTime = 0;
                });

                using (row.Enter())
                {
                    // Track hover for drag-drop targeting + hover-to-navigate
                    if (_isDragging && entry.IsDirectory && paper.IsParentHovered)
                    {
                        _dropTargetPath = entry.FullPath;

                        // Hover-to-open: if hovering the same directory for 2 seconds, navigate into it
                        if (_hoverDirPath == entry.FullPath)
                        {
                            if (paper.Time - _hoverDirTime > 2.0f)
                            {
                                NavigateTo(entry.FullPath);
                                _hoverDirPath = "";
                                _hoverDirTime = 0;
                            }
                        }
                        else
                        {
                            _hoverDirPath = entry.FullPath;
                            _hoverDirTime = (float)paper.Time;
                        }
                    }

                    // Right-click context menu
                    var ctxEntry = entry;
                    ContextMenu.RightClickMenu(paper, $"ofd_ctx_{i}", b =>
                    {
                        b.Item("Rename", () =>
                        {
                            _renamingPath = ctxEntry.FullPath;
                            _renameBuf = ctxEntry.Name;
                        }, icon: icons.Pencil);

                        b.Item("Delete", () =>
                        {
                            string name = ctxEntry.Name;
                            string path = ctxEntry.FullPath;
                            bool isDir = ctxEntry.IsDirectory;
                            Modal.Confirm("Delete", $"Are you sure you want to delete '{name}'?\nThis cannot be undone.", () =>
                            {
                                try { if (isDir) Directory.Delete(path, true); else File.Delete(path); }
                                catch { }
                                _renamingPath = "";
                                RefreshEntries();
                            });
                        }, icon: icons.Trash);

                        if (ctxEntry.IsDirectory && _config?.OnAddFavorite != null)
                        {
                            b.Separator();
                            b.Item("Add to Favorites", () => _config.OnAddFavorite(ctxEntry.FullPath), icon: icons.Star);
                        }
                    });

                    string icon = _config?.GetIcon != null ? _config.GetIcon(Path.GetExtension(entry.Name), entry.IsDirectory) : (entry.IsDirectory ? icons.Folder : icons.File);
                    var iconCol = entry.IsDirectory ? Color.FromArgb(255, 220, 180, 80) : ink.C400;
                    paper.Box($"ofd_i_{i}").Width(m.IconBoxWidth).Height(m.RowHeight).Text(icon, font).TextColor(iconCol)
                        .FontSize(m.FontSizeSmall).Alignment(TextAlignment.MiddleCenter);

                    if (isRen)
                    {
                        using (paper.Row($"ofd_ren_{i}").Width(UnitValue.Stretch()).Height(m.RowHeight).RowBetween(m.Spacing).Enter())
                        {
                            Origami.TextField(paper, $"ofd_ren_t_{i}", _renameBuf, v => _renameBuf = v)
                                .Width(UnitValue.Stretch()).Show();
                            Origami.Button(paper, $"ofd_ren_ok_{i}", icons.Check, () =>
                            {
                                try
                                {
                                    if (!string.IsNullOrWhiteSpace(_renameBuf) && _renameBuf != entry.Name)
                                    {
                                        string np = Path.Combine(Path.GetDirectoryName(entry.FullPath)!, _renameBuf);
                                        if (entry.IsDirectory) Directory.Move(entry.FullPath, np);
                                        else File.Move(entry.FullPath, np);
                                    }
                                }
                                catch { }
                                _renamingPath = "";
                                RefreshEntries();
                            }).Width(28).Show();
                            Origami.Button(paper, $"ofd_ren_x_{i}", icons.Close, () => _renamingPath = "").Width(28).Show();
                        }
                    }
                    else
                    {
                        paper.Box($"ofd_n_{i}").Width(UnitValue.Stretch()).Height(m.RowHeight).ChildLeft(m.Spacing)
                            .Text(entry.Name, font).TextColor(ink.C500)
                            .FontSize(m.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
                    }

                    paper.Box($"ofd_s_{i}").Width(80).Height(m.RowHeight)
                        .Text(entry.IsDirectory ? "" : FormatSize(entry.Size), font).TextColor(ink.C400)
                        .FontSize(m.FontSizeSmall - 1).Alignment(TextAlignment.MiddleRight).ChildRight(m.SpacingLarge);

                    paper.Box($"ofd_d_{i}").Width(140).Height(m.RowHeight)
                        .Text(entry.LastModified.ToString("yyyy-MM-dd  HH:mm"), font).TextColor(ink.C400)
                        .FontSize(m.FontSizeSmall - 1).Alignment(TextAlignment.MiddleRight).ChildRight(m.SpacingLarge);
                }
            }

            if (display.Count == 0)
                paper.Box("ofd_empty").Height(60).Text("This folder is empty", font).TextColor(ink.C300)
                    .FontSize(m.FontSize).Alignment(TextAlignment.MiddleCenter);
        });

        // If no directory was hovered this frame, reset hover-to-open timer
        if (_isDragging && string.IsNullOrEmpty(_dropTargetPath))
        {
            _hoverDirPath = "";
            _hoverDirTime = 0;
        }

        // Auto-scroll during drag when pointer is near the top/bottom edges
        if (_isDragging && _scrollAreaTop > 0 && _scrollAreaBottom > _scrollAreaTop)
        {
            float my = (float)paper.PointerPos.Y;
            float edgeZone = 40f;
            float scrollSpeed = 300f * paper.DeltaTime;

            if (my > _scrollAreaTop && my < _scrollAreaTop + edgeZone)
            {
                float factor = 1f - (my - _scrollAreaTop) / edgeZone;
                ScrollViewBuilder.s_pendingScrollBy["ofd_scroll"] = new Vector.Float2(0, -scrollSpeed * factor);
            }
            else if (my < _scrollAreaBottom && my > _scrollAreaBottom - edgeZone)
            {
                float factor = 1f - (_scrollAreaBottom - my) / edgeZone;
                ScrollViewBuilder.s_pendingScrollBy["ofd_scroll"] = new Vector.Float2(0, scrollSpeed * factor);
            }
        }
    }

    // ── Bottom Bar ───────────────────────────────────────────

    private static void DrawBottomBar(Paper paper, Scribe.FontFile font, OrigamiRamp ink, OrigamiTheme theme)
    {
        var m = theme.Metrics;
        using (paper.Column("ofd_bot").Height(BottomH)
            .BackgroundColor(theme.Neutral.C200)
            .Padding(m.SpacingLarge, m.SpacingLarge, m.Padding, m.Padding).ColBetween(m.SpacingMedium).Enter())
        {
            using (paper.Row("ofd_nr").Height(m.RowHeight).RowBetween(m.SpacingLarge).Enter())
            {
                paper.Box("ofd_nl").Width(70).Height(m.RowHeight)
                    .Text(_mode == FileDialogMode.SelectFolder ? "Folder:" : "File name:", font)
                    .TextColor(ink.C400).FontSize(m.FontSizeSmall).Alignment(TextAlignment.MiddleRight);

                Origami.TextField(paper, "ofd_fn", _fileName, v => _fileName = v)
                    .Width(UnitValue.Stretch()).Show();

                if (_mode != FileDialogMode.SelectFolder && _typeFilterLabels.Length > 1)
                    Origami.Dropdown(paper, "ofd_filt", _activeFilterIndex,
                        v => { _activeFilterIndex = v; RefreshEntries(); }, _typeFilterLabels).Show();
            }

            using (paper.Row("ofd_br").Height(m.RowHeight + 2).RowBetween(m.SpacingLarge).Enter())
            {
                paper.Box("ofd_bsp");
                string label = _mode switch { FileDialogMode.Save => "Save", FileDialogMode.SelectFolder => "Select Folder", _ => "Open" };
                Origami.Button(paper, "ofd_ok", label, ConfirmSelection).Primary().Width(101).Show();
                Origami.Button(paper, "ofd_cancel", "Cancel", () => Close(null)).Width(60).Show();
            }
        }
    }

    // ── Drag & Drop ─────────────────────────────────────────────

    private static void TryMoveEntry(string sourcePath, string targetDirPath)
    {
        if (!Directory.Exists(targetDirPath)) return;
        string name = Path.GetFileName(sourcePath);
        string destPath = Path.Combine(targetDirPath, name);
        if (sourcePath == destPath) return;

        // Don't move a directory into itself
        if (Directory.Exists(sourcePath) && destPath.StartsWith(sourcePath + Path.DirectorySeparatorChar))
            return;

        try
        {
            if (File.Exists(destPath) || Directory.Exists(destPath))
            {
                Modal.Confirm("Overwrite?", $"'{name}' already exists in the destination. Overwrite?", () =>
                {
                    try
                    {
                        if (Directory.Exists(sourcePath))
                        {
                            if (Directory.Exists(destPath)) Directory.Delete(destPath, true);
                            Directory.Move(sourcePath, destPath);
                        }
                        else
                        {
                            File.Move(sourcePath, destPath, true);
                        }
                        RefreshEntries();
                    }
                    catch { }
                });
                return;
            }

            if (Directory.Exists(sourcePath))
                Directory.Move(sourcePath, destPath);
            else
                File.Move(sourcePath, destPath);
            RefreshEntries();
        }
        catch { }
    }

    // ── Helpers ───────────────────────────────────────────────

    private static bool MatchesPattern(string fileName, string pattern)
    {
        if (pattern == "*.*") return true;
        string ext = pattern.StartsWith("*") ? pattern[1..] : pattern;
        return fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase);
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
