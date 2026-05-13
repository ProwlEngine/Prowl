using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;


using Prowl.Editor.Docking;
using Prowl.Editor.Widgets;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Scribe;

using Color = System.Drawing.Color;
using TextAlignment = Prowl.PaperUI.TextAlignment;

namespace Prowl.Editor.Panels;

[EditorWindow("Prowl/Marketplace")]
public class MarketplacePanel : DockPanel
{
    public override string Title => "Marketplace";
    public override string Icon => EditorIcons.Store;

    private const float ToolbarHeight = 34f;
    private const float ListPaneWidth = 255f;
    private const float CardHeight = 78f;
    private const float CardGap = 4f;

    // Data
    private List<ProwlPackage> _packages = [];
    private bool _isLoading;
    private string? _loadError;
    private bool _initialized;

    // Filter / selection
    private string _searchText = "";
    private string _activeCategory = "all";
    private string? _selectedId;

    // Import dialog state
    private ProwlPackage? _importPackage;
    private PackageVersion? _importVersion;
    private string _importTargetFolder = "";
    private readonly Dictionary<string, bool> _folderOpenState = [];
    private bool _isImporting;
    private string _importStatusText = "";

    private static readonly (string key, string label)[] s_categories =
    [
        ("all",      "All"),
        ("shader",   "Shaders"),
        ("script",   "Scripts"),
        ("asset",    "Assets"),
        ("template", "Templates"),
    ];

    /// <inheritdoc/>
    public override void OnGUI(Paper paper, float width, float height)
    {
        FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;

        if (!_initialized && !_isLoading)
        {
            _initialized = true;
            _ = LoadAsync();
        }

        using (paper.Column("mkt_root").Size(width, height).Enter())
        {
            DrawToolbar(paper, font, width);
            DrawBody(paper, font, width, height - ToolbarHeight);
        }
    }

    private void DrawToolbar(Paper paper, FontFile font, float width)
    {
        using (paper.Row("mkt_toolbar")
            .Height(ToolbarHeight)
            .ChildLeft(6).ChildRight(6).ChildTop(4).ChildBottom(4)
            .RowBetween(6)
            .BackgroundColor(EditorTheme.Neutral300)
            .Enter())
        {
            EditorGUI.SearchBar(paper, "mkt_search", _searchText, "Search packages...")
                .OnValueChanged(v => _searchText = v);

            paper.Box("mkt_tb_sep").Width(1).BackgroundColor(EditorTheme.Ink200);

            foreach (var (key, label) in s_categories)
            {
                bool active = _activeCategory == key;
                EditorGUI.ToggleButton(paper, $"mkt_cat_{key}", label, active, fitWidth: true)
                    .OnValueChanged(_ => _activeCategory = key);
            }

            paper.Box("mkt_tb_stretch").Width(UnitValue.Stretch(1));

            EditorGUI.Button(paper, "mkt_refresh", $"{EditorIcons.ArrowsRotate}  Refresh", width: 86)
                .OnValueChanged(clicked =>
                {
                    if (!_isLoading)
                        _ = LoadAsync();
                });
        }
    }

    private void DrawBody(Paper paper, FontFile font, float width, float height)
    {
        if (_isLoading)
        {
            paper.Box("mkt_loading").Size(width, height)
                .Text("Loading packages...", font)
                .TextColor(EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSize)
                .Alignment(TextAlignment.MiddleCenter);
            return;
        }

        if (_loadError != null)
        {
            paper.Box("mkt_error").Size(width, height)
                .Text($"Could not load packages: {_loadError}", font)
                .TextColor(Color.FromArgb(255, 220, 80, 80))
                .FontSize(EditorTheme.FontSize)
                .Alignment(TextAlignment.MiddleCenter);
            return;
        }

        using (paper.Row("mkt_body").Size(width, height).Enter())
        {
            DrawPackageList(paper, font, height);
            paper.Box("mkt_divider").Width(1).Height(height).BackgroundColor(EditorTheme.Ink200);
            DrawPackageDetail(paper, font, width - ListPaneWidth - 1f, height);
        }
    }

