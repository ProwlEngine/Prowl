// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Assets;
using Prowl.Editor.Preferences;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;

namespace Prowl.Editor;

public class AssetSelectorWindow : EditorWindow
{
    private string _searchText = "";
    private readonly Type type;
    private readonly Action<Guid, ushort> _onAssetSelected;

    protected override bool Center { get; } = true;
    protected override double Width { get; } = 512;
    protected override double Height { get; } = 512;
    protected override bool BackgroundFade { get; } = true;
    protected override bool IsDockable => false;
    protected override bool LockSize => true;

    public AssetSelectorWindow(Type type, Action<Guid, ushort> onAssetSelected) : base()
    {
        Title = FontAwesome6.Book + " Asset Selector";
        this.type = type;
        _onAssetSelected = onAssetSelected;
    }

    protected override void Draw()
    {
        double ItemSize = EditorStylePrefs.Instance.ItemSize;

        gui.CurrentNode.Layout(LayoutType.Column);
        gui.CurrentNode.ScaleChildren();
        gui.CurrentNode.Padding(0, 10, 10, 10);

        using (gui.Node("Search").Width(Size.Percentage(1f)).MaxHeight(ItemSize).Clip().Enter())
        {
            gui.Search("SearchInput", ref _searchText, 0, 0, Size.Percentage(1f), ItemSize);
        }

        using (gui.Node("Body").Width(Size.Percentage(1f)).MarginTop(5).Layout(LayoutType.Column).Clip().Scroll().Enter())
        {
            double xPos = gui.CurrentNode.LayoutData.InnerRect.x + 3;
            using (gui.Node("None", -1).Width(Size.Percentage(1f)).Height(ItemSize).Enter())
            {
                var interact = gui.GetInteractable();
                if (interact.TakeFocus())
                {
                    _onAssetSelected(Guid.Empty, 0);
                    isOpened = false;
                }

                if (interact.IsHovered())
                    gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Hovering);

                gui.Draw2D.DrawText(Font.DefaultFont, "None", 20, new Vector2(xPos, gui.CurrentNode.LayoutData.Rect.y + 7), Color.white);
            }

            var assets = AssetDatabase.GetAllAssetsOfType(type);
            int i = 0; // Used to help the ID's of the nodes, Ensures every node has a unique ID
            foreach (var asset in assets)
            {
                if (AssetDatabase.TryGetFile(asset.Item2, out var file))
                {
                    if (string.IsNullOrEmpty(_searchText) || file.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
                    {
                        // just using the index as an id, unique we don't need both string and int id
                        using (gui.Node("", i++).Width(Size.Percentage(1f)).Height(ItemSize).Enter())
                        {
                            var interact = gui.GetInteractable();
                            if (interact.TakeFocus())
                            {
                                _onAssetSelected(asset.Item2, asset.Item3);
                                isOpened = false;
                            }

                            if (interact.IsHovered())
                                gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Hovering, (float)EditorStylePrefs.Instance.AssetRoundness);

                            gui.Draw2D.DrawText(Font.DefaultFont, AssetDatabase.GetRelativePath(file.FullName) + "." + asset.Item1, 20, new Vector2(xPos, gui.CurrentNode.LayoutData.Rect.y + 7), Color.white);
                        }
                    }
                }
            }
        }

        // Clicked outside Window
        if (gui.IsPointerClick(MouseButton.Left) && !gui.IsPointerHovering())
            isOpened = false;
    }
}
