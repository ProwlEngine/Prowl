// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Net;
using System.Text;

using NuGet.Protocol.Core.Types;

using Prowl.Editor.Assets;
using Prowl.Editor.Preferences;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;

namespace Prowl.Editor;

public class PackageManagerWindow : EditorWindow
{
    protected override double Width { get; } = 512 + (512 / 2);
    protected override double Height { get; } = 512;

    private string Error = "";

    private enum Page { Installed, AllPackages, Source }
    private Page _currentPage = Page.Installed;
    private (string, string) _currentSource;

    // Selected Package
    bool _showProjectDetails;
    string[] _projectVersions = [];
    int _selectedVersionIndex;
    List<IPackageSearchMetadata> _selectedPackageMetaData = [];

    byte loadingPackageEntries;
    bool loadingDetails;
    string package_Title, package_Authors, package_Description, package_Downloads, package_Version, package_PublishDate, package_ProjectURL, package_LicenseURL;
    Texture2D? package_Icon;
    List<(string, string)> package_Dependencies;
    IPackageSearchMetadata? _metadata;

    readonly List<(IPackageSearchMetadata, Texture2D?)> PackageEntries = [];

    string _searchText;

    public PackageManagerWindow() : base()
    {
        Title = FontAwesome6.BoxesPacking + " PackageManager";
        PopulateListWith(AssetDatabase.Packages);
    }

    protected override void Draw()
    {
        gui.CurrentNode.Layout(LayoutType.Row);
        gui.CurrentNode.ScaleChildren();

        using (gui.Node("PackageFeeds").ExpandHeight().MaxWidth(200).Padding(10).Layout(LayoutType.Column).Spacing(10).Enter())
        {
            EditorGUI.Text("Sources:");
            if (EditorGUI.StyledButton("Installed"))
            {
                _searchText = "";
                _currentPage = Page.Installed;
                PopulateListWith(AssetDatabase.Packages);
            }

            if (EditorGUI.StyledButton("All Packages"))
            {
                _searchText = "";
                _currentPage = Page.AllPackages;
                PopulateListWithSearchResults("");
            }

            EditorGUI.Text("---");

            foreach (var source in PackageManagerPreferences.Instance.Sources)
            {
                if (source.IsEnabled)
                {
                    if (EditorGUI.StyledButton(source.Name))
                    {
                        _searchText = "";
                        _currentPage = Page.Source;
                        _currentSource = (source.Name, source.Source);
                        PopulateListWithSearchResults("", source.Source);
                    }
                }
            }
        }

        using (gui.Node("PackageList").ExpandHeight().ExpandWidth().Layout(LayoutType.Column).ScaleChildren().Enter())
        {
            gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.WindowBGTwo);
            DrawPackageList();
        }

