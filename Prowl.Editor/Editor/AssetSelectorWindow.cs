using Hexa.NET.ImGui;
using Prowl.Editor.Assets;
using Prowl.Icons;

namespace Prowl.Editor.EditorWindows;

public class AssetSelectorWindow : EditorWindow
{
    private string _searchText = "";
    private Type type;
    private Action<Guid> _onAssetSelected;

    protected override ImGuiWindowFlags Flags { get; } = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.Tooltip | ImGuiWindowFlags.NoDecoration;
    protected override bool Center { get; } = true;
    protected override int Width { get; } = 512;
    protected override int Height { get; } = 512;
    protected override bool BackgroundFade { get; } = true;

    public AssetSelectorWindow(Type type, Action<Guid> onAssetSelected) : base()
    {
        Title = FontAwesome6.Book + " Assets Selection";
        this.type = type;
        _onAssetSelected = onAssetSelected;
    }

    protected override void Draw()
    {
        ImGui.BeginChild("assetChild");

        GUIHelper.Search("##searchBox", ref _searchText, ImGui.GetContentRegionAvail().X);

        ImGui.PushStyleColor(ImGuiCol.ChildBg, ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg]);
        ImGui.BeginChild("assetList", new System.Numerics.Vector2(ImGui.GetWindowWidth(), ImGui.GetWindowHeight() - 29));

        if (ImGui.Selectable("  None", false, ImGuiSelectableFlags.None, new System.Numerics.Vector2(ImGui.GetWindowWidth(), 21)))
        {
            _onAssetSelected(Guid.Empty);
            isOpened = false;
        }

        ImGui.Separator();

        var assets = AssetDatabase.GetAllAssetsOfType(type);
        foreach (var asset in assets)
        {
            if (AssetDatabase.TryGetFile(asset, out var file))
            {
                if (string.IsNullOrEmpty(_searchText) || file.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
                {
                    // Selectable
                    if (ImGui.Selectable("  " + AssetDatabase.GetRelativePath(file.FullName), false, ImGuiSelectableFlags.None, new System.Numerics.Vector2(ImGui.GetWindowWidth(), 21)))
                    {
                        _onAssetSelected(asset);
                        isOpened = false;
                    }
                    ImGui.Separator();
                }
            }
        }

        ImGui.EndChild();

        ImGui.PopStyleColor();
        ImGui.EndChild();

        // Click outside window should close it
        if (ImGui.IsMouseDown(0) && !ImGui.IsMouseHoveringRect(ImGui.GetWindowPos(), ImGui.GetWindowPos() + ImGui.GetWindowSize(), false))
            isOpened = false;
    }
}
