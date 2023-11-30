using HexaEngine.ImGuiNET;
using Prowl.Editor.Assets;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.Assets;
using Prowl.Runtime.ImGUI.Widgets;
using Prowl.Runtime.Resources;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using Debug = Prowl.Runtime.Debug;

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

        if (Project.HasProject == false) ImGui.BeginDisabled(true);

        RenderHeader();

        ImGui.BeginChild("Body");
        if (Project.HasProject) RenderBody();
        ImGui.EndChild();

        if (Project.HasProject == false) ImGui.EndDisabled();

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
                    if(ImGui.MenuItem("Show In Explorer"))
                    {
                        using Process fileopener = new Process();
                        fileopener.StartInfo.FileName = "explorer";
                        fileopener.StartInfo.Arguments = "\"" + new DirectoryInfo(entryPath).Parent!.FullName + "\"";
                        fileopener.Start();
                    }


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
                    if(ImGui.MenuItem("Show In Explorer"))
                    {
                        using Process fileopener = new Process();
                        fileopener.StartInfo.FileName = "explorer";
                        fileopener.StartInfo.Arguments = "\"" + new FileInfo(entryPath).Directory!.FullName + "\"";
                        fileopener.Start();
                    }


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

        var lineColor = AssetsWindow.GetFileColor(ext.ToLower().Trim());

        var pos = ImGui.GetCursorScreenPos();
        drawList.AddLine(new(0, pos.Y), new(pos.X + thumbnailSize, pos.Y + 1f), lineColor, 3f);

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 2);
        ImGui.TextUnformatted(Settings.m_HideExtensions ? Path.GetFileNameWithoutExtension(filePath) : Path.GetFileName(filePath));

        ImGui.EndGroup();

        GUIHelper.Tooltip(Path.GetFileName(filePath));

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
    private static (long, bool) lastGenerated = (-1, false);
    private Texture2D GetThumbnail(string path)
    {
        string ext = Path.GetExtension(path);
        string fileName = "FileIcon.png";

        if (lastGenerated.Item1 != Time.frameCount || !lastGenerated.Item2)
        {
            if (TextureImporter.Supported.Contains(ext, StringComparer.OrdinalIgnoreCase))
            {
                string? relativeAssetPath = AssetDatabase.GetRelativePath(path);

                if (cachedThumbnails.TryGetValue(path, out Texture2D? value))
                    return value;

#warning TODO: if the texture at path changes this needs to somehow know and update

                if (relativeAssetPath != null) // if its null fallback to the default FileIcon
                {
                    lastGenerated = (Time.frameCount, true);
                    var tex = Application.AssetProvider.LoadAsset<Texture2D>(relativeAssetPath);
                    cachedThumbnails[path] = tex;
                    return tex;
                }
            }
            else if (ImporterAttribute.SupportsExtension(ext))
            {
                fileName = ImporterAttribute.GetIconForExtension(ext);
            }
            else if (ext.Equals(".cs", StringComparison.OrdinalIgnoreCase)) // Special case, is a c# script
                fileName = "CSharpIcon.png";

        }

        if (!cachedThumbnails.ContainsKey(fileName))
        {
            lastGenerated = (Time.frameCount, true);
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
