using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using Color = System.Drawing.Color;

namespace Prowl.Editor.Widgets;

public enum FileDialogMode { Open, Save, SelectFolder }

public class FileDialogEntry
{
    public string Name;
    public string FullPath;
    public bool IsDirectory;
    public long Size;
    public DateTime LastModified;
}

public static class FileDialog
{
    private static bool _isOpen;
    private static FileDialogMode _mode;
    private static string _currentPath = "";
    private static string _fileName = "";
    private static string _searchFilter = "";
    private static string _selectedPath = "";
    private static int _selectedIndex = -1;
    private static string[] _typeFilters = { "*.*" };
    private static string[] _typeFilterLabels = { "All Files (*.*)" };
    private static int _activeFilterIndex = 0;
    private static Action<string?>? _onComplete;
    private static List<FileDialogEntry> _entries = new();
    private static List<string> _pathHistory = new();
    private static int _historyIndex = -1;
    private static int _sortColumn; // 0=name, 1=size, 2=date
    private static bool _sortAscending = true;
    private static string _newFolderName = "";
    private static bool _creatingFolder;
    private static bool _showHidden = false;

    // Quick access locations
    private static readonly (string label, string icon, Func<string> path)[] QuickAccess =
    {
        ("Desktop", EditorIcons.Desktop, () => Environment.GetFolderPath(Environment.SpecialFolder.Desktop)),
        ("Documents", EditorIcons.FolderOpen, () => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
        ("Downloads", EditorIcons.Download, () => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")),
        ("User", EditorIcons.User, () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
    };

    public static bool IsOpen => _isOpen;

    public static void Open(FileDialogMode mode, Action<string?> onComplete,
        string? startPath = null, string[]? filters = null, string[]? filterLabels = null)
    {
        _isOpen = true;
        _mode = mode;
        _onComplete = onComplete;
        _selectedIndex = -1;
        _selectedPath = "";
        _fileName = "";
        _searchFilter = "";
        _creatingFolder = false;
        _sortColumn = 0;
        _sortAscending = true;

        if (filters != null && filterLabels != null && filters.Length == filterLabels.Length)
        {
            _typeFilters = filters;
            _typeFilterLabels = filterLabels;
            _activeFilterIndex = 0;
        }
        else
        {
            _typeFilters = new[] { "*.*" };
            _typeFilterLabels = new[] { "All Files (*.*)" };
            _activeFilterIndex = 0;
        }

        string path = startPath ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        NavigateTo(path);
    }

    public static void Close(string? result = null)
    {
        _isOpen = false;
        _onComplete?.Invoke(result);
        _onComplete = null;
    }

    private static void NavigateTo(string path)
    {
        if (!Directory.Exists(path)) return;
        _currentPath = Path.GetFullPath(path);
        _selectedIndex = -1;
        _selectedPath = "";

        // Manage history
        if (_historyIndex < _pathHistory.Count - 1)
            _pathHistory.RemoveRange(_historyIndex + 1, _pathHistory.Count - _historyIndex - 1);
        _pathHistory.Add(_currentPath);
        _historyIndex = _pathHistory.Count - 1;

        RefreshEntries();
    }

    private static void NavigateBack()
    {
        if (_historyIndex > 0)
        {
            _historyIndex--;
            _currentPath = _pathHistory[_historyIndex];
            _selectedIndex = -1;
            RefreshEntries();
        }
    }

    private static void NavigateForward()
    {
        if (_historyIndex < _pathHistory.Count - 1)
        {
            _historyIndex++;
            _currentPath = _pathHistory[_historyIndex];
            _selectedIndex = -1;
            RefreshEntries();
        }
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

            // Directories first
            foreach (var dir in dirInfo.EnumerateDirectories())
            {
                if (!_showHidden && (dir.Attributes & FileAttributes.Hidden) != 0) continue;
                _entries.Add(new FileDialogEntry
                {
                    Name = dir.Name,
                    FullPath = dir.FullName,
                    IsDirectory = true,
                    LastModified = dir.LastWriteTime
                });
            }

            // Files (filtered by type)
            if (_mode != FileDialogMode.SelectFolder)
            {
                string filter = _typeFilters[_activeFilterIndex];
                var patterns = filter.Split(';').Select(f => f.Trim()).ToArray();

                foreach (var file in dirInfo.EnumerateFiles())
                {
                    if (!_showHidden && (file.Attributes & FileAttributes.Hidden) != 0) continue;

                    bool matches = patterns.Any(p => p == "*.*" || MatchesPattern(file.Name, p));
                    if (!matches) continue;

                    _entries.Add(new FileDialogEntry
                    {
                        Name = file.Name,
                        FullPath = file.FullName,
                        IsDirectory = false,
                        Size = file.Length,
                        LastModified = file.LastWriteTime
                    });
                }
            }

            ApplySort();
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }

    private static bool MatchesPattern(string fileName, string pattern)
    {
        if (pattern == "*.*") return true;
        string ext = pattern.StartsWith("*") ? pattern[1..] : pattern;
        return fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplySort()
    {
        // Directories always first
        var dirs = _entries.Where(e => e.IsDirectory);
        var files = _entries.Where(e => !e.IsDirectory);

        IEnumerable<FileDialogEntry> SortBy(IEnumerable<FileDialogEntry> items) => _sortColumn switch
        {
            1 => _sortAscending ? items.OrderBy(e => e.Size) : items.OrderByDescending(e => e.Size),
            2 => _sortAscending ? items.OrderBy(e => e.LastModified) : items.OrderByDescending(e => e.LastModified),
            _ => _sortAscending ? items.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                                : items.OrderByDescending(e => e.Name, StringComparer.OrdinalIgnoreCase),
        };

        _entries = SortBy(dirs).Concat(SortBy(files)).ToList();
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
    }

    // ================================================================
    //  DRAW
    // ================================================================

    private const float DialogWidth = 750f;
    private const float DialogHeight = 500f;
    private const float SidebarWidth = 160f;
    private const float ToolbarHeight = 32f;
    private const float BottomBarHeight = 70f;
    private const float RowHeight = 24f;
    private const float HeaderHeight = 22f;

    public static void Draw(Paper paper)
    {
        if (!_isOpen) return;
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        // Fullscreen blocker
        paper.Box("fd_overlay")
            .PositionType(PositionType.SelfDirected)
            .Position(0, 0)
            .Size(UnitValue.Stretch(), UnitValue.Stretch())
            .BackgroundColor(Color.FromArgb(120, 0, 0, 0))
            .Layer(Layer.Overlay)
            .OnClick(0, (_, _) => { }); // block clicks

        // Dialog window centered via auto margins
        using (paper.Column("fd_window")
            .Size(DialogWidth, DialogHeight)
            .Margin(UnitValue.StretchOne)
            .BackgroundColor(EditorTheme.Neutral300)
            .BorderColor(EditorTheme.Ink200).BorderWidth(1).Rounded(8)
            .Layer(Layer.Overlay)
            .Enter())
        {
            DrawToolbar(paper, font);
            DrawBody(paper, font);
            DrawBottomBar(paper, font);
        }
    }

    private static void DrawToolbar(Paper paper, Prowl.Scribe.FontFile font)
    {
        using (paper.Row("fd_toolbar")
            .Height(ToolbarHeight)
            .BackgroundColor(EditorTheme.Neutral200)
            .ChildLeft(6).ChildRight(6).RowBetween(4)
            .Enter())
        {
            float btnW = 28f;

            // Back
            var backColor = _historyIndex > 0 ? EditorTheme.Ink500 : EditorTheme.Ink300;
            paper.Box("fd_back").Width(btnW).Height(ToolbarHeight)
                .Text(EditorIcons.ArrowLeft, font).TextColor(backColor).FontSize(14f)
                .Alignment(TextAlignment.MiddleCenter)
                .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                .Rounded(4)
                .OnClick(0, (_, _) => NavigateBack());

            // Forward
            var fwdColor = _historyIndex < _pathHistory.Count - 1 ? EditorTheme.Ink500 : EditorTheme.Ink300;
            paper.Box("fd_fwd").Width(btnW).Height(ToolbarHeight)
                .Text(EditorIcons.ArrowRight, font).TextColor(fwdColor).FontSize(14f)
                .Alignment(TextAlignment.MiddleCenter)
                .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                .Rounded(4)
                .OnClick(0, (_, _) => NavigateForward());

            // Up
            paper.Box("fd_up").Width(btnW).Height(ToolbarHeight)
                .Text(EditorIcons.ArrowUp, font).TextColor(EditorTheme.Ink500).FontSize(14f)
                .Alignment(TextAlignment.MiddleCenter)
                .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                .Rounded(4)
                .OnClick(0, (_, _) => NavigateUp());

            // Breadcrumb path
            paper.Box("fd_path")
                .Height(ToolbarHeight)
                .BackgroundColor(EditorTheme.Neutral300)
                .Rounded(4)
                .ChildLeft(8)
                .Text(_currentPath, font).TextColor(EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSize - 3)
                .Alignment(TextAlignment.MiddleLeft);

            // New folder button
            paper.Box("fd_newfolder").Width(btnW).Height(ToolbarHeight)
                .Text(EditorIcons.FolderPlus, font).TextColor(EditorTheme.Ink500).FontSize(14f)
                .Alignment(TextAlignment.MiddleCenter)
                .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                .Rounded(4)
                .OnClick(0, (_, _) =>
                {
                    _creatingFolder = !_creatingFolder;
                    _newFolderName = "New Folder";
                });
        }
    }

    private static void DrawBody(Paper paper, Prowl.Scribe.FontFile font)
    {
        using (paper.Row("fd_body")
            .Enter())
        {
            // Sidebar
            DrawSidebar(paper, font);

            // File list area
            using (paper.Column("fd_filelist_area")
                .Enter())
            {
                DrawColumnHeaders(paper, font);
                DrawFileList(paper, font);
            }
        }
    }

    private static void DrawSidebar(Paper paper, Prowl.Scribe.FontFile font)
    {
        using (paper.Column("fd_sidebar")
            .Width(SidebarWidth)
            .BackgroundColor(EditorTheme.Neutral200)
            .ChildTop(8).ChildLeft(4).ChildRight(4).ColBetween(2)
            .Enter())
        {
            // Quick access
            paper.Box("fd_qa_label").Height(20)
                .Text("Quick Access", font).TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSize - 3).Alignment(TextAlignment.MiddleLeft)
                .ChildLeft(8);

            foreach (var (label, icon, pathFn) in QuickAccess)
            {
                string qPath = pathFn();
                bool isCurrent = _currentPath.Equals(qPath, StringComparison.OrdinalIgnoreCase);
                paper.Box($"fd_qa_{label}").Height(24)
                    .BackgroundColor(isCurrent ? EditorTheme.Purple400 : Color.Transparent)
                    .Hovered.BackgroundColor(isCurrent ? EditorTheme.Purple400 : EditorTheme.Ink200).End()
                    .Rounded(4)
                    .ChildLeft(8)
                    .Text($"{icon}  {label}", font).TextColor(EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSize - 2).Alignment(TextAlignment.MiddleLeft)
                    .OnClick(qPath, (p, _) => NavigateTo(p));
            }

            // Separator
            paper.Box("fd_qa_sep").Height(1).Margin(4, 8, 4, 8)
                .BackgroundColor(EditorTheme.Ink200);

            // Drives
            paper.Box("fd_drv_label").Height(20)
                .Text("Drives", font).TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSize - 3).Alignment(TextAlignment.MiddleLeft)
                .ChildLeft(8);

            try
            {
                foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
                {
                    string dName = drive.Name;
                    string dLabel = $"{EditorIcons.HardDrive}  {dName}";
                    bool isCurrent = _currentPath.StartsWith(dName, StringComparison.OrdinalIgnoreCase);
                    paper.Box($"fd_drv_{dName}").Height(24)
                        .BackgroundColor(isCurrent ? EditorTheme.Purple400 : Color.Transparent)
                        .Hovered.BackgroundColor(isCurrent ? EditorTheme.Purple400 : EditorTheme.Ink200).End()
                        .Rounded(4)
                        .ChildLeft(8)
                        .Text(dLabel, font).TextColor(EditorTheme.Ink500)
                        .FontSize(EditorTheme.FontSize - 2).Alignment(TextAlignment.MiddleLeft)
                        .OnClick(dName, (p, _) => NavigateTo(p));
                }
            }
            catch { }
        }
    }

    private static void DrawColumnHeaders(Paper paper, Prowl.Scribe.FontFile font)
    {
        using (paper.Row("fd_headers")
            .Height(HeaderHeight)
            .BackgroundColor(EditorTheme.Neutral200)
            .ChildLeft(4).RowBetween(0)
            .Enter())
        {
            void ColHeader(string id, string label, int col, float? width = null)
            {
                var el = paper.Box(id).Height(HeaderHeight);
                if (width.HasValue) el.Width(width.Value);
                el.ChildLeft(8)
                    .Text(label + (_sortColumn == col ? (_sortAscending ? " \u25B4" : " \u25BE") : ""), font)
                    .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSize - 3)
                    .Alignment(TextAlignment.MiddleLeft)
                    .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                    .OnClick(col, (c, _) =>
                    {
                        if (_sortColumn == c) _sortAscending = !_sortAscending;
                        else { _sortColumn = c; _sortAscending = true; }
                        ApplySort();
                    });
            }

            ColHeader("fd_h_name", "Name", 0);
            ColHeader("fd_h_size", "Size", 1, 80f);
            ColHeader("fd_h_date", "Modified", 2, 140f);
        }
    }

    private static void DrawFileList(Paper paper, Prowl.Scribe.FontFile font)
    {
        float listWidth = DialogWidth - SidebarWidth;
        float listHeight = DialogHeight - ToolbarHeight - HeaderHeight - BottomBarHeight;

        Origami.ScrollView(paper, "fd_scroll", listWidth, listHeight).Body(() =>
        {
            // New folder entry
            if (_creatingFolder)
            {
                using (paper.Row("fd_newfolder_row")
                    .Height(RowHeight)
                    .BackgroundColor(EditorTheme.Purple400)
                    .ChildLeft(8).RowBetween(8)
                    .Enter())
                {
                    paper.Box("fd_nf_ico").Width(20).Height(RowHeight)
                        .Text(EditorIcons.Folder, font).TextColor(EditorTheme.Ink500)
                        .FontSize(EditorTheme.FontSize - 2).Alignment(TextAlignment.MiddleCenter);

                    EditorGUI.TextField(paper, "fd_nf_name", "Name", _newFolderName)
                        .OnValueChanged(v => _newFolderName = v);

                    EditorGUI.Button(paper, "fd_nf_ok", "Create", width: 60)
                        .OnValueChanged(_ =>
                        {
                            try
                            {
                                string newPath = Path.Combine(_currentPath, _newFolderName);
                                Directory.CreateDirectory(newPath);
                                _creatingFolder = false;
                                RefreshEntries();
                            }
                            catch { }
                        });

                    EditorGUI.Button(paper, "fd_nf_cancel", "Cancel", width: 60)
                        .OnValueChanged(_ => _creatingFolder = false);
                }
            }

            // Apply search filter
            var displayEntries = string.IsNullOrEmpty(_searchFilter)
                ? _entries
                : _entries.Where(e => e.Name.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase)).ToList();

            for (int i = 0; i < displayEntries.Count; i++)
            {
                var entry = displayEntries[i];
                bool isSelected = entry.FullPath == _selectedPath;
                int idx = i;

                using (paper.Row($"fd_row_{i}")
                    .Height(RowHeight)
                    .BackgroundColor(isSelected ? EditorTheme.Purple400 : (i % 2 == 0 ? Color.Transparent : Color.FromArgb(15, 255, 255, 255)))
                    .Hovered.BackgroundColor(isSelected ? EditorTheme.Purple400 : EditorTheme.Ink200).End()
                    .ChildLeft(8).RowBetween(0)
                    .OnClick(entry, (e, ev) =>
                    {
                        _selectedIndex = idx;
                        _selectedPath = e.FullPath;
                        if (!e.IsDirectory)
                            _fileName = e.Name;
                    })
                    .OnDoubleClick(entry, (e, ev) =>
                    {
                        if (e.IsDirectory)
                            NavigateTo(e.FullPath);
                        else
                            ConfirmSelection();
                    })
                    .Enter())
                {
                    // Icon + Name
                    string icon = entry.IsDirectory ? EditorIcons.Folder : GetFileIcon(entry.Name);
                    var iconColor = entry.IsDirectory ? Color.FromArgb(255, 220, 180, 80) : EditorTheme.Ink400;

                    paper.Box($"fd_ico_{i}").Width(20).Height(RowHeight)
                        .Text(icon, font).TextColor(iconColor)
                        .FontSize(EditorTheme.FontSize - 2).Alignment(TextAlignment.MiddleCenter);

                    paper.Box($"fd_name_{i}").Height(RowHeight)
                        .ChildLeft(4)
                        .Text(entry.Name, font).TextColor(EditorTheme.Ink500)
                        .FontSize(EditorTheme.FontSize - 2).Alignment(TextAlignment.MiddleLeft);

                    // Size
                    string sizeText = entry.IsDirectory ? "" : FormatSize(entry.Size);
                    paper.Box($"fd_size_{i}").Width(80).Height(RowHeight)
                        .Text(sizeText, font).TextColor(EditorTheme.Ink400)
                        .FontSize(EditorTheme.FontSize - 3).Alignment(TextAlignment.MiddleRight)
                        .ChildRight(8);

                    // Date
                    paper.Box($"fd_date_{i}").Width(140).Height(RowHeight)
                        .Text(entry.LastModified.ToString("yyyy-MM-dd  HH:mm"), font)
                        .TextColor(EditorTheme.Ink400)
                        .FontSize(EditorTheme.FontSize - 3).Alignment(TextAlignment.MiddleRight)
                        .ChildRight(8);
                }
            }

            if (displayEntries.Count == 0)
            {
                paper.Box("fd_empty").Height(60)
                    .Text("This folder is empty", font).TextColor(EditorTheme.Ink300)
                    .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleCenter);
            }
        });
    }

    private static void DrawBottomBar(Paper paper, Prowl.Scribe.FontFile font)
    {
        using (paper.Column("fd_bottom")
            .Height(BottomBarHeight)
            .BackgroundColor(EditorTheme.Neutral200)
            .ChildLeft(8).ChildRight(8).ChildTop(6).ChildBottom(6).ColBetween(6)
            .Enter())
        {
            // File name row
            using (paper.Row("fd_name_row")
                .Height(24).RowBetween(8)
                .Enter())
            {
                paper.Box("fd_name_lbl").Width(70).Height(24)
                    .Text(_mode == FileDialogMode.SelectFolder ? "Folder:" : "File name:", font)
                    .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSize - 2)
                    .Alignment(TextAlignment.MiddleRight);

                EditorGUI.TextField(paper, "fd_filename", "", _fileName)
                    .OnValueChanged(v => _fileName = v);

                // Filter dropdown
                if (_mode != FileDialogMode.SelectFolder && _typeFilterLabels.Length > 1)
                {
                    EditorGUI.Dropdown(paper, "fd_filter", "Filter", _activeFilterIndex, _typeFilterLabels)
                        .OnValueChanged(v => { _activeFilterIndex = v; RefreshEntries(); });
                }
            }

            // Buttons row
            using (paper.Row("fd_btn_row")
                .Height(26).RowBetween(8)
                .Enter())
            {
                // Spacer
                paper.Box("fd_btn_spacer");

                string confirmLabel = _mode switch
                {
                    FileDialogMode.Save => "Save",
                    FileDialogMode.SelectFolder => "Select Folder",
                    _ => "Open"
                };

                EditorGUI.Button(paper, "fd_btn_ok", confirmLabel, width: 101)
                    .OnValueChanged(_ => ConfirmSelection());

                EditorGUI.Button(paper, "fd_btn_cancel", "Cancel", width: 60)
                    .OnValueChanged(_ => Close(null));
            }
        }
    }

    private static void ConfirmSelection()
    {
        string result;
        if (_mode == FileDialogMode.SelectFolder)
        {
            result = !string.IsNullOrEmpty(_selectedPath) && Directory.Exists(_selectedPath)
                ? _selectedPath : _currentPath;
        }
        else if (_mode == FileDialogMode.Save)
        {
            result = !string.IsNullOrEmpty(_fileName)
                ? Path.Combine(_currentPath, _fileName) : "";
        }
        else
        {
            // Open mode
            if (!string.IsNullOrEmpty(_selectedPath) && File.Exists(_selectedPath))
                result = _selectedPath;
            else if (!string.IsNullOrEmpty(_fileName))
                result = Path.Combine(_currentPath, _fileName);
            else
                return; // nothing selected
        }

        if (!string.IsNullOrEmpty(result))
            Close(result);
    }

    private static string GetFileIcon(string fileName) => FileIconRegistry.GetIconForFile(fileName);
}
