using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

using Prowl.Editor.Widgets;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;

using Color = System.Drawing.Color;
using Texture2D = Prowl.Runtime.Resources.Texture2D;

namespace Prowl.Editor.Packages;

/// <summary>
/// Overlay dialog for importing a .prowlpackage file.
/// Shows a file tree with per-asset status (Add/Replace/Skip) and a detail panel.
/// </summary>
public static class PackageImportDialog
{
    private static bool _isOpen;
    private static string _packagePath = "";
    private static PackageManifest? _manifest;
    private static ZipArchive? _archive;
    private static FileStream? _archiveStream; // kept alive while dialog is open

    private static List<TreeNode> _rootNodes = new();
    private static HashSet<string> _expandedFolders = new();
    private static HashSet<string> _enabledPaths = new();
    private static Dictionary<string, ImportAction> _assetActions = new();
    private static string? _selectedAssetPath;

    // Thumbnail cache for the detail panel
    private static Dictionary<string, Texture2D?> _thumbCache = new();

    // Whether to also import project settings
    private static bool _importProjectSettings;
    private static bool _hasProjectSettings;

    private class TreeNode
    {
        public string Name = "";
        public string FullPath = "";
        public bool IsFolder;
        public List<TreeNode> Children = new();
    }

    private const float DialogWidth = 720f;
    private const float DialogHeight = 520f;
    private const float TreeWidth = 340f;
    private const float RowHeight = 22f;

    public static bool IsOpen => _isOpen;

    public static void Open(string packagePath)
    {
        try
        {
            _packagePath = packagePath;
            _archiveStream = File.OpenRead(packagePath);
            _archive = new ZipArchive(_archiveStream, ZipArchiveMode.Read);
            _manifest = ProwlPackage.ReadManifest(_archive);

            if (_manifest == null)
            {
                CloseArchive();
                ModalDialog.Message("Import Error", "Invalid package: missing manifest.json");
                return;
            }

            _hasProjectSettings = _manifest.ContainsProjectSettings;
            _importProjectSettings = false; // off by default - user must opt in
            _selectedAssetPath = null;

            // Compute import actions for each asset
            _assetActions.Clear();
            _enabledPaths.Clear();
            var project = Project.Current;
            string assetsPath = project?.AssetsPath ?? "";

            foreach (var asset in _manifest.Assets)
            {
                var action = ProwlPackage.DetermineAction(_archive, asset, assetsPath);
                _assetActions[asset.Path] = action;

                // Enable by default unless identical (Skip)
                if (action != ImportAction.Skip)
                    _enabledPaths.Add(asset.Path);
            }

            BuildTree();
            _isOpen = true;
        }
        catch (Exception ex)
        {
            CloseArchive();
            ModalDialog.Message("Import Error", $"Failed to open package: {ex.Message}");
        }
    }

    public static void Close()
    {
        _isOpen = false;
        CloseArchive();
        _manifest = null;
        _rootNodes.Clear();
        _assetActions.Clear();
        _enabledPaths.Clear();
        _selectedAssetPath = null;

        // Dispose cached thumbnails
        foreach (var tex in _thumbCache.Values)
            tex?.Dispose();
        _thumbCache.Clear();
    }

    private static void CloseArchive()
    {
        _archive?.Dispose();
        _archive = null;
        _archiveStream?.Dispose();
        _archiveStream = null;
    }

