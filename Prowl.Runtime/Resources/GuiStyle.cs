using Prowl.Runtime.GUI.Graphics;

namespace Prowl.Runtime
{
    // TODO: Improve the usage of this, Right now my focus is just getting all the UI and style in, then I can go back and clean up this code and use it properly everywhere for styling

    public sealed class GuiStyle : EngineObject
    {
        // Scales/Offsets
        public const double ItemHeight = 30;
        public const double ItemPadding = 4;

        // Base Colors 
        public static Color Black => new(0, 0, 0, 255);
        public static Color Background => new(15, 15, 18);
        public static Color WindowBackground => new(31, 33, 40);
        public static Color Borders => new(49, 52, 66); // Border
        public static Color Base4 => new(100, 100, 110);
        public static Color Base5 => new(139, 139, 147);
        public static Color Base6 => new(112, 112, 124);
        public static Color Base7 => new(138, 138, 152);
        public static Color Base8 => new(169, 169, 183);
        public static Color Base9 => new(208, 208, 218);
        public static Color Base10 => new(234, 234, 244);
        public static Color Base11 => new(255, 255, 255);

        // Accents
        public static Color Blue => new(39, 117, 255);
        public static Color Green => new(80, 209, 178);
        public static Color Violet => new(119, 71, 202);
        public static Color Orange => new(236, 140, 85);
        public static Color Yellow => new(236, 230, 99);
        public static Color Indigo => new(84, 21, 241);
        public static Color Emerald => new(94, 234, 141);
        public static Color Fuchsia => new(221, 80, 214);
        public static Color Red => new(226, 55, 56);
        public static Color Sky => new(11, 214, 244);
        public static Color Pink => new(251, 123, 184);

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

        public static Color RandomPastelColor(int seed)
        {
            System.Random random = new System.Random(seed);
            float r = (float)(random.NextDouble() * 0.5 + 0.5);
            float g = (float)(random.NextDouble() * 0.5 + 0.5);
            float b = (float)(random.NextDouble() * 0.5 + 0.5);
            return new Color(r, g, b) * 0.8f;
        }

    }

}
