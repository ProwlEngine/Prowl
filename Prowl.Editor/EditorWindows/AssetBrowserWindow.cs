using HexaEngine.ImGuiNET;
using Prowl.Editor.Assets;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.Assets;
using Prowl.Runtime.ImGUI.Widgets;
using Prowl.Runtime.Resources;
using System.Numerics;
using System.Reflection;

namespace Prowl.Editor.EditorWindows;

public class AssetBrowserWindow : EditorWindow {

    public class AssetBrowserSettings : IProjectSetting
    {
        [Text("Settings for Asset Browser.")]
        public bool m_HideExtensions = false;
        public float m_ThumbnailSize = 0.0f;

        [Space]
        [Seperator]
        [Space]
        [Text("Settings for Asset Engine.")]
        [Tooltip("Auto recompile all scripts when a change is detected.")]
        public bool m_AutoRecompile = true;
        [Tooltip("Auto recompile all shaders when a change is detected.")]
        public bool m_AutoRecompileShaders = true;
    }

    public static AssetBrowserSettings Settings => Project.ProjectSettings.GetSetting<AssetBrowserSettings>();

    public static string CurrentActiveDirectory;

    public string Selected { get; private set; } = string.Empty;
    private string _searchText = "";
    private string m_SelectedFilePath = "";

    protected override ImGuiWindowFlags Flags => ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse;

    private string m_AssetsDirectory;
    private string m_DefaultsDirectory;
    private string m_CurrentDirectory;
    private List<DirectoryEntry> m_DirectoryEntries;
    private static float s_LastDomainReloadTime = 0.0f;
    private static FileSystemWatcher watcher;
    private float ThumbnailSize => (1.0f + Settings.m_ThumbnailSize) * 65f;

    public AssetBrowserWindow()
    {
        Title = "Asset Browser";
        Project.OnProjectChanged += Invalidate;
        Invalidate();
    }

    ~AssetBrowserWindow()
    {
        Project.OnProjectChanged -= Invalidate;
    }

