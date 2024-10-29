// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Text;
using System.Text.Json;

using Prowl.Editor.Assets;
using Prowl.Editor.Preferences;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.Rendering;

using SemVersion;

namespace Prowl.Editor;

public class PackageManagerWindow : EditorWindow
{
    protected override double Width { get; } = 512 + (512 / 2);
    protected override double Height { get; } = 512;

    //private string Error = "";
    //
    //private enum Page { Installed, AllPackages }
    //private Page _currentPage = Page.Installed;
    //
    //// Selected Package
    //bool _showProjectDetails;
    //SemanticVersion[] _projectVersions = [];
    //int _selectedVersionIndex;
    //GithubPackageMetaData _selectedPackageMetaData;
    //
    //byte loadingPackageEntries;
    //bool loadingDetails;
    //string package_Title, package_Author, package_Description, package_Version, package_ProjectURL, package_License;
    //SemanticVersion? package_InstalledVersion;
    //Texture2D? package_Icon;
    //List<(string, string)> package_Dependencies;
    //GithubPackageMetaData? _metadata;
    //
    //readonly List<(GithubPackageMetaData, Texture2D?)> PackageEntries = [];
    //
    //string _searchText;
    //
    //Dictionary<string, string> _installedPackages = [];
    //
    //Dictionary<string, GithubPackageMetaData> _metaCache = [];
    //
    //public PackageManagerWindow() : base()
    //{
    //    Title = FontAwesome6.BoxesPacking + " PackageManager";
    //
    //    DirectoryInfo packagesPath = Project.Active!.PackagesDirectory;
    //    string packagesJsonPath = Path.Combine(packagesPath.FullName, "Packages.json");
    //
    //    // Load Packages.json
    //    _installedPackages = [];
    //    if (File.Exists(packagesJsonPath))
    //    {
    //        try
    //        {
    //            _installedPackages = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(packagesJsonPath)) ?? [];
    //        }
    //        catch (Exception ex)
    //        {
    //            Debug.LogError($"Failed to parse Packages.json: {ex.Message}");
    //            return;
    //        }
    //    }
    //    else
    //    {
    //        Debug.LogWarning("Packages.json not found, creating new file...");
    //    }
    //
    //    PopulateListWith(_installedPackages.Keys);
    //}

    protected override void Draw()
    {
    //    gui.CurrentNode.Layout(LayoutType.Row);
    //    gui.CurrentNode.ScaleChildren();
    //
    //    using (gui.Node("PackageFeeds").ExpandHeight().MaxWidth(200).Padding(10).Layout(LayoutType.Column).Spacing(10).Enter())
    //    {
    //        EditorGUI.Text("Sources:");
    //        if (EditorGUI.StyledButton("Installed"))
    //        {
    //            _searchText = "";
    //            _currentPage = Page.Installed;
    //            PopulateListWith(AssetDatabase.GetInstalledPackages());
    //        }
    //
    //        if (EditorGUI.StyledButton("All Packages"))
    //        {
    //            _searchText = "";
    //            _currentPage = Page.AllPackages;
    //            PopulateListWithSearchResults("");
    //        }
    //    }
    //
    //    using (gui.Node("PackageList").ExpandHeight().ExpandWidth().Layout(LayoutType.Column).ScaleChildren().Enter())
    //    {
    //        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.WindowBGTwo);
    //        DrawPackageList();
    //    }
    //
    //    using (gui.Node("PackageDetails").ExpandHeight().ExpandWidth().Layout(LayoutType.Column, false).Clip().Scroll().Enter())
    //    {
    //        DrawPackageDetails();
    //    }
    }

