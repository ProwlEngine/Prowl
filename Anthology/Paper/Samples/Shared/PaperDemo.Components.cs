using FontStashSharp;
using Prowl.PaperUI;
using Prowl.PaperUI.Extras;
using Prowl.Vector;

using System.Drawing;
using System.Reflection;
using System.Data.Common;

namespace Shared
{
    public static class Components
    {
        public static void DefineStyles()
        {
            Button.DefineStyles();
            Input.DefineStyles();
        }
    }

    public static class Button
    {
        public static void DefineStyles()
        {
            // Primary button variant
            Paper.CreateStyleFamily("shadcs-button-primary")
                .Base(new StyleTemplate()
                    .Height(50)
                    .Rounded(8)
                    .BackgroundColor(Themes.primaryColor)
                    .Transition(GuiProp.BackgroundColor, 0.2)
                    .Transition(GuiProp.ScaleX, 0.1)
                    .Transition(GuiProp.ScaleY, 0.1))
                .Hovered(new StyleTemplate()
                    .BackgroundColor(Themes.secondaryColor))
                .Active(new StyleTemplate()
                    .Scale(0.95)
                    .BackgroundColor(Color.FromArgb(200, Themes.primaryColor)))
                .Register();

            // Icon button styles
            Paper.CreateStyleFamily("shadcs-icon-button-primary")
                .Base(new StyleTemplate()
                    .Width(40)
                    .Height(40)
                    .Rounded(8)
                    .BackgroundColor(Color.FromArgb(50, 0, 0, 0))
                    .Transition(GuiProp.BackgroundColor, 0.2)
                    .Transition(GuiProp.Rounded, 0.2))
                .Hovered(new StyleTemplate()
                    .BackgroundColor(Color.FromArgb(100, Themes.primaryColor))
                    .Rounded(20))
                .Register();
        }

        public static ElementBuilder Primary(string id, string text = "")
        {
            return PaperDemo.P.Box("shadcs-button-" + id)
                .Text(Text.Center(text, Fonts.fontMedium, Themes.lightTextColor))
                .Style("shadcs-button-primary");
        }

        public static ElementBuilder IconPrimary(string id, string text = "")
        {
            return Paper.Box("shadcs-button-" + id)
                .Text(Text.Center(text, Fonts.fontMedium, Themes.lightTextColor))
                .Style("shadcs-icon-button-primary");
        }
    }

    public static class Input
    {
        public static void DefineStyles()
        {
            // Text field styles
            Paper.CreateStyleFamily("text-field")
                .Base(new StyleTemplate()
                    .Width(300)
                    .Height(40)
                    .Rounded(8)
                    .BackgroundColor(Color.FromArgb(50, 0, 0, 0))
                    .BorderColor(Color.Transparent)
                    .BorderWidth(0)
                    .Transition(GuiProp.BorderColor, 0.2)
                    .Transition(GuiProp.BorderWidth, 0.2)
                    .Transition(GuiProp.BackgroundColor, 0.2))
                .Focused(new StyleTemplate()
                    .BorderColor(Themes.primaryColor)
                    .BorderWidth(2)
                    .BackgroundColor(Color.FromArgb(80, 0, 0, 0)))
                .Register();

        }

        public static ElementBuilder Primary(string id, string value, Action<string> onChange = null, string placeholder = "")
        {
            return PaperDemo.P.Box("shadcs-input-" + id)
                .TextField(value, Fonts.fontMedium, onChange, null, placeholder)
                .Style("text-field")
                .SetScroll(Scroll.ScrollX);
        }
    }
}
