using Prowl.PaperUI;
using Prowl.PaperUI.Extras;
using Prowl.Vector;

using System.Drawing;
using System.Reflection;

// using Shared.Components;

namespace Shared
{
    public static partial class PaperDemo
    {
        // Track state for interactive elements
        static double sliderValue = 0.5f;
        static int selectedTabIndex = 0;
        static Vector2 chartPosition = new Vector2(0, 0);
        static double zoomLevel = 1.0f;
        static bool[] toggleState = { true, false, true, false, true };

        // Sample data for visualization
        static double[] dataPoints = { 0.2f, 0.5f, 0.3f, 0.8f, 0.4f, 0.7f, 0.6f };
        static readonly string[] tabNames = { "Dashboard", "Analytics", "Profile", "Settings", "Windows" };

        static double time = 0;

        static string searchText = "";
        // static bool searchFocused = false;

        public static Paper Gui;

        public static void Initialize(Paper paper)
        {
            Gui = paper;
            Fonts.Initialize();
            Themes.Initialize();
        }

        public static void RenderUI()
        {
            // Update time for animations
            time += 0.016f; // Assuming ~60fps

            //TestWindows();

            // Main container with light gray background
            using (Gui.Column("MainContainer")
                .BackgroundColor(Themes.backgroundColor)
                //.Style(BoxStyle.Solid(backgroundColor))
                .Enter())
            {
                // A stupid simple way to benchmark the performance of the UI (Adds the entire ui multiple times)
                for (int i = 0; i < 1; i++)
                {
                    Gui.PushID((ulong)i);
                    // Top navigation bar
                    RenderTopNavBar();

                    // Main content area
                    using (Gui.Row("ContentArea")
                        .Enter())
                    {
                        // Left sidebar
                        RenderSidebar();

                        // Content area (tabs content)
                        RenderMainContent();
                    }

                    // Footer
                    RenderFooter();
                    Gui.PopID();
                }
            }
        }

        public static bool isWindowAOpen = true;
        public static bool isWindowBOpen = true;
        public static WindowManager windowManager;

        private static void TestWindows()
        {
            windowManager ??= new WindowManager(Gui);

            // Window Tests
            windowManager.SetWindowFont(Fonts.fontMedium);
            windowManager.Window("MyTestWindowA", ref isWindowAOpen, "Test Window", () =>
            {
                // Window content rendering
                using (Gui.Column("WindowInnerContent")
                        .Enter())
                {
                    windowManager.Window("MyTestWindowB", ref isWindowBOpen, "Recursive Window", () =>
                    {
                        // Window content rendering
                        using (Gui.Column("WindowInnerContent")
                            .Enter())
                        {
                            using (Gui.Box("Title")
                                .Height(40)
                                .Text(Text.Center("Hello from Window System", Fonts.fontLarge, Themes.textColor))
                                .Enter()) { }

                            using (Gui.Box("Content")
                                .Text(Text.Left("This is content inside the window. You can close, resize, and drag this window.", Fonts.fontMedium, Themes.textColor))
                                .Enter()) { }

                            using (Gui.Box("Button")
                                .PositionType(PositionType.SelfDirected)
                                .Width(200)
                                .Height(200)
                                .Margin(Gui.Stretch(), 0, Gui.Stretch(), 0)
                                .BackgroundColor(Themes.primaryColor)
                                //.Style(BoxStyle.SolidRounded(primaryColor, 8f))
                                //.HoverStyle(BoxStyle.SolidRounded(secondaryColor, 12f))
                                //.ActiveStyle(BoxStyle.SolidRounded(primaryColor, 16f))
                                //.FocusedStyle(BoxStyle.SolidRoundedWithBorder(backgroundColor, textColor, 20f, 1f))
                                .Text(Text.Center("Click Me", Fonts.fontMedium, Color.White))
                                .OnClick((rect) => Console.WriteLine("Button in window clicked!"))
                                .Enter()) { }
                        }
                    });
                }
            });


            //var myWindowB = ImGui.CreateWindow(
            //    fontMedium,
            //    "My OtherWindow",
            //    new Vector2(100, 400),
            //    new Vector2(200, 100),
            //    (window) => {
            //        // Window content rendering
            //        using (ImGui.Column("WindowInnerContent")
            //            .Enter())
            //        {
            //            using (ImGui.LayoutBox("Title")
            //                .Height(40)
            //                .Text(TextStyle.Center("Why Hello There", fontLarge, textColor))
            //                .Enter()) { }
            //        }
            //    }
            //);
        }