    private static void BuildTree()
    {
        _rootNodes.Clear();
        _expandedFolders.Clear();

        if (_manifest == null) return;

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
            _expandedFolders.Add(folderPath);

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

        foreach (var asset in _manifest.Assets)
        {
            string dir = Path.GetDirectoryName(asset.Path)?.Replace('\\', '/') ?? "";
            var fileNode = new TreeNode
            {
                Name = Path.GetFileName(asset.Path),
                FullPath = asset.Path,
                IsFolder = false
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

        // Fullscreen blocker
        EditorGUI.Backdrop(paper, "pkgimp_overlay");

        // Dialog centered
        using (paper.Column("pkgimp_window")
            .Size(DialogWidth, DialogHeight)
            .Margin(UnitValue.StretchOne)
            .BackgroundColor(EditorTheme.Neutral300)
            .BorderColor(EditorTheme.Ink200).BorderWidth(1).Rounded(8)
            .Layer(Layer.Overlay)
            .Enter())
        {
            DrawTitle(paper, font);

            if (_hasProjectSettings)
                DrawSettingsWarning(paper, font);

            DrawBody(paper, font);
            DrawBottomBar(paper, font);
        }
    }

    private static void DrawTitle(Paper paper, Prowl.Scribe.FontFile font)
    {
        using (paper.Row("pkgimp_title")
            .Height(32)
            .BackgroundColor(EditorTheme.Neutral200)
            .Rounded(8)
            .ChildLeft(12)
            .Enter())
        {
            string fileName = Path.GetFileName(_packagePath);
            paper.Box("pkgimp_title_text")
                .Height(32)
                .Text($"Import ProwlPackage - {fileName}", font)
                .TextColor(EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSize + 1)
                .Alignment(TextAlignment.MiddleLeft);

            paper.Box("pkgimp_title_spacer").Width(UnitValue.Stretch());

            paper.Box("pkgimp_close")
                .Size(32)
                .Text(EditorIcons.Xmark, font).TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleCenter)
                .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                .Rounded(4)
                .OnClick((_) => Close());
        }
    }

    private static void DrawSettingsWarning(Paper paper, Prowl.Scribe.FontFile font)
    {
        using (paper.Row("pkgimp_settings_warn")
            .Height(36)
            .BackgroundColor(Color.FromArgb(255, 80, 60, 20))
            .ChildLeft(12).RowBetween(8)
            .Enter())
        {
            paper.Box("pkgimp_warn_ico")
                .Size(20, 36)
                .Text(EditorIcons.TriangleExclamation, font)
                .TextColor(Color.FromArgb(255, 255, 200, 60))
                .FontSize(EditorTheme.FontSize)
                .Alignment(TextAlignment.MiddleCenter);

            paper.Box("pkgimp_warn_text")
                .Height(36)
                .Text("This package contains project settings. Recommended for fresh projects.", font)
                .TextColor(Color.FromArgb(255, 255, 220, 120))
                .FontSize(EditorTheme.FontSize - 1)
                .Alignment(TextAlignment.MiddleLeft);

            paper.Box("pkgimp_warn_spacer").Width(UnitValue.Stretch());

            Origami.Checkbox(paper, "pkgimp_settings_chk", _importProjectSettings, v => _importProjectSettings = v)
                .LabelRight("Import Settings").Show();
        }
    }

    private static void DrawBody(Paper paper, Prowl.Scribe.FontFile font)
    {
        float warningHeight = _hasProjectSettings ? 36f : 0f;
        float bodyHeight = DialogHeight - 32 - warningHeight - 44;

        using (paper.Row("pkgimp_body")
            .Height(bodyHeight)
            .Enter())
        {
            // Left: file tree
            DrawFileTree(paper, font, bodyHeight);

            // Separator
            paper.Box("pkgimp_sep")
                .Width(1)
                .BackgroundColor(EditorTheme.Ink200);

            // Right: detail panel
            DrawDetailPanel(paper, font, bodyHeight);
        }
    }

    private static void DrawFileTree(Paper paper, Prowl.Scribe.FontFile font, float height)
    {
        Origami.ScrollView(paper, "pkgimp_tree_scroll", TreeWidth, height)
            .Body(() =>
            {
                foreach (var node in _rootNodes)
                    DrawTreeNode(paper, font, node, 0);
            });
    }

    private static void DrawTreeNode(Paper paper, Prowl.Scribe.FontFile font, TreeNode node, int depth)
    {
        float indent = depth * 16f;
        string id = $"pkgimp_node_{node.FullPath.GetHashCode():X}";

        if (node.IsFolder)
        {
            bool expanded = _expandedFolders.Contains(node.FullPath);
            bool allEnabled = AllChildrenEnabled(node);

            using (paper.Row(id)
                .Height(RowHeight)
                .ChildLeft(indent + 4)
                .Hovered.BackgroundColor(EditorTheme.Ink100).End()
                .Enter())
            {
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
                    .TextColor(EditorTheme.Ink500)
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
            var action = _assetActions.GetValueOrDefault(node.FullPath, ImportAction.Add);
            bool isSelected = _selectedAssetPath == node.FullPath;

            Color statusColor = action switch
            {
                ImportAction.Add => Color.FromArgb(255, 80, 200, 80),
                ImportAction.Replace => Color.FromArgb(255, 220, 180, 40),
                ImportAction.Skip => EditorTheme.Ink300,
                _ => EditorTheme.Ink400
            };

            Color bgColor = isSelected ? EditorTheme.Purple400 : Color.Transparent;

            using (paper.Row(id)
                .Height(RowHeight)
                .ChildLeft(indent + 20)
                .BackgroundColor(bgColor)
                .Hovered.BackgroundColor(isSelected ? EditorTheme.Purple400 : EditorTheme.Ink100).End()
                .OnClick(node.FullPath, (path, _) => _selectedAssetPath = path)
                .Enter())
            {
                Origami.Checkbox(paper, $"{id}_chk", enabled, v =>
                {
                    if (v) _enabledPaths.Add(node.FullPath);
                    else _enabledPaths.Remove(node.FullPath);
                }).Show();

                // Status dot
                paper.Box($"{id}_dot")
                    .Size(8, RowHeight)
                    .Margin(0, 0, 4, 0);

                string icon = FileIconRegistry.GetIconForFile(node.Name);
                paper.Box($"{id}_ico")
                    .Size(16, RowHeight)
                    .Text(icon, font)
                    .TextColor(statusColor)
                    .FontSize(EditorTheme.FontSize - 2)
                    .Alignment(TextAlignment.MiddleCenter);

                paper.Box($"{id}_name")
                    .Height(RowHeight).ChildLeft(4)
                    .Text(node.Name, font)
                    .TextColor(enabled ? statusColor : EditorTheme.Ink300)
                    .FontSize(EditorTheme.FontSize - 1)
                    .Alignment(TextAlignment.MiddleLeft);
            }
        }
    }

    private static void DrawDetailPanel(Paper paper, Prowl.Scribe.FontFile font, float height)
    {
        float detailWidth = DialogWidth - TreeWidth - 1;

        using (paper.Column("pkgimp_detail")
            .Width(detailWidth).Height(height)
            .ChildLeft(16).ChildRight(16).ChildTop(16)
            .ColBetween(8)
            .Enter())
        {
            if (_selectedAssetPath == null || _manifest == null)
            {
                paper.Box("pkgimp_detail_hint")
                    .Height(60)
                    .Text("Select an asset to view details", font)
                    .TextColor(EditorTheme.Ink300)
                    .FontSize(EditorTheme.FontSize)
                    .Alignment(TextAlignment.MiddleCenter);
                return;
            }

            var asset = _manifest.Assets.FirstOrDefault(a =>
                a.Path.Equals(_selectedAssetPath, StringComparison.OrdinalIgnoreCase));

            if (asset == null) return;

            var action = _assetActions.GetValueOrDefault(asset.Path, ImportAction.Add);

            // Thumbnail
            var thumbTex = GetPackageThumbnail(asset.Path);
            float thumbDisplaySize = Math.Min(detailWidth - 32, 128);
            if (thumbTex != null)
            {
                paper.Box("pkgimp_thumb")
                    .Size(thumbDisplaySize, thumbDisplaySize)
                    .Rounded(4)
                    .BackgroundColor(EditorTheme.Neutral200)
                    .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
                    {
                        canvas.DrawImage(thumbTex,
                            (float)r.Min.X, (float)r.Min.Y,
                            (float)r.Size.X, (float)r.Size.Y);
                    }));
            }

            // Info rows
            DetailRow(paper, font, "pkgimp_d_name", "Name", Path.GetFileName(asset.Path));
            DetailRow(paper, font, "pkgimp_d_path", "Path", asset.Path);

            // Type name - show just the class name, not the full assembly-qualified name
            string typeName = asset.MainAssetType;
            int commaIdx = typeName.IndexOf(',');
            if (commaIdx > 0) typeName = typeName[..commaIdx];
            int dotIdx = typeName.LastIndexOf('.');
            if (dotIdx >= 0) typeName = typeName[(dotIdx + 1)..];
            if (string.IsNullOrEmpty(typeName)) typeName = "Unknown";
            DetailRow(paper, font, "pkgimp_d_type", "Type", typeName);

            DetailRow(paper, font, "pkgimp_d_guid", "GUID", asset.Guid);
            DetailRow(paper, font, "pkgimp_d_size", "Size", FormatSize(asset.FileSize));

            // Status with color
            string statusText = action switch
            {
                ImportAction.Add => "Add (new asset)",
                ImportAction.Replace => "Replace (files differ)",
                ImportAction.Skip => "Nothing (identical)",
                _ => "Unknown"
            };
            Color statusColor = action switch
            {
                ImportAction.Add => Color.FromArgb(255, 80, 200, 80),
                ImportAction.Replace => Color.FromArgb(255, 220, 180, 40),
                ImportAction.Skip => EditorTheme.Ink300,
                _ => EditorTheme.Ink400
            };

            using (paper.Row("pkgimp_d_status")
                .Height(RowHeight)
                .RowBetween(8)
                .Enter())
            {
                paper.Box("pkgimp_d_status_lbl")
                    .Width(50).Height(RowHeight)
                    .Text("Status", font).TextColor(EditorTheme.Ink400)
                    .FontSize(EditorTheme.FontSize - 2)
                    .Alignment(TextAlignment.MiddleLeft);

                paper.Box("pkgimp_d_status_val")
                    .Height(RowHeight)
                    .Text(statusText, font).TextColor(statusColor)
                    .FontSize(EditorTheme.FontSize - 1)
                    .Alignment(TextAlignment.MiddleLeft);
            }
        }
    }

