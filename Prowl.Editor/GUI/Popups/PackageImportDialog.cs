using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

using Prowl.Editor.Projects;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Runtime;

using Color = System.Drawing.Color;
using Texture2D = Prowl.Runtime.Resources.Texture2D;

namespace Prowl.Editor.GUI.Popups;

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

    private static HashSet<string> _enabledPaths = new();
    private static Dictionary<string, ImportAction> _assetActions = new();
    private static string? _selectedAssetPath;

    // Thumbnail cache for the detail panel
    private static Dictionary<string, Texture2D?> _thumbCache = new();

    // Whether to also import project settings
    private static bool _importProjectSettings;
    private static bool _hasProjectSettings;

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
                Origami.Message(Loc.Get("package.import_error"), Loc.Get("package.err_no_manifest"));
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

                _enabledPaths.Add(asset.Path);
            }

            _isOpen = true;
            _modal = new OrigamiUI.CustomDrawModal((p, layer, _) => DrawInternal(p, layer));
            Modal.Push(_modal);
        }
        catch (Exception ex)
        {
            CloseArchive();
            Origami.Message(Loc.Get("package.import_error"), Loc.Get("package.err_open", new { message = ex.Message }));
        }
    }

    private static OrigamiUI.IModal? _modal;

    public static void Close()
    {
        _isOpen = false;
        CloseArchive();
        _manifest = null;
        _assetActions.Clear();
        _enabledPaths.Clear();
        _selectedAssetPath = null;
        foreach (var tex in _thumbCache.Values)
            tex?.Dispose();
        _thumbCache.Clear();
        if (_modal != null) { Modal.Remove(_modal); _modal = null; }
    }

    private static void CloseArchive()
    {
        _archive?.Dispose();
        _archive = null;
        _archiveStream?.Dispose();
        _archiveStream = null;
    }

    // ================================================================
    //  Flat tree node builder
    // ================================================================

    private static Color GetActionColor(ImportAction action) => action switch
    {
        ImportAction.Add => EditorTheme.Green400,
        ImportAction.Replace => EditorTheme.Amber400,
        ImportAction.Skip => EditorTheme.Ink300,
        _ => EditorTheme.Ink400
    };

    private static string GetActionBadge(ImportAction action) => action switch
    {
        ImportAction.Add => Loc.Get("package.badge_add"),
        ImportAction.Replace => Loc.Get("package.badge_replace"),
        ImportAction.Skip => Loc.Get("package.badge_identical"),
        _ => ""
    };

    /// <summary>
    /// Builds a flat depth-first list of TreeNode for the Origami Tree widget.
    /// Folders first (sorted), then files (sorted), at each level.
    /// </summary>
    private static List<TreeNode> BuildFlatNodes()
    {
        if (_manifest == null)
            return new List<TreeNode>();

        // First, build a temporary hierarchy so we can sort folders-first, then flatten.
        var folderChildren = new Dictionary<string, List<(string name, string fullPath, bool isFolder)>>(StringComparer.OrdinalIgnoreCase);
        var allFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void EnsureFolder(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || !allFolders.Add(folderPath))
                return;

            string parent = Path.GetDirectoryName(folderPath)?.Replace('\\', '/') ?? "";
            EnsureFolder(parent);

            if (!folderChildren.ContainsKey(parent))
                folderChildren[parent] = new();
            folderChildren[parent].Add((Path.GetFileName(folderPath), folderPath, true));

            if (!folderChildren.ContainsKey(folderPath))
                folderChildren[folderPath] = new();
        }

        foreach (var asset in _manifest.Assets)
        {
            string dir = Path.GetDirectoryName(asset.Path)?.Replace('\\', '/') ?? "";
            EnsureFolder(dir);

            if (!folderChildren.ContainsKey(dir))
                folderChildren[dir] = new();
            folderChildren[dir].Add((Path.GetFileName(asset.Path), asset.Path, false));
        }

        // Sort each level: folders first, then alphabetical
        foreach (var list in folderChildren.Values)
        {
            list.Sort((a, b) =>
            {
                if (a.isFolder != b.isFolder) return a.isFolder ? -1 : 1;
                return string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase);
            });
        }

        // Collect all file paths into a set for quick lookup when computing folder check state
        var filePaths = new HashSet<string>(_manifest.Assets.Select(a => a.Path), StringComparer.OrdinalIgnoreCase);

        // Flatten depth-first
        var result = new List<TreeNode>();

        void Flatten(string parentPath, int depth)
        {
            if (!folderChildren.TryGetValue(parentPath, out var children))
                return;

            foreach (var (name, fullPath, isFolder) in children)
            {
                if (isFolder)
                {
                    // Compute checked/indeterminate for folder by collecting all descendant files
                    int totalFiles = 0;
                    int enabledFiles = 0;
                    CountDescendantFiles(fullPath, ref totalFiles, ref enabledFiles);

                    bool allChecked = totalFiles > 0 && enabledFiles == totalFiles;
                    bool someChecked = enabledFiles > 0 && enabledFiles < totalFiles;

                    result.Add(new TreeNode
                    {
                        Id = fullPath,
                        Label = name,
                        Icon = EditorIcons.Folder,
                        IconColor = EditorTheme.Amber400,
                        HasChildren = true,
                        DefaultExpanded = true,
                        Depth = depth,
                        Checked = allChecked,
                        Indeterminate = someChecked,
                        UserData = fullPath
                    });

                    Flatten(fullPath, depth + 1);
                }
                else
                {
                    var action = _assetActions.GetValueOrDefault(fullPath, ImportAction.Add);
                    bool enabled = _enabledPaths.Contains(fullPath);
                    var color = GetActionColor(action);

                    result.Add(new TreeNode
                    {
                        Id = fullPath,
                        Label = name,
                        Icon = EditorRegistries.GetFileIcon(name),
                        IconColor = color,
                        LabelColor = enabled ? color : EditorTheme.Ink300,
                        IsLeaf = true,
                        Depth = depth,
                        Checked = enabled,
                        Badge = GetActionBadge(action),
                        BadgeColor = color,
                        UserData = fullPath
                    });
                }
            }
        }

        void CountDescendantFiles(string folderPath, ref int total, ref int enabled)
        {
            if (!folderChildren.TryGetValue(folderPath, out var children))
                return;

            foreach (var (_, childPath, childIsFolder) in children)
            {
                if (childIsFolder)
                    CountDescendantFiles(childPath, ref total, ref enabled);
                else
                {
                    total++;
                    if (_enabledPaths.Contains(childPath))
                        enabled++;
                }
            }
        }

        Flatten("", 0);
        return result;
    }

    /// <summary>
    /// Toggle all descendant file nodes of a folder in the flat list.
    /// Starting from folderIndex+1, all nodes with Depth > folderDepth are descendants.
    /// </summary>
    private static void ToggleFolderChildren(List<TreeNode> nodes, TreeNode folder, bool enabled)
    {
        int folderIdx = nodes.IndexOf(folder);
        if (folderIdx < 0) return;

        for (int i = folderIdx + 1; i < nodes.Count; i++)
        {
            if (nodes[i].Depth <= folder.Depth)
                break; // past the folder's subtree

            // Only toggle leaf (file) nodes
            if (nodes[i].IsLeaf)
            {
                string path = (string)nodes[i].UserData!;
                if (enabled) _enabledPaths.Add(path);
                else _enabledPaths.Remove(path);
            }
        }
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

        using (paper.Column("pkgimp_window")
            .Size(DialogWidth, DialogHeight)
            .Margin(UnitValue.StretchOne)
            .BackgroundColor(EditorTheme.Neutral300)
            .BorderColor(EditorTheme.Ink200).BorderWidth(1).Rounded(8)
            .Layer(layer)
            .StopEventPropagation()
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
                .Text(Loc.Get("package.import_title", new { file = fileName }), font)
                .TextColor(EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSizeLarge)
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
            .BackgroundColor(EditorTheme.Amber300)
            .ChildLeft(12).RowBetween(8)
            .Enter())
        {
            paper.Box("pkgimp_warn_ico")
                .Size(20, 36)
                .Text(EditorIcons.TriangleExclamation, font)
                .TextColor(EditorTheme.Amber400)
                .FontSize(EditorTheme.FontSize)
                .Alignment(TextAlignment.MiddleCenter);

            paper.Box("pkgimp_warn_text")
                .Height(36)
                .Text(Loc.Get("package.settings_note"), font)
                .TextColor(EditorTheme.Amber500)
                .FontSize(EditorTheme.FontSizeSmall)
                .Alignment(TextAlignment.MiddleLeft);

            paper.Box("pkgimp_warn_spacer").Width(UnitValue.Stretch());

            Origami.Checkbox(paper, "pkgimp_settings_chk", _importProjectSettings, v => _importProjectSettings = v)
                .LabelRight(Loc.Get("package.import_settings")).Show();
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
            // Left: file tree using Origami Tree widget
            var nodes = BuildFlatNodes();
            Origami.Tree(paper, "pkgimp_tree", TreeWidth, bodyHeight)
                .Nodes(nodes)
                .Checkboxes()
                .IsSelected(n => (string?)n.UserData == _selectedAssetPath)
                .OnSelect(e => _selectedAssetPath = (string?)e.Node.UserData)
                .OnCheckedChanged((n, v) =>
                {
                    string path = (string)n.UserData!;
                    if (n.HasChildren)
                    {
                        // Folder: toggle all descendant files
                        ToggleFolderChildren(nodes, n, v);
                    }
                    else
                    {
                        // File: toggle individual path
                        if (v) _enabledPaths.Add(path);
                        else _enabledPaths.Remove(path);
                    }
                })
                .EmptyMessage(Loc.Get("package.no_assets"))
                .Show();

            // Separator
            paper.Box("pkgimp_sep")
                .Width(1)
                .BackgroundColor(EditorTheme.Ink200);

            // Right: detail panel
            DrawDetailPanel(paper, font, bodyHeight);
        }
    }

    private static void DrawDetailPanel(Paper paper, Prowl.Scribe.FontFile font, float height)
    {
        float detailWidth = DialogWidth - TreeWidth - 1;

        using (paper.Column("pkgimp_detail")
            .Width(detailWidth).Height(height)
            .Padding(16, 16, 16, 0)
            .ColBetween(8)
            .Enter())
        {
            if (_selectedAssetPath == null || _manifest == null)
            {
                paper.Box("pkgimp_detail_hint")
                    .Height(60)
                    .Text(Loc.Get("package.select_asset"), font)
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
            DetailRow(paper, font, "pkgimp_d_name", Loc.Get("inspector.name"), Path.GetFileName(asset.Path));
            DetailRow(paper, font, "pkgimp_d_path", Loc.Get("inspector.path"), asset.Path);

            // Type name - show just the class name, not the full assembly-qualified name
            string typeName = asset.MainAssetType;
            int commaIdx = typeName.IndexOf(',');
            if (commaIdx > 0) typeName = typeName[..commaIdx];
            int dotIdx = typeName.LastIndexOf('.');
            if (dotIdx >= 0) typeName = typeName[(dotIdx + 1)..];
            if (string.IsNullOrEmpty(typeName)) typeName = Loc.Get("inspector.unknown");
            DetailRow(paper, font, "pkgimp_d_type", Loc.Get("inspector.type"), typeName);

            DetailRow(paper, font, "pkgimp_d_guid", Loc.Get("inspector.guid"), asset.Guid);
            DetailRow(paper, font, "pkgimp_d_size", Loc.Get("inspector.size"), FormatSize(asset.FileSize));

            // Status with color
            string statusText = action switch
            {
                ImportAction.Add => Loc.Get("package.status_add"),
                ImportAction.Replace => Loc.Get("package.status_replace"),
                ImportAction.Skip => Loc.Get("package.status_skip"),
                _ => Loc.Get("inspector.unknown")
            };
            Color statusColor = action switch
            {
                ImportAction.Add => EditorTheme.Green400,
                ImportAction.Replace => EditorTheme.Amber400,
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
                    .Text(Loc.Get("package.status"), font).TextColor(EditorTheme.Ink400)
                    .FontSize(EditorTheme.FontSizeSmall)
                    .Alignment(TextAlignment.MiddleLeft);

                paper.Box("pkgimp_d_status_val")
                    .Height(RowHeight)
                    .Text(statusText, font).TextColor(statusColor)
                    .FontSize(EditorTheme.FontSizeSmall)
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
                .FontSize(EditorTheme.FontSizeSmall)
                .Alignment(TextAlignment.MiddleLeft);

            paper.Box($"{id}_val")
                .Height(RowHeight)
                .Text(value, font).TextColor(EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSizeSmall)
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
                .Text(Loc.Get("package.import_count", new { enabled = enabledCount, total = totalCount, add = addCount, replace = replaceCount }), font)
                .TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSizeSmall)
                .Alignment(TextAlignment.MiddleLeft);

            paper.Box("pkgimp_spacer").Width(UnitValue.Stretch());

            Origami.Button(paper, "pkgimp_cancel", Loc.Get("common.cancel"), () => { Close(); }).Width(80).Show();

            Origami.Button(paper, "pkgimp_import", Loc.Get("pref.import"), () => { DoImport(); }).Width(80).Show();
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
        var db = EditorAssetBackend.Instance;
        if (db != null)
        {
            // Reinitialize to pick up all changes
            var freshDb = new EditorAssetBackend(project);
            freshDb.Initialize();
        }

        string message = failed > 0
            ? Loc.Get("package.imported_msg_failed", new { count = imported, failed = failed })
            : Loc.Get("package.imported_msg", new { count = imported });
        Toasts.Success(Loc.Get("package.imported_title"), message);
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

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
    }
}
