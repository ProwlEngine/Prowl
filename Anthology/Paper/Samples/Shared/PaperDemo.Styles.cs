using FontStashSharp;
using Prowl.PaperUI;
using Prowl.PaperUI.Extras;
using Prowl.Vector;

using System.Drawing;
using System.Reflection;

namespace Shared
{
    public static partial class PaperDemo
    {
        private static void DefineStyles()
        {
            // Card styles with hover effects
            Paper.CreateStyleFamily("card")
                .Base(new StyleTemplate()
                    .BackgroundColor(cardBackground)
                    .Rounded(8)
                    .Transition(GuiProp.Rounded, 0.2)
                    .Transition(GuiProp.BorderColor, 0.3)
                    .Transition(GuiProp.BorderWidth, 0.2)
                    .Transition(GuiProp.ScaleX, 0.2)
                    .Transition(GuiProp.ScaleY, 0.2))
                .Hovered(new StyleTemplate()
                    .Rounded(12)
                    .BorderColor(primaryColor)
                    .BorderWidth(2)
                    .Scale(1.05))
                .Register();

            // Button styles
            Paper.CreateStyleFamily("button")
                .Base(new StyleTemplate()
                    .Height(40)
                    .Rounded(8)
                    .BackgroundColor(Color.FromArgb(50, 0, 0, 0))
                    .Transition(GuiProp.BackgroundColor, 0.2)
                    .Transition(GuiProp.Rounded, 0.2)
                    .Transition(GuiProp.ScaleX, 0.1)
                    .Transition(GuiProp.ScaleY, 0.1))
                .Hovered(new StyleTemplate()
                    .BackgroundColor(Color.FromArgb(100, primaryColor))
                    .Rounded(12))
                .Active(new StyleTemplate()
                    .Scale(0.95)
                    .BackgroundColor(Color.FromArgb(150, primaryColor)))
                .Register();

            // Primary button variant
            Paper.CreateStyleFamily("button-primary")
                .Base(new StyleTemplate()
                    .Height(50)
                    .Rounded(8)
                    .BackgroundColor(primaryColor)
                    .Transition(GuiProp.BackgroundColor, 0.2)
                    .Transition(GuiProp.ScaleX, 0.1)
                    .Transition(GuiProp.ScaleY, 0.1))
                .Hovered(new StyleTemplate()
                    .BackgroundColor(secondaryColor))
                .Active(new StyleTemplate()
                    .Scale(0.95)
                    .BackgroundColor(Color.FromArgb(200, primaryColor)))
                .Register();

            // Icon button styles
            Paper.CreateStyleFamily("icon-button")
                .Base(new StyleTemplate()
                    .Width(40)
                    .Height(40)
                    .Rounded(8)
                    .BackgroundColor(Color.FromArgb(50, 0, 0, 0))
                    .Transition(GuiProp.BackgroundColor, 0.2)
                    .Transition(GuiProp.Rounded, 0.2))
                .Hovered(new StyleTemplate()
                    .BackgroundColor(Color.FromArgb(100, primaryColor))
                    .Rounded(20))
                .Register();

            // Sidebar styles
            Paper.CreateStyleFamily("sidebar")
                .Base(new StyleTemplate()
                    .BackgroundColor(cardBackground)
                    .Rounded(8)
                    .Width(75)
                    .Transition(GuiProp.Width, 0.25, Paper.Easing.EaseIn)
                    .Transition(GuiProp.BorderColor, 0.75)
                    .Transition(GuiProp.BorderWidth, 0.75)
                    .Transition(GuiProp.Rounded, 0.25))
                .Hovered(new StyleTemplate()
                    .Width(240)
                    .BorderColor(primaryColor)
                    .BorderWidth(3)
                    .Rounded(16))
                .Register();

            // Menu item styles
            Paper.CreateStyleFamily("menu-item")
                .Base(new StyleTemplate()
                    .Height(50)
                    .Margin(10, 10, 5, 5)
                    .Rounded(8)
                    .BorderColor(Color.Transparent)
                    .BorderWidth(0)
                    .Transition(GuiProp.BackgroundColor, 0.2)
                    .Transition(GuiProp.BorderWidth, 0.1)
                    .Transition(GuiProp.BorderColor, 0.2))
                .Hovered(new StyleTemplate()
                    .BackgroundColor(Color.FromArgb(20, primaryColor))
                    .BorderWidth(2)
                    .BorderColor(primaryColor))
                .Register();

            // Menu item selected state
            Paper.RegisterStyle("menu-item-selected", new StyleTemplate()
                .BorderColor(primaryColor)
                .BorderWidth(2)
                .BackgroundColor(Color.FromArgb(30, primaryColor)));

            // Toggle switch styles
            Paper.CreateStyleFamily("toggle")
                .Base(new StyleTemplate()
                    .Width(60)
                    .Height(30)
                    .Rounded(20)
                    .Transition(GuiProp.BackgroundColor, 0.25, Paper.Easing.CubicInOut))
                .Register();

            Paper.RegisterStyle("toggle-on", new StyleTemplate()
                .BackgroundColor(secondaryColor));

            Paper.RegisterStyle("toggle-off", new StyleTemplate()
                .BackgroundColor(Color.FromArgb(100, lightTextColor)));

            Paper.RegisterStyle("toggle-dot", new StyleTemplate()
                .Width(24)
                .Height(24)
                .Rounded(20)
                .BackgroundColor(Color.White)
                //.PositionType(PositionType.SelfDirected)
                .Top(Paper.Pixels(3))
                .Transition(GuiProp.Left, 0.25, Paper.Easing.CubicInOut));

            // Tab styles
            Paper.CreateStyleFamily("tab")
                .Base(new StyleTemplate()
                    .Transition(GuiProp.BackgroundColor, 0.2))
                .Hovered(new StyleTemplate()
                    .BackgroundColor(Color.FromArgb(20, primaryColor)))
                .Register();

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
                    .BorderColor(primaryColor)
                    .BorderWidth(2)
                    .BackgroundColor(Color.FromArgb(80, 0, 0, 0)))
                .Register();

