using Hexa.NET.ImGuizmo;
using ImageMagick;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.GUI.Graphics;
using System.Reflection.Emit;

namespace Prowl.Editor
{
    public class ProjectsWindow : EditorWindow
    {
        public static bool WindowDrawnThisFrame = false;
        public string SelectedProject = "";
        private string _searchText = "";
        private string createName = "";
        private string[] tabNames = [FontAwesome6.RectangleList + "  Projects", FontAwesome6.PuzzlePiece + "  Create", FontAwesome6.BookOpen + "  Learn", FontAwesome6.DoorOpen + "  Quit"];
        private int currentTab = 0;

        protected override bool Center { get; } = true;
        protected override double Width { get; } = 512 + (512 / 2);
        protected override double Height { get; } = 512;
        protected override bool BackgroundFade { get; } = true;
        protected override bool TitleBar { get; } = false;
        protected override bool RoundCorners => false;
        protected override double Padding => 0;

        public ProjectsWindow() : base()
        {
            Title = FontAwesome6.Book + " Project Window";
        }

        protected override void Draw()
        {
            if (Project.HasProject)
                isOpened = false;

            g.CurrentNode.Layout(LayoutType.Row);
            g.CurrentNode.ScaleChildren();

            using (g.Node("Side").Height(Size.Percentage(1f)).MaxWidth(150).Layout(LayoutType.Column).Enter())
            {
                DrawSidePanel();
            }

            using (g.Node("Main").Height(Size.Percentage(1f)).Enter())
            {
                g.PushID((ulong)currentTab);
                if (currentTab == 0) // Projects tab
                    DrawProjectsTab();
                else if (currentTab == 1) // Create tab
                    CreateProjectTab();
                g.PopID();
            }

        }
        private void DrawProjectsTab()
        {
            var rect = g.CurrentNode.LayoutData.Rect;
            g.Draw2D.DrawRectFilled(rect, GuiStyle.WindowBackground * 0.25f, 10);
            Vector2 shadowA = new(rect.x, rect.y);
            Vector2 shadowB = new(rect.x, rect.y + (rect.height - 60));
            g.Draw2D.DrawVerticalBlackGradient(shadowA, shadowB, 30, 0.25f);
            Rect footer = new(shadowB.x, shadowB.y, rect.width, 60);
            g.Draw2D.DrawRectFilled(footer, GuiStyle.WindowBackground, 10, 4);
            Vector2 shadowC = new(rect.x, rect.y + rect.height);
            g.Draw2D.DrawVerticalBlackGradient(shadowB, shadowC, 20, 0.25f);

            g.InputField("SearchInput", ref _searchText, 0x100, Gui.InputFieldFlags.None, 25, 50, 150);

            using (g.Node("List").Width(565).Height(345).Left(25).Top(80).Layout(LayoutType.Column).Clip().Enter())
            {
                Directory.CreateDirectory(Project.Projects_Directory);
                var folders = new DirectoryInfo(Project.Projects_Directory).EnumerateDirectories();
                folders = folders.OrderByDescending((x) => x.LastWriteTimeUtc);

                foreach (var projectFolder in folders)
                    if (string.IsNullOrEmpty(_searchText) || projectFolder.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
                        DisplayProject(projectFolder.Name);

                g.ScrollV();
            }

            using (g.Node("OpenBtn").TopLeft(455, 452).Scale(162, 60).Enter())
            {
                if (!string.IsNullOrEmpty(SelectedProject))
                {
                    if (g.IsNodePressed())
                    {
                        Project.Open(SelectedProject);
                        isOpened = false;
                    }

                    var col = g.IsNodeActive() ? GuiStyle.SelectedColor :
                              g.IsNodeHovered() ? GuiStyle.HoveredColor * 0.8f : GuiStyle.HoveredColor;

                    g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, col, 10, 4);
                    g.Draw2D.DrawText("Open", g.CurrentNode.LayoutData.Rect);
                }
                else
                {
                    g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, Color.white * 0.4f, 10, 4);
                    g.Draw2D.DrawText("Open", g.CurrentNode.LayoutData.Rect);
                }
            }
        }
        private void DisplayProject(string name)
        {
            var proj = Project.GetPath(name);
            using (g.Node(name).Height(48).Width(Size.Percentage(1f, -17)).Margin(5).Enter())
            {
                var rect = g.CurrentNode.LayoutData.Rect;
                g.Draw2D.DrawText(UIDrawList.DefaultFont, name, 20, rect.Position + new Vector2(8, 5), Color.white);
                string path = proj.FullName;
                // Cut of the path if it's too long
                if (path.Length > 48)
                    path = string.Concat("...", path.AsSpan(path.Length - 48));
                g.Draw2D.DrawText(UIDrawList.DefaultFont, path, 20, rect.Position + new Vector2(8, 22), Color.white * 0.5f);

                g.Draw2D.DrawText(UIDrawList.DefaultFont, GetFormattedLastModifiedTime(proj.LastWriteTime), 20, rect.Position + new Vector2(rect.width - 125, 14), Color.white * 0.5f);

                var interact = g.GetInteractable();
                if (interact.TakeFocus() || SelectedProject == name)
                {
                    SelectedProject = name;
                    g.Draw2D.DrawRect(g.CurrentNode.LayoutData.Rect, new(0.7f, 0.7f, 0.7f, 1f), 1, 2);
                }
                else if (interact.IsHovered())
                    g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, new(0.1f, 0.1f, 0.1f, 0.4f), 2);
                //else if (interact.IsDoubleClicked())
                //{
                //    Project.Open(name);
                //    isOpened = false;
                //}
            }

        }

        private string GetFormattedLastModifiedTime(DateTime lastModified)
        {
            TimeSpan timeSinceLastModified = DateTime.Now - lastModified;

            if (timeSinceLastModified.TotalMinutes < 1)
                return "Just now";
            else if (timeSinceLastModified.TotalMinutes < 60)
                return $"{(int)timeSinceLastModified.TotalMinutes} minutes ago";
            else if (timeSinceLastModified.TotalHours < 24)
                return $"{(int)timeSinceLastModified.TotalHours} hours ago";
            else
                return $"{(int)timeSinceLastModified.TotalDays} days ago";
        }

        private void CreateProjectTab()
        {
            var rect = g.CurrentNode.LayoutData.Rect;
            g.Draw2D.DrawRectFilled(rect, GuiStyle.WindowBackground * 0.25f, 10);
            Vector2 shadowA = new(rect.x, rect.y);
            Vector2 shadowB = new(rect.x, rect.y + (rect.height - 77));
            g.Draw2D.DrawVerticalBlackGradient(shadowA, shadowB, 30, 0.25f);
            Rect footer = new(shadowB.x, shadowB.y, rect.width, 77);
            g.Draw2D.DrawRectFilled(footer, GuiStyle.WindowBackground, 10, 4);
            Vector2 shadowC = new(rect.x, rect.y + rect.height);
            g.Draw2D.DrawVerticalBlackGradient(shadowB, shadowC, 20, 0.25f);

            g.InputField("CreateInput", ref createName, 0x100, Gui.InputFieldFlags.None, 30, 450, 340);
            string path = Project.GetPath(createName).FullName;
            if (path.Length > 48)
                path = string.Concat("...", path.AsSpan(path.Length - 48));
            g.Draw2D.DrawText(UIDrawList.DefaultFont, path, 20, rect.Position + new Vector2(30, 480), Color.white * 0.5f);

            using (g.Node("CreateBtn").TopLeft(445, 435).Scale(172, 77).Enter())
            {
                if (g.IsNodePressed())
                {
                    Project.CreateNew(createName);
                    currentTab = 0;
                }
                var col = g.IsNodeActive() ? GuiStyle.Indigo :
                          g.IsNodeHovered() ? GuiStyle.SelectedColor * 0.8f : GuiStyle.HoveredColor;

                g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, col, 10, 4);
                g.Draw2D.DrawText("Create", g.CurrentNode.LayoutData.Rect);
            }
        }

        private void DrawSidePanel()
        {
            using (g.Node("Name").Height(40).Width(Size.Percentage(1f)).Enter())
            {
                Rect rect = g.CurrentNode.LayoutData.Rect;
                g.Draw2D.DrawText(UIDrawList.DefaultFont, "Prowl", 40, rect, Color.white);
            }

            for (int i = 0; i < tabNames.Length; i++)
            {
                bool isQuit = i == tabNames.Length - 1;
                using (g.Node(tabNames[i]).Height(40).Width(Size.Percentage(1f)).Top(Offset.Percentage(isQuit ? 1 : 0, isQuit ? -40 : 0)).Enter())
                {
                    if (isQuit) g.CurrentNode.IgnoreLayout();
                    var interact = g.GetInteractable();
                    if (interact.TakeFocus() || currentTab == i)
                    {
                        g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.SelectedColor);
                        currentTab = i;
                        if (i == 3)
                            Application.Quit();
                    }
                    else if (interact.IsHovered())
                        g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.HoveredColor, i == 3 ? 10 : 0, 8);

                    Rect rect = g.CurrentNode.LayoutData.Rect;
                    g.Draw2D.DrawText(UIDrawList.DefaultFont, tabNames[i], 20, rect, Color.white);
                }
            }
        }
    }
}