        private static void RenderTopNavBar()
        {
            using (Gui.Row("TopNavBar")
                .Height(70)
                .Style("container")
                .Margin(15, 15, 15, 0)
                .Enter())
            {
                // Logo
                using (Gui.Box("Logo")
                    .Width(180)
                    .Enter())
                {
                    Gui.Box("LogoInner")
                        .Size(50)
                        .Margin(10)
                        .Text(Text.Center(Icons.Newspaper, Fonts.fontLarge, Themes.lightTextColor));

                    Gui.Box("LogoText")
                        .PositionType(PositionType.SelfDirected)
                        .Left(50 + 15)
                        .Text(Text.Left("PaperUI Demo", Fonts.fontTitle, Themes.textColor));
                }

                // Spacer
                using (Gui.Box("Spacer").Enter()) { }

                // Button.Primary("hello");

                // Search bar - now using text-field style
                // Paper.Box("SearchTextField")
                //     .TextField(searchText, Fonts.fontMedium, newValue => searchText = newValue, "Search...")
                //     .Style("text-field")
                //     .SetScroll(Scroll.ScrollX)
                Input.Secondary("SearchTextField", searchText, newValue => searchText = newValue, "Search...")
                    .Margin(0, 15, 15, 0);

                // Theme Switch - using icon-button style
                // Paper.Box("LightIcon")
                //     .Style("icon-button")
                //     .Margin(0, 10, 15, 0)
                //     .Text(Text.Center(Icons.Lightbulb, Fonts.fontMedium, Themes.lightTextColor))
                //     .OnClick((rect) => Themes.ToggleTheme());
                Button.IconPrimary("LightIcon", Icons.Lightbulb)
                    .Margin(0, 10, 15, 0)
                    .OnClick((rect) => Themes.ToggleTheme());

                // Notification icon
                // Paper.Box("NotificationIcon")
                //     .Style("icon-button")
                //     .Margin(0, 10, 15, 0)
                //     .Text(Text.Center(Icons.CircleExclamation, Fonts.fontMedium, Themes.lightTextColor))
                //     .OnClick((rect) => Console.WriteLine("Notifications clicked"));
                Button.IconSecondary("NotificationIcon", Icons.CircleExclamation)
                    .Margin(0, 10, 15, 0)
                    .OnClick((rect) => Console.WriteLine("Notifications clicked"));

                // User Profile
                // Paper.Box("UserProfile")
                //     .Width(40)
                //     .Height(40)
                //     .Rounded(40)
                //     .BackgroundColor(Themes.secondaryColor)
                //     .Margin(0, 15, 15, 0)
                //     .Text(Text.Center("M", Fonts.fontMedium, Color.White))
                //     .OnClick((rect) => Console.WriteLine("Profile clicked"));
                Button.IconPrimary("UserProfile", "M")
                    // .Width(40)
                    // .Height(40)
                    // .Rounded(40)
                    // .BackgroundColor(Themes.secondaryColor)
                    .Margin(0, 15, 15, 0)
                    .OnClick((rect) => Console.WriteLine("Profile clicked"));
            }
        }

        private static void RenderSidebar()
        {
            using (Gui.Column("Sidebar")
                .Style("sidebar")  // Automatic hover expansion with transitions
                .Margin(15)
                .Enter())
            {
                // Menu header
                Gui.Box("MenuHeader")
                    .Height(60)
                    .Text(Text.Center("Menu", Fonts.fontMedium, Themes.textColor));

                string[] menuIcons = { Icons.House, Icons.ChartBar, Icons.User, Icons.Gear, Icons.WindowMaximize };
                string[] menuItems = { "Dashboard", "Analytics", "Users", "Settings", "Windows" };

                for (int i = 0; i < menuItems.Length; i++)
                {
                    int index = i;
                    bool isSelected = selectedTabIndex == index;

                    using (Gui.Box($"MenuItemContainer_{i}")
                        .Style("menu-item")
                        .StyleIf(isSelected, "menu-item-selected")
                        .OnClick((rect) => selectedTabIndex = index)
                        .Clip()
                        .Enter())
                    {
                        Gui.Box($"MenuItemIcon_{i}")
                            .Width(55)
                            .Height(50)
                            .Text(Text.Center(menuIcons[i], Fonts.fontSmall, Themes.textColor));

                        Gui.Box($"MenuItem_{i}")
                            .Width(100)
                            .PositionType(PositionType.SelfDirected)
                            .Left(50 + 15)
                            .Text(Text.Center($"{menuItems[i]}", Fonts.fontSmall, Themes.textColor));
                    }
                }

                // Spacer
                using (Gui.Box("SidebarSpacer").Enter()) { }

                // Upgrade box
                using (Gui.Box("UpgradeBox")
                    .Margin(15)
                    .Height(Gui.Auto)
                    .Rounded(8)
                    .BackgroundColor(Themes.primaryColor)
                    .AspectRatio(0.5f)
                    .Enter())
                {
                    using (Gui.Column("UpgradeContent")
                        .Margin(15)
                        .Clip()
                        .Enter())
                    {
                        Gui.Box("UpgradeText")
                            .Text(Text.Center("Upgrade to Pro", Fonts.fontMedium, Color.White));

                        Gui.Box("UpgradeButton")
                            .Style("button")
                            .Height(30)
                            .BackgroundColor(Color.White)
                            .Text(Text.Center("Upgrade", Fonts.fontSmall, Themes.primaryColor))
                            .OnClick((rect) => Console.WriteLine("Upgrade clicked"));
                    }
                }
            }
        }

        private static void RenderMainContent()
        {
            using (Gui.Column("MainContent")
                .Margin(0, 15, 15, 15)
                .Enter())
            {
                // Tabs navigation
                RenderTabsNavigation();

                // Tab content based on selected tab
                switch (selectedTabIndex)
                {
                    case 0: RenderDashboardTab(); break;
                    case 1: RenderAnalyticsTab(); break;
                    case 2: RenderProfileTab(); break;
                    case 3: RenderSettingsTab(); break;
                    case 4: RenderWindowsTab(); break;
                    default: RenderDashboardTab(); break;
                }
            }
        }

