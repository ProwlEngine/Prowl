using Hexa.NET.ImGui;
using Prowl.Editor.Assets;
using Prowl.Editor.ImGUI.Widgets;
using Prowl.Icons;
using Prowl.Runtime;
using System.Reflection;

namespace Prowl.Editor.EditorWindows;

public class AssetBrowserWindow : EditorWindow
{
    public static EditorSettings Settings => Project.ProjectSettings.GetSetting<EditorSettings>();

    public DirectoryInfo CurDirectory;
    public bool Locked = false;

    private string _searchText = "";
    private readonly List<FileSystemInfo> _found = new();
    private readonly Dictionary<string, AssetRef<Texture2D>> _cachedThumbnails = new();
    private static (long, bool) _lastGenerated = (-1, false);
    internal static string? RenamingEntry = null;

    private const float PingDuration = 3f;
    private float _pingTimer = 0;
    private FileInfo _pingedFile;

    protected override ImGuiWindowFlags Flags => ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse;

    private float ThumbnailSize => (1.0f + Settings.m_ThumbnailSize) * 90f;

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

    private void OnAssetPinged(FileInfo assetPath)
    {
        _pingTimer = PingDuration;
        _pingedFile = assetPath;
        CurDirectory = _pingedFile.Directory;
    }

    public static void StartRename(string? entry)
    {
        RenamingEntry = entry;
    }