        using (gui.Node("PackageDetails").ExpandHeight().ExpandWidth().Layout(LayoutType.Column, false).Clip().Scroll().Enter())
        {
            DrawPackageDetails();
        }
    }

    private void DrawPackageList()
    {
        using (gui.Node("Header").ExpandWidth().MaxHeight(75).Layout(LayoutType.Column, false).Padding(5).Enter())
        {
            EditorGUI.Text(_currentPage.ToString());
            var update = gui.Search("SearchInput", ref _searchText, 0, 0, Size.Percentage(1f), EditorStylePrefs.Instance.ItemSize);

            gui.Draw2D.DrawText("Include Prerelease", gui.CurrentNode.LayoutData.GlobalContentPosition + new Vector2(35, 50), Color.white * 0.9f);
            if (gui.Checkbox("prerelease", ref PackageManagerPreferences.Instance.IncludePrerelease, 0, 0, out _))
            {
                PackageManagerPreferences.Instance.Save();
                update = true;
            }

            if (update)
            {
                if (_currentPage == Page.Source)
                    PopulateListWithSearchResults(_searchText, _currentSource.Item2);
                else
                    PopulateListWithSearchResults(_searchText);
            }
        }

        if (loadingPackageEntries != 0)
        {
            gui.Draw2D.LoadingIndicatorCircle(gui.CurrentNode.LayoutData.InnerRect.Center, 25, Color.white, Color.white, 12, 1f);
            return;
        }

        if (Error != "")
        {
            gui.Draw2D.DrawText(Error, gui.CurrentNode.LayoutData.InnerRect);
            return;
        }

        if (PackageEntries.Count == 0)
        {
            gui.Draw2D.DrawText("No Packages Found", gui.CurrentNode.LayoutData.InnerRect);
            return;
        }

        using (gui.Node("List").ExpandHeight().ExpandWidth().Layout(LayoutType.Column).Spacing(10).Padding(5).Scroll().Clip().Enter())
        {
            for (int i = 0; i < PackageEntries.Count; i++)
            {
                var entry = PackageEntries[i];
                using (gui.Node("PackageEntry", i).ExpandWidth().Height(64).Clip().Enter())
                {
                    if (gui.IsNodePressed())
                        ClickPackage(entry.Item1.Identity.Id);

                    if (gui.IsNodeHovered())
                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Hovering);

                    var startpos = gui.CurrentNode.LayoutData.GlobalPosition;
                    var pos = gui.CurrentNode.LayoutData.GlobalPosition;
                    if (entry.Item2 != null)
                    {
                        var width = gui.CurrentNode.LayoutData.GlobalContentHeight;
                        gui.Draw2D.DrawImage(entry.Item2, new Rect(pos.x, pos.y, width, width));
                        pos.x += width + 5;
                    }

                    gui.Draw2D.DrawText(entry.Item1.Title, pos);
                    pos.y += 20;
                    gui.Draw2D.DrawText(entry.Item1.Description, 17, pos, Color.white * 0.9f, gui.CurrentNode.LayoutData.GlobalContentWidth - (pos.x - startpos.x));
                }
            }
        }
    }

    private async void DrawPackageDetails()
    {
        if (loadingDetails)
        {
            gui.Draw2D.LoadingIndicatorCircle(gui.CurrentNode.LayoutData.InnerRect.Center, 25, Color.white, Color.white, 12, 1f);
            return;
        }

        if (!_showProjectDetails)
            return;

        var width = gui.CurrentNode.LayoutData.GlobalContentWidth;
        var height = gui.CurrentNode.LayoutData.GlobalContentHeight;

        if (package_Icon != null)
        {
            using (gui.Node("PackageIcon").Left(width / 4).Scale(width / 2).Enter())
            {
                gui.Draw2D.DrawImage(package_Icon, gui.CurrentNode.LayoutData.InnerRect);
            }
            height -= width / 2;
        }

        using (gui.Node("PackageDetails").Height(height).Width(width).Layout(LayoutType.Column).Scroll().Clip().Padding(5).Spacing(5).Enter())
        {
            StringBuilder sb = new();

            var itemSize = EditorStylePrefs.Instance.ItemSize;
            var installWidth = width - 75 - 10 - 10;
            using (gui.Node("Installed").Height(itemSize).ExpandWidth().Layout(LayoutType.Row).Enter())
            {
                var installedPackage = AssetDatabase.GetInstalledPackage(_metadata.Identity.Id);
                if (installedPackage != null)
                {
                    if (gui.Combo("Version", "VersionPopup", ref _selectedVersionIndex, _projectVersions, 0, 0, 75, itemSize))
                        PopulateDetails(_projectVersions[_selectedVersionIndex]);

                    bool canUpdate = _projectVersions[_selectedVersionIndex] != installedPackage.Identity.Version.ToString();
                    if (canUpdate && EditorGUI.StyledButton("Update", installWidth / 2, itemSize))
                    {
                        AssetDatabase.UninstallPackage(_metadata.Identity.Id, installedPackage.Identity.Version.ToString());
                        await AssetDatabase.InstallPackage(_metadata.Identity.Id, _projectVersions[_selectedVersionIndex]);
                    }

                    if (EditorGUI.StyledButton("Uninstall", canUpdate ? installWidth / 2 : installWidth, itemSize))
                        AssetDatabase.UninstallPackage(_metadata.Identity.Id, installedPackage.Identity.Version.ToString());

                    sb.AppendLine("==========");
                    sb.AppendLine("Installed Version: " + installedPackage.Identity.Version.ToString());
                    sb.AppendLine("==========");
                }
                else
                {
                    if (gui.Combo("Version", "VersionPopup", ref _selectedVersionIndex, _projectVersions, 0, 0, 75, itemSize))
                        PopulateDetails(_projectVersions[_selectedVersionIndex]);
                    if (EditorGUI.StyledButton("Install", installWidth, itemSize))
                        await AssetDatabase.InstallPackage(_metadata.Identity.Id, _projectVersions[_selectedVersionIndex]);
                }
            }

            sb.AppendLine("Title: " + package_Title);
            sb.AppendLine("Description:");
            sb.AppendLine(package_Description);
            sb.AppendLine("");
            sb.AppendLine("Authors: " + package_Authors);
            //sb.AppendLine("Downloads: " + package_Downloads);
            sb.AppendLine("Version: " + package_Version);
            sb.AppendLine("Publish Date: " + package_PublishDate);

            EditorGUI.TextSimple(sb.ToString());

            if (EditorGUI.StyledButton("Project Website"))
                System.Diagnostics.Process.Start("explorer", package_ProjectURL.ToString());

            if (EditorGUI.StyledButton("License"))
                System.Diagnostics.Process.Start("explorer", package_LicenseURL.ToString());


            if (package_Dependencies.Count > 0)
            {
                StringBuilder sb2 = new();
                sb2.AppendLine("");
                sb2.AppendLine("Dependencies:");
                for (int i = 0; i < package_Dependencies.Count; i++)
                    sb2.AppendLine(package_Dependencies[i].Item1 + " " + package_Dependencies[i].Item2);
                EditorGUI.TextSimple(sb2.ToString());
            }
        }
    }

    private async void PopulateListWithSearchResults(string search, string? source = null)
    {
        loadingPackageEntries++;
        PackageEntries.Clear();
        try
        {
            List<IPackageSearchMetadata> searchResults = [];
            if (source == null)
            {
                foreach (var src in PackageManagerPreferences.Instance.Sources)
                {
                    if (!src.IsEnabled) continue;

                    try
                    {
                        searchResults.AddRange(await AssetDatabase.SearchPackages(search, src.Source, PackageManagerPreferences.Instance.IncludePrerelease));
                    }
                    catch (Exception ex)
                    {
                        Error = $"An Error Occurred Error in Source: {src.Name} : " + ex.Message;
                    }
                }
            }
            else
            {
                searchResults.AddRange(await AssetDatabase.SearchPackages(search, source, PackageManagerPreferences.Instance.IncludePrerelease));
            }

            if (searchResults != null)
                PopulateListWith(searchResults);
        }
        catch (Exception ex)
        {
            Error = "Error: " + ex.Message;
        }
        loadingPackageEntries--;
    }

    private async void PopulateListWith(List<IPackageSearchMetadata> searchresults)
    {
        loadingPackageEntries++;
        foreach (var entry in PackageEntries)
            entry.Item2?.DestroyImmediate();

        PackageEntries.Clear();
        foreach (var result in searchresults)
        {
            PackageEntries.Add((result, await DownloadIcon(result.IconUrl)));
        }
        loadingPackageEntries--;
    }

    private async Task<Texture2D?> DownloadIcon(Uri? url)
    {
        if (string.IsNullOrWhiteSpace(url?.ToString()))
            return null;

        try
        {
            WebClient wc = new WebClient();
            byte[] bytes = await wc.DownloadDataTaskAsync(url);
            MemoryStream ms = new MemoryStream(bytes);
            return Texture2DLoader.FromStream(ms);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async void ClickPackage(string packageId)
    {
        loadingDetails = true;
        Error = "";
        try
        {
            List<IPackageSearchMetadata> metadata = [];
            List<NuGet.Versioning.NuGetVersion> versions = [];
            if (_currentPage == Page.Source)
            {
                metadata.AddRange(await AssetDatabase.GetPackageMetadata(packageId, _currentSource.Item2, PackageManagerPreferences.Instance.IncludePrerelease));
                // Get Versions
                versions.AddRange(await AssetDatabase.GetPackageVersions(packageId, _currentSource.Item2, PackageManagerPreferences.Instance.IncludePrerelease));
            }
            else
            {
                if (_currentPage == Page.Installed)
                {
                    metadata.AddRange(await AssetDatabase.GetPackageMetadata(packageId, Project.Active.PackagesDirectory.FullName, PackageManagerPreferences.Instance.IncludePrerelease));
                }
                else if (_currentPage == Page.AllPackages)
                {
                    foreach (var source in PackageManagerPreferences.Instance.Sources)
                        if (source.IsEnabled)
                            metadata.AddRange(await AssetDatabase.GetPackageMetadata(packageId, source.Source, PackageManagerPreferences.Instance.IncludePrerelease));
                }

                // Get Versions
                foreach (var source in PackageManagerPreferences.Instance.Sources)
                    if (source.IsEnabled)
                        versions.AddRange(await AssetDatabase.GetPackageVersions(packageId, source.Source, PackageManagerPreferences.Instance.IncludePrerelease));
            }

            string latestVersion = metadata.LastOrDefault().Identity.Version.ToString();

            _projectVersions = versions.Select(x => x.ToString()).Reverse().ToArray();

            _selectedPackageMetaData = metadata;

            _selectedVersionIndex = 0;
            PopulateDetails(latestVersion);

            _showProjectDetails = true;
        }
        catch (Exception)
        {
            _showProjectDetails = false;
        }
        loadingDetails = false;
    }

    private async void PopulateDetails(string version)
    {
        _metadata = _selectedPackageMetaData.SingleOrDefault(x => x.Identity.Version.ToString() == version);

        package_Version = version;
        package_Dependencies = [];

        package_Icon?.DestroyImmediate();

        if (_metadata != null)
        {
            package_Icon = await DownloadIcon(_metadata.IconUrl);

            package_Title = _metadata.Title;
            package_Authors = _metadata.Authors;
            package_Description = _metadata.Description;
            package_Downloads = _metadata.DownloadCount?.ToString() ?? "Unknown";
            package_PublishDate = DateTime.Parse(_metadata.Published.ToString()).ToString("g");
            package_ProjectURL = _metadata.ProjectUrl.ToString();
            package_LicenseURL = _metadata.LicenseUrl.ToString();

            if (_metadata.DependencySets.ToList().Count > 0)
                foreach (var dependency in _metadata.DependencySets.FirstOrDefault().Packages)
                    package_Dependencies.Add((dependency.Id, dependency.VersionRange.ToString()));
        }
        else
        {
            _metadata = _selectedPackageMetaData.Last();
            package_Icon = await DownloadIcon(_metadata.IconUrl);

            package_Title = _metadata.Title;
            package_Authors = "Unknown";
            package_Description = "Unknown";
            package_Downloads = "Unknown";
            package_PublishDate = "Unknown";
            package_ProjectURL = "Unknown";
            package_LicenseURL = "Unknown";
        }
    }
}