    private static void DetailRow(Paper paper, Prowl.Scribe.FontFile font, string id, string label, string value)
    {
        using (paper.Row(id)
            .Height(RowHeight)
            .RowBetween(8)
            .Enter())
        {
            paper.Box($"{id}_lbl")
                .Width(50).Height(RowHeight)
                .Text(label, font).TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSize - 2)
                .Alignment(TextAlignment.MiddleLeft);

            paper.Box($"{id}_val")
                .Height(RowHeight)
                .Text(value, font).TextColor(EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSize - 1)
                .Alignment(TextAlignment.MiddleLeft)
                .Clip();
        }
    }

    private static void DrawBottomBar(Paper paper, Prowl.Scribe.FontFile font)
    {
        using (paper.Row("pkgimp_bottom")
            .Height(44)
            .ChildRight(12).ChildBottom(10).ChildTop(10)
            .RowBetween(8)
            .Enter())
        {
            int enabledCount = _enabledPaths.Count;
            int totalCount = _manifest?.Assets.Count ?? 0;
            int addCount = _assetActions.Count(kv => kv.Value == ImportAction.Add && _enabledPaths.Contains(kv.Key));
            int replaceCount = _assetActions.Count(kv => kv.Value == ImportAction.Replace && _enabledPaths.Contains(kv.Key));

            paper.Box("pkgimp_count")
                .Height(24).ChildLeft(12)
                .Text($"{enabledCount} of {totalCount} selected ({addCount} add, {replaceCount} replace)", font)
                .TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSize - 1)
                .Alignment(TextAlignment.MiddleLeft);

            paper.Box("pkgimp_spacer").Width(UnitValue.Stretch());

            EditorGUI.Button(paper, "pkgimp_cancel", "Cancel", width: 80)
                .OnValueChanged(_ => Close());

            EditorGUI.Button(paper, "pkgimp_import", "Import", width: 80)
                .OnValueChanged(_ => DoImport());
        }
    }

    // ================================================================
    //  Import execution
    // ================================================================

    private static void DoImport()
    {
        if (_archive == null || _manifest == null) return;

        var project = Project.Current;
        if (project == null) return;

        int imported = 0;
        int failed = 0;

        foreach (var asset in _manifest.Assets)
        {
            if (!_enabledPaths.Contains(asset.Path)) continue;

            try
            {
                if (ProwlPackage.ExtractAsset(_archive, asset.Path, project.AssetsPath))
                    imported++;
                else
                    failed++;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProwlPackage] Failed to import '{asset.Path}': {ex.Message}");
                failed++;
            }
        }

        // Project settings
        if (_importProjectSettings && _hasProjectSettings)
        {
            try
            {
                ProwlPackage.ExtractProjectSettings(_archive, project.ProjectSettingsPath);
                Debug.Log("[ProwlPackage] Imported project settings.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProwlPackage] Failed to import project settings: {ex.Message}");
            }
        }

        Close();

        // Trigger asset database rescan to pick up the new/changed files
        var db = EditorAssetDatabase.Instance;
        if (db != null)
        {
            // Reinitialize to pick up all changes
            var freshDb = new EditorAssetDatabase(project);
            freshDb.Initialize();
        }

        string message = failed > 0
            ? $"Imported {imported} assets ({failed} failed)"
            : $"Imported {imported} assets";
        Toasts.Success("Package Imported", message);
        Debug.Log($"[ProwlPackage] {message}");
    }

    // ================================================================
    //  Thumbnail loading
    // ================================================================

    private static Texture2D? GetPackageThumbnail(string assetPath)
    {
        if (_archive == null) return null;

        if (_thumbCache.TryGetValue(assetPath, out var cached))
            return cached;

        try
        {
            byte[]? data = ProwlPackage.ReadThumbnail(_archive, assetPath);
            if (data == null || data.Length <= 8)
            {
                _thumbCache[assetPath] = null;
                return null;
            }

            // Parse size header
            int w = BitConverter.ToInt32(data, 0);
            int h = BitConverter.ToInt32(data, 4);

            if (w <= 0 || h <= 0 || data.Length != 8 + w * h * 4)
            {
                _thumbCache[assetPath] = null;
                return null;
            }

            byte[] pixels = new byte[data.Length - 8];
            Buffer.BlockCopy(data, 8, pixels, 0, pixels.Length);

            var tex = new Texture2D((uint)w, (uint)h, false, TextureImageFormat.Color4b);
            tex.SetData<byte>(pixels);
            tex.SetTextureFilters(TextureMin.Linear, TextureMag.Linear);
            _thumbCache[assetPath] = tex;
            return tex;
        }
        catch
        {
            _thumbCache[assetPath] = null;
            return null;
        }
    }

    // ================================================================
    //  Helpers
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

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
    }
}