    protected override void Draw()
    {
        s_LastDomainReloadTime += Time.deltaTimeF;

        const ImGuiTableFlags tableFlags = ImGuiTableFlags.Resizable | ImGuiTableFlags.ContextMenuInBody | ImGuiTableFlags.Resizable;

        if (Project.HasProject == false) ImGui.BeginDisabled(true);
        Vector2 availableRegion = ImGui.GetContentRegionAvail();

        if (ImGui.BeginTable("MainViewTable", 2, tableFlags, availableRegion))
        {
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 150f);
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.None);

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);

            ImGui.BeginChild("Tree");
            if (Project.HasProject) RenderSideView();
            ImGui.EndChild();

            ImGui.TableSetColumnIndex(1);

            RenderHeader();

            ImGui.BeginChild("Body");

            if (Project.HasProject)
                RenderBody();

            ImGui.EndChild();

            //if (ImGui.BeginPopupContextItem())
            //{
            //    if (ImGui.MenuItem("Refresh Database"))
            //        AssetDatabase.RefreshAll();
            //    ImGui.Separator();
            //
            //
            //    ImGui.EndPopup();
            //}

            ImGui.EndTable();
        }
        if (Project.HasProject == false) ImGui.EndDisabled();

        if (ImGui.IsMouseDown(ImGuiMouseButton.Left) && ImGui.IsWindowHovered()) {
            Selection.Clear();
        }

        if (ImGui.IsKeyPressed(ImGuiKey.Delete) && ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows))
        {
            if (Selection.Current is string && Directory.Exists(Selection.Current as string))
            {
                Directory.Delete(Selection.Current as string, true);
                Selection.Clear();
            }
        }
    }

    public void Invalidate()
    {
        m_DirectoryEntries = new List<DirectoryEntry>();

        m_AssetsDirectory = Project.ProjectAssetDirectory;
        m_DefaultsDirectory = Project.ProjectDefaultsDirectory;
        m_CurrentDirectory = m_AssetsDirectory;
        CurrentActiveDirectory = m_CurrentDirectory;

        Refresh();

        watcher?.Dispose();

        if (Project.HasProject == false) return;

        watcher = new FileSystemWatcher(m_AssetsDirectory);
        watcher.NotifyFilter = NotifyFilters.Attributes | NotifyFilters.CreationTime | NotifyFilters.DirectoryName | NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size;
        watcher.Changed += (s, e) =>
        {
            if (s_LastDomainReloadTime < 0.1f) return;
            Refresh();

            string ext = Path.GetExtension(e.FullPath);
            if (ext.Equals(".cs", StringComparison.OrdinalIgnoreCase))
            {
                if (Settings.m_AutoRecompile)
                    EditorApplication.Instance.RegisterReloadOfExternalAssemblies();
            }
            else if (ext.Equals(".glsl", StringComparison.OrdinalIgnoreCase))
            {
                // TODO: Crashes the editor Unloading shaders doesnt seem to work
                //if (Settings.m_AutoRecompileShaders)
                //    AssetProvider.RemoveAsset(e.FullPath, true);
            }

            switch (e.ChangeType)
            {
                case WatcherChangeTypes.Created: Debug.Log($"Asset Added: {e.FullPath}"); break;
                case WatcherChangeTypes.Deleted: Debug.Log($"Asset Removed: {e.FullPath}"); break;
                case WatcherChangeTypes.Renamed: Debug.Log($"Asset Renamed (Caught by 'Changed'): {e.FullPath}"); break; // TODO: Does this ever hit? Seems to just be the Renamed action that gets hit
                case WatcherChangeTypes.Changed: Debug.Log($"Asset Changed:{e.FullPath}"); break;
            }
        };
        watcher.Renamed += (s, e) =>
        {
            // On rename the files Asset generally didnt change, so we probably dont need to do any special updating
            if (s_LastDomainReloadTime < 0.1f) return;
            Refresh();
            Debug.Log($"Asset Renamed (Caught by 'Renamed'): {e.FullPath}");
            // TODO: Update all Asset with saved paths to this new one
        };
        watcher.EnableRaisingEvents = true;
    }

    private void RenderHeader()
    {
        float windowWidth = ImGui.GetWindowWidth();
        float windowPad = ImGui.GetStyle().WindowPadding.X;

        const int searchBarSize = 125;
        const int sizeSliderSize = 75;
        const int padding = 5;
        const int rightOffset = 43;

        if (ImGui.Button("   " + FontAwesome6.FileImport + "   "))
        {
            var dialog = new ImFileDialogInfo()
            {
                title = "Import File",
                directoryPath = new DirectoryInfo(m_CurrentDirectory),
                OnComplete = (path) =>
                {
                    string relativePath = Path.GetRelativePath(CurrentActiveDirectory, path);
                    string assetPath = Path.Combine(CurrentActiveDirectory, relativePath);
                    if (File.Exists(assetPath))
                    {
                        Debug.Log($"Asset already exists: {assetPath}");
                        return;
                    }
                    var file = new FileInfo(path);
                    file.CopyTo(assetPath);
                    AssetDatabase.Refresh(new FileInfo(assetPath));
                },
            };
            ImGuiFileDialog.FileDialog(dialog);
        }

        ImGui.SameLine();

        // Up button
        bool disabledUpButton = m_CurrentDirectory == m_AssetsDirectory;
        disabledUpButton |= m_CurrentDirectory == m_DefaultsDirectory;

        if (disabledUpButton) ImGui.BeginDisabled(true);
        if (ImGui.Button(FontAwesome6.ArrowUp))
            UpdateDirectoryEntries(Path.GetDirectoryName(m_CurrentDirectory));
        if (disabledUpButton) ImGui.EndDisabled();

        ImGui.SameLine();

        float cPX = ImGui.GetCursorPosX();
        float cPY = ImGui.GetCursorPosY();
        ImGui.SetNextItemWidth(searchBarSize);
        if (ImGui.InputText("##searchBox", ref _searchText, 0x100)) Refresh();

        if (string.IsNullOrEmpty(_searchText))
        {
            ImGui.SameLine();
            ImGui.SetCursorPosX(cPX + ImGui.GetFontSize() * 0.5f);
            ImGui.TextUnformatted(FontAwesome6.MagnifyingGlass + " Search...");
        }

        if (Project.HasProject == false) return;

        ImGui.SetCursorPosY(cPY);
        ImGui.SetCursorPosX(cPX + searchBarSize + padding);
        string assetPath = Path.GetRelativePath(Project.ProjectDirectory, m_CurrentDirectory);
        ImGui.Text(assetPath);

        ImGui.SetCursorPosY(cPY);
        ImGui.SetCursorPosX(windowWidth - rightOffset - sizeSliderSize - padding);
        ImGui.SetNextItemWidth(sizeSliderSize);
        ImGui.SliderFloat("##ThumbnailSizeSlider", ref Settings.m_ThumbnailSize, -0.2f, 1.0f);

        ImGui.SetCursorPosY(cPY);
        ImGui.SetCursorPosX(windowWidth - rightOffset);

        if (ImGui.Button("   " + FontAwesome6.Gears + "   "))
            _ = new ProjectSettingsWindow(Settings);
    }

    private void RenderSideView()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0.0f);
        int count = 0;
        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanFullWidth | ImGuiTreeNodeFlags.FramePadding;
        var rootFolders = AssetDatabase.GetRootfolders();
        RenderSideViewFolder(ref count, flags, rootFolders[1]); // Assets Folder
        RenderSideViewFolder(ref count, flags, rootFolders[0]); // Defaults Folder
        ImGui.PopStyleVar();
    }

    private void RenderSideViewFolder(ref int count, ImGuiTreeNodeFlags flags, DirectoryInfo root)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0f, 0.321f, 1f));
        string displayName = $"{FontAwesome6.Folder} {Path.GetRelativePath(Project.ProjectDirectory, root.FullName)}";
        bool opened = ImGui.TreeNodeEx(displayName, flags);
        ImGui.PopStyleColor();

        if (!ImGui.IsItemToggledOpen() && ImGui.IsItemClicked())
            UpdateDirectoryEntries(root.FullName);

        if (opened)
        {
            DrawDirectory(root.FullName, ref count, 1);
            ImGui.TreePop();
        }
    }

    private void DrawDirectory(string directory, ref int count, int level = 0)
    {
        DirectoryInfo[] subDirectories = new DirectoryInfo(directory).GetDirectories();

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        foreach (DirectoryInfo subDirectory in subDirectories)
        {
            if (string.IsNullOrEmpty(_searchText) == false)
                if(Path.GetFileName(subDirectory.FullName).Contains(_searchText, StringComparison.CurrentCultureIgnoreCase) == false)
                    continue;

            ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanFullWidth | ImGuiTreeNodeFlags.FramePadding;

            if (subDirectory.GetDirectories().Length == 0)
                flags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;

            if (Selection.Current is string && subDirectory.FullName == Selection.Current as string) flags |= ImGuiTreeNodeFlags.Selected;

            string relativePath = Path.GetRelativePath(directory, subDirectory.FullName);
            string displayName = $"{FontAwesome6.Folder} {relativePath}";
            bool opened = ImGui.TreeNodeEx(displayName, flags);

            if (count++ % 2 == 0)
                drawList.AddRectFilled(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.GetColorU32(new Vector4(0.5f, 0.5f, 0.5f, 0.1f)));

            if (!ImGui.IsItemToggledOpen() && ImGui.IsItemClicked())
            {
                Selection.Select(subDirectory.FullName, false);
                UpdateDirectoryEntries(subDirectory.FullName);
            }

            if (opened)
                DrawDirectory(subDirectory.FullName, ref count, level + 1);

            if (opened && subDirectory.GetDirectories().Length > 0)
                ImGui.TreePop();
        }
    }

    private void RenderBody()
    {
        float contentWidth = ImGui.GetContentRegionAvail().X;
        var startPos = ImGui.GetCursorPos();
        var curPos = startPos;
        for (int i = 0; i < m_DirectoryEntries.Count; i++)
        {
            ImGui.SetCursorPos(curPos);
            var entry = m_DirectoryEntries[i];
            string entryPath = Path.Combine(m_CurrentDirectory, entry.Name);

            ImGui.PushID(i);

            ImGui.BeginChild("ClipBox", new Vector2(ThumbnailSize, ThumbnailSize), false, ImGuiWindowFlags.NoScrollbar);
            if (entry.IsDirectory)
                RenderDirectoryEntry(entryPath);
            else
                RenderFileEntry(entryPath);
            ImGui.EndChild();

            if (entry.IsDirectory)
            {
                if (ImGui.BeginPopupContextItem())
                {
                    MenuItem.DrawMenuRoot("Create");
                    CreateAssetMenuHandler.DrawMenuItems();
                    ImGui.Separator();
                    if (ImGui.MenuItem("Reimport"))
                        AssetDatabase.ReimportFolder(new(entryPath));
                    if (ImGui.MenuItem("Reimport All"))
                        AssetDatabase.ReimportAll();
                    if (ImGui.MenuItem("Refresh"))
                        AssetDatabase.Refresh(new FileInfo(entryPath));
                    if (ImGui.MenuItem("Refresh All"))
                        AssetDatabase.RefreshAll();
                    ImGui.Separator();
                    ImGui.MenuItem("Show In Explorer");


                    ImGui.EndPopup();
                }
            }
            else
            {
                if (ImGui.BeginPopupContextItem())
                {
                    var relativeAssetPath = AssetDatabase.GetRelativePath(entryPath);
                    ImGui.MenuItem("Create");
                    ImGui.Separator();
                    if (ImGui.MenuItem("Reimport"))
                        AssetDatabase.Reimport(relativeAssetPath);
                    if (ImGui.MenuItem("Reimport All"))
                        AssetDatabase.ReimportAll();
                    ImGui.Separator();
                    ImGui.MenuItem("Show In Explorer");


                    ImGui.EndPopup();
                }
            }

            ImGui.PopID();

            const float padding = 8;
            int rowCount = Math.Max((int)(contentWidth / (ThumbnailSize + padding)), 1);
            float itemSize = ((ThumbnailSize) + padding);
            curPos.X = ((i + 1) % rowCount) * itemSize;
            curPos.Y = ((i + 1) / rowCount) * itemSize;
        }

        if (ImGui.IsWindowFocused() && ImGui.GetIO().KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.C))
            if (Selection.Current is string)
            {
                string path = Selection.Current as string;
                if (File.Exists(path))
                    ImGui.SetClipboardText(path);
            }
    }

    private void RenderDirectoryEntry(string directoryPath)
    {
        if (!Directory.Exists(directoryPath)) return;

        var info = new DirectoryInfo(directoryPath);

        bool isSelected = directoryPath == m_SelectedFilePath;

        float thumbnailSize = Math.Min(ThumbnailSize, ImGui.GetContentRegionAvail().X);
        ImGui.BeginGroup();

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0.0f, 4.0f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0.0f);

        ImGui.Selectable("##" + directoryPath, isSelected, ImGuiSelectableFlags.AllowOverlap, new Vector2(thumbnailSize, thumbnailSize));
        ImGui.GetWindowDrawList().AddRectFilled(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.GetColorU32(new Vector4(0.5f, 0.5f, 0.5f, 0.1f)));
        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(0))
        {
            m_SelectedFilePath = isSelected ? null : directoryPath;
            //Selection.Select(directoryPath);
            UpdateDirectoryEntries(directoryPath);
        }

        ImGui.PopStyleVar(2);

        string fileName = info.GetFiles().Any() || info.GetDirectories().Any() ? "FolderFilledIcon.png" : "FolderEmptyIcon.png";
        if (!cachedThumbnails.ContainsKey(fileName))
        {
            using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Prowl.Editor.EmbeddedResources." + fileName);
            cachedThumbnails[fileName] = new Texture2D(stream);
            cachedThumbnails[fileName].SetFilter(Raylib_cs.TextureFilter.TEXTURE_FILTER_BILINEAR);
        }
        Texture2D thumbnail = cachedThumbnails[fileName];


        // Image should draw smaller to fix Text
        thumbnailSize -= 30;
        float thumbnailWidth = ((float)thumbnail.Width / thumbnail.Height) * thumbnailSize;
        float xOffset = ((thumbnailSize - thumbnailWidth) / 2) + 15;
        ImGui.SetCursorPos(new Vector2(xOffset, 10));
        ImGui.Image((IntPtr)thumbnail.Handle, new Vector2(thumbnailWidth, thumbnailSize), new Vector2(0, 1), new Vector2(1, 0));

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 2);
        ImGui.TextUnformatted(Path.GetFileName(directoryPath));

        ImGui.EndGroup();
    }

    private void RenderFileEntry(string filePath)
    {
        if (!File.Exists(filePath)) return;
        string ext = Path.GetExtension(filePath);

        bool isSelected = filePath == m_SelectedFilePath;

        float thumbnailSize = Math.Min(ThumbnailSize, ImGui.GetContentRegionAvail().X);
        ImGui.BeginGroup();

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0.0f, 4.0f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0.0f);

        if (ImGui.Selectable("##" + filePath, isSelected, ImGuiSelectableFlags.AllowOverlap, new Vector2(thumbnailSize, thumbnailSize)))
        {
            m_SelectedFilePath = isSelected ? null : filePath;
            Selection.Select(filePath);
        }
        ImGui.GetWindowDrawList().AddRectFilled(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.GetColorU32(new Vector4(0.5f, 0.5f, 0.5f, 0.1f)));

        ImGui.PopStyleVar(2);

        Texture2D thumbnail = GetThumbnail(filePath);
        if (thumbnail != null)
        {
            // Image should draw smaller to fix Text
            thumbnailSize -= 30;
            float thumbnailWidth = ((float)thumbnail.Width / thumbnail.Height) * thumbnailSize;
            float xOffset = ((thumbnailSize - thumbnailWidth) / 2) + 15;
            ImGui.SetCursorPos(new Vector2(xOffset, 10));
            ImGui.Image((IntPtr)thumbnail.Handle, new Vector2(thumbnailWidth, thumbnailSize), new Vector2(0, 1), new Vector2(1, 0));
            thumbnailSize += 30;
        }

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();

        var ran = new System.Random(ext.ToLower().Trim().GetHashCode() + 5); // +1 cause i didnt like the colors it gave without it :P
        float r = 0;
        float g = 0;
        float b = 0;
        ImGui.ColorConvertHSVtoRGB((float)ran.NextDouble(), 0.8f + (float)ran.NextDouble() * 0.2f, 0.8f + (float)ran.NextDouble() * 0.2f, ref r, ref g, ref b );
        var lineColor = ImGui.GetColorU32(new Vector4(r, g, b, 1.0f));

        var pos = ImGui.GetCursorScreenPos();
        drawList.AddLine(new(0, pos.Y), new(pos.X + thumbnailSize, pos.Y + 1f), lineColor, 3f);

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 2);
        ImGui.TextUnformatted(Settings.m_HideExtensions ? Path.GetFileNameWithoutExtension(filePath) : Path.GetFileName(filePath));

        ImGui.EndGroup();

        // Drag and Drop Payload
        if (ImporterAttribute.SupportsExtension(ext))
        {
            Type type = ImporterAttribute.GetGeneralType(ext);
            if (type != null)
            {

                var guid = AssetDatabase.GUIDFromAssetPath(Path.GetRelativePath(Project.ProjectDirectory, filePath));
                DragnDrop.OfferAsset(guid, type.Name);
            }
        }
    }

    private void UpdateDirectoryEntries(string directoryPath)
    {
        m_CurrentDirectory = directoryPath;
        CurrentActiveDirectory = m_CurrentDirectory;

        m_DirectoryEntries.Clear();

        DirectoryInfo directoryInfo = new DirectoryInfo(directoryPath);

        try
        {
            if (string.IsNullOrEmpty(_searchText))
            {
                foreach (var directory in directoryInfo.GetDirectories())
                    m_DirectoryEntries.Add(new DirectoryEntry(directory.Name, true));

                foreach (var file in directoryInfo.GetFiles())
                    m_DirectoryEntries.Add(new DirectoryEntry(file.Name, false));
            }
            else
            {
                SearchOption searchOption = SearchOption.AllDirectories;

                foreach (var file in directoryInfo.GetDirectories("*" + _searchText + "*", searchOption))
                    m_DirectoryEntries.Add(new DirectoryEntry(Path.GetRelativePath(directoryPath, file.FullName), true));

                foreach (var file in directoryInfo.GetFiles("*" + _searchText + "*", searchOption))
                    m_DirectoryEntries.Add(new DirectoryEntry(Path.GetRelativePath(directoryPath, file.FullName), false));
            }

            // Remove all Entries with the extension .meta
            m_DirectoryEntries = m_DirectoryEntries.Where(x => !Path.GetExtension(x.Name).Equals(".meta", StringComparison.OrdinalIgnoreCase)).ToList();

        }
        catch (Exception e)
        {
            throw new Exception("Failed to access directory entries: " +  e.Message);
        }
    }

    private void Refresh()
    {
        s_LastDomainReloadTime = 0.0f;

        if (!Directory.Exists(m_AssetsDirectory)) return;

        m_CurrentDirectory = m_AssetsDirectory;
        CurrentActiveDirectory = m_CurrentDirectory;

        UpdateDirectoryEntries(m_CurrentDirectory);
    }

    private Dictionary<string, Texture2D> cachedThumbnails = new();
    private Texture2D GetThumbnail(string path)
    {
        string ext = Path.GetExtension(path);
        string fileName = "FileIcon.png";


        if (TextureImporter.Supported.Contains(ext, StringComparer.OrdinalIgnoreCase))
        {
            string? relativeAssetPath = AssetDatabase.GetRelativePath(path);
            if(relativeAssetPath != null) // if its null fallback to the default FileIcon
                return Application.AssetProvider.LoadAsset<Texture2D>(relativeAssetPath);
        }
        else if (ImporterAttribute.SupportsExtension(ext))
        {
            fileName = ImporterAttribute.GetIconForExtension(ext);
        }
        else if(ext.Equals(".cs", StringComparison.OrdinalIgnoreCase)) // Special case, is a c# script
            fileName = "CSharpIcon.png";

        if (!cachedThumbnails.ContainsKey(fileName))
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Prowl.Editor.EmbeddedResources." + fileName))
            {
                cachedThumbnails[fileName] = new Texture2D(stream);
                cachedThumbnails[fileName].SetFilter(Raylib_cs.TextureFilter.TEXTURE_FILTER_BILINEAR);
            }
        }
        return cachedThumbnails[fileName];
    }

    private struct DirectoryEntry
    {
        public string Name { get; }
        public bool IsDirectory { get; }

        public DirectoryEntry(string name, bool isDirectory)
        {
            Name = name;
            IsDirectory = isDirectory;
        }
    }

}