            // Stat card styles
            Paper.CreateStyleFamily("stat-card")
                .Base(new StyleTemplate()
                    .BackgroundColor(cardBackground)
                    .Rounded(8)
                    .Transition(GuiProp.Rounded, 0.2)
                    .Transition(GuiProp.BorderColor, 0.3)
                    .Transition(GuiProp.BorderWidth, 0.2)
                    .Transition(GuiProp.ScaleX, 0.2)
                    .Transition(GuiProp.ScaleY, 0.2))
                .Hovered(new StyleTemplate()
                    .Rounded(12)
                    .BorderWidth(2)
                    .Scale(1.05))
                .Register();

            // Period button styles
            Paper.CreateStyleFamily("period-button")
                .Base(new StyleTemplate()
                    .Width(60)
                    .Height(30)
                    .Rounded(8)
                    .Margin(5, 5, 0, 0)
                    .BackgroundColor(Color.FromArgb(50, 0, 0, 0))
                    .Transition(GuiProp.BackgroundColor, 0.2))
                .Hovered(new StyleTemplate()
                    .BackgroundColor(Color.FromArgb(50, primaryColor)))
                .Register();

            Paper.RegisterStyle("period-button-selected", new StyleTemplate()
                .BackgroundColor(primaryColor));

            // Activity icon styles
            Paper.RegisterStyle("activity-icon", new StyleTemplate()
                .Width(40)
                .Height(40)
                .Rounded(20));

            // Separator style
            Paper.RegisterStyle("separator", new StyleTemplate()
                .Height(1)
                .Margin(15, 15, 0, 0)
                .BackgroundColor(Color.FromArgb(30, 0, 0, 0)));

            // Container styles
            Paper.RegisterStyle("container", new StyleTemplate()
                .BackgroundColor(cardBackground)
                .Rounded(8));

            // Skill bar styles
            Paper.RegisterStyle("skill-bar-bg", new StyleTemplate()
                .Height(15)
                .Rounded(7.5)
                .BackgroundColor(Color.FromArgb(30, 0, 0, 0)));

            Paper.RegisterStyle("skill-bar-fg", new StyleTemplate()
                .Rounded(7.5));
        }
    }
}