    protected override void Draw()
    {
        if (!Project.HasProject)
            return;

        ImGui.PushStyleColor(ImGuiCol.Header, EditorGui.SelectedColor);
        // Ensure we always have a Directory, if the current one is deleted move to its parent
        // if theres no parent move to the Assets Directory
        // If theres no project directory well why the hell are we here? the line above should have stopped us
        while (!Path.Exists(CurDirectory.FullName))
            CurDirectory = CurDirectory.Parent ?? new DirectoryInfo(Project.ProjectAssetDirectory);

        RenderHeader();
        ImGui.BeginChild("Body");
        RenderBody();
        ImGui.EndChild();
        AssetsWindow.HandleFileContextMenu(null, CurDirectory, true);

        if (DragnDrop.ReceiveReference<GameObject>(out var go))
        {
            var prefab = new Prefab {
                GameObject = Serializer.Serialize(go),
                Name = go.Name
            };
            FileInfo file = new FileInfo(CurDirectory + $"/{prefab.Name}.prefab");
            while (File.Exists(file.FullName))
                file = new FileInfo(file.FullName.Replace(".prefab", "") + " new.prefab");

            StringTagConverter.WriteToFile(Serializer.Serialize(prefab), file);

            AssetDatabase.Update();
            AssetDatabase.Ping(file);
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
        if (Locked)
            return;

        if (to is DirectoryInfo directory)
            CurDirectory = directory;
        else if (to is FileInfo file)
            CurDirectory = file.Directory;
    }

    private void RenderHeader()
    {
        float windowWidth = ImGui.GetWindowWidth();

        const int searchBarSize = 125;
        const int sizeSliderSize = 75;
        const int padding = 5;
        const int rightOffset = 43;

        bool disabledUpButton = CurDirectory.FullName.Equals(Project.ProjectAssetDirectory, StringComparison.OrdinalIgnoreCase)
            || CurDirectory.FullName.Equals(Project.ProjectDefaultsDirectory, StringComparison.OrdinalIgnoreCase)
            || CurDirectory.FullName.Equals(Project.ProjectPackagesDirectory, StringComparison.OrdinalIgnoreCase)
            || CurDirectory.Parent == null;

        if (disabledUpButton)
            ImGui.BeginDisabled(true);
        if (ImGui.Button(FontAwesome6.ArrowUp))
            CurDirectory = CurDirectory.Parent!;
        if (disabledUpButton)
            ImGui.EndDisabled();

        ImGui.SameLine();

        float cPX = ImGui.GetCursorPosX();
        float cPY = ImGui.GetCursorPosY();
        if (GUIHelper.Search("##searchBox", ref _searchText, searchBarSize))
        {
            _found.Clear();
            if (!string.IsNullOrEmpty(_searchText))
            {
                _found.AddRange(CurDirectory.EnumerateFiles("*", SearchOption.AllDirectories));
                _found.AddRange(CurDirectory.EnumerateDirectories("*", SearchOption.AllDirectories));
                _found.RemoveAll(f => f.Extension.Equals(".meta", StringComparison.OrdinalIgnoreCase) || !f.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase));
            }
        }

        if (!Project.HasProject)
            return;

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
        var start = ImGui.GetCursorScreenPos();
        var size = ImGui.GetContentRegionAvail();

        int rowCount = Math.Max((int)(contentWidth / (ThumbnailSize + padding)), 1);
        float itemSize = ThumbnailSize + padding;

        var curPos = ImGui.GetCursorPos() + new System.Numerics.Vector2(5, 5);
        int i = 0;
        if (!string.IsNullOrEmpty(_searchText))
        {
            foreach (var entry in _found)
                RenderEntry(rowCount, itemSize, ref curPos, ref i, entry);
        }
        else
        {
            var directories = CurDirectory.GetDirectories();
            foreach (var folder in directories)
            {
                if (folder.Exists)
                    RenderEntry(rowCount, itemSize, ref curPos, ref i, folder);
            }
            var files = CurDirectory.GetFiles();
            foreach (var file in files)
            {
                if (file.Exists && !file.Extension.Equals(".meta", StringComparison.OrdinalIgnoreCase))
                    RenderEntry(rowCount, itemSize, ref curPos, ref i, file);
            }
        }

        // Background rect for entire body
        var drawList = ImGui.GetWindowDrawList();
        var min = start;
        var max = start + size;
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.FrameBg), 0f);
    }

    private void RenderEntry(int rowCount, float itemSize, ref System.Numerics.Vector2 curPos, ref int i, FileSystemInfo entry)
    {
        ImGui.PushID(i);
        ImGui.SetCursorPos(curPos);
        ImGui.BeginChild("ClipBox", new System.Numerics.Vector2(ThumbnailSize, ThumbnailSize), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        RenderFileSystemEntry(entry);
        ImGui.EndChild();

        if (_pingTimer > 0 && _pingedFile.FullName.Equals(entry.FullName, StringComparison.OrdinalIgnoreCase))
        {
            _pingTimer -= Time.deltaTimeF;
            if (_pingTimer > PingDuration - 1f)
            {
                CurDirectory = _pingedFile.Directory;
                ImGui.ScrollToItem(ImGuiScrollFlags.None);
            }
            GUIHelper.ItemRect(1f, 0.8f, 0.0f, 0.8f, MathF.Sin(_pingTimer) * 1f, 3f, 2.5f);
            GUIHelper.ItemRect(1f, 0.8f, 0.0f, 0.8f, MathF.Sin(_pingTimer) * 6f, 3f, 2.5f);
        }

        AssetsWindow.HandleFileContextMenu(entry, CurDirectory, true);
        ImGui.PopID();

        curPos.X = 5 + ((i + 1) % rowCount) * itemSize;
        curPos.Y = 5 + ((i + 1) / rowCount) * itemSize;
        i++;
    }

    private void RenderFileSystemEntry(FileSystemInfo entry)
    {
        if (!entry.Exists)
            return;

        bool isSelected = AssetsWindow.SelectHandler.IsSelected(entry);
        float thumbnailSize = Math.Min(ThumbnailSize, ImGui.GetContentRegionAvail().X);
        ImGui.BeginGroup();

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new System.Numerics.Vector2(0.0f, 4.0f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0.0f);

        ImGui.Selectable("##" + entry.FullName, isSelected, ImGuiSelectableFlags.AllowOverlap, System.Numerics.Vector2.One * thumbnailSize);

        var gradientStart = ImGui.GetColorU32(new System.Numerics.Vector4(0.2f, 0.2f, 0.2f, 0.8f));
        var gradientEnd = ImGui.GetColorU32(new System.Numerics.Vector4(0.1f, 0.1f, 0.1f, 0.8f));
        if (entry is FileInfo f)
        {
            gradientStart = AssetsWindow.GetFileColor(f.Extension.ToLower().Trim(), 0.2f);
            gradientEnd = AssetsWindow.GetFileColor(f.Extension.ToLower().Trim(), 0.8f);
        }

        // Draw a gradient background
        var drawList = ImGui.GetWindowDrawList();
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        //drawList.AddRectFilledMultiColor(min, max, ImGui.GetColorU32(gradientStart), ImGui.GetColorU32(gradientStart), ImGui.GetColorU32(gradientEnd), ImGui.GetColorU32(gradientEnd));
        //drawList.AddRectFilledMultiColor
        //GUIHelper.ItemRectFilled(gradientStart, 0, 8f); 
        
        int vertStartIdx = drawList.VtxBuffer.Size;
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(gradientStart), 8f);
        int vertEndIdx = drawList.VtxBuffer.Size;
        ImGui.ShadeVertsLinearColorGradientKeepAlpha(drawList, vertStartIdx, vertEndIdx, min, new System.Numerics.Vector2(min.X, max.Y), gradientStart, gradientEnd);

        if (ImGui.IsItemHovered())
        {
            if (entry is FileInfo fileInfo)
                AssetsWindow.HandleFileClick(fileInfo);
            else
            {
                if (ImGui.IsMouseClicked(0))
                {
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

        var thumbnail = GetEntryThumbnail(entry);
        if (thumbnail.IsAvailable)
            DrawThumbnailForEntry(thumbnail.Res!, thumbnailSize);

        ImGui.PushID(entry.FullName);
        if (entry is FileInfo file)
        {
            var pos = ImGui.GetCursorScreenPos();
            drawList.AddLine(new(0, pos.Y), new(pos.X + thumbnailSize, pos.Y + 1f), gradientStart, 3f);

            if (RenamingEntry == entry.FullName)
            {
                string newName = entry.Name;
                ImGui.SetNextItemWidth(thumbnailSize);
                if (ImGui.InputText("##Rename", ref newName, 255, ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll))
                {
                    string newPath = Path.Combine(file.Directory.FullName, newName);
                    if (File.Exists(newPath))
                        EditorGui.Notify("A file with the same name already exists.");
                    else
                    {
                        AssetDatabase.Rename(file, newName);
                    }
                    RenamingEntry = null;
                }
                if (!ImGui.IsItemActive() && (ImGui.IsMouseClicked(ImGuiMouseButton.Left) || ImGui.IsMouseClicked(ImGuiMouseButton.Right)))
                    RenamingEntry = null;
                ImGui.SetKeyboardFocusHere(-1);
            }
            else
            {
                var text = Settings.m_HideExtensions ? Path.GetFileNameWithoutExtension(entry.FullName) : Path.GetFileName(entry.FullName);
                GUIHelper.TextCenter(text);
                if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(0))
                    RenamingEntry = entry.FullName;
            }
        }
        else
        {
            if (RenamingEntry == entry.FullName)
            {
                string newName = entry.Name;
                ImGui.SetNextItemWidth(thumbnailSize);
                if (ImGui.InputText("##Rename", ref newName, 255, ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll))
                {
                    string newPath = Path.Combine((entry as DirectoryInfo).Parent.FullName, newName);
                    if (Directory.Exists(newPath))
                        EditorGui.Notify("A directory with the same name already exists.");
                    else
                    {
                        (entry as DirectoryInfo).MoveTo(newPath);
                    }
                    RenamingEntry = null;
                }
                if (!ImGui.IsItemActive() && (ImGui.IsMouseClicked(ImGuiMouseButton.Left) || ImGui.IsMouseClicked(ImGuiMouseButton.Right)))
                    RenamingEntry = null;
                ImGui.SetKeyboardFocusHere(-1);
            }
            else
            {
                GUIHelper.TextCenter(entry.Name);
                if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(0))
                    RenamingEntry = entry.FullName;
            }

        }
        ImGui.PopID();

        ImGui.EndGroup();
    }

    private void DrawThumbnailForEntry(Texture2D thumbnail, float thumbnailSize)
    {
        if (thumbnail == null)
            return;

        thumbnailSize -= 30;
        float thumbnailWidth = ((float)thumbnail.Width / thumbnail.Height) * thumbnailSize;
        float xOffset = ((thumbnailSize - thumbnailWidth) / 2) + 15;
        ImGui.SetCursorPos(new System.Numerics.Vector2(xOffset, 10));
        ImGui.Image((IntPtr)thumbnail.Handle, new System.Numerics.Vector2(thumbnailWidth, thumbnailSize), System.Numerics.Vector2.UnitY, System.Numerics.Vector2.UnitX);
    }

    private AssetRef<Texture2D> GetEntryThumbnail(FileSystemInfo entry)
    {
        string fileName = "FileIcon.png";

        if (entry is DirectoryInfo directory)
        {
            fileName = directory.EnumerateFiles().Any() || directory.EnumerateDirectories().Any() ? "FolderFilledIcon.png" : "FolderEmptyIcon.png";
            if (!_cachedThumbnails.ContainsKey(fileName))
            {
                using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Prowl.Editor.EmbeddedResources." + fileName);
                _cachedThumbnails[fileName] = Texture2DLoader.FromStream(stream);
            }
        }
        else if (entry is FileInfo file)
        {
            if (_lastGenerated.Item1 != Time.frameCount || !_lastGenerated.Item2)
            {
                if (TextureImporter.Supported.Contains(file.Extension, StringComparer.OrdinalIgnoreCase))
                {
                    string relativeAssetPath = AssetDatabase.GetRelativePath(file.FullName);
                    if (_cachedThumbnails.TryGetValue(file.FullName, out var value))
                        return value;

                    if (relativeAssetPath != null)
                    {
                        _lastGenerated = (Time.frameCount, true);
                        var tex = Application.AssetProvider.LoadAsset<Texture2D>(relativeAssetPath);
                        if (tex.IsAvailable)
                        {
                            _cachedThumbnails[file.FullName] = tex;
                            return tex.Res!;
                        }
                    }
                }
                else if (ImporterAttribute.SupportsExtension(file.Extension))
                    fileName = ImporterAttribute.GetIconForExtension(file.Extension);
                else if (file.Extension.Equals(".cs", StringComparison.OrdinalIgnoreCase))
                    fileName = "CSharpIcon.png";
            }
        }

        if (!_cachedThumbnails.ContainsKey(fileName))
        {
            _lastGenerated = (Time.frameCount, true);
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Prowl.Editor.EmbeddedResources." + fileName))
                _cachedThumbnails[fileName] = Texture2DLoader.FromStream(stream);
        }
        return _cachedThumbnails[fileName].Res;
    }
}