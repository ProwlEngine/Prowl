using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Editor.AssetsDatabase;
using Prowl.Editor.Core;
using Prowl.Editor.GUI.Registries;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using Color = System.Drawing.Color;

namespace Prowl.Editor.GUI.Popups;

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
        _modal = new OrigamiUI.CustomDrawModal((p, layer, _) => DrawInternal(p, layer));
        Modal.Push(_modal);
    }

    private static OrigamiUI.IModal? _modal;

    public static void Close()
    {
        _isOpen = false;
        _explicitPaths.Clear();
        _dependencyPaths.Clear();
        _enabledPaths.Clear();
        if (_modal != null) { Modal.Remove(_modal); _modal = null; }
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
                string? depPath = db.GuidToPathIncludingSubAssets(depGuid);
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
    }

    // ================================================================
    //  Draw
    // ================================================================

    public static void Draw(Paper paper) { } // Now handled by modal stack

    private static void DrawInternal(Paper paper, int layer)
    {
        if (!_isOpen) return;
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        if (_includeDependencies != _lastIncludeDependencies)
        {
            _lastIncludeDependencies = _includeDependencies;
            RebuildTreeAndEnabled();
        }

        using (paper.Column("pkgexp_window")
            .Size(DialogWidth, DialogHeight)
            .Margin(UnitValue.StretchOne)
            .BackgroundColor(EditorTheme.Neutral300)
            .BorderColor(EditorTheme.Ink200).BorderWidth(1).Rounded(8)
            .Layer(layer)
            .StopEventPropagation()
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

        var nodes = BuildFlatNodeList();

        Origami.Tree(paper, "pkgexp_tree", DialogWidth - 16, treeHeight)
            .Nodes(nodes)
            .Checkboxes()
            .OnCheckedChanged((node, isChecked) =>
            {
                string path = (string)node.UserData!;

                if (node.HasChildren)
                {
                    // Folder: toggle all descendant files
                    int folderIndex = nodes.IndexOf(node);
                    for (int i = folderIndex + 1; i < nodes.Count; i++)
                    {
                        if (nodes[i].Depth <= node.Depth) break;
                        if (nodes[i].IsLeaf)
                        {
                            string childPath = (string)nodes[i].UserData!;
                            if (isChecked) _enabledPaths.Add(childPath);
                            else _enabledPaths.Remove(childPath);
                        }
                    }
                }
                else
                {
                    // File: toggle single path
                    if (isChecked) _enabledPaths.Add(path);
                    else _enabledPaths.Remove(path);
                }
            })
            .EmptyMessage("No assets to export.")
            .Show();
    }

    /// <summary>
    /// Build a flat depth-first list of TreeNodes from the current paths for the Origami Tree widget.
    /// </summary>
    private static List<TreeNode> BuildFlatNodeList()
    {
        var allPaths = new List<string>(_explicitPaths);
        if (_includeDependencies)
            allPaths.AddRange(_dependencyPaths);

        allPaths = allPaths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Collect all unique folder paths and file entries
        var folderSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in allPaths)
        {
            string? dir = Path.GetDirectoryName(path)?.Replace('\\', '/');
            while (!string.IsNullOrEmpty(dir))
            {
                if (!folderSet.Add(dir)) break;
                dir = Path.GetDirectoryName(dir)?.Replace('\\', '/');
            }
        }

        // Build intermediate structure: for each folder, collect its direct children (folders + files)
        // Then emit depth-first, folders sorted before files, alphabetical within each group.
        var childFolders = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var childFiles = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (string folder in folderSet)
        {
            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/') ?? "";
            if (!childFolders.ContainsKey(parent))
                childFolders[parent] = new List<string>();
            childFolders[parent].Add(folder);
        }

        foreach (string path in allPaths)
        {
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "";
            if (!childFiles.ContainsKey(parent))
                childFiles[parent] = new List<string>();
            childFiles[parent].Add(path);
        }

        var result = new List<TreeNode>();

        void EmitChildren(string parentKey, int depth)
        {
            // Emit folders first, sorted
            if (childFolders.TryGetValue(parentKey, out var folders))
            {
                folders.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string folderPath in folders)
                {
                    // Check if all/any descendant files are enabled
                    bool allChecked = true;
                    bool anyChecked = false;
                    CheckDescendants(folderPath, ref allChecked, ref anyChecked);

                    result.Add(new TreeNode
                    {
                        Id = "f_" + folderPath,
                        Label = Path.GetFileName(folderPath),
                        Icon = EditorIcons.Folder,
                        IconColor = Color.FromArgb(255, 220, 180, 80),
                        HasChildren = true,
                        DefaultExpanded = true,
                        Depth = depth,
                        Checked = allChecked,
                        Indeterminate = !allChecked && anyChecked,
                        UserData = folderPath
                    });

                    EmitChildren(folderPath, depth + 1);
                }
            }

            // Then files, sorted
            if (childFiles.TryGetValue(parentKey, out var files))
            {
                files.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string filePath in files)
                {
                    bool isDep = _dependencyPaths.Contains(filePath);
                    bool enabled = _enabledPaths.Contains(filePath);
                    Color? labelColor = isDep ? Color.FromArgb(255, 140, 180, 220) : null;
                    Color? iconColor = isDep ? Color.FromArgb(255, 140, 180, 220) : null;

                    result.Add(new TreeNode
                    {
                        Id = "a_" + filePath,
                        Label = Path.GetFileName(filePath),
                        Icon = FileIconRegistry.GetIconForFile(filePath),
                        IconColor = iconColor,
                        LabelColor = labelColor,
                        Badge = isDep ? "(dependency)" : null,
                        BadgeColor = isDep ? Color.FromArgb(255, 100, 140, 180) : null,
                        HasChildren = false,
                        IsLeaf = true,
                        Depth = depth,
                        Checked = enabled,
                        UserData = filePath
                    });
                }
            }
        }

        void CheckDescendants(string folderPath, ref bool allChecked, ref bool anyChecked)
        {
            if (childFiles.TryGetValue(folderPath, out var files))
            {
                foreach (string f in files)
                {
                    if (_enabledPaths.Contains(f)) anyChecked = true;
                    else allChecked = false;
                }
            }
            if (childFolders.TryGetValue(folderPath, out var folders))
            {
                foreach (string sub in folders)
                    CheckDescendants(sub, ref allChecked, ref anyChecked);
            }
        }

        EmitChildren("", 0);
        return result;
    }

    private static void DrawOptions(Paper paper, Prowl.Scribe.FontFile font)
    {
        using (paper.Column("pkgexp_options")
            .Height(90)
            .Padding(12, 12, 8, 0)
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

                Origami.Button(paper, "pkgexp_browse", "...", () =>
                    {
                        EditorApplication.OpenFileDialog(FileDialogMode.Save, path =>
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
                    }).Width(30).Show();
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

            Origami.Button(paper, "pkgexp_cancel", "Cancel", () => { Close(); }).Width(80).Show();

            Origami.Button(paper, "pkgexp_export", "Export", () => { DoExport(); }).Width(80).Show();
        }
    }

    private static void DoExport()
    {
        if (string.IsNullOrWhiteSpace(_outputPath))
        {
            Origami.Message("Export Error", "Please specify an output path.");
            return;
        }

        if (_enabledPaths.Count == 0)
        {
            Origami.Message("Export Error", "No assets selected for export.");
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
            Origami.Message("Export Failed", ex.Message);
        }
    }

}