    //private void DrawPackageList()
    //{
    //    using (gui.Node("Header").ExpandWidth().MaxHeight(75).Layout(LayoutType.Column, false).Padding(5).Enter())
    //    {
    //        EditorGUI.Text(_currentPage.ToString());
    //        bool update = gui.Search("SearchInput", ref _searchText, 0, 0, Size.Percentage(1f), EditorStylePrefs.Instance.ItemSize);
    //
    //        gui.Draw2D.DrawText("Include Prerelease", gui.CurrentNode.LayoutData.GlobalContentPosition + new Vector2(35, 50), Color.white * 0.9f);
    //        if (gui.Checkbox("prerelease", ref PackageManagerPreferences.Instance.IncludePrerelease, 0, 0, out _))
    //        {
    //            PackageManagerPreferences.Instance.Save();
    //            update = true;
    //        }
    //
    //        if (update)
    //        {
    //            PopulateListWithSearchResults(_searchText);
    //        }
    //    }
    //
    //    if (loadingPackageEntries != 0)
    //    {
    //        gui.Draw2D.LoadingIndicatorCircle(gui.CurrentNode.LayoutData.InnerRect.Center, 25, Color.white, Color.white, 12, 1f);
    //        return;
    //    }
    //
    //    if (Error != "")
    //    {
    //        gui.Draw2D.DrawText(Error, gui.CurrentNode.LayoutData.InnerRect);
    //        return;
    //    }
    //
    //    if (PackageEntries.Count == 0)
    //    {
    //        gui.Draw2D.DrawText("No Packages Found", gui.CurrentNode.LayoutData.InnerRect);
    //        return;
    //    }
    //
    //    using (gui.Node("List").ExpandHeight().ExpandWidth().Layout(LayoutType.Column).Spacing(10).Padding(5).Scroll().Clip().Enter())
    //    {
    //        for (int i = 0; i < PackageEntries.Count; i++)
    //        {
    //            (GithubPackageMetaData, Texture2D?) entry = PackageEntries[i];
    //            using (gui.Node("PackageEntry", i).ExpandWidth().Height(64).Clip().Enter())
    //            {
    //                if (gui.IsNodePressed())
    //                    ClickPackage(entry.Item1.repository.githubPath);
    //
    //                if (gui.IsNodeHovered())
    //                    gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Hovering);
    //
    //                var startpos = gui.CurrentNode.LayoutData.GlobalPosition;
    //                var pos = gui.CurrentNode.LayoutData.GlobalPosition;
    //                if (entry.Item2 != null)
    //                {
    //                    var width = gui.CurrentNode.LayoutData.GlobalContentHeight;
    //                    gui.Draw2D.DrawImage(entry.Item2, new Rect(pos.x, pos.y, width, width));
    //                    pos.x += width + 5;
    //                }
    //
    //                gui.Draw2D.DrawText(entry.Item1.name, pos);
    //                pos.y += 20;
    //                gui.Draw2D.DrawText(entry.Item1.description, 17, pos, Color.white * 0.9f, gui.CurrentNode.LayoutData.GlobalContentWidth - (pos.x - startpos.x));
    //            }
    //        }
    //    }
    //}
    //
    //private void DrawPackageDetails()
    //{
    //    if (loadingDetails)
    //    {
    //        gui.Draw2D.LoadingIndicatorCircle(gui.CurrentNode.LayoutData.InnerRect.Center, 25, Color.white, Color.white, 12, 1f);
    //        return;
    //    }
    //
    //    if (!_showProjectDetails)
    //        return;
    //
    //    var width = gui.CurrentNode.LayoutData.GlobalContentWidth;
    //    var height = gui.CurrentNode.LayoutData.GlobalContentHeight;
    //
    //    if (package_Icon != null)
    //    {
    //        using (gui.Node("PackageIcon").Left(width / 4).Scale(width / 2).Enter())
    //        {
    //            gui.Draw2D.DrawImage(package_Icon, gui.CurrentNode.LayoutData.InnerRect);
    //        }
    //        height -= width / 2;
    //    }
    //
    //    using (gui.Node("PackageDetails").Height(height).Width(width).Layout(LayoutType.Column).Scroll().Clip().Padding(5).Spacing(5).Enter())
    //    {
    //        StringBuilder sb = new();
    //
    //        var itemSize = EditorStylePrefs.Instance.ItemSize;
    //        var installWidth = width - 75 - 10 - 10;
    //        using (gui.Node("Installed").Height(itemSize).ExpandWidth().Layout(LayoutType.Row).Enter())
    //        {
    //            if (package_InstalledVersion is not null)
    //            {
    //                if (gui.Combo("Version", "VersionPopup", ref _selectedVersionIndex, _projectVersions.Select(x => x.ToString()).ToArray(), 0, 0, 75, itemSize))
    //                    PopulateDetails(_projectVersions[_selectedVersionIndex]);
    //
    //                bool canUpdate = _projectVersions[_selectedVersionIndex] != package_InstalledVersion;
    //                if (canUpdate && EditorGUI.StyledButton("Update", installWidth / 2, itemSize))
    //                {
    //                    //AssetDatabase.UninstallPackage(_metadata.repository);
    //                    AssetDatabase.InstallPackage(_metadata.repository.githubPath, _projectVersions[_selectedVersionIndex].ToString());
    //                }
    //
    //                if (EditorGUI.StyledButton("Uninstall", canUpdate ? installWidth / 2 : installWidth, itemSize))
    //                    AssetDatabase.UninstallPackage(_metadata.repository.githubPath);
    //
    //                sb.AppendLine("==========");
    //                sb.AppendLine("Installed Version: " + package_InstalledVersion);
    //                sb.AppendLine("==========");
    //            }
    //            else
    //            {
    //                if (gui.Combo("Version", "VersionPopup", ref _selectedVersionIndex, _projectVersions.Select(x => x.ToString()).ToArray(), 0, 0, 75, itemSize))
    //                    PopulateDetails(_projectVersions[_selectedVersionIndex]);
    //                if (EditorGUI.StyledButton("Install", installWidth, itemSize))
    //                    AssetDatabase.InstallPackage(_metadata.repository.githubPath, _projectVersions[_selectedVersionIndex].ToString());
    //            }
    //        }
    //
    //        sb.AppendLine("Title: " + package_Title);
    //        sb.AppendLine("Description:");
    //        sb.AppendLine(package_Description);
    //        sb.AppendLine("");
    //        sb.AppendLine("Author: " + package_Author);
    //        //sb.AppendLine("Downloads: " + package_Downloads);
    //        sb.AppendLine("Version: " + package_Version);
    //
    //        EditorGUI.TextSimple(sb.ToString());
    //
    //        if (EditorGUI.StyledButton("Project Website"))
    //            System.Diagnostics.Process.Start("explorer", package_ProjectURL.ToString());
    //
    //        if (EditorGUI.StyledButton("License"))
    //            System.Diagnostics.Process.Start("explorer", package_License.ToString());
    //
    //
    //        if (package_Dependencies.Count > 0)
    //        {
    //            StringBuilder sb2 = new();
    //            sb2.AppendLine("");
    //            sb2.AppendLine("Dependencies:");
    //            for (int i = 0; i < package_Dependencies.Count; i++)
    //                sb2.AppendLine(package_Dependencies[i].Item1 + " " + package_Dependencies[i].Item2);
    //            EditorGUI.TextSimple(sb2.ToString());
    //        }
    //    }
    //}
    //
    //private async void PopulateListWithSearchResults(string search)
    //{
    //    loadingPackageEntries++;
    //    PackageEntries.Clear();
    //    try
    //    {
    //        PopulateListWith(["pfraces-graveyard/git-install"]);
    //    }
    //    catch (Exception ex)
    //    {
    //        Error = "Error: " + ex.Message;
    //    }
    //    loadingPackageEntries--;
    //}
    //
    //private async void PopulateListWith(IEnumerable<string> repoList)
    //{
    //    loadingPackageEntries++;
    //    foreach (var entry in PackageEntries)
    //        entry.Item2?.DestroyImmediate();
    //
    //    PackageEntries.Clear();
    //    foreach (var result in searchresults)
    //    {
    //        PackageEntries.Add((result, await DownloadIcon(result.iconurl ?? "")));
    //    }
    //    loadingPackageEntries--;
    //}
    //
    //private async Task<Texture2D?> DownloadIcon(string url)
    //{
    //    if (string.IsNullOrWhiteSpace(url))
    //        return null;
    //
    //    try
    //    {
    //        HttpClient wc = new HttpClient();
    //        byte[] bytes = await wc.GetByteArrayAsync(url);
    //        MemoryStream ms = new MemoryStream(bytes);
    //        return Texture2DLoader.FromStream(ms);
    //    }
    //    catch (Exception)
    //    {
    //        return null;
    //    }
    //}
    //
    //private async void ClickPackage(string packageId)
    //{
    //    loadingDetails = true;
    //    Error = "";
    //    try
    //    {
    //        List<SemanticVersion> semVers = await AssetDatabase.GetVersions(packageId);
    //        _projectVersions = [.. semVers.OrderByDescending(x => x)];
    //
    //        _metadata = await AssetDatabase.GetDetails(packageId);
    //
    //        _selectedVersionIndex = 0;
    //        PopulateDetails(_projectVersions[_selectedVersionIndex]);
    //
    //        _showProjectDetails = true;
    //    }
    //    catch (Exception)
    //    {
    //        _showProjectDetails = false;
    //    }
    //    loadingDetails = false;
    //}
    //
    //private async void PopulateDetails(SemanticVersion version)
    //{
    //    package_Version = version.ToString();
    //    package_Dependencies = [];
    //
    //    package_Icon?.DestroyImmediate();
    //
    //    if (_metadata != null)
    //    {
    //        package_Icon = await DownloadIcon(_metadata.iconurl ?? "");
    //
    //        package_Title = _metadata.name ?? "undefined";
    //        package_Author = _metadata.author ?? "undefined";
    //        package_Description = _metadata.description ?? "undefined";
    //        package_ProjectURL = _metadata.homepage ?? "undefined";
    //        package_License = _metadata.license ?? "undefined";
    //
    //        foreach (KeyValuePair<string, string> dependency in _metadata.dependencies)
    //                package_Dependencies.Add((dependency.Key, dependency.Value ?? "*"));
    //
    //        package_InstalledVersion = await AssetDatabase.GetInstalledVersion(_metadata.repository.githubPath);
    //    }
    //    else
    //    {
    //        package_Icon = null;
    //
    //        package_Title = "Unknown";
    //        package_Author = "Unknown";
    //        package_Description = "Unknown";
    //        package_ProjectURL = "Unknown";
    //        package_License = "Unknown";
    //    }
    //}
}
