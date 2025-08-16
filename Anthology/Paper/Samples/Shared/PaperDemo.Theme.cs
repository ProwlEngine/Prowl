using System.Drawing;
using Prowl.PaperUI;

/*
SHADCN discord theme colors
discovered through https://ui.jln.dev/

@layer base {
    :root {
      --background: 0 0% 97.69%;
      --foreground: 334 55% 1%;
      --muted: 0 0% 93.85%;
      --muted-foreground: 0 0% 10.2%;
      --popover: 0 0% 100%;
      --popover-foreground: 0 0% 10.2%;
      --card: 0 0% 100%;
      --card-foreground: 0 0% 13.73%;
      --border: 0 0% 80.78%;
      --input: 0 0% 80.78%;
      --primary: 211.29 100% 50%;
      --primary-foreground: 0 0% 100%;
      --secondary: 0 0% 85.49%;
      --secondary-foreground: 334 0% 10%;
      --accent: 211.29 100% 50%;
      --accent-foreground: 334 0% 100%;
      --destructive: 3.19 100% 59.41%;
      --destructive-foreground: 18 0% 100%;
      --ring: 0 0% 60%;
      --chart-1: 211.29 100% 50%;
      --chart-2: 0 0% 85.49%;
      --chart-3: 211.29 100% 50%;
      --chart-4: 0 0% 88.49%;
      --chart-5: 211.29 103% 50%;
      --radius: 0.5rem;
    }
  
    .dark {
      --background: 217.5 9.09% 17.25%;
      --foreground: 334 34% 98%;
      --muted: 210 9.09% 12.94%;
      --muted-foreground: 334 0% 60.77%;
      --popover: 210 9.09% 12.94%;
      --popover-foreground: 334 34% 98%;
      --card: 210 9.09% 12.94%;
      --card-foreground: 334 34% 98%;
      --border: 334 0% 18.46%;
      --input: 214.29 5.04% 27.25%;
      --primary: 226.73 58.43% 65.1%;
      --primary-foreground: 0 0% 100%;
      --secondary: 214.29 5.04% 27.25%;
      --secondary-foreground: 334 0% 100%;
      --accent: 217.5 9.09% 17.25%;
      --accent-foreground: 226.73 58.43% 65.1%;
      --destructive: 358.16 68.78% 53.53%;
      --destructive-foreground: 0 0% 100%;
      --ring: 217.5 9.09% 17.25%;
      --chart-1: 226.73 58.43% 65.1%;
      --chart-2: 214.29 5.04% 27.25%;
      --chart-3: 217.5 9.09% 17.25%;
      --chart-4: 214.29 5.04% 30.25%;
      --chart-5: 226.73 61.43% 65.1%;
    }
  }

*/

namespace Shared
{
    public static class Themes
    {

        //Theme
        public static Color backgroundColor;
        public static Color cardBackground;
        public static Color primaryColor;
        public static Color secondaryColor;
        public static Color textColor;
        public static Color lightTextColor;
        public static Color[] colorPalette;
        public static bool isDark;

        public static void Initialize()
        {
            ToggleTheme();
        }

        public static void ToggleTheme()
        {
            isDark = !isDark;

            if (isDark)
            {
                //Dark
                backgroundColor = Color.FromArgb(255, 18, 18, 23);
                cardBackground = Color.FromArgb(255, 30, 30, 46);
                primaryColor = Color.FromArgb(255, 94, 104, 202);
                secondaryColor = Color.FromArgb(255, 162, 155, 254);
                textColor = Color.FromArgb(255, 226, 232, 240);
                lightTextColor = Color.FromArgb(255, 148, 163, 184);
                colorPalette = [
                    Color.FromArgb(255, 94, 234, 212),   // Cyan
                    Color.FromArgb(255, 162, 155, 254),  // Purple  
                    Color.FromArgb(255, 249, 115, 22),   // Orange
                    Color.FromArgb(255, 248, 113, 113),  // Red
                    Color.FromArgb(255, 250, 204, 21)    // Yellow
                ];
            }
            else
            {

                //Light
                backgroundColor = Color.FromArgb(255, 243, 244, 246);
                cardBackground = Color.FromArgb(255, 255, 255, 255);
                primaryColor = Color.FromArgb(255, 59, 130, 246);
                secondaryColor = Color.FromArgb(255, 16, 185, 129);
                textColor = Color.FromArgb(255, 31, 41, 55);
                lightTextColor = Color.FromArgb(255, 107, 114, 128);
                colorPalette = [
                    Color.FromArgb(255, 59, 130, 246),   // Blue
                    Color.FromArgb(255, 16, 185, 129),   // Teal  
                    Color.FromArgb(255, 239, 68, 68),    // Red
                    Color.FromArgb(255, 245, 158, 11),   // Amber
                    Color.FromArgb(255, 139, 92, 246)    // Purple
                ];
            }

            // Redefine styles with new theme colors
            DefineStyles();
            Components.DefineStyles();
        }

