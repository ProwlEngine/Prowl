using HexaEngine.ImGuiNET;
using Prowl.Editor.Assets;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.Assets;
using Prowl.Runtime.ImGUI.Widgets;
using Prowl.Runtime.SceneManagement;
using System.Reflection;

namespace Prowl.Editor.EditorWindows;

public class AssetBrowserWindow : EditorWindow {

    public static EditorSettings Settings => Project.ProjectSettings.GetSetting<EditorSettings>();

    public DirectoryInfo CurDirectory;
    public bool Locked = false;

    private string _searchText = "";
    private readonly List<FileSystemInfo> _found = [];
    private readonly Dictionary<string, Texture2D> cachedThumbnails = new();
    private static (long, bool) lastGenerated = (-1, false);

    protected override ImGuiWindowFlags Flags => ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse;

    private float ThumbnailSize => (1.0f + Settings.m_ThumbnailSize) * 65f;


    private const float PingDuration = 3f;
    private float pingTimer = 0;
    private FileInfo pingedFile;

    public AssetBrowserWindow() : base()
    {
        Title = FontAwesome6.BoxOpen + " Asset Browser";
        Project.OnProjectChanged += Invalidate;
        AssetsWindow.SelectHandler.OnSelectObject += SelectionChanged;
        AssetDatabase.Pinged += OnAssetPinged;
        Invalidate();
    }

    ~AssetBrowserWindow()
    {
        Project.OnProjectChanged -= Invalidate;
        AssetsWindow.SelectHandler.OnSelectObject -= SelectionChanged;
        AssetDatabase.Pinged -= OnAssetPinged;
    }

    private void OnAssetPinged(string relativeAssetPath)
    {
        pingTimer = PingDuration;
        pingedFile = AssetDatabase.RelativeToFile(relativeAssetPath);
        CurDirectory = pingedFile.Directory;
    }

    protected override void Draw()
    {
        if (Project.HasProject == false) return;

        ImGui.PushStyleColor(ImGuiCol.Header, EditorGui.SelectedColor);
        // Ensure we always have a Directory, if the current one is deleted move to its parent
        // if theres no parent move to the Assets Directory
        // If theres no project directory well why the hell are we here? the line above should have stopped us
        while (CurDirectory?.Exists == false)
            CurDirectory = CurDirectory.Parent ?? new DirectoryInfo(Project.ProjectAssetDirectory);

        RenderHeader();
        ImGui.BeginChild("Body");
        RenderBody();
        ImGui.EndChild();
        AssetsWindow.HandleFileContextMenu(null);
        if (DragnDrop.ReceiveReference<GameObject>(out var go)) {
            // Create Prefab
            var prefab = new Prefab();
            prefab.GameObject = (CompoundTag)TagSerializer.Serialize(go);
            prefab.Name = go.Name;
            FileInfo file = new FileInfo(CurDirectory + $"/{prefab.Name}.prefab");
            while (file.Exists) {
                file = new FileInfo(file.FullName.Replace(".prefab", "") + " new.prefab");
            }
            StringTagConverter.WriteToFile((CompoundTag)TagSerializer.Serialize(prefab), file);

            var r = AssetDatabase.FileToRelative(file);
            AssetDatabase.Reimport(r);
            AssetDatabase.Ping(r);
        }

        if (!AssetsWindow.SelectHandler.SelectedThisFrame && ImGui.IsItemClicked(0))
            AssetsWindow.SelectHandler.Clear();
        ImGui.PopStyleColor();
    }

    public void Invalidate()
    {
        CurDirectory = new DirectoryInfo(Project.ProjectAssetDirectory);
    }

    private void SelectionChanged(object to)
    {
        if (Locked) return;
        if (to is DirectoryInfo directory) CurDirectory = directory;
        else if (to is FileInfo file) CurDirectory = file.Directory;
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
        disabledUpButton |= CurDirectory.FullName.Equals(Project.ProjectPackagesDirectory, StringComparison.OrdinalIgnoreCase);
        disabledUpButton |= CurDirectory.Parent == null;

        if (disabledUpButton) ImGui.BeginDisabled(true);
        if (ImGui.Button(FontAwesome6.ArrowUp))
            CurDirectory = CurDirectory.Parent!;
        if (disabledUpButton) ImGui.EndDisabled();

        ImGui.SameLine();

        float cPX = ImGui.GetCursorPosX();
        float cPY = ImGui.GetCursorPosY();
        if (GUIHelper.Search("##searchBox", ref _searchText, searchBarSize)) {
            _found.Clear();
            if (!string.IsNullOrEmpty(_searchText)) {
                _found.AddRange(CurDirectory.EnumerateFiles("*", SearchOption.AllDirectories));
                _found.AddRange(CurDirectory.EnumerateDirectories("*", SearchOption.AllDirectories));
                // Remove Meta's & only keep the ones with SearchText inside them
                _found.RemoveAll(f => f.Extension.Equals(".meta", StringComparison.OrdinalIgnoreCase) || !f.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase));
            }
        }

        if (Project.HasProject == false) return;

        ImGui.SetCursorPosY(cPY);
        ImGui.SetCursorPosX(cPX + searchBarSize + padding);
        string assetPath = Path.GetRelativePath(Project.ProjectDirectory, CurDirectory.FullName);
        ImGui.Text(assetPath);

        ImGui.SetCursorPosY(cPY);
        ImGui.SetCursorPosX(windowWidth - rightOffset - sizeSliderSize - padding - 30);
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