    private void DrawPackageList(Paper paper, FontFile font, float height)
    {
        List<ProwlPackage> filtered = FilteredPackages();

        Origami.ScrollView(paper, "mkt_list", ListPaneWidth, height)
            .Padding(6f, 6f, 6f, 6f)
            .ColSpacing(CardGap)
            .Body(() =>
            {
                if (filtered.Count == 0)
                {
                    paper.Box("mkt_list_empty")
                        .Width(ListPaneWidth - 12f).Height(60)
                        .Text("No packages found.", font)
                        .TextColor(EditorTheme.Ink300)
                        .FontSize(EditorTheme.FontSize)
                        .Alignment(TextAlignment.MiddleCenter);
                    return;
                }

                for (int i = 0; i < filtered.Count; i++)
                    DrawPackageCard(paper, font, i, filtered[i]);
            });
    }

    private void DrawPackageCard(Paper paper, FontFile font, int index, ProwlPackage pkg)
    {
        bool isSelected = _selectedId == pkg.Id;
        Color cardBg = isSelected ? EditorTheme.Purple400 : EditorTheme.Neutral400;
        Color cardHoverBg = isSelected ? EditorTheme.Purple500 : EditorTheme.Neutral500;
        Color nameColor = EditorTheme.Ink400;
        Color dimColor = isSelected ? EditorTheme.Ink300 : EditorTheme.Ink300;

        string pkgId = pkg.Id;

        using (paper.Column($"mkt_card_{index}")
            .Width(ListPaneWidth - 12f)
            .Height(CardHeight)
            .BackgroundColor(cardBg)
            .Hovered.BackgroundColor(cardHoverBg).End()
            .Rounded(4f)
            .ChildLeft(10f).ChildRight(10f).ChildTop(8f).ChildBottom(8f)
            .ColBetween(3f)
            .OnClick(pkgId, (id, _) => _selectedId = id)
            .Enter())
        {
            // Name
            paper.Box($"mkt_card_{index}_name")
                .Height(18f)
                .Text(pkg.Name, font)
                .TextColor(nameColor)
                .FontSize(13f)
                .Alignment(TextAlignment.MiddleLeft);

            // Category + version row
            using (paper.Row($"mkt_card_{index}_meta")
                .Height(15f)
                .RowBetween(6f)
                .Enter())
            {
                Color catColor = GetCategoryColor(pkg.Category);

                paper.Box($"mkt_card_{index}_cat")
                    .Height(15f).Width(UnitValue.Auto)
                    .Text(CapitalizeFirst(pkg.Category), font)
                    .TextColor(catColor)
                    .FontSize(10f)
                    .Alignment(TextAlignment.MiddleLeft);

                if (pkg.LatestVersion != null)
                {
                    paper.Box($"mkt_card_{index}_ver")
                        .Height(15f).Width(UnitValue.Auto)
                        .Text($"v{pkg.LatestVersion.Version}", font)
                        .TextColor(dimColor)
                        .FontSize(10f)
                        .Alignment(TextAlignment.MiddleLeft);
                }
            }

            // Description (clipped)
            paper.Box($"mkt_card_{index}_desc")
                .Height(20f)
                .Text(pkg.Description, font)
                .TextColor(dimColor)
                .FontSize(11f)
                .Alignment(TextAlignment.MiddleLeft)
                .Clip();
        }
    }

    private void DrawPackageDetail(Paper paper, FontFile font, float width, float height)
    {
        ProwlPackage? pkg = _selectedId != null
            ? _packages.FirstOrDefault(p => p.Id == _selectedId)
            : null;

        if (pkg == null)
        {
            paper.Box("mkt_detail_empty").Size(width, height)
                .Text("Select a package to see details.", font)
                .TextColor(EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSize)
                .Alignment(TextAlignment.MiddleCenter);
            return;
        }

        PackageVersion? ver = pkg.LatestVersion;

        Origami.ScrollView(paper, "mkt_detail", width, height)
            .Padding(18f, 18f, 16f, 16f)
            .ColSpacing(8f)
            .Body(() => DrawDetailContent(paper, font, pkg, ver, width - 36f));
    }