        public static void DefineStyles()
        {
            // Card styles with hover effects
            PaperDemo.P.CreateStyleFamily("card")
                .Base(new StyleTemplate()
                    .BackgroundColor(cardBackground)
                    .Rounded(8)
                    .BoxShadow(0, 2, 6, 0, Color.FromArgb(100, 0, 0, 0))
                    .Transition(GuiProp.BoxShadow, 0.2)
                    .Transition(GuiProp.Rounded, 0.2)
                    .Transition(GuiProp.BorderColor, 0.3)
                    .Transition(GuiProp.BorderWidth, 0.2)
                    .Transition(GuiProp.ScaleX, 0.2)
                    .Transition(GuiProp.ScaleY, 0.2))
                .Hovered(new StyleTemplate()
                    .Rounded(12)
                    .BorderColor(primaryColor)
                    .BorderWidth(2)
                    .BoxShadow(0, 4, 12, 0, Color.FromArgb(160, 0, 0, 0))
                    .Scale(1.05))
                .Register();

            // Button styles
            PaperDemo.P.CreateStyleFamily("button")
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
            PaperDemo.P.CreateStyleFamily("button-primary")
                .Base(new StyleTemplate()
                    .Height(50)
                    .Rounded(8)
                    .BackgroundColor(primaryColor)
                    .BackgroundLinearGradient(0, 0, 0, 1, primaryColor, secondaryColor)
                    .Transition(GuiProp.BackgroundGradient, 0.2)
                    .Transition(GuiProp.BackgroundColor, 0.2)
                    .Transition(GuiProp.ScaleX, 0.1)
                    .Transition(GuiProp.ScaleY, 0.1))
                .Hovered(new StyleTemplate()
                    .BackgroundColor(secondaryColor)
                    .BackgroundLinearGradient(0, 0, 0, 1, secondaryColor, primaryColor))
                .Active(new StyleTemplate()
                    .Scale(0.95)
                    .BackgroundColor(Color.FromArgb(200, primaryColor))
                    .BackgroundLinearGradient(0, 0, 0, 1, Color.FromArgb(200, primaryColor), Color.FromArgb(200, secondaryColor)))
                .Register();

            // Icon button styles
            PaperDemo.P.CreateStyleFamily("icon-button")
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
            PaperDemo.P.CreateStyleFamily("sidebar")
                .Base(new StyleTemplate()
                    .BackgroundColor(cardBackground)
                    .Rounded(8)
                    .Width(75)
                    .Transition(GuiProp.Width, 0.25, Easing.EaseIn)
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
            PaperDemo.P.CreateStyleFamily("menu-item")
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
            PaperDemo.P.RegisterStyle("menu-item-selected", new StyleTemplate()
                .BorderColor(primaryColor)
                .BorderWidth(2)
                .BackgroundColor(Color.FromArgb(30, primaryColor)));

            // Toggle switch styles
            PaperDemo.P.CreateStyleFamily("toggle")
                .Base(new StyleTemplate()
                    .Width(60)
                    .Height(30)
                    .Rounded(20)
                    .Transition(GuiProp.BackgroundColor, 0.25, Easing.CubicInOut))
                .Register();

            PaperDemo.P.RegisterStyle("toggle-on", new StyleTemplate()
                .BackgroundColor(secondaryColor));

            PaperDemo.P.RegisterStyle("toggle-off", new StyleTemplate()
                .BackgroundColor(Color.FromArgb(100, lightTextColor)));

            PaperDemo.P.RegisterStyle("toggle-dot", new StyleTemplate()
                .Width(24)
                .Height(24)
                .Rounded(20)
                .BackgroundColor(Color.White)
                //.PositionType(PositionType.SelfDirected)
                .Top(PaperDemo.P.Pixels(3))
                .Transition(GuiProp.Left, 0.25, Easing.CubicInOut));

            // Tab styles
            PaperDemo.P.CreateStyleFamily("tab")
                .Base(new StyleTemplate()
                    .Transition(GuiProp.BackgroundColor, 0.2))
                .Hovered(new StyleTemplate()
                    .BackgroundColor(Color.FromArgb(20, primaryColor)))
                .Register();

            // Text field styles
            PaperDemo.P.CreateStyleFamily("text-field")
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
            PaperDemo.P.CreateStyleFamily("stat-card")
                .Base(new StyleTemplate()
                    .BackgroundColor(cardBackground)
                    .Rounded(8)
                    .BoxShadow(4, 4, 5, 0, Color.FromArgb(100, 0, 0, 0))
                    .Transition(GuiProp.BoxShadow, 0.2)
                    .Transition(GuiProp.Rounded, 0.2)
                    .Transition(GuiProp.BorderColor, 0.3)
                    .Transition(GuiProp.BorderWidth, 0.2)
                    .Transition(GuiProp.ScaleX, 0.2)
                    .Transition(GuiProp.ScaleY, 0.2))
                .Hovered(new StyleTemplate()
                    .Rounded(12)
                    .BorderWidth(2)
                    .BoxShadow(0, 0, 4, 0, Color.FromArgb(0, 0, 0, 0))
                    .Scale(1.05))
                .Register();

            // Period button styles
            PaperDemo.P.CreateStyleFamily("period-button")
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

            PaperDemo.P.RegisterStyle("period-button-selected", new StyleTemplate()
                .BackgroundColor(primaryColor));

            // Activity icon styles
            PaperDemo.P.RegisterStyle("activity-icon", new StyleTemplate()
                .Width(40)
                .Height(40)
                .Rounded(20));

            // Separator style
            PaperDemo.P.RegisterStyle("separator", new StyleTemplate()
                .Height(1)
                .Margin(15, 15, 0, 0)
                .BackgroundColor(Color.FromArgb(30, 0, 0, 0)));

            // Container styles
            PaperDemo.P.RegisterStyle("container", new StyleTemplate()
                .BackgroundColor(cardBackground)
                .Rounded(8));

            // Skill bar styles
            PaperDemo.P.RegisterStyle("skill-bar-bg", new StyleTemplate()
                .Height(15)
                .Rounded(7.5)
                .BackgroundColor(Color.FromArgb(30, 0, 0, 0)));

            PaperDemo.P.RegisterStyle("skill-bar-fg", new StyleTemplate()
                .Rounded(7.5));
        }

    }
}
