using Prowl.Runtime;
using Prowl.Runtime.Assets;
using Prowl.Runtime.Resources;
using Prowl.Runtime.Utils;
using Prowl.Editor.Assets;
using Prowl.Icons;
using HexaEngine.ImGuiNET;
using System.IO;
using System.Numerics;
using System.Reflection;

namespace Prowl.Editor.EditorWindows;

public class AssetBrowserWindow : EditorWindow {

    public static string CurrentActiveDirectory;

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

    public AssetBrowserSettings Settings => Project.ProjectSettings.GetSetting<AssetBrowserSettings>();

    public string Selected { get; private set; } = string.Empty;
    private string _searchText = "";
    private string m_SelectedFilePath = "";

    protected override ImGuiWindowFlags Flags => ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse;

    private string m_AssetsDirectory;
    private string m_CurrentDirectory;
    private List<DirectoryEntry> m_DirectoryEntries;
    private static float s_LastDomainReloadTime = 0.0f;
    private static FileSystemWatcher watcher;
    private float ThumbnailSize => (1.0f + Settings.m_ThumbnailSize) * 80f;

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
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 200);
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
            {
                float bottom = ImGui.GetScrollY() + ImGui.GetContentRegionAvail().Y - 23;
                RenderBody();
                RenderFooter(bottom);
            }
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

    private void RenderFooter(float bottom)
    {
        if (Project.HasProject)
        {
            // Get Bottom of child window
            ImGui.SetCursorPosY(bottom - 5);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 5);
            float y = ImGui.GetCursorPosY();
            float x = ImGui.GetCursorPosX();
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.0f, 0.0f, 0.0f, 0.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.5f, 0.5f, 0.5f, 0.25f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.5f, 0.5f, 0.5f, 0.25f));

            string assetPath = Path.GetRelativePath(Project.ProjectDirectory, m_CurrentDirectory);
            string current = "";
            var paths = assetPath.Split(Path.DirectorySeparatorChar);
            for (int i = 0; i < paths.Length; i++)
            {
                ImGui.SetCursorPosY(y);
                ImGui.SetCursorPosX(x);

                if (i == 0) ImGui.BeginDisabled();
                ImGui.SetCursorPosY(y + 5);
                ImGui.Text(paths[i]);
                x += ImGui.GetItemRectSize().X + 1;
                if (i == 0) ImGui.EndDisabled();
                current = Path.Combine(current, paths[i]);
                if (i != paths.Length - 1)
                {
                    ImGui.SetCursorPosY(y + 4);
                    ImGui.SetCursorPosX(x);
                    ImGui.Text(@"\");
                    x += ImGui.GetItemRectSize().X + 1;
                }
            }

            ImGui.PopStyleColor(3);
        }
    }

    private void RenderHeader()
    {
        float windowWidth = ImGui.GetWindowWidth();
        float windowPad = ImGui.GetStyle().WindowPadding.X;

        if (ImGui.Button("   " + FontAwesome6.FileImport + "   "))
        {
#warning TODO: Import Asset
        }

        ImGui.SameLine();

        // Up button
        bool disabledUpButton = m_CurrentDirectory == m_AssetsDirectory;
        if (disabledUpButton) ImGui.BeginDisabled(true);
        if (ImGui.Button(FontAwesome6.ArrowUp))
            UpdateDirectoryEntries(Path.GetDirectoryName(m_CurrentDirectory));
        if (disabledUpButton) ImGui.EndDisabled();

        ImGui.SameLine();

        float cPX = ImGui.GetCursorPosX();
        float cPY = ImGui.GetCursorPosY();
        float xSize = windowWidth - cPX - 50;
        ImGui.SetNextItemWidth(xSize - windowPad);
        if (ImGui.InputText("##searchBox", ref _searchText, 0x100)) Refresh();

        if (string.IsNullOrEmpty(_searchText))
        {
            ImGui.SameLine();
            ImGui.SetCursorPosX(cPX + ImGui.GetFontSize() * 0.5f);
            ImGui.TextUnformatted(FontAwesome6.MagnifyingGlass + " Search...");
        }

        ImGui.SetCursorPosX(cPX + xSize);
        ImGui.SetCursorPosY(cPY);
        if (ImGui.Button("   " + FontAwesome6.Gears + "   "))
            new ProjectSettingsWindow(Settings);
    }

    private void RenderSideView()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0.0f);
        int count = 0;
        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanFullWidth | ImGuiTreeNodeFlags.FramePadding;
        if (ImGui.TreeNodeEx("Database", flags))
        {
            foreach(var root in AssetDatabase.GetRootfolders())
            {
                string relativePath = Path.GetRelativePath(Project.ProjectDirectory, root.FullName);
                string displayName = $"{FontAwesome6.Folder} {relativePath}";
                bool opened = ImGui.TreeNodeEx(displayName, flags);

                if (!ImGui.IsItemToggledOpen() && ImGui.IsItemClicked())
                {
                    UpdateDirectoryEntries(root.FullName);
                }

                if (opened)
                    DrawDirectory(root.FullName, ref count, 1);

                if (opened && root.GetDirectories().Length > 0)
                    ImGui.TreePop();
            }

            //if (!ImGui.IsItemToggledOpen() && ImGui.IsItemClicked())
            //{
            //    UpdateDirectoryEntries(Project.ProjectAssetDirectory);
            //}
            //DrawDirectory(Project.ProjectAssetDirectory, ref count);
            ImGui.TreePop();
        }
        ImGui.PopStyleVar();
    }

    private (Vector2, Vector2) DrawDirectory(string directory, ref int count, int level = 0)
    {

        DirectoryInfo[] subDirectories = new DirectoryInfo(directory).GetDirectories();

        uint lineColor = ImGui.GetColorU32(new Vector4(0.5f, 0.5f, 0.5f, 1.0f));
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        (Vector2, Vector2) myRect = (Vector2.Zero, Vector2.Zero);
        foreach (DirectoryInfo subDirectory in subDirectories)
        {
            //Path.GetFileName(directory).Contains(str, StringComparison.CurrentCultureIgnoreCase) ||
            //Directory.GetFiles(directory).Any(f => Path.GetFileName(f).Contains(str, StringComparison.CurrentCultureIgnoreCase)) ||
            //Directory.GetDirectories(directory).Any(d => Path.GetFileName(d).Contains(str, StringComparison.CurrentCultureIgnoreCase));
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

            myRect = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());

            if (count++ % 2 == 0)
                drawList.AddRectFilled(myRect.Item1, myRect.Item2, ImGui.GetColorU32(new Vector4(0.5f, 0.5f, 0.5f, 0.1f)));

            if (!ImGui.IsItemToggledOpen() && ImGui.IsItemClicked())
            {
                Selection.Select(subDirectory.FullName, false);
                UpdateDirectoryEntries(subDirectory.FullName);
            }

            if (opened)
            {
                // Draw vertical lines
                float horizontalTreeLineSize = 4.0f; // chosen arbitrarily
                Vector2 verticalLineStart = ImGui.GetCursorScreenPos() + new Vector2(horizontalTreeLineSize, -13);
                Vector2 verticalLineEnd = verticalLineStart;

                // Draws the child Directories and returns the Rect for the Last Entry
                var rect = DrawDirectory(subDirectory.FullName, ref count, level + 1);

                if (rect.Item1 != Vector2.Zero)
                {
                    float midpoint = (rect.Item1.Y + rect.Item2.Y) * 0.5f;
                    drawList.AddLine(new(verticalLineStart.X, midpoint), new(verticalLineStart.X + horizontalTreeLineSize + 1, midpoint), lineColor);
                    verticalLineEnd.Y = midpoint;
                }

                drawList.AddLine(verticalLineStart, verticalLineEnd, lineColor);
            }

            if (opened && subDirectory.GetDirectories().Length > 0)
                ImGui.TreePop();
        }
        return myRect;
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

            const float padding = 15;
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
            if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.None))
            {
                Type type = ImporterAttribute.GetGeneralType(ext);
                if (type != null)
                {
                    unsafe
                    {
                        ImGui.SetDragDropPayload($"ASSETPAYLOAD_{type.Name}", null, 0);
                    }
                    Selection.SetDragging(AssetDatabase.GUIDFromAssetPath(Path.GetRelativePath(Project.ProjectDirectory, filePath)));
                    ImGui.TextUnformatted(type.Name + " " + Path.GetRelativePath(Project.ProjectAssetDirectory, filePath));
                    ImGui.EndDragDropSource();
                }
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
