using HexaEngine.ImGuiNET;
using Prowl.Editor.Assets;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.Assets;
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

    public DirectoryInfo CurDirectory;
    public bool Locked = false;

    private string _searchText = "";
    private readonly List<FileSystemInfo> _found = [];
    private readonly Dictionary<string, Texture2D> cachedThumbnails = new();
    private static (long, bool) lastGenerated = (-1, false);

    protected override ImGuiWindowFlags Flags => ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse;

    private float ThumbnailSize => (1.0f + Settings.m_ThumbnailSize) * 65f;


    public AssetBrowserWindow()
    {
        Title = "Asset Browser";
        Project.OnProjectChanged += Invalidate;
        Selection.OnSelectionChanged += SelectionChanged;
        Invalidate();
    }

    ~AssetBrowserWindow()
    {
        Project.OnProjectChanged -= Invalidate;
        Selection.OnSelectionChanged -= SelectionChanged;
    }

    protected override void Draw()
    {
        if (Project.HasProject == false) return;

        // Ensure we always have a Directory, if the current one is deleted move to its parent
        // if theres no parent move to the Assets Directory
        // If theres no project directory well why the hell are we here? the line above should have stopped us
        while(CurDirectory?.Exists == false)
            CurDirectory = CurDirectory.Parent ?? new DirectoryInfo(Project.ProjectAssetDirectory);

        RenderHeader();
        ImGui.BeginChild("Body");
        RenderBody();
        ImGui.EndChild();
    }

    public void Invalidate()
    {
        CurDirectory = new DirectoryInfo(Project.ProjectAssetDirectory);
    }

    private void SelectionChanged(object from, object to)
    {
        if (Locked) return;
        if (to is not string str) return;
        if (Directory.Exists(str)) CurDirectory = new DirectoryInfo(str);
        else if (File.Exists(str)) CurDirectory = new FileInfo(str).Directory;
    }

    private void RenderHeader()
    {
        float windowWidth = ImGui.GetWindowWidth();

        const int searchBarSize = 125;
        const int sizeSliderSize = 75;
        const int padding = 5;
        const int rightOffset = 43;

        // Up button
#warning TODO: Project.IsPathInProject(CurDirectory.Parent!.FullName); Would be nice here, then we disable if its not
        bool disabledUpButton = CurDirectory.FullName.Equals(Project.ProjectAssetDirectory, StringComparison.OrdinalIgnoreCase);
        disabledUpButton |= CurDirectory.FullName.Equals(Project.ProjectDefaultsDirectory, StringComparison.OrdinalIgnoreCase);
        disabledUpButton |= CurDirectory.Parent == null;

        if (disabledUpButton) ImGui.BeginDisabled(true);
        if (ImGui.Button(FontAwesome6.ArrowUp))
            CurDirectory = CurDirectory.Parent!;
        if (disabledUpButton) ImGui.EndDisabled();

        ImGui.SameLine();

        float cPX = ImGui.GetCursorPosX();
        float cPY = ImGui.GetCursorPosY();
        ImGui.SetNextItemWidth(searchBarSize);
        if (ImGui.InputText("##searchBox", ref _searchText, 0x100))
        {
            _found.Clear();
            if (!string.IsNullOrEmpty(_searchText))
            {
                _found.AddRange(CurDirectory.EnumerateFiles("*", SearchOption.AllDirectories));
                _found.AddRange(CurDirectory.EnumerateDirectories("*", SearchOption.AllDirectories));
                // Remove Meta's & only keep the ones with SearchText inside them
                _found.RemoveAll(f => f.Extension.Equals(".meta", StringComparison.OrdinalIgnoreCase) || !f.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase));
            }
        }

        if (string.IsNullOrEmpty(_searchText))
        {
            ImGui.SameLine();
            ImGui.SetCursorPosX(cPX + ImGui.GetFontSize() * 0.5f);
            ImGui.TextUnformatted(FontAwesome6.MagnifyingGlass + " Search...");
        }

        if (Project.HasProject == false) return;

        ImGui.SetCursorPosY(cPY);
        ImGui.SetCursorPosX(cPX + searchBarSize + padding);
        string assetPath = Path.GetRelativePath(Project.ProjectDirectory, CurDirectory.FullName);
        ImGui.Text(assetPath);

        ImGui.SetCursorPosY(cPY);
        ImGui.SetCursorPosX(windowWidth - rightOffset - sizeSliderSize - padding - 24);
        if (ImGui.Button(Locked ? FontAwesome6.Lock : FontAwesome6.LockOpen))
            Locked = !Locked;

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
        const float padding = 8;
        float contentWidth = ImGui.GetContentRegionAvail().X;

        int rowCount = Math.Max((int)(contentWidth / (ThumbnailSize + padding)), 1);
        float itemSize = ((ThumbnailSize) + padding);

        var curPos = ImGui.GetCursorPos() + new Vector2(5, 5);
        int i = 0;
        if (!string.IsNullOrEmpty(_searchText))
        {
            // Show only Filters elements
            foreach(var entry in _found)
            {
                ImGui.PushID(i);
                ImGui.SetCursorPos(curPos);
                ImGui.BeginChild("ClipBox", new Vector2(ThumbnailSize, ThumbnailSize), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
                RenderFileSystemEntry(entry);
                ImGui.EndChild();
                AssetsWindow.FileRightClick(entry);
                ImGui.PopID();

                curPos.X = 5 + ((i + 1) % rowCount) * itemSize;
                curPos.Y = 5 + ((i + 1) / rowCount) * itemSize;
                i++;
            }
        }
        else
        {
            foreach (var folder in CurDirectory.EnumerateDirectories())
            {
                ImGui.PushID(i);
                ImGui.SetCursorPos(curPos);
                ImGui.BeginChild("ClipBox", new Vector2(ThumbnailSize, ThumbnailSize), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
                RenderFileSystemEntry(folder);
                ImGui.EndChild();
                AssetsWindow.FileRightClick(folder);
                ImGui.PopID();

                curPos.X = 5 + ((i + 1) % rowCount) * itemSize;
                curPos.Y = 5 + ((i + 1) / rowCount) * itemSize;
                i++;
            }
            foreach (var file in CurDirectory.EnumerateFiles())
            {
                if (file.Extension.Equals(".meta", StringComparison.OrdinalIgnoreCase)) continue;

                ImGui.PushID(i);
                ImGui.SetCursorPos(curPos);
                ImGui.BeginChild("ClipBox", new Vector2(ThumbnailSize, ThumbnailSize), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
                RenderFileSystemEntry(file);
                ImGui.EndChild();
                AssetsWindow.FileRightClick(file);
                ImGui.PopID();

                curPos.X = 5 + ((i + 1) % rowCount) * itemSize;
                curPos.Y = 5 + ((i + 1) / rowCount) * itemSize;
                i++;
            }
        }
    }

    private void RenderFileSystemEntry(FileSystemInfo entry)
    {
        if (!entry.Exists) return;

        bool isSelected = entry.FullName == Selection.Current as string;
        float thumbnailSize = Math.Min(ThumbnailSize, ImGui.GetContentRegionAvail().X);
        ImGui.BeginGroup();

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0.0f, 4.0f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0.0f);

        ImGui.Selectable("##" + entry.FullName, isSelected, ImGuiSelectableFlags.AllowOverlap, Vector2.One * thumbnailSize);
        GUIHelper.ItemRect(0.5f, 0.5f, 0.5f, 0.1f);
        if (ImGui.IsItemHovered())
        {
            if (entry is FileInfo)
            {
                if (ImGui.IsMouseClicked(0))
                    Selection.Select(entry.FullName);
            }
            else
            {
                // Folder selection is a bit differant, just clicking will select but keep the same directory
                // Double clicking will select and change the directory to the one clicked
                if (ImGui.IsMouseClicked(0))
                {
                    var old = CurDirectory;
                    Selection.Select(entry.FullName);
                    CurDirectory = old;
                }
                if (ImGui.IsMouseDoubleClicked(0))
                    CurDirectory = new DirectoryInfo(entry.FullName);
            }
        }
        GUIHelper.Tooltip(entry.Name);
        ImGui.PopStyleVar(2);

        Texture2D thumbnail = GetEntryThumbnail(entry);
        DrawThumbnailForEntry(thumbnail, thumbnailSize);

        if (entry is FileInfo file)
        {
            ImDrawListPtr drawList = ImGui.GetWindowDrawList();
            var lineColor = AssetsWindow.GetFileColor(file.Extension.ToLower().Trim());
            var pos = ImGui.GetCursorScreenPos();
            drawList.AddLine(new(0, pos.Y), new(pos.X + thumbnailSize, pos.Y + 1f), lineColor, 3f);
            ImGui.TextUnformatted(Settings.m_HideExtensions ? Path.GetFileNameWithoutExtension(entry.FullName) : Path.GetFileName(entry.FullName));
        }
        else
            ImGui.TextUnformatted(entry.Name);

        ImGui.EndGroup();
    }

    private void DrawThumbnailForEntry(Texture2D thumbnail, float thumbnailSize)
    {
        if (thumbnail == null) return;
        // Thumbnail should draw smaller to fit Text
        thumbnailSize -= 30;
        float thumbnailWidth = ((float)thumbnail.Width / thumbnail.Height) * thumbnailSize;
        float xOffset = ((thumbnailSize - thumbnailWidth) / 2) + 15;
        ImGui.SetCursorPos(new Vector2(xOffset, 10));
        ImGui.Image((IntPtr)thumbnail.Handle, new Vector2(thumbnailWidth, thumbnailSize), Vector2.UnitY, Vector2.UnitX);
    }

    private Texture2D GetEntryThumbnail(FileSystemInfo entry)
    {
        string fileName = "FileIcon.png";
        if (entry is DirectoryInfo directory)
        {
            fileName = directory.EnumerateFiles().Any() || directory.EnumerateDirectories().Any() ? "FolderFilledIcon.png" : "FolderEmptyIcon.png";
            if (!cachedThumbnails.ContainsKey(fileName))
            {
                using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Prowl.Editor.EmbeddedResources." + fileName);
                cachedThumbnails[fileName] = new Texture2D(stream);
                cachedThumbnails[fileName].SetFilter(Raylib_cs.TextureFilter.TEXTURE_FILTER_BILINEAR);
            }
        }
        else if (entry is FileInfo file)
        {
            if (lastGenerated.Item1 != Time.frameCount || !lastGenerated.Item2)
            {
                if (TextureImporter.Supported.Contains(file.Extension, StringComparer.OrdinalIgnoreCase))
                {
                    string? relativeAssetPath = AssetDatabase.GetRelativePath(file.FullName);

                    if (cachedThumbnails.TryGetValue(file.FullName, out Texture2D? value))
                        return value;

#warning TODO: if the texture at path changes this needs to somehow know and update

                    if (relativeAssetPath != null) // if its null fallback to the default FileIcon
                    {
                        lastGenerated = (Time.frameCount, true);
                        var tex = Application.AssetProvider.LoadAsset<Texture2D>(relativeAssetPath);
                        cachedThumbnails[file.FullName] = tex;
                        return tex;
                    }
                }
                else if (ImporterAttribute.SupportsExtension(file.Extension))
                {
                    fileName = ImporterAttribute.GetIconForExtension(file.Extension);
                }
                else if (file.Extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)) // Special case, is a c# script
                    fileName = "CSharpIcon.png";
            }
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
}