        private static void RenderTabsNavigation()
        {
            using (Gui.Row("TabsNav")
                .Height(60)
                .Style("container")
                .Enter())
            {
                for (int i = 0; i < tabNames.Length; i++)
                {
                    int index = i;
                    bool isSelected = i == selectedTabIndex;
                    Color tabColor = isSelected ? Themes.primaryColor : Themes.lightTextColor;
                    double tabWidth = 1.0f / tabNames.Length;

                    using (Gui.Box($"Tab_{i}")
                        .Width(Gui.Stretch(tabWidth))
                        .Style("tab")
                        .Text(Text.Center(tabNames[i], Fonts.fontMedium, tabColor))
                        .OnClick((rect) => selectedTabIndex = index)
                        .Enter())
                    {
                        // Show indicator line for selected tab
                        if (isSelected)
                        {
                            Gui.Box($"TabIndicator_{i}")
                                .Height(4)
                                .BackgroundColor(Themes.primaryColor)
                                .Rounded(2);
                        }
                    }
                }
            }
        }

        private static void RenderDashboardTab()
        {
            using (Gui.Row("DashboardCards")
        .Height(120)
        .Margin(0, 0, 15, 0)
        .Enter())
            {
                string[] statNames = { "Total Users", "Revenue", "Projects", "Conversion" };
                string[] statValues = { "3,456", "$12,345", "24", "8.5%" };

                for (int i = 0; i < 4; i++)
                {
                    using (Gui.Box($"StatCard_{i}")
                        .Width(Gui.Stretch(0.25f))
                        .Style("stat-card")
                        .Hovered
                            .BorderColor(Themes.colorPalette[i % Themes.colorPalette.Length])  // Dynamic border color
                            .End()
                        .Margin(i == 0 ? 0 : (15 / 2f), i == 3 ? 0 : (15 / 2f), 0, 0)
                        .Enter())
                    {
                        // Card icon with conditional styling based on parent hover
                        Gui.Box($"StatIcon_{i}")
                            .Size(40)
                            .BackgroundColor(Color.FromArgb(150, Themes.colorPalette[i % Themes.colorPalette.Length]))
                            .Rounded(8)
                            .If(Gui.IsParentHovered)
                                .Rounded(20)
                                .End()
                            .Transition(GuiProp.Rounded, 0.3, Easing.QuartOut)
                            .Margin(15, 0, 15, 0)
                            .IsNotInteractable();

                        using (Gui.Column($"StatContent_{i}")
                            .Margin(10, 15, 15, 15)
                            .Enter())
                        {
                            Gui.Box($"StatLabel_{i}")
                                .Height(Gui.Pixels(25))
                                .Text(Text.Left(statNames[i], Fonts.fontSmall, Themes.lightTextColor));

                            Gui.Box($"StatValue_{i}")
                                .Text(Text.Left(statValues[i], Fonts.fontLarge, Themes.textColor));
                        }
                    }
                }
            }

            // Charts and graphs row
            using (Gui.Row("ChartRow")
                .Margin(0, 0, 15, 0)
                .Enter())
            {
                // Chart area
                using (Gui.Box("ChartArea")
                    .Width(Gui.Stretch(0.7f))
                    .Rounded(8)
                    .BackgroundColor(Themes.cardBackground)
                    //.Style(BoxStyle.SolidRounded(cardBackground, 8f))
                    .Enter())
                {
                    // Chart header
                    using (Gui.Row("ChartHeader")
                        .Height(60)
                        .Margin(20, 20, 20, 0)
                        .Enter())
                    {
                        using (Gui.Box("ChartTitle")
                            .Text(Text.Left("Performance Overview", Fonts.fontMedium, Themes.textColor))
                            .Enter()) { }

                        using (Gui.Row("ChartControls")
                            .Width(280)
                            .Enter())
                        {
                            string[] periods = { "Day", "Week", "Month", "Year" };
                            foreach (var period in periods)
                            {
                                using (Gui.Box($"Period_{period}")
                                    .Width(60)
                                    .Height(30)
                                    .Rounded(8)
                                    .Margin(5, 5, 0, 0)
                                    .BackgroundColor(period == "Week" ? Themes.primaryColor : Color.FromArgb(50, 0, 0, 0))
                                    .Hovered
                                        .BackgroundColor(Color.FromArgb(50, Themes.primaryColor))
                                        .End()
                                    .Transition(GuiProp.BackgroundColor, 0.2f)
                                    .Text(Text.Center(period, Fonts.fontSmall, period == "Week" ? Color.White : Themes.lightTextColor))
                                    .OnClick((rect) => Console.WriteLine($"Period {period} clicked"))
                                    .Enter()) { }
                            }
                        }
                    }

                    // Chart content
                    using (Gui.Box("Chart")
                        .Margin(20)
                        .OnDragging((e) => chartPosition += e.Delta)
                        .OnScroll((e) => zoomLevel = Math.Clamp(zoomLevel + e.Delta * 0.1f, 0.5f, 2.0f))
                        .Clip()
                        .Enter())
                    {
                        using (Gui.Box("ChartCanvas")
                            .Translate(chartPosition.x, chartPosition.y)
                            .Scale(zoomLevel)
                            //.TransformSelf((rect) => {
                            //    Transform t = Transform.CreateTranslation(chartPosition) * Transform.CreateScale(zoomLevel);
                            //    //t.RotateWithOrigin(Math.Abs(Math.Sin(time * 0.01f)), rect.Center.X, rect.Center.Y);
                            //    return t;
                            //})
                            .Enter())
                        {
                            // Draw a simple chart with animated data
                            Gui.AddActionElement((vg, rect) => {

                                // Draw grid lines
                                for (int i = 0; i <= 5; i++)
                                {
                                    double y = rect.y + (rect.height / 5) * i;
                                    vg.BeginPath();
                                    vg.MoveTo(rect.x, y);
                                    vg.LineTo(rect.x + rect.width, y);
                                    vg.SetStrokeColor(Themes.lightTextColor);
                                    vg.SetStrokeWidth(1);
                                    vg.Stroke();
                                }

                                // Draw animated data points
                                vg.BeginPath();
                                double pointSpacing = rect.width / (dataPoints.Length - 1);
                                double animatedValue;

                                // Draw fill
                                vg.MoveTo(rect.x, rect.y + rect.height);

                                for (int i = 0; i < dataPoints.Length; i++)
                                {
                                    animatedValue = dataPoints[i] + Math.Sin(time * 0.25f + i * 0.5f) * 0.1f;
                                    //animatedValue = Math.Clamp(animatedValue, 0.1f, 0.9f);
                                    animatedValue = Math.Min(Math.Max(animatedValue, 0.1f), 0.9f); // Clamp to [0.1, 0.9]

                                    double x = rect.x + i * pointSpacing;
                                    double y = rect.y + rect.height - (animatedValue * rect.height);

                                    if (i == 0)
                                        vg.MoveTo(x, y);
                                    else
                                        vg.LineTo(x, y);
                                }

                                // Complete the fill path
                                vg.LineTo(rect.x + rect.width, rect.y + rect.height);
                                vg.LineTo(rect.x, rect.y + rect.height);

                                // Fill with gradient
                                //var paint = vg.LinearGradient(
                                //    rect.x, rect.y,
                                //    rect.x, rect.y + rect.height,
                                //    Color.FromArgb(100, primaryColor),
                                //    Color.FromArgb(10, primaryColor));
                                //vg.SetFillPaint(paint);
                                vg.SaveState();
                                vg.SetLinearBrush(rect.x, rect.y, rect.x, rect.y + rect.height, Color.FromArgb(100, Themes.primaryColor), Color.FromArgb(10, Themes.primaryColor));
                                vg.FillComplex();
                                vg.RestoreState();

                                vg.ClosePath();

                                // Draw the line
                                vg.BeginPath();
                                for (int i = 0; i < dataPoints.Length; i++)
                                {
                                    animatedValue = dataPoints[i] + Math.Sin(time * 0.25f + i * 0.5f) * 0.1f;
                                    //animatedValue = Math.Clamp(animatedValue, 0.1f, 0.9f);
                                    animatedValue = Math.Min(Math.Max(animatedValue, 0.1f), 0.9f); // Clamp to [0.1, 0.9]

                                    double x = rect.x + i * pointSpacing;
                                    double y = rect.y + rect.height - (animatedValue * rect.height);

                                    if (i == 0)
                                        vg.MoveTo(x, y);
                                    else
                                        vg.LineTo(x, y);
                                }

                                vg.SetStrokeColor(Themes.primaryColor);
                                vg.SetStrokeWidth(3);
                                vg.Stroke();

                                // Draw points
                                for (int i = 0; i < dataPoints.Length; i++)
                                {
                                    animatedValue = dataPoints[i] + Math.Sin(time * 0.25f + i * 0.5f) * 0.1f;
                                    //animatedValue = Math.Clamp(animatedValue, 0.1f, 0.9f);
                                    animatedValue = Math.Min(Math.Max(animatedValue, 0.1f), 0.9f); // Clamp to [0.1, 0.9]

                                    double x = rect.x + i * pointSpacing;
                                    double y = rect.y + rect.height - (animatedValue * rect.height);

                                    vg.BeginPath();
                                    vg.Circle(x, y, 6);
                                    vg.SetFillColor(Color.White);
                                    vg.Fill();

                                    vg.BeginPath();
                                    vg.Circle(x, y, 4);
                                    vg.SetFillColor(Themes.primaryColor);
                                    vg.Fill();
                                }

                                vg.ClosePath();
                            });
                        }
                    }
                }

                // Side panel
                using (Gui.Column("SidePanel")
                    .Width(Gui.Stretch(0.3f))
                    .Margin(15, 0, 0, 0)
                    .Enter())
                {
                    // Activity panel
                    using (Gui.Box("ActivityPanel")
                        .BackgroundColor(Themes.cardBackground)
                        //.Style(BoxStyle.SolidRounded(cardBackground, 8f))
                        .Rounded(8)
                        .Enter())
                    {
                        // Panel header
                        using (Gui.Box("PanelHeader")
                            .Height(60)
                            .Margin(20, 20, 20, 0)
                            .Text(Text.Left("Recent Activity", Fonts.fontMedium, Themes.textColor))
                            .Enter()) { }

                        // Activity items
                        string[] activities = {
                            "John updated the project",
                            "Alice completed a task",
                            "New user registered",
                            "Project deadline updated",
                            "Team meeting scheduled"
                        };

                        string[] timestamps = {
                            "5m ago", "23m ago", "1h ago", "2h ago", "3h ago"
                        };

                        for (int i = 0; i < activities.Length; i++)
                        {
                            using (Gui.Row($"Activity_{i}")
                                .Height(70)
                                .Margin(15, 15, i == 0 ? 5 : 0, 5)
                                .Enter())
                            {
                                // Activity icon
                                using (Gui.Box($"ActivityIcon_{i}")
                                    .Width(40)
                                    .Height(40)
                                    .Rounded(8)
                                    .Margin(0, 0, 15, 0)
                                    .BackgroundColor(Color.FromArgb(150, Themes.colorPalette[i % Themes.colorPalette.Length]))
                                    //.Style(BoxStyle.SolidRounded(Color.FromArgb(150, colorPalette[i % colorPalette.Length]), 20f))
                                    .Enter()) { }

                                // Activity content
                                using (Gui.Column($"ActivityContent_{i}")
                                    .Margin(10, 0, 0, 0)
                                    .Enter())
                                {
                                    using (Gui.Box($"ActivityText_{i}")
                                        .Height(Gui.Pixels(20))
                                        .Margin(0, 0, 15, 0)
                                        .Text(Text.Left(activities[i], Fonts.fontSmall, Themes.textColor))
                                        .Enter()) { }

                                    using (Gui.Box($"ActivityTime_{i}")
                                        .Height(Gui.Pixels(20))
                                        .Text(Text.Left(timestamps[i], Fonts.fontSmall, Themes.lightTextColor))
                                        .Enter()) { }
                                }
                            }

                            // Add separator except for the last item
                            if (i < activities.Length - 1)
                            {
                                Gui.Box($"Separator_{i}").Style("seperator");
                            }
                        }
                    }
                }
            }
        }

