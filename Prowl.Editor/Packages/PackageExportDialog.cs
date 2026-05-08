using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Editor.Widgets;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using Color = System.Drawing.Color;

namespace Prowl.Editor.Packages;

/// <summary>
/// Overlay dialog for exporting selected assets into a .prowlpackage file.
/// Follows the same static overlay pattern as FileDialog.
/// </summary>
public static class PackageExportDialog
{
    private static bool _isOpen;
    private static List<string> _explicitPaths = new();    // user-selected assets
    private static HashSet<string> _dependencyPaths = new(); // auto-resolved dependencies
    private static HashSet<string> _enabledPaths = new();
    private static bool _includeDependencies = true;
    private static bool _lastIncludeDependencies = true; // track toggle changes
    private static bool _includeProjectSettings;
    private static string _outputPath = "";

    // Tree structure for folder grouping
    private static List<TreeNode> _rootNodes = new();
    private static HashSet<string> _expandedFolders = new();

    private class TreeNode
    {
        public string Name = "";
        public string FullPath = ""; // Relative asset path (empty for folders that are just grouping)
        public bool IsFolder;
        public bool IsDependency; // True if this asset was pulled in via dependency resolution
        public List<TreeNode> Children = new();
    }

    private const float DialogWidth = 550f;
    private const float DialogHeight = 480f;
    private const float RowHeight = 22f;

    public static bool IsOpen => _isOpen;

    public static void Open(List<string> selectedAssetPaths)
    {
        _explicitPaths = selectedAssetPaths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _includeDependencies = true;
        _lastIncludeDependencies = true;
        _includeProjectSettings = false;

        // Default output path
        var project = Project.Current;
        if (project != null)
            _outputPath = Path.Combine(project.PackagesPath, project.Name + ".prowlpackage");

        ResolveDependencies();
        RebuildTreeAndEnabled();
        _isOpen = true;
    }

    public static void Close()
    {
        _isOpen = false;
        _explicitPaths.Clear();
        _dependencyPaths.Clear();
        _enabledPaths.Clear();
        _rootNodes.Clear();
    }

    /// <summary>
    /// Walk dependency graph from explicit paths to find all referenced assets.
    /// </summary>
    private static void ResolveDependencies()
    {
        _dependencyPaths.Clear();

        var db = EditorAssetDatabase.Instance;
        if (db == null) return;

        var explicitSet = new HashSet<string>(_explicitPaths, StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(_explicitPaths);

        while (queue.Count > 0)
        {
            string path = queue.Dequeue();
            if (!visited.Add(path)) continue;

            var entry = db.GetEntry(path);
            if (entry?.Dependencies == null) continue;

            foreach (var depGuid in entry.Dependencies)
            {
                string? depPath = db.GuidToPath(depGuid);
                if (depPath == null || visited.Contains(depPath)) continue;

                if (!explicitSet.Contains(depPath))
                    _dependencyPaths.Add(depPath);

                queue.Enqueue(depPath);
            }
        }
    }

    /// <summary>
    /// Rebuild the tree from explicit paths + (optionally) dependency paths.
    /// Preserves expanded/enabled state where possible.
    /// </summary>
    private static void RebuildTreeAndEnabled()
    {
        // Collect all paths that should appear in the tree
        var allPaths = new List<string>(_explicitPaths);
        if (_includeDependencies)
            allPaths.AddRange(_dependencyPaths);

        allPaths = allPaths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Reset enabled: all on by default
        _enabledPaths = new HashSet<string>(allPaths, StringComparer.OrdinalIgnoreCase);

        BuildTree(allPaths);
    }

    private static void BuildTree(List<string> allPaths)
    {
        _rootNodes.Clear();
        _expandedFolders.Clear();

        var folderMap = new Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase);

        TreeNode GetOrCreateFolder(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath))
                return null!;

            if (folderMap.TryGetValue(folderPath, out var existing))
                return existing;

            var node = new TreeNode
            {
                Name = Path.GetFileName(folderPath),
                FullPath = folderPath,
                IsFolder = true
            };
            folderMap[folderPath] = node;

