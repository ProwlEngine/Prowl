using Prowl.Runtime.GUI.Graphics;

namespace Prowl.Runtime
{
    public sealed class GuiStyle : EngineObject
    {
        public static Color HoveredColor => new(0.19f, 0.37f, 0.55f, 1.00f);
        public static Color SelectedColor => new(0.06f, 0.53f, 0.98f, 1.00f);
        public static Color HeaderColor => new(0.08f, 0.08f, 0.09f, 1.00f);
        public static Color WindowBGColor => new(0.17f, 0.17f, 0.18f, 1.00f);
        public static Color FrameBGColor => new(0.10f, 0.11f, 0.11f, 1.00f);

        public AssetRef<Font> Font = UIDrawList.DefaultFont;

        // Text
        public float FontSize = 20;
        public Color TextColor = Color.white;
        public Color TextHighlightColor = SelectedColor;

        public Color Border = new(0.08f, 0.08f, 0.09f, 1.00f);
        public float BorderThickness = 1;

        public Color WidgetColor = new(0.24f, 0.24f, 0.25f, 1.00f);
        public float WidgetRoundness = 2;

        public Color BtnHoveredColor = HoveredColor;
        public Color BtnActiveColor = SelectedColor;

        public float ScrollBarRoundness = 10;
        public Color ScrollBarHoveredColor = HoveredColor;
        public Color ScrollBarActiveColor = SelectedColor;

    }

}