        private static void RenderAnalyticsTab()
        {
            using (Gui.Row("AnalyticsContent")
                .Enter())
            {
                using (Gui.Box("AnalyticsContent")
                    .BackgroundColor(Themes.cardBackground)
                    //.Style(BoxStyle.SolidRounded(cardBackground, 8f))
                    .Margin(0, 15 / 2, 15, 0)
                    .Enter())
                {
                    // Analytics header
                    using (Gui.Box("AnalyticsHeader")
                        .Height(80)
                        .Margin(20)
                        .Text(Text.Left("Analytics Dashboard", Fonts.fontLarge, Themes.textColor))
                        .Enter()) { }

                    // Interactive slider as a demo control
                    using (Gui.Column("SliderSection")
                        .Height(100)
                        .Margin(20, 20, 0, 0)
                        .Enter())
                    {
                        using (Gui.Box("SliderLabel")
                            .Height(30)
                            .Text(Text.Left($"Green Amount: {sliderValue:F2}", Fonts.fontMedium, Themes.textColor))
                            .Enter()) { }

                        // Slider control
                        Slider.Primary("SliderTrack", sliderValue, newValue => sliderValue = newValue);
                    }

                    double[] values = { sliderValue, 0.2f, 0.15f, 0.25f, 0.1f };
                    PieChart.Primary("PieChart", values, 0);
                }

                using (Gui.Box("ScrollTest")
                    .BackgroundColor(Themes.cardBackground)
                    //.Style(BoxStyle.SolidRounded(cardBackground, 8f))
                    .Margin(15 / 2, 0, 15, 0)
                    .Enter())
                {
                    // Dynamic content amount based on time
                    int amount = (int)(Math.Abs(Math.Sin(time * 0.25)) * 25) + 10;

                    // Create a grid layout for items
                    using (Gui.Row("GridContainer")
                        .Enter())
                    {
                        // Left column - cards
                        using (Gui.Column("LeftColumn")
                            .Width(Gui.Stretch(0.6))
                            .SetScroll(Scroll.ScrollY)
                            .Enter())
                        {
                            double scrollState = Gui.GetElementStorage<ScrollState>(Gui.CurrentParent, "ScrollState", new ScrollState()).Position.y;

                            for (int i = 0; i < 10; i++)
                            {
                                // Calculate animations based on time and index
                                double hue = (i * 25 + time * 20) % 360;
                                double saturation = 0.7;
                                double value = 0.8;

                                // Convert HSV to RGB
                                double h = hue / 60;
                                int hi = (int)Math.Floor(h) % 6;
                                double f = h - Math.Floor(h);
                                double p = value * (1 - saturation);
                                double q = value * (1 - f * saturation);
                                double t = value * (1 - (1 - f) * saturation);

                                double r, g, b;

                                switch (hi)
                                {
                                    case 0: r = value; g = t; b = p; break;
                                    case 1: r = q; g = value; b = p; break;
                                    case 2: r = p; g = value; b = t; break;
                                    case 3: r = p; g = q; b = value; break;
                                    case 4: r = t; g = p; b = value; break;
                                    default: r = value; g = p; b = q; break;
                                }

                                // Convert to Color
                                Color itemColor = Color.FromArgb(255,
                                    (int)(r * 255),
                                    (int)(g * 255),
                                    (int)(b * 255));

                                // Custom icon for each card
                                string icon = Icons.GetRandomIcon(i);

                                using (Gui.Box($"Card_{i}")
                                    .Height(70)
                                    .Margin(10, 10, 5, 5)
                                    .BackgroundColor(Color.FromArgb(230, itemColor))
                                    .BorderColor(Themes.isDark ? Color.FromArgb(50, 255, 255, 255) : Color.FromArgb(50, 0, 0, 0))
                                    .BorderWidth(1)
                                    .Rounded(12)
                                    .Enter())
                                {
                                    using (Gui.Row("CardContent")
                                        .Margin(10)
                                        .Enter())
                                    {
                                        // Icon
                                        using (Gui.Box($"CardIcon_{i}")
                                            .Width(50)
                                            .Height(50)
                                            .Rounded(25)
                                            .BackgroundColor(Color.FromArgb(60, 255, 255, 255))
                                            .Text(Text.Center(icon, Fonts.fontMedium, Themes.textColor))
                                            .Enter()) { }

                                        // Content
                                        using (Gui.Column($"CardTextColumn_{i}")
                                            .Margin(10, 0, 0, 0)
                                            .Enter())
                                        {
                                            using (Gui.Box($"CardTitle_{i}")
                                                .Height(25)
                                                .Text(Text.Left($"Item {i}", Fonts.fontMedium, Themes.textColor))
                                                .Enter()) { }

                                            using (Gui.Box($"CardDescription_{i}")
                                                .Text(Text.Left($"Interactive card with animations", Fonts.fontSmall,
                                                    Color.FromArgb(200, Themes.textColor)))
                                                .Enter()) { }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

        }

        private static void RenderProfileTab()
        {
            using (Gui.Row("ProfileContent")
                .Margin(0, 0, 15, 0)
                .Enter())
            {
                // Left panel - profile info
                using (Gui.Column("ProfileDetails")
                    .Width(Gui.Stretch(0.4f))
                    .BackgroundColor(Themes.cardBackground)
                    //.Style(BoxStyle.SolidRounded(cardBackground, 8f))
                    .Enter())
                {
                    // Profile header with avatar
                    using (Gui.Column("ProfileHeader")
                        .Height(250)
                        .Enter())
                    {
                        // Avatar
                        using (Gui.Row("AvatarSpot")
                            .Height(120)
                            .Margin(0, 0, 40, 20)
                            .Enter())
                        {
                            // Spacer to Center Avatar
                            using (Gui.Box("Spacer0").Enter()) { }

                            // Avatar
                            using (Gui.Box("Avatar")
                                .Width(120)
                                .Height(120)
                                .BackgroundColor(Themes.secondaryColor)
                                //.Style(BoxStyle.SolidRounded(secondaryColor, 60f))
                                .Text(Text.Center("J", Fonts.fontTitle, Color.White))
                                .Enter()) { }

                            // Spacer to Center Avatar
                            using (Gui.Box("Spacer1").Enter()) { }
                        }


                        // User name
                        using (Gui.Box("UserName")
                            .Height(40)
                            .Text(Text.Center("John Doe", Fonts.fontLarge, Themes.textColor))
                            .Enter()) { }

                        // User title
                        using (Gui.Box("UserTitle")
                            .Height(30)
                            .Text(Text.Center("Senior Developer", Fonts.fontMedium, Themes.lightTextColor))
                            .Enter()) { }
                    }

                    // User stats
                    using (Gui.Row("UserStats")
                        .Height(80)
                        .Margin(20, 20, 0, 0)
                        .Enter())
                    {
                        string[] statLabels = { "Projects", "Tasks", "Teams" };
                        string[] statValues = { "24", "148", "5" };

                        for (int i = 0; i < statLabels.Length; i++)
                        {
                            using (Gui.Column($"Stat_{i}")
                                .Width(Gui.Stretch(1.0f / statLabels.Length))
                                .Enter())
                            {
                                using (Gui.Box($"StatValue_{i}")
                                    .Height(40)
                                    .Text(Text.Center(statValues[i], Fonts.fontLarge, Themes.primaryColor))
                                    .Enter()) { }

                                using (Gui.Box($"StatLabel_{i}")
                                    .Height(30)
                                    .Text(Text.Center(statLabels[i], Fonts.fontSmall, Themes.lightTextColor))
                                    .Enter()) { }
                            }
                        }
                    }

                    // Contact info
                    using (Gui.Column("ContactInfo")
                        .Margin(20)
                        .Enter())
                    {
                        string[] contactLabels = { "Email", "Phone", "Location", "Department" };
                        string[] contactValues = { "john.doe@example.com", "(555) 123-4567", "San Francisco, CA", "Engineering" };

                        for (int i = 0; i < contactLabels.Length; i++)
                        {
                            using (Gui.Row($"ContactRow_{i}")
                                .Height(50)
                                .Enter())
                            {
                                using (Gui.Box($"ContactLabel_{i}")
                                    .Width(100)
                                    .Text(Text.Left(contactLabels[i] + ":", Fonts.fontSmall, Themes.lightTextColor))
                                    .Enter()) { }

                                using (Gui.Box($"ContactValue_{i}")
                                    .Text(Text.Left(contactValues[i], Fonts.fontSmall, Themes.textColor))
                                    .Enter()) { }
                            }
                        }
                    }
                }

                // Right panel - profile activity
                using (Gui.Column("ProfileActivity")
                    .Width(Gui.Stretch(0.6f))
                    .Margin(15, 0, 0, 0)
                    .Enter())
                {
                    // Activity tracker
                    using (Gui.Box("ActivityTracker")
                        .Height(Gui.Stretch(0.6f))
                        .BackgroundColor(Themes.cardBackground)
                        //.Style(BoxStyle.SolidRounded(cardBackground, 8f))
                        .Enter())
                    {
                        // Header
                        using (Gui.Box("ActivityHeader")
                            .Height(60)
                            .Margin(20, 20, 0, 0)
                            .Text(Text.Left("Activity Tracker", Fonts.fontMedium, Themes.textColor))
                            .Enter()) { }

                        // Week days
                        using (Gui.Row("WeekDays")
                            .Height(30)
                            .Margin(20, 20, 0, 0)
                            .Enter())
                        {
                            string[] days = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
                            foreach (var day in days)
                            {
                                using (Gui.Box($"Day_{day}")
                                    .Text(Text.Center(day, Fonts.fontSmall, Themes.lightTextColor))
                                    .Enter()) { }
                            }
                        }

                        // Activity grid - contribution calendar
                        using (Gui.Box("ActivityGrid")
                            .Margin(20, 20, 0, 20)
                            .Enter())
                        {
                            // Render contribution graph
                            Gui.AddActionElement((vg, rect) => {
                                int days = 7;
                                int weeks = 4;
                                double cellWidth = rect.width / days;
                                double cellHeight = rect.height / weeks;
                                double cellSize = Math.Min(cellWidth, cellHeight) * 0.8f;
                                double cellMargin = Math.Min(cellWidth, cellHeight) * 0.1f;

                                for (int week = 0; week < days; week++)
                                {
                                    for (int day = 0; day < weeks; day++)
                                    {
                                        // Calculate position
                                        double x = rect.x + week * cellWidth + cellMargin;
                                        double y = rect.y + day * cellHeight + cellMargin;

                                        // Generate intensity based on position and time
                                        double value = Math.Sin(week * 0.4f + day * 0.7f + time) * 0.5f + 0.5f;
                                        value = Math.Pow(value, 1.5f);

                                        // Draw cell
                                        vg.BeginPath();
                                        vg.RoundedRect(x, y, cellSize, cellSize, 3, 3, 3, 3);

                                        // Apply color based on intensity
                                        int alpha = (int)(40 + value * 215);
                                        vg.SetFillColor(Color.FromArgb(alpha, Themes.primaryColor));
                                        vg.Fill();
                                    }
                                }
                            });
                        }
                    }

                    // Skills section
                    using (Gui.Box("SkillsSection")
                        .Height(Gui.Stretch(0.4f))
                        .BackgroundColor(Themes.cardBackground)
                        //.Style(BoxStyle.SolidRounded(cardBackground, 8f))
                        .Margin(0, 0, 15, 0)
                        .Enter())
                    {
                        // Header
                        using (Gui.Box("SkillsHeader")
                            .Height(20)
                            .Margin(20, 20, 20, 0)
                            .Text(Text.MiddleLeft("Skills", Fonts.fontMedium, Themes.textColor))
                            .Enter()) { }

                        // Skill bars
                        string[] skills = { "Programming", "Design", "Communication", "Leadership", "Problem Solving" };
                        double[] skillLevels = { 0.9f, 0.75f, 0.8f, 0.6f, 0.85f };

                        using (Gui.Column("SkillBars")
                            .Margin(20)
                            .Enter())
                        {
                            for (int i = 0; i < skills.Length; i++)
                            {
                                using (Gui.Column($"Skill_{i}")
                                    .Height(Gui.Stretch(1.0f / skills.Length))
                                    .Margin(0, 0, i == 0 ? 0 : 10, 0)
                                    .Enter())
                                {
                                    // Skill label
                                    using (Gui.Box($"SkillLabel_{i}")
                                        .Height(25)
                                        .Text(Text.Left(skills[i], Fonts.fontSmall, Themes.textColor))
                                        .Enter()) { }

                                    // Skill bar
                                    using (Gui.Row($"SkillBarBg_{i}")
                                        .Height(15)
                                        .BackgroundColor(Color.FromArgb(30, 0, 0, 0))
                                        //.Style(BoxStyle.SolidRounded(Color.FromArgb(30, 0, 0, 0), 7.5f))
                                        .Enter())
                                    {
                                        // Animate the skill level with time
                                        double animatedLevel = skillLevels[i];

                                        using (Gui.Box($"SkillBarFg_{i}")
                                            .Width(Gui.Percent(animatedLevel * 100f))
                                            .BackgroundColor(Themes.colorPalette[i % Themes.colorPalette.Length])
                                            //.Style(BoxStyle.SolidRoundedWithBorder(colorPalette[i % colorPalette.Length], primaryColor, 7.5f, 2))
                                            .Enter()) { }

                                        // Percentage label
                                        using (Gui.Box($"SkillPercent_{i}")
                                            .Width(40)
                                            .Text(Text.Right($"{animatedLevel * 100:F0}%", Fonts.fontSmall, Themes.lightTextColor))
                                            .Enter()) { }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private static void RenderSettingsTab()
        {
            using (Gui.Row("SettingsContent")
                .Margin(0, 0, 15, 0)
                .Enter())
            {
                // Settings categories sidebar
                using (Gui.Column("SettingsCategories")
                    .Width(200)
                    .Style("container")
                    .Enter())
                {
                    string[] categories = {
               "General", "Account", "Appearance",
               "Notifications", "Privacy", "Security"
           };

                    for (int i = 0; i < categories.Length; i++)
                    {
                        bool isSelected = i == 0;
                        Color itemTextColor = isSelected ? Themes.primaryColor : Themes.textColor;
                        var index = i;

                        Button.Outline($"SettingsCat_{i}", $"  {categories[i]}")
                            .Margin(10, 10, 5, 5)
                            // .StyleIf(isSelected, "period-button-selected")
                            .OnClick((rect) => { Console.WriteLine($"Category {categories[index]} clicked"); });

                        // Gui.Box($"SettingsCat_{i}")
                        //     .Height(50)
                        //     .Margin(10, 10, 5, 5)
                        //     .Style("button")
                        //     // .StyleIf(isSelected, "period-button-selected")
                        //     .Text(Text.Left($"  {categories[i]}", Fonts.fontSmall, itemTextColor))
                        //     .OnClick((rect) => { Console.WriteLine($"Category {categories[index]} clicked"); });
                    }
                }

                // Settings content
                using (Gui.Column("SettingsOptions")
                    .Style("container")
                    .Margin(15, 0, 0, 0)
                    .Enter())
                {
                    // Settings header
                    Gui.Box("SettingsHeader")
                        .Height(80)
                        .Margin(20)
                        .Text(Text.Left("General Settings", Fonts.fontLarge, Themes.textColor));

                    // Toggle options - much cleaner now!
                    string[] options = {
               "Enable notifications", "Dark mode", "Auto-save changes",
               "Show analytics data", "Email notifications"
           };

                    for (int i = 0; i < options.Length; i++)
                    {
                        using (Gui.Row($"Setting_{i}")
                            .Height(60)
                            .Margin(20, 20, i == 0 ? 0 : 5, 5)
                            .Enter())
                        {
                            // Option label
                            Gui.Box($"SettingLabel_{i}")
                                .Text(Text.Left(options[i], Fonts.fontMedium, Themes.textColor));

                            bool isOn = toggleState[i];
                            int index = i;
                            Switch.Primary($"Switch{index}", toggleState[index]).OnClick((_) => toggleState[index] = !toggleState[index]);

                            // // Toggle switch - much simpler with styles!
                            // bool isOn = toggleState[i];
                            // int index = i;

                            // using (Gui.Box($"ToggleSwitch_{i}")
                            //     .Style("toggle")
                            //     .StyleIf(isOn, "toggle-on")
                            //     .StyleIf(!isOn, "toggle-off")
                            //     .OnClick((rect) =>
                            //     {
                            //         toggleState[index] = !toggleState[index];
                            //         Console.WriteLine($"Toggle {options[index]}: {!isOn}");
                            //     })
                            //     .Enter())
                            // {
                            //     Gui.Box($"ToggleDot_{i}")
                            //         .Style("toggle-dot")
                            //         .Left(Gui.Pixels(isOn ? 32 : 4));
                            // }
                        }

                        // Add separator except for the last item
                        if (i < options.Length - 1)
                        {
                            Gui.Box($"Separator_{i}").Style("separator");
                        }
                    }

                    // Save button
                    Gui.Box("SaveSettings")
                        .Style("button-primary")
                        .Text(Text.Center("Save Changes", Fonts.fontMedium, Color.White))
                        .Margin(20, 0, 20, 20)
                        .OnClick((rect) => Console.WriteLine("Save settings clicked"));
                }
            }
        }
        private static void RenderWindowsTab()
        {
            using (Gui.Row("WindowsContent")
                .Margin(0, 0, 15, 0)
                .Enter())
            {
                // Settings content
                using (Gui.Column("SettingsOptions")
                    .BackgroundColor(Themes.cardBackground)
                    //.Style(BoxStyle.SolidRounded(cardBackground, 8f))
                    .Margin(15 / 2, 0, 0, 0)
                    .Clip()
                    .Enter())
                {
                    // Button to open windows
                    using (Gui.Box("OpenWindowsButton")
                        .Height(50)
                        .Margin(20)
                        .Text(Text.Center("Open Windows", Fonts.fontMedium, Themes.textColor))
                        .Style("button.primary")
                        .OnClick((rect) => OpenWindows())
                        .Enter()) { }

                    TestWindows();
                }
            }
        }

        private static void OpenWindows()
        {
            isWindowAOpen = true;
            isWindowBOpen = true;
        }

        private static void RenderFooter()
        {
            using (Gui.Row("Footer")
                .Height(50)
                .Rounded(8)
                .BackgroundColor(Themes.cardBackground)
                //.Style(BoxStyle.SolidRoundedWithBorder(cardBackground, Color.FromArgb(30, 0, 0, 0), 4f, 1f))
                .Margin(15, 15, 0, 15)
                .Enter())
            {
                // Copyright
                using (Gui.Box("Copyright")
                    .Margin(15, 0, 0, 0)
                    .Text(Text.Left("© 2025 PaperUI Demo.", Fonts.fontSmall, Themes.lightTextColor))
                    .Enter()) { }

                // FPS Counter
                Gui.Box("FPS").Text(Text.Left($"FPS: {1f / Gui.DeltaTime:F1}", Fonts.fontSmall, Themes.lightTextColor));
                Gui.Box("NodeCounter").Text(Text.Left($"Nodes: {Gui.CountOfAllElements}", Fonts.fontSmall, Themes.lightTextColor));
                Gui.Box("MS").Text(Text.Left($"Frame ms: {Gui.MillisecondsSpent}", Fonts.fontSmall, Themes.lightTextColor));

                // Footer links
                string[] links = { "Terms", "Privacy", "Contact", "Help" };
                using (Gui.Row("FooterLinks")
                    .Enter())
                {
                    foreach (var link in links)
                    {
                        using (Gui.Box($"Link_{link}")
                            .Width(Gui.Stretch(1f / links.Length))
                            .Text(Text.Center(link, Fonts.fontSmall, Themes.primaryColor))
                            .OnClick((rect) => Console.WriteLine($"Link {link} clicked"))
                            .Enter()) { }
                    }
                }
            }
        }
    }
}