    private void DrawDetailContent(Paper paper, FontFile font, ProwlPackage pkg, PackageVersion? ver, float contentWidth)
    {
        // Name
        paper.Box("mkt_d_name")
            .Height(26f)
            .Text(pkg.Name, font)
            .TextColor(EditorTheme.Ink500)
            .FontSize(17f)
            .Alignment(TextAlignment.MiddleLeft);

        // Category + version badge row
        using (paper.Row("mkt_d_badges")
            .Height(20f)
            .RowBetween(8f)
            .Enter())
        {
            Color catColor = GetCategoryColor(pkg.Category);

            paper.Box("mkt_d_cat")
                .Height(20f).Width(UnitValue.Auto)
                .BackgroundColor(Color.FromArgb(40, catColor.R, catColor.G, catColor.B))
                .Rounded(3f)
                .Text($"  {CapitalizeFirst(pkg.Category)}  ", font)
                .TextColor(catColor)
                .FontSize(11f)
                .Alignment(TextAlignment.MiddleCenter);

            if (ver != null)
            {
                paper.Box("mkt_d_ver")
                    .Height(20f).Width(UnitValue.Auto)
                    .Text($"v{ver.Version}", font)
                    .TextColor(EditorTheme.Ink300)
                    .FontSize(11f)
                    .Alignment(TextAlignment.MiddleLeft);
            }
        }

        // Separator
        paper.Box("mkt_d_sep1").Height(1f).BackgroundColor(EditorTheme.Ink200);

        // Description
        paper.Box("mkt_d_desc")
            .Width(contentWidth)
            .Height(UnitValue.Auto)
            .MinHeight(40f)
            .Text(pkg.Description, font)
            .TextColor(EditorTheme.Ink400)
            .FontSize(EditorTheme.FontSize)
            .Alignment(TextAlignment.Left)
            .Wrap(TextWrapMode.Wrap);

        // Tags
        if (!string.IsNullOrWhiteSpace(pkg.Tags))
        {
            string[] tags = pkg.Tags.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);
            if (tags.Length > 0)
            {
                paper.Box("mkt_d_tags_lbl")
                    .Height(16f)
                    .Text("Tags", font)
                    .TextColor(EditorTheme.Ink300)
                    .FontSize(11f)
                    .Alignment(TextAlignment.MiddleLeft);

                using (paper.Row("mkt_d_tags_row")
                    .Height(UnitValue.Auto)
                    .MinHeight(22f)
                    .RowBetween(4f)
                    .Enter())
                {
                    for (int t = 0; t < tags.Length; t++)
                    {
                        paper.Box($"mkt_d_tag_{t}")
                            .Height(20f).Width(UnitValue.Auto)
                            .BackgroundColor(EditorTheme.Neutral500)
                            .Rounded(3f)
                            .Text($"  {tags[t]}  ", font)
                            .TextColor(EditorTheme.Ink400)
                            .FontSize(10f)
                            .Alignment(TextAlignment.MiddleCenter);
                    }
                }
            }
        }

        // Stats
        if (ver != null)
        {
            paper.Box("mkt_d_sep2").Height(1f).BackgroundColor(EditorTheme.Ink200);

            using (paper.Row("mkt_d_stats")
                .Height(16f)
                .RowBetween(20f)
                .Enter())
            {
                DrawStatLabel(paper, font, "mkt_d_size",
                    $"{EditorIcons.Download}  {FormatBytes(ver.FileSize)}");
                DrawStatLabel(paper, font, "mkt_d_dl",
                    $"{EditorIcons.ArrowDown}  {ver.DownloadCount:N0} downloads");
            }

            // Release notes
            if (!string.IsNullOrWhiteSpace(ver.ReleaseNotes))
            {
                paper.Box("mkt_d_relnotes_lbl")
                    .Height(16f)
                    .Text("Release notes", font)
                    .TextColor(EditorTheme.Ink300)
                    .FontSize(11f)
                    .Alignment(TextAlignment.MiddleLeft);

                paper.Box("mkt_d_relnotes")
                    .Width(contentWidth)
                    .Height(UnitValue.Auto)
                    .MinHeight(20f)
                    .Text(ver.ReleaseNotes, font)
                    .TextColor(EditorTheme.Ink300)
                    .FontSize(11f)
                    .Alignment(TextAlignment.Left)
                    .Wrap(TextWrapMode.Wrap);
            }
        }

        // Import button
        paper.Box("mkt_d_btnsep").Height(8f);