        var curPos = ImGui.GetCursorPos() + new System.Numerics.Vector2(5, 5);
        int i = 0;
        if (!string.IsNullOrEmpty(_searchText)) {
            // Show only Filters elements
            foreach (var entry in _found)
                RenderEntry(rowCount, itemSize, ref curPos, ref i, entry);
        } else {
            var directories = CurDirectory.GetDirectories();
            foreach (var folder in directories) {
                if (!folder.Exists) return;
                RenderEntry(rowCount, itemSize, ref curPos, ref i, folder);
            }
            var files = CurDirectory.GetFiles();
            foreach (var file in files) {
                if (!file.Exists) return;
                if (file.Extension.Equals(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                RenderEntry(rowCount, itemSize, ref curPos, ref i, file);
            }
        }
    }

    private void RenderEntry(int rowCount, float itemSize, ref System.Numerics.Vector2 curPos, ref int i, FileSystemInfo entry)
    {
        ImGui.PushID(i);
        ImGui.SetCursorPos(curPos);
        ImGui.BeginChild("ClipBox", new System.Numerics.Vector2(ThumbnailSize, ThumbnailSize), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        RenderFileSystemEntry(entry);
        ImGui.EndChild();

        // Ping Rendering
        if (pingTimer > 0 && pingedFile.FullName.Equals(entry.FullName, StringComparison.OrdinalIgnoreCase)) {
            pingTimer -= Time.deltaTimeF;
            if (pingTimer > PingDuration - 1f) {
                // For the first second lock scroll to the target file and directory
                CurDirectory = pingedFile.Directory;
                ImGui.ScrollToItem(ImGuiScrollFlags.None);
            }
            GUIHelper.ItemRect(1f, 0.8f, 0.0f, 0.8f, MathF.Sin(pingTimer) * 1f, 3f, 2.5f);
            GUIHelper.ItemRect(1f, 0.8f, 0.0f, 0.8f, MathF.Sin(pingTimer) * 6f, 3f, 2.5f);
        }

        AssetsWindow.HandleFileContextMenu(entry);
        ImGui.PopID();

        curPos.X = 5 + ((i + 1) % rowCount) * itemSize;
        curPos.Y = 5 + ((i + 1) / rowCount) * itemSize;
        i++;
    }

    private void RenderFileSystemEntry(FileSystemInfo entry)
    {
        if (!entry.Exists) return;

        bool isSelected = AssetsWindow.SelectHandler.IsSelected(entry);
        float thumbnailSize = Math.Min(ThumbnailSize, ImGui.GetContentRegionAvail().X);
        ImGui.BeginGroup();

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new System.Numerics.Vector2(0.0f, 4.0f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0.0f);

        ImGui.Selectable("##" + entry.FullName, isSelected, ImGuiSelectableFlags.AllowOverlap, System.Numerics.Vector2.One * thumbnailSize);

        GUIHelper.ItemRectFilled(0.5f, 0.5f, 0.5f, 0.1f);

        if (ImGui.IsItemHovered()) {
            if (entry is FileInfo fileInfo) {
                AssetsWindow.HandleFileClick(fileInfo);
            } else {
                // Folder selection is a bit differant, just clicking will select but keep the same directory
                // Double clicking will select and change the directory to the one clicked
                if (ImGui.IsMouseClicked(0)) {
                    var old = CurDirectory;
                    AssetsWindow.SelectHandler.Select(entry);
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

        if (entry is FileInfo file) {
            ImDrawListPtr drawList = ImGui.GetWindowDrawList();
            var lineColor = AssetsWindow.GetFileColor(file.Extension.ToLower().Trim());
            var pos = ImGui.GetCursorScreenPos();
            drawList.AddLine(new(0, pos.Y), new(pos.X + thumbnailSize, pos.Y + 1f), lineColor, 3f);
            ImGui.TextUnformatted(Settings.m_HideExtensions ? Path.GetFileNameWithoutExtension(entry.FullName) : Path.GetFileName(entry.FullName));

        } else
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
        ImGui.SetCursorPos(new System.Numerics.Vector2(xOffset, 10));
        ImGui.Image((IntPtr)thumbnail.Handle, new System.Numerics.Vector2(thumbnailWidth, thumbnailSize), System.Numerics.Vector2.UnitY, System.Numerics.Vector2.UnitX);
    }

    private Texture2D GetEntryThumbnail(FileSystemInfo entry)
    {
        string fileName = "FileIcon.png";
        if (entry is DirectoryInfo directory) {
            fileName = directory.EnumerateFiles().Any() || directory.EnumerateDirectories().Any() ? "FolderFilledIcon.png" : "FolderEmptyIcon.png";
            if (!cachedThumbnails.ContainsKey(fileName)) {
                using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Prowl.Editor.EmbeddedResources." + fileName);
                cachedThumbnails[fileName] = Texture2D.FromStream(stream);
            }
        } else if (entry is FileInfo file) {
            if (lastGenerated.Item1 != Time.frameCount || !lastGenerated.Item2) {
                if (TextureImporter.Supported.Contains(file.Extension, StringComparer.OrdinalIgnoreCase)) {
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
                } else if (ImporterAttribute.SupportsExtension(file.Extension)) {
                    fileName = ImporterAttribute.GetIconForExtension(file.Extension);
                } else if (file.Extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)) // Special case, is a c# script
                    fileName = "CSharpIcon.png";
            }
        }

        if (!cachedThumbnails.ContainsKey(fileName)) {
            lastGenerated = (Time.frameCount, true);
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Prowl.Editor.EmbeddedResources." + fileName)) {
                cachedThumbnails[fileName] = Texture2D.FromStream(stream);
            }
        }
        return cachedThumbnails[fileName];
    }
}