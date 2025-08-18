using System.Drawing;
using Prowl.PaperUI;

// SHADCN discord theme colors
// discovered through https://ui.jln.dev/
// @layer base {
//     
//     .dark {
//       --background: 217.5 9.09% 17.25%; /* 40, 43, 48 */
//       --foreground: 334 34% 98%; /* 255, 247, 252 */
//       --muted: 210 9.09% 12.94%; /* 30, 32, 36 */
//       --muted-foreground: 334 0% 60.77%; /* 155, 155, 155 */
//       --popover: 210 9.09% 12.94%; /* 30, 32, 36 */
//       --popover-foreground: 334 34% 98%; /* 255, 247, 252 */
//       --card: 210 9.09% 12.94%; /* 30, 32, 36 */
//       --card-foreground: 334 34% 98%; /* 255, 247, 252 */
//       --border: 334 0% 18.46%; /* 47, 47, 47 */
//       --input: 214.29 5.04% 27.25%; /* 66, 68, 72 */
//       --primary: 226.73 58.43% 65.1%; /* 69, 135, 235 */
//       --primary-foreground: 0 0% 100%; /* 255, 255, 255 */
//       --secondary: 214.29 5.04% 27.25%; /* 66, 68, 72 */
//       --secondary-foreground: 334 0% 100%; /* 255, 255, 255 */
//       --accent: 217.5 9.09% 17.25%; /* 40, 43, 48 */
//       --accent-foreground: 226.73 58.43% 65.1%; /* 69, 135, 235 */
//       --destructive: 358.16 68.78% 53.53%; /* 230, 43, 52 */
//       --destructive-foreground: 0 0% 100%; /* 255, 255, 255 */
//       --ring: 217.5 9.09% 17.25%; /* 40, 43, 48 */
//       --chart-1: 226.73 58.43% 65.1%; /* 69, 135, 235 */
//       --chart-2: 214.29 5.04% 27.25%; /* 66, 68, 72 */
//       --chart-3: 217.5 9.09% 17.25%; /* 40, 43, 48 */
//       --chart-4: 214.29 5.04% 30.25%; /* 72, 74, 79 */
//       --chart-5: 226.73 61.43% 65.1%; /* 64, 135, 239 */
//     }
//
//     :root {
//       --background: 0 0% 97.69%; /* 249, 249, 249 */
//       --foreground: 334 55% 1%; /* 4, 1, 3 */
//       --muted: 0 0% 93.85%; /* 239, 239, 239 */
//       --muted-foreground: 0 0% 10.2%; /* 26, 26, 26 */
//       --popover: 0 0% 100%; /* 255, 255, 255 */
//       --popover-foreground: 0 0% 10.2%; /* 26, 26, 26 */
//       --card: 0 0% 100%; /* 255, 255, 255 */
//       --card-foreground: 0 0% 13.73%; /* 35, 35, 35 */
//       --border: 0 0% 80.78%; /* 206, 206, 206 */
//       --input: 0 0% 80.78%; /* 206, 206, 206 */
//       --primary: 211.29 100% 50%; /* 0, 149, 255 */
//       --primary-foreground: 0 0% 100%; /* 255, 255, 255 */
//       --secondary: 0 0% 85.49%; /* 218, 218, 218 */
//       --secondary-foreground: 334 0% 10%; /* 26, 26, 26 */
//       --accent: 211.29 100% 50%; /* 0, 149, 255 */
//       --accent-foreground: 334 0% 100%; /* 255, 255, 255 */
//       --destructive: 3.19 100% 59.41%; /* 303, 1, 0 */
//       --destructive-foreground: 18 0% 100%; /* 255, 255, 255 */
//       --ring: 0 0% 60%; /* 153, 153, 153 */
//       --chart-1: 211.29 100% 50%; /* 0, 149, 255 */
//       --chart-2: 0 0% 85.49%; /* 218, 218, 218 */
//       --chart-3: 211.29 100% 50%; /* 0, 149, 255 */
//       --chart-4: 0 0% 88.49%; /* 226, 226, 226 */
//       --chart-5: 211.29 103% 50%; /* 0, 152, 255 */
//       --radius: 0.5rem;
//     }
//   }

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
                backgroundColor = Color.FromArgb(255, 40, 43, 48);
                cardBackground = Color.FromArgb(255, 30, 32, 36);
                primaryColor = Color.FromArgb(255, 69, 135, 235);
                secondaryColor = Color.FromArgb(255, 66, 68, 72);
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
                backgroundColor = Color.FromArgb(255, 240, 240, 240);
                cardBackground = Color.FromArgb(255, 255, 255, 255);
                primaryColor = Color.FromArgb(255, 0, 149, 255);
                secondaryColor = Color.FromArgb(255, 218, 218, 218);
                textColor = Color.FromArgb(255, 31, 41, 55);//4, 1, 3);
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
            PaperDemo.Gui.CreateStyleFamily("card")
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
            PaperDemo.Gui.CreateStyleFamily("button")
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
            PaperDemo.Gui.CreateStyleFamily("button-primary")
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
            PaperDemo.Gui.CreateStyleFamily("icon-button")
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
            PaperDemo.Gui.CreateStyleFamily("sidebar")
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
            PaperDemo.Gui.CreateStyleFamily("menu-item")
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
            PaperDemo.Gui.RegisterStyle("menu-item-selected", new StyleTemplate()
                .BorderColor(primaryColor)
                .BorderWidth(2)
                .BackgroundColor(Color.FromArgb(30, primaryColor)));

            // Tab styles
            PaperDemo.Gui.CreateStyleFamily("tab")
                .Base(new StyleTemplate()
                    .Transition(GuiProp.BackgroundColor, 0.2))
                .Hovered(new StyleTemplate()
                    .BackgroundColor(Color.FromArgb(20, primaryColor)))
                .Register();

            // Text field styles
            PaperDemo.Gui.CreateStyleFamily("text-field")
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
            PaperDemo.Gui.CreateStyleFamily("stat-card")
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
            PaperDemo.Gui.CreateStyleFamily("period-button")
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

            PaperDemo.Gui.RegisterStyle("period-button-selected", new StyleTemplate()
                .BackgroundColor(primaryColor));

            // Activity icon styles
            PaperDemo.Gui.RegisterStyle("activity-icon", new StyleTemplate()
                .Width(40)
                .Height(40)
                .Rounded(20));

            // Separator style
            PaperDemo.Gui.RegisterStyle("separator", new StyleTemplate()
                .Height(1)
                .Margin(15, 15, 0, 0)
                .BackgroundColor(Color.FromArgb(30, 0, 0, 0)));

            // Container styles
            PaperDemo.Gui.RegisterStyle("container", new StyleTemplate()
                .BackgroundColor(cardBackground)
                .Rounded(8));

            // Skill bar styles
            PaperDemo.Gui.RegisterStyle("skill-bar-bg", new StyleTemplate()
                .Height(15)
                .Rounded(7.5)
                .BackgroundColor(Color.FromArgb(30, 0, 0, 0)));

            PaperDemo.Gui.RegisterStyle("skill-bar-fg", new StyleTemplate()
                .Rounded(7.5));
        }

    }
}