        if (ver != null)
        {
            using (paper.Row("mkt_d_btn_row").Height(32f).RowBetween(8f).Enter())
            {
                if (ProwlService.IsSignedIn)
                {
                    EditorGUI.Button(paper, "mkt_d_import", $"{EditorIcons.Download}  Import Package", width: 150)
                        .OnValueChanged(clicked => OpenImportDialog(pkg, ver));
                }
                else
                {
                    string signInLabel = ProwlService.IsSigningIn ? "Signing in..." : $"{EditorIcons.ArrowRightToBracket}  Sign in to Import";
                    EditorGUI.Button(paper, "mkt_d_signin_import", signInLabel, width: 160)
                        .OnValueChanged(clicked =>
                        {
                            if (!ProwlService.IsSigningIn)
                                _ = ProwlService.SignInWithGitHubAsync();
                        });
                }
            }
        }
        else
        {
            paper.Box("mkt_d_nover")
                .Height(20f)
                .Text("No versions available.", font)
                .TextColor(EditorTheme.Ink300)
                .FontSize(11f)
                .Alignment(TextAlignment.MiddleLeft);
        }
    }

    private static void DrawStatLabel(Paper paper, FontFile font, string id, string text)
    {
        paper.Box(id)
            .Height(16f).Width(UnitValue.Auto)
            .Text(text, font)
            .TextColor(EditorTheme.Ink300)
            .FontSize(11f)
            .Alignment(TextAlignment.MiddleLeft);
    }

    private void OpenImportDialog(ProwlPackage pkg, PackageVersion ver)
    {
        _importPackage = pkg;
        _importVersion = ver;
        _importTargetFolder = "";
        _isImporting = false;
        _importStatusText = "";
        _folderOpenState.Clear();

        ModalDialog.Show(
            new ModalDialogEntry("Import Package", DrawImportDialogContent, width: 580, height: 460)
                .Button("Cancel", ModalDialog.Close)
                .Button("Import", () => _ = DoImportAsync()));
    }

    private void DrawImportDialogContent(Paper paper)
    {
        FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;

        const float folderPaneWidth = 200f;
        const float bodyHeight = 340f;

        // Package summary header
        if (_importPackage != null)
        {
            using (paper.Row("imp_header")
                .Height(36f)
                .RowBetween(10f)
                .ChildTop(4f).ChildBottom(8f)
                .Enter())
            {
                paper.Box("imp_hdr_name")
                    .Height(28f).Width(UnitValue.Auto)
                    .Text(_importPackage.Name, font)
                    .TextColor(EditorTheme.Ink500)
                    .FontSize(14f)
                    .Alignment(TextAlignment.MiddleLeft);

                if (_importVersion != null)
                {
                    paper.Box("imp_hdr_ver")
                        .Height(28f).Width(UnitValue.Auto)
                        .Text($"v{_importVersion.Version}", font)
                        .TextColor(EditorTheme.Ink300)
                        .FontSize(12f)
                        .Alignment(TextAlignment.MiddleLeft);

                    paper.Box("imp_hdr_size")
                        .Height(28f).Width(UnitValue.Auto)
                        .Text(FormatBytes(_importVersion.FileSize), font)
                        .TextColor(EditorTheme.Ink300)
                        .FontSize(12f)
                        .Alignment(TextAlignment.MiddleLeft);
                }
            }
        }

        paper.Box("imp_sep1").Height(1f).BackgroundColor(EditorTheme.Ink200);

        // Body: folder tree | destination summary
        using (paper.Row("imp_body").Height(bodyHeight).RowBetween(0f).Enter())
        {
            // Folder tree for picking the destination
            using (paper.Column("imp_left")
                .Width(folderPaneWidth)
                .Height(bodyHeight)
                .BackgroundColor(EditorTheme.Neutral200)
                .Enter())
            {
                paper.Box("imp_left_hdr")
                    .Height(22f)
                    .BackgroundColor(EditorTheme.Neutral300)
                    .Text("Select destination folder", font)
                    .TextColor(EditorTheme.Ink300)
                    .FontSize(10f)
                    .Alignment(TextAlignment.MiddleCenter);

                float treeH = bodyHeight - 22f;

                if (Project.Current == null)
                {
                    paper.Box("imp_noproject").Width(folderPaneWidth).Height(treeH)
                        .Text("No project loaded.", font)
                        .TextColor(EditorTheme.Ink300)
                        .FontSize(EditorTheme.FontSize)
                        .Alignment(TextAlignment.MiddleCenter);
                }
                else
                {
                    Origami.ScrollView(paper, "imp_tree_scroll", folderPaneWidth, treeH)
                        .Padding(4f, 4f, 4f, 4f)
                        .Body(() =>
                        {
                            using (paper.Column("imp_tree_inner")
                                .Width(folderPaneWidth - 8f)
                                .Height(UnitValue.Auto)
                                .Enter())
                            {
                                DrawImportFolderNode(paper, font,
                                    Project.Current.AssetsPath, "Assets", 0);
                            }
                        });
                }
            }

            // Vertical divider
            paper.Box("imp_vdiv").Width(1f).Height(bodyHeight).BackgroundColor(EditorTheme.Ink200);

            // Summary of where the package will be extracted
            float rightW = 580f - 16f - folderPaneWidth - 1f; // modal content width - folder pane - divider
            using (paper.Column("imp_right")
                .Width(rightW)
                .Height(bodyHeight)
                .ChildLeft(14f).ChildRight(14f).ChildTop(12f).ChildBottom(12f)
                .ColBetween(8f)
                .Enter())
            {
                paper.Box("imp_r_lbl1")
                    .Height(14f)
                    .Text("Destination", font)
                    .TextColor(EditorTheme.Ink300)
                    .FontSize(10f)
                    .Alignment(TextAlignment.MiddleLeft);

                string dest = string.IsNullOrEmpty(_importTargetFolder)
                    ? "Assets  (root)"
                    : $"Assets/{_importTargetFolder}";

                paper.Box("imp_r_dest")
                    .Height(22f)
                    .BackgroundColor(EditorTheme.Neutral300)
                    .Rounded(3f)
                    .Text(dest, font)
                    .TextColor(EditorTheme.Ink400)
                    .FontSize(EditorTheme.FontSize)
                    .Alignment(TextAlignment.MiddleLeft);

                paper.Box("imp_r_sep").Height(1f).BackgroundColor(EditorTheme.Ink200);

                // What will be imported
                paper.Box("imp_r_lbl2")
                    .Height(14f)
                    .Text("What happens", font)
                    .TextColor(EditorTheme.Ink300)
                    .FontSize(10f)
                    .Alignment(TextAlignment.MiddleLeft);

                string whatHappens = _importVersion != null
                    ? $"The package archive ({_importVersion.FileName}) will be downloaded and its contents extracted to {dest}."
                    : "Package contents will be extracted to the selected folder.";

                paper.Box("imp_r_what")
                    .Width(rightW - 28f)
                    .Height(UnitValue.Auto)
                    .MinHeight(44f)
                    .Text(whatHappens, font)
                    .TextColor(EditorTheme.Ink300)
                    .FontSize(11f)
                    .Alignment(TextAlignment.Left)
                    .Wrap(TextWrapMode.Wrap);

                if (_isImporting)
                {
                    paper.Box("imp_r_status")
                        .Height(16f)
                        .Text(_importStatusText, font)
                        .TextColor(EditorTheme.Ink300)
                        .FontSize(11f)
                        .Alignment(TextAlignment.MiddleLeft);
                }
            }
        }
    }

    private void DrawImportFolderNode(Paper paper, FontFile font, string absolutePath, string displayName, int depth)
    {
        if (Project.Current == null) return;

        string relPath = absolutePath == Project.Current.AssetsPath
            ? ""
            : Path.GetRelativePath(Project.Current.AssetsPath, absolutePath).Replace('\\', '/');

        bool isSelected = _importTargetFolder == relPath;

        string[] subDirs;
        try
        {
            subDirs = Directory.GetDirectories(absolutePath)
                .Where(d => !Path.GetFileName(d).StartsWith('.') && Path.GetFileName(d) != "__MACOSX")
                .OrderBy(d => d)
                .ToArray();
        }
        catch { subDirs = []; }

        bool hasChildren = subDirs.Length > 0;
        string stateKey = $"imp_fo_{relPath}";
        bool isOpen = _folderOpenState.GetValueOrDefault(stateKey, depth < 1);

        float indent = depth * 14f;

        using (paper.Row($"imp_fn_{Math.Abs(relPath.GetHashCode())}")
            .Height(22f)
            .BackgroundColor(isSelected ? EditorTheme.Purple400 : Color.Transparent)
            .Hovered.BackgroundColor(isSelected ? EditorTheme.Purple500 : EditorTheme.Ink200).End()
            .Rounded(3f)
            .ChildLeft(indent + 2f)
            .ChildRight(2f)
            .OnClick(relPath, (path, _) => _importTargetFolder = path)
            .Enter())
        {
            // Expand/collapse arrow
            if (hasChildren)
            {
                EditorGUI.ButtonSquareGhost(paper, $"imp_arr_{Math.Abs(relPath.GetHashCode())}",
                        isOpen ? EditorIcons.AngleDown : EditorIcons.AngleRight)
                    .OnValueChanged(clicked => _folderOpenState[stateKey] = !isOpen);
            }
            else
            {
                paper.Box($"imp_arr_pad_{Math.Abs(relPath.GetHashCode())}").Width(22f);
            }

            // Folder icon
            paper.Box($"imp_fn_ico_{Math.Abs(relPath.GetHashCode())}")
                .Width(16f).Height(22f)
                .Text(EditorIcons.Folder, font)
                .TextColor(isSelected ? EditorTheme.Ink400 : EditorTheme.Ink300)
                .FontSize(11f)
                .Alignment(TextAlignment.MiddleCenter);

            // Folder name
            paper.Box($"imp_fn_name_{Math.Abs(relPath.GetHashCode())}")
                .Height(22f)
                .Text(displayName, font)
                .TextColor(isSelected ? EditorTheme.Ink500 : EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSize)
                .Alignment(TextAlignment.MiddleLeft);
        }

        if (isOpen)
        {
            foreach (string subDir in subDirs)
                DrawImportFolderNode(paper, font, subDir, Path.GetFileName(subDir), depth + 1);
        }
    }

    private async Task DoImportAsync()
    {
        if (_importPackage == null || _importVersion == null || Project.Current == null) return;

        _isImporting = true;
        _importStatusText = "Downloading...";

        string tempFile = Path.Combine(Path.GetTempPath(), $"prowl_pkg_{Guid.NewGuid():N}.zip");

        try
        {
            string? downloadUrl = ProwlService.GetPackagePublicUrl(_importPackage.Id, _importVersion.FilePath);
            if (string.IsNullOrEmpty(downloadUrl))
                throw new Exception("Could not resolve package download URL.");

            string destPath = string.IsNullOrEmpty(_importTargetFolder)
                ? Project.Current.AssetsPath
                : Path.Combine(Project.Current.AssetsPath, _importTargetFolder);

            using (var http = new HttpClient())
            {
                byte[] bytes = await http.GetByteArrayAsync(downloadUrl);
                File.WriteAllBytes(tempFile, bytes);
            }

            _importStatusText = "Extracting...";
            ZipFile.ExtractToDirectory(tempFile, destPath, overwriteFiles: true);

            // macOS zips bake in a __MACOSX metadata folder — remove it
            string macosxDir = Path.Combine(destPath, "__MACOSX");
            if (Directory.Exists(macosxDir))
                Directory.Delete(macosxDir, recursive: true);

            Runtime.Debug.LogSuccess($"[Marketplace] Imported '{_importPackage.Name}' v{_importVersion.Version} → {destPath}");
            ModalDialog.Close();
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogError($"[Marketplace] Import failed: {ex.Message}");
            _importStatusText = $"Error: {ex.Message}";
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
            _isImporting = false;
        }
    }

    private async Task LoadAsync()
    {
        _isLoading = true;
        _loadError = null;
        try
        {
            _packages = await ProwlService.FetchPackagesAsync();
        }
        catch (Exception ex)
        {
            _loadError = ex.Message;
            _packages = [];
        }
        finally
        {
            _isLoading = false;
        }
    }

    private List<ProwlPackage> FilteredPackages()
    {
        IEnumerable<ProwlPackage> result = _packages;

        if (_activeCategory != "all")
            result = result.Where(p => p.Category == _activeCategory);

        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            string q = _searchText.Trim().ToLowerInvariant();
            result = result.Where(p =>
                p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                p.Description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                p.Tags.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        return result.ToList();
    }

    private static Color GetCategoryColor(string category) => category switch
    {
        "shader"   => EditorTheme.Purple400,
        "script"   => EditorTheme.Blue400,
        "asset"    => Color.FromArgb(255, 61, 122, 87),
        "template" => Color.FromArgb(255, 180, 130, 50),
        _          => EditorTheme.Ink300,
    };

    private static string CapitalizeFirst(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "—";
        string[] suffixes = ["B", "KB", "MB", "GB"];
        int order = Math.Min((int)(Math.Log(bytes) / Math.Log(1024)), suffixes.Length - 1);
        double value = bytes / Math.Pow(1024, order);
        return $"{value:0.##} {suffixes[order]}";
    }
}