            string parent = Path.GetDirectoryName(folderPath)?.Replace('\\', '/') ?? "";
            if (!string.IsNullOrEmpty(parent))
            {
                var parentNode = GetOrCreateFolder(parent);
                parentNode.Children.Add(node);
            }
            else
            {
                _rootNodes.Add(node);
            }

            return node;
        }

        foreach (string path in allPaths)
        {
            string dir = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "";
            var fileNode = new TreeNode
            {
                Name = Path.GetFileName(path),
                FullPath = path,
                IsFolder = false,
                IsDependency = _dependencyPaths.Contains(path)
            };

            if (!string.IsNullOrEmpty(dir))
            {
                var folder = GetOrCreateFolder(dir);
                folder.Children.Add(fileNode);
            }
            else
            {
                _rootNodes.Add(fileNode);
            }
        }

        SortNodes(_rootNodes);
    }

    private static void SortNodes(List<TreeNode> nodes)
    {
        nodes.Sort((a, b) =>
        {
            if (a.IsFolder != b.IsFolder) return a.IsFolder ? -1 : 1;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        foreach (var node in nodes)
            if (node.Children.Count > 0)
                SortNodes(node.Children);
    }

    // ================================================================
    //  Draw
    // ================================================================

    public static void Draw(Paper paper)
    {
        if (!_isOpen) return;
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        // Detect toggle change and rebuild tree
        if (_includeDependencies != _lastIncludeDependencies)
        {
            _lastIncludeDependencies = _includeDependencies;
            RebuildTreeAndEnabled();
        }

        // Fullscreen blocker
        EditorGUI.Backdrop(paper, "pkgexp_overlay");

        // Dialog centered
        using (paper.Column("pkgexp_window")
            .Size(DialogWidth, DialogHeight)
            .Margin(UnitValue.StretchOne)
            .BackgroundColor(EditorTheme.Neutral300)
            .BorderColor(EditorTheme.Ink200).BorderWidth(1).Rounded(8)
            .Layer(Layer.Overlay)
            .Enter())
        {
            DrawTitle(paper, font);
            DrawFileTree(paper, font);
            DrawOptions(paper, font);
            DrawBottomBar(paper, font);
        }
    }

    private static void DrawTitle(Paper paper, Prowl.Scribe.FontFile font)
    {
        using (paper.Row("pkgexp_title")
            .Height(32)
            .BackgroundColor(EditorTheme.Neutral200)
            .Rounded(8)
            .ChildLeft(12)
            .Enter())
        {
            paper.Box("pkgexp_title_text")
                .Height(32)
                .Text("Export ProwlPackage", font)
                .TextColor(EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSize + 1)
                .Alignment(TextAlignment.MiddleLeft);

            paper.Box("pkgexp_title_spacer").Width(UnitValue.Stretch());

            paper.Box("pkgexp_close")
                .Size(32)
                .Text(EditorIcons.Xmark, font).TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleCenter)
                .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                .Rounded(4)
                .OnClick((_) => Close());
        }
    }

    private static void DrawFileTree(Paper paper, Prowl.Scribe.FontFile font)
    {
        float treeHeight = DialogHeight - 32 - 90 - 60; // title - options - bottom bar

        Origami.ScrollView(paper, "pkgexp_tree_scroll", DialogWidth - 16, treeHeight)
            .Body(() =>
            {
                foreach (var node in _rootNodes)
                    DrawTreeNode(paper, font, node, 0);
            });
    }

    private static void DrawTreeNode(Paper paper, Prowl.Scribe.FontFile font, TreeNode node, int depth)
    {
        float indent = depth * 16f;
        string id = $"pkgexp_node_{node.FullPath.GetHashCode():X}";

        if (node.IsFolder)
        {
            bool expanded = _expandedFolders.Contains(node.FullPath);
            bool allEnabled = AllChildrenEnabled(node);
            bool anyEnabled = AnyChildrenEnabled(node);

            using (paper.Row(id)
                .Height(RowHeight)
                .ChildLeft(indent + 4)
                .Hovered.BackgroundColor(EditorTheme.Ink100).End()
                .Enter())
            {
                // Expand arrow
                paper.Box($"{id}_arrow")
                    .Size(16, RowHeight)
                    .Text(expanded ? EditorIcons.AngleDown : EditorIcons.AngleRight, font)
                    .TextColor(EditorTheme.Ink400)
                    .FontSize(EditorTheme.FontSize - 2)
                    .Alignment(TextAlignment.MiddleCenter)
                    .OnClick((_) =>
                    {
                        if (expanded) _expandedFolders.Remove(node.FullPath);
                        else _expandedFolders.Add(node.FullPath);
                    });

                Origami.Checkbox(paper, $"{id}_chk", allEnabled, v =>
                {
                    SetFolderEnabled(node, v);
                }).Show();

                paper.Box($"{id}_ico")
                    .Size(16, RowHeight)
                    .Text(EditorIcons.Folder, font)
                    .TextColor(Color.FromArgb(255, 220, 180, 80))
                    .FontSize(EditorTheme.FontSize - 2)
                    .Alignment(TextAlignment.MiddleCenter);

                paper.Box($"{id}_name")
                    .Height(RowHeight).ChildLeft(4)
                    .Text(node.Name, font)
                    .TextColor(anyEnabled ? EditorTheme.Ink500 : EditorTheme.Ink300)
                    .FontSize(EditorTheme.FontSize - 1)
                    .Alignment(TextAlignment.MiddleLeft);
            }

            if (expanded)
            {
                foreach (var child in node.Children)
                    DrawTreeNode(paper, font, child, depth + 1);
            }
        }
        else
        {
            bool enabled = _enabledPaths.Contains(node.FullPath);
            bool isDep = node.IsDependency;

            // Dependency items shown with a distinct tint
            Color nameColor;
            if (!enabled)
                nameColor = EditorTheme.Ink300;
            else if (isDep)
                nameColor = Color.FromArgb(255, 140, 180, 220); // blue-ish tint for deps
            else
                nameColor = EditorTheme.Ink500;

            using (paper.Row(id)
                .Height(RowHeight)
                .ChildLeft(indent + 20)
                .Hovered.BackgroundColor(EditorTheme.Ink100).End()
                .Enter())
            {
                Origami.Checkbox(paper, $"{id}_chk", enabled, v =>
                {
                    if (v) _enabledPaths.Add(node.FullPath);
                    else _enabledPaths.Remove(node.FullPath);
                }).Show();

                string icon = FileIconRegistry.GetIconForFile(node.Name);
                paper.Box($"{id}_ico")
                    .Size(16, RowHeight)
                    .Text(icon, font)
                    .TextColor(isDep ? Color.FromArgb(255, 140, 180, 220) : EditorTheme.Ink400)
                    .FontSize(EditorTheme.FontSize - 2)
                    .Alignment(TextAlignment.MiddleCenter);

                paper.Box($"{id}_name")
                    .Height(RowHeight).ChildLeft(4)
                    .Text(node.Name, font)
                    .TextColor(nameColor)
                    .FontSize(EditorTheme.FontSize - 1)
                    .Alignment(TextAlignment.MiddleLeft);

                // Show "(dependency)" label for auto-included assets
                if (isDep)
                {
                    paper.Box($"{id}_dep_label")
                        .Height(RowHeight).ChildLeft(4)
                        .Text("(dependency)", font)
                        .TextColor(Color.FromArgb(255, 100, 140, 180))
                        .FontSize(EditorTheme.FontSize - 3)
                        .Alignment(TextAlignment.MiddleLeft);
                }
            }
        }
    }

    private static void DrawOptions(Paper paper, Prowl.Scribe.FontFile font)
    {
        using (paper.Column("pkgexp_options")
            .Height(90)
            .ChildLeft(12).ChildRight(12).ChildTop(8)
            .ColBetween(6)
            .Enter())
        {
            string depLabel = _dependencyPaths.Count > 0
                ? $"Include Dependencies ({_dependencyPaths.Count})"
                : "Include Dependencies";

            Origami.Checkbox(paper, "pkgexp_deps", _includeDependencies, v => _includeDependencies = v)
                .LabelRight(depLabel).Show();

            Origami.Checkbox(paper, "pkgexp_settings", _includeProjectSettings, v => _includeProjectSettings = v)
                .LabelRight("Include Project Settings").Show();

            // Output path
            using (paper.Row("pkgexp_path_row")
                .Height(EditorTheme.RowHeight)
                .RowBetween(6)
                .Enter())
            {
                paper.Box("pkgexp_path_lbl")
                    .Width(60).Height(EditorTheme.RowHeight)
                    .Text("Save to:", font).TextColor(EditorTheme.Ink400)
                    .FontSize(EditorTheme.FontSize - 1)
                    .Alignment(TextAlignment.MiddleRight);

                Origami.TextField(paper, "pkgexp_path", _outputPath, v => _outputPath = v)
                    .Width(UnitValue.Stretch()).Show();

                EditorGUI.Button(paper, "pkgexp_browse", "...", width: 30)
                    .OnValueChanged(_ =>
                    {
                        FileDialog.Open(FileDialogMode.Save, path =>
                        {
                            if (path != null)
                            {
                                if (!path.EndsWith(".prowlpackage", StringComparison.OrdinalIgnoreCase))
                                    path += ".prowlpackage";
                                _outputPath = path;
                            }
                        },
                        startPath: Path.GetDirectoryName(_outputPath),
                        filters: new[] { "*.prowlpackage" },
                        filterLabels: new[] { "ProwlPackage (*.prowlpackage)" });
                    });
            }
        }
    }

    private static void DrawBottomBar(Paper paper, Prowl.Scribe.FontFile font)
    {
        using (paper.Row("pkgexp_bottom")
            .Height(40)
            .ChildRight(12).ChildBottom(8).ChildTop(8)
            .RowBetween(8)
            .Enter())
        {
            int enabledCount = _enabledPaths.Count;
            int totalInTree = _explicitPaths.Count + (_includeDependencies ? _dependencyPaths.Count : 0);

            paper.Box("pkgexp_count")
                .Height(24).ChildLeft(12)
                .Text($"{enabledCount} of {totalInTree} assets selected", font)
                .TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSize - 1)
                .Alignment(TextAlignment.MiddleLeft);

            paper.Box("pkgexp_spacer").Width(UnitValue.Stretch());

            EditorGUI.Button(paper, "pkgexp_cancel", "Cancel", width: 80)
                .OnValueChanged(_ => Close());

            EditorGUI.Button(paper, "pkgexp_export", "Export", width: 80)
                .OnValueChanged(_ => DoExport());
        }
    }

    private static void DoExport()
    {
        if (string.IsNullOrWhiteSpace(_outputPath))
        {
            ModalDialog.Message("Export Error", "Please specify an output path.");
            return;
        }

        if (_enabledPaths.Count == 0)
        {
            ModalDialog.Message("Export Error", "No assets selected for export.");
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_outputPath)!);
            // Pass includeDependencies=false since we already resolved deps into the enabled set
            ProwlPackage.Export(_outputPath, _enabledPaths, _includeProjectSettings, includeDependencies: false);
            Toasts.Success("Package Exported", $"Exported {_enabledPaths.Count} assets");
            Close();
        }
        catch (Exception ex)
        {
            ModalDialog.Message("Export Failed", ex.Message);
        }
    }

    // ================================================================
    //  Tree helpers
    // ================================================================

    private static bool AllChildrenEnabled(TreeNode folder)
    {
        foreach (var child in folder.Children)
        {
            if (child.IsFolder)
            {
                if (!AllChildrenEnabled(child)) return false;
            }
            else
            {
                if (!_enabledPaths.Contains(child.FullPath)) return false;
            }
        }
        return true;
    }

    private static bool AnyChildrenEnabled(TreeNode folder)
    {
        foreach (var child in folder.Children)
        {
            if (child.IsFolder)
            {
                if (AnyChildrenEnabled(child)) return true;
            }
            else
            {
                if (_enabledPaths.Contains(child.FullPath)) return true;
            }
        }
        return false;
    }

    private static void SetFolderEnabled(TreeNode folder, bool enabled)
    {
        foreach (var child in folder.Children)
        {
            if (child.IsFolder)
                SetFolderEnabled(child, enabled);
            else
            {
                if (enabled) _enabledPaths.Add(child.FullPath);
                else _enabledPaths.Remove(child.FullPath);
            }
        }
    }
}
