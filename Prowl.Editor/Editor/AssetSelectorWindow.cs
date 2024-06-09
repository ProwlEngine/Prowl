using Prowl.Editor.Assets;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.GUI.Graphics;

namespace Prowl.Editor
{
    public class AssetSelectorWindow : EditorWindow
    {
        private string _searchText = "";
        private Type type;
        private Action<Guid, ushort> _onAssetSelected;

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
            gui.CurrentNode.Layout(LayoutType.Column);
            gui.CurrentNode.ScaleChildren();
            gui.CurrentNode.Padding(0, 10, 10, 10);

            using (gui.Node("Search").Width(Size.Percentage(1f)).MaxHeight(GuiStyle.ItemHeight).Clip().Enter())
            {
                gui.Search("SearchInput", ref _searchText, 0, 0, Size.Percentage(1f), GuiStyle.ItemHeight);
            }

            using (gui.Node("Body").Width(Size.Percentage(1f)).MarginTop(5).Layout(LayoutType.Column).Enter())
            {
                double xPos = gui.CurrentNode.LayoutData.InnerRect.x + 3;
                using (gui.Node("None", -1).Width(Size.Percentage(1f)).Height(GuiStyle.ItemHeight).Enter())
                {
                    var interact = gui.GetInteractable();
                    if (interact.TakeFocus())
                    {
                        _onAssetSelected(Guid.Empty, 0);
                        isOpened = false;
                    }

                    if (interact.IsHovered())
                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GuiStyle.Base5);

                    gui.Draw2D.DrawText(UIDrawList.DefaultFont, "None", 20, new Vector2(xPos, gui.CurrentNode.LayoutData.Rect.y + 7), GuiStyle.Base10);
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
                            using (gui.Node("", i++).Width(Size.Percentage(1f)).Height(GuiStyle.ItemHeight).Enter())
                            {
                                var interact = gui.GetInteractable();
                                if (interact.TakeFocus())
                                {
                                    _onAssetSelected(asset.Item2, asset.Item3);
                                    isOpened = false;
                                }

                                if (interact.IsHovered())
                                    gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, GuiStyle.Base5);

                                gui.Draw2D.DrawText(UIDrawList.DefaultFont, AssetDatabase.GetRelativePath(file.FullName) + "." + asset.Item1, 20, new Vector2(xPos, gui.CurrentNode.LayoutData.Rect.y + 7), GuiStyle.Base10);
                            }
                        }
                    }
                }
            }

            // Clicked outside Window
            if (gui.IsPointerClick(Veldrid.MouseButton.Left) && !gui.IsPointerHovering())
                isOpened = false;
        }
    }
}